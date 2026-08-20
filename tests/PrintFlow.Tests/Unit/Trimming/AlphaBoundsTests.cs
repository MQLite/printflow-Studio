using PrintFlow.Domain.Trimming;

namespace PrintFlow.Tests.Unit.Trimming;

/// <summary>
/// The alpha-content scan, tested as the pure function it is (Epic 11200 Part B §20).
/// </summary>
/// <remarks>
/// No file, no decoder, no workspace: these assert the rule itself — <c>alpha &gt; 0</c>
/// contributes, everything else does not — because that rule is what a future refactor of the
/// imaging code is most likely to bend. The two cases worth the most are the ones a threshold
/// would break: <see cref="Alpha_of_1_counts_as_content"/> and the sparse edge pixel.
/// </remarks>
public sealed class AlphaBoundsTests
{
    // -----------------------------------------------------------------------------
    // §20.1–§20.2: a single visible pixel, wherever it sits
    // -----------------------------------------------------------------------------

    [Fact]
    public void One_opaque_pixel_in_the_centre_bounds_exactly_that_pixel()
    {
        byte[] plane = Plane(5, 5, (2, 2, 255));

        TrimBounds bounds = AlphaBounds.Compute(5, 5, plane).ShouldNotBeNull();

        bounds.ShouldBe(TrimBounds.FromEdges(2, 2, 3, 3));
        bounds.Width.ShouldBe(1);
        bounds.Height.ShouldBe(1);
    }

    /// <summary>
    /// Every corner and every edge midpoint of a 5×5 canvas, one at a time.
    /// </summary>
    /// <remarks>
    /// The corners are where an off-by-one in the exclusive-edge convention shows up: a
    /// bottom-right pixel at (4,4) must yield an exclusive edge of 5, and clipping it would
    /// be invisible in any test whose content sits comfortably inside the canvas.
    /// </remarks>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(4, 0)]
    [InlineData(0, 4)]
    [InlineData(4, 4)]
    [InlineData(2, 0)]
    [InlineData(0, 2)]
    [InlineData(4, 2)]
    [InlineData(2, 4)]
    public void One_pixel_on_any_edge_or_corner_is_kept(int x, int y)
    {
        byte[] plane = Plane(5, 5, (x, y, 255));

        AlphaBounds.Compute(5, 5, plane).ShouldBe(TrimBounds.FromEdges(x, y, x + 1, y + 1));
    }

    // -----------------------------------------------------------------------------
    // §20.3–§20.4: borders and sparse content
    // -----------------------------------------------------------------------------

    [Fact]
    public void A_transparent_border_is_excluded_from_the_content_bounds()
    {
        byte[] plane = Plane(8, 6);
        for (int y = 1; y < 5; y++)
        {
            for (int x = 2; x < 6; x++)
            {
                plane[(y * 8) + x] = 255;
            }
        }

        AlphaBounds.Compute(8, 6, plane).ShouldBe(TrimBounds.FromEdges(2, 1, 6, 5));
    }

    [Fact]
    public void Sparse_disconnected_pixels_all_contribute_to_one_bounding_box()
    {
        byte[] plane = Plane(10, 10, (1, 7, 255), (8, 2, 255), (4, 4, 255));

        AlphaBounds.Compute(10, 10, plane).ShouldBe(TrimBounds.FromEdges(1, 2, 9, 8));
    }

    /// <summary>A single stray pixel on an outer edge still holds the whole canvas open.</summary>
    /// <remarks>
    /// Ignoring it as noise would be a silent crop of the operator's artwork. Keeping it is
    /// visible in review, which is the recoverable direction to be wrong in.
    /// </remarks>
    [Fact]
    public void A_lone_edge_pixel_is_never_dismissed_as_noise()
    {
        byte[] plane = Plane(9, 9, (4, 4, 255), (0, 8, 3));

        AlphaBounds.Compute(9, 9, plane).ShouldBe(TrimBounds.FromEdges(0, 4, 5, 9));
    }

    // -----------------------------------------------------------------------------
    // §20.5–§20.7: whole canvas, nothing at all, and degenerate shapes
    // -----------------------------------------------------------------------------

    [Fact]
    public void A_fully_opaque_canvas_bounds_the_whole_canvas()
    {
        byte[] plane = Plane(4, 3);
        Array.Fill(plane, (byte)255);

        AlphaBounds.Compute(4, 3, plane).ShouldBe(TrimBounds.Canvas(4, 3));
    }

    [Fact]
    public void A_fully_transparent_canvas_has_no_bounds_at_all()
    {
        AlphaBounds.Compute(6, 6, Plane(6, 6)).ShouldBeNull();
    }

    [Fact]
    public void One_pixel_wide_content_keeps_its_single_column()
    {
        byte[] plane = Plane(7, 7, (3, 1, 255), (3, 2, 255), (3, 3, 255));

        TrimBounds bounds = AlphaBounds.Compute(7, 7, plane).ShouldNotBeNull();

        bounds.ShouldBe(TrimBounds.FromEdges(3, 1, 4, 4));
        bounds.Width.ShouldBe(1);
        bounds.Height.ShouldBe(3);
    }

    [Fact]
    public void One_pixel_tall_content_keeps_its_single_row()
    {
        byte[] plane = Plane(7, 7, (1, 5, 255), (2, 5, 255), (3, 5, 255));

        TrimBounds bounds = AlphaBounds.Compute(7, 7, plane).ShouldNotBeNull();

        bounds.ShouldBe(TrimBounds.FromEdges(1, 5, 4, 6));
        bounds.Width.ShouldBe(3);
        bounds.Height.ShouldBe(1);
    }

    // -----------------------------------------------------------------------------
    // §20.8–§20.10: the threshold rule, stated three ways
    // -----------------------------------------------------------------------------

    /// <summary>
    /// The rule is <c>alpha &gt; 0</c>, not <c>alpha &gt; 10</c> or <c>alpha &gt; 128</c>.
    /// </summary>
    /// <remarks>
    /// This is the test that fails first if anyone ever "cleans up" the scan with a threshold.
    /// A barely-visible antialiased edge is still artwork, and a threshold shaves the soft
    /// edge off every cut-out in the shop without anyone noticing until it is printed.
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(254)]
    [InlineData(255)]
    public void Alpha_of_1_counts_as_content(byte alpha)
    {
        byte[] plane = Plane(3, 3, (1, 1, alpha));

        AlphaBounds.Compute(3, 3, plane).ShouldBe(TrimBounds.FromEdges(1, 1, 2, 2));
    }

    [Fact]
    public void Alpha_of_zero_never_counts_even_beside_content()
    {
        byte[] plane = Plane(5, 1, (0, 0, 0), (1, 0, 0), (2, 0, 255), (3, 0, 0), (4, 0, 0));

        AlphaBounds.Compute(5, 1, plane).ShouldBe(TrimBounds.FromEdges(2, 0, 3, 1));
    }

    // -----------------------------------------------------------------------------
    // Contract
    // -----------------------------------------------------------------------------

    [Fact]
    public void An_alpha_plane_of_the_wrong_length_is_refused_rather_than_read_off_the_end()
    {
        Should.Throw<ArgumentException>(() => AlphaBounds.Compute(4, 4, new byte[15]));
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(4, 0)]
    [InlineData(-1, 4)]
    public void A_canvas_without_pixels_is_refused(int width, int height)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => AlphaBounds.Compute(width, height, []));
    }

    /// <summary>A canvas of the given size with the listed pixels set and everything else transparent.</summary>
    private static byte[] Plane(int width, int height, params (int X, int Y, byte Alpha)[] pixels)
    {
        byte[] plane = new byte[width * height];
        foreach ((int x, int y, byte alpha) in pixels)
        {
            plane[(y * width) + x] = alpha;
        }

        return plane;
    }
}
