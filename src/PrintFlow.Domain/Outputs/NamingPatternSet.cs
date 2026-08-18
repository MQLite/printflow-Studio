namespace PrintFlow.Domain.Outputs;

/// <summary>
/// The output-naming patterns loaded from the signed workstation preset
/// (<c>storageAndNamingContract</c>), never duplicated or hard-coded in business code
/// (MVP design §9.4; Epic 11100 plan §13.2).
/// </summary>
/// <remarks>
/// Each pattern is a composite-format string. <c>{0}</c> is always the sanitised output
/// name stem; artefact-specific patterns may use additional positional arguments
/// (<see cref="Files.OutputFileNaming"/> documents each pattern's argument list).
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
    /// </remarks>
    public static readonly NamingPatternSet DesignDefault = new(
        EnhancedPattern: "{0}_HD.png",
        CutoutPattern: "{0}_CUTOUT.png",
        ProductionTiffPattern: "{0}_{1}mm_CMYK_W.tif",
        CollisionSuffixPattern: "_{0:D2}");
}
