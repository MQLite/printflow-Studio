using PrintFlow.Domain.Results;

namespace PrintFlow.App.Navigation;

/// <summary>A single, current-screen-owned visit to utilities from a task; not a back stack.</summary>
public interface IUtilityTaskNavigation
{
    bool HasSourceTask { get; }
    bool IsCurrentUtility(object requester);
    Task<OperationFailure?> ReturnFromUtilityAsync(object requester, CancellationToken cancellationToken);
}
