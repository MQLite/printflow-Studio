using PrintFlow.Domain.Results;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Gate;

/// <summary>
/// Epic 11100's foundation implementation of <see cref="IEnvironmentGate"/>: no real
/// workstation verification (plan §8; Epic 11500 owns that).
/// </summary>
/// <remarks>
/// Deliberately inspects nothing — no Meitu/Photoshop executable, no resolution/DPI, no
/// display, no Action hash, no application window. It exists only to make one fact structural
/// ahead of Epic 11500 actually implementing verification: a <see cref="AdapterExecutionMode.Production"/>
/// adapter is refused, never silently allowed through as if it were verified.
/// </remarks>
public sealed class FoundationEnvironmentGate : IEnvironmentGate
{
    /// <inheritdoc />
    public OperationResult<Unit> Verify(AdapterExecutionMode mode) => mode switch
    {
        AdapterExecutionMode.Fake => OperationResult.Ok(),
        AdapterExecutionMode.Production => OperationResult.Fail<Unit>(
            FailureCode.EnvironmentNotVerified,
            "No production adapter may run until Epic 11500 verifies the workstation environment. " +
            "This foundation gate never performs that verification itself."),
        _ => OperationResult.Fail<Unit>(
            FailureCode.EnvironmentNotVerified, $"Unknown adapter execution mode '{mode}'."),
    };
}
