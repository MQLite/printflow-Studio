using System.Windows.Media;
using PrintFlow.Domain.Files;

namespace PrintFlow.Infrastructure.Imaging;

/// <summary>
/// What a WIC <see cref="PixelFormat"/> tells us about a file, and — just as importantly —
/// what it does not.
/// </summary>
/// <remarks>
/// Shared by <see cref="WicFileInspector"/> (which records the facts on a Revision) and
/// <see cref="DeterministicAlphaTrimProcessor"/> (which refuses to trim a file with no alpha).
/// Those two must agree: an inspector that records <c>HasAlpha = false</c> while the trim
/// processor happily crops the same file would be a contradiction the operator eventually
/// pays for.
///
/// Both answers are deliberately three-valued. <c>null</c> means "this format does not say" —
/// an indexed format carries a palette that may or may not contain transparency — and is
/// never quietly rounded down to <c>false</c>.
/// </remarks>
internal static class WicPixelFormats
{
    /// <summary>
    /// Whether the format carries an alpha channel: <c>true</c>, <c>false</c>, or <c>null</c>
    /// when the format alone cannot say.
    /// </summary>
    /// <remarks>
    /// Premultiplied and straight variants both answer <c>true</c>. Whether alpha was
    /// premultiplied changes how the colour channels are read, never whether an alpha value
    /// exists — and the trim scan reads alpha itself, so the distinction does not reach it.
    /// </remarks>
    public static bool? HasAlpha(PixelFormat format)
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

    /// <summary>The colour mode a format implies, or <see cref="ColourMode.Unknown"/>.</summary>
    /// <remarks>
    /// Indexed formats carry a palette that may itself be any of the modes below; claiming
    /// RGB for them would be a guess PrintFlow does not make.
    /// </remarks>
    public static ColourMode ColourModeOf(PixelFormat format)
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

        return ColourMode.Unknown;
    }
}
