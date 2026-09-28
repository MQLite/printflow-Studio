using System.Windows.Input;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Trimming;

namespace PrintFlow.Tests.Unit.Ui;

/// <summary>
/// The view's handle-drag state machine and key mapping (SCRUM-11147), without mouse or keyboard
/// devices: press, threshold, move, release and the boundary a cancel returns to.
/// </summary>
public sealed class TrimHandleDragTests
{
    private static readonly TrimBounds Start = TrimBounds.FromEdges(1180, 780, 2820, 2220);

    [Fact]
    public void A_press_and_release_without_moving_past_the_threshold_moves_nothing()
    {
        TrimHandleDrag drag = new(CropHandle.Left, 300, 400, Start, thresholdX: 4, thresholdY: 4);

        drag.Displacement(300, 400).ShouldBeNull();
        drag.Displacement(303, 397).ShouldBeNull("inside the system drag threshold is still a press");
        drag.IsMoving.ShouldBeFalse();
        drag.Start.ShouldBe(Start, "a lost capture or Esc returns to exactly this boundary");
    }

    [Fact]
    public void Past_the_threshold_the_whole_displacement_from_the_press_applies_and_keeps_applying()
    {
        TrimHandleDrag drag = new(CropHandle.TopLeft, 300, 400, Start, thresholdX: 4, thresholdY: 4);

        drag.Displacement(295, 400).ShouldBe((-5d, 0d), "measured from the press, not from the threshold");
        drag.IsMoving.ShouldBeTrue();
        drag.Displacement(301, 401).ShouldBe((1d, 1d), "once moving, small displacements count too");
        drag.Displacement(300, 400).ShouldBe((0d, 0d));
    }

    [Fact]
    public void A_drag_through_the_view_model_moves_by_displacement_and_a_cancel_restores_the_start()
    {
        CropSurfaceLayout fitted = new(1024, 800, 2048, 1536, 4000, 3000, IsFitToViewport: true, ZoomScale: 1);
        // Grabbed 4 DIP inside the left edge (302.08), dragged 12.08 DIP left: the edge moves to
        // 290 DIP (source 1132), not to where the pointer ends up.
        TrimHandleDrag drag = new(CropHandle.Left, 306.08, 500, Start, 4, 4);
        (double dx, double dy) = drag.Displacement(294, 500)!.Value;

        CropHandleGesture.TryDrag(fitted, drag.Start, drag.Handle, dx, dy, out TrimBounds moved).ShouldBeTrue();
        moved.ShouldBe(TrimBounds.FromEdges(1132, 780, 2820, 2220));
    }

    [Theory]
    [InlineData(Key.Left, ModifierKeys.None, -1, 0, false, false)]
    [InlineData(Key.Right, ModifierKeys.Shift, 1, 0, true, false)]
    [InlineData(Key.Up, ModifierKeys.Control, 0, -1, false, true)]
    [InlineData(Key.Down, ModifierKeys.Control | ModifierKeys.Shift, 0, 1, true, true)]
    public void Arrow_keys_map_to_a_direction_step_and_border_snap(
        Key key, ModifierKeys modifiers, int x, int y, bool big, bool border)
    {
        TrimHandleKeys.TryMap(key, modifiers, out int dx, out int dy, out bool bigStep, out bool toBorder).ShouldBeTrue();
        (dx, dy, bigStep, toBorder).ShouldBe((x, y, big, border));
    }

    [Theory]
    [InlineData(Key.Enter)]
    [InlineData(Key.Space)]
    [InlineData(Key.Tab)]
    [InlineData(Key.Escape)]
    public void Other_keys_are_not_the_handles_and_are_left_alone(Key key) =>
        TrimHandleKeys.TryMap(key, ModifierKeys.None, out _, out _, out _, out _).ShouldBeFalse(
            "Enter and Space never submit from a handle; Tab keeps moving focus");
}
