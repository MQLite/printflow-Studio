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

/// <summary>
/// Production gate extension used after an operation has atomically acquired the workstation
/// lease. The explicit capability lets reinspection recognise its own owner without weakening
/// any other workstation check.
/// </summary>
public interface IWorkstationScopedEnvironmentGate : IEnvironmentGate
{
    OperationResult<Unit> Verify(
        AdapterExecutionMode mode,
        IWorkstationAutomationLease workstationLease);
}

/// <summary>
/// Production gate extension for work that uses the verified workstation configuration but does
/// not control the shared Meitu/Photoshop automation domain.
/// </summary>
/// <remarks>
/// This is an explicit invocation context, not an ownership capability. It may omit only the
/// physical automation-availability condition; every other production workstation check remains
/// required. Today the sole caller is in-process PDF inspection and raster preparation.
/// </remarks>
public interface IInternalProductionEnvironmentGate : IEnvironmentGate
{
    OperationResult<Unit> VerifyForInternalWork(AdapterExecutionMode mode);
}
