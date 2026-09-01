using System.Collections.Immutable;
using System.Text.Json;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Preset;

namespace PrintFlow.Infrastructure.Verification;

/// <summary>
/// Interprets the accepted workstation requirements out of the hash-verified preset manifest
/// (Part A §3, §4).
/// </summary>
/// <remarks>
/// The order is the whole point and is not negotiable: the configured manifest is opened
/// read-only and refused unless its bytes hash to the configured SHA-256, and only the bytes
/// that passed that check are ever parsed. A manifest whose hash is wrong is not a manifest with
/// a suspect field; it is not evidence at all, and nothing in it is read for meaning.
/// <para>
/// Every section this reads is required. A manifest that omits one is a contract gap, reported
/// as such and failing closed — never filled in from a previous report, a default, or a value
/// remembered from Epic 11400 (§27).
/// </para>
/// </remarks>
internal static class PresetWorkstationRequirements
{
    /// <summary>
    /// Verifies the manifest's own digest, then reads the accepted requirements from it.
    /// </summary>
    /// <param name="manifestAbsolutePath">The configured preset manifest.</param>
    /// <param name="presetId">The configured preset id.</param>
    /// <param name="presetVersion">The configured preset version.</param>
    /// <param name="expectedSha256">The digest configuration says the manifest must hash to.</param>
    internal static OperationResult<WorkstationRequirements> Read(
        string manifestAbsolutePath, string presetId, string presetVersion, Sha256 expectedSha256)
    {
        OperationResult<JsonDocument> manifest = VerifiedJsonFile.Read(
            manifestAbsolutePath, expectedSha256, "Workstation preset manifest");
        if (manifest.IsFailure)
        {
            return OperationResult.Fail<WorkstationRequirements>(manifest.Failure);
        }

        using JsonDocument document = manifest.Value;
        JsonElement root = document.RootElement;

        // The manifest states its own identity. Configuration states which identity it expected.
        // Disagreement means appsettings points at a manifest that hashes correctly but is not
        // the preset this installation was configured against — refused rather than reconciled.
        string? declaredId = StringOrNull(root, "presetId");
        string? declaredVersion = StringOrNull(root, "presetVersion");
        if (!string.Equals(declaredId, presetId, StringComparison.Ordinal) ||
            !string.Equals(declaredVersion, presetVersion, StringComparison.Ordinal))
        {
            return Gap(
                $"The verified manifest declares preset '{declaredId} {declaredVersion}', but this " +
                $"installation is configured for '{presetId} {presetVersion}'.");
        }

        if (!TryObject(root, "workstation", out JsonElement workstation) ||
            !TryObject(workstation, "operatingSystem", out JsonElement operatingSystem))
        {
            return Gap("The verified preset records no workstation.operatingSystem section.");
        }

        string? edition = StringOrNull(operatingSystem, "edition");
        string? version = StringOrNull(operatingSystem, "version");
        string? build = StringOrNull(operatingSystem, "build");
        string? architecture = StringOrNull(operatingSystem, "architecture");
        string? uiCulture = StringOrNull(operatingSystem, "uiCulture");
        if (edition is null || version is null || build is null || architecture is null || uiCulture is null)
        {
            return Gap(
                "The verified preset's workstation.operatingSystem section is incomplete; edition, " +
                "version, build, architecture and uiCulture are all required.");
        }

        if (!TryObject(workstation, "sessionContract", out JsonElement sessionContract))
        {
            return Gap("The verified preset records no workstation.sessionContract section.");
        }

        string? allowedSession = StringOrNull(sessionContract, "allowedSession");
        if (allowedSession is null)
        {
            return Gap("The verified preset's sessionContract records no allowedSession.");
        }

        AcceptedSession session = new(
            allowedSession,
            BoolOrDefault(sessionContract, "remoteAutomationAllowed", false),
            BoolOrDefault(sessionContract, "blockOnActiveRemoteSession", true));

        OperationResult<AcceptedDisplay> display = ReadDisplay(root);
        if (display.IsFailure)
        {
            return OperationResult.Fail<WorkstationRequirements>(display.Failure);
        }

        if (!TryObject(root, "storageAndNamingContract", out JsonElement storage))
        {
            return Gap("The verified preset records no storageAndNamingContract section.");
        }

        string? outputRoot = StringOrNull(storage, "defaultOutputRoot");
        if (outputRoot is null)
        {
            return Gap("The verified preset's storageAndNamingContract records no defaultOutputRoot.");
        }

        AcceptedWorkspace workspace = new(
            outputRoot, StringOrNull(storage, "volume"), StringOrNull(storage, "fileSystem"));

        OperationResult<AcceptedExecutable> meitu = ReadExecutable(root, "meituContract", "Meitu");
        if (meitu.IsFailure)
        {
            return OperationResult.Fail<WorkstationRequirements>(meitu.Failure);
        }

        OperationResult<AcceptedExecutable> photoshop = ReadExecutable(root, "photoshopContract", "Photoshop");
        if (photoshop.IsFailure)
        {
            return OperationResult.Fail<WorkstationRequirements>(photoshop.Failure);
        }

        OperationResult<AcceptedActionArtifact> action = ReadActionArtifact(root);
        if (action.IsFailure)
        {
            return OperationResult.Fail<WorkstationRequirements>(action.Failure);
        }

        OperationResult<ImmutableArray<AcceptedEvidence>> evidence = ReadEvidence(root);
        if (evidence.IsFailure)
        {
            return OperationResult.Fail<WorkstationRequirements>(evidence.Failure);
        }

        return OperationResult.Ok(new WorkstationRequirements(
            new ProductionPresetRef(presetId, presetVersion, expectedSha256),
            new AcceptedOperatingSystem(edition, version, build, architecture, uiCulture),
            session,
            display.Value,
            workspace,
            meitu.Value,
            photoshop.Value,
            action.Value,
            evidence.Value));
    }

    private static OperationResult<AcceptedDisplay> ReadDisplay(JsonElement root)
    {
        if (!TryObject(root, "displayContract", out JsonElement contract))
        {
            return Gap<AcceptedDisplay>("The verified preset records no displayContract section.");
        }

        DisplayRectangle? bounds = ReadRectangle(contract, "monitorBounds");
        DisplayRectangle? workArea = ReadRectangle(contract, "workArea");
        int? count = IntOrNull(contract, "activeDisplayCount");
        int? dpi = IntOrNull(contract, "systemDpi");
        int? scale = IntOrNull(contract, "scalePercent");
        string? primary = StringOrNull(contract, "primaryDisplay");

        if (bounds is null || workArea is null || count is null || dpi is null || scale is null || primary is null)
        {
            return Gap<AcceptedDisplay>(
                "The verified preset's displayContract is incomplete; activeDisplayCount, " +
                "primaryDisplay, monitorBounds, workArea, systemDpi and scalePercent are all required.");
        }

        return OperationResult.Ok(
            new AcceptedDisplay(count.Value, primary, bounds, workArea, dpi.Value, scale.Value));
    }

    private static OperationResult<AcceptedExecutable> ReadExecutable(
        JsonElement root, string section, string application)
    {
        if (!TryObject(root, section, out JsonElement contract))
        {
            return Gap<AcceptedExecutable>($"The verified preset records no {section} section.");
        }

        string? path = StringOrNull(contract, "executablePath");
        if (string.IsNullOrWhiteSpace(path))
        {
            return Gap<AcceptedExecutable>(
                $"The verified preset's {section} records no executablePath, so no {application} " +
                "installation is accepted.");
        }

        if (!Sha256.TryParse(StringOrNull(contract, "executableSha256") ?? string.Empty, out Sha256 digest))
        {
            return Gap<AcceptedExecutable>(
                $"The verified preset's {section} records no usable executableSha256, so the " +
                $"accepted {application} binary cannot be identified.");
        }

        // Version facts are read exactly as the manifest records them, including the absence of
        // one. Photoshop's accepted identity here is productVersion "20.0" plus the full build
        // fileVersion — not the "20.0.10" shorthand some earlier prose used, which the accepted
        // binary does not report and which this verifier therefore never demands (§13).
        return OperationResult.Ok(new AcceptedExecutable(
            path,
            digest,
            StringOrNull(contract, "productVersion") ?? StringOrNull(contract, "acceptedVersion"),
            StringOrNull(contract, "fileVersion"),
            StringOrNull(contract, "uiLanguage")));
    }

    private static OperationResult<AcceptedActionArtifact> ReadActionArtifact(JsonElement root)
    {
        if (!TryObject(root, "photoshopActionContract", out JsonElement contract))
        {
            return Gap<AcceptedActionArtifact>(
                "The verified preset records no photoshopActionContract section.");
        }

        string? path = StringOrNull(contract, "artifactPath");
        string? setName = StringOrNull(contract, "setName");
        long? bytes = LongOrNull(contract, "artifactBytes");
        if (string.IsNullOrWhiteSpace(path) || setName is null || bytes is null ||
            !Sha256.TryParse(StringOrNull(contract, "artifactSha256") ?? string.Empty, out Sha256 digest))
        {
            return Gap<AcceptedActionArtifact>(
                "The verified preset's photoshopActionContract is incomplete; setName, artifactPath, " +
                "artifactBytes and artifactSha256 are all required to identify the canonical Action file.");
        }

        return OperationResult.Ok(new AcceptedActionArtifact(setName, path, bytes.Value, digest));
    }

    private static OperationResult<ImmutableArray<AcceptedEvidence>> ReadEvidence(JsonElement root)
    {
        if (!root.TryGetProperty("sourceManifestIntegrity", out JsonElement integrity) ||
            integrity.ValueKind != JsonValueKind.Array)
        {
            return Gap<ImmutableArray<AcceptedEvidence>>(
                "The verified preset records no sourceManifestIntegrity list, so its evidence chain " +
                "cannot be verified.");
        }

        ImmutableArray<AcceptedEvidence>.Builder entries = ImmutableArray.CreateBuilder<AcceptedEvidence>();
        foreach (JsonElement entry in integrity.EnumerateArray())
        {
            string? path = StringOrNull(entry, "path");
            if (string.IsNullOrWhiteSpace(path) ||
                !Sha256.TryParse(StringOrNull(entry, "sha256") ?? string.Empty, out Sha256 digest))
            {
                return Gap<ImmutableArray<AcceptedEvidence>>(
                    "A sourceManifestIntegrity entry states no usable path and SHA-256 pair. An " +
                    "unreadable integrity row is refused rather than skipped.");
            }

            entries.Add(new AcceptedEvidence(path, digest));
        }

        if (entries.Count == 0)
        {
            return Gap<ImmutableArray<AcceptedEvidence>>(
                "The verified preset's sourceManifestIntegrity list is empty, so it vouches for nothing.");
        }

        return OperationResult.Ok(entries.ToImmutable());
    }

    /// <summary>
    /// A required fact the accepted preset does not state. Reported as a contract gap rather
    /// than guessed at, hard-coded, or copied from an earlier report (§27).
    /// </summary>
    private static OperationResult<WorkstationRequirements> Gap(string detail) =>
        Gap<WorkstationRequirements>(detail);

    private static OperationResult<T> Gap<T>(string detail) =>
        OperationResult.Fail<T>(FailureCode.EnvironmentNotVerified, detail);

    private static bool TryObject(JsonElement parent, string name, out JsonElement value) =>
        parent.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Object;

    private static string? StringOrNull(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? IntOrNull(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out int number)
            ? number
            : null;

    private static long? LongOrNull(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt64(out long number)
            ? number
            : null;

    private static bool BoolOrDefault(JsonElement parent, string name, bool fallback) =>
        parent.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;

    private static DisplayRectangle? ReadRectangle(JsonElement parent, string name)
    {
        if (!TryObject(parent, name, out JsonElement rectangle))
        {
            return null;
        }

        int? left = IntOrNull(rectangle, "left");
        int? top = IntOrNull(rectangle, "top");
        int? right = IntOrNull(rectangle, "right");
        int? bottom = IntOrNull(rectangle, "bottom");

        return left is null || top is null || right is null || bottom is null
            ? null
            : new DisplayRectangle(left.Value, top.Value, right.Value, bottom.Value);
    }
}
