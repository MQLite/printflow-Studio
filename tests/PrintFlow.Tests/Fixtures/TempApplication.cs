using System.IO;
using System.Text;
using PrintFlow.Domain.Files;

namespace PrintFlow.Tests.Fixtures;

/// <summary>
/// A throwaway installed layout — its own configuration file, workspace root, database location
/// and synthetic preset — so the real startup sequence can be run end to end without touching
/// <c>D:\PrintFlowStudio</c> (Epic 11100 Part 3C1 §9, §15).
/// </summary>
internal sealed class TempApplication : IDisposable
{
    private const string PresetRelativePath = @"preset\synthetic-preset.json";
    private const string DatabaseRelativePath = @"Data\printflow.db";

    private readonly string _root;

    public TempApplication(string adapterMode = "Fake", string? presetSha256Override = null)
    {
        _root = Path.Combine(Path.GetTempPath(), "PrintFlowTests", Guid.NewGuid().ToString("N"));
        WorkspaceRoot = Path.Combine(_root, "workspace");
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(WorkspaceRoot);

        (_, Sha256 presetHash) = PresetFixture.Write(
            Path.Combine(WorkspaceRoot, "preset"), "synthetic-preset.json");

        ConfigurationFilePath = Path.Combine(_root, "appsettings.json");
        File.WriteAllText(
            ConfigurationFilePath,
            $$"""
              {
                "Workspace": { "Root": {{Quote(WorkspaceRoot)}} },
                "Database": { "RelativePath": {{Quote(DatabaseRelativePath)}} },
                "Preset": {
                  "Id": "{{PresetFixture.PresetId}}",
                  "Version": "{{PresetFixture.PresetVersion}}",
                  "Path": {{Quote(PresetRelativePath)}},
                  "ExpectedSha256": "{{presetSha256Override ?? presetHash.Value}}"
                },
                "Adapters": { "Mode": "{{adapterMode}}" },
                "Logging": { "RetentionDays": 30 }
              }
              """,
            Encoding.UTF8);
    }

    /// <summary>The <c>appsettings.json</c> the startup sequence is pointed at.</summary>
    public string ConfigurationFilePath { get; }

    /// <summary>The workspace root that configuration names.</summary>
    public string WorkspaceRoot { get; }

    /// <summary>Where the startup sequence will create the application database.</summary>
    public string DatabasePath => Path.Combine(WorkspaceRoot, DatabaseRelativePath);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        try
        {
            if (Directory.Exists(_root))
            {
                foreach (string file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup; a locked file must not fail an unrelated test.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string Quote(string path) =>
        System.Text.Json.JsonSerializer.Serialize(path);
}
