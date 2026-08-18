using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Engine;

namespace PrintFlow.Workflow.Effects;

/// <summary>
/// The complete result of applying one command: either a new state plus the effects it
/// requires, or a rejection. Never both.
/// </summary>
public sealed record WorkflowTransition
{
    private static readonly IReadOnlyList<WorkflowEffect> NoEffects = [];

    private WorkflowTransition(
        WorkflowSnapshot? newState,
        IReadOnlyList<WorkflowEffect> effects,
        CommandRejection? rejection)
    {
        NewState = newState;
        Effects = effects;
        Rejection = rejection;
    }

    /// <summary>The resulting state, or null when the command was rejected.</summary>
    public WorkflowSnapshot? NewState { get; }

    /// <summary>Work the application layer must perform. Empty on rejection.</summary>
    public IReadOnlyList<WorkflowEffect> Effects { get; }

    /// <summary>Why the command was refused, or null when it was accepted.</summary>
    public CommandRejection? Rejection { get; }

    public bool IsAccepted => Rejection is null;

    public bool IsRejected => Rejection is not null;

    /// <summary>The accepted state. Throws when the command was rejected — check first.</summary>
    public WorkflowSnapshot State => NewState
        ?? throw new InvalidOperationException(
            $"The command was rejected ({Rejection}); there is no new state.");

    public static WorkflowTransition Accepted(
        WorkflowSnapshot newState,
        params WorkflowEffect[] effects) =>
        new(newState, effects.Length == 0 ? NoEffects : effects, rejection: null);

    public static WorkflowTransition Accepted(
        WorkflowSnapshot newState,
        IReadOnlyList<WorkflowEffect> effects) =>
        new(newState, effects, rejection: null);

    public static WorkflowTransition Rejected(RejectionCode code, string debugMessage) =>
        new(newState: null, NoEffects, new CommandRejection(code, debugMessage));

    public static WorkflowTransition Rejected(CommandRejection rejection) =>
        new(newState: null, NoEffects, rejection);
}
