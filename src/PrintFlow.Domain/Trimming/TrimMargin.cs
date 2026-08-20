using System.Globalization;

namespace PrintFlow.Domain.Trimming;

/// <summary>
/// The pixel margin a trim keeps outside the alpha content, per edge, together with the
/// <see cref="TrimMode"/> that was asked for.
/// </summary>
/// <remarks>
/// Margins only ever <b>expand</b> the crop rectangle. A negative value is refused rather
/// than read as "crop further in": cropping into the operator's artwork is a destructive
/// operation that must be asked for explicitly, never expressed as the sign of a number
/// (Epic 11200 Part B §5).
///
/// Mode and values are set together by the factories, so an <see cref="TrimMode.EdgeSpecificMargin"/>
/// carrying four equal numbers is possible (the operator typed them) but a
/// <see cref="TrimMode.TightCrop"/> carrying a non-zero margin is not constructible at all.
/// <c>default</c> is <see cref="Tight"/>.
/// </remarks>
public readonly record struct TrimMargin
{
    private TrimMargin(TrimMode mode, int top, int right, int bottom, int left)
    {
        Mode = mode;
        Top = top;
        Right = right;
        Bottom = bottom;
        Left = left;
    }

    /// <summary>What the operator asked for; see <see cref="TrimMode"/>.</summary>
    public TrimMode Mode { get; }

    /// <summary>Pixels kept above the content.</summary>
    public int Top { get; }

    /// <summary>Pixels kept to the right of the content.</summary>
    public int Right { get; }

    /// <summary>Pixels kept below the content.</summary>
    public int Bottom { get; }

    /// <summary>Pixels kept to the left of the content.</summary>
    public int Left { get; }

    /// <summary>Crop exactly to the alpha content. Also the value of <c>default</c>.</summary>
    public static TrimMargin Tight => new(TrimMode.TightCrop, 0, 0, 0, 0);

    /// <summary>True when no edge keeps any margin, whatever mode was asked for.</summary>
    public bool IsNone => Top == 0 && Right == 0 && Bottom == 0 && Left == 0;

    /// <summary>The same non-negative margin on all four edges.</summary>
    public static TrimMargin Uniform(int pixels)
    {
        Require(pixels, nameof(pixels));
        return new TrimMargin(TrimMode.UniformMargin, pixels, pixels, pixels, pixels);
    }

    /// <summary>An independently chosen non-negative margin per edge.</summary>
    public static TrimMargin PerEdge(int top, int right, int bottom, int left)
    {
        Require(top, nameof(top));
        Require(right, nameof(right));
        Require(bottom, nameof(bottom));
        Require(left, nameof(left));
        return new TrimMargin(TrimMode.EdgeSpecificMargin, top, right, bottom, left);
    }

    private static void Require(int pixels, string parameterName)
    {
        if (pixels < 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName, pixels,
                "A trim margin is non-negative: margins expand the crop, they never crop further in.");
        }
    }

    public override string ToString() => string.Format(
        CultureInfo.InvariantCulture, "{0}(top {1}, right {2}, bottom {3}, left {4} px)",
        Mode, Top, Right, Bottom, Left);
}
