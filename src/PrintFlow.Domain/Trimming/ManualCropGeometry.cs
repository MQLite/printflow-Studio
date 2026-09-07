namespace PrintFlow.Domain.Trimming;

/// <summary>Immutable source-space manual selection and the actual crop, using TrimBounds half-open edges.</summary>
public sealed record ManualCropGeometry
{
    private ManualCropGeometry(TrimBounds selected, TrimBounds applied, ManualCropMargin margin)
    {
        SelectedBounds = selected;
        AppliedBounds = applied;
        Margin = margin;
    }

    public TrimBounds SelectedBounds { get; }
    public TrimBounds AppliedBounds { get; }
    public ManualCropMargin Margin { get; }

    public static ManualCropGeometry Create(TrimBounds selected, ManualCropMargin margin, int canvasWidth, int canvasHeight)
    {
        if (selected.IsEmpty || !selected.FitsWithin(canvasWidth, canvasHeight))
            throw new ArgumentException("The selected rectangle must be non-empty and inside the source canvas.", nameof(selected));
        TrimBounds applied = TrimBounds.FromEdges(
            (int)Math.Max(0L, selected.Left - (long)margin.Left),
            (int)Math.Max(0L, selected.Top - (long)margin.Top),
            (int)Math.Min(canvasWidth, selected.RightExclusive + (long)margin.Right),
            (int)Math.Min(canvasHeight, selected.BottomExclusive + (long)margin.Bottom));
        return new ManualCropGeometry(selected, applied, margin);
    }

    /// <summary>Reads recorded facts; never infers a historical selection from output dimensions.</summary>
    public static ManualCropGeometry Restore(TrimBounds selected, TrimBounds applied, ManualCropMargin margin)
    {
        if (selected.IsEmpty || applied.IsEmpty || applied.Left > selected.Left || applied.Top > selected.Top ||
            applied.RightExclusive < selected.RightExclusive || applied.BottomExclusive < selected.BottomExclusive ||
            applied.Left != Math.Max(0L, selected.Left - (long)margin.Left) ||
            applied.Top != Math.Max(0L, selected.Top - (long)margin.Top) ||
            applied.RightExclusive > selected.RightExclusive + (long)margin.Right ||
            applied.BottomExclusive > selected.BottomExclusive + (long)margin.Bottom)
            throw new ArgumentException("Applied bounds must contain the selected bounds.", nameof(applied));
        return new ManualCropGeometry(selected, applied, margin);
    }
}
