using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using PrintFlow.Domain.AiDesign;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.AiDesign;
using PrintFlow.Workflow.AiDesign.Memorial;

namespace PrintFlow.Infrastructure.AiDesign.Authoring;

public sealed class FileAiAuthoringAssetFork(string workspaceRoot) : IAiAuthoringAssetFork
{
    private readonly VerifiedAiAttachmentReader _reader = new(workspaceRoot);

    public Task<OperationResult<IReadOnlyList<AiDesignAttachment>>> CopyAsync(Guid sourceOwnerId, Guid targetOwnerId,
        IReadOnlyList<AiDesignAttachment> assets, CancellationToken ct) =>
        CopyAndPublishAsync(sourceOwnerId, targetOwnerId, assets, value => Task.FromResult(OperationResult.Ok(value)), ct);

    public async Task<OperationResult<T>> CopyAndPublishAsync<T>(Guid sourceOwnerId, Guid targetOwnerId,
        IReadOnlyList<AiDesignAttachment> assets, Func<IReadOnlyList<AiDesignAttachment>, Task<OperationResult<T>>> publish,
        CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var selected = assets.ToArray();
            if (sourceOwnerId == Guid.Empty || targetOwnerId == Guid.Empty || sourceOwnerId == targetOwnerId ||
                selected.Length > AiDesignAttachmentLimits.MaximumFiles || selected.Any(asset => asset is null ||
                    asset.ByteLength <= 0 || asset.ByteLength > AiDesignAttachmentLimits.ImageBytes) ||
                selected.Sum(asset => asset.ByteLength) > AiDesignAttachmentLimits.TotalBytes ||
                selected.Select(asset => asset.Id).Distinct().Count() != selected.Length ||
                selected.Select(asset => asset.Order).Distinct().Count() != selected.Length)
                return OperationResult.Fail<T>(FailureCode.PreconditionNotMet, "Invalid fork attachment manifest.");
            if (selected.Length == 0) return await publish(Array.Empty<AiDesignAttachment>());

            using var batch = AiDesignOwnedStagingFiles.BeginAttachments(workspaceRoot, targetOwnerId);
            using var sequence = AiDesignOwnedStagingFiles.CreateRequest(workspaceRoot, targetOwnerId,
                AiDesignOwnedStagingFiles.RequestPurpose.AttachmentSequence);
            var verifiedReads = new List<VerifiedAiAttachmentRead>();
            try
            {
                var copied = new List<AiDesignAttachment>();
                foreach (var source in selected)
                {
                    var opened = await _reader.OpenAsync(sourceOwnerId, source, ct);
                    if (opened.IsFailure) return OperationResult.Fail<T>(opened.Failure);
                    var original = opened.Value;
                    verifiedReads.Add(original);
                    if (!await VerifiedAiAttachmentReader.HasImageHeaderAsync(original.Stream, source.MediaType, ct))
                        return OperationResult.Fail<T>(FailureCode.PreconditionNotMet, "Fork source is not a supported image.");

                    var staged = batch.Create(source.Order, Path.GetExtension(source.UploadName));
                    await original.Stream.CopyToAsync(staged.Snapshot.Output, ct);
                    await staged.PublishSnapshotAsync(ct);
                    var child = source with { Id = staged.Id, Snapshot = new(targetOwnerId, staged.RelativePath) };
                    var seal = staged.CreateIdentity();
                    await JsonSerializer.SerializeAsync(seal.Output, child, FileAiDesignRequestStore.JsonOptions, ct);
                    await seal.CloseFlushedAsync(ct);
                    staged.MarkReadOnly();

                    var verified = await _reader.OpenAsync(targetOwnerId, child, ct);
                    if (verified.IsFailure) return OperationResult.Fail<T>(verified.Failure);
                    verifiedReads.Add(verified.Value);
                    copied.Add(child);
                }

                // Subsequent additions must allocate after the largest inherited order, even
                // if its attachment is no longer active when the child is edited.
                byte[] lastOrder = Encoding.ASCII.GetBytes(selected.Max(asset => asset.Order).ToString(CultureInfo.InvariantCulture));
                await sequence.Output.WriteAsync(lastOrder, ct);
                await sequence.MoveWithinBatchAsync(ct);

                ct.ThrowIfCancellationRequested();
                var result = await publish(copied.AsReadOnly());
                if (result.IsSuccess)
                {
                    sequence.Release();
                    batch.Commit();
                }
                return result;
            }
            finally
            {
                foreach (var verified in verifiedReads) await verified.DisposeAsync();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or JsonException or
            OperationCanceledException or Win32Exception or NotSupportedException)
        {
            return OperationResult.Fail<T>(ex is OperationCanceledException ? FailureCode.Cancelled : FailureCode.PersistenceError,
                "Fork attachments could not be copied.");
        }
    }
}
