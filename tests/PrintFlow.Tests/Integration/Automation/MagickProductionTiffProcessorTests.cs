using System.IO;
using System.Security.Cryptography;
using ImageMagick;
using ImageMagick.Drawing;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Adapters.Magick;
using PrintFlow.Infrastructure.Adapters.Photoshop;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Integration.Automation;

public sealed class MagickProductionTiffProcessorTests
{
    private static readonly byte[] PresetBytes = """
        {"photoshopContract":{"colourSettings":{
          "rgbWorkingSpace":"sRGB IEC61966-2.1", "cmykWorkingSpace":"Coated FOGRA39 (ISO 12647-2:2004)",
          "grayWorkingSpace":"Dot Gain 15%", "spotWorkingSpace":"Dot Gain 15%"
        }}}
        """u8.ToArray();

    [Theory]
    [InlineData(WhiteUnderbaseBranch.W1_0px, 25)]
    [InlineData(WhiteUnderbaseBranch.W1_1px, 9)]
    [InlineData(WhiteUnderbaseBranch.W1_2px, 1)]
    public async Task Local_production_tiff_applies_each_selected_white_ink_contraction(
        WhiteUnderbaseBranch branch, long expectedInkPixels)
    {
        using var temp = new TempWorkspace();
        var workspace = new FileWorkspace(temp.Root);
        var (request, output) = Ready(workspace, branch);
        var processor = CreateProcessor(temp, workspace, request.Preset.ManifestSha256);

        var result = await processor.GenerateAsync(request, default);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.TechnicalDetail : null);
        result.Value.ProducedFile.ShouldBe(request.ExpectedOutput);
        byte[] tiff = File.ReadAllBytes(output);
        result.Value.ValidatedImageSha256.ShouldBe(Sha256.FromBytes(SHA256.HashData(tiff)));
        var inspected = new ProductionTiffInspector().Inspect(output).Value;
        inspected.W1NonWhiteSampleCount.ShouldBe(expectedInkPixels);
        inspected.W1IsPhotoshopSpotChannel.ShouldBeTrue();
        inspected.PixelWidth.ShouldBe(7);
        inspected.PixelHeight.ShouldBe(7);
        inspected.XResolutionDpi.ShouldBe(300);
        inspected.PhotoshopLayerCount.ShouldBe(1);
    }

    [Fact]
    public async Task Two_pixel_contraction_preserves_fractional_alpha_at_the_offset_used_by_photoshop()
    {
        using var temp = new TempWorkspace();
        var workspace = new FileWorkspace(temp.Root);
        var (request, output) = Ready(workspace, WhiteUnderbaseBranch.W1_2px, partialAlpha: true);

        var result = await CreateProcessor(temp, workspace, request.Preset.ManifestSha256).GenerateAsync(request, default);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.TechnicalDetail : null);
        var raster = new ProductionTiffInspector().InspectForReview(output).Value.Raster;
        using var tiff = File.OpenRead(output);
        tiff.Position = raster.StripOffsets[0] + (3 * 7 + 3) * 5 + 4;
        tiff.ReadByte().ShouldBe(127);
    }

    [Fact]
    public async Task Local_tiff_refuses_working_input_changed_since_sizing()
    {
        using var temp = new TempWorkspace();
        var workspace = new FileWorkspace(temp.Root);
        var (request, output) = Ready(workspace, WhiteUnderbaseBranch.W1_0px);
        File.AppendAllText(workspace.ResolveAbsolute(request.ApprovedInput), "changed");

        var result = await CreateProcessor(temp, workspace, request.Preset.ManifestSha256).GenerateAsync(request, default);

        result.Failure.Code.ShouldBe(FailureCode.RevisionIntegrityMismatch);
        File.Exists(output).ShouldBeFalse();
    }

    private static (PhotoshopRequest Request, string OutputPath) Ready(FileWorkspace workspace, WhiteUnderbaseBranch branch,
        bool partialAlpha = false)
    {
        const string directory = "Sessions/S1/Working/A1";
        var input = WorkspaceFileRef.Create(directory + "/input.png", WorkspaceArea.Working);
        var output = WorkspaceFileRef.Create(directory + "/output.tif", WorkspaceArea.Working);
        string inputPath = workspace.ResolveAbsolute(input);
        Directory.CreateDirectory(Path.GetDirectoryName(inputPath)!);
        using var source = new MagickImage(MagickColors.Transparent, 7, 7);
        new Drawables().FillColor(MagickColors.Red).Rectangle(1, 1, 5, 5).Draw(source);
        if (partialAlpha)
        {
            using var pixels = source.GetPixels();
            pixels.SetPixel(1, 2, new ushort[] { ushort.MaxValue, 0, 0, 128 * 257 });
        }
        byte[] bytes = source.ToByteArray(MagickFormat.Png);
        File.WriteAllBytes(inputPath, bytes);
        var limit = PrintDimensions.FromMillimetres(200, 100, SizePreset.Custom);
        var sourceHash = Sha256.FromBytes(SHA256.HashData(bytes));
        var geometry = new FitWithinBoundsPreparation(PrintPreparationPlan.For(
            RevisionId.From(Guid.NewGuid()), sourceHash, 7, 7, limit));
        var preset = new ProductionPresetRef("test", "1", Sha256.FromBytes(SHA256.HashData(PresetBytes)));
        return (new PhotoshopRequest(input, limit, geometry, preset, branch, output.FileName,
            WorkspaceDirRef.Create(directory), output), workspace.ResolveAbsolute(output));
    }

    private static MagickProductionTiffProcessor CreateProcessor(TempWorkspace temp, FileWorkspace workspace, Sha256 expectedHash) =>
        new(workspace, temp.CreateSourceFile("preset.json", PresetBytes), expectedHash);
}
