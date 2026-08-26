namespace PrintFlow.Domain.Outputs;

/// <summary>
/// The output-naming patterns loaded from the signed workstation preset
/// (<c>storageAndNamingContract</c>), never duplicated or hard-coded in business code
/// (MVP design §9.4; Epic 11100 plan §13.2).
/// </summary>
/// <remarks>
/// Each pattern is a named-token pattern in the accepted manifest's own language, rendered
/// by <see cref="Files.NamingPatternRenderer"/> and never by <c>string.Format</c>:
/// <c>{Name}</c> is the established output name, <c>{SizeMm}</c> the target width in whole
/// millimetres, and <c>{Sequence}</c> / <c>{Sequence:00}</c> the collision sequence.
/// <see cref="Files.OutputFileNaming"/> documents which tokens each pattern may use.
/// </remarks>
public sealed record NamingPatternSet(
    string EnhancedPattern,
    string CutoutPattern,
    string ProductionTiffPattern,
    string CollisionSuffixPattern)
{
    /// <summary>
    /// The patterns from the MVP design's own examples (§9.4): <c>Name_HD.png</c>,
    /// <c>Name_CUTOUT.png</c>, <c>Name_280mm_CMYK_W.tif</c>, collision suffix <c>_02</c>.
    /// </summary>
    /// <remarks>
    /// Used only as the fallback until a preset value is available; production naming is
    /// always driven by the verified preset, never by this default.
    /// <para>
    /// The spellings are the accepted workstation manifest's own, character for character —
    /// every accepted version from v1.0.0 to v1.8.0 writes them this way. They used to be
    /// positional (<c>{0}_HD.png</c>), which no accepted manifest has ever used and which
    /// made the fallback disagree with the very configuration it stands in for
    /// (naming-contract fix §5).
    /// </para>
    /// </remarks>
    public static readonly NamingPatternSet DesignDefault = new(
        EnhancedPattern: "{Name}_HD.png",
        CutoutPattern: "{Name}_CUTOUT.png",
        ProductionTiffPattern: "{Name}_{SizeMm}mm_CMYK_W.tif",
        CollisionSuffixPattern: "_{Sequence:00}");
}
