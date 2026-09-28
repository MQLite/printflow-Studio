using System.Windows.Input;
using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Trimming;

namespace PrintFlow.App.Views;

/// <summary>
/// One handle drag on the trim adjustment surface, from press to release (SCRUM-11147).
/// </summary>
/// <remarks>
/// The view's only state machine, kept apart from the mouse events so it can be exercised
/// without them. It remembers the boundary the drag began with — what a refused position, a
/// lost capture or Esc goes back to — and turns pointer positions into a displacement from the
/// press, never an absolute position, so grabbing a handle anywhere inside its square does not
/// jump the edge. Below the system drag threshold nothing moves at all; once the threshold has
/// been crossed every later position counts, including small ones.
/// </remarks>
internal sealed class TrimHandleDrag(CropHandle handle, double pressX, double pressY, TrimBounds start,
    double thresholdX, double thresholdY)
{
    public CropHandle Handle { get; } = handle;

    /// <summary>The boundary when the handle was pressed.</summary>
    public TrimBounds Start { get; } = start;

    /// <summary>Whether the pointer has moved past the drag threshold since the press.</summary>
    public bool IsMoving { get; private set; }

    /// <summary>
    /// The displacement to apply for a pointer position, or null while it is still a press.
    /// </summary>
    public (double DeltaX, double DeltaY)? Displacement(double x, double y)
    {
        double dx = x - pressX;
        double dy = y - pressY;
        if (!IsMoving && Math.Abs(dx) < thresholdX && Math.Abs(dy) < thresholdY)
        {
            return null;
        }

        IsMoving = true;
        return (dx, dy);
    }
}

/// <summary>What a key does to a focused trim handle (SCRUM-11147).</summary>
internal static class TrimHandleKeys
{
    /// <summary>
    /// Arrow keys move one source pixel; Shift moves ten; Ctrl moves to the picture border.
    /// Any other key is not the handle's.
    /// </summary>
    public static bool TryMap(Key key, ModifierKeys modifiers, out int directionX, out int directionY, out bool bigStep, out bool toBorder)
    {
        (directionX, directionY) = key switch
        {
            Key.Left => (-1, 0),
            Key.Right => (1, 0),
            Key.Up => (0, -1),
            Key.Down => (0, 1),
            _ => (0, 0),
        };
        bigStep = modifiers.HasFlag(ModifierKeys.Shift);
        toBorder = modifiers.HasFlag(ModifierKeys.Control);
        return directionX != 0 || directionY != 0;
    }
}
