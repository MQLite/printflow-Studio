using System.IO;
using System.Security.Cryptography;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Adapters.Photoshop;
using PrintFlow.Infrastructure.Configuration;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Tests.Fixtures;

namespace PrintFlow.Tests.Smoke;

/// <summary>Opt-in C1 chain on one fresh synthetic W1_1px document.</summary>
public sealed class PhotoshopTiffWorkstationSmoke
{
    private const string EnableVariable = "PRINTFLOW_PHOTOSHOP_TIFF_SMOKE";

    [Fact]
    public async Task Save_one_exact_production_TIFF_copy_and_validate_its_disk_facts()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1") return;

        await using WorkstationAutomationLeaseScope automationLease =
            await WorkstationAutomationLeaseScope.AcquireDefaultAsync();

        PrintFlowConfiguration configuration = PrintFlowConfiguration.LoadFromFile(
            RepositoryFile("appsettings.json"));
        string token = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss") + "-" +
                       Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        string root = Path.Combine(configuration.Workspace.Root, "QA", "Epic11400C1", token);
        string evidence = Path.Combine(root, "Evidence");
        FileWorkspace workspace = new(root);
        string manifest = Path.Combine(configuration.Workspace.Root, configuration.Preset.Path);
        Sha256 presetSha = Sha256.Parse(configuration.Preset.ExpectedSha256);
        IPhotoshopTiffAutomation automation = PhotoshopAutomationComposition.CreateTiffAutomation(
            manifest, presetSha, workspace, evidence, TimeProvider.System);

        string outputStem = "PF_C1_W1_1PX_" + token.Replace("-", string.Empty, StringComparison.Ordinal);
        WorkspaceFileRef managed = WorkspaceFileRef.Create(
            $"Sessions/S_C1/Working/A_{token}/{outputStem}_WORKING.png", WorkspaceArea.Working);
        string sourcePath = workspace.ResolveAbsolute(managed);
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        // Use a deliberately compressible synthetic field. CC 2019 may store individual layer
        // channels raw when PackBits would expand high-frequency data even though the fixed save
        // option is LayerCompression.RLE; the production contract requires the resulting layer
        // payload, not merely the option object, to match the accepted baseline's RLE records.
        File.WriteAllBytes(sourcePath, SyntheticImages.PngWithAlpha(
            1200, 800, static (_, _) => byte.MaxValue, dpi: 240));
        Sha256 backingBefore = Hash(sourcePath);
        PrintDimensions dimensions = PrintDimensions.FromMillimetres(50.8, 100, SizePreset.Custom);
        FitWithinBoundsPreparation preparation = new(PrintPreparationPlan.For(
            new RevisionId(Guid.NewGuid()), backingBefore, 1200, 800, dimensions));
        OperationResult<string> rendered = OutputFileNaming.BuildProposedFileName(
            NamingArtifactKind.ProductionTiff,
            OutputName.Sanitise(outputStem),
            NamingPatternSet.DesignDefault,
            dimensions.WidthMm);
        rendered.IsSuccess.ShouldBeTrue(rendered.IsFailure ? rendered.Failure.ToString() : string.Empty);
        string relativeDirectory = managed.RelativePath[..^managed.FileName.Length];
        WorkspaceFileRef output = WorkspaceFileRef.Create(
            relativeDirectory + rendered.Value, WorkspaceArea.Working);

        Console.WriteLine($"controlled workspace : {root}");
        Console.WriteLine($"preset               : v{configuration.Preset.Version} / {presetSha}");
        Console.WriteLine($"managed source       : {managed.RelativePath}");
        Console.WriteLine($"rendered output      : {output.RelativePath}");
        Console.WriteLine("cleanup policy       : retain TIFF and document; no automated discard route exists");

        OperationResult<PhotoshopOpenedDocument> opened = await automation
            .OpenManagedWorkingFileAsync(managed, CancellationToken.None);
        opened.IsSuccess.ShouldBeTrue(opened.IsFailure ? opened.Failure.ToString() : string.Empty);

        OperationResult<PhotoshopPreparedDocument> prepared = await automation
            .PrepareDocumentAsync(opened.Value, preparation, CancellationToken.None);
        prepared.IsSuccess.ShouldBeTrue(prepared.IsFailure ? prepared.Failure.ToString() : string.Empty);

        OperationResult<PhotoshopW1PreparedDocument> w1 = await automation.ExecuteW1Async(
            opened.Value, prepared.Value, WhiteUnderbaseBranch.W1_1px, CancellationToken.None);
        w1.IsSuccess.ShouldBeTrue(w1.IsFailure ? w1.Failure.ToString() : string.Empty);

        OperationResult<PhotoshopValidatedTiffCandidate> saved = await automation.SaveProductionTiffAsync(
            opened.Value, w1.Value, output, CancellationToken.None);
        if (saved.IsFailure)
        {
            Console.WriteLine($"TIFF failure         : {saved.Failure}");
            Console.WriteLine($"failure context      : {string.Join(" | ", saved.Failure.Context.Select(
                pair => $"{pair.Key}={pair.Value}"))}");
        }
        saved.IsSuccess.ShouldBeTrue(saved.IsFailure ? saved.Failure.ToString() : string.Empty);
        PhotoshopValidatedTiffCandidate candidate = saved.Value;

        Console.WriteLine($"active before/after  : {candidate.DocumentFullPathBefore} | {candidate.DocumentFullPathAfter}");
        Console.WriteLine($"TIFF bytes/SHA       : {candidate.ByteLength} / {candidate.Sha256}");
        Console.WriteLine($"pixels/DPI           : {candidate.Facts.PixelWidth}×{candidate.Facts.PixelHeight} / " +
                          $"{candidate.Facts.XResolutionDpi:R}×{candidate.Facts.YResolutionDpi:R}");
        Console.WriteLine($"byte/compression     : {candidate.Facts.ByteOrder} / {candidate.Facts.Compression}");
        Console.WriteLine($"samples/bits/order   : {candidate.Facts.SamplesPerPixel} / " +
                          $"{string.Join(',', candidate.Facts.BitsPerSample)} / " +
                          $"planar={candidate.Facts.PlanarConfiguration}");
        Console.WriteLine($"CMYK/W1              : photometric={candidate.Facts.PhotometricInterpretation}, " +
                          $"names={string.Join(',', candidate.Facts.ExtraChannelNames)}, " +
                          $"spot={candidate.Facts.W1IsPhotoshopSpotChannel}, " +
                          $"nonwhite={candidate.Facts.W1NonWhiteSampleCount}, " +
                          $"alpha={candidate.Facts.HasAlphaOrTransparencySample}");
        Console.WriteLine($"layers/pyramid       : layers={candidate.Facts.PhotoshopLayerCount}, " +
                          $"allRle={candidate.Facts.AllPhotoshopLayerChannelsUseRle}, " +
                          $"pyramid={candidate.Facts.HasImagePyramid}");
        Console.WriteLine($"settle               : observations={candidate.SettlingObservations.Length}, " +
                          $"elapsed={candidate.Elapsed.TotalSeconds:R}s");
        Console.WriteLine($"backing SHA          : {backingBefore} -> {Hash(sourcePath)}");
        Console.WriteLine($"save settings        : {candidate.SaveSettings}");
        Console.WriteLine("workflow objects     : AdapterOutput=false / Revision=false / ReviewRequired=false");

        Hash(sourcePath).ShouldBe(backingBefore);
        Directory.EnumerateFiles(Path.GetDirectoryName(sourcePath)!).Order().ShouldBe(
            new[] { sourcePath, workspace.ResolveAbsolute(output) }.Order());
        Console.WriteLine("OPERATOR CLEANUP REQUIRED: close the one synthetic document with Don't Save when evidence is recorded.");
        Console.WriteLine("Photoshop was not closed; the validated TIFF remains outside Git in the QA Working directory.");
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
}
