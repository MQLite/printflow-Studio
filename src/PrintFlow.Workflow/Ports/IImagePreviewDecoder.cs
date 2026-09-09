using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;

namespace PrintFlow.Workflow.Ports;

/// <summary>
/// One decoded, display-ready representation of a managed file (Epic 11200 Part C1 §5).
/// </summary>
/// <remarks>
/// Everything here is about <i>showing</i> pixels and nothing about deciding anything.
/// <see cref="Payload"/> is a freshly encoded PNG at 96 dpi, which is what makes a preview
/// bind to a WPF <c>Image</c> with one image pixel per device-independent pixel regardless of
/// what dpi the underlying artefact records. It is never written to disk, never persisted, and
/// never hashed: the Revision's own bytes remain the only thing an approval is bound to
/// (Part C1 §18).
/// <para>
/// <see cref="PixelWidth"/>/<see cref="PixelHeight"/> describe the payload;
/// <see cref="SourcePixelWidth"/>/<see cref="SourcePixelHeight"/> describe the artefact. They
/// differ only when the artefact was larger than
/// <see cref="IImagePreviewDecoder.MaximumDisplayEdge"/> and the preview was reduced to fit —
/// reported rather than hidden, so a screen can say so instead of implying it is showing every
/// pixel (Part C1 §6).
/// </para>
/// </remarks>
/// <param name="HasTransparency">
/// Whether the <b>source</b> frame's pixel format carries an alpha channel. Read from the
/// artefact rather than from the payload, which is always BGRA and would therefore answer
/// "yes" for everything.
/// </param>
public sealed record DecodedPreview(
    int PixelWidth,
    int PixelHeight,
    int SourcePixelWidth,
    int SourcePixelHeight,
    bool HasTransparency,
    ReadOnlyMemory<byte> Payload)
{
    /// <summary>True when the payload is a reduced-size stand-in rather than every pixel.</summary>
    public bool IsDownsampledForDisplay =>
        PixelWidth < SourcePixelWidth || PixelHeight < SourcePixelHeight;
}

/// <summary>
/// Turns one workspace file into bytes a UI can display (Epic 11200 Part C1 §3).
/// </summary>
/// <remarks>
/// Deliberately takes a <see cref="WorkspaceFileRef"/> and never a path: the implementation
/// resolves it through <see cref="IWorkspace"/>, so the containment rules that govern every
/// other file operation govern this one too and no caller can name a file of its own choosing.
/// <para>
/// A failure here means "this could not be displayed" and nothing more. It is not an
/// <c>AttemptFailed</c>, it does not invalidate a Revision, and it must never be mapped onto
/// one: a decoder with no codec for a container says nothing about whether the bytes on disk
/// are the approved ones (Part C1 §21).
/// </para>
/// </remarks>
public interface IImagePreviewDecoder
{
    /// <summary>
    /// The longest edge, in pixels, a preview payload may have before it is reduced.
    /// </summary>
    /// <remarks>
    /// Exposed so a screen can explain a reduced preview honestly rather than restating a
    /// number the decoder owns.
    /// </remarks>
    int MaximumDisplayEdge { get; }

    /// <summary>
    /// The longest edge, in pixels, a list thumbnail may have (Jira 11602).
    /// </summary>
    /// <remarks>
    /// A second, much smaller bound rather than a caller-supplied number, because "how big a
    /// thumbnail is" is the decoder's business and a screen that chose its own could ask Home
    /// for a hundred full-size previews by accident. Home lists up to a hundred rows, so the
    /// figure that matters is the per-row one: at this bound a decoded row costs tens of
    /// kilobytes rather than the megabytes <see cref="MaximumDisplayEdge"/> permits a review
    /// surface, which is the difference between a list and a working-set problem.
    /// </remarks>
    int ThumbnailEdge { get; }

    /// <summary>Decodes <paramref name="file"/> into a display payload. Reads only; never writes.</summary>
    Task<OperationResult<DecodedPreview>> DecodeAsync(
        WorkspaceFileRef file, CancellationToken cancellationToken);

    /// <summary>
    /// Decodes <paramref name="file"/> reduced to <see cref="ThumbnailEdge"/>. Reads only.
    /// </summary>
    /// <remarks>
    /// The same read-only operation as <see cref="DecodeAsync"/> with a smaller bound — not a
    /// different kind of image and not a cached one. Nothing is stored: a thumbnail is decoded
    /// when a list asks for it and released when the list lets go, exactly as every other
    /// preview is (Epic 11200 Part C1 §6).
    /// </remarks>
    Task<OperationResult<DecodedPreview>> DecodeThumbnailAsync(
        WorkspaceFileRef file, CancellationToken cancellationToken);
}
