using System.IO;
using System.Text.Json;
using PrintFlow.Domain.Files;
using PrintFlow.Infrastructure.Preset;
using PrintFlow.Infrastructure.Configuration;

namespace PrintFlow.Tests.Fixtures;

/// <summary>
/// The <c>storageAndNamingContract</c> patterns the accepted workstation manifest actually
/// carries, together with the means to check that claim against the manifest itself
/// (naming-contract fix §9).
/// </summary>
/// <remarks>
/// These four strings are transcribed from the immutable accepted manifest, which lives beside
/// the signed Epic 11000 baseline and is never copied into the repository. Every accepted
/// version from v1.0.0 through v1.8.0 writes them exactly this way.
/// <para>
/// Transcription is a risk in itself — a constant can drift from the file it claims to mirror —
/// so <see cref="ReadConfiguredManifestPatterns"/> reads the real manifest at the configured
/// path and the accompanying tests compare the two whenever a workstation actually has it.
/// Nothing here writes to, reformats, or opens that file for anything but reading.
/// </para>
/// </remarks>
internal static class AcceptedNamingContract
{
    public const string EnhancedPattern = "{Name}_HD.png";

    public const string CutoutPattern = "{Name}_CUTOUT.png";

    public const string ProductionTiffPattern = "{Name}_{SizeMm}mm_CMYK_W.tif";

    public const string CollisionPattern = "_{Sequence:00}";

    /// <summary>The four accepted patterns, keyed by their manifest property names.</summary>
    public static IReadOnlyDictionary<string, string> Patterns { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["enhancedPattern"] = EnhancedPattern,
            ["cutoutPattern"] = CutoutPattern,
            ["productionTiffPattern"] = ProductionTiffPattern,
            ["collisionPattern"] = CollisionPattern,
        };

    /// <summary>
    /// The naming patterns held by the manifest this installation is configured against, or
    /// <see langword="null"/> when that manifest is not present on this machine.
    /// </summary>
    /// <remarks>
    /// The path comes from the repository's own committed <c>appsettings.json</c> — the same
    /// configuration the shell starts from — so this reads whichever manifest production would
    /// read, rather than one the test chose.
    /// </remarks>
    public static IReadOnlyDictionary<string, string>? ReadConfiguredManifestPatterns()
    {
        string? settings = RepositoryFile("appsettings.json");
        if (settings is null)
        {
            return null;
        }

        PrintFlowConfiguration configuration = PrintFlowConfiguration.LoadFromFile(settings);
        string manifest = Path.Combine(configuration.Workspace.Root, configuration.Preset.Path);
        if (!File.Exists(manifest))
        {
            return null;
        }

        // Read-only and shared-read, like the production loader: the accepted manifest is
        // signed evidence and is never opened with a write intent.
        using FileStream stream = new(manifest, FileMode.Open, FileAccess.Read, FileShare.Read);
        using JsonDocument document = JsonDocument.Parse(stream);

        if (!document.RootElement.TryGetProperty("storageAndNamingContract", out JsonElement contract))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        Dictionary<string, string> found = new(StringComparer.Ordinal);
        foreach (string property in Patterns.Keys)
        {
            if (contract.TryGetProperty(property, out JsonElement value) &&
                value.ValueKind == JsonValueKind.String)
            {
                found[property] = value.GetString()!;
            }
        }

        return found;
    }

    /// <summary>
    /// A real <see cref="WorkstationPresetProvider"/> over the accepted manifest this
    /// installation is configured against, or <see langword="null"/> when that manifest is not
    /// present on this machine.
    /// </summary>
    /// <remarks>
    /// The identity, path and expected hash all come from the repository's committed
    /// <c>appsettings.json</c>, so the provider verifies the same bytes against the same digest
    /// the shell would. A hash mismatch therefore fails the caller rather than being papered
    /// over — that is the point of returning the production loader instead of a stand-in.
    /// </remarks>
    public static WorkstationPresetProvider? ConfiguredProvider()
    {
        string? settings = RepositoryFile("appsettings.json");
        if (settings is null)
        {
            return null;
        }

        PrintFlowConfiguration configuration = PrintFlowConfiguration.LoadFromFile(settings);
        string manifest = Path.Combine(configuration.Workspace.Root, configuration.Preset.Path);
        if (!File.Exists(manifest) || !Sha256.TryParse(configuration.Preset.ExpectedSha256, out Sha256 expected))
        {
            return null;
        }

        return new WorkstationPresetProvider(
            manifest, configuration.Preset.Id, configuration.Preset.Version, expected);
    }

    /// <summary>Locates a file in the repository root, walking up from the test output directory.</summary>
    private static string? RepositoryFile(string fileName)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "PrintFlowStudio.sln")))
        {
            current = current.Parent;
        }

        if (current is null)
        {
            return null;
        }

        string path = Path.Combine(current.FullName, fileName);
        return File.Exists(path) ? path : null;
    }
}
