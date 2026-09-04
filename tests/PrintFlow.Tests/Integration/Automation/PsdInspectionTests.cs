using System.IO;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Adapters.Photoshop;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Tests.Integration.Ui;

namespace PrintFlow.Tests.Integration.Automation;

public sealed class PsdInspectionTests
{
    [Fact]
    public void Compatible_composite_is_a_positive_file_fact()
    {
        using TempWorkspace workspace = new();
        string file = workspace.CreateSourceFile("supported.psd", PsdInputPreparationTests.RgbCompositePsd());
        PsdCompositeProbe.Inspect(file).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData("absent", FailureCode.PsdCompositeMissing)]
    [InlineData("false", FailureCode.PsdCompositeMissing)]
    [InlineData("truncated", FailureCode.PsdUnreadable)]
    [InlineData("psb", FailureCode.PsdUnreadable)]
    [InlineData("bad-length", FailureCode.PsdUnreadable)]
    public void Missing_or_untrustworthy_composite_evidence_is_refused(string variant, FailureCode code)
    {
        using TempWorkspace workspace = new();
        byte[] bytes = PsdInputPreparationTests.RgbCompositePsd();
        switch (variant)
        {
            case "absent": bytes[39] = 32; break; // resource 1056 instead of 1057
            case "false": bytes[50] = 0; break;
            case "truncated": bytes = bytes[..30]; break;
            case "psb": bytes[5] = 2; break;
            case "bad-length": bytes[30] = 127; break;
        }
        string file = workspace.CreateSourceFile("unsupported.psd", bytes);
        var result = PsdCompositeProbe.Inspect(file);
        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(code);
    }

    [Theory]
    [InlineData("RGB", 8, "Red,COMPONENT;Green,COMPONENT;Blue,COMPONENT", false, false)]
    [InlineData("CMYK", 8, "Cyan,COMPONENT;W1,SPOTCOLOR", true, true)]
    [InlineData("RGB", 16, "Mask,MASKEDAREA", false, false)]
    public void Photoshop_facts_are_decoded_without_inventing_alpha_or_channel_decisions(
        string mode, int depth, string channels, bool spots, bool w1)
    {
        var result = RotPhotoshopPsdNativeBridge.Decode($"PF-PSD-1\n0\nunsupported\n4\n3\n{mode}\n{depth}\n\n{channels}\n20.0\nsource-active");
        result.IsSuccess.ShouldBeTrue();
        var facts = result.Value.Inspection!;
        facts.HasTransparency.ShouldBeNull();
        facts.OriginalMode.ShouldBe(mode);
        facts.BitDepth.ShouldBe(depth);
        facts.HasSpots.ShouldBe(spots);
        facts.HasW1.ShouldBe(w1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Independent_png_validation_checks_canvas_and_actual_transparency(bool transparent)
    {
        using TempWorkspace workspace = new();
        string file = workspace.CreateSourceFile("prepared.png", SyntheticImages.Png(4, 3, alpha: transparent));
        var facts = (await new WicFileInspector().InspectAsync(file, CancellationToken.None)).Value;
        var inspection = new PsdInspection(4, 3, "RGB", 8, true, transparent, [], "20.0");
        ProductionPhotoshopOutputProcessor.ValidatePsdRaster(file, facts, inspection).IsSuccess.ShouldBeTrue();
        ProductionPhotoshopOutputProcessor.ValidatePsdRaster(file, facts, inspection with { HasTransparency = !transparent }).IsFailure.ShouldBeTrue();
        ProductionPhotoshopOutputProcessor.ValidatePsdRaster(file, facts, inspection with { PixelWidth = 5 }).IsFailure.ShouldBeTrue();
        ProductionPhotoshopOutputProcessor.ValidatePsdRaster(file, facts, inspection with { BitDepth = 16 }).IsFailure.ShouldBeTrue();
        ProductionPhotoshopOutputProcessor.ValidatePsdRaster(file, facts, inspection with { Channels = [new("W1", "SPOTCOLOR")] }).IsFailure.ShouldBeTrue();
    }
}
