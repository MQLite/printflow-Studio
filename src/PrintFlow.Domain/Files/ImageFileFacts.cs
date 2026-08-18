namespace PrintFlow.Domain.Files;

/// <summary>Formats PrintFlow can manage. Detected from magic bytes, never from the extension.</summary>
public enum ImageFormat
{
    Unknown,
    Png,
    Jpeg,
    Tiff,
    Psd,
    Pdf,
}

/// <summary>Colour mode of a managed file, where it can be determined.</summary>
public enum ColourMode
{
    Unknown,
    Grayscale,
    Rgb,
    Cmyk,
}

/// <summary>
/// The structural facts about a file, established once at validation time and then frozen
/// into a <c>Revision</c>.
/// </summary>
/// <remarks>
/// Pixel metadata is nullable because PSD and single-page PDF imports may legitimately be
/// recorded before rasterisation (MVP design §9.2). A missing value is recorded as unknown
/// rather than guessed.
/// </remarks>
public sealed record FileFacts(
    ImageFormat Format,
    long ByteLength,
    Sha256 Sha256,
    int? PixelWidth,
    int? PixelHeight,
    double? DpiX,
    double? DpiY,
    ColourMode ColourMode,
    bool? HasAlpha)
{
    public bool HasPixelDimensions => PixelWidth is > 0 && PixelHeight is > 0;
}
