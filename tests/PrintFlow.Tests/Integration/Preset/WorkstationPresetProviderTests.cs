using System.IO;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Preset;
using PrintFlow.Tests.Fixtures;

namespace PrintFlow.Tests.Integration.Preset;

/// <summary>
/// Task 11100.0: real signed workstation preset loading and hash verification, against a
/// synthetic fixture only (task §43). The real production manifest is never used in a test.
/// </summary>
public sealed class WorkstationPresetProviderTests
{
    [Fact]
    public void Valid_manifest_verifies_and_exposes_naming_patterns()
    {
        using TempWorkspace workspace = new();
        (string path, Sha256 hash) = PresetFixture.Write(workspace.Root);

        WorkstationPresetProvider provider = new(path, PresetFixture.PresetId, PresetFixture.PresetVersion, hash);

        OperationResult<ProductionPresetRef> reference = provider.GetVerifiedPreset();
        reference.IsSuccess.ShouldBeTrue();
        reference.Value.PresetId.ShouldBe(PresetFixture.PresetId);
        reference.Value.ManifestSha256.ShouldBe(hash);

        OperationResult<NamingPatternSet> patterns = provider.GetNamingPatterns();
        patterns.IsSuccess.ShouldBeTrue();
        patterns.Value.EnhancedPattern.ShouldBe("{0}_HD.png");
        patterns.Value.ProductionTiffPattern.ShouldBe("{0}_{1}mm_CMYK_W.tif");
    }

    [Fact]
    public void Hash_mismatch_fails_closed()
    {
        using TempWorkspace workspace = new();
        (string path, Sha256 _) = PresetFixture.Write(workspace.Root);
        Sha256 wrongHash = Sha256.Parse(new string('0', 64));

        WorkstationPresetProvider provider = new(path, PresetFixture.PresetId, PresetFixture.PresetVersion, wrongHash);

        OperationResult<ProductionPresetRef> reference = provider.GetVerifiedPreset();
        reference.IsFailure.ShouldBeTrue();
        reference.Failure.Code.ShouldBe(FailureCode.PresetHashMismatch);
    }

    [Fact]
    public void Missing_file_fails_closed()
    {
        using TempWorkspace workspace = new();
        string missingPath = Path.Combine(workspace.Root, "does-not-exist.json");

        WorkstationPresetProvider provider = new(
            missingPath, PresetFixture.PresetId, PresetFixture.PresetVersion, Sha256.Parse(new string('A', 64)));

        OperationResult<ProductionPresetRef> reference = provider.GetVerifiedPreset();
        reference.IsFailure.ShouldBeTrue();
        reference.Failure.Code.ShouldBe(FailureCode.EnvironmentNotVerified);
    }

    [Fact]
    public void Invalid_json_fails_closed()
    {
        using TempWorkspace workspace = new();
        string path = Path.Combine(workspace.Root, "broken.json");
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes("{ not valid json");
        File.WriteAllBytes(path, bytes);
        Sha256 hash = Sha256.FromBytes(System.Security.Cryptography.SHA256.HashData(bytes));

        WorkstationPresetProvider provider = new(path, PresetFixture.PresetId, PresetFixture.PresetVersion, hash);

        OperationResult<ProductionPresetRef> reference = provider.GetVerifiedPreset();
        reference.IsFailure.ShouldBeTrue();
        reference.Failure.Code.ShouldBe(FailureCode.EnvironmentNotVerified);
    }

    [Fact]
    public void Provider_never_writes_or_reformats_the_fixture()
    {
        using TempWorkspace workspace = new();
        (string path, Sha256 hash) = PresetFixture.Write(workspace.Root);
        byte[] before = File.ReadAllBytes(path);
        DateTime beforeWrite = File.GetLastWriteTimeUtc(path);

        WorkstationPresetProvider provider = new(path, PresetFixture.PresetId, PresetFixture.PresetVersion, hash);
        provider.GetVerifiedPreset().IsSuccess.ShouldBeTrue();
        provider.GetNamingPatterns().IsSuccess.ShouldBeTrue();

        File.ReadAllBytes(path).ShouldBe(before);
        File.GetLastWriteTimeUtc(path).ShouldBe(beforeWrite);
    }

    [Fact]
    public void Result_is_cached_after_first_verification()
    {
        using TempWorkspace workspace = new();
        (string path, Sha256 hash) = PresetFixture.Write(workspace.Root);
        WorkstationPresetProvider provider = new(path, PresetFixture.PresetId, PresetFixture.PresetVersion, hash);

        provider.GetVerifiedPreset().IsSuccess.ShouldBeTrue();

        // Mutating the file after first verification must not retroactively change a cached
        // result — the signed manifest is treated as evidence that does not change mid-process.
        File.WriteAllText(path, "corrupted");
        provider.GetVerifiedPreset().IsSuccess.ShouldBeTrue();
    }
}
