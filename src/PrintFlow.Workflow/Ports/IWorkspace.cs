using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;

namespace PrintFlow.Workflow.Ports;

/// <summary>
/// Owns every path PrintFlow ever writes to: session directories, the immutable source
/// snapshot, per-attempt working copies, and collision-safe promotion into
/// <c>Approved</c>/<c>Rejected</c> (Epic 11100 Task 11106b; plan §12).
/// </summary>
/// <remarks>
/// Everything outside this module speaks <see cref="WorkspaceFileRef"/>/<see cref="WorkspaceDirRef"/>,
/// never a raw string path (MVP design invariant 12). <see cref="ResolveAbsolute"/> and
/// <see cref="ResolveAbsoluteDirectory"/> are the only path joins in the system.
///
/// Every method below except <see cref="CreateSession"/> takes the session's own
/// <see cref="WorkspaceDirRef"/> rather than a bare <see cref="SessionId"/>: that reference is
/// exactly the <c>ProcessingSession.Workspace</c> value the caller already holds (freshly
/// created, or reloaded from persistence), so the workspace module never has to remember a
/// session-id-to-directory mapping of its own.
/// </remarks>
public interface IWorkspace
{
    /// <summary>Creates the session directory tree (<c>Source</c>, <c>Working</c>, <c>Approved</c>, <c>Rejected</c>, <c>Logs</c>).</summary>
    OperationResult<WorkspaceDirRef> CreateSession(SessionId id, DateTimeOffset createdUtc);

    /// <summary>
    /// Copies the operator's source file into <c>Source\</c> and marks the copy read-only.
    /// </summary>
    /// <remarks>
    /// The source at <paramref name="sourceAbsolutePath"/> is opened read-only exactly once
    /// and is never renamed, moved, or deleted (MVP design invariant 1). Hashing and metadata
    /// extraction are a separate <see cref="IFileInspector"/> call so a partially failed
    /// import never has to guess whether the copy is trustworthy.
    /// </remarks>
    Task<OperationResult<WorkspaceFileRef>> ImportSourceAsync(
        WorkspaceDirRef session, string sourceAbsolutePath, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a fresh, independent working copy of <paramref name="source"/> for one attempt.
    /// </summary>
    /// <remarks>
    /// Every attempt — first try or retry — gets its own <c>Working\&lt;attemptId&gt;\</c>
    /// directory, which is what makes "every retry starts from a clean working copy"
    /// structural rather than a convention to remember (MVP design invariant 8).
    /// </remarks>
    Task<OperationResult<WorkspaceFileRef>> CreateWorkingCopyAsync(
        WorkspaceDirRef session, AttemptId attemptId, WorkspaceFileRef source, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically reserves <paramref name="proposedFileName"/> in <paramref name="area"/>,
    /// trying collision suffixes from <paramref name="patterns"/> until one succeeds.
    /// </summary>
    /// <remarks>
    /// Uses <c>FileMode.CreateNew</c> under the hood so the guarantee is an OS-level
    /// atomic fact, not a check-then-write race (MVP design §9.4; plan §13.3). The reservation
    /// is a zero-byte placeholder; the caller writes the real bytes afterwards.
    /// </remarks>
    OperationResult<WorkspaceFileRef> ReserveOutput(
        WorkspaceDirRef session, WorkspaceArea area, string proposedFileName, NamingPatternSet patterns);

    /// <summary>Copies the bytes at <paramref name="source"/> unchanged into the reservation at <paramref name="reservedTarget"/>.</summary>
    Task<OperationResult<Unit>> WriteReservedAsync(
        WorkspaceFileRef reservedTarget, WorkspaceFileRef source, CancellationToken cancellationToken);

    /// <summary>Moves a file into <c>Rejected\</c> for retention until the session ends.</summary>
    Task<OperationResult<WorkspaceFileRef>> MoveToRejectedAsync(
        WorkspaceDirRef session, WorkspaceFileRef source, string fileName, CancellationToken cancellationToken);

    /// <summary>Removes safe-to-delete working copies once a session concludes.</summary>
    OperationResult<Unit> CleanupWorking(WorkspaceDirRef session);

    /// <summary>
    /// Lists every file currently present under this session's <c>Working\</c> area, each
    /// attributed to the attempt folder it sits in.
    /// </summary>
    /// <remarks>
    /// Deliberately scoped to <c>Working\</c> and nothing else: startup recovery is the only
    /// caller, and confining what it can even see to the one disposable area makes "recovery
    /// never touches <c>Source</c>, <c>Approved</c>, <c>Baseline</c> or <c>TestData</c>"
    /// (Epic 11100 Part 3B §8) a property of the seam rather than a rule recovery has to
    /// remember. A missing <c>Working\</c> directory is an empty list, not a failure.
    /// </remarks>
    OperationResult<IReadOnlyList<WorkingFileEntry>> ListWorkingFiles(WorkspaceDirRef session);

    /// <summary>
    /// Moves one orphaned working file into <c>Quarantine\</c>. Never deletes.
    /// </summary>
    /// <remarks>
    /// The reference must belong to <see cref="WorkspaceArea.Working"/>; anything else is
    /// refused rather than moved. That refusal is the second half of the containment above —
    /// even a caller holding an <c>Approved</c> reference cannot route it through here.
    /// </remarks>
    OperationResult<Unit> QuarantineWorkingFile(WorkspaceFileRef file, string reason);

    /// <summary>
    /// Records that a file exists on disk with no corresponding metadata (a commit that failed
    /// after the file write completed). Never deletes; only marks for later inspection.
    /// </summary>
    OperationResult<Unit> Quarantine(string absolutePath, string reason);

    /// <summary>Resolves a workspace file reference to an absolute path, guaranteed to be under the configured root.</summary>
    string ResolveAbsolute(WorkspaceFileRef reference);

    /// <summary>Resolves a workspace directory reference to an absolute path, guaranteed to be under the configured root.</summary>
    string ResolveAbsoluteDirectory(WorkspaceDirRef reference);
}

/// <summary>
/// The only deletion route PrintFlow ever calls (Epic 11100 Task 11106b; plan §12.2).
/// </summary>
/// <remarks>
/// There is deliberately no hard-delete path anywhere in the solution. A failure to move a
/// file to the Recycle Bin is a structured failure, never a silent fallback to permanent
/// deletion.
/// </remarks>
public interface IRecycleBin
{
    /// <summary>Sends the file at <paramref name="absolutePath"/> to the Windows Recycle Bin.</summary>
    OperationResult<Unit> SendToRecycleBin(string absolutePath);
}
