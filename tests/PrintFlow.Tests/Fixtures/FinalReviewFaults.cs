using System.IO;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Fixtures;

/// <summary>
/// A workspace that behaves exactly like the real one until a named fault is armed
/// (Epic 11400 Part C2B §32, §33).
/// </summary>
/// <remarks>
/// A decorator rather than a mock: every call it does not deliberately break goes to the real
/// <see cref="Infrastructure.Workspace.FileWorkspace"/> and really touches the disk, so a test
/// about a failed promotion is still a test about the real reservation, the real collision
/// numbering and the real bytes. Only the one step under examination is made to fail.
/// </remarks>
internal sealed class FaultingWorkspace : IWorkspace
{
    private readonly IWorkspace _inner;

    public FaultingWorkspace(IWorkspace inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <summary>When set, <see cref="WriteReservedAsync"/> fails with this code.</summary>
    public FailureCode? WriteReservedFailsWith { get; set; }

    /// <summary>
    /// When true, <see cref="WriteReservedAsync"/> reports success but writes different bytes —
    /// the post-copy hash mismatch §32 asks for, and the one failure mode a copy cannot report
    /// about itself.
    /// </summary>
    public bool CorruptWrittenBytes { get; set; }

    /// <summary>Every path this workspace was asked to quarantine, in call order.</summary>
    public List<string> Quarantined { get; } = [];

    public OperationResult<WorkspaceDirRef> CreateSession(SessionId id, DateTimeOffset createdUtc) =>
        _inner.CreateSession(id, createdUtc);

    public Task<OperationResult<WorkspaceFileRef>> ImportSourceAsync(
        WorkspaceDirRef session, string sourceAbsolutePath, CancellationToken cancellationToken) =>
        _inner.ImportSourceAsync(session, sourceAbsolutePath, cancellationToken);

    public Task<OperationResult<WorkspaceFileRef>> CreateWorkingCopyAsync(
        WorkspaceDirRef session, AttemptId attemptId, WorkspaceFileRef source, CancellationToken cancellationToken) =>
        _inner.CreateWorkingCopyAsync(session, attemptId, source, cancellationToken);

    public OperationResult<WorkspaceFileRef> ReserveOutput(
        WorkspaceDirRef session, WorkspaceArea area, string proposedFileName, NamingPatternSet patterns) =>
        _inner.ReserveOutput(session, area, proposedFileName, patterns);

    public async Task<OperationResult<PrintFlow.Domain.Results.Unit>> WriteReservedAsync(
        WorkspaceFileRef reservedTarget, WorkspaceFileRef source, CancellationToken cancellationToken)
    {
        if (WriteReservedFailsWith is FailureCode code)
        {
            return OperationResult.Fail<PrintFlow.Domain.Results.Unit>(
                code, $"Simulated I/O failure writing '{reservedTarget.RelativePath}'.");
        }

        OperationResult<PrintFlow.Domain.Results.Unit> written =
            await _inner.WriteReservedAsync(reservedTarget, source, cancellationToken);

        if (written.IsSuccess && CorruptWrittenBytes)
        {
            await File.WriteAllTextAsync(
                ResolveAbsolute(reservedTarget), "not the reviewed TIFF", cancellationToken);
        }

        return written;
    }

    public Task<OperationResult<WorkspaceFileRef>> MoveToRejectedAsync(
        WorkspaceDirRef session, WorkspaceFileRef source, string fileName, CancellationToken cancellationToken) =>
        _inner.MoveToRejectedAsync(session, source, fileName, cancellationToken);

    public OperationResult<PrintFlow.Domain.Results.Unit> CleanupWorking(WorkspaceDirRef session) =>
        _inner.CleanupWorking(session);

    public OperationResult<IReadOnlyList<WorkingFileEntry>> ListWorkingFiles(WorkspaceDirRef session) =>
        _inner.ListWorkingFiles(session);

    public OperationResult<PrintFlow.Domain.Results.Unit> QuarantineWorkingFile(WorkspaceFileRef file, string reason) =>
        _inner.QuarantineWorkingFile(file, reason);

    public OperationResult<PrintFlow.Domain.Results.Unit> Quarantine(string absolutePath, string reason)
    {
        Quarantined.Add(absolutePath);
        return _inner.Quarantine(absolutePath, reason);
    }

    public string ResolveAbsolute(WorkspaceFileRef reference) => _inner.ResolveAbsolute(reference);

    public string ResolveAbsoluteDirectory(WorkspaceDirRef reference) => _inner.ResolveAbsoluteDirectory(reference);
}

/// <summary>
/// A repository that stops committing once it has committed a given number of times
/// (Epic 11400 Part C2B §33, §34).
/// </summary>
/// <remarks>
/// This is how a crash is simulated deterministically. A real crash between two transactions
/// leaves the first one on disk and the second one absent; failing the <i>n</i>th commit leaves
/// exactly that, with the difference that the test can then build a fresh service against the same
/// database and ask what the next process sees — which is the question the crash cases are really
/// about.
/// <para>
/// Everything before the fault is a real SQLite write, so the state the "restarted" service reads
/// back is the state a real crash would have left, not a fixture's idea of it.
/// </para>
/// </remarks>
internal sealed class FaultingRepository : ISessionRepository
{
    private readonly ISessionRepository _inner;
    private int _commits;

    public FaultingRepository(ISessionRepository inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <summary>The 1-based commit that fails, and every commit after it. Null commits everything.</summary>
    public int? FailFromCommit { get; set; }

    /// <summary>How many commits were attempted.</summary>
    public int AttemptedCommits => _commits;

    public Task<OperationResult<SessionAggregate?>> LoadAsync(SessionId id, CancellationToken cancellationToken) =>
        _inner.LoadAsync(id, cancellationToken);

    public Task<OperationResult<IReadOnlyList<SessionListItem>>> ListRecentAsync(
        int maxCount, DateTimeOffset since, CancellationToken cancellationToken) =>
        _inner.ListRecentAsync(maxCount, since, cancellationToken);

    public Task<OperationResult<PrintFlow.Domain.Results.Unit>> CommitAsync(
        SessionMutation mutation, CancellationToken cancellationToken)
    {
        _commits++;
        return FailFromCommit is int from && _commits >= from
            ? Task.FromResult(OperationResult.Fail<PrintFlow.Domain.Results.Unit>(
                FailureCode.PersistenceError, $"Simulated crash: commit {_commits} never landed."))
            : _inner.CommitAsync(mutation, cancellationToken);
    }

    public Task<OperationResult<IReadOnlyList<ProcessingAttempt>>> FindRunningAttemptsAsync(
        CancellationToken cancellationToken) =>
        _inner.FindRunningAttemptsAsync(cancellationToken);

    public Task<OperationResult<AutomationLockState>> GetAutomationLockAsync(CancellationToken cancellationToken) =>
        _inner.GetAutomationLockAsync(cancellationToken);
}
