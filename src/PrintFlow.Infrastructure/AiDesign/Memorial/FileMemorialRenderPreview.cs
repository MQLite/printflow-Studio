using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrintFlow.Domain.AiDesign.Memorial;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.AiDesign.Memorial;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.AiDesign.Memorial;

public sealed class FileMemorialRenderPreview(IMemorialRenderStore renders) : IMemorialRenderPreview
{
    private const int MaximumDisplayEdge = 2048;

    public async Task<OperationResult<DecodedPreview>> LoadAsync(MemorialRenderResult expected, CancellationToken ct)
    {
        var opened = await renders.OpenVerifiedAsync(expected, ct);
        if (opened.IsFailure) return OperationResult.Fail<DecodedPreview>(opened.Failure);
        await using var png = opened.Value;
        try { return await MemorialSta.RunAsync(() => Decode(png, ct)); }
        catch (OperationCanceledException) { return OperationResult.Fail<DecodedPreview>(FailureCode.Cancelled, "Memorial preview cancelled."); }
        catch (Exception ex) when (FileMemorialRenderStore.IsBoundaryFailure(ex))
        { return OperationResult.Fail<DecodedPreview>(FailureCode.OutputUnreadable, "Memorial PNG could not be displayed."); }
    }

    private static OperationResult<DecodedPreview> Decode(Stream png, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var frame = new PngBitmapDecoder(png, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None).Frames.Single();
        int width = frame.PixelWidth, height = frame.PixelHeight;
        if (width <= 0 || height <= 0 || (long)width * height > MemorialRasterTarget.MaximumPixels)
            return OperationResult.Fail<DecodedPreview>(FailureCode.OutputValidationFailed, "Memorial preview pixel limit exceeded.");
        double factor = Math.Min(1, (double)MaximumDisplayEdge / Math.Max(width, height));
        BitmapSource scaled = factor < 1 ? new TransformedBitmap(frame, new ScaleTransform(factor, factor)) : frame;
        BitmapSource bgra = scaled.Format == PixelFormats.Bgra32 ? scaled : new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
        int stride = checked(bgra.PixelWidth * 4);
        byte[] bytes = new byte[checked(stride * bgra.PixelHeight)];
        bgra.CopyPixels(bytes, stride, 0);
        var display = BitmapSource.Create(bgra.PixelWidth, bgra.PixelHeight, 96, 96, PixelFormats.Bgra32, null, bytes, stride);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(display));
        using var result = new MemoryStream(); encoder.Save(result);
        ct.ThrowIfCancellationRequested();
        return OperationResult.Ok(new DecodedPreview(display.PixelWidth, display.PixelHeight, width, height, true, result.ToArray()));
    }
}
