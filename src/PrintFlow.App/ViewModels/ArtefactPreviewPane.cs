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
        string? unavailable,
        int payloadPixelWidth,
        int payloadPixelHeight,
        int sourcePixelWidth,
        int sourcePixelHeight)
    {
        Heading = heading;
        FileName = fileName;
        Payload = payload;
        HasImage = hasImage;
        Detail = detail;
        Unavailable = unavailable;
        PayloadPixelWidth = payloadPixelWidth;
        PayloadPixelHeight = payloadPixelHeight;
        SourcePixelWidth = sourcePixelWidth;
        SourcePixelHeight = sourcePixelHeight;
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

    /// <summary>Width of <see cref="Payload"/> in its own pixels; zero when there is no image.</summary>
    /// <remarks>
    /// The payload's dimensions and the artefact's are both carried because a manual crop needs
    /// the ratio between them: what the operator drags over is the payload, and what a crop is
    /// recorded in is the source, and for a reduced preview those differ (Part C1 §6;
    /// Part C2 §7). <see cref="Detail"/> states the source figures for the operator to read;
    /// these four are for <see cref="CropSurfaceLayout"/> to calculate with.
    /// </remarks>
    public int PayloadPixelWidth { get; }

    /// <inheritdoc cref="PayloadPixelWidth" />
    public int PayloadPixelHeight { get; }

    /// <summary>Width of the artefact itself in pixels; zero when there is no image.</summary>
    /// <inheritdoc cref="PayloadPixelWidth" />
    public int SourcePixelWidth { get; }

    /// <inheritdoc cref="SourcePixelWidth" />
    public int SourcePixelHeight { get; }

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

        return new ArtefactPreviewPane(
            heading, fileName, preview.Payload, hasImage: true, detail, unavailable: null,
            preview.PixelWidth, preview.PixelHeight, preview.SourcePixelWidth, preview.SourcePixelHeight);
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

        return WithoutImage(heading, fileName, message);
    }

    /// <summary>
    /// A pane labelled by an explanation the caller already has, rather than by a failure
    /// (SCRUM-11078).
    /// </summary>
    /// <remarks>
    /// The route for a file the product knows in advance it cannot draw yet — a PSD or a PDF
    /// before its managed raster has been prepared. Asking the preview seam and rendering its
    /// refusal would say the same thing less clearly, and inventing a picture is the one thing
    /// §21 forbids: what is offered here is a sentence, and the pane is still an empty one.
    /// </remarks>
    internal static ArtefactPreviewPane WithoutImage(string heading, string fileName, string message) =>
        new(heading, fileName, ReadOnlyMemory<byte>.Empty, hasImage: false, string.Empty, message,
            payloadPixelWidth: 0, payloadPixelHeight: 0, sourcePixelWidth: 0, sourcePixelHeight: 0);
}
