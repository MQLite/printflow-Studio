using System.IO;
using ImageMagick;

namespace PrintFlow.Infrastructure.Imaging;

internal static class BiRefNetMatte
{
    internal const int Edge = 1024;
    internal const int Area = Edge * Edge;

    public static float[] PrepareInput(MagickImage source, CancellationToken token)
    {
        using var modelImage = source.Clone();
        modelImage.Progress += (_, progress) => progress.Cancel = token.IsCancellationRequested;
        modelImage.AutoOrient();
        if (modelImage.GetColorProfile() is not null)
        {
            if (!modelImage.TransformColorSpace(ColorProfiles.SRGB))
                throw new MagickImageErrorException("Could not convert the input ICC profile to sRGB for inference.");
        }
        else if (modelImage.ColorSpace != ColorSpace.sRGB)
        {
            modelImage.ColorSpace = ColorSpace.sRGB;
        }

        // Composite only the model copy. Hidden RGB in transparent pixels cannot
        // influence segmentation and the output retains its straight samples.
        if (modelImage.HasAlpha)
        {
            modelImage.BackgroundColor = new MagickColor("#808080");
            modelImage.Alpha(AlphaAction.Remove);
        }

        modelImage.FilterType = FilterType.Triangle;
        modelImage.Resize(new MagickGeometry(Edge, Edge) { IgnoreAspectRatio = true });
        token.ThrowIfCancellationRequested();
        using var pixels = modelImage.GetPixels();
        ushort[] values = pixels.ToShortArray(PixelMapping.RGB)
            ?? throw new MagickImageErrorException("Could not read the model RGB samples.");
        var input = new float[3 * Area];
        for (int i = 0; i < Area; i++)
        {
            if (i % Edge == 0)
                token.ThrowIfCancellationRequested();
            input[i] = (values[3 * i] / 65535f - 0.485f) / 0.229f;
            input[Area + i] = (values[3 * i + 1] / 65535f - 0.456f) / 0.224f;
            input[2 * Area + i] = (values[3 * i + 2] / 65535f - 0.406f) / 0.225f;
        }
        return input;
    }

    public static void Apply(MagickImage source, float[] logits, uint depth, CancellationToken token)
    {
        if (logits.Length != Area)
            throw new InvalidDataException("The BiRefNet matte has the wrong dimensions.");

        // Sigmoid is applied before geometry is mapped back. A continuous matte
        // avoids thresholding fine hairs and antialiased edges.
        for (int i = 0; i < logits.Length; i++)
        {
            if (i % Edge == 0)
                token.ThrowIfCancellationRequested();
            if (!float.IsFinite(logits[i]))
                throw new InvalidDataException("The BiRefNet matte contains invalid sample values.");
            logits[i] = 1f / (1f + MathF.Exp(-logits[i]));
        }

        // A palette entry is shared across pixels. Materialise direct samples before editing Alpha.
        source.ClassType = ClassType.Direct;
        if (!source.HasAlpha)
            source.HasAlpha = true;
        using var pixels = source.GetPixels();
        int alphaChannel = (int)(pixels.GetChannelIndex(PixelChannel.Alpha)
            ?? throw new InvalidDataException("The working image has no writable Alpha channel."));
        int width = (int)source.Width;
        int height = (int)source.Height;
        int maximum = depth == 16 ? ushort.MaxValue : byte.MaxValue;
        int quantumScale = ushort.MaxValue / maximum;
        // Map original pixel centres back into the orientation used by the model.
        // EXIF's transposes exchange the source axes; flips reverse the respective axis.
        Orientation orientation = source.Orientation;
        bool transpose = orientation is Orientation.LeftTop or Orientation.RightTop or
            Orientation.RightBottom or Orientation.LeftBottom;
        bool reverseColumn = orientation is Orientation.TopRight or Orientation.BottomRight or
            Orientation.RightBottom or Orientation.LeftBottom;
        bool reverseRow = orientation is Orientation.BottomLeft or Orientation.BottomRight or
            Orientation.RightTop or Orientation.RightBottom;
        var columns = new SamplePosition[width];
        for (int x = 0; x < width; x++)
            columns[x] = Position(reverseColumn ? width - 1 - x : x, width);

        int lowest = maximum, highest = 0;
        bool outputVisible = false, alphaReduced = false;

        for (int y = 0; y < height; y++)
        {
            token.ThrowIfCancellationRequested();
            var row = pixels.GetArea(0, y, source.Width, 1)
                ?? throw new InvalidDataException("Could not access working image pixels.");
            int channels = row.Length / width;
            SamplePosition rowPosition = Position(reverseRow ? height - 1 - y : y, height);
            for (int x = 0; x < width; x++)
            {
                SamplePosition horizontal = transpose ? rowPosition : columns[x];
                SamplePosition vertical = transpose ? columns[x] : rowPosition;
                float upper = Lerp(logits[vertical.First * Edge + horizontal.First],
                    logits[vertical.First * Edge + horizontal.Second], horizontal.Weight);
                float lower = Lerp(logits[vertical.Second * Edge + horizontal.First],
                    logits[vertical.Second * Edge + horizontal.Second], horizontal.Weight);
                float coverage = Lerp(upper, lower, vertical.Weight);
                int index = x * channels + alphaChannel;
                if (row[index] > 0)
                {
                    int modelAlpha = (int)MathF.Round(coverage * maximum);
                    lowest = Math.Min(lowest, modelAlpha);
                    highest = Math.Max(highest, modelAlpha);
                }

                int alpha = (int)MathF.Round(row[index] * coverage / quantumScale);
                ushort composed = (ushort)(alpha * quantumScale);
                alphaReduced |= composed < row[index];
                row[index] = composed;
                outputVisible |= alpha > 0;
            }
            pixels.SetArea(0, y, source.Width, 1, row);
        }

        if (!outputVisible)
            throw new InvalidDataException("The BiRefNet mask leaves no visible foreground.");
        if (!alphaReduced)
            throw new InvalidDataException("The BiRefNet mask did not remove any background.");
        if (lowest == highest)
            throw new InvalidDataException("The BiRefNet mask is uniform across the visible input.");
    }

    private static SamplePosition Position(int value, int size)
    {
        float position = Math.Clamp((value + 0.5f) * Edge / size - 0.5f, 0f, Edge - 1);
        int first = (int)position;
        return new(first, Math.Min(first + 1, Edge - 1), position - first);
    }

    private static float Lerp(float a, float b, float weight) => a + (b - a) * weight;

    private readonly record struct SamplePosition(int First, int Second, float Weight);
}
