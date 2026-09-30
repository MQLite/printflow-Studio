using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.WorkstationEntry;

public sealed class ContainedCorrection(OwnedPaths paths, IWorkspace workspace) : ICorrectionPackageStore
{
    private readonly FileCorrectionPackageStore inner = new(workspace);
    public async Task<OperationResult<CorrectionFileOutcome>> PlaceVerifiedCopyAsync(WorkspaceDirRef folder, string fileName,
        WorkspaceFileRef source, Sha256 expected, string partialSuffix, bool markReadOnly, CancellationToken cancellationToken)
    {
        Check(folder, fileName);
        using NativePathLease held = paths.Read(workspace.ResolveAbsolute(source), "workspace");
        return await inner.PlaceVerifiedCopyAsync(folder, fileName, source, expected, partialSuffix, markReadOnly, cancellationToken);
    }
    public Task<OperationResult<CorrectionFileOutcome>> WriteTextOnceAsync(WorkspaceDirRef folder, string fileName, string content, CancellationToken cancellationToken)
    { Check(folder, fileName); return inner.WriteTextOnceAsync(folder, fileName, content, cancellationToken); }
    public bool Exists(WorkspaceDirRef folder, string fileName) { Check(folder, fileName); return inner.Exists(folder, fileName); }
    public string ResolveFolder(WorkspaceDirRef folder) => paths.Require(inner.ResolveFolder(folder), "workspace");
    private void Check(WorkspaceDirRef folder, string leaf)
    {
        string directory = workspace.ResolveAbsoluteDirectory(folder);
        paths.EnsureDirectory(directory);
        if (Path.GetFileName(leaf) != leaf) throw new IOException("Correction output must be a leaf.");
        paths.CheckWritable(Path.Combine(directory, leaf), "workspace");
    }
}
