using System.IO;
using System.Windows.Media;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Trimming;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Ports;
using FileWorkspace = PrintFlow.Infrastructure.Workspace.FileWorkspace;

namespace PrintFlow.Tests.Integration.Files;

/// <summary>
/// <see cref="WicManualCropProcessor"/> against real synthetic images (Epic 11200 Part C2 §27).
/// </summary>
/// <remarks>
/// Real files and real WIC throughout: the whole claim of this slice is that the operator's
/// rectangle becomes exactly those pixels on disk, and a doubled decoder would prove nothing
/// about that. Every assertion below reads the output back through WIC rather than trusting what
/// was written.
/// <para>
/// The test that matters most is the opaque one. Automatic trimming refuses a file with no alpha
/// on purpose (Part B), and manual crop must <b>not</b> inherit that refusal — the operator is
/// there precisely because the automatic path could not decide.
/// </para>
/// </remarks>
public sealed class ManualCropProcessorTests
{
    // -----------------------------------------------------------------------------
    // §27: transparent PNG — exact dimensions and exact pixels
    // -----------------------------------------------------------------------------

    /// <summary>
    /// A known rectangle out of a transparent PNG yields exactly those pixels.
    /// </summary>
    /// <remarks>
    /// The source's alpha is a diagonal ramp, so every pixel has a different value and the
    /// surviving block can only match if the crop took the rectangle it was asked for. A crop
    /// off by one row, or with its axes transposed, produces a different plane rather than an
    /// accidentally correct one.
    /// </remarks>
    [Fact]
    public async Task A_transparent_png_is_cropped_to_exactly_the_requested_rectangle()
    {
        using ManualCropFixture fixture = new();

        WorkspaceFileRef input = fixture.WriteInput(
            "alpha.png", SyntheticImages.PngWithAlpha(12, 10, (x, y) => (byte)(1 + (x * 10) + y)));

        TrimBounds crop = TrimBounds.FromEdges(3, 2, 8, 7);

        OperationResult<ManualCropResult> result = await fixture.CropAsync(input, crop);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : "");

        result.Value.AppliedBounds.ShouldBe(crop);
        result.Value.SourceWidth.ShouldBe(12);
        result.Value.SourceHeight.ShouldBe(10);
        result.Value.ResultWidth.ShouldBe(5);
        result.Value.ResultHeight.ShouldBe(5);

        byte[] plane = SyntheticImages.ReadAlphaPlane(
            fixture.Resolve(result.Value.ProducedFile), out int width, out int height);

        width.ShouldBe(5);
        height.ShouldBe(5);

        // Every surviving pixel is the one that stood at the same place in the source.
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                plane[(y * width) + x].ShouldBe((byte)(1 + ((x + 3) * 10) + (y + 2)));
            }
        }
    }

    /// <summary>Nothing outside the rectangle survives, on any edge (§27, "no clipping beyond").</summary>
    /// <remarks>
    /// A one-pixel crop from the interior. It is the sharpest version of the bounds claim: any
    /// off-by-one in any direction changes which single pixel comes out.
    /// </remarks>
    [Fact]
    public async Task A_single_pixel_crop_takes_that_pixel_and_no_neighbour()
    {
        using ManualCropFixture fixture = new();

        WorkspaceFileRef input = fixture.WriteInput(
            "one-pixel.png", SyntheticImages.PngWithAlpha(8, 8, (x, y) => (byte)(1 + (x * 8) + y)));

        OperationResult<ManualCropResult> result =
            await fixture.CropAsync(input, TrimBounds.FromSize(5, 3, 1, 1));
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : "");

        byte[] plane = SyntheticImages.ReadAlphaPlane(
            fixture.Resolve(result.Value.ProducedFile), out int width, out int height);

        (width, height).ShouldBe((1, 1));
        plane.ShouldBe([(byte)(1 + (5 * 8) + 3)]);
    }

    // -----------------------------------------------------------------------------
    // §27: opaque RGB — the case automatic trimming refuses
    // -----------------------------------------------------------------------------

    /// <summary>
    /// A PNG with no alpha channel is cropped normally, and its colour is untouched.
    /// </summary>
    /// <remarks>
    /// The point of the whole slice, asserted at the seam. The same file is fed to the
    /// deterministic trim first, which must still refuse it — so the two processors are shown
    /// disagreeing about this file on purpose, which is the design: one measures alpha and
    /// cannot, the other is told the answer by a human and does not need to.
    /// </remarks>
    [Fact]
    public async Task An_opaque_png_with_no_alpha_is_cropped_although_automatic_trim_refuses_it()
    {
        using ManualCropFixture fixture = new();

        WorkspaceFileRef input = fixture.WriteInput(
            "opaque.png",
            SyntheticImages.OpaqueRgbPng(12, 10, (x, y) => ((byte)(x * 20), (byte)(y * 25), (byte)0x44)));

        // The automatic path refuses this file, and keeps refusing it (Part B).
        OperationResult<TrimResult> automatic = await new DeterministicAlphaTrimProcessor(fixture.Workspace)
            .TrimAsync(
                new TrimRequest(input, fixture.Output("auto.png"), TrimMargin.Tight),
                CancellationToken.None);

        automatic.IsSuccess.ShouldBeTrue();
        automatic.Value.Outcome.ShouldBe(TrimOutcome.ManualCropRequired);

        // The operator's rectangle is honoured all the same.
        TrimBounds crop = TrimBounds.FromEdges(4, 1, 9, 8);
        OperationResult<ManualCropResult> manual = await fixture.CropAsync(input, crop);
        manual.IsSuccess.ShouldBeTrue(manual.IsFailure ? manual.Failure.ToString() : "");

        string produced = fixture.Resolve(manual.Value.ProducedFile);
        byte[] pixels = SyntheticImages.ReadBgra(produced, out int width, out int height);

        (width, height).ShouldBe((5, 7));

        // The colour is the source's, pixel for pixel. Manual crop alters no channel.
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = ((y * width) + x) * 4;
                pixels[i].ShouldBe((byte)0x44);
                pixels[i + 1].ShouldBe((byte)((y + 1) * 25));
                pixels[i + 2].ShouldBe((byte)((x + 4) * 20));
            }
        }

        // An opaque source stays opaque: nothing here invents an alpha channel.
        SyntheticImages.FormatOf(produced).ShouldBe(PixelFormats.Bgr24);
    }

    // -----------------------------------------------------------------------------
    // §27: JPEG input
    // -----------------------------------------------------------------------------

    /// <summary>A JPEG WIC can decode is cropped, and the output is a PNG.</summary>
    /// <remarks>
    /// The pixels are not asserted value by value: JPEG is lossy, so the bytes that come back
    /// are not the bytes that went in and an exact comparison would be testing the codec. What
    /// is asserted is what the crop is responsible for — the container it produced, and the
    /// rectangle it kept.
    /// </remarks>
    [Fact]
    public async Task A_jpeg_source_is_cropped_and_written_as_a_png()
    {
        using ManualCropFixture fixture = new();

        WorkspaceFileRef input = fixture.WriteInput("photo.jpg", SyntheticImages.Jpeg(16, 12));

        OperationResult<ManualCropResult> result =
            await fixture.CropAsync(input, TrimBounds.FromEdges(2, 3, 10, 9));
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : "");

        string produced = fixture.Resolve(result.Value.ProducedFile);
        SyntheticImages.ReadBgra(produced, out int width, out int height);

        (width, height).ShouldBe((8, 6));
        FormatSniffer.Detect(File.ReadAllBytes(produced)).ShouldBe(ImageFormat.Png);
    }

    // -----------------------------------------------------------------------------
    // §27: DPI
    // -----------------------------------------------------------------------------

    /// <summary>The source's resolution travels with the crop (§11, §27).</summary>
    /// <remarks>
    /// A crop changes the canvas extent, not the physical size a pixel stands for. Losing the
    /// dpi would silently rescale everything the print pipeline derives from it downstream.
    /// </remarks>
    [Theory]
    [InlineData(300.0)]
    [InlineData(600.0)]
    public async Task The_source_dpi_is_preserved(double dpi)
    {
        using ManualCropFixture fixture = new();

        WorkspaceFileRef input = fixture.WriteInput(
            $"dpi-{dpi}.png", SyntheticImages.PngWithAlpha(10, 10, (_, _) => 255, dpi));

        OperationResult<ManualCropResult> result =
            await fixture.CropAsync(input, TrimBounds.FromEdges(2, 2, 6, 6));
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : "");

        (double x, double y) = SyntheticImages.ReadDpi(fixture.Resolve(result.Value.ProducedFile));
        x.ShouldBe(dpi, 0.5);
        y.ShouldBe(dpi, 0.5);
    }

    // -----------------------------------------------------------------------------
    // §23: rectangles that are refused
    // -----------------------------------------------------------------------------

    /// <summary>
    /// A rectangle that does not fit the decoded canvas is refused, never shrunk.
    /// </summary>
    /// <remarks>
    /// The caller mapped the drag onto a canvas and clamped it there; a rectangle arriving here
    /// that still does not fit means the caller and the file disagree about the image. Cropping
    /// "as much as fits" would hide that and hand back a Revision nobody asked for.
    /// </remarks>
    [Fact]
    public async Task A_rectangle_larger_than_the_source_is_refused_rather_than_clamped()
    {
        using ManualCropFixture fixture = new();

        WorkspaceFileRef input = fixture.WriteInput(
            "small.png", SyntheticImages.PngWithAlpha(8, 8, (_, _) => 255));

        OperationResult<ManualCropResult> result =
            await fixture.CropAsync(input, TrimBounds.FromEdges(4, 4, 20, 20));

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);
        File.Exists(fixture.Resolve(fixture.Output("manual-crop.png"))).ShouldBeFalse();
    }

    /// <summary>An empty rectangle is refused before anything is decoded (§23).</summary>
    [Fact]
    public async Task An_empty_rectangle_is_refused()
    {
        using ManualCropFixture fixture = new();

        WorkspaceFileRef input = fixture.WriteInput(
            "empty-crop.png", SyntheticImages.PngWithAlpha(8, 8, (_, _) => 255));

        OperationResult<ManualCropResult> result = await fixture.CropAsync(input, default);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);
    }

    /// <summary>A missing input is reported, not thrown.</summary>
    [Fact]
    public async Task A_missing_input_reports_OutputMissing()
    {
        using ManualCropFixture fixture = new();

        OperationResult<ManualCropResult> result = await fixture.CropAsync(
            WorkspaceFileRef.Create("Sessions/nowhere/Working/gone.png", WorkspaceArea.Working),
            TrimBounds.FromEdges(0, 0, 2, 2));

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.OutputMissing);
    }

    // -----------------------------------------------------------------------------
    // §27: cancellation
    // -----------------------------------------------------------------------------

    /// <summary>A cancelled crop produces no usable output (§27).</summary>
    /// <remarks>
    /// The negative half is the important one: no file the orchestrator could go on to inspect,
    /// hash and record as a Revision. A cancellation that left a half-written PNG behind would
    /// be exactly the "usable output Revision" §27 forbids.
    /// </remarks>
    [Fact]
    public async Task A_cancelled_crop_produces_no_output()
    {
        using ManualCropFixture fixture = new();

        WorkspaceFileRef input = fixture.WriteInput(
            "cancelled.png", SyntheticImages.PngWithAlpha(16, 16, (_, _) => 255));

        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        OperationResult<ManualCropResult> result = await fixture.CropAsync(
            input, TrimBounds.FromEdges(2, 2, 10, 10), cancelled.Token);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.Cancelled);
        File.Exists(fixture.Resolve(fixture.Output("manual-crop.png"))).ShouldBeFalse();
    }

    /// <summary>The processor identity is stable, because attempts record it (§14).</summary>
    [Fact]
    public void The_processor_identity_is_the_stated_one()
    {
        using ManualCropFixture fixture = new();
        fixture.Processor.ProcessorId.ShouldBe("internal-manual-crop-v1");
    }

    // -----------------------------------------------------------------------------

    /// <summary>
    /// A throwaway workspace holding one attempt directory, with the real processor over it.
    /// </summary>
    /// <remarks>
    /// The real <c>FileWorkspace</c> rather than a stub, so the reference-to-path resolution
    /// under test is the one production uses — the processor never sees a path it did not get
    /// from the workspace.
    /// </remarks>
    private sealed class ManualCropFixture : IDisposable
    {
        private const string AttemptDirectory = "Sessions/S_crop/Working/attempt";

        private readonly TempWorkspace _root = new();

        public ManualCropFixture()
        {
            Workspace = new FileWorkspace(_root.Root);
            Processor = new WicManualCropProcessor(Workspace);

            Directory.CreateDirectory(Path.Combine(
                _root.Root, AttemptDirectory.Replace('/', Path.DirectorySeparatorChar)));
        }

        public IWorkspace Workspace { get; }

        public IManualCropProcessor Processor { get; }

        public WorkspaceFileRef WriteInput(string fileName, byte[] bytes)
        {
            WorkspaceFileRef reference = Output(fileName);
            File.WriteAllBytes(Workspace.ResolveAbsolute(reference), bytes);
            return reference;
        }

        public WorkspaceFileRef Output(string fileName) =>
            WorkspaceFileRef.Create($"{AttemptDirectory}/{fileName}", WorkspaceArea.Working);

        public string Resolve(WorkspaceFileRef reference) => Workspace.ResolveAbsolute(reference);

        public Task<OperationResult<ManualCropResult>> CropAsync(
            WorkspaceFileRef input, TrimBounds crop, CancellationToken cancellationToken = default) =>
            Processor.CropAsync(
                new ManualCropRequest(input, Output("manual-crop.png"), crop), cancellationToken);

        public void Dispose() => _root.Dispose();
    }
}
