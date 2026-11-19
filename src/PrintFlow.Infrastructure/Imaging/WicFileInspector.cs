using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Imaging;

/// <summary>
/// Reads a file exactly once, hashing it while sniffing its format and — where WIC can
/// reliably decode the container — its pixel dimensions, DPI, colour mode and alpha
/// (Epic 11100 Task 11106a; plan §14.2).
/// </summary>
/// <remarks>
/// Hashing streams the whole file with a 1&#160;MB buffer rather than loading it into memory,
/// so large production files do not spike working set. Hashing itself is the readability
/// proof (plan §10.1): a file that cannot be streamed to the end throws or fails, and no
/// Revision is ever created from it.
///
/// WIC decodes PNG, JPEG and TIFF. PSD, PDF and WebP are recorded with format, length and hash.
/// Their preparation processors establish pixel metadata from the decoded raster.
/// </remarks>
public sealed class WicFileInspector : IFileInspector
{
    private const int BufferSize = 1 << 20;

    /// <inheritdoc />
    public Task<OperationResult<FileFacts>> InspectAsync(string absolutePath, CancellationToken cancellationToken) =>
        InspectCoreAsync(absolutePath, requirePngDecode: false, cancellationToken);

    public Task<OperationResult<FileFacts>> InspectPngOutputAsync(string absolutePath, CancellationToken cancellationToken) =>
        InspectCoreAsync(absolutePath, requirePngDecode: true, cancellationToken);

    private static async Task<OperationResult<FileFacts>> InspectCoreAsync(
        string absolutePath, bool requirePngDecode, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);

        if (!File.Exists(absolutePath))
        {
            return OperationResult.Fail<FileFacts>(
                FailureCode.OutputMissing, $"File not found: '{absolutePath}'.");
        }

        byte[] header = new byte[FormatSniffer.RequiredHeaderLength];
        int headerLength;
        long length;
        Sha256 hash;
        WicMetadata? verifiedPng = null;

        try
        {
            await using FileStream stream = new(
                absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                BufferSize, useAsync: true);

            length = stream.Length;
            if (length <= 0)
            {
                return OperationResult.Fail<FileFacts>(
                    FailureCode.OutputUnreadable, $"File is empty: '{absolutePath}'.");
            }

            headerLength = await ReadFullyAsync(stream, header, cancellationToken);
            stream.Position = 0;

            using IncrementalHash sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[BufferSize];
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                sha.AppendData(buffer, 0, read);
            }

            hash = Sha256.FromBytes(sha.GetHashAndReset());

            if (requirePngDecode)
            {
                if (FormatSniffer.Detect(header.AsSpan(0, headerLength)) != ImageFormat.Png)
                    return OperationResult.Fail<FileFacts>(FailureCode.OutputValidationFailed, "The output is not PNG.");
                stream.Position = 0;
                verifiedPng = VerifyPngDecode(stream, cancellationToken);
            }
        }
        catch (IOException ex)
        {
            return OperationResult.Fail<FileFacts>(
                FailureCode.OutputUnreadable, $"File could not be read completely: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return OperationResult.Fail<FileFacts>(
                FailureCode.OutputUnreadable, $"File could not be read completely: {ex.Message}");
        }
        catch (Exception ex) when (requirePngDecode &&
            ex is FileFormatException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            return OperationResult.Fail<FileFacts>(FailureCode.OutputValidationFailed,
                $"The output PNG could not be decoded: {ex.Message}");
        }

        ImageFormat format = FormatSniffer.Detect(header.AsSpan(0, headerLength));
        WicMetadata metadata = verifiedPng ?? (format is ImageFormat.Png or ImageFormat.Jpeg or ImageFormat.Tiff
            ? TryDecodeWic(absolutePath)
            : WicMetadata.Unavailable);

        FileFacts facts = new(
            format,
            length,
            hash,
            metadata.PixelWidth,
            metadata.PixelHeight,
            metadata.DpiX,
            metadata.DpiY,
            metadata.ColourMode,
            metadata.HasAlpha);

        return OperationResult.Ok(facts);
    }

    private static async Task<int> ReadFullyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private readonly record struct WicMetadata(
        int? PixelWidth, int? PixelHeight, double? DpiX, double? DpiY, ColourMode ColourMode, bool? HasAlpha)
    {
        public static readonly WicMetadata Unavailable = new(null, null, null, null, ColourMode.Unknown, null);
    }

    private static WicMetadata TryDecodeWic(string absolutePath)
    {
        try
        {
            using FileStream stream = new(absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            BitmapDecoder decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.None);

            if (decoder.Frames.Count == 0)
            {
                return WicMetadata.Unavailable;
            }

            BitmapFrame frame = decoder.Frames[0];
            return MetadataOf(frame);
        }
        catch (NotSupportedException)
        {
            // WIC has no codec for this container's exact variant. Honest unknown, not a guess.
            return WicMetadata.Unavailable;
        }
        catch (FileFormatException)
        {
            return WicMetadata.Unavailable;
        }
    }

    private static WicMetadata VerifyPngDecode(Stream stream, CancellationToken cancellationToken)
    {
        BitmapDecoder decoder = new PngBitmapDecoder(
            stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
        BitmapFrame frame = decoder.Frames[0];
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        const int tileWidth = 1024;
        const int tileHeight = 16;
        int stride = tileWidth * 4;
        byte[] pixels = new byte[stride * tileHeight];
        for (int y = 0; y < frame.PixelHeight;)
        {
            int height = Math.Min(tileHeight, frame.PixelHeight - y);
            for (int x = 0; x < frame.PixelWidth;)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int width = Math.Min(tileWidth, frame.PixelWidth - x);
                converted.CopyPixels(new Int32Rect(x, y, width, height), pixels, stride, 0);
                x += width;
            }
            y += height;
        }
        return MetadataOf(frame);
    }

    private static WicMetadata MetadataOf(BitmapFrame frame) => new(
        frame.PixelWidth, frame.PixelHeight,
        frame.DpiX > 0 ? frame.DpiX : null,
        frame.DpiY > 0 ? frame.DpiY : null,
        WicPixelFormats.ColourModeOf(frame.Format), WicPixelFormats.HasAlpha(frame.Format));
}
