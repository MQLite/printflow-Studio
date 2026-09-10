using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace PrintFlow.Infrastructure.Adapters.Fake;

/// <summary>
/// Which deliberate deviation from the accepted production TIFF layout a fake output carries
/// (SCRUM-11097).
/// </summary>
/// <remarks>
/// Every field defaults to the accepted value, so <c>new FakeProductionTiffOptions()</c> is a file
/// <c>ProductionTiffInspector</c> genuinely accepts and each named deviation is exactly one step
/// away from it. That is the point of the shape: a fault is the accepted file with one fact
/// changed, never a separately hand-built "bad file" whose other facts might differ by accident
/// and make a test pass for the wrong reason.
/// </remarks>
internal sealed record FakeProductionTiffOptions(
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
/// The one deterministic encoder for PrintFlow's accepted production TIFF layout, and for the
/// closed set of deliberate deviations from it (SCRUM-11097).
/// </summary>
/// <remarks>
/// It ships beside <see cref="FakePhotoshopOutputProcessor"/> rather than living in the test
/// project because Fake is a real product mode: SCRUM-11097 requires the fake to produce genuine
/// structural faults through the same port Production implements, and a fake that cannot write a
/// TIFF can only ever return a pre-labelled verdict. The test project's
/// <c>ProductionTiffFixture</c> forwards here, so the repository has exactly one TIFF encoder and
/// a fault the fake emits is byte-for-byte the fault the inspector's own tests describe.
/// <para>
/// It writes bytes and nothing else. It runs no validation, reaches no verdict, and knows nothing
/// about preparations — whether the result is acceptable is decided by
/// <c>ProductionTiffInspector</c> and <c>ProductionTiffPreparationMatch</c>, and that separation
/// is what makes a fake-emitted fault evidence that the Product detects it rather than evidence
/// that the fake labelled it.
/// </para>
/// <para>
/// The accepted layout is five 8-bit interleaved samples, uncompressed, PhotometricInterpretation
/// 5, ExtraSamples 0, 300 PPI, a Photoshop W1 spot resource and an RLE <c>Layr</c> block. Nothing
/// here is a claim that Photoshop ran: nothing is resampled, no colour is converted, and no
/// underbase is generated.
/// </para>
/// </remarks>
internal static class FakeProductionTiff
{
    internal static void WriteAt(string path, FakeProductionTiffOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Create(options ?? new FakeProductionTiffOptions()));
    }

    internal static byte[] Create(FakeProductionTiffOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        byte[] bits = Shorts(
            Enumerable.Repeat((ushort)8, options.SamplesPerPixel).ToArray(), options.LittleEndian);
        byte[] xResolution = Rational(options.Dpi, 1, options.LittleEndian);
        byte[] yResolution = Rational(options.Dpi, 1, options.LittleEndian);
        byte[] resources = PhotoshopResources(options.ChannelName, options.ChannelKind);
        byte[] imageSource = PhotoshopImageSourceData(
            options.LayerCompression, options.LittleEndian, options.PixelWidth, options.PixelHeight);
        byte[] pixels = Pixels(options);

        const ushort entryCount = 18;
        int ifdLength = 2 + entryCount * 12 + 4;
        int dataOffset = 8 + ifdLength;
        int bitsOffset = dataOffset;
        int xResolutionOffset = bitsOffset + bits.Length;
        int yResolutionOffset = xResolutionOffset + xResolution.Length;
        int resourcesOffset = yResolutionOffset + yResolution.Length;
        int imageSourceOffset = resourcesOffset + resources.Length;
        int pixelsOffset = imageSourceOffset + imageSource.Length;
        int secondIfdOffset = pixelsOffset + pixels.Length;
        int totalLength = secondIfdOffset + (options.IncludePyramidIfd ? 6 : 0);

        using MemoryStream stream = new(totalLength);
        WriteAscii(stream, options.LittleEndian ? "II" : "MM");
        WriteUInt16(stream, 42, options.LittleEndian);
        WriteUInt32(stream, 8, options.LittleEndian);
        WriteUInt16(stream, entryCount, options.LittleEndian);

        Entry(stream, 254, 4, 1, ScalarLong(0, options.LittleEndian), options.LittleEndian);
        Entry(stream, 256, 4, 1, ScalarLong(options.PixelWidth, options.LittleEndian), options.LittleEndian);
        Entry(stream, 257, 4, 1, ScalarLong(options.PixelHeight, options.LittleEndian), options.LittleEndian);
        Entry(stream, 258, 3, options.SamplesPerPixel,
            OffsetOrInline(bits, bitsOffset, options.LittleEndian), options.LittleEndian);
        Entry(stream, 259, 3, 1, ScalarShort(options.Compression, options.LittleEndian), options.LittleEndian);
        Entry(stream, 262, 3, 1,
            ScalarShort(options.PhotometricInterpretation, options.LittleEndian), options.LittleEndian);
        Entry(stream, 273, 4, 1, ScalarLong(pixelsOffset, options.LittleEndian), options.LittleEndian);
        Entry(stream, 274, 3, 1, ScalarShort(1, options.LittleEndian), options.LittleEndian);
        Entry(stream, 277, 3, 1,
            ScalarShort(options.SamplesPerPixel, options.LittleEndian), options.LittleEndian);
        Entry(stream, 278, 4, 1, ScalarLong(options.PixelHeight, options.LittleEndian), options.LittleEndian);
        Entry(stream, 279, 4, 1, ScalarLong(pixels.Length, options.LittleEndian), options.LittleEndian);
        Entry(stream, 282, 5, 1, ScalarLong(xResolutionOffset, options.LittleEndian), options.LittleEndian);
        Entry(stream, 283, 5, 1, ScalarLong(yResolutionOffset, options.LittleEndian), options.LittleEndian);
        Entry(stream, 284, 3, 1,
            ScalarShort(options.PlanarConfiguration, options.LittleEndian), options.LittleEndian);
        Entry(stream, 296, 3, 1, ScalarShort(2, options.LittleEndian), options.LittleEndian);
        Entry(stream, 338, 3, 1,
            ScalarShort(options.ExtraSample, options.LittleEndian), options.LittleEndian);
        Entry(stream, 34377, 1, checked((uint)resources.Length),
            ScalarLong(resourcesOffset, options.LittleEndian), options.LittleEndian);
        Entry(stream, 37724, 7, checked((uint)imageSource.Length),
            ScalarLong(imageSourceOffset, options.LittleEndian), options.LittleEndian);
        WriteUInt32(stream, options.IncludePyramidIfd ? secondIfdOffset : 0, options.LittleEndian);

        stream.Write(bits);
        stream.Write(xResolution);
        stream.Write(yResolution);
        stream.Write(resources);
        stream.Write(imageSource);
        stream.Write(pixels);
        if (options.IncludePyramidIfd)
        {
            WriteUInt16(stream, 0, options.LittleEndian);
            WriteUInt32(stream, 0, options.LittleEndian);
        }
        return stream.ToArray();
    }

    /// <summary>
    /// The strip's interleaved samples: white ink everywhere unless the caller asks otherwise.
    /// </summary>
    /// <remarks>
    /// <see cref="FakeProductionTiffOptions.CmykSamples"/> and
    /// <see cref="FakeProductionTiffOptions.FifthSampleEverywhere"/> exist for the preview
    /// regression, which needs a file whose colour is distinguishable and whose W1 channel is
    /// full ink for <i>every</i> pixel. <see cref="FakeProductionTiffOptions.W1VerticalBands"/>
    /// is the review-payload form: equal-width vertical bands of <i>stored</i> fifth-sample
    /// values, left to right, so a file can carry deliberately distinguishable 0% (255), partial
    /// and 100% (0) white-ink regions.
    /// </remarks>
    private static byte[] Pixels(FakeProductionTiffOptions options)
    {
        ushort samples = options.SamplesPerPixel;
        int width = options.PixelWidth;
        int height = options.PixelHeight;
        byte[] data = Enumerable.Repeat(byte.MaxValue, width * height * samples).ToArray();

        if (options.CmykSamples is { Length: > 0 } cmyk)
        {
            int colourSamples = Math.Min(cmyk.Length, samples);
            for (int pixel = 0; pixel < width * height; pixel++)
            {
                Array.Copy(cmyk, 0, data, pixel * samples, colourSamples);
            }
        }

        if (samples < 5)
        {
            return data;
        }

        if (options.W1VerticalBands is { Length: > 0 } bands)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int band = Math.Min(bands.Length - 1, x * bands.Length / width);
                    data[((y * width) + x) * samples + 4] = bands[band];
                }
            }
        }
        else if (options.FifthSampleEverywhere is { } fifth)
        {
            for (int pixel = 0; pixel < width * height; pixel++)
            {
                data[pixel * samples + 4] = fifth;
            }
        }
        else if (options.FifthSampleNonEmpty)
        {
            data[4] = 0;
        }

        return data;
    }

    private static byte[] PhotoshopResources(string name, byte kind)
    {
        using MemoryStream stream = new();
        byte[] latinName = Encoding.Latin1.GetBytes(name);
        byte[] pascal = new byte[latinName.Length + 1];
        pascal[0] = checked((byte)latinName.Length);
        latinName.CopyTo(pascal, 1);
        Resource(stream, 1006, pascal);

        using MemoryStream unicode = new();
        string terminated = name + '\0';
        WriteBigUInt32(unicode, terminated.Length);
        unicode.Write(Encoding.BigEndianUnicode.GetBytes(terminated));
        Resource(stream, 1045, unicode.ToArray());

        using MemoryStream display = new();
        WriteBigUInt32(display, 1);
        display.Write(new byte[]
        {
            0, 0,
            0xff, 0xff, 0, 0, 0, 0, 0, 0,
            0, 100,
            kind,
        });
        Resource(stream, 1077, display.ToArray());
        return stream.ToArray();
    }

    private static void Resource(Stream stream, ushort id, byte[] data)
    {
        WriteAscii(stream, "8BIM");
        WriteBigUInt16(stream, id);
        stream.WriteByte(0);
        stream.WriteByte(0);
        WriteBigUInt32(stream, data.Length);
        stream.Write(data);
        if ((data.Length & 1) != 0) stream.WriteByte(0);
    }

    private static byte[] PhotoshopImageSourceData(
        ushort layerCompression, bool littleEndian, int width, int height)
    {
        using MemoryStream layer = new();
        WriteUInt16(layer, 1, littleEndian);
        WriteUInt32(layer, 0, littleEndian);
        WriteUInt32(layer, 0, littleEndian);
        WriteUInt32(layer, height, littleEndian);
        WriteUInt32(layer, width, littleEndian);
        WriteUInt16(layer, 5, littleEndian);
        short[] ids = [-1, 0, 1, 2, 3];
        foreach (short id in ids)
        {
            WriteUInt16(layer, unchecked((ushort)id), littleEndian);
            WriteUInt32(layer, 2, littleEndian);
        }
        WritePhotoshopIdentifier(layer, "8BIM", littleEndian);
        WritePhotoshopIdentifier(layer, "norm", littleEndian);
        layer.Write(new byte[] { 255, 0, 8, 0 });
        WriteUInt32(layer, 0, littleEndian);
        foreach (short _ in ids) WriteUInt16(layer, layerCompression, littleEndian);

        using MemoryStream result = new();
        WriteAscii(result, "Adobe Photoshop Document Data Block\0");
        WritePhotoshopIdentifier(result, "8BIM", littleEndian);
        WritePhotoshopIdentifier(result, "Layr", littleEndian);
        WriteUInt32(result, layer.Length, littleEndian);
        result.Write(layer.ToArray());
        return result.ToArray();
    }

    private static void Entry(
        Stream stream, ushort tag, ushort type, uint count, byte[] value, bool littleEndian)
    {
        WriteUInt16(stream, tag, littleEndian);
        WriteUInt16(stream, type, littleEndian);
        WriteUInt32(stream, count, littleEndian);
        stream.Write(value);
    }

    private static byte[] OffsetOrInline(byte[] data, int offset, bool littleEndian) =>
        data.Length <= 4 ? [.. data, .. new byte[4 - data.Length]] : ScalarLong(offset, littleEndian);

    private static byte[] ScalarShort(long value, bool littleEndian)
    {
        byte[] result = new byte[4];
        if (littleEndian) BinaryPrimitives.WriteUInt16LittleEndian(result, checked((ushort)value));
        else BinaryPrimitives.WriteUInt16BigEndian(result, checked((ushort)value));
        return result;
    }

    private static byte[] ScalarLong(long value, bool littleEndian)
    {
        byte[] result = new byte[4];
        if (littleEndian) BinaryPrimitives.WriteUInt32LittleEndian(result, checked((uint)value));
        else BinaryPrimitives.WriteUInt32BigEndian(result, checked((uint)value));
        return result;
    }

    private static byte[] Shorts(ushort[] values, bool littleEndian)
    {
        byte[] result = new byte[values.Length * 2];
        for (int index = 0; index < values.Length; index++)
        {
            if (littleEndian) BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(index * 2), values[index]);
            else BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(index * 2), values[index]);
        }
        return result;
    }

    private static byte[] Rational(uint numerator, uint denominator, bool littleEndian)
    {
        byte[] result = new byte[8];
        if (littleEndian)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(result, numerator);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), denominator);
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(result, numerator);
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(4), denominator);
        }
        return result;
    }

    private static void WritePhotoshopIdentifier(Stream stream, string value, bool littleEndian)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(value);
        if (littleEndian) Array.Reverse(bytes);
        stream.Write(bytes);
    }

    private static void WriteAscii(Stream stream, string value) =>
        stream.Write(Encoding.ASCII.GetBytes(value));

    private static void WriteUInt16(Stream stream, long value, bool littleEndian) =>
        stream.Write(ScalarShort(value, littleEndian), 0, 2);

    private static void WriteUInt32(Stream stream, long value, bool littleEndian) =>
        stream.Write(ScalarLong(value, littleEndian));

    private static void WriteBigUInt16(Stream stream, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteBigUInt32(Stream stream, long value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, checked((uint)value));
        stream.Write(bytes);
    }
}
