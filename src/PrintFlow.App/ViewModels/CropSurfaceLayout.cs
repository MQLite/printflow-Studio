using PrintFlow.Domain.Trimming;

namespace PrintFlow.App.ViewModels;

/// <summary>
/// Where a preview is drawn on its crop surface, and therefore how a drag maps onto source
/// pixels (Epic 11200 Part C2 §7, §32).
/// </summary>
/// <remarks>
/// Pure arithmetic over six numbers. It opens no file, holds no bitmap, names no WPF type and
/// changes nothing — which is why it is a separate type rather than code behind a view. Every
/// case §32 lists (fitted, magnified, a non-square viewport, a letterboxed image, both
/// boundaries) is a value in and a value out, assertable with no mouse, no window and no STA
/// thread. Driving that geometry through synthesised mouse events would be slower, more
/// brittle, and would prove less.
/// <para>
/// <b>Three coordinate spaces, and confusing any two of them clips the operator's artwork.</b>
/// </para>
/// <list type="number">
///   <item><b>Surface</b> — device-independent pixels measured from the top-left of the crop
///         surface, which is the scrollable <i>content</i> area the image is centred in. What a
///         mouse event reports.</item>
///   <item><b>Payload</b> — pixels of the decoded preview image. This is <i>not</i> the source:
///         a preview larger than <c>IImagePreviewDecoder.MaximumDisplayEdge</c> is reduced for
///         display, so at 100% zoom one payload pixel is one screen pixel while standing for
///         several source pixels (Part C1 §6).</item>
///   <item><b>Source</b> — pixels of the artefact itself. The only space a crop may be recorded
///         in, because it is the only one that does not change when the window does
///         (Part C2 §6).</item>
/// </list>
/// <para>
/// <b>Why there is no scroll term.</b> The surface is the scrolled content, not the visible
/// window onto it, so a scrolled-away part of the image is at the same surface coordinate it
/// was before the operator scrolled. Scrolling is therefore accounted for by construction
/// rather than by an offset this type would have to be told about and could be told wrongly.
/// The remaining three things §7 names — fit-to-viewport scaling, the preview's own display
/// reduction, and the current zoom — are <see cref="DisplayScale"/> and
/// <see cref="SourcePerPayloadX"/>; the fourth, letterboxing, is <see cref="LetterboxX"/>.
/// </para>
/// </remarks>
/// <param name="SurfaceWidth">Width of the crop surface, in device-independent pixels.</param>
/// <param name="SurfaceHeight">Height of the crop surface, in device-independent pixels.</param>
/// <param name="PayloadPixelWidth">Width of the decoded preview image.</param>
/// <param name="PayloadPixelHeight">Height of the decoded preview image.</param>
/// <param name="SourcePixelWidth">Width of the artefact the crop will be applied to.</param>
/// <param name="SourcePixelHeight">Height of the artefact the crop will be applied to.</param>
/// <param name="IsFitToViewport">Whether the whole image is scaled to fit rather than magnified.</param>
/// <param name="ZoomScale">The magnification applied when not fitted; ignored when it is.</param>
public readonly record struct CropSurfaceLayout(
    double SurfaceWidth,
    double SurfaceHeight,
    int PayloadPixelWidth,
    int PayloadPixelHeight,
    int SourcePixelWidth,
    int SourcePixelHeight,
    bool IsFitToViewport,
    double ZoomScale)
{
    /// <summary>
    /// How close to a pixel boundary an edge must be, in source pixels, to be treated as on it.
    /// </summary>
    /// <remarks>
    /// Outward rounding and floating-point arithmetic do not mix without this. An edge the
    /// operator put exactly on a boundary arrives here as 9.000000000001 after being scaled to
    /// the screen and back, and a bare <c>Ceiling</c> turns that into 10 — a whole extra column
    /// of artwork the operator did not select, on a rectangle that looked exactly right. The
    /// tolerance is a millionth of a pixel: far below anything a mouse can express, and far
    /// above the error the conversion accumulates.
    /// </remarks>
    private const double EdgeTolerance = 1e-6;

    /// <summary>
    /// False when the numbers cannot describe a real surface — a zero-sized area, an image with
    /// no pixels, a non-positive zoom.
    /// </summary>
    /// <remarks>
    /// Asked first by every conversion below rather than trusted. A layout is read from live WPF
    /// measurements, and a control that has not been arranged yet reports zeroes; dividing by
    /// those would produce infinities that then silently became a crop.
    /// </remarks>
    public bool IsUsable =>
        SurfaceWidth > 0 && SurfaceHeight > 0 &&
        PayloadPixelWidth > 0 && PayloadPixelHeight > 0 &&
        SourcePixelWidth > 0 && SourcePixelHeight > 0 &&
        (IsFitToViewport || ZoomScale > 0);

    /// <summary>
    /// Device-independent pixels per payload pixel, as WPF is actually drawing the image.
    /// </summary>
    /// <remarks>
    /// Fitted, this is what <c>Stretch="Uniform"</c> does: the smaller of the two ratios, so the
    /// whole image is inside the surface with one axis exactly filled. Magnified, it is the zoom
    /// itself, because the preview payload is encoded at 96&#160;dpi and drawn with
    /// <c>Stretch="None"</c> under a <c>ScaleTransform</c> — which is what makes "100%" mean one
    /// payload pixel per device-independent pixel (Part C1 §12).
    /// </remarks>
    public double DisplayScale => IsFitToViewport
        ? Math.Min(SurfaceWidth / PayloadPixelWidth, SurfaceHeight / PayloadPixelHeight)
        : ZoomScale;

    /// <summary>The drawn width of the image, in device-independent pixels.</summary>
    public double DisplayWidth => PayloadPixelWidth * DisplayScale;

    /// <summary>The drawn height of the image, in device-independent pixels.</summary>
    public double DisplayHeight => PayloadPixelHeight * DisplayScale;

    /// <summary>
    /// The unused surface space to the left of the image; zero once the image fills it.
    /// </summary>
    /// <remarks>
    /// The image is centred, so a fitted portrait image on a landscape surface has empty space
    /// on both sides. Ignoring it would shift every crop left by exactly this much — the single
    /// most likely way for a crop surface to be wrong while still looking plausible.
    /// </remarks>
    public double LetterboxX => Math.Max(0, (SurfaceWidth - DisplayWidth) / 2);

    /// <inheritdoc cref="LetterboxX" />
    public double LetterboxY => Math.Max(0, (SurfaceHeight - DisplayHeight) / 2);

    /// <summary>Source pixels per payload pixel: greater than one when the preview was reduced.</summary>
    public double SourcePerPayloadX => (double)SourcePixelWidth / PayloadPixelWidth;

    /// <inheritdoc cref="SourcePerPayloadX" />
    public double SourcePerPayloadY => (double)SourcePixelHeight / PayloadPixelHeight;

    /// <summary>Maps one surface x-coordinate to a (possibly out-of-range) source x-coordinate.</summary>
    public double ToSourceX(double surfaceX) =>
        (surfaceX - LetterboxX) / DisplayScale * SourcePerPayloadX;

    /// <inheritdoc cref="ToSourceX" />
    public double ToSourceY(double surfaceY) =>
        (surfaceY - LetterboxY) / DisplayScale * SourcePerPayloadY;

    /// <summary>Maps one source x-coordinate back to where it is drawn, so a selection can be outlined.</summary>
    public double ToSurfaceX(double sourceX) =>
        (sourceX / SourcePerPayloadX * DisplayScale) + LetterboxX;

    /// <inheritdoc cref="ToSurfaceX" />
    public double ToSurfaceY(double sourceY) =>
        (sourceY / SourcePerPayloadY * DisplayScale) + LetterboxY;

    /// <summary>
    /// Converts a drag between two surface points into a source-pixel rectangle.
    /// </summary>
    /// <remarks>
    /// The corners may arrive in any order — an operator dragging up and to the left is drawing
    /// the same rectangle as one dragging down and to the right — so they are normalised first.
    /// <para>
    /// <b>Clamping, and where it stops.</b> The rectangle is intersected with the canvas
    /// <i>before</i> it is rounded, so a drag that starts outside the image and ends inside it
    /// yields the part that overlaps — which is the part the operator drew over the artwork
    /// (Part C2 §23). A rectangle that misses the canvas entirely has no overlap and is refused
    /// rather than clamped into a sliver at the edge: nothing the operator could see was
    /// selected, so nothing should be cropped.
    /// </para>
    /// <para>
    /// Rounding is outward — <c>floor</c> on the near edges, <c>ceil</c> on the far ones — so
    /// every pixel the rectangle touched survives. Rounding inward would quietly shave a row off
    /// two sides of every crop. <see cref="EdgeTolerance"/> is what keeps outward rounding from
    /// over-reaching on an edge that is a hair's breadth past a pixel boundary.
    /// </para>
    /// </remarks>
    /// <returns>
    /// False when the layout cannot be used, or when the drag selects no pixel at all: a click
    /// that never moved, a zero-height drag, or a rectangle entirely off the image.
    /// </returns>
    public bool TryToSourceBounds(
        double x1, double y1, double x2, double y2, out TrimBounds bounds)
    {
        bounds = default;

        if (!IsUsable)
        {
            return false;
        }

        double sourceLeft = ToSourceX(Math.Min(x1, x2));
        double sourceRight = ToSourceX(Math.Max(x1, x2));
        double sourceTop = ToSourceY(Math.Min(y1, y2));
        double sourceBottom = ToSourceY(Math.Max(y1, y2));

        double left = Math.Max(0, sourceLeft);
        double right = Math.Min(SourcePixelWidth, sourceRight);
        double top = Math.Max(0, sourceTop);
        double bottom = Math.Min(SourcePixelHeight, sourceBottom);

        if (right <= left || bottom <= top)
        {
            return false;
        }

        int leftPixel = Math.Clamp((int)Math.Floor(left + EdgeTolerance), 0, SourcePixelWidth - 1);
        int topPixel = Math.Clamp((int)Math.Floor(top + EdgeTolerance), 0, SourcePixelHeight - 1);
        int rightPixel = Math.Clamp((int)Math.Ceiling(right - EdgeTolerance), leftPixel + 1, SourcePixelWidth);
        int bottomPixel = Math.Clamp((int)Math.Ceiling(bottom - EdgeTolerance), topPixel + 1, SourcePixelHeight);

        bounds = TrimBounds.FromEdges(leftPixel, topPixel, rightPixel, bottomPixel);
        return true;
    }

    /// <summary>
    /// Converts a source-pixel rectangle back to where it should be outlined on the surface.
    /// </summary>
    /// <remarks>
    /// The inverse of <see cref="TryToSourceBounds"/>, and the reason the outline stays over the
    /// same artwork when the operator zooms or the pane is resized: the selection is stored in
    /// source pixels and re-projected each time, rather than remembered as screen coordinates
    /// that would drift the moment anything moved.
    /// </remarks>
    public bool TryToSurfaceRect(
        TrimBounds bounds, out double x, out double y, out double width, out double height)
    {
        x = y = width = height = 0;

        if (!IsUsable || bounds.IsEmpty)
        {
            return false;
        }

        x = ToSurfaceX(bounds.Left);
        y = ToSurfaceY(bounds.Top);
        width = ToSurfaceX(bounds.RightExclusive) - x;
        height = ToSurfaceY(bounds.BottomExclusive) - y;
        return true;
    }
}
