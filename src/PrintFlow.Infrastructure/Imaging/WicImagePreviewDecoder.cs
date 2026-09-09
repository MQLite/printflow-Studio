using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Imaging;

/// <summary>
/// Decodes a managed file with WIC and re-encodes it as a display PNG
/// (Epic 11200 Part C1 §3, §6).
/// </summary>
/// <remarks>
/// <b>Full resolution up to <see cref="MaximumDisplayEdge"/>, reduced beyond it.</b> A preview
/// larger than that is a working-set problem with no operator benefit — nothing on a 1000×700
/// window can show 8000 pixels of detail at once — so the payload is scaled down and says so
/// through <see cref="DecodedPreview.IsDownsampledForDisplay"/>. The Revision itself is never
/// touched: this writes no file at all (§8, §12).
///
/// <b>The payload is normalised to BGRA at 96 dpi.</b> That is a display decision, not an image
/// edit: at 96 dpi a WPF <c>Image</c> renders one payload pixel per device-independent pixel, so
/// "100% zoom" means 100% rather than 32% for a 300-dpi artefact. The artefact's real dpi is
/// operator information and reaches the screen through <c>FileFacts</c>, which this does not
/// alter.
///
/// <b>Display alpha comes from the source, or from nothing.</b> A frame that positively carries
/// alpha keeps every decoded alpha value; a frame that does not is drawn opaque, because the
/// alpha byte WIC produces for such a frame is a conversion artefact rather than a transparency
/// the artefact expressed (Epic 11600 Part D1 §4). Colour bytes are never touched either way,
/// and neither is the file (§9, §10).
///
/// <b>Nothing is cached.</b> Each call opens the file, decodes, encodes and releases; the
/// returned bytes are the caller's and become collectable as soon as the screen drops them.
/// A process-wide image cache would keep every artefact an operator glanced at alive for the
/// life of the application, which is precisely what §6 rules out.
/// </remarks>
public sealed class WicImagePreviewDecoder : IImagePreviewDecoder, IDiagnosticImagePreviewDecoder
{
    private const int BytesPerBgra32Pixel = 4;
    private const int AlphaByteOffset = 3;
    private const double DisplayDpi = 96.0;

    private readonly IWorkspace _workspace;

    public WicImagePreviewDecoder(IWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        _workspace = workspace;
    }

    /// <inheritdoc />
    /// <remarks>
    /// 2048 keeps the largest payload at 16 MB of BGRA while still exceeding any viewport this
    /// application shows, so an operator zoomed to 100% is looking at real pixels on every
    /// image a review screen can actually fit.
    /// </remarks>
    public int MaximumDisplayEdge => 2048;

    /// <inheritdoc />
    /// <remarks>
    /// 192 is a row height, not a viewport: large enough for an operator to recognise their own
    /// artwork at a glance in a list, and small enough that a hundred of them together stay well
    /// inside the working set a single review preview is already allowed. The same decode path
    /// produces it, so a thumbnail is the artefact reduced and never a different picture.
    /// </remarks>
    public int ThumbnailEdge => 192;

    /// <inheritdoc />
    public async Task<OperationResult<DecodedPreview>> DecodeAsync(
        WorkspaceFileRef file, CancellationToken cancellationToken)
    {
        string absolutePath = _workspace.ResolveAbsolute(file);

        return await DecodeAbsoluteAsync(absolutePath, MaximumDisplayEdge, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult<DecodedPreview>> DecodeThumbnailAsync(
        WorkspaceFileRef file, CancellationToken cancellationToken)
    {
        string absolutePath = _workspace.ResolveAbsolute(file);

        return await DecodeAbsoluteAsync(absolutePath, ThumbnailEdge, cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperationResult<DecodedPreview>> DecodeDiagnosticAsync(
        string persistedAbsolutePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(persistedAbsolutePath);
        if (!Path.IsPathFullyQualified(persistedAbsolutePath))
        {
            return Task.FromResult(OperationResult.Fail<DecodedPreview>(
                FailureCode.OutputUnreadable, "The persisted diagnostic image path is not absolute."));
        }

        return DecodeAbsoluteAsync(persistedAbsolutePath, MaximumDisplayEdge, cancellationToken);
    }

    private async Task<OperationResult<DecodedPreview>> DecodeAbsoluteAsync(
        string absolutePath, int maximumEdge, CancellationToken cancellationToken)
    {

        try
        {
            return await Task.Run(() => Decode(absolutePath, maximumEdge, cancellationToken), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Fail<DecodedPreview>(
                FailureCode.Cancelled, "Preview decoding was cancelled.");
        }
    }

    private static OperationResult<DecodedPreview> Decode(
        string absolutePath, int maximumEdge, CancellationToken cancellationToken)
    {
        if (!File.Exists(absolutePath))
        {
            return OperationResult.Fail<DecodedPreview>(
                FailureCode.OutputMissing, $"Preview source not found: '{absolutePath}'.");
        }

        BitmapFrame frame;
        try
        {
            // OnLoad so the handle is released before this method returns: a preview must never
            // be the reason a later step cannot rewrite or move the file it looked at (§6).
            using FileStream stream = new(absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            BitmapDecoder decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.OnLoad);

            if (decoder.Frames.Count == 0)
            {
                return Undisplayable($"'{Path.GetFileName(absolutePath)}' decoded to no frames.");
            }

            frame = decoder.Frames[0];
            Freeze(frame);
        }
        catch (NotSupportedException)
        {
            return Undisplayable($"WIC has no codec for '{Path.GetFileName(absolutePath)}'.");
        }
        catch (FileFormatException)
        {
            return Undisplayable($"'{Path.GetFileName(absolutePath)}' is not a container WIC can read.");
        }
        catch (IOException ex)
        {
            return OperationResult.Fail<DecodedPreview>(
                FailureCode.OutputUnreadable, $"Preview source could not be read: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return OperationResult.Fail<DecodedPreview>(
                FailureCode.OutputUnreadable, $"Preview source could not be read: {ex.Message}");
        }

        int sourceWidth = frame.PixelWidth;
        int sourceHeight = frame.PixelHeight;
        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            return Undisplayable("The decoded frame reports no pixels.");
        }

        // Read from the source frame, before any conversion: the payload below is BGRA for
        // everything, so asking it would answer "transparent" for an opaque JPEG. Three-valued
        // on purpose — "unknown" is what makes the normalisation in ToDisplayBgra correct.
        bool? sourceAlpha = WicPixelFormats.SourceAlpha(frame);
        bool hasTransparency = sourceAlpha == true;

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            BitmapSource scaled = Scale(frame, sourceWidth, sourceHeight, maximumEdge);
            BitmapSource payloadSource = ToDisplayBgra(scaled, sourceAlpha == true);

            PngBitmapEncoder encoder = new();
            encoder.Frames.Add(BitmapFrame.Create(payloadSource));

            using MemoryStream output = new();
            encoder.Save(output);

            return OperationResult.Ok(new DecodedPreview(
                payloadSource.PixelWidth,
                payloadSource.PixelHeight,
                sourceWidth,
                sourceHeight,
                hasTransparency,
                output.ToArray()));
        }
        catch (NotSupportedException ex)
        {
            // A format WIC can describe but not present as BGRA — CMYK TIFF is the realistic
            // case. Honest "cannot display", never "the artefact is bad" (§21).
            return Undisplayable($"The frame could not be prepared for display: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            return Undisplayable($"The frame could not be prepared for display: {ex.Message}");
        }
        catch (OverflowException ex)
        {
            return Undisplayable($"The frame is too large to prepare for display: {ex.Message}");
        }
    }

    /// <summary>Reduces the frame so its longest edge fits <paramref name="maximumEdge"/>.</summary>
    private static BitmapSource Scale(BitmapSource frame, int width, int height, int maximumEdge)
    {
        int longest = Math.Max(width, height);
        if (longest <= maximumEdge)
        {
            return frame;
        }

        double factor = (double)maximumEdge / longest;
        TransformedBitmap scaled = new(frame, new ScaleTransform(factor, factor));
        Freeze(scaled);
        return scaled;
    }

    /// <summary>
    /// Copies the frame into a fresh BGRA32 bitmap declared at 96 dpi, normalising the alpha
    /// byte when the source never positively claimed one (Epic 11600 Part D1 §3, §4, §9).
    /// </summary>
    /// <remarks>
    /// The copy is what makes the dpi normalisation possible at all — WIC carries dpi on the
    /// source, and <see cref="PngBitmapEncoder"/> writes whatever the source declares. Bounded
    /// by <see cref="IImagePreviewDecoder.MaximumDisplayEdge"/>, so the buffer is at most 16 MB
    /// and is released with the encoded bytes.
    /// <para>
    /// <paramref name="sourceAlphaIsPositivelyIdentified"/> is the whole of the alpha rule. When
    /// the source frame genuinely carries alpha, every decoded alpha value is kept exactly as
    /// WIC produced it — a transparent PNG previews transparent, and a partly transparent one
    /// previews partly transparent. When it does not, the alpha byte of the BGRA conversion is
    /// not a transparency the artefact ever expressed: it is whatever WIC happened to place in
    /// the fourth position, which for a five-sample separated TIFF is the Photoshop spot channel.
    /// Reading that as transparency draws a fully invisible preview of a perfectly good
    /// Production TIFF, so it is normalised to opaque instead.
    /// </para>
    /// <para>
    /// Only the alpha byte is touched. B, G and R are copied out of the conversion and written
    /// back untouched, because a preview repair that quietly altered colour would be a worse
    /// defect than the one it fixed (§9). And it is only the <i>preview</i> that is normalised:
    /// this method reads a decoded frame and returns a new in-memory bitmap, so the file on disk
    /// is neither opened for writing nor re-encoded (§2, §10).
    /// </para>
    /// </remarks>
    private static BitmapSource ToDisplayBgra(BitmapSource source, bool sourceAlphaIsPositivelyIdentified)
    {
        BitmapSource bgra = source.Format == PixelFormats.Bgra32
            ? source
            : Frozen(new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0));

        int width = bgra.PixelWidth;
        int height = bgra.PixelHeight;
        int stride = width * BytesPerBgra32Pixel;

        byte[] pixels = new byte[stride * height];
        bgra.CopyPixels(pixels, stride, 0);

        if (!sourceAlphaIsPositivelyIdentified)
        {
            for (int alpha = AlphaByteOffset; alpha < pixels.Length; alpha += BytesPerBgra32Pixel)
            {
                pixels[alpha] = byte.MaxValue;
            }
        }

        return Frozen(BitmapSource.Create(
            width, height, DisplayDpi, DisplayDpi, PixelFormats.Bgra32, null, pixels, stride));
    }

    /// <summary>
    /// A file the operator's workstation cannot render.
    /// </summary>
    /// <remarks>
    /// <see cref="FailureCode.OutputUnreadable"/> rather than a new code: the existing one
    /// already means "the bytes could not be turned into something usable", and the review
    /// surface treats every preview failure the same way — show "Preview unavailable", keep the
    /// metadata and the workflow controls, change nothing about the session (§21).
    /// </remarks>
    private static OperationResult<DecodedPreview> Undisplayable(string detail) =>
        OperationResult.Fail<DecodedPreview>(FailureCode.OutputUnreadable, detail);

    private static void Freeze(Freezable freezable)
    {
        if (freezable.CanFreeze)
        {
            freezable.Freeze();
        }
    }

    private static BitmapSource Frozen(BitmapSource source)
    {
        Freeze(source);
        return source;
    }
}
