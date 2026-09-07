using System.IO;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Adapters.Photoshop;

/// <summary>
/// Decodes one validated production TIFF into the Colour, White ink and Colour + white overlay
/// payloads final review shows (SCRUM-11104 §6, §8, §9, §11, §15).
/// </summary>
/// <remarks>
/// <b>It reads the TIFF's own samples, not a codec's idea of them.</b> The generic preview seam
/// hands a five-sample separated file to WIC, which presents it as BGRA with the Photoshop spot
/// channel occupying the alpha byte — a representation PrintFlow then has to normalise to opaque
/// so the artefact is visible at all (<c>WicImagePreviewDecoder</c>). That is honest enough for
/// "show me the file", but it cannot answer "show me the white ink", because the one channel a
/// specialist review is about has been spent on transparency. So this reads the uncompressed
/// interleaved strips directly and keeps all five samples (§6, §9).
///
/// <b>Validation comes first, always.</b> Nothing here decides whether a file is a production
/// TIFF: <see cref="ProductionTiffInspector"/> does, and this refuses to produce a payload for
/// anything it did not accept. The inspector's raster is where the strips are, so there is one
/// parse and one accepted layout rather than a second reader with its own opinion (§45, §61).
///
/// <b>The bytes that hash are the bytes that draw.</b> The caller states the SHA-256 it believes
/// it is reviewing and the file is hashed before it is read. A mismatch is refused rather than
/// resolved in favour of whatever is on disk — which is what stops one file being displayed while
/// another is approved, and what makes a TIFF changed after validation fail closed instead of
/// silently re-previewing (§16, §17, §40).
///
/// <b>One decode, three payloads.</b> A mode switch on the review surface changes which frozen
/// bitmap is shown; it never re-reads 74 MB of samples (§15). Nothing is cached across calls and
/// nothing is written to disk: the file is opened, read, and released.
/// </remarks>
public sealed class ProductionTiffReviewDecoder : ITiffReviewDecoder
{
    /// <summary>
    /// How stored fifth-sample values map to screen intensity (§8, §41).
    /// </summary>
    /// <remarks>
    /// Photoshop stores a spot channel inverted with respect to the process inks beside it: in
    /// the accepted production TIFF a fifth sample of 255 is <i>no</i> white ink and 0 is 100%
    /// coverage. That is not an assumption made here — it is the convention
    /// <see cref="ProductionTiffInspector"/> already validates against, counting a channel of all
    /// 255s as "wholly empty on disk", and the same convention the Photoshop histogram bridge
    /// counts coverage with. The baseline production TIFF shows it plainly: outside the artwork
    /// C=M=Y=K=0 with W1=255, inside it W1=0.
    /// <para>
    /// So the preview inverts: displayed intensity = 255 − stored. Bright means ink, black means
    /// none, which is the polarity §8 asks for and the one an operator reads as "this is where
    /// the white goes".
    /// </para>
    /// </remarks>
    public const string WhiteInkPolarityIdentifier =
        "w1-spot-inverted-v1: stored 255 = no white ink, stored 0 = 100% white ink; " +
        "displayed intensity = 255 - stored, so bright means ink";

    /// <summary>
    /// How CMYK ink values become screen pixels (§6, §7).
    /// </summary>
    /// <remarks>
    /// The naive device conversion, stated so nobody has to guess which one it is: per channel,
    /// R = (255 − C) × (255 − K) / 255 and so on, with separated sample values read the way the
    /// TIFF specification defines them for PhotometricInterpretation 5 — 0 is no ink, 255 is full
    /// ink.
    /// <para>
    /// The embedded ICC profile is deliberately <b>not</b> applied. This is an uncalibrated
    /// screen approximation for visual inspection — "is the right artwork here, the right way up,
    /// with the white ink where I expect it" — and it makes no claim whatsoever about printed
    /// colour. Applying a profile would produce a prettier picture that invited exactly the
    /// judgement the file's structural validation explicitly does not make (§7).
    /// </para>
    /// </remarks>
    public const string ColourConversionIdentifier =
        "cmyk-naive-device-v1: R=(255-C)(255-K)/255, G=(255-M)(255-K)/255, B=(255-Y)(255-K)/255; " +
        "no ICC profile applied; an uncalibrated screen approximation, not a colour proof";

    private const double DisplayDpi = 96.0;
    private const int BytesPerBgra32Pixel = 4;

    /// <summary>The white marker drawn over colour where the fifth sample carries ink (§11, §12).</summary>
    /// <remarks>
    /// A restrained fixed-strength wash rather than coverage-proportional opacity. Proportional
    /// shading would read as "this is how white it will look", which is a printed-appearance
    /// claim; a flat marker reads as "the white ink is here", which is the only thing the overlay
    /// is entitled to say. It is applied where there is any ink at all, matching what validation
    /// counts as non-empty.
    /// </remarks>
    private const double OverlayStrength = 0.55;

    private readonly IWorkspace _workspace;
    private readonly ProductionTiffInspector _inspector = new();

    public ProductionTiffReviewDecoder(IWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        _workspace = workspace;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The same 2048 the generic preview seam uses, for the same reason and so that a TIFF and an
    /// ordinary artefact are not reduced to different degrees on the same screen. Three payloads
    /// share it, and all three are reduced by the same factor so the modes stay pixel-aligned
    /// (§35).
    /// </remarks>
    public int MaximumDisplayEdge => 2048;

    /// <inheritdoc />
    public async Task<OperationResult<DecodedTiffReview>> DecodeAsync(
        WorkspaceFileRef file, Sha256 expectedSha256, CancellationToken cancellationToken)
    {
        string absolutePath = _workspace.ResolveAbsolute(file);

        try
        {
            return await Task.Run(() => Decode(absolutePath, expectedSha256, cancellationToken), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Fail<DecodedTiffReview>(
                FailureCode.Cancelled, "Production TIFF review decoding was cancelled.");
        }
    }

    private OperationResult<DecodedTiffReview> Decode(
        string absolutePath, Sha256 expectedSha256, CancellationToken cancellationToken)
    {
        if (!File.Exists(absolutePath))
        {
            return OperationResult.Fail<DecodedTiffReview>(
                FailureCode.OutputMissing,
                $"The production TIFF is no longer present at '{absolutePath}'.");
        }

        OperationResult<Sha256> actual = HashOf(absolutePath);
        if (actual.IsFailure)
        {
            return OperationResult.Fail<DecodedTiffReview>(actual.Failure);
        }

        // Before anything is read for display. A preview of bytes that are not the reviewed
        // bytes is worse than no preview: it is a picture of one file above an approval bound
        // to another (§16, §17, §40).
        if (!actual.Value.Equals(expectedSha256))
        {
            return OperationResult.Fail<DecodedTiffReview>(
                FailureCode.RevisionIntegrityMismatch,
                $"The production TIFF at '{absolutePath}' hashes {actual.Value.ShortForm}, not the " +
                $"reviewed {expectedSha256.ShortForm}; it changed after validation and no review " +
                "payload was produced.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        OperationResult<ProductionTiffInspection> inspected = _inspector.InspectForReview(absolutePath);
        if (inspected.IsFailure)
        {
            return OperationResult.Fail<DecodedTiffReview>(inspected.Failure);
        }

        ProductionTiffInspection inspection = inspected.Value;

        try
        {
            return Compose(absolutePath, inspection, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EndOfStreamException)
        {
            return OperationResult.Fail<DecodedTiffReview>(
                FailureCode.OutputUnreadable,
                $"The production TIFF could not be read for display: {ex.Message}");
        }
        catch (Exception ex) when (ex is OverflowException or OutOfMemoryException)
        {
            return OperationResult.Fail<DecodedTiffReview>(
                FailureCode.OutputUnreadable,
                $"The production TIFF is too large to prepare for display: {ex.Message}");
        }
    }

    /// <summary>
    /// Reads the strips once and accumulates the three images together.
    /// </summary>
    /// <remarks>
    /// Reduction is a box average over an integer block, computed while the samples stream past
    /// rather than by building a full-size bitmap first: a 3307 × 4474 production TIFF would
    /// otherwise cost 59 MB per payload before anything was scaled. The same block grid serves
    /// all three images, which is what makes them the same size and the same alignment by
    /// construction rather than by three separate scalings agreeing (§35).
    /// <para>
    /// The white-ink sample count is accumulated over every source pixel, not over the reduced
    /// grid, so it can be compared with what the inspector independently established (§51).
    /// </para>
    /// </remarks>
    private OperationResult<DecodedTiffReview> Compose(
        string absolutePath, ProductionTiffInspection inspection, CancellationToken cancellationToken)
    {
        ProductionTiffRaster raster = inspection.Raster;
        int width = raster.PixelWidth;
        int height = raster.PixelHeight;
        int samples = raster.SamplesPerPixel;

        int block = ReductionBlock(width, height);
        int previewWidth = Math.Max(1, (width + block - 1) / block);
        int previewHeight = Math.Max(1, (height + block - 1) / block);

        long cells = (long)previewWidth * previewHeight;
        long[] red = new long[cells];
        long[] green = new long[cells];
        long[] blue = new long[cells];
        long[] whiteInk = new long[cells];
        int[] counts = new int[cells];
        long whiteInkSampleCount = 0;

        using FileStream stream = new(
            absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: false);

        byte[] row = new byte[checked(width * samples)];
        for (int strip = 0; strip < raster.StripOffsets.Length; strip++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            long firstRow = (long)strip * raster.RowsPerStrip;
            long rows = Math.Min(raster.RowsPerStrip, height - firstRow);
            stream.Position = raster.StripOffsets[strip];

            for (long line = 0; line < rows; line++)
            {
                ReadExactly(stream, row);

                int cellRow = (int)((firstRow + line) / block);
                int rowBase = cellRow * previewWidth;

                for (int x = 0; x < width; x++)
                {
                    int offset = x * samples;
                    byte cyan = row[offset];
                    byte magenta = row[offset + 1];
                    byte yellow = row[offset + 2];
                    byte black = row[offset + 3];
                    byte spot = row[offset + 4];

                    int cell = rowBase + (x / block);
                    red[cell] += (255 - cyan) * (255 - black) / 255;
                    green[cell] += (255 - magenta) * (255 - black) / 255;
                    blue[cell] += (255 - yellow) * (255 - black) / 255;
                    whiteInk[cell] += 255 - spot;
                    counts[cell]++;

                    if (spot != byte.MaxValue)
                    {
                        whiteInkSampleCount++;
                    }
                }
            }
        }

        byte[] colour = new byte[checked(cells * BytesPerBgra32Pixel)];
        byte[] white = new byte[colour.Length];
        byte[] overlay = new byte[colour.Length];

        for (long cell = 0; cell < cells; cell++)
        {
            int divisor = Math.Max(1, counts[cell]);
            byte r = Clamp(red[cell] / divisor);
            byte g = Clamp(green[cell] / divisor);
            byte b = Clamp(blue[cell] / divisor);
            byte ink = Clamp(whiteInk[cell] / divisor);

            long index = cell * BytesPerBgra32Pixel;
            Write(colour, index, b, g, r);
            Write(white, index, ink, ink, ink);
            Write(
                overlay,
                index,
                Blend(b, ink),
                Blend(g, ink),
                Blend(r, ink));
        }

        return OperationResult.Ok(new DecodedTiffReview(
            width,
            height,
            previewWidth,
            previewHeight,
            samples,
            inspection.Facts.BitsPerSample.IsDefaultOrEmpty ? 0 : inspection.Facts.BitsPerSample[0],
            inspection.Facts.XResolutionDpi,
            inspection.Facts.YResolutionDpi,
            "CMYK",
            inspection.Facts.ExtraChannelNames.IsDefaultOrEmpty
                ? string.Empty
                : inspection.Facts.ExtraChannelNames[0],
            whiteInkSampleCount,
            ColourConversionIdentifier,
            WhiteInkPolarityIdentifier,
            EncodePng(colour, previewWidth, previewHeight),
            EncodePng(white, previewWidth, previewHeight),
            EncodePng(overlay, previewWidth, previewHeight)));
    }

    /// <summary>How many source pixels each preview pixel averages, on both axes.</summary>
    private int ReductionBlock(int width, int height)
    {
        int longest = Math.Max(width, height);
        return longest <= MaximumDisplayEdge
            ? 1
            : (longest + MaximumDisplayEdge - 1) / MaximumDisplayEdge;
    }

    /// <summary>Marks white-ink coverage over the colour pixel without hiding it (§11, §12).</summary>
    private static byte Blend(byte colour, byte ink) =>
        Clamp((long)Math.Round(colour + (255 - colour) * (ink / 255.0) * OverlayStrength));

    private static void Write(byte[] buffer, long index, byte blue, byte green, byte red)
    {
        buffer[index] = blue;
        buffer[index + 1] = green;
        buffer[index + 2] = red;
        buffer[index + 3] = byte.MaxValue;
    }

    private static byte Clamp(long value) =>
        (byte)Math.Clamp(value, 0, byte.MaxValue);

    /// <summary>
    /// One BGRA buffer as PNG bytes at 96 dpi.
    /// </summary>
    /// <remarks>
    /// Fully opaque by construction — every alpha byte is written 255 — because a production TIFF
    /// has no transparency to express. The fifth sample is white ink, and drawing it as alpha is
    /// precisely the confusion this decoder exists to end.
    /// </remarks>
    private static byte[] EncodePng(byte[] bgra, int width, int height)
    {
        int stride = width * BytesPerBgra32Pixel;
        BitmapSource source = BitmapSource.Create(
            width, height, DisplayDpi, DisplayDpi, PixelFormats.Bgra32, null, bgra, stride);
        source.Freeze();

        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(source));

        using MemoryStream output = new();
        encoder.Save(output);
        return output.ToArray();
    }

    private static OperationResult<Sha256> HashOf(string absolutePath)
    {
        try
        {
            using FileStream stream = new(
                absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: false);
            return OperationResult.Ok(Sha256.FromBytes(SHA256.HashData(stream)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Fail<Sha256>(
                FailureCode.OutputUnreadable,
                $"The production TIFF could not be re-read for review: {ex.Message}");
        }
    }

    private static void ReadExactly(Stream stream, byte[] buffer)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int got = stream.Read(buffer, read, buffer.Length - read);
            if (got == 0)
            {
                throw new EndOfStreamException("A production TIFF strip ended early.");
            }
            read += got;
        }
    }
}
