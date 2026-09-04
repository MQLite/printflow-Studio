using PrintFlow.Domain.Trimming;

namespace PrintFlow.Tests.Unit.Trimming;

/// <summary>
/// The pair of rectangles one produced trim established (SCRUM-11081 §3, §4, §5).
/// </summary>
/// <remarks>
/// Pure geometry, like the two types it is built from: no file, no decoder, no database. What is
/// worth testing here is the one rule the type adds on top of <see cref="TrimBounds"/> — that an
/// applied rectangle can never cut inside the content it was expanded from — because that rule is
/// what makes a stored pair interpretable at all.
/// </remarks>
public sealed class TrimGeometryTests
{
    /// <summary>A tight trim detects and applies the same rectangle (§4, §5).</summary>
    [Fact]
    public void A_tight_pair_is_valid_and_reports_itself_as_tight()
    {
        TrimBounds content = TrimBounds.FromEdges(3, 2, 8, 7);

        TrimGeometry geometry = TrimGeometry.Create(content, content);

        geometry.ContentBounds.ShouldBe(content);
        geometry.AppliedBounds.ShouldBe(content);
        geometry.IsTightToContent.ShouldBeTrue();
    }

    /// <summary>A margined trim keeps the detected rectangle unchanged (§4).</summary>
    /// <remarks>
    /// The property that makes two rectangles worth storing rather than one: the content bounds
    /// answer "where is the artwork", and no margin, clamp, display scale or print size may move
    /// that answer.
    /// </remarks>
    [Fact]
    public void A_margin_expands_the_applied_rectangle_and_leaves_the_content_alone()
    {
        TrimBounds content = TrimBounds.FromEdges(3, 2, 8, 7);
        TrimBounds applied = content.Expand(TrimMargin.Uniform(2), 12, 10);

        TrimGeometry geometry = TrimGeometry.Create(content, applied);

        geometry.ContentBounds.ShouldBe(content);
        geometry.AppliedBounds.ShouldBe(TrimBounds.FromEdges(1, 0, 10, 9));
        geometry.IsTightToContent.ShouldBeFalse();
    }

    /// <summary>A clamped margin is still a valid pair (§5).</summary>
    /// <remarks>
    /// The clamp is what keeps every applied rectangle inside the source canvas, so "the whole
    /// canvas" is the widest legal answer and not an error. It is also the case a size-only
    /// record could never reconstruct: the applied rectangle here is neither content nor content
    /// plus the requested margin.
    /// </remarks>
    [Fact]
    public void A_margin_clamped_to_the_canvas_is_a_valid_pair()
    {
        TrimBounds content = TrimBounds.FromEdges(3, 2, 8, 7);
        TrimBounds applied = content.Expand(TrimMargin.Uniform(500), 12, 10);

        TrimGeometry geometry = TrimGeometry.Create(content, applied);

        geometry.AppliedBounds.ShouldBe(TrimBounds.Canvas(12, 10));
        geometry.AppliedBounds.CoversCanvas(12, 10).ShouldBeTrue();
    }

    /// <summary>
    /// An applied rectangle that cuts inside the detected content is refused (§5).
    /// </summary>
    /// <remarks>
    /// One edge at a time, because a single combined check would pass if two errors cancelled.
    /// A margin only ever grows the crop and the clamp only ever stops it at the canvas edge, so
    /// none of these four pairs describes a trim this product can perform — and a stored row
    /// claiming one would put a fiction on the operator's review screen.
    /// </remarks>
    [Theory]
    [InlineData(4, 2, 8, 7)]  // left moved in
    [InlineData(3, 3, 8, 7)]  // top moved in
    [InlineData(3, 2, 7, 7)]  // right moved in
    [InlineData(3, 2, 8, 6)]  // bottom moved in
    public void An_applied_rectangle_inside_the_content_is_refused(
        int left, int top, int right, int bottom)
    {
        TrimBounds content = TrimBounds.FromEdges(3, 2, 8, 7);
        TrimBounds applied = TrimBounds.FromEdges(left, top, right, bottom);

        Should.Throw<ArgumentException>(() => TrimGeometry.Create(content, applied));
    }

    /// <summary>A default rectangle is not a measurement, and is refused as either half.</summary>
    /// <remarks>
    /// <c>default(TrimBounds)</c> is the one rectangle the factories cannot produce, and it means
    /// "never given a value". Accepting it would let a caller record a zero-area crop that no
    /// alpha scan ever returned.
    /// </remarks>
    [Fact]
    public void A_default_rectangle_is_refused_on_either_side()
    {
        TrimBounds real = TrimBounds.FromEdges(3, 2, 8, 7);

        Should.Throw<ArgumentException>(() => TrimGeometry.Create(default, real));
        Should.Throw<ArgumentException>(() => TrimGeometry.Create(real, default));
    }

    /// <summary>Two geometries with the same rectangles are the same value.</summary>
    /// <remarks>
    /// It is compared by value across a database round trip and in the read model, so structural
    /// equality is a property the persistence tests rely on rather than an incidental one.
    /// </remarks>
    [Fact]
    public void Equality_is_by_the_two_rectangles()
    {
        TrimBounds content = TrimBounds.FromEdges(3, 2, 8, 7);
        TrimBounds applied = TrimBounds.FromEdges(1, 0, 10, 9);

        TrimGeometry.Create(content, applied).ShouldBe(TrimGeometry.Create(content, applied));
        TrimGeometry.Create(content, applied).ShouldNotBe(TrimGeometry.Create(content, content));
    }
}
