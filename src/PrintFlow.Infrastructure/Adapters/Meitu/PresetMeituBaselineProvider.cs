using System.Collections.Immutable;
using System.Text.Json;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
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

        return OperationResult.Ok(new MeituBaseline(
            executablePath,
            digest,
            StringOrNull(contract, "acceptedVersion") ?? "(unrecorded)",
            StringOrNull(contract, "uiLanguage") ?? "(unrecorded)",
            StringArray(windowContract, "acceptedObservedTitles"),
            welcome.Value.WindowTitle,
            welcome.Value.Markers));
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
