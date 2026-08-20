using PrintFlow.Domain.Trimming;

namespace PrintFlow.Tests.Unit.Trimming;

/// <summary>
/// Margins expand the crop and are clamped to the canvas — never the other way round
/// (Epic 11200 Part B §21).
/// </summary>
/// <remarks>
/// Two properties matter more than the arithmetic. First, a margin can never produce a
/// coordinate outside the source image, because the crop that follows would then either throw
/// or read garbage. Second, a negative margin is refused rather than quietly meaning "crop
/// further in": trimming into the artwork is destructive and must be asked for in words, not
/// in a minus sign.
/// </remarks>
public sealed class TrimMarginTests
{
    private static readonly TrimBounds Content = TrimBounds.FromEdges(5, 6, 20, 22);

    // -----------------------------------------------------------------------------
    // Modes
    // -----------------------------------------------------------------------------

    [Fact]
    public void A_tight_crop_leaves_the_content_bounds_exactly_as_they_are()
    {
        Content.Expand(TrimMargin.Tight, 100, 100).ShouldBe(Content);
    }

    [Fact]
    public void The_default_margin_is_a_tight_crop()
    {
        TrimMargin margin = default;

        margin.Mode.ShouldBe(TrimMode.TightCrop);
        margin.IsNone.ShouldBeTrue();
        margin.ShouldBe(TrimMargin.Tight);
    }

    [Fact]
    public void A_uniform_margin_grows_all_four_edges_equally()
    {
        TrimBounds expanded = Content.Expand(TrimMargin.Uniform(3), 100, 100);

        expanded.ShouldBe(TrimBounds.FromEdges(2, 3, 23, 25));
    }

    [Fact]
    public void An_edge_specific_margin_grows_each_edge_by_its_own_amount()
    {
        TrimBounds expanded = Content.Expand(TrimMargin.PerEdge(top: 1, right: 2, bottom: 3, left: 4), 100, 100);

        expanded.ShouldBe(TrimBounds.FromEdges(1, 5, 22, 25));
    }

    [Fact]
    public void The_mode_travels_with_the_margin_so_the_two_cannot_disagree()
    {
        TrimMargin.Tight.Mode.ShouldBe(TrimMode.TightCrop);
        TrimMargin.Uniform(4).Mode.ShouldBe(TrimMode.UniformMargin);
        TrimMargin.PerEdge(1, 2, 3, 4).Mode.ShouldBe(TrimMode.EdgeSpecificMargin);

        // A uniform margin of zero is still a uniform margin: the operator asked for a margin
        // and typed nothing, which is a different fact from asking for a tight crop.
        TrimMargin.Uniform(0).Mode.ShouldBe(TrimMode.UniformMargin);
        TrimMargin.Uniform(0).IsNone.ShouldBeTrue();
    }

    // -----------------------------------------------------------------------------
    // Clamping
    // -----------------------------------------------------------------------------

    [Fact]
    public void A_margin_larger_than_the_space_above_and_left_clamps_to_zero_not_to_a_negative()
    {
        TrimBounds content = TrimBounds.FromEdges(5, 5, 30, 30);

        TrimBounds expanded = content.Expand(TrimMargin.Uniform(10), 100, 100);

        expanded.Left.ShouldBe(0);
        expanded.Top.ShouldBe(0);
        expanded.RightExclusive.ShouldBe(40);
        expanded.BottomExclusive.ShouldBe(40);
    }

    [Fact]
    public void A_margin_larger_than_the_space_below_and_right_clamps_to_the_canvas_edge()
    {
        TrimBounds content = TrimBounds.FromEdges(40, 40, 45, 46);

        TrimBounds expanded = content.Expand(TrimMargin.Uniform(10), 50, 50);

        expanded.RightExclusive.ShouldBe(50);
        expanded.BottomExclusive.ShouldBe(50);
        expanded.Left.ShouldBe(30);
        expanded.Top.ShouldBe(30);
    }

    [Fact]
    public void A_margin_bigger_than_the_whole_canvas_yields_the_whole_canvas()
    {
        TrimBounds expanded = Content.Expand(TrimMargin.Uniform(10_000), 100, 100);

        expanded.ShouldBe(TrimBounds.Canvas(100, 100));
        expanded.CoversCanvas(100, 100).ShouldBeTrue();
    }

    /// <summary>An absurd margin clamps rather than wrapping round through integer overflow.</summary>
    [Fact]
    public void An_enormous_margin_clamps_instead_of_overflowing()
    {
        TrimBounds expanded = Content.Expand(TrimMargin.Uniform(int.MaxValue), 100, 100);

        expanded.ShouldBe(TrimBounds.Canvas(100, 100));
    }

    // -----------------------------------------------------------------------------
    // Refusals
    // -----------------------------------------------------------------------------

    [Theory]
    [InlineData(-1)]
    [InlineData(-10)]
    [InlineData(int.MinValue)]
    public void A_negative_uniform_margin_is_refused(int pixels)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => TrimMargin.Uniform(pixels));
    }

    [Theory]
    [InlineData(-1, 0, 0, 0)]
    [InlineData(0, -1, 0, 0)]
    [InlineData(0, 0, -1, 0)]
    [InlineData(0, 0, 0, -1)]
    public void A_negative_margin_on_any_single_edge_is_refused(int top, int right, int bottom, int left)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => TrimMargin.PerEdge(top, right, bottom, left));
    }

    [Fact]
    public void Expanding_bounds_that_do_not_fit_the_stated_canvas_is_refused()
    {
        Should.Throw<ArgumentException>(() => Content.Expand(TrimMargin.Tight, 10, 10));
    }
}
