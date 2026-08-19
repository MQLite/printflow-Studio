using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Effects;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Workflow.Services;

/// <summary>
/// The single implementation of <see cref="IStartupRecoveryService"/> (Epic 11100 Part 3B).
/// </summary>
/// <remarks>
/// Ordering is fixed and deterministic (Part 3B §9):
/// <list type="number">
///   <item>read the automation lock and every persisted <c>Running</c> attempt;</item>
///   <item>establish whether the lock's owner is genuinely gone;</item>
///   <item>per session, one transaction: attempts to <c>Interrupted</c>, their steps to
///         <c>Interrupted</c>, and — for the lock-holding session only — the lock release;</item>
///   <item>release a confirmed-stale lock held by a session with no running attempt, in its
///         own transaction;</item>
///   <item>quarantine orphaned working files, after every commit has landed.</item>
/// </list>
/// File moves come last and are never claimed to be part of a transaction: SQLite cannot roll
/// a file move back. Crashing between the commit and the moves leaves the database correct and
/// some leftovers un-quarantined, which is the harmless direction — the reverse order would
/// let a crash lose files whose records still pointed at them.
/// </remarks>
public sealed class StartupRecoveryService : IStartupRecoveryService
{
    private readonly IWorkflowEngine _engine;
    private readonly ISessionRepository _repository;
    private readonly IWorkspace _workspace;
    private readonly IProcessLiveness _processLiveness;
    private readonly IIdGenerator _idGenerator;
    private readonly TimeProvider _timeProvider;
    private readonly int _processId;
    private readonly string _machineName;

    public StartupRecoveryService(
        IWorkflowEngine engine,
        ISessionRepository repository,
        IWorkspace workspace,
        IProcessLiveness processLiveness,
        IIdGenerator idGenerator,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(processLiveness);
        ArgumentNullException.ThrowIfNull(idGenerator);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _engine = engine;
        _repository = repository;
        _workspace = workspace;
        _processLiveness = processLiveness;
        _idGenerator = idGenerator;
        _timeProvider = timeProvider;
        _processId = Environment.ProcessId;
        _machineName = Environment.MachineName;
    }

    /// <inheritdoc />
    public async Task<OperationResult<StartupRecoveryReport>> RecoverAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset nowUtc = _timeProvider.GetUtcNow();
        List<StartupRecoveryEntry> entries = [];

        OperationResult<AutomationLockState> lockRead = await _repository.GetAutomationLockAsync(cancellationToken);
        if (lockRead.IsFailure)
        {
            return OperationResult.Fail<StartupRecoveryReport>(lockRead.Failure);
        }

        LockVerdict lockVerdict = VerifyLock(lockRead.Value, nowUtc, entries);

        OperationResult<IReadOnlyList<ProcessingAttempt>> running =
            await _repository.FindRunningAttemptsAsync(cancellationToken);
        if (running.IsFailure)
        {
            return OperationResult.Fail<StartupRecoveryReport>(running.Failure);
        }

        // An attempt belonging to a session whose owner is provably still running is not a
        // crash — it is work in progress in another live process. Leave it entirely alone.
        List<IGrouping<SessionId, ProcessingAttempt>> bySession = running.Value
            .Where(attempt => attempt.SessionId != lockVerdict.ProtectedSession)
            .GroupBy(attempt => attempt.SessionId)
            .OrderBy(group => group.Key.Value)
            .ToList();

        bool lockReleased = false;
        List<SessionId> recovered = [];

        foreach (IGrouping<SessionId, ProcessingAttempt> group in bySession)
        {
            bool releaseHere = lockVerdict.ReleaseStaleLock && !lockReleased && lockVerdict.HeldBy == group.Key;

            bool committed = await RecoverSessionAsync(
                group.Key, [.. group], releaseHere, nowUtc, entries, cancellationToken);

            if (!committed)
            {
                continue;
            }

            recovered.Add(group.Key);
            if (releaseHere)
            {
                lockReleased = true;
                entries.Add(new StartupRecoveryEntry(
                    StartupRecoveryAction.AutomationLockReleased, nowUtc, group.Key, null, null,
                    "The automation lock's owning process is gone; the lock was released."));
            }
        }

        if (lockVerdict.ReleaseStaleLock && !lockReleased && lockVerdict.HeldBy is SessionId orphanedLockOwner)
        {
            await ReleaseOrphanedLockAsync(orphanedLockOwner, nowUtc, entries, cancellationToken);
        }

        // File work only after every metadata commit has landed.
        foreach (SessionId sessionId in recovered)
        {
            await QuarantineOrphanWorkingFilesAsync(sessionId, nowUtc, entries, cancellationToken);
        }

        return OperationResult.Ok(new StartupRecoveryReport(entries));
    }

    // -------------------------------------------------------------------------------------
    // Stale-lock decision
    // -------------------------------------------------------------------------------------

    /// <summary>What the liveness check concluded about the persisted automation lock.</summary>
    /// <param name="HeldBy">The session recorded as holding the lock, if any.</param>
    /// <param name="ReleaseStaleLock">True only when the owning process is confirmed gone.</param>
    /// <param name="ProtectedSession">
    /// A session whose running attempts must not be touched, because its process may still be
    /// driving them. Null when nothing needs protecting.
    /// </param>
    private readonly record struct LockVerdict(
        SessionId? HeldBy, bool ReleaseStaleLock, SessionId? ProtectedSession);

    private LockVerdict VerifyLock(
        AutomationLockState state, DateTimeOffset nowUtc, List<StartupRecoveryEntry> entries)
    {
        if (!state.IsHeld || state.SessionId is not SessionId holder)
        {
            return new LockVerdict(null, ReleaseStaleLock: false, ProtectedSession: null);
        }

        ProcessLiveness liveness = state.ProcessId is int processId
            ? _processLiveness.Check(processId, state.MachineName)
            : ProcessLiveness.Unknown;

        if (liveness == ProcessLiveness.Dead)
        {
            return new LockVerdict(holder, ReleaseStaleLock: true, ProtectedSession: null);
        }

        // Alive or unverifiable: fail closed. The lock stays, and so does everything the
        // possibly-live owner might still be working on.
        entries.Add(new StartupRecoveryEntry(
            StartupRecoveryAction.AutomationLockRetained, nowUtc, holder, null, null,
            $"The automation lock is held and its owner is {liveness}; it was not released."));

        return new LockVerdict(holder, ReleaseStaleLock: false, ProtectedSession: holder);
    }

    private async Task ReleaseOrphanedLockAsync(
        SessionId holder, DateTimeOffset nowUtc, List<StartupRecoveryEntry> entries,
        CancellationToken cancellationToken)
    {
        OperationResult<SessionAggregate?> loaded = await _repository.LoadAsync(holder, cancellationToken);
        if (loaded.IsFailure || loaded.Value is not { } aggregate)
        {
            entries.Add(new StartupRecoveryEntry(
                StartupRecoveryAction.RecoveryFailed, nowUtc, holder, null, FailureCode.PersistenceError,
                "The automation lock's owning session could not be loaded; the lock was left held."));
            return;
        }

        SessionMutation mutation = SessionMutation.Empty(aggregate.Session) with
        {
            LockChange = new AutomationLockChange(
                AutomationLockAction.Release, holder, nowUtc, _processId, _machineName),
        };

        OperationResult<Unit> committed = await _repository.CommitAsync(mutation, cancellationToken);
        entries.Add(committed.IsSuccess
            ? new StartupRecoveryEntry(
                StartupRecoveryAction.AutomationLockReleased, nowUtc, holder, null, null,
                "The automation lock's owning process is gone; the lock was released.")
            : new StartupRecoveryEntry(
                StartupRecoveryAction.RecoveryFailed, nowUtc, holder, null, committed.Failure.Code,
                "Releasing the stale automation lock failed; it was left held."));
    }

    // -------------------------------------------------------------------------------------
    // Running to Interrupted
    // -------------------------------------------------------------------------------------

    /// <summary>Recovers one session's crashed attempts in a single transaction.</summary>
    /// <returns>True when the transaction committed.</returns>
    private async Task<bool> RecoverSessionAsync(
        SessionId sessionId, IReadOnlyList<ProcessingAttempt> crashed, bool releaseLock,
        DateTimeOffset nowUtc, List<StartupRecoveryEntry> entries, CancellationToken cancellationToken)
    {
        OperationResult<SessionAggregate?> loaded = await _repository.LoadAsync(sessionId, cancellationToken);
        if (loaded.IsFailure || loaded.Value is not { } aggregate)
        {
            entries.Add(new StartupRecoveryEntry(
                StartupRecoveryAction.RecoveryFailed, nowUtc, sessionId, null, FailureCode.PersistenceError,
                "A session with a running attempt could not be loaded; nothing was changed."));
            return false;
        }

        WorkflowSnapshot snapshot = aggregate.ToSnapshot();
        List<ProcessingAttempt> interruptedAttempts = [];
        List<StartupRecoveryEntry> pending = [];

        foreach (ProcessingAttempt attempt in crashed.OrderBy(a => a.StartedAtUtc).ThenBy(a => a.Id.Value))
        {
            // The attempt row is corrected unconditionally. Whatever the step records say, an
            // attempt whose process is gone is not running, and it never carries an output
            // Revision — Interrupt() cannot set one, and a database CHECK refuses one anyway.
            interruptedAttempts.Add(attempt.Interrupt(nowUtc));
            pending.Add(new StartupRecoveryEntry(
                StartupRecoveryAction.AttemptInterrupted, nowUtc, sessionId, attempt.Id, null,
                $"Attempt for step {attempt.Step} was still Running at startup and produced no Revision."));

            CommandContext context = new(
                nowUtc,
                CommandContext.UnknownOperator,
                ReviewId.From(_idGenerator.NewId()),
                AttemptId.From(_idGenerator.NewId()));

            WorkflowTransition transition = _engine.Apply(
                snapshot, new WorkflowCommand.System.AttemptInterrupted(attempt.Id, attempt.Step), context);

            if (transition.IsRejected)
            {
                // Recovery corrects records; it never forces a state the engine refuses. The
                // step is left exactly as persisted and the refusal is reported.
                pending.Add(new StartupRecoveryEntry(
                    StartupRecoveryAction.StepNotTransitioned, nowUtc, sessionId, attempt.Id,
                    FailureCode.PreconditionNotMet,
                    $"Step {attempt.Step} was left unchanged: {transition.Rejection}"));
                continue;
            }

            snapshot = transition.State;
            pending.Add(new StartupRecoveryEntry(
                StartupRecoveryAction.StepInterrupted, nowUtc, sessionId, attempt.Id, null,
                $"Step {attempt.Step} was moved to Interrupted; it holds no result."));
        }

        ProcessingSession updatedSession = aggregate.Session with
        {
            CurrentStep = snapshot.CurrentStep?.Step ?? aggregate.Session.CurrentStep,
            State = snapshot.SessionState,
            UpdatedAtUtc = nowUtc,
        };

        // The engine emits ReleaseAutomationLock alongside every AttemptInterrupted, and that
        // effect is deliberately *not* applied here. The engine reasons about one session and
        // cannot know who holds the singleton lock; obeying it would let a crashed session
        // release a lock a genuinely live process is holding. Ownership was settled once, up
        // front, by the liveness check.
        SessionMutation mutation = SessionMutation.Empty(updatedSession) with
        {
            UpsertSteps = snapshot.Steps,
            UpsertAttempts = interruptedAttempts,
            LockChange = releaseLock
                ? new AutomationLockChange(AutomationLockAction.Release, sessionId, nowUtc, _processId, _machineName)
                : null,
        };

        OperationResult<Unit> committed = await _repository.CommitAsync(mutation, cancellationToken);
        if (committed.IsFailure)
        {
            entries.Add(new StartupRecoveryEntry(
                StartupRecoveryAction.RecoveryFailed, nowUtc, sessionId, null, committed.Failure.Code,
                "Recovering this session's crashed attempts failed and was rolled back."));
            return false;
        }

        entries.AddRange(pending);
        return true;
    }

    // -------------------------------------------------------------------------------------
    // Orphan quarantine
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Moves safely attributable leftovers under this session's <c>Working\</c> area into
    /// <c>Quarantine\</c> (Part 3B §7).
    /// </summary>
    /// <remarks>
    /// A file is a candidate only when all three hold: it sits in a
    /// <c>Working\&lt;attemptId&gt;\</c> folder naming an attempt this session actually owns;
    /// that attempt ended Failed, Interrupted or Cancelled; and no Revision or PrintOutput of
    /// the session refers to it. The third condition is what protects a successful step's
    /// result, because for an adapter-backed step the Revision's file <i>is</i> the working
    /// copy. Anything failing a condition — including an unrecognised folder — is reported and
    /// left exactly where it is.
    ///
    /// The scan is confined to sessions this pass actually recovered rather than sweeping the
    /// whole workspace: recovery's business is what the crash left behind, and a startup that
    /// found nothing to recover has no business moving files at all.
    /// </remarks>
    private async Task QuarantineOrphanWorkingFilesAsync(
        SessionId sessionId, DateTimeOffset nowUtc, List<StartupRecoveryEntry> entries,
        CancellationToken cancellationToken)
    {
        OperationResult<SessionAggregate?> loaded = await _repository.LoadAsync(sessionId, cancellationToken);
        if (loaded.IsFailure || loaded.Value is not { } aggregate)
        {
            entries.Add(new StartupRecoveryEntry(
                StartupRecoveryAction.RecoveryFailed, nowUtc, sessionId, null, FailureCode.PersistenceError,
                "A recovered session could not be re-read; no working file was touched."));
            return;
        }

        OperationResult<IReadOnlyList<WorkingFileEntry>> working =
            _workspace.ListWorkingFiles(aggregate.Session.Workspace);
        if (working.IsFailure)
        {
            entries.Add(new StartupRecoveryEntry(
                StartupRecoveryAction.RecoveryFailed, nowUtc, sessionId, null, working.Failure.Code,
                "Working files could not be listed; none was touched."));
            return;
        }

        HashSet<string> referenced = new(StringComparer.OrdinalIgnoreCase);
        foreach (Revision revision in aggregate.Revisions)
        {
            referenced.Add(revision.File.RelativePath);
        }

        foreach (PrintOutput output in aggregate.Outputs)
        {
            referenced.Add(output.File.RelativePath);
        }

        Dictionary<Guid, AttemptStatus> attemptStatus = aggregate.Attempts
            .GroupBy(a => a.Id.Value)
            .ToDictionary(g => g.Key, g => g.Last().Status);

        foreach (WorkingFileEntry file in working.Value)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (referenced.Contains(file.File.RelativePath))
            {
                continue;
            }

            if (!Guid.TryParseExact(file.AttemptFolderName, "D", out Guid folderId) ||
                !attemptStatus.TryGetValue(folderId, out AttemptStatus status))
            {
                entries.Add(new StartupRecoveryEntry(
                    StartupRecoveryAction.UnattributedFileReported, nowUtc, sessionId, null, null,
                    $"'{file.File.RelativePath}' belongs to no known attempt and was left untouched."));
                continue;
            }

            if (status is not (AttemptStatus.Failed or AttemptStatus.Interrupted or AttemptStatus.Cancelled))
            {
                continue;
            }

            AttemptId attemptId = AttemptId.From(folderId);
            OperationResult<Unit> quarantined = _workspace.QuarantineWorkingFile(
                file.File,
                $"Startup recovery: leftover from {status} attempt {attemptId}, referenced by no Revision.");

            entries.Add(quarantined.IsSuccess
                ? new StartupRecoveryEntry(
                    StartupRecoveryAction.WorkingFileQuarantined, nowUtc, sessionId, attemptId, null,
                    $"'{file.File.RelativePath}' was moved to Quarantine.")
                : new StartupRecoveryEntry(
                    StartupRecoveryAction.RecoveryFailed, nowUtc, sessionId, attemptId, quarantined.Failure.Code,
                    $"'{file.File.RelativePath}' could not be quarantined and was left in place."));
        }
    }
}
