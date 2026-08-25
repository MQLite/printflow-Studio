using System.IO;
using PrintFlow.Domain.Files;
using PrintFlow.Infrastructure.Adapters.Fake;
using PrintFlow.Infrastructure.Adapters.Meitu;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Ports;
using FileWorkspace = PrintFlow.Infrastructure.Workspace.FileWorkspace;

namespace PrintFlow.Tests.Integration.Automation;

public sealed class FakeMeituBackgroundRemovalTests : IDisposable
{
    private readonly TempWorkspace _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Fake_cutout_is_a_separate_deterministic_transparent_PNG()
    {
        IWorkspace workspace = new FileWorkspace(_temp.Root);
        WorkspaceFileRef input = WorkspaceFileRef.Create(
            "Sessions/S/Working/A_1/Name.png", WorkspaceArea.Working);
        WorkspaceFileRef output = WorkspaceFileRef.Create(
            "Sessions/S/Working/A_1/Name_CUTOUT.png", WorkspaceArea.Working);
        string inputPath = workspace.ResolveAbsolute(input);
        string outputPath = workspace.ResolveAbsolute(output);
        Directory.CreateDirectory(Path.GetDirectoryName(inputPath)!);
        byte[] source = SyntheticImages.OpaqueRgbPng(
            4, 3, (x, y) => ((byte)(20 + x), (byte)(40 + y), (byte)90));
        await File.WriteAllBytesAsync(inputPath, source);

        FakeMeituProcessor fake = new(workspace);
        var result = await fake.ProcessAsync(
            new MeituRequest(
                input,
                MeituOperation.RemoveBackground,
                BackgroundRemovalDecision.Unspecified,
                WorkspaceDirRef.Create("Sessions/S/Working/A_1"),
                output),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.TechnicalDetail : string.Empty);
        result.Value.ProducedFile.ShouldBe(output);
        (await File.ReadAllBytesAsync(inputPath)).ShouldBe(source);

        FileFacts facts = (await new WicFileInspector().InspectAsync(
            outputPath, CancellationToken.None)).Value;
        facts.Format.ShouldBe(ImageFormat.Png);
        facts.PixelWidth.ShouldBe(4);
        facts.PixelHeight.ShouldBe(3);

        MeituTransparencyFacts alpha = (await new WicMeituTransparencyInspector().InspectAsync(
            outputPath, CancellationToken.None)).Value;
        alpha.HasTransparentPixels.ShouldBeTrue();
        alpha.HasVisiblePixels.ShouldBeTrue();
    }
}
