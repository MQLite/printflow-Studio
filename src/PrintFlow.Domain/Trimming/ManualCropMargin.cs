namespace PrintFlow.Domain.Trimming;

/// <summary>Explicit outward expansion of an operator-selected rectangle, independent of automatic trim settings.</summary>
public readonly record struct ManualCropMargin
{
    private ManualCropMargin(TrimMode mode, int top, int right, int bottom, int left)
    {
        if (top < 0 || right < 0 || bottom < 0 || left < 0)
            throw new ArgumentOutOfRangeException(nameof(top), "Manual crop margins must be non-negative.");
        Mode = mode;
        Top = top;
        Right = right;
        Bottom = bottom;
        Left = left;
    }

    public TrimMode Mode { get; }
    public int Top { get; }
    public int Right { get; }
    public int Bottom { get; }
    public int Left { get; }
    public static ManualCropMargin Tight => default;
    public static ManualCropMargin Uniform(int pixels) => new(TrimMode.UniformMargin, pixels, pixels, pixels, pixels);
    public static ManualCropMargin PerEdge(int top, int right, int bottom, int left) =>
        new(TrimMode.EdgeSpecificMargin, top, right, bottom, left);
}
