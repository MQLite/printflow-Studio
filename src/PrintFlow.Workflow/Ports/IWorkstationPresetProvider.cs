using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;

namespace PrintFlow.Workflow.Ports;

/// <summary>
/// Supplies the signed workstation preset the application was configured against.
/// </summary>
/// <remarks>
/// The seam only. Epic 11100 Part 1 defines the interface and the immutable reference type;
/// the real loader — read-only, SHA-256-verified against the value in configuration — lands
/// with the rest of Task 11100.0.
///
/// Rules that hold for every implementation, now and later:
/// <list type="bullet">
///   <item>it reads; it never writes, normalises, migrates or repairs the manifest;</item>
///   <item>it verifies the manifest SHA-256 and fails closed on mismatch
///         (<see cref="FailureCode.PresetHashMismatch"/>);</item>
///   <item>the manifest lives outside the repository and its contents are never committed;</item>
///   <item>tests use a synthetic fixture, never the signed production manifest.</item>
/// </list>
/// Production-environment enforcement is not part of this seam: full verification is
/// Epic 11500.
/// </remarks>
public interface IWorkstationPresetProvider
{
    /// <summary>
    /// Returns the verified preset reference, or a failure explaining why it cannot be trusted.
    /// </summary>
    OperationResult<ProductionPresetRef> GetVerifiedPreset();

    /// <summary>
    /// Returns the output-naming patterns carried by the verified preset
    /// (<c>storageAndNamingContract</c>), or a failure when the preset itself is not verified.
    /// </summary>
    /// <remarks>
    /// Naming code must never hard-code <c>_HD</c>, <c>_CUTOUT</c>, or the collision suffix —
    /// they come from here (Epic 11100 Task 11107; plan §13.2).
    /// </remarks>
    OperationResult<NamingPatternSet> GetNamingPatterns();

    /// <summary>
    /// Returns the executable named-size recommendations carried by the verified preset
    /// (<c>productionGeometryContract.resize.limitsMillimetres</c>), or a failure when the preset
    /// itself is not verified (Epic 11400 Part B1A.2D §3).
    /// </summary>
    /// <remarks>
    /// <b>This is the only authority for what a named preset means as a print size.</b> No screen,
    /// view model, service or adapter may state an A4 or A5 production value, and nothing derives
    /// one from <see cref="PrintDimensions.NominalMillimetres"/> — the ISO paper size a preset is
    /// named after and the limit this shop prints it at are different numbers, and the preset is
    /// the one that decides (§3, §4).
    /// <para>
    /// A preset that configures no recommendation for a named size does not fall back to a paper
    /// standard: that size is simply not offered. Absence is fail-closed, as everything about a
    /// signed preset is.
    /// </para>
    /// </remarks>
    OperationResult<PresetPrintRecommendationSet> GetPrintSizeRecommendations();
}
