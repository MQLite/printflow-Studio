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
    double ProjectedHeightMm,
    int SourcePixelWidth,
    int SourcePixelHeight)
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
    public int ProjectedPixelWidth => ProjectedPixels().Width;

    /// <summary>The projected height in whole pixels at the fixed production resolution.</summary>
    public int ProjectedPixelHeight => ProjectedPixels().Height;

    /// <summary>
    /// Predicts Photoshop's one-edge constrained resize in the same order Photoshop performs it:
    /// first resolve the commanded millimetre edge to an integer pixel count, then derive the
    /// uncommanded edge proportionally from that integer and the immutable source ratio.
    /// </summary>
    /// <remarks>
    /// Converting both projected millimetre values independently is subtly different. For example,
    /// 2400 × 1800 constrained to a 135 mm Height at 300 ppi becomes 1594 px on the commanded edge,
    /// then 2125 px on the proportional edge. Independently converting 180 × 135 mm predicts 2126 ×
    /// 1594, a pair Photoshop cannot return from the single-edge command. The live B1A.3 seam caught
    /// that one-pixel disagreement; keeping the projection here preserves one Domain authority while
    /// still sending only the concrete edge to Infrastructure.
    /// </remarks>
    private (int Width, int Height) ProjectedPixels()
    {
        if (!RequiresShrink)
        {
            return (SourcePixelWidth, SourcePixelHeight);
        }

        int constrainedPixels = PrintDimensions.PixelsFromMillimetres(LimitingValueMm!.Value);
        return LimitingEdge switch
        {
            LimitingEdge.Width =>
                (constrainedPixels, ScaleProportionally(
                    constrainedPixels, SourcePixelHeight, SourcePixelWidth)),
            LimitingEdge.Height =>
                (ScaleProportionally(constrainedPixels, SourcePixelWidth, SourcePixelHeight),
                    constrainedPixels),
            _ => throw new InvalidOperationException(
                "A shrinking fit must identify the one edge sent to Photoshop."),
        };
    }

    private static int ScaleProportionally(
        int constrainedPixels, int otherSourcePixels, int constrainedSourcePixels) =>
        checked((int)Math.Round(
            constrainedPixels * (double)otherSourcePixels / constrainedSourcePixels,
            MidpointRounding.AwayFromZero));
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
            return NoResize(
                sourceWidthPixels, sourceHeightPixels, sourceWidthMm, sourceHeightMm);
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
                projectedHeightMm,
                sourceWidthPixels,
                sourceHeightPixels);
        }

        double projectedWidthMm = maxHeightMm * sourceWidthPixels / sourceHeightPixels;
        return Shrink(
            LimitingEdge.Height,
            maxHeightMm,
            sourceWidthMm,
            sourceHeightMm,
            projectedWidthMm,
            maxHeightMm,
            sourceWidthPixels,
            sourceHeightPixels);
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
            return NoResize(
                sourceWidthPixels, sourceHeightPixels, sourceWidthMm, sourceHeightMm);
        }

        if (sourceWidthPixels >= sourceHeightPixels)
        {
            return Shrink(
                LimitingEdge.Width,
                maxLongEdgeMm,
                sourceWidthMm,
                sourceHeightMm,
                maxLongEdgeMm,
                maxLongEdgeMm * sourceHeightPixels / sourceWidthPixels,
                sourceWidthPixels,
                sourceHeightPixels);
        }

        return Shrink(
            LimitingEdge.Height,
            maxLongEdgeMm,
            sourceWidthMm,
            sourceHeightMm,
            maxLongEdgeMm * sourceWidthPixels / sourceHeightPixels,
            maxLongEdgeMm,
            sourceWidthPixels,
            sourceHeightPixels);
    }

    /// <summary>
    /// Calculates a short-edge-only fit: whichever source edge is shorter is held at
    /// <paramref name="maxShortEdgeMm"/>, and the longer edge runs on proportionally
    /// (Epic 11400 post-final A5 correction §6, §7, §8).
    /// </summary>
    /// <remarks>
    /// The mirror of <see cref="CalculateLongEdge"/>, and deliberately not expressible through it:
    /// a landscape source is limited on its <b>Height</b> and a portrait one on its
    /// <b>Width</b> — the opposite selection — and the resulting long edge is never clamped. A
    /// 2:1 landscape source under a 135 mm short edge projects to roughly 270 × 135 mm, and that
    /// is the correct answer rather than an escape from a second limit (§8).
    /// <para>
    /// Width wins a square-source tie, matching <see cref="CalculateLongEdge"/> and the accepted
    /// <c>longEdgeSquareTie</c> rule, so a square source has one deterministic concrete edge
    /// rather than an arbitrary one (§7).
    /// </para>
    /// <para>
    /// A source already inside the limit is left alone. Enlarging to reach the recommendation is
    /// never ordinary preset behaviour: adding pixels the source does not hold is a separate
    /// judgement with its own explicit authority, and nothing here grants one (§6).
    /// </para>
    /// </remarks>
    public static FitWithinBoundsResult CalculateShortEdge(
        int sourceWidthPixels,
        int sourceHeightPixels,
        double maxShortEdgeMm)
    {
        ValidateSource(sourceWidthPixels, sourceHeightPixels);
        ValidateLimit(maxShortEdgeMm, nameof(maxShortEdgeMm));

        (double sourceWidthMm, double sourceHeightMm) =
            SourceMillimetres(sourceWidthPixels, sourceHeightPixels);
        if (Math.Min(sourceWidthMm, sourceHeightMm) <= maxShortEdgeMm)
        {
            return NoResize(
                sourceWidthPixels, sourceHeightPixels, sourceWidthMm, sourceHeightMm);
        }

        // Landscape is limited on Height, portrait on Width, and a square tie goes to Width.
        if (sourceWidthPixels > sourceHeightPixels)
        {
            return Shrink(
                LimitingEdge.Height,
                maxShortEdgeMm,
                sourceWidthMm,
                sourceHeightMm,
                maxShortEdgeMm * sourceWidthPixels / sourceHeightPixels,
                maxShortEdgeMm,
                sourceWidthPixels,
                sourceHeightPixels);
        }

        return Shrink(
            LimitingEdge.Width,
            maxShortEdgeMm,
            sourceWidthMm,
            sourceHeightMm,
            maxShortEdgeMm,
            maxShortEdgeMm * sourceHeightPixels / sourceWidthPixels,
            sourceWidthPixels,
            sourceHeightPixels);
    }

    /// <summary>
    /// The fit box that reproduces <see cref="CalculateShortEdge"/>'s decision for one exact
    /// source (post-final A5 correction §8).
    /// </summary>
    /// <remarks>
    /// The short-edge limit goes on the axis the source makes shorter; the other axis is bounded
    /// by the source's own millimetres, which is a bound that can never bind and is therefore the
    /// honest way to write "unbounded" into a plan whose stored limits are two positive numbers.
    /// <para>
    /// It lives here, beside the calculation and over the same
    /// <see cref="SourceMillimetres"/> conversion, so the bounds a plan records and the decision
    /// it records cannot be computed from two different roundings of the same source.
    /// </para>
    /// </remarks>
    public static (double MaxWidthMm, double MaxHeightMm) ShortEdgeBounds(
        int sourceWidthPixels,
        int sourceHeightPixels,
        double maxShortEdgeMm)
    {
        ValidateSource(sourceWidthPixels, sourceHeightPixels);
        ValidateLimit(maxShortEdgeMm, nameof(maxShortEdgeMm));

        (double sourceWidthMm, double sourceHeightMm) =
            SourceMillimetres(sourceWidthPixels, sourceHeightPixels);

        return sourceWidthPixels > sourceHeightPixels
            ? (Math.Max(sourceWidthMm, maxShortEdgeMm), maxShortEdgeMm)
            : (maxShortEdgeMm, Math.Max(sourceHeightMm, maxShortEdgeMm));
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

    private static FitWithinBoundsResult NoResize(
        int sourceWidthPixels,
        int sourceHeightPixels,
        double sourceWidthMm,
        double sourceHeightMm) =>
        new(
            LimitingEdge.None,
            null,
            PhotoshopResizeMode.None,
            sourceWidthMm,
            sourceHeightMm,
            sourceWidthMm,
            sourceHeightMm,
            sourceWidthPixels,
            sourceHeightPixels);

    private static FitWithinBoundsResult Shrink(
        LimitingEdge limitingEdge,
        double limitingValueMm,
        double sourceWidthMm,
        double sourceHeightMm,
        double projectedWidthMm,
        double projectedHeightMm,
        int sourceWidthPixels,
        int sourceHeightPixels) =>
        new(
            limitingEdge,
            limitingValueMm,
            PhotoshopResizeMode.BicubicSharper,
            sourceWidthMm,
            sourceHeightMm,
            projectedWidthMm,
            projectedHeightMm,
            sourceWidthPixels,
            sourceHeightPixels);

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
