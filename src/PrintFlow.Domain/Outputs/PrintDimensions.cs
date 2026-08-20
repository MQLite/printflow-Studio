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
        if (!IsUsableMillimetres(widthMm))
        {
            throw new ArgumentOutOfRangeException(nameof(widthMm), widthMm, "Width must be a positive number of millimetres.");
        }

        if (!IsUsableMillimetres(heightMm))
        {
            throw new ArgumentOutOfRangeException(nameof(heightMm), heightMm, "Height must be a positive number of millimetres.");
        }

        int pixelWidth = ToPixels(widthMm);
        int pixelHeight = ToPixels(heightMm);
        return new PrintDimensions(widthMm, heightMm, pixelWidth, pixelHeight, preset);
    }

    /// <summary>
    /// Creates dimensions from millimetres, or reports that the pair is unusable.
    /// </summary>
    /// <remarks>
    /// The non-throwing form of <see cref="FromMillimetres"/>, for the operator-input path: a
    /// mistyped size is an ordinary thing to type, not an exceptional condition. Both apply
    /// <b>the same</b> rule through <see cref="IsUsableMillimetres"/>, so a screen cannot end
    /// up accepting a value the throwing factory would have refused, and the rule itself is
    /// still stated only here (Epic 11100 Part 3C3B §4).
    /// </remarks>
    public static bool TryFromMillimetres(
        double widthMm, double heightMm, SizePreset preset, out PrintDimensions dimensions)
    {
        if (!IsUsableMillimetres(widthMm) || !IsUsableMillimetres(heightMm))
        {
            dimensions = default;
            return false;
        }

        dimensions = FromMillimetres(widthMm, heightMm, preset);
        return true;
    }

    /// <summary>
    /// The nominal millimetres of a named preset, or null for <see cref="SizePreset.Custom"/>.
    /// </summary>
    /// <remarks>
    /// Here rather than in a view model so "how big is A4" has exactly one answer in the
    /// codebase. These are the nominal ISO 216 sizes the shop's presets are named after. They
    /// are a starting point the operator can still edit rather than an automatic resize, and
    /// they say nothing about what the signed workstation preset permits — that limit check is
    /// Epic 11400's (MVP design §8.3).
    /// </remarks>
    public static (double WidthMm, double HeightMm)? NominalMillimetres(SizePreset preset) => preset switch
    {
        SizePreset.A3Landscape => (420d, 297d),
        SizePreset.A3Portrait => (297d, 420d),
        SizePreset.A4 => (210d, 297d),
        SizePreset.A5 => (148d, 210d),
        _ => null,
    };

    /// <summary>Creates dimensions at a named preset's nominal size.</summary>
    public static PrintDimensions FromPreset(SizePreset preset) =>
        NominalMillimetres(preset) is { } nominal
            ? FromMillimetres(nominal.WidthMm, nominal.HeightMm, preset)
            : throw new ArgumentOutOfRangeException(
                nameof(preset), preset, "Only a named preset has a nominal size; Custom is entered explicitly.");

    private static bool IsUsableMillimetres(double millimetres) =>
        double.IsFinite(millimetres) && millimetres > 0;

    private static int ToPixels(double millimetres) =>
        (int)Math.Round(millimetres / MillimetresPerInch * ProductionDpi, MidpointRounding.AwayFromZero);

    public override string ToString() =>
        $"{WidthMm:0.##}×{HeightMm:0.##} mm ({PixelWidth}×{PixelHeight} px @ {ProductionDpi} dpi, {Preset})";
}
