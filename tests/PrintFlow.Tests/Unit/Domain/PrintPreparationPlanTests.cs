using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;

namespace PrintFlow.Tests.Unit.Domain;

/// <summary>
/// The source-bound preparation plan: one <c>FitWithinBounds</c> result tied to the exact
/// artefact it was calculated from (Epic 11400 Part B1A.2A §5, §6, §7, §22).
/// </summary>
/// <remarks>
/// Two families of assertion live here, and they are different claims. The first is that the plan
/// carries the calculator's answer unchanged — that a limiting edge is never re-decided on the way
/// into the record. The second is that a plan whose parts contradict each other cannot be
/// constructed at all, which is what makes the database mapper's fail-closed rule enforceable
/// rather than merely intended (§13).
/// </remarks>
public sealed class PrintPreparationPlanTests
{
    private static readonly RevisionId Source =
        RevisionId.From(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

    private static readonly RevisionId Other =
        RevisionId.From(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));

    private static readonly Sha256 Bytes = Sha256.Parse(new string('a', 64));

    private static readonly Sha256 OtherBytes = Sha256.Parse(new string('b', 64));

    // -------------------------------------------------------------------------------------
    // §22.1–§22.6: the calculator's answer, carried unchanged
    // -------------------------------------------------------------------------------------

    /// <summary>A source wider than the box is limited by its width (§22.1).</summary>
    [Fact]
    public void A_wide_source_is_limited_by_width()
    {
        PrintPreparationPlan plan = Plan(6000, 3000, 280, 400);

        plan.LimitingEdge.ShouldBe(LimitingEdge.Width);
        plan.LimitingValueMm.ShouldBe(280);
        plan.Mode.ShouldBe(PrintPreparationMode.ProportionalShrink);
    }

    /// <summary>A source taller than the box is limited by its height (§22.2).</summary>
    [Fact]
    public void A_tall_source_is_limited_by_height()
    {
        PrintPreparationPlan plan = Plan(3000, 6000, 280, 400);

        plan.LimitingEdge.ShouldBe(LimitingEdge.Height);
        plan.LimitingValueMm.ShouldBe(400);
    }

    /// <summary>
    /// A source with the box's own ratio picks the accepted deterministic edge (§22.3).
    /// </summary>
    /// <remarks>
    /// 7200×5600 px against 360×280 mm is exactly the box's ratio, so both edges reach their
    /// limit together. The contract fixes the tie to Width so two runs of the same job cannot
    /// send Photoshop different instructions; the plan must carry that answer rather than
    /// re-deriving one.
    /// </remarks>
    [Fact]
    public void An_exact_ratio_match_picks_the_accepted_deterministic_edge()
    {
        Plan(7200, 5600, 360, 280).LimitingEdge.ShouldBe(LimitingEdge.Width);
    }

    /// <summary>Already inside the box means resolution only, and no edge at all (§22.4).</summary>
    [Fact]
    public void A_source_already_within_bounds_is_resolution_only()
    {
        PrintPreparationPlan plan = Plan(1000, 750, 280, 400);

        plan.Mode.ShouldBe(PrintPreparationMode.ResolutionOnly);
        plan.LimitingEdge.ShouldBe(LimitingEdge.None);
        plan.LimitingValueMm.ShouldBeNull();
        plan.ResizePolicy.ShouldBe(PhotoshopResizeMode.None);
        plan.RequiresShrink.ShouldBeFalse();

        // Nothing is resampled, so the projection is the source's own pixels exactly — not a
        // millimetre round-trip that happens to land nearby.
        plan.ProjectedPixelWidth.ShouldBe(1000);
        plan.ProjectedPixelHeight.ShouldBe(750);
    }

    /// <summary>A shrink is always BicubicSharper, and never anything else (§22.5).</summary>
    [Fact]
    public void A_shrink_uses_the_fixed_resampling_policy()
    {
        PrintPreparationPlan plan = Plan(6000, 3000, 280, 400);

        plan.RequiresShrink.ShouldBeTrue();
        plan.ResizePolicy.ShouldBe(PhotoshopResizeMode.BicubicSharper);
        plan.ProductionDpi.ShouldBe(300);
    }

    /// <summary>
    /// No plan ever projects more pixels than it started with (§22.6).
    /// </summary>
    /// <remarks>
    /// The no-enlargement rule from the far side: a box larger than the source in <i>both</i>
    /// directions is exactly the situation in which an "fit to the box" implementation would
    /// upscale. The plan reports resolution-only and keeps the source's pixels.
    /// </remarks>
    [Theory]
    [InlineData(1000, 750, 280d, 400d)]
    [InlineData(400, 400, 420d, 297d)]
    [InlineData(6000, 3000, 280d, 400d)]
    [InlineData(3000, 6000, 280d, 400d)]
    [InlineData(7200, 5600, 360d, 280d)]
    public void No_plan_projects_more_pixels_than_its_source(
        int sourceWidth, int sourceHeight, double maxWidthMm, double maxHeightMm)
    {
        PrintPreparationPlan plan = Plan(sourceWidth, sourceHeight, maxWidthMm, maxHeightMm);

        plan.ProjectedPixelWidth.ShouldBeLessThanOrEqualTo(sourceWidth);
        plan.ProjectedPixelHeight.ShouldBeLessThanOrEqualTo(sourceHeight);
    }

    // -------------------------------------------------------------------------------------
    // §22.7–§22.9: the binding
    // -------------------------------------------------------------------------------------

    /// <summary>The plan records both halves of the artefact identity (§22.7).</summary>
    [Fact]
    public void A_plan_binds_the_revision_and_the_hash_it_was_calculated_from()
    {
        PrintPreparationPlan plan = Plan(6000, 3000, 280, 400);

        plan.SourceRevisionId.ShouldBe(Source);
        plan.SourceSha256.ShouldBe(Bytes);
        plan.SourcePixelWidth.ShouldBe(6000);
        plan.SourcePixelHeight.ShouldBe(3000);
        plan.Covers(Source, Bytes).ShouldBeTrue();
    }

    /// <summary>A different Revision is not covered, whatever its bytes (§22.8).</summary>
    [Fact]
    public void A_plan_does_not_cover_a_different_revision()
    {
        Plan(6000, 3000, 280, 400).Covers(Other, Bytes).ShouldBeFalse();
    }

    /// <summary>
    /// The same Revision with different bytes is not covered either (§22.9).
    /// </summary>
    /// <remarks>
    /// The case an id-only binding would pass: a file replaced in place keeps its Revision id and
    /// changes what a limiting edge would have to be calculated against.
    /// </remarks>
    [Fact]
    public void A_plan_does_not_cover_mutated_bytes_under_the_same_revision()
    {
        Plan(6000, 3000, 280, 400).Covers(Source, OtherBytes).ShouldBeFalse();
    }

    // -------------------------------------------------------------------------------------
    // §13: a contradictory stored plan cannot be rebuilt
    // -------------------------------------------------------------------------------------

    /// <summary>A round trip through <c>Rehydrate</c> returns the same plan.</summary>
    [Fact]
    public void A_valid_plan_rehydrates_to_an_equal_plan()
    {
        PrintPreparationPlan plan = Plan(6000, 3000, 280, 400);

        PrintPreparationPlan restored = PrintPreparationPlan.Rehydrate(
            plan.SourceRevisionId, plan.SourceSha256, plan.SourcePixelWidth, plan.SourcePixelHeight,
            plan.MaxWidthMm, plan.MaxHeightMm, plan.LimitKind, plan.Mode, plan.LimitingEdge,
            plan.LimitingValueMm, plan.ProjectedPixelWidth, plan.ProjectedPixelHeight,
            plan.ResizePolicy);

        restored.ShouldBe(plan);
    }

    /// <summary>
    /// A shrink with no edge, an edge with no value, an upscale, or a mismatched policy are all
    /// refused rather than interpreted.
    /// </summary>
    /// <remarks>
    /// The mode, the edge, the limiting value and the resampling policy are four expressions of
    /// one decision. Any combination in which they disagree is a record no reader could honestly
    /// interpret, so the type refuses to be one — which is what the SQLite mapper leans on when it
    /// rebuilds a row it did not write.
    /// </remarks>
    [Fact]
    public void A_shrink_without_a_limiting_edge_is_refused() =>
        Should.Throw<ArgumentException>(() => Rehydrate(
            mode: PrintPreparationMode.ProportionalShrink,
            edge: LimitingEdge.None,
            limitingValueMm: 280,
            policy: PhotoshopResizeMode.BicubicSharper));

    [Fact]
    public void A_shrink_without_a_limiting_value_is_refused() =>
        Should.Throw<ArgumentException>(() => Rehydrate(
            mode: PrintPreparationMode.ProportionalShrink,
            edge: LimitingEdge.Width,
            limitingValueMm: null,
            policy: PhotoshopResizeMode.BicubicSharper));

    [Fact]
    public void A_resolution_only_plan_carrying_a_limiting_value_is_refused() =>
        Should.Throw<ArgumentException>(() => Rehydrate(
            mode: PrintPreparationMode.ResolutionOnly,
            edge: LimitingEdge.None,
            limitingValueMm: 280,
            policy: PhotoshopResizeMode.None));

    [Fact]
    public void A_resolution_only_plan_that_claims_to_resample_is_refused() =>
        Should.Throw<ArgumentException>(() => Rehydrate(
            mode: PrintPreparationMode.ResolutionOnly,
            edge: LimitingEdge.None,
            limitingValueMm: null,
            policy: PhotoshopResizeMode.BicubicSharper));

    [Fact]
    public void A_legacy_fit_plan_cannot_gain_the_new_enlargement_policy() =>
        Should.Throw<ArgumentException>(() => Rehydrate(
            mode: PrintPreparationMode.ResolutionOnly,
            edge: LimitingEdge.None,
            limitingValueMm: null,
            policy: PhotoshopResizeMode.PreserveDetails));

    /// <summary>An enlargement cannot be expressed, however it is assembled (§22.6).</summary>
    [Fact]
    public void A_plan_projecting_more_pixels_than_its_source_is_refused() =>
        Should.Throw<ArgumentException>(() => Rehydrate(
            mode: PrintPreparationMode.ProportionalShrink,
            edge: LimitingEdge.Width,
            limitingValueMm: 280,
            policy: PhotoshopResizeMode.BicubicSharper,
            projectedPixelWidth: 9000));

    [Theory]
    [InlineData(0, 100)]
    [InlineData(-1, 100)]
    [InlineData(100, 0)]
    public void A_plan_without_positive_source_pixels_is_refused(int sourceWidth, int sourceHeight) =>
        Should.Throw<ArgumentOutOfRangeException>(() => PrintPreparationPlan.Rehydrate(
            Source, Bytes, sourceWidth, sourceHeight, 280, 400, SizePreset.Custom,
            PrintPreparationMode.ResolutionOnly, LimitingEdge.None, null, 10, 10,
            PhotoshopResizeMode.None));

    private static PrintPreparationPlan Plan(
        int sourceWidth, int sourceHeight, double maxWidthMm, double maxHeightMm) =>
        PrintPreparationPlan.For(
            Source, Bytes, sourceWidth, sourceHeight,
            PrintDimensions.FromMillimetres(maxWidthMm, maxHeightMm, SizePreset.Custom));

    private static PrintPreparationPlan Rehydrate(
        PrintPreparationMode mode,
        LimitingEdge edge,
        double? limitingValueMm,
        PhotoshopResizeMode policy,
        int projectedPixelWidth = 3000,
        int projectedPixelHeight = 1500) =>
        PrintPreparationPlan.Rehydrate(
            Source, Bytes, 6000, 3000, 280, 400, SizePreset.Custom,
            mode, edge, limitingValueMm, projectedPixelWidth, projectedPixelHeight, policy);
}
