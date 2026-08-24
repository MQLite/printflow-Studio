using System.Collections.Immutable;
using System.Text.Json;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Infrastructure.Preset;

namespace PrintFlow.Infrastructure.Adapters.Meitu;

/// <summary>
/// Reads the Meitu baseline out of the signed workstation preset, following the preset's own
/// integrity list to the clean-start evidence file (Epic 11300 Part A §7, §2).
/// </summary>
/// <remarks>
/// The chain has exactly one root of trust: <c>appsettings.json</c> names the preset manifest
/// and the digest it must hash to. Everything else is reached from inside that verified
/// document — the executable path and digest from <c>meituContract</c>, and the recognition
/// markers from the <c>clean-start.json</c> whose path <i>and</i> SHA-256 the manifest records.
/// So an attacker who can edit the evidence file cannot change what PrintFlow recognises
/// without also breaking the manifest hash.
///
/// Markers are extracted from the evidence file's prose rather than invented here. Epic 11000
/// wrote them as human descriptions ("图片编辑 / PhotoEditor entry"), so
/// <see cref="MeituMarkerExtraction"/> takes the whitespace-delimited tokens that contain CJK
/// characters — a deterministic rule with its own unit tests, not a hand-copied list that could
/// drift from the signed evidence.
///
/// The result is computed once and cached: signed evidence does not change while the process
/// runs, and re-reading it per call would only add a way for the two answers to differ.
/// </remarks>
public sealed class PresetMeituBaselineProvider : IMeituBaselineProvider
{
    private readonly string _manifestAbsolutePath;
    private readonly Sha256 _expectedManifestSha256;
    private readonly Lazy<OperationResult<MeituBaseline>> _baseline;

    /// <param name="manifestAbsolutePath">The signed workstation preset manifest.</param>
    /// <param name="expectedManifestSha256">The digest configuration says it must hash to.</param>
    public PresetMeituBaselineProvider(string manifestAbsolutePath, Sha256 expectedManifestSha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestAbsolutePath);

        _manifestAbsolutePath = manifestAbsolutePath;
        _expectedManifestSha256 = expectedManifestSha256;
        _baseline = new Lazy<OperationResult<MeituBaseline>>(Load);
    }

    /// <inheritdoc />
    public OperationResult<MeituBaseline> GetVerifiedBaseline() => _baseline.Value;

    private OperationResult<MeituBaseline> Load()
    {
        OperationResult<JsonDocument> manifest = VerifiedJsonFile.Read(
            _manifestAbsolutePath, _expectedManifestSha256, "Workstation preset manifest");
        if (manifest.IsFailure)
        {
            return OperationResult.Fail<MeituBaseline>(manifest.Failure);
        }

        using JsonDocument document = manifest.Value;
        JsonElement root = document.RootElement;

        if (!root.TryGetProperty("meituContract", out JsonElement contract) ||
            contract.ValueKind != JsonValueKind.Object)
        {
            return OperationResult.Fail<MeituBaseline>(
                FailureCode.EnvironmentNotVerified,
                "The verified workstation preset declares no meituContract, so no Meitu executable is accepted.");
        }

        string? executablePath = StringOrNull(contract, "executablePath");
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return OperationResult.Fail<MeituBaseline>(
                FailureCode.MeituNotInstalled,
                "The workstation preset's meituContract records no executablePath.");
        }

        string? digestText = StringOrNull(contract, "executableSha256");
        if (!Sha256.TryParse(digestText ?? string.Empty, out Sha256 digest))
        {
            return OperationResult.Fail<MeituBaseline>(
                FailureCode.MeituNotInstalled,
                "The workstation preset's meituContract records no usable executableSha256, so the " +
                "running binary could never be shown to be the accepted one.");
        }

        JsonElement windowContract = contract.TryGetProperty("windowContract", out JsonElement w)
            ? w
            : default;

        OperationResult<CleanStartEvidence> welcome = ReadCleanStartEvidence(root);
        if (welcome.IsFailure)
        {
            return OperationResult.Fail<MeituBaseline>(welcome.Failure);
        }

        OperationResult<MeituCardShape?> card = ReadOptional(
            root, StartPageCardEvidence, "Meitu start-page card evidence", ReadCardShape);
        if (card.IsFailure)
        {
            return OperationResult.Fail<MeituBaseline>(card.Failure);
        }

        OperationResult<MeituFileDialogSignature?> dialog = ReadOptional(
            root, FileDialogEvidence, "Meitu file-picker evidence", ReadFileDialog);
        if (dialog.IsFailure)
        {
            return OperationResult.Fail<MeituBaseline>(dialog.Failure);
        }

        OperationResult<MeituEditorSignature?> editorWithFile = ReadOptional(
            root, EditorWithWorkingCopyEvidence, "Meitu editor-with-document evidence", ReadEditor);
        if (editorWithFile.IsFailure)
        {
            return OperationResult.Fail<MeituBaseline>(editorWithFile.Failure);
        }

        OperationResult<MeituEditorSignature?> editorEmpty = ReadOptional(
            root, EditorEmptyEvidence, "Meitu empty-editor evidence", ReadEditor);
        if (editorEmpty.IsFailure)
        {
            return OperationResult.Fail<MeituBaseline>(editorEmpty.Failure);
        }

        return OperationResult.Ok(new MeituBaseline(
            executablePath,
            digest,
            StringOrNull(contract, "acceptedVersion") ?? "(unrecorded)",
            StringOrNull(contract, "uiLanguage") ?? "(unrecorded)",
            StringArray(windowContract, "acceptedObservedTitles"),
            welcome.Value.WindowTitle,
            welcome.Value.Markers,
            card.Value,
            dialog.Value,
            editorWithFile.Value,
            editorEmpty.Value));
    }

    private const string StartPageCardEvidence = @"apps\meitu\start-page-card-target.json";
    private const string FileDialogEvidence = @"apps\meitu\open-file-dialog.json";
    private const string EditorWithWorkingCopyEvidence = @"apps\meitu\editor-with-working-copy.json";
    private const string EditorEmptyEvidence = @"apps\meitu\editor-empty.json";

    /// <summary>
    /// Reads one optional evidence file the preset may or may not vouch for.
    /// </summary>
    /// <remarks>
    /// The two absences are deliberately different, and the difference is the fail-closed rule
    /// (§10, §23):
    /// <list type="bullet">
    ///   <item>the preset <b>does not list</b> the file — a success carrying <c>null</c>. The
    ///   state it would have described stays unreachable, which is what "PrintFlow has not been
    ///   shown this screen" is supposed to mean;</item>
    ///   <item>the preset <b>lists</b> the file but its bytes do not verify, it is unreadable,
    ///   or its contents are malformed — a failure of the whole baseline. The chain claimed to
    ///   vouch for something that does not check out, and continuing on the remaining evidence
    ///   would be trusting an authority that has just been shown to be wrong.</item>
    /// </list>
    /// </remarks>
    private static OperationResult<T?> ReadOptional<T>(
        JsonElement presetRoot,
        string pathSuffix,
        string description,
        Func<JsonElement, OperationResult<T>> read)
        where T : class
    {
        OperationResult<JsonDocument?> document = ReadVouchedFor(presetRoot, pathSuffix, description);
        if (document.IsFailure)
        {
            return OperationResult.Fail<T?>(document.Failure);
        }

        if (document.Value is null)
        {
            return OperationResult.Ok<T?>(null);
        }

        using JsonDocument evidence = document.Value;
        OperationResult<T> parsed = read(evidence.RootElement);
        return parsed.IsFailure
            ? OperationResult.Fail<T?>(parsed.Failure)
            : OperationResult.Ok<T?>(parsed.Value);
    }

    /// <summary>
    /// Follows <c>sourceManifestIntegrity</c> to a file, verifying it against the digest the
    /// preset records. Returns <c>null</c> when the preset does not list it at all.
    /// </summary>
    private static OperationResult<JsonDocument?> ReadVouchedFor(
        JsonElement presetRoot, string pathSuffix, string description)
    {
        if (!presetRoot.TryGetProperty("sourceManifestIntegrity", out JsonElement integrity) ||
            integrity.ValueKind != JsonValueKind.Array)
        {
            return OperationResult.Fail<JsonDocument?>(
                FailureCode.EnvironmentNotVerified,
                "The verified preset carries no sourceManifestIntegrity list, so no evidence file can be trusted.");
        }

        foreach (JsonElement entry in integrity.EnumerateArray())
        {
            string? path = StringOrNull(entry, "path");
            if (path is null || !path.EndsWith(pathSuffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!Sha256.TryParse(StringOrNull(entry, "sha256") ?? string.Empty, out Sha256 expected))
            {
                return OperationResult.Fail<JsonDocument?>(
                    FailureCode.EnvironmentNotVerified,
                    $"The preset records {description} without a usable SHA-256.");
            }

            OperationResult<JsonDocument> verified = VerifiedJsonFile.Read(path, expected, description);
            return verified.IsFailure
                ? OperationResult.Fail<JsonDocument?>(verified.Failure)
                : OperationResult.Ok<JsonDocument?>(verified.Value);
        }

        return OperationResult.Ok<JsonDocument?>(null);
    }

    /// <summary>Reads the structural shape of a start-page card and the label that titles it.</summary>
    private static OperationResult<MeituCardShape> ReadCardShape(JsonElement root)
    {
        if (!root.TryGetProperty("cardTarget", out JsonElement card) ||
            card.ValueKind != JsonValueKind.Object)
        {
            return OperationResult.Fail<MeituCardShape>(
                FailureCode.EnvironmentNotVerified,
                "The Meitu start-page card evidence declares no cardTarget object.");
        }

        string?[] required =
        [
            StringOrNull(card, "labelControlType"),
            StringOrNull(card, "labelClassName"),
            StringOrNull(card, "labelAutomationIdSuffix"),
            StringOrNull(card, "cardControlType"),
            StringOrNull(card, "cardClassName"),
            StringOrNull(card, "cardAutomationIdSuffix"),
        ];

        if (Array.Exists(required, value => string.IsNullOrWhiteSpace(value)))
        {
            return OperationResult.Fail<MeituCardShape>(
                FailureCode.EnvironmentNotVerified,
                "The Meitu start-page card evidence does not record every field the structural rule needs, " +
                "so PrintFlow has no complete description of the control it would invoke.");
        }

        // Parsed strictly against the closed pattern set, so evidence cannot demand a pattern
        // the adapter has no reviewed way to use.
        if (!Enum.TryParse(StringOrNull(card, "requiredCardPattern"), ignoreCase: false, out UiPatternKind pattern))
        {
            return OperationResult.Fail<MeituCardShape>(
                FailureCode.EnvironmentNotVerified,
                $"The Meitu start-page card evidence records requiredCardPattern " +
                $"'{StringOrNull(card, "requiredCardPattern")}', which is not one PrintFlow knows how to use.");
        }

        return OperationResult.Ok(new MeituCardShape(
            required[0]!, required[1]!, required[2]!, required[3]!, required[4]!, required[5]!, pattern));
    }

    /// <summary>Reads the signature of the picker the start-page card opens.</summary>
    private static OperationResult<MeituFileDialogSignature> ReadFileDialog(JsonElement root)
    {
        if (!root.TryGetProperty("fileDialog", out JsonElement dialog) ||
            dialog.ValueKind != JsonValueKind.Object)
        {
            return OperationResult.Fail<MeituFileDialogSignature>(
                FailureCode.EnvironmentNotVerified,
                "The Meitu file-picker evidence declares no fileDialog object.");
        }

        string?[] required =
        [
            StringOrNull(dialog, "kind"),
            StringOrNull(dialog, "windowClassName"),
            StringOrNull(dialog, "fileNameAutomationId"),
            StringOrNull(dialog, "fileNameControlType"),
            StringOrNull(dialog, "confirmAutomationId"),
            StringOrNull(dialog, "confirmControlType"),
        ];

        if (Array.Exists(required, value => string.IsNullOrWhiteSpace(value)))
        {
            return OperationResult.Fail<MeituFileDialogSignature>(
                FailureCode.EnvironmentNotVerified,
                "The Meitu file-picker evidence does not record the window class and both control " +
                "identities, so PrintFlow has no complete description of what it would type into.");
        }

        return OperationResult.Ok(new MeituFileDialogSignature(
            required[0]!,
            required[1]!,
            StringArray(dialog, "acceptedTitles"),
            required[2]!,
            required[3]!,
            required[4]!,
            required[5]!));
    }

    /// <summary>Reads an editor screen's signature.</summary>
    private static OperationResult<MeituEditorSignature> ReadEditor(JsonElement root)
    {
        if (!root.TryGetProperty("editor", out JsonElement editor) ||
            editor.ValueKind != JsonValueKind.Object)
        {
            return OperationResult.Fail<MeituEditorSignature>(
                FailureCode.EnvironmentNotVerified, "The Meitu editor evidence declares no editor object.");
        }

        string? title = StringOrNull(editor, "windowTitle");
        if (string.IsNullOrWhiteSpace(title))
        {
            return OperationResult.Fail<MeituEditorSignature>(
                FailureCode.EnvironmentNotVerified,
                "The Meitu editor evidence records no windowTitle, so the screen has no signed title to match.");
        }

        // Taken verbatim, unlike the Epic 11000 clean-start markers. Those were written as prose
        // for a person to read and need extracting; these files were authored as evidence for
        // this rule and record exact labels. Running the CJK extraction over them would silently
        // drop a Latin-only label such as "PhotoEditor" — a marker quietly disappearing is worse
        // than one that has to be written exactly.
        ImmutableArray<string> markers = StringArray(editor, "requiredMarkers");
        if (markers.IsEmpty)
        {
            // A signature with no positive markers would rest on the window title alone, and a
            // title is the one property of a window that anything can claim (§15).
            return OperationResult.Fail<MeituEditorSignature>(
                FailureCode.EnvironmentNotVerified,
                "The Meitu editor evidence lists no usable positive markers, so the screen could only be " +
                "recognised by its title.");
        }

        if (!editor.TryGetProperty("minimumRequiredMarkers", out JsonElement minimumElement) ||
            !minimumElement.TryGetInt32(out int minimum) ||
            minimum <= 0 ||
            minimum > markers.Length)
        {
            return OperationResult.Fail<MeituEditorSignature>(
                FailureCode.EnvironmentNotVerified,
                $"The Meitu editor evidence records no usable minimumRequiredMarkers for its {markers.Length} " +
                "marker(s).");
        }

        if (!Enum.TryParse(
                StringOrNull(editor, "fileNameLocation"), ignoreCase: false, out MeituFileNameLocation location))
        {
            return OperationResult.Fail<MeituEditorSignature>(
                FailureCode.EnvironmentNotVerified,
                $"The Meitu editor evidence records fileNameLocation " +
                $"'{StringOrNull(editor, "fileNameLocation")}', which is not a place PrintFlow knows how to look.");
        }

        OperationResult<MeituControlSignature?> openControl = ReadOpenControl(editor);
        return openControl.IsFailure
            ? OperationResult.Fail<MeituEditorSignature>(openControl.Failure)
            : OperationResult.Ok(
                new MeituEditorSignature(title, markers, minimum, location, openControl.Value));
    }

    /// <summary>
    /// Reads an editor screen's open control, which most screens do not have.
    /// </summary>
    /// <remarks>
    /// Absent is a success carrying <c>null</c>: an editor already showing a document has no
    /// empty-state open button, and requiring one would make that screen unreadable. Present but
    /// incomplete is a failure, on the same principle as a vouched-for file that does not
    /// verify — the evidence claims something it does not actually describe.
    /// </remarks>
    private static OperationResult<MeituControlSignature?> ReadOpenControl(JsonElement editor)
    {
        if (!editor.TryGetProperty("openControl", out JsonElement control) ||
            control.ValueKind != JsonValueKind.Object)
        {
            return OperationResult.Ok<MeituControlSignature?>(null);
        }

        string?[] required =
        [
            StringOrNull(control, "name"),
            StringOrNull(control, "automationIdContains"),
            StringOrNull(control, "controlType"),
            StringOrNull(control, "className"),
        ];

        if (Array.Exists(required, value => string.IsNullOrWhiteSpace(value)))
        {
            return OperationResult.Fail<MeituControlSignature?>(
                FailureCode.EnvironmentNotVerified,
                "The Meitu editor evidence declares an openControl without recording every property needed " +
                "to identify it uniquely.");
        }

        if (!Enum.TryParse(
                StringOrNull(control, "requiredPattern"), ignoreCase: false, out UiPatternKind pattern))
        {
            return OperationResult.Fail<MeituControlSignature?>(
                FailureCode.EnvironmentNotVerified,
                $"The Meitu editor evidence records openControl.requiredPattern " +
                $"'{StringOrNull(control, "requiredPattern")}', which is not one PrintFlow knows how to use.");
        }

        return OperationResult.Ok<MeituControlSignature?>(
            new MeituControlSignature(required[0]!, required[1]!, required[2]!, required[3]!, pattern));
    }

    /// <summary>The clean start page's own signature: its exact title and its markers.</summary>
    private readonly record struct CleanStartEvidence(string WindowTitle, ImmutableArray<string> Markers);

    /// <summary>
    /// Follows <c>sourceManifestIntegrity</c> to the Meitu clean-start evidence file and reads
    /// the title and structural markers it recorded.
    /// </summary>
    private static OperationResult<CleanStartEvidence> ReadCleanStartEvidence(JsonElement presetRoot)
    {
        if (!presetRoot.TryGetProperty("sourceManifestIntegrity", out JsonElement integrity) ||
            integrity.ValueKind != JsonValueKind.Array)
        {
            return OperationResult.Fail<CleanStartEvidence>(
                FailureCode.EnvironmentNotVerified,
                "The verified preset carries no sourceManifestIntegrity list, so no evidence file can be trusted.");
        }

        foreach (JsonElement entry in integrity.EnumerateArray())
        {
            string? path = StringOrNull(entry, "path");
            if (path is null ||
                !path.EndsWith(@"apps\meitu\clean-start.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!Sha256.TryParse(StringOrNull(entry, "sha256") ?? string.Empty, out Sha256 expected))
            {
                return OperationResult.Fail<CleanStartEvidence>(
                    FailureCode.EnvironmentNotVerified,
                    "The preset records the Meitu clean-start evidence file without a usable SHA-256.");
            }

            OperationResult<JsonDocument> evidence = VerifiedJsonFile.Read(
                path, expected, "Meitu clean-start evidence");
            if (evidence.IsFailure)
            {
                return OperationResult.Fail<CleanStartEvidence>(evidence.Failure);
            }

            using JsonDocument document = evidence.Value;
            ImmutableArray<string> markers =
                MeituMarkerExtraction.Extract(StringArray(document.RootElement, "stableStructuralMarkers"));

            if (markers.IsEmpty)
            {
                return OperationResult.Fail<CleanStartEvidence>(
                    FailureCode.EnvironmentNotVerified,
                    "The Meitu clean-start evidence file lists no usable structural markers.");
            }

            // The exact title Epic 11000 saw on the clean start page — not one of the accepted
            // titles generally. That distinction is what stops the editor, whose title is the
            // start page's title plus a feature suffix, from ever being read as a start page.
            string? title = StringOrNull(document.RootElement, "windowTitle");
            return string.IsNullOrWhiteSpace(title)
                ? OperationResult.Fail<CleanStartEvidence>(
                    FailureCode.EnvironmentNotVerified,
                    "The Meitu clean-start evidence file records no windowTitle, so the start page has no " +
                    "signed title to match against.")
                : OperationResult.Ok(new CleanStartEvidence(title, markers));
        }

        return OperationResult.Fail<CleanStartEvidence>(
            FailureCode.EnvironmentNotVerified,
            "The verified preset does not vouch for a Meitu clean-start evidence file, so the welcome " +
            "page has no signed recognition signature.");
    }

    private static string? StringOrNull(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out JsonElement value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static ImmutableArray<string> StringArray(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out JsonElement array) ||
            array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        ImmutableArray<string>.Builder builder = ImmutableArray.CreateBuilder<string>();
        foreach (JsonElement item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } text)
            {
                builder.Add(text);
            }
        }

        return builder.ToImmutable();
    }
}
