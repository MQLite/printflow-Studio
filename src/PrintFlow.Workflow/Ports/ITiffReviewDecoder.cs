using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;

namespace PrintFlow.Workflow.Ports;

/// <summary>
/// One production TIFF decoded into the three display representations final review needs
/// (SCRUM-11104 §6, §8, §11, §15).
/// </summary>
/// <remarks>
/// Everything here is about <i>showing</i> a validated file and nothing about deciding anything.
/// The three payloads are freshly encoded PNGs at 96 dpi — the same convention
/// <see cref="DecodedPreview.Payload"/> follows and for the same reason: at 96 dpi a WPF
/// <c>Image</c> renders one payload pixel per device-independent pixel, so "100%" means 100%
/// for a 300-ppi production artefact. Nothing is written to disk, nothing is hashed, and the
/// TIFF's own bytes remain the only thing an approval is bound to.
/// <para>
/// One decode produces all three, which is what lets a mode switch be a change of which frozen
/// bitmap is on screen rather than a second pass over 74 MB of interleaved samples (§15).
/// </para>
/// <para>
/// <see cref="PreviewPixelWidth"/>/<see cref="PreviewPixelHeight"/> describe the payloads and
/// are identical for all three; <see cref="PixelWidth"/>/<see cref="PixelHeight"/> describe the
/// TIFF canvas. They differ only when the canvas was larger than
/// <see cref="ITiffReviewDecoder.MaximumDisplayEdge"/> and every payload was reduced by the same
/// factor — reported rather than hidden, so a screen can say so instead of implying it is
/// showing every pixel (§35).
/// </para>
/// </remarks>
/// <param name="ColourPayload">
/// The CMYK content converted for screen. An uncalibrated approximation for visual inspection,
/// never a colour proof — see <see cref="ColourConversion"/>.
/// </param>
/// <param name="WhiteInkPayload">
/// The validated white-ink spot channel, decoded from the TIFF's own fifth sample and from
/// nothing else. Bright means ink; see <see cref="WhiteInkPolarity"/>.
/// </param>
/// <param name="OverlayPayload">
/// The colour content with white-ink coverage marked over it. Display-only composition; the
/// TIFF is neither read differently nor written.
/// </param>
/// <param name="WhiteInkSampleCount">
/// How many fifth samples carry any white ink at all, counted over the whole canvas rather than
/// over the reduced payload, so it can be compared against what validation established.
/// </param>
/// <param name="ColourConversion">
/// A stable, non-localised identifier for the exact screen conversion used, so a report or a
/// support question can say which arithmetic produced the pixels an operator looked at.
/// </param>
/// <param name="WhiteInkPolarity">
/// A stable, non-localised identifier for how stored fifth-sample values were mapped to screen
/// intensity. One closed, tested convention (§41).
/// </param>
/// <param name="InkChannelCount">
/// How many inks the file carries per pixel — four process plus the named spot, for an accepted
/// production output. Deliberately PrintFlow's vocabulary rather than the TIFF's own tag name:
/// the accepted boundary rule keeps parser vocabulary inside Infrastructure, and an operator
/// reading "5 ink channels" is being told the production fact rather than a tag (§27, §43).
/// </param>
public sealed record DecodedTiffReview(
    int PixelWidth,
    int PixelHeight,
    int PreviewPixelWidth,
    int PreviewPixelHeight,
    int InkChannelCount,
    int BitsPerSample,
    double XResolutionDpi,
    double YResolutionDpi,
    string ColourMode,
    string WhiteInkChannelName,
    long WhiteInkSampleCount,
    string ColourConversion,
    string WhiteInkPolarity,
    ReadOnlyMemory<byte> ColourPayload,
    ReadOnlyMemory<byte> WhiteInkPayload,
    ReadOnlyMemory<byte> OverlayPayload)
{
    /// <summary>True when the payloads are a reduced-size stand-in rather than every pixel.</summary>
    public bool IsDownsampledForDisplay =>
        PreviewPixelWidth < PixelWidth || PreviewPixelHeight < PixelHeight;
}

/// <summary>
/// Turns one managed production TIFF into the bytes a specialist review surface can display
/// (SCRUM-11104 §43).
/// </summary>
/// <remarks>
/// Deliberately takes a <see cref="WorkspaceFileRef"/> and never a path, exactly as
/// <see cref="IImagePreviewDecoder"/> does: the implementation resolves it through
/// <see cref="IWorkspace"/>, so the containment rules that govern every other file operation
/// govern this one too and no caller can name a file of its own choosing.
/// <para>
/// It also takes the <see cref="Sha256"/> the caller believes it is reviewing. That is the whole
/// of the binding rule: a decoder that produced pixels for whatever happened to be at the path
/// would let one file be displayed while another was approved, so the bytes are hashed and the
/// result is refused when it does not match (§16, §17, §40).
/// </para>
/// <para>
/// The port is a display seam and nothing else. It creates no Revision, records no review, and
/// is never a validation gate: a file reaches it only after
/// <c>ProductionTiffInspector</c> has already accepted it and a <c>PrintOutput</c> exists (§45).
/// </para>
/// </remarks>
public interface ITiffReviewDecoder
{
    /// <summary>The longest edge, in pixels, a payload may have before every one is reduced.</summary>
    int MaximumDisplayEdge { get; }

    /// <summary>
    /// Decodes <paramref name="file"/> into the three review payloads, or refuses.
    /// </summary>
    /// <remarks>
    /// Reads only; never writes. Fails with <see cref="FailureCode.RevisionIntegrityMismatch"/>
    /// when the bytes on disk are not <paramref name="expectedSha256"/>, and with
    /// <see cref="FailureCode.OutputValidationFailed"/> when they are not an accepted production
    /// TIFF. Neither outcome changes anything about the session.
    /// </remarks>
    Task<OperationResult<DecodedTiffReview>> DecodeAsync(
        WorkspaceFileRef file, Sha256 expectedSha256, CancellationToken cancellationToken);
}
