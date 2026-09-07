using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Text;
using PrintFlow.Domain.Results;

namespace PrintFlow.Infrastructure.Adapters.Photoshop;

internal interface IProductionTiffInspector
{
    OperationResult<ProductionTiffFacts> Inspect(string absolutePath);
}

/// <summary>
/// Where the accepted layout's pixels actually are, for a reader that has already been told the
/// layout is accepted (SCRUM-11104 §15, §45).
/// </summary>
/// <remarks>
/// Deliberately not part of <see cref="ProductionTiffFacts"/>. Facts are the validation record a
/// Revision's audit note is written from and a file offset is not a fact about production
/// quality; it is a detail only something about to read the samples has any use for. Keeping it
/// separate is what stops "where the bytes are" from leaking into the workflow-facing record.
/// <para>
/// It exists at all so the review decoder does not become a second TIFF parser. One parse
/// establishes both that the file is an accepted production TIFF and where its strips lie, which
/// is also what makes "no review payload for an unvalidated TIFF" structural rather than a rule
/// two parsers each have to remember (§45, §61).
/// </para>
/// </remarks>
internal sealed record ProductionTiffRaster(
    int PixelWidth,
    int PixelHeight,
    int SamplesPerPixel,
    uint RowsPerStrip,
    ImmutableArray<uint> StripOffsets,
    ImmutableArray<uint> StripByteCounts);

/// <summary>One validated production TIFF: what it is, and where its samples are.</summary>
internal sealed record ProductionTiffInspection(
    ProductionTiffFacts Facts,
    ProductionTiffRaster Raster);

/// <summary>
/// Narrow classic-TIFF parser for PrintFlow's accepted uncompressed separated-CMYK + W1 output.
/// It deliberately does not become the PNG/JPEG inspector and does not decode composite colour.
/// </summary>
internal sealed class ProductionTiffInspector : IProductionTiffInspector
{
    private const ushort TiffMagic = 42;
    private const ushort TypeByte = 1;
    private const ushort TypeAscii = 2;
    private const ushort TypeShort = 3;
    private const ushort TypeLong = 4;
    private const ushort TypeRational = 5;
    private const ushort TypeUndefined = 7;

    public OperationResult<ProductionTiffFacts> Inspect(string absolutePath)
    {
        OperationResult<ProductionTiffInspection> inspected = InspectForReview(absolutePath);
        return inspected.IsFailure
            ? OperationResult.Fail<ProductionTiffFacts>(inspected.Failure)
            : OperationResult.Ok(inspected.Value.Facts);
    }

    /// <summary>
    /// The same single validation pass, keeping the strip geometry a review decode needs
    /// (SCRUM-11104 §15, §45).
    /// </summary>
    /// <remarks>
    /// <see cref="Inspect"/> is this method with the raster dropped, rather than a second parser
    /// beside it. That is deliberate: two readers of the same bytes are two chances for the one
    /// that draws pixels to accept a layout the one that validates would have refused, and the
    /// whole point of §45 is that a review payload cannot exist for a file validation rejected.
    /// </remarks>
    internal OperationResult<ProductionTiffInspection> InspectForReview(string absolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);

        try
        {
            using FileStream stream = new(
                absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: false);
            using BinaryReader reader = new(stream, Encoding.ASCII, leaveOpen: true);

            if (stream.Length < 8)
            {
                return Invalid("The file is too short to be a TIFF.");
            }

            byte first = reader.ReadByte();
            byte second = reader.ReadByte();
            bool littleEndian = first == (byte)'I' && second == (byte)'I';
            if (!littleEndian && !(first == (byte)'M' && second == (byte)'M'))
            {
                return Invalid("The TIFF byte-order marker is missing.");
            }

            if (ReadUInt16(reader, littleEndian) != TiffMagic)
            {
                return Invalid("The classic TIFF magic value is not 42.");
            }

            uint firstIfdOffset = ReadUInt32(reader, littleEndian);
            List<Dictionary<ushort, TiffEntry>> ifds = ReadIfds(
                reader, stream.Length, firstIfdOffset, littleEndian);
            Dictionary<ushort, TiffEntry> firstIfd = ifds[0];

            uint width = ScalarUnsigned(reader, firstIfd, 256, littleEndian);
            uint height = ScalarUnsigned(reader, firstIfd, 257, littleEndian);
            ushort[] bits = Shorts(reader, Required(firstIfd, 258), littleEndian);
            ushort compression = CheckedUShort(ScalarUnsigned(reader, firstIfd, 259, littleEndian), 259);
            ushort photometric = CheckedUShort(ScalarUnsigned(reader, firstIfd, 262, littleEndian), 262);
            uint[] stripOffsets = UnsignedValues(reader, Required(firstIfd, 273), littleEndian);
            ushort samples = CheckedUShort(ScalarUnsigned(reader, firstIfd, 277, littleEndian), 277);
            uint rowsPerStrip = ScalarUnsigned(reader, firstIfd, 278, littleEndian);
            uint[] stripByteCounts = UnsignedValues(reader, Required(firstIfd, 279), littleEndian);
            double xResolution = Rational(reader, Required(firstIfd, 282), littleEndian);
            double yResolution = Rational(reader, Required(firstIfd, 283), littleEndian);
            ushort planar = CheckedUShort(ScalarUnsigned(reader, firstIfd, 284, littleEndian), 284);
            ushort resolutionUnit = CheckedUShort(ScalarUnsigned(reader, firstIfd, 296, littleEndian), 296);
            ushort[] extraSamples = Shorts(reader, Required(firstIfd, 338), littleEndian);

            if (!littleEndian)
            {
                return Invalid("Production TIFF byte order is not IBM PC / little-endian.");
            }
            if (width == 0 || height == 0 || width > int.MaxValue || height > int.MaxValue)
            {
                return Invalid("The TIFF pixel geometry is absent or unsupported.");
            }
            if (samples != 5 || bits.Length != 5 || bits.Any(value => value != 8))
            {
                return Invalid("Production TIFF requires exactly five 8-bit samples.");
            }
            if (compression != 1)
            {
                return Invalid("Production TIFF image compression is not None (TIFF value 1).");
            }
            if (photometric != 5)
            {
                return Invalid("Production TIFF is not separated CMYK (PhotometricInterpretation 5).");
            }
            if (planar != 1)
            {
                return Invalid("Production TIFF samples are not interleaved/chunky.");
            }
            if (resolutionUnit != 2 || Math.Abs(xResolution - 300) > 0.0000001 ||
                Math.Abs(yResolution - 300) > 0.0000001)
            {
                return Invalid("Production TIFF resolution is not exactly 300 pixels per inch.");
            }
            if (extraSamples.Length != 1 || extraSamples[0] != 0)
            {
                return Invalid(
                    "The fifth sample is not exactly one TIFF ExtraSamples=0 production ink; " +
                    "alpha/transparency samples are refused.");
            }

            if (firstIfd.TryGetValue(254, out TiffEntry? subfileType) && subfileType is not null &&
                ScalarUnsigned(reader, subfileType, littleEndian) != 0)
            {
                return Invalid("The first production image is marked as a reduced or alternate subfile.");
            }

            bool hasPyramid = ifds.Count != 1 || firstIfd.ContainsKey(330);
            if (hasPyramid)
            {
                return Invalid("The TIFF contains another IFD or SubIFD; image pyramid must be off.");
            }

            PhotoshopResourceFacts resources = ReadPhotoshopResources(
                reader, Required(firstIfd, 34377));
            if (resources.Names.Length != 1 ||
                !string.Equals(resources.Names[0], "W1", StringComparison.Ordinal) ||
                resources.UnicodeNames.Length != 1 ||
                !string.Equals(resources.UnicodeNames[0], "W1", StringComparison.Ordinal) ||
                resources.ChannelKinds.Length != 1 || resources.ChannelKinds[0] != 2)
            {
                return Invalid(
                    "Photoshop image resources do not identify exactly one W1 channel as a spot colour.");
            }

            TiffEntry imageSourceData = Required(firstIfd, 37724);
            PhotoshopLayerFacts layers = ReadPhotoshopLayerFacts(
                reader, imageSourceData, littleEndian);
            if (layers.LayerCount < 1 || !layers.AllChannelsRle)
            {
                return Invalid("Photoshop layer data is absent or is not wholly RLE-compressed.");
            }

            long nonWhiteW1 = CountNonWhiteFifthSamples(
                reader, stream.Length, width, height, rowsPerStrip, stripOffsets, stripByteCounts);
            if (nonWhiteW1 <= 0)
            {
                return Invalid("The named W1 production ink sample is wholly empty on disk.");
            }

            return OperationResult.Ok(new ProductionTiffInspection(
                new ProductionTiffFacts(
                ByteOrder: "IBM PC / little-endian",
                PixelWidth: checked((int)width),
                PixelHeight: checked((int)height),
                XResolutionDpi: xResolution,
                YResolutionDpi: yResolution,
                ResolutionUnit: resolutionUnit,
                BitsPerSample: [.. bits],
                SamplesPerPixel: samples,
                PhotometricInterpretation: photometric,
                Compression: compression,
                PlanarConfiguration: planar,
                ExtraSamples: [.. extraSamples],
                ExtraChannelNames: resources.UnicodeNames,
                W1IsPhotoshopSpotChannel: true,
                W1NonWhiteSampleCount: nonWhiteW1,
                HasAlphaOrTransparencySample: false,
                HasImagePyramid: false,
                HasPhotoshopImageSourceData: true,
                PhotoshopLayerCount: layers.LayerCount,
                AllPhotoshopLayerChannelsUseRle: layers.AllChannelsRle,
                ImageFileDirectoryCount: ifds.Count,
                ValidationLimitations:
                [
                    "W1 sample content is proven non-empty from the fifth uncompressed interleaved sample; " +
                    "the inspector does not interpret the ink's visual meaning.",
                ]),
                new ProductionTiffRaster(
                    checked((int)width),
                    checked((int)height),
                    samples,
                    rowsPerStrip,
                    [.. stripOffsets],
                    [.. stripByteCounts])));
        }
        catch (TiffValidationException ex)
        {
            return Invalid(ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EndOfStreamException or
                                   OverflowException or ArgumentException)
        {
            return OperationResult.Fail<ProductionTiffInspection>(OperationFailure.Create(
                FailureCode.OutputUnreadable,
                $"The production TIFF could not be read completely: {ex.Message}",
                isRetryable: false));
        }
    }

    private static List<Dictionary<ushort, TiffEntry>> ReadIfds(
        BinaryReader reader, long fileLength, uint firstOffset, bool littleEndian)
    {
        List<Dictionary<ushort, TiffEntry>> result = [];
        HashSet<uint> seen = [];
        uint offset = firstOffset;
        while (offset != 0)
        {
            if (result.Count >= 16 || !seen.Add(offset))
            {
                throw new TiffValidationException("The TIFF IFD chain is cyclic or unexpectedly long.");
            }
            if (offset > fileLength - 2)
            {
                throw new TiffValidationException("A TIFF IFD offset lies outside the file.");
            }

            reader.BaseStream.Position = offset;
            ushort count = ReadUInt16(reader, littleEndian);
            long requiredBytes = checked((long)count * 12 + 4);
            if (reader.BaseStream.Position > fileLength - requiredBytes)
            {
                throw new TiffValidationException("A TIFF IFD is truncated.");
            }

            Dictionary<ushort, TiffEntry> entries = [];
            for (int index = 0; index < count; index++)
            {
                ushort tag = ReadUInt16(reader, littleEndian);
                ushort type = ReadUInt16(reader, littleEndian);
                uint valueCount = ReadUInt32(reader, littleEndian);
                byte[] valueField = ReadExactly(reader, 4);
                TiffEntry entry = new(tag, type, valueCount, valueField, littleEndian);
                if (!entries.TryAdd(tag, entry))
                {
                    throw new TiffValidationException($"TIFF tag {tag} is duplicated.");
                }
            }

            offset = ReadUInt32(reader, littleEndian);
            result.Add(entries);
        }

        if (result.Count == 0)
        {
            throw new TiffValidationException("The TIFF contains no Image File Directory.");
        }
        return result;
    }

    private static TiffEntry Required(Dictionary<ushort, TiffEntry> entries, ushort tag) =>
        entries.TryGetValue(tag, out TiffEntry? value) && value is not null
            ? value
            : throw new TiffValidationException($"Required TIFF tag {tag} is missing.");

    private static uint ScalarUnsigned(
        BinaryReader reader, Dictionary<ushort, TiffEntry> entries, ushort tag, bool littleEndian) =>
        ScalarUnsigned(reader, Required(entries, tag), littleEndian);

    private static uint ScalarUnsigned(BinaryReader reader, TiffEntry entry, bool littleEndian)
    {
        uint[] values = UnsignedValues(reader, entry, littleEndian);
        return values.Length == 1
            ? values[0]
            : throw new TiffValidationException($"TIFF tag {entry.Tag} is not scalar.");
    }

    private static uint[] UnsignedValues(BinaryReader reader, TiffEntry entry, bool littleEndian)
    {
        byte[] data = EntryBytes(reader, entry);
        if (entry.Type == TypeShort)
        {
            ushort[] values = DecodeShorts(data, littleEndian);
            return [.. values.Select(value => (uint)value)];
        }
        if (entry.Type == TypeLong)
        {
            uint[] values = new uint[checked((int)entry.Count)];
            for (int index = 0; index < values.Length; index++)
            {
                values[index] = DecodeUInt32(data.AsSpan(index * 4, 4), littleEndian);
            }
            return values;
        }
        throw new TiffValidationException(
            $"TIFF tag {entry.Tag} has unsupported integer type {entry.Type}.");
    }

    private static ushort[] Shorts(BinaryReader reader, TiffEntry entry, bool littleEndian)
    {
        if (entry.Type != TypeShort)
        {
            throw new TiffValidationException($"TIFF tag {entry.Tag} is not SHORT.");
        }
        return DecodeShorts(EntryBytes(reader, entry), littleEndian);
    }

    private static ushort[] DecodeShorts(byte[] data, bool littleEndian)
    {
        ushort[] values = new ushort[data.Length / 2];
        for (int index = 0; index < values.Length; index++)
        {
            values[index] = DecodeUInt16(data.AsSpan(index * 2, 2), littleEndian);
        }
        return values;
    }

    private static double Rational(BinaryReader reader, TiffEntry entry, bool littleEndian)
    {
        if (entry.Type != TypeRational || entry.Count != 1)
        {
            throw new TiffValidationException($"TIFF tag {entry.Tag} is not one RATIONAL.");
        }
        byte[] data = EntryBytes(reader, entry);
        uint numerator = DecodeUInt32(data.AsSpan(0, 4), littleEndian);
        uint denominator = DecodeUInt32(data.AsSpan(4, 4), littleEndian);
        if (denominator == 0)
        {
            throw new TiffValidationException($"TIFF tag {entry.Tag} has a zero rational denominator.");
        }
        return numerator / (double)denominator;
    }

    private static byte[] EntryBytes(BinaryReader reader, TiffEntry entry)
    {
        int typeSize = entry.Type switch
        {
            TypeByte or TypeAscii or TypeUndefined => 1,
            TypeShort => 2,
            TypeLong => 4,
            TypeRational => 8,
            _ => throw new TiffValidationException(
                $"TIFF tag {entry.Tag} has unsupported type {entry.Type}."),
        };
        long byteCount = checked((long)entry.Count * typeSize);
        if (byteCount > int.MaxValue)
        {
            throw new TiffValidationException($"TIFF tag {entry.Tag} is too large to inspect in memory.");
        }
        if (byteCount <= 4)
        {
            return entry.ValueField[..(int)byteCount];
        }
        if (entry.ValueOffset > reader.BaseStream.Length - byteCount)
        {
            throw new TiffValidationException($"TIFF tag {entry.Tag} points outside the file.");
        }
        reader.BaseStream.Position = entry.ValueOffset;
        return ReadExactly(reader, checked((int)byteCount));
    }

    private static PhotoshopResourceFacts ReadPhotoshopResources(BinaryReader reader, TiffEntry entry)
    {
        byte[] data = EntryBytes(reader, entry);
        int offset = 0;
        List<string> names = [];
        List<string> unicodeNames = [];
        List<byte> kinds = [];
        HashSet<ushort> seenRelevant = [];
        while (offset < data.Length)
        {
            if (data.Length - offset < 12 || !data.AsSpan(offset, 4).SequenceEqual("8BIM"u8))
            {
                throw new TiffValidationException("Photoshop image-resource data is malformed.");
            }
            offset += 4;
            ushort id = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset, 2));
            offset += 2;
            int nameStart = offset;
            int resourceNameLength = data[offset++];
            offset = checked(offset + resourceNameLength);
            if ((offset - nameStart) % 2 != 0) offset++;
            if (offset > data.Length - 4)
            {
                throw new TiffValidationException("A Photoshop image-resource name is truncated.");
            }
            uint length = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));
            offset += 4;
            if (length > int.MaxValue || offset > data.Length - (int)length)
            {
                throw new TiffValidationException("A Photoshop image resource points outside tag 34377.");
            }
            ReadOnlySpan<byte> resource = data.AsSpan(offset, (int)length);
            if (id is 1006 or 1045 or 1077)
            {
                if (!seenRelevant.Add(id))
                {
                    throw new TiffValidationException($"Photoshop image resource {id} is duplicated.");
                }
                if (id == 1006) names.AddRange(ReadPascalStrings(resource));
                if (id == 1045) unicodeNames.AddRange(ReadUnicodeStrings(resource));
                if (id == 1077) kinds.AddRange(ReadDisplayInfoKinds(resource));
            }
            offset = checked(offset + (int)length);
            if ((length & 1) != 0) offset++;
        }
        if (seenRelevant.Count != 3)
        {
            throw new TiffValidationException(
                "Photoshop alpha-name, Unicode-name or display-info resources are missing.");
        }
        return new PhotoshopResourceFacts([.. names], [.. unicodeNames], [.. kinds]);
    }

    private static IEnumerable<string> ReadPascalStrings(ReadOnlySpan<byte> data)
    {
        List<string> result = [];
        int offset = 0;
        while (offset < data.Length)
        {
            int length = data[offset++];
            if (offset > data.Length - length)
            {
                throw new TiffValidationException("A Photoshop Pascal channel name is truncated.");
            }
            result.Add(Encoding.Latin1.GetString(data.Slice(offset, length)));
            offset += length;
        }
        return result;
    }

    private static IEnumerable<string> ReadUnicodeStrings(ReadOnlySpan<byte> data)
    {
        List<string> result = [];
        int offset = 0;
        while (offset < data.Length)
        {
            if (offset > data.Length - 4)
            {
                throw new TiffValidationException("A Photoshop Unicode channel-name length is truncated.");
            }
            uint characterCount = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));
            offset += 4;
            long byteCount = checked((long)characterCount * 2);
            if (byteCount > int.MaxValue || offset > data.Length - (int)byteCount)
            {
                throw new TiffValidationException("A Photoshop Unicode channel name is truncated.");
            }
            string value = Encoding.BigEndianUnicode.GetString(data.Slice(offset, (int)byteCount));
            result.Add(value.TrimEnd('\0'));
            offset += (int)byteCount;
        }
        return result;
    }

    private static IEnumerable<byte> ReadDisplayInfoKinds(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4)
        {
            throw new TiffValidationException("Photoshop DisplayInfo is truncated.");
        }
        uint count = BinaryPrimitives.ReadUInt32BigEndian(data[..4]);
        if (count > int.MaxValue || data.Length != checked(4 + (int)count * 13))
        {
            throw new TiffValidationException("Photoshop DisplayInfo count does not match its data.");
        }
        byte[] kinds = new byte[(int)count];
        for (int index = 0; index < kinds.Length; index++)
        {
            kinds[index] = data[4 + index * 13 + 12];
        }
        return kinds;
    }

    private static PhotoshopLayerFacts ReadPhotoshopLayerFacts(
        BinaryReader reader, TiffEntry entry, bool littleEndian)
    {
        if (entry.Type is not (TypeByte or TypeUndefined) || entry.Count <= 36)
        {
            throw new TiffValidationException("Photoshop ImageSourceData has an unsupported TIFF type.");
        }
        long blockStart = entry.ValueOffset;
        long blockEnd = checked(blockStart + entry.Count);
        if (blockEnd > reader.BaseStream.Length)
        {
            throw new TiffValidationException("Photoshop ImageSourceData points outside the TIFF.");
        }
        reader.BaseStream.Position = blockStart;
        byte[] header = ReadExactly(reader, 36);
        if (!header.AsSpan().SequenceEqual("Adobe Photoshop Document Data Block\0"u8))
        {
            throw new TiffValidationException("Photoshop ImageSourceData magic is missing.");
        }

        string signature = ReadPhotoshopIdentifier(reader, littleEndian);
        string key = ReadPhotoshopIdentifier(reader, littleEndian);
        uint layerBlockLength = ReadUInt32(reader, littleEndian);
        long layerBlockEnd = checked(reader.BaseStream.Position + layerBlockLength);
        if (!string.Equals(signature, "8BIM", StringComparison.Ordinal) ||
            !string.Equals(key, "Layr", StringComparison.Ordinal) || layerBlockEnd > blockEnd)
        {
            throw new TiffValidationException("Photoshop ImageSourceData has no accepted Layr block.");
        }

        short signedLayerCount = unchecked((short)ReadUInt16(reader, littleEndian));
        int layerCount = Math.Abs((int)signedLayerCount);
        if (layerCount == 0 || layerCount > 4096)
        {
            throw new TiffValidationException("Photoshop layer count is absent or unreasonable.");
        }

        List<uint[]> channelLengthsByLayer = new(layerCount);
        for (int layer = 0; layer < layerCount; layer++)
        {
            Skip(reader, 16, layerBlockEnd);
            ushort channelCount = ReadUInt16(reader, littleEndian);
            if (channelCount == 0 || channelCount > 64)
            {
                throw new TiffValidationException("A Photoshop layer channel count is unsupported.");
            }
            uint[] channelLengths = new uint[channelCount];
            for (int channel = 0; channel < channelCount; channel++)
            {
                Skip(reader, 2, layerBlockEnd);
                channelLengths[channel] = ReadUInt32(reader, littleEndian);
                if (channelLengths[channel] < 2)
                {
                    throw new TiffValidationException("A Photoshop layer channel record is truncated.");
                }
            }
            if (!string.Equals(ReadPhotoshopIdentifier(reader, littleEndian), "8BIM", StringComparison.Ordinal))
            {
                throw new TiffValidationException("A Photoshop layer blend signature is invalid.");
            }
            _ = ReadPhotoshopIdentifier(reader, littleEndian);
            Skip(reader, 4, layerBlockEnd);
            uint extraLength = ReadUInt32(reader, littleEndian);
            Skip(reader, extraLength, layerBlockEnd);
            channelLengthsByLayer.Add(channelLengths);
        }

        bool allRle = true;
        foreach (uint[] lengths in channelLengthsByLayer)
        {
            foreach (uint length in lengths)
            {
                if (reader.BaseStream.Position > layerBlockEnd - length)
                {
                    throw new TiffValidationException("Photoshop layer channel pixels overrun the Layr block.");
                }
                ushort compression = ReadUInt16(reader, littleEndian);
                allRle &= compression == 1;
                reader.BaseStream.Position += length - 2;
            }
        }
        if (reader.BaseStream.Position > layerBlockEnd)
        {
            throw new TiffValidationException("Photoshop layer data overruns its declared block.");
        }
        return new PhotoshopLayerFacts(layerCount, allRle);
    }

    private static long CountNonWhiteFifthSamples(
        BinaryReader reader,
        long fileLength,
        uint width,
        uint height,
        uint rowsPerStrip,
        uint[] offsets,
        uint[] byteCounts)
    {
        if (rowsPerStrip == 0 || offsets.Length != byteCounts.Length)
        {
            throw new TiffValidationException("TIFF strip arrays or RowsPerStrip are invalid.");
        }
        long expectedStripCount = ((long)height + rowsPerStrip - 1) / rowsPerStrip;
        if (offsets.Length != expectedStripCount)
        {
            throw new TiffValidationException("TIFF strip count does not cover the image exactly.");
        }

        byte[] buffer = new byte[1 << 20];
        long nonWhite = 0;
        for (int strip = 0; strip < offsets.Length; strip++)
        {
            uint firstRow = checked((uint)strip * rowsPerStrip);
            uint rows = Math.Min(rowsPerStrip, height - firstRow);
            long expectedBytes = checked((long)width * rows * 5);
            if (byteCounts[strip] != expectedBytes || offsets[strip] > fileLength - expectedBytes)
            {
                throw new TiffValidationException(
                    "An uncompressed TIFF strip does not contain exactly five samples per pixel.");
            }

            reader.BaseStream.Position = offsets[strip];
            long remaining = expectedBytes;
            long positionInStrip = 0;
            while (remaining > 0)
            {
                int wanted = (int)Math.Min(buffer.Length, remaining);
                int read = reader.BaseStream.Read(buffer, 0, wanted);
                if (read == 0) throw new EndOfStreamException("A TIFF strip ended early.");
                for (int index = 0; index < read; index++)
                {
                    if ((positionInStrip + index) % 5 == 4 && buffer[index] != byte.MaxValue)
                    {
                        nonWhite++;
                    }
                }
                positionInStrip += read;
                remaining -= read;
            }
        }
        return nonWhite;
    }

    private static string ReadPhotoshopIdentifier(BinaryReader reader, bool littleEndian)
    {
        byte[] value = ReadExactly(reader, 4);
        if (littleEndian) Array.Reverse(value);
        return Encoding.ASCII.GetString(value);
    }

    private static void Skip(BinaryReader reader, long count, long boundary)
    {
        if (count < 0 || reader.BaseStream.Position > boundary - count)
        {
            throw new TiffValidationException("Photoshop layer metadata overruns its declared block.");
        }
        reader.BaseStream.Position += count;
    }

    private static ushort CheckedUShort(uint value, ushort tag) =>
        value <= ushort.MaxValue
            ? (ushort)value
            : throw new TiffValidationException($"TIFF tag {tag} exceeds SHORT range.");

    private static ushort ReadUInt16(BinaryReader reader, bool littleEndian) =>
        DecodeUInt16(ReadExactly(reader, 2), littleEndian);

    private static uint ReadUInt32(BinaryReader reader, bool littleEndian) =>
        DecodeUInt32(ReadExactly(reader, 4), littleEndian);

    private static ushort DecodeUInt16(ReadOnlySpan<byte> bytes, bool littleEndian) => littleEndian
        ? BinaryPrimitives.ReadUInt16LittleEndian(bytes)
        : BinaryPrimitives.ReadUInt16BigEndian(bytes);

    private static uint DecodeUInt32(ReadOnlySpan<byte> bytes, bool littleEndian) => littleEndian
        ? BinaryPrimitives.ReadUInt32LittleEndian(bytes)
        : BinaryPrimitives.ReadUInt32BigEndian(bytes);

    private static byte[] ReadExactly(BinaryReader reader, int count)
    {
        byte[] bytes = reader.ReadBytes(count);
        return bytes.Length == count ? bytes : throw new EndOfStreamException();
    }

    private static OperationResult<ProductionTiffInspection> Invalid(string detail) =>
        OperationResult.Fail<ProductionTiffInspection>(OperationFailure.Create(
            FailureCode.OutputValidationFailed,
            detail + " The Working artefact was retained and no output metadata was created.",
            isRetryable: false,
            context: new Dictionary<string, string>
            {
                ["validatedCandidateCreated"] = "false",
                ["adapterOutputCreated"] = "false",
                ["revisionCreated"] = "false",
                ["workingArtefactRetained"] = "true",
            }));

    private sealed record TiffEntry(
        ushort Tag,
        ushort Type,
        uint Count,
        byte[] ValueField,
        bool LittleEndian)
    {
        public uint ValueOffset => DecodeUInt32(ValueField, LittleEndian);
    }

    private sealed record PhotoshopResourceFacts(
        ImmutableArray<string> Names,
        ImmutableArray<string> UnicodeNames,
        ImmutableArray<byte> ChannelKinds);

    private readonly record struct PhotoshopLayerFacts(int LayerCount, bool AllChannelsRle);

    private sealed class TiffValidationException(string message) : Exception(message);
}
