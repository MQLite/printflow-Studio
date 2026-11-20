using System.IO;
using System.Security.Cryptography;
using PrintFlow.Domain.AiDesign;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.AiDesign.Authoring;
using PrintFlow.Workflow.AiDesign;

namespace PrintFlow.Infrastructure.AiDesign;

public sealed class CloudDesignPdfExport(FileAiDesignRequestStore store) : ICloudDesignPdfExport
{
    public async Task<OperationResult<Unit>> SaveAsync(Guid requestId, string destination, CancellationToken ct)
    {
        try
        {
            if (!Path.IsPathFullyQualified(destination) ||
                !".pdf".Equals(Path.GetExtension(destination), StringComparison.OrdinalIgnoreCase))
                return Failed();

            var result = await store.FindSnapshotAsync(requestId, ct).ConfigureAwait(false);
            if (result.IsFailure || result.Value is not { State: AiDesignState.Imported, CloudReceipt: { } receipt })
                return Failed();

            string path = Path.Combine(store.RequestsRoot, requestId.ToString("D"), AiDesignOwnedStagingFiles.OpenAiPdfName);
            await using var source = VerifiedAiAttachmentReader.OpenRegular(path);
            if (source.Length is <= 5 or > CloudDesignLimits.MaxArtifactBytes ||
                Sha256.FromBytes(await SHA256.HashDataAsync(source, ct).ConfigureAwait(false)) != receipt.PdfSha256)
                return Failed();

            source.Position = 0;
            string target = Path.GetFullPath(destination);
            string folder = Path.GetDirectoryName(target)!;
            using var output = AiDesignOwnedStagingFiles.OwnedFile.Create(folder,
                Path.Combine(folder, $".printflow-{Guid.NewGuid():N}.tmp"), target);
            await source.CopyToAsync(output.Output, ct).ConfigureAwait(false);
            await output.MoveWithinBatchAsync(ct).ConfigureAwait(false);
            output.Release();
            return OperationResult.Ok();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or ArgumentException or System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            return Failed();
        }
    }

    private static OperationResult<Unit> Failed() => OperationResult.Fail<Unit>(FailureCode.OutputValidationFailed,
        "The approved cloud PDF could not be saved.");
}
