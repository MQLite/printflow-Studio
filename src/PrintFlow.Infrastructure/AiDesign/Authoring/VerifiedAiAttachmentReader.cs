using System.IO;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text.Json;
using PrintFlow.Domain.AiDesign;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Infrastructure.Delivery;
using PrintFlow.Infrastructure.Workspace;

namespace PrintFlow.Infrastructure.AiDesign.Authoring;

public sealed class VerifiedAiAttachmentReader
{
    private readonly string _workspaceRoot;
    public VerifiedAiAttachmentReader(string workspaceRoot)
    {
        if (!Path.IsPathFullyQualified(workspaceRoot)) throw new ArgumentException("Absolute workspace root required.", nameof(workspaceRoot));
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
    }

    public async Task<OperationResult<VerifiedAiAttachmentRead>> OpenAsync(Guid ownerId, AiDesignAttachment asset, CancellationToken ct)
    {
        var held = new List<IDisposable>();
        try
        {
            ct.ThrowIfCancellationRequested();
            if (ownerId == Guid.Empty || asset is null || asset.Id == Guid.Empty || asset.Snapshot is null ||
                asset.Snapshot.RequestId != ownerId || asset.Order <= 0 || asset.UploadName is null || asset.Purpose is null ||
                !Sha256.TryParse(asset.Sha256.Value, out _)) throw new IOException("Invalid attachment manifest.");
            string ext = Path.GetExtension(asset.UploadName).ToLowerInvariant();
            if (!FileAiDesignAttachments.Types.TryGetValue(ext, out string? media) || media != asset.MediaType ||
                asset.UploadName != $"attachment-{asset.Order:D2}{ext}" || asset.Snapshot.RelativePath != $"Attachments/{asset.Id:N}/{asset.UploadName}" ||
                asset.ByteLength <= 0 || asset.ByteLength > FileAiDesignAttachments.Limit(media)) throw new IOException("Attachment manifest mismatch.");
            string folder = Path.Combine(_workspaceRoot, "AiDesign", "Requests", ownerId.ToString("D"), "Attachments", asset.Id.ToString("N"));
            // Pin from volume root down, refusing each reparse target before opening the next.
            PinParents(folder, held);
            var seal = OpenRegular(Path.Combine(folder, "identity.json")); held.Add(seal);
            if (seal.Length is <= 0 or > 65536) throw new IOException("Invalid attachment seal.");
            var saved = await JsonSerializer.DeserializeAsync<AiDesignAttachment>(seal, FileAiDesignRequestStore.JsonOptions, ct);
            if (saved is null || saved with { Purpose = asset.Purpose } != asset) throw new IOException("Unselected attachment identity.");
            var snapshot = OpenRegular(Path.Combine(folder, asset.UploadName)); held.Add(snapshot);
            if (snapshot.Length != asset.ByteLength || Sha256.FromBytes(await SHA256.HashDataAsync(snapshot, ct)) != asset.Sha256 ||
                snapshot.Position != asset.ByteLength || snapshot.Length != asset.ByteLength) throw new IOException("Attachment integrity mismatch.");
            snapshot.Position = 0;
            var lease = new VerifiedAiAttachmentRead(snapshot, held.ToArray()); held.Clear();
            return OperationResult.Ok(lease);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or JsonException or
            OperationCanceledException or Win32Exception or NotSupportedException)
        {
            return OperationResult.Fail<VerifiedAiAttachmentRead>(ex is OperationCanceledException ? FailureCode.Cancelled : FailureCode.RevisionIntegrityMismatch,
                "AttachmentIntegrityOrOwnershipMismatch");
        }
        finally { for (int i = held.Count - 1; i >= 0; i--) held[i].Dispose(); }
    }

    internal static void PinParents(string folder, List<IDisposable> held)
    {
        var parents = new Stack<string>();
        for (string? current = folder; current is not null; current = Path.GetDirectoryName(current)) parents.Push(current);
        foreach (string parent in parents)
        {
            ReparsePointGuard.RefuseAncestry(parent); held.Add(WindowsDeliveryNative.OpenDirectory(parent));
        }
    }

    internal static async Task<bool> HasImageHeaderAsync(Stream stream, string mediaType, CancellationToken ct)
    {
        stream.Position = 0; byte[] bytes = new byte[12]; int total = 0;
        try
        {
            while (total < bytes.Length)
            {
                int read = await stream.ReadAsync(bytes.AsMemory(total), ct); if (read == 0) break; total += read;
            }
            return mediaType switch
            {
                "image/png" => total >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
                "image/jpeg" => total >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255,
                "image/webp" => total == 12 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8),
                _ => false
            };
        }
        finally { stream.Position = 0; }
    }

    internal static FileStream OpenRegular(string path)
    {
        ReparsePointGuard.RefuseAncestry(path);
        var handle = WindowsDeliveryNative.OpenSource(path);
        try
        {
            if (!NativeMethods.GetFileInformationByHandle(handle, out NativeMethods.ByHandleFileInformation info) ||
                info.NumberOfLinks != 1 || (info.Attributes & (uint)(FileAttributes.Device | FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                throw new IOException("Regular unshared attachment file required.");
            return new FileStream(handle, FileAccess.Read, 65536, isAsync: false);
        }
        catch { handle.Dispose(); throw; }
    }
}

public sealed class VerifiedAiAttachmentRead : IAsyncDisposable
{
    internal VerifiedAiAttachmentRead(Stream stream, IDisposable[] resources) => Stream = new HeldReadStream(stream, resources);
    public Stream Stream { get; }
    public ValueTask DisposeAsync() => Stream.DisposeAsync();

    // The lease exposes read/seek only, never a path to reopen or a mutable file handle.
    private sealed class HeldReadStream(Stream input, IDisposable[] resources) : Stream
    {
        private bool _disposed;
        public override bool CanRead => !_disposed && input.CanRead;
        public override bool CanSeek => !_disposed && input.CanSeek;
        public override bool CanWrite => false;
        public override long Length => input.Length;
        public override long Position { get => input.Position; set => input.Position = value; }
        public override int Read(byte[] buffer, int offset, int count) => input.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => input.Read(buffer);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => input.ReadAsync(buffer, ct);
        public override long Seek(long offset, SeekOrigin origin) => input.Seek(offset, origin);
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                for (int i = resources.Length - 1; i >= 0; i--) resources[i].Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
