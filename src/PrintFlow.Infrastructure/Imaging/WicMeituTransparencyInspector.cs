using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Adapters.Meitu;

namespace PrintFlow.Infrastructure.Imaging;

/// <summary>
/// The narrow WIC alpha reader used after the existing generic file inspection pipeline has
/// established a readable PNG and its structural facts.
/// </summary>
/// <remarks>
/// This is deliberately not another file inspector: it does not sniff formats, hash bytes or
/// derive metadata. It answers only the question <c>FileFacts.HasAlpha</c> cannot answer —
/// whether any decoded pixel is actually transparent and whether any remains visible. The
/// BGRA32 conversion is the same measurement technique used by the Epic 11200 alpha trim; it
/// reads the alpha byte and never rewrites the image.
/// </remarks>
public sealed class WicMeituTransparencyInspector : IMeituTransparencyInspector
{
    private const int BytesPerPixel = 4;

    public async Task<OperationResult<MeituTransparencyFacts>> InspectAsync(
        string absolutePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);

        try
        {
            return await Task.Run(
                () => Inspect(absolutePath, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Fail<MeituTransparencyFacts>(
                FailureCode.Cancelled, "Transparency inspection was cancelled.");
        }
    }

    private static OperationResult<MeituTransparencyFacts> Inspect(
        string absolutePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(absolutePath))
        {
            return OperationResult.Fail<MeituTransparencyFacts>(
                FailureCode.OutputMissing, $"File not found: '{absolutePath}'.");
        }

        try
        {
            using FileStream stream = new(
                absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            BitmapDecoder decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.OnLoad);

            if (decoder.Frames.Count == 0)
            {
                return OperationResult.Fail<MeituTransparencyFacts>(
                    FailureCode.OutputUnreadable,
                    "The exported PNG contains no decodable image frame.");
            }

            BitmapSource frame = decoder.Frames[0];
            int width = frame.PixelWidth;
            int height = frame.PixelHeight;
            if (width <= 0 || height <= 0)
            {
                return OperationResult.Fail<MeituTransparencyFacts>(
                    FailureCode.OutputUnreadable,
                    "The exported PNG decoded to invalid pixel dimensions.");
            }

            BitmapSource bgra = frame.Format == PixelFormats.Bgra32
                ? frame
                : new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);

            int stride = checked(width * BytesPerPixel);
            byte[] row = new byte[stride];
            byte[] alphaPlane = new byte[checked(width * height)];

            for (int y = 0; y < height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bgra.CopyPixels(new Int32Rect(0, y, width, 1), row, stride, 0);

                for (int x = 0; x < width; x++)
                {
                    alphaPlane[(y * width) + x] = row[(x * BytesPerPixel) + 3];
                }
            }

            return OperationResult.Ok(MeituTransparencyRule.Summarise(alphaPlane));
        }
        catch (FileFormatException ex)
        {
            return Unreadable(ex);
        }
        catch (NotSupportedException ex)
        {
            return Unreadable(ex);
        }
        catch (IOException ex)
        {
            return Unreadable(ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unreadable(ex);
        }
        catch (OverflowException ex)
        {
            return Unreadable(ex);
        }
    }

    private static OperationResult<MeituTransparencyFacts> Unreadable(Exception exception) =>
        OperationResult.Fail<MeituTransparencyFacts>(
            FailureCode.OutputUnreadable,
            $"The exported PNG's alpha values could not be read: {exception.Message}");
}
