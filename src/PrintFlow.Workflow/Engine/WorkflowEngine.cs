using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Reviews;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Trimming;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Definitions;
using PrintFlow.Workflow.Effects;

namespace PrintFlow.Workflow.Engine;

/// <summary>
/// The pure workflow reducer (Jira 11104).
/// </summary>
/// <remarks>
/// Structure of every command handler, in order:
///
/// <list type="number">
///   <item>session-level gate — is the session in a state that permits this command at all;</item>
///   <item>table lookup — does <see cref="TransitionTable"/> allow this command in this step state;</item>
///   <item>definition guards — is the step current, skippable, review-bearing, and are its
///         preconditions satisfied;</item>
///   <item>new state plus effects.</item>
/// </list>
///
/// Nothing falls through: a command that is not explicitly allowed is explicitly rejected
/// with a code, which is what the exhaustiveness test verifies.
/// </remarks>
public sealed class WorkflowEngine : IWorkflowEngine
{
    /// <summary>A shared instance; the engine holds no state, so one is enough.</summary>
    public static readonly WorkflowEngine Instance = new();

    /// <inheritdoc />
    public WorkflowTransition Apply(WorkflowSnapshot state, WorkflowCommand command, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        return command switch
        {
            WorkflowCommand.SelectWorkflow c => SelectWorkflow(state, c, context),
            WorkflowCommand.SetOutputName c => SetOutputName(state, c),
            WorkflowCommand.ConfirmOriginal c => ConfirmOriginal(state, c, context),
            WorkflowCommand.StartStep c => StartStep(state, c, context),
            WorkflowCommand.Approve c => Approve(state, c, context),
            WorkflowCommand.Reject c => Reject(state, c, context),
            WorkflowCommand.Retry c => Retry(state, c, context),
            WorkflowCommand.SubmitManualCrop c => SubmitManualCrop(state, c, context),
            WorkflowCommand.Skip c => Skip(state, c, context),
            WorkflowCommand.HandOff c => HandOff(state, c, context),
            WorkflowCommand.SetPrintDimensions c => SetPrintDimensions(state, c, context),
            WorkflowCommand.SetPresetFitSize c => SetPresetFitSize(state, c, context),
            WorkflowCommand.SetCustomTargetEdgeSize c => SetCustomTargetEdgeSize(state, c, context),
            WorkflowCommand.AuthoriseEnlargement c => AuthoriseEnlargement(state, c),
            WorkflowCommand.SelectWhiteUnderbaseBranch c => SelectWhiteUnderbaseBranch(state, c),
            WorkflowCommand.SetTrimParameters c => SetTrimParameters(state, c),
            WorkflowCommand.SetBackgroundRemovalDecision c => SetBackgroundRemovalDecision(state, c),
            WorkflowCommand.ReturnToStep c => ReturnToStep(state, c, context),
            WorkflowCommand.Complete => Complete(state, context),
            WorkflowCommand.AddAnotherSize => AddAnotherSize(state, context),
            WorkflowCommand.AbandonSession c => AbandonSession(state, c, context),
            WorkflowCommand.System.AttemptSucceeded c => AttemptSucceeded(state, c, context),
            WorkflowCommand.System.AttemptFailed c => AttemptFailed(state, c, context),
            WorkflowCommand.System.AttemptInterrupted c => AttemptInterrupted(state, c, context),
            WorkflowCommand.System.AttemptCancelled c => AttemptCancelled(state, c, context),
            WorkflowCommand.ReenterAutomation => ReenterAutomation(state, context),
            _ => WorkflowTransition.Rejected(
                RejectionCode.CommandNotApplicable,
                $"The engine has no handler for command '{command.Kind}'."),
        };
    }

    /// <inheritdoc />
    public IReadOnlyList<CommandKind> AvailableCommands(WorkflowSnapshot state)
    {
        ArgumentNullException.ThrowIfNull(state);

        // Probing with a placeholder context is safe: no handler reads the context before
        // deciding legality, and a probe produces no effects because nothing is applied.
        CommandContext probe = new(
            DateTimeOffset.UnixEpoch, CommandContext.UnknownOperator, default, default);

        List<CommandKind> available = [];
        foreach (CommandKind kind in TransitionTable.AllCommands)
        {
            WorkflowCommand? probeCommand = BuildProbe(state, kind);
            if (probeCommand is null)
            {
                continue;
            }

            if (Apply(state, probeCommand, probe).IsAccepted)
            {
                available.Add(kind);
            }
        }

        return available;
    }

    /// <inheritdoc />
    public IReadOnlyList<StepKind> AvailableReturnTargets(WorkflowSnapshot state)
    {
        ArgumentNullException.ThrowIfNull(state);

        // The same placeholder context AvailableCommands uses, and safe for the same reason:
        // nothing is applied, so the reset timestamps a probe would compute are discarded with
        // the transition that produced them.
        CommandContext probe = new(
            DateTimeOffset.UnixEpoch, CommandContext.UnknownOperator, default, default);

        List<StepKind> targets = [];
        foreach (StepDefinition step in state.Definition.Steps)
        {
            // The real command, not a summary of what it would say. Whatever ReturnToStep's
            // preconditions are today or become later, this list is exactly the set that
            // satisfies them.
            if (Apply(state, new WorkflowCommand.ReturnToStep(step.Kind), probe).IsAccepted)
            {
                targets.Add(step.Kind);
            }
        }

        return targets;
    }

    // ---------------------------------------------------------------------------------
    // Session-scoped commands
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Changing the workflow is legal only while the session has produced nothing derived.
    /// After that the operator must end the session and re-import (MVP design §6.1).
    /// </summary>
    private static WorkflowTransition SelectWorkflow(
        WorkflowSnapshot state, WorkflowCommand.SelectWorkflow command, CommandContext context)
    {
        if (!SessionStateRules.AllowsProgress(state.SessionState))
        {
            return NotActive(state, nameof(WorkflowCommand.SelectWorkflow));
        }

        if (state.HasDerivedRevision)
        {
            return WorkflowTransition.Rejected(
                RejectionCode.WorkflowLocked,
                "The workflow cannot be changed once a derived Revision exists. End the session and re-import.");
        }

        if (command.Type == state.WorkflowType)
        {
            return WorkflowTransition.Accepted(state);
        }

        // Re-shape the session onto the new definition. Import may already have completed,
        // so its result is carried across rather than discarded.
        WorkflowSnapshot reshaped = WorkflowSnapshot.Create(
            state.SessionId, command.Type, state.OutputName, context.NowUtc)
            with { RequiresPsdPreparation = state.RequiresPsdPreparation, RequiresPdfPreparation = state.RequiresPdfPreparation };

        SessionStep? importBefore = state.Step(StepKind.Import);
        if (importBefore is not null && importBefore.CurrentRevisionId is not null)
        {
            SessionStep importAfter = reshaped.Steps[0] with
            {
                State = importBefore.State,
                CurrentRevisionId = importBefore.CurrentRevisionId,
                CurrentRevisionSha256 = importBefore.CurrentRevisionSha256,
                AttemptCount = importBefore.AttemptCount,
                EnteredStateAtUtc = importBefore.EnteredStateAtUtc,
            };
            reshaped = reshaped.WithStep(importAfter);
        }

        return WorkflowTransition.Accepted(
            reshaped,
            new WorkflowEffect.PersistWorkflowSelection(command.Type));
    }

    private static WorkflowTransition SetOutputName(
        WorkflowSnapshot state, WorkflowCommand.SetOutputName command)
    {
        if (!SessionStateRules.AllowsProgress(state.SessionState))
        {
            return NotActive(state, nameof(WorkflowCommand.SetOutputName));
        }

        if (string.IsNullOrWhiteSpace(command.Name.Value))
        {
            return WorkflowTransition.Rejected(
                RejectionCode.InvalidPayload, "An output name cannot be empty.");
        }

        return WorkflowTransition.Accepted(
            state with { OutputName = command.Name },
            new WorkflowEffect.PersistOutputName(command.Name));
    }

    /// <summary>
    /// Records the operator's explicit white-underbase decision.
    /// </summary>
    /// <remarks>
    /// There is no default branch and the engine never infers one from image content
    /// (MVP design §12). Photoshop output cannot start until this has been supplied.
    /// </remarks>
    private static WorkflowTransition SelectWhiteUnderbaseBranch(
        WorkflowSnapshot state, WorkflowCommand.SelectWhiteUnderbaseBranch command)
    {
        if (!SessionStateRules.AllowsProgress(state.SessionState))
        {
            return NotActive(state, nameof(WorkflowCommand.SelectWhiteUnderbaseBranch));
        }

        if (!state.Definition.Contains(StepKind.PhotoshopOutput))
        {
            return WorkflowTransition.Rejected(
                RejectionCode.CommandNotApplicable,
                $"Workflow {state.WorkflowType} produces no TIFF, so it has no white-underbase branch.");
        }

        if (string.IsNullOrWhiteSpace(command.Justification))
        {
            return WorkflowTransition.Rejected(
                RejectionCode.InvalidPayload,
                "A white-underbase branch must carry the operator's justification; it is a review decision, not a default.");
        }

        return WorkflowTransition.Accepted(
            state with { WhiteUnderbaseBranch = command.Branch },
            new WorkflowEffect.PersistWhiteUnderbaseBranch(command.Branch, command.Justification.Trim()));
    }

    /// <summary>
    /// Records the margin the next deterministic Trim attempt will run with
    /// (Epic 11200 Part C3 §9, §13).
    /// </summary>
    /// <remarks>
    /// Legal only while Trim is the current step and is <i>between</i> attempts, which is the
    /// state-machine half of "only when the automatic trim is about to run" (§9). Recording a
    /// margin while an attempt is Processing would change what a running trim claimed to use,
    /// and recording one against a result already awaiting review would leave the attempt row
    /// describing one margin and the session another — the exact drift §15 exists to prevent.
    /// <para>
    /// The historical half — a step that failed with <c>ManualCropRequired</c> is on the manual
    /// path, where no margin can help — is <c>ManualCropEligibility</c>'s, because it is a
    /// question about attempt history that a snapshot deliberately cannot answer. Both run:
    /// this decides whether the command is legal, and <c>SessionView</c> decides whether the
    /// control is offered (§17).
    /// </para>
    /// <para>
    /// Accepting one starts nothing. No attempt, no file, no Revision — it records a decision,
    /// and <c>StartStep</c> reads it later.
    /// </para>
    /// </remarks>
    private static WorkflowTransition SetTrimParameters(
        WorkflowSnapshot state, WorkflowCommand.SetTrimParameters command)
    {
        if (!SessionStateRules.AllowsProgress(state.SessionState))
        {
            return NotActive(state, nameof(WorkflowCommand.SetTrimParameters));
        }

        if (!state.Definition.Contains(StepKind.Trim))
        {
            return NotInWorkflow(state, StepKind.Trim);
        }

        if (state.CurrentStep is not { Step: StepKind.Trim } trim)
        {
            return WorkflowTransition.Rejected(
                RejectionCode.PreconditionNotMet,
                "A trim margin can only be set while Trim is the current step; it describes the run that is about to happen.");
        }

        if (!AcceptsTrimParameters(trim.State))
        {
            return WorkflowTransition.Rejected(
                RejectionCode.PreconditionNotMet,
                $"Trim is {trim.State}; a margin is set between attempts, not during one or after a result has been produced.");
        }

        return WorkflowTransition.Accepted(
            state with { TrimMargin = command.Margin },
            new WorkflowEffect.PersistTrimParameters(command.Margin));
    }

    /// <summary>
    /// Records the reviewed-content authority the next Background Removal attempt may run
    /// under (Epic 11300 Part C2B1 §5, §6).
    /// </summary>
    /// <remarks>
    /// Every guard here exists to keep one sentence true: the decision means "automatic
    /// selection is authorised for <i>this</i> reviewed content", never "automatic selection is
    /// enabled for this session" (§4).
    /// <list type="bullet">
    ///   <item>The session must be active, and the workflow must actually contain the step.</item>
    ///   <item>The decision must be an explicit authorisation. <c>Unspecified</c> is the absence
    ///         of a decision, so a command carrying it is refused rather than recorded (§7).</item>
    ///   <item>Background Removal must be the current step and <i>between</i> attempts, the same
    ///         window <see cref="SetTrimParameters"/> uses and for the same reason: authorising
    ///         content mid-attempt, or after a result is already awaiting review, would leave the
    ///         session claiming an authority the attempt row does not describe (§11).</item>
    ///   <item>The supplied Revision and hash must be exactly what Background Removal will
    ///         consume. This is the whole of §6 and §8: authority granted for Revision A cannot
    ///         be recorded against a session whose upstream has since become B, and a hash that
    ///         no longer matches means the operator decided about bytes that are gone (§24).</item>
    /// </list>
    /// <para>
    /// Accepting one starts nothing. No attempt, no working copy, no adapter call, no Revision.
    /// </para>
    /// </remarks>
    private static WorkflowTransition SetBackgroundRemovalDecision(
        WorkflowSnapshot state, WorkflowCommand.SetBackgroundRemovalDecision command)
    {
        if (!SessionStateRules.AllowsProgress(state.SessionState))
        {
            return NotActive(state, nameof(WorkflowCommand.SetBackgroundRemovalDecision));
        }

        if (!state.Definition.Contains(StepKind.BackgroundRemoval))
        {
            return NotInWorkflow(state, StepKind.BackgroundRemoval);
        }

        if (command.Decision != BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent)
        {
            return WorkflowTransition.Rejected(
                RejectionCode.InvalidPayload,
                $"'{command.Decision}' is not an explicit authorisation. Unspecified is the absence of a " +
                "decision, not a value that can be recorded.");
        }

        if (state.CurrentStep is not { Step: StepKind.BackgroundRemoval } backgroundRemoval)
        {
            return WorkflowTransition.Rejected(
                RejectionCode.PreconditionNotMet,
                "A background-removal decision can only be set while BackgroundRemoval is the current step; " +
                "it authorises the run that is about to happen.");
        }

        if (!AcceptsBackgroundRemovalDecision(backgroundRemoval.State))
        {
            return WorkflowTransition.Rejected(
                RejectionCode.PreconditionNotMet,
                $"BackgroundRemoval is {backgroundRemoval.State}; reviewed content is authorised between " +
                "attempts, not during one or after a result has been produced.");
        }

        if (state.UpstreamResultOf(StepKind.BackgroundRemoval) is not { } upstream)
        {
            return WorkflowTransition.Rejected(
                RejectionCode.PreconditionNotMet,
                "BackgroundRemoval has no validated upstream result, so there is no reviewed content to authorise.");
        }

        if (upstream.Id != command.ReviewedRevisionId)
        {
            return WorkflowTransition.Rejected(
                RejectionCode.PreconditionNotMet,
                $"Revision {command.ReviewedRevisionId} is not what BackgroundRemoval will consume ({upstream.Id}); " +
                "authority is granted for reviewed content, never transferred to other content.");
        }

        if (!upstream.Sha256.Equals(command.DisplayedHash))
        {
            return WorkflowTransition.Rejected(
                RejectionCode.PreconditionNotMet,
                $"The displayed hash {command.DisplayedHash.ShortForm} does not match the current upstream result " +
                $"{upstream.Sha256.ShortForm}; the content reviewed is no longer the content on offer.");
        }

        BackgroundRemovalAuthority authority = BackgroundRemovalAuthority.For(
            command.Decision, upstream.Id, upstream.Sha256);

        return WorkflowTransition.Accepted(
            state with { BackgroundRemovalAuthority = authority },
            new WorkflowEffect.PersistBackgroundRemovalDecision(authority));
    }

    /// <summary>The BackgroundRemoval step states in which a new attempt is the next action.</summary>
    /// <remarks>
    /// Written out rather than expressed as "not Processing and not ReviewRequired", so a step
    /// state added later is excluded until someone decides it belongs — the same reasoning as
    /// <see cref="AcceptsTrimParameters"/>.
    /// </remarks>
    private static bool AcceptsBackgroundRemovalDecision(StepState state) => state is
        StepState.Waiting or StepState.RetryRequired or StepState.Failed or StepState.Interrupted;

    /// <summary>The Trim step states in which a new deterministic attempt is the next action.</summary>
    /// <remarks>
    /// Written out rather than expressed as "not Processing and not ReviewRequired", so a step
    /// state added later is excluded until someone decides it belongs.
    /// </remarks>
    private static bool AcceptsTrimParameters(StepState state) => state is
        StepState.Waiting or StepState.RetryRequired or StepState.Failed or StepState.Interrupted;

    /// <summary>
    /// Returns to an earlier step and invalidates everything derived from it.
    /// </summary>
    /// <remarks>
    /// The engine states the invalidation as an effect; the recursive descendant walk and the
    /// file moves belong to Tasks 11105 and 11108 (Epic 11100 plan §10.4).
    /// </remarks>
    private static WorkflowTransition ReturnToStep(
        WorkflowSnapshot state, WorkflowCommand.ReturnToStep command, CommandContext context)
    {
        if (!SessionStateRules.AllowsProgress(state.SessionState))
        {
            return NotActive(state, nameof(WorkflowCommand.ReturnToStep));
        }

        SessionStep? target = state.Step(command.Target);
        if (target is null)
        {
            return NotInWorkflow(state, command.Target);
        }

        SessionStep? current = state.CurrentStep;
        bool isUpstream = current is null || target.Ordinal < current.Ordinal;
        if (!isUpstream)
        {
            return WorkflowTransition.Rejected(
                RejectionCode.PreconditionNotMet,
                $"Step {command.Target} is not upstream of the current position; there is nothing to return to.");
        }

        List<SessionStep> steps = new(state.Steps.Count);
        foreach (SessionStep step in state.Steps)
        {
            steps.Add(step.Ordinal >= target.Ordinal ? step.Reset(context.NowUtc) : step);
        }

        // Print dimensions and the W1 branch are decisions attached to the run being
        // rewound, so they are cleared with it and must be made again explicitly. The
        // maximum-bound plan and its semantics marker go with them: the plan is what those
        // millimetres mean against a particular source, and leaving one behind without the other
        // would be a reading with nothing to read (Epic 11400 Part B1A.2A §4).
        bool clearsDimensions = state.Definition.IndexOf(StepKind.PrintDimensions) >= target.Ordinal;

        WorkflowSnapshot newState = state with
        {
            Steps = steps,
            Dimensions = clearsDimensions ? null : state.Dimensions,
            DimensionSemantics = clearsDimensions ? null : state.DimensionSemantics,
            PrintPreparationPlan = clearsDimensions ? null : state.PrintPreparationPlan,

            // The flexible-size decision goes with them, enlargement authority included. Returning
            // to the size step exists so the operator can choose a different target, and an
            // authority that survived that would be a confirmation of the old target still sitting
            // beside the new one (Epic 11400 Part B1A.2D §31).
            SizeSelection = clearsDimensions ? null : state.SizeSelection,
            TargetEdgePlan = clearsDimensions ? null : state.TargetEdgePlan,
            EnlargementAuthority = clearsDimensions ? null : state.EnlargementAuthority,
            WhiteUnderbaseBranch = clearsDimensions ? null : state.WhiteUnderbaseBranch,
            LatestApprovedRevisionId = LatestApprovedBefore(steps, target.Ordinal),
            HasDerivedRevision = HasDerivedRevision(steps),
        };

        List<WorkflowEffect> effects = [];
        if (target.CurrentRevisionId is RevisionId from)
        {
            effects.Add(new WorkflowEffect.InvalidateDescendants(from, InvalidationReason.SessionReset));
        }
        else if (state.UpstreamRevisionOf(command.Target) is RevisionId upstream)
        {
            effects.Add(new WorkflowEffect.InvalidateDescendants(upstream, InvalidationReason.UpstreamChanged));
        }

        effects.Add(new WorkflowEffect.ResetStepsFrom(command.Target));

        return WorkflowTransition.Accepted(newState, effects);
    }

    /// <summary>
    /// Completion requires every step finished and the terminal artefact approved.
    /// </summary>
    private static WorkflowTransition Complete(WorkflowSnapshot state, CommandContext context)
    {
        if (!SessionStateRules.AllowsProgress(state.SessionState))
        {
            return NotActive(state, nameof(WorkflowCommand.Complete));
        }

        SessionStep? unfinished = state.CurrentStep;
        if (unfinished is not null)
        {
            return WorkflowTransition.Rejected(
                RejectionCode.WorkflowNotComplete,
                $"Step {unfinished.Step} is {unfinished.State}; every step must be Approved or Skipped first.");
        }

        // The terminal step is the produced artefact. It is never skippable, so anything
        // other than Approved here means the artefact was not actually reviewed.
        StepKind terminalKind = state.Definition.Terminal.Kind;
        SessionStep terminal = state.Step(terminalKind)!;
        if (terminal.State != StepState.Approved)
        {
            return WorkflowTransition.Rejected(
                RejectionCode.WorkflowNotComplete,
                $"The terminal step {terminalKind} is {terminal.State}; it must be Approved.");
        }

        return WorkflowTransition.Accepted(
            state with { SessionState = SessionState.Completed },
            new WorkflowEffect.CleanupWorking(),
            new WorkflowEffect.ReleaseAutomationLock(),
            new WorkflowEffect.MarkSessionCompleted(context.NowUtc));
    }

    /// <summary>
    /// Reopens a completed production session to produce another output size.
    /// </summary>
    /// <remarks>
    /// The new output is a sibling derived from the same approved Revision, not a descendant
    /// of the existing one, so approved PrintOutputs are deliberately left untouched
    /// (Epic 11100 plan §10.4). The W1 branch is cleared: each output requires its own
    /// explicit decision.
    /// </remarks>
    private static WorkflowTransition AddAnotherSize(WorkflowSnapshot state, CommandContext context)
    {
        if (state.SessionState != SessionState.Completed)
        {
            return WorkflowTransition.Rejected(
                RejectionCode.PreconditionNotMet,
                $"AddAnotherSize applies to a Completed session; this one is {state.SessionState}.");
        }

        if (!state.Definition.Contains(StepKind.PhotoshopOutput))
        {
            return WorkflowTransition.Rejected(
                RejectionCode.CommandNotApplicable,
                $"Workflow {state.WorkflowType} produces no TIFF, so there is no additional size to add.");
        }

        RevisionId? source = state.UpstreamRevisionOf(StepKind.PrintDimensions);
        if (source is null)
        {
            return WorkflowTransition.Rejected(
                RejectionCode.PreconditionNotMet,
                "No approved source Revision is available to produce another size from.");
        }

        int dimensionsOrdinal = state.Definition.IndexOf(StepKind.PrintDimensions);
        List<SessionStep> steps = new(state.Steps.Count);
        foreach (SessionStep step in state.Steps)
        {
            steps.Add(step.Ordinal >= dimensionsOrdinal ? step.Reset(context.NowUtc) : step);
        }

        WorkflowSnapshot newState = state with
        {
            SessionState = SessionState.Active,
            Steps = steps,

            // Each additional size is its own decision, so the plan is cleared with the
            // dimensions it belongs to rather than carried forward. A plan retained here would
            // let the second output run on limits the operator recorded for the first
            // (Epic 11400 Part B1A.2A §4).
            Dimensions = null,
            DimensionSemantics = null,
            PrintPreparationPlan = null,

            // A new output size starts with no target, no override and no permission to enlarge.
            // Carrying an enlargement confirmation from the previous sibling would authorise a
            // second run nobody was asked about; the completed sibling keeps its own immutable
            // audit either way (Epic 11400 Part B1A.2D §32).
            SizeSelection = null,
            TargetEdgePlan = null,
            EnlargementAuthority = null,
            WhiteUnderbaseBranch = null,
        };

        return WorkflowTransition.Accepted(
            newState,
            new WorkflowEffect.BeginAdditionalOutput(source.Value));
    }

    private static WorkflowTransition AbandonSession(
        WorkflowSnapshot state, WorkflowCommand.AbandonSession command, CommandContext context)
    {
        if (!SessionStateRules.AllowsAbandon(state.SessionState))
        {
            return WorkflowTransition.Rejected(
                RejectionCode.PreconditionNotMet,
                $"A {state.SessionState} session cannot be abandoned.");
        }

        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            return WorkflowTransition.Rejected(
                RejectionCode.InvalidPayload, "Abandoning a session requires a reason.");
        }

        return WorkflowTransition.Accepted(
            state with { SessionState = SessionState.Abandoned },
            new WorkflowEffect.ReleaseAutomationLock(),
            new WorkflowEffect.MarkSessionAbandoned(context.NowUtc, command.Reason.Trim()));
    }

    // ---------------------------------------------------------------------------------
    // Step-scoped commands
    // ---------------------------------------------------------------------------------

    private static WorkflowTransition ConfirmOriginal(
        WorkflowSnapshot state, WorkflowCommand.ConfirmOriginal command, CommandContext context)
    {
        if (state.RequiresPsdPreparation || state.RequiresPdfPreparation)
        {
            return WorkflowTransition.Rejected(RejectionCode.PreconditionNotMet,
                "Prepare the source document and review its managed raster before continuing.");
        }
        StepResolution resolved = Resolve(state, StepKind.OriginalConfirmation, CommandKind.ConfirmOriginal);
        if (resolved.Rejection is not null)
        {
            return WorkflowTransition.Rejected(resolved.Rejection);
        }

        SessionStep step = resolved.Step!;
        StepDefinition definition = resolved.Definition!;

        List<WorkflowEffect> effects = [];

        // GENERATE_PRINT_TIFF treats this as a real design-readiness review, so it produces
        // a hash-bound decision rather than a bare acknowledgement.
        if (definition.RequiresReview)
        {
            RevisionId? subject = state.UpstreamRevisionOf(StepKind.OriginalConfirmation);
            SessionStep? importStep = state.Step(StepKind.Import);
            if (subject is null || importStep?.CurrentRevisionSha256 is not Sha256 hash)
            {
                return WorkflowTransition.Rejected(
                    RejectionCode.PreconditionNotMet,
                    "The imported original has not been validated, so it cannot be reviewed yet.");
            }

            effects.Add(new WorkflowEffect.RecordReview(
                context.NewReviewId,
                StepKind.OriginalConfirmation,
                ReviewSubjectKind.Revision,
                subject.Value.Value,
                hash,
                IsApproved: true,
                QuickReason: null,
                command.Notes));
        }

        SessionStep confirmed = step.WithState(StepState.Approved, context.NowUtc);
        return WorkflowTransition.Accepted(state.WithStep(confirmed), effects);
    }

    private static WorkflowTransition StartStep(
        WorkflowSnapshot state, WorkflowCommand.StartStep command, CommandContext context)
    {
        StepResolution resolved = Resolve(state, command.Step, CommandKind.StartStep);
        if (resolved.Rejection is not null)
        {
            return WorkflowTransition.Rejected(resolved.Rejection);
        }

        SessionStep step = resolved.Step!;
        StepDefinition definition = resolved.Definition!;

        if (!definition.ProducesRevision)
        {
            return WorkflowTransition.Rejected(
                RejectionCode.CommandNotApplicable,
                $"Step {command.Step} produces no Revision; it is confirmed by the operator, not started.");
        }

        // Photoshop output must not begin until both production decisions exist. The W1
        // branch in particular is never inferred (MVP design §12).
        if (command.Step == StepKind.PhotoshopOutput)
        {
            if (state.Dimensions is null)
            {
                return WorkflowTransition.Rejected(
                    RejectionCode.PreconditionNotMet,
                    "Photoshop output requires confirmed print dimensions.");
            }

            // The maximum-bound plan, refused here — before the automation lock, before the
            // attempt row, before the working copy and before the adapter call — so a legacy or
            // stale plan produces no attempt at all rather than a fabricated Photoshop failure
            // (Epic 11400 Part B1A.2A §10, §15).
            //
            // UsablePrintPreparationPlan, not PrintPreparationPlan: holding a plan is not the
            // same as being able to run it. A plan calculated against Revision A does not carry
            // to a session whose upstream is now B, and one whose bound hash no longer matches
            // describes bytes that are gone (§7). Nothing rebinds it silently; the operator
            // reconfirms the limits against the content Photoshop will actually consume.
            //
            // A legacy exact pair fails here too, and deliberately for its own reason: those two
            // millimetres were never a fit box, and which of them was meant as the limiting edge
            // is not something to infer from the source ratio (§10).
            // UsablePhotoshopPreparation is the one authority (Part B1A.2D §13): it covers both
            // accepted sizing contracts, and for a target-edge enlargement it also requires the
            // matching authority. A missing enlargement confirmation is a missing product
            // decision, not a failed Photoshop run, so it is refused here — before the attempt
            // row exists — rather than surfacing later as a fabricated external failure (§14).
            if (state.UsablePhotoshopPreparation is null)
            {
                return WorkflowTransition.Rejected(
                    RejectionCode.PreconditionNotMet,
                    state.DimensionSemantics switch
                    {
                        PrintDimensionSemantics.LegacyExactPair =>
                            "DIMENSION REVIEW REQUIRED: this session's print size was recorded as two exact " +
                            "dimensions, before maximum bounds existed. The limits must be reconfirmed under the " +
                            "current contract; nothing infers which edge was intended.",
                        _ when state.NeedsEnlargementAuthority =>
                            "ENLARGEMENT NOT AUTHORISED: the recorded target needs more pixels than the source " +
                            "holds at 300 ppi. Enlarging is a separate explicit confirmation for this exact " +
                            "source and this exact target; nothing grants it automatically.",
                        _ =>
                            "Photoshop output requires a preparation plan calculated from the exact upstream " +
                            "Revision it will consume. A plan calculated from different or since-changed " +
                            "content does not carry over.",
                    });
            }

            if (state.WhiteUnderbaseBranch is null)
            {
                return WorkflowTransition.Rejected(
                    RejectionCode.PreconditionNotMet,
                    "Photoshop output requires an explicit white-underbase branch (W1_0px, W1_1px or W1_2px). There is no default.");
            }
        }

        // Background Removal must not begin without an explicit, still-usable authority for the
        // content it is about to consume. Refused here, before the automation lock, before the
        // attempt row, before the working copy and before the adapter call — a missing product
        // decision is not a failed Meitu processing attempt, and recording one as such would put
        // a fabricated failure in the audit history (Epic 11300 Part C2B1 §7, §26).
        //
        // UsableBackgroundRemovalAuthority, not BackgroundRemovalAuthority: holding an authority
        // is not the same as being authorised. One granted over Revision A does not carry to a
        // session whose upstream is now B, and one whose bound hash no longer matches describes
        // bytes that are gone (§8, §24). Nothing re-binds it silently; the operator decides again.
        if (command.Step == StepKind.BackgroundRemoval && state.UsableBackgroundRemovalAuthority is null)
        {
            return WorkflowTransition.Rejected(
                RejectionCode.PreconditionNotMet,
                "PRODUCT DECISION REQUIRED: background removal requires an explicit authority for the reviewed " +
                "content it will consume. There is no default, and an authority granted over different or " +
                "since-changed content does not carry over.");
        }

        RevisionId? input = state.UpstreamRevisionOf(command.Step);
        if (input is null && command.Step != StepKind.Import)
        {
            return WorkflowTransition.Rejected(
                RejectionCode.PreconditionNotMet,
                $"Step {command.Step} has no upstream Revision to work from.");
        }

        List<WorkflowEffect> effects = [];

        // Every attempt, first or retry, works on a fresh copy (MVP design invariant 8).
        if (input is RevisionId source)
        {
            effects.Add(new WorkflowEffect.CreateWorkingCopy(command.Step, source, WorkspaceArea.Working));
        }

        effects.Add(new WorkflowEffect.RecordAttemptStarted(
            context.NewAttemptId,
            command.Step,
            definition.Operation ?? OperationKind.Import,
            input,
            step.AttemptCount));

        effects.Add(new WorkflowEffect.RunAdapter(
            context.NewAttemptId,
            command.Step,
            definition.Adapter,
            definition.Operation ?? OperationKind.Import,
            input));

        SessionStep started = step with
        {
            State = StepState.Processing,
            AttemptCount = step.AttemptCount + 1,
            EnteredStateAtUtc = context.NowUtc,
        };

        return WorkflowTransition.Accepted(state.WithStep(started), effects);
    }

    private static WorkflowTransition Approve(
        WorkflowSnapshot state, WorkflowCommand.Approve command, CommandContext context)
    {
        StepResolution resolved = Resolve(state, command.Step, CommandKind.Approve);
        if (resolved.Rejection is not null)
        {
            return WorkflowTransition.Rejected(resolved.Rejection);
        }

        SessionStep step = resolved.Step!;

        if (step.CurrentRevisionId is not RevisionId subject ||
            step.CurrentRevisionSha256 is not Sha256 current)
        {
            return WorkflowTransition.Rejected(
                RejectionCode.PreconditionNotMet,
                $"Step {command.Step} has no validated result to approve.");
        }

        // The decision binds to the bytes the operator actually saw. A stale hash means the
        // result changed underneath the review (MVP design invariants 2 and 3).
        if (!current.Equals(command.ReviewedHash))
        {
            return WorkflowTransition.Rejected(
                RejectionCode.PreconditionNotMet,
                $"The reviewed hash {command.ReviewedHash.ShortForm} does not match the current result {current.ShortForm}.");
        }

        bool isPrintOutput = command.Step == StepKind.PhotoshopOutput;

        SessionStep approved = step.WithState(StepState.Approved, context.NowUtc);
        WorkflowSnapshot newState = state.WithStep(approved) with
        {
            LatestApprovedRevisionId = subject,
            ApprovedPrintOutputCount = state.ApprovedPrintOutputCount + (isPrintOutput ? 1 : 0),
        };

        return WorkflowTransition.Accepted(
            newState,
            new WorkflowEffect.RecordReview(
                context.NewReviewId,
                command.Step,
                isPrintOutput ? ReviewSubjectKind.PrintOutput : ReviewSubjectKind.Revision,
                subject.Value,
                command.ReviewedHash,
                IsApproved: true,
                QuickReason: null,
                command.Notes));
    }

    private static WorkflowTransition Reject(
        WorkflowSnapshot state, WorkflowCommand.Reject command, CommandContext context)
    {
        StepResolution resolved = Resolve(state, command.Step, CommandKind.Reject);
        if (resolved.Rejection is not null)
        {
            return WorkflowTransition.Rejected(resolved.Rejection);
        }

        SessionStep step = resolved.Step!;

        if (step.CurrentRevisionId is not RevisionId subject ||
            step.CurrentRevisionSha256 is not Sha256 current)
        {
            return WorkflowTransition.Rejected(
                RejectionCode.PreconditionNotMet,
                $"Step {command.Step} has no validated result to reject.");
        }

        if (!current.Equals(command.ReviewedHash))
        {
            return WorkflowTransition.Rejected(
                RejectionCode.PreconditionNotMet,
                $"The reviewed hash {command.ReviewedHash.ShortForm} does not match the current result {current.ShortForm}.");
        }

        bool isPrintOutput = command.Step == StepKind.PhotoshopOutput;

        // The rejected result stops being the step's offer; the decision itself is kept
        // forever, because the audit trail must survive the invalidation.
        SessionStep rejected = step with
        {
            State = StepState.RetryRequired,
            CurrentRevisionId = null,
            CurrentRevisionSha256 = null,
            EnteredStateAtUtc = context.NowUtc,
        };

        WorkflowSnapshot newState = state.WithStep(rejected);
        newState = newState with { HasDerivedRevision = HasDerivedRevision(newState.Steps) };

        return WorkflowTransition.Accepted(
            newState,
            new WorkflowEffect.RecordReview(
                context.NewReviewId,
                command.Step,
                isPrintOutput ? ReviewSubjectKind.PrintOutput : ReviewSubjectKind.Revision,
                subject.Value,
                command.ReviewedHash,
                IsApproved: false,
                command.Reason,
                command.Notes),
            new WorkflowEffect.InvalidateDescendants(subject, InvalidationReason.Rejected));
    }

    /// <summary>
    /// Returns a rejected, failed or interrupted step to a state where a new attempt is legal.
    /// </summary>
    /// <remarks>
    /// The fresh working copy is created by the following <c>StartStep</c>, which is the one
    /// place that emits <see cref="WorkflowEffect.CreateWorkingCopy"/>. Retry therefore never
    /// leaves a half-prepared copy behind if the operator changes their mind.
    /// </remarks>
    private static WorkflowTransition Retry(
        WorkflowSnapshot state, WorkflowCommand.Retry command, CommandContext context)
    {
        StepResolution resolved = Resolve(state, command.Step, CommandKind.Retry);
        if (resolved.Rejection is not null)
        {
            return WorkflowTransition.Rejected(resolved.Rejection);
        }

        SessionStep step = resolved.Step!;
        SessionStep retryable = step with
        {
            State = StepState.Waiting,
            CurrentRevisionId = null,
            CurrentRevisionSha256 = null,
            EnteredStateAtUtc = context.NowUtc,
        };

        WorkflowSnapshot newState = state.WithStep(retryable);
        newState = newState with { HasDerivedRevision = HasDerivedRevision(newState.Steps) };

        return WorkflowTransition.Accepted(newState);
    }

    /// <summary>
    /// Starts an attempt that crops the step's upstream Revision to the operator's rectangle
    /// (Epic 11200 Part C2 §12, §13).
    /// </summary>
    /// <remarks>
    /// <b>This is half of the eligibility rule, and deliberately the half a pure reducer can
    /// answer.</b> A <see cref="WorkflowSnapshot"/> holds no attempt history, so the engine can
    /// see that Trim is the current step and that it is Failed or RetryRequired, but not
    /// <i>why</i>. The other half — that the failure really was
    /// <c>ManualCropRequired</c>, or that the rejected result really was a manual crop — is
    /// checked by <see cref="Services.ManualCropEligibility"/> in the application layer, which
    /// does hold the attempts. Both must pass before a crop runs; neither is a UI concern
    /// (Part C2 §3).
    /// <para>
    /// Structured exactly like <see cref="StartStep"/>, including the fresh working copy, so a
    /// manual crop is an ordinary producing attempt in every respect that matters to the audit
    /// trail. It is not a <see cref="HandOff"/> and does not change the session state
    /// (Part C2 §4).
    /// </para>
    /// </remarks>
    private static WorkflowTransition SubmitManualCrop(
        WorkflowSnapshot state, WorkflowCommand.SubmitManualCrop command, CommandContext context)
    {
        StepResolution resolved = Resolve(state, command.Step, CommandKind.SubmitManualCrop);
        if (resolved.Rejection is not null)
        {
            return WorkflowTransition.Rejected(resolved.Rejection);
        }

        // Trim is the only step an operator-drawn rectangle means anything for. Enhancement and
        // BackgroundRemoval fail for reasons a crop cannot fix, and PhotoshopOutput produces a
        // production TIFF from confirmed dimensions rather than from a drag.
        if (command.Step != StepKind.Trim)
        {
            return WorkflowTransition.Rejected(
                RejectionCode.CommandNotApplicable,
                $"Step {command.Step} has no manual crop; only Trim can be cropped by hand.");
        }

        if (command.Crop.IsEmpty)
        {
            return WorkflowTransition.Rejected(
                RejectionCode.InvalidPayload, "A manual crop rectangle contains at least one pixel.");
        }

        RevisionId? input = state.UpstreamRevisionOf(command.Step);
        if (input is not RevisionId source)
        {
            return WorkflowTransition.Rejected(
                RejectionCode.PreconditionNotMet,
                $"Step {command.Step} has no upstream Revision to crop.");
        }

        SessionStep step = resolved.Step!;

        List<WorkflowEffect> effects =
        [
            // Every attempt, automatic or manual, works on a fresh copy (MVP design invariant 8).
            new WorkflowEffect.CreateWorkingCopy(command.Step, source, WorkspaceArea.Working),

            new WorkflowEffect.RecordAttemptStarted(
                context.NewAttemptId, command.Step, OperationKind.ManualImport, source, step.AttemptCount),

            new WorkflowEffect.RunManualCrop(context.NewAttemptId, command.Step, source, command.Crop),
        ];

        SessionStep started = step with
        {
            State = StepState.Processing,
            AttemptCount = step.AttemptCount + 1,
            EnteredStateAtUtc = context.NowUtc,
        };

        return WorkflowTransition.Accepted(state.WithStep(started), effects);
    }

    private static WorkflowTransition Skip(
        WorkflowSnapshot state, WorkflowCommand.Skip command, CommandContext context)
    {
        StepResolution resolved = Resolve(state, command.Step, CommandKind.Skip);
        if (resolved.Rejection is not null)
        {
            return WorkflowTransition.Rejected(resolved.Rejection);
        }

        StepDefinition definition = resolved.Definition!;
        if (!definition.IsSkippable)
        {
            return WorkflowTransition.Rejected(
                RejectionCode.StepNotSkippable,
                $"Step {command.Step} is not skippable.");
        }

        // Skipping creates no Revision: the step simply offers nothing, so downstream steps
        // fall through to the last upstream result (MVP design §7.2).
        SessionStep skipped = resolved.Step! with
        {
            State = StepState.Skipped,
            CurrentRevisionId = null,
            CurrentRevisionSha256 = null,
            SkipReason = command.EffectiveReason,
            EnteredStateAtUtc = context.NowUtc,
        };

        return WorkflowTransition.Accepted(
            state.WithStep(skipped),
            new WorkflowEffect.RecordSkip(command.Step, command.EffectiveReason));
    }

    /// <summary>
    /// Transfers the work to the operator and ends automated progression for this session
    /// (MVP design §6.5).
    /// </summary>
    private static WorkflowTransition HandOff(
        WorkflowSnapshot state, WorkflowCommand.HandOff command, CommandContext context)
    {
        StepResolution resolved = Resolve(state, command.Step, CommandKind.HandOff);
        if (resolved.Rejection is not null)
        {
            return WorkflowTransition.Rejected(resolved.Rejection);
        }

        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            return WorkflowTransition.Rejected(
                RejectionCode.InvalidPayload, "Manual handoff requires a reason.");
        }

        string reason = command.Reason.Trim();
        List<WorkflowEffect> effects = [];

        RevisionId? source = state.LatestApprovedRevisionId ?? state.UpstreamRevisionOf(command.Step);
        if (source is RevisionId from)
        {
            effects.Add(new WorkflowEffect.CreateWorkingCopy(command.Step, from, WorkspaceArea.Working));
        }

        effects.Add(new WorkflowEffect.OpenForManualWork(command.Step, reason));
        effects.Add(new WorkflowEffect.ReleaseAutomationLock());
        effects.Add(new WorkflowEffect.MarkSessionHandedOff(context.NowUtc, reason));

        return WorkflowTransition.Accepted(
            state with { SessionState = SessionState.HandedOff },
            effects);
    }

    /// <summary>
    /// Records the maximum physical bounds a production output must fit within
    /// (Epic 11400 Part B1A.2A §4, §8, §14).
    /// </summary>
    /// <remarks>
    /// The command is the one it always was and the payload is unchanged; what changed is what
    /// the payload <i>means</i>. Under the accepted B1A.1 contract the two millimetres are limits
    /// rather than two exact output dimensions, and which single edge Photoshop receives is
    /// <see cref="Domain.Outputs.FitWithinBounds"/>'s answer against the source pixels — never an
    /// axis the operator picked (§3).
    /// <para>
    /// The engine states the legality it can see: the session is active, PrintDimensions is the
    /// current step, the workflow produces a TIFF, the millimetres are positive, and an upstream
    /// result actually exists to fit against. It cannot see source pixels — a snapshot carries no
    /// <c>FileFacts</c> — so the calculation and its binding belong to
    /// <c>SessionService</c>, which holds the Revisions and re-verifies the bytes first (§14).
    /// </para>
    /// <para>
    /// Accepting one starts nothing: no attempt, no working copy, no adapter call, no Revision.
    /// </para>
    /// </remarks>
    private static WorkflowTransition SetPrintDimensions(
        WorkflowSnapshot state, WorkflowCommand.SetPrintDimensions command, CommandContext context)
    {
        StepResolution resolved = Resolve(state, StepKind.PrintDimensions, CommandKind.SetPrintDimensions);
        if (resolved.Rejection is not null)
        {
            return WorkflowTransition.Rejected(resolved.Rejection);
        }

        if (command.Dimensions.WidthMm <= 0 || command.Dimensions.HeightMm <= 0)
        {
            return WorkflowTransition.Rejected(
                RejectionCode.InvalidPayload, "Print dimensions must be positive.");
        }

        // A named preset arriving with millimetres attached is a caller having decided what that
        // preset means, and under v1.11.0 that decision belongs to the configured preset alone.
        // Refused rather than corrected: silently replacing the supplied pair with the configured
        // recommendation would accept a command that said something else (Part B1A.2D §3, §19).
        if (command.Dimensions.Preset != SizePreset.Custom)
        {
            return WorkflowTransition.Rejected(
                RejectionCode.InvalidPayload,
                $"{command.Dimensions.Preset} is a named preset, and its executable limit comes from the " +
                "configured workstation preset rather than from millimetres supplied here. Use " +
                $"{nameof(WorkflowCommand.SetPresetFitSize)}, or record a custom size.");
        }

        // Bounds mean nothing without something to fit inside them, and a plan calculated from
        // no source would be a plan calculated from a guess (§8). This is the same upstream
        // result the plan binds to and the same one Photoshop output will consume.
        if (state.UpstreamResultOf(StepKind.PhotoshopOutput) is null)
        {
            return WorkflowTransition.Rejected(
                RejectionCode.PreconditionNotMet,
                "Photoshop output has no validated upstream result, so there are no source pixels to fit " +
                "within the requested bounds.");
        }

        SessionStep confirmed = resolved.Step!.WithState(StepState.Approved, context.NowUtc);
        WorkflowSnapshot newState = state.WithStep(confirmed) with
        {
            Dimensions = command.Dimensions,

            // Stated here rather than left for the service to remember: anything the engine
            // accepts through this handler was recorded under the current contract, so the
            // reading and the pair are written by the same act (§9).
            DimensionSemantics = PrintDimensionSemantics.MaxBoundsV1,

            // A newly recorded size replaces the previous decision whole. Leaving a target-edge
            // plan, a selection or an enlargement authority behind a fresh fit box would let an
            // old confirmation apply to a target nobody asked about (Part B1A.2D §31).
            SizeSelection = null,
            TargetEdgePlan = null,
            EnlargementAuthority = null,
        };

        return WorkflowTransition.Accepted(
            newState,
            new WorkflowEffect.PersistPrintDimensions(command.Dimensions));
    }

    /// <summary>
    /// Records a named preset, whose executable limits the service resolves from the configured
    /// preset (Epic 11400 Part B1A.2D §3, §15, §17).
    /// </summary>
    /// <remarks>
    /// The engine decides legality and stamps the reading; it cannot decide the millimetres,
    /// because a snapshot carries neither the verified preset nor the source's pixels. So the
    /// state it produces deliberately leaves <see cref="WorkflowSnapshot.Dimensions"/>,
    /// <see cref="WorkflowSnapshot.SizeSelection"/> and
    /// <see cref="WorkflowSnapshot.PrintPreparationPlan"/> for <c>SessionService</c> to attach in
    /// the same act — the arrangement <see cref="SetPrintDimensions"/> already uses for the plan
    /// (§17).
    /// <para>
    /// Ordinary preset use stays exactly what the accepted B1A.1 contract made it: the
    /// recommendation becomes a fit box and <c>FitWithinBounds</c> selects the single edge.
    /// <c>ScaleToTargetEdge</c> is never called for it (§17).
    /// </para>
    /// </remarks>
    private static WorkflowTransition SetPresetFitSize(
        WorkflowSnapshot state, WorkflowCommand.SetPresetFitSize command, CommandContext context)
    {
        StepResolution resolved = Resolve(state, StepKind.PrintDimensions, CommandKind.SetPresetFitSize);
        if (resolved.Rejection is not null)
        {
            return WorkflowTransition.Rejected(resolved.Rejection);
        }

        if (command.Preset == SizePreset.Custom)
        {
            return WorkflowTransition.Rejected(
                RejectionCode.InvalidPayload,
                "Custom is what typing a size produces, not a named preset with a configured " +
                "recommendation behind it.");
        }

        if (state.UpstreamResultOf(StepKind.PhotoshopOutput) is null)
        {
            return WorkflowTransition.Rejected(
                RejectionCode.PreconditionNotMet,
                "Photoshop output has no validated upstream result, so there are no source pixels to fit " +
                "within the preset's recommendation.");
        }

        SessionStep confirmed = resolved.Step!.WithState(StepState.Approved, context.NowUtc);
        return WorkflowTransition.Accepted(
            state.WithStep(confirmed) with
            {
                // Ordinary preset use is a maximum-bound decision, so it is recorded under the
                // reading it is made under. TargetEdgeV1 is what a custom edge produces, and the
                // two are never interchangeable (§5).
                DimensionSemantics = PrintDimensionSemantics.MaxBoundsV1,
                Dimensions = null,
                SizeSelection = null,
                PrintPreparationPlan = null,
                TargetEdgePlan = null,
                EnlargementAuthority = null,
            });
    }

    /// <summary>
    /// Records one exact operator-chosen physical edge (Epic 11400 Part B1A.2D §6, §15, §31).
    /// </summary>
    /// <remarks>
    /// The projection is the service's to calculate, for the same reason the fit is: it needs the
    /// upstream Revision's own pixels and its re-verified bytes, neither of which a snapshot
    /// carries (§16). What the engine settles is that the step may accept a size now, that the
    /// request is a positive number of millimetres, and that there is a source to project against.
    /// <para>
    /// Any previous enlargement authority is cleared here and not conditionally kept. A new target
    /// is a new question, and an authority granted for the old one names an edge, a request and a
    /// projection that no longer describe what is on offer — so keeping it would be a permission
    /// looking for something to apply to (§31).
    /// </para>
    /// </remarks>
    private static WorkflowTransition SetCustomTargetEdgeSize(
        WorkflowSnapshot state, WorkflowCommand.SetCustomTargetEdgeSize command, CommandContext context)
    {
        StepResolution resolved = Resolve(
            state, StepKind.PrintDimensions, CommandKind.SetCustomTargetEdgeSize);
        if (resolved.Rejection is not null)
        {
            return WorkflowTransition.Rejected(resolved.Rejection);
        }

        if (command.Millimetres <= 0)
        {
            return WorkflowTransition.Rejected(
                RejectionCode.InvalidPayload, "A target edge must be positive millimetres.");
        }

        if (command.Edge is not (TargetEdge.Width or TargetEdge.Height or TargetEdge.LongEdge))
        {
            return WorkflowTransition.Rejected(
                RejectionCode.InvalidPayload, $"'{command.Edge}' is not a target edge.");
        }

        if (command.OverriddenPreset == SizePreset.Custom)
        {
            return WorkflowTransition.Rejected(
                RejectionCode.InvalidPayload,
                "Custom is not a recommendation, so there is nothing for it to override.");
        }

        if (state.UpstreamResultOf(StepKind.PhotoshopOutput) is null)
        {
            return WorkflowTransition.Rejected(
                RejectionCode.PreconditionNotMet,
                "Photoshop output has no validated upstream result, so there are no source pixels to " +
                "project the requested edge against.");
        }

        SessionStep confirmed = resolved.Step!.WithState(StepState.Approved, context.NowUtc);
        return WorkflowTransition.Accepted(
            state.WithStep(confirmed) with
            {
                DimensionSemantics = PrintDimensionSemantics.TargetEdgeV1,
                Dimensions = null,
                SizeSelection = null,
                PrintPreparationPlan = null,
                TargetEdgePlan = null,
                EnlargementAuthority = null,
            });
    }

    /// <summary>
    /// Records the operator's explicit permission to enlarge one exact target
    /// (Epic 11400 Part B1A.2D §9, §10).
    /// </summary>
    /// <remarks>
    /// The second confirmation, and it is accepted only against the plan currently on offer. The
    /// payload names what the operator was looking at, and every part of it must still be true:
    /// the Revision, the bytes, the edge and the requested millimetres. A mismatch is refused
    /// rather than reconciled, so "I agreed to enlarge this" cannot drift into "enlargement is on"
    /// (§9, §10).
    /// <para>
    /// The authority itself is built by <see cref="EnlargementAuthority.For"/> from the plan, not
    /// from the payload. That is what binds it to the projected scale and pixel pair the operator
    /// was shown, and it is why nothing outside the Domain can mint one (§35).
    /// </para>
    /// </remarks>
    private static WorkflowTransition AuthoriseEnlargement(
        WorkflowSnapshot state, WorkflowCommand.AuthoriseEnlargement command)
    {
        if (!SessionStateRules.AllowsProgress(state.SessionState))
        {
            return NotActive(state, nameof(WorkflowCommand.AuthoriseEnlargement));
        }

        if (!state.Definition.Contains(StepKind.PhotoshopOutput))
        {
            return NotInWorkflow(state, StepKind.PhotoshopOutput);
        }

        if (state.UsableTargetEdgePlan is not { } plan)
        {
            return WorkflowTransition.Rejected(
                RejectionCode.PreconditionNotMet,
                "There is no current target-edge plan to authorise. An enlargement is confirmed against " +
                "the size actually recorded against the content Photoshop will consume.");
        }

        if (!plan.RequiresEnlargementAuthority)
        {
            return WorkflowTransition.Rejected(
                RejectionCode.PreconditionNotMet,
                $"The recorded target is a {plan.Projection.Direction} and adds no pixels; there is no " +
                "enlargement to authorise, and recording one would read as permission for a run nobody " +
                "asked for.");
        }

        if (plan.SourceRevisionId != command.ReviewedRevisionId)
        {
            return WorkflowTransition.Rejected(
                RejectionCode.PreconditionNotMet,
                $"Revision {command.ReviewedRevisionId} is not the source this plan was calculated from " +
                $"({plan.SourceRevisionId}); authority is granted for reviewed content, never transferred.");
        }

        if (!plan.SourceSha256.Equals(command.DisplayedHash))
        {
            return WorkflowTransition.Rejected(
                RejectionCode.PreconditionNotMet,
                $"The displayed hash {command.DisplayedHash.ShortForm} does not match the plan's source " +
                $"{plan.SourceSha256.ShortForm}; the content reviewed is no longer the content on offer.");
        }

        if (plan.Projection.SelectedTargetEdge != command.Edge ||
            plan.Projection.RequestedMillimetres != command.Millimetres)
        {
            return WorkflowTransition.Rejected(
                RejectionCode.PreconditionNotMet,
                $"The confirmation names {command.Edge} at {command.Millimetres:0.####} mm, and the recorded " +
                $"target is {plan.Projection.SelectedTargetEdge} at " +
                $"{plan.Projection.RequestedMillimetres:0.####} mm; an enlargement is authorised for one " +
                "exact target.");
        }

        EnlargementAuthority authority = EnlargementAuthority.For(plan);
        return WorkflowTransition.Accepted(
            state with { EnlargementAuthority = authority },
            new WorkflowEffect.PersistEnlargementAuthority(authority));
    }

    // ---------------------------------------------------------------------------------
    // System commands
    // ---------------------------------------------------------------------------------

    private static WorkflowTransition AttemptSucceeded(
        WorkflowSnapshot state, WorkflowCommand.System.AttemptSucceeded command, CommandContext context)
    {
        StepResolution resolved = Resolve(state, command.Step, CommandKind.AttemptSucceeded);
        if (resolved.Rejection is not null)
        {
            return WorkflowTransition.Rejected(resolved.Rejection);
        }

        SessionStep step = resolved.Step!;
        StepDefinition definition = resolved.Definition!;

        StepState destination = TransitionTable.Destination(CommandKind.AttemptSucceeded, definition);

        SessionStep produced = step with
        {
            State = destination,
            CurrentRevisionId = command.OutputRevision,
            CurrentRevisionSha256 = command.OutputHash,
            EnteredStateAtUtc = context.NowUtc,
        };

        WorkflowSnapshot newState = state.WithStep(produced);
        newState = newState with
        {
            HasDerivedRevision = HasDerivedRevision(newState.Steps),
            LatestApprovedRevisionId = destination == StepState.Approved
                ? command.OutputRevision
                : state.LatestApprovedRevisionId,
        };

        List<WorkflowEffect> effects =
        [
            new WorkflowEffect.PersistRevision(command.OutputRevision, command.Step, command.AttemptId),
        ];

        if (definition.IsAdapterBacked)
        {
            effects.Add(new WorkflowEffect.ReleaseAutomationLock());
        }

        return WorkflowTransition.Accepted(newState, effects);
    }

    private static WorkflowTransition AttemptFailed(
        WorkflowSnapshot state, WorkflowCommand.System.AttemptFailed command, CommandContext context)
    {
        StepResolution resolved = Resolve(state, command.Step, CommandKind.AttemptFailed);
        if (resolved.Rejection is not null)
        {
            return WorkflowTransition.Rejected(resolved.Rejection);
        }

        // A failed attempt never creates a Revision, so the step offers nothing downstream
        // (MVP design invariant 4).
        SessionStep failed = resolved.Step!.WithState(StepState.Failed, context.NowUtc);

        List<WorkflowEffect> effects =
        [
            new WorkflowEffect.RecordAttemptFailure(command.AttemptId, command.Step, command.Failure),
        ];

        if (resolved.Definition!.IsAdapterBacked)
        {
            effects.Add(new WorkflowEffect.ReleaseAutomationLock());
        }

        return WorkflowTransition.Accepted(state.WithStep(failed), effects);
    }

    private static WorkflowTransition AttemptInterrupted(
        WorkflowSnapshot state, WorkflowCommand.System.AttemptInterrupted command, CommandContext context)
    {
        StepResolution resolved = Resolve(state, command.Step, CommandKind.AttemptInterrupted);
        if (resolved.Rejection is not null)
        {
            return WorkflowTransition.Rejected(resolved.Rejection);
        }

        SessionStep interrupted = resolved.Step!.WithState(StepState.Interrupted, context.NowUtc);

        List<WorkflowEffect> effects =
        [
            new WorkflowEffect.RecordAttemptInterrupted(command.AttemptId, command.Step),
            new WorkflowEffect.ReleaseAutomationLock(),
        ];

        return WorkflowTransition.Accepted(state.WithStep(interrupted), effects);
    }

    /// <summary>
    /// Closes a running attempt because a human stopped it (Epic 11300 Part D2A §12, §16).
    /// </summary>
    /// <remarks>
    /// The rule that matters here is the one that is <i>absent</i>: nothing in this handler can
    /// reach a Revision. It sets a step state and records a failure, exactly as
    /// <see cref="AttemptFailed"/> does, and it has no parameter naming an output — so §12's
    /// "no cancelled Revision" is a property of the signature rather than a discipline.
    /// <para>
    /// §16's boundary is enforced by <see cref="TransitionTable"/> rather than restated here.
    /// A step whose attempt already succeeded is no longer <c>Processing</c>, and
    /// <see cref="CommandKind.AttemptCancelled"/> is legal only from <c>Processing</c> — so a
    /// Stop that arrives after a validated output has been committed is refused, and cannot
    /// rewrite a completed Attempt/Revision transaction into a failure. That is the whole of
    /// "late Stop cannot erase success", and it is a table row rather than an <c>if</c>.
    /// </para>
    /// <para>
    /// The automation lock is released unconditionally, and unlike <see cref="AttemptFailed"/>
    /// this does not first ask whether the step is adapter-backed. A stopped run is over
    /// however it was performed, and holding a global lock on behalf of a run that has stopped
    /// is exactly the state §32 requires not to persist.
    /// </para>
    /// </remarks>
    private static WorkflowTransition AttemptCancelled(
        WorkflowSnapshot state, WorkflowCommand.System.AttemptCancelled command, CommandContext context)
    {
        StepResolution resolved = Resolve(state, command.Step, CommandKind.AttemptCancelled);
        if (resolved.Rejection is not null)
        {
            return WorkflowTransition.Rejected(resolved.Rejection);
        }

        SessionStep stopped = resolved.Step!.WithState(
            TransitionTable.Destination(CommandKind.AttemptCancelled, resolved.Definition!), context.NowUtc);

        return WorkflowTransition.Accepted(
            state.WithStep(stopped),
            [
                new WorkflowEffect.RecordAttemptCancelled(command.AttemptId, command.Step, command.Failure),
                new WorkflowEffect.ReleaseAutomationLock(),
            ]);
    }

    /// <summary>
    /// Returns a handed-off session to automation, at the operator's explicit request
    /// (Epic 11300 Part D2A §22, §30).
    /// </summary>
    /// <remarks>
    /// The one command that lifts <c>SessionState.HandedOff</c>, and therefore the one thing
    /// standing between a takeover and automation quietly resuming. Everything else that could
    /// drive the session forward goes through <see cref="Resolve"/>, which refuses a session
    /// that is not <c>Active</c> — so a restart, a reload, or an operator pressing Run again
    /// all fail closed until this command is issued (§30).
    /// <para>
    /// It starts nothing. The step goes back to <c>Waiting</c>, which is exactly where
    /// <see cref="Retry"/> leaves it, and the new attempt is produced by the ordinary
    /// <see cref="StartStep"/> path afterwards — with the fresh working copy and the normal
    /// safe-state verification that path already performs. Re-entry is therefore explicit
    /// twice over: once to leave the handed-off state, and once to begin work (§22).
    /// </para>
    /// <para>
    /// No effect here touches a file, an attempt row or a Revision. The handed-off attempt is
    /// closed history and stays exactly as it was written (§15), and nothing scans the external
    /// application or the workspace for whatever the operator produced while they owned it
    /// (§23).
    /// </para>
    /// </remarks>
    private static WorkflowTransition ReenterAutomation(WorkflowSnapshot state, CommandContext context)
    {
        if (state.SessionState != SessionState.HandedOff)
        {
            return WorkflowTransition.Rejected(
                RejectionCode.PreconditionNotMet,
                $"The session is {state.SessionState}; returning to automation applies only to a " +
                "session that was handed off to the operator.");
        }

        SessionStep? current = state.CurrentStep;
        if (current is null)
        {
            return WorkflowTransition.Rejected(
                RejectionCode.PreconditionNotMet,
                "The session has no step left to return to, so there is no automation to re-enter.");
        }

        // Only from a step that actually stopped. A handed-off session sitting on an Approved
        // or ReviewRequired step has a result waiting for a decision, and re-entry must not
        // quietly discard it by resetting the step to Waiting.
        if (current.State is not (StepState.Interrupted or StepState.Failed or StepState.RetryRequired))
        {
            return WorkflowTransition.Rejected(
                RejectionCode.IllegalStateTransition,
                $"Step {current.Step} is {current.State}; returning to automation applies to a step " +
                "whose attempt stopped without a result.");
        }

        SessionStep reset = current with
        {
            State = StepState.Waiting,
            CurrentRevisionId = null,
            CurrentRevisionSha256 = null,
            EnteredStateAtUtc = context.NowUtc,
        };

        return WorkflowTransition.Accepted(
            state.WithStep(reset) with { SessionState = SessionState.Active },
            [new WorkflowEffect.MarkSessionReenteredAutomation(context.NowUtc)]);
    }

    // ---------------------------------------------------------------------------------
    // Shared guards
    // ---------------------------------------------------------------------------------

    /// <summary>Outcome of the shared session/step/table guards.</summary>
    private readonly record struct StepResolution(
        SessionStep? Step,
        StepDefinition? Definition,
        CommandRejection? Rejection)
    {
        public static StepResolution Ok(SessionStep step, StepDefinition definition) =>
            new(step, definition, null);

        public static StepResolution Refused(RejectionCode code, string message) =>
            new(null, null, new CommandRejection(code, message));
    }

    /// <summary>
    /// Runs the guards every step-scoped command shares: the session is active, the step
    /// belongs to this workflow, it is the step the session is on, and the table permits the
    /// command in its current state.
    /// </summary>
    private static StepResolution Resolve(WorkflowSnapshot state, StepKind kind, CommandKind command)
    {
        if (!SessionStateRules.AllowsProgress(state.SessionState))
        {
            return StepResolution.Refused(
                RejectionCode.SessionNotActive,
                $"The session is {state.SessionState}; {command} is no longer legal.");
        }

        SessionStep? step = state.Step(kind);
        StepDefinition? definition = state.Definition.Find(kind);
        if (step is null || definition is null)
        {
            return StepResolution.Refused(
                RejectionCode.StepNotInWorkflow,
                $"Workflow {state.WorkflowType} has no step {kind}.");
        }

        SessionStep? current = state.CurrentStep;
        if (current is null || current.Step != kind)
        {
            return StepResolution.Refused(
                RejectionCode.NotCurrentStep,
                $"Step {kind} is not the current step ({current?.Step.ToString() ?? "none"}).");
        }

        if (TransitionTable.Lookup(step.State, command) != TransitionOutcome.Allowed)
        {
            return StepResolution.Refused(
                RejectionCode.IllegalStateTransition,
                $"{command} is not legal for step {kind} in state {step.State}.");
        }

        return StepResolution.Ok(step, definition);
    }

    private static WorkflowTransition NotActive(WorkflowSnapshot state, string command) =>
        WorkflowTransition.Rejected(
            RejectionCode.SessionNotActive,
            $"The session is {state.SessionState}; {command} is no longer legal.");

    private static WorkflowTransition NotInWorkflow(WorkflowSnapshot state, StepKind kind) =>
        WorkflowTransition.Rejected(
            RejectionCode.StepNotInWorkflow,
            $"Workflow {state.WorkflowType} has no step {kind}.");

    /// <summary>True when any step past Import currently offers a Revision.</summary>
    private static bool HasDerivedRevision(IReadOnlyList<SessionStep> steps)
    {
        foreach (SessionStep step in steps)
        {
            if (step.Step != StepKind.Import && step.CurrentRevisionId is not null)
            {
                return true;
            }
        }

        return false;
    }

    private static RevisionId? LatestApprovedBefore(IReadOnlyList<SessionStep> steps, int ordinal)
    {
        RevisionId? found = null;
        foreach (SessionStep step in steps)
        {
            if (step.Ordinal >= ordinal)
            {
                break;
            }

            if (step.State == StepState.Approved && step.CurrentRevisionId is not null)
            {
                found = step.CurrentRevisionId;
            }
        }

        return found;
    }

    /// <summary>
    /// Builds a representative command for legality probing.
    /// </summary>
    /// <remarks>
    /// Payload-carrying commands are probed with a stand-in payload chosen so that the
    /// <i>payload</i> guard cannot be what decides the answer. The probe therefore reports the
    /// category question — <i>may this command be attempted here at all</i> — while the real
    /// command still has to satisfy every payload guard when it is issued. No guard is
    /// relaxed to make probing possible (Epic 11100 Part 3C3B §8).
    /// <para>
    /// <see cref="CommandKind.SelectWorkflow"/> is the exception, and is probed with the
    /// session's <i>current</i> workflow. Its two guards — the session is Active, and no
    /// derived Revision exists — are both checked before the payload is looked at, so the
    /// probe answers "may the workflow still be chosen?" without inventing a choice the
    /// operator has not made. That question is exactly what the Workflow Selection screen
    /// asks, and answering it here keeps the workflow lock defined in one place
    /// (MVP design §6.1, invariant 12).
    /// </para>
    /// <para>
    /// <see cref="CommandKind.Approve"/> and <see cref="CommandKind.Reject"/> are probed with
    /// the step's <b>own current hash</b>, never a synthesised one. Their legality includes
    /// "the reviewed hash still matches the result on offer", so probing with anything else
    /// would answer a different question than the operator's click will ask. When the step has
    /// no result there is nothing to review and no probe is built (Epic 11100 Part 3C3A §5).
    /// </para>
    /// <para>
    /// <see cref="CommandKind.HandOff"/> and <see cref="CommandKind.AbandonSession"/> carry a
    /// free-text reason the operator has not written yet, so they are probed with
    /// <see cref="ProbeReason"/>. That deliberately separates the two questions §5 draws apart:
    /// the probe answers <i>may this command category be attempted here</i>, and the real
    /// command still has to satisfy the non-empty-reason guard when it is issued. Validation is
    /// not weakened — an empty reason is refused exactly as before.
    /// </para>
    /// <para>
    /// <see cref="CommandKind.SetPrintDimensions"/> is probed with <see cref="ProbeDimensions"/>
    /// and <see cref="CommandKind.SelectWhiteUnderbaseBranch"/> with
    /// <see cref="ProbeBranch"/> plus <see cref="ProbeReason"/>. Both stand-ins are valid by
    /// construction, precisely so the payload check cannot be what answers the probe: what
    /// varies, and therefore what the answer reports, is whether the session is active, whether
    /// PrintDimensions is the current step, and whether this workflow produces a TIFF at all.
    /// The operator's own size and branch still go through every guard when the real command is
    /// issued — a non-positive dimension is refused as before, and
    /// <see cref="ProbeBranch"/> is <b>never</b> a default: nothing is applied by a probe, so no
    /// session acquires a branch it did not explicitly choose (MVP design §12; Part 3C3B §8).
    /// </para>
    /// <para>
    /// <see cref="CommandKind.SetTrimParameters"/> is probed with the session's <b>current</b>
    /// margin, the same way <see cref="CommandKind.SelectWorkflow"/> is probed with its current
    /// workflow. Its payload is valid by construction — <c>TrimMargin</c>'s factories refuse a
    /// negative pixel count — so the guards that vary are the ones that answer: is the session
    /// active, is Trim this workflow's current step, and is it between attempts. Probing with an
    /// invented margin would be worse than pointless here: it would answer a question about a
    /// number the operator never typed (Epic 11200 Part C3 §9, §13).
    /// </para>
    /// <para>
    /// <see cref="CommandKind.SetOutputName"/> and <see cref="CommandKind.ReturnToStep"/> stay
    /// unprobed. <c>SetOutputName</c> has no screen in this slice, and <c>ReturnToStep</c> has
    /// no payload-independent answer — "may I return" depends on <i>which</i> step, so a single
    /// stand-in target would report something no button is asking (Part 3C3B §8). The screen
    /// that offers destinations asks <see cref="AvailableReturnTargets"/> instead, which
    /// answers with the real targets rather than a yes/no (Part C3 §8).
    /// </para>
    /// </remarks>
    private static WorkflowCommand? BuildProbe(WorkflowSnapshot state, CommandKind kind)
    {
        SessionStep? current = state.CurrentStep;
        StepKind step = current?.Step ?? state.Definition.Terminal.Kind;

        return kind switch
        {
            CommandKind.SelectWorkflow => new WorkflowCommand.SelectWorkflow(state.WorkflowType),
            CommandKind.ConfirmOriginal => new WorkflowCommand.ConfirmOriginal(),
            CommandKind.StartStep => new WorkflowCommand.StartStep(step),
            CommandKind.Retry => new WorkflowCommand.Retry(step),
            CommandKind.SubmitManualCrop => new WorkflowCommand.SubmitManualCrop(step, ProbeCrop),
            CommandKind.Skip => new WorkflowCommand.Skip(step),
            CommandKind.HandOff => new WorkflowCommand.HandOff(step, ProbeReason),
            CommandKind.SetPrintDimensions => new WorkflowCommand.SetPrintDimensions(ProbeDimensions),

            // Probed with a real configured-preset request only in the sense that the payload is
            // structurally valid: A4 here is a stand-in, exactly as ProbeDimensions is, and the
            // millimetres behind it are never read by a probe because nothing is applied. What
            // varies, and therefore what the answer reports, is whether PrintDimensions is the
            // current step and whether a source exists to size against (Part B1A.2D §15).
            CommandKind.SetPresetFitSize => new WorkflowCommand.SetPresetFitSize(ProbeSizePreset),
            CommandKind.SetCustomTargetEdgeSize =>
                new WorkflowCommand.SetCustomTargetEdgeSize(ProbeTargetEdge, ProbeTargetMillimetres),

            // Probed with the plan the session actually holds, never an invented target: an
            // enlargement confirmation is about one exact recorded size, so a stand-in would
            // answer a question no screen is asking. A session with no plan needing authority
            // gets no probe at all, which is the honest "there is nothing to confirm" (§10).
            CommandKind.AuthoriseEnlargement
                when state.UsableTargetEdgePlan is { RequiresEnlargementAuthority: true } pending =>
                new WorkflowCommand.AuthoriseEnlargement(
                    pending.SourceRevisionId,
                    pending.SourceSha256,
                    pending.Projection.SelectedTargetEdge,
                    pending.Projection.RequestedMillimetres),
            CommandKind.SelectWhiteUnderbaseBranch =>
                new WorkflowCommand.SelectWhiteUnderbaseBranch(ProbeBranch, ProbeReason),
            CommandKind.SetTrimParameters => new WorkflowCommand.SetTrimParameters(state.TrimMargin),
            CommandKind.SetBackgroundRemovalDecision
                when state.Definition.Contains(StepKind.BackgroundRemoval)
                    && state.UpstreamResultOf(StepKind.BackgroundRemoval) is { } reviewed =>
                new WorkflowCommand.SetBackgroundRemovalDecision(
                    ProbeBackgroundRemovalDecision, reviewed.Id, reviewed.Sha256),
            CommandKind.Complete => new WorkflowCommand.Complete(),
            CommandKind.AddAnotherSize => new WorkflowCommand.AddAnotherSize(),
            CommandKind.AbandonSession => new WorkflowCommand.AbandonSession(ProbeReason),

            // Probed with the real command, because it has no payload at all: what varies, and
            // therefore what the answer reports, is whether the session is handed off and
            // whether its current step is one whose attempt stopped without a result
            // (Epic 11300 Part D2A §22).
            CommandKind.ReenterAutomation => new WorkflowCommand.ReenterAutomation(),
            CommandKind.Approve when current?.CurrentRevisionSha256 is Sha256 hash =>
                new WorkflowCommand.Approve(step, hash),
            CommandKind.Reject when current?.CurrentRevisionSha256 is Sha256 hash =>
                new WorkflowCommand.Reject(step, hash, RejectionReason.Other),
            _ => null,
        };
    }

    /// <summary>
    /// The stand-in decision used when probing
    /// <see cref="CommandKind.SetBackgroundRemovalDecision"/>.
    /// </summary>
    /// <remarks>
    /// Emphatically not a default, and for the same reason <see cref="ProbeBranch"/> is not:
    /// the probed transition is discarded and only its accepted/rejected verdict is read, so no
    /// session acquires an authority nobody granted. It has to name the authorised member rather
    /// than <c>Unspecified</c>, because probing with the refusal value would answer a question
    /// about a command the handler rejects outright — the probe would then always say "no" and
    /// tell the screen nothing about whether the operator may decide (Epic 11300 Part C2B1 §22).
    /// <para>
    /// The revision and hash are the session's <b>actual</b> current upstream result, never
    /// invented ones. An invented pair would answer a question about content that does not
    /// exist; the real pair means the probe reports exactly the guards that vary — is the
    /// session active, is BackgroundRemoval the current step, is it between attempts.
    /// </para>
    /// </remarks>
    private const BackgroundRemovalDecision ProbeBackgroundRemovalDecision =
        BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent;

    /// <summary>
    /// The stand-in reason used when probing a command that requires one.
    /// </summary>
    /// <remarks>
    /// Never persisted: a probe produces no effects because nothing is applied. It exists only
    /// so the reason guard is satisfied while the guards that actually vary — session state and
    /// step state — decide the answer.
    /// </remarks>
    private const string ProbeReason = "probe";

    /// <summary>
    /// The stand-in size used when probing <see cref="CommandKind.SetPrintDimensions"/>.
    /// </summary>
    /// <remarks>
    /// A valid size on purpose, so the positivity guard is satisfied and the probe reports the
    /// step question instead. Never persisted and never shown: a probe applies nothing, so no
    /// session is silently given a size.
    /// <para>
    /// Deliberately <see cref="SizePreset.Custom"/>. <c>SetPrintDimensions</c> is the custom
    /// fit-box route and refuses a named preset outright, so probing with one would answer "no"
    /// for a reason that has nothing to do with the session (Part B1A.2D §3).
    /// </para>
    /// </remarks>
    private static readonly PrintDimensions ProbeDimensions =
        PrintDimensions.FromMillimetres(100d, 100d, SizePreset.Custom);

    /// <summary>
    /// The stand-in preset used when probing <see cref="CommandKind.SetPresetFitSize"/>.
    /// </summary>
    /// <remarks>
    /// A named preset is required for the payload guard, and which one is irrelevant: the probed
    /// transition is discarded and only its accepted/rejected verdict is read, so no session
    /// acquires A4. The configured millimetres behind the name are never resolved by a probe —
    /// that is <c>SessionService</c>'s work, and a probe reaches no service (Part B1A.2D §3).
    /// </remarks>
    private const SizePreset ProbeSizePreset = SizePreset.A4;

    /// <summary>
    /// The stand-in target used when probing <see cref="CommandKind.SetCustomTargetEdgeSize"/>.
    /// </summary>
    /// <remarks>
    /// Valid by construction, so the positivity guard is satisfied and the probe reports the step
    /// question a screen is actually asking. The operator's own edge and millimetres go through
    /// every guard, and through the exact target-edge calculation, when the real command is
    /// issued.
    /// </remarks>
    private const TargetEdge ProbeTargetEdge = TargetEdge.LongEdge;

    /// <inheritdoc cref="ProbeTargetEdge" />
    private const decimal ProbeTargetMillimetres = 100m;

    /// <summary>
    /// The stand-in branch used when probing
    /// <see cref="CommandKind.SelectWhiteUnderbaseBranch"/>.
    /// </summary>
    /// <remarks>
    /// Emphatically not a default. The enum has no member that means "unchosen", so a probe has
    /// to name one; which one is irrelevant, because the probed transition is discarded and only
    /// its accepted/rejected verdict is read. The operator's choice remains the only branch that
    /// ever reaches a session (MVP design §12).
    /// </remarks>
    private const WhiteUnderbaseBranch ProbeBranch = WhiteUnderbaseBranch.W1_1px;

    /// <summary>
    /// The stand-in rectangle used when probing <see cref="CommandKind.SubmitManualCrop"/>.
    /// </summary>
    /// <remarks>
    /// A single pixel: valid by construction, so the non-empty guard is satisfied and the probe
    /// reports the question a screen is actually asking — <i>may a manual crop be started for
    /// this step at all</i>. The operator's own rectangle still goes through every guard, in
    /// the engine and again in the processor, when the real command is issued. It is never
    /// persisted and never cropped to: a probe applies nothing.
    /// <para>
    /// A positive probe is necessary but not sufficient for the crop control to appear. The
    /// screen reads <c>SessionView.CanManualCrop</c>, which also requires the attempt history
    /// to show a genuine <c>ManualCropRequired</c> condition (Part C2 §3).
    /// </para>
    /// </remarks>
    private static readonly TrimBounds ProbeCrop = TrimBounds.Canvas(1, 1);
}
