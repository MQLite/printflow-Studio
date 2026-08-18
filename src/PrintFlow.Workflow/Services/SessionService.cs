using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Reviews;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Definitions;
using PrintFlow.Workflow.Effects;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Workflow.Services;

/// <summary>
/// Interprets the effects <see cref="IWorkflowEngine"/> returns for one command and turns them
/// into real work: file I/O through <see cref="IWorkspace"/>, adapter calls, and exactly one
/// metadata transaction per phase (Epic 11100 plan §35; "glue" row of §20.2).
/// </summary>
/// <remarks>
/// <see cref="WorkflowEngine"/> itself stays untouched and pure — this type is the only place
/// that sequences file work before metadata commits (plan §10.5) and the only place that
/// decides how a <see cref="WorkflowEffect.RunAdapter"/> is actually carried out. A step that
/// invokes an adapter produces <b>two</b> metadata transactions, not one: the first records the
/// attempt as <c>Running</c> before any file work begins (so a crash mid-attempt is
/// detectable — plan §38), and the second records the outcome once the file work and its
/// validation are complete.
/// </remarks>
public sealed class SessionService : ISessionService
{
    private readonly IWorkflowEngine _engine;
    private readonly ISessionRepository _repository;
    private readonly IWorkspace _workspace;
    private readonly IFileInspector _fileInspector;
    private readonly IMeituProcessor _meitu;
    private readonly IPhotoshopOutputProcessor _photoshop;
    private readonly IWorkstationPresetProvider _presetProvider;
    private readonly RevisionIntegrityGuard _integrityGuard;
    private readonly IIdGenerator _idGenerator;
    private readonly TimeProvider _timeProvider;
    private readonly int _processId;
    private readonly string _machineName;

    public SessionService(
        IWorkflowEngine engine,
        ISessionRepository repository,
        IWorkspace workspace,
        IFileInspector fileInspector,
        IMeituProcessor meitu,
        IPhotoshopOutputProcessor photoshop,
        IWorkstationPresetProvider presetProvider,
        IIdGenerator idGenerator,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(fileInspector);
        ArgumentNullException.ThrowIfNull(meitu);
        ArgumentNullException.ThrowIfNull(photoshop);
        ArgumentNullException.ThrowIfNull(presetProvider);
        ArgumentNullException.ThrowIfNull(idGenerator);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _engine = engine;
        _repository = repository;
        _workspace = workspace;
        _fileInspector = fileInspector;
        _meitu = meitu;
        _photoshop = photoshop;
        _presetProvider = presetProvider;
        _idGenerator = idGenerator;
        _timeProvider = timeProvider;
        _integrityGuard = new RevisionIntegrityGuard(workspace, fileInspector);
        _processId = Environment.ProcessId;
        _machineName = Environment.MachineName;
    }

    /// <inheritdoc />
    public async Task<OperationResult<SessionView>> ImportAsync(
        WorkflowType workflowType, string sourceAbsolutePath, string? outputName, string? operatorName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceAbsolutePath);

        OutputName name = OutputName.Sanitise(outputName ?? StemOf(sourceAbsolutePath));

        SessionId id = SessionId.From(_idGenerator.NewId());
        CommandContext context = CommandContext.Create(_timeProvider, _idGenerator, operatorName);

        OperationResult<WorkspaceDirRef> created = _workspace.CreateSession(id, context.NowUtc);
        if (created.IsFailure)
        {
            return OperationResult.Fail<SessionView>(created.Failure);
        }

        WorkspaceDirRef workspaceDir = created.Value;
        ProcessingSession session = ProcessingSession.Start(id, workflowType, name, workspaceDir, context.NowUtc);
        WorkflowSnapshot initialSnapshot = WorkflowSnapshot.Create(id, workflowType, name, context.NowUtc);

        WorkflowTransition started = _engine.Apply(initialSnapshot, new WorkflowCommand.StartStep(StepKind.Import), context);
        if (started.IsRejected)
        {
            return OperationResult.Fail<SessionView>(MapRejection(started.Rejection!));
        }

        ProcessingAttempt runningAttempt = ProcessingAttempt.Start(
            context.NewAttemptId, id, StepKind.Import, null, OperationKind.Import, "internal-import-v1", context.NowUtc);

        SessionMutation opening = new(
            session, started.State.Steps, [], [], [runningAttempt], [], [], null, null);

        OperationResult<Unit> committedOpening = await _repository.CommitAsync(opening, cancellationToken);
        if (committedOpening.IsFailure)
        {
            return OperationResult.Fail<SessionView>(committedOpening.Failure);
        }

        OperationResult<WorkspaceFileRef> imported =
            await _workspace.ImportSourceAsync(workspaceDir, sourceAbsolutePath, cancellationToken);
        if (imported.IsFailure)
        {
            return await FailImportAsync(session, started.State, context, runningAttempt, imported.Failure, cancellationToken);
        }

        OperationResult<FileFacts> inspected =
            await _fileInspector.InspectAsync(_workspace.ResolveAbsolute(imported.Value), cancellationToken);
        if (inspected.IsFailure)
        {
            return await FailImportAsync(session, started.State, context, runningAttempt, inspected.Failure, cancellationToken);
        }

        RevisionId revisionId = RevisionId.From(_idGenerator.NewId());
        Revision rootRevision = Revision.Create(
            revisionId, id, null, OperationKind.Import, imported.Value, inspected.Value, context.NowUtc);

        WorkflowCommand.System.AttemptSucceeded succeeded = new(
            context.NewAttemptId, StepKind.Import, revisionId, inspected.Value.Sha256);
        WorkflowTransition finished = _engine.Apply(started.State, succeeded, context);
        if (finished.IsRejected)
        {
            return OperationResult.Fail<SessionView>(MapRejection(finished.Rejection!));
        }

        InputSnapshot snapshotRecord = new(
            SnapshotId.From(_idGenerator.NewId()), id, revisionId, sourceAbsolutePath,
            FileNameOf(sourceAbsolutePath), context.NowUtc);

        ProcessingSession sessionAfterImport = MergeSession(session, finished.State, finished.Effects, context.NowUtc);
        ProcessingAttempt succeededAttempt = runningAttempt.Succeed(revisionId, context.NowUtc);

        SessionAggregate aggregateSoFar = new(session, null, started.State.Steps, [], [runningAttempt], [], []);
        SessionMutation closing = BuildMetadataMutation(
            aggregateSoFar, sessionAfterImport, finished.State, finished.Effects, context,
            newRevisions: [rootRevision], newInputSnapshot: snapshotRecord, upsertAttempts: [succeededAttempt]);

        OperationResult<Unit> committedClosing = await _repository.CommitAsync(closing, cancellationToken);
        if (committedClosing.IsFailure)
        {
            return OperationResult.Fail<SessionView>(committedClosing.Failure);
        }

        return OperationResult.Ok(SessionView.From(finished.State, _engine.AvailableCommands(finished.State)));
    }

    /// <inheritdoc />
    public async Task<OperationResult<SessionView>> ExecuteAsync(
        SessionId id, WorkflowCommand command, string? operatorName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        OperationResult<SessionAggregate?> loaded = await _repository.LoadAsync(id, cancellationToken);
        if (loaded.IsFailure)
        {
            return OperationResult.Fail<SessionView>(loaded.Failure);
        }

        if (loaded.Value is null)
        {
            return OperationResult.Fail<SessionView>(FailureCode.PreconditionNotMet, $"No session {id} exists.");
        }

        SessionAggregate aggregate = loaded.Value;
        WorkflowSnapshot snapshot = aggregate.ToSnapshot();
        CommandContext context = CommandContext.Create(_timeProvider, _idGenerator, operatorName);

        OperationResult<Unit> integrity = await EnsureIntegrityAsync(aggregate, snapshot, command, context, cancellationToken);
        if (integrity.IsFailure)
        {
            return OperationResult.Fail<SessionView>(integrity.Failure);
        }

        WorkflowTransition transition = _engine.Apply(snapshot, command, context);
        if (transition.IsRejected)
        {
            return OperationResult.Fail<SessionView>(MapRejection(transition.Rejection!));
        }

        WorkflowEffect.RunAdapter? runAdapter = transition.Effects.OfType<WorkflowEffect.RunAdapter>().FirstOrDefault();
        if (runAdapter is null)
        {
            ProcessingSession updatedSession = MergeSession(aggregate.Session, transition.State, transition.Effects, context.NowUtc);
            SessionMutation mutation = BuildMetadataMutation(aggregate, updatedSession, transition.State, transition.Effects, context);

            OperationResult<Unit> committed = await _repository.CommitAsync(mutation, cancellationToken);
            if (committed.IsFailure)
            {
                return OperationResult.Fail<SessionView>(committed.Failure);
            }

            return OperationResult.Ok(SessionView.From(transition.State, _engine.AvailableCommands(transition.State)));
        }

        return await RunAdapterBackedStepAsync(aggregate, transition, context, runAdapter, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult<SessionView>> LoadAsync(SessionId id, CancellationToken cancellationToken)
    {
        OperationResult<SessionAggregate?> loaded = await _repository.LoadAsync(id, cancellationToken);
        if (loaded.IsFailure)
        {
            return OperationResult.Fail<SessionView>(loaded.Failure);
        }

        if (loaded.Value is null)
        {
            return OperationResult.Fail<SessionView>(FailureCode.PreconditionNotMet, $"No session {id} exists.");
        }

        WorkflowSnapshot snapshot = loaded.Value.ToSnapshot();
        return OperationResult.Ok(SessionView.From(snapshot, _engine.AvailableCommands(snapshot)));
    }

    // -------------------------------------------------------------------------------------
    // Revision integrity (Jira 11105)
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Re-hashes the Revision a command is about to consume, if any, before the command ever
    /// reaches the engine. A mismatch invalidates the Revision in its own metadata transaction
    /// and the command is refused (plan §10.3).
    /// </summary>
    private async Task<OperationResult<Unit>> EnsureIntegrityAsync(
        SessionAggregate aggregate, WorkflowSnapshot snapshot, WorkflowCommand command, CommandContext context,
        CancellationToken cancellationToken)
    {
        Revision? subject = command switch
        {
            WorkflowCommand.Approve approve => FindRevision(aggregate, snapshot.Step(approve.Step)?.CurrentRevisionId),
            WorkflowCommand.Reject reject => FindRevision(aggregate, snapshot.Step(reject.Step)?.CurrentRevisionId),
            WorkflowCommand.StartStep start => FindRevision(aggregate, snapshot.UpstreamRevisionOf(start.Step)),
            _ => null,
        };

        if (subject is null)
        {
            return OperationResult.Ok();
        }

        OperationResult<Sha256> verified = await _integrityGuard.VerifyAsync(subject, cancellationToken);
        if (verified.IsSuccess)
        {
            return OperationResult.Ok();
        }

        RevisionInvalidation invalidation = new(subject.Id, InvalidationReason.FileMutated, context.NowUtc);
        SessionMutation mutation = new(
            aggregate.Session with { UpdatedAtUtc = context.NowUtc },
            aggregate.Steps, [], [invalidation], [], [], [], null, null);
        await _repository.CommitAsync(mutation, cancellationToken);

        return OperationResult.Fail<Unit>(verified.Failure);
    }

    private static Revision? FindRevision(SessionAggregate aggregate, RevisionId? id) =>
        id is null ? null : aggregate.Revisions.FirstOrDefault(r => r.Id == id.Value);

    // -------------------------------------------------------------------------------------
    // Adapter-backed steps: two metadata transactions around the file work
    // -------------------------------------------------------------------------------------

    private async Task<OperationResult<SessionView>> RunAdapterBackedStepAsync(
        SessionAggregate aggregate, WorkflowTransition started, CommandContext context,
        WorkflowEffect.RunAdapter runAdapter, CancellationToken cancellationToken)
    {
        StepDefinition? definition = started.State.Definition.Find(runAdapter.Step);
        if (definition is null)
        {
            return OperationResult.Fail<SessionView>(
                FailureCode.PreconditionNotMet, $"Step {runAdapter.Step} is not part of this workflow.");
        }

        AutomationLockChange? acquire = null;
        if (definition.IsAdapterBacked)
        {
            OperationResult<AutomationLockState> lockState = await _repository.GetAutomationLockAsync(cancellationToken);
            if (lockState.IsFailure)
            {
                return OperationResult.Fail<SessionView>(lockState.Failure);
            }

            if (lockState.Value.IsHeld && lockState.Value.SessionId != aggregate.Session.Id)
            {
                return OperationResult.Fail<SessionView>(
                    FailureCode.AdapterUnavailable,
                    $"Meitu/Photoshop is already controlled by session {lockState.Value.SessionId}.");
            }

            acquire = new AutomationLockChange(
                AutomationLockAction.Acquire, aggregate.Session.Id, context.NowUtc, _processId, _machineName);
        }

        int retrySequence = started.Effects
            .OfType<WorkflowEffect.RecordAttemptStarted>()
            .FirstOrDefault()?.RetrySequence ?? 0;

        ProcessingAttempt runningAttempt = ProcessingAttempt.Start(
            context.NewAttemptId, aggregate.Session.Id, runAdapter.Step, runAdapter.InputRevision,
            runAdapter.Operation, AdapterIdFor(runAdapter.Adapter), context.NowUtc, retrySequence: retrySequence);

        ProcessingSession sessionAfterStart = MergeSession(aggregate.Session, started.State, started.Effects, context.NowUtc);

        SessionMutation opening = BuildMetadataMutation(
            aggregate, sessionAfterStart, started.State, started.Effects, context, upsertAttempts: [runningAttempt]);
        opening = opening with { LockChange = acquire };

        OperationResult<Unit> committedStart = await _repository.CommitAsync(opening, cancellationToken);
        if (committedStart.IsFailure)
        {
            return OperationResult.Fail<SessionView>(committedStart.Failure);
        }

        SessionAggregate afterStart = aggregate with
        {
            Session = sessionAfterStart,
            Steps = started.State.Steps,
            Attempts = [.. aggregate.Attempts, runningAttempt],
        };

        OperationResult<(WorkspaceFileRef Output, FileFacts Facts)> work =
            await PerformStepWorkAsync(afterStart, started.State, definition, runAdapter, context, cancellationToken);

        if (work.IsFailure)
        {
            return await FailAttemptAsync(
                afterStart, started.State, context, runAdapter.Step, runningAttempt, work.Failure, cancellationToken);
        }

        RevisionId revisionId = RevisionId.From(_idGenerator.NewId());
        Revision newRevision = Revision.Create(
            revisionId, aggregate.Session.Id, runAdapter.InputRevision, runAdapter.Operation,
            work.Value.Output, work.Value.Facts, context.NowUtc);

        WorkflowCommand.System.AttemptSucceeded succeeded = new(
            context.NewAttemptId, runAdapter.Step, revisionId, work.Value.Facts.Sha256);
        WorkflowTransition finished = _engine.Apply(started.State, succeeded, context);
        if (finished.IsRejected)
        {
            return OperationResult.Fail<SessionView>(MapRejection(finished.Rejection!));
        }

        ProcessingAttempt succeededAttempt = runningAttempt.Succeed(revisionId, context.NowUtc);
        ProcessingSession sessionAfterFinish = MergeSession(sessionAfterStart, finished.State, finished.Effects, context.NowUtc);

        List<PrintOutput> newOutputs = [];
        if (runAdapter.Step == StepKind.PhotoshopOutput)
        {
            OperationResult<PrintOutput> output = BuildPrintOutput(
                aggregate.Session.Id, revisionId, started.State, work.Value, context);
            if (output.IsFailure)
            {
                return OperationResult.Fail<SessionView>(output.Failure);
            }

            newOutputs.Add(output.Value);
        }

        SessionMutation finishing = BuildMetadataMutation(
            afterStart, sessionAfterFinish, finished.State, finished.Effects, context,
            newRevisions: [newRevision], upsertAttempts: [succeededAttempt], upsertOutputs: newOutputs);

        OperationResult<Unit> committedFinish = await _repository.CommitAsync(finishing, cancellationToken);
        if (committedFinish.IsFailure)
        {
            return OperationResult.Fail<SessionView>(committedFinish.Failure);
        }

        return OperationResult.Ok(SessionView.From(finished.State, _engine.AvailableCommands(finished.State)));
    }

    private async Task<OperationResult<(WorkspaceFileRef Output, FileFacts Facts)>> PerformStepWorkAsync(
        SessionAggregate aggregate, WorkflowSnapshot state, StepDefinition definition,
        WorkflowEffect.RunAdapter runAdapter, CommandContext context, CancellationToken cancellationToken)
    {
        WorkspaceDirRef session = aggregate.Session.Workspace;
        WorkspaceFileRef? input = runAdapter.InputRevision is RevisionId inputId
            ? aggregate.Revisions.FirstOrDefault(r => r.Id == inputId)?.File
            : null;

        if (runAdapter.Adapter == AdapterKind.None)
        {
            // ApprovedPngExport: promote the approved upstream bytes unchanged. The existing
            // hash-bound approval already covers the promoted file by construction (plan §7.3).
            if (input is not { } sourceRef)
            {
                return OperationResult.Fail<(WorkspaceFileRef, FileFacts)>(
                    FailureCode.PreconditionNotMet, "Nothing to promote: no upstream Revision.");
            }

            OperationResult<NamingPatternSet> patterns = _presetProvider.GetNamingPatterns();
            if (patterns.IsFailure)
            {
                return OperationResult.Fail<(WorkspaceFileRef, FileFacts)>(patterns.Failure);
            }

            string proposedName = aggregate.Session.OutputName.Value + ".png";
            OperationResult<WorkspaceFileRef> reserved =
                _workspace.ReserveOutput(session, WorkspaceArea.Approved, proposedName, patterns.Value);
            if (reserved.IsFailure)
            {
                return OperationResult.Fail<(WorkspaceFileRef, FileFacts)>(reserved.Failure);
            }

            OperationResult<Unit> written = await _workspace.WriteReservedAsync(reserved.Value, sourceRef, cancellationToken);
            if (written.IsFailure)
            {
                return OperationResult.Fail<(WorkspaceFileRef, FileFacts)>(written.Failure);
            }

            return await InspectAsync(reserved.Value, cancellationToken);
        }

        if (input is not { } upstreamRef)
        {
            return OperationResult.Fail<(WorkspaceFileRef, FileFacts)>(
                FailureCode.PreconditionNotMet, $"Step {runAdapter.Step} has no upstream Revision to work from.");
        }

        OperationResult<WorkspaceFileRef> workingCopy =
            await _workspace.CreateWorkingCopyAsync(session, context.NewAttemptId, upstreamRef, cancellationToken);
        if (workingCopy.IsFailure)
        {
            return OperationResult.Fail<(WorkspaceFileRef, FileFacts)>(workingCopy.Failure);
        }

        switch (runAdapter.Adapter)
        {
            case AdapterKind.Meitu:
            {
                MeituOperation operation = runAdapter.Operation == OperationKind.Enhance
                    ? MeituOperation.Enhance
                    : MeituOperation.RemoveBackground;

                OperationResult<AdapterOutput> result = await _meitu.ProcessAsync(
                    new MeituRequest(workingCopy.Value, operation, ParentDirOf(workingCopy.Value), workingCopy.Value),
                    cancellationToken);
                if (result.IsFailure)
                {
                    return OperationResult.Fail<(WorkspaceFileRef, FileFacts)>(result.Failure);
                }

                return await InspectAsync(result.Value.ProducedFile, cancellationToken);
            }

            case AdapterKind.Photoshop:
            {
                if (state.Dimensions is not { } dimensions)
                {
                    return OperationResult.Fail<(WorkspaceFileRef, FileFacts)>(
                        FailureCode.PreconditionNotMet, "Photoshop output requires confirmed print dimensions.");
                }

                if (state.WhiteUnderbaseBranch is not { } branch)
                {
                    return OperationResult.Fail<(WorkspaceFileRef, FileFacts)>(
                        FailureCode.PreconditionNotMet, "Photoshop output requires an explicit white-underbase branch.");
                }

                OperationResult<ProductionPresetRef> preset = _presetProvider.GetVerifiedPreset();
                if (preset.IsFailure)
                {
                    return OperationResult.Fail<(WorkspaceFileRef, FileFacts)>(preset.Failure);
                }

                OperationResult<NamingPatternSet> patterns = _presetProvider.GetNamingPatterns();
                if (patterns.IsFailure)
                {
                    return OperationResult.Fail<(WorkspaceFileRef, FileFacts)>(patterns.Failure);
                }

                string tiffName = OutputFileNaming.BuildProposedFileName(
                    NamingArtifactKind.ProductionTiff, aggregate.Session.OutputName, patterns.Value, dimensions.WidthMm);

                OperationResult<WorkspaceFileRef> reserved =
                    _workspace.ReserveOutput(session, WorkspaceArea.Approved, tiffName, patterns.Value);
                if (reserved.IsFailure)
                {
                    return OperationResult.Fail<(WorkspaceFileRef, FileFacts)>(reserved.Failure);
                }

                OperationResult<AdapterOutput> result = await _photoshop.GenerateAsync(
                    new PhotoshopRequest(
                        workingCopy.Value, dimensions, preset.Value, branch, reserved.Value.FileName,
                        ParentDirOf(workingCopy.Value), reserved.Value),
                    cancellationToken);
                if (result.IsFailure)
                {
                    return OperationResult.Fail<(WorkspaceFileRef, FileFacts)>(result.Failure);
                }

                return await InspectAsync(result.Value.ProducedFile, cancellationToken);
            }

            case AdapterKind.Internal:
                // Trim placeholder: Epic 11100 defines the step shape only (StepKind.Trim,
                // its state, its position in each workflow). The real alpha-bound crop
                // algorithm, manual adjustment and trim review belong to Epic 11200. Passing
                // the working copy through unchanged still genuinely exercises the attempt ->
                // validation -> revision -> review pipeline; it claims no cropping behaviour.
                return await InspectAsync(workingCopy.Value, cancellationToken);

            default:
                return OperationResult.Fail<(WorkspaceFileRef, FileFacts)>(
                    FailureCode.PreconditionNotMet, $"Unsupported adapter kind '{runAdapter.Adapter}'.");
        }
    }

    private OperationResult<PrintOutput> BuildPrintOutput(
        SessionId sessionId, RevisionId revisionId, WorkflowSnapshot stateBeforeFinish,
        (WorkspaceFileRef Output, FileFacts Facts) work, CommandContext context)
    {
        if (stateBeforeFinish.Dimensions is not { } dimensions)
        {
            return OperationResult.Fail<PrintOutput>(
                FailureCode.PreconditionNotMet, "Print dimensions are required to record a PrintOutput.");
        }

        if (stateBeforeFinish.WhiteUnderbaseBranch is not { } branch)
        {
            return OperationResult.Fail<PrintOutput>(
                FailureCode.PreconditionNotMet, "A white-underbase branch is required to record a PrintOutput.");
        }

        OperationResult<ProductionPresetRef> preset = _presetProvider.GetVerifiedPreset();
        if (preset.IsFailure)
        {
            return OperationResult.Fail<PrintOutput>(preset.Failure);
        }

        RevisionId sourceRevisionId = stateBeforeFinish.UpstreamRevisionOf(StepKind.PhotoshopOutput) ?? revisionId;

        // Shares the same underlying GUID as its twin Revision row, purely so the two records
        // describing the same physical TIFF (the generic Revision the engine's review
        // mechanics need, and this production-specific record) are trivially joinable.
        PrintOutputId outputId = PrintOutputId.From(revisionId.Value);

        return OperationResult.Ok(PrintOutput.Create(
            outputId, sessionId, sourceRevisionId, dimensions, branch, preset.Value,
            work.Output, work.Facts.ByteLength, work.Facts.Sha256, context.NowUtc));
    }

    private async Task<OperationResult<(WorkspaceFileRef, FileFacts)>> InspectAsync(
        WorkspaceFileRef file, CancellationToken cancellationToken)
    {
        string absolute = _workspace.ResolveAbsolute(file);
        OperationResult<FileFacts> inspected = await _fileInspector.InspectAsync(absolute, cancellationToken);
        return inspected.IsSuccess
            ? OperationResult.Ok((file, inspected.Value))
            : OperationResult.Fail<(WorkspaceFileRef, FileFacts)>(inspected.Failure);
    }

    private async Task<OperationResult<SessionView>> FailAttemptAsync(
        SessionAggregate aggregate, WorkflowSnapshot stateAfterStart, CommandContext context, StepKind step,
        ProcessingAttempt runningAttempt, OperationFailure failure, CancellationToken cancellationToken)
    {
        WorkflowCommand.System.AttemptFailed failedCommand = new(runningAttempt.Id, step, failure);
        WorkflowTransition failedTransition = _engine.Apply(stateAfterStart, failedCommand, context);
        if (failedTransition.IsRejected)
        {
            return OperationResult.Fail<SessionView>(MapRejection(failedTransition.Rejection!));
        }

        ProcessingAttempt failedAttempt = runningAttempt.Fail(failure, context.NowUtc);
        ProcessingSession updatedSession =
            MergeSession(aggregate.Session, failedTransition.State, failedTransition.Effects, context.NowUtc);

        SessionMutation mutation = BuildMetadataMutation(
            aggregate, updatedSession, failedTransition.State, failedTransition.Effects, context,
            upsertAttempts: [failedAttempt]);

        await _repository.CommitAsync(mutation, cancellationToken);
        return OperationResult.Fail<SessionView>(failure);
    }

    private async Task<OperationResult<SessionView>> FailImportAsync(
        ProcessingSession session, WorkflowSnapshot stateAfterStart, CommandContext context,
        ProcessingAttempt runningAttempt, OperationFailure failure, CancellationToken cancellationToken)
    {
        WorkflowCommand.System.AttemptFailed failedCommand = new(runningAttempt.Id, StepKind.Import, failure);
        WorkflowTransition failedTransition = _engine.Apply(stateAfterStart, failedCommand, context);
        if (failedTransition.IsRejected)
        {
            return OperationResult.Fail<SessionView>(MapRejection(failedTransition.Rejection!));
        }

        ProcessingAttempt failedAttempt = runningAttempt.Fail(failure, context.NowUtc);
        ProcessingSession updatedSession = MergeSession(session, failedTransition.State, failedTransition.Effects, context.NowUtc);

        SessionMutation mutation = new(
            updatedSession, failedTransition.State.Steps, [], [], [failedAttempt], [], [], null, null);

        await _repository.CommitAsync(mutation, cancellationToken);
        return OperationResult.Fail<SessionView>(failure);
    }

    // -------------------------------------------------------------------------------------
    // Metadata assembly shared by every command path
    // -------------------------------------------------------------------------------------

    private SessionMutation BuildMetadataMutation(
        SessionAggregate aggregate, ProcessingSession updatedSession, WorkflowSnapshot newSnapshot,
        IReadOnlyList<WorkflowEffect> effects, CommandContext context,
        IReadOnlyList<Revision>? newRevisions = null, InputSnapshot? newInputSnapshot = null,
        IReadOnlyList<ProcessingAttempt>? upsertAttempts = null, IReadOnlyList<PrintOutput>? upsertOutputs = null)
    {
        List<ReviewDecision> reviews = [];
        List<RevisionInvalidation> revisionInvalidations = [];
        List<PrintOutput> outputUpdates = upsertOutputs is null ? [] : [.. upsertOutputs];
        AutomationLockChange? lockChange = null;

        foreach (WorkflowEffect effect in effects)
        {
            switch (effect)
            {
                case WorkflowEffect.RecordReview review:
                    reviews.Add(new ReviewDecision(
                        review.ReviewId, aggregate.Session.Id, review.Step, review.SubjectKind, review.SubjectId,
                        review.ReviewedHash, context.Operator, context.NowUtc, review.IsApproved,
                        review.QuickReason, review.Notes));

                    // A PrintOutput's cached ReviewState is a query convenience only — the
                    // ReviewDecision row above remains the authority — but it must still track
                    // the decision, or every reload would show every production TIFF as
                    // perpetually unreviewed regardless of what was actually approved.
                    if (review.SubjectKind == ReviewSubjectKind.PrintOutput)
                    {
                        PrintOutput? output = aggregate.Outputs
                            .Concat(outputUpdates)
                            .LastOrDefault(o => o.Id.Value == review.SubjectId);
                        if (output is not null)
                        {
                            outputUpdates.Add(output with
                            {
                                ReviewState = review.IsApproved ? ReviewState.Approved : ReviewState.Rejected,
                            });
                        }
                    }

                    break;

                case WorkflowEffect.InvalidateDescendants invalidate:
                    (List<RevisionInvalidation> revInv, List<PrintOutput> outInv) = ComputeDescendantInvalidations(
                        aggregate, invalidate.FromRevision, invalidate.Reason, context.NowUtc);
                    revisionInvalidations.AddRange(revInv);
                    outputUpdates.AddRange(outInv);
                    break;

                case WorkflowEffect.ReleaseAutomationLock:
                    lockChange = new AutomationLockChange(
                        AutomationLockAction.Release, aggregate.Session.Id, context.NowUtc, _processId, _machineName);
                    break;
            }
        }

        return new SessionMutation(
            updatedSession, newSnapshot.Steps, newRevisions ?? [], revisionInvalidations,
            upsertAttempts ?? [], reviews, outputUpdates, newInputSnapshot, lockChange);
    }

    /// <summary>
    /// Recursive descendant walk over <see cref="Revision.SourceRevisionId"/> (plan §10.4).
    /// The root itself is never invalidated by this walk — only what derives from it; siblings
    /// derived from the same ancestor at an earlier point are untouched by construction, since
    /// they do not descend from <paramref name="fromRevision"/>.
    /// </summary>
    private static (List<RevisionInvalidation>, List<PrintOutput>) ComputeDescendantInvalidations(
        SessionAggregate aggregate, RevisionId fromRevision, InvalidationReason reason, DateTimeOffset nowUtc)
    {
        HashSet<RevisionId> descendants = [];
        Queue<RevisionId> frontier = new();
        frontier.Enqueue(fromRevision);

        while (frontier.Count > 0)
        {
            RevisionId current = frontier.Dequeue();
            foreach (Revision candidate in aggregate.Revisions)
            {
                if (candidate.SourceRevisionId == current && descendants.Add(candidate.Id))
                {
                    frontier.Enqueue(candidate.Id);
                }
            }
        }

        List<RevisionInvalidation> revisionInvalidations =
            [.. descendants.Select(id => new RevisionInvalidation(id, reason, nowUtc))];

        List<PrintOutput> outputInvalidations =
        [
            .. aggregate.Outputs
                .Where(o => o.IsValid && descendants.Contains(o.SourceRevisionId))
                .Select(o => o.Invalidate(reason)),
        ];

        return (revisionInvalidations, outputInvalidations);
    }

    private static ProcessingSession MergeSession(
        ProcessingSession current, WorkflowSnapshot newSnapshot, IReadOnlyList<WorkflowEffect> effects, DateTimeOffset nowUtc)
    {
        ProcessingSession updated = current with
        {
            WorkflowType = newSnapshot.WorkflowType,
            OutputName = newSnapshot.OutputName,
            CurrentStep = newSnapshot.CurrentStep?.Step ?? current.CurrentStep,
            State = newSnapshot.SessionState,
            UpdatedAtUtc = nowUtc,
            Dimensions = newSnapshot.Dimensions,
            WhiteUnderbaseBranch = newSnapshot.WhiteUnderbaseBranch,
        };

        foreach (WorkflowEffect effect in effects)
        {
            updated = effect switch
            {
                WorkflowEffect.MarkSessionCompleted c => updated with { CompletedAtUtc = c.AtUtc },
                WorkflowEffect.MarkSessionHandedOff h => updated with { HandedOffAtUtc = h.AtUtc, HandOffReason = h.Reason },
                WorkflowEffect.MarkSessionAbandoned a => updated with { AbandonedAtUtc = a.AtUtc, AbandonReason = a.Reason },
                _ => updated,
            };
        }

        return updated;
    }

    private static WorkspaceDirRef ParentDirOf(WorkspaceFileRef file)
    {
        string path = file.RelativePath;
        int slash = path.LastIndexOf('/');
        return WorkspaceDirRef.Create(slash < 0 ? path : path[..slash]);
    }

    /// <summary>
    /// The file name component of an absolute path, without touching the file system.
    /// </summary>
    /// <remarks>
    /// Deliberately manual rather than the BCL path helper type: the Workflow project carries no
    /// file-system dependency at all (plan §5), and this is pure string splitting on an
    /// already-known path, not a disk access.
    /// </remarks>
    private static string FileNameOf(string absolutePath)
    {
        int lastSeparator = absolutePath.LastIndexOfAny(['\\', '/']);
        return lastSeparator < 0 ? absolutePath : absolutePath[(lastSeparator + 1)..];
    }

    /// <summary>The file name with its extension removed, for defaulting an unset output name.</summary>
    private static string StemOf(string absolutePath)
    {
        string fileName = FileNameOf(absolutePath);
        int dot = fileName.LastIndexOf('.');
        return dot > 0 ? fileName[..dot] : fileName;
    }

    private string AdapterIdFor(AdapterKind kind) => kind switch
    {
        AdapterKind.None => "internal-promote-v1",
        AdapterKind.Meitu => _meitu.AdapterId,
        AdapterKind.Photoshop => _photoshop.AdapterId,
        AdapterKind.Internal => "internal-trim-placeholder-v1",
        _ => "unknown",
    };

    private static OperationFailure MapRejection(CommandRejection rejection) =>
        OperationFailure.Create(
            FailureCode.PreconditionNotMet, rejection.ToString(), isRetryable: false);
}
