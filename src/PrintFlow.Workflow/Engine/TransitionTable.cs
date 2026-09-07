using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Definitions;

namespace PrintFlow.Workflow.Engine;

/// <summary>
/// Every command the engine understands, as a closed set.
/// </summary>
/// <remarks>
/// Having a discrete enum alongside the command records is what makes exhaustiveness
/// testable: a test can enumerate <c>StepState × CommandKind</c> and assert that every pair
/// has an explicit outcome, with no silent fall-through (Epic 11100 plan §8.1).
/// </remarks>
public enum CommandKind
{
    SelectWorkflow,
    SetOutputName,
    ConfirmOriginal,
    StartStep,
    Approve,
    Reject,
    Retry,
    Skip,
    KeepOriginalExtent,
    HandOff,
    SubmitManualCrop,
    SetPrintDimensions,

    /// <summary>Take a named preset's configured recommendation (Part B1A.2D §3).</summary>
    SetPresetFitSize,

    /// <summary>Record one exact operator-chosen physical edge (Part B1A.2D §6).</summary>
    SetCustomTargetEdgeSize,

    /// <summary>Permit one exact enlargement, explicitly and separately (Part B1A.2D §10).</summary>
    AuthoriseEnlargement,

    SelectWhiteUnderbaseBranch,
    SetTrimParameters,
    SetBackgroundRemovalDecision,
    ReturnToStep,
    Complete,
    AddAnotherSize,
    AbandonSession,

    /// <summary>Bring a handed-off session back under automation, explicitly (Part D2A §22).</summary>
    ReenterAutomation,
    SubmitManualResult,

    AttemptSucceeded,
    AttemptFailed,
    AttemptInterrupted,

    /// <summary>A human stopped a running attempt (Part D2A §12, §19).</summary>
    AttemptCancelled,
}

/// <summary>What the step-level table says about one state/command pair.</summary>
public enum TransitionOutcome
{
    /// <summary>The pair is illegal at step level and must be rejected.</summary>
    Rejected,

    /// <summary>The pair is legal at step level, subject to the guards in <see cref="WorkflowEngine"/>.</summary>
    Allowed,

    /// <summary>
    /// The command is session-scoped: the step's state does not decide it. The engine
    /// resolves it with session-level rules instead.
    /// </summary>
    SessionScoped,
}

/// <summary>
/// The explicit legality table for step-scoped commands (Epic 11100 plan §8.3).
/// </summary>
/// <remarks>
/// Legality lives in one readable table rather than scattered across conditionals, so an
/// unhandled combination is a visible gap rather than an accidental fall-through. The table
/// answers only "could this command apply to a step in this state"; the engine then applies
/// the step-definition guards — skippable, requires review, is current, preconditions —
/// before accepting.
/// </remarks>
public static class TransitionTable
{
    /// <summary>Commands whose legality depends on the session rather than on a step state.</summary>
    private static readonly CommandKind[] SessionScopedCommands =
    [
        CommandKind.SelectWorkflow,
        CommandKind.SetOutputName,
        CommandKind.SelectWhiteUnderbaseBranch,

        // A trim margin is a decision about the run, not a step-state transition: it changes
        // no step's state and starts nothing. The engine still checks that Trim is the current
        // step and is between attempts before accepting one — that guard just is not a row in
        // this table, because the table answers "what does the *target step's* state permit",
        // and this command names no step (Epic 11200 Part C3 §13).
        CommandKind.SetTrimParameters,

        // And a reviewed-content authority for Background Removal, for the same reason: it
        // names no step, changes no step state, and starts nothing. Whether Background Removal
        // is the current step and is between attempts is a guard the engine applies, not a row
        // in this table (Epic 11300 Part C2B1 §5, §6).
        CommandKind.SetBackgroundRemovalDecision,

        // And permission to enlarge, for the same reason again — with one difference worth
        // stating: it is legal *after* the size step is finished, because the operator confirms
        // the enlargement once they have been shown what the recorded target actually costs. A
        // row on the PrintDimensions step could not express that, and the engine's own guard is
        // what checks a plan needing authority is actually on offer (Part B1A.2D §10).
        CommandKind.AuthoriseEnlargement,
        CommandKind.ReturnToStep,
        CommandKind.Complete,
        CommandKind.AddAnotherSize,
        CommandKind.AbandonSession,

        // Re-entry after a takeover is decided entirely by the session: it is legal exactly
        // when the session is HandedOff, which is a state no step row records. It names no
        // step in its payload for the same reason (Epic 11300 Part D2A §22).
        CommandKind.ReenterAutomation,
        CommandKind.SubmitManualResult,
    ];

    /// <summary>
    /// The step-state rows of plan §8.3. A pair absent from a row is rejected.
    /// </summary>
    private static readonly Dictionary<StepState, CommandKind[]> AllowedByState = new()
    {
        [StepState.Waiting] =
        [
            CommandKind.ConfirmOriginal,
            CommandKind.KeepOriginalExtent,
            CommandKind.StartStep,
            CommandKind.Skip,
            CommandKind.SetPrintDimensions,

            // The three sizing routes sit in the same row because they are one decision made
            // three ways, and all three finish the same step. Which of them a session used is
            // recorded in its semantics and its selection, not in where it was legal
            // (Part B1A.2D §15).
            CommandKind.SetPresetFitSize,
            CommandKind.SetCustomTargetEdgeSize,
        ],

        // A running attempt is finished by the system, never by the operator.
        //
        // AttemptCancelled belongs in this row and nowhere else, and that is what makes an
        // operator Stop safe to model. The operator does not close the attempt — they ask the
        // run to stop, and the application layer raises this once the run has actually
        // unwound and reported what it left behind. A Stop pressed against a step that is not
        // Processing has nothing to close, and the table refuses it rather than inventing a
        // stopped attempt (Epic 11300 Part D2A §12, §24).
        [StepState.Processing] =
        [
            CommandKind.AttemptSucceeded,
            CommandKind.AttemptFailed,
            CommandKind.AttemptInterrupted,
            CommandKind.AttemptCancelled,
        ],

        [StepState.ReviewRequired] =
        [
            CommandKind.Approve,
            CommandKind.KeepOriginalExtent,
            CommandKind.Reject,
            CommandKind.HandOff,
        ],

        // A finished step is reopened only by returning to it, which is session-scoped.
        [StepState.Approved] = [],

        // SubmitManualCrop belongs here as well as under Failed: rejecting a manual crop
        // leaves the step RetryRequired, and the corrective path after that is another manual
        // crop rather than the deterministic trim that already refused (Part C2 §21).
        [StepState.RetryRequired] =
        [
            CommandKind.KeepOriginalExtent,
            CommandKind.StartStep,
            CommandKind.Retry,
            CommandKind.Skip,
            CommandKind.HandOff,
            CommandKind.SubmitManualCrop,
        ],

        [StepState.Skipped] = [],

        [StepState.Failed] =
        [
            CommandKind.KeepOriginalExtent,
            CommandKind.StartStep,
            CommandKind.Retry,
            CommandKind.Skip,
            CommandKind.HandOff,
            CommandKind.SubmitManualCrop,
        ],

        [StepState.Interrupted] =
        [
            CommandKind.KeepOriginalExtent,
            CommandKind.StartStep,
            CommandKind.Retry,
            CommandKind.Skip,
            CommandKind.HandOff,
        ],
    };

    /// <summary>Every command kind, for exhaustiveness tests.</summary>
    public static IReadOnlyList<CommandKind> AllCommands { get; } = Enum.GetValues<CommandKind>();

    /// <summary>Every step state, for exhaustiveness tests.</summary>
    public static IReadOnlyList<StepState> AllStepStates { get; } = Enum.GetValues<StepState>();

    /// <summary>True when the command is decided by session rules rather than by a step state.</summary>
    public static bool IsSessionScoped(CommandKind command) =>
        Array.IndexOf(SessionScopedCommands, command) >= 0;

    /// <summary>Looks up the explicit outcome for one state/command pair.</summary>
    public static TransitionOutcome Lookup(StepState state, CommandKind command)
    {
        if (IsSessionScoped(command))
        {
            return TransitionOutcome.SessionScoped;
        }

        CommandKind[] allowed = AllowedByState[state];
        return Array.IndexOf(allowed, command) >= 0
            ? TransitionOutcome.Allowed
            : TransitionOutcome.Rejected;
    }

    /// <summary>
    /// The state a step moves to when a step-scoped command is accepted.
    /// </summary>
    /// <remarks>
    /// Separated from <see cref="Lookup"/> so the legality question and the destination
    /// question can each be read on their own.
    /// </remarks>
    public static StepState Destination(CommandKind command, StepDefinition step) => command switch
    {
        CommandKind.ConfirmOriginal => StepState.Approved,
        CommandKind.StartStep => StepState.Processing,
        CommandKind.Approve => StepState.Approved,
        CommandKind.Reject => StepState.RetryRequired,

        // Retry returns the step to Waiting; the new attempt begins from a fresh working
        // copy when StartStep follows (MVP design §7.2).
        CommandKind.Retry => StepState.Waiting,

        CommandKind.Skip => StepState.Skipped,
        CommandKind.KeepOriginalExtent => StepState.Skipped,
        CommandKind.SetPrintDimensions => StepState.Approved,
        CommandKind.SetPresetFitSize => StepState.Approved,
        CommandKind.SetCustomTargetEdgeSize => StepState.Approved,

        // A submitted crop starts an attempt, exactly as StartStep does. Its result is a
        // Revision that still has to be reviewed; nothing here approves anything (Part C2 §17).
        CommandKind.SubmitManualCrop => StepState.Processing,

        // A produced result that needs no review is approved by construction: for
        // ApprovedPngExport the bytes are unchanged, so the upstream hash-bound approval
        // already covers them (plan §7.3).
        CommandKind.AttemptSucceeded => step.RequiresReview ? StepState.ReviewRequired : StepState.Approved,

        CommandKind.AttemptFailed => StepState.Failed,
        CommandKind.AttemptInterrupted => StepState.Interrupted,

        // A stopped attempt lands on Interrupted, not Failed. Nothing failed — a person ended
        // the run — and Interrupted is this workflow's existing "did not finish, produced
        // nothing" state, from which Retry, Skip and HandOff are already legal. Reusing it also
        // means a crash *during* a stop, which startup recovery closes as Interrupted, leaves
        // the step in the same place a completed stop would: no second recovery state, and no
        // duplicate (Epic 11300 Part D2A §12, §31).
        CommandKind.AttemptCancelled => StepState.Interrupted,

        // HandOff ends the session's automated progression; the step keeps its state.
        CommandKind.HandOff => StepState.ReviewRequired,

        _ => throw new ArgumentOutOfRangeException(
            nameof(command), command, "The command is not step-scoped and has no step destination."),
    };
}
