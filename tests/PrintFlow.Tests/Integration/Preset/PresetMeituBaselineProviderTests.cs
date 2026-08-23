using System.IO;
using System.Security.Cryptography;
using System.Text;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Adapters.Meitu;
using PrintFlow.Tests.Fixtures;

namespace PrintFlow.Tests.Integration.Preset;

/// <summary>
/// The signed chain from <c>appsettings.json</c> to the Meitu recognition markers
/// (Epic 11300 Part A §7, §2).
/// </summary>
/// <remarks>
/// Every fixture here is synthetic: fake paths, fake digests, a temp directory. The signed
/// production manifest is never copied into the repository, and these tests would pass on a
/// machine that has never had Meitu installed.
/// </remarks>
public sealed class PresetMeituBaselineProviderTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    private string Write(string fileName, string json)
    {
        string path = Path.Combine(_workspace.Root, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(json));
        return path;
    }

    private static Sha256 HashOf(string path) =>
        Sha256.FromBytes(SHA256.HashData(File.ReadAllBytes(path)));

    /// <summary>Writes a clean-start evidence file and returns its path and digest.</summary>
    private (string Path, Sha256 Sha256) WriteEvidence(params string[] markers)
    {
        string json = $$"""
            {
              "windowTitle": "美图秀秀",
              "stableStructuralMarkers": [{{string.Join(", ", markers.Select(m => $"\"{m}\""))}}]
            }
            """;

        string path = Write(@"Baseline\apps\meitu\clean-start.json", json);
        return (path, HashOf(path));
    }

    private string WriteManifest(string? evidencePath, Sha256? evidenceHash, string? meituContract = null)
    {
        string integrity = evidencePath is null
            ? "[]"
            : $$"""
                [{ "path": {{System.Text.Json.JsonSerializer.Serialize(evidencePath)}}, "sha256": "{{evidenceHash}}" }]
                """;

        string contract = meituContract ?? """
            {
              "acceptedVersion": "9.9.9.9",
              "executablePath": "C:\\Fake\\XiuXiu.exe",
              "executableSha256": "1111111111111111111111111111111111111111111111111111111111111111",
              "uiLanguage": "zh-CN",
              "windowContract": { "acceptedObservedTitles": ["美图秀秀", "美图秀秀-图片编辑"] }
            }
            """;

        return Write("preset.json", $$"""
            {
              "presetId": "test-workstation-v1",
              "meituContract": {{contract}},
              "sourceManifestIntegrity": {{integrity}}
            }
            """);
    }

    [Fact]
    public void A_verified_chain_yields_the_executable_identity_and_the_signed_markers()
    {
        (string evidencePath, Sha256 evidenceHash) = WriteEvidence("图片编辑 / PhotoEditor entry", "抠图 entry");
        string manifest = WriteManifest(evidencePath, evidenceHash);

        PresetMeituBaselineProvider provider = new(manifest, HashOf(manifest));
        OperationResult<MeituBaseline> baseline = provider.GetVerifiedBaseline();

        baseline.IsSuccess.ShouldBeTrue();
        baseline.Value.ExecutablePath.ShouldBe(@"C:\Fake\XiuXiu.exe");
        baseline.Value.AcceptedVersion.ShouldBe("9.9.9.9");
        baseline.Value.AcceptedWindowTitles.ShouldContain("美图秀秀-图片编辑");
        baseline.Value.WelcomeWindowTitle.ShouldBe("美图秀秀");
        baseline.Value.WelcomeMarkers.ShouldBe(["图片编辑", "抠图"], ignoreOrder: true);
    }

    /// <summary>
    /// Evidence that records markers but no title carries no usable start-page signature.
    /// </summary>
    /// <remarks>
    /// Fails closed rather than falling back to the first accepted title: the accepted titles
    /// include the editor's, and defaulting to one of those would reintroduce exactly the
    /// confusion the exact-title rule exists to remove.
    /// </remarks>
    [Fact]
    public void Clean_start_evidence_with_no_window_title_is_refused()
    {
        string evidencePath = Write(@"Baseline\apps\meitu\clean-start.json", """
            { "stableStructuralMarkers": ["图片编辑 entry", "抠图 entry"] }
            """);
        string manifest = WriteManifest(evidencePath, HashOf(evidencePath));

        PresetMeituBaselineProvider provider = new(manifest, HashOf(manifest));
        OperationResult<MeituBaseline> baseline = provider.GetVerifiedBaseline();

        baseline.IsFailure.ShouldBeTrue();
        baseline.Failure.Code.ShouldBe(FailureCode.EnvironmentNotVerified);
    }

    [Fact]
    public void A_manifest_whose_hash_does_not_match_is_refused()
    {
        (string evidencePath, Sha256 evidenceHash) = WriteEvidence("图片编辑 entry", "抠图 entry");
        string manifest = WriteManifest(evidencePath, evidenceHash);

        PresetMeituBaselineProvider provider = new(
            manifest, Sha256.Parse("2222222222222222222222222222222222222222222222222222222222222222"));

        provider.GetVerifiedBaseline().Failure.Code.ShouldBe(FailureCode.PresetHashMismatch);
    }

    /// <summary>
    /// Tampering with the evidence file is caught even though the manifest is untouched.
    /// </summary>
    /// <remarks>
    /// This is the property that makes the chain worth having. The recognition markers decide
    /// whether PrintFlow believes it is looking at a safe screen, so an evidence file that could
    /// be edited independently would let anyone with write access to that directory redefine
    /// "safe".
    /// </remarks>
    [Fact]
    public void Editing_the_evidence_file_after_the_manifest_was_signed_is_refused()
    {
        (string evidencePath, Sha256 evidenceHash) = WriteEvidence("图片编辑 entry", "抠图 entry");
        string manifest = WriteManifest(evidencePath, evidenceHash);
        Sha256 manifestHash = HashOf(manifest);

        File.WriteAllText(evidencePath, """{ "stableStructuralMarkers": ["任何界面"] }""");

        PresetMeituBaselineProvider provider = new(manifest, manifestHash);
        provider.GetVerifiedBaseline().Failure.Code.ShouldBe(FailureCode.PresetHashMismatch);
    }

    [Fact]
    public void A_preset_that_vouches_for_no_evidence_file_has_no_recognition_signature()
    {
        string manifest = WriteManifest(evidencePath: null, evidenceHash: null);

        PresetMeituBaselineProvider provider = new(manifest, HashOf(manifest));
        OperationResult<MeituBaseline> baseline = provider.GetVerifiedBaseline();

        baseline.IsFailure.ShouldBeTrue();
        baseline.Failure.Code.ShouldBe(FailureCode.EnvironmentNotVerified);
    }

    [Fact]
    public void A_preset_with_no_meituContract_accepts_no_executable()
    {
        string manifest = Write("preset.json", """{ "presetId": "test-workstation-v1" }""");

        PresetMeituBaselineProvider provider = new(manifest, HashOf(manifest));
        OperationResult<MeituBaseline> baseline = provider.GetVerifiedBaseline();

        baseline.IsFailure.ShouldBeTrue();
        baseline.Failure.Code.ShouldBe(FailureCode.EnvironmentNotVerified);
    }

    /// <summary>
    /// A contract with a path but no usable digest is refused rather than trusted on the path.
    /// </summary>
    [Fact]
    public void A_meituContract_without_a_binary_digest_is_refused()
    {
        (string evidencePath, Sha256 evidenceHash) = WriteEvidence("图片编辑 entry", "抠图 entry");
        string manifest = WriteManifest(evidencePath, evidenceHash, meituContract: """
            { "executablePath": "C:\\Fake\\XiuXiu.exe", "executableSha256": null }
            """);

        PresetMeituBaselineProvider provider = new(manifest, HashOf(manifest));
        provider.GetVerifiedBaseline().Failure.Code.ShouldBe(FailureCode.MeituNotInstalled);
    }

    [Fact]
    public void A_missing_manifest_fails_closed()
    {
        PresetMeituBaselineProvider provider = new(
            Path.Combine(_workspace.Root, "absent.json"),
            Sha256.Parse("3333333333333333333333333333333333333333333333333333333333333333"));

        provider.GetVerifiedBaseline().Failure.Code.ShouldBe(FailureCode.EnvironmentNotVerified);
    }
}
