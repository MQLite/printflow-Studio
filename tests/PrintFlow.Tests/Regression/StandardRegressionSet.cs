using System.Collections.Immutable;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PrintFlow.Tests.Regression;

/// <summary>
/// The seven categories SCRUM-11065 requires of the standard local regression set.
/// </summary>
/// <remarks>
/// Quoted from the original requirement rather than paraphrased, and spelled exactly as
/// <c>Set-PrintFlowProductionRevalidation.ps1</c> spells them, because these two lists decide
/// between them whether a production gate opens. A set that satisfied one spelling and not the
/// other would report itself complete and be recorded as incomplete, or worse.
/// </remarks>
public static class StandardRegressionCategories
{
    /// <summary>Every required category, in the order the requirement lists them.</summary>
    public static readonly ImmutableArray<string> Required =
    [
        "NORMAL_JPG_PORTRAIT",
        "COMPLEX_BACKGROUND_FINE_HAIR",
        "TRANSPARENT_PNG",
        "COMPLETE_CUSTOMER_DESIGN",
        "PSD_WITH_COMPOSITE_PREVIEW",
        "SINGLE_PAGE_PDF",
        "REFERENCE_PRODUCTION_TIFF",
    ];
}

/// <summary>What one asset's manifest promises about the file and about processing it.</summary>
/// <remarks>
/// Deliberately a partial view. The manifests carry provenance prose, honest-limits notes and
/// per-category expected properties that only a person reads; this type models the fields the
/// runner has to act on, and the loader keeps the rest as raw JSON so nothing is silently lost
/// when a manifest is read and re-read.
/// <para>
/// <c>ManifestPath</c> and <c>ManifestSha256</c> are the manifest file's own identity, so a run can
/// bind the set's <i>content</i> and not merely its id: a set re-issued under one name is a
/// different set, and "the same set passed" would otherwise be a claim about a name (PF-AUDIT-R1).
/// </para>
/// </remarks>
public sealed record RegressionAssetManifest(
    int SchemaVersion,
    string? SetId,
    string? FixtureSetVersion,
    string FixtureId,
    string Category,
    string SourcePath,
    string Sha256,
    long Length,
    string Format,
    string ExpectedWorkflow,
    ImmutableArray<string> ExpectedProcessingPath,
    ImmutableArray<string> ExpectedExternalApplications,
    string ComparisonMode,
    ImmutableArray<RegressionManualCheck> ManualChecks,
    RegressionBooleanExpectation EnhancedOutputIsNotSmallerThanSource,
    RegressionBooleanExpectation EnhancedOutputIsLargerThanSource,
    RegressionBooleanExpectation TrimBoundsStrictlyInsideCanvas,
    RegressionBooleanExpectation TrimMatchesAlphaBoundsAndMargins,
    string? ManifestPath = null,
    string? ManifestSha256 = null)
{
    /// <summary>The schema this build reads.</summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>Whether this asset's expected path involves an external application at all.</summary>
    public bool RequiresExternalApplication => ExpectedExternalApplications.Length > 0;
}

/// <summary>A named boolean exactly as a manifest supplied it.</summary>
/// <remarks>
/// Presence is separate from value so preflight can distinguish a missing property, a false
/// property and a property whose JSON type is not boolean. Those are three different authoring
/// failures and none may silently default to the v2 Enhancement contract.
/// </remarks>
public sealed record RegressionBooleanExpectation(bool Present, bool? Value);

/// <summary>A qualitative check the manifest states a person has to make.</summary>
/// <param name="Id">Stable identifier, so a decision can be recorded against it later.</param>
/// <param name="Question">What the reviewer is being asked.</param>
/// <param name="Automatable">Always false in practice; present so a manifest can say so out loud.</param>
public sealed record RegressionManualCheck(string Id, string Question, bool Automatable);

/// <summary>Why a set was rejected, in terms an operator can act on.</summary>
public sealed record RegressionSetProblem(string AssetId, string Detail);

/// <summary>
/// The set as a whole: its identity, its manifests, and whether it is fit to run.
/// </summary>
/// <remarks>
/// <b>Loading is not validating.</b> <see cref="Load"/> reads whatever is on disk and reports
/// what it could not read; <see cref="Validate"/> decides whether the result is a set. Keeping
/// them apart means an unreadable manifest produces "this manifest is unreadable" rather than an
/// exception from inside a runner that was halfway through starting Photoshop.
/// </remarks>
public sealed record StandardRegressionSet(
    string Root,
    string? SetId,
    ImmutableArray<RegressionAssetManifest> Assets,
    ImmutableArray<RegressionSetProblem> LoadProblems)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Reads every manifest under <paramref name="root"/>\manifests.</summary>
    public static StandardRegressionSet Load(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        string manifestFolder = Path.Combine(root, "manifests");
        if (!Directory.Exists(manifestFolder))
        {
            return new StandardRegressionSet(root, null, [],
                [new RegressionSetProblem("(set)", $"No manifests folder under '{root}'.")]);
        }

        List<RegressionAssetManifest> assets = [];
        List<RegressionSetProblem> problems = [];
        string? setId = null;

        foreach (string file in Directory.EnumerateFiles(manifestFolder, "*.json").Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            try
            {
                // ReadAllText rather than the byte overload: these manifests are written by
                // Windows PowerShell, whose utf8 encoding emits a BOM, and JsonDocument over raw
                // bytes rejects one.
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                });
                JsonElement rootElement = document.RootElement;
                setId ??= String(rootElement, "setId");

                RegressionAssetManifest? manifest = ReadManifest(rootElement, name, out string? failure);
                if (manifest is null)
                {
                    problems.Add(new RegressionSetProblem(name, failure ?? "Unreadable manifest."));
                    continue;
                }

                // The manifest's own bytes, digested here where the file is already open, so a run
                // can record which set content it read.
                assets.Add(manifest with
                {
                    ManifestPath = file,
                    ManifestSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))),
                });
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                problems.Add(new RegressionSetProblem(name, $"Unreadable manifest: {ex.Message}"));
            }
        }

        return new StandardRegressionSet(root, setId, [.. assets], [.. problems]);
    }

    /// <summary>
    /// The static half of the procedure: everything answerable without an external application.
    /// </summary>
    /// <remarks>
    /// This is Layer 1 in full. It refuses on any of: an unreadable manifest, a missing category,
    /// a duplicate category, a referenced file that is not there, a SHA-256 that does not match
    /// the bytes, a manifest still recording PENDING, an unknown comparison mode, or a set
    /// identity that disagrees with itself.
    /// <para>
    /// <b>The hash is recomputed, never trusted.</b> A fixed set whose integrity rested on file
    /// names would accept a replaced JPEG as the original, which is precisely the drift a set
    /// used for upgrade regression exists to catch.
    /// </para>
    /// </remarks>
    public ImmutableArray<RegressionSetProblem> Validate(bool requireExecutableExpectations = false)
    {
        List<RegressionSetProblem> problems = [.. LoadProblems];

        foreach (RegressionAssetManifest asset in Assets)
        {
            if (asset.SchemaVersion != RegressionAssetManifest.CurrentSchemaVersion)
            {
                problems.Add(new RegressionSetProblem(asset.FixtureId,
                    $"Manifest schema {asset.SchemaVersion}; this build reads schema " +
                    $"{RegressionAssetManifest.CurrentSchemaVersion}."));
            }

            if (!string.IsNullOrWhiteSpace(SetId) && !string.Equals(asset.SetId, SetId, StringComparison.Ordinal))
            {
                problems.Add(new RegressionSetProblem(asset.FixtureId,
                    $"Manifest names set '{asset.SetId}' but the set is '{SetId}'."));
            }

            string? expectedFixtureVersion = asset.SetId switch
            {
                "printflow-regression-v1" => "v1",
                "printflow-regression-v2" => "v2",
                "printflow-regression-v3" => "v3",
                _ => null,
            };
            if (expectedFixtureVersion is not null &&
                !string.Equals(asset.FixtureSetVersion, expectedFixtureVersion, StringComparison.Ordinal))
            {
                problems.Add(new RegressionSetProblem(asset.FixtureId,
                    $"Manifest set '{asset.SetId}' requires fixtureSetVersion " +
                    $"'{expectedFixtureVersion}', not '{asset.FixtureSetVersion ?? "(missing)"}'."));
            }

            if (!File.Exists(asset.SourcePath))
            {
                problems.Add(new RegressionSetProblem(asset.FixtureId,
                    $"The manifest's file '{asset.SourcePath}' does not exist."));
                continue;
            }

            FileInfo file = new(asset.SourcePath);
            if (file.Length != asset.Length)
            {
                problems.Add(new RegressionSetProblem(asset.FixtureId,
                    $"'{asset.SourcePath}' is {file.Length} bytes; the manifest records {asset.Length}."));
            }

            string actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(asset.SourcePath)));
            if (!string.Equals(actual, asset.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add(new RegressionSetProblem(asset.FixtureId,
                    $"SHA-256 drift. The manifest records {asset.Sha256}; the bytes hash to {actual}. " +
                    "The set is invalid until this is explicitly rebaselined."));
            }

            if (!ComparisonModes.Contains(asset.ComparisonMode))
            {
                problems.Add(new RegressionSetProblem(asset.FixtureId,
                    $"Unknown comparison mode '{asset.ComparisonMode}'. Expected one of " +
                    string.Join(", ", ComparisonModes) + "."));
            }

            if (asset.ExpectedProcessingPath.IsDefaultOrEmpty)
            {
                problems.Add(new RegressionSetProblem(asset.FixtureId,
                    "The manifest records no expected processing path, which the requirement asks for by name."));
            }


            if (requireExecutableExpectations &&
                string.Equals(asset.Category, "NORMAL_JPG_PORTRAIT", StringComparison.OrdinalIgnoreCase))
            {
                ValidatePortraitExecutionExpectation(asset, problems);
            }

            if (requireExecutableExpectations &&
                string.Equals(asset.Category, "COMPLEX_BACKGROUND_FINE_HAIR", StringComparison.OrdinalIgnoreCase))
            {
                ValidateFineHairExecutionExpectation(asset, problems);
            }
        }

        foreach (string category in StandardRegressionCategories.Required)
        {
            int count = Assets.Count(a => string.Equals(a.Category, category, StringComparison.OrdinalIgnoreCase));
            if (count == 0)
            {
                problems.Add(new RegressionSetProblem("(set)", $"Required category {category} is absent."));
            }
            else if (count > 1)
            {
                // One dedicated asset per category. Two assets claiming the same category means
                // one of the seven is being covered twice while another may be covered by a file
                // that was only labelled to fill a gap.
                problems.Add(new RegressionSetProblem("(set)",
                    $"Category {category} is claimed by {count} assets; the set expects exactly one."));
            }
        }

        return [.. problems];
    }

    private static void ValidatePortraitExecutionExpectation(
        RegressionAssetManifest asset,
        List<RegressionSetProblem> problems)
    {
        if (string.Equals(asset.SetId, "printflow-regression-v1", StringComparison.Ordinal))
        {
            problems.Add(new RegressionSetProblem(asset.FixtureId,
                "The v1 portrait uses the historical enhancedOutputIsLargerThanSource contract. " +
                "It remains readable for historical review but is incompatible with a new execution; " +
                "select printflow-regression-v2 explicitly."));
            return;
        }

        // v3 changes only the fine-hair trim expectation; its portrait keeps v2's contract exactly.
        string? version = asset.SetId switch
        {
            "printflow-regression-v2" => "v2",
            "printflow-regression-v3" => "v3",
            _ => null,
        };
        if (version is null)
        {
            return;
        }

        RegressionBooleanExpectation expected = asset.EnhancedOutputIsNotSmallerThanSource;
        if (!expected.Present)
        {
            problems.Add(new RegressionSetProblem(asset.FixtureId,
                $"The {version} portrait does not declare enhancedOutputIsNotSmallerThanSource."));
        }
        else if (expected.Value is null)
        {
            problems.Add(new RegressionSetProblem(asset.FixtureId,
                $"The {version} portrait expectation enhancedOutputIsNotSmallerThanSource must be boolean true."));
        }
        else if (expected.Value is false)
        {
            problems.Add(new RegressionSetProblem(asset.FixtureId,
                $"The {version} portrait expectation enhancedOutputIsNotSmallerThanSource is false; true is required."));
        }

        if (asset.EnhancedOutputIsLargerThanSource.Present)
        {
            problems.Add(new RegressionSetProblem(asset.FixtureId,
                $"The {version} portrait conflicts with the approved contract because it also declares " +
                "enhancedOutputIsLargerThanSource."));
        }
    }

    /// <summary>
    /// The v3 fine-hair trim contract: exact alpha-bounds/margin/crop geometry, never the v2
    /// unconditional shrink property under another name.
    /// </summary>
    /// <remarks>
    /// Only v3 is checked. v1 and v2 manifests keep their frozen <c>trimBoundsStrictlyInsideCanvas</c>
    /// meaning, and the caller keeps enforcing it for them; nothing here reinterprets that property.
    /// </remarks>
    private static void ValidateFineHairExecutionExpectation(
        RegressionAssetManifest asset,
        List<RegressionSetProblem> problems)
    {
        if (!string.Equals(asset.SetId, RegressionTrimGeometry.SetId, StringComparison.Ordinal))
        {
            return;
        }

        RegressionBooleanExpectation expected = asset.TrimMatchesAlphaBoundsAndMargins;
        if (!expected.Present)
        {
            problems.Add(new RegressionSetProblem(asset.FixtureId,
                "The v3 fine-hair manifest does not declare trimMatchesAlphaBoundsAndMargins."));
        }
        else if (expected.Value is null)
        {
            problems.Add(new RegressionSetProblem(asset.FixtureId,
                "The v3 fine-hair expectation trimMatchesAlphaBoundsAndMargins must be boolean true."));
        }
        else if (expected.Value is false)
        {
            problems.Add(new RegressionSetProblem(asset.FixtureId,
                "The v3 fine-hair expectation trimMatchesAlphaBoundsAndMargins is false; true is required."));
        }

        if (asset.TrimBoundsStrictlyInsideCanvas.Present)
        {
            problems.Add(new RegressionSetProblem(asset.FixtureId,
                "The v3 fine-hair manifest conflicts with the approved trim contract because it also " +
                "declares the v2 trimBoundsStrictlyInsideCanvas property."));
        }
    }

    private static readonly ImmutableArray<string> ComparisonModes =
        ["Structural", "ExactHash", "ReferenceProperties", "ManualVisual"];

    private static RegressionAssetManifest? ReadManifest(JsonElement root, string fallbackId, out string? failure)
    {
        failure = null;

        string? category = String(root, "category");
        if (string.IsNullOrWhiteSpace(category))
        {
            failure = "No category.";
            return null;
        }

        if (!root.TryGetProperty("file", out JsonElement file))
        {
            failure = "No file block.";
            return null;
        }

        string? path = String(file, "path");
        string? sha = String(file, "sha256");
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(sha))
        {
            failure = "The file block records no path or no sha256.";
            return null;
        }

        // "PENDING" anywhere in an accepted manifest is the one thing this loader treats as a
        // structural fault rather than a value. The set inherited exactly one such field and the
        // whole point of accepting the set is that it no longer has any.
        string raw = root.GetRawText();
        if (raw.Contains("\"PENDING\"", StringComparison.Ordinal))
        {
            failure = "The manifest still records a PENDING expectation. An accepted fixed set states outcomes.";
            return null;
        }

        return new RegressionAssetManifest(
            SchemaVersion: Int(root, "schemaVersion") ?? 0,
            SetId: String(root, "setId"),
            FixtureSetVersion: String(root, "fixtureSetVersion"),
            FixtureId: String(root, "fixtureId") ?? fallbackId,
            Category: category!.ToUpperInvariant(),
            SourcePath: path!,
            Sha256: sha!,
            Length: Long(file, "length") ?? -1,
            Format: String(file, "format") ?? "(unstated)",
            ExpectedWorkflow: String(root, "expectedWorkflow") ?? "(unstated)",
            ExpectedProcessingPath: Strings(root, "expectedProcessingPath"),
            ExpectedExternalApplications: Strings(root, "expectedExternalApplications"),
            ComparisonMode: root.TryGetProperty("comparisonPolicy", out JsonElement policy)
                ? String(policy, "mode") ?? "(unstated)"
                : "(unstated)",
            ManualChecks: ManualChecks(root),
            EnhancedOutputIsNotSmallerThanSource: BooleanExpectation(
                root, "enhancedOutputIsNotSmallerThanSource"),
            EnhancedOutputIsLargerThanSource: BooleanExpectation(
                root, "enhancedOutputIsLargerThanSource"),
            TrimBoundsStrictlyInsideCanvas: BooleanExpectation(
                root, "trimBoundsStrictlyInsideCanvas"),
            TrimMatchesAlphaBoundsAndMargins: BooleanExpectation(
                root, RegressionTrimGeometry.AssertionName));
    }

    private static RegressionBooleanExpectation BooleanExpectation(JsonElement root, string name)
    {
        if (!root.TryGetProperty("expectedProperties", out JsonElement properties) ||
            properties.ValueKind != JsonValueKind.Object ||
            !properties.TryGetProperty(name, out JsonElement value))
        {
            return new RegressionBooleanExpectation(false, null);
        }

        return new RegressionBooleanExpectation(true, value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        });
    }

    private static ImmutableArray<RegressionManualCheck> ManualChecks(JsonElement root)
    {
        if (!root.TryGetProperty("manualChecks", out JsonElement checks) ||
            checks.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return
        [
            .. checks.EnumerateArray()
                .Where(c => c.ValueKind == JsonValueKind.Object)
                .Select(c => new RegressionManualCheck(
                    String(c, "id") ?? "(unnamed)",
                    String(c, "question") ?? "(unstated)",
                    c.TryGetProperty("automatable", out JsonElement a) && a.ValueKind == JsonValueKind.True)),
        ];
    }

    private static string? String(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int result) ? result : null;

    private static long? Long(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.TryGetInt64(out long result) ? result : null;

    private static ImmutableArray<string> Strings(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return
        [
            .. value.EnumerateArray()
                .Where(v => v.ValueKind == JsonValueKind.String)
                .Select(v => v.GetString()!),
        ];
    }
}
