using System.Buffers.Binary;
using System.IO;
using PrintFlow.Domain.Results;

namespace PrintFlow.Infrastructure.Adapters.Photoshop;

/// <summary>
/// Reads only the PSD envelope and Adobe resource 1057. This does not interpret layers or
/// render pixels; Photoshop remains the sole PSD renderer. Missing evidence fails closed.
/// https://www.adobe.com/devnet-apps/photoshop/fileformatashtml/
/// </summary>
internal static class PsdCompositeProbe
{
    internal static OperationResult<bool> Inspect(string path)
    {
        try
        {
            using FileStream file = File.OpenRead(path);
            Span<byte> header = stackalloc byte[26];
            file.ReadExactly(header);
            if (!header[..4].SequenceEqual("8BPS"u8) || BinaryPrimitives.ReadUInt16BigEndian(header[4..]) != 1)
                return Refuse(FailureCode.PsdUnreadable, "The source is not a supported PSD v1 document.");
            if (BinaryPrimitives.ReadUInt32BigEndian(header[14..]) is 0 or > 30000 ||
                BinaryPrimitives.ReadUInt32BigEndian(header[18..]) is 0 or > 30000)
                return Refuse(FailureCode.PsdUnreadable, "The PSD canvas dimensions are invalid.");
            Skip(file, U32(file)); // Colour-mode data.
            long resourceEnd = checked(file.Position + 4 + U32(file));
            if (resourceEnd > file.Length) throw new InvalidDataException("Truncated image resources.");
            bool? merged = null;
            int count = 0;
            Span<byte> signature = stackalloc byte[4];
            while (file.Position < resourceEnd)
            {
                if (++count > 100000) throw new InvalidDataException("Too many image resources.");
                file.ReadExactly(signature);
                if (!signature.SequenceEqual("8BIM"u8)) throw new InvalidDataException("Invalid resource signature.");
                ushort id = U16(file);
                int nameLength = file.ReadByte();
                if (nameLength < 0) throw new EndOfStreamException();
                Skip(file, (uint)(nameLength + ((nameLength + 1) % 2)));
                uint size = U32(file);
                long end = checked(file.Position + size + (size % 2));
                if (end > resourceEnd) throw new InvalidDataException("Resource exceeds its section.");
                if (id == 1057)
                {
                    if (merged is not null || size < 17 || U32(file) != 1)
                        throw new InvalidDataException("Ambiguous version information.");
                    int flag = file.ReadByte();
                    if (flag is not (0 or 1)) throw new InvalidDataException("Invalid merged-data flag.");
                    merged = flag == 1;
                }
                file.Position = end;
            }
            if (file.Position != resourceEnd) throw new InvalidDataException("Resource alignment error.");
            Skip(file, U32(file)); // Layer/mask data is deliberately opaque.
            ushort compression = U16(file);
            if (compression > 3 || file.Position >= file.Length)
                throw new InvalidDataException("The composite image section is absent or malformed.");
            return merged == true ? OperationResult.Ok(true) : Refuse(FailureCode.PsdCompositeMissing,
                "PSD requires an explicit Photoshop-compatible composite (resource 1057 hasRealMergedData=true). " +
                "Save with Maximize Compatibility enabled in Photoshop and import again.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or OverflowException)
        {
            return Refuse(FailureCode.PsdUnreadable, "PSD structure is unreadable: " + ex.Message);
        }
    }

    private static ushort U16(Stream stream)
    {
        Span<byte> bytes = stackalloc byte[2]; stream.ReadExactly(bytes);
        return BinaryPrimitives.ReadUInt16BigEndian(bytes);
    }
    private static uint U32(Stream stream)
    {
        Span<byte> bytes = stackalloc byte[4]; stream.ReadExactly(bytes);
        return BinaryPrimitives.ReadUInt32BigEndian(bytes);
    }
    private static void Skip(Stream stream, uint length)
    {
        long end = checked(stream.Position + length);
        if (end > stream.Length) throw new EndOfStreamException();
        stream.Position = end;
    }
    private static OperationResult<bool> Refuse(FailureCode code, string detail) =>
        OperationResult.Fail<bool>(code, detail);
}
