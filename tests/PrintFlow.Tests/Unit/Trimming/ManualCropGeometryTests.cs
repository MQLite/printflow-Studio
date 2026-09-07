using PrintFlow.Domain.Trimming;

namespace PrintFlow.Tests.Unit.Trimming;

public sealed class ManualCropGeometryTests
{
    [Fact]
    public void Tight_keeps_the_selection() =>
        ManualCropGeometry.Create(TrimBounds.FromEdges(10, 10, 20, 20), ManualCropMargin.Tight, 30, 30)
            .AppliedBounds.ShouldBe(TrimBounds.FromEdges(10, 10, 20, 20));

    [Fact]
    public void Uniform_expands_each_edge() =>
        ManualCropGeometry.Create(TrimBounds.FromEdges(10, 10, 20, 20), ManualCropMargin.Uniform(5), 30, 30)
            .AppliedBounds.ShouldBe(TrimBounds.FromEdges(5, 5, 25, 25));

    [Fact]
    public void Per_edge_preserves_independent_values() =>
        ManualCropGeometry.Create(TrimBounds.FromEdges(10, 10, 20, 20), ManualCropMargin.PerEdge(1, 4, 3, 2), 30, 30)
            .AppliedBounds.ShouldBe(TrimBounds.FromEdges(8, 9, 24, 23));

    [Fact]
    public void Huge_margins_clamp_without_overflow_and_preserve_requested_values()
    {
        var selected = TrimBounds.FromEdges(1, 2, 7, 8);
        var geometry = ManualCropGeometry.Create(selected, ManualCropMargin.Uniform(int.MaxValue), 12, 10);
        geometry.SelectedBounds.ShouldBe(selected);
        geometry.AppliedBounds.ShouldBe(TrimBounds.Canvas(12, 10));
        geometry.Margin.Top.ShouldBe(int.MaxValue);
    }

    [Fact]
    public void Invalid_base_is_refused_not_clamped()
    {
        Should.Throw<ArgumentException>(() => ManualCropGeometry.Create(default, ManualCropMargin.Uniform(5), 30, 30));
        Should.Throw<ArgumentException>(() => ManualCropGeometry.Create(TrimBounds.FromEdges(10, 10, 40, 20), ManualCropMargin.Uniform(5), 30, 30));
        Should.Throw<ArgumentOutOfRangeException>(() => TrimBounds.FromEdges(-1, 0, 5, 5));
    }

    [Theory]
    [InlineData(-1, 0, 0, 0)]
    [InlineData(0, -1, 0, 0)]
    [InlineData(0, 0, -1, 0)]
    [InlineData(0, 0, 0, -1)]
    public void Negative_margins_are_refused(int top, int right, int bottom, int left) =>
        Should.Throw<ArgumentOutOfRangeException>(() => ManualCropMargin.PerEdge(top, right, bottom, left));

    [Fact]
    public void Negative_uniform_is_refused() => Should.Throw<ArgumentOutOfRangeException>(() => ManualCropMargin.Uniform(-1));
}
