namespace PrintFlow.Domain.Outputs;

/// <summary>
/// The shape of a configured named-preset recommendation (Epic 11400 Part B1A.2D §3, §6).
/// </summary>
/// <remarks>
/// The signed workstation preset states a named size's executable limit in one of three forms, and
/// the difference is not cosmetic: a box constrains both edges independently, a long-edge
/// recommendation constrains whichever edge the source happens to make longer, and a short-edge
/// recommendation constrains whichever edge the source makes shorter while leaving the other free
/// to run on. Recording which form was configured is what lets a persisted decision be read back
/// as the decision that was actually made rather than as an equivalent-looking one.
/// </remarks>
public enum PresetRecommendationKind
{
    /// <summary>Two independent maximum bounds, as A3 is configured.</summary>
    MaximumBox,

    /// <summary>One maximum for whichever source edge is longer, as A4 is configured.</summary>
    MaximumLongEdge,

    /// <summary>
    /// One maximum for whichever source edge is <i>shorter</i>, as A5 is configured from v1.15.0
    /// (Epic 11400 post-final A5 correction §4, §6).
    /// </summary>
    /// <remarks>
    /// Emphatically not a square <see cref="MaximumBox"/> of the same value, and that is the whole
    /// reason this member exists rather than 135 × 135 bounds. A short-edge limit places no cap on
    /// the long edge at all: a 2:1 source under A5's 135 mm short edge prints at roughly
    /// 270 × 135 mm, which a 135 × 135 box would have refused and a
    /// <see cref="MaximumLongEdge"/> of 135 would have shrunk to 135 × 67.5 mm. Encoding it as
    /// either would lose the product meaning the shop actually configured, and a persisted
    /// decision could no longer answer "A5 was a maximum-short-edge recommendation of 135 mm".
    /// </remarks>
    MaximumShortEdge,
}

/// <summary>
/// What the configured workstation preset actually recommends for one named size
/// (Epic 11400 Part B1A.2D §3, §4).
/// </summary>
/// <remarks>
/// <b>This is the executable authority, and <see cref="PrintDimensions.NominalMillimetres"/> is
/// not.</b> The two are different questions that happen to share a name. "A4" as a piece of paper
/// is 210 × 297 mm; "A4" as this shop's print recommendation is a 280 mm maximum long edge, and
/// nothing derives the second from the first. A build that read the nominal ISO size as the
/// production limit would print A4 jobs 17 mm too long on the long edge and call it configured
/// behaviour (§3, §4).
/// <para>
/// So a recommendation is only ever <i>read</i> from the verified preset, never constructed from a
/// paper standard, a screen value, or a constant in a view model. The nominal sizes remain
/// available as descriptive metadata — what the preset is named after — and are never persisted as
/// an executable limit.
/// </para>
/// <para>
/// <see cref="Fit"/> and <see cref="Covers"/> are what make this a recommendation rather than a
/// second sizing algorithm. <see cref="Fit"/> is the one place a configured form becomes a sizing
/// decision, and it delegates every case to <see cref="FitWithinBounds"/>; <see cref="Covers"/> is
/// the one place a projected result is measured against what was configured. Neither comparison is
/// restated in <c>SessionService</c>, a view model, the Fake adapter or the Photoshop adapter
/// (post-final A5 correction §10).
/// </para>
/// <para>
/// A box and a long edge are both expressible as a fit box that <see cref="FitWithinBounds"/>
/// already owned — a long-edge limit of <c>L</c> is exactly the square box <c>L × L</c>, because
/// fitting proportionally inside a square constrains the longer source edge to <c>L</c> and lets
/// Photoshop derive the other. A <see cref="PresetRecommendationKind.MaximumShortEdge"/> is
/// <b>not</b>: no fixed box expresses "limit the shorter edge and leave the longer one free", which
/// is why <see cref="AsFitBounds"/> refuses it and <see cref="AsFitBoundsFor"/> — bound to one
/// source, as the short edge itself is — exists beside it (§6, §8).
/// </para>
/// </remarks>
public sealed record PresetPrintRecommendation
{
    private PresetPrintRecommendation(
        SizePreset preset, PresetRecommendationKind kind, decimal maxWidthMm, decimal maxHeightMm)
    {
        Preset = preset;
        Kind = kind;
        MaxWidthMm = maxWidthMm;
        MaxHeightMm = maxHeightMm;
    }

    /// <summary>Which named size this recommends. Never <see cref="SizePreset.Custom"/>.</summary>
    public SizePreset Preset { get; }

    /// <summary>Which of the three configured forms was written.</summary>
    public PresetRecommendationKind Kind { get; }

    /// <summary>
    /// The configured maximum width. Equal to the single edge for a long- or short-edge form.
    /// </summary>
    public decimal MaxWidthMm { get; }

    /// <summary>
    /// The configured maximum height. Equal to the single edge for a long- or short-edge form.
    /// </summary>
    public decimal MaxHeightMm { get; }

    /// <summary>The configured long edge, or null unless a long edge was configured.</summary>
    public decimal? MaxLongEdgeMm =>
        Kind == PresetRecommendationKind.MaximumLongEdge ? MaxWidthMm : null;

    /// <summary>The configured short edge, or null unless a short edge was configured.</summary>
    public decimal? MaxShortEdgeMm =>
        Kind == PresetRecommendationKind.MaximumShortEdge ? MaxWidthMm : null;

    /// <summary>A configured two-bound box, as A3 landscape and A3 portrait are written.</summary>
    public static PresetPrintRecommendation MaximumBox(
        SizePreset preset, decimal maxWidthMm, decimal maxHeightMm)
    {
        RequireNamed(preset);
        RequirePositive(maxWidthMm, nameof(maxWidthMm));
        RequirePositive(maxHeightMm, nameof(maxHeightMm));
        return new PresetPrintRecommendation(
            preset, PresetRecommendationKind.MaximumBox, maxWidthMm, maxHeightMm);
    }

    /// <summary>A configured single long-edge maximum, as A4 is written.</summary>
    public static PresetPrintRecommendation MaximumLongEdge(SizePreset preset, decimal maxLongEdgeMm)
    {
        RequireNamed(preset);
        RequirePositive(maxLongEdgeMm, nameof(maxLongEdgeMm));
        return new PresetPrintRecommendation(
            preset, PresetRecommendationKind.MaximumLongEdge, maxLongEdgeMm, maxLongEdgeMm);
    }

    /// <summary>A configured single short-edge maximum, as A5 is written from v1.15.0.</summary>
    /// <remarks>
    /// Both bounds carry the configured value for the same reason the long-edge form does: the
    /// pair is the persisted representation, and <see cref="Kind"/> is what says how to read it.
    /// What it is <b>not</b> is a 135 × 135 box — <see cref="Covers"/> and <see cref="Fit"/> both
    /// dispatch on the kind, and neither ever caps the long edge (post-final A5 correction §4, §8).
    /// </remarks>
    public static PresetPrintRecommendation MaximumShortEdge(SizePreset preset, decimal maxShortEdgeMm)
    {
        RequireNamed(preset);
        RequirePositive(maxShortEdgeMm, nameof(maxShortEdgeMm));
        return new PresetPrintRecommendation(
            preset, PresetRecommendationKind.MaximumShortEdge, maxShortEdgeMm, maxShortEdgeMm);
    }

    /// <summary>
    /// This recommendation as the source-independent fit box <see cref="FitWithinBounds"/>
    /// consumes.
    /// </summary>
    /// <remarks>
    /// Available only for the two forms a fixed box can honestly express. A maximum short edge
    /// cannot be one: the box that produces it depends on which way round the source is, so
    /// <see cref="AsFitBoundsFor"/> is the form that answers for it and this one refuses rather
    /// than returning the square box that would silently cap the long edge (§8).
    /// </remarks>
    public PrintDimensions AsFitBounds() =>
        Kind == PresetRecommendationKind.MaximumShortEdge
            ? throw new InvalidOperationException(
                $"{Preset} configures a maximum short edge of {MaxWidthMm:0.##} mm, which no fixed " +
                "box expresses; the long edge is deliberately unbounded. Ask AsFitBoundsFor for a " +
                "particular source instead.")
            : PrintDimensions.FromMillimetres((double)MaxWidthMm, (double)MaxHeightMm, Preset);

    /// <summary>
    /// This recommendation as the fit box that produces its decision for one exact source.
    /// </summary>
    /// <remarks>
    /// For a box and a long edge the answer is <see cref="AsFitBounds"/> and the source is
    /// irrelevant. For a short edge it is not: the bound goes on whichever axis the source makes
    /// shorter, and the other axis is bounded by the source's own millimetres — a bound that can
    /// never bind, which is exactly how "the long edge is unbounded" is written in a schema whose
    /// plan columns are two positive millimetre values (§8).
    /// <para>
    /// The pairing matters more than the numbers: fitting the same source into the box this
    /// returns reproduces <see cref="Fit"/>'s decision exactly, so a persisted plan's stored
    /// bounds and its stored limiting edge can never come apart, and a 2:1 landscape source under
    /// A5 records bounds of roughly 508 × 135 mm rather than a 135 × 135 box it visibly does not
    /// fit inside.
    /// </para>
    /// </remarks>
    public PrintDimensions AsFitBoundsFor(int sourcePixelWidth, int sourcePixelHeight)
    {
        if (Kind != PresetRecommendationKind.MaximumShortEdge)
        {
            return AsFitBounds();
        }

        (double maxWidthMm, double maxHeightMm) = FitWithinBounds.ShortEdgeBounds(
            sourcePixelWidth, sourcePixelHeight, (double)MaxWidthMm);
        return PrintDimensions.FromMillimetres(maxWidthMm, maxHeightMm, Preset);
    }

    /// <summary>
    /// The sizing decision this recommendation makes for one exact source
    /// (post-final A5 correction §6, §10, §11).
    /// </summary>
    /// <remarks>
    /// The single place a configured form becomes a fit. Every case delegates to
    /// <see cref="FitWithinBounds"/>, so limiting-edge selection, the already-within-limits case,
    /// the no-enlargement rule and the projected pixels keep exactly one implementation — and a
    /// fourth configured form could only ever be added here.
    /// </remarks>
    public FitWithinBoundsResult Fit(int sourcePixelWidth, int sourcePixelHeight) => Kind switch
    {
        PresetRecommendationKind.MaximumBox => FitWithinBounds.Calculate(
            sourcePixelWidth, sourcePixelHeight, (double)MaxWidthMm, (double)MaxHeightMm),
        PresetRecommendationKind.MaximumLongEdge => FitWithinBounds.CalculateLongEdge(
            sourcePixelWidth, sourcePixelHeight, (double)MaxWidthMm),
        PresetRecommendationKind.MaximumShortEdge => FitWithinBounds.CalculateShortEdge(
            sourcePixelWidth, sourcePixelHeight, (double)MaxWidthMm),
        _ => throw new InvalidOperationException($"Unknown recommendation kind {Kind}."),
    };

    /// <summary>
    /// Whether a projected result stays inside what this recommendation permits
    /// (post-final A5 correction §9, §10).
    /// </summary>
    /// <remarks>
    /// The one authority for "did that go past the preset", and the reason an explicit override is
    /// no longer classified by comparing the operator's typed scalar against a single number. The
    /// three forms genuinely ask different questions of the same geometry:
    /// <list type="bullet">
    ///   <item>a box wants both projected edges inside their own bound;</item>
    ///   <item>a long edge wants the longer projected edge inside it;</item>
    ///   <item>a short edge wants the <i>shorter</i> projected edge inside it, and says nothing
    ///         whatever about the longer one.</item>
    /// </list>
    /// <para>
    /// So a 160 mm width typed against A5's 135 mm short edge is <b>not</b> an excess if the
    /// artwork is wide enough that 160 mm of width still leaves under 135 mm of height — and a
    /// 100 mm width on a very tall source can be, which no comparison of 100 against 135 could
    /// ever have discovered (§9, §21).
    /// </para>
    /// <para>
    /// Whole pixels rather than millimetres, because a projection is whole pixels: comparing the
    /// two through <see cref="PrintDimensions.PixelsFromMillimetres"/> uses the codebase's one
    /// rounding rule and needs no tolerance, where millimetre doubles would need one and would
    /// decide the exactly-at-the-limit case somewhere no contract states.
    /// </para>
    /// </remarks>
    public bool Covers(int projectedPixelWidth, int projectedPixelHeight)
    {
        RequirePositivePixels(projectedPixelWidth, nameof(projectedPixelWidth));
        RequirePositivePixels(projectedPixelHeight, nameof(projectedPixelHeight));

        return Kind switch
        {
            PresetRecommendationKind.MaximumBox =>
                projectedPixelWidth <= PixelsOf(MaxWidthMm) &&
                projectedPixelHeight <= PixelsOf(MaxHeightMm),
            PresetRecommendationKind.MaximumLongEdge =>
                Math.Max(projectedPixelWidth, projectedPixelHeight) <= PixelsOf(MaxWidthMm),
            PresetRecommendationKind.MaximumShortEdge =>
                Math.Min(projectedPixelWidth, projectedPixelHeight) <= PixelsOf(MaxWidthMm),
            _ => throw new InvalidOperationException($"Unknown recommendation kind {Kind}."),
        };
    }

    /// <summary>
    /// Whether <paramref name="limits"/> are exactly this recommendation
    /// (Epic 11400 Part B1A.2D §19).
    /// </summary>
    /// <remarks>
    /// Exact rather than tolerant. A named-preset decision that is <i>almost</i> the configured
    /// recommendation is a decision made against something other than the configured
    /// recommendation, and the whole point of §3 is that no other source supplies those numbers.
    /// </remarks>
    public bool Matches(PrintDimensions limits) =>
        limits.Preset == Preset &&
        (decimal)limits.MaxWidthMm == MaxWidthMm &&
        (decimal)limits.MaxHeightMm == MaxHeightMm;

    private static void RequireNamed(SizePreset preset)
    {
        if (preset == SizePreset.Custom)
        {
            throw new ArgumentOutOfRangeException(
                nameof(preset), preset, "Only a named preset carries a configured recommendation.");
        }
    }

    private static void RequirePositive(decimal millimetres, string parameterName)
    {
        if (millimetres <= 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName, millimetres, "A configured print limit must be positive millimetres.");
        }
    }

    private static void RequirePositivePixels(int pixels, string parameterName)
    {
        if (pixels <= 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName, pixels, "A projected geometry is positive whole pixels.");
        }
    }

    private static int PixelsOf(decimal millimetres) =>
        PrintDimensions.PixelsFromMillimetres((double)millimetres);

    public override string ToString() => Kind switch
    {
        PresetRecommendationKind.MaximumLongEdge => $"{Preset} max long edge {MaxWidthMm:0.##} mm",
        PresetRecommendationKind.MaximumShortEdge => $"{Preset} max short edge {MaxWidthMm:0.##} mm",
        _ => $"{Preset} max {MaxWidthMm:0.##}×{MaxHeightMm:0.##} mm",
    };
}

/// <summary>
/// Every named-size recommendation the verified preset configures (Epic 11400 Part B1A.2D §3).
/// </summary>
/// <remarks>
/// A closed lookup rather than a dictionary passed around loose, so "what does this installation
/// recommend for A4" has one answer and one place to ask it. A preset that configures no
/// recommendation for a named size does not silently fall back to a paper standard: the size is
/// simply not offered, which is the fail-closed reading of a preset that does not mention it.
/// </remarks>
public sealed class PresetPrintRecommendationSet
{
    private readonly IReadOnlyDictionary<SizePreset, PresetPrintRecommendation> _byPreset;

    public PresetPrintRecommendationSet(IEnumerable<PresetPrintRecommendation> recommendations)
    {
        ArgumentNullException.ThrowIfNull(recommendations);

        Dictionary<SizePreset, PresetPrintRecommendation> byPreset = [];
        foreach (PresetPrintRecommendation recommendation in recommendations)
        {
            if (!byPreset.TryAdd(recommendation.Preset, recommendation))
            {
                throw new ArgumentException(
                    $"The preset configures {recommendation.Preset} more than once; " +
                    "a named size has exactly one recommendation.",
                    nameof(recommendations));
            }
        }

        _byPreset = byPreset;
    }

    /// <summary>Every configured recommendation, in <see cref="SizePreset"/> order.</summary>
    public IReadOnlyList<PresetPrintRecommendation> All =>
        [.. _byPreset.Values.OrderBy(r => r.Preset)];

    /// <summary>The recommendation for <paramref name="preset"/>, or null when none is configured.</summary>
    public PresetPrintRecommendation? For(SizePreset preset) =>
        _byPreset.TryGetValue(preset, out PresetPrintRecommendation? found) ? found : null;
}
