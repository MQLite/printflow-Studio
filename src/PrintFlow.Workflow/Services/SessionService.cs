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
using System.Collections.Concurrent;

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
    private readonly ITrimProcessor _trim;
    private readonly IManualCropProcessor _manualCrop;
    private readonly IWorkstationPresetProvider _presetProvider;
    private readonly IEnvironmentGate _environmentGate;
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
    /// Opaque, single-use handles for enlargement warnings this service returned to a screen.
    /// The shell sees only the Guid; the exact Revision/hash/target command stays here.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, EnlargementOffer> _enlargementOffers = new();

    private sealed record EnlargementOffer(
        SessionId SessionId, WorkflowCommand.AuthoriseEnlargement Command);

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
        TimeProvider timeProvider)
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
        _trim = trim;
        _manualCrop = manualCrop;
        _presetProvider = presetProvider;
        _environmentGate = environmentGate;
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

        if (established.IsFailure)
        {
            return await FailImportAsync(session, started.State, context, runningAttempt, established.Failure, cancellationToken);
        }

        (WorkspaceFileRef importedSource, FileFacts sourceFacts) = established.Value;

        RevisionId revisionId = RevisionId.From(_idGenerator.NewId());
        Revision rootRevision = Revision.Create(
            revisionId, id, null, OperationKind.Import, importedSource, sourceFacts, context.NowUtc);

        WorkflowCommand.System.AttemptSucceeded succeeded = new(
            context.NewAttemptId, StepKind.Import, revisionId, sourceFacts.Sha256);
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
        if (!_enlargementOffers.TryRemove(enlargementOfferId, out EnlargementOffer? offer) ||
            offer.SessionId != id)
        {
            return OperationResult.Fail<SessionView>(
                FailureCode.PreconditionNotMet,
                "That enlargement offer is no longer current. Review the refreshed size before continuing.");
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

                    // The configured recommendation becomes a fit box, and FitWithinBounds owns it
                    // from there. A maximum long edge is the square box of that side: fitting
                    // proportionally inside one constrains whichever source edge is longer, which
                    // is what a long-edge limit means. No second limiting-edge rule exists (§17).
                    PrintDimensions limits = recommendation.Value.AsFitBounds();
                    return OperationResult.Ok(new RecordedSize(
                        limits,
                        FlexibleSizeSelection.PresetFit(recommendation.Value),
                        PrintPreparationPlan.For(
                            source.RevisionId, source.Sha256, source.PixelWidth, source.PixelHeight,
                            limits),
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
        IReadOnlyList<ProcessingAttempt> attempts)
    {
        Guid? enlargementOfferId = null;
        if (state.UsableTargetEdgePlan is { RequiresEnlargementAuthority: true } offered &&
            state.NeedsEnlargementAuthority)
        {
            enlargementOfferId = Guid.NewGuid();
            _enlargementOffers[enlargementOfferId.Value] = new EnlargementOffer(
                state.SessionId,
                new WorkflowCommand.AuthoriseEnlargement(
                    offered.SourceRevisionId,
                    offered.SourceSha256,
                    offered.Projection.SelectedTargetEdge,
                    offered.Projection.RequestedMillimetres));
        }

        return OperationResult.Ok(SessionView.From(
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
            enlargementOfferId));
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
        OperationResult<(WorkspaceFileRef File, FileFacts Facts, string? Notes)> promoted =
            await InspectAsync(approved, cancellationToken);
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
            aggregate.Session.Id, runningAttempt.Id, work.Step, definition.IsAdapterBacked);

        OperationResult<(WorkspaceFileRef Output, FileFacts Facts, string? AdapterNotes)> produced;
        try
        {
            produced = await PerformStepWorkAsync(
                afterStart, started.State, definition, work, runningAttempt, context, stop, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            produced = OperationResult.Fail<(WorkspaceFileRef, FileFacts, string?)>(
                OperationFailure.Create(
                    FailureCode.Cancelled,
                    "PrintFlow orchestration was cancelled after the attempt started. No further " +
                    "adapter input was requested; the external application may still be running.",
                    isRetryable: true,
                    context: new Dictionary<string, string>
                    {
                        ["attemptId"] = runningAttempt.Id.ToString(),
                        ["step"] = work.Step.ToString(),
                        ["retainedExternalState"] = definition.IsAdapterBacked ? "unknown" : "none",
                    }));
        }
        finally
        {
            // Unregistered whatever happened, so a later Stop against a finished run is refused
            // rather than setting a flag nothing will ever read.
            _runs.End(runningAttempt.Id);
        }

        // §15 and §16, in the order the code has to take them. A Stop that arrives while the
        // work is already finishing does not get to undo it: an adapter that returned a
        // validated output has produced a real file, and the success transaction below runs to
        // completion. The requested stop is honoured afterwards, by ending the session's
        // automated progression — never by rewriting what the attempt did.
        if (produced.IsSuccess)
        {
            return await CompleteProducingStepAsync(
                aggregate, afterStart, started, context, work, runningAttempt, produced.Value, stop,
                cancellationToken);
        }

        if (stop.RequestedMode is { } stopped)
        {
            // The operator asked for this, so it is recorded as a stop rather than as a
            // failure — including when the run ended on some other adapter failure while
            // stopping. Both facts survive: the mode and retained external state go into the
            // structured context, and whatever the adapter reported goes into the detail (§29).
            return await StopAttemptAsync(
                afterStart, started.State, context, work.Step, runningAttempt, definition,
                stopped, stop, produced.Failure);
        }

        // Cancellation is the one failure whose caller token cannot be used to close the
        // attempt: it is already cancelled. The adapter has stopped receiving input, while
        // this short metadata transaction truthfully ends the attempt and releases the
        // global automation lock. A process crash before this commit is still covered by
        // startup Running -> Interrupted recovery.
        CancellationToken closingToken = produced.Failure.Code == FailureCode.Cancelled
            ? CancellationToken.None
            : cancellationToken;
        return await FailAttemptAsync(
            afterStart, started.State, context, work.Step, runningAttempt, produced.Failure, closingToken);
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
        (WorkspaceFileRef Output, FileFacts Facts, string? AdapterNotes) produced,
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
            revisionId, context.NowUtc, produced.AdapterNotes);
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
    private async Task<OperationResult<(WorkspaceFileRef Output, FileFacts Facts, string? AdapterNotes)>> PerformStepWorkAsync(
        SessionAggregate aggregate, WorkflowSnapshot state, StepDefinition definition,
        ProducingWork work, ProcessingAttempt attempt, CommandContext context,
        IAutomationStopSignal stop, CancellationToken cancellationToken)
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
                return OperationResult.Fail<(WorkspaceFileRef, FileFacts, string?)>(
                    FailureCode.PreconditionNotMet, "Nothing to promote: no upstream Revision.");
            }

            OperationResult<NamingPatternSet> patterns = _presetProvider.GetNamingPatterns();
            if (patterns.IsFailure)
            {
                return OperationResult.Fail<(WorkspaceFileRef, FileFacts, string?)>(patterns.Failure);
            }

            string proposedName = aggregate.Session.OutputName.Value + ".png";
            OperationResult<WorkspaceFileRef> reserved =
                _workspace.ReserveOutput(session, WorkspaceArea.Approved, proposedName, patterns.Value);
            if (reserved.IsFailure)
            {
                return OperationResult.Fail<(WorkspaceFileRef, FileFacts, string?)>(reserved.Failure);
            }

            OperationResult<Unit> written = await _workspace.WriteReservedAsync(reserved.Value, sourceRef, cancellationToken);
            if (written.IsFailure)
            {
                return OperationResult.Fail<(WorkspaceFileRef, FileFacts, string?)>(written.Failure);
            }

            return await InspectAsync(reserved.Value, cancellationToken);
        }

        if (input is not { } upstreamRef)
        {
            return OperationResult.Fail<(WorkspaceFileRef, FileFacts, string?)>(
                FailureCode.PreconditionNotMet, $"Step {work.Step} has no upstream Revision to work from.");
        }

        OperationResult<WorkspaceFileRef> workingCopy =
            await _workspace.CreateWorkingCopyAsync(session, context.NewAttemptId, upstreamRef, cancellationToken);
        if (workingCopy.IsFailure)
        {
            return OperationResult.Fail<(WorkspaceFileRef, FileFacts, string?)>(workingCopy.Failure);
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
                    return OperationResult.Fail<(WorkspaceFileRef, FileFacts, string?)>(meituPatterns.Failure);
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
                    return OperationResult.Fail<(WorkspaceFileRef, FileFacts, string?)>(producedName.Failure);
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
                        return OperationResult.Fail<(WorkspaceFileRef, FileFacts, string?)>(
                            FailureCode.PreconditionNotMet,
                            "Background removal reached the adapter without a recorded reviewed-content authority. " +
                            "No request is built: a missing product decision is not something to guess at.");
                    }

                    decision = authority.Decision;
                }

                OperationResult<AdapterOutput> result = await _meitu.ProcessAsync(
                    new MeituRequest(
                        workingCopy.Value,
                        operation,
                        decision,
                        ParentDirOf(workingCopy.Value),
                        SiblingOf(workingCopy.Value, producedName.Value))
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
                    return OperationResult.Fail<(WorkspaceFileRef, FileFacts, string?)>(result.Failure);
                }

                return await InspectAsync(
                    result.Value.ProducedFile, cancellationToken, result.Value.AdapterNotes);
            }

            case AdapterKind.Photoshop:
            {
                if (state.Dimensions is not { } dimensions)
                {
                    return OperationResult.Fail<(WorkspaceFileRef, FileFacts, string?)>(
                        FailureCode.PreconditionNotMet, "Photoshop output requires confirmed print dimensions.");
                }

                if (state.WhiteUnderbaseBranch is not { } branch)
                {
                    return OperationResult.Fail<(WorkspaceFileRef, FileFacts, string?)>(
                        FailureCode.PreconditionNotMet, "Photoshop output requires an explicit white-underbase branch.");
                }

                // The plan the attempt row already recorded, never a fresh read of the session
                // and never a recalculation here. The engine refused to start the step without a
                // usable plan, so the value is one that was bound to the exact bytes about to be
                // opened; this guard is what makes that a property of this code rather than a
                // promise about a caller elsewhere (Epic 11400 Part B1A.2A §12, §17).
                if (attempt.Preparation is not { } preparation)
                {
                    return OperationResult.Fail<(WorkspaceFileRef, FileFacts, string?)>(
                        FailureCode.PreconditionNotMet,
                        "Photoshop output reached the adapter without a recorded preparation. No request is " +
                        "built: which edge is written, and whether an enlargement was authorised, are not " +
                        "things to work out here.");
                }

                OperationResult<ProductionPresetRef> preset = _presetProvider.GetVerifiedPreset();
                if (preset.IsFailure)
                {
                    return OperationResult.Fail<(WorkspaceFileRef, FileFacts, string?)>(preset.Failure);
                }

                OperationResult<NamingPatternSet> patterns = _presetProvider.GetNamingPatterns();
                if (patterns.IsFailure)
                {
                    return OperationResult.Fail<(WorkspaceFileRef, FileFacts, string?)>(patterns.Failure);
                }

                OperationResult<string> tiffName = OutputFileNaming.BuildProposedFileName(
                    NamingArtifactKind.ProductionTiff, aggregate.Session.OutputName, patterns.Value, dimensions.WidthMm);
                if (tiffName.IsFailure)
                {
                    return OperationResult.Fail<(WorkspaceFileRef, FileFacts, string?)>(tiffName.Failure);
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

                OperationResult<AdapterOutput> result = await _photoshop.GenerateAsync(
                    new PhotoshopRequest(
                        workingCopy.Value, dimensions, preparation, preset.Value, branch,
                        reserved.FileName, ParentDirOf(workingCopy.Value), reserved),
                    cancellationToken);
                if (result.IsFailure)
                {
                    return OperationResult.Fail<(WorkspaceFileRef, FileFacts, string?)>(result.Failure);
                }

                return await InspectAsync(
                    result.Value.ProducedFile, cancellationToken, result.Value.AdapterNotes);
            }

            case AdapterKind.Internal:
            {
                // Checked rather than assumed: AdapterKind.Internal means "deterministic
                // in-process pixel work", and Trim is only the first such step. A future
                // internal step routed here by accident would silently be trimmed.
                if (definition.Kind != StepKind.Trim)
                {
                    return OperationResult.Fail<(WorkspaceFileRef, FileFacts, string?)>(
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
                        ? OperationResult.Fail<(WorkspaceFileRef, FileFacts, string?)>(cropped.Failure)
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
                    return OperationResult.Fail<(WorkspaceFileRef, FileFacts, string?)>(trimmed.Failure);
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
                    return OperationResult.Fail<(WorkspaceFileRef, FileFacts, string?)>(
                        OperationFailure.Create(
                            FailureCode.ManualCropRequired,
                            result.ManualCropReason ?? "No usable alpha content was found.",
                            isRetryable: false));
                }

                return await InspectAsync(result.ProducedFile!.Value, cancellationToken);
            }

            default:
                return OperationResult.Fail<(WorkspaceFileRef, FileFacts, string?)>(
                    FailureCode.PreconditionNotMet, $"Unsupported adapter kind '{work.Adapter}'.");
        }
    }

    private OperationResult<PrintOutput> BuildPrintOutput(
        SessionId sessionId, RevisionId revisionId, WorkflowSnapshot stateBeforeFinish,
        (WorkspaceFileRef Output, FileFacts Facts, string? AdapterNotes) work, CommandContext context)
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

    private async Task<OperationResult<(WorkspaceFileRef, FileFacts, string?)>> InspectAsync(
        WorkspaceFileRef file, CancellationToken cancellationToken, string? adapterNotes = null)
    {
        string absolute = _workspace.ResolveAbsolute(file);
        OperationResult<FileFacts> inspected = await _fileInspector.InspectAsync(absolute, cancellationToken);
        return inspected.IsSuccess
            ? OperationResult.Ok((file, inspected.Value, adapterNotes))
            : OperationResult.Fail<(WorkspaceFileRef, FileFacts, string?)>(inspected.Failure);
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

        OperationResult<Unit> committed = await _repository.CommitAsync(mutation, cancellationToken);
        return committed.IsSuccess
            ? OperationResult.Fail<SessionView>(failure)
            : OperationResult.Fail<SessionView>(committed.Failure);
    }

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

        WorkflowCommand.System.AttemptCancelled cancelled = new(runningAttempt.Id, step, failure);
        WorkflowTransition stopped = _engine.Apply(stateAfterStart, cancelled, context);
        if (stopped.IsRejected)
        {
            return OperationResult.Fail<SessionView>(MapRejection(stopped.Rejection!));
        }

        ProcessingAttempt cancelledAttempt = runningAttempt.Cancel(
            failure, context.NowUtc, DescribeRetainedState(mode, stop, definition.IsAdapterBacked));

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
            aggregate, updatedSession, state, effects, context, upsertAttempts: [cancelledAttempt]);

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

        return inspected.IsFailure
            ? OperationResult.Fail<(WorkspaceFileRef, FileFacts)>(inspected.Failure)
            : OperationResult.Ok((imported.Value, inspected.Value));
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
