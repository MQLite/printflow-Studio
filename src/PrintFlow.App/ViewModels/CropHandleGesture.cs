using PrintFlow.Domain.Trimming;

namespace PrintFlow.App.ViewModels;

/// <summary>One of the eight handles on a trim boundary (SCRUM-11147).</summary>
public enum CropHandle
{
    Left,
    Top,
    Right,
    Bottom,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

/// <summary>
/// What an edge or corner handle does to a source-pixel boundary (SCRUM-11147).
/// </summary>
/// <remarks>
/// Pure arithmetic beside <see cref="CropSurfaceLayout"/>, never instead of it: a pointer drag is
/// turned into a source rectangle by the layout's own <see cref="CropSurfaceLayout.TryToSourceBounds"/>,
/// so mapping, outward rounding, edge tolerance and the refusal of anything outside the picture
/// are exactly what drawing a rectangle already uses. What this adds is only what a handle means:
/// which edges move, that they move by the pointer's <i>displacement</i> rather than jumping to
/// the pointer, that an edge the operator did not touch is copied exactly, and that a handle
/// never silently flips the rectangle. A refused position returns false and changes nothing; the
/// caller keeps the boundary it had.
/// </remarks>
public static class CropHandleGesture
{
    /// <summary>The drawn size of a handle, in device-independent pixels.</summary>
    public const double HandleSize = 10;

    public static bool MovesLeft(CropHandle handle) => handle is CropHandle.Left or CropHandle.TopLeft or CropHandle.BottomLeft;

    public static bool MovesRight(CropHandle handle) => handle is CropHandle.Right or CropHandle.TopRight or CropHandle.BottomRight;

    public static bool MovesTop(CropHandle handle) => handle is CropHandle.Top or CropHandle.TopLeft or CropHandle.TopRight;

    public static bool MovesBottom(CropHandle handle) => handle is CropHandle.Bottom or CropHandle.BottomLeft or CropHandle.BottomRight;

    public static bool IsCorner(CropHandle handle) => handle >= CropHandle.TopLeft;

    /// <summary>
    /// Moves the handle's edges by a pointer displacement measured on the crop surface.
    /// </summary>
    /// <param name="layout">The surface the drag happened on.</param>
    /// <param name="start">The boundary when the handle was pressed.</param>
    /// <param name="handle">The handle being dragged.</param>
    /// <param name="deltaX">Pointer movement since the press, in surface units.</param>
    /// <param name="deltaY">Pointer movement since the press, in surface units.</param>
    /// <param name="moved">The new boundary, or <paramref name="start"/> when refused.</param>
    /// <returns>
    /// False when the position is outside the picture, has no area, or would carry an edge onto or
    /// past its opposite edge. A corner is refused whole when either of its axes is invalid.
    /// </returns>
    public static bool TryDrag(
        CropSurfaceLayout layout, TrimBounds start, CropHandle handle, double deltaX, double deltaY, out TrimBounds moved)
    {
        moved = start;
        if (start.IsEmpty || !double.IsFinite(deltaX) || !double.IsFinite(deltaY) ||
            !layout.TryToSurfaceRect(start, out double x, out double y, out double width, out double height))
        {
            return false;
        }

        double left = x, top = y, right = x + width, bottom = y + height;
        if (MovesLeft(handle)) left += deltaX;
        if (MovesRight(handle)) right += deltaX;
        if (MovesTop(handle)) top += deltaY;
        if (MovesBottom(handle)) bottom += deltaY;

        // Crossing is refused before normalisation could turn it into a flipped rectangle.
        if (left >= right || top >= bottom)
        {
            return false;
        }

        if (!layout.TryToSourceBounds(left, top, right, bottom, out TrimBounds mapped))
        {
            return false;
        }

        return TryCompose(start, handle, mapped.Left, mapped.Top, mapped.RightExclusive, mapped.BottomExclusive, out moved);
    }

    /// <summary>
    /// Moves the handle's edges by whole source pixels (the keyboard nudge).
    /// </summary>
    /// <remarks>
    /// An arrow along an axis the handle does not move (Up on the left edge) is not an error and
    /// changes nothing. Everything else is validated exactly like a drag: inside the picture,
    /// at least one pixel, no crossing.
    /// </remarks>
    public static bool TryNudge(
        TrimBounds start, CropHandle handle, int deltaX, int deltaY, int canvasWidth, int canvasHeight, out TrimBounds moved)
    {
        moved = start;
        long left = start.Left, top = start.Top, right = start.RightExclusive, bottom = start.BottomExclusive;
        if (MovesLeft(handle)) left += deltaX;
        if (MovesRight(handle)) right += deltaX;
        if (MovesTop(handle)) top += deltaY;
        if (MovesBottom(handle)) bottom += deltaY;
        return TryInCanvas(start, handle, left, top, right, bottom, canvasWidth, canvasHeight, out moved);
    }

    /// <summary>
    /// Moves the handle's edges to the picture border in the arrow's direction (Ctrl+arrow).
    /// </summary>
    /// <param name="directionX">-1 for Left, +1 for Right, 0 for neither.</param>
    /// <param name="directionY">-1 for Up, +1 for Down, 0 for neither.</param>
    public static bool TrySnapToBorder(
        TrimBounds start, CropHandle handle, int directionX, int directionY, int canvasWidth, int canvasHeight, out TrimBounds moved)
    {
        moved = start;
        long left = start.Left, top = start.Top, right = start.RightExclusive, bottom = start.BottomExclusive;
        long borderX = directionX < 0 ? 0 : canvasWidth;
        long borderY = directionY < 0 ? 0 : canvasHeight;
        if (directionX != 0 && MovesLeft(handle)) left = borderX;
        if (directionX != 0 && MovesRight(handle)) right = borderX;
        if (directionY != 0 && MovesTop(handle)) top = borderY;
        if (directionY != 0 && MovesBottom(handle)) bottom = borderY;
        return TryInCanvas(start, handle, left, top, right, bottom, canvasWidth, canvasHeight, out moved);
    }

    /// <summary>
    /// Where a handle is drawn: centred on its edge or corner, but anchored <i>inside</i> the
    /// boundary so it is never clipped when the boundary lies on the picture border. With a
    /// surface size, the centre is also kept far enough inside the surface for the whole handle
    /// to show, which matters only on a boundary narrower than a handle.
    /// </summary>
    public static (double X, double Y) Centre(double x, double y, double width, double height, CropHandle handle,
        double surfaceWidth = double.PositiveInfinity, double surfaceHeight = double.PositiveInfinity)
    {
        (double cx, double cy) = Anchored(x, y, width, height, handle);
        const double half = HandleSize / 2;
        if (double.IsFinite(surfaceWidth)) cx = Math.Clamp(cx, half, Math.Max(half, surfaceWidth - half));
        if (double.IsFinite(surfaceHeight)) cy = Math.Clamp(cy, half, Math.Max(half, surfaceHeight - half));
        return (cx, cy);
    }

    private static (double X, double Y) Anchored(double x, double y, double width, double height, CropHandle handle)
    {
        const double half = HandleSize / 2;
        double left = x + half, right = x + width - half, top = y + half, bottom = y + height - half;
        double middleX = x + (width / 2), middleY = y + (height / 2);
        return handle switch
        {
            CropHandle.Left => (left, middleY),
            CropHandle.Right => (right, middleY),
            CropHandle.Top => (middleX, top),
            CropHandle.Bottom => (middleX, bottom),
            CropHandle.TopLeft => (left, top),
            CropHandle.TopRight => (right, top),
            CropHandle.BottomLeft => (left, bottom),
            _ => (right, bottom),
        };
    }

    /// <summary>
    /// The handle a press lands on, or null. When handles overlap on a small boundary, the handle
    /// whose centre is nearest wins, and a corner wins a tie.
    /// </summary>
    public static CropHandle? HitTest(double x, double y, double width, double height, double pointX, double pointY,
        double surfaceWidth = double.PositiveInfinity, double surfaceHeight = double.PositiveInfinity)
    {
        const double half = HandleSize / 2;
        CropHandle? best = null;
        double bestDistance = double.MaxValue;
        foreach (CropHandle handle in Enum.GetValues<CropHandle>())
        {
            (double cx, double cy) = Centre(x, y, width, height, handle, surfaceWidth, surfaceHeight);
            if (Math.Abs(pointX - cx) > half || Math.Abs(pointY - cy) > half)
            {
                continue;
            }

            double distance = ((pointX - cx) * (pointX - cx)) + ((pointY - cy) * (pointY - cy));
            if (distance < bestDistance || (distance == bestDistance && IsCorner(handle) && best is { } current && !IsCorner(current)))
            {
                best = handle;
                bestDistance = distance;
            }
        }

        return best;
    }

    private static bool TryInCanvas(
        TrimBounds start, CropHandle handle, long left, long top, long right, long bottom,
        int canvasWidth, int canvasHeight, out TrimBounds moved)
    {
        moved = start;
        if (left < 0 || top < 0 || right > canvasWidth || bottom > canvasHeight)
        {
            return false;
        }

        return TryCompose(start, handle, (int)left, (int)top, (int)right, (int)bottom, out moved);
    }

    /// <summary>
    /// Takes only the handle's own edges from the candidate and copies every other edge exactly
    /// from the pre-gesture boundary; refuses anything empty.
    /// </summary>
    private static bool TryCompose(
        TrimBounds start, CropHandle handle, int left, int top, int right, int bottom, out TrimBounds moved)
    {
        moved = start;
        int l = MovesLeft(handle) ? left : start.Left;
        int t = MovesTop(handle) ? top : start.Top;
        int r = MovesRight(handle) ? right : start.RightExclusive;
        int b = MovesBottom(handle) ? bottom : start.BottomExclusive;
        if (r <= l || b <= t)
        {
            return false;
        }

        moved = TrimBounds.FromEdges(l, t, r, b);
        return true;
    }
}
