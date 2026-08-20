using System.Globalization;

namespace PrintFlow.Domain.Trimming;

/// <summary>
/// A rectangle of pixels inside an image, in the image's own pixel coordinates.
/// </summary>
/// <remarks>
/// <b>Convention, stated once:</b> <see cref="Left"/> and <see cref="Top"/> are inclusive,
/// <see cref="RightExclusive"/> and <see cref="BottomExclusive"/> are exclusive — the same
/// half-open convention as <c>Int32Rect(x, y, width, height)</c>. So a single pixel at
/// (3,&#160;4) is <c>FromEdges(3, 4, 4, 5)</c>, and <see cref="Width"/> is a plain
/// subtraction with no off-by-one correction anywhere. An inclusive right/bottom was
/// rejected precisely because it needs that correction at every call site, and one missed
/// <c>+1</c> silently clips a column of the operator's artwork.
///
/// Pure geometry: this type never touches a file, a decoder or a pixel buffer.
/// </remarks>
public readonly record struct TrimBounds
{
    private TrimBounds(int left, int top, int rightExclusive, int bottomExclusive)
    {
        Left = left;
        Top = top;
        RightExclusive = rightExclusive;
        BottomExclusive = bottomExclusive;
    }

    /// <summary>Inclusive left edge, in pixels from the left of the image.</summary>
    public int Left { get; }

    /// <summary>Inclusive top edge, in pixels from the top of the image.</summary>
    public int Top { get; }

    /// <summary>Exclusive right edge: the first column <i>outside</i> the rectangle.</summary>
    public int RightExclusive { get; }

    /// <summary>Exclusive bottom edge: the first row <i>outside</i> the rectangle.</summary>
    public int BottomExclusive { get; }

    /// <summary>Width in pixels.</summary>
    public int Width => RightExclusive - Left;

    /// <summary>Height in pixels.</summary>
    public int Height => BottomExclusive - Top;

    /// <summary>
    /// True only for <c>default</c>. Every rectangle the factories produce contains at least
    /// one pixel, so "empty" means "never given a value", not "a legitimate zero-area crop".
    /// </summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>Creates a rectangle from its inclusive top-left and exclusive bottom-right edges.</summary>
    public static TrimBounds FromEdges(int left, int top, int rightExclusive, int bottomExclusive)
    {
        if (left < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(left), left, "Pixel coordinates are not negative.");
        }

        if (top < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(top), top, "Pixel coordinates are not negative.");
        }

        if (rightExclusive <= left)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rightExclusive), rightExclusive,
                "The exclusive right edge must be past the left edge; a trim rectangle contains at least one pixel.");
        }

        if (bottomExclusive <= top)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bottomExclusive), bottomExclusive,
                "The exclusive bottom edge must be past the top edge; a trim rectangle contains at least one pixel.");
        }

        return new TrimBounds(left, top, rightExclusive, bottomExclusive);
    }

    /// <summary>Creates a rectangle from its origin and size, the <c>Int32Rect</c> spelling.</summary>
    public static TrimBounds FromSize(int x, int y, int width, int height)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "A trim rectangle has positive width.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "A trim rectangle has positive height.");
        }

        return FromEdges(x, y, checked(x + width), checked(y + height));
    }

    /// <summary>The rectangle covering an entire <paramref name="width"/> × <paramref name="height"/> canvas.</summary>
    public static TrimBounds Canvas(int width, int height) => FromSize(0, 0, width, height);

    /// <summary>True when this rectangle is exactly the whole canvas — the <see cref="TrimOutcome.NoChangeRequired"/> test.</summary>
    public bool CoversCanvas(int canvasWidth, int canvasHeight) =>
        Left == 0 && Top == 0 && RightExclusive == canvasWidth && BottomExclusive == canvasHeight;

    /// <summary>True when every pixel of this rectangle is inside the given canvas.</summary>
    public bool FitsWithin(int canvasWidth, int canvasHeight) =>
        Left >= 0 && Top >= 0 && RightExclusive <= canvasWidth && BottomExclusive <= canvasHeight;

    /// <summary>
    /// Grows this rectangle by <paramref name="margin"/>, clamped to the canvas.
    /// </summary>
    /// <remarks>
    /// The clamp is the whole point: a 10&#160;px left margin on content starting at column 5
    /// yields left&#160;=&#160;0, never −5. Margins can therefore never produce a coordinate
    /// outside the source image, so no caller has to re-check the result before cropping
    /// (Epic 11200 Part B §8). Arithmetic widens to <see cref="long"/> so an absurdly large
    /// margin clamps to the canvas instead of overflowing past it.
    /// </remarks>
    public TrimBounds Expand(TrimMargin margin, int canvasWidth, int canvasHeight)
    {
        if (canvasWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(canvasWidth), canvasWidth, "A canvas has positive width.");
        }

        if (canvasHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(canvasHeight), canvasHeight, "A canvas has positive height.");
        }

        if (!FitsWithin(canvasWidth, canvasHeight))
        {
            throw new ArgumentException(
                $"{this} does not fit inside a {canvasWidth}×{canvasHeight} canvas.", nameof(canvasWidth));
        }

        int left = (int)Math.Max(0L, Left - (long)margin.Left);
        int top = (int)Math.Max(0L, Top - (long)margin.Top);
        int right = (int)Math.Min(canvasWidth, RightExclusive + (long)margin.Right);
        int bottom = (int)Math.Min(canvasHeight, BottomExclusive + (long)margin.Bottom);

        return new TrimBounds(left, top, right, bottom);
    }

    public override string ToString() => string.Format(
        CultureInfo.InvariantCulture, "[{0},{1} → {2},{3}) {4}×{5}",
        Left, Top, RightExclusive, BottomExclusive, Width, Height);
}
