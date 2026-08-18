using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Workflow.Ports;

/// <summary>
/// Session metadata persistence (Epic 11100 Task 11108; plan §11.4).
/// </summary>
/// <remarks>
/// The workflow/business layer never sees <c>SqliteConnection</c>, Dapper types, or SQL
/// strings — everything crosses this seam as domain types. <see cref="CommitAsync"/> is the
/// only write path: one operator or system command produces one <see cref="SessionMutation"/>,
/// written in one transaction (plan §33).
/// </remarks>
public interface ISessionRepository
{
    /// <summary>Loads a complete session aggregate, or null when no session has this id.</summary>
    Task<OperationResult<SessionAggregate?>> LoadAsync(SessionId id, CancellationToken cancellationToken);

    /// <summary>Lists the most recent sessions for the Home/Recent Processing screen.</summary>
    Task<OperationResult<IReadOnlyList<SessionListItem>>> ListRecentAsync(
        int maxCount, DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>Commits every change in <paramref name="mutation"/> as a single transaction.</summary>
    Task<OperationResult<Unit>> CommitAsync(SessionMutation mutation, CancellationToken cancellationToken);

    /// <summary>
    /// Finds every <see cref="ProcessingAttempt"/> still <c>Running</c> — the crash-detection
    /// query startup recovery uses to convert them to <c>Interrupted</c>.
    /// </summary>
    Task<OperationResult<IReadOnlyList<ProcessingAttempt>>> FindRunningAttemptsAsync(CancellationToken cancellationToken);

    /// <summary>Reads the current holder of the singleton global automation lock, if any.</summary>
    Task<OperationResult<AutomationLockState>> GetAutomationLockAsync(CancellationToken cancellationToken);
}
