using System.Numerics;
using ImageMagick;
using Microsoft.ML.OnnxRuntime;
using PrintFlow.Domain.AiDesign;

namespace PrintFlow.Design;

internal sealed class PortraitPair
{
    private readonly Portrait _source;
    private readonly Portrait _candidate;
    private readonly byte[] _candidatePng;
    private readonly (int X, int Y)? _pixelTranslation;
    public byte[] SourceCrop { get; }
    public byte[] CandidateCrop { get; }

    private PortraitPair(Portrait source, Portrait candidate, byte[] png, CancellationToken ct)
    {
        _source = source;
        _candidate = candidate;
        _candidatePng = png;
        _pixelTranslation = FindPixelTranslation(ct);
        SourceCrop = source.AlignedCrop(ct);
        CandidateCrop = candidate.AlignedCrop(ct);
    }

    public static PortraitPair? TryCreate(CloudDesignReference source, byte[] candidate, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var format = source.MediaType switch
        {
            "image/jpeg" => MagickFormat.Jpeg, "image/png" => MagickFormat.Png,
            "image/webp" => MagickFormat.WebP, _ => MagickFormat.Unknown
        };
        if (format == MagickFormat.Unknown || source.Data.Length is 0 or > CloudDesignLimits.MaxReferenceBytes ||
            candidate.Length is 0 or > CloudDesignLimits.MaxArtifactBytes) return null;
        try
        {
            var reference = Portrait.Read(source.Data, format, true, ct);
            if (reference is null) return null;
            var generated = Portrait.Read(candidate, MagickFormat.Png, false, ct);
            return generated is null ? null : new PortraitPair(reference, generated, candidate, ct);
        }
        catch (Exception ex) when (ex is MagickException or OnnxRuntimeException or DllNotFoundException or TypeInitializationException)
        {
            ct.ThrowIfCancellationRequested();
            return null;
        }
    }

    // A copied photograph can acquire slightly different predicted landmarks in a different layout.
    // Find its actual integer displacement using two independent, textured eye patches at native scale.
    private (int X, int Y)? FindPixelTranslation(CancellationToken ct)
    {
        if (_source.NativeGamma is null || _source.NativeGamma != _candidate.NativeGamma) return null;
        float eyeSpan = _source.Frame.Axis.Length();
        if (Vector2.Distance(_source.Frame.Axis, _candidate.Frame.Axis) > eyeSpan * .20f) return null;
        int half = Math.Clamp((int)(eyeSpan * .065f), 2, 8);
        var eyes = new[] { _source.Face.FirstEye, _source.Face.SecondEye };
        foreach (var eye in eyes)
        {
            int x = (int)MathF.Round(eye.X) - half, y = (int)MathF.Round(eye.Y) - half;
            if (!_source.HasTexture(x, y, half * 2 + 1)) return null;
        }
        var estimated = _candidate.Frame.Origin - _source.Frame.Origin;
        int dx = (int)MathF.Round(estimated.X), dy = (int)MathF.Round(estimated.Y);
        int radius = Math.Clamp((int)MathF.Ceiling(eyeSpan * .12f), 3, 16);
        (int, int)? found = null;
        for (int y = dy - radius; y <= dy + radius; y++)
        {
            ct.ThrowIfCancellationRequested();
            for (int x = dx - radius; x <= dx + radius; x++)
            {
                if (!eyes.All(eye => IdenticalRectangle((int)MathF.Round(eye.X) - half, (int)MathF.Round(eye.Y) - half,
                    2 * half + 1, 2 * half + 1, x, y, ct))) continue;
                if (found is not null) return null;
                found = (x, y);
            }
        }
        return found;
    }

    public bool HasIdenticalFacePixels(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_pixelTranslation is not { } offset) return false;
        // Compare the entire enclosing rectangle, including temples, cheeks, forehead and chin.
        // Require every row to exist in both images, without clamping, resampling or a difference tolerance.
        var (left, top, right, bottom) = PixelBounds(_source.Frame, new(-1.12f, -1.03f), new(1.12f, 1.56f));
        return IdenticalRectangle(left, top, right - left, bottom - top, offset.X, offset.Y, ct);
    }

    private bool IdenticalRectangle(int x, int y, int width, int height, int dx, int dy, CancellationToken ct)
    {
        if (!_source.Contains(x, y, width, height) || !_candidate.Contains(x + dx, y + dy, width, height)) return false;
        for (int row = 0; row < height; row++)
        {
            ct.ThrowIfCancellationRequested();
            var source = _source.Row(x, y + row, width);
            if (!source.SequenceEqual(_candidate.Row(x + dx, y + row + dy, width))) return false;
            for (int alpha = 3; alpha < source.Length; alpha += 4)
                if (source[alpha] != 255) return false;
        }
        return true;
    }

    public byte[]? Compose(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var source = _source.Frame;
        var target = _pixelTranslation is { } shift
            ? source with { Origin = source.Origin + new Vector2(shift.X, shift.Y) }
            : _candidate.Frame;
        if (target.Axis.Length() > source.Axis.Length() * 2.5f ||
            !Compatible(_source.Face.Nose, _candidate.Face.Nose) ||
            !Compatible(_source.Face.FirstMouthCorner, _candidate.Face.FirstMouthCorner) ||
            !Compatible(_source.Face.SecondMouthCorner, _candidate.Face.SecondMouthCorner)) return null;

        bool Compatible(Vector2 sourcePoint, Vector2 targetPoint) =>
            Vector2.Distance(source.ToLocal(sourcePoint), target.ToLocal(targetPoint)) < .20f;

        // The ellipse is in eye-span units. Keep hair, ears, neck, and all pixels outside the face on the poster.
        var center = new Vector2(0, .30f);
        var radii = new Vector2(.85f, 1.03f);
        var (left, top, right, bottom) = PixelBounds(target, center - radii, center + radii);
        // If the face runs off the canvas, the ellipse would produce a visibly truncated repair.
        if (left < _candidate.Left || top < _candidate.Top || right > _candidate.Left + _candidate.Width ||
            bottom > _candidate.Top + _candidate.Height) return null;
        int width = right - left, height = bottom - top;
        if (width <= 0 || height <= 0) return null;
        byte[] patch = new byte[checked(width * height * 4)];
        for (int y = 0; y < height; y++)
        {
            ct.ThrowIfCancellationRequested();
            for (int x = 0; x < width; x++)
            {
                var pixel = new Vector2(left + x, top + y);
                var local = target.ToLocal(pixel);
                float radius = ((local - center) / radii).Length();
                if (radius >= 1) continue;
                var rgba = patch.AsSpan((y * width + x) * 4, 4);
                var sourcePoint = _pixelTranslation is { } offset ? pixel - new Vector2(offset.X, offset.Y) : source.ToImage(local);
                if (!_source.Sample(sourcePoint, rgba) || rgba[3] < 250 ||
                    _candidate.AlphaAt(left + x, top + y) < 250) return null;
                float feather = Math.Clamp((1 - radius) / .24f, 0, 1);
                rgba[3] = (byte)MathF.Round(255 * feather * feather * (3 - 2 * feather));
            }
        }
        try
        {
            using var overlay = FromRgba(patch, width, height);
            using var poster = new MagickImage(_candidatePng, new MagickReadSettings { Format = MagickFormat.Png });
            if (poster.GetColorProfile() is { } profile)
            {
                overlay.SetProfile(ColorProfiles.SRGB);
                if (!overlay.TransformColorSpace(profile)) return null;
            }
            poster.Composite(overlay, left, top, CompositeOperator.Over);
            byte[] png = poster.ToByteArray(MagickFormat.Png);
            ct.ThrowIfCancellationRequested();
            return png.Length <= CloudDesignLimits.MaxArtifactBytes ? png : null;
        }
        catch (MagickException)
        {
            ct.ThrowIfCancellationRequested();
            return null;
        }
    }

    private static (int Left, int Top, int Right, int Bottom) PixelBounds(FaceFrame frame, Vector2 min, Vector2 max)
    {
        var a = frame.ToImage(min);
        var b = frame.ToImage(new Vector2(max.X, min.Y));
        var c = frame.ToImage(new Vector2(min.X, max.Y));
        var d = frame.ToImage(max);
        var lower = Vector2.Min(Vector2.Min(a, b), Vector2.Min(c, d));
        var upper = Vector2.Max(Vector2.Max(a, b), Vector2.Max(c, d));
        return ((int)MathF.Floor(lower.X), (int)MathF.Floor(lower.Y),
            (int)MathF.Ceiling(upper.X) + 1, (int)MathF.Ceiling(upper.Y) + 1);
    }

    private static MagickImage FromRgba(byte[] rgba, int width, int height) =>
        new(rgba, new PixelReadSettings((uint)width, (uint)height, StorageType.Char, PixelMapping.RGBA));

    private sealed record Portrait(FaceLandmarks Face, FaceFrame Frame, byte[] Pixels, int Left, int Top, int Width, int Height,
        double? NativeGamma)
    {
        // Square review crops include the forehead and chin and use the same landmark coordinates in both images.
        private static readonly Vector2 CropMin = new(-1.35f, -1.08f);
        private const float CropSpan = 2.7f;

        public static Portrait? Read(byte[] bytes, MagickFormat format, bool orient, CancellationToken ct)
        {
            var settings = new MagickReadSettings { Format = format, FrameCount = 1 };
            using var input = new MemoryStream(bytes, writable: false);
            var info = new MagickImageInfo(input, settings);
            if (info.Width is < 24 or > 16384 || info.Height is < 24 or > 16384 ||
                (long)info.Width * info.Height > CloudDesignLimits.MaxArtifactPixels) return null;
            ct.ThrowIfCancellationRequested();
            input.Position = 0;
            using var image = new MagickImage(input, settings);
            if (orient) image.AutoOrient();
            else if (image.Orientation is not (Orientation.Undefined or Orientation.TopLeft)) return null;
            var profile = image.GetColorProfile();
            // Native equality is available only where conversion to these RGBA bytes loses no colour precision.
            double? nativeGamma = image.Depth <= 8 && image.ColorSpace == ColorSpace.sRGB && profile is null ? image.Gamma : null;
            if (profile is not null)
            {
                if (!image.TransformColorSpace(ColorProfiles.SRGB)) return null;
            }
            else if (image.ColorSpace != ColorSpace.sRGB) image.ColorSpace = ColorSpace.sRGB;

            var face = PortraitFaceDetector.Locate(image, ct);
            if (face is null || !face.HasUsablePose()) return null;
            var frame = face.Frame();
            var (left, top, right, bottom) = PixelBounds(frame, CropMin, CropMin + new Vector2(CropSpan));
            left = Math.Max(0, left);
            top = Math.Max(0, top);
            right = Math.Min((int)image.Width, right);
            bottom = Math.Min((int)image.Height, bottom);
            if (right <= left || bottom <= top) return null;
            using var pixels = image.GetPixels();
            var rgba = pixels.ToByteArray(left, top, (uint)(right - left), (uint)(bottom - top), PixelMapping.RGBA);
            return rgba is null ? null : new Portrait(face, frame, rgba, left, top, right - left, bottom - top, nativeGamma);
        }

        public byte[] AlignedCrop(CancellationToken ct)
        {
            const int edge = 256;
            var pixels = new byte[edge * edge * 4];
            for (int y = 0; y < edge; y++)
            {
                ct.ThrowIfCancellationRequested();
                for (int x = 0; x < edge; x++)
                {
                    var local = CropMin + new Vector2(x + .5f, y + .5f) * (CropSpan / edge);
                    var pixel = pixels.AsSpan((y * edge + x) * 4, 4);
                    if (!Sample(Frame.ToImage(local), pixel)) pixel.Clear();
                }
            }
            using var image = FromRgba(pixels, edge, edge);
            return image.ToByteArray(MagickFormat.Png32);
        }

        public byte AlphaAt(int x, int y) => Pixels[((y - Top) * Width + x - Left) * 4 + 3];

        public bool Contains(int x, int y, int width, int height) => width > 0 && height > 0 &&
            x >= Left && y >= Top && x + width <= Left + Width && y + height <= Top + Height;

        public ReadOnlySpan<byte> Row(int x, int y, int width) => Pixels.AsSpan(((y - Top) * Width + x - Left) * 4, width * 4);

        public bool HasTexture(int x, int y, int edge)
        {
            if (!Contains(x, y, edge, edge)) return false;
            int min = 255, max = 0;
            for (int row = 0; row < edge; row++)
            {
                var pixels = Row(x, y + row, edge);
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    int intensity = (pixels[i] + pixels[i + 1] + pixels[i + 2]) / 3;
                    min = Math.Min(min, intensity);
                    max = Math.Max(max, intensity);
                }
            }
            return max - min >= 24;
        }

        public bool Sample(Vector2 point, Span<byte> rgba)
        {
            float x = point.X - Left, y = point.Y - Top;
            if (x < 0 || y < 0 || x > Width - 1 || y > Height - 1) return false;
            int x0 = (int)x, y0 = (int)y;
            int x1 = Math.Min(x0 + 1, Width - 1), y1 = Math.Min(y0 + 1, Height - 1);
            int p00 = (y0 * Width + x0) * 4, p01 = (y0 * Width + x1) * 4;
            int p10 = (y1 * Width + x0) * 4, p11 = (y1 * Width + x1) * 4;
            float wx = x - x0, wy = y - y0;
            for (int channel = 0; channel < 4; channel++)
            {
                float upper = float.Lerp(Pixels[p00 + channel], Pixels[p01 + channel], wx);
                float lower = float.Lerp(Pixels[p10 + channel], Pixels[p11 + channel], wx);
                rgba[channel] = (byte)MathF.Round(float.Lerp(upper, lower, wy));
            }
            return true;
        }
    }
}
