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
}
