using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Automation;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Reviews;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Settings;
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
public sealed partial class SessionService : ISessionService
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

    /// <summary>
    /// The only disposal route in the system, used by exactly one caller: final rejection of a
    /// production TIFF (Epic 11400 Part C2B §14; MVP design §10).
    /// </summary>
    /// <remarks>
    /// A port rather than a concrete type, so nothing in the workflow layer knows how a file is
    /// recycled — and there is deliberately no <c>File.Delete</c> anywhere near it. A recycle
    /// that fails is a structured failure that stops the rejection, never a fallback to
    /// permanent deletion.
    /// </remarks>
    private readonly IRecycleBin _recycleBin;

    private readonly IFileInspector _fileInspector;
    private readonly IMeituProcessor _meitu;
    private readonly IPhotoshopOutputProcessor _photoshop;
    private readonly IPdfPreparationProcessor _pdf;
    private readonly ITrimProcessor _trim;
    private readonly IManualCropProcessor _manualCrop;
    private readonly IManualResultImporter? _manualResults;
    private readonly IDiagnosticImagePreviewDecoder? _diagnosticImages;

    /// <summary>
    /// The persisted operator preferences, read at exactly one point: the trim safety margin a
    /// <b>newly imported</b> job starts with (SCRUM-11118).
    /// </summary>
    /// <remarks>
    /// Optional, and answered by <c>TrimMargin.Tight</c> when it is absent, unreadable or unset,
    /// so every existing construction of this service keeps the behaviour it has: crop exactly
    /// to the alpha content.
    /// <para>
    /// <b>It is a default, not a policy.</b> It is consulted once, at import, and the value it
    /// yields becomes that session's own persisted pending trim decision — which the operator
    /// may then change, and which nothing here ever revisits. Changing the default later cannot
    /// reach back into a session that already exists, and no already-run trim, Revision or
    /// approved output is affected by it at all.
    /// </para>
    /// </remarks>
    private readonly ISettingsRepository? _settings;

    private readonly IWorkstationPresetProvider _presetProvider;
    private readonly IEnvironmentGate _environmentGate;
    private readonly IWorkstationAutomationLeaseManager? _automationLeases;
    private readonly IWorkstationAutomationLease? _enclosingAutomationLease;
    private readonly RevisionIntegrityGuard _integrityGuard;
    private readonly IIdGenerator _idGenerator;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Which attempt is currently driving automation, and the only route an operator's Stop
    /// takes to reach it (Epic 11300 Part D2A §28).
    /// </summary>
    /// <remarks>
    /// Owned here rather than injected because its lifetime is exactly this service's: it holds
    /// in-process state about a run this service is performing, and a second
    /// <see cref="SessionService"/> sharing one would be able to stop a run it is not
    /// executing. It is emphatically not the automation lock — that one is persisted,
    /// machine-wide, and survives this process (§32).
    /// </remarks>
    private readonly AutomationRunRegistry _runs = new();

    /// <summary>
    /// The one opaque, single-use enlargement offer this service currently exposes per session.
    /// The shell sees only the Guid; the exact Revision/hash/target command stays here. Access is
    /// kept under one short lock so replacing a displayed offer and accepting that exact display
    /// have a single order even when a refresh and a click arrive together.
    /// </summary>
    private readonly object _enlargementOfferGate = new();
    private readonly Dictionary<SessionId, EnlargementOffer> _enlargementOffers = [];

    private sealed record EnlargementOffer(
        Guid Id, WorkflowCommand.AuthoriseEnlargement Command);

    private readonly int _processId;
    private readonly string _machineName;

    public SessionService(
        IWorkflowEngine engine,
        ISessionRepository repository,
        IWorkspace workspace,
        IRecycleBin recycleBin,
        IFileInspector fileInspector,
        IMeituProcessor meitu,
        IPhotoshopOutputProcessor photoshop,
        ITrimProcessor trim,
        IManualCropProcessor manualCrop,
        IWorkstationPresetProvider presetProvider,
        IEnvironmentGate environmentGate,
        IIdGenerator idGenerator,
        TimeProvider timeProvider,
        IPdfPreparationProcessor? pdf = null,
        IManualResultImporter? manualResults = null,
        ISettingsRepository? settings = null,
        IDiagnosticImagePreviewDecoder? diagnosticImages = null,
        IWorkstationAutomationLease? enclosingAutomationLease = null,
        IWorkstationAutomationLeaseManager? automationLeases = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(recycleBin);
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
        _recycleBin = recycleBin;
        _fileInspector = fileInspector;
        _meitu = meitu;
        _photoshop = photoshop;
        _pdf = pdf ?? new UnavailablePdfPreparationProcessor();
        _trim = trim;
        _manualCrop = manualCrop;
        _manualResults = manualResults;
        _settings = settings;
        _diagnosticImages = diagnosticImages;
        _presetProvider = presetProvider;
        _environmentGate = environmentGate;
        _automationLeases = automationLeases;
        _enclosingAutomationLease = enclosingAutomationLease;
        _idGenerator = idGenerator;
        _timeProvider = timeProvider;
        _integrityGuard = new RevisionIntegrityGuard(workspace, fileInspector);
        _processId = Environment.ProcessId;
        _machineName = Environment.MachineName;
        _runs.Changed += (_, view) => AutomationRuntimeChanged?.Invoke(this, view);
    }

    /// <inheritdoc />
    public event EventHandler<AutomationRuntimeView>? AutomationRuntimeChanged;

    /// <inheritdoc />
    public AutomationRuntimeView GetAutomationRuntime(SessionId id) => _runs.For(id);

    /// <inheritdoc />
    public OperationResult<Unit> RequestStop(SessionId id, AutomationStopMode mode) =>
        _runs.RequestStop(id, mode);

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

        // The configured default, applied to this new job only. Read here rather than inside the
        // domain constructors, so the value a session is created with is a decision this service
        // made once and persisted, not a lookup the domain repeats (SCRUM-11118).
        TrimMargin startingMargin = await ResolveDefaultTrimMarginAsync(cancellationToken)
            .ConfigureAwait(false);

        ProcessingSession session = ProcessingSession.Start(id, workflowType, name, workspaceDir, context.NowUtc)
            with { TrimMargin = startingMargin };
        WorkflowSnapshot initialSnapshot = WorkflowSnapshot.Create(id, workflowType, name, context.NowUtc)
            with { TrimMargin = startingMargin };

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

        OperationResult<(WorkspaceFileRef Source, FileFacts Facts)> established;
        try
        {
            established = await EstablishSourceAsync(workspaceDir, sourceAbsolutePath, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The same containment RunProducingStepAsync already gives every producing step,
            // extended to the one producing path that had none. Import is the only operation an
            // operator can start from Home, and its command is cancellable, so an escape here is
            // an escape all the way out of the view model — which is a terminated shell, not a
            // reported failure. Nothing in this method's own awaits can reach here today (the
            // workspace and the repository both answer cancellation structurally), so this is the
            // boundary that keeps that true rather than a second cancellation policy.
            established = OperationResult.Fail<(WorkspaceFileRef, FileFacts)>(OperationFailure.Create(
                FailureCode.Cancelled,
                "The import was cancelled before the source was established. No source snapshot " +
                "and no Revision were created.",
                isRetryable: true,
                context: new Dictionary<string, string>
                {
                    ["attemptId"] = runningAttempt.Id.ToString(),
                    ["step"] = StepKind.Import.ToString(),
                    ["retainedExternalState"] = "none",
                }));
        }

        // The instant the import work ended, for the same reason the producing path observes one
        // (SCRUM-11137 prerequisite §3). Copying and hashing a large source is not instantaneous,
        // and the opening context was stamped before any of it happened.
        CommandContext closing = context.At(_timeProvider.GetUtcNow());

        if (established.IsFailure)
        {
            return await FailImportAsync(session, started.State, closing, runningAttempt, established.Failure, cancellationToken);
        }

        (WorkspaceFileRef importedSource, FileFacts sourceFacts) = established.Value;

        RevisionId revisionId = RevisionId.From(_idGenerator.NewId());
        Revision rootRevision = Revision.Create(
            revisionId, id, null, OperationKind.Import, importedSource, sourceFacts, closing.NowUtc);

        WorkflowCommand.System.AttemptSucceeded succeeded = new(
            closing.NewAttemptId, StepKind.Import, revisionId, sourceFacts.Sha256);
        WorkflowTransition finished = _engine.Apply(started.State, succeeded, closing);
        if (finished.IsRejected)
        {
            return OperationResult.Fail<SessionView>(MapRejection(finished.Rejection!));
        }

        InputSnapshot snapshotRecord = new(
            SnapshotId.From(_idGenerator.NewId()), id, revisionId, sourceAbsolutePath,
            FileNameOf(sourceAbsolutePath), closing.NowUtc);

        ProcessingSession sessionAfterImport = MergeSession(session, finished.State, finished.Effects, closing.NowUtc);
        ProcessingAttempt succeededAttempt = runningAttempt.Succeed(revisionId, closing.NowUtc);

        SessionAggregate aggregateSoFar = new(session, null, started.State.Steps, [], [runningAttempt], [], []);
        SessionMutation closingMutation = BuildMetadataMutation(
            aggregateSoFar, sessionAfterImport, finished.State, finished.Effects, closing,
            newRevisions: [rootRevision], newInputSnapshot: snapshotRecord, upsertAttempts: [succeededAttempt]);

        OperationResult<Unit> committedClosing = await _repository.CommitAsync(closingMutation, cancellationToken);
        if (committedClosing.IsFailure)
        {
            return OperationResult.Fail<SessionView>(committedClosing.Failure);
        }

        // A freshly imported session has produced nothing yet, so it holds no PrintOutput.
        return ViewOf(finished.State, [rootRevision], [], [succeededAttempt]);
    }

    /// <summary>
    /// The trim safety margin a job imported now starts with (SCRUM-11118).
    /// </summary>
    /// <remarks>
    /// Every unusable answer resolves to <see cref="TrimMargin.Tight"/> — no repository, a
    /// persistence failure, no persisted row, a value that is not a whole number, and a
    /// negative or zero one. That is the margin every trim has had since Epic 11200 Part B, so
    /// the failure direction of a settings read is "behave exactly as before", never "start a
    /// production job with a margin nobody chose".
    /// <para>
    /// A uniform margin, because the setting is one number. The per-edge form stays what it has
    /// always been: a decision the operator makes on a specific image while looking at it, not
    /// a default (Epic 11200 Part C3 §10).
    /// </para>
    /// </remarks>
    private async Task<TrimMargin> ResolveDefaultTrimMarginAsync(CancellationToken cancellationToken)
    {
        if (_settings is null)
        {
            return TrimMargin.Tight;
        }

        OperationResult<SettingEntry?> persisted = await _settings
            .ReadAsync(SettingKey.TrimSafetyMarginPixels, cancellationToken)
            .ConfigureAwait(false);

        return persisted.IsSuccess && persisted.Value?.AsInteger() is { } pixels && pixels > 0
            ? TrimMargin.Uniform(pixels)
            : TrimMargin.Tight;
    }

    /// <inheritdoc />
    public async Task<OperationResult<SessionView>> ExecuteAsync(
        SessionId id, WorkflowCommand command, string? operatorName, CancellationToken cancellationToken)
    {
        using IDisposable? completionLease = command is WorkflowCommand.Complete or WorkflowCommand.AddAnotherSize
            ? await SessionCompletionGate.EnterAsync(id, cancellationToken) : null;
        return await ExecuteCoreAsync(id, command, operatorName, expectedFailureAttemptId: null, cancellationToken);
    }

    private async Task<OperationResult<SessionView>> ExecuteCoreAsync(
        SessionId id,
        WorkflowCommand command,
        string? operatorName,
        AttemptId? expectedFailureAttemptId,
        CancellationToken cancellationToken)
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
        WorkflowSnapshot snapshot = aggregate.ToSnapshot(ConfiguredRecommendations());

        // Error Details recovery is addressed to one immutable attempt. Its earlier diagnostic
        // read is presentation, not authority: validate the identity again on the aggregate this
        // command will actually apply to, so a retry/new failure between those two reads cannot
        // receive an action from a stale page.
        if (expectedFailureAttemptId is { } expected &&
            ErrorDetailsSelection.Current(snapshot.CurrentStep, aggregate.Attempts)?.Id != expected)
        {
            return OperationResult.Fail<SessionView>(
                FailureCode.PreconditionNotMet,
                "This recovery action is no longer available for the error that was opened.");
        }

        CommandContext context = CommandContext.Create(_timeProvider, _idGenerator, operatorName);

        // Legacy PSD sessions may already have acknowledged an opaque source under the old
        // workflow. Do not fabricate preparation metadata or continue their late-failure path.
        if ((snapshot.RequiresPsdPreparation || snapshot.RequiresPdfPreparation) && snapshot.Step(StepKind.OriginalConfirmation)?.CurrentRevisionId is null &&
            (command is WorkflowCommand.SetPrintDimensions or WorkflowCommand.SetPresetFitSize or
                WorkflowCommand.SetCustomTargetEdgeSize ||
             command is WorkflowCommand.StartStep { Step: not (StepKind.Import or StepKind.OriginalConfirmation) }))
        {
            return OperationResult.Fail<SessionView>(snapshot.RequiresPdfPreparation ? FailureCode.PdfPreparationFailed : FailureCode.PsdPreparationFailed,
                "This source document has no prepared raster. Return to Original Confirmation and prepare it before continuing.");
        }

        if (command is WorkflowCommand.Retry && snapshot.RequiresPdfPreparation &&
            aggregate.Attempts.LastOrDefault(a => a.Operation == OperationKind.PreparePdf)?.Failure is
                { Code: FailureCode.PdfMultiplePages or FailureCode.PdfEncrypted or FailureCode.PdfUnreadable } refusal)
            return OperationResult.Fail<SessionView>(refusal);

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

        if (command is WorkflowCommand.SubmitManualResult submission &&
            (!ManualResultEligibility.CanSubmit(snapshot) || snapshot.CurrentStep!.Step != submission.Step))
            return OperationResult.Fail<SessionView>(FailureCode.PreconditionNotMet,
                "Manual result submission requires an eligible handed-off step.");

        OperationResult<Unit> integrity = await EnsureIntegrityAsync(aggregate, snapshot, command, context, cancellationToken);
        if (integrity.IsFailure)
        {
            return OperationResult.Fail<SessionView>(integrity.Failure);
        }

        // Calculated before the command is applied, so a session whose source cannot support a
        // plan records no bounds at all rather than bounds with nothing behind them. Its inputs
        // are the bytes EnsureIntegrityAsync has just re-verified (Epic 11400 Part B1A.2A §14).
        RecordedSize? recorded = null;
        if (command is WorkflowCommand.SetPrintDimensions
            or WorkflowCommand.SetPresetFitSize
            or WorkflowCommand.SetCustomTargetEdgeSize)
        {
            OperationResult<RecordedSize> planned = PlanSize(aggregate, snapshot, command);
            if (planned.IsFailure)
            {
                return OperationResult.Fail<SessionView>(planned.Failure);
            }

            recorded = planned.Value;
        }

        WorkflowTransition transition = _engine.Apply(snapshot, command, context);
        if (transition.IsRejected)
        {
            return OperationResult.Fail<SessionView>(MapRejection(transition.Rejection!));
        }

        ProducingWork? work = ProducingWorkOf(transition.Effects);
        if (work is null)
        {
            // The engine accepted the decision and stamped the semantics; the millimetres, the
            // selection and the plan it could not calculate are attached here, to the state it
            // produced. Nothing else writes these fields, so the accepted decision and the
            // geometry behind it are always the same act (Epic 11400 Part B1A.2D §17).
            WorkflowSnapshot state = recorded is null
                ? transition.State
                : transition.State with
                {
                    Dimensions = recorded.Dimensions,
                    SizeSelection = recorded.Selection,
                    PrintPreparationPlan = recorded.BoundsPlan,
                    TargetEdgePlan = recorded.TargetEdgePlan,
                };

            // The file lifecycle a final production-TIFF review carries with it: promotion into
            // Approved\ before an approval may be recorded, disposal through the Recycle Bin
            // before a rejection may be (Epic 11400 Part C2B §6, §10, §12, §15). It runs here,
            // after the engine has accepted the decision and before the transaction that records
            // it, so a promotion or a disposal that did not happen cannot leave a reviewed state
            // behind. Every other command — and every review of a Revision that is not a
            // PrintOutput — passes straight through with nothing to do.
            OperationResult<PrintOutput?> lifecycle =
                await PerformFinalReviewFileWorkAsync(aggregate, command, context, cancellationToken);
            if (lifecycle.IsFailure)
            {
                return OperationResult.Fail<SessionView>(lifecycle.Failure);
            }

            ProcessingSession updatedSession = MergeSession(aggregate.Session, state, transition.Effects, context.NowUtc);
            SessionMutation mutation = BuildMetadataMutation(
                aggregate, updatedSession, state, transition.Effects, context,
                upsertOutputs: lifecycle.Value is { } lifecycleOutput ? [lifecycleOutput] : null);

            OperationResult<Unit> committed = await _repository.CommitAsync(mutation, cancellationToken);
            if (committed.IsFailure)
            {
                return OperationResult.Fail<SessionView>(committed.Failure);
            }

            if (transition.Effects.Any(effect => effect is WorkflowEffect.CleanupWorking))
            {
                // Completion has committed. Maintenance failure must never relabel approved
                // production work as failed. Startup retries from the same durable metadata.
                SessionCleanupResult cleanup = await new SessionRetentionService(_repository, _workspace, _timeProvider)
                    .CleanupUnderCompletionGateAsync(id, CancellationToken.None);
                OperationResult<SessionView> completedView = await LoadAsync(id, CancellationToken.None);
                return completedView.IsFailure ? completedView :
                    OperationResult.Ok(completedView.Value with { CompletionCleanup = cleanup });
            }

            return ViewOf(
                state, aggregate.Revisions, OutputsAfter(aggregate.Outputs, mutation), aggregate.Attempts);
        }

        return await RunProducingStepAsync(aggregate, transition, context, work, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult<SessionView>> AuthoriseCurrentEnlargementAsync(
        SessionId id,
        Guid enlargementOfferId,
        string? operatorName,
        CancellationToken cancellationToken)
    {
        EnlargementOffer offer;
        lock (_enlargementOfferGate)
        {
            if (!_enlargementOffers.TryGetValue(id, out offer!) || offer.Id != enlargementOfferId)
            {
                return OperationResult.Fail<SessionView>(
                    FailureCode.PreconditionNotMet,
                    "That enlargement offer is no longer current. Review the refreshed size before continuing.");
            }

            // Acceptance linearises here. A stale click never consumes the replacement offer,
            // while a second click on this exact handle is refused as already consumed.
            _enlargementOffers.Remove(id);
        }

        // ExecuteAsync loads again, re-verifies the source bytes and asks the engine to match
        // every hidden binding fact captured when the warning was rendered. If anything changed
        // since then, this exact command is refused and the caller refreshes persisted truth.
        return await ExecuteAsync(
            id,
            offer.Command,
            operatorName,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult<PrintDimensionsPreflight>> PreviewPrintDimensionsAsync(
        SessionId id, WorkflowCommand sizingCommand, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sizingCommand);

        OperationResult<SessionAggregate?> loaded = await _repository.LoadAsync(id, cancellationToken);
        if (loaded.IsFailure)
        {
            return OperationResult.Fail<PrintDimensionsPreflight>(loaded.Failure);
        }

        if (loaded.Value is not { } aggregate)
        {
            return OperationResult.Fail<PrintDimensionsPreflight>(
                FailureCode.PreconditionNotMet, $"No session {id} exists.");
        }

        WorkflowSnapshot snapshot = aggregate.ToSnapshot(ConfiguredRecommendations());
        OperationResult<RecordedSize> planned = PlanSize(aggregate, snapshot, sizingCommand);
        if (planned.IsFailure)
        {
            return OperationResult.Fail<PrintDimensionsPreflight>(planned.Failure);
        }

        return OperationResult.Ok(PreflightFrom(
            planned.Value,
            aggregate.Revisions,
            aggregate.Attempts,
            snapshot,
            enlargementAuthorised: false,
            enlargementOfferId: null));
    }

    /// <summary>
    /// One recorded size decision, in every form the session has to keep
    /// (Epic 11400 Part B1A.2D §6, §17).
    /// </summary>
    /// <remarks>
    /// The four values are written together or not at all, which is what stops a session from
    /// holding millimetres with no plan behind them, or a target-edge plan beside a fit box.
    /// Exactly one of <paramref name="BoundsPlan"/> and <paramref name="TargetEdgePlan"/> is ever
    /// set: they are the two accepted sizing contracts, and a row that held both would be a
    /// session that made two different decisions at once (§5).
    /// </remarks>
    /// <param name="Dimensions">
    /// The operator-facing millimetres, kept for display, naming and the <c>PrintOutput</c> audit.
    /// Never an executable Photoshop target pair.
    /// </param>
    /// <param name="Selection">
    /// The flexible-size decision, or null for a typed custom fit box — which predates the
    /// flexible-size vocabulary and is not retrofitted into it (§21).
    /// </param>
    /// <param name="BoundsPlan">The FitWithinBoundsV1 plan, or null for a target-edge decision.</param>
    /// <param name="TargetEdgePlan">The TargetEdgeV1 plan, or null for a maximum-bound decision.</param>
    private sealed record RecordedSize(
        PrintDimensions Dimensions,
        FlexibleSizeSelection? Selection,
        PrintPreparationPlan? BoundsPlan,
        TargetEdgePrintPreparationPlan? TargetEdgePlan);

    /// <summary>
    /// Calculates the source-bound geometry one accepted sizing command asks for, or reports why
    /// it cannot (Epic 11400 Part B1A.2A §5, §6, §8; Part B1A.2D §16, §17).
    /// </summary>
    /// <remarks>
    /// Everything the pure engine cannot see, in one place: the verified preset's configured
    /// recommendations, and the source's own validated pixel dimensions.
    /// <para>
    /// It contains no sizing arithmetic of its own. Ordinary preset use goes to
    /// <see cref="PrintPreparationPlan.For"/> and therefore to <c>FitWithinBounds</c>; a custom
    /// edge goes to <see cref="TargetEdgePrintPreparationPlan.For"/> and therefore to
    /// <c>ScaleToTargetEdge</c>; and neither calculation is ever asked to do the other one's job.
    /// Each has exactly one implementation, wherever a plan comes from (§17).
    /// </para>
    /// <para>
    /// Every refusal below is a "there is nothing to size against" rather than a fallback. Nothing
    /// here reads a filename, a screen value, a paper standard, a guessed size, or the
    /// independently converted <c>PrintDimensions.PixelWidth</c> — each of those would produce a
    /// plan for a different image, or a different shop, wearing this one's binding (§3, §8).
    /// </para>
    /// </remarks>
    private OperationResult<RecordedSize> PlanSize(
        SessionAggregate aggregate, WorkflowSnapshot snapshot, WorkflowCommand command)
    {
        OperationResult<SizingSource> resolved = ResolveSizingSource(aggregate, snapshot);
        if (resolved.IsFailure)
        {
            return OperationResult.Fail<RecordedSize>(resolved.Failure);
        }

        SizingSource source = resolved.Value;

        try
        {
            switch (command)
            {
                case WorkflowCommand.SetPrintDimensions typed:
                    return OperationResult.Ok(new RecordedSize(
                        typed.Dimensions,
                        Selection: null,
                        PrintPreparationPlan.For(
                            source.RevisionId, source.Sha256, source.PixelWidth, source.PixelHeight,
                            typed.Dimensions),
                        TargetEdgePlan: null));

                case WorkflowCommand.SetPresetFitSize preset:
                {
                    OperationResult<PresetPrintRecommendation> recommendation =
                        ResolveRecommendation(preset.Preset);
                    if (recommendation.IsFailure)
                    {
                        return OperationResult.Fail<RecordedSize>(recommendation.Failure);
                    }

                    // The configured recommendation is handed to the Domain whole, and the Domain
                    // decides. PresetPrintRecommendation.Fit is the single sizing authority for
                    // every configured form — a box, a maximum long edge, and from v1.15.0 a
                    // maximum short edge — so no service, screen or adapter has an opinion about
                    // which edge A5 limits or whether it caps the other one. Nothing here reads a
                    // millimetre, an ISO paper size or PrintDimensions.NominalMillimetres
                    // (post-final A5 correction §10, §11).
                    PrintPreparationPlan plan = PrintPreparationPlan.For(
                        source.RevisionId, source.Sha256, source.PixelWidth, source.PixelHeight,
                        recommendation.Value);

                    return OperationResult.Ok(new RecordedSize(
                        PrintDimensions.FromMillimetres(
                            plan.MaxWidthMm, plan.MaxHeightMm, recommendation.Value.Preset),
                        FlexibleSizeSelection.PresetFit(recommendation.Value),
                        plan,
                        TargetEdgePlan: null));
                }

                case WorkflowCommand.SetCustomTargetEdgeSize custom:
                {
                    FlexibleSizeSelection selection;
                    if (custom.OverriddenPreset is { } overridden)
                    {
                        OperationResult<PresetPrintRecommendation> recommendation =
                            ResolveRecommendation(overridden);
                        if (recommendation.IsFailure)
                        {
                            return OperationResult.Fail<RecordedSize>(recommendation.Failure);
                        }

                        // The recommendation is retained beside the override rather than replaced
                        // by it, so "the operator went past A4's 280 mm" stays readable as exactly
                        // that afterwards (§6).
                        selection = FlexibleSizeSelection.OverridePreset(
                            recommendation.Value, custom.Edge, custom.Millimetres);
                    }
                    else
                    {
                        selection = FlexibleSizeSelection.CustomTarget(custom.Edge, custom.Millimetres);
                    }

                    TargetEdgePrintPreparationPlan plan = TargetEdgePrintPreparationPlan.For(
                        source.RevisionId, source.Sha256, source.PixelWidth, source.PixelHeight,
                        selection);

                    // The millimetres the session records describe the plan; they are never a
                    // second target. Which single edge Photoshop is given remains the plan's
                    // answer alone (§6).
                    return OperationResult.Ok(new RecordedSize(
                        plan.AsRecordedDimensions(), selection, BoundsPlan: null, plan));
                }

                default:
                    return OperationResult.Fail<RecordedSize>(
                        FailureCode.PreconditionNotMet,
                        $"'{command.Kind}' is not a sizing command; there is nothing to calculate.");
            }
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
        {
            // A size the calculation will not produce is an ordinary thing for an operator to
            // type, so it is reported rather than thrown out of the command path.
            return OperationResult.Fail<RecordedSize>(
                FailureCode.PreconditionNotMet,
                $"No plan fits that size to {source.PixelWidth}×{source.PixelHeight} px: {ex.Message}");
        }
    }

    /// <summary>
    /// The configured recommendation for one named preset, or why it cannot be trusted
    /// (Epic 11400 Part B1A.2D §3, §4).
    /// </summary>
    /// <remarks>
    /// The only route from a preset name to millimetres in the whole application. A preset the
    /// verified manifest does not configure is refused rather than filled in from
    /// <c>PrintDimensions.NominalMillimetres</c>: the ISO paper size a preset is named after and
    /// the limit this shop prints it at are different numbers, and only one of them is
    /// executable (§4).
    /// </remarks>
    /// <summary>
    /// What the verified preset recommends for named sizes right now, or null when it cannot be
    /// verified (post-final A5 correction §14).
    /// </summary>
    /// <remarks>
    /// Read once per snapshot and handed to it, so "is this pending preset fit still the
    /// recommendation the shop configures" is answered against the same authority
    /// <see cref="ResolveRecommendation"/> records a new decision from. Null when the preset
    /// cannot be verified, which fails a pending preset fit closed rather than letting an
    /// unverifiable installation run one.
    /// </remarks>
    private PresetPrintRecommendationSet? ConfiguredRecommendations() =>
        _presetProvider.GetPrintSizeRecommendations() is { IsSuccess: true } configured
            ? configured.Value
            : null;

    private OperationResult<PresetPrintRecommendation> ResolveRecommendation(SizePreset preset)
    {
        OperationResult<PresetPrintRecommendationSet> configured =
            _presetProvider.GetPrintSizeRecommendations();
        if (configured.IsFailure)
        {
            return OperationResult.Fail<PresetPrintRecommendation>(configured.Failure);
        }

        return configured.Value.For(preset) is { } recommendation
            ? OperationResult.Ok(recommendation)
            : OperationResult.Fail<PresetPrintRecommendation>(
                FailureCode.EnvironmentNotVerified,
                $"The verified workstation preset configures no print recommendation for {preset}, so " +
                "that size cannot be recorded. Nothing substitutes a nominal paper size for a " +
                "configured production limit.");
    }

    /// <summary>The exact artefact a size decision may be bound to.</summary>
    private readonly record struct SizingSource(
        RevisionId RevisionId, Sha256 Sha256, int PixelWidth, int PixelHeight);

    /// <summary>
    /// Resolves the upstream artefact a size will be calculated against, or reports why none is
    /// usable (Epic 11400 Part B1A.2A §8; Part B1A.2D §16).
    /// </summary>
    /// <remarks>
    /// Its inputs are the bytes <c>EnsureIntegrityAsync</c> has just re-verified, and the pixels
    /// are the Revision's own recorded <c>FileFacts</c> — never a second hashing path, and never
    /// a dimension read from anywhere else (§16).
    /// </remarks>
    private static OperationResult<SizingSource> ResolveSizingSource(
        SessionAggregate aggregate, WorkflowSnapshot snapshot)
    {
        if (snapshot.UpstreamResultOf(StepKind.PhotoshopOutput) is not { } upstream)
        {
            return OperationResult.Fail<SizingSource>(
                FailureCode.PreconditionNotMet,
                "Photoshop output has no validated upstream result, so there are no source pixels to size " +
                "against.");
        }

        if (FindRevision(aggregate, upstream.Id) is not { } source)
        {
            return OperationResult.Fail<SizingSource>(
                FailureCode.PreconditionNotMet,
                $"Upstream Revision {upstream.Id} is not loaded on this session; nothing may be sized to it.");
        }

        if (!source.IsValid)
        {
            return OperationResult.Fail<SizingSource>(
                FailureCode.PreconditionNotMet,
                $"Upstream Revision {upstream.Id} has been invalidated; a size recorded against it would " +
                "describe a file the workflow will not consume.");
        }

        // Belt and braces against the step row and the Revision row disagreeing about which bytes
        // are on offer. The binding is only as good as the hash it carries, so a disagreement is
        // refused rather than resolved in either direction.
        if (!source.Sha256.Equals(upstream.Sha256))
        {
            return OperationResult.Fail<SizingSource>(
                FailureCode.RevisionIntegrityMismatch,
                $"Upstream Revision {upstream.Id} and its step entry disagree about the current hash; " +
                "no plan may be bound to an artefact whose identity is unsettled.");
        }

        if (!source.Facts.HasPixelDimensions)
        {
            return OperationResult.Fail<SizingSource>(
                FailureCode.PreconditionNotMet,
                $"Upstream Revision {upstream.Id} has no recorded pixel dimensions, so no plan can be " +
                "calculated. Nothing is guessed from the file name or the requested millimetres.");
        }

        return OperationResult.Ok(new SizingSource(
            upstream.Id, upstream.Sha256, source.Facts.PixelWidth!.Value, source.Facts.PixelHeight!.Value));
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
        TrimBounds? ManualCrop,
        string? ManualResultPath = null, ManualCropMargin ManualCropMargin = default);

    /// <summary>
    /// What one attempt's file work produced, on its way to the closing transaction.
    /// </summary>
    /// <remarks>
    /// A named record rather than the three-element tuple this used to be, because
    /// <see cref="TrimGeometry"/> is the member that made the difference visible: a fourth
    /// anonymous slot on a value threaded through twenty return statements is a slot that gets
    /// filled in the wrong order eventually. Naming it also states the rule that matters —
    /// everything here is <i>measured</i>, and every field is carried from whatever performed
    /// the work rather than recomputed afterwards.
    /// </remarks>
    /// <param name="Output">The validated file this attempt produced.</param>
    /// <param name="Facts">What the inspector read back off that file, never what was requested.</param>
    /// <param name="AdapterNotes">Runtime evidence from an external application, or null.</param>
    /// <param name="TrimGeometry">
    /// The rectangles the deterministic trim established, carried from the processor result that
    /// actually wrote <paramref name="Output"/> (SCRUM-11081). Null for every other kind of work,
    /// including a manual crop. Never re-derived by scanning the cropped file: the alpha bounds
    /// of an already-trimmed image are its own canvas, which would silently turn a margined crop
    /// into a tight one.
    /// </param>
    private sealed record StepWork(
        WorkspaceFileRef Output,
        FileFacts Facts,
        string? AdapterNotes = null,
        TrimGeometry? TrimGeometry = null,
        PsdInspection? PsdInspection = null,
        PdfInspection? PdfInspection = null, ManualCropGeometry? ManualCropGeometry = null);

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
                        run.Operation == OperationKind.PreparePsd ? _photoshop.PsdPreparationAdapterId : AdapterIdFor(run.Adapter), ManualCrop: null);

                case WorkflowEffect.ImportManualResult manual:
                    return new ProducingWork(manual.AttemptId, manual.Step, AdapterKind.Internal,
                        OperationKind.ManualResultImport, manual.InputRevision,
                        "manual-result-import-v1", null, manual.SelectedPath);

                case WorkflowEffect.RunManualCrop crop:
                    // The processor's own identity, asked for rather than hard-coded, so the
                    // attempt row can never claim an implementation that did not run.
                    return new ProducingWork(
                        crop.AttemptId, crop.Step, AdapterKind.Internal, OperationKind.ManualImport,
                        crop.InputRevision, _manualCrop.ProcessorId, crop.Crop, ManualCropMargin: crop.Margin);
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

        WorkflowSnapshot snapshot = loaded.Value.ToSnapshot(ConfiguredRecommendations());
        return ViewOf(snapshot, loaded.Value.Revisions, loaded.Value.Outputs, loaded.Value.Attempts);
    }

    /// <inheritdoc />
    public Task<OperationResult<IReadOnlyList<SessionListItem>>> ListRecentAsync(CancellationToken cancellationToken) =>
        _repository.ListRecentAsync(
            RecentSessionLimit, _timeProvider.GetUtcNow() - RecentSessionWindow, cancellationToken);

    /// <inheritdoc />
    public async Task<OperationResult<Unit>> RemoveFromRecentAsync(
        SessionId id, CancellationToken cancellationToken)
    {
        OperationResult<SessionAggregate?> loaded = await _repository.LoadAsync(id, cancellationToken);
        if (loaded.IsFailure)
        {
            return OperationResult.Fail<Unit>(loaded.Failure);
        }

        if (loaded.Value is not { } aggregate)
        {
            return OperationResult.Fail<Unit>(FailureCode.PreconditionNotMet, $"No session {id} exists.");
        }

        if (!SessionStateRules.AllowsRecordRemoval(aggregate.Session.State))
        {
            return OperationResult.Fail<Unit>(
                FailureCode.PreconditionNotMet,
                $"Session {id} is {aggregate.Session.State}; only a finished job's record may be taken off " +
                "Recent Processing.");
        }

        // Belt as well as the repository's braces, and for a reason no state name carries: a row
        // recorded as Running is an operation this process believes is in flight, and a card is
        // not something to take away while work is still happening behind it. The same pair of
        // facts already gates completion retention, which is the other operation allowed to touch
        // a finished session (SqliteSessionRepository's IsRetentionMaintenance check).
        //
        // Interrupted is deliberately *not* a bar. An interrupted attempt on a Completed or
        // Abandoned session is resolved history: startup recovery draws its candidates only from
        // Active and HandedOff sessions, so a crashed job the operator has since abandoned is no
        // longer recoverable and refusing to remove its card would strand it on Home forever
        // (SCRUM-11112; Jira 11602).
        if (aggregate.Attempts.Any(attempt => attempt.Status is AttemptStatus.Running))
        {
            return OperationResult.Fail<Unit>(
                FailureCode.PreconditionNotMet,
                $"Session {id} still has an attempt in flight; its record stays on Recent Processing.");
        }

        return await _repository.RemoveFromRecentAsync(id, _timeProvider.GetUtcNow(), cancellationToken);
    }

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
        IReadOnlyList<ProcessingAttempt> attempts)
    {
        state = state with { RequiresPsdPreparation = revisions.Any(r => r.IsRoot && r.Facts.Format == ImageFormat.Psd),
            RequiresPdfPreparation = revisions.Any(r => r.IsRoot && r.Facts.Format == ImageFormat.Pdf) };
        Guid? enlargementOfferId = null;
        if (state.UsableTargetEdgePlan is { RequiresEnlargementAuthority: true } offered &&
            state.NeedsEnlargementAuthority)
        {
            enlargementOfferId = Guid.NewGuid();
            lock (_enlargementOfferGate)
            {
                _enlargementOffers[state.SessionId] = new EnlargementOffer(
                    enlargementOfferId.Value,
                    new WorkflowCommand.AuthoriseEnlargement(
                        offered.SourceRevisionId,
                        offered.SourceSha256,
                        offered.Projection.SelectedTargetEdge,
                        offered.Projection.RequestedMillimetres));
            }
        }
        else
        {
            // Any view which no longer presents a confirmation also withdraws its handle. This
            // covers an accepted authority, a changed/cleared plan and a return upstream.
            lock (_enlargementOfferGate)
            {
                _enlargementOffers.Remove(state.SessionId);
            }
        }

        SessionView view = SessionView.From(
            state, _engine.AvailableCommands(state), revisions, outputs, attempts, ProcessingMode,
            _engine.AvailableReturnTargets(state),

            // The configured recommendations, resolved here and offered to the screen, so the
            // named sizes a UI can present are exactly the ones the verified preset configures.
            // A provider that cannot verify the preset offers none, which is what stops an
            // unverified installation from showing an A4 button with a paper standard behind it
            // (Epic 11400 Part B1A.2D §3, §28).
            _presetProvider.GetPrintSizeRecommendations() is { IsSuccess: true } configured
                ? configured.Value.All
                : [],
            enlargementOfferId);

        RecordedSize? currentSize = CurrentRecordedSize(state);
        if (currentSize is not null)
        {
            view = view with
            {
                Preflight = PreflightFrom(
                    currentSize,
                    revisions,
                    attempts,
                    state,
                    state.HasUsableEnlargementAuthority,
                    enlargementOfferId),
            };
        }

        return OperationResult.Ok(view);
    }

    private static RecordedSize? CurrentRecordedSize(WorkflowSnapshot snapshot)
    {
        if (snapshot.UsablePrintPreparationPlan is { } bounds)
        {
            return new RecordedSize(
                snapshot.Dimensions!.Value, snapshot.SizeSelection, bounds, TargetEdgePlan: null);
        }

        if (snapshot.UsableTargetEdgePlan is { } target)
        {
            return new RecordedSize(
                target.AsRecordedDimensions(), snapshot.SizeSelection, BoundsPlan: null, target);
        }

        return null;
    }

    private static PrintDimensionsPreflight PreflightFrom(
        RecordedSize size,
        IReadOnlyList<Revision> revisions,
        IReadOnlyList<ProcessingAttempt> attempts,
        WorkflowSnapshot snapshot,
        bool enlargementAuthorised,
        Guid? enlargementOfferId)
    {
        const double MillimetresPerInch = 25.4;

        RevisionId sourceRevisionId;
        int sourcePixelWidth;
        int sourcePixelHeight;
        int outputPixelWidth;
        int outputPixelHeight;
        int productionOutputPpi;
        bool requiresEnlargement;

        if (size.BoundsPlan is { } bounds)
        {
            sourceRevisionId = bounds.SourceRevisionId;
            sourcePixelWidth = bounds.SourcePixelWidth;
            sourcePixelHeight = bounds.SourcePixelHeight;
            outputPixelWidth = bounds.ProjectedPixelWidth;
            outputPixelHeight = bounds.ProjectedPixelHeight;
            productionOutputPpi = bounds.ProductionDpi;
            requiresEnlargement = false;
        }
        else
        {
            TargetEdgePrintPreparationPlan target = size.TargetEdgePlan!;
            sourceRevisionId = target.SourceRevisionId;
            sourcePixelWidth = target.SourcePixelWidth;
            sourcePixelHeight = target.SourcePixelHeight;
            outputPixelWidth = target.Projection.ProjectedPixelWidth;
            outputPixelHeight = target.Projection.ProjectedPixelHeight;
            productionOutputPpi = target.ProductionDpi;
            requiresEnlargement = target.RequiresEnlargementAuthority;
        }

        double widthMm = outputPixelWidth * MillimetresPerInch / productionOutputPpi;
        double heightMm = outputPixelHeight * MillimetresPerInch / productionOutputPpi;
        (GraphicBoundsKind kind, TrimBounds? artwork, TrimBounds? canvas) =
            GraphicBoundsFor(sourceRevisionId, revisions, attempts, snapshot);

        return new PrintDimensionsPreflight(
            sourceRevisionId,
            sourcePixelWidth,
            sourcePixelHeight,
            kind,
            artwork,
            canvas,
            widthMm,
            heightMm,
            outputPixelWidth,
            outputPixelHeight,
            productionOutputPpi,
            TiffEffectiveResolution.EffectiveDpi(sourcePixelWidth, widthMm),
            TiffEffectiveResolution.EffectiveDpi(sourcePixelHeight, heightMm),
            requiresEnlargement,
            enlargementAuthorised)
        {
            EnlargementOfferId = enlargementOfferId,
        };
    }

    private static (GraphicBoundsKind Kind, TrimBounds? Artwork, TrimBounds? Canvas) GraphicBoundsFor(
        RevisionId sourceRevisionId,
        IReadOnlyList<Revision> revisions,
        IReadOnlyList<ProcessingAttempt> attempts,
        WorkflowSnapshot snapshot)
    {
        ProcessingAttempt? producingAttempt =
            attempts.FirstOrDefault(attempt => attempt.OutputRevisionId == sourceRevisionId);

        if (producingAttempt?.TrimGeometry is { } automatic)
        {
            return (GraphicBoundsKind.AutomaticTrim, automatic.ContentBounds, automatic.AppliedBounds);
        }

        if (producingAttempt?.ManualCropGeometry is { } manual)
        {
            return (GraphicBoundsKind.ManualCrop, manual.SelectedBounds, manual.AppliedBounds);
        }

        if (snapshot.Step(StepKind.Trim)?.State == StepState.Skipped)
        {
            return (GraphicBoundsKind.FullOriginalCanvas, null, null);
        }

        Revision? source = revisions.FirstOrDefault(revision => revision.Id == sourceRevisionId);
        if (producingAttempt?.Step == StepKind.Trim &&
            source?.Operation is OperationKind.Trim or OperationKind.ManualImport)
        {
            return (GraphicBoundsKind.GeometryUnavailable, null, null);
        }

        return (GraphicBoundsKind.NoRelevantGeometry, null, null);
    }

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
            // Retaining the approved original is a decision about those exact bytes.
            WorkflowCommand.KeepOriginalExtent => FindRevision(aggregate, snapshot.UpstreamRevisionOf(StepKind.Trim)),
            WorkflowCommand.SubmitManualResult manual => FindRevision(aggregate, snapshot.UpstreamRevisionOf(manual.Step)),

            // Authorising reviewed content is a decision about specific bytes, exactly as an
            // Approve is, so it is checked exactly as an Approve is (Epic 11300 Part C2B2 §20).
            // Without this the operator could authorise automatic selection over a file that had
            // already changed underneath the screen: the engine compares the displayed hash with
            // the Revision's *recorded* hash, and a file mutated in place still matches its own
            // record. The mutation would surface at StartStep instead — after an authority had
            // been written for content nobody reviewed.
            //
            // It is the same guard, resolving the same Revision StartStep would: no second
            // hashing path exists, and none is added here.
            WorkflowCommand.SetBackgroundRemovalDecision =>
                FindRevision(aggregate, snapshot.UpstreamRevisionOf(StepKind.BackgroundRemoval)),

            // Recording maximum bounds is a decision about specific pixels, so it is checked
            // exactly as an Approve is (Epic 11400 Part B1A.2A §14). The plan's limiting edge is
            // calculated from the source's own dimensions and bound to its hash; without this the
            // operator could record bounds against a file that had already changed underneath,
            // and the mutation would only surface at StartStep — after a plan had been written
            // for content nobody fitted anything to.
            //
            // The same Revision StartStep would resolve, through the same guard. No second
            // hashing path exists, and none is added here.
            //
            // All four sizing and enlargement decisions take the same route, because all four are
            // decisions about specific pixels. A target edge binds a projected pixel pair to a
            // hash exactly as a fit box binds a limiting edge to one, and an enlargement
            // confirmation is a judgement about content the operator was shown — so none of them
            // may be recorded against bytes that have already moved (Part B1A.2D §16).
            WorkflowCommand.SetPrintDimensions
                or WorkflowCommand.SetPresetFitSize
                or WorkflowCommand.SetCustomTargetEdgeSize
                or WorkflowCommand.AuthoriseEnlargement =>
                FindRevision(aggregate, snapshot.UpstreamRevisionOf(StepKind.PhotoshopOutput)),

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
    // Final production-TIFF review: promotion and disposal (Epic 11400 Part C2B)
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Carries out the file lifecycle a final review decision implies, before the transaction
    /// that records the decision (Part C2B §6, §10, §12, §15).
    /// </summary>
    /// <remarks>
    /// Returns the updated <see cref="PrintOutput"/> for the caller to commit, or <c>null</c> when
    /// the command is not a decision about a production output and there is no file work to do.
    /// A failure means nothing was recorded: the step is still <c>ReviewRequired</c>, no approval
    /// and no rejection exists, and the operator can decide again once the cause is cleared.
    /// <para>
    /// Deliberately not adapter-aware. It reaches the output through the step's current Revision
    /// and the twin <see cref="PrintOutputId"/>, so a TIFF produced by the Fake adapter takes the
    /// same route as one produced by Photoshop (§36). Nothing here asks which adapter ran.
    /// </para>
    /// <para>
    /// The exact-hash authority is <b>not</b> re-implemented here. Every command that reaches this
    /// point has already been through <see cref="EnsureIntegrityAsync"/>, which re-read the bytes
    /// on disk and refused the command unless they still hashed to the Revision's recorded hash —
    /// the same guard an Approve of any other artefact goes through (§5). What this method adds is
    /// the independent re-hash of the <i>promoted</i> bytes, which is a different question.
    /// </para>
    /// </remarks>
    private async Task<OperationResult<PrintOutput?>> PerformFinalReviewFileWorkAsync(
        SessionAggregate aggregate, WorkflowCommand command, CommandContext context,
        CancellationToken cancellationToken)
    {
        if (command is not (WorkflowCommand.Approve or WorkflowCommand.Reject))
        {
            return OperationResult.Ok<PrintOutput?>(null);
        }

        (StepKind step, bool isApproval) = command switch
        {
            WorkflowCommand.Approve approve => (approve.Step, true),
            WorkflowCommand.Reject reject => (reject.Step, false),
            _ => throw new InvalidOperationException("Unreachable: the command was checked above."),
        };

        RevisionId? current = aggregate.Steps.FirstOrDefault(s => s.Step == step)?.CurrentRevisionId;
        if (current is not RevisionId reviewed ||
            FindRevision(aggregate, reviewed) is not { } revision ||
            aggregate.Outputs.FirstOrDefault(o => o.Id.Value == reviewed.Value) is not { } output)
        {
            // A reviewed Revision that has no twin PrintOutput is an ordinary intermediate
            // artefact — an enhanced PNG, a cut-out, a trim. Those have never had a promotion or
            // a disposal on approval and do not gain one here (§19).
            return OperationResult.Ok<PrintOutput?>(null);
        }

        return isApproval
            ? await PromoteApprovedOutputAsync(aggregate, output, revision, context, cancellationToken)
            : RecycleRejectedOutput(output, revision, context);
    }

    /// <summary>
    /// Copies the exact reviewed TIFF into <c>Approved\</c> and confirms it arrived intact
    /// (Part C2B §6, §7, §8, §9, §10).
    /// </summary>
    /// <remarks>
    /// <b>The chosen lifecycle is reserve-copy-verify, not move.</b> It is the promotion primitive
    /// the workspace already has and the one <c>ApprovedPngExport</c> already promotes through:
    /// <see cref="IWorkspace.ReserveOutput"/> claims a name with <c>FileMode.CreateNew</c>, so the
    /// established <c>{Name}</c>, <c>{Name}_02</c>, … collision contract is honoured atomically and
    /// an existing approved file is never overwritten, and
    /// <see cref="IWorkspace.WriteReservedAsync"/> copies the bytes unchanged. Nothing reopens
    /// Photoshop, resaves, recompresses or regenerates anything: approval is a lifecycle operation
    /// over bytes that were already validated (§8).
    /// <para>
    /// The Working original is deliberately <i>retained</i> rather than deleted. It is the file the
    /// producing Revision names, and that Revision is immutable and still has to re-hash against
    /// real bytes; removing it would break the integrity guard for the sake of tidiness. Clearing
    /// <c>Working\</c> is a session-completion concern, and the state of that concern is recorded
    /// in the report rather than quietly changed here (§20).
    /// </para>
    /// <para>
    /// <b>Ordering.</b> The reservation is persisted in its own transaction <i>before</i> any bytes
    /// are copied. A process that dies after the copy but before the review commit therefore leaves
    /// behind the destination it had already claimed, and the operator's second approval resumes
    /// into that same file instead of reserving a second name. That is the whole reason the
    /// reservation is persisted at all, and it is what makes "no duplicate Approved copy" true
    /// across a crash rather than merely likely (§10, §11, §33).
    /// </para>
    /// </remarks>
    private async Task<OperationResult<PrintOutput?>> PromoteApprovedOutputAsync(
        SessionAggregate aggregate, PrintOutput output, Revision revision, CommandContext context,
        CancellationToken cancellationToken)
    {
        if (output.File.Area == WorkspaceArea.Approved && output.PromotionReservation is null)
        {
            // Already promoted, and this approval is a replay of one that completed its file work.
            // Nothing is copied a second time and no name is reserved (§11).
            return OperationResult.Ok<PrintOutput?>(null);
        }

        OperationResult<WorkspaceFileRef> destination =
            await ReserveApprovedDestinationAsync(aggregate, output, revision, context, cancellationToken);
        if (destination.IsFailure)
        {
            return OperationResult.Fail<PrintOutput?>(destination.Failure);
        }

        WorkspaceFileRef approved = destination.Value;

        OperationResult<Unit> copied = await _workspace.WriteReservedAsync(approved, revision.File, cancellationToken);
        if (copied.IsFailure)
        {
            // The reservation stays recorded on purpose: the destination is claimed, the operator
            // can approve again once the cause is cleared, and the retry writes into the same
            // file rather than claiming a second name.
            return OperationResult.Fail<PrintOutput?>(copied.Failure);
        }

        // The promoted bytes are re-read and re-hashed independently of the copy that wrote them.
        // "WriteReservedAsync returned success" is the copy's own report; this is the only moment
        // at which the file that is about to be called the approved deliverable can still be
        // compared with the file that was validated (§6, §8).
        OperationResult<StepWork> promoted = await InspectAsync(approved, cancellationToken);
        if (promoted.IsFailure)
        {
            return await AbandonPromotionAsync(
                aggregate, output, approved, context, promoted.Failure, cancellationToken);
        }

        FileFacts facts = promoted.Value.Facts;
        if (!facts.Sha256.Equals(revision.Sha256) || facts.ByteLength != revision.Facts.ByteLength)
        {
            return await AbandonPromotionAsync(
                aggregate,
                output,
                approved,
                context,
                OperationFailure.Create(
                    FailureCode.OutputValidationFailed,
                    $"The promoted TIFF at '{approved.RelativePath}' does not match the reviewed file: " +
                    $"recorded {revision.Sha256.ShortForm}/{revision.Facts.ByteLength} bytes, " +
                    $"found {facts.Sha256.ShortForm}/{facts.ByteLength} bytes. It was not approved.",
                    isRetryable: true,
                    context: new Dictionary<string, string>
                    {
                        ["promoted"] = "false",
                        ["reviewRecorded"] = "false",
                    }),
                cancellationToken);
        }

        return OperationResult.Ok<PrintOutput?>(output.Promoted(approved));
    }

    /// <summary>
    /// Claims the <c>Approved</c> destination for this output, resuming an interrupted promotion
    /// rather than starting a second one (Part C2B §7, §11, §33).
    /// </summary>
    /// <remarks>
    /// The proposed name is the one the workflow's own naming authority already rendered for this
    /// TIFF — <c>revision.File.FileName</c> — so approval names nothing and Infrastructure names
    /// nothing. Only the collision suffix is decided here, and it is decided by the workspace
    /// against what is actually on disk.
    /// </remarks>
    private async Task<OperationResult<WorkspaceFileRef>> ReserveApprovedDestinationAsync(
        SessionAggregate aggregate, PrintOutput output, Revision revision, CommandContext context,
        CancellationToken cancellationToken)
    {
        if (output.PromotionReservation is { } resumed)
        {
            return OperationResult.Ok(resumed);
        }

        OperationResult<NamingPatternSet> patterns = _presetProvider.GetNamingPatterns();
        if (patterns.IsFailure)
        {
            return OperationResult.Fail<WorkspaceFileRef>(patterns.Failure);
        }

        OperationResult<WorkspaceFileRef> reserved = _workspace.ReserveOutput(
            aggregate.Session.Workspace, WorkspaceArea.Approved, revision.File.FileName, patterns.Value);
        if (reserved.IsFailure)
        {
            return reserved;
        }

        SessionMutation claim = new(
            aggregate.Session with { UpdatedAtUtc = context.NowUtc },
            aggregate.Steps, [], [], [], [], [output.ReservingPromotion(reserved.Value)], null, null);

        OperationResult<Unit> committed = await _repository.CommitAsync(claim, cancellationToken);
        if (committed.IsSuccess)
        {
            return reserved;
        }

        // The name was claimed on disk but the claim never reached the database, so nothing will
        // ever resume into it. It is quarantined out of Approved\ — the workspace's existing answer
        // to a file with no metadata behind it — rather than left as an empty file the next
        // approval would collide with and number around (§32).
        //
        // A hard process death in this same window leaves the empty reservation behind, because no
        // code runs to clear it. What it cannot leave is a second copy of the approved TIFF: the
        // residue is a zero-byte name, the bytes are copied only after this commit lands, and no
        // record points at it.
        _workspace.Quarantine(
            _workspace.ResolveAbsolute(reserved.Value),
            $"Final approval of {output.Id} reserved this name but could not record the reservation.");

        return OperationResult.Fail<WorkspaceFileRef>(committed.Failure);
    }

    /// <summary>
    /// Gives up a promotion whose destination could not be established, leaving nothing in
    /// <c>Approved\</c> that could be mistaken for the deliverable (Part C2B §32).
    /// </summary>
    /// <remarks>
    /// The half-written file is quarantined rather than deleted — the workspace's existing answer
    /// to "a file exists on disk with no metadata behind it" — and the reservation is released so
    /// the operator's next approval claims a fresh name instead of writing into a destination that
    /// has already failed once. The review is not recorded either way.
    /// </remarks>
    private async Task<OperationResult<PrintOutput?>> AbandonPromotionAsync(
        SessionAggregate aggregate, PrintOutput output, WorkspaceFileRef approved, CommandContext context,
        OperationFailure failure, CancellationToken cancellationToken)
    {
        _workspace.Quarantine(
            _workspace.ResolveAbsolute(approved),
            $"Final approval of {output.Id} could not establish the promoted TIFF: {failure.TechnicalDetail}");

        SessionMutation release = new(
            aggregate.Session with { UpdatedAtUtc = context.NowUtc },
            aggregate.Steps, [], [], [], [], [output.WithoutPromotionReservation()], null, null);
        await _repository.CommitAsync(release, cancellationToken);

        return OperationResult.Fail<PrintOutput?>(failure);
    }

    /// <summary>
    /// Sends the exact rejected TIFF to the Windows Recycle Bin (Part C2B §12, §14, §15).
    /// </summary>
    /// <remarks>
    /// <b>Ordering: disposal first, then the transaction that records the rejection.</b> A recycle
    /// that fails therefore records no rejection at all — the step is still <c>ReviewRequired</c>,
    /// the TIFF is still where it was, and the operator can reject again once the cause is cleared.
    /// The alternative ordering would let the database say a TIFF was disposed of while it sat on
    /// disk, which is the one claim §14 forbids: a failed disposal must not be dressed up as a
    /// completed rejection.
    /// <para>
    /// A crash in the window between the two leaves the opposite, and it is deterministic: no
    /// review decision exists, the step is still <c>ReviewRequired</c>, and the file is in the
    /// Windows Recycle Bin where the operator can restore it. The next decision on that step is
    /// refused by <see cref="RevisionIntegrityGuard"/> with
    /// <see cref="FailureCode.RevisionIntegrityMismatch"/> — the artefact cannot be re-read — and
    /// the Revision is invalidated, so no approval of a disposed TIFF is reachable (§34).
    /// </para>
    /// <para>
    /// There is no hard-delete path here, and none anywhere behind it: <see cref="IRecycleBin"/>
    /// has no fallback, so an unrecyclable file is retained rather than erased (§14).
    /// </para>
    /// </remarks>
    private OperationResult<PrintOutput?> RecycleRejectedOutput(
        PrintOutput output, Revision revision, CommandContext context)
    {
        if (output.RecycledAtUtc is not null)
        {
            // A replayed rejection disposes of nothing a second time.
            return OperationResult.Ok<PrintOutput?>(null);
        }

        OperationResult<Unit> recycled = _recycleBin.SendToRecycleBin(_workspace.ResolveAbsolute(revision.File));
        if (recycled.IsFailure)
        {
            return OperationResult.Fail<PrintOutput?>(OperationFailure.Create(
                recycled.Failure.Code,
                "The generated TIFF could not be sent to the Recycle Bin, so the rejection was not " +
                $"recorded and the file was left where it is: {recycled.Failure.TechnicalDetail}",
                isRetryable: true,
                context: new Dictionary<string, string>
                {
                    ["recycled"] = "false",
                    ["reviewRecorded"] = "false",
                    ["hardDeleted"] = "false",
                }));
        }

        return OperationResult.Ok<PrintOutput?>(output.Recycled(context.NowUtc));
    }

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

        bool drivesExternalApplication = definition.IsAdapterBacked && work.ManualResultPath is null;

        if (!drivesExternalApplication)
        {
            return await RunProducingStepWithinLeaseAsync(
                aggregate, started, context, work, definition, drivesExternalApplication,
                cancellationToken).ConfigureAwait(false);
        }

        AdapterExecutionMode adapterMode = AdapterModeFor(work.Adapter);
        bool drivesWorkstationAutomation =
            work.Adapter is AdapterKind.Meitu or AdapterKind.Photoshop;
        if (adapterMode != AdapterExecutionMode.Production || !drivesWorkstationAutomation)
        {
            OperationResult<Unit> gate = work.Adapter == AdapterKind.Pdf &&
                                         _environmentGate is IInternalProductionEnvironmentGate internalGate
                ? internalGate.VerifyForInternalWork(adapterMode)
                : _environmentGate.Verify(adapterMode);
            return gate.IsFailure
                ? OperationResult.Fail<SessionView>(gate.Failure)
                : await RunProducingStepWithinLeaseAsync(
                    aggregate, started, context, work, definition, drivesExternalApplication,
                    cancellationToken).ConfigureAwait(false);
        }

        if (_automationLeases is null)
        {
            return OperationResult.Fail<SessionView>(
                FailureCode.AdapterUnavailable,
                "No workstation automation lease authority was composed for this external operation.");
        }

        OperationResult<IWorkstationAutomationLease> acquired = await _automationLeases
            .TryAcquireAsync(_enclosingAutomationLease, cancellationToken)
            .ConfigureAwait(false);
        if (acquired.IsFailure)
        {
            return OperationResult.Fail<SessionView>(acquired.Failure);
        }

        IWorkstationAutomationLease lease = acquired.Value;
        OperationResult<SessionView> result;
        try
        {
            OperationResult<Unit> gate = _environmentGate is IWorkstationScopedEnvironmentGate scoped
                ? scoped.Verify(adapterMode, lease)
                : _environmentGate.Verify(adapterMode);
            result = gate.IsFailure
                ? OperationResult.Fail<SessionView>(gate.Failure)
                : await RunProducingStepWithinLeaseAsync(
                    aggregate, started, context, work, definition, drivesExternalApplication,
                    cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            OperationResult<Unit> exceptionalRelease = await lease
                .ReleaseAsync(CancellationToken.None)
                .ConfigureAwait(false);
            if (exceptionalRelease.IsFailure)
            {
                throw new InvalidOperationException(
                    $"The operation faulted and its workstation lease could not be released: " +
                    exceptionalRelease.Failure.TechnicalDetail);
            }

            throw;
        }

        OperationResult<Unit> released = await lease
            .ReleaseAsync(CancellationToken.None)
            .ConfigureAwait(false);
        return released.IsFailure
            ? OperationResult.Fail<SessionView>(released.Failure)
            : result;
    }

    private async Task<OperationResult<SessionView>> RunProducingStepWithinLeaseAsync(
        SessionAggregate aggregate,
        WorkflowTransition started,
        CommandContext context,
        ProducingWork work,
        StepDefinition definition,
        bool drivesExternalApplication,
        CancellationToken cancellationToken)
    {
        AutomationLockChange? acquire = null;
        if (drivesExternalApplication)
        {
            OperationResult<AutomationLockState> lockState = await _repository
                .GetAutomationLockAsync(cancellationToken)
                .ConfigureAwait(false);
            if (lockState.IsFailure)
            {
                return OperationResult.Fail<SessionView>(lockState.Failure);
            }

            if (lockState.Value.IsHeld && lockState.Value.SessionId != aggregate.Session.Id)
            {
                string holder = lockState.Value.SessionId is { } sessionId
                    ? $"session {sessionId}"
                    : "the Production Readiness live verification";
                return OperationResult.Fail<SessionView>(
                    FailureCode.AdapterUnavailable,
                    $"Meitu/Photoshop is already correlated with {holder} in this business database.");
            }

            acquire = new AutomationLockChange(
                AutomationLockAction.Acquire, aggregate.Session.Id, context.NowUtc, _processId, _machineName);
        }

        int retrySequence = started.Effects
            .OfType<WorkflowEffect.RecordAttemptStarted>()
            .FirstOrDefault()?.RetrySequence ?? 0;

        AttemptId? retryOf = retrySequence == 0
            ? null
            : aggregate.Attempts
                .Where(attempt => attempt.Step == work.Step)
                .OrderByDescending(attempt => attempt.RetrySequence)
                .ThenByDescending(attempt => attempt.StartedAtUtc)
                .Select(attempt => (AttemptId?)attempt.Id)
                .FirstOrDefault();

        ProcessingAttempt runningAttempt = ProcessingAttempt.Start(
            context.NewAttemptId, aggregate.Session.Id, work.Step, work.InputRevision,
            work.Operation, work.ProcessorId, context.NowUtc,
            retryOfAttemptId: retryOf, retrySequence: retrySequence);

        if (work.ManualResultPath is { } selectedEvidence)
            runningAttempt = runningAttempt.WithManualResultSourcePath(selectedEvidence) with
            {
                AdapterNotes = $"Manual result selected as {selectedEvidence.Replace('\\', '/').Split('/')[^1]}; operator {context.Operator}",
            };

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

        // The reviewed-content authority, written with the same opening transaction and for the
        // same reason: the row must say what this attempt was authorised to do, not what the
        // session was later allowed to do. A second attempt over different reviewed content gets
        // its own row; this one is never rewritten (Epic 11300 Part C2B1 §11, §18).
        //
        // Reading the *usable* authority rather than the raw one is not a second guard — the
        // engine already refused to start the step without one — it is the same predicate, so
        // what is snapshotted is exactly what the engine validated and never a stale record that
        // happened to still be sitting on the session (§8).
        if (work.Step == StepKind.BackgroundRemoval &&
            started.State.UsableBackgroundRemovalAuthority is { } authority)
        {
            runningAttempt = runningAttempt.WithBackgroundRemovalAuthority(authority);
        }

        // The resolved geometry, written with the same opening transaction and for the same
        // reason: the row must say which size and which source produced this output, not what the
        // session was later allowed to do. A second attempt against a different target or
        // different content gets its own row; this one is never rewritten, because the attempt
        // upsert leaves these columns out of its DO UPDATE clause (Epic 11400 Part B1A.2A §12;
        // Part B1A.2D §24).
        //
        // UsablePhotoshopPreparation rather than any raw field — not a second guard, the same
        // predicate the engine just applied, so what is snapshotted is exactly what was validated
        // and never a stale plan that happened to still be sitting on the session (§13). For an
        // enlargement it carries the exact authority the run went ahead under, so a later change
        // of mind cannot relabel this attempt as unauthorised, or an unauthorised one as
        // permitted (§24).
        if (work.Step == StepKind.PhotoshopOutput &&
            started.State.UsablePhotoshopPreparation is { } preparation)
        {
            runningAttempt = runningAttempt.WithPreparation(preparation);
        }

        if (work.ManualResultPath is not null && work.Step == StepKind.BackgroundRemoval &&
            FindRevision(aggregate, work.InputRevision) is { } manualInput)
        {
            runningAttempt = runningAttempt.WithBackgroundRemovalAuthority(BackgroundRemovalAuthority.For(
                BackgroundRemovalDecision.ManualResultForReviewedContent, manualInput.Id, manualInput.Sha256));
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

        // Registered only now, after the attempt row exists. A Stop that arrived before the
        // opening transaction would have nothing to close and no row to audit against, so the
        // window in which Stop is offered is exactly the window in which it is meaningful
        // (Part D2A §24, §28).
        IAutomationStopSignal stop = _runs.Begin(
            aggregate.Session.Id, runningAttempt.Id, work.Step, drivesExternalApplication);

        OperationResult<StepWork> produced;
        WorkspaceFileRef? establishedExpectedOutput = null;
        try
        {
            produced = await PerformStepWorkAsync(
                afterStart, started.State, definition, work, runningAttempt, context, stop,
                output => establishedExpectedOutput = output,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            produced = OperationResult.Fail<StepWork>(
                OperationFailure.Create(
                    FailureCode.Cancelled,
                    "PrintFlow orchestration was cancelled after the attempt started. No further " +
                    "adapter input was requested; the external application may still be running.",
                    isRetryable: true,
                    context: new Dictionary<string, string>
                    {
                        ["attemptId"] = runningAttempt.Id.ToString(),
                        ["step"] = work.Step.ToString(),
                        ["retainedExternalState"] = drivesExternalApplication ? "unknown" : "none",
                    }));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Everything else a step's work can throw (Epic 11600 Part A §9, §10). Without this
            // the exception unwinds past the closing transaction, and the two things it leaves
            // behind are exactly the two §9 forbids: an attempt frozen at Running with its step
            // frozen at Processing, and the automation lock still held — by a process that is
            // still alive, so startup recovery's liveness check would refuse to release it even
            // if the operator restarted. Every later adapter-backed step in every session is
            // then blocked by a run that ended long ago.
            //
            // Containment rather than a rethrow, following the containment ImportAsync already
            // gives the one other producing path: an escape from here is an escape out of the
            // view model, which is a terminated shell rather than a reported failure. The
            // exception is not swallowed — its type and message go into the attempt's failure
            // record, which is immutable history.
            //
            // The filter deliberately also takes an OperationCanceledException raised while the
            // caller's token is *not* cancelled. That is a step throwing cancellation nobody
            // asked for, which is a fault like any other and must not be reported to the
            // operator as though they had stopped the run.
            produced = OperationResult.Fail<StepWork>(
                OperationFailure.Create(
                    FailureCode.AdapterUnavailable,
                    $"The {work.Step} operation ended with an unhandled {ex.GetType().Name}: {ex.Message} " +
                    "No output was validated and no Revision was created. " +
                    (drivesExternalApplication
                        ? "The external application was left untouched; what it retains is unknown."
                        : "No external application was involved."),
                    isRetryable: true,
                    context: new Dictionary<string, string>
                    {
                        ["attemptId"] = runningAttempt.Id.ToString(),
                        ["step"] = work.Step.ToString(),
                        ["faultType"] = ex.GetType().FullName ?? ex.GetType().Name,
                        ["revisionCreated"] = "false",
                        ["retainedExternalState"] = drivesExternalApplication ? "unknown" : "none",
                    },
                    messageKey: "Failure_OperationFaulted"));
        }
        finally
        {
            // Unregistered whatever happened, so a later Stop against a finished run is refused
            // rather than setting a flag nothing will ever read.
            _runs.End(runningAttempt.Id);
        }

        // Once a destination has been constructed it is historical evidence even if the
        // adapter throws, validation fails, or the operator stops the run. Attach it in one
        // place after containment so no post-destination exit can accidentally report that a
        // path was never established.
        if (produced.IsFailure && establishedExpectedOutput is { } expectedOutput)
        {
            produced = OperationResult.Fail<StepWork>(WithExpectedOutput(produced.Failure, expectedOutput));
        }

        // The instant the work actually ended, observed once here and used for whichever closing
        // transaction follows (SCRUM-11137 prerequisite §3, §6, §7). The opening context was
        // stamped before the adapter was called — minutes ago, for a real Photoshop run — so
        // closing on it recorded every attempt as having ended at the moment it began. Observed
        // before the branch rather than inside each of the three closing paths, so a success, a
        // stop and a failure are all timed by the same rule, and the attempt, its step and its
        // session agree on when the attempt ended.
        CommandContext closing = context.At(_timeProvider.GetUtcNow());

        // §15 and §16, in the order the code has to take them. A Stop that arrives while the
        // work is already finishing does not get to undo it: an adapter that returned a
        // validated output has produced a real file, and the success transaction below runs to
        // completion. The requested stop is honoured afterwards, by ending the session's
        // automated progression — never by rewriting what the attempt did.
        if (produced.IsSuccess)
        {
            return await CompleteProducingStepAsync(
                aggregate, afterStart, started, closing, work, runningAttempt, produced.Value, stop,
                cancellationToken);
        }

        if (stop.RequestedMode is { } stopped)
        {
            // The operator asked for this, so it is recorded as a stop rather than as a
            // failure — including when the run ended on some other adapter failure while
            // stopping. Both facts survive: the mode and retained external state go into the
            // structured context, and whatever the adapter reported goes into the detail (§29).
            return await StopAttemptAsync(
                afterStart, started.State, closing, work.Step, runningAttempt, definition,
                stopped, stop, produced.Failure);
        }

        // Cancellation is the one failure whose caller token cannot be used to close the
        // attempt: it is already cancelled. The adapter has stopped receiving input, while
        // this short metadata transaction truthfully ends the attempt and releases the
        // per-database automation correlation row. A process crash before this commit is still covered by
        // startup Running -> Interrupted recovery.
        //
        // A contained fault is closed the same way and for the same reason (Epic 11600 Part A
        // §9). Nothing is known about why the step threw, including whether it threw on its way
        // out of a cancellation the caller had already requested, and a closing transaction
        // that gave up on the caller's token would leave behind the held lock this containment
        // exists to prevent.
        CancellationToken closingToken =
            produced.Failure.Code == FailureCode.Cancelled ||
            produced.Failure.Context.ContainsKey("faultType")
                ? CancellationToken.None
                : cancellationToken;
        return await FailAttemptAsync(
            afterStart, started.State, closing, work.Step, runningAttempt, produced.Failure, closingToken);
    }

    /// <summary>
    /// Commits the success transaction for a producing step, then honours a stop that arrived
    /// while it was finishing (Epic 11300 Part D2A §15, §16).
    /// </summary>
    /// <remarks>
    /// Extracted from <see cref="RunProducingStepAsync"/> so the success path reads in one
    /// piece and so §16's boundary sits at a visible seam: everything in this method happens
    /// <i>after</i> the point where a Stop can still prevent an output, and nothing in it
    /// consults <paramref name="stop"/> until the Revision has been committed.
    /// </remarks>
    private async Task<OperationResult<SessionView>> CompleteProducingStepAsync(
        SessionAggregate aggregate,
        SessionAggregate afterStart,
        WorkflowTransition started,
        CommandContext context,
        ProducingWork work,
        ProcessingAttempt runningAttempt,
        StepWork produced,
        IAutomationStopSignal stop,
        CancellationToken cancellationToken)
    {

        // The Revision hangs off the Revision this attempt actually consumed. For a manual crop
        // that is the file the operator drew on, which is what makes ManualImport lineage
        // truthful rather than positional (Part C2 §16).
        RevisionId revisionId = RevisionId.From(_idGenerator.NewId());
        Revision newRevision = Revision.Create(
            revisionId, aggregate.Session.Id, work.InputRevision, work.Operation,
            produced.Output, produced.Facts, context.NowUtc);

        WorkflowCommand.System.AttemptSucceeded succeeded = new(
            context.NewAttemptId, work.Step, revisionId, produced.Facts.Sha256);
        WorkflowTransition finished = _engine.Apply(started.State, succeeded, context);
        if (finished.IsRejected)
        {
            return OperationResult.Fail<SessionView>(MapRejection(finished.Rejection!));
        }

        ProcessingAttempt succeededAttempt = runningAttempt.Succeed(
            revisionId, context.NowUtc, produced.AdapterNotes)
            with { PsdInspection = produced.PsdInspection, PdfInspection = produced.PdfInspection };

        // The crop geometry joins the attempt in the same closing transaction as its status, its
        // Revision and the step's move to ReviewRequired — never as a later best-effort update
        // (SCRUM-11081 §10). If that transaction fails, there is no successful attempt, no
        // Revision and no bounds; the three cannot come apart.
        if (produced.TrimGeometry is { } geometry)
        {
            succeededAttempt = succeededAttempt.WithTrimGeometry(geometry);
        }

        if (produced.ManualCropGeometry is { } manualGeometry)
            succeededAttempt = succeededAttempt.WithManualCropGeometry(manualGeometry);

        ProcessingSession sessionAfterFinish = MergeSession(
            afterStart.Session, finished.State, finished.Effects, context.NowUtc);

        List<PrintOutput> newOutputs = [];
        if (work.Step == StepKind.PhotoshopOutput)
        {
            OperationResult<PrintOutput> output = BuildPrintOutput(
                aggregate.Session.Id, revisionId, started.State, produced, context);
            if (output.IsFailure)
            {
                return OperationResult.Fail<SessionView>(output.Failure);
            }

            newOutputs.Add(output.Value);
        }

        SessionMutation finishing = BuildMetadataMutation(
            afterStart, sessionAfterFinish, finished.State, finished.Effects, context,
            newRevisions: [newRevision], upsertAttempts: [succeededAttempt], upsertOutputs: newOutputs);

        if (work.ManualResultPath is not null)
            finishing = finishing with { LockChange = null };

        // Closed on CancellationToken.None when a stop is pending, for the same reason a
        // cancelled attempt is: the validated file already exists, and losing the transaction
        // that records it would leave a real output with no Revision — the one outcome §16
        // exists to prevent.
        OperationResult<Unit> committedFinish = await _repository.CommitAsync(
            finishing, stop.RequestedMode is null ? cancellationToken : CancellationToken.None);
        if (committedFinish.IsFailure)
        {
            return OperationResult.Fail<SessionView>(committedFinish.Failure);
        }

        SessionAggregate afterFinish = afterStart with
        {
            Session = sessionAfterFinish,
            Steps = finished.State.Steps,
            Revisions = [.. afterStart.Revisions, newRevision],
            Attempts = [.. afterStart.Attempts, succeededAttempt],
            Outputs = OutputsAfter(afterStart.Outputs, finishing),
        };

        // §16's boundary, on the far side of the success transaction. A Take Over that arrived
        // while the export was completing still hands Meitu to the operator — but it hands over
        // a session whose Attempt and Revision are complete, and it cannot turn either into a
        // failure, because the only thing left to change is the session's automated
        // progression. A plain Stop has nothing further to do: the run is over.
        if (stop.RequestedMode == AutomationStopMode.TakeOver)
        {
            return await HandOffAfterSuccessAsync(afterFinish, finished.State, context, work.Step, stop);
        }

        return ViewOf(
            finished.State, afterFinish.Revisions, afterFinish.Outputs, afterFinish.Attempts);
    }

    /// <summary>Performs one attempt's file work, whatever kind of work that is.</summary>
    /// <remarks>
    /// <paramref name="attempt"/> is the row already written by the opening transaction, and it
    /// is deliberately what the Meitu request is built from rather than the live session state:
    /// the decision that reaches the adapter and the decision the audit history records are then
    /// the same value by construction, not two reads of a setting that could have moved in
    /// between (Epic 11300 Part C2B1 §12).
    /// </remarks>
    private async Task<OperationResult<StepWork>> PerformStepWorkAsync(
        SessionAggregate aggregate, WorkflowSnapshot state, StepDefinition definition,
        ProducingWork work, ProcessingAttempt attempt, CommandContext context,
        IAutomationStopSignal stop,
        Action<WorkspaceFileRef> establishExpectedOutput,
        CancellationToken cancellationToken)
    {
        WorkspaceDirRef session = aggregate.Session.Workspace;
        if (work.ManualResultPath is { } selected)
        {
            if (_manualResults is null)
                return OperationResult.Fail<StepWork>(FailureCode.PreconditionNotMet, "Manual result import is unavailable.");
            var imported = await _manualResults.ImportAsync(session, attempt.Id, work.Step,
                FindRevision(aggregate, work.InputRevision)!.Facts, selected, cancellationToken);
            return imported.IsFailure
                ? OperationResult.Fail<StepWork>(imported.Failure)
                : OperationResult.Ok(new StepWork(imported.Value.File, imported.Value.Facts,
                    $"Manual result selected as {imported.Value.File.FileName}; copied SHA256 {imported.Value.Facts.Sha256}; operator {context.Operator}"));
        }
        WorkspaceFileRef? input = work.InputRevision is RevisionId inputId
            ? aggregate.Revisions.FirstOrDefault(r => r.Id == inputId)?.File
            : null;

        if (work.Adapter == AdapterKind.None)
        {
            // ApprovedPngExport: promote the approved upstream bytes unchanged. The existing
            // hash-bound approval already covers the promoted file by construction (plan §7.3).
            if (input is not { } sourceRef)
            {
                return OperationResult.Fail<StepWork>(
                    FailureCode.PreconditionNotMet, "Nothing to promote: no upstream Revision.");
            }

            OperationResult<NamingPatternSet> patterns = _presetProvider.GetNamingPatterns();
            if (patterns.IsFailure)
            {
                return OperationResult.Fail<StepWork>(patterns.Failure);
            }

            string proposedName = aggregate.Session.OutputName.Value + ".png";
            OperationResult<WorkspaceFileRef> reserved =
                _workspace.ReserveOutput(session, WorkspaceArea.Approved, proposedName, patterns.Value);
            if (reserved.IsFailure)
            {
                return OperationResult.Fail<StepWork>(reserved.Failure);
            }

            establishExpectedOutput(reserved.Value);

            OperationResult<Unit> written = await _workspace.WriteReservedAsync(reserved.Value, sourceRef, cancellationToken);
            if (written.IsFailure)
            {
                return OperationResult.Fail<StepWork>(WithExpectedOutput(written.Failure, reserved.Value));
            }

            return await InspectAsync(reserved.Value, cancellationToken);
        }

        if (input is not { } upstreamRef)
        {
            return OperationResult.Fail<StepWork>(
                FailureCode.PreconditionNotMet, $"Step {work.Step} has no upstream Revision to work from.");
        }

        OperationResult<WorkspaceFileRef> workingCopy =
            await _workspace.CreateWorkingCopyAsync(session, context.NewAttemptId, upstreamRef, cancellationToken);
        if (workingCopy.IsFailure)
        {
            return OperationResult.Fail<StepWork>(workingCopy.Failure);
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
                    return OperationResult.Fail<StepWork>(meituPatterns.Failure);
                }

                // A pattern the naming authority cannot render is reported, not thrown: it
                // arrives as preset data, and an exception escaping here would leave the
                // shell with an unhandled fault rather than a failed step (naming-contract
                // fix §6).
                OperationResult<string> producedName = OutputFileNaming.BuildProposedFileName(
                    work.Operation == OperationKind.Enhance
                        ? NamingArtifactKind.Enhanced
                        : NamingArtifactKind.Cutout,
                    aggregate.Session.OutputName,
                    meituPatterns.Value);
                if (producedName.IsFailure)
                {
                    return OperationResult.Fail<StepWork>(producedName.Failure);
                }

                // The decision the attempt row already recorded, never a constant and never a
                // fresh read of the session. For background removal the engine refused to start
                // the step at all without a usable authority, so the value here is one a human
                // granted over content they reviewed; the guard below is what makes that a
                // property of this code rather than a promise about a caller elsewhere
                // (Epic 11300 Part C2B1 §7, §12).
                //
                // Enhancement is unchanged: it is not a background removal, so it carries
                // Unspecified exactly as it always has, and nothing about its behaviour moves.
                BackgroundRemovalDecision decision = BackgroundRemovalDecision.Unspecified;
                if (operation == MeituOperation.RemoveBackground)
                {
                    if (attempt.BackgroundRemovalAuthority is not { } authority)
                    {
                        return OperationResult.Fail<StepWork>(
                            FailureCode.PreconditionNotMet,
                            "Background removal reached the adapter without a recorded reviewed-content authority. " +
                            "No request is built: a missing product decision is not something to guess at.");
                    }

                    decision = authority.Decision;
                }

                WorkspaceFileRef expectedOutput = SiblingOf(workingCopy.Value, producedName.Value);
                establishExpectedOutput(expectedOutput);
                OperationResult<AdapterOutput> result = await _meitu.ProcessAsync(
                    new MeituRequest(
                        workingCopy.Value,
                        operation,
                        decision,
                        ParentDirOf(workingCopy.Value),
                        expectedOutput)
                    {
                        // The one seam through which an operator's Stop reaches Meitu. What the
                        // adapter may do with it is decided by AutomationStopPolicy from the
                        // phase the adapter itself reports — this only hands over the channel
                        // (Part D2A §4, §9).
                        Stop = stop,
                    },
                    cancellationToken);
                if (result.IsFailure)
                {
                    return OperationResult.Fail<StepWork>(WithExpectedOutput(result.Failure, expectedOutput));
                }

                OperationResult<StepWork> inspected = await InspectAsync(
                    result.Value.ProducedFile, cancellationToken, result.Value.AdapterNotes);
                return inspected.IsFailure
                    ? OperationResult.Fail<StepWork>(WithExpectedOutput(inspected.Failure, expectedOutput))
                    : inspected;
            }

            case AdapterKind.Pdf:
            {
                WorkspaceFileRef destination = SiblingOf(workingCopy.Value, "prepared-pdf.png");
                establishExpectedOutput(destination);
                var prepared = await _pdf.PrepareAsync(new PdfPreparationRequest(workingCopy.Value, destination)
                    { Stop = stop }, cancellationToken);
                if (prepared.IsFailure)
                    return OperationResult.Fail<StepWork>(WithExpectedOutput(prepared.Failure, destination));
                if (prepared.Value.ProducedFile != destination || prepared.Value.PdfInspection is not { } inspection ||
                    !inspection.IsPreparedSinglePage)
                    return OperationResult.Fail<StepWork>(WithExpectedOutput(
                        OperationFailure.Create(FailureCode.PdfPreparationFailed,
                            "PDF preparation returned no accepted single-page inspection or the wrong destination."),
                        destination));
                var inspected = await InspectAsync(destination, cancellationToken, prepared.Value.AdapterNotes);
                if (inspected.IsFailure)
                    return OperationResult.Fail<StepWork>(WithExpectedOutput(
                        OperationFailure.Create(FailureCode.PdfPreparationFailed,
                            inspected.Failure.TechnicalDetail, isRetryable: true) with { PdfInspection = inspection },
                        destination));
                FileFacts facts = inspected.Value.Facts;
                if (facts != prepared.Value.ValidatedPdfRaster || facts.Format != ImageFormat.Png || facts.ColourMode != ColourMode.Rgb ||
                    facts.PixelWidth != inspection.PixelWidth || facts.PixelHeight != inspection.PixelHeight ||
                    facts.DpiX is not { } dx || facts.DpiY is not { } dy || Math.Abs(dx - 300) > .02 || Math.Abs(dy - 300) > .02 ||
                    (inspection.HasTransparency == true && facts.HasAlpha != true))
                    return OperationResult.Fail<StepWork>(WithExpectedOutput(
                        OperationFailure.Create(FailureCode.PdfPreparationFailed,
                            "The independently inspected raster differs from the PDF full-page 300-PPI contract.")
                            with { PdfInspection = inspection },
                        destination));
                return OperationResult.Ok(inspected.Value with { PdfInspection = inspection });
            }

            case AdapterKind.Photoshop:
            {
                if (work.Operation == OperationKind.PreparePsd)
                {
                    WorkspaceFileRef destination = SiblingOf(workingCopy.Value, "prepared-psd.png");
                    establishExpectedOutput(destination);
                    OperationResult<AdapterOutput> prepared = await _photoshop.PreparePsdAsync(
                        new PsdPreparationRequest(workingCopy.Value, destination) { Stop = stop }, cancellationToken);
                    if (prepared.IsFailure)
                    {
                        return OperationResult.Fail<StepWork>(WithExpectedOutput(prepared.Failure, destination));
                    }

                    if (prepared.Value.ProducedFile != destination || prepared.Value.PsdInspection is not { } inspection)
                    {
                        return OperationResult.Fail<StepWork>(WithExpectedOutput(
                            OperationFailure.Create(FailureCode.PsdPreparationFailed,
                                "PSD preparation returned no inspection or the wrong output destination."),
                            destination));
                    }

                    OperationResult<StepWork> inspected = await InspectAsync(destination, cancellationToken,
                        prepared.Value.AdapterNotes);
                    if (inspected.IsFailure)
                    {
                        return OperationResult.Fail<StepWork>(WithExpectedOutput(
                            inspected.Failure with { PsdInspection = inspection }, destination));
                    }

                    FileFacts facts = inspected.Value.Facts;
                    if (facts.Format != ImageFormat.Png || facts.ColourMode != ColourMode.Rgb ||
                        facts.PixelWidth != inspection.PixelWidth || facts.PixelHeight != inspection.PixelHeight ||
                        !inspection.HasRealMergedData || inspection.OriginalMode != "RGB" || inspection.BitDepth != 8 ||
                        inspection.HasTransparency is null ||
                        (inspection.HasTransparency == true && facts.HasAlpha != true))
                    {
                        return OperationResult.Fail<StepWork>(WithExpectedOutput(
                            OperationFailure.Create(FailureCode.PsdPreparationFailed,
                                "The prepared PSD raster did not independently match the accepted full-canvas RGB/8 contract.")
                                with { PsdInspection = inspection },
                            destination));
                    }

                    return OperationResult.Ok(inspected.Value with { PsdInspection = inspection });
                }

                if (state.Dimensions is not { } dimensions)
                {
                    return OperationResult.Fail<StepWork>(
                        FailureCode.PreconditionNotMet, "Photoshop output requires confirmed print dimensions.");
                }

                if (state.WhiteUnderbaseBranch is not { } branch)
                {
                    return OperationResult.Fail<StepWork>(
                        FailureCode.PreconditionNotMet, "Photoshop output requires an explicit white-underbase branch.");
                }

                // The plan the attempt row already recorded, never a fresh read of the session
                // and never a recalculation here. The engine refused to start the step without a
                // usable plan, so the value is one that was bound to the exact bytes about to be
                // opened; this guard is what makes that a property of this code rather than a
                // promise about a caller elsewhere (Epic 11400 Part B1A.2A §12, §17).
                if (attempt.Preparation is not { } preparation)
                {
                    return OperationResult.Fail<StepWork>(
                        FailureCode.PreconditionNotMet,
                        "Photoshop output reached the adapter without a recorded preparation. No request is " +
                        "built: which edge is written, and whether an enlargement was authorised, are not " +
                        "things to work out here.");
                }

                OperationResult<ProductionPresetRef> preset = _presetProvider.GetVerifiedPreset();
                if (preset.IsFailure)
                {
                    return OperationResult.Fail<StepWork>(preset.Failure);
                }

                OperationResult<NamingPatternSet> patterns = _presetProvider.GetNamingPatterns();
                if (patterns.IsFailure)
                {
                    return OperationResult.Fail<StepWork>(patterns.Failure);
                }

                OperationResult<string> tiffName = OutputFileNaming.BuildProposedFileName(
                    NamingArtifactKind.ProductionTiff, aggregate.Session.OutputName, patterns.Value, dimensions.WidthMm);
                if (tiffName.IsFailure)
                {
                    return OperationResult.Fail<StepWork>(tiffName.Failure);
                }

                // The destination this attempt's TIFF may occupy: the workflow's rendered name,
                // in this attempt's own Working directory, exactly as every other producing step
                // places its result (Epic 11400 Part C2A §7).
                //
                // Working rather than Approved, and that is the whole point of the placement. The
                // file does not exist yet and will not be reviewed until it does, so promoting it
                // into Approved\ before an operator has seen it would put an unreviewed artefact
                // in the one area that is supposed to mean "reviewed" — and would put a live
                // Photoshop save inside it. Promotion into Approved\ on approval, and into
                // Rejected\ on rejection, is Part C2B's (§28).
                //
                // Not a ReserveOutput call either: the attempt directory is named by an attempt
                // id, so the name cannot collide, and a reservation writes a zero-byte placeholder
                // that C1's saver would correctly refuse to overwrite. A retry gets a new attempt
                // directory and therefore a new destination, leaving the old file untouched (§21).
                WorkspaceFileRef reserved = SiblingOf(workingCopy.Value, tiffName.Value);
                establishExpectedOutput(reserved);

                OperationResult<AdapterOutput> result = await _photoshop.GenerateAsync(
                    new PhotoshopRequest(
                        workingCopy.Value, dimensions, preparation, preset.Value, branch,
                        reserved.FileName, ParentDirOf(workingCopy.Value), reserved),
                    cancellationToken);
                if (result.IsFailure)
                {
                    return OperationResult.Fail<StepWork>(WithExpectedOutput(result.Failure, reserved));
                }

                OperationResult<StepWork> inspectedOutput = await InspectAsync(
                    result.Value.ProducedFile, cancellationToken, result.Value.AdapterNotes);
                return inspectedOutput.IsFailure
                    ? OperationResult.Fail<StepWork>(WithExpectedOutput(inspectedOutput.Failure, reserved))
                    : inspectedOutput;
            }

            case AdapterKind.Internal:
            {
                // Checked rather than assumed: AdapterKind.Internal means "deterministic
                // in-process pixel work", and Trim is only the first such step. A future
                // internal step routed here by accident would silently be trimmed.
                if (definition.Kind != StepKind.Trim)
                {
                    return OperationResult.Fail<StepWork>(
                        FailureCode.PreconditionNotMet,
                        $"Step {definition.Kind} is internal but has no deterministic processor.");
                }

                // A rectangle on the work means a human chose it, so the alpha scan is skipped
                // entirely: the automatic path already refused this file, and re-running it
                // would refuse again for the same reason (Part C2 §10, §21).
                if (work.ManualCrop is { } crop)
                {
                    WorkspaceFileRef expectedOutput = SiblingOf(workingCopy.Value, ManualCropOutputFileName);
                    establishExpectedOutput(expectedOutput);
                    OperationResult<ManualCropResult> cropped = await _manualCrop.CropAsync(
                        new ManualCropRequest(
                            workingCopy.Value,
                            expectedOutput,
                            crop, work.ManualCropMargin),
                        cancellationToken);

                    if (cropped.IsFailure)
                        return OperationResult.Fail<StepWork>(WithExpectedOutput(cropped.Failure, expectedOutput));
                    ManualCropGeometry expected = ManualCropGeometry.Create(
                        crop, work.ManualCropMargin, cropped.Value.SourceWidth, cropped.Value.SourceHeight);
                    if (cropped.Value.AppliedBounds != expected.AppliedBounds ||
                        cropped.Value.Geometry != expected)
                        return OperationResult.Fail<StepWork>(WithExpectedOutput(
                            OperationFailure.Create(FailureCode.OutputValidationFailed,
                                "The manual crop processor returned different geometry than requested."),
                            expectedOutput));
                    OperationResult<StepWork> inspectedCrop = await InspectAsync(
                        cropped.Value.ProducedFile, cancellationToken, manualCropGeometry: cropped.Value.Geometry);
                    return inspectedCrop.IsFailure
                        ? OperationResult.Fail<StepWork>(WithExpectedOutput(inspectedCrop.Failure, expectedOutput))
                        : inspectedCrop;
                }

                // The operator's recorded decision, not a constant. It arrives here from
                // WorkflowSnapshot.TrimMargin, which SetTrimParameters is the only way to
                // change — so nothing between the screen and the processor can substitute a
                // margin, and the same value is what the attempt row recorded before this ran
                // (Epic 11200 Part C3 §13, §14).
                WorkspaceFileRef trimOutput = SiblingOf(workingCopy.Value, TrimOutputFileName);
                establishExpectedOutput(trimOutput);
                OperationResult<TrimResult> trimmed = await _trim.TrimAsync(
                    new TrimRequest(
                        workingCopy.Value,
                        trimOutput,
                        state.TrimMargin),
                    cancellationToken);
                if (trimmed.IsFailure)
                {
                    return OperationResult.Fail<StepWork>(WithExpectedOutput(trimmed.Failure, trimOutput));
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
                    return OperationResult.Fail<StepWork>(WithExpectedOutput(
                        OperationFailure.Create(
                            FailureCode.ManualCropRequired,
                            result.ManualCropReason ?? "No usable alpha content was found.",
                            isRetryable: false), trimOutput));
                }

                // The processor's own rectangles, carried to the closing transaction rather than
                // discarded here (SCRUM-11081 §9). This is the exact geometry that produced the
                // file being inspected on the next line: the alpha scan's content rectangle and
                // the margined, canvas-clamped rectangle the crop was actually taken at. A
                // produced trim always has both — TrimResult.Produced is the only way to build
                // one — so a null here would mean the outcome was ManualCropRequired, which
                // returned above.
                OperationResult<StepWork> inspectedTrim = await InspectAsync(
                    result.ProducedFile!.Value,
                    cancellationToken,
                    trimGeometry: TrimGeometry.Create(result.ContentBounds!.Value, result.AppliedBounds!.Value));
                return inspectedTrim.IsFailure
                    ? OperationResult.Fail<StepWork>(WithExpectedOutput(inspectedTrim.Failure, trimOutput))
                    : inspectedTrim;
            }

            default:
                return OperationResult.Fail<StepWork>(
                    FailureCode.PreconditionNotMet, $"Unsupported adapter kind '{work.Adapter}'.");
        }
    }

    private OperationResult<PrintOutput> BuildPrintOutput(
        SessionId sessionId, RevisionId revisionId, WorkflowSnapshot stateBeforeFinish,
        StepWork work, CommandContext context)
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

    /// <summary>
    /// Reads back what was actually written, and carries the producer's own measurements with it.
    /// </summary>
    /// <remarks>
    /// <paramref name="trimGeometry"/> passes straight through rather than being derived from
    /// <paramref name="file"/>. The inspector can say how large the cropped PNG is; it cannot say
    /// where in the source that rectangle sat, and re-running the alpha scan over an
    /// already-cropped image would answer a different question — the trimmed file's own content
    /// bounds, which for a margined crop are not the source's (SCRUM-11081 §9).
    /// </remarks>
    private async Task<OperationResult<StepWork>> InspectAsync(
        WorkspaceFileRef file,
        CancellationToken cancellationToken,
        string? adapterNotes = null,
        TrimGeometry? trimGeometry = null, ManualCropGeometry? manualCropGeometry = null)
    {
        string absolute = _workspace.ResolveAbsolute(file);
        OperationResult<FileFacts> inspected = await _fileInspector.InspectAsync(absolute, cancellationToken);
        if (inspected.IsSuccess && manualCropGeometry is { AppliedBounds: var bounds } &&
            (inspected.Value.PixelWidth != bounds.Width || inspected.Value.PixelHeight != bounds.Height))
            return OperationResult.Fail<StepWork>(FailureCode.OutputValidationFailed,
                "The manual crop raster dimensions do not match its applied bounds.");
        return inspected.IsSuccess
            ? OperationResult.Ok(new StepWork(file, inspected.Value, adapterNotes, trimGeometry, ManualCropGeometry: manualCropGeometry))
            : OperationResult.Fail<StepWork>(inspected.Failure);
    }

    private async Task<OperationResult<SessionView>> FailAttemptAsync(
        SessionAggregate aggregate, WorkflowSnapshot stateAfterStart, CommandContext context, StepKind step,
        ProcessingAttempt runningAttempt, OperationFailure failure, CancellationToken cancellationToken)
    {
        failure = FailureEvidence.ForAttempt(failure, runningAttempt.Id);
        WorkflowCommand.System.AttemptFailed failedCommand = new(runningAttempt.Id, step, failure);
        WorkflowTransition failedTransition = _engine.Apply(stateAfterStart, failedCommand, context);
        if (failedTransition.IsRejected)
        {
            return OperationResult.Fail<SessionView>(MapRejection(failedTransition.Rejection!));
        }

        if (runningAttempt.Operation == OperationKind.ManualResultImport)
        {
            WorkflowTransition handedOff = _engine.Apply(failedTransition.State,
                new WorkflowCommand.HandOff(step, "Manual result validation failed; manual processing remains authorised."), context);
            if (handedOff.IsRejected)
                return OperationResult.Fail<SessionView>(MapRejection(handedOff.Rejection!));
            failedTransition = WorkflowTransition.Accepted(handedOff.State,
                handedOff.Effects.Where(e => e is not WorkflowEffect.ReleaseAutomationLock).ToArray());
        }

        ProcessingAttempt failedAttempt = runningAttempt.Fail(failure, context.NowUtc)
            with { PsdInspection = failure.PsdInspection, PdfInspection = failure.PdfInspection };
        ProcessingSession updatedSession =
            MergeSession(aggregate.Session, failedTransition.State, failedTransition.Effects, context.NowUtc);

        SessionMutation mutation = BuildMetadataMutation(
            aggregate, updatedSession, failedTransition.State, failedTransition.Effects, context,
            upsertAttempts: [failedAttempt]) with
        {
            NewAutomationLog = [RecordAutomationStop(aggregate.Session.Id, step, failure, context.NowUtc)],
        };

        OperationResult<Unit> committed = await _repository.CommitAsync(mutation, cancellationToken);
        return committed.IsSuccess
            ? OperationResult.Fail<SessionView>(failure)
            : OperationResult.Fail<SessionView>(committed.Failure);
    }

    /// <summary>
    /// Builds the one durable <see cref="AutomationLogEntry"/> an automation stop produces
    /// (Jira 11108; MVP design §17.6).
    /// </summary>
    /// <remarks>
    /// Called only where an attempt ends carrying a structured <see cref="OperationFailure"/> —
    /// a failure or a stop. An <c>Interrupted</c> attempt, which startup recovery writes for a
    /// process that died, carries no failure at all, and it gets no row rather than an invented
    /// code: the table's <c>FailureCode</c> is NOT NULL because every row it holds is a real
    /// structured error, not a lifecycle breadcrumb.
    /// <para>
    /// The identifier is minted here rather than arriving through <c>CommandContext</c>: the
    /// engine is a pure reducer that knows nothing about a diagnostic log, and pre-allocating an
    /// id on every context for the rare command that stops would be allocation the reducer's
    /// determinism does not need.
    /// </para>
    /// </remarks>
    private AutomationLogEntry RecordAutomationStop(
        SessionId sessionId, StepKind step, OperationFailure failure, DateTimeOffset atUtc) =>
        AutomationLogEntry.ForStop(
            AutomationLogId.From(_idGenerator.NewId()), sessionId, step, atUtc, failure);

    /// <summary>
    /// The stable English reason recorded on the session when an operator takes the external
    /// application over from a running attempt (Epic 11300 Part D2A §18, §29).
    /// </summary>
    /// <remarks>
    /// English and unlocalised, like every other persisted internal value (MVP design §13.4).
    /// What the operator reads is the shell's own wording, resolved from the resource file at
    /// display time; this is what the audit row says.
    /// </remarks>
    internal const string TakeOverHandOffReason =
        "Operator took over the external application while an automated attempt was running.";

    /// <summary>
    /// Closes a running attempt because a human stopped it, and — for a takeover — hands the
    /// session to the operator (Epic 11300 Part D2A §12, §18, §19, §29).
    /// </summary>
    /// <remarks>
    /// Deliberately a sibling of <see cref="FailAttemptAsync"/> rather than a flag on it. The
    /// two produce different attempt statuses, different step states and different session
    /// outcomes, and the one thing that must never happen — a stop being recorded as an
    /// automation failure — is exactly what a shared method with a boolean would eventually do.
    /// <para>
    /// Everything is committed on <see cref="CancellationToken.None"/>. The caller's token is
    /// very often already cancelled by the time a stop unwinds, and a metadata transaction that
    /// gave up here would leave the attempt <c>Running</c> and the automation lock held — the
    /// exact state §32 requires not to survive a stop. A crash before this commit is still
    /// covered by D1's startup <c>Running → Interrupted</c> recovery.
    /// </para>
    /// <para>
    /// The takeover's <c>HandOff</c> is applied as a second command against the state the first
    /// one produced, not synthesised: it goes through the same engine, the same guards and the
    /// same effects as an operator pressing Hand Off on a stopped step, which is what makes
    /// §18's "use the existing HandedOff semantics" true rather than merely intended. That
    /// command emits no adapter call and no external input of any kind.
    /// </para>
    /// </remarks>
    private async Task<OperationResult<SessionView>> StopAttemptAsync(
        SessionAggregate aggregate,
        WorkflowSnapshot stateAfterStart,
        CommandContext context,
        StepKind step,
        ProcessingAttempt runningAttempt,
        StepDefinition definition,
        AutomationStopMode mode,
        IAutomationStopSignal stop,
        OperationFailure? adapterFailure)
    {
        OperationFailure failure = DescribeStop(
            mode, stop, definition.IsAdapterBacked, runningAttempt, step, adapterFailure);
        failure = FailureEvidence.ForAttempt(failure, runningAttempt.Id);

        WorkflowCommand.System.AttemptCancelled cancelled = new(runningAttempt.Id, step, failure);
        WorkflowTransition stopped = _engine.Apply(stateAfterStart, cancelled, context);
        if (stopped.IsRejected)
        {
            return OperationResult.Fail<SessionView>(MapRejection(stopped.Rejection!));
        }

        ProcessingAttempt cancelledAttempt = runningAttempt.Cancel(
            failure, context.NowUtc, DescribeRetainedState(mode, stop, definition.IsAdapterBacked))
            with { PsdInspection = adapterFailure?.PsdInspection, PdfInspection = adapterFailure?.PdfInspection };

        WorkflowSnapshot state = stopped.State;
        List<WorkflowEffect> effects = [.. stopped.Effects];

        if (mode == AutomationStopMode.TakeOver)
        {
            WorkflowTransition handedOff = _engine.Apply(
                state, new WorkflowCommand.HandOff(step, TakeOverHandOffReason), context);
            if (handedOff.IsRejected)
            {
                return OperationResult.Fail<SessionView>(MapRejection(handedOff.Rejection!));
            }

            state = handedOff.State;
            effects.AddRange(handedOff.Effects);
        }

        ProcessingSession updatedSession = MergeSession(aggregate.Session, state, effects, context.NowUtc);
        SessionMutation mutation = BuildMetadataMutation(
            aggregate, updatedSession, state, effects, context, upsertAttempts: [cancelledAttempt]) with
        {
            NewAutomationLog = [RecordAutomationStop(aggregate.Session.Id, step, failure, context.NowUtc)],
        };

        OperationResult<Unit> committed = await _repository.CommitAsync(mutation, CancellationToken.None);
        return committed.IsFailure
            ? OperationResult.Fail<SessionView>(committed.Failure)
            : OperationResult.Fail<SessionView>(failure);
    }

    /// <summary>
    /// Hands the session to the operator after an attempt that had already succeeded
    /// (Epic 11300 Part D2A §16).
    /// </summary>
    /// <remarks>
    /// The narrow case where §16 and §17 meet: the operator asked to take Meitu over, and the
    /// export finished before the request could stop anything. The Revision stands. What
    /// changes is only the session's automated progression, so the same <c>HandOff</c> command
    /// an operator could have pressed a moment later does the whole job.
    /// <para>
    /// A refusal here is not an error to surface: the step may have landed somewhere
    /// <c>HandOff</c> is not legal from, and in that case the honest outcome is the successful
    /// view the run actually produced rather than a failure about a takeover that arrived too
    /// late to mean anything.
    /// </para>
    /// </remarks>
    private async Task<OperationResult<SessionView>> HandOffAfterSuccessAsync(
        SessionAggregate aggregate,
        WorkflowSnapshot state,
        CommandContext context,
        StepKind step,
        IAutomationStopSignal stop)
    {
        WorkflowTransition handedOff = _engine.Apply(
            state, new WorkflowCommand.HandOff(step, TakeOverHandOffReason), context);
        if (handedOff.IsRejected)
        {
            return ViewOf(state, aggregate.Revisions, aggregate.Outputs, aggregate.Attempts);
        }

        ProcessingSession updatedSession = MergeSession(
            aggregate.Session, handedOff.State, handedOff.Effects, context.NowUtc);
        SessionMutation mutation = BuildMetadataMutation(
            aggregate, updatedSession, handedOff.State, handedOff.Effects, context);

        OperationResult<Unit> committed = await _repository.CommitAsync(mutation, CancellationToken.None);
        return committed.IsFailure
            ? OperationResult.Fail<SessionView>(committed.Failure)
            : ViewOf(handedOff.State, aggregate.Revisions, aggregate.Outputs, aggregate.Attempts);
    }

    /// <summary>
    /// Builds the structured record of one stop: what was asked for, how far the operation had
    /// got, whether a signed cancel was actually invoked, and what may be left running
    /// (Epic 11300 Part D2A §29).
    /// </summary>
    /// <remarks>
    /// The keys are the audit §29 enumerates, and they are written here — once, from the signal
    /// — rather than by each adapter, so a stop against the fake adapter and a stop against
    /// production produce the same queryable shape. Anything the adapter itself observed is
    /// preserved in the technical detail rather than replacing it.
    /// <para>
    /// <c>meituCancelInvoked</c> is read from the signal and never from the mode. "The operator
    /// pressed Stop during Busy" and "a signed cancel control was resolved and invoked" are
    /// different claims, and §10 turns on the difference: the second is false whenever the
    /// control could not be proven, and the audit has to say so.
    /// </para>
    /// </remarks>
    private static OperationFailure DescribeStop(
        AutomationStopMode mode,
        IAutomationStopSignal stop,
        bool drivesExternalApplication,
        ProcessingAttempt attempt,
        StepKind step,
        OperationFailure? adapterFailure)
    {
        RetainedExternalState retained = drivesExternalApplication
            ? AutomationStopPolicy.RetainedFor(stop.Phase, stop.OperationLeftBusyAfterCancel)
            : RetainedExternalState.None;

        string headline = mode == AutomationStopMode.TakeOver
            ? "The operator took over the external application. PrintFlow stopped this attempt and " +
              "produced no further automated input; nothing was cancelled, dismissed or closed."
            : stop.OperationCancelWasInvoked && stop.OperationLeftBusyAfterCancel
                ? "The operator stopped this operation. PrintFlow invoked the signed cancel control " +
                  "once and the external application positively left its busy state."
                : stop.OperationCancelWasInvoked
                    ? "The operator stopped this operation. PrintFlow invoked the signed cancel control " +
                      "once, but the external application did not positively leave its busy state. " +
                      "It may be unresponsive and operator action is required."
                : "The operator stopped this operation. PrintFlow stopped its own orchestration; no " +
                  "cancel control was invoked, so the external operation may still be running and " +
                  "operator action may be required.";

        AutomationStopAudit record = new(mode, stop.Phase, stop.OperationCancelWasInvoked, retained);

        Dictionary<string, string> audit = new()
        {
            [AutomationStopAudit.StopRequestedKey] = "true",
            [AutomationStopAudit.ModeKey] = mode.ToString(),
            [AutomationStopAudit.PhaseKey] = stop.Phase.ToString(),
            [AutomationStopAudit.CancelInvokedKey] = stop.OperationCancelWasInvoked ? "true" : "false",
            ["meituLeftBusy"] = stop.OperationLeftBusyAfterCancel ? "true" : "false",
            [AutomationStopAudit.RetainedKey] = retained.ToString(),
            ["operatorActionRequired"] = record.OperatorActionMayBeRequired ? "true" : "false",
            ["forceTerminationInvoked"] = "false",
            ["revisionCreated"] = "false",
            ["attemptId"] = attempt.Id.ToString(),
            ["step"] = step.ToString(),
        };

        if (adapterFailure is not null)
        {
            audit["adapterFailureCode"] = adapterFailure.Code.ToString();

            // Preserve only the closed Error Details evidence fields. The stop owns its own
            // code/message and audit vocabulary; the adapter remains the authority for a local
            // capture and for the destination that had already been established.
            foreach (string key in new[]
                     {
                         AutomationLogEntry.ScreenshotContextKey,
                         FailureEvidence.ExpectedOutputPathKey,
                         FailureEvidence.ExpectedOutputEstablishedKey,
                     })
            {
                if (adapterFailure.Context.TryGetValue(key, out string? value))
                {
                    audit[key] = value;
                }
            }
        }

        string detail = adapterFailure is null
            ? headline
            : $"{headline} The run reported: {adapterFailure.TechnicalDetail}";

        return OperationFailure.Create(
            FailureCode.Cancelled,
            detail,
            isRetryable: true,
            context: audit,
            messageKey: mode == AutomationStopMode.TakeOver
                ? "Failure_AutomationHandedOff"
                : "Failure_AutomationStopped");
    }

    /// <summary>The human-readable retained-state note kept on the stopped attempt row (§29).</summary>
    private static string DescribeRetainedState(
        AutomationStopMode mode, IAutomationStopSignal stop, bool drivesExternalApplication)
    {
        RetainedExternalState retained = drivesExternalApplication
            ? AutomationStopPolicy.RetainedFor(stop.Phase, stop.OperationLeftBusyAfterCancel)
            : RetainedExternalState.None;

        return $"stop:{mode}; phase {stop.Phase}; " +
               $"signed cancel invoked {(stop.OperationCancelWasInvoked ? "yes" : "no")}; " +
               $"left Busy after cancel {(stop.OperationLeftBusyAfterCancel ? "yes" : "no")}; " +
               $"retained external state {retained}; force termination no";
    }

    /// <summary>
    /// The two steps that turn the operator's chosen file into an established source: the copy
    /// into the managed workspace, and the inspection that describes what was copied.
    /// </summary>
    /// <remarks>
    /// Together rather than separately because neither half is a source on its own — a copy
    /// nothing has read is not something a Revision may be written against — and because that
    /// makes them one cancellable unit with one containment boundary at the caller.
    /// </remarks>
    private async Task<OperationResult<(WorkspaceFileRef Source, FileFacts Facts)>> EstablishSourceAsync(
        WorkspaceDirRef workspaceDir, string sourceAbsolutePath, CancellationToken cancellationToken)
    {
        OperationResult<WorkspaceFileRef> imported =
            await _workspace.ImportSourceAsync(workspaceDir, sourceAbsolutePath, cancellationToken);
        if (imported.IsFailure)
        {
            return OperationResult.Fail<(WorkspaceFileRef, FileFacts)>(imported.Failure);
        }

        OperationResult<FileFacts> inspected =
            await _fileInspector.InspectAsync(_workspace.ResolveAbsolute(imported.Value), cancellationToken);

        if (inspected.IsFailure)
        {
            return OperationResult.Fail<(WorkspaceFileRef, FileFacts)>(inspected.Failure);
        }

        OperationFailure? refusal = RefuseUnacceptableInput(inspected.Value, FileNameOf(sourceAbsolutePath));
        return refusal is not null
            ? OperationResult.Fail<(WorkspaceFileRef, FileFacts)>(refusal)
            : OperationResult.Ok((imported.Value, inspected.Value));
    }

    /// <summary>
    /// Whether these established facts describe a file PrintFlow Studio accepts as an input, and
    /// if not, exactly why (Jira 11201, 11601; MVP design §9.2).
    /// </summary>
    /// <remarks>
    /// Applied to the <i>managed copy's</i> facts — the same single read that produces the hash a
    /// Revision is bound to — so acceptance is decided about the bytes the session would actually
    /// work on, from their magic bytes rather than from a file name anyone can rename.
    /// <para>
    /// Two failures rather than one, because they are two different situations for the operator:
    /// <see cref="FailureCode.SourceFormatUnsupported"/> means "choose a different kind of file",
    /// and <see cref="FailureCode.SourceImageUnreadable"/> means "this file is damaged". Both
    /// carry the detected format and the operator's own file name as structured context, so the
    /// screen can say which format it found without a second opinion about the file — the shell
    /// never re-inspects and never keeps a format list of its own.
    /// </para>
    /// <para>
    /// This refuses; it does not clean up. An import refused here follows exactly the same path
    /// as every other import failure — <see cref="FailImportAsync"/> records the failed attempt
    /// and its structured evidence, so a refusal is explainable through Error Details rather
    /// than vanishing (SCRUM-11120). No Revision and no <c>InputSnapshot</c> are created, so
    /// nothing downstream can consume a file that was never accepted.
    /// </para>
    /// </remarks>
    private static OperationFailure? RefuseUnacceptableInput(FileFacts facts, string sourceFileName)
    {
        Dictionary<string, string> context = new(2)
        {
            ["sourceFormat"] = facts.Format.ToString(),
            ["sourceFileName"] = sourceFileName,
        };

        if (!SupportedInputFormats.IsSupported(facts.Format))
        {
            return OperationFailure.Create(
                FailureCode.SourceFormatUnsupported,
                $"'{sourceFileName}' was detected as {facts.Format} from its magic bytes, which is not an " +
                "accepted PrintFlow Studio input. No Revision and no InputSnapshot were created.",
                isRetryable: false,
                context: context);
        }

        if (SupportedInputFormats.IsDecodedAtImport(facts.Format) &&
            (facts.PixelWidth is null || facts.PixelHeight is null))
        {
            return OperationFailure.Create(
                FailureCode.SourceImageUnreadable,
                $"'{sourceFileName}' is a {facts.Format} container that carries no readable image. No " +
                "Revision and no InputSnapshot were created.",
                isRetryable: false,
                context: context);
        }

        return null;
    }

    private async Task<OperationResult<SessionView>> FailImportAsync(
        ProcessingSession session, WorkflowSnapshot stateAfterStart, CommandContext context,
        ProcessingAttempt runningAttempt, OperationFailure failure, CancellationToken cancellationToken)
    {
        failure = FailureEvidence.ForAttempt(failure, runningAttempt.Id);
        WorkflowCommand.System.AttemptFailed failedCommand = new(runningAttempt.Id, StepKind.Import, failure);
        WorkflowTransition failedTransition = _engine.Apply(stateAfterStart, failedCommand, context);
        if (failedTransition.IsRejected)
        {
            return OperationResult.Fail<SessionView>(MapRejection(failedTransition.Rejection!));
        }

        ProcessingAttempt failedAttempt = runningAttempt.Fail(failure, context.NowUtc);
        ProcessingSession updatedSession = MergeSession(session, failedTransition.State, failedTransition.Effects, context.NowUtc);

        SessionMutation mutation = new(
            updatedSession, failedTransition.State.Steps, [], [], [failedAttempt], [], [], null, null)
        {
            NewAutomationLog = [RecordAutomationStop(session.Id, StepKind.Import, failure, context.NowUtc)],
        };

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
        List<RevisionReviewStateChange> revisionReviewStateChanges = [];
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

                    if (review.SubjectKind == ReviewSubjectKind.Revision)
                    {
                        revisionReviewStateChanges.Add(new RevisionReviewStateChange(
                            RevisionId.From(review.SubjectId),
                            review.ReviewedHash,
                            review.IsApproved ? ReviewState.Approved : ReviewState.Rejected));
                    }

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
            RevisionReviewStateChanges = revisionReviewStateChanges,
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
            BackgroundRemovalAuthority = newSnapshot.BackgroundRemovalAuthority,

            // Carried across with the dimensions they belong to, so the pair, its reading and the
            // plan behind it are always written and cleared together (Epic 11400 Part B1A.2A §4).
            DimensionSemantics = newSnapshot.DimensionSemantics,
            PrintPreparationPlan = newSnapshot.PrintPreparationPlan,

            // And the flexible-size decision with them, enlargement authority included. The
            // snapshot is the authority for all five: the engine clears them together on
            // ReturnToStep and AddAnotherSize, and a merge that kept one behind would leave a
            // confirmation attached to a size nobody chose (Part B1A.2D §31, §32).
            SizeSelection = newSnapshot.SizeSelection,
            TargetEdgePlan = newSnapshot.TargetEdgePlan,
            EnlargementAuthority = newSnapshot.EnlargementAuthority,
        };

        foreach (WorkflowEffect effect in effects)
        {
            updated = effect switch
            {
                WorkflowEffect.MarkSessionCompleted c => updated with { CompletedAtUtc = c.AtUtc },
                WorkflowEffect.MarkSessionHandedOff h => updated with { HandedOffAtUtc = h.AtUtc, HandOffReason = h.Reason },
                WorkflowEffect.MarkSessionAbandoned a => updated with { AbandonedAtUtc = a.AtUtc, AbandonReason = a.Reason },

                // Re-entry clears the session-level handoff record because those two fields
                // answer "is this session handed off right now", and it no longer is. The
                // history of the takeover lives on the closed attempt row, which is never
                // rewritten (Epic 11300 Part D2A §15, §22).
                WorkflowEffect.MarkSessionReenteredAutomation => updated with
                {
                    HandedOffAtUtc = null,
                    HandOffReason = null,
                },

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

    private OperationFailure WithExpectedOutput(OperationFailure failure, WorkspaceFileRef expectedOutput) =>
        FailureEvidence.WithExpectedOutputPath(failure, _workspace.ResolveAbsolute(expectedOutput));

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
        AdapterKind.Pdf => _pdf.AdapterId,
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
        AdapterKind.Pdf => _pdf.Mode,
        _ => throw new InvalidOperationException(
            $"Adapter kind '{kind}' is not adapter-backed; it has no execution mode to gate."),
    };

    private static OperationFailure MapRejection(CommandRejection rejection) =>
        OperationFailure.Create(
            FailureCode.PreconditionNotMet, rejection.ToString(), isRetryable: false);
}
