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

    private const string DocumentIdentityEvidence = """
        {
          "editor": {
            "windowTitle": "美图秀秀-图片编辑",
            "requiredMarkers": ["关闭图片", "保存", "基础调整"],
            "minimumRequiredMarkers": 2,
            "fileNameLocation": "VisibleText"
          },
          "documentIdentity": {
            "saveMarkerName": "保存",
            "saveControl": {
              "labelControlType": "Text",
              "labelClassName": "QLabel",
              "labelAutomationIdSuffix": ".textLabel",
              "cardControlType": "Button",
              "cardClassName": "OptionButton",
              "cardAutomationIdSuffix": ".saveButton",
              "requiredCardPattern": "Invoke"
            },
            "dialogTitle": "Form",
            "dialogClassName": "QtSaveDialog",
            "fileNameControl": {
              "name": "",
              "automationIdContains": ".wName.fileNameEdit",
              "controlType": "Edit",
              "className": "QLineEdit",
              "requiredPattern": "Value"
            },
            "cancelControl": {
              "name": "",
              "automationIdContains": ".titleFrame.closeButton",
              "controlType": "Button",
              "className": "IconFontButton",
              "requiredPattern": "Invoke"
            },
            "outputBaseNameSuffix": "_副本"
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

    [Fact]
    public void A_vouched_for_document_identity_route_is_read_from_the_signed_file()
    {
        Vouch(@"Baseline\apps\meitu\editor-with-working-copy.json", DocumentIdentityEvidence);

        MeituBaseline baseline = Load().Value;

        baseline.DocumentIdentity.ShouldNotBeNull();
        baseline.DocumentIdentity.SaveMarkerName.ShouldBe("保存");
        baseline.DocumentIdentity.DialogClassName.ShouldBe("QtSaveDialog");
        baseline.DocumentIdentity.FileNameControl.RequiredPattern.ShouldBe(UiPatternKind.Value);
        baseline.DocumentIdentity.CancelControl.RequiredPattern.ShouldBe(UiPatternKind.Invoke);
        baseline.DocumentIdentity.OutputBaseNameSuffix.ShouldBe("_副本");
    }

    [Fact]
    public void An_incomplete_vouched_for_document_identity_route_fails_closed()
    {
        Vouch(
            @"Baseline\apps\meitu\editor-with-working-copy.json",
            DocumentIdentityEvidence.Replace(
                "\"automationIdContains\": \".wName.fileNameEdit\"",
                "\"automationIdContains\": \"\"",
                StringComparison.Ordinal));

        Load().Failure.Code.ShouldBe(FailureCode.EnvironmentNotVerified);
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

    // -----------------------------------------------------------------------------
    // Part B2A evidence: the Enhancement route and the close route (§3, §9)
    // -----------------------------------------------------------------------------

    private const string CloseDocumentEvidence = """
        {
          "closeDocument": {
            "markerName": "关闭图片",
            "control": {
              "labelControlType": "Text",
              "labelClassName": "QLabel",
              "labelAutomationIdSuffix": ".textLabel",
              "cardControlType": "Button",
              "cardClassName": "IconTextButton",
              "cardAutomationIdSuffix": ".closeButton",
              "requiredCardPattern": "Invoke"
            }
          }
        }
        """;

    private const string EnhancementEvidence = """
        {
          "enhancement": {
            "actionMarkerName": "AI变清晰",
            "actionControl": {
              "markerControlType": "Text",
              "markerClassName": "QLabel",
              "markerAutomationIdSuffix": ".titleLabel",
              "ownerControlType": "CheckBox",
              "ownerClassName": "ModuleButton",
              "ownerAutomationIdSuffix": ".ModuleButton",
              "markerRelativeAutomationIdSuffix": ".buttonWidget.titleLabel",
              "ownerAncestorDepth": 2,
              "requiredOwnerPattern": "Invoke"
            },
            "busy": {
              "requiredMarkers": ["变清晰中", "变清晰时长", "取消"],
              "minimumRequiredMarkers": 2
            },
            "completion": {
              "requiredMarkers": ["AI超清", "高清", "保持原尺寸", "重置", "批量AI变清晰"],
              "minimumRequiredMarkers": 4,
              "requiresBusyAbsent": true
            }
          }
        }
        """;

    private const string BackgroundRemovalEvidence = """
        {
          "backgroundRemoval": {
            "actionMarkerName": "抠图",
            "actionControl": {
              "markerControlType": "CheckBox",
              "markerClassName": "PageButton",
              "markerAutomationIdSuffix": "",
              "ownerControlType": "CheckBox",
              "ownerClassName": "PageButton",
              "ownerAutomationIdSuffix": "",
              "markerRelativeAutomationIdSuffix": "",
              "ownerAncestorDepth": 0,
              "requiredOwnerPattern": "Invoke"
            },
            "returnMarkerName": "调整",
            "returnControl": {
              "markerControlType": "CheckBox",
              "markerClassName": "PageButton",
              "markerAutomationIdSuffix": "",
              "ownerControlType": "CheckBox",
              "ownerClassName": "PageButton",
              "ownerAutomationIdSuffix": "",
              "markerRelativeAutomationIdSuffix": "",
              "ownerAncestorDepth": 0,
              "requiredOwnerPattern": "Invoke"
            },
            "observedAutomaticModeName": "自动选择",
            "modePolicy": "OPERATOR_OR_REVIEWED_CONTENT_DECISION",
            "autoStartsOnEntry": true,
            "busy": {
              "requiredMarkers": ["智能识别中...", "返回结果中...", "图片合成中...", "取消"],
              "minimumRequiredMarkers": 2
            },
            "completion": {
              "requiredMarkers": ["自动选择", "局部抠图", "手动修补", "反选", "移除背景"],
              "minimumRequiredMarkers": 5,
              "requiresBusyAbsent": true
            }
          }
        }
        """;

    /// <summary>Returns the enhancement evidence with one JSON fragment replaced.</summary>
    private static string EnhancementWith(string find, string replace) =>
        EnhancementEvidence.Replace(find, replace, StringComparison.Ordinal);

    private static string BackgroundRemovalWith(string find, string replace) =>
        BackgroundRemovalEvidence.Replace(find, replace, StringComparison.Ordinal);

    [Fact]
    public void A_vouched_for_close_route_is_read_from_the_signed_file()
    {
        Vouch(@"Baseline\apps\meitu\editor-close-document.json", CloseDocumentEvidence);

        MeituBaseline baseline = Load().Value;

        baseline.CloseDocument.ShouldNotBeNull();
        baseline.CloseDocument.MarkerName.ShouldBe("关闭图片");
        baseline.CloseDocument.Control.CardClassName.ShouldBe("IconTextButton");
        baseline.CloseDocument.Control.CardAutomationIdSuffix.ShouldBe(".closeButton");
    }

    [Fact]
    public void A_vouched_for_Enhancement_route_is_read_from_the_signed_file()
    {
        Vouch(@"Baseline\apps\meitu\editor-enhancement.json", EnhancementEvidence);

        MeituBaseline baseline = Load().Value;

        baseline.Enhancement.ShouldNotBeNull();
        baseline.Enhancement.ActionMarkerName.ShouldBe("AI变清晰");
        baseline.Enhancement.ActionControl.OwnerAncestorDepth.ShouldBe(2);
        baseline.Enhancement.ActionControl.OwnerClassName.ShouldBe("ModuleButton");
        baseline.Enhancement.ActionControl.MarkerRelativeAutomationIdSuffix
            .ShouldBe(".buttonWidget.titleLabel");
        baseline.Enhancement.Busy.RequiredMarkers.Length.ShouldBe(3);
        baseline.Enhancement.Busy.MinimumRequiredMarkers.ShouldBe(2);
        baseline.Enhancement.Completion.MinimumRequiredMarkers.ShouldBe(4);
        baseline.Enhancement.Completion.RequiresBusyAbsent.ShouldBeTrue();
    }

    [Fact]
    public void A_vouched_for_Background_Removal_route_is_read_from_the_signed_file()
    {
        Vouch(@"Baseline\apps\meitu\editor-background-removal.json", BackgroundRemovalEvidence);

        MeituBackgroundRemovalSignature signature = Load().Value.BackgroundRemoval.ShouldNotBeNull();

        signature.ActionMarkerName.ShouldBe("抠图");
        signature.ActionControl.OwnerAncestorDepth.ShouldBe(0);
        signature.ActionControl.OwnerClassName.ShouldBe("PageButton");
        signature.ReturnMarkerName.ShouldBe("调整");
        signature.ObservedAutomaticModeName.ShouldBe("自动选择");
        signature.ModePolicy.ShouldBe(
            MeituBackgroundRemovalModePolicy.OperatorOrReviewedContentDecision);
        signature.AutoStartsOnEntry.ShouldBeTrue();
        signature.Busy.MinimumRequiredMarkers.ShouldBe(2);
        signature.Completion.MinimumRequiredMarkers.ShouldBe(5);
        signature.Completion.RequiresBusyAbsent.ShouldBeTrue();
    }

    [Fact]
    public void Without_vouched_for_B2A_evidence_both_routes_are_unreachable()
    {
        MeituBaseline baseline = Load().Value;

        baseline.CloseDocument.ShouldBeNull();
        baseline.Enhancement.ShouldBeNull();
        baseline.BackgroundRemoval.ShouldBeNull();
    }

    [Fact]
    public void Background_Removal_evidence_must_preserve_the_signed_mode_policy()
    {
        Vouch(
            @"Baseline\apps\meitu\editor-background-removal.json",
            BackgroundRemovalWith(
                "OPERATOR_OR_REVIEWED_CONTENT_DECISION",
                "ALWAYS_PERSON"));

        OperationResult<MeituBaseline> baseline = Load();

        baseline.IsFailure.ShouldBeTrue();
        baseline.Failure.TechnicalDetail.ShouldContain("mode policy");
    }

    [Fact]
    public void Background_Removal_evidence_must_record_that_entry_auto_starts_processing()
    {
        Vouch(
            @"Baseline\apps\meitu\editor-background-removal.json",
            BackgroundRemovalWith("\"autoStartsOnEntry\": true", "\"autoStartsOnEntry\": false"));

        OperationResult<MeituBaseline> baseline = Load();

        baseline.IsFailure.ShouldBeTrue();
        baseline.Failure.TechnicalDetail.ShouldContain("auto-starts");
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("5")]
    public void Background_Removal_evidence_refuses_an_unreviewed_structural_depth(string depth)
    {
        Vouch(
            @"Baseline\apps\meitu\editor-background-removal.json",
            BackgroundRemovalWith(
                "\"ownerAncestorDepth\": 0",
                $"\"ownerAncestorDepth\": {depth}"));

        OperationResult<MeituBaseline> baseline = Load();

        baseline.IsFailure.ShouldBeTrue();
        baseline.Failure.TechnicalDetail.ShouldContain("ownerAncestorDepth");
    }

    [Fact]
    public void Editing_the_Background_Removal_evidence_after_signing_fails_closed()
    {
        Vouch(@"Baseline\apps\meitu\editor-background-removal.json", BackgroundRemovalEvidence);
        Write(
            @"Baseline\apps\meitu\editor-background-removal.json",
            BackgroundRemovalWith("\"minimumRequiredMarkers\": 5", "\"minimumRequiredMarkers\": 1"));

        OperationResult<MeituBaseline> baseline = Load();

        baseline.IsFailure.ShouldBeTrue();
        baseline.Failure.Code.ShouldBe(FailureCode.PresetHashMismatch);
    }

    /// <summary>
    /// Evidence that lists no positive Busy markers is refused rather than read down.
    /// </summary>
    /// <remarks>
    /// A Busy signature with nothing in it would be satisfied by an automation read that
    /// returned nothing at all, and Busy outranks every content state — so an empty signature
    /// would make PrintFlow report a failed read as "Meitu is working".
    /// </remarks>
    [Fact]
    public void Enhancement_evidence_with_no_positive_Busy_markers_is_refused()
    {
        Vouch(
            @"Baseline\apps\meitu\editor-enhancement.json",
            EnhancementWith("[\"变清晰中\", \"变清晰时长\", \"取消\"]", "[]"));

        OperationResult<MeituBaseline> baseline = Load();

        baseline.IsFailure.ShouldBeTrue();
        baseline.Failure.Code.ShouldBe(FailureCode.EnvironmentNotVerified);
        baseline.Failure.TechnicalDetail.ShouldContain("Busy");
    }

    /// <summary>
    /// Evidence that lists no positive completion markers is refused.
    /// </summary>
    /// <remarks>
    /// The mirror hazard, and the worse one: an empty completion signature would let a run be
    /// declared finished on the strength of nothing having been seen.
    /// </remarks>
    [Fact]
    public void Enhancement_evidence_with_no_positive_completion_markers_is_refused()
    {
        Vouch(
            @"Baseline\apps\meitu\editor-enhancement.json",
            EnhancementWith(
                "[\"AI超清\", \"高清\", \"保持原尺寸\", \"重置\", \"批量AI变清晰\"]", "[]"));

        OperationResult<MeituBaseline> baseline = Load();

        baseline.IsFailure.ShouldBeTrue();
        baseline.Failure.TechnicalDetail.ShouldContain("completion");
    }

    [Fact]
    public void Enhancement_evidence_demanding_more_markers_than_it_lists_is_refused()
    {
        Vouch(
            @"Baseline\apps\meitu\editor-enhancement.json",
            EnhancementWith("\"minimumRequiredMarkers\": 4", "\"minimumRequiredMarkers\": 9"));

        Load().IsFailure.ShouldBeTrue();
    }

    /// <summary>
    /// A walk depth outside the reviewed range is refused.
    /// </summary>
    /// <remarks>
    /// Depth zero would make the text marker its own action target — the Part A defect written
    /// into evidence. A large depth would leave the marker's own subtree entirely and reach the
    /// window, which is no longer a rule anchored to the marker at all.
    /// </remarks>
    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("20")]
    public void Enhancement_evidence_with_an_owner_depth_outside_the_reviewed_range_is_refused(string depth)
    {
        Vouch(
            @"Baseline\apps\meitu\editor-enhancement.json",
            EnhancementWith("\"ownerAncestorDepth\": 2", $"\"ownerAncestorDepth\": {depth}"));

        OperationResult<MeituBaseline> baseline = Load();

        baseline.IsFailure.ShouldBeTrue();
        baseline.Failure.TechnicalDetail.ShouldContain("ownerAncestorDepth");
    }

    [Fact]
    public void Enhancement_evidence_missing_a_field_the_action_rule_needs_is_refused()
    {
        Vouch(
            @"Baseline\apps\meitu\editor-enhancement.json",
            EnhancementWith("\"ownerClassName\": \"ModuleButton\",", string.Empty));

        Load().IsFailure.ShouldBeTrue();
    }

    /// <summary>
    /// Whether Busy and completion may overlap has to be stated, not defaulted.
    /// </summary>
    /// <remarks>
    /// It is a property of the application. Defaulting it either way would put a guess about
    /// Meitu's behaviour into PrintFlow rather than into the evidence.
    /// </remarks>
    [Fact]
    public void Enhancement_evidence_that_does_not_state_the_Busy_overlap_rule_is_refused()
    {
        Vouch(
            @"Baseline\apps\meitu\editor-enhancement.json",
            EnhancementWith("\"requiresBusyAbsent\": true", "\"requiresBusyAbsent\": \"yes\""));

        OperationResult<MeituBaseline> baseline = Load();

        baseline.IsFailure.ShouldBeTrue();
        baseline.Failure.TechnicalDetail.ShouldContain("Busy");
    }

    [Fact]
    public void Enhancement_evidence_naming_a_pattern_PrintFlow_does_not_know_is_refused()
    {
        Vouch(
            @"Baseline\apps\meitu\editor-enhancement.json",
            EnhancementWith("\"requiredOwnerPattern\": \"Invoke\"", "\"requiredOwnerPattern\": \"Drag\""));

        Load().IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Close_evidence_without_a_marker_to_anchor_the_walk_is_refused()
    {
        Vouch(
            @"Baseline\apps\meitu\editor-close-document.json",
            CloseDocumentEvidence.Replace("\"markerName\": \"关闭图片\",", string.Empty, StringComparison.Ordinal));

        OperationResult<MeituBaseline> baseline = Load();

        baseline.IsFailure.ShouldBeTrue();
        baseline.Failure.TechnicalDetail.ShouldContain("markerName");
    }

    /// <summary>Editing signed B2A evidence after the manifest was written fails closed.</summary>
    [Fact]
    public void Editing_the_Enhancement_evidence_after_signing_fails_closed()
    {
        Vouch(@"Baseline\apps\meitu\editor-enhancement.json", EnhancementEvidence);
        Write(
            @"Baseline\apps\meitu\editor-enhancement.json",
            EnhancementWith("\"minimumRequiredMarkers\": 2", "\"minimumRequiredMarkers\": 1"));

        OperationResult<MeituBaseline> baseline = Load();

        baseline.IsFailure.ShouldBeTrue();
        baseline.Failure.Code.ShouldBe(FailureCode.PresetHashMismatch);
    }

    // -----------------------------------------------------------------------------
    // The export route (Epic 11300 Part B2B §39)
    // -----------------------------------------------------------------------------

    private const string ExportEvidence = """
        {
          "export": {
            "surfaceTitle": "Form",
            "surfaceClassName": "Qt51517QWindowToolSaveBits",
            "fileNameControl": {
              "name": "",
              "automationIdContains": ".SaveMaskWidget.widgetRight.wName.fileNameEdit",
              "controlType": "Edit",
              "className": "QLineEdit",
              "requiredPattern": "Value"
            },
            "formatControl": {
              "name": "",
              "automationIdContains": ".SaveMaskWidget.widgetRight.wName.formatCombo",
              "controlType": "ComboBox",
              "className": "proui::NoAnimationComboBox",
              "requiredPattern": "Value"
            },
            "requiredFormatValue": "png",
            "saveAsControl": {
              "name": "另存为",
              "automationIdContains": ".SaveMaskWidget.widgetRight.saveAsButton",
              "controlType": "Button",
              "className": "QPushButton",
              "requiredPattern": "Invoke"
            },
            "destinationDialog": {
              "windowClassName": "#32770",
              "acceptedTitles": ["图片另存为"],
              "fileNameAutomationId": "1001",
              "fileNameControlType": "Edit",
              "confirmAutomationId": "1",
              "confirmControlType": "Button",
              "cancelAutomationId": "2",
              "cancelControlType": "Button"
            },
            "resultSurface": {
              "recognition": {
                "requiredMarkers": ["保存成功", "打开所在文件夹"],
                "minimumRequiredMarkers": 2
              },
              "closeControl": {
                "name": "",
                "automationIdContains": ".SaveResultMaskWidget.titleFrame.closeButton",
                "controlType": "Button",
                "className": "proui::IconFontButton",
                "requiredPattern": "Invoke"
              }
            }
          }
        }
        """;

    private static string ExportWith(string find, string replace) =>
        ExportEvidence.Replace(find, replace, StringComparison.Ordinal);

    [Fact]
    public void A_vouched_for_export_route_is_read_from_the_signed_file()
    {
        Vouch(@"Baseline\apps\meitu\editor-export.json", ExportEvidence);

        MeituBaseline baseline = Load().Value;

        baseline.Export.ShouldNotBeNull();
        baseline.Export.SurfaceClassName.ShouldBe("Qt51517QWindowToolSaveBits");
        baseline.Export.RequiredFormatValue.ShouldBe("png");
        baseline.Export.SaveAsControl.Name.ShouldBe("另存为");
        baseline.Export.Destination.WindowClassName.ShouldBe("#32770");
        baseline.Export.Destination.FileNameAutomationId.ShouldBe("1001");
        baseline.Export.Destination.CancelAutomationId.ShouldBe("2");
        baseline.Export.Result.RequiredMarkers.Length.ShouldBe(2);
        baseline.Export.Result.CloseControl.AutomationIdContains
            .ShouldBe(".SaveResultMaskWidget.titleFrame.closeButton");
    }

    /// <summary>
    /// Without vouched-for export evidence the route stays unreachable, so nothing can be
    /// exported and nothing can succeed.
    /// </summary>
    [Fact]
    public void Without_vouched_for_export_evidence_the_route_is_unreachable()
    {
        Load().Value.Export.ShouldBeNull();
    }

    /// <summary>
    /// Evidence that records no required format is refused rather than defaulted to PNG.
    /// </summary>
    /// <remarks>
    /// §11 forbids inferring the format from the output extension when the surface carries its
    /// own selector. Defaulting here would put that inference back, one layer down and out of
    /// sight of the code that was written to avoid it.
    /// </remarks>
    [Fact]
    public void Export_evidence_with_no_required_format_value_is_refused()
    {
        Vouch(
            @"Baseline\apps\meitu\editor-export.json",
            ExportWith("\"requiredFormatValue\": \"png\",", string.Empty));

        OperationResult<MeituBaseline> baseline = Load();

        baseline.IsFailure.ShouldBeTrue();
        baseline.Failure.TechnicalDetail.ShouldContain("format");
    }

    /// <summary>
    /// Export evidence with no destination dialog is refused: there would be nowhere to place
    /// the result.
    /// </summary>
    [Fact]
    public void Export_evidence_with_no_destination_dialog_is_refused()
    {
        Vouch(
            @"Baseline\apps\meitu\editor-export.json",
            ExportWith("\"destinationDialog\"", "\"unusedDialog\""));

        OperationResult<MeituBaseline> baseline = Load();

        baseline.IsFailure.ShouldBeTrue();
        baseline.Failure.TechnicalDetail.ShouldContain("destinationDialog");
    }

    /// <summary>
    /// A destination dialog with no cancel control is refused alongside the other two.
    /// </summary>
    /// <remarks>
    /// Not a completeness reflex. A route that can raise this dialog but not back out of it would
    /// have to leave a modal on the operator's screen every time the export could not proceed —
    /// so the ability to refuse safely is part of the evidence, not an extra.
    /// </remarks>
    [Fact]
    public void A_destination_dialog_with_no_cancel_control_is_refused()
    {
        Vouch(
            @"Baseline\apps\meitu\editor-export.json",
            ExportWith("\"cancelAutomationId\": \"2\",", string.Empty));

        OperationResult<MeituBaseline> baseline = Load();

        baseline.IsFailure.ShouldBeTrue();
        baseline.Failure.TechnicalDetail.ShouldContain("cancel control");
    }

    [Fact]
    public void Export_evidence_with_no_accepted_destination_title_is_refused()
    {
        Vouch(
            @"Baseline\apps\meitu\editor-export.json",
            ExportWith("\"acceptedTitles\": [\"图片另存为\"],", "\"acceptedTitles\": [],"));

        Load().IsFailure.ShouldBeTrue();
    }

    /// <summary>
    /// Result-surface evidence with no positive markers is refused.
    /// </summary>
    /// <remarks>
    /// The Save surface and the result surface share a window class and a title, so markers are
    /// the only thing that tells them apart. Evidence without them would license PrintFlow to
    /// dismiss whichever owned surface it found — including the modified-document prompt §25
    /// forbids answering.
    /// </remarks>
    [Fact]
    public void Export_evidence_with_no_positive_result_markers_is_refused()
    {
        Vouch(
            @"Baseline\apps\meitu\editor-export.json",
            ExportWith("[\"保存成功\", \"打开所在文件夹\"]", "[]"));

        OperationResult<MeituBaseline> baseline = Load();

        baseline.IsFailure.ShouldBeTrue();
        baseline.Failure.TechnicalDetail.ShouldContain("positive");
    }

    [Fact]
    public void Export_evidence_with_no_result_surface_is_refused()
    {
        Vouch(
            @"Baseline\apps\meitu\editor-export.json",
            ExportWith("\"resultSurface\"", "\"unusedSurface\""));

        Load().IsFailure.ShouldBeTrue();
    }

    /// <summary>A control whose pattern is not one this slice knows about is refused.</summary>
    /// <remarks>
    /// The closed <see cref="UiPatternKind"/> set doing its job at the evidence boundary: signed
    /// evidence can only ask PrintFlow to check for a pattern it can actually check for, so an
    /// unknown one fails the chain rather than silently becoming "no requirement".
    /// </remarks>
    [Fact]
    public void Export_evidence_naming_a_pattern_this_slice_does_not_know_is_refused()
    {
        Vouch(
            @"Baseline\apps\meitu\editor-export.json",
            ExportWith("\"requiredPattern\": \"Invoke\"", "\"requiredPattern\": \"Levitate\""));

        Load().IsFailure.ShouldBeTrue();
    }

    /// <summary>A control the evidence does not fully describe is refused.</summary>
    [Fact]
    public void Export_evidence_with_an_incomplete_control_description_is_refused()
    {
        Vouch(
            @"Baseline\apps\meitu\editor-export.json",
            ExportWith("\"controlType\": \"ComboBox\",", string.Empty));

        OperationResult<MeituBaseline> baseline = Load();

        baseline.IsFailure.ShouldBeTrue();
        baseline.Failure.TechnicalDetail.ShouldContain("formatControl");
    }

    /// <summary>Editing signed export evidence after the manifest was written fails closed.</summary>
    [Fact]
    public void Editing_the_export_evidence_after_signing_fails_closed()
    {
        Vouch(@"Baseline\apps\meitu\editor-export.json", ExportEvidence);
        Write(
            @"Baseline\apps\meitu\editor-export.json",
            ExportWith("\"requiredFormatValue\": \"png\"", "\"requiredFormatValue\": \"jpg\""));

        OperationResult<MeituBaseline> baseline = Load();

        baseline.IsFailure.ShouldBeTrue();
        baseline.Failure.Code.ShouldBe(FailureCode.PresetHashMismatch);
    }
}
