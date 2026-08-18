using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Reviews;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Outputs;

namespace PrintFlow.Workflow.Services;

/// <summary>One <c>Revision</c> being marked invalid, as part of a mutation.</summary>
public sealed record RevisionInvalidation(RevisionId RevisionId, InvalidationReason Reason, DateTimeOffset AtUtc);

/// <summary>Whether an automation-lock change acquires or releases the singleton lock.</summary>
public enum AutomationLockAction
{
    Acquire,
    Release,
}

/// <summary>A change to the singleton global automation lock (MVP design invariant 7).</summary>
public sealed record AutomationLockChange(
    AutomationLockAction Action, SessionId SessionId, DateTimeOffset AtUtc, int ProcessId, string MachineName);

/// <summary>
/// Everything one operator or system command needs written to metadata, as a single batch
/// (Epic 11100 Task 11108, §32–§34).
/// </summary>
/// <remarks>
/// <see cref="ISessionRepository.CommitAsync"/> writes every non-empty list here inside <b>one</b>
/// SQLite transaction. There is deliberately no smaller <c>SaveRevision()</c>/<c>SaveStep()</c>
/// API that could leave a workflow transition half-applied — <see cref="SessionService"/> is
/// the only place that builds one of these, from the effects <c>IWorkflowEngine</c> returned
/// for a single command.
/// </remarks>
public sealed record SessionMutation(
    ProcessingSession Session,
    IReadOnlyList<SessionStep> UpsertSteps,
    IReadOnlyList<Revision> NewRevisions,
    IReadOnlyList<RevisionInvalidation> RevisionInvalidations,
    IReadOnlyList<ProcessingAttempt> UpsertAttempts,
    IReadOnlyList<ReviewDecision> NewReviews,
    IReadOnlyList<PrintOutput> UpsertOutputs,
    InputSnapshot? NewSnapshot,
    AutomationLockChange? LockChange)
{
    public static SessionMutation Empty(ProcessingSession session) => new(
        session, [], [], [], [], [], [], null, null);
}
