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
public sealed record FlexibleSizeSelection
{
    private FlexibleSizeSelection(
        OperatorSizingMode mode,
        SizePreset? basedOnPreset,
        bool presetOverridden,
        decimal? configuredPresetLimitMm,
        TargetEdge? selectedTargetEdge,
        decimal? requestedMillimetres)
    {
        Mode = mode;
        BasedOnPreset = basedOnPreset;
        PresetOverridden = presetOverridden;
        ConfiguredPresetLimitMm = configuredPresetLimitMm;
        SelectedTargetEdge = selectedTargetEdge;
        RequestedMillimetres = requestedMillimetres;
    }

    public OperatorSizingMode Mode { get; }

    public SizePreset? BasedOnPreset { get; }

    public bool PresetOverridden { get; }

    public decimal? ConfiguredPresetLimitMm { get; }

    public TargetEdge? SelectedTargetEdge { get; }

    public decimal? RequestedMillimetres { get; }

    /// <summary>True only when an explicitly overridden request exceeds its preset recommendation.</summary>
    public bool PresetLimitExceeded =>
        PresetOverridden && RequestedMillimetres > ConfiguredPresetLimitMm;

    /// <summary>An ordinary named preset; FitWithinBounds remains its sizing authority.</summary>
    public static FlexibleSizeSelection PresetFit(SizePreset preset)
    {
        RequireNamedPreset(preset);
        return new FlexibleSizeSelection(
            OperatorSizingMode.PresetFit, preset, false, null, null, null);
    }

    /// <summary>A custom size with exactly one explicit target edge and millimetre value.</summary>
    public static FlexibleSizeSelection CustomTarget(
        TargetEdge targetEdge, decimal requestedMillimetres)
    {
        RequireMillimetres(requestedMillimetres, nameof(requestedMillimetres));
        return new FlexibleSizeSelection(
            OperatorSizingMode.CustomTargetEdge,
            basedOnPreset: null,
            presetOverridden: false,
            configuredPresetLimitMm: null,
            targetEdge,
            requestedMillimetres);
    }

    /// <summary>
    /// A named preset used as the recommendation, followed by an explicit custom-edge override.
    /// The configured limit is retained; it is never silently replaced by the requested value.
    /// </summary>
    public static FlexibleSizeSelection OverridePreset(
        SizePreset preset,
        decimal configuredPresetLimitMm,
        TargetEdge targetEdge,
        decimal requestedMillimetres)
    {
        RequireNamedPreset(preset);
        RequireMillimetres(configuredPresetLimitMm, nameof(configuredPresetLimitMm));
        RequireMillimetres(requestedMillimetres, nameof(requestedMillimetres));
        return new FlexibleSizeSelection(
            OperatorSizingMode.CustomTargetEdge,
            preset,
            presetOverridden: true,
            configuredPresetLimitMm,
            targetEdge,
            requestedMillimetres);
    }

    private static void RequireNamedPreset(SizePreset preset)
    {
        if (preset == SizePreset.Custom)
        {
            throw new ArgumentOutOfRangeException(
                nameof(preset), preset, "PresetFit and preset override require a named preset.");
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
