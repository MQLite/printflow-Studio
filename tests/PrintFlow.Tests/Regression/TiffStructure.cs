using System.Buffers.Binary;
using System.IO;

namespace PrintFlow.Tests.Regression;

/// <summary>
/// The structural facts of a classic TIFF, read straight from its first IFD.
/// </summary>
/// <remarks>
/// <b>Why not <c>ProductionTiffInspector</c>.</b> The Product has a perfectly good TIFF parser
/// and this deliberately does not use it. Two of the things the regression set checks are that
/// PrintFlow's own output is a separated CMYK + W1 TIFF at 300 dpi, and that the archived
/// Maintop-proven reference still is. Asking the Product's parser whether the Product's output
/// is what the Product's parser expects answers a narrower question than it appears to: a change
/// that moved both together would pass. This reads the bytes independently.
/// <para>
/// Narrow on purpose. It reads the first IFD's scalar tags and nothing else — no strips, no
/// Photoshop resource block, no layer records. Everything the set asserts is in those tags.
/// </para>
/// </remarks>
internal sealed record TiffStructure(
    bool LittleEndian,
    int Width,
    int Height,
    int SamplesPerPixel,
    int PhotometricInterpretation,
    int Compression,
    int PlanarConfiguration,
    double XResolution,
    double YResolution,
    int ResolutionUnit)
{
    /// <summary>Reads <paramref name="path"/>, throwing only if it is not a classic TIFF at all.</summary>
    public static TiffStructure Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using FileStream file = File.OpenRead(path);
        Span<byte> header = stackalloc byte[8];
        file.ReadExactly(header);

        bool little = header[0] == 0x49 && header[1] == 0x49;
        bool big = header[0] == 0x4D && header[1] == 0x4D;
        if (!little && !big)
        {
            throw new InvalidDataException($"'{path}' does not start with a TIFF byte-order mark.");
        }

        if (U16(header[2..], little) != 42)
        {
            throw new InvalidDataException($"'{path}' is not a classic TIFF (magic is not 42).");
        }

        file.Position = U32(header[4..], little);

        Span<byte> countBytes = stackalloc byte[2];
        file.ReadExactly(countBytes);
        int entries = U16(countBytes, little);

        Dictionary<int, double> scalars = [];
        byte[] entry = new byte[12];
        for (int i = 0; i < entries; i++)
        {
            file.ReadExactly(entry);
            int tag = U16(entry.AsSpan(0, 2), little);
            int type = U16(entry.AsSpan(2, 2), little);
            uint count = U32(entry.AsSpan(4, 4), little);
            Span<byte> value = entry.AsSpan(8, 4);

            // Only the first element of each tag is kept: every tag this reads is a scalar, and
            // BitsPerSample — the one vector worth having — is proved by SamplesPerPixel plus the
            // Product's own validation rather than duplicated here.
            double first = type switch
            {
                3 => U16(value, little),
                4 => U32(value, little),
                5 => ReadRational(file, U32(value, little), little),
                1 => value[0],
                _ => double.NaN,
            };

            if (count > 0 && !double.IsNaN(first))
            {
                scalars[tag] = first;
            }
        }

        int Scalar(int tag, int fallback) =>
            scalars.TryGetValue(tag, out double value) ? (int) value : fallback;

        double Rational(int tag) => scalars.TryGetValue(tag, out double value) ? value : 0;

        return new TiffStructure(
            little,
            Scalar(256, 0),
            Scalar(257, 0),
            Scalar(277, 0),
            Scalar(262, -1),
            Scalar(259, 0),
            Scalar(284, 0),
            Rational(282),
            Rational(283),
            Scalar(296, 0));
    }

    private static double ReadRational(FileStream file, uint offset, bool little)
    {
        long resume = file.Position;
        try
        {
            file.Position = offset;
            Span<byte> bytes = stackalloc byte[8];
            file.ReadExactly(bytes);
            uint denominator = U32(bytes[4..], little);
            return denominator == 0 ? 0 : (double) U32(bytes, little) / denominator;
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or ArgumentOutOfRangeException)
        {
            return double.NaN;
        }
        finally { file.Position = resume; }
    }

    private static ushort U16(ReadOnlySpan<byte> bytes, bool little) => little
        ? BinaryPrimitives.ReadUInt16LittleEndian(bytes)
        : BinaryPrimitives.ReadUInt16BigEndian(bytes);

    private static uint U32(ReadOnlySpan<byte> bytes, bool little) => little
        ? BinaryPrimitives.ReadUInt32LittleEndian(bytes)
        : BinaryPrimitives.ReadUInt32BigEndian(bytes);
}
