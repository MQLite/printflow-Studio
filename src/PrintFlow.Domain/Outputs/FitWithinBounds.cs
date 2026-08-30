namespace PrintFlow.Domain.Outputs;

/// <summary>The edge, if any, that the future Photoshop operation is allowed to write.</summary>
public enum LimitingEdge
{
    None,
    Width,
    Height,
}

/// <summary>The fixed internal resize policies accepted by the sizing contracts.</summary>
public enum PhotoshopResizeMode
{
    None,
    BicubicSharper,
    PreserveDetails,
}

/// <summary>
/// A pure calculation result describing a future Photoshop Image Size operation.
/// </summary>
/// <remarks>
/// This is not an adapter command and cannot open, resize, save, or otherwise mutate a document.
/// When <see cref="RequiresShrink"/> is true, only <see cref="LimitingValueMm"/> on
/// <see cref="LimitingEdge"/> may be written. The projected pair exists solely to prove that the
/// selected edge fits the other bound; it must never be sent to Photoshop as two target values.
/// The actual Photoshop-returned millimetres and pixels remain production authority.
/// </remarks>
public readonly record struct FitWithinBoundsResult(
    LimitingEdge LimitingEdge,
    double? LimitingValueMm,
    PhotoshopResizeMode ResizeMode,
    double SourceWidthMm,
    double SourceHeightMm,
    double ProjectedWidthMm,
    double ProjectedHeightMm)
{
    public int ResolutionPpi => PrintDimensions.ProductionDpi;

    public bool RequiresShrink => ResizeMode == PhotoshopResizeMode.BicubicSharper;

    /// <summary>The projected width in whole pixels at the fixed production resolution.</summary>
    /// <remarks>
    /// Planning evidence, in the same sense <see cref="ProjectedWidthMm"/> is: it proves the
    /// selected edge fits the other bound and gives Fake mode something deterministic to size
    /// a synthetic output by. It is never a Photoshop target — sending a pair would be the
    /// non-proportional resize the contract forbids — and it is never the actual result. The
    /// real Photoshop-returned pixels are B1A.3's to read (Epic 11400 Part B1A.2A §20).
    /// <para>
    /// Computed here rather than by each caller so §6's single-authority rule covers the
    /// projected pixel calculation too, and through
    /// <see cref="PrintDimensions.PixelsFromMillimetres"/> so it rounds exactly as every other
    /// millimetre-to-pixel conversion in the codebase does.
    /// </para>
    /// </remarks>
    public int ProjectedPixelWidth => PrintDimensions.PixelsFromMillimetres(ProjectedWidthMm);

    /// <summary>The projected height in whole pixels at the fixed production resolution.</summary>
    public int ProjectedPixelHeight => PrintDimensions.PixelsFromMillimetres(ProjectedHeightMm);
}

/// <summary>
/// Selects the one limiting edge needed to fit source pixels proportionally within millimetre
/// limits at the fixed production resolution.
/// </summary>
public static class FitWithinBounds
{
    private const double MillimetresPerInch = 25.4;

    /// <summary>Calculates a maximum-width/maximum-height fit.</summary>
    public static FitWithinBoundsResult Calculate(
        int sourceWidthPixels,
        int sourceHeightPixels,
        double maxWidthMm,
        double maxHeightMm)
    {
        ValidateSource(sourceWidthPixels, sourceHeightPixels);
        ValidateLimit(maxWidthMm, nameof(maxWidthMm));
        ValidateLimit(maxHeightMm, nameof(maxHeightMm));

        (double sourceWidthMm, double sourceHeightMm) =
            SourceMillimetres(sourceWidthPixels, sourceHeightPixels);

        if (sourceWidthMm <= maxWidthMm && sourceHeightMm <= maxHeightMm)
        {
            return NoResize(sourceWidthMm, sourceHeightMm);
        }

        // sourceWidth/sourceHeight >= maxWidth/maxHeight without dividing either ratio.
        // The equality case is deliberately Width, matching the accepted product rule.
        bool widthLimits = sourceWidthPixels * maxHeightMm >= sourceHeightPixels * maxWidthMm;
        if (widthLimits)
        {
            double projectedHeightMm = maxWidthMm * sourceHeightPixels / sourceWidthPixels;
            return Shrink(
                LimitingEdge.Width,
                maxWidthMm,
                sourceWidthMm,
                sourceHeightMm,
                maxWidthMm,
                projectedHeightMm);
        }

        double projectedWidthMm = maxHeightMm * sourceWidthPixels / sourceHeightPixels;
        return Shrink(
            LimitingEdge.Height,
            maxHeightMm,
            sourceWidthMm,
            sourceHeightMm,
            projectedWidthMm,
            maxHeightMm);
    }

    /// <summary>
    /// Calculates a long-edge-only fit. Width wins a square-source tie so the result is
    /// deterministic; Photoshop still derives the other edge proportionally.
    /// </summary>
    public static FitWithinBoundsResult CalculateLongEdge(
        int sourceWidthPixels,
        int sourceHeightPixels,
        double maxLongEdgeMm)
    {
        ValidateSource(sourceWidthPixels, sourceHeightPixels);
        ValidateLimit(maxLongEdgeMm, nameof(maxLongEdgeMm));

        (double sourceWidthMm, double sourceHeightMm) =
            SourceMillimetres(sourceWidthPixels, sourceHeightPixels);
        if (Math.Max(sourceWidthMm, sourceHeightMm) <= maxLongEdgeMm)
        {
            return NoResize(sourceWidthMm, sourceHeightMm);
        }

        if (sourceWidthPixels >= sourceHeightPixels)
        {
            return Shrink(
                LimitingEdge.Width,
                maxLongEdgeMm,
                sourceWidthMm,
                sourceHeightMm,
                maxLongEdgeMm,
                maxLongEdgeMm * sourceHeightPixels / sourceWidthPixels);
        }

        return Shrink(
            LimitingEdge.Height,
            maxLongEdgeMm,
            sourceWidthMm,
            sourceHeightMm,
            maxLongEdgeMm * sourceWidthPixels / sourceHeightPixels,
            maxLongEdgeMm);
    }

    /// <summary>
    /// Interprets the existing operator-facing <see cref="PrintDimensions"/> millimetres as
    /// maximum bounds. Its independently converted pixel properties are intentionally unused.
    /// </summary>
    public static FitWithinBoundsResult Calculate(
        int sourceWidthPixels,
        int sourceHeightPixels,
        PrintDimensions limits) =>
        Calculate(sourceWidthPixels, sourceHeightPixels, limits.MaxWidthMm, limits.MaxHeightMm);

    private static (double WidthMm, double HeightMm) SourceMillimetres(int widthPixels, int heightPixels) =>
        (
            widthPixels * MillimetresPerInch / PrintDimensions.ProductionDpi,
            heightPixels * MillimetresPerInch / PrintDimensions.ProductionDpi);

    private static FitWithinBoundsResult NoResize(double sourceWidthMm, double sourceHeightMm) =>
        new(
            LimitingEdge.None,
            null,
            PhotoshopResizeMode.None,
            sourceWidthMm,
            sourceHeightMm,
            sourceWidthMm,
            sourceHeightMm);

    private static FitWithinBoundsResult Shrink(
        LimitingEdge limitingEdge,
        double limitingValueMm,
        double sourceWidthMm,
        double sourceHeightMm,
        double projectedWidthMm,
        double projectedHeightMm) =>
        new(
            limitingEdge,
            limitingValueMm,
            PhotoshopResizeMode.BicubicSharper,
            sourceWidthMm,
            sourceHeightMm,
            projectedWidthMm,
            projectedHeightMm);

    private static void ValidateSource(int widthPixels, int heightPixels)
    {
        if (widthPixels <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(widthPixels), widthPixels, "Source width must be positive pixels.");
        }

        if (heightPixels <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(heightPixels), heightPixels, "Source height must be positive pixels.");
        }
    }

    private static void ValidateLimit(double millimetres, string parameterName)
    {
        if (!double.IsFinite(millimetres) || millimetres <= 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName, millimetres, "A fit limit must be positive finite millimetres.");
        }
    }
}
