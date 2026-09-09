using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;
using FileWorkspace = PrintFlow.Infrastructure.Workspace.FileWorkspace;

namespace PrintFlow.Tests.Integration.Files;

/// <summary>
/// Where a display alpha byte is allowed to come from (Epic 11600 Part D1 §4, §6, §7, §9, §12).
/// </summary>
/// <remarks>
/// A valid Production TIFF — 945 × 945, CMYK plus one non-empty Photoshop W1 spot channel, and
/// no alpha by the Production TIFF contract — previewed as a fully invisible image. The artefact
/// was never at fault: WIC converts the five-sample frame to BGRA32 and puts the fifth sample,
/// which is ink, where alpha belongs. Every pixel then reads as fully transparent while its
/// colour is still sitting right there in the payload.
/// <para>
/// So the rule these tests pin down is one about <i>provenance</i>, not about channel counts.
/// Alpha survives the preview only when the source frame positively said it had alpha; when the
/// source never said so, the byte WIC produced is a conversion artefact and the preview is drawn
/// opaque. Nothing here touches a file: every assertion is about the disposable preview payload,
/// and the TIFF's bytes are re-read afterwards to prove it.
/// </para>
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class PreviewAlphaNormalizationTests
{
    // -----------------------------------------------------------------------------
    // §7: the minimal CMYK + W1 reproduction
    // -----------------------------------------------------------------------------

    /// <summary>
    /// The defect, in four pixels: WIC's own conversion makes an all-ink W1 channel look like
    /// total transparency (§4, §5, §7).
    /// </summary>
    /// <remarks>
    /// This is the "before" half of the regression and it deliberately does not go through the
    /// decoder. It reproduces exactly what the decoder used to do — convert to BGRA32 and keep
    /// the result verbatim — so the test still fails loudly if some future WIC stops mapping the
    /// fifth sample into alpha and the fix quietly becomes untested rather than unnecessary.
    /// </remarks>
    [Fact]
    public void WICs_own_conversion_of_a_CMYK_W1_frame_reports_every_pixel_transparent()
    {
        using TempWorkspace workspace = new();
        string path = WriteMinimalCmykW1Tiff(workspace);

        BitmapSource frame = DecodeFrame(path);
        WicPixelFormats.SourceAlpha(frame).ShouldBeNull(
            "a five-sample separated frame is a format PrintFlow cannot name, which is not the " +
            "same as a format it knows carries alpha");

        byte[] converted = new byte[frame.PixelWidth * frame.PixelHeight * 4];
        new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0)
            .CopyPixels(converted, frame.PixelWidth * 4, 0);

        AlphaBytes(converted).ShouldAllBe(alpha => alpha == 0);
        ColourBytes(converted).ShouldContain(colour => colour != 0,
            "the colour never went anywhere — only the alpha byte was wrong");
    }

    /// <summary>
    /// The same four pixels through the real decoder: opaque, and the same colour (§7, §9).
    /// </summary>
    [Fact]
    public async Task A_CMYK_W1_TIFF_previews_opaque_with_its_colour_bytes_untouched()
    {
        using TempWorkspace workspace = new();
        string path = WriteMinimalCmykW1Tiff(workspace);

        BitmapSource frame = DecodeFrame(path);
        byte[] beforeNormalisation = new byte[frame.PixelWidth * frame.PixelHeight * 4];
        new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0)
            .CopyPixels(beforeNormalisation, frame.PixelWidth * 4, 0);

        DecodedPreview preview = await DecodeAsync(workspace, path);

        byte[] payload = SyntheticImages.DecodePayloadBgra(preview.Payload, out int width, out int height);
        (width, height).ShouldBe((2, 2));

        AlphaBytes(payload).ShouldAllBe(alpha => alpha == 255);
        ColourBytes(payload).ShouldBe(ColourBytes(beforeNormalisation),
            "alpha normalisation may not become a colour conversion");

        preview.HasTransparency.ShouldBeFalse(
            "the Production TIFF contract says this artefact has no alpha, and the preview must " +
            "not contradict it merely because WIC produced an alpha byte");
    }

    /// <summary>The file the preview looked at is byte-identical afterwards (§8, §10).</summary>
    [Fact]
    public async Task Previewing_a_CMYK_W1_TIFF_does_not_write_to_it()
    {
        using TempWorkspace workspace = new();
        string path = WriteMinimalCmykW1Tiff(workspace);

        byte[] before = File.ReadAllBytes(path);

        await DecodeAsync(workspace, path);

        File.ReadAllBytes(path).ShouldBe(before);
    }

    // -----------------------------------------------------------------------------
    // §6: real transparency is still real
    // -----------------------------------------------------------------------------

    /// <summary>
    /// A PNG that genuinely carries alpha keeps every value of it — 0, partial and 255 (§6).
    /// </summary>
    /// <remarks>
    /// The partial value is the one that matters. Forcing opacity would flatten it to 255 just
    /// as it flattens the fully transparent pixel, so a test that only asked "are some pixels
    /// still transparent" could pass against a half-broken rule.
    /// </remarks>
    [Fact]
    public async Task A_transparent_PNG_keeps_its_alpha_exactly()
    {
        using TempWorkspace workspace = new();
        string path = workspace.CreateSourceFile(
            "alpha.png",
            SyntheticImages.PngWithAlpha(3, 1, (x, _) => x switch
            {
                0 => (byte)0x00,
                1 => (byte)0x7F,
                _ => (byte)0xFF,
            }));

        DecodedPreview preview = await DecodeAsync(workspace, path);

        preview.HasTransparency.ShouldBeTrue();
        AlphaBytes(SyntheticImages.DecodePayloadBgra(preview.Payload, out _, out _))
            .ShouldBe([0x00, 0x7F, 0xFF]);
    }

    /// <summary>
    /// An indexed image's transparency lives in its palette, and is read from there (§4, §6).
    /// </summary>
    /// <remarks>
    /// <c>Indexed8</c> names no alpha channel, so a rule that asked only the format name would
    /// call this "not positively identified" and paint the transparent entry opaque. Asking the
    /// palette turns an unknown into a positive fact, which is exactly the distinction §5 draws.
    /// </remarks>
    [Fact]
    public async Task An_indexed_image_with_a_transparent_palette_entry_keeps_that_transparency()
    {
        using TempWorkspace workspace = new();
        string path = workspace.CreateSourceFile(
            "indexed.gif", SyntheticImages.IndexedWithTransparentEntry(2, 1));

        DecodedPreview preview = await DecodeAsync(workspace, path);

        preview.HasTransparency.ShouldBeTrue();
        AlphaBytes(SyntheticImages.DecodePayloadBgra(preview.Payload, out _, out _))
            .ShouldBe([0x00, 0xFF]);
    }

    // -----------------------------------------------------------------------------
    // §12: ordinary images did not change
    // -----------------------------------------------------------------------------

    /// <summary>An image with no alpha at all still previews opaque, at its own size (§12).</summary>
    [Theory]
    [InlineData("opaque.png")]
    [InlineData("opaque.jpg")]
    public async Task An_image_with_no_alpha_channel_previews_opaque_at_its_own_size(string fileName)
    {
        using TempWorkspace workspace = new();
        byte[] bytes = fileName.EndsWith(".png", StringComparison.Ordinal)
            ? SyntheticImages.OpaqueRgbPng(4, 3, (x, y) => ((byte)(x * 20), (byte)(y * 30), (byte)0x40))
            : SyntheticImages.Jpeg(4, 3);
        string path = workspace.CreateSourceFile(fileName, bytes);

        DecodedPreview preview = await DecodeAsync(workspace, path);

        preview.HasTransparency.ShouldBeFalse();
        preview.PixelWidth.ShouldBe(4);
        preview.PixelHeight.ShouldBe(3);
        preview.SourcePixelWidth.ShouldBe(4);
        preview.SourcePixelHeight.ShouldBe(3);
        preview.IsDownsampledForDisplay.ShouldBeFalse();

        byte[] payload = SyntheticImages.DecodePayloadBgra(preview.Payload, out int width, out int height);
        (width, height).ShouldBe((4, 3));
        AlphaBytes(payload).ShouldAllBe(alpha => alpha == 255);
    }

    // -----------------------------------------------------------------------------
    // §12: through the real service, on a Revision of a real session
    // -----------------------------------------------------------------------------

    /// <summary>
    /// The whole seam: a CMYK+W1 TIFF held as a Revision previews visibly (§12).
    /// </summary>
    /// <remarks>
    /// Nothing is doubled — real workspace, real database, real repository, real WIC decoder —
    /// because the defect this closes was invisible to every layer above the payload bytes.
    /// <para>
    /// The TIFF reaches the session the way a production TIFF actually does: as the Photoshop
    /// step's own output Revision, produced by driving the real Generate Print TIFF workflow to
    /// final review against an accepted separated-CMYK + W1 file. It used to be dropped in
    /// through <c>ImportAsync</c>, which was always a shortcut and is now refused outright —
    /// TIFF is an output of this product and not one of its inputs
    /// (<see cref="SupportedInputFormats"/>; MVP design §9.2), so importing one would have
    /// started a session with no production path. Coming through the workflow makes this the
    /// stronger statement anyway: the bytes previewed are the ones the Photoshop step wrote.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_CMYK_W1_TIFF_Revision_previews_visibly_through_the_preview_service()
    {
        using HomeScreenHarness harness = TiffFinalReviewFixture.Harness(out _);
        TiffFinalReviewFixture.Review review =
            await TiffFinalReviewFixture.ReviewRequiredAsync(harness, "preview-alpha.png");

        SessionAggregate aggregate = await review.ReloadAsync();
        Revision tiff = aggregate.Revisions.Single(
            revision => revision.Operation == OperationKind.PhotoshopOutput);
        tiff.Facts.Format.ShouldBe(ImageFormat.Tiff);

        string absolute = harness.Inner.FileWorkspace.ResolveAbsolute(tiff.File);
        byte[] before = File.ReadAllBytes(absolute);

        OperationResult<ImagePreview> preview = await harness.Previews.GetPreviewAsync(
            review.Id, tiff.Id, CancellationToken.None);

        preview.IsSuccess.ShouldBeTrue(preview.IsFailure ? preview.Failure.ToString() : "");
        preview.Value.HasTransparency.ShouldBeFalse();

        byte[] alpha = AlphaBytes(
            SyntheticImages.DecodePayloadBgra(preview.Value.Payload, out _, out _));
        alpha.ShouldNotBeEmpty();
        alpha.ShouldAllBe(value => value == 255,
            "an operator cannot review an artefact the review screen draws as nothing");

        File.ReadAllBytes(absolute).ShouldBe(before, "a preview reads; it never writes");
    }

    // -----------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------

    /// <summary>
    /// The 2 × 2 stand-in for the live artefact: distinguishable CMYK ink under a W1 channel
    /// that is full ink in every pixel.
    /// </summary>
    /// <remarks>
    /// The fifth sample is load-bearing. The inspector-facing default zeroes one sample, which
    /// is enough to make the channel non-empty but leaves three of four pixels opaque after
    /// conversion — not the all-transparent result the 945 × 945 artefact showed.
    /// </remarks>
    private static ProductionTiffFixtureOptions MinimalCmykW1Options => new(
        CmykSamples: [0x10, 0x40, 0x80, 0x20],
        FifthSampleEverywhere: 0);

    private static string WriteMinimalCmykW1Tiff(TempWorkspace workspace)
    {
        string path = Path.Combine(workspace.Root, "minimal-cmyk-w1.tif");
        ProductionTiffFixture.WriteAt(path, MinimalCmykW1Options);
        return path;
    }

    private static async Task<DecodedPreview> DecodeAsync(TempWorkspace workspace, string absolutePath)
    {
        IImagePreviewDecoder decoder = new WicImagePreviewDecoder(new FileWorkspace(workspace.Root));

        WorkspaceFileRef file = WorkspaceFileRef.Create(
            Path.GetRelativePath(workspace.Root, absolutePath).Replace('\\', '/'),
            WorkspaceArea.Working);

        OperationResult<DecodedPreview> decoded = await decoder.DecodeAsync(file, CancellationToken.None);
        decoded.IsSuccess.ShouldBeTrue(decoded.IsFailure ? decoded.Failure.ToString() : "");
        return decoded.Value;
    }

    private static BitmapSource DecodeFrame(string absolutePath)
    {
        using FileStream stream = new(absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
            BitmapCacheOption.OnLoad).Frames[0];
    }

    private static byte[] AlphaBytes(byte[] bgra) =>
        [.. bgra.Where((_, index) => index % 4 == 3)];

    private static byte[] ColourBytes(byte[] bgra) =>
        [.. bgra.Where((_, index) => index % 4 != 3)];
}
