using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PrintFlow.Infrastructure.Configuration;

/// <summary>Workspace root configuration (Epic 11100 plan §18).</summary>
public sealed record WorkspaceConfiguration(string Root);

/// <summary>Database location, relative to the workspace root.</summary>
public sealed record DatabaseConfiguration(string RelativePath);

/// <summary>
/// Identifies the signed workstation preset this installation is configured against, and the
/// hash it must verify against before anything trusts it (Epic 11100 Task 11100.0).
/// </summary>
public sealed record PresetConfiguration(string Id, string Version, string Path, string ExpectedSha256);

/// <summary>Which adapter implementations the composition root wires up.</summary>
public sealed record AdaptersConfiguration(string Mode);

/// <summary>Local log retention.</summary>
public sealed record LoggingConfiguration(int RetentionDays);

/// <summary>
/// The complete <c>appsettings.json</c> shape (Epic 11100 plan §18, §3.2).
/// </summary>
/// <remarks>
/// Parsed with <see cref="System.Text.Json"/> directly rather than
/// <c>Microsoft.Extensions.Configuration</c>: the shape is small, flat, and has exactly one
/// source file plus one optional git-ignored override, so a configuration-binding framework
/// buys nothing here. No secret ever lives in this file — the preset hash is a public
/// integrity check, not sensitive content.
/// </remarks>
public sealed record PrintFlowConfiguration(
    WorkspaceConfiguration Workspace,
    DatabaseConfiguration Database,
    PresetConfiguration Preset,
    AdaptersConfiguration Adapters,
    LoggingConfiguration Logging)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>
    /// Loads <c>appsettings.json</c> from <paramref name="path"/>, then applies
    /// <c>appsettings.local.json</c> from the same directory if present.
    /// </summary>
    /// <remarks>
    /// The local override file is git-ignored (plan §19.3) and lets a developer point the
    /// workspace root or preset path somewhere other than the production location without
    /// editing the committed file.
    /// </remarks>
    public static PrintFlowConfiguration LoadFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string json = File.ReadAllText(path);
        PrintFlowConfiguration configuration = Parse(json);

        string localPath = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(path) ?? ".", "appsettings.local.json");
        if (File.Exists(localPath))
        {
            string localJson = File.ReadAllText(localPath);
            configuration = MergeLocalOverride(configuration, localJson);
        }

        return configuration;
    }

    /// <summary>Parses configuration from an in-memory JSON document (used by tests).</summary>
    public static PrintFlowConfiguration Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        PrintFlowConfiguration? configuration =
            JsonSerializer.Deserialize<PrintFlowConfiguration>(json, SerializerOptions);

        return configuration ?? throw new InvalidOperationException(
            "appsettings.json parsed to null; the file must contain a JSON object.");
    }

    /// <summary>
    /// A local override may replace any top-level section wholesale; sections it omits keep
    /// the committed file's values.
    /// </summary>
    private static PrintFlowConfiguration MergeLocalOverride(PrintFlowConfiguration baseline, string localJson)
    {
        using JsonDocument document = JsonDocument.Parse(localJson);
        JsonElement root = document.RootElement;

        WorkspaceConfiguration workspace = ReadSection(root, "Workspace", baseline.Workspace);
        DatabaseConfiguration database = ReadSection(root, "Database", baseline.Database);
        PresetConfiguration preset = ReadSection(root, "Preset", baseline.Preset);
        AdaptersConfiguration adapters = ReadSection(root, "Adapters", baseline.Adapters);
        LoggingConfiguration logging = ReadSection(root, "Logging", baseline.Logging);

        return new PrintFlowConfiguration(workspace, database, preset, adapters, logging);
    }

    private static T ReadSection<T>(JsonElement root, string propertyName, T fallback) =>
        root.TryGetProperty(propertyName, out JsonElement section)
            ? JsonSerializer.Deserialize<T>(section.GetRawText(), SerializerOptions) ?? fallback
            : fallback;
}
