using PrintFlow.Domain.Files;

namespace PrintFlow.Domain.Outputs;

/// <summary>The physical size of the raster presented at the print dimensions step.</summary>
public sealed record EmbeddedPrintSize(decimal WidthMm, decimal HeightMm)
{
    public decimal LongEdgeMm => Math.Max(WidthMm, HeightMm);

    public static EmbeddedPrintSize? From(FileFacts? facts)
    {
        if (facts is not { PixelWidth: > 0, PixelHeight: > 0, DpiX: > 0, DpiY: > 0 })
            return null;

        int width = facts.PixelWidth.Value, height = facts.PixelHeight.Value;
        double outputWidth = width * (double)PrintDimensions.ProductionDpi / facts.DpiX.Value;
        double outputHeight = height * (double)PrintDimensions.ProductionDpi / facts.DpiY.Value;
        if (!(outputWidth >= 0.5 && outputWidth <= int.MaxValue &&
              outputHeight >= 0.5 && outputHeight <= int.MaxValue))
            return null;

        var size = new EmbeddedPrintSize(width * 25.4m / (decimal)facts.DpiX.Value,
            height * 25.4m / (decimal)facts.DpiY.Value);
        // Both physical dimensions must be attainable with the proportional TIFF resize.
        try
        {
            var plan = ScaleToTargetEdge.Calculate(width, height, TargetEdge.LongEdge, size.LongEdgeMm);
            return Math.Abs(plan.ProjectedPixelWidth - outputWidth) <= 1 &&
                Math.Abs(plan.ProjectedPixelHeight - outputHeight) <= 1 ? size : null;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
