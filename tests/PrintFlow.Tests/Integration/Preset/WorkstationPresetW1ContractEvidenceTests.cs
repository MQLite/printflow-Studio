using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Adapters.Photoshop;

namespace PrintFlow.Tests.Integration.Preset;

/// <summary>Focused verified-preset loading for the closed B1B runtime contract.</summary>
public sealed class WorkstationPresetW1ContractEvidenceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "PrintFlowW1PresetTests", Guid.NewGuid().ToString("N"));

    public WorkstationPresetW1ContractEvidenceTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Exact_verified_runtime_evidence_loads_the_closed_three_branch_contract()
    {
        (string manifest, Sha256 hash) = WritePreset(RuntimeActions());

        OperationResult<PhotoshopBaseline> result =
            new PresetPhotoshopBaselineProvider(manifest, hash).GetVerifiedBaseline();

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : string.Empty);
        PhotoshopW1ActionContract contract = result.Value.W1Action.ShouldNotBeNull();
        contract.SetName.ShouldBe("PrintFlow DTF");
        contract.Branches.Select(b => b.Branch).ShouldBe(
            Enum.GetValues<WhiteUnderbaseBranch>(), ignoreOrder: true);
        contract.Branches.Single(b => b.Branch == WhiteUnderbaseBranch.W1_1px)
            .RuntimeCommands.ShouldBe(["转换模式", "设置 选区", "收缩", "建立"]);
    }

    [Fact]
    public void Evidence_hash_mismatch_fails_the_verified_baseline()
    {
        (string manifest, Sha256 hash) = WritePreset(RuntimeActions(), corruptEvidenceAfterHash: true);

        OperationResult<PhotoshopBaseline> result =
            new PresetPhotoshopBaselineProvider(manifest, hash).GetVerifiedBaseline();

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.PresetHashMismatch);
    }

    [Fact]
    public void Duplicate_or_missing_branch_in_verified_evidence_fails_closed()
    {
        JsonArray actions = RuntimeActions();
        actions[2]!["branch"] = "W1_1px";
        actions[2]!["actionName"] = "W1_1px";
        (string manifest, Sha256 hash) = WritePreset(actions);

        OperationResult<PhotoshopBaseline> result =
            new PresetPhotoshopBaselineProvider(manifest, hash).GetVerifiedBaseline();

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.EnvironmentNotVerified);
        result.Failure.TechnicalDetail.ShouldContain("missing or duplicated");
    }

    private (string Manifest, Sha256 Hash) WritePreset(
        JsonArray actions, bool corruptEvidenceAfterHash = false)
    {
        string evidence = Path.Combine(_root, "apps", "photoshop-2019", "cmyk-w1-action-runtime.json");
        Directory.CreateDirectory(Path.GetDirectoryName(evidence)!);
        JsonObject runtime = new()
        {
            ["actionArtifact"] = new JsonObject
            {
                ["path"] = @"D:\PrintFlowStudio\Baseline\workstation-v1\actions\authoring\PrintFlow-DTF-v1.atn",
                ["sha256"] = "A04203EDEA623C0737D911601A3A005033789BD095130F02A5F8C04CBFCD83EE",
            },
            ["runtimeActionContract"] = new JsonObject
            {
                ["setName"] = "PrintFlow DTF",
                ["actions"] = actions,
            },
        };
        File.WriteAllText(evidence, runtime.ToJsonString());
        Sha256 evidenceHash = Hash(evidence);

        JsonObject manifest = new()
        {
            ["photoshopContract"] = new JsonObject
            {
                ["executablePath"] = @"D:\Adobe Photoshop CC 2019\Photoshop.exe",
                ["executableSha256"] = new string('1', 64),
                ["productVersion"] = "20.0",
                ["fileVersion"] = "20.0.10",
                ["uiLanguage"] = "Simplified Chinese",
                ["uiContract"] = new JsonObject
                {
                    ["mainWindowClassName"] = "Photoshop",
                    ["noDocumentWindowTitle"] = "Adobe Photoshop CC 2019",
                },
            },
            ["photoshopActionContract"] = new JsonObject
            {
                ["setName"] = "PrintFlow DTF",
                ["artifactPath"] = @"D:\PrintFlowStudio\Baseline\workstation-v1\actions\authoring\PrintFlow-DTF-v1.atn",
                ["artifactSha256"] = "A04203EDEA623C0737D911601A3A005033789BD095130F02A5F8C04CBFCD83EE",
                ["actions"] = new JsonObject
                {
                    ["W1_0px"] = new JsonArray(),
                    ["W1_1px"] = new JsonArray(),
                    ["W1_2px"] = new JsonArray(),
                },
            },
            ["sourceManifestIntegrity"] = new JsonArray
            {
                new JsonObject
                {
                    ["path"] = evidence,
                    ["sha256"] = evidenceHash.ToString(),
                },
            },
        };
        string manifestPath = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(manifestPath, manifest.ToJsonString());
        Sha256 manifestHash = Hash(manifestPath);
        if (corruptEvidenceAfterHash)
        {
            File.WriteAllText(evidence, runtime.ToJsonString() + " ");
        }
        return (manifestPath, manifestHash);
    }

    private static JsonArray RuntimeActions() => new()
    {
        Action("W1_0px", ["转换模式", "设置 选区", "建立"]),
        Action("W1_1px", ["转换模式", "设置 选区", "收缩", "建立"]),
        Action("W1_2px", ["转换模式", "设置 选区", "收缩", "建立"]),
    };

    private static JsonObject Action(string name, string[] commands) => new()
    {
        ["branch"] = name,
        ["actionName"] = name,
        ["runtimeCommands"] = new JsonArray(commands.Select(value => JsonValue.Create(value)).ToArray()),
    };

    private static Sha256 Hash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Sha256.FromBytes(SHA256.HashData(stream));
    }
}
