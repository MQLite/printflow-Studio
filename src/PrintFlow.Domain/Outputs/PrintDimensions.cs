namespace PrintFlow.Domain.Outputs;

/// <summary>
/// The shop's validated size presets (MVP design §8.3, signed preset
/// <c>productionGeometryContract.resize.limitsMillimetres</c>).
/// </summary>
public enum SizePreset
{
    A3Landscape,
    A3Portrait,
    A4,
    A5,
    Custom,
}

/// <summary>
/// Target physical dimensions for a production output, at the fixed production resolution.
/// </summary>
/// <remarks>
/// Production resolution is fixed at 300 DPI (MVP design §8.3). Non-proportional stretching
/// is never permitted, and resizing is shrink-only — but the *limits* that make those rules
/// concrete come from the signed workstation preset, not from this type. Epic 11100 models
/// the value and its internal consistency; Epic 11400 applies it through Photoshop.
/// </remarks>
public readonly record struct PrintDimensions
{
    /// <summary>Fixed production resolution (MVP design §8.3).</summary>
    public const int ProductionDpi = 300;

    private const double MillimetresPerInch = 25.4;

    private PrintDimensions(
        double widthMm, double heightMm, int pixelWidth, int pixelHeight, SizePreset preset)
    {
        WidthMm = widthMm;
        HeightMm = heightMm;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        Preset = preset;
    }

    public double WidthMm { get; }

    public double HeightMm { get; }

    public int PixelWidth { get; }

    public int PixelHeight { get; }

    public int Dpi => ProductionDpi;

    public SizePreset Preset { get; }

    /// <summary>
    /// Creates dimensions from millimetres, deriving pixels at the fixed production DPI.
    /// </summary>
    public static PrintDimensions FromMillimetres(double widthMm, double heightMm, SizePreset preset)
    {
        if (!double.IsFinite(widthMm) || widthMm <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(widthMm), widthMm, "Width must be a positive number of millimetres.");
        }

        if (!double.IsFinite(heightMm) || heightMm <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(heightMm), heightMm, "Height must be a positive number of millimetres.");
        }

        int pixelWidth = ToPixels(widthMm);
        int pixelHeight = ToPixels(heightMm);
        return new PrintDimensions(widthMm, heightMm, pixelWidth, pixelHeight, preset);
    }

    private static int ToPixels(double millimetres) =>
        (int)Math.Round(millimetres / MillimetresPerInch * ProductionDpi, MidpointRounding.AwayFromZero);

    public override string ToString() =>
        $"{WidthMm:0.##}×{HeightMm:0.##} mm ({PixelWidth}×{PixelHeight} px @ {ProductionDpi} dpi, {Preset})";
}
