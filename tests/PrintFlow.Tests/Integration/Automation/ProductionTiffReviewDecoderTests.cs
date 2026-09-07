using System.IO;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Adapters.Photoshop;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Integration.Automation;

/// <summary>
/// The specialist review decode, proven against the TIFF's own samples
/// (SCRUM-11104 §6–§10, §15–§17, §35, §41, §42).
/// </summary>
/// <remarks>
/// Every white-ink assertion here reads the decoded payload back and compares it against the
/// <b>stored fifth-sample values the fixture wrote</b>, region by region. "The channel exists" is
/// explicitly not accepted as proof (§10): a decoder that drew the alpha byte, the colour
/// luminance or a constant would pass that and fail these.
/// </remarks>
public sealed class ProductionTiffReviewDecoderTests : IDisposable
{
    private const string Baseline =
        @"D:\PrintFlowStudio\Baseline\workstation-v1\actions\authoring\FIX-CUSTOMER-DESIGN-001_W1-1PX.tif";

    /// <summary>
    /// Three bands of deliberately distinguishable stored W1: none, half, full (§10).
    /// </summary>
    /// <remarks>
    /// 255 is the accepted "no white ink" value and 0 is 100% coverage, so the expected preview
    /// runs the other way — black, mid grey, white — which is what makes a decoder that forgot to
    /// invert fail rather than merely look different.
    /// </remarks>
    private static readonly byte[] Bands = [255, 128, 0];

    private readonly TempWorkspace _workspace = new();

    // -----------------------------------------------------------------------------------
    // §8, §9, §10, §41 — the white-ink preview is the TIFF's fifth sample, inverted
    // -----------------------------------------------------------------------------------

    [Fact]
    public async Task The_white_ink_preview_reproduces_the_stored_fifth_sample_of_every_region()
    {
        Decoded decoded = await DecodeAsync(new ProductionTiffFixtureOptions(
            PixelWidth: 9, PixelHeight: 3, W1VerticalBands: Bands, CmykSamples: [0, 0, 0, 0]));

        byte[] white = decoded.Bgra(decoded.Review.WhiteInkPayload, out int width, out int height);
        (width, height).ShouldBe((9, 3));

        // Displayed intensity = 255 - stored, so no ink is black and full ink is white.
        Grey(white, width, x: 1, y: 1).ShouldBe((byte)0);
        Grey(white, width, x: 4, y: 1).ShouldBe((byte)127);
        Grey(white, width, x: 7, y: 1).ShouldBe((byte)255);

        decoded.Review.WhiteInkPolarity.ShouldBe(ProductionTiffReviewDecoder.WhiteInkPolarityIdentifier);
        decoded.Review.WhiteInkChannelName.ShouldBe("W1");
    }

    /// <summary>
    /// The white-ink preview is not the colour image in disguise (§9).
    /// </summary>
    /// <remarks>
    /// Two files with identical W1 bands and completely different CMYK must produce byte-identical
    /// white-ink payloads. A decoder deriving white from luminance, from alpha, or from anything
    /// the colour channels carry cannot satisfy that.
    /// </remarks>
    [Fact]
    public async Task The_white_ink_preview_is_independent_of_the_colour_content()
    {
        Decoded pale = await DecodeAsync(new ProductionTiffFixtureOptions(
            PixelWidth: 9, PixelHeight: 3, W1VerticalBands: Bands, CmykSamples: [0, 0, 0, 0]));
        Decoded dark = await DecodeAsync(new ProductionTiffFixtureOptions(
            PixelWidth: 9, PixelHeight: 3, W1VerticalBands: Bands, CmykSamples: [200, 180, 160, 140]));

        pale.Review.WhiteInkPayload.ToArray().ShouldBe(dark.Review.WhiteInkPayload.ToArray());
        pale.Review.ColourPayload.ToArray().ShouldNotBe(dark.Review.ColourPayload.ToArray());
    }

    /// <summary>
    /// The count the payload reports is the count validation established (§51).
    /// </summary>
    [Fact]
    public async Task The_white_ink_sample_count_agrees_with_the_production_inspector()
    {
        Decoded decoded = await DecodeAsync(new ProductionTiffFixtureOptions(
            PixelWidth: 9, PixelHeight: 3, W1VerticalBands: Bands));

        OperationResult<ProductionTiffFacts> facts = new ProductionTiffInspector().Inspect(decoded.Path);

        facts.IsSuccess.ShouldBeTrue();
        decoded.Review.WhiteInkSampleCount.ShouldBe(facts.Value.W1NonWhiteSampleCount);

        // Two of the three bands carry ink; the 255 band does not.
        decoded.Review.WhiteInkSampleCount.ShouldBe(18);
    }

    // -----------------------------------------------------------------------------------
    // §6, §7 — the colour preview is the CMYK content, converted for screen
    // -----------------------------------------------------------------------------------

    [Fact]
    public async Task The_colour_preview_converts_the_separated_CMYK_samples_for_screen()
    {
        Decoded decoded = await DecodeAsync(new ProductionTiffFixtureOptions(
            PixelWidth: 4, PixelHeight: 2, CmykSamples: [0x10, 0x40, 0x80, 0x20],
            FifthSampleEverywhere: 0));

        byte[] colour = decoded.Bgra(decoded.Review.ColourPayload, out int width, out _);

        (byte b, byte g, byte r) = Pixel(colour, width, x: 2, y: 1);
        r.ShouldBe(Expected(0x10, 0x20));
        g.ShouldBe(Expected(0x40, 0x20));
        b.ShouldBe(Expected(0x80, 0x20));

        decoded.Review.ColourMode.ShouldBe("CMYK");
        decoded.Review.ColourConversion.ShouldBe(ProductionTiffReviewDecoder.ColourConversionIdentifier);

        static byte Expected(int ink, int black) => (byte)((255 - ink) * (255 - black) / 255);
    }

    /// <summary>
    /// The direct decode agrees with an independent codec on which way the ink runs (§6, §7).
    /// </summary>
    /// <remarks>
    /// WIC is not PrintFlow's arithmetic and knows nothing about this decoder, so its own
    /// separated-CMYK conversion is a genuine second opinion on the one thing a naive formula
    /// could get catastrophically wrong: reading 0 as full ink rather than none. A tolerance is
    /// allowed because the two conversions are not required to be the same conversion — only to
    /// agree about the direction and rough magnitude.
    /// </remarks>
    [Fact]
    public async Task The_colour_preview_agrees_with_an_independent_codec_about_ink_direction()
    {
        Decoded decoded = await DecodeAsync(new ProductionTiffFixtureOptions(
            PixelWidth: 4, PixelHeight: 2, CmykSamples: [0x10, 0x40, 0x80, 0x20],
            FifthSampleEverywhere: 0));

        byte[] mine = decoded.Bgra(decoded.Review.ColourPayload, out int width, out _);
        (byte b, byte g, byte r) = Pixel(mine, width, x: 2, y: 1);

        using FileStream stream = new(decoded.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        BitmapSource frame = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
            BitmapCacheOption.OnLoad).Frames[0];
        BitmapSource converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        byte[] theirs = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(theirs, converted.PixelWidth * 4, 0);
        (byte wicB, byte wicG, byte wicR) = Pixel(theirs, converted.PixelWidth, x: 2, y: 1);

        Math.Abs(r - wicR).ShouldBeLessThanOrEqualTo(24, $"red {r} vs WIC {wicR}");
        Math.Abs(g - wicG).ShouldBeLessThanOrEqualTo(24, $"green {g} vs WIC {wicG}");
        Math.Abs(b - wicB).ShouldBeLessThanOrEqualTo(24, $"blue {b} vs WIC {wicB}");
    }

    // -----------------------------------------------------------------------------------
    // §11, §12 — the overlay marks white ink over colour without replacing either
    // -----------------------------------------------------------------------------------

    [Fact]
    public async Task The_overlay_marks_white_ink_over_the_colour_and_leaves_bare_colour_alone()
    {
        Decoded decoded = await DecodeAsync(new ProductionTiffFixtureOptions(
            PixelWidth: 9, PixelHeight: 3, W1VerticalBands: Bands,
            CmykSamples: [200, 180, 160, 140]));

        byte[] colour = decoded.Bgra(decoded.Review.ColourPayload, out int width, out _);
        byte[] overlay = decoded.Bgra(decoded.Review.OverlayPayload, out _, out _);

        // No ink: the overlay is the colour image exactly.
        Pixel(overlay, width, x: 1, y: 1).ShouldBe(Pixel(colour, width, x: 1, y: 1));

        // Full ink: lighter than the colour underneath, and not pure white — an overlay that
        // painted the artwork out would be a picture of the underbase, not of both.
        (byte b, byte g, byte r) = Pixel(overlay, width, x: 7, y: 1);
        (byte cb, byte cg, byte cr) = Pixel(colour, width, x: 7, y: 1);
        r.ShouldBeGreaterThan(cr);
        g.ShouldBeGreaterThan(cg);
        b.ShouldBeGreaterThan(cb);
        (r == 255 && g == 255 && b == 255).ShouldBeFalse("the artwork must remain visible under the marker");

        // Half ink sits between the two.
        Pixel(overlay, width, x: 4, y: 1).Red.ShouldBeInRange(cr, r);
    }

    // -----------------------------------------------------------------------------------
    // §35 — one canvas, three payloads, one geometry
    // -----------------------------------------------------------------------------------

    [Fact]
    public async Task All_three_payloads_share_the_TIFF_geometry()
    {
        Decoded decoded = await DecodeAsync(new ProductionTiffFixtureOptions(
            PixelWidth: 9, PixelHeight: 3, W1VerticalBands: Bands));

        decoded.Review.PixelWidth.ShouldBe(9);
        decoded.Review.PixelHeight.ShouldBe(3);
        decoded.Review.PreviewPixelWidth.ShouldBe(9);
        decoded.Review.PreviewPixelHeight.ShouldBe(3);
        decoded.Review.IsDownsampledForDisplay.ShouldBeFalse();

        foreach (ReadOnlyMemory<byte> payload in
                 new[] { decoded.Review.ColourPayload, decoded.Review.WhiteInkPayload, decoded.Review.OverlayPayload })
        {
            decoded.Bgra(payload, out int width, out int height);
            (width, height).ShouldBe((9, 3));
        }
    }

    /// <summary>
    /// A canvas larger than the display edge reduces every mode by the same factor, and says so.
    /// </summary>
    [Fact]
    public async Task A_canvas_beyond_the_display_edge_reduces_every_mode_identically()
    {
        Decoded decoded = await DecodeAsync(new ProductionTiffFixtureOptions(
            PixelWidth: 4200, PixelHeight: 60, W1VerticalBands: Bands));

        decoded.Review.PixelWidth.ShouldBe(4200);
        decoded.Review.IsDownsampledForDisplay.ShouldBeTrue();

        (int width, int height)[] sizes =
        [
            .. new[] { decoded.Review.ColourPayload, decoded.Review.WhiteInkPayload, decoded.Review.OverlayPayload }
                .Select(payload =>
                {
                    decoded.Bgra(payload, out int w, out int h);
                    return (w, h);
                }),
        ];

        sizes.Distinct().Count().ShouldBe(1);
        sizes[0].ShouldBe((decoded.Review.PreviewPixelWidth, decoded.Review.PreviewPixelHeight));
        Math.Max(sizes[0].width, sizes[0].height).ShouldBeLessThanOrEqualTo(2048);

        // The count is over the source canvas, not the reduced grid.
        decoded.Review.WhiteInkSampleCount.ShouldBe(4200L / 3 * 2 * 60);
    }

    // -----------------------------------------------------------------------------------
    // §16, §17, §40, §45 — the bytes that hash are the bytes that draw
    // -----------------------------------------------------------------------------------

    [Fact]
    public async Task A_TIFF_changed_after_validation_produces_no_payload()
    {
        string path = Path.Combine(_workspace.Root, "mutated.tif");
        ProductionTiffFixture.WriteAt(path, new ProductionTiffFixtureOptions(
            PixelWidth: 9, PixelHeight: 3, W1VerticalBands: Bands));
        Sha256 original = HashOf(path);

        // Still a perfectly valid production TIFF — just not the reviewed one.
        ProductionTiffFixture.WriteAt(path, new ProductionTiffFixtureOptions(
            PixelWidth: 9, PixelHeight: 3, W1VerticalBands: [0, 0, 0]));
        HashOf(path).ShouldNotBe(original);

        OperationResult<DecodedTiffReview> decoded = await Decoder()
            .DecodeAsync(Reference("mutated.tif"), original, CancellationToken.None);

        decoded.IsFailure.ShouldBeTrue();
        decoded.Failure.Code.ShouldBe(FailureCode.RevisionIntegrityMismatch);
    }

    [Fact]
    public async Task A_missing_TIFF_produces_no_payload()
    {
        OperationResult<DecodedTiffReview> decoded = await Decoder().DecodeAsync(
            Reference("absent.tif"), HashOfBytes([1, 2, 3]), CancellationToken.None);

        decoded.IsFailure.ShouldBeTrue();
        decoded.Failure.Code.ShouldBe(FailureCode.OutputMissing);
    }

    /// <summary>
    /// A file the production inspector refuses never becomes a review payload (§45).
    /// </summary>
    /// <remarks>
    /// The hash matches, so nothing about integrity stops it. What stops it is that the decode
    /// runs behind validation rather than instead of it — a four-sample TIFF has no fifth sample
    /// to draw, and inventing one is precisely what §9 forbids.
    /// </remarks>
    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    public async Task A_TIFF_the_production_inspector_refuses_produces_no_payload(ushort samples)
    {
        string path = Path.Combine(_workspace.Root, "wrong-layout.tif");
        ProductionTiffFixture.WriteAt(path, new ProductionTiffFixtureOptions(SamplesPerPixel: samples));

        OperationResult<DecodedTiffReview> decoded = await Decoder()
            .DecodeAsync(Reference("wrong-layout.tif"), HashOf(path), CancellationToken.None);

        decoded.IsFailure.ShouldBeTrue();
        decoded.Failure.Code.ShouldBe(FailureCode.OutputValidationFailed);
    }

    [Fact]
    public async Task Decoding_writes_nothing_and_leaves_the_TIFF_byte_identical()
    {
        string path = Path.Combine(_workspace.Root, "untouched.tif");
        ProductionTiffFixture.WriteAt(path, new ProductionTiffFixtureOptions(
            PixelWidth: 9, PixelHeight: 3, W1VerticalBands: Bands));
        byte[] before = File.ReadAllBytes(path);

        OperationResult<DecodedTiffReview> decoded = await Decoder()
            .DecodeAsync(Reference("untouched.tif"), HashOf(path), CancellationToken.None);

        decoded.IsSuccess.ShouldBeTrue(decoded.IsFailure ? decoded.Failure.ToString() : string.Empty);
        File.ReadAllBytes(path).ShouldBe(before, "a review decode reads; it never writes");
    }

    // -----------------------------------------------------------------------------------
    // The real thing: a genuine Photoshop production TIFF
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// The accepted baseline TIFF decodes into all three modes, and its white ink agrees with the
    /// independent inspector count (§51, §60).
    /// </summary>
    /// <remarks>
    /// This is the one file in the repository that a real production Photoshop run actually
    /// produced, which makes it the only place the polarity claim can be checked against
    /// something nobody in this slice wrote. Its background carries C=M=Y=K=0 with W1=255 and its
    /// artwork carries ink with W1=0 — so a preview that inverted the wrong way would draw a white
    /// design on a black field, and the corner assertions below would fail.
    /// </remarks>
    [Fact]
    public async Task The_accepted_baseline_production_TIFF_decodes_into_all_three_modes()
    {
        File.Exists(Baseline).ShouldBeTrue();

        FileWorkspace workspace = new(Path.GetDirectoryName(Baseline)!);
        ProductionTiffReviewDecoder decoder = new(workspace);
        WorkspaceFileRef reference = WorkspaceFileRef.Create(
            Path.GetFileName(Baseline), WorkspaceArea.Working);

        OperationResult<DecodedTiffReview> decoded = await decoder.DecodeAsync(
            reference, HashOf(Baseline), CancellationToken.None);

        decoded.IsSuccess.ShouldBeTrue(decoded.IsFailure ? decoded.Failure.ToString() : string.Empty);
        decoded.Value.PixelWidth.ShouldBe(3307);
        decoded.Value.PixelHeight.ShouldBe(4474);
        decoded.Value.InkChannelCount.ShouldBe(5);
        decoded.Value.BitsPerSample.ShouldBe(8);
        decoded.Value.ColourMode.ShouldBe("CMYK");
        decoded.Value.WhiteInkChannelName.ShouldBe("W1");

        // The same 8,228,624 the inspector counts, from the same fifth sample.
        decoded.Value.WhiteInkSampleCount.ShouldBe(8_228_624);

        byte[] white = SyntheticImages.DecodePayloadBgra(
            decoded.Value.WhiteInkPayload, out int width, out int height);
        byte[] colour = SyntheticImages.DecodePayloadBgra(decoded.Value.ColourPayload, out _, out _);

        (width, height).ShouldBe((decoded.Value.PreviewPixelWidth, decoded.Value.PreviewPixelHeight));

        // Top-left corner: no artwork, no white ink. Black in the white-ink mode, paper in colour.
        Grey(white, width, x: 0, y: 0).ShouldBeLessThan((byte)16);
        Pixel(colour, width, x: 0, y: 0).Red.ShouldBeGreaterThan((byte)239);

        // And somewhere in the middle of the design there is ink to see.
        Grey(white, width, x: width / 2, y: height / 2).ShouldBeGreaterThan((byte)200);
    }

    // -----------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------

    private sealed record Decoded(string Path, DecodedTiffReview Review)
    {
        public byte[] Bgra(ReadOnlyMemory<byte> payload, out int width, out int height) =>
            SyntheticImages.DecodePayloadBgra(payload, out width, out height);
    }

    private async Task<Decoded> DecodeAsync(ProductionTiffFixtureOptions options)
    {
        string name = Guid.NewGuid().ToString("N") + ".tif";
        string path = Path.Combine(_workspace.Root, name);
        ProductionTiffFixture.WriteAt(path, options);

        OperationResult<DecodedTiffReview> decoded = await Decoder()
            .DecodeAsync(Reference(name), HashOf(path), CancellationToken.None);

        decoded.IsSuccess.ShouldBeTrue(decoded.IsFailure ? decoded.Failure.ToString() : string.Empty);
        return new Decoded(path, decoded.Value);
    }

    private ProductionTiffReviewDecoder Decoder() => new(new FileWorkspace(_workspace.Root));

    private static WorkspaceFileRef Reference(string fileName) =>
        WorkspaceFileRef.Create(fileName, WorkspaceArea.Working);

    private static Sha256 HashOf(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Sha256.FromBytes(SHA256.HashData(stream));
    }

    private static Sha256 HashOfBytes(byte[] bytes) => Sha256.FromBytes(SHA256.HashData(bytes));

    private static (byte Blue, byte Green, byte Red) Pixel(byte[] bgra, int width, int x, int y)
    {
        int index = ((y * width) + x) * 4;
        return (bgra[index], bgra[index + 1], bgra[index + 2]);
    }

    private static byte Grey(byte[] bgra, int width, int x, int y)
    {
        (byte blue, byte green, byte red) = Pixel(bgra, width, x, y);
        blue.ShouldBe(green);
        green.ShouldBe(red);
        return red;
    }

    public void Dispose()
    {
        _workspace.Dispose();
        GC.SuppressFinalize(this);
    }
}
