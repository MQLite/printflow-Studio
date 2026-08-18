using PrintFlow.Domain.Files;

namespace PrintFlow.Infrastructure.Imaging;

/// <summary>
/// Detects an image format from its magic bytes. Never trusts the file extension
/// (Epic 11100 Task 11106a; plan §14.2, §6).
/// </summary>
public static class FormatSniffer
{
    /// <summary>The largest header any recognised signature needs.</summary>
    public const int RequiredHeaderLength = 8;

    public static ImageFormat Detect(ReadOnlySpan<byte> header)
    {
        if (StartsWith(header, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return ImageFormat.Png;
        }

        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return ImageFormat.Jpeg;
        }

        // TIFF little-endian ("II*\0") and big-endian ("MM\0*").
        if (StartsWith(header, "II"u8) && header.Length >= 4 && header[2] == 0x2A && header[3] == 0x00)
        {
            return ImageFormat.Tiff;
        }

        if (StartsWith(header, "MM"u8) && header.Length >= 4 && header[2] == 0x00 && header[3] == 0x2A)
        {
            return ImageFormat.Tiff;
        }

        if (StartsWith(header, "8BPS"u8))
        {
            return ImageFormat.Psd;
        }

        if (StartsWith(header, "%PDF"u8))
        {
            return ImageFormat.Pdf;
        }

        return ImageFormat.Unknown;
    }

    private static bool StartsWith(ReadOnlySpan<byte> header, ReadOnlySpan<byte> signature) =>
        header.Length >= signature.Length && header[..signature.Length].SequenceEqual(signature);
}
