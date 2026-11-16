namespace PrintFlow.Domain.AiDesign.Memorial;

public sealed record RectMm(double X, double Y, double Width, double Height);
public sealed record RectPx(int X, int Y, int Width, int Height);
public sealed record MemorialCanvas(double WidthMm, double HeightMm, double? OuterDiameterMm, double? SafeDiameterMm);
public enum MemorialLayerRole { Person, OriginalDecor, GeneratedDecor, Background }
public sealed record MemorialImageLayer(Guid AssetId, MemorialLayerRole Role, RectMm Bounds, RectPx? SourceCrop, int ZOrder)
{
    /// <summary>Full source pixel rectangle, origin zero. The renderer must verify it against the decoded source.</summary>
    public RectPx? SourceExtent { get; init; }
    /// <summary>Explicit permission for unprotected generated edge decoration to enter the badge ring.</summary>
    public bool AllowEdgeExtension { get; init; }
}
public enum MemorialTextPlacement { Flat, Arc }
public sealed record MemorialTextStyle(string Key, string FontFamily, double FontSizeMm,
    RectMm Bounds, MemorialTextPlacement Placement, double? ArcRadiusMm);
public sealed record MemorialTextLine(string Key, string Text, string FontFamily, double FontSizeMm,
    RectMm Bounds, MemorialTextPlacement Placement, double? ArcRadiusMm);
public sealed record MemorialLayout(MemorialCanvas Canvas, IReadOnlyList<MemorialImageLayer> Images,
    IReadOnlyList<MemorialTextLine> Text, bool ProductTemplateConfirmed);
