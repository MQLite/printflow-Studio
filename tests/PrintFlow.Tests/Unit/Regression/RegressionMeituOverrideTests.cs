using System.IO;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using PrintFlow.Infrastructure.Configuration;
using PrintFlow.Tests.Regression;

namespace PrintFlow.Tests.Unit.Regression;

public sealed class RegressionMeituOverrideTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pf-meitu-override-" + Guid.NewGuid());

    public RegressionMeituOverrideTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Selection_pins_actual_bytes_and_preserves_original_preset_and_other_requirements()
    {
        PrintFlowConfiguration configuration = Prepare();
        byte[] before = File.ReadAllBytes(configuration.Preset.Path);
        PrintFlowConfiguration changed = RegressionMeituOverride.Apply(configuration, Executable, _root);
        File.ReadAllBytes(configuration.Preset.Path).ShouldBe(before);
        changed.Workspace.ShouldBe(configuration.Workspace);
        changed.Preset.ExpectedSha256.ShouldBe(Hash(File.ReadAllBytes(changed.Preset.Path)));
        JsonObject derived = JsonNode.Parse(File.ReadAllText(changed.Preset.Path))!.AsObject();
        derived["meituContract"]!["executablePath"]!.GetValue<string>().ShouldBe(Executable);
        derived["meituContract"]!["executableSha256"]!.GetValue<string>()
            .ShouldBe(Hash(File.ReadAllBytes(Executable)));
        derived["photoshopContract"]!["sentinel"]!.GetValue<string>().ShouldBe("unchanged");
        derived["status"]!.GetValue<string>().ShouldBe("REGRESSION_EXECUTABLE_OVERRIDE_NOT_REVALIDATED");
        derived["regressionExecutableOverride"]!["publicationProblem"]!.GetValue<string>()
            .ShouldBe(RegressionMeituOverride.PublicationProblem);
        derived["regressionExecutableOverride"]!["originalMeituContract"]!["acceptedVersion"]!
            .GetValue<string>().ShouldBe("old");
    }

    [Fact]
    public void Tampered_original_preset_is_refused_before_derivation()
    {
        PrintFlowConfiguration configuration = Prepare();
        File.AppendAllText(configuration.Preset.Path, " ");
        Should.Throw<InvalidDataException>(() => RegressionMeituOverride.Apply(configuration, Executable, _root));
        File.Exists(Path.Combine(_root, "meitu-override-preset.json")).ShouldBeFalse();
    }

    [Fact]
    public void Existing_run_snapshot_is_never_overwritten()
    {
        PrintFlowConfiguration configuration = Prepare();
        string snapshot = Path.Combine(_root, "meitu-override-preset.json");
        File.WriteAllText(snapshot, "existing evidence");
        Should.Throw<IOException>(() => RegressionMeituOverride.Apply(configuration, Executable, _root));
        File.ReadAllText(snapshot).ShouldBe("existing evidence");
    }

    private string Executable => Path.Combine(_root, "XiuXiu.exe");

    private PrintFlowConfiguration Prepare()
    {
        File.Copy(typeof(RegressionMeituOverrideTests).Assembly.Location, Executable);
        string path = Path.Combine(_root, "original.json");
        File.WriteAllText(path, """
            {"meituContract":{"executablePath":"old.exe","executableSha256":"old","acceptedVersion":"old"},
             "photoshopContract":{"sentinel":"unchanged"},"status":"ACCEPTED_IMMUTABLE"}
            """);
        return new(new(_root), new("db"), new("test", "1.0.0", path, Hash(File.ReadAllBytes(path))),
            new("Production"), new(30));
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
