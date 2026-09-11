using PrintFlow.Domain.Results;

namespace PrintFlow.Workflow.Ports;

/// <summary>The passive state of the one workstation external-automation authority.</summary>
public enum WorkstationAutomationLeaseStatus
{
    Free,
    Busy,
    Owned,
    Unknown,
}

/// <summary>A bounded passive description; it is never itself permission to automate.</summary>
public sealed record WorkstationAutomationLeaseObservation(
    WorkstationAutomationLeaseStatus Status,
    string ResourceId,
    string Description);

/// <summary>
/// An unforgeable, temporary capability to drive PrintFlow's external applications.
/// </summary>
/// <remarks>
/// A caller must retain this object until its adapter and persistence unwind have completed.
/// Release is explicit because a failed release is material and must be reported.
/// </remarks>
public interface IWorkstationAutomationLease
{
    string ResourceId { get; }

    string OwnerToken { get; }

    bool IsActive { get; }

    Task<OperationResult<Unit>> ReleaseAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The single physical-control authority shared by the normal App and every real runner,
/// independently of their business databases.
/// </summary>
public interface IWorkstationAutomationLeaseManager
{
    string ResourceId { get; }

    /// <summary>
    /// Acquires a new workstation owner, or creates a nested scope from an explicitly supplied
    /// active owner. Process identity and ambient context never imply nesting.
    /// </summary>
    Task<OperationResult<IWorkstationAutomationLease>> TryAcquireAsync(
        IWorkstationAutomationLease? enclosingLease,
        CancellationToken cancellationToken);

    /// <summary>Reads Free/Busy/Owned/Unknown without acquiring or releasing ownership.</summary>
    Task<WorkstationAutomationLeaseObservation> ObserveAsync(
        IWorkstationAutomationLease? ownLease,
        CancellationToken cancellationToken);
}
