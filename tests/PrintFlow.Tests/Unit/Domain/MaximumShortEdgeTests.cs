using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;

namespace PrintFlow.Tests.Unit.Domain;

/// <summary>
/// The maximum-short-edge recommendation: what it limits, what it deliberately does not, and how
/// it differs from the two forms that already existed (Epic 11400 post-final A5 correction §4, §6,
/// §7, §8, §9, §10).
/// </summary>
/// <remarks>
/// The defect this file exists to prevent is a short-edge limit quietly implemented as something
/// that looks equivalent. Two near-misses are available and both are wrong:
/// <list type="bullet">
///   <item>a 135 × 135 <c>MaximumBox</c>, which caps the long edge as well and would print a
///         panoramic A5 job at 135 × 45 mm instead of 405 × 135 mm;</item>
///   <item>the old <c>MaximumLongEdge</c> of 135, which limits the opposite edge and would print
///         that same job at 135 × 45 mm too — by a different route, with the same result.</item>
/// </list>
/// Both would pass a test that only ever looked at a 4:3 source, so every case below is chosen so
/// that the three forms give visibly different answers.
/// </remarks>
public sealed class MaximumShortEdgeTests
{
    private static readonly RevisionId Revision = RevisionId.From(Guid.NewGuid());
    private static readonly Sha256 Bytes = Sha256.Parse(new string('c', 64));

    private static readonly PresetPrintRecommendation A5 =
        PresetPrintRecommendation.MaximumShortEdge(SizePreset.A5, 135m);

    // -------------------------------------------------------------------------------------
    // §4: the kind is a first-class configured form
    // -------------------------------------------------------------------------------------

    [Fact]
    public void A_short_edge_recommendation_reports_its_own_kind_and_value()
    {
        A5.Kind.ShouldBe(PresetRecommendationKind.MaximumShortEdge);
        A5.Preset.ShouldBe(SizePreset.A5);
        A5.MaxShortEdgeMm.ShouldBe(135m);
        A5.MaxLongEdgeMm.ShouldBeNull();
        A5.ToString().ShouldBe("A5 max short edge 135 mm");
    }

    /// <summary>
    /// It is not a box, and it refuses to pretend to be one (§4, §8).
    /// </summary>
    /// <remarks>
    /// <c>AsFitBounds</c> is the source-independent conversion the other two forms use. A short
    /// edge has no source-independent box — the bound goes on a different axis depending on which
    /// way round the artwork is — so it throws rather than returning the 135 × 135 square, which
    /// is precisely the encoding the correction forbids.
    /// </remarks>
    [Fact]
    public void A_short_edge_recommendation_is_not_expressible_as_a_fixed_box()
    {
        Should.Throw<InvalidOperationException>(() => A5.AsFitBounds());

        // The other two forms are unaffected.
        PresetPrintRecommendation.MaximumLongEdge(SizePreset.A4, 280m).AsFitBounds()
            .MaxWidthMm.ShouldBe(280d);
        PresetPrintRecommendation.MaximumBox(SizePreset.A3Landscape, 360m, 280m).AsFitBounds()
            .MaxHeightMm.ShouldBe(280d);
    }

    // -------------------------------------------------------------------------------------
    // §6, §7: which concrete edge, and never an enlargement
    // -------------------------------------------------------------------------------------

    /// <summary>A landscape source is limited on its Height (§7).</summary>
    [Fact]
    public void A_landscape_source_beyond_the_limit_is_limited_on_its_height()
    {
        // 6000 × 3000 px at 300 ppi is 508 × 254 mm; the short edge is the 254 mm height.
        FitWithinBoundsResult fit = FitWithinBounds.CalculateShortEdge(6000, 3000, 135d);

        fit.LimitingEdge.ShouldBe(LimitingEdge.Height);
        fit.LimitingValueMm.ShouldBe(135d);
        fit.ResizeMode.ShouldBe(PhotoshopResizeMode.BicubicSharper);
        fit.ProjectedHeightMm.ShouldBe(135d);
        fit.ProjectedWidthMm.ShouldBe(270d);
    }

    /// <summary>
    /// The projected pair follows Photoshop's one-edge integer rounding, rather than converting
    /// two ideal millimetre dimensions independently.
    /// </summary>
    [Fact]
    public void A_landscape_projection_matches_the_integer_pair_returned_by_Photoshop()
    {
        FitWithinBoundsResult fit = FitWithinBounds.CalculateShortEdge(2400, 1800, 135d);

        fit.LimitingEdge.ShouldBe(LimitingEdge.Height);
        fit.ProjectedPixelHeight.ShouldBe(1594);
        fit.ProjectedPixelWidth.ShouldBe(2125);

        // Independent conversion of the ideal 180 mm long edge is the off-by-one near miss.
        PrintDimensions.PixelsFromMillimetres(fit.ProjectedWidthMm).ShouldBe(2126);
    }

    /// <summary>A portrait source is limited on its Width — the opposite axis (§7).</summary>
    [Fact]
    public void A_portrait_source_beyond_the_limit_is_limited_on_its_width()
    {
        FitWithinBoundsResult fit = FitWithinBounds.CalculateShortEdge(3000, 6000, 135d);

        fit.LimitingEdge.ShouldBe(LimitingEdge.Width);
        fit.LimitingValueMm.ShouldBe(135d);
        fit.ProjectedWidthMm.ShouldBe(135d);
        fit.ProjectedHeightMm.ShouldBe(270d);
    }

    /// <summary>A square source ties deterministically to Width (§7).</summary>
    [Fact]
    public void A_square_source_ties_to_width()
    {
        FitWithinBoundsResult fit = FitWithinBounds.CalculateShortEdge(4000, 4000, 135d);

        fit.LimitingEdge.ShouldBe(LimitingEdge.Width);
        fit.LimitingValueMm.ShouldBe(135d);
        fit.ProjectedWidthMm.ShouldBe(135d);
        fit.ProjectedHeightMm.ShouldBe(135d);
    }

    /// <summary>
    /// A source already inside the limit is left exactly alone (§6, correction §24).
    /// </summary>
    /// <remarks>
    /// Ordinary preset use never enlarges. Stretching a small image up to the recommendation would
    /// be the software adding pixels the source does not hold, which needs an explicit authority
    /// nothing here grants — and would silently soften every small A5 job in the shop.
    /// </remarks>
    [Fact]
    public void A_source_already_within_the_limit_is_resolution_only()
    {
        // 2000 × 1000 px is 169.3 × 84.7 mm; the 84.7 mm short edge is well inside 135 mm.
        FitWithinBoundsResult fit = FitWithinBounds.CalculateShortEdge(2000, 1000, 135d);

        fit.LimitingEdge.ShouldBe(LimitingEdge.None);
        fit.LimitingValueMm.ShouldBeNull();
        fit.ResizeMode.ShouldBe(PhotoshopResizeMode.None);
        fit.ProjectedPixelWidth.ShouldBe(2000);
        fit.ProjectedPixelHeight.ShouldBe(1000);
    }

    // -------------------------------------------------------------------------------------
    // §8: no hidden long-edge cap
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// A panoramic source keeps its long edge, and that is the whole point (§8, §25).
    /// </summary>
    /// <remarks>
    /// The regression test that separates the correct implementation from both near-misses. A 3:1
    /// source under A5 prints at roughly 405 × 135 mm: the short edge is held at 135 mm and the
    /// long edge runs on. A 135 × 135 box and a 135 mm long edge would both have produced
    /// 135 × 45 mm, and nothing else in the suite tells those three apart.
    /// </remarks>
    [Fact]
    public void A_panoramic_source_keeps_a_long_edge_far_past_the_short_edge_limit()
    {
        // 6000 × 2000 px = 508 × 169.3 mm at 300 ppi.
        FitWithinBoundsResult shortEdge = FitWithinBounds.CalculateShortEdge(6000, 2000, 135d);

        shortEdge.LimitingEdge.ShouldBe(LimitingEdge.Height);
        shortEdge.ProjectedHeightMm.ShouldBe(135d);
        shortEdge.ProjectedWidthMm.ShouldBe(405d);
        shortEdge.ProjectedWidthMm.ShouldBeGreaterThan(135d);

        // The two forms it must not be confused with, over the same source.
        FitWithinBounds.CalculateLongEdge(6000, 2000, 135d).ProjectedWidthMm.ShouldBe(135d);
        FitWithinBounds.Calculate(6000, 2000, 135d, 135d).ProjectedWidthMm.ShouldBe(135d);
    }

    // -------------------------------------------------------------------------------------
    // §10: one authority answers whether a projection is covered
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// <c>Covers</c> asks each configured form its own question (§10).
    /// </summary>
    [Fact]
    public void Covers_measures_the_edge_the_configured_form_actually_constrains()
    {
        // 4800 × 1590 px = 406.4 × 134.6 mm. Long edge far past 135 mm, short edge just inside it.
        PresetPrintRecommendation longEdge =
            PresetPrintRecommendation.MaximumLongEdge(SizePreset.A5, 135m);
        PresetPrintRecommendation box =
            PresetPrintRecommendation.MaximumBox(SizePreset.A5, 135m, 135m);

        A5.Covers(4800, 1590).ShouldBeTrue();
        longEdge.Covers(4800, 1590).ShouldBeFalse();
        box.Covers(4800, 1590).ShouldBeFalse();

        // And the short edge really is measured: one pixel past 135 mm is not covered.
        A5.Covers(4800, 1595).ShouldBeFalse();
        A5.Covers(4800, 1594).ShouldBeTrue();
    }

    /// <summary>A box measures both of its own bounds independently (§10).</summary>
    [Fact]
    public void Covers_measures_both_bounds_of_a_box()
    {
        PresetPrintRecommendation a3 =
            PresetPrintRecommendation.MaximumBox(SizePreset.A3Landscape, 360m, 280m);

        // 360 mm = 4252 px, 280 mm = 3307 px at 300 ppi.
        a3.Covers(4252, 3307).ShouldBeTrue();
        a3.Covers(4253, 3307).ShouldBeFalse();
        a3.Covers(4252, 3308).ShouldBeFalse();
    }

    // -------------------------------------------------------------------------------------
    // §11: the recommendation is the single sizing authority
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// <c>Fit</c> dispatches every configured form to <c>FitWithinBounds</c> and adds nothing
    /// (§10, §11).
    /// </summary>
    [Fact]
    public void Fit_delegates_each_configured_form_to_the_one_sizing_authority()
    {
        A5.Fit(6000, 3000).ShouldBe(FitWithinBounds.CalculateShortEdge(6000, 3000, 135d));

        PresetPrintRecommendation.MaximumLongEdge(SizePreset.A4, 280m).Fit(6000, 3000)
            .ShouldBe(FitWithinBounds.CalculateLongEdge(6000, 3000, 280d));

        PresetPrintRecommendation.MaximumBox(SizePreset.A3Landscape, 360m, 280m).Fit(6000, 3000)
            .ShouldBe(FitWithinBounds.Calculate(6000, 3000, 360d, 280d));
    }

    /// <summary>
    /// The bounds a plan records reproduce the decision it recorded (§8).
    /// </summary>
    /// <remarks>
    /// The plan's stored millimetres are the box the fit was made within, and for a short edge
    /// that box is orientation-dependent: the limit on the short axis, the source's own size on
    /// the long one — a bound that cannot bind. Fitting the same source into it must give back the
    /// same answer, or a persisted plan's limits and its limiting edge would be two facts that
    /// could disagree.
    /// </remarks>
    [Theory]
    [InlineData(6000, 3000)]
    [InlineData(3000, 6000)]
    [InlineData(4000, 4000)]
    [InlineData(2000, 1000)]
    [InlineData(6000, 2000)]
    [InlineData(1000, 900)]
    public void The_recorded_bounds_reproduce_the_short_edge_decision(int widthPx, int heightPx)
    {
        PrintDimensions bounds = A5.AsFitBoundsFor(widthPx, heightPx);

        FitWithinBounds.Calculate(widthPx, heightPx, bounds).ShouldBe(A5.Fit(widthPx, heightPx));
    }

    /// <summary>
    /// A plan built from the recommendation carries the short-edge decision and honest bounds
    /// (§11).
    /// </summary>
    [Fact]
    public void A_plan_built_from_a_short_edge_recommendation_records_the_resolved_edge()
    {
        PrintPreparationPlan plan =
            PrintPreparationPlan.For(Revision, Bytes, 6000, 2000, A5);

        plan.LimitKind.ShouldBe(SizePreset.A5);
        plan.Mode.ShouldBe(PrintPreparationMode.ProportionalShrink);
        plan.LimitingEdge.ShouldBe(LimitingEdge.Height);
        plan.LimitingValueMm.ShouldBe(135d);
        plan.ResizePolicy.ShouldBe(PhotoshopResizeMode.BicubicSharper);
        plan.ProductionDpi.ShouldBe(300);

        // 405 × 135 mm, and the long edge is emphatically not clamped to 135 (§8).
        plan.ProjectedPixelWidth.ShouldBe(4782);
        plan.ProjectedPixelHeight.ShouldBe(1594);

        // The bounds it was calculated within: 135 mm on the short axis, the source's own 508 mm
        // on the long one, which never binds.
        plan.MaxHeightMm.ShouldBe(135d);
        plan.MaxWidthMm.ShouldBe(508d, 0.001d);
    }
}
