using System.Globalization;
using System.IO;
using ImageMagick;
using ImageMagick.Formats;
using PrintFlow.Domain.Settings;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Tests.Integration.Persistence;
using Xunit.Abstractions;

namespace PrintFlow.Tests.Smoke;

[Collection(SqliteCollection.Name)]
public sealed class LocalBackgroundMatteModelSmoke(ITestOutputHelper log)
{
    private static string? Model => Environment.GetEnvironmentVariable("PRINTFLOW_B2_MODEL");
    private static string? Samples => Environment.GetEnvironmentVariable("PRINTFLOW_B2_SAMPLES");

    public sealed class ProvisionedTheoryAttribute : TheoryAttribute
    {
        public ProvisionedTheoryAttribute()
        {
            if (string.IsNullOrWhiteSpace(Model) || string.IsNullOrWhiteSpace(Samples))
                Skip = "Set PRINTFLOW_B2_MODEL and PRINTFLOW_B2_SAMPLES (portrait.png and general.png).";
        }
    }

    [ProvisionedTheory]
    [InlineData("portrait", false)]
    [InlineData("general", false)]
    [InlineData("portrait", true)]
    public async Task Pinned_model_preserves_every_rgb_sample_canvas_and_existing_alpha(string sample, bool withAlpha)
    {
        using var temp = new TempWorkspace();
        var backend = new LocalBackgroundMatteBackend(new FileWorkspace(temp.Root), Model!);
        backend.GetCapability(ImageProcessingMethod.BiRefNet)!.Unavailable.ShouldBeNull();
        string name = withAlpha ? sample + "-16bit-alpha" : sample;
        var request = LocalBackgroundMatteTests.Request(name);
        string input = LocalEnhancementTests.Absolute(temp, request.Input);
        string originalPath = Path.Combine(Samples!, sample + ".png");

        if (withAlpha)
            AddExistingAlpha(originalPath, input);
        else
            File.Copy(originalPath, input);

        var result = await LocalBackgroundMatteTests.Must(backend.ProcessAsync(request, default));

        string output = LocalEnhancementTests.Absolute(temp, request.ExpectedOutput);
        using var before = new MagickImage(input);
        using var after = new MagickImage(output);
        (after.Width, after.Height, after.Depth).ShouldBe((before.Width, before.Height, before.Depth));
        after.GetColorProfile()?.ToByteArray().ShouldBe(before.GetColorProfile()?.ToByteArray());
        ushort[] source = LocalEnhancementTests.Rgba(before);
        ushort[] cutout = LocalEnhancementTests.Rgba(after);
        int clear = 0, opaque = 0, partial = 0, retainedPartial = 0;
        for (int index = 0; index < cutout.Length; index += 4)
        {
            if (!cutout.AsSpan(index, 3).SequenceEqual(source.AsSpan(index, 3)))
                throw new InvalidDataException($"RGB changed at pixel {index / 4}.");
            ushort alpha = cutout[index + 3];
            ushort originalAlpha = source[index + 3];
            if (alpha > originalAlpha)
                throw new InvalidDataException($"Alpha increased at pixel {index / 4}.");
            if (alpha == 0)
                clear++;
            else if (alpha == ushort.MaxValue)
                opaque++;
            else
                partial++;

            if (originalAlpha is > 0 and < ushort.MaxValue && alpha == originalAlpha)
                retainedPartial++;
        }

        clear.ShouldBeGreaterThan(0);
        opaque.ShouldBeGreaterThan(0);
        partial.ShouldBeGreaterThan(0);
        if (withAlpha)
        {
            retainedPartial.ShouldBeGreaterThan(0);
            File.Copy(input, Path.Combine(Samples!, name + ".png"), overwrite: true);
        }

        File.Copy(output, Path.Combine(Samples!, name + "-cutout.png"), overwrite: true);
        log.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{name}: {result.AdapterNotes} {result.Elapsed.TotalSeconds:F2}s; transparent {clear}, partial {partial}, opaque {opaque}, retained partial {retainedPartial}."));
    }

    private static void AddExistingAlpha(string input, string output)
    {
        using var image = new MagickImage(input);
        ushort[] samples = LocalEnhancementTests.Rgba(image);
        for (int i = 0; i < samples.Length / 4; i++)
        {
            int x = i % (int)image.Width;
            samples[i * 4 + 3] = x < 24 ? (ushort)0 : x is >= 163 and < 167 ? (ushort)29297 : ushort.MaxValue;
        }
        image.ReadPixels(samples, new PixelReadSettings(image.Width, image.Height, StorageType.Quantum, PixelMapping.RGBA));
        image.SetProfile(ColorProfiles.SRGB);
        image.Write(output, new PngWriteDefines { BitDepth = 16, PreserveiCCP = true, ColorType = ColorType.TrueColorAlpha });
    }
}
