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
    HandOff,
    SetPrintDimensions,
    SelectWhiteUnderbaseBranch,
    ReturnToStep,
    Complete,
    AddAnotherSize,
    AbandonSession,
    AttemptSucceeded,
    AttemptFailed,
    AttemptInterrupted,
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
        CommandKind.ReturnToStep,
        CommandKind.Complete,
        CommandKind.AddAnotherSize,
        CommandKind.AbandonSession,
    ];

    /// <summary>
    /// The step-state rows of plan §8.3. A pair absent from a row is rejected.
    /// </summary>
    private static readonly Dictionary<StepState, CommandKind[]> AllowedByState = new()
    {
        [StepState.Waiting] =
        [
            CommandKind.ConfirmOriginal,
            CommandKind.StartStep,
            CommandKind.Skip,
            CommandKind.SetPrintDimensions,
        ],

        // A running attempt is finished by the system, never by the operator.
        [StepState.Processing] =
        [
            CommandKind.AttemptSucceeded,
            CommandKind.AttemptFailed,
            CommandKind.AttemptInterrupted,
        ],

        [StepState.ReviewRequired] =
        [
            CommandKind.Approve,
            CommandKind.Reject,
            CommandKind.HandOff,
        ],

        // A finished step is reopened only by returning to it, which is session-scoped.
        [StepState.Approved] = [],

        [StepState.RetryRequired] =
        [
            CommandKind.StartStep,
            CommandKind.Retry,
            CommandKind.Skip,
            CommandKind.HandOff,
        ],

        [StepState.Skipped] = [],

        [StepState.Failed] =
        [
            CommandKind.StartStep,
            CommandKind.Retry,
            CommandKind.Skip,
            CommandKind.HandOff,
        ],

        [StepState.Interrupted] =
        [
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
        CommandKind.SetPrintDimensions => StepState.Approved,

        // A produced result that needs no review is approved by construction: for
        // ApprovedPngExport the bytes are unchanged, so the upstream hash-bound approval
        // already covers them (plan §7.3).
        CommandKind.AttemptSucceeded => step.RequiresReview ? StepState.ReviewRequired : StepState.Approved,

        CommandKind.AttemptFailed => StepState.Failed,
        CommandKind.AttemptInterrupted => StepState.Interrupted,

        // HandOff ends the session's automated progression; the step keeps its state.
        CommandKind.HandOff => StepState.ReviewRequired,

        _ => throw new ArgumentOutOfRangeException(
            nameof(command), command, "The command is not step-scoped and has no step destination."),
    };
}
