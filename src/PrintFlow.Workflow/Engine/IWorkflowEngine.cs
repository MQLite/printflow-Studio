using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Effects;

namespace PrintFlow.Workflow.Engine;

/// <summary>
/// Applies one command to one workflow state and reports what should happen next.
/// </summary>
/// <remarks>
/// The engine is pure. It performs no I/O, reads no clock, generates no identifier, writes
/// no log, invokes no adapter, and holds no mutable state. Time and identifiers arrive in
/// <see cref="CommandContext"/>; required work is returned as
/// <see cref="WorkflowEffect"/> data for the application layer to interpret.
///
/// That purity is not decoration: it is what lets the entire transition matrix for all
/// three workflows be tested without a database, a disk, or a fake adapter, and it is what
/// stops a workflow rule from quietly acquiring a side effect later.
/// </remarks>
public interface IWorkflowEngine
{
    /// <summary>
    /// Applies <paramref name="command"/> to <paramref name="state"/>.
    /// </summary>
    /// <returns>
    /// An accepted transition carrying the new state and its effects, or a rejection. A
    /// rejection changes nothing and produces no effects.
    /// </returns>
    WorkflowTransition Apply(WorkflowSnapshot state, WorkflowCommand command, CommandContext context);

    /// <summary>
    /// The commands that would currently be accepted.
    /// </summary>
    /// <remarks>
    /// The UI enables buttons from this list, so legality has exactly one source of truth
    /// and a <see cref="CommandRejection"/> in production means a defect rather than an
    /// ordinary refusal (Epic 11100 plan §9.2).
    /// </remarks>
    IReadOnlyList<CommandKind> AvailableCommands(WorkflowSnapshot state);

    /// <summary>
    /// The steps <c>ReturnToStep</c> would currently accept, in workflow order
    /// (Epic 11200 Part C3 §4, §8).
    /// </summary>
    /// <remarks>
    /// <c>ReturnToStep</c> is the one command <see cref="AvailableCommands"/> cannot answer,
    /// and deliberately does not try to: its legality depends on <i>which</i> step, so a single
    /// stand-in target would report something no button is asking. A screen that needs to offer
    /// destinations therefore asks for the destinations rather than for a yes/no, and gets them
    /// from the same handler that will accept the click.
    /// <para>
    /// Every element of the result is a target the real command accepts, because each one is
    /// produced by actually applying <c>ReturnToStep(target)</c> and keeping the accepted ones.
    /// There is no separate list of "steps you can probably go back to" to drift out of step
    /// with the rule — which is what §4's "do not duplicate ReturnToStep legality in WPF"
    /// asks for, stated at the layer that owns the rule rather than promised at the one that
    /// reads it.
    /// </para>
    /// <para>
    /// An empty list means returning is not currently legal at all, and is the honest answer
    /// for a completed, handed-off or abandoned session and for a session still on its first
    /// step.
    /// </para>
    /// </remarks>
    IReadOnlyList<StepKind> AvailableReturnTargets(WorkflowSnapshot state);
}
