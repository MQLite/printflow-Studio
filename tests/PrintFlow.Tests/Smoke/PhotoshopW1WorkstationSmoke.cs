using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Adapters.Photoshop;
using PrintFlow.Infrastructure.Configuration;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Tests.Fixtures;

namespace PrintFlow.Tests.Smoke;

/// <summary>The opt-in B1B live matrix on three fresh synthetic prepared documents.</summary>
public sealed class PhotoshopW1WorkstationSmoke
{
    private const string EnableVariable = "PRINTFLOW_PHOTOSHOP_W1_SMOKE";
    private const string BranchVariable = "PRINTFLOW_PHOTOSHOP_W1_SMOKE_BRANCH";
    private const string CloseVariable = "PRINTFLOW_PHOTOSHOP_W1_SMOKE_CLOSE";

    [Fact]
    public async Task Execute_all_three_exact_W1_Actions_once_and_validate_factual_results()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
        {
            return;
        }

        await using WorkstationAutomationLeaseScope automationLease =
            await WorkstationAutomationLeaseScope.AcquireDefaultAsync();

        string root = Path.Combine(
            Path.GetTempPath(), "PrintFlowPhotoshopW1Smoke", Guid.NewGuid().ToString("N"));
        string evidence = Path.Combine(root, "Evidence");
        FileWorkspace workspace = new(root);
        PrintFlowConfiguration configuration = PrintFlowConfiguration.LoadFromFile(
            RepositoryFile("appsettings.json"));
        string acceptedManifest = Path.Combine(configuration.Workspace.Root, configuration.Preset.Path);
        string candidateManifest = CreateCandidateManifest(root, acceptedManifest);
        Sha256 candidateHash = Hash(candidateManifest);
        IPhotoshopW1Automation automation = PhotoshopAutomationComposition.CreateW1Automation(
            candidateManifest, candidateHash, workspace, evidence, TimeProvider.System);

        Console.WriteLine($"controlled workspace : {root}");
        Console.WriteLine("candidate preset     : temporary discovery-only copy; not accepted evidence");
        bool close = Environment.GetEnvironmentVariable(CloseVariable) == "1";
        WhiteUnderbaseBranch[] branches = SelectedBranches();
        Console.WriteLine($"branches             : {string.Join(", ", branches)}");
        Console.WriteLine($"cleanup policy       : {(close ? "close each exact synthetic document with signed discard" : "retain synthetic documents")}");

        foreach (WhiteUnderbaseBranch branch in branches)
        {
            string token = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
            WorkspaceFileRef managed = WorkspaceFileRef.Create(
                $"Sessions/S_B1B/Working/{token}/PF_B1B_{branch}_{token}.png",
                WorkspaceArea.Working);
            string absolute = workspace.ResolveAbsolute(managed);
            Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
            bool opensAsBackgroundLayer = branch == WhiteUnderbaseBranch.W1_1px;
            File.WriteAllBytes(absolute, SyntheticImages.Png(
                1200, 800, dpi: 240, alpha: !opensAsBackgroundLayer));
            Sha256 backingBefore = Hash(absolute);
            FitWithinBoundsPreparation preparation = new(PrintPreparationPlan.For(
                new RevisionId(Guid.NewGuid()),
                backingBefore,
                sourcePixelWidth: 1200,
                sourcePixelHeight: 800,
                PrintDimensions.FromMillimetres(50.8, 100, SizePreset.Custom)));

            OperationResult<PhotoshopOpenedDocument> opened = await automation
                .OpenManagedWorkingFileAsync(managed, CancellationToken.None);
            opened.IsSuccess.ShouldBeTrue(opened.IsFailure ? opened.Failure.ToString() : string.Empty);

            OperationResult<PhotoshopPreparedDocument> prepared = await automation
                .PrepareDocumentAsync(opened.Value, preparation, CancellationToken.None);
            prepared.IsSuccess.ShouldBeTrue(prepared.IsFailure ? prepared.Failure.ToString() : string.Empty);

            OperationResult<PhotoshopW1PreparedDocument> executed = await automation.ExecuteW1Async(
                opened.Value, prepared.Value, branch, CancellationToken.None);
            if (executed.IsFailure)
            {
                Console.WriteLine($"W1 failure           : {executed.Failure}");
                Console.WriteLine($"failure context      : {string.Join(" | ", executed.Failure.Context.Select(
                    pair => $"{pair.Key}={pair.Value}"))}");
            }
            executed.IsSuccess.ShouldBeTrue(executed.IsFailure ? executed.Failure.ToString() : string.Empty);
            PhotoshopW1PreparedDocument result = executed.Value;

            Console.WriteLine(string.Empty);
            Console.WriteLine($"## {branch}");
            Console.WriteLine($"document             : {result.DocumentFullPath}");
            Console.WriteLine($"source layer case     : {(opensAsBackgroundLayer ? "flattened Background" : "alpha layer")}");
            Console.WriteLine($"document count       : {prepared.Value.Actual.DocumentCount} -> " +
                              $"{result.OtherDocumentsMayBeOpen} other-documents-may-be-open");
            Console.WriteLine($"before               : {prepared.Value.Actual.PixelWidth}×" +
                              $"{prepared.Value.Actual.PixelHeight} px, " +
                              $"{prepared.Value.Actual.ResolutionPpi:R} PPI, " +
                              $"{prepared.Value.Actual.ColourMode}, {prepared.Value.Actual.BitDepth}");
            Console.WriteLine($"after                : {result.PixelWidth}×{result.PixelHeight} px, " +
                              $"{result.ResolutionPpi:R} PPI, {result.ColourMode}, {result.BitDepth}");
            Console.WriteLine($"physical mm          : {result.PhysicalWidthMm:R}×{result.PhysicalHeightMm:R}");
            Console.WriteLine($"process channels     : {string.Join(" | ", result.ProcessChannels.Select(
                c => $"{c.Name}/{c.Type}"))}");
            Console.WriteLine($"W1                   : {result.W1.Name}/{result.W1.Type}, " +
                              $"non-empty={result.W1.IsNonEmpty}, " +
                              $"non-white={result.W1.NonWhitePixelCount}, " +
                              $"solidity={result.W1.SolidityPercent?.ToString("R") ?? "unavailable"}, " +
                              $"colour={string.Join(",", result.W1.SpotColourComponents)}");
            Console.WriteLine($"action               : {result.ActionSetName} / {result.ActionName} / once=" +
                              result.ActionInvocationOccurredExactlyOnce);
            Console.WriteLine($"backing SHA          : {backingBefore} -> {Hash(absolute)}");
            Console.WriteLine("saved/output         : false / false");

            Hash(absolute).ShouldBe(backingBefore);
            Directory.EnumerateFiles(Path.GetDirectoryName(absolute)!).ShouldHaveSingleItem();

            if (close)
            {
                OperationResult<PhotoshopTarget> closed = await automation.CloseExactDocumentAsync(
                    opened.Value, managed, CancellationToken.None);
                closed.IsSuccess.ShouldBeTrue(closed.IsFailure ? closed.Failure.ToString() : string.Empty);
            }
        }

        Console.WriteLine(string.Empty);
        Console.WriteLine(close
            ? "CLEANUP COMPLETE: every exact synthetic document was closed with signed discard."
            : $"OPERATOR CLEANUP REQUIRED: {branches.Length} exact synthetic CMYK/W1 document(s) remain open and modified in memory.");
        Console.WriteLine(close
            ? "Photoshop itself was not closed; no synthetic document remains loaded."
            : "Photoshop was not closed and the retained workspace must not be deleted yet.");
    }

    private static WhiteUnderbaseBranch[] SelectedBranches()
    {
        string? selected = Environment.GetEnvironmentVariable(BranchVariable);
        if (string.IsNullOrWhiteSpace(selected))
        {
            return Enum.GetValues<WhiteUnderbaseBranch>();
        }

        return Enum.TryParse(selected, ignoreCase: false, out WhiteUnderbaseBranch branch) &&
               Enum.IsDefined(branch)
            ? [branch]
            : throw new InvalidOperationException(
                $"{BranchVariable} must be one exact {nameof(WhiteUnderbaseBranch)} value.");
    }

    private static string CreateCandidateManifest(string root, string acceptedManifest)
    {
        string runtimePath = Path.Combine(root, "apps", "photoshop-2019", "cmyk-w1-action-runtime.json");
        Directory.CreateDirectory(Path.GetDirectoryName(runtimePath)!);
        JsonObject runtime = new()
        {
            ["schema"] = "printflow.photoshop-cmyk-w1-action-runtime",
            ["schemaVersion"] = 1,
            ["status"] = "DISCOVERY_CANDIDATE_NOT_ACCEPTED",
            ["actionArtifact"] = new JsonObject
            {
                ["path"] = @"D:\PrintFlowStudio\Baseline\workstation-v1\actions\authoring\PrintFlow-DTF-v1.atn",
                ["sha256"] = "A04203EDEA623C0737D911601A3A005033789BD095130F02A5F8C04CBFCD83EE",
            },
            ["runtimeActionContract"] = RuntimeContract(),
        };
        File.WriteAllText(runtimePath, runtime.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        JsonNode manifest = JsonNode.Parse(File.ReadAllText(acceptedManifest))!;
        JsonArray integrity = manifest["sourceManifestIntegrity"]!.AsArray();
        integrity.Add(new JsonObject
        {
            ["path"] = runtimePath,
            ["sha256"] = Hash(runtimePath).ToString(),
        });
        string candidatePath = Path.Combine(root, "candidate-preset.json");
        File.WriteAllText(candidatePath,
            manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return candidatePath;
    }

    private static JsonObject RuntimeContract() => new()
    {
        ["setName"] = "PrintFlow DTF",
        ["actions"] = new JsonArray
        {
            RuntimeAction("W1_0px", ["转换模式", "设置 选区", "建立"]),
            RuntimeAction("W1_1px", ["转换模式", "设置 选区", "收缩", "建立"]),
            RuntimeAction("W1_2px", ["转换模式", "设置 选区", "收缩", "建立"]),
        },
    };

    private static JsonObject RuntimeAction(string name, string[] commands) => new()
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

    private static string RepositoryFile(string fileName)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "PrintFlowStudio.sln")))
        {
            current = current.Parent;
        }
        return current is null
            ? throw new InvalidOperationException("The repository root could not be located.")
            : Path.Combine(current.FullName, fileName);
    }
}
