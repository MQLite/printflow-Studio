using PrintFlow.Domain.Files;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Fixtures;

/// <summary>
/// A verified preset whose naming patterns the test chooses, so "the preset supplies something
/// the renderer cannot honour" can be exercised through the real service and the real screen
/// (naming-contract fix §6, §11).
/// </summary>
/// <remarks>
/// Deliberately a stub of the <i>port</i> rather than of PrintFlow's own naming code: the
/// question these tests ask is what the workflow and the shell do when a hash-verified manifest
/// carries a pattern nothing can render, and answering it requires the whole path below the
/// provider to be real.
/// </remarks>
internal sealed class StubNamingPresetProvider : IWorkstationPresetProvider
{
    private readonly NamingPatternSet _patterns;

    public StubNamingPresetProvider(NamingPatternSet patterns) => _patterns = patterns;

    /// <summary>A provider whose cutout pattern names a token no artefact kind supplies.</summary>
    public static StubNamingPresetProvider WithUnrenderableCutoutPattern() =>
        new(NamingPatternSet.DesignDefault with { CutoutPattern = "{Foo}_CUTOUT.png" });

    public OperationResult<ProductionPresetRef> GetVerifiedPreset() =>
        OperationResult.Ok(new ProductionPresetRef(
            "test-workstation-v1", "0.0.1", Sha256.Parse(new string('a', 64))));

    public OperationResult<NamingPatternSet> GetNamingPatterns() => OperationResult.Ok(_patterns);

    /// <summary>
    /// The same recommendations the synthetic manifest configures, so a naming test can still
    /// record a named preset size if it needs one.
    /// </summary>
    public OperationResult<PresetPrintRecommendationSet> GetPrintSizeRecommendations() =>
        OperationResult.Ok(PresetFixture.Recommendations);
}
