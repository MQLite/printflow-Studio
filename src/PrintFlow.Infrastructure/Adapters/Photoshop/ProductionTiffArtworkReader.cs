using System.Buffers.Binary;
using System.Collections.Immutable;
using System.IO;

namespace PrintFlow.Infrastructure.Adapters.Photoshop;

/// <summary>Reads straight CMYK and artwork opacity from a single Photoshop raster layer.</summary>
internal sealed class ProductionTiffArtworkReader
{
    private const int SampleCount = 5;
    private readonly ImmutableArray<PhotoshopArtworkChannel> _channels;
    private readonly long[] _positions = new long[SampleCount];
    private readonly byte[] _rowLengths;
    private readonly byte[] _packed;
    private readonly byte[] _row;
    private readonly int _width;

    public ProductionTiffArtworkReader(ProductionTiffRaster raster, int rowsPerBand)
    {
        _channels = raster.ArtworkChannels;
        _width = raster.PixelWidth;
        _rowLengths = new byte[rowsPerBand * sizeof(ushort)];
        _packed = new byte[(int)Math.Min(ushort.MaxValue, (long)_width * 2) * rowsPerBand];
        _row = new byte[_width];
        Samples = new byte[_width * SampleCount * rowsPerBand];

        for (int channel = 0; channel < SampleCount; channel++)
            _positions[channel] = _channels[channel].Offset + sizeof(ushort) + (long)raster.PixelHeight * sizeof(ushort);
    }

    public byte[] Samples { get; }

    public bool TryReadRows(Stream stream, int firstRow, int rowCount)
    {
        Span<byte> rowLengths = _rowLengths.AsSpan(0, rowCount * sizeof(ushort));
        for (int channel = 0; channel < SampleCount; channel++)
        {
            PhotoshopArtworkChannel plane = _channels[channel];
            long position = _positions[channel];
            long channelEnd = plane.Offset + plane.Length;
            if (position > channelEnd)
                return false;

            // A channel holds its compression marker, every row's byte count, then the packed rows.
            stream.Position = plane.Offset + sizeof(ushort) + (long)firstRow * sizeof(ushort);
            if (stream.ReadAtLeast(rowLengths, rowLengths.Length, throwOnEndOfStream: false) != rowLengths.Length)
                return false;

            long packedLength = 0;
            for (int y = 0; y < rowCount; y++)
                packedLength += RowLength(rowLengths, y);
            if (packedLength > _packed.Length || packedLength > channelEnd - position)
                return false;

            Span<byte> packed = _packed.AsSpan(0, (int)packedLength);
            stream.Position = position;
            if (stream.ReadAtLeast(packed, packed.Length, throwOnEndOfStream: false) != packed.Length)
                return false;

            for (int y = 0; y < rowCount; y++)
            {
                int length = RowLength(rowLengths, y);
                if (!TryUnpack(packed[..length], _row))
                    return false;
                packed = packed[length..];

                int offset = y * _width * SampleCount + channel;
                int invert = channel == SampleCount - 1 ? 0 : byte.MaxValue;
                for (int x = 0; x < _width; x++, offset += SampleCount)
                    Samples[offset] = (byte)(_row[x] ^ invert);
            }

            _positions[channel] = position + packedLength;
        }

        return true;
    }

    private static int RowLength(ReadOnlySpan<byte> table, int row) =>
        BinaryPrimitives.ReadUInt16LittleEndian(table[(row * sizeof(ushort))..]);

    private static bool TryUnpack(ReadOnlySpan<byte> packed, Span<byte> row)
    {
        while (!packed.IsEmpty)
        {
            int header = unchecked((sbyte)packed[0]);
            packed = packed[1..];
            if (header == sbyte.MinValue)
                continue;

            int count = header < 0 ? 1 - header : header + 1;
            int sourceCount = header < 0 ? 1 : count;
            if (count > row.Length || sourceCount > packed.Length)
                return false;

            if (header < 0)
                row[..count].Fill(packed[0]);
            else
                packed[..count].CopyTo(row);
            packed = packed[sourceCount..];
            row = row[count..];
        }

        return row.IsEmpty;
    }
}
