using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Trimming;

namespace PrintFlow.Tests.Unit.Ui;

/// <summary>
/// Handle gestures over the existing crop mapping (SCRUM-11147 design §5). Values in, values out:
/// no window, no mouse.
/// </summary>
public sealed class CropHandleGestureTests
{
    /// <summary>The design's worked example: 4000×3000 source, 2048×1536 preview, 1024×800 fitted surface.</summary>
    private static readonly CropSurfaceLayout Fitted = new(1024, 800, 2048, 1536, 4000, 3000, IsFitToViewport: true, ZoomScale: 1);

    /// <summary>The same image magnified 200%: the surface is the drawn image, no letterbox.</summary>
    private static readonly CropSurfaceLayout Magnified = new(4096, 3072, 2048, 1536, 4000, 3000, IsFitToViewport: false, ZoomScale: 2);

    /// <summary>A portrait source fitted on a landscape surface: 212 DIP of letterbox on each side.</summary>
    private static readonly CropSurfaceLayout Letterboxed = new(1024, 800, 1536, 2048, 3000, 4000, IsFitToViewport: true, ZoomScale: 1);

    private static readonly TrimBounds Automatic = TrimBounds.FromEdges(1180, 780, 2820, 2220);

    [Fact]
    public void The_worked_example_expands_left_and_reduces_bottom_by_pointer_displacement()
    {
        Fitted.LetterboxY.ShouldBe(16);
        Fitted.TryToSurfaceRect(Automatic, out double x, out double y, out double w, out double h).ShouldBeTrue();
        x.ShouldBe(302.08, 1e-9);
        (y + h).ShouldBe(584.32, 1e-9);

        CropHandleGesture.TryDrag(Fitted, Automatic, CropHandle.Left, -12.08, 0, out TrimBounds expanded).ShouldBeTrue();
        expanded.ShouldBe(TrimBounds.FromEdges(1132, 780, 2820, 2220));

        CropHandleGesture.TryDrag(Fitted, expanded, CropHandle.Bottom, 0, -24.32, out TrimBounds reduced).ShouldBeTrue();
        reduced.ShouldBe(TrimBounds.FromEdges(1132, 780, 2820, 2125));
    }

    [Theory]
    [MemberData(nameof(Handles))]
    public void Pressing_without_moving_changes_nothing_on_every_layout(CropHandle handle)
    {
        foreach ((CropSurfaceLayout layout, TrimBounds start) in new[]
                 {
                     (Fitted, Automatic), (Magnified, Automatic),
                     (Letterboxed, TrimBounds.FromEdges(100, 100, 2900, 3900)),
                     (Fitted, TrimBounds.FromEdges(0, 0, 4000, 3000)), (Fitted, TrimBounds.FromEdges(1999, 1499, 2000, 1500)),
                 })
        {
            CropHandleGesture.TryDrag(layout, start, handle, 0, 0, out TrimBounds moved).ShouldBeTrue();
            moved.ShouldBe(start);
        }
    }

    [Theory]
    [MemberData(nameof(Handles))]
    public void Only_the_handles_own_edges_move_and_the_others_are_copied_exactly(CropHandle handle)
    {
        CropHandleGesture.TryDrag(Magnified, Automatic, handle, 10, 10, out TrimBounds moved).ShouldBeTrue();

        (moved.Left != Automatic.Left).ShouldBe(CropHandleGesture.MovesLeft(handle));
        (moved.RightExclusive != Automatic.RightExclusive).ShouldBe(CropHandleGesture.MovesRight(handle));
        (moved.Top != Automatic.Top).ShouldBe(CropHandleGesture.MovesTop(handle));
        (moved.BottomExclusive != Automatic.BottomExclusive).ShouldBe(CropHandleGesture.MovesBottom(handle));
    }

    [Fact]
    public void Magnified_and_letterboxed_moves_use_the_existing_mapping_and_outward_rounding()
    {
        // 200%: 10 DIP is 9.765625 source px; near edges floor, far edges ceil.
        CropHandleGesture.TryDrag(Magnified, Automatic, CropHandle.TopLeft, 10, 10, out TrimBounds tl).ShouldBeTrue();
        tl.ShouldBe(TrimBounds.FromEdges(1189, 789, 2820, 2220));
        CropHandleGesture.TryDrag(Magnified, Automatic, CropHandle.Right, 10, 0, out TrimBounds right).ShouldBeTrue();
        right.RightExclusive.ShouldBe(2830);

        // Letterbox: the right edge 2900 is drawn at 212 + 580 = 792 DIP; 10 DIP more is 50 source px.
        TrimBounds portrait = TrimBounds.FromEdges(100, 100, 2900, 3900);
        CropHandleGesture.TryDrag(Letterboxed, portrait, CropHandle.Right, 10, 0, out TrimBounds wider).ShouldBeTrue();
        wider.ShouldBe(TrimBounds.FromEdges(100, 100, 2950, 3900));
        CropHandleGesture.TryDrag(Letterboxed, portrait, CropHandle.Right, 50, 0, out TrimBounds outside).ShouldBeFalse(
            "past the picture into the letterbox is refused, never clamped");
        outside.ShouldBe(portrait);
    }

    [Fact]
    public void Outside_zero_area_and_crossing_positions_are_refused_and_keep_the_previous_boundary()
    {
        CropHandleGesture.TryDrag(Fitted, Automatic, CropHandle.Left, -400, 0, out TrimBounds a).ShouldBeFalse("outside");
        a.ShouldBe(Automatic);
        CropHandleGesture.TryDrag(Fitted, Automatic, CropHandle.Left, 721.92 - 302.08, 0, out TrimBounds b).ShouldBeFalse("zero width");
        b.ShouldBe(Automatic);
        CropHandleGesture.TryDrag(Fitted, Automatic, CropHandle.Left, 500, 0, out TrimBounds c).ShouldBeFalse("crossing never flips");
        c.ShouldBe(Automatic);
        CropHandleGesture.TryDrag(Fitted, Automatic, CropHandle.Bottom, 0, -400, out TrimBounds d).ShouldBeFalse("crossing");
        d.ShouldBe(Automatic);
    }

    [Fact]
    public void A_corner_is_all_or_nothing()
    {
        CropHandleGesture.TryDrag(Fitted, Automatic, CropHandle.TopLeft, -5, -300, out TrimBounds moved).ShouldBeFalse(
            "a valid horizontal move does not survive an invalid vertical one");
        moved.ShouldBe(Automatic);
    }

    [Fact]
    public void A_one_pixel_boundary_is_legal_as_it_is_today()
    {
        // 0.1 DIP short of the right edge is 0.39 source px: outward rounding keeps one pixel.
        CropHandleGesture.TryDrag(Fitted, Automatic, CropHandle.Left, 721.92 - 302.08 - 0.1, 0, out TrimBounds sliver).ShouldBeTrue();
        sliver.ShouldBe(TrimBounds.FromEdges(2819, 780, 2820, 2220));
        CropHandleGesture.TryNudge(sliver, CropHandle.Left, 1, 0, 4000, 3000, out TrimBounds none).ShouldBeFalse();
        none.ShouldBe(sliver);
    }

    [Fact]
    public void Keyboard_nudges_move_whole_source_pixels_and_are_validated_the_same_way()
    {
        CropHandleGesture.TryNudge(Automatic, CropHandle.Left, -1, 0, 4000, 3000, out TrimBounds one).ShouldBeTrue();
        one.ShouldBe(TrimBounds.FromEdges(1179, 780, 2820, 2220));
        CropHandleGesture.TryNudge(Automatic, CropHandle.BottomRight, 10, 10, 4000, 3000, out TrimBounds ten).ShouldBeTrue();
        ten.ShouldBe(TrimBounds.FromEdges(1180, 780, 2830, 2230));
        CropHandleGesture.TryNudge(Automatic, CropHandle.Left, 0, -1, 4000, 3000, out TrimBounds sideways).ShouldBeTrue();
        sideways.ShouldBe(Automatic, "an arrow along an axis the handle does not move changes nothing");

        TrimBounds atBorder = TrimBounds.FromEdges(0, 0, 4000, 3000);
        CropHandleGesture.TryNudge(atBorder, CropHandle.Left, -1, 0, 4000, 3000, out TrimBounds outside).ShouldBeFalse();
        outside.ShouldBe(atBorder);
        CropHandleGesture.TryNudge(atBorder, CropHandle.Right, 1, 0, 4000, 3000, out _).ShouldBeFalse();
    }

    [Fact]
    public void Ctrl_arrow_puts_an_edge_exactly_on_the_picture_border()
    {
        CropHandleGesture.TrySnapToBorder(Automatic, CropHandle.Left, -1, 0, 4000, 3000, out TrimBounds left).ShouldBeTrue();
        left.ShouldBe(TrimBounds.FromEdges(0, 780, 2820, 2220));
        CropHandleGesture.TrySnapToBorder(Automatic, CropHandle.BottomRight, 1, 0, 4000, 3000, out TrimBounds right).ShouldBeTrue();
        right.ShouldBe(TrimBounds.FromEdges(1180, 780, 4000, 2220), "only the arrow's axis moves");
        CropHandleGesture.TrySnapToBorder(Automatic, CropHandle.Top, 0, -1, 4000, 3000, out TrimBounds top).ShouldBeTrue();
        top.Top.ShouldBe(0);
        CropHandleGesture.TrySnapToBorder(Automatic, CropHandle.Left, 1, 0, 4000, 3000, out TrimBounds crossing).ShouldBeFalse();
        crossing.ShouldBe(Automatic);
    }

    [Fact]
    public void Handles_are_anchored_inside_and_the_nearest_centre_wins_with_corners_winning_ties()
    {
        CropHandleGesture.Centre(0, 0, 200, 100, CropHandle.TopLeft).ShouldBe((5d, 5d));
        CropHandleGesture.Centre(0, 0, 200, 100, CropHandle.BottomRight).ShouldBe((195d, 95d));
        CropHandleGesture.Centre(0, 0, 200, 100, CropHandle.Right).ShouldBe((195d, 50d));

        CropHandleGesture.HitTest(100, 100, 200, 100, 105, 105).ShouldBe(CropHandle.TopLeft);
        CropHandleGesture.HitTest(100, 100, 200, 100, 104, 150).ShouldBe(CropHandle.Left);
        CropHandleGesture.HitTest(100, 100, 200, 100, 200, 150).ShouldBeNull("the middle of the boundary is not a handle");

        // An 8×8 boundary: every handle overlaps. Nearest centre first, a corner on a tie.
        CropHandleGesture.HitTest(0, 0, 8, 8, 5, 5).ShouldBe(CropHandle.TopLeft);
        CropHandleGesture.HitTest(0, 0, 8, 8, 3, 3).ShouldBe(CropHandle.BottomRight);
        CropHandleGesture.HitTest(0, 0, 8, 8, 4, 5).ShouldBe(CropHandle.Top);
        CropHandleGesture.HitTest(0, 0, 8, 8, 5, 4.5).ShouldBe(CropHandle.TopLeft, "equidistant from Left and TopLeft");
    }

    [Fact]
    public void Given_the_surface_size_a_handle_narrower_boundary_keeps_every_handle_whole_inside_it()
    {
        // A 3-DIP-wide boundary against the right border of a 98 × 100 surface: the inside-anchored
        // left handle would centre at 100, past the border; it is kept at 93 (half a handle inside).
        CropHandleGesture.Centre(95, 0, 3, 100, CropHandle.Left, 98, 100).ShouldBe((93d, 50d));
        CropHandleGesture.Centre(95, 0, 3, 100, CropHandle.TopLeft, 98, 100).ShouldBe((93d, 5d));
        foreach (CropHandle handle in Enum.GetValues<CropHandle>())
        {
            (double cx, double cy) = CropHandleGesture.Centre(95, 0, 3, 100, handle, 98, 100);
            (cx - (CropHandleGesture.HandleSize / 2)).ShouldBeGreaterThanOrEqualTo(0);
            (cx + (CropHandleGesture.HandleSize / 2)).ShouldBeLessThanOrEqualTo(98);
            (cy - (CropHandleGesture.HandleSize / 2)).ShouldBeGreaterThanOrEqualTo(0);
            (cy + (CropHandleGesture.HandleSize / 2)).ShouldBeLessThanOrEqualTo(100);
        }

        // The press is hit-tested against the same clamped centres that are drawn: the drawn
        // left handle at (93, 50) is hit, and the place past the border where the unclamped left
        // handle would have been is not a handle at all.
        CropHandleGesture.HitTest(95, 0, 3, 100, 93, 50, 98, 100).ShouldNotBeNull();
        CropHandleGesture.HitTest(95, 0, 3, 100, 100, 50, 98, 100).ShouldBeNull();
        CropHandleGesture.HitTest(95, 0, 3, 100, 100, 50).ShouldBe(CropHandle.Left, "unclamped, it would sit past the border");
    }

    public static TheoryData<CropHandle> Handles()
    {
        TheoryData<CropHandle> data = [];
        foreach (CropHandle handle in Enum.GetValues<CropHandle>()) data.Add(handle);
        return data;
    }
}
