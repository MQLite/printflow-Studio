using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;

namespace PrintFlow.Workflow.Ports;

public sealed record PhotoshopBackgroundEditSource(
    WorkspaceDirRef SessionWorkspace, WorkspaceFileRef Image, FileFacts Facts, Guid EditId)
{
    public string PsdFileName => $"Cutout-{EditId.ToString("N")[..8]}.psd";
}

public sealed record PhotoshopBackgroundSave(string PsdPath, Sha256 PsdHash, string PreviewPath);

public interface IPhotoshopBackgroundEditor
{
    /// <summary>Opens an editable copy and yields readable saves while the caller is waiting.</summary>
    IAsyncEnumerable<OperationResult<PhotoshopBackgroundSave>> WatchSavesAsync(
        PhotoshopBackgroundEditSource source, CancellationToken cancellationToken);
}
