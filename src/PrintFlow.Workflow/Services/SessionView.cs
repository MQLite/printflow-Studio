using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Engine;

namespace PrintFlow.Workflow.Services;

/// <summary>
/// A flattened, UI-safe read model for one session (Epic 11100 plan §9.2).
/// </summary>
/// <remarks>
/// The UI binds to this and to nothing else: no <see cref="WorkflowSnapshot"/>, no
/// <see cref="Domain.Sessions.SessionStep"/> setter, no adapter reference. Available commands
/// come from the engine's own <c>AvailableCommands</c>, so a button is enabled by the same rule
/// that will accept the click — one source of truth for legality (MVP design invariant 12).
/// </remarks>
public sealed record SessionView(
    SessionId Id,
    WorkflowType WorkflowType,
    OutputName OutputName,
    SessionState State,
    IReadOnlyList<SessionStep> Steps,
    PrintDimensions? Dimensions,
    WhiteUnderbaseBranch? WhiteUnderbaseBranch,
    IReadOnlyList<CommandKind> AvailableCommands)
{
    public static SessionView From(WorkflowSnapshot snapshot, IReadOnlyList<CommandKind> availableCommands) => new(
        snapshot.SessionId,
        snapshot.WorkflowType,
        snapshot.OutputName,
        snapshot.SessionState,
        snapshot.Steps,
        snapshot.Dimensions,
        snapshot.WhiteUnderbaseBranch,
        availableCommands);
}
