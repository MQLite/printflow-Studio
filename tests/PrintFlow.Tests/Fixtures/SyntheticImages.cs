using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PrintFlow.Tests.Fixtures;

/// <summary>
/// Generates small, genuinely valid synthetic images at test time via WIC's own encoders —
/// never a real customer or production file (task §50).
/// </summary>
internal static class SyntheticImages
{
    public static byte[] Png(int width = 4, int height = 3, double dpi = 300, bool alpha = true)
    {
        PixelFormat format = alpha ? PixelFormats.Bgra32 : PixelFormats.Bgr32;
        WriteableBitmap bitmap = new(width, height, dpi, dpi, format, null);
        WritePattern(bitmap, width, height, format.BitsPerPixel / 8);

        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using MemoryStream stream = new();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// A BGRA32 PNG whose alpha is decided per pixel by <paramref name="alphaAt"/>, with a
    /// constant, recognisable colour underneath.
    /// </summary>
    /// <remarks>
    /// The colour is the same everywhere on purpose, including under fully transparent pixels:
    /// it means a trim that measured colour instead of alpha would find the whole canvas
    /// interesting and could not accidentally produce the right answer. The alpha channel is
    /// the only thing that varies, so it is the only thing a passing test can be measuring.
    /// </remarks>
    public static byte[] PngWithAlpha(int width, int height, Func<int, int, byte> alphaAt, double dpi = 300)
    {
        ArgumentNullException.ThrowIfNull(alphaAt);

        WriteableBitmap bitmap = new(width, height, dpi, dpi, PixelFormats.Bgra32, null);
        byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = ((y * width) + x) * 4;
                pixels[i] = 0x20;
                pixels[i + 1] = 0x40;
                pixels[i + 2] = 0x80;
                pixels[i + 3] = alphaAt(x, y);
            }
        }

        bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);

        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using MemoryStream stream = new();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>The alpha byte of every pixel of a PNG, row-major, as WIC decodes it.</summary>
    /// <remarks>
    /// Reads back through WIC rather than trusting what was written, so an integration test can
    /// assert that the <i>surviving</i> pixels are the ones the algorithm claimed to keep.
    /// </remarks>
    public static byte[] ReadAlphaPlane(string absolutePath, out int width, out int height)
    {
        using FileStream stream = new(absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        BitmapDecoder decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
            BitmapCacheOption.OnLoad);

        BitmapSource frame = decoder.Frames[0];
        width = frame.PixelWidth;
        height = frame.PixelHeight;

        BitmapSource bgra = frame.Format == PixelFormats.Bgra32
            ? frame
            : new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);

        byte[] pixels = new byte[width * height * 4];
        bgra.CopyPixels(pixels, width * 4, 0);

        byte[] plane = new byte[width * height];
        for (int i = 0; i < plane.Length; i++)
        {
            plane[i] = pixels[(i * 4) + 3];
        }

        return plane;
    }

    /// <summary>The pixel format WIC reports for a file, preserving the stored format.</summary>
    public static PixelFormat FormatOf(string absolutePath)
    {
        using FileStream stream = new(absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        BitmapDecoder decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
            BitmapCacheOption.OnLoad);

        return decoder.Frames[0].Format;
    }

    public static byte[] Jpeg(int width = 4, int height = 3, double dpi = 300)
    {
        WriteableBitmap bitmap = new(width, height, dpi, dpi, PixelFormats.Bgr32, null);
        WritePattern(bitmap, width, height, 4);

        JpegBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using MemoryStream stream = new();
        encoder.Save(stream);
        return stream.ToArray();
    }

    public static byte[] Tiff(int width = 4, int height = 3, double dpi = 300)
    {
        WriteableBitmap bitmap = new(width, height, dpi, dpi, PixelFormats.Bgra32, null);
        WritePattern(bitmap, width, height, 4);

        TiffBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using MemoryStream stream = new();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>Magic bytes only — WIC cannot reliably decode PSD, and the inspector does not try.</summary>
    public static byte[] PsdHeaderOnly() =>
        [0x38, 0x42, 0x50, 0x53, 0x00, 0x01, 0, 0, 0, 0, 0, 0, 0, 3, 0, 0, 0, 4, 0, 8, 0, 3];

    /// <summary>Magic bytes only — a minimal, non-rasterisable PDF stand-in.</summary>
    public static byte[] PdfHeaderOnly() => System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n%%EOF\n");

    /// <summary>Not an image at all — used for the plain SHA-256 known-vector check.</summary>
    public static byte[] KnownVectorAbc() => System.Text.Encoding.ASCII.GetBytes("abc");

    /// <summary>The published SHA-256 of the three-byte ASCII string "abc".</summary>
    public const string KnownVectorAbcSha256 =
        "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD";

    private static void WritePattern(WriteableBitmap bitmap, int width, int height, int bytesPerPixel)
    {
        byte[] pixels = new byte[width * height * bytesPerPixel];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (byte)(i % 251);
        }

        bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * bytesPerPixel, 0);
    }
}
