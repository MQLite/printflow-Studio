using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace PrintFlow.Infrastructure.Adapters.Magick;

internal static class ProductionTiffWriter
{
    private const int IfdOffset = 8;
    private const ushort EntryCount = 19;
    private const int ProcessChannels = 4;
    private const int CompositeSamples = 5;
    private const int MaximumPackBitsPacket = 128;

    /// <summary>
    /// Writes an uncompressed production composite and one editable CMYK layer to an empty,
    /// seekable TIFF stream. CMYK inputs use ink coverage. The composite's W1 sample is already
    /// inverted to Photoshop's spot polarity. The layer's alpha is separate from the spot ink.
    /// </summary>
    internal static void Write(
        Stream destination, int width, int height, byte[] compositeCmykw, byte[] layerCmyk,
        byte[] alpha, byte[] profile, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(compositeCmykw);
        ArgumentNullException.ThrowIfNull(layerCmyk);
        ArgumentNullException.ThrowIfNull(alpha);
        ArgumentNullException.ThrowIfNull(profile);
        token.ThrowIfCancellationRequested();
        if (!destination.CanWrite || !destination.CanSeek || destination.Position != 0 || destination.Length != 0)
            throw new ArgumentException("A new, empty, seekable TIFF destination is required.", nameof(destination));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        long maximumRowBytes = width + ((long)width + MaximumPackBitsPacket - 1) / MaximumPackBitsPacket;
        if (maximumRowBytes > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(width), "A TIFF layer scanline must fit a 16-bit RLE length.");

        long pixelCount = (long)width * height;
        if (alpha.Length != pixelCount || layerCmyk.Length != pixelCount * ProcessChannels ||
            compositeCmykw.Length != pixelCount * CompositeSamples)
            throw new ArgumentException("The production TIFF raster lengths do not match the dimensions.");

        if (profile.Length < 132 || !profile.AsSpan(16, 4).SequenceEqual("CMYK"u8) ||
            !profile.AsSpan(36, 4).SequenceEqual("acsp"u8) || BinaryPrimitives.ReadUInt32BigEndian(profile) != profile.Length)
            throw new ArgumentException("A complete CMYK ICC profile is required.", nameof(profile));

        // Include the worst-case layer RLE expansion and all TIFF/Photoshop headers before writing.
        long maximumFileBytes = compositeCmykw.Length + (long)profile.Length +
            ((maximumRowBytes + sizeof(ushort)) * height + sizeof(ushort)) * CompositeSamples + 4096;
        if (maximumFileBytes > uint.MaxValue)
            throw new ArgumentException("The production TIFF exceeds the classic TIFF offset range.");

        using BinaryWriter writer = new(destination, Encoding.ASCII, leaveOpen: true);
        writer.Write("II"u8);
        writer.Write((ushort)42);
        writer.Write(IfdOffset);
        destination.Position = IfdOffset + sizeof(ushort) + EntryCount * 12 + sizeof(uint);

        uint bitsOffset = Offset(writer);
        for (int sample = 0; sample < CompositeSamples; sample++)
            writer.Write((ushort)8);
        uint resolutionOffset = Offset(writer);
        writer.Write(300u);
        writer.Write(1u);

        uint resourcesOffset = Offset(writer);
        WriteSpotResources(writer);
        uint resourcesLength = Offset(writer) - resourcesOffset;
        uint profileOffset = Offset(writer);
        writer.Write(profile);
        Pad(writer, 2);

        uint stripOffset = Offset(writer);
        const int writeChunk = 1024 * 1024;
        for (int start = 0; start < compositeCmykw.Length;)
        {
            token.ThrowIfCancellationRequested();
            int count = Math.Min(writeChunk, compositeCmykw.Length - start);
            writer.Write(compositeCmykw, start, count);
            start += count;
        }
        Pad(writer, 4);

        uint layerOffset = Offset(writer);
        WriteLayer(writer, width, height, layerCmyk, alpha, (int)maximumRowBytes, token);
        uint end = Offset(writer);
        token.ThrowIfCancellationRequested();

        destination.Position = IfdOffset;
        writer.Write(EntryCount);
        Entry(writer, 254, 4, 1, 0);                                // NewSubfileType
        Entry(writer, 256, 4, 1, (uint)width);                      // ImageWidth
        Entry(writer, 257, 4, 1, (uint)height);                     // ImageLength
        Entry(writer, 258, 3, CompositeSamples, bitsOffset);        // BitsPerSample
        Entry(writer, 259, 3, 1, 1);                                // Compression: None
        Entry(writer, 262, 3, 1, 5);                                // PhotometricInterpretation: separated
        Entry(writer, 273, 4, 1, stripOffset);                      // StripOffsets
        Entry(writer, 274, 3, 1, 1);                                // Orientation: top-left
        Entry(writer, 277, 3, 1, CompositeSamples);                 // SamplesPerPixel
        Entry(writer, 278, 4, 1, (uint)height);                     // RowsPerStrip
        Entry(writer, 279, 4, 1, (uint)compositeCmykw.Length);      // StripByteCounts
        Entry(writer, 282, 5, 1, resolutionOffset);                 // XResolution
        Entry(writer, 283, 5, 1, resolutionOffset);                 // YResolution
        Entry(writer, 284, 3, 1, 1);                                // PlanarConfiguration: chunky
        Entry(writer, 296, 3, 1, 2);                                // ResolutionUnit: inches
        Entry(writer, 338, 3, 1, 0);                                // ExtraSamples: spot, no composite alpha
        Entry(writer, 34377, 1, resourcesLength, resourcesOffset);   // Photoshop image resources
        Entry(writer, 34675, 7, (uint)profile.Length, profileOffset); // ICC profile
        Entry(writer, 37724, 7, end - layerOffset, layerOffset);     // Photoshop ImageSourceData
        writer.Write(0u);                                           // End of the IFD chain
        destination.Position = end;
    }

    private static void WriteSpotResources(BinaryWriter writer)
    {
        // Photoshop image-resource blocks always use big-endian fields, even in little-endian TIFFs.
        // ResolutionInfo stores each 300 PPI value as a 16.16 fixed-point number, in inches.
        WriteResource(writer, 1005, [1, 44, 0, 0, 0, 1, 0, 1, 1, 44, 0, 0, 0, 1, 0, 1]);
        WriteResource(writer, 1006, [2, (byte)'W', (byte)'1']);
        WriteResource(writer, 1045, [0, 0, 0, 3, 0, (byte)'W', 0, (byte)'1', 0, 0]);
        // DisplayInfo: version 1, RGB red preview swatch, 100% solidity, spot channel kind 2.
        WriteResource(writer, 1077, [0, 0, 0, 1, 0, 0, 255, 255, 0, 0, 0, 0, 0, 0, 0, 100, 2]);
        WriteResource(writer, 1053, [0, 0, 0, 6]);
        // Alternate Spot Colors: reference Lab preview swatch for channel ID 6.
        WriteResource(writer, 1067, [0, 1, 0, 1, 0, 0, 0, 6, 0, 7, 21, 53, 31, 144, 27, 78, 0, 0]);
    }

    private static void WriteResource(BinaryWriter writer, ushort id, ReadOnlySpan<byte> data)
    {
        writer.Write("8BIM"u8);
        writer.Write(BinaryPrimitives.ReverseEndianness(id));
        writer.Write((ushort)0); // Empty Pascal resource name and its padding.
        writer.Write(BinaryPrimitives.ReverseEndianness((uint)data.Length));
        writer.Write(data);
        Pad(writer, 2);
    }

    private static void WriteLayer(
        BinaryWriter writer, int width, int height, byte[] layer, byte[] alpha, int maximumRowBytes,
        CancellationToken token)
    {
        Stream destination = writer.BaseStream;
        // The four-character block and blend identifiers are reversed in little-endian TIFFs.
        writer.Write("Adobe Photoshop Document Data Block\0MIB8ryaL"u8);
        uint blockSizeOffset = Offset(writer);
        writer.Write(0u);
        uint layerStart = Offset(writer);
        writer.Write((short)1);
        writer.Write(0); // top
        writer.Write(0); // left
        writer.Write(height);
        writer.Write(width);
        writer.Write((ushort)CompositeSamples);
        uint channelRecords = Offset(writer);
        for (short channel = -1; channel < ProcessChannels; channel++)
        {
            writer.Write(channel);
            writer.Write(0u);
        }

        writer.Write("MIB8mron"u8);
        writer.Write((byte)255); // opacity
        writer.Write((byte)0);   // clipping
        writer.Write((byte)8);   // visible pixel layer
        writer.Write((byte)0);   // reserved
        const int blendingRangesLength = (1 + ProcessChannels) * 8;
        writer.Write(16u + blendingRangesLength); // mask length, blending ranges and Pascal name
        writer.Write(0u);
        writer.Write((uint)blendingRangesLength);
        for (int range = 0; range < blendingRangesLength / sizeof(uint); range++)
            writer.Write(0xffff0000u); // Full source and destination range for grey and each process ink.
        writer.Write("\u0007Artwork"u8);

        byte[] row = new byte[width];
        byte[] packed = new byte[maximumRowBytes];
        ushort[] rowLengths = new ushort[height];
        uint[] channelLengths = new uint[CompositeSamples];
        for (int channel = -1; channel < ProcessChannels; channel++)
        {
            uint channelStart = Offset(writer);
            writer.Write((ushort)1); // RLE
            uint rowTableOffset = Offset(writer);
            destination.Position += (long)height * sizeof(ushort);
            for (int y = 0; y < height; y++)
            {
                token.ThrowIfCancellationRequested();
                if (channel < 0)
                {
                    alpha.AsSpan(y * width, width).CopyTo(row);
                }
                else
                {
                    int index = y * width * ProcessChannels + channel;
                    for (int x = 0; x < width; x++, index += ProcessChannels)
                        row[x] = (byte)(255 - layer[index]); // Photoshop layer CMYK is inverted.
                }
                int length = PackBits(row, packed);
                rowLengths[y] = checked((ushort)length);
                writer.Write(packed, 0, length);
            }

            uint channelEnd = Offset(writer);
            channelLengths[channel + 1] = channelEnd - channelStart;
            destination.Position = rowTableOffset;
            foreach (ushort length in rowLengths)
                writer.Write(length);
            destination.Position = channelEnd;
        }

        Pad(writer, 2);
        uint layerEnd = Offset(writer);
        Pad(writer, 4);
        uint blockEnd = Offset(writer);
        destination.Position = channelRecords;
        for (short channel = -1; channel < ProcessChannels; channel++)
        {
            writer.Write(channel);
            writer.Write(channelLengths[channel + 1]);
        }
        destination.Position = blockSizeOffset;
        writer.Write(layerEnd - layerStart);
        destination.Position = blockEnd;
    }

    private static int PackBits(ReadOnlySpan<byte> row, Span<byte> packed)
    {
        int source = 0;
        int target = 0;
        while (source < row.Length)
        {
            int start = source;
            if (StartsRun(row, source))
            {
                int limit = Math.Min(source + MaximumPackBitsPacket, row.Length);
                byte value = row[source++];
                while (source < limit && row[source] == value)
                    source++;
                packed[target++] = unchecked((byte)(1 - (source - start)));
                packed[target++] = value;
            }
            else
            {
                do { source++; }
                while (source < row.Length && source - start < MaximumPackBitsPacket && !StartsRun(row, source));
                int count = source - start;
                packed[target++] = (byte)(count - 1);
                row.Slice(start, count).CopyTo(packed[target..]);
                target += count;
            }
        }
        return target;
    }

    private static bool StartsRun(ReadOnlySpan<byte> row, int index) =>
        index < row.Length - 2 && row[index] == row[index + 1] && row[index] == row[index + 2];

    private static void Pad(BinaryWriter writer, int alignment)
    {
        while (writer.BaseStream.Position % alignment != 0)
            writer.Write((byte)0);
    }

    private static uint Offset(BinaryWriter writer) => checked((uint)writer.BaseStream.Position);

    private static void Entry(BinaryWriter writer, ushort tag, ushort type, uint count, uint value)
    {
        writer.Write(tag);
        writer.Write(type);
        writer.Write(count);
        writer.Write(value);
    }
}
