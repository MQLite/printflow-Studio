using System.Collections.Immutable;
using System.Text.Json;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Preset;

namespace PrintFlow.Infrastructure.Adapters.Photoshop;

/// <summary>
/// Reads the Photoshop baseline out of the signed workstation preset, following the preset's own
/// integrity list to the evidence files (Epic 11400 Part A §5, §25).
/// </summary>
/// <remarks>
/// The chain has exactly one root of trust: <c>appsettings.json</c> names the preset manifest and
/// the digest it must hash to. Everything else is reached from inside that verified document —
/// the executable path, versions and digest from <c>photoshopContract</c>, and the UI structure
/// from the evidence files whose paths <i>and</i> SHA-256s the manifest records. An attacker who
/// can edit an evidence file cannot change what PrintFlow recognises or presses without also
/// breaking the manifest hash.
///
/// Every UI-structure member is optional, and an absent one leaves the corresponding capability
/// refused rather than defaulted. That is the fail-closed rule in its most literal form: there is
/// no control id, class name, dialog title or window class written anywhere in the adapter's
/// code, so a preset that vouches for nothing produces an adapter that does nothing.
///
/// The result is computed once and cached: signed evidence does not change while the process
/// runs, and re-reading it per call would only add a way for the two answers to differ.
/// </remarks>
public sealed class PresetPhotoshopBaselineProvider : IPhotoshopBaselineProvider
{
    // Matched by path suffix against the manifest's own integrity list, exactly as the Meitu
    // provider does. The manifest records an absolute path and a digest for each; matching on
    // the suffix means the workspace root can move without the adapter caring, while the digest
    // still decides whether the file that was found is the file that was signed.
    private const string OpenDialogEvidence = @"apps\photoshop-2019\open-file-dialog.json";
    private const string WindowStateEvidence = @"apps\photoshop-2019\window-states.json";
    private const string DocumentIdentityEvidence = @"apps\photoshop-2019\document-identity.json";
    private const string OwnedDocumentCleanupEvidence = @"apps\photoshop-2019\owned-document-cleanup.json";
    private const string W1ActionEvidence = @"apps\photoshop-2019\cmyk-w1-action-runtime.json";

    private readonly string _manifestAbsolutePath;
    private readonly Sha256 _expectedManifestSha256;
    private readonly Lazy<OperationResult<PhotoshopBaseline>> _baseline;

    /// <param name="manifestAbsolutePath">The signed workstation preset manifest.</param>
    /// <param name="expectedManifestSha256">The digest configuration says it must hash to.</param>
    public PresetPhotoshopBaselineProvider(string manifestAbsolutePath, Sha256 expectedManifestSha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestAbsolutePath);

        _manifestAbsolutePath = manifestAbsolutePath;
        _expectedManifestSha256 = expectedManifestSha256;
        _baseline = new Lazy<OperationResult<PhotoshopBaseline>>(Load);
    }

    /// <inheritdoc />
    public OperationResult<PhotoshopBaseline> GetVerifiedBaseline() => _baseline.Value;

    private OperationResult<PhotoshopBaseline> Load()
    {
        OperationResult<JsonDocument> manifest = VerifiedJsonFile.Read(
            _manifestAbsolutePath, _expectedManifestSha256, "Workstation preset manifest");
        if (manifest.IsFailure)
        {
            return OperationResult.Fail<PhotoshopBaseline>(manifest.Failure);
        }

        using JsonDocument document = manifest.Value;
        JsonElement root = document.RootElement;

        if (!root.TryGetProperty("photoshopContract", out JsonElement contract) ||
            contract.ValueKind != JsonValueKind.Object)
        {
            return OperationResult.Fail<PhotoshopBaseline>(
                FailureCode.EnvironmentNotVerified,
                "The verified workstation preset declares no photoshopContract, so no Photoshop " +
                "executable is accepted.");
        }

        string? executablePath = StringOrNull(contract, "executablePath");
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return OperationResult.Fail<PhotoshopBaseline>(
                FailureCode.PhotoshopNotInstalled,
                "The workstation preset's photoshopContract records no executablePath.");
        }

        if (!Sha256.TryParse(StringOrNull(contract, "executableSha256") ?? string.Empty, out Sha256 digest))
        {
            return OperationResult.Fail<PhotoshopBaseline>(
                FailureCode.PhotoshopNotInstalled,
                "The workstation preset's photoshopContract records no usable executableSha256, so the " +
                "running binary could never be shown to be the accepted one.");
        }

        string? productVersion = StringOrNull(contract, "productVersion");
        string? fileVersion = StringOrNull(contract, "fileVersion");
        if (string.IsNullOrWhiteSpace(productVersion) || string.IsNullOrWhiteSpace(fileVersion))
        {
            return OperationResult.Fail<PhotoshopBaseline>(
                FailureCode.PhotoshopNotInstalled,
                "The workstation preset's photoshopContract records no accepted product and file " +
                "version, so a Photoshop upgrade could not be detected before automation ran.");
        }

        JsonElement uiContract = contract.TryGetProperty("uiContract", out JsonElement ui) ? ui : default;

        string mainWindowClass = StringOrNull(uiContract, "mainWindowClassName") ?? string.Empty;
        string noDocumentTitle = StringOrNull(uiContract, "noDocumentWindowTitle") ?? string.Empty;
        if (mainWindowClass.Length == 0 || noDocumentTitle.Length == 0)
        {
            return OperationResult.Fail<PhotoshopBaseline>(
                FailureCode.EnvironmentNotVerified,
                "The workstation preset's photoshopContract carries no uiContract naming the main " +
                "window class and the no-document window title, so PrintFlow could not tell the " +
                "accepted Photoshop window from any other window in its process.");
        }

        OperationResult<PhotoshopWindowStateSignature?> states = ReadOptional(
            root, WindowStateEvidence, "Photoshop window-state evidence", ReadWindowStates);
        if (states.IsFailure)
        {
            return OperationResult.Fail<PhotoshopBaseline>(states.Failure);
        }

        OperationResult<PhotoshopOpenDialogSignature?> openDialog = ReadOptional(
            root, OpenDialogEvidence, "Photoshop Open-dialog evidence", ReadOpenDialog);
        if (openDialog.IsFailure)
        {
            return OperationResult.Fail<PhotoshopBaseline>(openDialog.Failure);
        }

        OperationResult<PhotoshopDocumentIdentitySignature?> identity = ReadOptional(
            root, DocumentIdentityEvidence, "Photoshop document-identity evidence", ReadDocumentIdentity);
        if (identity.IsFailure)
        {
            return OperationResult.Fail<PhotoshopBaseline>(identity.Failure);
        }

        OperationResult<PhotoshopW1ActionContract?> w1 = ReadOptional(
            root, W1ActionEvidence, "Photoshop CMYK + W1 Action runtime evidence", ReadW1Action);
        if (w1.IsFailure)
        {
            return OperationResult.Fail<PhotoshopBaseline>(w1.Failure);
        }

        if (w1.Value is { } w1Contract)
        {
            OperationResult<Unit> agreement = VerifyManifestActionAgreement(root, w1Contract);
            if (agreement.IsFailure)
            {
                return OperationResult.Fail<PhotoshopBaseline>(agreement.Failure);
            }
        }

        OperationResult<PhotoshopOwnedDocumentCleanupSignature?> cleanup = ReadOptional(
            root,
            OwnedDocumentCleanupEvidence,
            "Photoshop owned-document cleanup evidence",
            ReadOwnedDocumentCleanup);
        if (cleanup.IsFailure)
        {
            return OperationResult.Fail<PhotoshopBaseline>(cleanup.Failure);
        }

        OperationResult<PhotoshopColourSettingsContract> colourSettings = ReadColourSettings(contract);
        if (colourSettings.IsFailure)
        {
            return OperationResult.Fail<PhotoshopBaseline>(colourSettings.Failure);
        }

        return OperationResult.Ok(new PhotoshopBaseline(
            executablePath,
            digest,
            productVersion,
            fileVersion,
            StringOrNull(contract, "uiLanguage") ?? "(unrecorded)",
            mainWindowClass,
            noDocumentTitle,
            StringArray(contract, "excludedInstallations"),
            states.Value,
            openDialog.Value,
            identity.Value,
            w1.Value,
            cleanup.Value,
            colourSettings.Value));
    }

    private static OperationResult<PhotoshopColourSettingsContract> ReadColourSettings(JsonElement contract)
    {
        JsonElement settings = contract.TryGetProperty("colourSettings", out JsonElement value)
            ? value
            : default;
        string rgb = StringOrNull(settings, "rgbWorkingSpace") ?? string.Empty;
        string cmyk = StringOrNull(settings, "cmykWorkingSpace") ?? string.Empty;
        string gray = StringOrNull(settings, "grayWorkingSpace") ?? string.Empty;
        string spot = StringOrNull(settings, "spotWorkingSpace") ?? string.Empty;
        return rgb.Length > 0 && cmyk.Length > 0 && gray.Length > 0 && spot.Length > 0
            ? OperationResult.Ok(new PhotoshopColourSettingsContract(rgb, cmyk, gray, spot))
            : OperationResult.Fail<PhotoshopColourSettingsContract>(
                FailureCode.EnvironmentNotVerified,
                "The verified preset's Photoshop colour-settings contract does not name all four working spaces.");
    }

    /// <summary>
    /// Reads one evidence file the manifest vouches for, or returns <c>null</c> when it vouches
    /// for none.
    /// </summary>
    /// <remarks>
    /// Three outcomes, and the middle one is the interesting one. No entry at all means the
    /// capability stays unreachable — a documented, fail-closed position. An entry whose file is
    /// missing, unreadable, hash-mismatched or malformed is a hard failure, because that is
    /// evidence the preset claims to have and does not: silently treating it as absent would
    /// turn tampering into a mere loss of capability.
    /// </remarks>
    private static OperationResult<T?> ReadOptional<T>(
        JsonElement root, string pathSuffix, string description, Func<JsonElement, OperationResult<T>> read)
        where T : class
    {
        if (!root.TryGetProperty("sourceManifestIntegrity", out JsonElement integrity) ||
            integrity.ValueKind != JsonValueKind.Array)
        {
            return OperationResult.Ok<T?>(null);
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
                return OperationResult.Fail<T?>(
                    FailureCode.EnvironmentNotVerified,
                    $"The workstation preset lists {description} without a usable path and SHA-256.");
            }

            OperationResult<JsonDocument> file = VerifiedJsonFile.Read(path, expected, description);
            if (file.IsFailure)
            {
                return OperationResult.Fail<T?>(file.Failure);
            }

            using JsonDocument document = file.Value;
            OperationResult<T> value = read(document.RootElement);
            return value.IsFailure
                ? OperationResult.Fail<T?>(value.Failure)
                : OperationResult.Ok<T?>(value.Value);
        }

        return OperationResult.Ok<T?>(null);
    }

    private static OperationResult<PhotoshopWindowStateSignature> ReadWindowStates(JsonElement root)
    {
        string start = StringOrNull(root, "startScreenMarkerClass") ?? string.Empty;
        string document = StringOrNull(root, "documentMarkerClass") ?? string.Empty;
        ImmutableArray<string> chrome = StringArray(root, "editorChromeClasses");

        if (start.Length == 0 || document.Length == 0 || chrome.IsDefaultOrEmpty)
        {
            return OperationResult.Fail<PhotoshopWindowStateSignature>(
                FailureCode.EnvironmentNotVerified,
                "The Photoshop window-state evidence does not carry a start-screen marker class, a " +
                "document marker class and at least one editor chrome class, so no screen could be " +
                "positively recognised.");
        }

        return OperationResult.Ok(new PhotoshopWindowStateSignature(start, document, chrome));
    }

    private static OperationResult<PhotoshopOpenDialogSignature> ReadOpenDialog(JsonElement root)
    {
        string windowClass = StringOrNull(root, "windowClassName") ?? string.Empty;
        string title = StringOrNull(root, "title") ?? string.Empty;
        string fileNameClass = StringOrNull(root, "fileNameControlClass") ?? string.Empty;
        string confirmClass = StringOrNull(root, "confirmControlClass") ?? string.Empty;
        string cancelClass = StringOrNull(root, "cancelControlClass") ?? string.Empty;

        if (windowClass.Length == 0 || title.Length == 0 || fileNameClass.Length == 0 ||
            confirmClass.Length == 0 || cancelClass.Length == 0 ||
            !TryInt(root, "fileNameControlId", out int fileNameId) ||
            !TryInt(root, "confirmControlId", out int confirmId) ||
            !TryInt(root, "cancelControlId", out int cancelId))
        {
            return OperationResult.Fail<PhotoshopOpenDialogSignature>(
                FailureCode.EnvironmentNotVerified,
                "The Photoshop Open-dialog evidence is incomplete, so PrintFlow has no positively " +
                "identified control to write a path to or to press.");
        }

        return OperationResult.Ok(new PhotoshopOpenDialogSignature(
            windowClass, title, fileNameId, fileNameClass, confirmId, confirmClass, cancelId, cancelClass));
    }

    private static OperationResult<PhotoshopDocumentIdentitySignature> ReadDocumentIdentity(JsonElement root)
    {
        string separator = StringOrNull(root, "titleSeparator") ?? string.Empty;
        string dialogClass = StringOrNull(root, "dialogClassName") ?? string.Empty;
        string dialogTitle = StringOrNull(root, "dialogTitle") ?? string.Empty;
        string fileNameClass = StringOrNull(root, "fileNameControlClass") ?? string.Empty;
        string addressClass = StringOrNull(root, "addressControlClass") ?? string.Empty;
        string addressPrefix = StringOrNull(root, "addressTextPrefix") ?? string.Empty;
        string cancelClass = StringOrNull(root, "cancelControlClass") ?? string.Empty;

        if (separator.Length == 0 || dialogClass.Length == 0 || dialogTitle.Length == 0 ||
            fileNameClass.Length == 0 || addressClass.Length == 0 || addressPrefix.Length == 0 ||
            cancelClass.Length == 0 ||
            !TryInt(root, "fileNameControlId", out int fileNameId) ||
            !TryInt(root, "addressControlId", out int addressId) ||
            !TryInt(root, "cancelControlId", out int cancelId))
        {
            return OperationResult.Fail<PhotoshopDocumentIdentitySignature>(
                FailureCode.EnvironmentNotVerified,
                "The Photoshop document-identity evidence is incomplete, so PrintFlow has no signed " +
                "route by which to establish which document is loaded.");
        }

        return OperationResult.Ok(new PhotoshopDocumentIdentitySignature(
            separator, dialogClass, dialogTitle, fileNameId, fileNameClass,
            addressId, addressClass, addressPrefix, cancelId, cancelClass));
    }

    private static OperationResult<PhotoshopW1ActionContract> ReadW1Action(JsonElement root)
    {
        JsonElement artifact = root.TryGetProperty("actionArtifact", out JsonElement a) ? a : default;
        JsonElement runtime = root.TryGetProperty("runtimeActionContract", out JsonElement r) ? r : default;
        string artifactPath = StringOrNull(artifact, "path") ?? string.Empty;
        string artifactHash = StringOrNull(artifact, "sha256") ?? string.Empty;
        string setName = StringOrNull(runtime, "setName") ?? string.Empty;

        if (artifactPath.Length == 0 || !Sha256.TryParse(artifactHash, out Sha256 sha256) ||
            setName.Length == 0 || !runtime.TryGetProperty("actions", out JsonElement actions) ||
            actions.ValueKind != JsonValueKind.Array)
        {
            return InvalidW1Evidence("artifact path/hash, exact set name or action array is missing");
        }

        ImmutableArray<PhotoshopW1BranchContract>.Builder branches =
            ImmutableArray.CreateBuilder<PhotoshopW1BranchContract>();
        foreach (JsonElement action in actions.EnumerateArray())
        {
            string branchText = StringOrNull(action, "branch") ?? string.Empty;
            WhiteUnderbaseBranch branch = branchText switch
            {
                "W1_0px" => WhiteUnderbaseBranch.W1_0px,
                "W1_1px" => WhiteUnderbaseBranch.W1_1px,
                "W1_2px" => WhiteUnderbaseBranch.W1_2px,
                _ => (WhiteUnderbaseBranch)(-1),
            };
            string actionName = StringOrNull(action, "actionName") ?? string.Empty;
            ImmutableArray<string> commands = StringArray(action, "runtimeCommands");
            if (!Enum.IsDefined(branch) || actionName.Length == 0 || commands.IsDefaultOrEmpty)
            {
                return InvalidW1Evidence($"branch '{branchText}' has no exact action name or command transcript");
            }

            branches.Add(new PhotoshopW1BranchContract(branch, actionName, commands));
        }

        if (branches.Count != 3 || branches.Select(b => b.Branch).Distinct().Count() != 3 ||
            branches.Select(b => b.ActionName).Distinct(StringComparer.Ordinal).Count() != 3)
        {
            return InvalidW1Evidence("the closed three-branch mapping is missing or duplicated");
        }

        foreach (WhiteUnderbaseBranch branch in Enum.GetValues<WhiteUnderbaseBranch>())
        {
            if (branches.Count(b => b.Branch == branch) != 1)
            {
                return InvalidW1Evidence($"branch '{branch}' does not occur exactly once");
            }
        }

        return OperationResult.Ok(new PhotoshopW1ActionContract(
            artifactPath, sha256, setName, branches.ToImmutable()));
    }

    private static OperationResult<PhotoshopOwnedDocumentCleanupSignature> ReadOwnedDocumentCleanup(
        JsonElement root)
    {
        JsonElement identity = root.TryGetProperty("saveAsCopyIdentity", out JsonElement i) ? i : default;
        JsonElement prompt = root.TryGetProperty("prompt", out JsonElement p) ? p : default;
        JsonElement message = prompt.TryGetProperty("message", out JsonElement m) ? m : default;

        if (!TryBool(identity, "fileNameFieldMayUseLastSaveFormatExtension", out bool extensionSubstitution) ||
            !extensionSubstitution)
        {
            return InvalidCleanupEvidence(
                "the observed post-Save-As-Copy identity filename-extension substitution is absent");
        }

        string windowClass = StringOrNull(prompt, "windowClassName") ?? string.Empty;
        string title = StringOrNull(prompt, "title") ?? string.Empty;
        string messageClass = StringOrNull(message, "controlClass") ?? string.Empty;
        string prefix = StringOrNull(message, "textPrefix") ?? string.Empty;
        string suffix = StringOrNull(message, "textSuffix") ?? string.Empty;
        string truncation = StringOrNull(message, "truncationMarker") ?? string.Empty;
        if (windowClass.Length == 0 || title.Length == 0 || messageClass.Length == 0 ||
            prefix.Length == 0 || suffix.Length == 0 || truncation.Length == 0 ||
            !TryInt(message, "controlId", out int messageId) ||
            !TryInt(message, "minimumDocumentNamePrefixLength", out int minimumPrefix) ||
            minimumPrefix < 8)
        {
            return InvalidCleanupEvidence("the prompt window or document-bound question is incomplete");
        }

        OperationResult<PhotoshopDiscardPromptControlSignature> save =
            ReadCleanupControl(prompt, "saveControl");
        OperationResult<PhotoshopDiscardPromptControlSignature> discard =
            ReadCleanupControl(prompt, "discardControl");
        OperationResult<PhotoshopDiscardPromptControlSignature> cancel =
            ReadCleanupControl(prompt, "cancelControl");
        if (save.IsFailure || discard.IsFailure || cancel.IsFailure)
        {
            return InvalidCleanupEvidence("the exact Save, discard and Cancel control set is incomplete");
        }

        if (new[] { save.Value.ControlId, discard.Value.ControlId, cancel.Value.ControlId }.Distinct().Count() != 3)
        {
            return InvalidCleanupEvidence("the Save, discard and Cancel controls are not distinct");
        }

        return OperationResult.Ok(new PhotoshopOwnedDocumentCleanupSignature(
            extensionSubstitution,
            windowClass,
            title,
            new PhotoshopDiscardPromptMessageSignature(
                messageId, messageClass, prefix, suffix, truncation, minimumPrefix),
            save.Value,
            discard.Value,
            cancel.Value));
    }

    private static OperationResult<PhotoshopDiscardPromptControlSignature> ReadCleanupControl(
        JsonElement prompt, string propertyName)
    {
        JsonElement control = prompt.TryGetProperty(propertyName, out JsonElement value) ? value : default;
        string className = StringOrNull(control, "class") ?? string.Empty;
        string text = StringOrNull(control, "text") ?? string.Empty;
        return className.Length > 0 && text.Length > 0 && TryInt(control, "id", out int id)
            ? OperationResult.Ok(new PhotoshopDiscardPromptControlSignature(id, className, text))
            : OperationResult.Fail<PhotoshopDiscardPromptControlSignature>(
                FailureCode.EnvironmentNotVerified,
                $"The Photoshop owned-document cleanup evidence has no complete {propertyName}.");
    }

    private static OperationResult<PhotoshopOwnedDocumentCleanupSignature> InvalidCleanupEvidence(
        string detail) =>
        OperationResult.Fail<PhotoshopOwnedDocumentCleanupSignature>(
            FailureCode.EnvironmentNotVerified,
            $"The Photoshop owned-document cleanup evidence is incomplete: {detail}.");

    private static OperationResult<Unit> VerifyManifestActionAgreement(
        JsonElement root, PhotoshopW1ActionContract contract)
    {
        JsonElement action = root.TryGetProperty("photoshopActionContract", out JsonElement value)
            ? value
            : default;
        string path = StringOrNull(action, "artifactPath") ?? string.Empty;
        string hash = StringOrNull(action, "artifactSha256") ?? string.Empty;
        string setName = StringOrNull(action, "setName") ?? string.Empty;
        if (!string.Equals(path, contract.ArtifactPath, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(hash, contract.ArtifactSha256.ToString(), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(setName, contract.SetName, StringComparison.Ordinal))
        {
            return OperationResult.Fail<Unit>(FailureCode.EnvironmentNotVerified,
                "The verified runtime W1 evidence disagrees with the preset's canonical Action artifact, " +
                "hash or exact set name.");
        }

        if (!action.TryGetProperty("actions", out JsonElement actions) ||
            actions.ValueKind != JsonValueKind.Object ||
            contract.Branches.Any(branch => !actions.TryGetProperty(branch.ActionName, out _)))
        {
            return OperationResult.Fail<Unit>(FailureCode.EnvironmentNotVerified,
                "The verified runtime W1 evidence names an Action not present in the preset contract.");
        }

        return OperationResult.Ok();
    }

    private static OperationResult<PhotoshopW1ActionContract> InvalidW1Evidence(string detail) =>
        OperationResult.Fail<PhotoshopW1ActionContract>(FailureCode.EnvironmentNotVerified,
            $"The Photoshop CMYK + W1 Action runtime evidence is incomplete: {detail}.");

    private static string? StringOrNull(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out JsonElement value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool TryInt(JsonElement element, string propertyName, out int value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out JsonElement property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt32(out value);
    }

    private static bool TryBool(JsonElement element, string propertyName, out bool value)
    {
        value = false;
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out JsonElement property) ||
            property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        value = property.GetBoolean();
        return true;
    }

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
