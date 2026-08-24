using System.IO;
using System.Security.Cryptography;
using System.Text;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Adapters.Meitu;
using PrintFlow.Infrastructure.Automation;
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

    /// <summary>Extra files the manifest should vouch for, beyond the clean-start evidence.</summary>
    /// <remarks>
    /// Part B1 adds optional evidence, and "optional" has two very different absences: not listed
    /// (the state stays unreachable) and listed-but-broken (the whole chain fails). Both need to
    /// be constructible, so the manifest builder takes the extra entries rather than assuming
    /// them.
    /// </remarks>
    private readonly List<(string Path, Sha256 Sha256)> _extraIntegrity = [];

    private string WriteManifest(string? evidencePath, Sha256? evidenceHash, string? meituContract = null)
    {
        List<string> entries = [];
        if (evidencePath is not null)
        {
            entries.Add(
                $$"""{ "path": {{System.Text.Json.JsonSerializer.Serialize(evidencePath)}}, "sha256": "{{evidenceHash}}" }""");
        }

        foreach ((string path, Sha256 sha) in _extraIntegrity)
        {
            entries.Add(
                $$"""{ "path": {{System.Text.Json.JsonSerializer.Serialize(path)}}, "sha256": "{{sha}}" }""");
        }

        string integrity = $"[{string.Join(", ", entries)}]";

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

    // -----------------------------------------------------------------------------
    // Part B1 evidence: reachable only when the chain vouches for it (§9, §10, §23)
    // -----------------------------------------------------------------------------

    private const string CardEvidence = """
        {
          "cardTarget": {
            "labelControlType": "Text",
            "labelClassName": "QLabel",
            "labelAutomationIdSuffix": ".titleLabel",
            "cardControlType": "CheckBox",
            "cardClassName": "CardButton",
            "cardAutomationIdSuffix": ".CardButton",
            "requiredCardPattern": "Invoke"
          }
        }
        """;

    private const string EmptyEditorEvidence = """
        {
          "editor": {
            "windowTitle": "美图秀秀-图片编辑",
            "requiredMarkers": ["打开图片", "新建画布", "最近打开"],
            "minimumRequiredMarkers": 2,
            "fileNameLocation": "VisibleText",
            "openControl": {
              "name": "打开图片",
              "automationIdContains": "OpenMaskWidget",
              "controlType": "Button",
              "className": "QPushButton",
              "requiredPattern": "Invoke"
            }
          }
        }
        """;

    /// <summary>Registers an extra evidence file and vouches for it with its real digest.</summary>
    private void Vouch(string relativePath, string json)
    {
        string path = Write(relativePath, json);
        _extraIntegrity.Add((path, HashOf(path)));
    }

    private OperationResult<MeituBaseline> Load()
    {
        (string evidencePath, Sha256 evidenceHash) = WriteEvidence("图片编辑 entry", "抠图 entry");
        string manifest = WriteManifest(evidencePath, evidenceHash);
        return new PresetMeituBaselineProvider(manifest, HashOf(manifest)).GetVerifiedBaseline();
    }

    /// <summary>
    /// Evidence the preset does not list leaves its state unreachable, without failing.
    /// </summary>
    /// <remarks>
    /// The distinction that makes the whole scheme fail closed rather than merely fail. A chain
    /// that vouches for no empty-editor signature is a perfectly valid chain — it just cannot
    /// recognise that screen, so the screen stops. Turning it into an error instead would push
    /// people towards adding evidence to make the error go away.
    /// </remarks>
    [Fact]
    public void Evidence_the_preset_does_not_vouch_for_leaves_its_state_unreachable()
    {
        OperationResult<MeituBaseline> baseline = Load();

        baseline.IsSuccess.ShouldBeTrue();
        baseline.Value.StartPageCard.ShouldBeNull();
        baseline.Value.FileDialog.ShouldBeNull();
        baseline.Value.EditorEmpty.ShouldBeNull();
        baseline.Value.EditorWithWorkingCopy.ShouldBeNull();
    }

    [Fact]
    public void A_vouched_for_card_structure_is_read_from_the_signed_file()
    {
        Vouch(@"Baseline\apps\meitu\start-page-card-target.json", CardEvidence);

        MeituBaseline baseline = Load().Value;

        baseline.StartPageCard.ShouldNotBeNull();
        baseline.StartPageCard.CardClassName.ShouldBe("CardButton");
        baseline.StartPageCard.LabelAutomationIdSuffix.ShouldBe(".titleLabel");
        baseline.StartPageCard.RequiredCardPattern.ShouldBe(UiPatternKind.Invoke);
    }

    [Fact]
    public void A_vouched_for_empty_editor_is_read_with_its_open_control()
    {
        Vouch(@"Baseline\apps\meitu\editor-empty.json", EmptyEditorEvidence);

        MeituBaseline baseline = Load().Value;

        baseline.EditorEmpty.ShouldNotBeNull();
        baseline.EditorEmpty.WindowTitle.ShouldBe("美图秀秀-图片编辑");
        baseline.EditorEmpty.RequiredMarkers.Length.ShouldBe(3);
        baseline.EditorEmpty.OpenControl.ShouldNotBeNull();
        baseline.EditorEmpty.OpenControl.Name.ShouldBe("打开图片");
    }

    /// <summary>
    /// Changing a vouched-for evidence file's bytes breaks the whole chain.
    /// </summary>
    /// <remarks>
    /// The property the SHA-256 exists for, stated over the new files as well as the old. An
    /// attacker — or an over-helpful edit — that adds a marker to make a screen recognisable
    /// does not get a more permissive PrintFlow, it gets one that refuses to start.
    /// </remarks>
    [Fact]
    public void Editing_a_vouched_for_evidence_file_after_signing_fails_closed()
    {
        string path = Write(@"Baseline\apps\meitu\start-page-card-target.json", CardEvidence);
        _extraIntegrity.Add((path, HashOf(path)));

        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(
            CardEvidence.Replace("CardButton", "QWidget", StringComparison.Ordinal)));

        OperationResult<MeituBaseline> baseline = Load();

        baseline.IsFailure.ShouldBeTrue();
        baseline.Failure.Code.ShouldBe(FailureCode.PresetHashMismatch);
    }

    [Fact]
    public void A_vouched_for_evidence_file_that_is_missing_fails_closed()
    {
        string path = Write(@"Baseline\apps\meitu\editor-empty.json", EmptyEditorEvidence);
        _extraIntegrity.Add((path, HashOf(path)));
        File.Delete(path);

        Load().Failure.Code.ShouldBe(FailureCode.EnvironmentNotVerified);
    }

    [Fact]
    public void A_vouched_for_evidence_file_recorded_without_a_digest_fails_closed()
    {
        string path = Write(@"Baseline\apps\meitu\editor-empty.json", EmptyEditorEvidence);
        _extraIntegrity.Add((path, default));

        Load().Failure.Code.ShouldBe(FailureCode.EnvironmentNotVerified);
    }

    /// <summary>
    /// An editor signature with no positive markers is refused.
    /// </summary>
    /// <remarks>
    /// A signature that named only a window title would let the screen be recognised by the one
    /// property any application can claim, and — for the empty editor — would amount to
    /// recognising it by the absence of anything else (§15).
    /// </remarks>
    [Fact]
    public void An_editor_signature_with_no_positive_markers_is_refused()
    {
        Vouch(@"Baseline\apps\meitu\editor-empty.json", """
            {
              "editor": {
                "windowTitle": "美图秀秀-图片编辑",
                "requiredMarkers": [],
                "minimumRequiredMarkers": 1,
                "fileNameLocation": "VisibleText"
              }
            }
            """);

        Load().Failure.Code.ShouldBe(FailureCode.EnvironmentNotVerified);
    }

    [Fact]
    public void An_editor_signature_demanding_more_markers_than_it_lists_is_refused()
    {
        Vouch(@"Baseline\apps\meitu\editor-empty.json", """
            {
              "editor": {
                "windowTitle": "美图秀秀-图片编辑",
                "requiredMarkers": ["打开图片"],
                "minimumRequiredMarkers": 4,
                "fileNameLocation": "VisibleText"
              }
            }
            """);

        Load().Failure.Code.ShouldBe(FailureCode.EnvironmentNotVerified);
    }

    /// <summary>
    /// Evidence may only demand a pattern or a location PrintFlow has a reviewed way to use.
    /// </summary>
    /// <remarks>
    /// The closed enums are the boundary of what signed evidence can ask for. Without this,
    /// "signed" would mean "hash-protected" but not "reviewed", and a new evidence file could
    /// name behaviour no one had implemented or considered.
    /// </remarks>
    [Fact]
    public void Evidence_naming_a_pattern_PrintFlow_does_not_know_is_refused()
    {
        Vouch(@"Baseline\apps\meitu\start-page-card-target.json",
            CardEvidence.Replace("\"Invoke\"", "\"DragAndDrop\"", StringComparison.Ordinal));

        Load().Failure.Code.ShouldBe(FailureCode.EnvironmentNotVerified);
    }

    [Fact]
    public void Evidence_naming_a_file_name_location_PrintFlow_does_not_know_is_refused()
    {
        Vouch(@"Baseline\apps\meitu\editor-empty.json",
            EmptyEditorEvidence.Replace("\"VisibleText\"", "\"Anywhere\"", StringComparison.Ordinal));

        Load().Failure.Code.ShouldBe(FailureCode.EnvironmentNotVerified);
    }

    [Fact]
    public void A_card_structure_missing_a_field_the_rule_needs_is_refused()
    {
        Vouch(@"Baseline\apps\meitu\start-page-card-target.json", """
            {
              "cardTarget": {
                "labelControlType": "Text",
                "labelClassName": "QLabel",
                "labelAutomationIdSuffix": ".titleLabel",
                "requiredCardPattern": "Invoke"
              }
            }
            """);

        Load().Failure.Code.ShouldBe(FailureCode.EnvironmentNotVerified);
    }

    [Fact]
    public void A_picker_signature_missing_a_control_identity_is_refused()
    {
        Vouch(@"Baseline\apps\meitu\open-file-dialog.json", """
            {
              "fileDialog": {
                "kind": "windows-common-dialog",
                "windowClassName": "#32770",
                "fileNameAutomationId": "1148",
                "fileNameControlType": "Edit"
              }
            }
            """);

        Load().Failure.Code.ShouldBe(FailureCode.EnvironmentNotVerified);
    }

    [Fact]
    public void A_vouched_for_picker_signature_is_read_from_the_signed_file()
    {
        Vouch(@"Baseline\apps\meitu\open-file-dialog.json", """
            {
              "fileDialog": {
                "kind": "windows-common-dialog",
                "windowClassName": "#32770",
                "acceptedTitles": ["打开图片"],
                "fileNameAutomationId": "1148",
                "fileNameControlType": "Edit",
                "confirmAutomationId": "1",
                "confirmControlType": "Button"
              }
            }
            """);

        MeituBaseline baseline = Load().Value;

        baseline.FileDialog.ShouldNotBeNull();
        baseline.FileDialog.WindowClassName.ShouldBe("#32770");
        baseline.FileDialog.FileNameControlType.ShouldBe("Edit");
        baseline.FileDialog.ConfirmAutomationId.ShouldBe("1");
    }
}
