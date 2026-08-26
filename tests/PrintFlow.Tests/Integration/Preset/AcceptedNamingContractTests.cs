using PrintFlow.Domain.Files;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Preset;
using PrintFlow.Tests.Fixtures;

namespace PrintFlow.Tests.Integration.Preset;

/// <summary>
/// The regression the previous suite was missing: the naming values the accepted workstation
/// preset actually supplies, carried through the real provider into the real renderer
/// (naming-contract fix §9).
/// </summary>
/// <remarks>
/// Every naming test before this one built a <see cref="NamingPatternSet"/> by hand, in a
/// positional syntax no accepted manifest has ever used. That is why 8,242 green tests could
/// coexist with a production configuration that terminated the WPF process: the suite and the
/// preset were speaking different languages, and nothing compared them.
/// <para>
/// These tests close that gap from both ends. The rendering tests run the accepted pattern
/// strings through <see cref="WorkstationPresetProvider"/> — the same loader production uses —
/// and assert the file names that come out. The transcription test reads the manifest this
/// installation is configured against and checks that those pattern strings are still what it
/// holds, so the constants cannot quietly drift away from the evidence they claim to mirror.
/// </para>
/// <para>
/// The accepted manifest itself is never written to, never reformatted, and never copied into
/// the repository — it is opened read-only or not at all.
/// </para>
/// </remarks>
public sealed class AcceptedNamingContractTests
{
    /// <summary>
    /// <c>{Name}_HD.png</c>, as supplied by the production provider, produces
    /// <c>&lt;Name&gt;_HD.png</c>.
    /// </summary>
    [Fact]
    public void The_accepted_enhanced_pattern_names_the_HD_output()
    {
        NamingPatternSet patterns = LoadThroughTheProductionProvider();
        patterns.EnhancedPattern.ShouldBe(AcceptedNamingContract.EnhancedPattern);

        OperationResult<string> name = OutputFileNaming.BuildProposedFileName(
            NamingArtifactKind.Enhanced, OutputName.Parse("Example"), patterns);

        name.IsSuccess.ShouldBeTrue(name.IsFailure ? name.Failure.ToString() : string.Empty);
        name.Value.ShouldBe("Example_HD.png");
    }

    /// <summary>
    /// <c>{Name}_CUTOUT.png</c>, as supplied by the production provider, produces
    /// <c>&lt;Name&gt;_CUTOUT.png</c> — the rendering the R2 Final Gate never reached.
    /// </summary>
    [Fact]
    public void The_accepted_cutout_pattern_names_the_CUTOUT_output()
    {
        NamingPatternSet patterns = LoadThroughTheProductionProvider();
        patterns.CutoutPattern.ShouldBe(AcceptedNamingContract.CutoutPattern);

        OperationResult<string> name = OutputFileNaming.BuildProposedFileName(
            NamingArtifactKind.Cutout, OutputName.Parse("Example"), patterns);

        name.IsSuccess.ShouldBeTrue(name.IsFailure ? name.Failure.ToString() : string.Empty);
        name.Value.ShouldBe("Example_CUTOUT.png");
    }

    /// <summary>The remaining two accepted patterns render too, from the same loaded set.</summary>
    [Fact]
    public void The_accepted_tiff_and_collision_patterns_render_from_the_same_loaded_set()
    {
        NamingPatternSet patterns = LoadThroughTheProductionProvider();
        patterns.ProductionTiffPattern.ShouldBe(AcceptedNamingContract.ProductionTiffPattern);
        patterns.CollisionSuffixPattern.ShouldBe(AcceptedNamingContract.CollisionPattern);

        OutputFileNaming
            .BuildProposedFileName(
                NamingArtifactKind.ProductionTiff, OutputName.Parse("Example"), patterns, targetWidthMm: 280)
            .Value.ShouldBe("Example_280mm_CMYK_W.tif");

        OutputFileNaming.BuildCollisionCandidate("Example_HD.png", patterns, 2)
            .Value.ShouldBe("Example_HD_02.png");
    }

    /// <summary>
    /// The transcribed contract still matches the manifest this installation is configured
    /// against.
    /// </summary>
    /// <remarks>
    /// Two assertions, and both are worth having. The first runs everywhere: the transcribed
    /// values must be named-token patterns, which is what makes the test above a test of the
    /// accepted contract rather than of a convenient invention. The second runs wherever the
    /// accepted manifest is actually present — this workstation, and any other pointed at a
    /// real baseline — and compares the constants to the file itself, character for character.
    /// </remarks>
    [Fact]
    public void The_transcribed_contract_matches_the_configured_manifest()
    {
        foreach ((string property, string pattern) in AcceptedNamingContract.Patterns)
        {
            pattern.ShouldContain("{", customMessage: $"{property} must be a named-token pattern.");
            pattern.ShouldNotContain("{0", customMessage: $"{property} must not be positional.");
        }

        IReadOnlyDictionary<string, string>? manifest =
            AcceptedNamingContract.ReadConfiguredManifestPatterns();
        if (manifest is null)
        {
            // No baseline on this machine: the accepted manifest is signed evidence held
            // outside the repository, so it is legitimately absent on a developer box.
            return;
        }

        foreach ((string property, string expected) in AcceptedNamingContract.Patterns)
        {
            manifest.ShouldContainKey(
                property, $"the configured manifest must supply {property}.");
            manifest[property].ShouldBe(
                expected,
                $"the accepted manifest's {property} is the contract; the implementation conforms to it, " +
                "never the other way round.");
        }
    }

    /// <summary>
    /// Loads the accepted naming values the way production does: a manifest on disk, hash
    /// verified, read by <see cref="WorkstationPresetProvider"/>.
    /// </summary>
    /// <remarks>
    /// The manifest written here carries the accepted <c>storageAndNamingContract</c> patterns
    /// under a synthetic identity, because the signed production manifest is never copied into
    /// the repository (task §43, §50). The naming values — the whole subject of these tests —
    /// are the real ones, and <see cref="The_transcribed_contract_matches_the_configured_manifest"/>
    /// is what keeps that true.
    /// </remarks>
    private static NamingPatternSet LoadThroughTheProductionProvider()
    {
        using TempWorkspace workspace = new();
        (string path, Sha256 hash) = PresetFixture.Write(workspace.Root);

        WorkstationPresetProvider provider =
            new(path, PresetFixture.PresetId, PresetFixture.PresetVersion, hash);

        OperationResult<NamingPatternSet> patterns = provider.GetNamingPatterns();
        patterns.IsSuccess.ShouldBeTrue(patterns.IsFailure ? patterns.Failure.ToString() : string.Empty);
        return patterns.Value;
    }
}
