using PrintFlow.Domain.Trimming;

namespace PrintFlow.Tests.Unit.Trimming;

/// <summary>
/// The half-open rectangle convention, asserted so it cannot drift (Epic 11200 Part B §4).
/// </summary>
/// <remarks>
/// The convention is the whole value of this type: left/top inclusive, right/bottom exclusive,
/// exactly like <c>Int32Rect</c>. If someone later reads <see cref="TrimBounds.RightExclusive"/>
/// as inclusive, every crop loses its last column and its last row — a defect that looks like
/// "the trim is slightly tight" rather than like a bug.
/// </remarks>
public sealed class TrimBoundsTests
{
    [Fact]
    public void Width_and_height_are_plain_subtractions_of_the_exclusive_edges()
    {
        TrimBounds bounds = TrimBounds.FromEdges(3, 4, 10, 9);

        bounds.Width.ShouldBe(7);
        bounds.Height.ShouldBe(5);
    }

    [Fact]
    public void A_single_pixel_spans_one_column_and_one_row()
    {
        TrimBounds bounds = TrimBounds.FromEdges(3, 4, 4, 5);

        bounds.Width.ShouldBe(1);
        bounds.Height.ShouldBe(1);
    }

    [Fact]
    public void The_origin_and_size_spelling_agrees_with_the_edge_spelling()
    {
        TrimBounds.FromSize(3, 4, 7, 5).ShouldBe(TrimBounds.FromEdges(3, 4, 10, 9));
    }

    [Fact]
    public void A_canvas_rectangle_covers_the_canvas_and_says_so()
    {
        TrimBounds canvas = TrimBounds.Canvas(40, 30);

        canvas.ShouldBe(TrimBounds.FromEdges(0, 0, 40, 30));
        canvas.CoversCanvas(40, 30).ShouldBeTrue();
        canvas.CoversCanvas(41, 30).ShouldBeFalse();
        canvas.CoversCanvas(40, 31).ShouldBeFalse();
    }

    [Fact]
    public void A_rectangle_one_pixel_short_of_the_canvas_does_not_cover_it()
    {
        TrimBounds.FromEdges(0, 0, 39, 30).CoversCanvas(40, 30).ShouldBeFalse();
        TrimBounds.FromEdges(1, 0, 40, 30).CoversCanvas(40, 30).ShouldBeFalse();
    }

    [Fact]
    public void Containment_is_measured_against_the_exclusive_edges()
    {
        TrimBounds bounds = TrimBounds.FromEdges(0, 0, 40, 30);

        bounds.FitsWithin(40, 30).ShouldBeTrue();
        bounds.FitsWithin(39, 30).ShouldBeFalse();
        bounds.FitsWithin(40, 29).ShouldBeFalse();
    }

    [Theory]
    [InlineData(-1, 0, 4, 4)]
    [InlineData(0, -1, 4, 4)]
    [InlineData(2, 0, 2, 4)]
    [InlineData(0, 2, 4, 2)]
    [InlineData(3, 0, 2, 4)]
    public void A_rectangle_containing_no_pixel_is_refused(int left, int top, int right, int bottom)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => TrimBounds.FromEdges(left, top, right, bottom));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void A_size_without_pixels_is_refused(int size)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => TrimBounds.FromSize(0, 0, size, 4));
        Should.Throw<ArgumentOutOfRangeException>(() => TrimBounds.FromSize(0, 0, 4, size));
    }

    /// <summary>Only the default value is empty; nothing the factories return ever is.</summary>
    [Fact]
    public void Only_the_default_value_is_empty()
    {
        default(TrimBounds).IsEmpty.ShouldBeTrue();
        TrimBounds.FromEdges(0, 0, 1, 1).IsEmpty.ShouldBeFalse();
    }
}
