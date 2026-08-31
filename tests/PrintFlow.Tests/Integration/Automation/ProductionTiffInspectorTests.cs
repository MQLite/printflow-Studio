using System.IO;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Adapters.Photoshop;
using PrintFlow.Tests.Fixtures;
using Shouldly;

namespace PrintFlow.Tests.Integration.Automation;

public sealed class ProductionTiffInspectorTests : IDisposable
{
    private const string Baseline =
        @"D:\PrintFlowStudio\Baseline\workstation-v1\actions\authoring\FIX-CUSTOMER-DESIGN-001_W1-1PX.tif";
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "PrintFlow-ProductionTiffInspector-" + Guid.NewGuid().ToString("N"));
    private readonly ProductionTiffInspector _inspector = new();

    [Fact]
    public void Accepted_baseline_is_independently_proven_as_cmyk_plus_nonempty_W1()
    {
        File.Exists(Baseline).ShouldBeTrue();

        OperationResult<ProductionTiffFacts> result = _inspector.Inspect(Baseline);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : string.Empty);
        result.Value.PixelWidth.ShouldBe(3307);
        result.Value.PixelHeight.ShouldBe(4474);
        result.Value.ByteOrder.ShouldBe("IBM PC / little-endian");
        result.Value.BitsPerSample.ShouldBe([8, 8, 8, 8, 8]);
        result.Value.SamplesPerPixel.ShouldBe((ushort)5);
        result.Value.Compression.ShouldBe((ushort)1);
        result.Value.PhotometricInterpretation.ShouldBe((ushort)5);
        result.Value.PlanarConfiguration.ShouldBe((ushort)1);
        result.Value.ExtraSamples.ShouldBe([0]);
        result.Value.ExtraChannelNames.ShouldBe(["W1"]);
        result.Value.W1IsPhotoshopSpotChannel.ShouldBeTrue();
        result.Value.W1NonWhiteSampleCount.ShouldBe(8_228_624);
        result.Value.HasAlphaOrTransparencySample.ShouldBeFalse();
        result.Value.HasImagePyramid.ShouldBeFalse();
        result.Value.PhotoshopLayerCount.ShouldBe(1);
        result.Value.AllPhotoshopLayerChannelsUseRle.ShouldBeTrue();
    }

    [Fact]
    public void Minimal_accepted_fixture_proves_the_same_closed_contract()
    {
        string path = ProductionTiffFixture.Write(_directory);

        OperationResult<ProductionTiffFacts> result = _inspector.Inspect(path);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : string.Empty);
        result.Value.PixelWidth.ShouldBe(2);
        result.Value.PixelHeight.ShouldBe(2);
        result.Value.W1NonWhiteSampleCount.ShouldBe(1);
    }

    public static TheoryData<ProductionTiffFixtureOptions, string> InvalidContracts => new()
    {
        { new(Compression: 5), "compression" },
        { new(LittleEndian: false), "byte order" },
        { new(SamplesPerPixel: 4), "five 8-bit" },
        { new(PlanarConfiguration: 2), "interleaved" },
        { new(ExtraSample: 2), "alpha" },
        { new(Dpi: 72), "300" },
        { new(ChannelName: "White"), "W1" },
        { new(ChannelKind: 1), "spot" },
        { new(FifthSampleNonEmpty: false), "empty" },
        { new(IncludePyramidIfd: true), "pyramid" },
        { new(LayerCompression: 2), "RLE" },
    };

    [Theory]
    [MemberData(nameof(InvalidContracts))]
    public void Any_nonproduction_fact_is_rejected(
        ProductionTiffFixtureOptions options, string expectedDetail)
    {
        string path = ProductionTiffFixture.Write(_directory, options);

        OperationResult<ProductionTiffFacts> result = _inspector.Inspect(path);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.OutputValidationFailed);
        result.Failure.TechnicalDetail.ShouldContain(expectedDetail, Case.Insensitive);
    }

    [Fact]
    public void Truncated_or_non_tiff_bytes_never_become_a_candidate()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "bad.tif");
        File.WriteAllBytes(path, [1, 2, 3]);

        OperationResult<ProductionTiffFacts> result = _inspector.Inspect(path);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.OutputValidationFailed);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }
}
