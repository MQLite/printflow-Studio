using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Trimming;

namespace PrintFlow.Tests.Unit.Ui;

/// <summary>
/// The display-to-source mapping, tested as arithmetic (Epic 11200 Part C2 §7, §32).
/// </summary>
/// <remarks>
/// Every case §32 asks for is here: a fitted image, 200% zoom, a non-square viewport, a
/// letterboxed image, the top-left boundary and the bottom-right boundary. None of them needs a
/// window, a mouse or an STA thread, which is the point of extracting the geometry in the first
/// place — a failure below names a wrong number rather than a WPF interaction that stopped
/// working.
/// <para>
/// The case that would be easiest to get wrong, and hardest to see, is the reduced preview: what
/// the operator drags over is the payload, and what a crop is recorded in is the source, and for
/// a large artefact those differ by a whole factor. It gets its own section.
/// </para>
/// </remarks>
public sealed class CropSurfaceLayoutTests
{
    // -----------------------------------------------------------------------------
    // Fitted: the opening state of every review (§32)
    // -----------------------------------------------------------------------------

    /// <summary>
    /// A square image fitted to a square surface maps one surface pixel to one source pixel.
    /// </summary>
    /// <remarks>
    /// The simplest possible case, asserted first so a failure anywhere below can be read as
    /// "the scaling is wrong" rather than "the whole mapping is wrong".
    /// </remarks>
    [Fact]
    public void A_square_image_fitted_to_a_square_surface_maps_one_to_one()
    {
        CropSurfaceLayout layout = Fitted(surface: 100, image: 100);

        layout.IsUsable.ShouldBeTrue();
        layout.DisplayScale.ShouldBe(1.0, 1e-9);
        layout.LetterboxX.ShouldBe(0);
        layout.LetterboxY.ShouldBe(0);

        layout.TryToSourceBounds(10, 20, 40, 60, out TrimBounds bounds).ShouldBeTrue();
        bounds.ShouldBe(TrimBounds.FromEdges(10, 20, 40, 60));
    }

    /// <summary>A fitted image half the surface's size halves every coordinate (§32).</summary>
    [Fact]
    public void A_fitted_image_scales_the_drag_by_the_fit_factor()
    {
        // 50×50 source shown on a 100×100 surface: Uniform doubles it.
        CropSurfaceLayout layout = Fitted(surface: 100, image: 50);

        layout.DisplayScale.ShouldBe(2.0, 1e-9);

        layout.TryToSourceBounds(20, 20, 60, 80, out TrimBounds bounds).ShouldBeTrue();
        bounds.ShouldBe(TrimBounds.FromEdges(10, 10, 30, 40));
    }

    // -----------------------------------------------------------------------------
    // Letterboxing: the offset that is invisible until a crop lands in the wrong place
    // -----------------------------------------------------------------------------

    /// <summary>
    /// A portrait image fitted to a landscape surface is centred, and the empty margin is
    /// subtracted before the mapping (§7, §32).
    /// </summary>
    /// <remarks>
    /// The failure this guards against is quiet and plausible-looking: ignore the margin and
    /// every crop lands exactly <see cref="CropSurfaceLayout.LetterboxX"/> too far left, which
    /// looks like a slightly mis-registered image rather than a bug.
    /// </remarks>
    [Fact]
    public void A_letterboxed_fitted_image_subtracts_the_centring_margin()
    {
        // 100×200 source on a 400×200 surface: fit scale 1.0, image 100 wide, 150 px spare
        // on each side.
        CropSurfaceLayout layout = new(
            SurfaceWidth: 400, SurfaceHeight: 200,
            PayloadPixelWidth: 100, PayloadPixelHeight: 200,
            SourcePixelWidth: 100, SourcePixelHeight: 200,
            IsFitToViewport: true, ZoomScale: 1.0);

        layout.DisplayScale.ShouldBe(1.0, 1e-9);
        layout.LetterboxX.ShouldBe(150, 1e-9);
        layout.LetterboxY.ShouldBe(0, 1e-9);

        // The image's own left edge sits at surface x = 150.
        layout.ToSourceX(150).ShouldBe(0, 1e-9);
        layout.ToSourceX(250).ShouldBe(100, 1e-9);

        layout.TryToSourceBounds(160, 10, 200, 60, out TrimBounds bounds).ShouldBeTrue();
        bounds.ShouldBe(TrimBounds.FromEdges(10, 10, 50, 60));
    }

    /// <summary>A landscape image on a tall surface is letterboxed vertically instead (§32).</summary>
    [Fact]
    public void A_non_square_viewport_fits_on_the_constraining_axis()
    {
        // 200×100 source on a 200×400 surface: width is the constraint, so scale 1.0 and
        // 150 px spare above and below.
        CropSurfaceLayout layout = new(
            SurfaceWidth: 200, SurfaceHeight: 400,
            PayloadPixelWidth: 200, PayloadPixelHeight: 100,
            SourcePixelWidth: 200, SourcePixelHeight: 100,
            IsFitToViewport: true, ZoomScale: 1.0);

        layout.DisplayScale.ShouldBe(1.0, 1e-9);
        layout.LetterboxX.ShouldBe(0, 1e-9);
        layout.LetterboxY.ShouldBe(150, 1e-9);

        layout.TryToSourceBounds(0, 150, 200, 250, out TrimBounds bounds).ShouldBeTrue();
        bounds.ShouldBe(TrimBounds.Canvas(200, 100));
    }

    // -----------------------------------------------------------------------------
    // Zoom (§32)
    // -----------------------------------------------------------------------------

    /// <summary>At 200% a drag covers half as much artwork as it does on screen (§32).</summary>
    [Fact]
    public void At_two_hundred_percent_a_drag_covers_half_as_many_source_pixels()
    {
        // 100×100 source magnified to 200×200 inside a 200×200 surface: no letterbox left.
        CropSurfaceLayout layout = new(
            SurfaceWidth: 200, SurfaceHeight: 200,
            PayloadPixelWidth: 100, PayloadPixelHeight: 100,
            SourcePixelWidth: 100, SourcePixelHeight: 100,
            IsFitToViewport: false, ZoomScale: 2.0);

        layout.DisplayScale.ShouldBe(2.0, 1e-9);
        layout.LetterboxX.ShouldBe(0, 1e-9);

        layout.TryToSourceBounds(40, 60, 100, 120, out TrimBounds bounds).ShouldBeTrue();
        bounds.ShouldBe(TrimBounds.FromEdges(20, 30, 50, 60));
    }

    /// <summary>A magnified image smaller than its surface is still centred (§32).</summary>
    [Fact]
    public void A_magnified_image_smaller_than_the_surface_is_still_letterboxed()
    {
        // 20×20 source at 200% is 40×40 on a 100×100 surface: 30 px spare on each side.
        CropSurfaceLayout layout = new(
            SurfaceWidth: 100, SurfaceHeight: 100,
            PayloadPixelWidth: 20, PayloadPixelHeight: 20,
            SourcePixelWidth: 20, SourcePixelHeight: 20,
            IsFitToViewport: false, ZoomScale: 2.0);

        layout.LetterboxX.ShouldBe(30, 1e-9);
        layout.ToSourceX(30).ShouldBe(0, 1e-9);
        layout.ToSourceX(70).ShouldBe(20, 1e-9);
    }

    /// <summary>Fit is ignored while magnified, and the zoom is ignored while fitted.</summary>
    [Fact]
    public void The_scale_comes_from_whichever_mode_is_active()
    {
        CropSurfaceLayout fitted = Fitted(surface: 100, image: 50) with { ZoomScale = 7.0 };
        fitted.DisplayScale.ShouldBe(2.0, 1e-9);

        CropSurfaceLayout zoomed = fitted with { IsFitToViewport = false };
        zoomed.DisplayScale.ShouldBe(7.0, 1e-9);
    }

    // -----------------------------------------------------------------------------
    // A reduced preview: payload pixels are not source pixels (Part C1 §6; Part C2 §7)
    // -----------------------------------------------------------------------------

    /// <summary>
    /// A preview reduced for display still produces a crop in the artefact's own pixels.
    /// </summary>
    /// <remarks>
    /// The subtlest of the four things §7 lists. A 4000&#160;px design previews at 2000&#160;px,
    /// so at 100% zoom a 100&#160;px drag is 200 source pixels — and a mapping that stopped at
    /// the payload would record a crop half the size the operator drew, on a file it would then
    /// be applied to at full resolution.
    /// </remarks>
    [Fact]
    public void A_reduced_preview_maps_the_drag_back_to_full_source_pixels()
    {
        CropSurfaceLayout layout = new(
            SurfaceWidth: 2000, SurfaceHeight: 2000,
            PayloadPixelWidth: 2000, PayloadPixelHeight: 2000,
            SourcePixelWidth: 4000, SourcePixelHeight: 4000,
            IsFitToViewport: false, ZoomScale: 1.0);

        layout.SourcePerPayloadX.ShouldBe(2.0, 1e-9);

        layout.TryToSourceBounds(100, 200, 300, 500, out TrimBounds bounds).ShouldBeTrue();
        bounds.ShouldBe(TrimBounds.FromEdges(200, 400, 600, 1000));
    }

    // -----------------------------------------------------------------------------
    // Boundaries (§32) and refusals (§23)
    // -----------------------------------------------------------------------------

    /// <summary>A drag over the whole fitted image selects the whole canvas exactly (§32).</summary>
    [Fact]
    public void A_drag_across_the_whole_image_selects_every_pixel_and_no_more()
    {
        CropSurfaceLayout layout = Fitted(surface: 100, image: 100);

        layout.TryToSourceBounds(0, 0, 100, 100, out TrimBounds bounds).ShouldBeTrue();
        bounds.ShouldBe(TrimBounds.Canvas(100, 100));
        bounds.FitsWithin(100, 100).ShouldBeTrue();
    }

    /// <summary>The top-left corner is inclusive and never negative (§32).</summary>
    [Fact]
    public void A_drag_starting_past_the_top_left_corner_is_refused()
    {
        CropSurfaceLayout layout = Fitted(surface: 100, image: 100);

        layout.TryToSourceBounds(-40, -25, 10, 10, out TrimBounds bounds).ShouldBeFalse();
        bounds.IsEmpty.ShouldBeTrue();
    }

    /// <summary>The bottom-right edge is exclusive and never past the canvas (§32).</summary>
    [Fact]
    public void A_drag_running_past_the_bottom_right_corner_is_refused()
    {
        CropSurfaceLayout layout = Fitted(surface: 100, image: 100);

        layout.TryToSourceBounds(90, 90, 400, 400, out TrimBounds bounds).ShouldBeFalse();
        bounds.IsEmpty.ShouldBeTrue();
    }

    /// <summary>Corners in any order describe the same rectangle.</summary>
    [Fact]
    public void A_drag_up_and_to_the_left_is_the_same_rectangle()
    {
        CropSurfaceLayout layout = Fitted(surface: 100, image: 100);

        layout.TryToSourceBounds(60, 70, 20, 30, out TrimBounds backwards).ShouldBeTrue();
        layout.TryToSourceBounds(20, 30, 60, 70, out TrimBounds forwards).ShouldBeTrue();

        backwards.ShouldBe(forwards);
    }

    /// <summary>A click that never moved selects nothing (§23).</summary>
    [Fact]
    public void A_click_without_a_drag_is_refused()
    {
        Fitted(surface: 100, image: 100)
            .TryToSourceBounds(40, 40, 40, 40, out _).ShouldBeFalse();
    }

    /// <summary>Zero width and zero height are each refused on their own (§23).</summary>
    [Theory]
    [InlineData(40.0, 10.0, 40.0, 60.0)]
    [InlineData(10.0, 40.0, 60.0, 40.0)]
    public void A_degenerate_drag_is_refused(double x1, double y1, double x2, double y2)
    {
        Fitted(surface: 100, image: 100)
            .TryToSourceBounds(x1, y1, x2, y2, out _).ShouldBeFalse();
    }

    /// <summary>
    /// A rectangle entirely off the artwork is refused rather than clamped (§23).
    /// </summary>
    /// <remarks>
    /// A selection outside the image is refused whether or not part overlaps the artwork.
    /// </remarks>
    [Fact]
    public void A_rectangle_entirely_outside_the_image_is_refused()
    {
        CropSurfaceLayout layout = new(
            SurfaceWidth: 400, SurfaceHeight: 200,
            PayloadPixelWidth: 100, PayloadPixelHeight: 200,
            SourcePixelWidth: 100, SourcePixelHeight: 200,
            IsFitToViewport: true, ZoomScale: 1.0);

        // Wholly inside the left letterbox margin, which stops at x = 150.
        layout.TryToSourceBounds(10, 10, 100, 100, out _).ShouldBeFalse();
    }

    /// <summary>A surface that has not been arranged yet answers "unusable", never infinity.</summary>
    [Theory]
    [InlineData(0.0, 100.0, 10, 10)]
    [InlineData(100.0, 0.0, 10, 10)]
    [InlineData(100.0, 100.0, 0, 10)]
    [InlineData(100.0, 100.0, 10, 0)]
    public void An_unmeasured_surface_produces_no_crop(
        double surfaceWidth, double surfaceHeight, int imageWidth, int imageHeight)
    {
        CropSurfaceLayout layout = new(
            surfaceWidth, surfaceHeight, imageWidth, imageHeight, imageWidth, imageHeight,
            IsFitToViewport: true, ZoomScale: 1.0);

        layout.IsUsable.ShouldBeFalse();
        layout.TryToSourceBounds(0, 0, 50, 50, out _).ShouldBeFalse();
        layout.TryToSurfaceRect(TrimBounds.Canvas(4, 4), out _, out _, out _, out _).ShouldBeFalse();
    }

    // -----------------------------------------------------------------------------
    // The outline: a round trip that keeps the rectangle over the same artwork (§7)
    // -----------------------------------------------------------------------------

    /// <summary>
    /// Projecting a selection back onto a differently scaled surface lands on the same artwork.
    /// </summary>
    /// <remarks>
    /// This is what makes zooming with a selection open safe: the rectangle is held in source
    /// pixels and re-projected, so the outline follows the artwork instead of staying where it
    /// was drawn on screen.
    /// </remarks>
    [Fact]
    public void A_selection_projects_back_onto_the_artwork_at_a_different_zoom()
    {
        CropSurfaceLayout fitted = Fitted(surface: 100, image: 100);
        fitted.TryToSourceBounds(10, 20, 40, 60, out TrimBounds bounds).ShouldBeTrue();

        CropSurfaceLayout magnified = new(
            SurfaceWidth: 200, SurfaceHeight: 200,
            PayloadPixelWidth: 100, PayloadPixelHeight: 100,
            SourcePixelWidth: 100, SourcePixelHeight: 100,
            IsFitToViewport: false, ZoomScale: 2.0);

        magnified.TryToSurfaceRect(bounds, out double x, out double y, out double w, out double h)
            .ShouldBeTrue();

        x.ShouldBe(20, 1e-9);
        y.ShouldBe(40, 1e-9);
        w.ShouldBe(60, 1e-9);
        h.ShouldBe(80, 1e-9);

        // And converting that outline back again gives the rectangle it started from.
        magnified.TryToSourceBounds(x, y, x + w, y + h, out TrimBounds roundTripped).ShouldBeTrue();
        roundTripped.ShouldBe(bounds);
    }

    /// <summary>
    /// A rectangle drawn on a pixel boundary survives an awkward scale without growing.
    /// </summary>
    /// <remarks>
    /// The defect this pins down was real and invisible: outward rounding plus floating-point
    /// drift turned an edge at exactly 9 into 9.000000000001, which <c>Ceiling</c> made 10, and
    /// the crop silently kept a column of artwork the operator had not selected. Scales like
    /// 1.25³ and 83⅓ are what produce the drift, so they are what this uses.
    /// </remarks>
    [Theory]
    [InlineData(1.25 * 1.25 * 1.25)]
    [InlineData(1000.0 / 12.0)]
    [InlineData(0.1)]
    [InlineData(3.7)]
    public void An_edge_on_a_pixel_boundary_does_not_gain_a_row_at_an_awkward_scale(double scale)
    {
        CropSurfaceLayout layout = new(
            SurfaceWidth: 12 * scale, SurfaceHeight: 10 * scale,
            PayloadPixelWidth: 12, PayloadPixelHeight: 10,
            SourcePixelWidth: 12, SourcePixelHeight: 10,
            IsFitToViewport: false, ZoomScale: scale);

        TrimBounds intended = TrimBounds.FromEdges(3, 2, 9, 7);

        // Project the intended rectangle onto the surface, then read it straight back.
        layout.TryToSurfaceRect(intended, out double x, out double y, out double w, out double h)
            .ShouldBeTrue();
        layout.TryToSourceBounds(x, y, x + w, y + h, out TrimBounds roundTripped).ShouldBeTrue();

        roundTripped.ShouldBe(intended);
    }

    /// <summary>A genuine fraction of a pixel is still kept, tolerance or not (§23).</summary>
    /// <remarks>
    /// The other side of the tolerance: it must absorb arithmetic noise without absorbing a
    /// small real selection. A drag covering a third of one pixel selects that pixel.
    /// </remarks>
    [Fact]
    public void A_drag_covering_a_fraction_of_a_pixel_still_selects_it()
    {
        CropSurfaceLayout layout = Fitted(surface: 100, image: 100);

        layout.TryToSourceBounds(4.3, 7.2, 4.6, 7.6, out TrimBounds bounds).ShouldBeTrue();
        bounds.ShouldBe(TrimBounds.FromSize(4, 7, 1, 1));
    }

    /// <summary>A square image of <paramref name="image"/> pixels fitted to a square surface.</summary>
    private static CropSurfaceLayout Fitted(double surface, int image) => new(
        SurfaceWidth: surface,
        SurfaceHeight: surface,
        PayloadPixelWidth: image,
        PayloadPixelHeight: image,
        SourcePixelWidth: image,
        SourcePixelHeight: image,
        IsFitToViewport: true,
        ZoomScale: 1.0);
}
