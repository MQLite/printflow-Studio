namespace PrintFlow.Domain.Outputs;

/// <summary>
/// The shape of a configured named-preset recommendation (Epic 11400 Part B1A.2D §3, §6).
/// </summary>
/// <remarks>
/// The signed workstation preset states a named size's executable limit in one of two forms, and
/// the difference is not cosmetic: a box constrains both edges independently, while a long-edge
/// recommendation constrains whichever edge the source happens to make longer. Recording which
/// form was configured is what lets a persisted decision be read back as the decision that was
/// actually made rather than as an equivalent-looking one.
/// </remarks>
public enum PresetRecommendationKind
{
    /// <summary>Two independent maximum bounds, as A3 is configured.</summary>
    MaximumBox,

    /// <summary>One maximum for whichever source edge is longer, as A4 and A5 are configured.</summary>
    MaximumLongEdge,
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
/// <see cref="AsFitBounds"/> is what makes this a recommendation rather than a second sizing
/// algorithm. Both forms reduce to a fit box that <see cref="FitWithinBounds"/> already owns: a
/// long-edge limit of <c>L</c> is exactly the square box <c>L × L</c>, because fitting
/// proportionally inside a square constrains the longer source edge to <c>L</c> and lets Photoshop
/// derive the other — which is the definition of a maximum long edge. There is deliberately no
/// second limiting-edge rule here (§17).
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

    /// <summary>Which of the two configured forms was written.</summary>
    public PresetRecommendationKind Kind { get; }

    /// <summary>The configured maximum width. Equal to the long edge for a long-edge form.</summary>
    public decimal MaxWidthMm { get; }

    /// <summary>The configured maximum height. Equal to the long edge for a long-edge form.</summary>
    public decimal MaxHeightMm { get; }

    /// <summary>The configured long edge, or null when a box was configured.</summary>
    public decimal? MaxLongEdgeMm =>
        Kind == PresetRecommendationKind.MaximumLongEdge ? MaxWidthMm : null;

    /// <summary>
    /// The largest physical edge this recommendation permits, which is what an explicit custom
    /// override is measured against (§11).
    /// </summary>
    /// <remarks>
    /// For a long-edge form it is the configured long edge. For a box it is the larger of the two
    /// bounds — the longest edge the recommendation could ever produce — so "the operator asked
    /// for more than the preset recommends" means the same thing in both forms, and a 320 mm
    /// request against a 360 × 280 box is honestly <i>not</i> an override of the recommendation.
    /// </remarks>
    public decimal RecommendedLimitMm => Math.Max(MaxWidthMm, MaxHeightMm);

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

    /// <summary>A configured single long-edge maximum, as A4 and A5 are written.</summary>
    public static PresetPrintRecommendation MaximumLongEdge(SizePreset preset, decimal maxLongEdgeMm)
    {
        RequireNamed(preset);
        RequirePositive(maxLongEdgeMm, nameof(maxLongEdgeMm));
        return new PresetPrintRecommendation(
            preset, PresetRecommendationKind.MaximumLongEdge, maxLongEdgeMm, maxLongEdgeMm);
    }

    /// <summary>
    /// This recommendation as the fit box <see cref="FitWithinBounds"/> consumes.
    /// </summary>
    /// <remarks>
    /// The one conversion, so ordinary preset use keeps exactly the sizing authority the accepted
    /// B1A.1 contract gave it. Nothing else in the codebase turns a recommendation into limits.
    /// </remarks>
    public PrintDimensions AsFitBounds() =>
        PrintDimensions.FromMillimetres((double)MaxWidthMm, (double)MaxHeightMm, Preset);

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

    public override string ToString() =>
        Kind == PresetRecommendationKind.MaximumLongEdge
            ? $"{Preset} max long edge {MaxWidthMm:0.##} mm"
            : $"{Preset} max {MaxWidthMm:0.##}×{MaxHeightMm:0.##} mm";
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
