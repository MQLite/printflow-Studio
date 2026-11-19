using System.IO;
using System.Security.Cryptography;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Imaging;

public sealed class WebpRasterPreparer(IWorkspace workspace, IFileInspector inspector) : IWebpRasterPreparer
{
    public async Task<OperationResult<FileFacts>> PrepareAsync(
        WorkspaceFileRef source, Sha256 sourceHash, WorkspaceFileRef destination, CancellationToken cancellationToken)
    {
        string target = workspace.ResolveAbsolute(destination);
        try
        {
            await using FileStream input = new(workspace.ResolveAbsolute(source), FileMode.Open,
                FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
            if (Sha256.FromBytes(await SHA256.HashDataAsync(input, cancellationToken)) != sourceHash)
                return OperationResult.Fail<FileFacts>(FailureCode.RevisionIntegrityMismatch, "The managed WebP source changed during import.");

            input.Position = 0;
            string directory = Path.GetDirectoryName(target)!;
            ReparsePointGuard.RefuseAncestry(directory);
            Directory.CreateDirectory(directory);
            // A failed attempt keeps its working files as evidence. The session publishes
            // a revision only after the completed raster passes inspection below.
            await using (FileStream output = new(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await Task.Run(() => WebpRasterCodec.WritePng(input, output, cancellationToken), cancellationToken);
                await output.FlushAsync(cancellationToken);
            }

            var inspected = await inspector.InspectAsync(target, cancellationToken);
            if (inspected.IsFailure) return inspected;
            if (inspected.Value is not { Format: ImageFormat.Png, HasPixelDimensions: true })
                return OperationResult.Fail<FileFacts>(FailureCode.OutputUnreadable, "The prepared WebP raster is not a readable PNG.");

            File.SetAttributes(target, FileAttributes.ReadOnly);
            return inspected;
        }
        catch (InvalidDataException ex)
        {
            return OperationResult.Fail<FileFacts>(FailureCode.SourceImageUnreadable, ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return OperationResult.Fail<FileFacts>(FailureCode.WorkspaceError, $"The WebP raster could not be prepared: {ex.Message}");
        }
    }
}
