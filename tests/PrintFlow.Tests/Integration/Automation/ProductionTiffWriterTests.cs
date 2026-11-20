using System.IO;
using ImageMagick;
using PrintFlow.Infrastructure.Adapters.Magick;
using PrintFlow.Infrastructure.Adapters.Photoshop;

namespace PrintFlow.Tests.Integration.Automation;

public sealed class ProductionTiffWriterTests
{
    [Fact]
    public void Composite_inks_and_editable_layer_round_trip_with_their_distinct_polarities()
    {
        const int width = 260;
        const int height = 3;
        byte[] layer = new byte[width * height * 4];
        byte[] composite = new byte[width * height * 5];
        byte[] alpha = new byte[width * height];
        byte[] profile = ColorProfiles.CoatedFOGRA39.ToByteArray();
        for (int index = 0; index < alpha.Length; index++)
        {
            int x = index % width;
            alpha[index] = x < 135 ? (byte)123 : (byte)(x % 256);
            composite[index * 5 + 4] = x < 3 ? (byte)255 : (byte)(x % 200);
            for (int ink = 0; ink < 4; ink++)
            {
                byte inkAmount = ink == 0 ? (byte)47 : (byte)((index + (ink * 41)) % 256);
                layer[index * 4 + ink] = inkAmount;
                composite[index * 5 + ink] = (byte)((inkAmount * alpha[index] + 127) / 255);
            }
        }

        string path = Path.Combine(Path.GetTempPath(), $"PrintFlow-TiffWriter-{Guid.NewGuid():N}.tif");
        try
        {
            using (FileStream destination = new(path, FileMode.CreateNew, FileAccess.Write))
            {
                ProductionTiffWriter.Write(destination, width, height, composite, layer, alpha, profile, default);
            }

            var inspected = new ProductionTiffInspector().InspectForReview(path);
            inspected.IsSuccess.ShouldBeTrue(inspected.IsFailure ? inspected.Failure.ToString() : string.Empty);
            ProductionTiffFacts facts = inspected.Value.Facts;
            facts.PixelWidth.ShouldBe(width);
            facts.PixelHeight.ShouldBe(height);
            facts.ExtraChannelNames.ShouldBe(["W1"]);
            facts.W1NonWhiteSampleCount.ShouldBe((width - 3) * height);

            using BinaryReader reader = new(File.OpenRead(path));
            var tags = ReadTags(reader);
            reader.BaseStream.Position = tags[273].Offset;
            reader.ReadBytes(composite.Length).ShouldBe(composite);
            reader.BaseStream.Position = tags[34675].Offset;
            reader.ReadBytes(tags[34675].Count).ShouldBe(profile);

            var channels = ReadLayer(reader, tags[37724].Offset, width, height);
            channels[-1].ShouldBe(alpha);
            for (short ink = 0; ink < 4; ink++)
            {
                byte[] stored = [.. Enumerable.Range(0, alpha.Length)
                    .Select(index => (byte)(255 - layer[index * 4 + ink]))];
                channels[ink].ShouldBe(stored);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Cancellation_and_mismatched_planes_leave_the_destination_empty()
    {
        using MemoryStream destination = new();
        byte[] profile = ColorProfiles.CoatedFOGRA39.ToByteArray();
        CancellationToken cancelled = new(canceled: true);

        Should.Throw<OperationCanceledException>(() =>
            ProductionTiffWriter.Write(destination, 1, 1, new byte[5], new byte[4], [255], profile, cancelled));
        Should.Throw<ArgumentException>(() =>
            ProductionTiffWriter.Write(destination, 2, 1, new byte[5], new byte[8], [255, 255], profile, default));

        destination.Length.ShouldBe(0);
    }

    private static Dictionary<ushort, (int Count, uint Offset)> ReadTags(BinaryReader reader)
    {
        reader.BaseStream.Position = 4;
        reader.BaseStream.Position = reader.ReadUInt32();
        int entryCount = reader.ReadUInt16();
        Dictionary<ushort, (int Count, uint Offset)> tags = new();
        for (int index = 0; index < entryCount; index++)
        {
            ushort tag = reader.ReadUInt16();
            _ = reader.ReadUInt16();
            tags.Add(tag, (reader.ReadInt32(), reader.ReadUInt32()));
        }
        return tags;
    }

    private static Dictionary<short, byte[]> ReadLayer(BinaryReader reader, uint offset, int width, int height)
    {
        reader.BaseStream.Position = offset + 48;
        reader.ReadInt16().ShouldBe((short)1);
        reader.ReadInt32().ShouldBe(0);
        reader.ReadInt32().ShouldBe(0);
        reader.ReadInt32().ShouldBe(height);
        reader.ReadInt32().ShouldBe(width);

        int channelCount = reader.ReadUInt16();
        (short Id, uint Length)[] channels = new (short, uint)[channelCount];
        for (int index = 0; index < channelCount; index++)
            channels[index] = (reader.ReadInt16(), reader.ReadUInt32());

        reader.BaseStream.Position += 12;
        int extraLength = reader.ReadInt32();
        reader.BaseStream.Position += extraLength;
        Dictionary<short, byte[]> decoded = new();
        foreach (var (id, length) in channels)
        {
            long channelEnd = reader.BaseStream.Position + length;
            reader.ReadUInt16().ShouldBe((ushort)1);
            ushort[] rowSizes = new ushort[height];
            for (int row = 0; row < height; row++)
                rowSizes[row] = reader.ReadUInt16();

            using MemoryStream plane = new();
            foreach (ushort rowSize in rowSizes)
            {
                long rowEnd = reader.BaseStream.Position + rowSize;
                long previousPlaneLength = plane.Length;
                while (reader.BaseStream.Position < rowEnd)
                {
                    int packet = reader.ReadSByte();
                    if (packet >= 0)
                        plane.Write(reader.ReadBytes(packet + 1));
                    else if (packet != -128)
                    {
                        byte sample = reader.ReadByte();
                        for (int i = 0; i < 1 - packet; i++)
                            plane.WriteByte(sample);
                    }
                }
                reader.BaseStream.Position.ShouldBe(rowEnd);
                plane.Length.ShouldBe(previousPlaneLength + width);
            }
            reader.BaseStream.Position.ShouldBe(channelEnd);
            decoded.Add(id, plane.ToArray());
        }
        return decoded;
    }
}
