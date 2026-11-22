using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageMagick;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Adapters.Photoshop;

/// <summary>
/// Decodes one validated production TIFF into the CMYK, white ink, composite and registration
/// overlay previews for final review (SCRUM-11104 §6, §8, §9, §11, §15).
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
/// One decode prepares all four previews (§15). When the editable layer reproduces the merged
/// CMYK strips, it supplies straight colour and opacity. W1 always comes from the fifth sample.
/// </remarks>
public sealed class ProductionTiffReviewDecoder : ITiffReviewDecoder
{
    /// <summary>
    /// How stored fifth-sample values map to white opacity (§8, §41).
    /// </summary>
    /// <remarks>
    /// Photoshop stores a spot channel inverted with respect to the process inks beside it: in
    /// the accepted production TIFF a fifth sample of 255 is <i>no</i> white ink and 0 is 100%
    /// coverage. That is not an assumption made here — it is the convention
    /// <see cref="ProductionTiffInspector"/> already validates against, counting a channel of all
    /// 255s as "wholly empty on disk", and the same convention the Photoshop histogram bridge
    /// counts coverage with. The baseline production TIFF shows it plainly: outside the artwork
    /// C=M=Y=K=0 with W1=255, inside it W1=0.
    /// </remarks>
    public const string WhiteInkPolarityIdentifier =
        "w1-spot-inverted-v2: stored 255 = no white ink, stored 0 = 100% white ink; " +
        "white preview opacity = 255 - stored";

    /// <summary>
    /// The screen conversion when an embedded CMYK ICC transform is unavailable (§6, §7).
    /// </summary>
    /// <remarks>
    /// The naive device conversion, stated so nobody has to guess which one it is: per channel,
    /// R = (255 − C) × (255 − K) / 255 and so on, with separated sample values read the way the
    /// TIFF specification defines them for PhotometricInterpretation 5 — 0 is no ink, 255 is full
    /// ink.
    /// </remarks>
    public const string ColourConversionIdentifier =
        "cmyk-naive-device-v1: R=(255-C)(255-K)/255, G=(255-M)(255-K)/255, B=(255-Y)(255-K)/255; " +
        "no ICC profile applied; an uncalibrated screen approximation, not a colour proof";

    private const string EmbeddedProfileConversionIdentifier =
        "cmyk-embedded-icc-to-srgb-v1: embedded CMYK ICC profile transformed to sRGB " +
        "with relative colorimetric rendering for screen display";
    private const double DisplayDpi = 96.0;
    private const int BytesPerBgra32Pixel = 4;
    private const int CmykChannels = 4;
    private const int RgbChannels = 3;
    private const int SourcePixelsPerBand = 1024 * 1024;
    private const int OverlayRed = 230;
    private const int OverlayGreen = 54;
    private const int OverlayBlue = 176;
    private const int MaximumOverlayTintAlpha = 140;

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
    /// ordinary artefact are not reduced to different degrees on the same screen. All payloads
    /// are reduced by the same factor so the modes stay pixel-aligned (§35).
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

    private OperationResult<DecodedTiffReview> Compose(
        string absolutePath, ProductionTiffInspection inspection, CancellationToken cancellationToken)
    {
        ProductionTiffRaster raster = inspection.Raster;
        int width = raster.PixelWidth;
        int height = raster.PixelHeight;
        int block = 1 + (Math.Max(width, height) - 1) / MaximumDisplayEdge;
        int previewWidth = 1 + (width - 1) / block;
        int previewHeight = 1 + (height - 1) / block;

        using FileStream stream = new(
            absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: false);

        ColorProfile? profile = ReadCmykProfile(stream, raster.EmbeddedIccProfile);
        PreviewPixels? pixels = profile is null
            ? null
            : Render(stream, raster, previewWidth, previewHeight, block, profile, cancellationToken);
        string conversion = pixels is null ? ColourConversionIdentifier : EmbeddedProfileConversionIdentifier;
        // If a profile transform failed, render every band using the same fallback conversion.
        pixels ??= Render(stream, raster, previewWidth, previewHeight, block, null, cancellationToken)!;

        cancellationToken.ThrowIfCancellationRequested();
        return OperationResult.Ok(new DecodedTiffReview(
            width,
            height,
            previewWidth,
            previewHeight,
            raster.SamplesPerPixel,
            inspection.Facts.BitsPerSample.IsDefaultOrEmpty ? 0 : inspection.Facts.BitsPerSample[0],
            inspection.Facts.XResolutionDpi,
            inspection.Facts.YResolutionDpi,
            "CMYK",
            inspection.Facts.ExtraChannelNames.IsDefaultOrEmpty
                ? string.Empty
                : inspection.Facts.ExtraChannelNames[0],
            pixels.WhiteInkSampleCount,
            conversion,
            WhiteInkPolarityIdentifier,
            EncodePng(pixels.Colour, previewWidth, previewHeight),
            EncodePng(pixels.WhiteInk, previewWidth, previewHeight),
            EncodePng(pixels.Overlay, previewWidth, previewHeight))
        {
            CompositePayload = EncodePng(pixels.Composite, previewWidth, previewHeight),
        });
    }

    private static ColorProfile? ReadCmykProfile(Stream stream, (long Offset, int Length)? location)
    {
        if (location is not { } profile)
            return null;

        Span<byte> header = stackalloc byte[128];
        stream.Position = profile.Offset;
        stream.ReadExactly(header);
        uint profileSize = BinaryPrimitives.ReadUInt32BigEndian(header);
        if (profileSize < 132 || profileSize > profile.Length || !header.Slice(16, 4).SequenceEqual("CMYK"u8) ||
            !header.Slice(36, 4).SequenceEqual("acsp"u8))
        {
            return null;
        }

        byte[] bytes = new byte[profileSize];
        header.CopyTo(bytes);
        stream.ReadExactly(bytes.AsSpan(header.Length));
        return new ColorProfile(bytes);
    }

    /// <summary>Box-averages the four previews on one shared grid while reading source bands.</summary>
    private static PreviewPixels? Render(
        Stream stream, ProductionTiffRaster raster, int previewWidth, int previewHeight, int block,
        ColorProfile? profile, CancellationToken cancellationToken)
    {
        bool useArtwork = !raster.ArtworkChannels.IsDefaultOrEmpty;
        PreviewPixels? pixels = RenderBands(stream, raster, previewWidth, previewHeight, block,
            profile, useArtwork, cancellationToken, out bool artworkRejected);
        return artworkRejected
            ? RenderBands(stream, raster, previewWidth, previewHeight, block,
                profile, false, cancellationToken, out _)
            : pixels;
    }

    private static PreviewPixels? RenderBands(
        Stream stream, ProductionTiffRaster raster, int previewWidth, int previewHeight, int block,
        ColorProfile? profile, bool useArtwork, CancellationToken cancellationToken, out bool artworkRejected)
    {
        artworkRejected = false;
        int width = raster.PixelWidth;
        int height = raster.PixelHeight;
        int samples = raster.SamplesPerPixel;
        int rowBytes = checked(width * samples);
        int rowsPerBand = Math.Min(height, Math.Max(1, SourcePixelsPerBand / width));
        byte[] source = new byte[checked(rowBytes * rowsPerBand)];
        PreviewCell[] cells = new PreviewCell[previewWidth];
        PreviewPixels pixels = new(checked(previewWidth * previewHeight * BytesPerBgra32Pixel));
        byte[]? cmyk = profile is null ? null : new byte[checked(width * rowsPerBand * CmykChannels)];
        ProductionTiffArtworkReader? artwork = useArtwork ? new(raster, rowsPerBand) : null;
        byte[] inkForDisplay = artwork?.Samples ?? source;

        for (int band = 0; band < height;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int rows = Math.Min(rowsPerBand, height - band);
            ReadSourceRows(stream, raster, rowBytes, band, rows, source);
            if (artwork is not null &&
                (!artwork.TryReadRows(stream, band, rows) ||
                 !MatchesProductionCmyk(source.AsSpan(0, rows * rowBytes), inkForDisplay)))
            {
                artworkRejected = true;
                return null;
            }

            byte[]? rgb = null;
            if (profile is not null && cmyk is not null)
            {
                int pixelCount = width * rows;
                for (int pixel = 0; pixel < pixelCount; pixel++)
                {
                    inkForDisplay.AsSpan(pixel * samples, CmykChannels)
                        .CopyTo(cmyk.AsSpan(pixel * CmykChannels, CmykChannels));
                }

                rgb = ConvertToSrgb(cmyk.AsSpan(0, pixelCount * CmykChannels), width, rows, profile);
                if (rgb is null)
                    return null;
            }

            for (int line = 0; line < rows; line++)
            {
                int y = band + line;
                int sourceRowOffset = line * rowBytes;
                int rgbRowOffset = line * width * RgbChannels;
                for (int x = 0; x < width; x++)
                {
                    int offset = sourceRowOffset + x * samples;
                    int whiteInk = 255 - source[offset + 4];
                    if (whiteInk != 0)
                        pixels.WhiteInkSampleCount++;

                    ref PreviewCell cell = ref cells[x / block];
                    if ((source[offset] | source[offset + 1] | source[offset + 2] | source[offset + 3]) != 0)
                    {
                        int rgbOffset = rgbRowOffset + x * RgbChannels;
                        int lightness = 255 - inkForDisplay[offset + 3];
                        int red = rgb is null ? (255 - inkForDisplay[offset]) * lightness / 255 : rgb[rgbOffset];
                        int green = rgb is null ? (255 - inkForDisplay[offset + 1]) * lightness / 255 : rgb[rgbOffset + 1];
                        int blue = rgb is null ? (255 - inkForDisplay[offset + 2]) * lightness / 255 : rgb[rgbOffset + 2];
                        int opacity = artwork is null ? byte.MaxValue : inkForDisplay[offset + 4];
                        cell.AddColour(red, green, blue, opacity, whiteInk);
                    }
                    else if (whiteInk != 0)
                    {
                        cell.AddWhiteOnly(whiteInk);
                    }
                }

                int cellHeight = y % block + 1;
                if (cellHeight == block || y == height - 1)
                {
                    int rowStart = y / block * previewWidth;
                    for (int cell = 0; cell < cells.Length; cell++)
                    {
                        int cellWidth = Math.Min(block, width - cell * block);
                        cells[cell].Write(pixels, (rowStart + cell) * BytesPerBgra32Pixel,
                            (long)cellWidth * cellHeight);
                    }

                    Array.Clear(cells);
                }
            }

            band += rows;
        }

        return pixels;
    }

    private static bool MatchesProductionCmyk(ReadOnlySpan<byte> merged, ReadOnlySpan<byte> artwork)
    {
        const int samples = CmykChannels + 1;
        for (int pixel = 0; pixel < merged.Length; pixel += samples)
        {
            int opacity = artwork[pixel + CmykChannels];
            for (int ink = 0; ink < CmykChannels; ink++)
            {
                if (merged[pixel + ink] != (artwork[pixel + ink] * opacity + 127) / 255)
                    return false;
            }
        }

        return true;
    }

    private static void ReadSourceRows(
        Stream stream, ProductionTiffRaster raster, int rowBytes, int firstRow, int rows, byte[] buffer)
    {
        for (int line = 0; line < rows;)
        {
            uint row = (uint)(firstRow + line);
            int strip = (int)(row / raster.RowsPerStrip);
            uint rowInStrip = row % raster.RowsPerStrip;
            int contiguousRows = (int)Math.Min((uint)(rows - line), raster.RowsPerStrip - rowInStrip);
            stream.Position = raster.StripOffsets[strip] + (long)rowInStrip * rowBytes;
            stream.ReadExactly(buffer.AsSpan(line * rowBytes, contiguousRows * rowBytes));
            line += contiguousRows;
        }
    }

    private static byte[]? ConvertToSrgb(ReadOnlySpan<byte> cmyk, int width, int height, ColorProfile profile)
    {
        try
        {
            PixelReadSettings settings = new((uint)width, (uint)height, StorageType.Char, PixelMapping.CMYK);
            using MagickImage image = new(cmyk, settings) { RenderingIntent = RenderingIntent.Relative };
            if (!image.TransformColorSpace(profile, ColorProfiles.SRGB) || image.ColorSpace != ColorSpace.sRGB)
                return null;

            using var pixels = image.GetPixelsUnsafe();
            return pixels.ToByteArray(PixelMapping.RGB);
        }
        catch (MagickException)
        {
            return null;
        }
    }

    private sealed class PreviewPixels(int length)
    {
        public byte[] Colour { get; } = new byte[length];
        public byte[] WhiteInk { get; } = new byte[length];
        public byte[] Overlay { get; } = new byte[length];
        public byte[] Composite { get; } = new byte[length];
        public long WhiteInkSampleCount { get; set; }
    }

    private struct PreviewCell
    {
        private WeightedPixel _colour;
        private WeightedPixel _overlay;
        private long _whiteInk;
        private long _whiteOnly;

        public void AddColour(int red, int green, int blue, int opacity, int whiteInk)
        {
            _colour.Add(red, green, blue, opacity);
            _whiteInk += whiteInk;
            // W1 sits under the colour. Only coverage beyond the colour's own mask shows as white.
            int exposedWhite = Math.Max(0, whiteInk - opacity);
            _whiteOnly += exposedWhite;

            int tintAlpha = (whiteInk * MaximumOverlayTintAlpha + 127) / 255;
            _overlay.Add(Tint(red, OverlayRed, tintAlpha), Tint(green, OverlayGreen, tintAlpha),
                Tint(blue, OverlayBlue, tintAlpha), opacity);
            _overlay.Add(OverlayRed, OverlayGreen, OverlayBlue, exposedWhite);
        }

        public void AddWhiteOnly(int whiteInk)
        {
            _whiteInk += whiteInk;
            _whiteOnly += whiteInk;
            _overlay.Add(OverlayRed, OverlayGreen, OverlayBlue, whiteInk);
        }

        public readonly void Write(PreviewPixels pixels, int index, long sampleCount)
        {
            _colour.Write(pixels.Colour, index, sampleCount);
            _overlay.Write(pixels.Overlay, index, sampleCount);
            byte whiteAlpha = Average(_whiteInk, sampleCount);
            if (whiteAlpha != 0)
                WritePixel(pixels.WhiteInk, index, 255, 255, 255, whiteAlpha);

            WeightedPixel composite = _colour;
            composite.Add(255, 255, 255, _whiteOnly);
            composite.Write(pixels.Composite, index, sampleCount);
        }

        private static int Tint(int colour, int marker, int alpha) =>
            (colour * (255 - alpha) + marker * alpha + 127) / 255;
    }

    /// <summary>Accumulates premultiplied samples and emits straight BGRA without blank-pixel halos.</summary>
    private struct WeightedPixel
    {
        private long _red;
        private long _green;
        private long _blue;
        private long _alpha;

        public void Add(int red, int green, int blue, long alpha)
        {
            _red += red * alpha;
            _green += green * alpha;
            _blue += blue * alpha;
            _alpha += alpha;
        }

        public readonly void Write(byte[] buffer, int index, long sampleCount)
        {
            byte alpha = Average(_alpha, sampleCount);
            if (alpha == 0)
                return;

            WritePixel(buffer, index, Average(_red, _alpha), Average(_green, _alpha),
                Average(_blue, _alpha), alpha);
        }
    }

    private static void WritePixel(byte[] buffer, int index, byte red, byte green, byte blue, byte alpha)
    {
        buffer[index] = blue;
        buffer[index + 1] = green;
        buffer[index + 2] = red;
        buffer[index + 3] = alpha;
    }

    private static byte Average(long sum, long count) => (byte)((sum + count / 2) / count);

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
}
