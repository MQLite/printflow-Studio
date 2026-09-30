using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.WorkstationEntry;

/// <summary>Real FileWorkspace behind run ownership; no alternative business state machine.</summary>
public sealed class ContainedWorkspace(OwnedPaths paths) : IWorkspace
{
    private readonly FileWorkspace inner = new(paths.At("workspace"));
    public string ResolveAbsolute(WorkspaceFileRef reference) => paths.Require(inner.ResolveAbsolute(reference), "workspace");
    public string ResolveAbsoluteDirectory(WorkspaceDirRef reference) => paths.Require(inner.ResolveAbsoluteDirectory(reference), "workspace");
    private void Check(WorkspaceFileRef file)
    {
        string path = ResolveAbsolute(file);
        if (File.Exists(path)) { using NativePathLease held = paths.Read(path, "workspace"); }
    }
    private void Check(WorkspaceDirRef directory) => ResolveAbsoluteDirectory(directory);
    public static string SessionDirectoryName(SessionId id, DateTimeOffset createdUtc) =>
        "S_" + createdUtc.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", System.Globalization.CultureInfo.InvariantCulture) + "_" + id.Value.ToString("N")[^8..];

    public OperationResult<WorkspaceDirRef> CreateSession(SessionId id, DateTimeOffset createdUtc)
    {
        paths.Require(paths.At("workspace"), "workspace");
        string expected = paths.At("workspace", "Sessions", SessionDirectoryName(id, createdUtc));
        paths.EnsureDirectory(expected);
        foreach (string area in new[] { "Source", "Working", "Approved", "Rejected", "Logs", "Revisions" }) paths.EnsureDirectory(Path.Combine(expected, area));
        paths.EnsureDirectory(paths.At("workspace", "Quarantine"));
        OperationResult<WorkspaceDirRef> result = inner.CreateSession(id, createdUtc);
        if (result.IsSuccess && ResolveAbsoluteDirectory(result.Value) != expected) throw new IOException("Product session directory differs from the protected naming contract.");
        return result;
    }
    public async Task<OperationResult<WorkspaceFileRef>> ImportSourceAsync(WorkspaceDirRef session, string sourceAbsolutePath, CancellationToken cancellationToken)
    {
        try { Check(session); using NativePathLease held = paths.Read(sourceAbsolutePath, "fixtures");
            return await inner.ImportSourceAsync(session, sourceAbsolutePath, cancellationToken); }
        catch (IOException ex) { return Refuse<WorkspaceFileRef>(ex); }
    }
    public async Task<OperationResult<WorkspaceFileRef>> CreateWorkingCopyAsync(WorkspaceDirRef session, AttemptId attemptId, WorkspaceFileRef source, CancellationToken cancellationToken)
    {
        try { Check(session); paths.EnsureDirectory(Path.Combine(ResolveAbsoluteDirectory(session), "Working", attemptId.Value.ToString("D"))); using NativePathLease held = paths.Read(ResolveAbsolute(source), "workspace");
            return await inner.CreateWorkingCopyAsync(session, attemptId, source, cancellationToken); }
        catch (IOException ex) { return Refuse<WorkspaceFileRef>(ex); }
    }
    public OperationResult<WorkspaceFileRef> ReserveOutput(WorkspaceDirRef session, WorkspaceArea area, string proposedFileName, NamingPatternSet patterns)
    { Check(session); return inner.ReserveOutput(session, area, proposedFileName, patterns); }
    public async Task<OperationResult<Unit>> WriteReservedAsync(WorkspaceFileRef reservedTarget, WorkspaceFileRef source, CancellationToken cancellationToken)
    {
        try { Check(reservedTarget); using NativePathLease held = paths.Read(ResolveAbsolute(source), "workspace");
            return await inner.WriteReservedAsync(reservedTarget, source, cancellationToken); }
        catch (IOException ex) { return Refuse<Unit>(ex); }
    }
    public Task<OperationResult<WorkspaceFileRef>> MoveToRejectedAsync(WorkspaceDirRef session, WorkspaceFileRef source, string fileName, CancellationToken cancellationToken)
    { Check(session); Check(source); return inner.MoveToRejectedAsync(session, source, fileName, cancellationToken); }
    public async Task<OperationResult<WorkspaceFileRef>> PromoteRevisionAsync(WorkspaceDirRef session, RevisionId revisionId, RetentionFile source, CancellationToken cancellationToken)
    { Check(session); paths.EnsureDirectory(Path.Combine(ResolveAbsoluteDirectory(session), "Revisions", revisionId.ToString())); using NativePathLease held = paths.Read(ResolveAbsolute(source.File), "workspace"); return await inner.PromoteRevisionAsync(session, revisionId, source, cancellationToken); }
    public OperationResult<Unit> VerifyRetentionFiles(WorkspaceDirRef session, IReadOnlyList<RetentionFile> files)
    { Check(session); foreach (RetentionFile file in files) Check(file.File); return inner.VerifyRetentionFiles(session, files); }
    public OperationResult<WorkingCleanupResult> CleanupWorking(WorkspaceDirRef session, WorkingCleanupPlan plan)
    { Check(session); foreach (RetentionFile file in plan.Preserve.Concat(plan.Delete)) Check(file.File); return inner.CleanupWorking(session, plan); }
    public OperationResult<IReadOnlyList<WorkingFileEntry>> ListWorkingFiles(WorkspaceDirRef session)
    {
        Check(session);
        string working = Path.Combine(ResolveAbsoluteDirectory(session), "Working");
        if (Directory.Exists(working))
            foreach (string directory in Directory.EnumerateDirectories(working)) paths.Require(directory, "workspace");
        OperationResult<IReadOnlyList<WorkingFileEntry>> result = inner.ListWorkingFiles(session);
        return result.IsFailure ? result : OperationResult.Ok<IReadOnlyList<WorkingFileEntry>>(result.Value.Where(entry => !paths.IsOwnedMarker(inner.ResolveAbsolute(entry.File))).ToArray());
    }
    public OperationResult<Unit> QuarantineWorkingFile(WorkspaceFileRef file)
        => QuarantineWorkingFile(file, "owned recovery");
    public OperationResult<Unit> QuarantineWorkingFile(WorkspaceFileRef file, string reason)
    { Check(file); paths.EnsureDirectory(paths.At("workspace", "Quarantine")); return inner.QuarantineWorkingFile(file, reason); }
    public OperationResult<Unit> Quarantine(string absolutePath, string reason)
    { paths.Require(absolutePath, "workspace"); paths.EnsureDirectory(paths.At("workspace", "Quarantine")); return inner.Quarantine(absolutePath, reason); }
    private static OperationResult<T> Refuse<T>(Exception ex) => OperationResult.Fail<T>(FailureCode.WorkspaceError, "Isolated entry refused: " + ex.Message);
}
