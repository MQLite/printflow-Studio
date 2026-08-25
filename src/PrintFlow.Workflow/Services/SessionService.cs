using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Reviews;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Trimming;
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
/// decides how a <see cref="WorkflowEffect.RunAdapter"/> or a
/// <see cref="WorkflowEffect.RunManualCrop"/> is actually carried out. A step that
/// produces a file makes <b>two</b> metadata transactions, not one: the first records the
/// attempt as <c>Running</c> before any file work begins (so a crash mid-attempt is
/// detectable — plan §38), and the second records the outcome once the file work and its
/// validation are complete.
/// </remarks>
public sealed class SessionService : ISessionService
{
    /// <summary>How many sessions "Recent Processing" shows at most (MVP design §10).</summary>
    public const int RecentSessionLimit = 100;

    /// <summary>How far back "Recent Processing" reaches (MVP design §10).</summary>
    public static readonly TimeSpan RecentSessionWindow = TimeSpan.FromDays(30);

    /// <summary>
    /// What a deterministic trim writes beside its working copy, inside the attempt's own
    /// directory.
    /// </summary>
    /// <remarks>
    /// A fixed name rather than a preset-driven one: this is an intermediate artefact of one
    /// attempt, not a deliverable. Operator-facing naming applies when the approved result is
    /// promoted into <c>Approved\</c>, and coupling Trim to the naming patterns would make a
    /// pixel operation depend on the signed preset for no gain. The extension is <c>.png</c>
    /// because a trimmed cut-out must keep its transparency (Part B §14).
    /// </remarks>
    private const string TrimOutputFileName = "trimmed.png";

    /// <summary>
    /// What a manual crop writes beside its working copy, inside the attempt's own directory.
    /// </summary>
    /// <remarks>
    /// A different name from <see cref="TrimOutputFileName"/> so the two are distinguishable on
    /// disk, and fixed for the same reason that one is. Uniqueness across attempts comes from
    /// the attempt directory rather than from the name, which is what lets a rejected crop and
    /// the crop that replaces it both survive without either overwriting the other (Part C2 §20).
    /// </remarks>
    private const string ManualCropOutputFileName = "manual-crop.png";

    private readonly IWorkflowEngine _engine;
    private readonly ISessionRepository _repository;
    private readonly IWorkspace _workspace;
    private readonly IFileInspector _fileInspector;
    private readonly IMeituProcessor _meitu;
    private readonly IPhotoshopOutputProcessor _photoshop;
    private readonly ITrimProcessor _trim;
    private readonly IManualCropProcessor _manualCrop;
    private readonly IWorkstationPresetProvider _presetProvider;
    private readonly IEnvironmentGate _environmentGate;
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
        ITrimProcessor trim,
        IManualCropProcessor manualCrop,
        IWorkstationPresetProvider presetProvider,
        IEnvironmentGate environmentGate,
        IIdGenerator idGenerator,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(fileInspector);
        ArgumentNullException.ThrowIfNull(meitu);
        ArgumentNullException.ThrowIfNull(photoshop);
        ArgumentNullException.ThrowIfNull(trim);
        ArgumentNullException.ThrowIfNull(manualCrop);
        ArgumentNullException.ThrowIfNull(presetProvider);
        ArgumentNullException.ThrowIfNull(environmentGate);
        ArgumentNullException.ThrowIfNull(idGenerator);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _engine = engine;
        _repository = repository;
        _workspace = workspace;
        _fileInspector = fileInspector;
        _meitu = meitu;
        _photoshop = photoshop;
        _trim = trim;
        _manualCrop = manualCrop;
        _presetProvider = presetProvider;
        _environmentGate = environmentGate;
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

        // A freshly imported session has produced nothing yet, so it holds no PrintOutput.
        return ViewOf(finished.State, [rootRevision], [], [succeededAttempt]);
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

        // The half of manual-crop eligibility the pure engine cannot see, checked before the
        // command reaches it. Button visibility is not a guard: a crop asked for against an
        // unrelated Trim failure, or against a rejected deterministic trim, is refused here even
        // if something managed to construct the command (Part C2 §13).
        if (command is WorkflowCommand.SubmitManualCrop &&
            !ManualCropEligibility.IsEligible(snapshot, aggregate.Attempts))
        {
            return OperationResult.Fail<SessionView>(
                FailureCode.PreconditionNotMet,
                "A manual crop is legal only after a Trim attempt reported ManualCropRequired, or " +
                "after an earlier manual crop was rejected.");
        }

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

        ProducingWork? work = ProducingWorkOf(transition.Effects);
        if (work is null)
        {
            ProcessingSession updatedSession = MergeSession(aggregate.Session, transition.State, transition.Effects, context.NowUtc);
            SessionMutation mutation = BuildMetadataMutation(aggregate, updatedSession, transition.State, transition.Effects, context);

            OperationResult<Unit> committed = await _repository.CommitAsync(mutation, cancellationToken);
            if (committed.IsFailure)
            {
                return OperationResult.Fail<SessionView>(committed.Failure);
            }

            return ViewOf(
                transition.State, aggregate.Revisions, OutputsAfter(aggregate.Outputs, mutation), aggregate.Attempts);
        }

        return await RunProducingStepAsync(aggregate, transition, context, work, cancellationToken);
    }

    /// <summary>
    /// The producing work one accepted command asked for, whichever effect described it.
    /// </summary>
    /// <remarks>
    /// A manual crop and an adapter call differ in what performs the work and in one extra piece
    /// of data — the operator's rectangle — and in nothing else that the attempt/Revision/review
    /// machinery cares about. Normalising both onto one shape is what lets that machinery exist
    /// once: a second near-copy of <see cref="RunProducingStepAsync"/> would be a second place
    /// for "record the attempt before the file work" and "hash before committing" to drift.
    /// </remarks>
    /// <param name="ProcessorId">Which code performs the work, written to the attempt row.</param>
    /// <param name="ManualCrop">
    /// The operator's rectangle in source pixels, or null for adapter-backed and internal
    /// automatic work. Its presence is what routes the work to <see cref="IManualCropProcessor"/>.
    /// </param>
    private sealed record ProducingWork(
        AttemptId AttemptId,
        StepKind Step,
        AdapterKind Adapter,
        OperationKind Operation,
        RevisionId? InputRevision,
        string ProcessorId,
        TrimBounds? ManualCrop);

    /// <summary>Reads the one producing effect out of a transition, or null when there is none.</summary>
    private ProducingWork? ProducingWorkOf(IReadOnlyList<WorkflowEffect> effects)
    {
        foreach (WorkflowEffect effect in effects)
        {
            switch (effect)
            {
                case WorkflowEffect.RunAdapter run:
                    return new ProducingWork(
                        run.AttemptId, run.Step, run.Adapter, run.Operation, run.InputRevision,
                        AdapterIdFor(run.Adapter), ManualCrop: null);

                case WorkflowEffect.RunManualCrop crop:
                    // The processor's own identity, asked for rather than hard-coded, so the
                    // attempt row can never claim an implementation that did not run.
                    return new ProducingWork(
                        crop.AttemptId, crop.Step, AdapterKind.Internal, OperationKind.ManualImport,
                        crop.InputRevision, _manualCrop.ProcessorId, crop.Crop);
            }
        }

        return null;
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
        return ViewOf(snapshot, loaded.Value.Revisions, loaded.Value.Outputs, loaded.Value.Attempts);
    }

    /// <inheritdoc />
    public Task<OperationResult<IReadOnlyList<SessionListItem>>> ListRecentAsync(CancellationToken cancellationToken) =>
        _repository.ListRecentAsync(
            RecentSessionLimit, _timeProvider.GetUtcNow() - RecentSessionWindow, cancellationToken);

    /// <summary>
    /// Builds the read model the UI sees, from the state the engine just produced plus the
    /// Revisions that state can refer to.
    /// </summary>
    /// <remarks>
    /// The single place a <see cref="SessionView"/> is constructed, so "what the screen knows"
    /// cannot drift between the import path, the command path and the reload path. The
    /// Revision, PrintOutput and attempt lists are passed in rather than re-read: after a
    /// command the caller already holds the authoritative set, including a row just written,
    /// and a second read would be a chance for the two to disagree.
    /// <para>
    /// The attempts are there for one thing — the current step's failure code, which the review
    /// surface needs in order to explain a <c>ManualCropRequired</c> outcome (Epic 11200 Part
    /// C1 §17). A path that ends in a failed attempt returns a failure rather than a view, so
    /// the code always arrives through the reload above; the lists passed on the success paths
    /// simply describe a step that did not fail.
    /// </para>
    /// </remarks>
    private OperationResult<SessionView> ViewOf(
        WorkflowSnapshot state,
        IReadOnlyList<Revision> revisions,
        IReadOnlyList<PrintOutput> outputs,
        IReadOnlyList<ProcessingAttempt> attempts) =>
        OperationResult.Ok(SessionView.From(
            state, _engine.AvailableCommands(state), revisions, outputs, attempts, ProcessingMode,
            _engine.AvailableReturnTargets(state)));

    /// <summary>
    /// The output rows as they stand after <paramref name="mutation"/> is committed.
    /// </summary>
    /// <remarks>
    /// <see cref="SessionMutation.UpsertOutputs"/> is a delta — a newly produced TIFF, a review
    /// state that just changed, an invalidation — so the view has to be built from the existing
    /// rows with that delta applied, keyed by id and last-write-wins, exactly as the repository
    /// will write it. Returning <paramref name="existing"/> unchanged would show a freshly
    /// approved output as still unreviewed until the next reload (Part 3C3B §15).
    /// </remarks>
    private static IReadOnlyList<PrintOutput> OutputsAfter(
        IReadOnlyList<PrintOutput> existing, SessionMutation mutation)
    {
        if (mutation.UpsertOutputs.Count == 0)
        {
            return existing;
        }

        Dictionary<PrintOutputId, PrintOutput> merged = [];
        foreach (PrintOutput output in existing.Concat(mutation.UpsertOutputs))
        {
            merged[output.Id] = output;
        }

        return [.. merged.Values];
    }

    /// <summary>
    /// How this installation actually processes work (Part 3C3A §8).
    /// </summary>
    /// <remarks>
    /// Reported as Fake if <i>either</i> adapter is a double. A session that would run one real
    /// application and one deterministic stand-in still produces output an operator must not
    /// mistake for production, so the weaker claim is the honest one.
    /// </remarks>
    private AdapterExecutionMode ProcessingMode =>
        _meitu.Mode == AdapterExecutionMode.Fake || _photoshop.Mode == AdapterExecutionMode.Fake
            ? AdapterExecutionMode.Fake
            : AdapterExecutionMode.Production;

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
    // Producing steps: two metadata transactions around the file work
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Runs one attempt that produces a file, from the opening transaction to the closing one.
    /// </summary>
    /// <remarks>
    /// One path for an adapter call, a deterministic trim, a promotion and a manual crop alike.
    /// The environment gate and the automation lock are taken only for genuinely adapter-backed
    /// steps, so a manual crop — which drives no external application — neither waits for the
    /// lock nor requires a verified workstation, exactly as the deterministic trim does not
    /// (Part B §17; Part C2 §9).
    /// </remarks>
    private async Task<OperationResult<SessionView>> RunProducingStepAsync(
        SessionAggregate aggregate, WorkflowTransition started, CommandContext context,
        ProducingWork work, CancellationToken cancellationToken)
    {
        StepDefinition? definition = started.State.Definition.Find(work.Step);
        if (definition is null)
        {
            return OperationResult.Fail<SessionView>(
                FailureCode.PreconditionNotMet, $"Step {work.Step} is not part of this workflow.");
        }

        if (definition.IsAdapterBacked)
        {
            OperationResult<Unit> gate = _environmentGate.Verify(AdapterModeFor(work.Adapter));
            if (gate.IsFailure)
            {
                return OperationResult.Fail<SessionView>(gate.Failure);
            }
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
            context.NewAttemptId, aggregate.Session.Id, work.Step, work.InputRevision,
            work.Operation, work.ProcessorId, context.NowUtc, retrySequence: retrySequence);

        // The parameter record, written with the opening transaction — before any pixel work —
        // so the row says what this attempt was asked to do rather than what it turned out to
        // do. A later attempt with a different margin gets its own row; this one is never
        // rewritten, which is what makes the two settings comparable afterwards (Part C3 §14,
        // §15). Only the deterministic trim has parameters: a manual crop is a rectangle a
        // human drew, and a margin means nothing to it (§17).
        if (work is { Step: StepKind.Trim, ManualCrop: null })
        {
            runningAttempt = runningAttempt.WithTrimParameters(started.State.TrimMargin);
        }

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

        OperationResult<(WorkspaceFileRef Output, FileFacts Facts)> produced =
            await PerformStepWorkAsync(afterStart, started.State, definition, work, context, cancellationToken);

        if (produced.IsFailure)
        {
            return await FailAttemptAsync(
                afterStart, started.State, context, work.Step, runningAttempt, produced.Failure, cancellationToken);
        }

        // The Revision hangs off the Revision this attempt actually consumed. For a manual crop
        // that is the file the operator drew on, which is what makes ManualImport lineage
        // truthful rather than positional (Part C2 §16).
        RevisionId revisionId = RevisionId.From(_idGenerator.NewId());
        Revision newRevision = Revision.Create(
            revisionId, aggregate.Session.Id, work.InputRevision, work.Operation,
            produced.Value.Output, produced.Value.Facts, context.NowUtc);

        WorkflowCommand.System.AttemptSucceeded succeeded = new(
            context.NewAttemptId, work.Step, revisionId, produced.Value.Facts.Sha256);
        WorkflowTransition finished = _engine.Apply(started.State, succeeded, context);
        if (finished.IsRejected)
        {
            return OperationResult.Fail<SessionView>(MapRejection(finished.Rejection!));
        }

        ProcessingAttempt succeededAttempt = runningAttempt.Succeed(revisionId, context.NowUtc);
        ProcessingSession sessionAfterFinish = MergeSession(sessionAfterStart, finished.State, finished.Effects, context.NowUtc);

        List<PrintOutput> newOutputs = [];
        if (work.Step == StepKind.PhotoshopOutput)
        {
            OperationResult<PrintOutput> output = BuildPrintOutput(
                aggregate.Session.Id, revisionId, started.State, produced.Value, context);
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

        return ViewOf(
            finished.State,
            [.. afterStart.Revisions, newRevision],
            OutputsAfter(afterStart.Outputs, finishing),
            [.. afterStart.Attempts, succeededAttempt]);
    }

    private async Task<OperationResult<(WorkspaceFileRef Output, FileFacts Facts)>> PerformStepWorkAsync(
        SessionAggregate aggregate, WorkflowSnapshot state, StepDefinition definition,
        ProducingWork work, CommandContext context, CancellationToken cancellationToken)
    {
        WorkspaceDirRef session = aggregate.Session.Workspace;
        WorkspaceFileRef? input = work.InputRevision is RevisionId inputId
            ? aggregate.Revisions.FirstOrDefault(r => r.Id == inputId)?.File
            : null;

        if (work.Adapter == AdapterKind.None)
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
                FailureCode.PreconditionNotMet, $"Step {work.Step} has no upstream Revision to work from.");
        }

        OperationResult<WorkspaceFileRef> workingCopy =
            await _workspace.CreateWorkingCopyAsync(session, context.NewAttemptId, upstreamRef, cancellationToken);
        if (workingCopy.IsFailure)
        {
            return OperationResult.Fail<(WorkspaceFileRef, FileFacts)>(workingCopy.Failure);
        }

        switch (work.Adapter)
        {
            case AdapterKind.Meitu:
            {
                MeituOperation operation = work.Operation == OperationKind.Enhance
                    ? MeituOperation.Enhance
                    : MeituOperation.RemoveBackground;

                // The expected output is a new file beside the working copy, named by the same
                // preset-driven naming authority that names the approved deliverable, and never
                // the working copy itself.
                //
                // It used to be the working copy, because the fake adapter processes in place and
                // nothing downstream cared. A real Meitu does care: exporting over the input would
                // destroy the very bytes the attempt validates its result against, and §19 of
                // Epic 11300 Part B2B requires the enhanced result to be a new file. Uniqueness
                // across attempts comes from the attempt's own directory rather than from the
                // name, which is what lets a rejected result and the retry that replaces it both
                // survive on disk (Epic 11200 Part C2 §20).
                OperationResult<NamingPatternSet> meituPatterns = _presetProvider.GetNamingPatterns();
                if (meituPatterns.IsFailure)
                {
                    return OperationResult.Fail<(WorkspaceFileRef, FileFacts)>(meituPatterns.Failure);
                }

                string producedName = OutputFileNaming.BuildProposedFileName(
                    work.Operation == OperationKind.Enhance
                        ? NamingArtifactKind.Enhanced
                        : NamingArtifactKind.Cutout,
                    aggregate.Session.OutputName,
                    meituPatterns.Value);

                OperationResult<AdapterOutput> result = await _meitu.ProcessAsync(
                    new MeituRequest(
                        workingCopy.Value,
                        operation,
                        ParentDirOf(workingCopy.Value),
                        SiblingOf(workingCopy.Value, producedName)),
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
            {
                // Checked rather than assumed: AdapterKind.Internal means "deterministic
                // in-process pixel work", and Trim is only the first such step. A future
                // internal step routed here by accident would silently be trimmed.
                if (definition.Kind != StepKind.Trim)
                {
                    return OperationResult.Fail<(WorkspaceFileRef, FileFacts)>(
                        FailureCode.PreconditionNotMet,
                        $"Step {definition.Kind} is internal but has no deterministic processor.");
                }

                // A rectangle on the work means a human chose it, so the alpha scan is skipped
                // entirely: the automatic path already refused this file, and re-running it
                // would refuse again for the same reason (Part C2 §10, §21).
                if (work.ManualCrop is { } crop)
                {
                    OperationResult<ManualCropResult> cropped = await _manualCrop.CropAsync(
                        new ManualCropRequest(
                            workingCopy.Value,
                            SiblingOf(workingCopy.Value, ManualCropOutputFileName),
                            crop),
                        cancellationToken);

                    return cropped.IsFailure
                        ? OperationResult.Fail<(WorkspaceFileRef, FileFacts)>(cropped.Failure)
                        : await InspectAsync(cropped.Value.ProducedFile, cancellationToken);
                }

                // The operator's recorded decision, not a constant. It arrives here from
                // WorkflowSnapshot.TrimMargin, which SetTrimParameters is the only way to
                // change — so nothing between the screen and the processor can substitute a
                // margin, and the same value is what the attempt row recorded before this ran
                // (Epic 11200 Part C3 §13, §14).
                OperationResult<TrimResult> trimmed = await _trim.TrimAsync(
                    new TrimRequest(
                        workingCopy.Value,
                        SiblingOf(workingCopy.Value, TrimOutputFileName),
                        state.TrimMargin),
                    cancellationToken);
                if (trimmed.IsFailure)
                {
                    return OperationResult.Fail<(WorkspaceFileRef, FileFacts)>(trimmed.Failure);
                }

                TrimResult result = trimmed.Value;
                if (result.Outcome == TrimOutcome.ManualCropRequired)
                {
                    // Deliberately a failed attempt and not a Revision. Nothing was produced,
                    // so fabricating one — from the untrimmed working copy, or as a
                    // ManualImport of a file no human has edited yet — would record a trim
                    // that never happened. The step ends in Failed with a stable code the
                    // manual-crop surface (Epic 11200 Part C) can route on, and the attempt
                    // row keeps the reason (Part B §10).
                    return OperationResult.Fail<(WorkspaceFileRef, FileFacts)>(
                        OperationFailure.Create(
                            FailureCode.ManualCropRequired,
                            result.ManualCropReason ?? "No usable alpha content was found.",
                            isRetryable: false));
                }

                return await InspectAsync(result.ProducedFile!.Value, cancellationToken);
            }

            default:
                return OperationResult.Fail<(WorkspaceFileRef, FileFacts)>(
                    FailureCode.PreconditionNotMet, $"Unsupported adapter kind '{work.Adapter}'.");
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
            upsertAttempts ?? [], reviews, outputUpdates, newInputSnapshot, lockChange)
        {
            RemoveSteps = StepsNoLongerInWorkflow(aggregate, newSnapshot),
        };
    }

    /// <summary>
    /// The step rows a re-shaped session has left behind (Part 3C3B, defect fix).
    /// </summary>
    /// <remarks>
    /// Choosing a different workflow replaces the step list, and upserting the new one does not
    /// remove the old one's rows. Since <c>ISessionRepository.LoadAsync</c> reads every step row
    /// the session has, those leftovers came back as part of the reconstructed snapshot — so a
    /// session switched to GENERATE_PRINT_TIFF reloaded still waiting on Enhancement, a step
    /// that workflow does not contain.
    /// <para>
    /// Computed by comparing the two step lists rather than from the workflow definitions, so
    /// there is nothing here to keep in step with the catalogue.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<StepKind> StepsNoLongerInWorkflow(
        SessionAggregate aggregate, WorkflowSnapshot newSnapshot)
    {
        HashSet<StepKind> kept = [.. newSnapshot.Steps.Select(step => step.Step)];
        return [.. aggregate.Steps.Select(step => step.Step).Where(kind => !kept.Contains(kind))];
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

        // A PrintOutput is caught by either of two distinct paths: its declared source
        // (SourceRevisionId) was invalidated, or — when nothing sits between the shared source
        // and PhotoshopOutput (e.g. GENERATE_PRINT_TIFF, which has no Enhancement/Trim step) —
        // its own twin Revision was, which the walk finds under the shared GUID (plan §16 item
        // 4) rather than under SourceRevisionId, since a PrintOutput's SourceRevisionId is the
        // upstream design it was produced from, never itself.
        List<PrintOutput> outputInvalidations =
        [
            .. aggregate.Outputs
                .Where(o => o.IsValid &&
                    (descendants.Contains(o.SourceRevisionId) || descendants.Contains(RevisionId.From(o.Id.Value))))
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
            TrimMargin = newSnapshot.TrimMargin,
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

    /// <summary>A reference to <paramref name="fileName"/> beside <paramref name="file"/>.</summary>
    /// <remarks>
    /// The same pure string work as <see cref="ParentDirOf"/> and for the same reason: the
    /// Workflow project has no file-system dependency, and naming a sibling inside an attempt's
    /// own working directory is not a path resolution — only <see cref="IWorkspace"/> turns a
    /// reference into a real path.
    /// </remarks>
    private static WorkspaceFileRef SiblingOf(WorkspaceFileRef file, string fileName)
    {
        string path = file.RelativePath;
        int slash = path.LastIndexOf('/');
        string directory = slash < 0 ? string.Empty : path[..(slash + 1)];
        return WorkspaceFileRef.Create(directory + fileName, file.Area);
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
        AdapterKind.Internal => _trim.ProcessorId,
        _ => "unknown",
    };

    /// <summary>
    /// The declared execution mode of the adapter that would actually run this step, for
    /// <see cref="IEnvironmentGate"/>. Only ever called for <see cref="AdapterKind.Meitu"/> or
    /// <see cref="AdapterKind.Photoshop"/> (plan §8: the gate applies to adapter-backed steps).
    /// </summary>
    private AdapterExecutionMode AdapterModeFor(AdapterKind kind) => kind switch
    {
        AdapterKind.Meitu => _meitu.Mode,
        AdapterKind.Photoshop => _photoshop.Mode,
        _ => throw new InvalidOperationException(
            $"Adapter kind '{kind}' is not adapter-backed; it has no execution mode to gate."),
    };

    private static OperationFailure MapRejection(CommandRejection rejection) =>
        OperationFailure.Create(
            FailureCode.PreconditionNotMet, rejection.ToString(), isRetryable: false);
}
