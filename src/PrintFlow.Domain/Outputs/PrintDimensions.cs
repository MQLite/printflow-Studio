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
/// Operator-facing maximum physical bounds for a production output, at the fixed production
/// resolution.
/// </summary>
/// <remarks>
/// Production resolution is fixed at 300 DPI (MVP design §8.3). Under the accepted Epic 11400
/// B1A.1 contract, <see cref="WidthMm"/> and <see cref="HeightMm"/> are limits rather than two
/// independently exact output dimensions. The source aspect ratio determines which limit is
/// written to Photoshop; Photoshop derives the other edge with proportions constrained.
/// Non-proportional stretching is never permitted and resizing is shrink-only.
/// <para>
/// The existing pixel properties remain the independent millimetre conversions needed by the
/// pre-existing UI and persistence model. They are not an executable Photoshop target pair.
/// The future B1A migration must replace that legacy representation after it can record the
/// actual constrained Photoshop result.
/// </para>
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

    /// <summary>The accepted fit box's maximum width; an explicit name for <see cref="WidthMm"/>.</summary>
    public double MaxWidthMm => WidthMm;

    /// <summary>The accepted fit box's maximum height; an explicit name for <see cref="HeightMm"/>.</summary>
    public double MaxHeightMm => HeightMm;

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

    /// <summary>
    /// Converts millimetres to whole pixels at the fixed production resolution.
    /// </summary>
    /// <remarks>
    /// The one millimetre-to-pixel rule in the codebase, made public rather than left private
    /// because <see cref="FitWithinBounds"/> has to state its projected plan in pixels, and a
    /// second rounding rule beside this one would be a second answer to "how many pixels is
    /// 280 mm" (Epic 11400 Part B1A.2A §6).
    /// <para>
    /// It is arithmetic and emphatically not a resize instruction. It produces no Photoshop
    /// target pair; which single edge Photoshop is allowed to receive is
    /// <see cref="FitWithinBounds"/>'s answer alone.
    /// </para>
    /// </remarks>
    public static int PixelsFromMillimetres(double millimetres) =>
        IsUsableMillimetres(millimetres)
            ? ToPixels(millimetres)
            : throw new ArgumentOutOfRangeException(
                nameof(millimetres), millimetres, "A pixel conversion needs positive finite millimetres.");

    /// <summary>
    /// Converts a whole pixel count back to the physical millimetres it occupies at the fixed
    /// production resolution (SCRUM-11097, SCRUM-11129).
    /// </summary>
    /// <remarks>
    /// The inverse of <see cref="PixelsFromMillimetres"/> and the same constant, made public for
    /// the same reason that one was: three private copies of this expression already existed —
    /// in <see cref="FitWithinBounds"/>, in <c>TargetEdgePrintPreparationPlan</c> and implicitly
    /// in the stored dimensions — and saved-TIFF validation needed a fourth. Both callers now
    /// name this method, so "what physical canvas do these pixels resolve to" has exactly one
    /// answer.
    /// <para>
    /// It is deliberately not rounded. Rounding belongs where a value is presented or stored, not
    /// where a canvas is derived, and a tolerance applied to an already-rounded millimetre would
    /// silently widen with the canvas.
    /// </para>
    /// <para>
    /// This is why PrintFlow has no independent physical-size assertion over a saved TIFF: at the
    /// fixed 300 PPI the pixel grid and the resolution together <i>define</i> the physical canvas,
    /// so a file whose pixels match the preparation and whose resolution is exactly 300 PPI cannot
    /// have a wrong physical size, and a file that fails either check already has one.
    /// </para>
    /// </remarks>
    public static double MillimetresFromPixels(int pixels) =>
        pixels > 0
            ? pixels * MillimetresPerInch / ProductionDpi
            : throw new ArgumentOutOfRangeException(
                nameof(pixels), pixels, "A millimetre conversion needs a positive pixel count.");

    private static bool IsUsableMillimetres(double millimetres) =>
        double.IsFinite(millimetres) && millimetres > 0;

    private static int ToPixels(double millimetres) =>
        (int)Math.Round(millimetres / MillimetresPerInch * ProductionDpi, MidpointRounding.AwayFromZero);

    public override string ToString() =>
        $"{WidthMm:0.##}×{HeightMm:0.##} mm ({PixelWidth}×{PixelHeight} px @ {ProductionDpi} dpi, {Preset})";
}
