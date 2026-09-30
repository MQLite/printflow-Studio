using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Infrastructure.Adapters.Photoshop;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.WorkstationEntry;

public sealed class ContainedFileInspector(OwnedPaths paths) : IFileInspector
{
    private readonly WicFileInspector inner = new();
    public async Task<OperationResult<FileFacts>> InspectAsync(string absolutePath, CancellationToken cancellationToken)
    {
        try { using NativePathLease held = paths.Read(absolutePath, "read"); return await inner.InspectAsync(absolutePath, cancellationToken); }
        catch (IOException ex) { return OperationResult.Fail<FileFacts>(FailureCode.WorkspaceError, ex.Message); }
    }
}

public sealed class ContainedDiagnosticDecoder(OwnedPaths paths, IDiagnosticImagePreviewDecoder inner) : IDiagnosticImagePreviewDecoder
{
    public async Task<OperationResult<DecodedPreview>> DecodeDiagnosticAsync(string persistedAbsolutePath, CancellationToken cancellationToken)
    {
        try { using NativePathLease held = paths.Read(persistedAbsolutePath, "evidence"); return await inner.DecodeDiagnosticAsync(persistedAbsolutePath, cancellationToken); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { return OperationResult.Fail<DecodedPreview>(FailureCode.WorkspaceError, "Diagnostic source refused before decoding: " + ex.Message); }
    }
}

public sealed class ContainedPreviewDecoder(OwnedPaths paths, IWorkspace workspace) : IImagePreviewDecoder
{
    private readonly WicImagePreviewDecoder inner = new(workspace);
    public int MaximumDisplayEdge => inner.MaximumDisplayEdge;
    public int ThumbnailEdge => inner.ThumbnailEdge;
    public async Task<OperationResult<DecodedPreview>> DecodeAsync(WorkspaceFileRef file, CancellationToken cancellationToken)
    { using NativePathLease held = paths.Read(workspace.ResolveAbsolute(file), "workspace"); return await inner.DecodeAsync(file, cancellationToken); }
    public async Task<OperationResult<DecodedPreview>> DecodeThumbnailAsync(WorkspaceFileRef file, CancellationToken cancellationToken)
    { using NativePathLease held = paths.Read(workspace.ResolveAbsolute(file), "workspace"); return await inner.DecodeThumbnailAsync(file, cancellationToken); }
}

public sealed class ContainedTiffDecoder(OwnedPaths paths, IWorkspace workspace) : ITiffReviewDecoder
{
    private readonly ProductionTiffReviewDecoder inner = new(workspace);
    public int MaximumDisplayEdge => inner.MaximumDisplayEdge;
    public async Task<OperationResult<DecodedTiffReview>> DecodeAsync(WorkspaceFileRef file, Sha256 expectedSha256, CancellationToken cancellationToken)
    { using NativePathLease held = paths.Read(workspace.ResolveAbsolute(file), "workspace"); return await inner.DecodeAsync(file, expectedSha256, cancellationToken); }
}

public sealed class ContainedManualImporter(OwnedPaths paths, IWorkspace workspace, IFileInspector inspector) : IManualResultImporter
{
    private readonly WicManualResultImporter inner = new(workspace, inspector);
    public Task<OperationResult<ManualResult>> ImportAsync(WorkspaceDirRef session, AttemptId attempt, StepKind step, FileFacts upstream, string selectedPath, CancellationToken cancellationToken)
        => ImportAsync(session, attempt, step, upstream, selectedPath, null, cancellationToken);
    public async Task<OperationResult<ManualResult>> ImportAsync(WorkspaceDirRef session, AttemptId attempt, StepKind step, FileFacts upstream, string selectedPath, Sha256? expectedSelectedHash, CancellationToken cancellationToken)
    { paths.EnsureDirectory(Path.Combine(workspace.ResolveAbsoluteDirectory(session), "Working", attempt.Value.ToString("D"))); using NativePathLease held = paths.Read(selectedPath, "fixtures"); return await inner.ImportAsync(session, attempt, step, upstream, selectedPath, expectedSelectedHash, cancellationToken); }
    public async Task<OperationResult<ManualResultPreflight>> PreflightAsync(StepKind step, FileFacts upstream, string selectedPath, CancellationToken cancellationToken)
    {
        try { using NativePathLease held = paths.Read(selectedPath, "fixtures"); return await inner.PreflightAsync(step, upstream, selectedPath, cancellationToken); }
        catch (IOException ex) { return OperationResult.Fail<ManualResultPreflight>(FailureCode.WorkspaceError, ex.Message); }
    }
    public async Task<OperationResult<Sha256>> HashSelectedAsync(string selectedPath, CancellationToken cancellationToken)
    { using NativePathLease held = paths.Read(selectedPath, "fixtures"); return await inner.HashSelectedAsync(selectedPath, cancellationToken); }
}
