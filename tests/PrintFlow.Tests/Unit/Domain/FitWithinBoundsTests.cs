using PrintFlow.Domain.Outputs;

namespace PrintFlow.Tests.Unit.Domain;

public sealed class FitWithinBoundsTests
{
    [Fact]
    public void Wide_source_inside_a_portrait_box_is_width_limiting()
    {
        FitWithinBoundsResult result = FitWithinBounds.Calculate(6000, 3000, 280, 400);

        AssertShrink(result, LimitingEdge.Width, 280);
        result.ProjectedWidthMm.ShouldBe(280);
        result.ProjectedHeightMm.ShouldBe(140);
        result.ProjectedHeightMm.ShouldBeLessThanOrEqualTo(400);
    }

    [Fact]
    public void Tall_source_inside_a_portrait_box_is_height_limiting()
    {
        FitWithinBoundsResult result = FitWithinBounds.Calculate(3000, 6000, 280, 400);

        AssertShrink(result, LimitingEdge.Height, 400);
        result.ProjectedWidthMm.ShouldBe(200);
        result.ProjectedHeightMm.ShouldBe(400);
        result.ProjectedWidthMm.ShouldBeLessThanOrEqualTo(280);
    }

    [Fact]
    public void Same_source_and_box_ratio_selects_width()
    {
        FitWithinBoundsResult result = FitWithinBounds.Calculate(7200, 5600, 360, 280);

        AssertShrink(result, LimitingEdge.Width, 360);
        result.ProjectedHeightMm.ShouldBe(280);
    }

    [Fact]
    public void Landscape_A3_bounds_fit_both_edges()
    {
        FitWithinBoundsResult result = FitWithinBounds.Calculate(7200, 5600, 360, 280);

        AssertShrink(result, LimitingEdge.Width, 360);
        result.ProjectedWidthMm.ShouldBeLessThanOrEqualTo(360);
        result.ProjectedHeightMm.ShouldBeLessThanOrEqualTo(280);
    }

    [Fact]
    public void Portrait_A3_bounds_fit_both_edges()
    {
        FitWithinBoundsResult result = FitWithinBounds.Calculate(3500, 6000, 280, 400);

        AssertShrink(result, LimitingEdge.Height, 400);
        result.ProjectedWidthMm.ShouldBe(233.33333333333334, tolerance: 0.000000001);
        result.ProjectedWidthMm.ShouldBeLessThanOrEqualTo(280);
    }

    [Fact]
    public void A4_long_edge_limit_uses_the_source_width_for_landscape()
    {
        FitWithinBoundsResult result = FitWithinBounds.CalculateLongEdge(4000, 2000, 280);

        AssertShrink(result, LimitingEdge.Width, 280);
        result.ProjectedHeightMm.ShouldBe(140);
    }

    [Fact]
    public void A5_long_edge_limit_uses_the_source_height_for_portrait()
    {
        FitWithinBoundsResult result = FitWithinBounds.CalculateLongEdge(2000, 4000, 135);

        AssertShrink(result, LimitingEdge.Height, 135);
        result.ProjectedWidthMm.ShouldBe(67.5);
    }

    [Fact]
    public void Image_already_within_limits_preserves_pixels_and_uses_NONE()
    {
        FitWithinBoundsResult result = FitWithinBounds.Calculate(1000, 750, 280, 400);

        result.RequiresShrink.ShouldBeFalse();
        result.ResizeMode.ShouldBe(PhotoshopResizeMode.None);
        result.LimitingEdge.ShouldBe(LimitingEdge.None);
        result.LimitingValueMm.ShouldBeNull();
        result.ProjectedWidthMm.ShouldBe(result.SourceWidthMm);
        result.ProjectedHeightMm.ShouldBe(result.SourceHeightMm);
        result.ResolutionPpi.ShouldBe(300);
    }

    [Fact]
    public void Source_exceeding_one_edge_shrinks_proportionally()
    {
        FitWithinBoundsResult result = FitWithinBounds.Calculate(4000, 1000, 280, 400);

        AssertShrink(result, LimitingEdge.Width, 280);
        result.ProjectedHeightMm.ShouldBe(70);
    }

    [Fact]
    public void Source_exceeding_both_edges_selects_the_correct_limit()
    {
        FitWithinBoundsResult result = FitWithinBounds.Calculate(6000, 5000, 360, 280);

        AssertShrink(result, LimitingEdge.Height, 280);
        result.ProjectedWidthMm.ShouldBe(336);
        result.ProjectedWidthMm.ShouldBeLessThanOrEqualTo(360);
    }

    [Fact]
    public void A_box_that_could_enlarge_the_source_leaves_it_smaller()
    {
        FitWithinBoundsResult result = FitWithinBounds.Calculate(1000, 500, 280, 400);

        result.ResizeMode.ShouldBe(PhotoshopResizeMode.None);
        result.ProjectedWidthMm.ShouldBe(84.66666666666667, tolerance: 0.000000001);
        result.ProjectedHeightMm.ShouldBe(42.333333333333336, tolerance: 0.000000001);
    }

    [Fact]
    public void PrintDimensions_are_consumed_as_maximum_bounds_not_as_a_target_pixel_pair()
    {
        PrintDimensions limits = PrintDimensions.FromMillimetres(280, 400, SizePreset.A3Portrait);

        limits.MaxWidthMm.ShouldBe(280);
        limits.MaxHeightMm.ShouldBe(400);

        FitWithinBoundsResult result = FitWithinBounds.Calculate(3000, 6000, limits);
        AssertShrink(result, LimitingEdge.Height, 400);
        result.ProjectedWidthMm.ShouldBe(200);
    }

    private static void AssertShrink(
        FitWithinBoundsResult result,
        LimitingEdge expectedEdge,
        double expectedValueMm)
    {
        result.RequiresShrink.ShouldBeTrue();
        result.ResizeMode.ShouldBe(PhotoshopResizeMode.BicubicSharper);
        result.LimitingEdge.ShouldBe(expectedEdge);
        result.LimitingValueMm.ShouldBe(expectedValueMm);
        result.ResolutionPpi.ShouldBe(PrintDimensions.ProductionDpi);
    }
}
