using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Reviews;
using PrintFlow.Domain.Sessions;
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
            WorkflowCommand.Skip c => Skip(state, c, context),
            WorkflowCommand.HandOff c => HandOff(state, c, context),
            WorkflowCommand.SetPrintDimensions c => SetPrintDimensions(state, c, context),
            WorkflowCommand.SelectWhiteUnderbaseBranch c => SelectWhiteUnderbaseBranch(state, c),
            WorkflowCommand.ReturnToStep c => ReturnToStep(state, c, context),
            WorkflowCommand.Complete => Complete(state, context),
            WorkflowCommand.AddAnotherSize => AddAnotherSize(state, context),
            WorkflowCommand.AbandonSession c => AbandonSession(state, c, context),
            WorkflowCommand.System.AttemptSucceeded c => AttemptSucceeded(state, c, context),
            WorkflowCommand.System.AttemptFailed c => AttemptFailed(state, c, context),
            WorkflowCommand.System.AttemptInterrupted c => AttemptInterrupted(state, c, context),
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
        if (state.SessionState != SessionState.Active)
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
            state.SessionId, command.Type, state.OutputName, context.NowUtc);

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
        if (state.SessionState != SessionState.Active)
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
        if (state.SessionState != SessionState.Active)
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
    /// Returns to an earlier step and invalidates everything derived from it.
    /// </summary>
    /// <remarks>
    /// The engine states the invalidation as an effect; the recursive descendant walk and the
    /// file moves belong to Tasks 11105 and 11108 (Epic 11100 plan §10.4).
    /// </remarks>
    private static WorkflowTransition ReturnToStep(
        WorkflowSnapshot state, WorkflowCommand.ReturnToStep command, CommandContext context)
    {
        if (state.SessionState != SessionState.Active)
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
        // rewound, so they are cleared with it and must be made again explicitly.
        bool clearsDimensions = state.Definition.IndexOf(StepKind.PrintDimensions) >= target.Ordinal;

        WorkflowSnapshot newState = state with
        {
            Steps = steps,
            Dimensions = clearsDimensions ? null : state.Dimensions,
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
        if (state.SessionState != SessionState.Active)
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
            Dimensions = null,
            WhiteUnderbaseBranch = null,
        };

        return WorkflowTransition.Accepted(
            newState,
            new WorkflowEffect.BeginAdditionalOutput(source.Value));
    }

    private static WorkflowTransition AbandonSession(
        WorkflowSnapshot state, WorkflowCommand.AbandonSession command, CommandContext context)
    {
        if (state.SessionState is not (SessionState.Active or SessionState.HandedOff))
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

            if (state.WhiteUnderbaseBranch is null)
            {
                return WorkflowTransition.Rejected(
                    RejectionCode.PreconditionNotMet,
                    "Photoshop output requires an explicit white-underbase branch (W1_0px, W1_1px or W1_2px). There is no default.");
            }
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

        SessionStep confirmed = resolved.Step!.WithState(StepState.Approved, context.NowUtc);
        WorkflowSnapshot newState = state.WithStep(confirmed) with { Dimensions = command.Dimensions };

        return WorkflowTransition.Accepted(
            newState,
            new WorkflowEffect.PersistPrintDimensions(command.Dimensions));
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
        if (state.SessionState != SessionState.Active)
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
    /// Payload-carrying commands whose legality depends on the payload itself are omitted
    /// from the probe: the engine cannot report whether a dimension the operator has not
    /// typed yet would be accepted. The UI enables those through their own screens.
    /// </remarks>
    private static WorkflowCommand? BuildProbe(WorkflowSnapshot state, CommandKind kind)
    {
        SessionStep? current = state.CurrentStep;
        StepKind step = current?.Step ?? state.Definition.Terminal.Kind;

        return kind switch
        {
            CommandKind.ConfirmOriginal => new WorkflowCommand.ConfirmOriginal(),
            CommandKind.StartStep => new WorkflowCommand.StartStep(step),
            CommandKind.Retry => new WorkflowCommand.Retry(step),
            CommandKind.Skip => new WorkflowCommand.Skip(step),
            CommandKind.HandOff => new WorkflowCommand.HandOff(step, "probe"),
            CommandKind.Complete => new WorkflowCommand.Complete(),
            CommandKind.AddAnotherSize => new WorkflowCommand.AddAnotherSize(),
            CommandKind.AbandonSession => new WorkflowCommand.AbandonSession("probe"),
            CommandKind.Approve when current?.CurrentRevisionSha256 is Sha256 hash =>
                new WorkflowCommand.Approve(step, hash),
            CommandKind.Reject when current?.CurrentRevisionSha256 is Sha256 hash =>
                new WorkflowCommand.Reject(step, hash, RejectionReason.Other),
            _ => null,
        };
    }
}
