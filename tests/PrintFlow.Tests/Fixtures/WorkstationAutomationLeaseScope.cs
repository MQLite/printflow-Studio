using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Fixtures;

/// <summary>
/// Explicit admission for opt-in smoke callers that invoke a real automation foundation directly,
/// outside <c>SessionService</c> or the live verifier. Calling this method is itself opt-in work:
/// every caller must place it after its environment-variable guard.
/// </summary>
internal sealed class WorkstationAutomationLeaseScope : IAsyncDisposable
{
    private readonly IWorkstationAutomationLease _lease;

    private WorkstationAutomationLeaseScope(IWorkstationAutomationLease lease) => _lease = lease;

    internal IWorkstationAutomationLease Lease => _lease;

    internal static Task<WorkstationAutomationLeaseScope> AcquireDefaultAsync(
        CancellationToken cancellationToken = default) =>
        AcquireAsync(new SqliteWorkstationAutomationLeaseManager(), cancellationToken);

    internal static async Task<WorkstationAutomationLeaseScope> AcquireAsync(
        IWorkstationAutomationLeaseManager manager,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manager);
        OperationResult<IWorkstationAutomationLease> acquired = await manager
            .TryAcquireAsync(enclosingLease: null, cancellationToken)
            .ConfigureAwait(false);
        if (acquired.IsFailure)
        {
            throw new InvalidOperationException(
                $"The opt-in live caller could not acquire workstation automation ownership: " +
                acquired.Failure.TechnicalDetail);
        }

        return new WorkstationAutomationLeaseScope(acquired.Value);
    }

    public async ValueTask DisposeAsync()
    {
        OperationResult<PrintFlow.Domain.Results.Unit> released = await _lease
            .ReleaseAsync(CancellationToken.None)
            .ConfigureAwait(false);
        if (released.IsFailure)
        {
            throw new InvalidOperationException(
                $"The opt-in live caller could not release workstation automation ownership: " +
                released.Failure.TechnicalDetail);
        }
    }
}
