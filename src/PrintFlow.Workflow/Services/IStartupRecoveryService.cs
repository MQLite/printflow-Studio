using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;

namespace PrintFlow.Workflow.Services;

/// <summary>What one startup-recovery step did (Epic 11100 Part 3B §15).</summary>
public enum StartupRecoveryAction
{
    /// <summary>A persisted <c>Running</c> attempt was recorded as <c>Interrupted</c>.</summary>
    AttemptInterrupted,

    /// <summary>The step that attempt belonged to was moved to <c>Interrupted</c>.</summary>
    StepInterrupted,

    /// <summary>
    /// The attempt row was corrected but the engine refused the step transition, so the step
    /// was left exactly as persisted rather than forced.
    /// </summary>
    StepNotTransitioned,

    /// <summary>A confirmed-stale automation lock was released.</summary>
    AutomationLockReleased,

    /// <summary>A held automation lock was left alone: its owner is alive, or unverifiable.</summary>
    AutomationLockRetained,

    /// <summary>An orphaned working file was moved to <c>Quarantine\</c>.</summary>
    WorkingFileQuarantined,

    /// <summary>A working file could not be attributed to a known attempt, and was left untouched.</summary>
    UnattributedFileReported,

    /// <summary>Recovery could not complete one unit of work. Nothing was forced.</summary>
    RecoveryFailed,
}

/// <summary>
/// One recorded recovery action, in enough detail to reconstruct afterwards what startup did
/// and why (Epic 11100 Part 3B §15).
/// </summary>
/// <remarks>
/// Deliberately carries identifiers, workspace-relative references and a stable code — never
/// image bytes, baseline contents, or the customer's original source path. Those are exactly
/// the values Part 3B forbids in a recovery record.
/// </remarks>
public sealed record StartupRecoveryEntry(
    StartupRecoveryAction Action,
    DateTimeOffset AtUtc,
    SessionId? SessionId,
    AttemptId? AttemptId,
    FailureCode? FailureCode,
    string Detail);

/// <summary>Everything one startup-recovery pass did.</summary>
/// <remarks>
/// Returned rather than logged from inside the service: <c>PrintFlow.Workflow</c> references
/// <c>PrintFlow.Domain</c> and nothing else, so it has no logging package and must not gain
/// one. The composition root owns the decision about where these entries are written.
/// </remarks>
public sealed record StartupRecoveryReport(IReadOnlyList<StartupRecoveryEntry> Entries)
{
    public static StartupRecoveryReport Empty { get; } = new([]);

    /// <summary>Attempts converted from <c>Running</c> to <c>Interrupted</c> by this pass.</summary>
    public int InterruptedAttemptCount =>
        Entries.Count(e => e.Action == StartupRecoveryAction.AttemptInterrupted);

    /// <summary>Working files moved to <c>Quarantine\</c> by this pass.</summary>
    public int QuarantinedFileCount =>
        Entries.Count(e => e.Action == StartupRecoveryAction.WorkingFileQuarantined);

    /// <summary>True when this pass released a confirmed-stale automation lock.</summary>
    public bool ReleasedAutomationLock =>
        Entries.Any(e => e.Action == StartupRecoveryAction.AutomationLockReleased);

    /// <summary>True when this pass changed nothing — the expected outcome of a clean shutdown.</summary>
    public bool IsNoOp => Entries.Count == 0;
}

/// <summary>
/// Reconciles persisted state with reality once, at application startup
/// (Epic 11100 Part 3B; plan §18, §38).
/// </summary>
/// <remarks>
/// The crash invariant this exists to enforce is a negative one: a <c>Running</c> attempt plus
/// a dead process is <b>not</b> a result. Recovery therefore never inspects a leftover file to
/// decide whether the work "really" succeeded, and never creates a Revision or PrintOutput —
/// only the ordinary attempt pipeline may do that. It corrects records and quarantines
/// unattributable leftovers; everything else is left for the operator to retry.
///
/// Deliberately not part of <c>ISessionService</c> and deliberately not inside
/// <c>WorkflowEngine</c>: the engine stays a pure reducer, and recovery is a startup concern
/// that spans every session at once rather than driving one.
///
/// <see cref="RecoverAsync"/> must be called before this process acquires the automation lock
/// or starts any attempt — <see cref="Ports.IProcessLiveness"/> reads a claim naming this
/// process's own id as a recycled id from an earlier run, which is only sound at startup.
/// Running it twice is safe by design (Part 3B §10): a second pass finds nothing left to do.
/// </remarks>
public interface IStartupRecoveryService
{
    /// <summary>Runs one recovery pass over every persisted session.</summary>
    Task<OperationResult<StartupRecoveryReport>> RecoverAsync(CancellationToken cancellationToken);
}
