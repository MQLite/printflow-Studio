using System.IO;
using System.Security.Cryptography;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Adapters.Photoshop;
using PrintFlow.Infrastructure.Configuration;
using PrintFlow.Infrastructure.Preset;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Smoke;

/// <summary>The opt-in B1A.3 live matrix against fresh synthetic managed Working documents.</summary>
public sealed class PhotoshopPreparationWorkstationSmoke
{
    private const string EnableVariable = "PRINTFLOW_PHOTOSHOP_PREPARATION_SMOKE";
    private const string CaseVariable = "PRINTFLOW_PHOTOSHOP_PREPARATION_CASE";

    [Fact]
    public async Task Prepare_resolution_shrink_enlarge_and_midpoint_documents_in_memory()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
        {
            return;
        }

        string root = Path.Combine(
            Path.GetTempPath(), "PrintFlowPhotoshopPreparationSmoke", Guid.NewGuid().ToString("N"));
        string evidence = Path.Combine(root, "Evidence");
        FileWorkspace workspace = new(root);
        PrintFlowConfiguration configuration = PrintFlowConfiguration.LoadFromFile(RepositoryFile("appsettings.json"));
        string manifest = Path.Combine(configuration.Workspace.Root, configuration.Preset.Path);
        Sha256 expectedPreset = Sha256.Parse(configuration.Preset.ExpectedSha256);
        WorkstationPresetProvider presetProvider = new(
            manifest, configuration.Preset.Id, configuration.Preset.Version, expectedPreset);
        PresetPrintRecommendation a5 = presetProvider.GetPrintSizeRecommendations().Value
            .For(SizePreset.A5).ShouldNotBeNull();
        a5.Kind.ShouldBe(PresetRecommendationKind.MaximumShortEdge);
        a5.MaxShortEdgeMm.ShouldBe(135m);
        IPhotoshopPreparationAutomation automation = PhotoshopAutomationComposition
            .CreatePreparationAutomation(manifest, expectedPreset, workspace, evidence, TimeProvider.System);

        Console.WriteLine($"controlled workspace : {root}");
        Console.WriteLine("cleanup policy       : retain; prepared documents have unsaved in-memory changes");

        List<LiveCase> cases =
        [
            new("ResolutionOnly", 1200, 800,
                (revision, hash) => new FitWithinBoundsPreparation(PrintPreparationPlan.For(
                    revision, hash, 1200, 800,
                    PrintDimensions.FromMillimetres(200, 200, SizePreset.Custom)))),
            new("ProportionalShrink-Width", 1200, 800,
                (revision, hash) => new FitWithinBoundsPreparation(PrintPreparationPlan.For(
                    revision, hash, 1200, 800,
                    PrintDimensions.FromMillimetres(50.8, 100, SizePreset.Custom)))),
            new("AuthorisedEnlarge-Height", 1200, 800,
                (revision, hash) => Target(revision, hash, 1200, 800,
                    TargetEdge.Height, 135.4666666667m)),
            new("Midpoint-Width-84.709mm", 2000, 1000,
                (revision, hash) => Target(revision, hash, 2000, 1000,
                    TargetEdge.Width, 84.709m)),
            new("A5-MaximumShortEdge-Landscape", 2400, 1800,
                (revision, hash) => new FitWithinBoundsPreparation(
                    PrintPreparationPlan.For(revision, hash, 2400, 1800, a5),
                    FlexibleSizeSelection.PresetFit(a5))),
        ];

        string? selectedCase = Environment.GetEnvironmentVariable(CaseVariable);
        if (!string.IsNullOrWhiteSpace(selectedCase))
        {
            cases = [.. cases.Where(c => string.Equals(c.Name, selectedCase, StringComparison.Ordinal))];
            cases.ShouldHaveSingleItem($"{CaseVariable} must name one exact controlled live case.");
        }

        foreach (LiveCase live in cases)
        {
            string token = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
            WorkspaceFileRef managed = WorkspaceFileRef.Create(
                $"Sessions/S_B1A3/Working/{token}/PF_B1A3_{live.Name}_{token}.png",
                WorkspaceArea.Working);
            string absolute = workspace.ResolveAbsolute(managed);
            Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
            File.WriteAllBytes(absolute, SyntheticImages.Png(
                live.SourceWidth, live.SourceHeight, dpi: 240, alpha: true));
            Sha256 beforeHash = Hash(absolute);
            PhotoshopPreparation preparation = live.Preparation(new RevisionId(Guid.NewGuid()), beforeHash);

            Console.WriteLine(string.Empty);
            Console.WriteLine($"## {live.Name}");
            Console.WriteLine($"source               : {live.SourceWidth}×{live.SourceHeight} px");
            Console.WriteLine($"projected            : {preparation.ProjectedPixelWidth}×{preparation.ProjectedPixelHeight} px");
            Console.WriteLine($"operation            : {preparation.ResizePolicy}, {preparation.PhotoshopEdge}, " +
                              $"{preparation.PhotoshopEdgeValueMm?.ToString("R") ?? "no physical edge"}");

            OperationResult<PhotoshopOpenedDocument> opened =
                await automation.OpenManagedWorkingFileAsync(managed, CancellationToken.None);
            opened.IsSuccess.ShouldBeTrue(opened.IsFailure ? opened.Failure.ToString() : string.Empty);

            OperationResult<PhotoshopPreparedDocument> prepared = await automation.PrepareDocumentAsync(
                opened.Value, preparation, CancellationToken.None);
            prepared.IsSuccess.ShouldBeTrue(prepared.IsFailure ? prepared.Failure.ToString() : string.Empty);

            PhotoshopPreparedDocument actual = prepared.Value;
            Console.WriteLine($"document count       : {actual.Before.DocumentCount}");
            Console.WriteLine($"other documents      : {actual.OtherDocumentsMayBeOpen}");
            Console.WriteLine($"before               : {actual.Before.PixelWidth}×{actual.Before.PixelHeight} px, " +
                              $"{actual.Before.ResolutionPpi:R} PPI, {actual.Before.ColourMode}, {actual.Before.BitDepth}");
            Console.WriteLine($"actual               : {actual.Actual.PixelWidth}×{actual.Actual.PixelHeight} px, " +
                              $"{actual.Actual.ResolutionPpi:R} PPI, " +
                              $"{actual.Actual.PhysicalWidthMm:R}×{actual.Actual.PhysicalHeightMm:R} mm");
            Console.WriteLine($"channels             : {string.Join(" | ", actual.Actual.Channels.Select(c => $"{c.Name}/{c.Type}"))}");
            Console.WriteLine($"W1                   : {actual.Actual.W1Exists}");
            Console.WriteLine($"backing SHA          : {beforeHash} -> {Hash(absolute)}");
            Console.WriteLine("saved/output/W1      : false / false / false");

            Hash(absolute).ShouldBe(beforeHash);
            Directory.EnumerateFiles(Path.GetDirectoryName(absolute)!).ShouldHaveSingleItem();
        }

        Console.WriteLine(string.Empty);
        Console.WriteLine($"OPERATOR CLEANUP REQUIRED: {cases.Count} exact synthetic document(s) remain open and modified in memory.");
        Console.WriteLine("Photoshop was not closed, no document was discarded, and the retained workspace must not be deleted yet.");
    }

    private static PhotoshopPreparation Target(
        RevisionId revision,
        Sha256 hash,
        int sourceWidth,
        int sourceHeight,
        TargetEdge edge,
        decimal millimetres)
    {
        TargetEdgePrintPreparationPlan plan = TargetEdgePrintPreparationPlan.For(
            revision,
            hash,
            sourceWidth,
            sourceHeight,
            FlexibleSizeSelection.CustomTarget(edge, millimetres));
        return new TargetEdgePreparation(
            plan,
            plan.RequiresEnlargementAuthority ? EnlargementAuthority.For(plan) : null);
    }

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

    private sealed record LiveCase(
        string Name,
        int SourceWidth,
        int SourceHeight,
        Func<RevisionId, Sha256, PhotoshopPreparation> Preparation);
}
