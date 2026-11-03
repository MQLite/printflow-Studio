using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrintFlow.Domain.AiDesign;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Workflow.AiDesign.Authoring;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.AiDesign.Authoring;

public sealed class FileAiAuthoringAssetPreview(string workspaceRoot) : IAiAuthoringAssetPreview
{
    private readonly VerifiedAiAttachmentReader _reader = new(workspaceRoot);
    public async Task<OperationResult<DecodedPreview>> LoadAsync(Guid ownerId, AiDesignAttachment asset, CancellationToken ct)
    {
        if (asset.MediaType is not ("image/png" or "image/jpeg" or "image/webp")) return Failed("Unsupported image type.");
        var opened = await _reader.OpenAsync(ownerId, asset, ct);
        if (opened.IsFailure) return OperationResult.Fail<DecodedPreview>(opened.Failure);
        await using var lease = opened.Value;
        try
        {
            if (!await VerifiedAiAttachmentReader.HasImageHeaderAsync(lease.Stream, asset.MediaType, ct)) return Failed("Image container does not match its type.");
        }
        catch (OperationCanceledException) { return OperationResult.Fail<DecodedPreview>(FailureCode.Cancelled, "Preview cancelled."); }
        catch (IOException) { return Failed("Image source could not be read."); }
        // The lease outlives the worker, including cancellation. Never reopen by pathname.
        var completion = new TaskCompletionSource<OperationResult<DecodedPreview>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.SetResult(Decode(lease.Stream, ct)); }
            catch (OperationCanceledException) { completion.SetResult(OperationResult.Fail<DecodedPreview>(FailureCode.Cancelled, "Preview cancelled.")); }
            catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or InvalidOperationException or
                FileFormatException or OverflowException or COMException)
            { completion.SetResult(Failed("Image could not be decoded for preview.")); }
        }) { IsBackground = true, Name = "Authoring image preview" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return await completion.Task;
    }

    private static OperationResult<DecodedPreview> Decode(Stream stream, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
        if (decoder.Frames.Count != 1) return Failed("A single image frame is required.");
        var frame = decoder.Frames[0]; int width = frame.PixelWidth, height = frame.PixelHeight;
        if (width <= 0 || height <= 0 || (long)width * height > 64_000_000) return Failed("Image pixel limit exceeded.");
        bool alpha = WicPixelFormats.SourceAlpha(frame) == true;
        double factor = Math.Min(1.0, 2048.0 / Math.Max(width, height));
        BitmapSource scaled = factor < 1 ? new TransformedBitmap(frame, new ScaleTransform(factor, factor)) : frame;
        BitmapSource bgra = scaled.Format == PixelFormats.Bgra32 ? scaled : new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
        int stride = checked(bgra.PixelWidth * 4); byte[] pixels = new byte[checked(stride * bgra.PixelHeight)];
        ct.ThrowIfCancellationRequested(); bgra.CopyPixels(pixels, stride, 0);
        if (!alpha) for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        var display = BitmapSource.Create(bgra.PixelWidth, bgra.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(display));
        using var output = new MemoryStream(); encoder.Save(output); ct.ThrowIfCancellationRequested();
        return OperationResult.Ok(new DecodedPreview(display.PixelWidth, display.PixelHeight, width, height, alpha, output.ToArray()));
    }

    private static OperationResult<DecodedPreview> Failed(string message) => OperationResult.Fail<DecodedPreview>(FailureCode.OutputUnreadable, message);
}
