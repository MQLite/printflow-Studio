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

        OperationResult<MeituDocumentIdentitySignature?> documentIdentity = ReadOptional(
            root, EditorWithWorkingCopyEvidence, "Meitu editor document-identity evidence", ReadDocumentIdentity);
        if (documentIdentity.IsFailure)
        {
            return OperationResult.Fail<MeituBaseline>(documentIdentity.Failure);
        }

        OperationResult<MeituEditorSignature?> editorEmpty = ReadOptional(
            root, EditorEmptyEvidence, "Meitu empty-editor evidence", ReadEditor);
        if (editorEmpty.IsFailure)
        {
            return OperationResult.Fail<MeituBaseline>(editorEmpty.Failure);
        }

        OperationResult<MeituCloseDocumentSignature?> closeDocument = ReadOptional(
            root, CloseDocumentEvidence, "Meitu close-document evidence", ReadCloseDocument);
        if (closeDocument.IsFailure)
        {
            return OperationResult.Fail<MeituBaseline>(closeDocument.Failure);
        }

        OperationResult<MeituEnhancementSignature?> enhancement = ReadOptional(
            root, EnhancementEvidence, "Meitu enhancement evidence", ReadEnhancement);
        if (enhancement.IsFailure)
        {
            return OperationResult.Fail<MeituBaseline>(enhancement.Failure);
        }

        OperationResult<MeituExportSignature?> export = ReadOptional(
            root, ExportEvidence, "Meitu export evidence", ReadExport);
        if (export.IsFailure)
        {
            return OperationResult.Fail<MeituBaseline>(export.Failure);
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
            documentIdentity.Value,
            editorEmpty.Value,
            closeDocument.Value,
            enhancement.Value,
            export.Value));
    }

    private const string StartPageCardEvidence = @"apps\meitu\start-page-card-target.json";
    private const string FileDialogEvidence = @"apps\meitu\open-file-dialog.json";
    private const string EditorWithWorkingCopyEvidence = @"apps\meitu\editor-with-working-copy.json";
    private const string EditorEmptyEvidence = @"apps\meitu\editor-empty.json";
    private const string CloseDocumentEvidence = @"apps\meitu\editor-close-document.json";
    private const string EnhancementEvidence = @"apps\meitu\editor-enhancement.json";
    private const string ExportEvidence = @"apps\meitu\editor-export.json";

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

        return ReadCardShapeObject(card, "start-page card");
    }

    private static OperationResult<MeituCardShape> ReadCardShapeObject(JsonElement card, string description)
    {
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
                $"The Meitu {description} evidence does not record every field the structural rule needs, " +
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

    /// <summary>Reads the signed Save-dialog route used to confirm document identity.</summary>
    private static OperationResult<MeituDocumentIdentitySignature> ReadDocumentIdentity(JsonElement root)
    {
        OperationResult<MeituEditorSignature> editor = ReadEditor(root);
        if (editor.IsFailure)
        {
            return OperationResult.Fail<MeituDocumentIdentitySignature>(editor.Failure);
        }

        if (!root.TryGetProperty("documentIdentity", out JsonElement identity) ||
            identity.ValueKind != JsonValueKind.Object)
        {
            return OperationResult.Fail<MeituDocumentIdentitySignature>(
                FailureCode.EnvironmentNotVerified,
                "The Meitu editor document-identity evidence declares no documentIdentity object.");
        }

        string? saveMarker = StringOrNull(identity, "saveMarkerName");
        string? dialogTitle = StringOrNull(identity, "dialogTitle");
        string? dialogClass = StringOrNull(identity, "dialogClassName");
        string? suffix = StringOrNull(identity, "outputBaseNameSuffix");
        if (string.IsNullOrWhiteSpace(saveMarker) || string.IsNullOrWhiteSpace(dialogTitle) ||
            string.IsNullOrWhiteSpace(dialogClass) || string.IsNullOrEmpty(suffix))
        {
            return OperationResult.Fail<MeituDocumentIdentitySignature>(
                FailureCode.EnvironmentNotVerified,
                "The Meitu document-identity evidence does not record the Save marker, owned dialog, and " +
                "exact output-basename suffix.");
        }

        if (!identity.TryGetProperty("saveControl", out JsonElement saveControl) ||
            saveControl.ValueKind != JsonValueKind.Object)
        {
            return OperationResult.Fail<MeituDocumentIdentitySignature>(
                FailureCode.EnvironmentNotVerified,
                "The Meitu document-identity evidence declares no structural saveControl.");
        }

        OperationResult<MeituCardShape> saveShape = ReadCardShapeObject(saveControl, "Save control");
        OperationResult<MeituControlSignature> fileName = ReadControl(identity, "fileNameControl", allowEmptyName: true);
        OperationResult<MeituControlSignature> cancel = ReadControl(identity, "cancelControl", allowEmptyName: false);
        if (saveShape.IsFailure)
        {
            return OperationResult.Fail<MeituDocumentIdentitySignature>(saveShape.Failure);
        }

        if (fileName.IsFailure)
        {
            return OperationResult.Fail<MeituDocumentIdentitySignature>(fileName.Failure);
        }

        if (cancel.IsFailure)
        {
            return OperationResult.Fail<MeituDocumentIdentitySignature>(cancel.Failure);
        }

        return OperationResult.Ok(new MeituDocumentIdentitySignature(
            editor.Value, saveMarker, saveShape.Value, dialogTitle, dialogClass,
            fileName.Value, cancel.Value, suffix));
    }


    /// <summary>Reads the signed route that returns the loaded editor to its empty state.</summary>
    private static OperationResult<MeituCloseDocumentSignature> ReadCloseDocument(JsonElement root)
    {
        if (!root.TryGetProperty("closeDocument", out JsonElement close) ||
            close.ValueKind != JsonValueKind.Object)
        {
            return OperationResult.Fail<MeituCloseDocumentSignature>(
                FailureCode.EnvironmentNotVerified,
                "The Meitu close-document evidence declares no closeDocument object.");
        }

        string? marker = StringOrNull(close, "markerName");
        if (string.IsNullOrWhiteSpace(marker))
        {
            return OperationResult.Fail<MeituCloseDocumentSignature>(
                FailureCode.EnvironmentNotVerified,
                "The Meitu close-document evidence records no markerName to anchor the control walk.");
        }

        if (!close.TryGetProperty("control", out JsonElement control) ||
            control.ValueKind != JsonValueKind.Object)
        {
            return OperationResult.Fail<MeituCloseDocumentSignature>(
                FailureCode.EnvironmentNotVerified,
                "The Meitu close-document evidence declares no structural control.");
        }

        OperationResult<MeituCardShape> shape = ReadCardShapeObject(control, "close-document control");
        return shape.IsFailure
            ? OperationResult.Fail<MeituCloseDocumentSignature>(shape.Failure)
            : OperationResult.Ok(new MeituCloseDocumentSignature(marker, shape.Value));
    }

    /// <summary>Reads the signed Enhancement action, Busy signature and completion signature.</summary>
    /// <remarks>
    /// All three or none. A partially described Enhancement route is refused rather than read
    /// down to what is present, because the parts are not independently useful: an action
    /// without a completion signature is a control PrintFlow could invoke and then never know
    /// had finished, which is worse than not being able to invoke it at all (Part B2A §14).
    /// </remarks>
    private static OperationResult<MeituEnhancementSignature> ReadEnhancement(JsonElement root)
    {
        if (!root.TryGetProperty("enhancement", out JsonElement enhancement) ||
            enhancement.ValueKind != JsonValueKind.Object)
        {
            return OperationResult.Fail<MeituEnhancementSignature>(
                FailureCode.EnvironmentNotVerified,
                "The Meitu enhancement evidence declares no enhancement object.");
        }

        string? marker = StringOrNull(enhancement, "actionMarkerName");
        if (string.IsNullOrWhiteSpace(marker))
        {
            return OperationResult.Fail<MeituEnhancementSignature>(
                FailureCode.EnvironmentNotVerified,
                "The Meitu enhancement evidence records no actionMarkerName, so the action has no anchor.");
        }

        if (!enhancement.TryGetProperty("actionControl", out JsonElement action) ||
            action.ValueKind != JsonValueKind.Object)
        {
            return OperationResult.Fail<MeituEnhancementSignature>(
                FailureCode.EnvironmentNotVerified,
                "The Meitu enhancement evidence declares no structural actionControl.");
        }

        OperationResult<MeituOwnedControlShape> shape = ReadOwnedControlShape(action);
        if (shape.IsFailure)
        {
            return OperationResult.Fail<MeituEnhancementSignature>(shape.Failure);
        }

        OperationResult<MeituBusySignature> busy = ReadBusy(enhancement);
        if (busy.IsFailure)
        {
            return OperationResult.Fail<MeituEnhancementSignature>(busy.Failure);
        }

        OperationResult<MeituCompletionSignature> completion = ReadCompletion(enhancement);
        return completion.IsFailure
            ? OperationResult.Fail<MeituEnhancementSignature>(completion.Failure)
            : OperationResult.Ok(new MeituEnhancementSignature(
                marker, shape.Value, busy.Value, completion.Value));
    }

    private static OperationResult<MeituOwnedControlShape> ReadOwnedControlShape(JsonElement control)
    {
        string?[] required =
        [
            StringOrNull(control, "markerControlType"),
            StringOrNull(control, "markerClassName"),
            StringOrNull(control, "markerAutomationIdSuffix"),
            StringOrNull(control, "ownerControlType"),
            StringOrNull(control, "ownerClassName"),
            StringOrNull(control, "ownerAutomationIdSuffix"),
            StringOrNull(control, "markerRelativeAutomationIdSuffix"),
        ];

        if (Array.Exists(required, value => string.IsNullOrWhiteSpace(value)))
        {
            return OperationResult.Fail<MeituOwnedControlShape>(
                FailureCode.EnvironmentNotVerified,
                "The Meitu enhancement evidence does not record every field the structural action rule " +
                "needs, so PrintFlow has no complete description of the control it would invoke.");
        }

        // A depth this rule will walk, and an upper bound on it. The bound is not tuning: an
        // evidence file that asked for a walk of twenty levels would leave the marker's own
        // subtree entirely and reach the window, and a rule that would climb that far on
        // instruction is no longer anchored to the marker at all (§8).
        if (!control.TryGetProperty("ownerAncestorDepth", out JsonElement depthElement) ||
            !depthElement.TryGetInt32(out int depth) || depth < 1 || depth > 4)
        {
            return OperationResult.Fail<MeituOwnedControlShape>(
                FailureCode.EnvironmentNotVerified,
                "The Meitu enhancement evidence records no usable ownerAncestorDepth between 1 and 4.");
        }

        if (!Enum.TryParse(
                StringOrNull(control, "requiredOwnerPattern"), ignoreCase: false, out UiPatternKind pattern))
        {
            return OperationResult.Fail<MeituOwnedControlShape>(
                FailureCode.EnvironmentNotVerified,
                $"The Meitu enhancement evidence records requiredOwnerPattern " +
                $"'{StringOrNull(control, "requiredOwnerPattern")}', which is not one PrintFlow knows how " +
                "to use.");
        }

        return OperationResult.Ok(new MeituOwnedControlShape(
            required[0]!, required[1]!, required[2]!, required[3]!, required[4]!, required[5]!,
            required[6]!, depth, pattern));
    }

    private static OperationResult<MeituBusySignature> ReadBusy(JsonElement enhancement)
    {
        OperationResult<(ImmutableArray<string> Markers, int Minimum)> markers =
            ReadPositiveMarkers(enhancement, "busy", "Busy");

        return markers.IsFailure
            ? OperationResult.Fail<MeituBusySignature>(markers.Failure)
            : OperationResult.Ok(new MeituBusySignature(markers.Value.Markers, markers.Value.Minimum));
    }

    private static OperationResult<MeituCompletionSignature> ReadCompletion(JsonElement enhancement)
    {
        OperationResult<(ImmutableArray<string> Markers, int Minimum)> markers =
            ReadPositiveMarkers(enhancement, "completion", "completion");
        if (markers.IsFailure)
        {
            return OperationResult.Fail<MeituCompletionSignature>(markers.Failure);
        }

        if (!enhancement.TryGetProperty("completion", out JsonElement completion) ||
            !completion.TryGetProperty("requiresBusyAbsent", out JsonElement flag) ||
            flag.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return OperationResult.Fail<MeituCompletionSignature>(
                FailureCode.EnvironmentNotVerified,
                "The Meitu enhancement evidence does not state whether completion requires the Busy " +
                "signature to be absent. Whether the two states may overlap is a property of the " +
                "application and is not assumed.");
        }

        return OperationResult.Ok(new MeituCompletionSignature(
            markers.Value.Markers, markers.Value.Minimum, flag.GetBoolean()));
    }

    /// <summary>
    /// Reads the signed export route (Epic 11300 Part B2B §5, §6, §8, §11, §39).
    /// </summary>
    /// <remarks>
    /// Every part is required and every absence is a refusal of the whole route. There is no
    /// partial export: a chain that describes the Save surface but not the destination dialog
    /// would leave PrintFlow able to press a button whose result it could not place, which is
    /// the failure this whole slice exists to make impossible.
    ///
    /// <c>requiredFormatValue</c> in particular is read rather than derived from the output
    /// extension. §11 forbids inferring PNG from the file name when the surface carries its own
    /// format selector, and this is where that stops being a convention.
    /// </remarks>
    private static OperationResult<MeituExportSignature> ReadExport(JsonElement root)
    {
        if (!root.TryGetProperty("export", out JsonElement export) ||
            export.ValueKind != JsonValueKind.Object)
        {
            return OperationResult.Fail<MeituExportSignature>(
                FailureCode.EnvironmentNotVerified,
                "The Meitu export evidence declares no export object.");
        }

        string? surfaceTitle = StringOrNull(export, "surfaceTitle");
        string? surfaceClass = StringOrNull(export, "surfaceClassName");
        string? requiredFormat = StringOrNull(export, "requiredFormatValue");
        if (string.IsNullOrWhiteSpace(surfaceTitle) || string.IsNullOrWhiteSpace(surfaceClass) ||
            string.IsNullOrWhiteSpace(requiredFormat))
        {
            return OperationResult.Fail<MeituExportSignature>(
                FailureCode.EnvironmentNotVerified,
                "The Meitu export evidence does not record the Save surface's title and class and the " +
                "exact format value the export requires, so PrintFlow could neither identify the surface " +
                "nor confirm the format before invoking anything.");
        }

        OperationResult<MeituControlSignature> fileName =
            ReadControl(export, "fileNameControl", allowEmptyName: true, evidence: "export");
        if (fileName.IsFailure)
        {
            return OperationResult.Fail<MeituExportSignature>(fileName.Failure);
        }

        OperationResult<MeituControlSignature> format =
            ReadControl(export, "formatControl", allowEmptyName: true, evidence: "export");
        if (format.IsFailure)
        {
            return OperationResult.Fail<MeituExportSignature>(format.Failure);
        }

        OperationResult<MeituControlSignature> saveAs =
            ReadControl(export, "saveAsControl", allowEmptyName: false, evidence: "export");
        if (saveAs.IsFailure)
        {
            return OperationResult.Fail<MeituExportSignature>(saveAs.Failure);
        }

        OperationResult<MeituExportDestinationSignature> destination = ReadExportDestination(export);
        if (destination.IsFailure)
        {
            return OperationResult.Fail<MeituExportSignature>(destination.Failure);
        }

        OperationResult<MeituExportResultSignature> result = ReadExportResult(export);
        return result.IsFailure
            ? OperationResult.Fail<MeituExportSignature>(result.Failure)
            : OperationResult.Ok(new MeituExportSignature(
                surfaceTitle,
                surfaceClass,
                fileName.Value,
                format.Value,
                requiredFormat,
                saveAs.Value,
                destination.Value,
                result.Value));
    }

    /// <summary>Reads the dialog in which the export's destination is actually named.</summary>
    private static OperationResult<MeituExportDestinationSignature> ReadExportDestination(JsonElement export)
    {
        if (!export.TryGetProperty("destinationDialog", out JsonElement dialog) ||
            dialog.ValueKind != JsonValueKind.Object)
        {
            return OperationResult.Fail<MeituExportDestinationSignature>(
                FailureCode.EnvironmentNotVerified,
                "The Meitu export evidence declares no destinationDialog, so PrintFlow has no signed way " +
                "to place the output anywhere it chose. Nothing is invoked.");
        }

        string?[] required =
        [
            StringOrNull(dialog, "windowClassName"),
            StringOrNull(dialog, "fileNameAutomationId"),
            StringOrNull(dialog, "fileNameControlType"),
            StringOrNull(dialog, "confirmAutomationId"),
            StringOrNull(dialog, "confirmControlType"),
            StringOrNull(dialog, "cancelAutomationId"),
            StringOrNull(dialog, "cancelControlType"),
        ];

        if (Array.Exists(required, value => string.IsNullOrWhiteSpace(value)))
        {
            return OperationResult.Fail<MeituExportDestinationSignature>(
                FailureCode.EnvironmentNotVerified,
                "The Meitu export evidence does not record the destination dialog's class and all three " +
                "control identities. The cancel control is required alongside the other two: a route that " +
                "can open this dialog but not back out of it would have to leave a modal on the operator's " +
                "screen whenever the export could not proceed.");
        }

        ImmutableArray<string> titles = StringArray(dialog, "acceptedTitles");
        return titles.IsEmpty
            ? OperationResult.Fail<MeituExportDestinationSignature>(
                FailureCode.EnvironmentNotVerified,
                "The Meitu export evidence records no accepted title for the destination dialog.")
            : OperationResult.Ok(new MeituExportDestinationSignature(
                required[0]!, titles, required[1]!, required[2]!,
                required[3]!, required[4]!, required[5]!, required[6]!));
    }

    /// <summary>Reads Meitu's post-save confirmation surface and the control that dismisses it.</summary>
    private static OperationResult<MeituExportResultSignature> ReadExportResult(JsonElement export)
    {
        if (!export.TryGetProperty("resultSurface", out JsonElement result) ||
            result.ValueKind != JsonValueKind.Object)
        {
            return OperationResult.Fail<MeituExportResultSignature>(
                FailureCode.EnvironmentNotVerified,
                "The Meitu export evidence declares no resultSurface. PrintFlow would then have no signed " +
                "description of the surface Meitu puts up after a save, and §24 forbids dismissing a " +
                "Meitu-owned surface whose exact action has not been observed.");
        }

        OperationResult<(ImmutableArray<string> Markers, int Minimum)> markers =
            ReadPositiveMarkers(result, "recognition", "save-result", evidence: "export");
        if (markers.IsFailure)
        {
            return OperationResult.Fail<MeituExportResultSignature>(markers.Failure);
        }

        OperationResult<MeituControlSignature> close =
            ReadControl(result, "closeControl", allowEmptyName: true, evidence: "export");
        return close.IsFailure
            ? OperationResult.Fail<MeituExportResultSignature>(close.Failure)
            : OperationResult.Ok(new MeituExportResultSignature(
                markers.Value.Markers, markers.Value.Minimum, close.Value));
    }

    /// <summary>
    /// Reads a positive marker list and its threshold, refusing every shape that would match
    /// something PrintFlow has not been shown.
    /// </summary>
    private static OperationResult<(ImmutableArray<string> Markers, int Minimum)> ReadPositiveMarkers(
        JsonElement enhancement, string propertyName, string description, string evidence = "enhancement")
    {
        if (!enhancement.TryGetProperty(propertyName, out JsonElement section) ||
            section.ValueKind != JsonValueKind.Object)
        {
            return OperationResult.Fail<(ImmutableArray<string>, int)>(
                FailureCode.EnvironmentNotVerified,
                $"The Meitu {evidence} evidence declares no {description} signature.");
        }

        ImmutableArray<string> markers = StringArray(section, "requiredMarkers");
        if (markers.IsEmpty)
        {
            return OperationResult.Fail<(ImmutableArray<string>, int)>(
                FailureCode.EnvironmentNotVerified,
                $"The Meitu {evidence} evidence lists no positive {description} markers. A signature " +
                "phrased as an absence would be satisfied by an automation read that returned nothing.");
        }

        if (!section.TryGetProperty("minimumRequiredMarkers", out JsonElement minimumElement) ||
            !minimumElement.TryGetInt32(out int minimum) || minimum <= 0 || minimum > markers.Length)
        {
            return OperationResult.Fail<(ImmutableArray<string>, int)>(
                FailureCode.EnvironmentNotVerified,
                $"The Meitu {evidence} evidence records no usable minimumRequiredMarkers for its " +
                $"{markers.Length} {description} marker(s).");
        }

        return OperationResult.Ok((markers, minimum));
    }

    private static OperationResult<MeituControlSignature> ReadControl(
        JsonElement parent, string propertyName, bool allowEmptyName, string evidence = "document-identity")
    {
        if (!parent.TryGetProperty(propertyName, out JsonElement control) ||
            control.ValueKind != JsonValueKind.Object)
        {
            return OperationResult.Fail<MeituControlSignature>(
                FailureCode.EnvironmentNotVerified,
                $"The Meitu {evidence} evidence declares no {propertyName}.");
        }

        string? name = StringOrNull(control, "name");
        string? automationIdContains = StringOrNull(control, "automationIdContains");
        string? controlType = StringOrNull(control, "controlType");
        string? className = StringOrNull(control, "className");
        if (name is null || (!allowEmptyName && name.Length == 0) ||
            string.IsNullOrWhiteSpace(automationIdContains) || string.IsNullOrWhiteSpace(controlType) ||
            string.IsNullOrWhiteSpace(className) ||
            !Enum.TryParse(
                StringOrNull(control, "requiredPattern"), ignoreCase: false, out UiPatternKind pattern))
        {
            return OperationResult.Fail<MeituControlSignature>(
                FailureCode.EnvironmentNotVerified,
                $"The Meitu {evidence} evidence does not completely describe {propertyName}.");
        }

        return OperationResult.Ok(new MeituControlSignature(
            name, automationIdContains, controlType, className, pattern));
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
