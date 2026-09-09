namespace PrintFlow.Domain.Files;

/// <summary>
/// Which containers PrintFlow Studio accepts as a customer input, stated once
/// (MVP design §9.1, §9.2; Jira 11201, 11405, 11406, 11407).
/// </summary>
/// <remarks>
/// The single Product authority on "supported input". It exists because the answer was
/// previously nowhere: the import path accepted whatever bytes it was given, the file dialog
/// offered its own filter, and the screen's refusal text would have had to restate a list of
/// its own — three places to disagree about the same product rule.
/// <para>
/// The set is exactly the four inputs the MVP has a production path for: PNG and JPEG enter the
/// Photoshop output flow directly (Jira 11405), PSD is prepared through Photoshop
/// (Jira 11406) and single-page PDF is rasterised at production DPI (Jira 11407). TIFF is
/// deliberately absent: it is an <i>output</i> of this product, and design §9.2 gives it no
/// input rule — accepting one would create a session that could only fail several steps later.
/// </para>
/// <para>
/// A supported format is not a promise that a particular file will process. Whether a PSD
/// carries a Photoshop-compatible composite, and whether a PDF has exactly one page, are
/// decided by their own preparation processors against the real file; this only says which
/// containers are worth starting a session for.
/// </para>
/// </remarks>
public static class SupportedInputFormats
{
    /// <summary>The accepted input containers, in the order an operator message should list them.</summary>
    public static IReadOnlyList<ImageFormat> All { get; } =
        [ImageFormat.Png, ImageFormat.Jpeg, ImageFormat.Psd, ImageFormat.Pdf];

    /// <summary>Whether <paramref name="format"/> may start a processing session at all.</summary>
    public static bool IsSupported(ImageFormat format) =>
        format is ImageFormat.Png or ImageFormat.Jpeg or ImageFormat.Psd or ImageFormat.Pdf;

    /// <summary>
    /// Whether PrintFlow decodes this container itself at import time, rather than preparing a
    /// managed raster from it later.
    /// </summary>
    /// <remarks>
    /// This is what separates "unsupported" from "supported but unreadable". A PNG or JPEG that
    /// carries no readable image is a damaged file and must be refused as one — Jira 11201 asks
    /// for the file to be decoded before external automation. A PSD or PDF legitimately has no
    /// pixel metadata at import (design §9.2, and <see cref="FileFacts"/> records null rather
    /// than guessing), so the same test applied to it would refuse every valid PSD in existence.
    /// Their readability is established by their own preparation step.
    /// </remarks>
    public static bool IsDecodedAtImport(ImageFormat format) =>
        format is ImageFormat.Png or ImageFormat.Jpeg;
}
