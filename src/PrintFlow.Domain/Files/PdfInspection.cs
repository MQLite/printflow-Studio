namespace PrintFlow.Domain.Files;

/// <summary>Observed PDF facts. Page numbers are one-based; geometry is in 1/96-inch units.</summary>
public sealed record PdfInspection(
    bool IsReadable,
    bool? IsEncrypted,
    int? PageCount,
    int? PreparedPageNumber,
    PdfPageBox? MediaBox,
    PdfPageBox? CropBox,
    int? RotationDegrees,
    double? PageWidth,
    double? PageHeight,
    int RequestedRasterDpi,
    int? PixelWidth,
    int? PixelHeight,
    bool? HasTransparency,
    string Provider)
{
    public const int ProductionDpi = 300;
    public double? PageWidthMillimetres => PageWidth * 25.4 / 96;
    public double? PageHeightMillimetres => PageHeight * 25.4 / 96;

    /// <summary>Nearest whole pixel, halves away from zero, explicitly passed to the renderer.</summary>
    public static int RasterPixels(double size) => checked((int)Math.Round(size * ProductionDpi / 96d,
        MidpointRounding.AwayFromZero));

    public bool IsPreparedSinglePage => IsReadable && IsEncrypted == false && PageCount == 1 &&
        PreparedPageNumber == 1 && RequestedRasterDpi == ProductionDpi &&
        MediaBox is not null && CropBox is not null && RotationDegrees is 0 or 90 or 180 or 270 &&
        PageWidth is > 0 && PageHeight is > 0 && PixelWidth is > 0 && PixelHeight is > 0 &&
        PixelWidth == RasterPixels(PageWidth.Value) && PixelHeight == RasterPixels(PageHeight.Value) &&
        HasTransparency is not null && !string.IsNullOrWhiteSpace(Provider);
}

public sealed record PdfPageBox(double X, double Y, double Width, double Height);
