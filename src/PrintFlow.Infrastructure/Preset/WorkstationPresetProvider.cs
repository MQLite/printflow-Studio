using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Preset;

/// <summary>
/// Loads the signed workstation preset manifest, verifies its SHA-256, and deserialises it
/// into immutable configuration (Epic 11100 Task 11100.0).
/// </summary>
/// <remarks>
/// Rules that hold unconditionally:
/// <list type="bullet">
///   <item>the manifest is opened read-only and streamed in full — never memory-mapped for
///         writing, never opened with any write share flag;</item>
///   <item>the file is never rewritten, reformatted, or normalised;</item>
///   <item>a hash mismatch or a missing/unreadable/invalid-JSON manifest fails closed with a
///         structured failure — never a silent fallback to "trust it anyway";</item>
///   <item>the result is computed once and cached, because the manifest is signed evidence
///         that does not change while the process runs.</item>
/// </list>
/// The manifest's full production schema (geometry limits, W1 branch identifiers, executable
/// hashes) belongs to Epic 11500's environment gate. This slice verifies integrity and reads
/// only the fields Epic 11100 itself consumes: identity, hash, and the naming patterns
/// (plan §3.3).
/// </remarks>
public sealed class WorkstationPresetProvider : IWorkstationPresetProvider
{
    private readonly string _manifestAbsolutePath;
    private readonly string _presetId;
    private readonly string _presetVersion;
    private readonly Sha256 _expectedSha256;
    private readonly Lazy<OperationResult<VerifiedPreset>> _verification;

    public WorkstationPresetProvider(
        string manifestAbsolutePath, string presetId, string presetVersion, Sha256 expectedSha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestAbsolutePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(presetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(presetVersion);

        _manifestAbsolutePath = manifestAbsolutePath;
        _presetId = presetId;
        _presetVersion = presetVersion;
        _expectedSha256 = expectedSha256;
        _verification = new Lazy<OperationResult<VerifiedPreset>>(Verify);
    }

    /// <inheritdoc />
    public OperationResult<ProductionPresetRef> GetVerifiedPreset() =>
        _verification.Value.Map(v => v.Reference);

    /// <inheritdoc />
    public OperationResult<NamingPatternSet> GetNamingPatterns() =>
        _verification.Value.Map(v => v.Naming);

    /// <inheritdoc />
    public OperationResult<PresetPrintRecommendationSet> GetPrintSizeRecommendations() =>
        _verification.Value.Map(v => v.Recommendations);

    private readonly record struct VerifiedPreset(
        ProductionPresetRef Reference,
        NamingPatternSet Naming,
        PresetPrintRecommendationSet Recommendations);

    private OperationResult<VerifiedPreset> Verify()
    {
        if (!File.Exists(_manifestAbsolutePath))
        {
            return OperationResult.Fail<VerifiedPreset>(
                FailureCode.EnvironmentNotVerified,
                $"Workstation preset manifest not found at '{_manifestAbsolutePath}'.");
        }

        byte[] bytes;
        try
        {
            // Read-only, shared-read: this file lives beside signed Epic 11000 evidence and
            // must never be opened with a write intent.
            using FileStream stream = new(
                _manifestAbsolutePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 1 << 20, useAsync: false);
            using MemoryStream buffer = new(checked((int)Math.Min(stream.Length, int.MaxValue)));
            stream.CopyTo(buffer);
            bytes = buffer.ToArray();
        }
        catch (IOException ex)
        {
            return OperationResult.Fail<VerifiedPreset>(
                FailureCode.EnvironmentNotVerified,
                $"Workstation preset manifest could not be read: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return OperationResult.Fail<VerifiedPreset>(
                FailureCode.EnvironmentNotVerified,
                $"Workstation preset manifest could not be read: {ex.Message}");
        }

        Sha256 actual = Sha256.FromBytes(SHA256.HashData(bytes));
        if (!actual.Equals(_expectedSha256))
        {
            return OperationResult.Fail<VerifiedPreset>(
                FailureCode.PresetHashMismatch,
                $"Preset manifest hash mismatch: expected {_expectedSha256}, computed {actual}.");
        }

        NamingPatternSet naming;
        PresetPrintRecommendationSet recommendations;
        try
        {
            naming = ReadNamingPatterns(bytes);
            recommendations = ReadPrintSizeRecommendations(bytes);
        }
        catch (JsonException ex)
        {
            return OperationResult.Fail<VerifiedPreset>(
                FailureCode.EnvironmentNotVerified,
                $"Workstation preset manifest is not valid JSON: {ex.Message}");
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
        {
            // A configured limit the Domain refuses — zero, negative, or a named size written
            // twice — is a manifest saying something no production run could act on. Refused
            // rather than partially loaded: a half-read geometry contract would silently offer
            // fewer sizes than the shop configured (Epic 11400 Part B1A.2D §3).
            return OperationResult.Fail<VerifiedPreset>(
                FailureCode.EnvironmentNotVerified,
                $"Workstation preset geometry contract is not usable: {ex.Message}");
        }

        ProductionPresetRef reference = new(_presetId, _presetVersion, actual);
        return OperationResult.Ok(new VerifiedPreset(reference, naming, recommendations));
    }

    /// <summary>
    /// Reads <c>productionGeometryContract.resize.limitsMillimetres</c> — the executable
    /// named-size recommendations (Epic 11400 Part B1A.2D §3, §4).
    /// </summary>
    /// <remarks>
    /// <b>The only place a preset name becomes millimetres.</b> Each configured entry states
    /// either a two-bound box (<c>maxWidth</c> and <c>maxHeight</c>) or a single
    /// <c>maxLongEdge</c>, and the two are kept apart rather than normalised, because which form
    /// was configured is part of what the shop decided (§6).
    /// <para>
    /// There is deliberately <b>no fallback</b>, unlike the naming patterns above. A missing
    /// naming pattern has a documented design default to fall back to; a missing print limit does
    /// not, and <c>PrintDimensions.NominalMillimetres</c> is emphatically not one — the ISO paper
    /// size a preset is named after and the limit this shop prints it at are different numbers.
    /// A manifest that configures nothing yields an empty set, and no named size is offered (§4).
    /// </para>
    /// <para>
    /// An entry the manifest does not name in <see cref="SizePreset"/> vocabulary is skipped
    /// rather than guessed at. A future preset naming a size this build does not know about is
    /// not an error — the build simply cannot offer it — and inventing an enum member for it here
    /// would be this code deciding what the shop meant.
    /// </para>
    /// </remarks>
    private static PresetPrintRecommendationSet ReadPrintSizeRecommendations(byte[] manifestBytes)
    {
        using JsonDocument document = JsonDocument.Parse(manifestBytes);

        if (!document.RootElement.TryGetProperty("productionGeometryContract", out JsonElement geometry) ||
            !geometry.TryGetProperty("resize", out JsonElement resize) ||
            !resize.TryGetProperty("limitsMillimetres", out JsonElement limits) ||
            limits.ValueKind != JsonValueKind.Object)
        {
            return new PresetPrintRecommendationSet([]);
        }

        List<PresetPrintRecommendation> recommendations = [];
        foreach (JsonProperty entry in limits.EnumerateObject())
        {
            if (PresetOf(entry.Name) is not { } preset || entry.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (DecimalOrNull(entry.Value, "maxLongEdge") is { } longEdge)
            {
                recommendations.Add(PresetPrintRecommendation.MaximumLongEdge(preset, longEdge));
                continue;
            }

            if (DecimalOrNull(entry.Value, "maxWidth") is { } width &&
                DecimalOrNull(entry.Value, "maxHeight") is { } height)
            {
                recommendations.Add(PresetPrintRecommendation.MaximumBox(preset, width, height));
            }
        }

        return new PresetPrintRecommendationSet(recommendations);
    }

    /// <summary>The manifest's size keys, in this build's vocabulary.</summary>
    /// <remarks>
    /// Written out rather than derived from the enum name, because the manifest's spelling is a
    /// published contract and the enum's is an implementation detail. If either is renamed, this
    /// mapping is where the two are reconciled deliberately.
    /// </remarks>
    private static SizePreset? PresetOf(string manifestKey) => manifestKey switch
    {
        "A3_LANDSCAPE" => SizePreset.A3Landscape,
        "A3_PORTRAIT" => SizePreset.A3Portrait,
        "A4" => SizePreset.A4,
        "A5" => SizePreset.A5,
        _ => null,
    };

    private static decimal? DecimalOrNull(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out JsonElement value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetDecimal(out decimal millimetres)
            ? millimetres
            : null;

    /// <summary>
    /// Reads <c>storageAndNamingContract</c> from the manifest, falling back to the MVP
    /// design's own documented pattern examples for any field the manifest omits.
    /// </summary>
    /// <remarks>
    /// The synthetic fixtures used in tests intentionally carry only the fields those tests
    /// exercise; falling back per-field (rather than failing when the section is absent) keeps
    /// the provider honest about "verified" (the hash matched) versus "complete" (every future
    /// production field is present) — completeness is Epic 11500's concern.
    /// </remarks>
    private static NamingPatternSet ReadNamingPatterns(byte[] manifestBytes)
    {
        using JsonDocument document = JsonDocument.Parse(manifestBytes);
        JsonElement root = document.RootElement;

        NamingPatternSet fallback = NamingPatternSet.DesignDefault;

        if (!root.TryGetProperty("storageAndNamingContract", out JsonElement contract))
        {
            return fallback;
        }

        return new NamingPatternSet(
            EnhancedPattern: StringOrDefault(contract, "enhancedPattern", fallback.EnhancedPattern),
            CutoutPattern: StringOrDefault(contract, "cutoutPattern", fallback.CutoutPattern),
            ProductionTiffPattern: StringOrDefault(contract, "productionTiffPattern", fallback.ProductionTiffPattern),
            CollisionSuffixPattern: StringOrDefault(contract, "collisionPattern", fallback.CollisionSuffixPattern));
    }

    private static string StringOrDefault(JsonElement element, string propertyName, string fallback) =>
        element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;
}
