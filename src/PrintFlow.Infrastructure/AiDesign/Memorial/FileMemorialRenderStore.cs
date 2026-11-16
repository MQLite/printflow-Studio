using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrintFlow.Domain.AiDesign.Memorial;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.AiDesign.Authoring;
using PrintFlow.Infrastructure.Delivery;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Workflow.AiDesign.Memorial;
using PrintFlow.Workflow.Delivery;

namespace PrintFlow.Infrastructure.AiDesign.Memorial;

/// <summary>Only a committed seal and its matching held PNG represent a managed render.</summary>
public sealed class FileMemorialRenderStore : IMemorialRenderStore
{
    private const int MaximumSealBytes = 16 * 1024;
    private readonly string _workspace;
    private readonly string _root;
    private sealed record Seal(int Version, MemorialRenderResult Render, DeliveryFileIdentity PngIdentity, DateTimeOffset PublishedAtUtc);
    private sealed record Opened(Seal Seal, VerifiedAiAttachmentRead Lease);

    public FileMemorialRenderStore(string workspaceRoot)
    {
        if (!Path.IsPathFullyQualified(workspaceRoot)) throw new ArgumentException("Absolute workspace root required.", nameof(workspaceRoot));
        _workspace = Path.GetFullPath(workspaceRoot);
        _root = Path.Combine(_workspace, "AiDesign", "Authoring", "Renders");
    }

    internal OperationResult<MemorialRenderResult> Publish(MemorialRenderPlan plan, Action<Stream> encode, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            if (!string.Equals(plan.SourceManifestSha256, MemorialCompositionModule.ComputeSourceManifest(plan), StringComparison.Ordinal) ||
                !MemorialRasterTarget.TryCreate(plan, out var target))
                return Failure<MemorialRenderResult>(FailureCode.PreconditionNotMet, "Invalid memorial render manifest or target.");
            var file = new MemorialRenderRef(plan.DraftId, Guid.NewGuid(), plan.Purpose);
            using var batch = new AiDesignOwnedStagingFiles.MemorialRenderBatch(_workspace, file.DraftId, file.RenderId);
            using (var bounded = new BoundedOutput(batch.Png, ct)) encode(bounded);
            ct.ThrowIfCancellationRequested();
            var facts = Inspect(batch.Png, target, ct);
            var result = new MemorialRenderResult(file, plan.DraftRevision, facts, plan.SourceManifestSha256);
            JsonSerializer.Serialize(batch.Seal, new Seal(1, result, batch.PngIdentity, DateTimeOffset.UtcNow), FileAiDesignRequestStore.JsonOptions);
            batch.Publish(ct);
            return OperationResult.Ok(result);
        }
        catch (OperationCanceledException) { return Cancelled<MemorialRenderResult>(); }
        catch (Exception ex) when (IsBoundaryFailure(ex))
        { return ct.IsCancellationRequested ? Cancelled<MemorialRenderResult>() : Failure<MemorialRenderResult>(FailureCode.OutputValidationFailed, $"Memorial PNG was not published: {ex.Message}"); }
    }

    public async Task<OperationResult<MemorialRenderResult>> ReadAsync(MemorialRenderRef file, CancellationToken ct)
    {
        if (file is null || !Enum.IsDefined(file.Purpose)) return Integrity<MemorialRenderResult>();
        var open = await OpenAsync(file.DraftId, file.RenderId, file.Purpose, null, ct);
        if (open.IsFailure) return OperationResult.Fail<MemorialRenderResult>(open.Failure);
        await using var lease = open.Value.Lease;
        return OperationResult.Ok(open.Value.Seal.Render);
    }

    public async Task<OperationResult<Stream>> OpenVerifiedAsync(MemorialRenderResult expected, CancellationToken ct)
    {
        if (expected?.File is not { } file || !Enum.IsDefined(file.Purpose)) return Integrity<Stream>();
        var open = await OpenAsync(file.DraftId, file.RenderId, file.Purpose, expected, ct);
        return open.IsFailure ? OperationResult.Fail<Stream>(open.Failure) : OperationResult.Ok(open.Value.Lease.Stream);
    }

    public async Task<OperationResult<IReadOnlyList<MemorialRenderResult>>> ListAsync(Guid draftId, CancellationToken ct)
    {
        if (draftId == Guid.Empty) return Integrity<IReadOnlyList<MemorialRenderResult>>();
        var held = new List<IDisposable>();
        try
        {
            ct.ThrowIfCancellationRequested();
            string folder = Path.Combine(_root, draftId.ToString("D"));
            ReparsePointGuard.RefuseAncestry(folder);
            try { _ = File.GetAttributes(folder); }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            { return OperationResult.Ok<IReadOnlyList<MemorialRenderResult>>(Array.Empty<MemorialRenderResult>()); }
            VerifiedAiAttachmentReader.PinParents(folder, held);
            List<Seal> results = [];
            foreach (string directory in Directory.EnumerateDirectories(folder))
            {
                ct.ThrowIfCancellationRequested();
                if (!Guid.TryParseExact(Path.GetFileName(directory), "D", out var renderId) || renderId == Guid.Empty) continue;
                var open = await OpenAsync(draftId, renderId, null, null, ct);
                if (open.IsFailure) continue; // Orphaned, invalid and in-progress files never become Ready.
                await using var lease = open.Value.Lease;
                results.Add(open.Value.Seal);
            }
            ct.ThrowIfCancellationRequested();
            return OperationResult.Ok<IReadOnlyList<MemorialRenderResult>>(Array.AsReadOnly(results
                .OrderByDescending(x => x.PublishedAtUtc).ThenBy(x => x.Render.File.RenderId).Select(x => x.Render).ToArray()));
        }
        catch (OperationCanceledException) { return Cancelled<IReadOnlyList<MemorialRenderResult>>(); }
        catch (Exception ex) when (IsBoundaryFailure(ex)) { return Integrity<IReadOnlyList<MemorialRenderResult>>(); }
        finally { Release(held); }
    }

    private async Task<OperationResult<Opened>> OpenAsync(Guid draftId, Guid renderId, MemorialRenderPurpose? purpose,
        MemorialRenderResult? expected, CancellationToken ct)
    {
        var held = new List<IDisposable>();
        try
        {
            ct.ThrowIfCancellationRequested();
            if (draftId == Guid.Empty || renderId == Guid.Empty) return Integrity<Opened>();
            string folder = Path.Combine(_root, draftId.ToString("D"), renderId.ToString("D"));
            VerifiedAiAttachmentReader.PinParents(folder, held);
            var identity = VerifiedAiAttachmentReader.OpenRegular(Path.Combine(folder, "identity.json")); held.Add(identity);
            if (identity.Length is <= 0 or > MaximumSealBytes) return Integrity<Opened>();
            var seal = await JsonSerializer.DeserializeAsync<Seal>(identity, FileAiDesignRequestStore.JsonOptions, ct);
            if (seal is not { Version: 1, PngIdentity: not null, Render: { File: { } file, Facts: { } facts } render } ||
                file.DraftId != draftId || file.RenderId != renderId || !Enum.IsDefined(file.Purpose) ||
                purpose is { } selected && file.Purpose != selected || expected is not null && expected != render ||
                render.DraftRevision < 0 || !Sha256.TryParse(render.SourceManifestSha256, out var sourceHash) ||
                sourceHash.Value != render.SourceManifestSha256 || !IsPngFacts(facts, file.Purpose)) return Integrity<Opened>();
            var png = VerifiedAiAttachmentReader.OpenRegular(Path.Combine(folder, "render.png")); held.Add(png);
            if (WindowsDeliveryNative.Identity(png.SafeFileHandle) != seal.PngIdentity || png.Length != facts.ByteLength ||
                Sha256.FromBytes(await SHA256.HashDataAsync(png, ct)) != facts.Sha256 || png.Position != facts.ByteLength || png.Length != facts.ByteLength)
                return Integrity<Opened>();
            ct.ThrowIfCancellationRequested();
            png.Position = 0;
            var lease = new VerifiedAiAttachmentRead(png, held.ToArray()); held.Clear();
            return OperationResult.Ok(new Opened(seal, lease));
        }
        catch (OperationCanceledException) { return Cancelled<Opened>(); }
        catch (Exception ex) when (IsBoundaryFailure(ex)) { return Integrity<Opened>(); }
        finally { Release(held); }
    }

    private static FileFacts Inspect(Stream png, MemorialRasterTarget target, CancellationToken ct)
    {
        if (png.Length is <= 0 or > MemorialRasterTarget.MaximumBytes) throw new IOException("PNG byte limit exceeded.");
        png.Position = 0;
        var decoder = new PngBitmapDecoder(png, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
        if (decoder.Frames.Count != 1) throw new IOException("A single PNG frame is required.");
        var frame = decoder.Frames[0];
        if (frame.PixelWidth != target.Width || frame.PixelHeight != target.Height ||
            !DpiMatches(frame.DpiX, target.Dpi) || !DpiMatches(frame.DpiY, target.Dpi) || WicPixelFormats.SourceAlpha(frame) != true ||
            WicPixelFormats.ColourModeOf(frame.Format) != ColourMode.Rgb) throw new IOException("PNG dimensions, resolution, colour or alpha do not match the render.");
        var pixels = frame.Format == PixelFormats.Bgra32 ? (BitmapSource)frame : new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        int stride = checked(target.Width * 4), rows = Math.Max(1, (256 * 1024) / stride);
        byte[] decoded = new byte[checked(stride * Math.Min(rows, target.Height))];
        for (int y = 0; y < target.Height; y += rows)
        {
            ct.ThrowIfCancellationRequested();
            int count = Math.Min(rows, target.Height - y);
            pixels.CopyPixels(new Int32Rect(0, y, target.Width, count), decoded, stride, 0);
            if (target.CircleRadiusPixels is null) continue;
            for (int r = 0; r < count; r++)
            {
                var (start, end) = target.CircleSpan(y + r);
                for (int x = 0; x < target.Width; x++)
                    if ((x < start || x >= end) && decoded[r * stride + x * 4 + 3] != 0)
                        throw new IOException("PNG contains visible pixels outside the badge circle.");
            }
        }
        png.Position = 0;
        var hash = Sha256.FromBytes(SHA256.HashData(png));
        ct.ThrowIfCancellationRequested();
        return new(ImageFormat.Png, png.Length, hash, frame.PixelWidth, frame.PixelHeight, frame.DpiX, frame.DpiY, ColourMode.Rgb, true);
    }

    private static bool IsPngFacts(FileFacts facts, MemorialRenderPurpose purpose)
    {
        int dpi = purpose == MemorialRenderPurpose.Preview ? 96 : 300;
        return facts.Format == ImageFormat.Png && facts.ByteLength is > 0 and <= MemorialRasterTarget.MaximumBytes &&
            Sha256.TryParse(facts.Sha256.Value, out _) && facts.PixelWidth is > 0 && facts.PixelHeight is > 0 &&
            (long)facts.PixelWidth.Value * facts.PixelHeight.Value <= MemorialRasterTarget.MaximumPixels &&
            facts.ColourMode == ColourMode.Rgb && facts.HasAlpha == true && DpiMatches(facts.DpiX, dpi) && DpiMatches(facts.DpiY, dpi);
    }

    private static bool DpiMatches(double? actual, int expected) => actual is { } value && Math.Abs(value - expected) < 0.03;
    internal static bool IsBoundaryFailure(Exception ex) => ex is IOException or UnauthorizedAccessException or ArgumentException or
        JsonException or Win32Exception or NotSupportedException or InvalidOperationException or COMException or OverflowException or FileFormatException;
    private static void Release(List<IDisposable> held) { for (int i = held.Count - 1; i >= 0; i--) held[i].Dispose(); }
    private static OperationResult<T> Failure<T>(FailureCode code, string detail) => OperationResult.Fail<T>(code, detail);
    private static OperationResult<T> Integrity<T>() => Failure<T>(FailureCode.RevisionIntegrityMismatch, "Memorial render integrity or ownership mismatch.");
    private static OperationResult<T> Cancelled<T>() => Failure<T>(FailureCode.Cancelled, "Memorial render operation cancelled.");

    private sealed class BoundedOutput(Stream output, CancellationToken ct) : Stream
    {
        public override bool CanRead => output.CanRead;
        public override bool CanSeek => true;
        public override bool CanWrite => true;
        public override long Length => output.Length;
        public override long Position { get => output.Position; set { Check(value); output.Position = value; } }
        public override void Flush() => output.Flush();
        public override int Read(byte[] buffer, int offset, int count) => output.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin)
        {
            long position = origin switch
            {
                SeekOrigin.Begin => offset, SeekOrigin.Current => checked(output.Position + offset),
                SeekOrigin.End => checked(output.Length + offset), _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };
            Position = position; return position;
        }
        public override void SetLength(long value) { Check(value); output.SetLength(value); }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer) { Check(checked(output.Position + buffer.Length)); output.Write(buffer); }
        private void Check(long value)
        {
            ct.ThrowIfCancellationRequested();
            if (value < 0 || value > MemorialRasterTarget.MaximumBytes) throw new IOException("PNG byte limit exceeded.");
        }
    }
}
