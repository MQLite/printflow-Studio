namespace PrintFlow.Domain.Outputs;

/// <summary>The two and only two ways an operator can choose a physical print size.</summary>
public enum OperatorSizingMode
{
    PresetFit,
    CustomTargetEdge,
}

/// <summary>The single physical edge made authoritative by a custom size decision.</summary>
public enum TargetEdge
{
    Width,
    Height,
    LongEdge,
}

/// <summary>Whether the fixed sizing operation preserves, reduces, or increases pixels.</summary>
public enum ResizeDirection
{
    ResolutionOnly,
    Shrink,
    Enlarge,
}

/// <summary>
/// One operator size decision. Its factories make a second independently authoritative custom
/// dimension and an axis on ordinary preset use structurally unrepresentable.
/// </summary>
/// <remarks>
/// Every named-preset form carries the <see cref="PresetPrintRecommendation"/> it was made
/// against, rather than a bare <see cref="SizePreset"/> and a number copied out of it. That is
/// what keeps §3's authority intact through persistence: a stored decision says which
/// recommendation the operator was actually shown, so an override can still be read as an
/// override of <i>that</i> recommendation after the configured preset moves on
/// (Epic 11400 Part B1A.2D §6, §19).
/// </remarks>
public sealed record FlexibleSizeSelection
{
    private FlexibleSizeSelection(
        OperatorSizingMode mode,
        PresetPrintRecommendation? recommendation,
        bool presetOverridden,
        TargetEdge? selectedTargetEdge,
        decimal? requestedMillimetres)
    {
        Mode = mode;
        Recommendation = recommendation;
        PresetOverridden = presetOverridden;
        SelectedTargetEdge = selectedTargetEdge;
        RequestedMillimetres = requestedMillimetres;
    }

    public OperatorSizingMode Mode { get; }

    /// <summary>The configured recommendation this decision was made against, if any.</summary>
    public PresetPrintRecommendation? Recommendation { get; }

    public SizePreset? BasedOnPreset => Recommendation?.Preset;

    public bool PresetOverridden { get; }

    public TargetEdge? SelectedTargetEdge { get; }

    public decimal? RequestedMillimetres { get; }

    // There is deliberately no PresetLimitExceeded here, and no single "configured limit"
    // millimetre value for it to compare against (post-final A5 correction §9, §10).
    //
    // Whether a request went past its recommendation is not a fact about the request. It is a
    // fact about what the request PROJECTS TO against a particular source, and the three
    // configured forms ask different questions of that projection — a box measures both edges, a
    // long edge measures the longer, a short edge measures the shorter and ignores the longer
    // entirely. A selection holds no source pixels, so it cannot answer, and the scalar
    // comparison it used to make (`requested > 135`) gave the wrong answer in both directions
    // once A5 became a short-edge recommendation: 160 mm of width on a wide source stays inside
    // A5's 135 mm short edge, and 100 mm of width on a tall one does not.
    //
    // TargetEdgePrintPreparationPlan.PresetLimitExceeded is where the question is asked, because
    // that is where the projection is, and PresetPrintRecommendation.Covers is the only thing
    // that answers it.

    /// <summary>An ordinary named preset; FitWithinBounds remains its sizing authority.</summary>
    public static FlexibleSizeSelection PresetFit(PresetPrintRecommendation recommendation)
    {
        ArgumentNullException.ThrowIfNull(recommendation);
        return new FlexibleSizeSelection(
            OperatorSizingMode.PresetFit, recommendation, false, null, null);
    }

    /// <summary>A custom size with exactly one explicit target edge and millimetre value.</summary>
    public static FlexibleSizeSelection CustomTarget(
        TargetEdge targetEdge, decimal requestedMillimetres)
    {
        RequireTargetEdge(targetEdge);
        RequireMillimetres(requestedMillimetres, nameof(requestedMillimetres));
        return new FlexibleSizeSelection(
            OperatorSizingMode.CustomTargetEdge,
            recommendation: null,
            presetOverridden: false,
            targetEdge,
            requestedMillimetres);
    }

    /// <summary>
    /// A named preset used as the recommendation, followed by an explicit custom-edge override.
    /// The configured recommendation is retained; it is never silently replaced by the request.
    /// </summary>
    public static FlexibleSizeSelection OverridePreset(
        PresetPrintRecommendation recommendation,
        TargetEdge targetEdge,
        decimal requestedMillimetres)
    {
        ArgumentNullException.ThrowIfNull(recommendation);
        RequireTargetEdge(targetEdge);
        RequireMillimetres(requestedMillimetres, nameof(requestedMillimetres));
        return new FlexibleSizeSelection(
            OperatorSizingMode.CustomTargetEdge,
            recommendation,
            presetOverridden: true,
            targetEdge,
            requestedMillimetres);
    }

    /// <summary>
    /// Rebuilds a stored selection, refusing one whose parts do not describe a single decision
    /// (Epic 11400 Part B1A.2D §23).
    /// </summary>
    /// <remarks>
    /// It routes back through the same three factories rather than assigning fields, so a
    /// persisted row cannot express a combination the operator could never have made — a preset
    /// fit that also carries a requested edge, an override with no recommendation behind it, or a
    /// custom target with no millimetres. Nothing is repaired: a row that does not describe one of
    /// the three decisions is refused.
    /// </remarks>
    public static FlexibleSizeSelection Rehydrate(
        OperatorSizingMode mode,
        PresetPrintRecommendation? recommendation,
        bool presetOverridden,
        TargetEdge? selectedTargetEdge,
        decimal? requestedMillimetres)
    {
        if (mode == OperatorSizingMode.PresetFit)
        {
            if (recommendation is null)
            {
                throw new ArgumentException(
                    "A stored PresetFit selection carries no configured recommendation; the executable " +
                    "limit is never re-derived from a paper standard.",
                    nameof(recommendation));
            }

            if (presetOverridden || selectedTargetEdge is not null || requestedMillimetres is not null)
            {
                throw new ArgumentException(
                    "A stored PresetFit selection also carries an override or a custom target edge; " +
                    "ordinary preset use asks for neither.",
                    nameof(mode));
            }

            return PresetFit(recommendation);
        }

        if (selectedTargetEdge is not { } edge || requestedMillimetres is not { } millimetres)
        {
            throw new ArgumentException(
                "A stored CustomTargetEdge selection needs both a target edge and its millimetres.",
                nameof(selectedTargetEdge));
        }

        if (presetOverridden)
        {
            return recommendation is null
                ? throw new ArgumentException(
                    "A stored preset override carries no recommendation to have overridden.",
                    nameof(recommendation))
                : OverridePreset(recommendation, edge, millimetres);
        }

        return recommendation is null
            ? CustomTarget(edge, millimetres)
            : throw new ArgumentException(
                "A stored custom target carries a recommendation without being marked an override; " +
                "the two readings differ and neither may be guessed.",
                nameof(recommendation));
    }

    private static void RequireTargetEdge(TargetEdge targetEdge)
    {
        if (targetEdge is not (TargetEdge.Width or TargetEdge.Height or TargetEdge.LongEdge))
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetEdge), targetEdge, "Unknown target edge.");
        }
    }

    private static void RequireMillimetres(decimal millimetres, string parameterName)
    {
        if (millimetres <= 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName, millimetres, "A target edge must be positive millimetres.");
        }
    }
}
