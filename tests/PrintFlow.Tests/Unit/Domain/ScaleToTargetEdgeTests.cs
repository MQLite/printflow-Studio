using PrintFlow.Domain.Outputs;

namespace PrintFlow.Tests.Unit.Domain;

public sealed class ScaleToTargetEdgeTests
{
    [Fact]
    public void Custom_width_is_the_only_authoritative_edge()
    {
        ScaleToTargetEdgeResult result = ScaleToTargetEdge.Calculate(6000, 4000, TargetEdge.Width, 254m);

        result.SelectedTargetEdge.ShouldBe(TargetEdge.Width);
        result.PhotoshopTargetEdge.ShouldBe(LimitingEdge.Width);
        result.ProjectedPixelWidth.ShouldBe(3000);
        result.ProjectedPixelHeight.ShouldBe(2000);
        AssertDirection(result, ResizeDirection.Shrink, PhotoshopResizeMode.BicubicSharper, 1, 2);
    }

    [Fact]
    public void Custom_height_is_the_only_authoritative_edge()
    {
        ScaleToTargetEdgeResult result = ScaleToTargetEdge.Calculate(4000, 6000, TargetEdge.Height, 254m);

        result.PhotoshopTargetEdge.ShouldBe(LimitingEdge.Height);
        result.ProjectedPixelWidth.ShouldBe(2000);
        result.ProjectedPixelHeight.ShouldBe(3000);
    }

    [Fact]
    public void Long_edge_uses_width_for_landscape()
    {
        ScaleToTargetEdgeResult result = ScaleToTargetEdge.Calculate(6000, 4000, TargetEdge.LongEdge, 254m);

        result.SelectedTargetEdge.ShouldBe(TargetEdge.LongEdge);
        result.PhotoshopTargetEdge.ShouldBe(LimitingEdge.Width);
        result.ProjectedPixelWidth.ShouldBe(3000);
        result.ProjectedPixelHeight.ShouldBe(2000);
    }

    [Fact]
    public void Long_edge_uses_height_for_portrait()
    {
        ScaleToTargetEdgeResult result = ScaleToTargetEdge.Calculate(4000, 6000, TargetEdge.LongEdge, 254m);

        result.PhotoshopTargetEdge.ShouldBe(LimitingEdge.Height);
        result.ProjectedPixelWidth.ShouldBe(2000);
        result.ProjectedPixelHeight.ShouldBe(3000);
    }

    [Fact]
    public void Long_edge_square_tie_is_width()
    {
        ScaleToTargetEdgeResult result = ScaleToTargetEdge.Calculate(3600, 3600, TargetEdge.LongEdge, 152.4m);

        result.PhotoshopTargetEdge.ShouldBe(LimitingEdge.Width);
        result.ProjectedPixelWidth.ShouldBe(1800);
        result.ProjectedPixelHeight.ShouldBe(1800);
    }

    [Fact]
    public void Exact_decimal_and_rational_midpoints_round_away_from_zero()
    {
        ScaleToTargetEdgeResult result = ScaleToTargetEdge.Calculate(2000, 1000, TargetEdge.Width, 84.709m);

        result.ProjectedPixelWidth.ShouldBe(1001);
        result.ProjectedPixelHeight.ShouldBe(501);
        result.ProjectedScale.ShouldBe(ResizeScale.FromPixels(1001, 2000));
    }

    [Fact]
    public void Equal_authoritative_pixels_are_resolution_only_and_select_none()
    {
        ScaleToTargetEdgeResult result = ScaleToTargetEdge.Calculate(3000, 2000, TargetEdge.Width, 254m);

        result.ProjectedPixelWidth.ShouldBe(3000);
        result.ProjectedPixelHeight.ShouldBe(2000);
        AssertDirection(result, ResizeDirection.ResolutionOnly, PhotoshopResizeMode.None, 1, 1);
    }

    [Fact]
    public void Smaller_authoritative_pixels_are_shrink_and_select_bicubic_sharper()
    {
        ScaleToTargetEdgeResult result = ScaleToTargetEdge.Calculate(3001, 2000, TargetEdge.Width, 254m);

        AssertDirection(result, ResizeDirection.Shrink, PhotoshopResizeMode.BicubicSharper, 3000, 3001);
    }

    [Fact]
    public void Larger_authoritative_pixels_are_enlarge_and_select_verified_preserve_details()
    {
        ScaleToTargetEdgeResult result = ScaleToTargetEdge.Calculate(2999, 2000, TargetEdge.Width, 254m);

        result.ProjectedPixelWidth.ShouldBe(3000);
        result.ProjectedPixelHeight.ShouldBe(2001);
        result.SourceCapacityExceeded.ShouldBeTrue();
        AssertDirection(result, ResizeDirection.Enlarge, PhotoshopResizeMode.PreserveDetails, 3000, 2999);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(-1, 100)]
    [InlineData(100, 0)]
    public void Source_pixels_must_be_positive(int width, int height) =>
        Should.Throw<ArgumentOutOfRangeException>(() =>
            ScaleToTargetEdge.Calculate(width, height, TargetEdge.Width, 100m));

    [Fact]
    public void Requested_millimetres_must_be_positive() =>
        Should.Throw<ArgumentOutOfRangeException>(() =>
            ScaleToTargetEdge.Calculate(100, 100, TargetEdge.Width, 0m));

    private static void AssertDirection(
        ScaleToTargetEdgeResult result,
        ResizeDirection direction,
        PhotoshopResizeMode policy,
        int scaleNumerator,
        int scaleDenominator)
    {
        result.Direction.ShouldBe(direction);
        result.ResizePolicy.ShouldBe(policy);
        result.ProjectedScale.ShouldBe(ResizeScale.FromPixels(scaleNumerator, scaleDenominator));
        result.ResolutionPpi.ShouldBe(300);
    }
}
