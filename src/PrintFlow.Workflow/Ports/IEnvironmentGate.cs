using PrintFlow.Domain.Results;

namespace PrintFlow.Workflow.Ports;

/// <summary>
/// Which kind of implementation an adapter is: a deterministic local double, or a real
/// production automation of Meitu/Photoshop.
/// </summary>
/// <remarks>
/// Declared by the adapter itself (<see cref="IMeituProcessor.Mode"/>,
/// <see cref="IPhotoshopOutputProcessor.Mode"/>) rather than inferred from
/// <c>AdapterId</c>, so <see cref="IEnvironmentGate"/> never has to string-sniff an
/// identifier to decide what it is looking at.
/// </remarks>
public enum AdapterExecutionMode
{
    Fake,
    Production,
}

/// <summary>
/// The seam a future real workstation-verification pass (Epic 11500) will occupy.
/// </summary>
/// <remarks>
/// Epic 11100 provides only the foundation: a gate that lets a <see cref="AdapterExecutionMode.Fake"/>
/// adapter through unconditionally and refuses a <see cref="AdapterExecutionMode.Production"/>
/// one with <see cref="FailureCode.EnvironmentNotVerified"/>, because nothing in this Epic has
/// verified a real workstation (no Meitu/Photoshop executable check, no resolution/DPI check,
/// no display or Action-hash check — those belong to Epic 11500). The invariant this seam
/// exists to make structural: a production adapter can never be invoked without first passing
/// through here, regardless of what future implementation replaces today's foundation gate.
/// </remarks>
public interface IEnvironmentGate
{
    /// <summary>Decides whether a step backed by an adapter in <paramref name="mode"/> may proceed.</summary>
    OperationResult<Unit> Verify(AdapterExecutionMode mode);
}
