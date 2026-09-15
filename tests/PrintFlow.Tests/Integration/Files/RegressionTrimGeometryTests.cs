using System.Collections.Immutable;
using System.IO;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Trimming;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Tests.Regression;
using PrintFlow.Tests.Smoke;
using PrintFlow.Workflow.Ports;
using FileWorkspace = PrintFlow.Infrastructure.Workspace.FileWorkspace;

namespace PrintFlow.Tests.Integration.Files;

/// <summary>
/// The <c>printflow-regression-v3</c> fine-hair trim contract at the actual caller boundary.
/// </summary>
/// <remarks>
/// Small synthetic PNGs only; no fixture image, desktop or lease. Positive cases use the real
/// <see cref="DeterministicAlphaTrimProcessor"/>, so the independent expectation is shown to agree
/// with the Product, and every negative case changes exactly one fact the check must not miss.
/// Colour varies per pixel, so an offset crop cannot match by accident.
/// </remarks>
public sealed class RegressionTrimGeometryTests
{
    [Fact]
    public async Task Full_canvas_alpha_passes_only_when_the_output_pixels_are_the_input()
    {
        using TrimCase trim = new();

        // Transparent interior, with alpha 1 as the extremum on every edge: a threshold anywhere
        // in the expectation would shrink the rectangle and disagree with the Product's geometry.
        trim.WriteInput(Png(24, 18, (x, y) =>
            (x, y) is (7, 0) or (0, 5) or (23, 9) or (12, 17) ? (byte)1
            : x is >= 6 and <= 17 && y is >= 4 and <= 13 ? (byte)255
            : (byte)0));
        TrimResult result = await trim.RunProductAsync(TrimMargin.Tight);
        result.Outcome.ShouldBe(TrimOutcome.NoChangeRequired);

        RegressionAssertion passed = trim.Verify(TrimMargin.Tight, Geometry(result));
        passed.Name.ShouldBe("trimMatchesAlphaBoundsAndMargins");
        passed.Held.ShouldBeTrue(passed.Detail);
        passed.Detail.ShouldContain("Full 24x18 extent retained because alpha>0 content reaches top/right/bottom/left");
        passed.Detail.ShouldContain("Output pixels equal input region [0,0 -> 24,18) 24x18: yes");

        trim.WriteOutput(Crop(trim.InputBytes, 0, 0, 24, 18, pixels => pixels[((3 * 24) + 10) * 4 + 2] ^= 0x40));
        RegressionAssertion changed = trim.Verify(TrimMargin.Tight, Geometry(result));
        changed.Held.ShouldBeFalse();
        changed.Detail.ShouldContain("NO, first difference at output (10,3) channel R");
    }

    [Fact]
    public async Task A_removable_border_passes_its_exact_crop_and_refuses_an_unchanged_copy()
    {
        using TrimCase trim = new();
        trim.WriteInput(BorderedInput());
        TrimResult result = await trim.RunProductAsync(TrimMargin.Tight);

        RegressionAssertion passed = trim.Verify(TrimMargin.Tight, Geometry(result));
        passed.Held.ShouldBeTrue(passed.Detail);
        passed.Detail.ShouldContain("Independent alpha>0 content [4,3 -> 25,16) 21x13");
        passed.Detail.ShouldContain("removed border top 3, right 5, bottom 4, left 4 px");

        // A no-op: the input copied as the output, with the Product's correct geometry.
        trim.WriteOutput(trim.InputBytes);
        RegressionAssertion unchanged = trim.Verify(TrimMargin.Tight, Geometry(result));
        unchanged.Held.ShouldBeFalse();
        unchanged.Detail.ShouldContain("Output size 30x20 vs expected 21x13 MISMATCH");

        // A no-op that also claims the full canvas was the content: the scan refuses the claim.
        TrimGeometry claimed = TrimGeometry.Create(TrimBounds.Canvas(30, 20), TrimBounds.Canvas(30, 20));
        RegressionAssertion lied = trim.Verify(TrimMargin.Tight, claimed);
        lied.Held.ShouldBeFalse();
        lied.Detail.ShouldContain("persisted ContentBounds [0,0 -> 30,20) 30x20 MISMATCH");
    }

    [Fact]
    public async Task A_plausible_size_at_the_wrong_offset_or_with_content_removed_is_refused()
    {
        using TrimCase trim = new();
        trim.WriteInput(BorderedInput());
        TrimResult result = await trim.RunProductAsync(TrimMargin.Tight);

        trim.WriteOutput(Crop(trim.InputBytes, 5, 3, 21, 13));
        RegressionAssertion shifted = trim.Verify(TrimMargin.Tight, Geometry(result));
        shifted.Held.ShouldBeFalse();
        shifted.Detail.ShouldContain("Output size 21x13 vs expected 21x13 match");
        shifted.Detail.ShouldContain("NO, first difference");

        // Overcropped by one column, with geometry that agrees with the overcrop.
        trim.WriteOutput(Crop(trim.InputBytes, 5, 3, 20, 13));
        TrimBounds overcrop = TrimBounds.FromSize(5, 3, 20, 13);
        RegressionAssertion removed = trim.Verify(TrimMargin.Tight, TrimGeometry.Create(overcrop, overcrop));
        removed.Held.ShouldBeFalse();
        removed.Detail.ShouldContain("persisted ContentBounds [5,3 -> 25,16) 20x13 MISMATCH");
    }

    [Fact]
    public async Task Recorded_asymmetric_margins_are_expanded_and_clamped_exactly()
    {
        using TrimCase trim = new();

        // Content [4,3 -> 28,16); the 5 px right margin clamps at the 30 px canvas edge.
        trim.WriteInput(Png(30, 20, (x, y) => x is >= 4 and <= 27 && y is >= 3 and <= 15 ? (byte)180 : (byte)0));
        TrimMargin margin = TrimMargin.PerEdge(top: 2, right: 5, bottom: 1, left: 3);
        TrimResult result = await trim.RunProductAsync(margin);

        RegressionAssertion passed = trim.Verify(margin, Geometry(result));
        passed.Held.ShouldBeTrue(passed.Detail);
        passed.Detail.ShouldContain("Expected applied (content + margin, clamped) [1,1 -> 30,17) 29x16");
        passed.Detail.ShouldContain("removed border top 1, right 0, bottom 3, left 1 px");

        TrimGeometry wrongApplied = TrimGeometry.Create(
            result.ContentBounds!.Value, TrimBounds.FromEdges(0, 1, 30, 17));
        RegressionAssertion tampered = trim.Verify(margin, wrongApplied);
        tampered.Held.ShouldBeFalse();
        tampered.Detail.ShouldContain("persisted AppliedBounds [0,1 -> 30,17) 30x16 MISMATCH");

        // The recorded margin, not a remembered one, shapes the expectation.
        RegressionAssertion tight = trim.Verify(TrimMargin.Tight, Geometry(result));
        tight.Held.ShouldBeFalse();
        tight.Detail.ShouldContain("persisted AppliedBounds [1,1 -> 30,17) 29x16 MISMATCH");
    }

    [Fact]
    public async Task Revision_bytes_lineage_caller_binding_attempt_choice_and_format_are_all_required()
    {
        using TrimCase trim = new();
        trim.WriteInput(BorderedInput());
        TrimResult result = await trim.RunProductAsync(TrimMargin.Tight);
        Evidence good = trim.Snapshot(TrimMargin.Tight, Geometry(result));
        trim.Check(good).Held.ShouldBeTrue();

        trim.Check(good with { Output = good.Output with { SourceRevisionId = RevisionId.From(Guid.NewGuid()) } })
            .Detail.ShouldContain("not a Trim of input");
        trim.Check(good with { Output = good.Output with { Operation = OperationKind.PromoteApproved } })
            .Detail.ShouldContain("not a Trim of input");
        trim.Check(good, attempts: [good.Attempt with { OutputRevisionId = null }])
            .Detail.ShouldContain("does not name both");

        // The verified output must be the artefact the caller approves and exports.
        trim.Check(good, trimmed: good.Output.Facts with { Sha256 = good.Input.Sha256 })
            .Detail.ShouldContain("holds Trim artefact");

        // The latest successful attempt decides; an earlier one or a later failure does not.
        ProcessingAttempt stale = good.Attempt with
        {
            Id = AttemptId.From(Guid.NewGuid()),
            TrimGeometry = TrimGeometry.Create(TrimBounds.Canvas(30, 20), TrimBounds.Canvas(30, 20)),
        };
        ProcessingAttempt failedLater = good.Attempt with
        {
            Id = AttemptId.From(Guid.NewGuid()),
            Status = AttemptStatus.Failed,
            OutputRevisionId = null,
            TrimGeometry = null,
        };
        trim.Check(good, attempts: [stale, good.Attempt, failedLater]).Held.ShouldBeTrue();
        trim.Check(good, attempts: [good.Attempt, stale]).Detail.ShouldContain("ContentBounds [0,0 -> 30,20) 30x20 MISMATCH");

        // Files that are not the Revision's recorded bytes, or are gone, are refused.
        trim.WriteOutput(Crop(trim.InputBytes, 4, 3, 21, 13, pixels => pixels[2] ^= 1));
        trim.Check(good).Detail.ShouldContain("These are not the Revision's bytes");
        trim.DeleteOutput();
        trim.Check(good).Detail.ShouldContain("does not exist");

        // A stored format other than straight 8-bit BGRA is refused, never converted.
        trim.WriteInput(SyntheticImages.OpaqueRgbPng(6, 4, (x, y) => ((byte)x, (byte)y, 0)));
        trim.WriteOutput(trim.InputBytes);
        Evidence opaque = trim.Snapshot(
            TrimMargin.Tight, TrimGeometry.Create(TrimBounds.Canvas(6, 4), TrimBounds.Canvas(6, 4)));
        trim.Check(opaque).Detail.ShouldContain("decodes as Bgr24");
    }

    [Fact]
    public async Task The_v3_caller_refuses_missing_facts_and_keeps_v2_and_visual_review_semantics()
    {
        using TrimCase trim = new();
        trim.WriteInput(BorderedInput());
        TrimResult result = await trim.RunProductAsync(TrimMargin.Tight);

        trim.Verify(TrimMargin.Tight, Geometry(result), withAttempt: false).Detail
            .ShouldContain("No successful Trim attempt");
        trim.Verify(null, Geometry(result)).Detail.ShouldContain("recorded no margin");
        trim.Verify(TrimMargin.Tight, null).Detail.ShouldContain("persisted no ContentBounds/AppliedBounds");
        trim.Verify(TrimMargin.Tight, Geometry(result), manifest: Manifest("v3", strict: null, exact: null))
            .Detail.ShouldContain("unambiguous v3");
        trim.Verify(TrimMargin.Tight, Geometry(result), manifest: Manifest("v3", strict: true, exact: true))
            .Detail.ShouldContain("unambiguous v3");

        // v2 keeps its frozen strict-shrink assertion, by name and by rule.
        RegressionAssertion v2 = trim.Verify(TrimMargin.Tight, Geometry(result), manifest: Manifest("v2", strict: true, exact: null));
        v2.Name.ShouldBe("trimBoundsInsideCanvas");
        v2.Held.ShouldBeTrue();
        v2.Detail.ShouldBe("Trim produced 21x13 from 30x20.");

        // An input with no alpha > 0 cannot make a vacuous comparison succeed.
        trim.WriteInput(Png(8, 6, (_, _) => 0));
        trim.WriteOutput(trim.InputBytes);
        RegressionAssertion empty = trim.Verify(
            TrimMargin.Tight, TrimGeometry.Create(TrimBounds.Canvas(8, 6), TrimBounds.Canvas(8, 6)));
        empty.Held.ShouldBeFalse();
        empty.Detail.ShouldContain("no pixel with alpha > 0");

        // A held structural check does not conclude the Operator's visual question.
        RegressionCaseResult pending = new(
            "FIX-FINE-HAIR-001", "COMPLEX_BACKGROUND_FINE_HAIR", RegressionOutcome.Pending, "PrepareAsset",
            ["Trim (internal alpha bounds)"], ["Meitu XiuXiu"], ["meitu"], [],
            [new RegressionAssertion("trimMatchesAlphaBoundsAndMargins", true, "synthetic")],
            [new RegressionManualDecision("FINE-HAIR-VISUAL-001", "Are strands retained?",
                RegressionOutcome.Pending, null, null, null, null)],
            null, "Synthetic caller-boundary record.");
        pending.Conclude().Outcome.ShouldBe(RegressionOutcome.Pending);
    }

    // ==========================================================================================
    // Helpers
    // ==========================================================================================

    private static TrimGeometry Geometry(TrimResult result) =>
        TrimGeometry.Create(result.ContentBounds!.Value, result.AppliedBounds!.Value);

    /// <summary>Content [4,3 -> 25,16), with a lone alpha-1 pixel forming its left edge.</summary>
    private static byte[] BorderedInput() =>
        Png(30, 20, (x, y) =>
            (x, y) == (4, 10) ? (byte)1
            : x is >= 5 and <= 24 && y is >= 3 and <= 15 ? (byte)200
            : (byte)0);

    private static RegressionAssetManifest Manifest(string version, bool? strict, bool? exact) =>
        new(2, $"printflow-regression-{version}", version, "FIX-FINE-HAIR-001", "COMPLEX_BACKGROUND_FINE_HAIR",
            "FIX-FINE-HAIR-001.jpg", new string('0', 64), 1, "JPEG", "PrepareAsset",
            ["Trim"], ["Meitu XiuXiu 7.8.7.5"], "Structural",
            [new RegressionManualCheck("FINE-HAIR-VISUAL-001", "Are strands retained?", false)],
            new RegressionBooleanExpectation(false, null), new RegressionBooleanExpectation(false, null),
            Expectation(strict), Expectation(exact));

    private static RegressionBooleanExpectation Expectation(bool? value) =>
        value is null ? new RegressionBooleanExpectation(false, null) : new RegressionBooleanExpectation(true, value);

    /// <summary>A straight BGRA32 PNG whose colour varies per pixel and whose alpha is given.</summary>
    private static byte[] Png(int width, int height, Func<int, int, byte> alphaAt)
    {
        byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = ((y * width) + x) * 4;
                pixels[i] = (byte)((x * 13) + (y * 7));
                pixels[i + 1] = (byte)((x * 3) + (y * 29));
                pixels[i + 2] = (byte)((x ^ y) * 5);
                pixels[i + 3] = alphaAt(x, y);
            }
        }

        return Encode(width, height, pixels);
    }

    /// <summary>A region of a PNG re-encoded as its own PNG, optionally with its samples changed.</summary>
    private static byte[] Crop(byte[] png, int left, int top, int width, int height, Action<byte[]>? change = null)
    {
        byte[] source = SyntheticImages.DecodePayloadBgra(png, out int sourceWidth, out _);
        byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            Buffer.BlockCopy(source, (((top + y) * sourceWidth) + left) * 4, pixels, y * width * 4, width * 4);
        }

        change?.Invoke(pixels);
        return Encode(width, height, pixels);
    }

    private static byte[] Encode(int width, int height, byte[] bgra)
    {
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(
            BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bgra, width * 4)));
        using MemoryStream stream = new();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private sealed record Evidence(Revision Input, Revision Output, ProcessingAttempt Attempt);

    /// <summary>One cutout, its trim output, and the persisted facts a v3 caller would read.</summary>
    private sealed class TrimCase : IDisposable
    {
        private readonly TempWorkspace _workspace = new();
        private readonly FileWorkspace _files;
        private readonly SessionId _session = SessionId.From(Guid.NewGuid());

        public TrimCase()
        {
            _files = new FileWorkspace(_workspace.Root);
            Directory.CreateDirectory(Path.Combine(_workspace.Root, "Sessions", "S_v3", "Working", "attempt"));
        }

        private static WorkspaceFileRef InputRef { get; } =
            WorkspaceFileRef.Create("Sessions/S_v3/Working/attempt/cutout.png", WorkspaceArea.Working);

        private static WorkspaceFileRef OutputRef { get; } =
            WorkspaceFileRef.Create("Sessions/S_v3/Working/attempt/trimmed.png", WorkspaceArea.Working);

        public byte[] InputBytes => File.ReadAllBytes(_files.ResolveAbsolute(InputRef));

        public void WriteInput(byte[] png) => File.WriteAllBytes(_files.ResolveAbsolute(InputRef), png);

        public void WriteOutput(byte[] png) => File.WriteAllBytes(_files.ResolveAbsolute(OutputRef), png);

        public async Task<TrimResult> RunProductAsync(TrimMargin margin)
        {
            OperationResult<TrimResult> result = await new DeterministicAlphaTrimProcessor(_files)
                .TrimAsync(new TrimRequest(InputRef, OutputRef, margin), CancellationToken.None);
            result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : "");
            return result.Value;
        }

        public void DeleteOutput() => File.Delete(_files.ResolveAbsolute(OutputRef));

        /// <summary>The persisted facts for the files as they are now: Revisions, hashes and one attempt.</summary>
        public Evidence Snapshot(TrimMargin? margin, TrimGeometry? geometry)
        {
            Revision input = RevisionOf(InputRef, OperationKind.RemoveBackground, null);
            Revision output = RevisionOf(OutputRef, OperationKind.Trim, input.Id);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            ProcessingAttempt attempt = ProcessingAttempt.Start(
                AttemptId.From(Guid.NewGuid()), _session, StepKind.Trim, input.Id, OperationKind.Trim,
                "internal-alpha-trim-v1", now) with
            {
                Status = AttemptStatus.Succeeded,
                EndedAtUtc = now,
                OutputRevisionId = output.Id,
                TrimParameters = margin,
                TrimGeometry = geometry,
            };

            return new Evidence(input, output, attempt);
        }

        /// <summary>The actual caller over recorded evidence, reading whatever files are on disk now.</summary>
        public RegressionAssertion Check(
            Evidence evidence,
            IReadOnlyList<ProcessingAttempt>? attempts = null,
            RegressionAssetManifest? manifest = null,
            FileFacts? trimmed = null) =>
            StandardRegressionSetWorkstationSmoke.FineHairTrimAssertion(
                manifest ?? Manifest("v3", strict: null, exact: true),
                evidence.Input.Facts, trimmed ?? evidence.Output.Facts,
                attempts ?? ImmutableArray.Create(evidence.Attempt),
                [evidence.Input, evidence.Output],
                _files.ResolveAbsolute);

        public RegressionAssertion Verify(
            TrimMargin? margin,
            TrimGeometry? geometry,
            bool withAttempt = true,
            RegressionAssetManifest? manifest = null) =>
            Check(Snapshot(margin, geometry), withAttempt ? null : Array.Empty<ProcessingAttempt>(), manifest);

        private Revision RevisionOf(WorkspaceFileRef file, OperationKind operation, RevisionId? source)
        {
            byte[] bytes = File.ReadAllBytes(_files.ResolveAbsolute(file));
            (int width, int height) = SyntheticImages.DecodeDimensions(bytes);
            return Revision.Create(
                RevisionId.From(Guid.NewGuid()), _session, source, operation, file,
                new FileFacts(ImageFormat.Png, bytes.Length, Sha256.FromBytes(SHA256.HashData(bytes)),
                    width, height, 96, 96, ColourMode.Rgb, true),
                DateTimeOffset.UtcNow);
        }

        public void Dispose() => _workspace.Dispose();
    }
}
