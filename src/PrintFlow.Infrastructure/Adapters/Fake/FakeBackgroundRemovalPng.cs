using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrintFlow.Domain.Results;

namespace PrintFlow.Infrastructure.Adapters.Fake;

/// <summary>
/// Produces the fake adapter's minimal deterministic transparent cutout: the same canvas and
/// decoded colour, with one transparent pixel and all remaining pixels visible.
/// </summary>
internal static class FakeBackgroundRemovalPng
{
    private const int BytesPerPixel = 4;

    internal static async Task<OperationResult<Unit>> WriteAsync(
        string inputPath, string outputPath, CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Run(
                () => Write(inputPath, outputPath, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Fail<Unit>(
                FailureCode.Cancelled, "Fake Background Removal was cancelled.");
        }
    }

    private static OperationResult<Unit> Write(
        string inputPath, string outputPath, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            using FileStream input = new(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            BitmapDecoder decoder = BitmapDecoder.Create(
                input,
                BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0)
            {
                return OperationResult.Fail<Unit>(
                    FailureCode.OutputUnreadable, "The fake cutout input has no decodable frame.");
            }

            BitmapSource frame = decoder.Frames[0];
            int width = frame.PixelWidth;
            int height = frame.PixelHeight;
            bool sourceHasAlpha = PrintFlow.Infrastructure.Imaging.WicPixelFormats.HasAlpha(frame.Format) == true;
            if (!sourceHasAlpha && checked(width * height) < 2)
            {
                return OperationResult.Fail<Unit>(
                    FailureCode.OutputValidationFailed,
                    "A deterministic fake cutout needs at least two pixels to contain both " +
                    "transparency and visible foreground.");
            }

            BitmapSource bgra = frame.Format == PixelFormats.Bgra32
                ? frame
                : new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            int stride = checked(width * BytesPerPixel);
            byte[] pixels = new byte[checked(stride * height)];
            bgra.CopyPixels(pixels, stride, 0);

            // Existing alpha is already deterministic source content and must remain intact —
            // the trim tests depend on its exact bounds. An opaque RGB input has no such plane,
            // so the fake supplies the smallest useful one instead.
            if (!sourceHasAlpha)
            {
                for (int pixel = 0; pixel < width * height; pixel++)
                {
                    pixels[(pixel * BytesPerPixel) + 3] =
                        pixel == 0 ? byte.MinValue : byte.MaxValue;
                }
            }

            WriteableBitmap cutout = new(width, height, frame.DpiX, frame.DpiY, PixelFormats.Bgra32, null);
            cutout.WritePixels(new System.Windows.Int32Rect(0, 0, width, height), pixels, stride, 0);

            PngBitmapEncoder encoder = new();
            encoder.Frames.Add(BitmapFrame.Create(cutout));
            using FileStream output = new(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            encoder.Save(output);
            return OperationResult.Ok();
        }
        catch (FileFormatException ex)
        {
            return Fail(ex);
        }
        catch (NotSupportedException ex)
        {
            return Fail(ex);
        }
        catch (IOException ex)
        {
            return Fail(ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Fail(ex);
        }
        catch (OverflowException ex)
        {
            return Fail(ex);
        }
    }

    private static OperationResult<Unit> Fail(Exception exception) =>
        OperationResult.Fail<Unit>(
            FailureCode.WorkspaceError,
            $"The deterministic fake cutout could not be written: {exception.Message}");
}
