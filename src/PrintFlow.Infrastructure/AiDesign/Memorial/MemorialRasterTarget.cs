using PrintFlow.Domain.AiDesign.Memorial;

namespace PrintFlow.Infrastructure.AiDesign.Memorial;

internal readonly record struct MemorialRasterTarget(int Width, int Height, int Dpi, double? CircleRadiusPixels)
{
    public const long MaximumPixels = 64_000_000;
    public const long MaximumBytes = 40L * 1024 * 1024;

    public static bool TryCreate(MemorialRenderPlan plan, out MemorialRasterTarget target)
    {
        target = default;
        int dpi = plan.Purpose switch { MemorialRenderPurpose.Preview => 96, MemorialRenderPurpose.DesignMaterial => 300, _ => 0 };
        var canvas = plan.Layout.Canvas;
        double width = Math.Ceiling(canvas.WidthMm / 25.4 * dpi), height = Math.Ceiling(canvas.HeightMm / 25.4 * dpi);
        if (dpi == 0 || !double.IsFinite(width) || !double.IsFinite(height) || width < 1 || height < 1 || width * height > MaximumPixels)
            return false;
        target = new((int)width, (int)height, dpi, canvas.OuterDiameterMm / 25.4 * dpi / 2);
        return true;
    }

    // The artwork circle follows the exact millimetres; ceiling padding is not artwork.
    public (int Start, int End) CircleSpan(int row)
    {
        if (CircleRadiusPixels is not { } radius) return (0, Width);
        double dy = row + 0.5 - radius;
        if (Math.Abs(dy) > radius) return (0, 0);
        double half = Math.Sqrt(radius * radius - dy * dy);
        return (Math.Clamp((int)Math.Ceiling(radius - half - 0.5), 0, Width),
            Math.Clamp((int)Math.Floor(radius + half - 0.5) + 1, 0, Width));
    }
}
