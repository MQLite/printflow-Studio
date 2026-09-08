using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Automation;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Reviews;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Files;

namespace PrintFlow.Workflow.Services;

/// <summary>One <c>Revision</c> being marked invalid, as part of a mutation.</summary>
public sealed record RevisionInvalidation(RevisionId RevisionId, InvalidationReason Reason, DateTimeOffset AtUtc);

/// <summary>One verified location switch or explicit end of rejected-result retention.</summary>
public sealed record RevisionRetentionChange(
    RevisionId RevisionId, WorkspaceFileRef ExpectedFile, Sha256 ExpectedHash,
    WorkspaceFileRef? PromotedFile, DateTimeOffset? ReleasedAtUtc);

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
    public IReadOnlyList<RevisionRetentionChange> RevisionRetentionChanges { get; init; } = [];

    /// <summary>
    /// Structured automation errors to append alongside the transition that produced them
    /// (Jira 11108; MVP design §17.6).
    /// </summary>
    /// <remarks>
    /// Part of the mutation rather than a separate best-effort write, because an entry here
    /// describes the <i>same</i> authoritative transition as the attempt row it accompanies. Were
    /// the two able to commit apart, a crash between them would leave either an attempt recorded
    /// as failed with no durable record of why, or a recorded error for a failure the database
    /// never accepted — both of which are exactly the "records remain consistent across failures"
    /// the task's acceptance forbids breaking. Committing them together introduces no new failure
    /// mode: the row is derived entirely from the failure the attempt already carries.
    /// <para>
    /// An empty list is the ordinary case. A success writes none, and so does a transition that
    /// produced no structured error.
    /// </para>
    /// </remarks>
    public IReadOnlyList<AutomationLogEntry> NewAutomationLog { get; init; } = [];

    /// <summary>
    /// Retention changes operate on the already committed completed aggregate. They must not
    /// upsert stale session metadata and accidentally turn a reopened session back to Completed.
    /// </summary>
    public bool IsRetentionMaintenance { get; init; }

    /// <summary>
    /// Step rows to delete, for a session that has been re-shaped onto a different workflow
    /// (Epic 11100 Part 3C3B, defect fix).
    /// </summary>
    /// <remarks>
    /// Stated explicitly rather than inferred as "whatever is not in
    /// <see cref="UpsertSteps"/>". Some commits legitimately carry no steps at all — startup
    /// recovery releasing a stale automation lock is one — and under an inferred rule such a
    /// commit would silently delete every step the session has. An empty list here means
    /// "remove nothing", which is the only safe default.
    /// <para>
    /// Only <c>SelectWorkflow</c> produces a non-empty one. Choosing a different workflow
    /// replaces the step list; without this the previous workflow's rows survived, and because
    /// <c>LoadAsync</c> reads every step row for the session, a reload rebuilt a snapshot
    /// containing steps the chosen workflow does not have — leaving the session apparently
    /// waiting on a step that is not part of it.
    /// </para>
    /// </remarks>
    public IReadOnlyList<StepKind> RemoveSteps { get; init; } = [];

    public static SessionMutation Empty(ProcessingSession session) => new(
        session, [], [], [], [], [], [], null, null);
}
