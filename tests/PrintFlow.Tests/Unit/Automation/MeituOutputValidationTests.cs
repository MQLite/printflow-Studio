using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Adapters.Meitu;

namespace PrintFlow.Tests.Unit.Automation;

// `PrintFlow.Tests.Unit` is a namespace in this assembly and wins over a compilation-unit
// alias, so the result type is aliased inside the namespace instead.
using Unit = PrintFlow.Domain.Results.Unit;

/// <summary>
/// What a file has to do before PrintFlow will call it an Enhancement result: appear, settle,
/// read, and measure up (Epic 11300 Part B2B §14–§19).
/// </summary>
public sealed class MeituOutputValidationTests
{
    private static MeituOutputObservation Seen(long length, bool readable = true) =>
        new(Exists: true, ByteLength: length, CanOpenForRead: readable);

    private static readonly MeituOutputObservation Absent = new(false, 0, false);

    // -----------------------------------------------------------------------------
    // Stability (§15)
    // -----------------------------------------------------------------------------

    [Fact]
    public void Three_consecutive_equal_readable_observations_are_settled()
    {
        MeituOutputStabilityRule.IsSettled([Seen(100), Seen(100), Seen(100)]).ShouldBeTrue();
    }

    [Fact]
    public void Growth_up_to_the_last_moment_is_not_settled()
    {
        MeituOutputStabilityRule.IsSettled([Seen(100), Seen(100), Seen(200)]).ShouldBeFalse();
    }

    /// <summary>
    /// A file that stops growing but is still held open is not settled.
    /// </summary>
    /// <remarks>
    /// The case a size-only rule gets wrong. A writer flushing in chunks pauses at a stable
    /// length routinely, and hashing it then would produce a digest of a partial file that is
    /// nonetheless perfectly reproducible — the worst kind of wrong answer.
    /// </remarks>
    [Fact]
    public void A_stable_size_that_cannot_be_opened_is_not_settled()
    {
        MeituOutputStabilityRule.IsSettled(
            [Seen(100), Seen(100), Seen(100, readable: false)]).ShouldBeFalse();
    }

    [Fact]
    public void A_zero_byte_file_is_never_settled_however_stable()
    {
        MeituOutputStabilityRule.IsSettled([Seen(0), Seen(0), Seen(0), Seen(0)]).ShouldBeFalse();
    }

    [Fact]
    public void A_file_that_has_not_appeared_is_not_settled()
    {
        MeituOutputStabilityRule.IsSettled([Absent, Absent, Absent]).ShouldBeFalse();
    }

    /// <summary>Fewer observations than the rule asks for is "not yet", never "good enough".</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Fewer_observations_than_required_are_not_settled(int count)
    {
        MeituOutputObservation[] observations = [.. Enumerable.Repeat(Seen(100), count)];
        MeituOutputStabilityRule.IsSettled(observations).ShouldBeFalse();
    }

    /// <summary>
    /// Only the most recent observations count: a file that grew and then settled is settled.
    /// </summary>
    [Fact]
    public void Earlier_growth_does_not_prevent_a_later_settle()
    {
        MeituOutputStabilityRule.IsSettled(
            [Absent, Seen(10), Seen(500), Seen(900), Seen(900), Seen(900)]).ShouldBeTrue();
    }

    /// <summary>A file that briefly vanished inside the window is not settled.</summary>
    [Fact]
    public void A_gap_inside_the_stability_window_is_not_settled()
    {
        MeituOutputStabilityRule.IsSettled([Seen(100), Absent, Seen(100)]).ShouldBeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_non_positive_requirement_settles_nothing(int required)
    {
        MeituOutputStabilityRule.IsSettled([Seen(100), Seen(100), Seen(100)], required).ShouldBeFalse();
    }

    /// <summary>The timeout message names which part of the rule was missing.</summary>
    [Fact]
    public void The_unsettled_description_names_the_part_that_failed()
    {
        MeituOutputStabilityRule.DescribeUnsettled([]).ShouldContain("never observed");
        MeituOutputStabilityRule.DescribeUnsettled([Absent]).ShouldContain("no file");
        MeituOutputStabilityRule.DescribeUnsettled([Seen(0)]).ShouldContain("empty");
        MeituOutputStabilityRule.DescribeUnsettled([Seen(5, readable: false)]).ShouldContain("holds it");
        MeituOutputStabilityRule.DescribeUnsettled([Seen(5), Seen(9)]).ShouldContain("still changing");
    }

    // -----------------------------------------------------------------------------
    // The Enhancement output contract (§16, §17)
    // -----------------------------------------------------------------------------

    private static FileFacts Facts(
        int width = 320, int height = 240, ImageFormat format = ImageFormat.Png,
        long bytes = 4096, string digest = "AA") =>
        new(format, bytes, Sha256.Parse(digest.PadLeft(64, '0')), width, height, 300, 300,
            ColourMode.Rgb, HasAlpha: true);

    [Fact]
    public void An_upscaled_png_is_accepted()
    {
        MeituEnhancementOutputRule.Validate(
            Facts(320, 240), Facts(1280, 960, bytes: 1_159_183), ImageFormat.Png)
            .IsSuccess.ShouldBeTrue();
    }

    /// <summary>
    /// An unchanged size is accepted, because Meitu's own panel offers it.
    /// </summary>
    /// <remarks>
    /// The reason the rule is "not smaller" rather than a multiplier. The module carries a
    /// 保持原尺寸 (keep original size) option, so a same-size result is a legitimate outcome of
    /// the very feature being automated, and a 4x rule would reject it.
    /// </remarks>
    [Fact]
    public void A_result_at_the_source_dimensions_is_accepted()
    {
        MeituEnhancementOutputRule.Validate(Facts(320, 240), Facts(320, 240), ImageFormat.Png)
            .IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void An_empty_result_is_refused()
    {
        OperationResult<Unit> result = MeituEnhancementOutputRule.Validate(
            Facts(), Facts(1280, 960, bytes: 0), ImageFormat.Png);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.OutputUnreadable);
    }

    /// <summary>
    /// A file whose bytes are not the required format is refused, whatever it is called.
    /// </summary>
    [Theory]
    [InlineData(ImageFormat.Jpeg)]
    [InlineData(ImageFormat.Tiff)]
    [InlineData(ImageFormat.Unknown)]
    public void A_result_whose_content_is_not_the_required_format_is_refused(ImageFormat actual)
    {
        OperationResult<Unit> result = MeituEnhancementOutputRule.Validate(
            Facts(), Facts(1280, 960, format: actual), ImageFormat.Png);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Context["actualFormat"].ShouldBe(actual.ToString());
    }

    [Theory]
    [InlineData(160, 960)]
    [InlineData(1280, 120)]
    [InlineData(160, 120)]
    public void A_result_smaller_than_its_source_in_either_direction_is_refused(int width, int height)
    {
        OperationResult<Unit> result = MeituEnhancementOutputRule.Validate(
            Facts(320, 240), Facts(width, height), ImageFormat.Png);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Context["sourcePixels"].ShouldBe("320x240");
    }

    [Fact]
    public void A_result_whose_dimensions_could_not_be_read_is_refused()
    {
        FileFacts unreadable = new(
            ImageFormat.Png, 4096, Sha256.Parse(new string('0', 64)), null, null, null, null,
            ColourMode.Unknown, null);

        MeituEnhancementOutputRule.Validate(Facts(), unreadable, ImageFormat.Png)
            .IsFailure.ShouldBeTrue();
    }

    /// <summary>
    /// An unmeasurable source refuses rather than waives the comparison.
    /// </summary>
    /// <remarks>
    /// "The source could not be measured, so anything the output says is acceptable" is how a
    /// shrunk result gets through. The rule fails closed in the direction that produces no
    /// Revision.
    /// </remarks>
    [Fact]
    public void A_source_whose_dimensions_could_not_be_read_refuses_the_comparison()
    {
        FileFacts unmeasurable = new(
            ImageFormat.Png, 4096, Sha256.Parse(new string('0', 64)), null, null, null, null,
            ColourMode.Unknown, null);

        MeituEnhancementOutputRule.Validate(unmeasurable, Facts(1280, 960), ImageFormat.Png)
            .IsFailure.ShouldBeTrue();
    }

    // -----------------------------------------------------------------------------
    // Source safety (§19)
    // -----------------------------------------------------------------------------

    [Fact]
    public void An_unchanged_working_copy_passes()
    {
        FileFacts before = Facts(digest: "AB");
        MeituEnhancementOutputRule.ConfirmSourceUnchanged(before, before, "a.png")
            .IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_working_copy_whose_bytes_changed_refuses_the_run()
    {
        OperationResult<Unit> result = MeituEnhancementOutputRule.ConfirmSourceUnchanged(
            Facts(digest: "AB"), Facts(digest: "CD"), "a.png");

        result.IsFailure.ShouldBeTrue();
        result.Failure.TechnicalDetail.ShouldContain("a.png");
    }

    /// <summary>
    /// A same-length file with different bytes is still a changed file.
    /// </summary>
    /// <remarks>
    /// Length is checked as well as the digest, but the digest is what does the work: an
    /// in-place re-save at the same size is exactly what a 覆盖原图 export would produce.
    /// </remarks>
    [Fact]
    public void A_same_length_working_copy_with_different_bytes_is_still_changed()
    {
        MeituEnhancementOutputRule.ConfirmSourceUnchanged(
            Facts(bytes: 4096, digest: "AB"), Facts(bytes: 4096, digest: "CD"), "a.png")
            .IsFailure.ShouldBeTrue();
    }
}
