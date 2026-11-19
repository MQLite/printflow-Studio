using System.IO;
using ImageMagick;
using PrintFlow.Domain.Files;

namespace PrintFlow.Infrastructure.Imaging;

internal static class WebpRasterCodec
{
    private const long MaxPixels = 64_000_000;
    private const long MaxEncodedBytes = 256L * 1024 * 1024;

    internal static MemoryStream? OpenPngForWebp(Stream source, CancellationToken cancellationToken)
    {
        Span<byte> header = stackalloc byte[FormatSniffer.RequiredHeaderLength];
        int length = source.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        source.Position = 0;
        if (FormatSniffer.Detect(header[..length]) != ImageFormat.Webp) return null;

        MemoryStream png = new();
        try
        {
            WritePng(source, png, cancellationToken);
            png.Position = 0;
            return png;
        }
        catch
        {
            png.Dispose();
            throw;
        }
    }

    internal static void WritePng(Stream source, Stream output, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (source.Length is <= 0 or > MaxEncodedBytes)
            throw new InvalidDataException("The WebP file is empty or exceeds the encoded size limit.");

        try
        {
            var settings = new MagickReadSettings { Format = MagickFormat.WebP };
            var metadata = new MagickImageInfo(source, settings);
            if (metadata.Width == 0 || metadata.Height == 0 || (long)metadata.Width * metadata.Height > MaxPixels)
                throw new InvalidDataException("The WebP image exceeds the supported pixel limit.");

            source.Position = 0;
            settings.FrameCount = 2;
            using MagickImageCollection frames = new();
            frames.Read(source, settings);
            if (frames.Count != 1)
                throw new InvalidDataException("A single WebP frame is required for print preparation.");

            cancellationToken.ThrowIfCancellationRequested();
            var frame = frames[0];
            frame.Write(output, frame.HasAlpha ? MagickFormat.Png32 : MagickFormat.Png24);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (MagickException ex)
        {
            throw new InvalidDataException("The WebP image could not be decoded.", ex);
        }
    }
}
