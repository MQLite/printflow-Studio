using System.Collections.Immutable;
using System.Text.Json;
using PrintFlow.Domain.Files;
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
            identity.Value));
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
