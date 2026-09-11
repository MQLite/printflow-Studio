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
/// strings — everything crosses this seam as domain types. <see cref="CommitAsync"/> writes
/// session changes: one operator or system command produces one <see cref="SessionMutation"/>,
/// written in one transaction (plan §33). The separate verification-lock release compares its
/// observed owner token because that lease does not belong to a session aggregate.
/// </remarks>
public interface ISessionRepository
{
    /// <summary>Candidate unresolved interruptions, with no recent-work retention window.</summary>
    Task<OperationResult<IReadOnlyList<SessionId>>> FindRecoveryCandidatesAsync(CancellationToken cancellationToken);

    /// <summary>Loads a complete session aggregate, or null when no session has this id.</summary>
    Task<OperationResult<SessionAggregate?>> LoadAsync(SessionId id, CancellationToken cancellationToken);

    /// <summary>Lists the most recent sessions for the Home/Recent Processing screen.</summary>
    Task<OperationResult<IReadOnlyList<SessionListItem>>> ListRecentAsync(
        int maxCount, DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>Commits every change in <paramref name="mutation"/> as a single transaction.</summary>
    Task<OperationResult<Unit>> CommitAsync(SessionMutation mutation, CancellationToken cancellationToken);

    /// <summary>
    /// Takes one finished session's record off Recent Processing, permanently
    /// (Jira 11602 "delete-record"; MVP design §13.2 item 7).
    /// </summary>
    /// <remarks>
    /// Deliberately <b>not</b> a <see cref="SessionMutation"/>. A mutation is what one workflow
    /// command wrote, and taking a record off a list is not a workflow transition: it changes no
    /// step, no attempt, no Revision and no review, and it must remain impossible for it to do
    /// so. Its own narrow operation is what makes that structural rather than a promise
    /// (Epic 11100 plan §33's "one command, one transaction" is preserved — this is one
    /// operator action and one statement).
    /// <para>
    /// Nothing is destroyed. The session, its steps, its attempts, its Revisions, its reviews
    /// and its PrintOutputs stay exactly as they were, and no file is touched: the customer
    /// source, the InputSnapshot bytes, approved PNGs and production TIFFs are outside what this
    /// can reach at all. What ends is the entry's appearance in <see cref="ListRecentAsync"/>.
    /// </para>
    /// <para>
    /// Implementations must re-check safety in the same statement that writes, and refuse rather
    /// than write anything when the session is not a finished one, still holds the automation
    /// lock, or still has a running or interrupted attempt. The caller checks first; this is the
    /// check that cannot be raced.
    /// </para>
    /// </remarks>
    Task<OperationResult<Unit>> RemoveFromRecentAsync(
        SessionId id, DateTimeOffset atUtc, CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult.Fail<Unit>(FailureCode.PersistenceError,
            "This repository cannot remove a record from Recent Processing."));

    /// <summary>
    /// Finds every <see cref="ProcessingAttempt"/> still <c>Running</c> — the crash-detection
    /// query startup recovery uses to convert them to <c>Interrupted</c>.
    /// </summary>
    Task<OperationResult<IReadOnlyList<ProcessingAttempt>>> FindRunningAttemptsAsync(CancellationToken cancellationToken);

    /// <summary>Every completed session, without the Home screen's age/count retention limits.</summary>
    Task<OperationResult<IReadOnlyList<SessionId>>> FindCompletedSessionsAsync(CancellationToken cancellationToken);

    /// <summary>Reads this business database's automation correlation/recovery holder, if any.</summary>
    Task<OperationResult<AutomationLockState>> GetAutomationLockAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads the structured automation errors recorded for one session, oldest first
    /// (Jira 11108; MVP design §17.6).
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="LoadAsync"/> rather than folded into the aggregate: every
    /// command path loads an aggregate, and none of them reasons about the error log, so adding
    /// it there would put a query on the hot path for the benefit of no caller. The reader
    /// exists because "restart preserves history" is not a claim that can be made about records
    /// nothing can read back.
    /// <para>
    /// The default refuses rather than returning an empty list. A repository that cannot read
    /// the log must say so; answering "no errors" would be a lie a caller could not detect.
    /// </para>
    /// </remarks>
    Task<OperationResult<IReadOnlyList<Domain.Automation.AutomationLogEntry>>> LoadAutomationLogAsync(
        SessionId sessionId, CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult.Fail<IReadOnlyList<Domain.Automation.AutomationLogEntry>>(
            FailureCode.PersistenceError, "This repository cannot read the automation log."));

    /// <summary>
    /// Releases only the exact environment-verification owner observed dead by startup.
    /// This lock has no session aggregate; it must never use a session mutation to release it.
    /// </summary>
    Task<OperationResult<Unit>> ReleaseEnvironmentVerificationLockAsync(
        AutomationLockState observed, CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult.Fail<Unit>(FailureCode.PersistenceError,
            "This repository cannot release an environment-verification lock."));
}
