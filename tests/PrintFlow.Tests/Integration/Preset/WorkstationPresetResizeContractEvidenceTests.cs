using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using PrintFlow.Infrastructure.Configuration;

namespace PrintFlow.Tests.Integration.Preset;

/// <summary>
/// Focused checks over the configured immutable flexible-size contract. The production baseline
/// legitimately does not exist on every developer machine, so the checks are dormant there;
/// on the accepted workstation they verify the exact external files selected by appsettings.
/// </summary>
public sealed class WorkstationPresetResizeContractEvidenceTests
{
    [Fact]
    public void Configured_workstation_preset_is_the_immutable_v1_11_contract()
    {
        (PrintFlowConfiguration configuration, string manifestPath)? configured = ConfiguredBaseline();
        if (configured is null)
        {
            return;
        }

        configured.Value.configuration.Preset.Version.ShouldBe("1.11.0");
        configured.Value.configuration.Preset.Path.ShouldEndWith(
            @"Baseline\workstation-v1\preset\printflow-workstation-v1.11.0.json");
        configured.Value.configuration.Adapters.Mode.ShouldBe("Fake");

        Hash(configured.Value.manifestPath).ShouldBe(
            configured.Value.configuration.Preset.ExpectedSha256,
            StringCompareShould.IgnoreCase);
        File.GetAttributes(configured.Value.manifestPath)
            .HasFlag(FileAttributes.ReadOnly).ShouldBeTrue();
    }

    [Fact]
    public void Manifest_reverifies_every_inherited_and_resize_evidence_entry()
    {
        (PrintFlowConfiguration configuration, string manifestPath)? configured = ConfiguredBaseline();
        if (configured is null)
        {
            return;
        }

        using JsonDocument manifest = ReadJson(configured.Value.manifestPath);
        JsonElement root = manifest.RootElement;
        root.GetProperty("presetVersion").GetString().ShouldBe("1.11.0");
        root.GetProperty("supersedes").GetProperty("presetVersion").GetString().ShouldBe("1.10.0");
        root.GetProperty("supersedes").GetProperty("manifestSha256").GetString().ShouldBe(
            "27B5E9E08FD7F968ADA2AAD88758FD4CBD0C3F66C39F8CCA60CC36CF0EEDD033");

        JsonElement integrity = root.GetProperty("sourceManifestIntegrity");
        integrity.GetArrayLength().ShouldBe(23);

        bool foundResizeEvidence = false;
        bool foundFlexibleSizeEvidence = false;
        foreach (JsonElement entry in integrity.EnumerateArray())
        {
            string path = entry.GetProperty("path").GetString().ShouldNotBeNull();
            string expected = entry.GetProperty("sha256").GetString().ShouldNotBeNull();
            File.Exists(path).ShouldBeTrue($"immutable evidence must exist: {path}");
            Hash(path).ShouldBe(expected, StringCompareShould.IgnoreCase);

            if (path.EndsWith(@"apps\photoshop-2019\resize-contract.json", StringComparison.OrdinalIgnoreCase))
            {
                foundResizeEvidence = true;
                File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly).ShouldBeTrue();
            }

            if (path.EndsWith(
                @"apps\photoshop-2019\flexible-size-and-enlargement-contract.json",
                StringComparison.OrdinalIgnoreCase))
            {
                foundFlexibleSizeEvidence = true;
                File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly).ShouldBeTrue();
            }
        }

        foundResizeEvidence.ShouldBeTrue();
        foundFlexibleSizeEvidence.ShouldBeTrue();
    }

    [Fact]
    public void Resize_evidence_records_the_complete_fit_within_bounds_policy()
    {
        (PrintFlowConfiguration configuration, string manifestPath)? configured = ConfiguredBaseline();
        if (configured is null)
        {
            return;
        }

        using JsonDocument manifest = ReadJson(configured.Value.manifestPath);
        JsonElement resize = manifest.RootElement
            .GetProperty("productionGeometryContract")
            .GetProperty("resize");

        resize.GetProperty("operatorUnit").GetString().ShouldBe("MILLIMETRES");
        resize.GetProperty("dimensionSemantics").GetString().ShouldBe(
            "FIT_WITHIN_BOUNDS_V1_OR_TARGET_EDGE_V1");
        resize.GetProperty("resolutionPpi").GetInt32().ShouldBe(300);
        resize.GetProperty("operatorChoosesAuthoritativeAxis").GetBoolean().ShouldBeFalse();
        resize.GetProperty("operatorChoosesResamplingMethod").GetBoolean().ShouldBeFalse();
        resize.GetProperty("automaticLimitingEdge")
            .GetProperty("writeOnlyLimitingEdge").GetBoolean().ShouldBeTrue();
        resize.GetProperty("automaticLimitingEdge")
            .GetProperty("otherEdgeAuthority").GetString().ShouldBe("PHOTOSHOP_CONSTRAINED_PROPORTIONS");
        resize.GetProperty("alreadyWithinLimits")
            .GetProperty("resampleMethod").GetString().ShouldBe("NONE");
        resize.GetProperty("alreadyWithinLimits")
            .GetProperty("preservePixelDimensionsExactly").GetBoolean().ShouldBeTrue();
        resize.GetProperty("shrink")
            .GetProperty("resampleMethod").GetString().ShouldBe("BICUBICSHARPER");
        resize.GetProperty("limitsMillimetres")
            .GetProperty("A4").GetProperty("maxLongEdge").GetInt32().ShouldBe(280);
        resize.GetProperty("limitsMillimetres")
            .GetProperty("A5").GetProperty("maxLongEdge").GetInt32().ShouldBe(135);

        string evidenceRelativePath = resize.GetProperty("evidence")[0].GetString().ShouldNotBeNull();
        string evidencePath = Path.Combine(
            configured.Value.configuration.Workspace.Root,
            "Baseline",
            "workstation-v1",
            evidenceRelativePath.Replace('/', Path.DirectorySeparatorChar));
        using JsonDocument evidence = ReadJson(evidencePath);
        JsonElement evidenceRoot = evidence.RootElement;

        evidenceRoot.GetProperty("status").GetString().ShouldBe("ACCEPTED_IMMUTABLE");
        evidenceRoot.GetProperty("fitWithinBounds")
            .GetProperty("constrainProportionsRequired").GetBoolean().ShouldBeTrue();
        evidenceRoot.GetProperty("alreadyWithinLimits")
            .GetProperty("enlarge").GetBoolean().ShouldBeFalse();
        evidenceRoot.GetProperty("actualPhotoshopResultBecomesProductionPixelPair")
            .GetBoolean().ShouldBeTrue();
        evidenceRoot.GetProperty("comparisonImagesRequired").GetBoolean().ShouldBeFalse();
        evidenceRoot.GetProperty("comparisonPairsRequired").GetBoolean().ShouldBeFalse();
        evidenceRoot.GetProperty("productionResizeExecutionAcceptedByThisSlice")
            .GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public void Flexible_size_evidence_records_override_authority_and_verified_enlargement_policy()
    {
        (PrintFlowConfiguration configuration, string manifestPath)? configured = ConfiguredBaseline();
        if (configured is null)
        {
            return;
        }

        using JsonDocument manifest = ReadJson(configured.Value.manifestPath);
        JsonElement resize = manifest.RootElement
            .GetProperty("productionGeometryContract")
            .GetProperty("resize");

        resize.GetProperty("sizingModes")[0].GetString().ShouldBe("PRESET_FIT");
        resize.GetProperty("sizingModes")[1].GetString().ShouldBe("CUSTOM_TARGET_EDGE");
        resize.GetProperty("presetPolicy").GetProperty("namedPresetIsRecommendedDefault")
            .GetBoolean().ShouldBeTrue();
        resize.GetProperty("presetPolicy").GetProperty("explicitOverrideAllowed")
            .GetBoolean().ShouldBeTrue();
        resize.GetProperty("customTargetEdge").GetProperty("authoritativePhysicalValueCount")
            .GetInt32().ShouldBe(1);
        resize.GetProperty("classification")
            .GetProperty("presetLimitExceededSeparateFromSourceCapacityExceeded")
            .GetBoolean().ShouldBeTrue();
        resize.GetProperty("classification").GetProperty("automaticEnlargementAllowed")
            .GetBoolean().ShouldBeFalse();
        resize.GetProperty("enlarge").GetProperty("explicitPerImagePerSizeAuthorityRequired")
            .GetBoolean().ShouldBeTrue();
        resize.GetProperty("enlarge").GetProperty("photoshopDomIdentifier")
            .GetString().ShouldBe("ResampleMethod.PRESERVEDETAILS");
        resize.GetProperty("operatorChoosesResamplingMethod").GetBoolean().ShouldBeFalse();

        string evidencePath = Path.Combine(
            configured.Value.configuration.Workspace.Root,
            "Baseline",
            "workstation-v1",
            manifest.RootElement.GetProperty("photoshopContract")
                .GetProperty("flexibleSizeContractEvidence")
                .GetString()
                .ShouldNotBeNull()
                .Replace('/', Path.DirectorySeparatorChar));
        using JsonDocument evidence = ReadJson(evidencePath);
        JsonElement root = evidence.RootElement;

        root.GetProperty("status").GetString().ShouldBe("ACCEPTED_IMMUTABLE");
        root.GetProperty("geometryContract").GetProperty("resolutionPpi").GetInt32().ShouldBe(300);
        root.GetProperty("resizeDirections").GetProperty("RESOLUTION_ONLY")
            .GetProperty("internalPolicy").GetString().ShouldBe("NONE");
        root.GetProperty("resizeDirections").GetProperty("SHRINK")
            .GetProperty("internalPolicy").GetString().ShouldBe("BICUBICSHARPER");
        root.GetProperty("resizeDirections").GetProperty("ENLARGE")
            .GetProperty("acceptedPhotoshopDomIdentifier")
            .GetString().ShouldBe("ResampleMethod.PRESERVEDETAILS");
        root.GetProperty("liveTechnicalVerification").GetProperty("status")
            .GetString().ShouldBe("PASS");
        root.GetProperty("liveTechnicalVerification").GetProperty("managedSyntheticSource")
            .GetProperty("sha256Before").GetString().ShouldBe(
                root.GetProperty("liveTechnicalVerification").GetProperty("managedSyntheticSource")
                    .GetProperty("sha256After").GetString());
        root.GetProperty("futureImplementationBoundary")
            .GetProperty("productionPhotoshopResizeIncluded").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public void Superseded_v1_10_remains_immutable_and_exact()
    {
        (PrintFlowConfiguration configuration, string _)? configured = ConfiguredBaseline();
        if (configured is null)
        {
            return;
        }

        string path = Path.Combine(
            configured.Value.configuration.Workspace.Root,
            @"Baseline\workstation-v1\preset\printflow-workstation-v1.10.0.json");

        Hash(path).ShouldBe(
            "27B5E9E08FD7F968ADA2AAD88758FD4CBD0C3F66C39F8CCA60CC36CF0EEDD033",
            StringCompareShould.IgnoreCase);
        File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly).ShouldBeTrue();
    }

    private static (PrintFlowConfiguration Configuration, string ManifestPath)? ConfiguredBaseline()
    {
        string? repository = RepositoryRoot();
        if (repository is null)
        {
            return null;
        }

        PrintFlowConfiguration configuration = PrintFlowConfiguration.LoadFromFile(
            Path.Combine(repository, "appsettings.json"));
        string manifestPath = Path.Combine(configuration.Workspace.Root, configuration.Preset.Path);
        return File.Exists(manifestPath) ? (configuration, manifestPath) : null;
    }

    private static JsonDocument ReadJson(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return JsonDocument.Parse(stream);
    }

    private static string Hash(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string? RepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "PrintFlowStudio.sln")))
        {
            current = current.Parent;
        }

        return current?.FullName;
    }
}
