using System.Numerics;
using System.Text.Json;
using ImageMagick;

namespace PrintFlow.Infrastructure.Imaging;

internal static class PrintedArtworkMatte
{
    internal const string ToolName = "locate_printed_artwork";
    internal readonly record struct Region(int Left, int Top, int Right, int Bottom);
    internal sealed record Layout(Region[] Regions, Region[] SolidGraphics);

    internal static object LocationTool { get; } = new
    {
        type = "function",
        name = ToolName,
        description = "Locate the complete printed design on the ORIGINAL garment photo. Group nearby graphics and all surrounding " +
            "text, including faint small lettering, in one rectangle. Use separate rectangles only for distant prints. " +
            "Include a narrow margin of bare fabric on all sides for background sampling. Exclude the garment outline. " +
            "Also identify solid filled graphics (for instance a photograph or a shaded emblem) within the design: " +
            "bound each such graphic tightly in solid_graphics so its pale interior stays opaque. Exclude standalone text, " +
            "outline art and graphics with intentional transparent holes from this list. Both lists use fractions of the " +
            "FULL input width and height in integers from 0 to 1000, origin top left.",
        strict = true,
        parameters = new
        {
            type = "object",
            additionalProperties = false,
            properties = new { regions = new { type = "array", items = RegionSchema() },
                solid_graphics = new { type = "array", items = RegionSchema() } },
            required = new[] { "regions", "solid_graphics" },
        },
    };

    private static object RegionSchema()
    {
        var coordinate = new { type = "integer" };
        return new
        {
            type = "object",
            additionalProperties = false,
            properties = new { left = coordinate, top = coordinate, right = coordinate, bottom = coordinate },
            required = new[] { "left", "top", "right", "bottom" },
        };
    }

    internal static Layout? ParseLayout(string? arguments)
    {
        if (arguments is null or { Length: > 8192 })
            return null;
        using var json = JsonDocument.Parse(arguments);
        if (json.RootElement.ValueKind != JsonValueKind.Object ||
            ReadRegions(json.RootElement, "regions") is not { Length: > 0 } bounds ||
            ReadRegions(json.RootElement, "solid_graphics") is not { } solids)
            return null;
        return new(bounds, solids);
    }

    private static Region[]? ReadRegions(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var regions) || regions.ValueKind != JsonValueKind.Array ||
            regions.GetArrayLength() > 16)
            return null;
        var parsed = new List<Region>();
        int totalArea = 0;
        foreach (var item in regions.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !ReadCoordinate(item, "left", out int l) || !ReadCoordinate(item, "right", out int r) ||
                !ReadCoordinate(item, "top", out int t) || !ReadCoordinate(item, "bottom", out int b) || l >= r || t >= b)
                return null;
            totalArea += (r - l) * (b - t);
            if (totalArea > 2_000_000) // At most two full input rasters, allowing overlapping estimates.
                return null;
            parsed.Add(new(l, t, r, b));
        }
        return parsed.ToArray();
    }

    private static bool ReadCoordinate(JsonElement item, string key, out int n)
    {
        n = 0;
        return item.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number &&
            v.TryGetInt32(out n) && n is >= 0 and <= 1000;
    }

    internal static byte[]? Extract(byte[] input, Layout layout, bool enhance, CancellationToken token)
    {
        using var source = new MagickImage(input);
        source.Progress += (_, progress) => progress.Cancel = token.IsCancellationRequested;
        // Model coordinates refer to the upright photograph. Keep its pixel grid for the entire matte.
        source.AutoOrient();
        int width = checked((int)source.Width), height = checked((int)source.Height);
        byte[] rgba;
        using (var pixels = source.GetPixels())
            rgba = pixels.ToByteArray(PixelMapping.RGBA)!;
        var matte = new byte[width * height];
        foreach (var region in layout.Regions)
        {
            int left = region.Left * width / 1000, right = (region.Right * width + 999) / 1000;
            int top = region.Top * height / 1000, bottom = (region.Bottom * height + 999) / 1000;
            if (right - left < 3 || bottom - top < 3)
                return null;
            ApplyRegion(rgba, matte, width, left, top, right, bottom, token);
        }
        if (!matte.Any(value => value > 0) || !matte.Contains((byte)0))
            return null;
        foreach (var solid in layout.SolidGraphics)
        {
            token.ThrowIfCancellationRequested();
            PreserveInterior(rgba, matte, width, height, solid, token);
        }

        using var alpha = new MagickImage(matte, new PixelReadSettings(source.Width, source.Height, StorageType.Char, "I"));
        if (enhance)
            source.UnsharpMask(0, 0.7, 0.45, 0.015, Channels.RGB);
        source.Composite(alpha, CompositeOperator.CopyAlpha);
        if (enhance && Math.Max(width, height) <= 1920 && (long)width * height <= 2_073_600)
        {
            source.FilterType = FilterType.Lanczos;
            source.Resize(source.Width * 2, source.Height * 2);
        }
        source.BackgroundColor = MagickColors.Transparent;
        source.Alpha(AlphaAction.Background);
        token.ThrowIfCancellationRequested();
        return source.ToByteArray(MagickFormat.Png);
    }

    // Only fill holes enclosed by the photographed ink silhouette. A loosely placed model
    // rectangle must never make its bare fabric, or gaps between separate elements, opaque.
    private static void PreserveInterior(byte[] rgba, byte[] matte, int width, int height, Region solid, CancellationToken token)
    {
        int left = solid.Left * width / 1000, top = solid.Top * height / 1000;
        int w = (solid.Right * width + 999) / 1000 - left, h = (solid.Bottom * height + 999) / 1000 - top;
        const int border = 3;
        int stride = w + 2 * border;
        byte[] boundary = new byte[stride * (h + 2 * border)];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            boundary[(y + border) * stride + x + border] = matte[(y + top) * width + x + left] >= 32 ? (byte)255 : (byte)0;
        using var enclosed = new MagickImage(boundary, new PixelReadSettings((uint)stride, (uint)(h + 2 * border), StorageType.Char, "I"));
        enclosed.Progress += (_, progress) => progress.Cancel = token.IsCancellationRequested;
        enclosed.Morphology(new MorphologySettings { Method = MorphologyMethod.Close, Kernel = Kernel.Disk, KernelArguments = "2" });
        enclosed.FloodFill(MagickColors.Gray, 0, 0);
        using var pixels = enclosed.GetPixels();
        byte[] filled = pixels.ToByteArray("I")!;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int index = (y + top) * width + x + left;
            int i = (y + border) * stride + x + border;
            byte exterior = filled[0];
            if (filled[i] != exterior && filled[i - 1] != exterior && filled[i + 1] != exterior &&
                filled[i - stride] != exterior && filled[i + stride] != exterior)
                matte[index] = rgba[index * 4 + 3];
        }
    }

    // Estimate the underlying fabric at reduced resolution, then compare each full-resolution
    // source pixel with that surface. Only the alpha channel comes from this calculation.
    private static void ApplyRegion(byte[] rgba, byte[] matte, int stride, int left, int top, int right, int bottom,
        CancellationToken token)
    {
        int width = right - left, height = bottom - top;
        int step = (Math.Max(width, height) + 383) / 384;
        int columns = (width - 2) / step + 2, rows = (height - 2) / step + 2;
        var samples = new Vector4[columns * rows];
        for (int y = 0; y < rows; y++)
        for (int x = 0; x < columns; x++)
        {
            int i = ((top + y * (height - 1) / (rows - 1)) * stride + left + x * (width - 1) / (columns - 1)) * 4;
            samples[y * columns + x] = new(rgba[i], rgba[i + 1], rgba[i + 2], 0);
        }

        var fabric = EstimateFabric(samples, columns, rows, token);
        for (int y = 0; y < height; y++)
        {
            token.ThrowIfCancellationRequested();
            float v = (float)y * (rows - 1) / (height - 1);
            int gridY = Math.Min((int)v, rows - 2);
            int row = gridY * columns;
            float fy = v - gridY;
            for (int x = 0; x < width; x++)
            {
                float u = (float)x * (columns - 1) / (width - 1);
                int column = Math.Min((int)u, columns - 2);
                int cell = row + column;
                var upper = Vector4.Lerp(fabric[cell], fabric[cell + 1], u - column);
                var lower = Vector4.Lerp(fabric[cell + columns], fabric[cell + columns + 1], u - column);
                var cloth = Vector4.Lerp(upper, lower, fy);
                int index = (top + y) * stride + left + x;
                var rgb = new Vector4(rgba[index * 4], rgba[index * 4 + 1], rgba[index * 4 + 2], 0);
                float distance = ColourDistance(rgb - cloth);
                float opacity = Math.Clamp((distance - 14) / 12, 0, 1);
                matte[index] = Math.Max(matte[index], (byte)MathF.Round(opacity * rgba[index * 4 + 3]));
            }
        }
    }

    private static Vector4[] EstimateFabric(Vector4[] pixels, int width, int height, CancellationToken token)
    {
        var smooth = BoxMean(pixels, width, height, 3);
        var fabric = new Vector4[pixels.Length];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            float u = (float)x / (width - 1), v = (float)y / (height - 1);
            var across = Vector4.Lerp(smooth[y * width], smooth[(y + 1) * width - 1], u);
            var down = Vector4.Lerp(smooth[x], smooth[(height - 1) * width + x], v);
            var corners = Vector4.Lerp(Vector4.Lerp(smooth[0], smooth[width - 1], u),
                Vector4.Lerp(smooth[^width], smooth[^1], u), v);
            fabric[y * width + x] = across + down - corners;
        }

        // Refine the lighting estimate using nearby cloth. High-contrast marks and dense
        // ink interiors must not become samples of the underlying fabric.
        var residual = new Vector4[pixels.Length];
        var ink = new Vector4[pixels.Length];
        foreach (var (radius, tolerance) in new[] { (16, 28), (9, 18), (6, 12) })
        {
            token.ThrowIfCancellationRequested();
            for (int i = 0; i < pixels.Length; i++)
            {
                var difference = pixels[i] - fabric[i];
                float distance = ColourDistance(difference);
                float weight = Math.Max(0, 1 - distance * distance / (tolerance * tolerance));
                weight *= weight;
                residual[i] = difference * weight;
                residual[i].W = weight;
                ink[i].X = distance > 22 ? 1 : 0;
            }
            var density = BoxMean(ink, width, height, 6);
            for (int i = 0; i < pixels.Length; i++)
                if (density[i].X > 0.25f)
                    residual[i] = Vector4.Zero;
            var adjustment = BoxMean(residual, width, height, radius);
            for (int i = 0; i < pixels.Length; i++)
            {
                fabric[i] += adjustment[i] / Math.Max(0.03f, adjustment[i].W);
                fabric[i].W = 0;
            }
        }
        return fabric;
    }

    private static float ColourDistance(Vector4 difference) =>
        Math.Max(Math.Abs(difference.X), Math.Max(Math.Abs(difference.Y), Math.Abs(difference.Z)));

    private static Vector4[] BoxMean(Vector4[] values, int width, int height, int radius)
    {
        int stride = width + 1;
        var integral = new Vector4[stride * (height + 1)];
        var result = new Vector4[values.Length];
        for (int y = 0; y < height; y++)
        {
            var sum = Vector4.Zero;
            for (int x = 0; x < width; x++)
            {
                sum += values[y * width + x];
                integral[(y + 1) * stride + x + 1] = integral[y * stride + x + 1] + sum;
            }
        }
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int l = Math.Max(0, x - radius), r = Math.Min(width, x + radius + 1);
            int t = Math.Max(0, y - radius), b = Math.Min(height, y + radius + 1);
            result[y * width + x] = (integral[b * stride + r] - integral[t * stride + r]
                - integral[b * stride + l] + integral[t * stride + l]) / ((r - l) * (b - t));
        }
        return result;
    }
}
