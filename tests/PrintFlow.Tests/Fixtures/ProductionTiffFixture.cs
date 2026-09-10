using System.IO;
using PrintFlow.Infrastructure.Adapters.Fake;

namespace PrintFlow.Tests.Fixtures;

/// <summary>
/// The test-facing shape of one deterministic production TIFF.
/// </summary>
/// <remarks>
/// <see cref="ProductionTiffFixtureOptions.PixelWidth"/> and
/// <see cref="ProductionTiffFixtureOptions.PixelHeight"/> keep their 2 x 2 default because that
/// is what every pre-existing caller wrote against. A review-payload test needs more: a W1 pattern
/// with distinguishable 0%, partial and 100% regions cannot exist on four pixels, and a
/// downsampling assertion needs a canvas larger than the display edge.
/// <para>
/// It stays a separate record from <c>FakeProductionTiffOptions</c> only because it is the type
/// dozens of <c>TheoryData</c> tables are already written in. The two carry the same fields and
/// this one is mapped onto that one before any byte is written, so there is no second contract
/// here — only a second name for the same one (SCRUM-11097).
/// </para>
/// </remarks>
public sealed record ProductionTiffFixtureOptions(
    bool LittleEndian = true,
    ushort Compression = 1,
    ushort SamplesPerPixel = 5,
    ushort PlanarConfiguration = 1,
    ushort ExtraSample = 0,
    uint Dpi = 300,
    string ChannelName = "W1",
    byte ChannelKind = 2,
    bool FifthSampleNonEmpty = true,
    bool IncludePyramidIfd = false,
    ushort LayerCompression = 1,
    byte[]? CmykSamples = null,
    int PixelWidth = 2,
    int PixelHeight = 2,
    byte[]? W1VerticalBands = null,
    byte? FifthSampleEverywhere = null,
    ushort PhotometricInterpretation = 5);

/// <summary>
/// Writes deterministic production TIFFs for tests, through the one shipped encoder
/// (SCRUM-11097).
/// </summary>
/// <remarks>
/// The encoder itself moved to <see cref="FakeProductionTiff"/> in the Fake adapter, because the
/// Fake Photoshop adapter has to be able to emit these files in the product and not only in the
/// test project. This forwards to it rather than keeping a private copy, so a fixture a test
/// asserts against and a file the fake emits are the same bytes for the same options — which is
/// what lets inspector coverage and adapter coverage be compared honestly rather than merely
/// looking alike.
/// </remarks>
internal static class ProductionTiffFixture
{
    internal static string Write(string directory, ProductionTiffFixtureOptions? options = null)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tif");
        WriteAt(path, options);
        return path;
    }

    internal static void WriteAt(string path, ProductionTiffFixtureOptions? options = null) =>
        FakeProductionTiff.WriteAt(path, Shipped(options ?? new ProductionTiffFixtureOptions()));

    private static FakeProductionTiffOptions Shipped(ProductionTiffFixtureOptions options) => new(
        options.LittleEndian,
        options.Compression,
        options.SamplesPerPixel,
        options.PlanarConfiguration,
        options.ExtraSample,
        options.Dpi,
        options.ChannelName,
        options.ChannelKind,
        options.FifthSampleNonEmpty,
        options.IncludePyramidIfd,
        options.LayerCompression,
        options.CmykSamples,
        options.PixelWidth,
        options.PixelHeight,
        options.W1VerticalBands,
        options.FifthSampleEverywhere,
        options.PhotometricInterpretation);
}
