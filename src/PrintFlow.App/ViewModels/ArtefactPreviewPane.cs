using System.Globalization;
using PrintFlow.App.Resources;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.Services;

namespace PrintFlow.App.ViewModels;

/// <summary>
/// One image the review screen is showing, with its label (Epic 11200 Part C1 §7, §11).
/// </summary>
/// <remarks>
/// A pane is either an image or a sentence explaining why there is none, and never both. That
/// is the whole of §21 made structural: a preview that could not be produced still leaves the
/// artefact's metadata, the operator's decision buttons and the session itself untouched — all
/// this type can do is fail to show a picture.
/// <para>
/// <see cref="Payload"/> is the encoded PNG the preview seam returned. It is turned into a
/// bitmap by <c>PreviewPayloadConverter</c> at bind time, so no view model ever opens a stream,
/// and dropping the pane drops the last reference to the bytes (§6, §29).
/// </para>
/// </remarks>
public sealed class ArtefactPreviewPane
{
    private ArtefactPreviewPane(
        string heading,
        string fileName,
        ReadOnlyMemory<byte> payload,
        bool hasImage,
        string detail,
        string? unavailable)
    {
        Heading = heading;
        FileName = fileName;
        Payload = payload;
        HasImage = hasImage;
        Detail = detail;
        Unavailable = unavailable;
    }

    /// <summary>"Before", "After", or the single-image heading. The operator's only orientation.</summary>
    public string Heading { get; }

    /// <summary>The workspace file name. Never a path.</summary>
    public string FileName { get; }

    /// <summary>Encoded PNG bytes, empty when there is no image.</summary>
    public ReadOnlyMemory<byte> Payload { get; }

    public bool HasImage { get; }

    /// <summary>Pixel dimensions, plus a note when the preview was reduced for display.</summary>
    public string Detail { get; }

    /// <summary>Why there is no image, or null when there is one.</summary>
    public string? Unavailable { get; }

    /// <summary>True exactly when <see cref="Unavailable"/> should be shown.</summary>
    public bool IsUnavailable => !HasImage;

    internal static ArtefactPreviewPane From(string heading, string fileName, ImagePreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);

        string pixels = string.Format(
            CultureInfo.CurrentCulture,
            Strings.Session_PreviewPixels,
            preview.SourcePixelWidth,
            preview.SourcePixelHeight);

        // The pixel figures are the artefact's, not the payload's: an operator reviewing a
        // 6000 px design must not be told it is 2048 px because that is all the screen decoded.
        string detail = preview.IsDownsampledForDisplay
            ? $"{pixels} · {Strings.Session_PreviewReduced}"
            : pixels;

        return new ArtefactPreviewPane(heading, fileName, preview.Payload, hasImage: true, detail, unavailable: null);
    }

    /// <summary>
    /// A pane that could not be filled, labelled by why.
    /// </summary>
    /// <remarks>
    /// <see cref="FailureCode.OutputUnreadable"/> is the "your workstation cannot render this
    /// container" case — a PSD, or a CMYK TIFF — and deserves a sentence saying the file is
    /// unaffected. Everything else (a missing file, a Revision that is not this session's)
    /// gets the plain notice: the operator's next action is the same either way, and inventing
    /// distinctions they cannot act on is noise.
    /// </remarks>
    internal static ArtefactPreviewPane Unreadable(string heading, string fileName, OperationFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        string message = failure.Code == FailureCode.OutputUnreadable
            ? Strings.Session_PreviewLoadFailed
            : Strings.Session_PreviewUnavailable;

        return new ArtefactPreviewPane(
            heading, fileName, ReadOnlyMemory<byte>.Empty, hasImage: false, string.Empty, message);
    }
}
