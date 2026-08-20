using System.IO;
using System.Windows.Media;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Trimming;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Ports;
using FileWorkspace = PrintFlow.Infrastructure.Workspace.FileWorkspace;

namespace PrintFlow.Tests.Integration.Files;

/// <summary>
/// The real processor against real PNGs written by WIC's own encoders
/// (Epic 11200 Part B §22).
/// </summary>
/// <remarks>
/// The unit tests prove the rule; these prove the plumbing around it — that the alpha the
/// scan sees is the alpha that was stored, that the file written back is a valid PNG that
/// still has an alpha channel, and above all that the two refusal cases really refuse.
/// Nothing here is a fixture checked into Git: every image is generated at test time, so
/// the repository never accumulates binary artefacts (§29).
/// </remarks>
public sealed class DeterministicTrimTests
{
    // -----------------------------------------------------------------------------
    // Case A: transparent border around opaque content
    // -----------------------------------------------------------------------------

    [Fact]
    public async Task A_transparent_border_is_cropped_away_and_the_content_survives_intact()
    {
        using TrimFixture fixture = new();
        // 12×10 canvas, opaque block from (3,2) to (7,6) inclusive.
        fixture.WriteInput(SyntheticImages.PngWithAlpha(
            12, 10, (x, y) => x is >= 3 and <= 7 && y is >= 2 and <= 6 ? (byte)255 : (byte)0));

        TrimResult result = await fixture.TrimAsync();

        result.Outcome.ShouldBe(TrimOutcome.Trimmed);
        result.ContentBounds.ShouldBe(TrimBounds.FromEdges(3, 2, 8, 7));
        result.AppliedBounds.ShouldBe(TrimBounds.FromEdges(3, 2, 8, 7));
        result.OriginalWidth.ShouldBe(12);
        result.OriginalHeight.ShouldBe(10);
        result.ResultWidth.ShouldBe(5);
        result.ResultHeight.ShouldBe(5);
        result.ProducedFile.ShouldBe(fixture.OutputRef);

        // A valid PNG, smaller, still carrying alpha, and every surviving pixel opaque —
        // which is only true if the crop landed on exactly the right rectangle.
        FileFacts facts = await fixture.InspectOutputAsync();
        facts.Format.ShouldBe(ImageFormat.Png);
        facts.PixelWidth.ShouldBe(5);
        facts.PixelHeight.ShouldBe(5);
        facts.HasAlpha.ShouldBe(true);

        byte[] plane = SyntheticImages.ReadAlphaPlane(fixture.OutputPath, out int width, out int height);
        width.ShouldBe(5);
        height.ShouldBe(5);
        plane.ShouldAllBe(alpha => alpha == 255);
    }

    /// <summary>The trim keeps the source resolution: it changes extent, not print size.</summary>
    [Fact]
    public async Task The_trimmed_output_keeps_the_source_resolution()
    {
        using TrimFixture fixture = new();
        fixture.WriteInput(SyntheticImages.PngWithAlpha(
            10, 10, (x, y) => x is >= 2 and <= 5 && y is >= 2 and <= 5 ? (byte)255 : (byte)0, dpi: 300));

        await fixture.TrimAsync();

        FileFacts facts = await fixture.InspectOutputAsync();
        facts.DpiX.ShouldNotBeNull();
        facts.DpiX!.Value.ShouldBe(300, tolerance: 0.5);
        facts.DpiY!.Value.ShouldBe(300, tolerance: 0.5);
    }

    // -----------------------------------------------------------------------------
    // Case B: content touching an edge
    // -----------------------------------------------------------------------------

    [Fact]
    public async Task Content_running_to_an_edge_is_not_clipped()
    {
        using TrimFixture fixture = new();
        // Content fills the left half and runs to the top, left and bottom edges.
        fixture.WriteInput(SyntheticImages.PngWithAlpha(10, 8, (x, _) => x < 4 ? (byte)255 : (byte)0));

        TrimResult result = await fixture.TrimAsync();

        result.Outcome.ShouldBe(TrimOutcome.Trimmed);
        result.ContentBounds.ShouldBe(TrimBounds.FromEdges(0, 0, 4, 8));
        result.ResultWidth.ShouldBe(4);
        result.ResultHeight.ShouldBe(8);

        byte[] plane = SyntheticImages.ReadAlphaPlane(fixture.OutputPath, out _, out _);
        plane.ShouldAllBe(alpha => alpha == 255);
    }

    [Fact]
    public async Task A_single_pixel_in_the_bottom_right_corner_is_kept()
    {
        using TrimFixture fixture = new();
        fixture.WriteInput(SyntheticImages.PngWithAlpha(
            16, 12, (x, y) => x == 15 && y == 11 ? (byte)255 : (byte)0));

        TrimResult result = await fixture.TrimAsync();

        result.Outcome.ShouldBe(TrimOutcome.Trimmed);
        result.ContentBounds.ShouldBe(TrimBounds.FromEdges(15, 11, 16, 12));
        result.ResultWidth.ShouldBe(1);
        result.ResultHeight.ShouldBe(1);
    }

    // -----------------------------------------------------------------------------
    // Case C: content already fills the canvas
    // -----------------------------------------------------------------------------

    [Fact]
    public async Task Content_filling_the_canvas_reports_no_change_but_still_produces_a_file()
    {
        using TrimFixture fixture = new();
        fixture.WriteInput(SyntheticImages.PngWithAlpha(9, 7, (_, _) => 255));

        TrimResult result = await fixture.TrimAsync();

        result.Outcome.ShouldBe(TrimOutcome.NoChangeRequired);
        result.ContentBounds.ShouldBe(TrimBounds.Canvas(9, 7));
        result.AppliedBounds.ShouldBe(TrimBounds.Canvas(9, 7));
        result.ResultWidth.ShouldBe(9);
        result.ResultHeight.ShouldBe(7);

        // A file is still written: the step is not skippable, so the workflow needs a real
        // hashable artefact to hang its Revision and its review on (§15).
        result.ProducedFile.ShouldNotBeNull();
        File.Exists(fixture.OutputPath).ShouldBeTrue();
        (await fixture.InspectOutputAsync()).Format.ShouldBe(ImageFormat.Png);
    }

    // -----------------------------------------------------------------------------
    // Case D: nothing to crop to
    // -----------------------------------------------------------------------------

    [Fact]
    public async Task A_fully_transparent_image_demands_a_manual_crop_and_writes_nothing()
    {
        using TrimFixture fixture = new();
        fixture.WriteInput(SyntheticImages.PngWithAlpha(8, 8, (_, _) => 0));

        TrimResult result = await fixture.TrimAsync();

        result.Outcome.ShouldBe(TrimOutcome.ManualCropRequired);
        result.ContentBounds.ShouldBeNull();
        result.AppliedBounds.ShouldBeNull();
        result.ProducedFile.ShouldBeNull();
        result.ManualCropReason.ShouldNotBeNullOrWhiteSpace();

        // Nothing was guessed and nothing was fabricated.
        File.Exists(fixture.OutputPath).ShouldBeFalse();
    }

    // -----------------------------------------------------------------------------
    // Case E: no alpha channel at all
    // -----------------------------------------------------------------------------

    /// <summary>
    /// The safety-critical case: an opaque RGB photograph is never segmented by colour.
    /// </summary>
    /// <remarks>
    /// A background-removal step that was skipped, or that failed, leaves exactly this file.
    /// The tempting behaviour — assume the white corner is background and crop to the rest —
    /// destroys artwork silently and is precisely what this asserts does not happen.
    /// </remarks>
    [Fact]
    public async Task An_RGB_image_with_no_alpha_channel_demands_a_manual_crop()
    {
        using TrimFixture fixture = new();
        fixture.WriteInput(SyntheticImages.Png(10, 8, alpha: false));

        // Precondition of the test itself: the file really has no alpha to read.
        SyntheticImages.FormatOf(fixture.InputPath).ShouldNotBe(PixelFormats.Bgra32);

        TrimResult result = await fixture.TrimAsync();

        result.Outcome.ShouldBe(TrimOutcome.ManualCropRequired);
        result.ProducedFile.ShouldBeNull();
        result.OriginalWidth.ShouldBe(10);
        result.OriginalHeight.ShouldBe(8);
        File.Exists(fixture.OutputPath).ShouldBeFalse();
    }

    [Fact]
    public async Task A_jpeg_is_never_trimmed_by_guessing_at_its_background()
    {
        using TrimFixture fixture = new();
        fixture.WriteInput(SyntheticImages.Jpeg(12, 9));

        TrimResult result = await fixture.TrimAsync();

        result.Outcome.ShouldBe(TrimOutcome.ManualCropRequired);
        File.Exists(fixture.OutputPath).ShouldBeFalse();
    }

    [Fact]
    public async Task A_container_WIC_cannot_decode_demands_a_manual_crop_rather_than_failing_silently()
    {
        using TrimFixture fixture = new();
        fixture.WriteInput(SyntheticImages.PsdHeaderOnly());

        TrimResult result = await fixture.TrimAsync();

        result.Outcome.ShouldBe(TrimOutcome.ManualCropRequired);
        result.OriginalWidth.ShouldBeNull();
        File.Exists(fixture.OutputPath).ShouldBeFalse();
    }

    // -----------------------------------------------------------------------------
    // Margins, end to end
    // -----------------------------------------------------------------------------

    [Fact]
    public async Task A_uniform_margin_keeps_transparent_pixels_around_the_content()
    {
        using TrimFixture fixture = new();
        fixture.WriteInput(SyntheticImages.PngWithAlpha(
            12, 12, (x, y) => x is >= 5 and <= 6 && y is >= 5 and <= 6 ? (byte)255 : (byte)0));

        TrimResult result = await fixture.TrimAsync(TrimMargin.Uniform(2));

        result.Outcome.ShouldBe(TrimOutcome.Trimmed);
        result.ContentBounds.ShouldBe(TrimBounds.FromEdges(5, 5, 7, 7));
        result.AppliedBounds.ShouldBe(TrimBounds.FromEdges(3, 3, 9, 9));
        result.ResultWidth.ShouldBe(6);
        result.ResultHeight.ShouldBe(6);

        // The margin really is transparent canvas, not duplicated content.
        byte[] plane = SyntheticImages.ReadAlphaPlane(fixture.OutputPath, out int width, out _);
        plane[0].ShouldBe((byte)0);
        plane[(2 * width) + 2].ShouldBe((byte)255);
    }

    [Fact]
    public async Task A_margin_clamped_by_the_canvas_never_reaches_outside_the_source()
    {
        using TrimFixture fixture = new();
        fixture.WriteInput(SyntheticImages.PngWithAlpha(
            10, 10, (x, y) => x is >= 1 and <= 2 && y is >= 1 and <= 2 ? (byte)255 : (byte)0));

        TrimResult result = await fixture.TrimAsync(TrimMargin.Uniform(5));

        result.AppliedBounds.ShouldBe(TrimBounds.FromEdges(0, 0, 8, 8));
        result.ResultWidth.ShouldBe(8);
        result.ResultHeight.ShouldBe(8);
    }

    [Fact]
    public async Task A_margin_large_enough_to_reach_every_edge_reports_no_change_required()
    {
        using TrimFixture fixture = new();
        fixture.WriteInput(SyntheticImages.PngWithAlpha(
            8, 8, (x, y) => x == 4 && y == 4 ? (byte)255 : (byte)0));

        TrimResult result = await fixture.TrimAsync(TrimMargin.Uniform(100));

        result.Outcome.ShouldBe(TrimOutcome.NoChangeRequired);
        result.ContentBounds.ShouldBe(TrimBounds.FromEdges(4, 4, 5, 5));
        result.AppliedBounds.ShouldBe(TrimBounds.Canvas(8, 8));
    }

    [Fact]
    public async Task An_edge_specific_margin_grows_only_the_edges_it_names()
    {
        using TrimFixture fixture = new();
        fixture.WriteInput(SyntheticImages.PngWithAlpha(
            20, 20, (x, y) => x is >= 8 and <= 11 && y is >= 8 and <= 11 ? (byte)255 : (byte)0));

        TrimResult result = await fixture.TrimAsync(
            TrimMargin.PerEdge(top: 1, right: 2, bottom: 3, left: 4));

        result.AppliedBounds.ShouldBe(TrimBounds.FromEdges(4, 7, 14, 15));
    }

    // -----------------------------------------------------------------------------
    // Determinism and failure surfaces
    // -----------------------------------------------------------------------------

    /// <summary>The same bytes give the same answer, which is what "deterministic" has to mean.</summary>
    [Fact]
    public async Task The_same_input_produces_a_byte_identical_output_every_time()
    {
        using TrimFixture fixture = new();
        fixture.WriteInput(SyntheticImages.PngWithAlpha(
            14, 11, (x, y) => x is >= 2 and <= 9 && y is >= 3 and <= 8 ? (byte)200 : (byte)0));

        await fixture.TrimAsync();
        byte[] first = File.ReadAllBytes(fixture.OutputPath);

        await fixture.TrimAsync();
        byte[] second = File.ReadAllBytes(fixture.OutputPath);

        second.ShouldBe(first);
    }

    [Fact]
    public async Task A_missing_input_is_a_structured_failure_not_a_manual_crop()
    {
        using TrimFixture fixture = new();

        OperationResult<TrimResult> result = await fixture.Processor.TrimAsync(
            new TrimRequest(fixture.InputRef, fixture.OutputRef, TrimMargin.Tight), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.OutputMissing);
    }

    [Fact]
    public async Task Cancellation_before_the_work_starts_is_reported_as_cancelled()
    {
        using TrimFixture fixture = new();
        fixture.WriteInput(SyntheticImages.PngWithAlpha(8, 8, (_, _) => 255));

        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        OperationResult<TrimResult> result = await fixture.Processor.TrimAsync(
            new TrimRequest(fixture.InputRef, fixture.OutputRef, TrimMargin.Tight), cancelled.Token);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.Cancelled);
    }

    [Fact]
    public void The_processor_identifies_itself_so_an_attempt_records_which_algorithm_ran()
    {
        using TrimFixture fixture = new();

        fixture.Processor.ProcessorId.ShouldBe("internal-alpha-trim-v1");
    }

    /// <summary>A temp workspace holding one input file and the path its trim would write to.</summary>
    private sealed class TrimFixture : IDisposable
    {
        private const string InputRelative = "Sessions/S_trim/Working/attempt/in.png";
        private const string OutputRelative = "Sessions/S_trim/Working/attempt/trimmed.png";

        private readonly TempWorkspace _workspace = new();
        private readonly FileWorkspace _files;

        public TrimFixture()
        {
            _files = new FileWorkspace(_workspace.Root);
            Directory.CreateDirectory(
                Path.Combine(_workspace.Root, "Sessions", "S_trim", "Working", "attempt"));
            Processor = new DeterministicAlphaTrimProcessor(_files);
        }

        public ITrimProcessor Processor { get; }

        public WorkspaceFileRef InputRef { get; } =
            WorkspaceFileRef.Create(InputRelative, WorkspaceArea.Working);

        public WorkspaceFileRef OutputRef { get; } =
            WorkspaceFileRef.Create(OutputRelative, WorkspaceArea.Working);

        public string InputPath => _files.ResolveAbsolute(InputRef);

        public string OutputPath => _files.ResolveAbsolute(OutputRef);

        public void WriteInput(byte[] bytes) => File.WriteAllBytes(InputPath, bytes);

        public async Task<TrimResult> TrimAsync(TrimMargin? margin = null)
        {
            OperationResult<TrimResult> result = await Processor.TrimAsync(
                new TrimRequest(InputRef, OutputRef, margin ?? TrimMargin.Tight), CancellationToken.None);

            result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : "");
            return result.Value;
        }

        public async Task<FileFacts> InspectOutputAsync()
        {
            OperationResult<FileFacts> facts =
                await new WicFileInspector().InspectAsync(OutputPath, CancellationToken.None);
            facts.IsSuccess.ShouldBeTrue(facts.IsFailure ? facts.Failure.ToString() : "");
            return facts.Value;
        }

        public void Dispose() => _workspace.Dispose();
    }
}
