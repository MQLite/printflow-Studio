using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using PrintFlow.Infrastructure.Configuration;

namespace PrintFlow.Tests.Regression;

/// <summary>A run-local executable selection, never an edit to the accepted workstation preset.</summary>
internal static class RegressionMeituOverride
{
    internal const string EnvironmentVariable = "PRINTFLOW_REGRESSION_MEITU_EXECUTABLE";
    internal const string PublicationProblem =
        "Operator-selected Meitu executable override: this run uses a derived preset, not the " +
        "accepted workstation baseline. Publication is forbidden pending separate preset revalidation.";

    internal static PrintFlowConfiguration Apply(
        PrintFlowConfiguration configuration, string executablePath, string runFolder)
    {
        string path = Path.GetFullPath(executablePath);
        if (!string.Equals(Path.GetFileName(path), "XiuXiu.exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The Meitu override must name an explicit XiuXiu.exe.");
        }

        // Pin bytes; the unchanged Product adapter still checks this hash before using the process.
        string executableSha = Hash(File.ReadAllBytes(path));
        string version = FileVersionInfo.GetVersionInfo(path).FileVersion ?? "unknown";
        string originalPath = Path.Combine(configuration.Workspace.Root, configuration.Preset.Path);
        byte[] original = File.ReadAllBytes(originalPath);
        if (!string.Equals(Hash(original), configuration.Preset.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The original preset hash does not match configuration.");
        }

        JsonObject preset = JsonNode.Parse(original)!.AsObject();
        JsonObject meitu = preset["meituContract"]!.AsObject();
        JsonNode originalMeitu = meitu.DeepClone();
        meitu["executablePath"] = path;
        meitu["executableSha256"] = executableSha;
        meitu["acceptedVersion"] = version;
        preset["status"] = "REGRESSION_EXECUTABLE_OVERRIDE_NOT_REVALIDATED";
        preset["regressionExecutableOverride"] = new JsonObject
        {
            ["originalPresetPath"] = originalPath,
            ["originalPresetSha256"] = Hash(original),
            ["originalMeituContract"] = originalMeitu,
            ["publicationProblem"] = PublicationProblem,
        };

        string derivedPath = Path.Combine(runFolder, "meitu-override-preset.json");
        using (FileStream stream = new(derivedPath, FileMode.CreateNew, FileAccess.Write))
        using (StreamWriter writer = new(stream))
        {
            writer.Write(preset.ToJsonString(new() { WriteIndented = true }));
        }

        return configuration with
        {
            Preset = configuration.Preset with
            {
                Path = derivedPath,
                ExpectedSha256 = Hash(File.ReadAllBytes(derivedPath)),
            },
        };
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
