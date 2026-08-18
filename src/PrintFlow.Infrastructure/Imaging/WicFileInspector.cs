using System.IO;
using System.Security.Cryptography;
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
/// WIC is asked to decode only PNG, JPEG and TIFF containers. PSD and PDF are recorded with
/// format, length and hash but null pixel metadata — nullable is honest; WIC does not reliably
/// decode either container, and a stronger decoder is deferred to Epic 11400 behind this same
/// interface (plan §14.3).
/// </remarks>
public sealed class WicFileInspector : IFileInspector
{
    private const int BufferSize = 1 << 20;

    /// <inheritdoc />
    public async Task<OperationResult<FileFacts>> InspectAsync(
        string absolutePath, CancellationToken cancellationToken)
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

        ImageFormat format = FormatSniffer.Detect(header.AsSpan(0, headerLength));
        WicMetadata metadata = format is ImageFormat.Png or ImageFormat.Jpeg or ImageFormat.Tiff
            ? TryDecodeWic(absolutePath)
            : WicMetadata.Unavailable;

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
            PixelFormat pixelFormat = frame.Format;

            return new WicMetadata(
                frame.PixelWidth,
                frame.PixelHeight,
                frame.DpiX > 0 ? frame.DpiX : null,
                frame.DpiY > 0 ? frame.DpiY : null,
                InferColourMode(pixelFormat),
                InferHasAlpha(pixelFormat));
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

    private static ColourMode InferColourMode(PixelFormat format)
    {
        if (format == PixelFormats.Cmyk32)
        {
            return ColourMode.Cmyk;
        }

        if (format == PixelFormats.Gray2 || format == PixelFormats.Gray4 ||
            format == PixelFormats.Gray8 || format == PixelFormats.Gray16 ||
            format == PixelFormats.Gray32Float)
        {
            return ColourMode.Grayscale;
        }

        if (format == PixelFormats.Bgr24 || format == PixelFormats.Rgb24 ||
            format == PixelFormats.Bgr32 || format == PixelFormats.Bgra32 ||
            format == PixelFormats.Pbgra32 || format == PixelFormats.Bgr101010 ||
            format == PixelFormats.Rgba64 || format == PixelFormats.Prgba64 ||
            format == PixelFormats.Rgb48 || format == PixelFormats.Rgba128Float ||
            format == PixelFormats.Prgba128Float || format == PixelFormats.Rgb128Float)
        {
            return ColourMode.Rgb;
        }

        // Indexed formats carry a palette that may itself be any of the above; claiming RGB
        // here would be a guess PrintFlow does not make.
        return ColourMode.Unknown;
    }

    private static bool? InferHasAlpha(PixelFormat format)
    {
        if (format == PixelFormats.Bgra32 || format == PixelFormats.Pbgra32 ||
            format == PixelFormats.Rgba64 || format == PixelFormats.Prgba64 ||
            format == PixelFormats.Rgba128Float || format == PixelFormats.Prgba128Float)
        {
            return true;
        }

        if (format == PixelFormats.Bgr24 || format == PixelFormats.Rgb24 ||
            format == PixelFormats.Bgr32 || format == PixelFormats.Gray2 ||
            format == PixelFormats.Gray4 || format == PixelFormats.Gray8 ||
            format == PixelFormats.Gray16 || format == PixelFormats.Gray32Float ||
            format == PixelFormats.Cmyk32 || format == PixelFormats.Bgr101010 ||
            format == PixelFormats.Rgb48 || format == PixelFormats.Rgb128Float)
        {
            return false;
        }

        return null;
    }
}
