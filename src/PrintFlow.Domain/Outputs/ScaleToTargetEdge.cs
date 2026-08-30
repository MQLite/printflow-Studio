using System.Numerics;

namespace PrintFlow.Domain.Outputs;

/// <summary>An exact, reduced ratio retained as the projected scale.</summary>
public readonly record struct ResizeScale
{
    private ResizeScale(int numerator, int denominator)
    {
        Numerator = numerator;
        Denominator = denominator;
    }

    public int Numerator { get; }

    public int Denominator { get; }

    public decimal Ratio => (decimal)Numerator / Denominator;

    public decimal Percentage => Ratio * 100m;

    public static ResizeScale FromPixels(int targetAuthoritativePixels, int sourceAuthoritativePixels)
    {
        Positive(targetAuthoritativePixels, nameof(targetAuthoritativePixels));
        Positive(sourceAuthoritativePixels, nameof(sourceAuthoritativePixels));
        int divisor = GreatestCommonDivisor(targetAuthoritativePixels, sourceAuthoritativePixels);
        return new ResizeScale(
            targetAuthoritativePixels / divisor,
            sourceAuthoritativePixels / divisor);
    }

    private static int GreatestCommonDivisor(int first, int second)
    {
        while (second != 0)
        {
            (first, second) = (second, first % second);
        }

        return first;
    }

    private static void Positive(int value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "A scale needs positive pixels.");
        }
    }
}

/// <summary>The pure projected result of writing one custom physical edge in Photoshop.</summary>
public readonly record struct ScaleToTargetEdgeResult(
    TargetEdge SelectedTargetEdge,
    LimitingEdge PhotoshopTargetEdge,
    decimal RequestedMillimetres,
    int ProjectedPixelWidth,
    int ProjectedPixelHeight,
    ResizeScale ProjectedScale,
    ResizeDirection Direction,
    PhotoshopResizeMode ResizePolicy)
{
    public int ResolutionPpi => PrintDimensions.ProductionDpi;

    public bool SourceCapacityExceeded => Direction == ResizeDirection.Enlarge;
}

/// <summary>
/// Calculates an exact target-edge projection without opening Photoshop or accepting two target
/// dimensions. Decimal input is converted to a rational, and both midpoint decisions are made on
/// integer remainders rather than binary floating point.
/// </summary>
public static class ScaleToTargetEdge
{
    public static ScaleToTargetEdgeResult Calculate(
        int sourcePixelWidth,
        int sourcePixelHeight,
        TargetEdge selectedTargetEdge,
        decimal requestedMillimetres)
    {
        Positive(sourcePixelWidth, nameof(sourcePixelWidth));
        Positive(sourcePixelHeight, nameof(sourcePixelHeight));
        if (requestedMillimetres <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedMillimetres), requestedMillimetres,
                "A target edge must be positive millimetres.");
        }

        LimitingEdge photoshopEdge = selectedTargetEdge switch
        {
            TargetEdge.Width => LimitingEdge.Width,
            TargetEdge.Height => LimitingEdge.Height,
            TargetEdge.LongEdge when sourcePixelWidth >= sourcePixelHeight => LimitingEdge.Width,
            TargetEdge.LongEdge => LimitingEdge.Height,
            _ => throw new ArgumentOutOfRangeException(
                nameof(selectedTargetEdge), selectedTargetEdge, "Unknown target edge."),
        };

        int requestedPixels = MillimetresToPixels(requestedMillimetres);
        int sourceAuthoritativePixels = photoshopEdge == LimitingEdge.Width
            ? sourcePixelWidth
            : sourcePixelHeight;
        int sourceOtherPixels = photoshopEdge == LimitingEdge.Width
            ? sourcePixelHeight
            : sourcePixelWidth;
        int derivedOtherPixels = RoundPositiveRational(
            (BigInteger)sourceOtherPixels * requestedPixels,
            sourceAuthoritativePixels,
            "The proportional target edge exceeds the supported pixel range.");

        int projectedWidth = photoshopEdge == LimitingEdge.Width
            ? requestedPixels
            : derivedOtherPixels;
        int projectedHeight = photoshopEdge == LimitingEdge.Height
            ? requestedPixels
            : derivedOtherPixels;

        ResizeDirection direction = requestedPixels.CompareTo(sourceAuthoritativePixels) switch
        {
            0 => ResizeDirection.ResolutionOnly,
            < 0 => ResizeDirection.Shrink,
            _ => ResizeDirection.Enlarge,
        };
        PhotoshopResizeMode policy = direction switch
        {
            ResizeDirection.ResolutionOnly => PhotoshopResizeMode.None,
            ResizeDirection.Shrink => PhotoshopResizeMode.BicubicSharper,
            ResizeDirection.Enlarge => PhotoshopResizeMode.PreserveDetails,
            _ => throw new InvalidOperationException("Unknown resize direction."),
        };

        return new ScaleToTargetEdgeResult(
            selectedTargetEdge,
            photoshopEdge,
            requestedMillimetres,
            projectedWidth,
            projectedHeight,
            ResizeScale.FromPixels(requestedPixels, sourceAuthoritativePixels),
            direction,
            policy);
    }

    private static int MillimetresToPixels(decimal millimetres)
    {
        (BigInteger numerator, BigInteger denominator) = DecimalRational(millimetres);
        return RoundPositiveRational(
            numerator * 1500,
            denominator * 127,
            "The requested millimetres exceed the supported pixel range.");
    }

    private static (BigInteger Numerator, BigInteger Denominator) DecimalRational(decimal value)
    {
        int[] bits = decimal.GetBits(value);
        BigInteger numerator =
            ((BigInteger)(uint)bits[2] << 64) |
            ((BigInteger)(uint)bits[1] << 32) |
            (uint)bits[0];
        int scale = (bits[3] >> 16) & 0x7F;
        return (numerator, BigInteger.Pow(10, scale));
    }

    private static int RoundPositiveRational(
        BigInteger numerator, BigInteger denominator, string overflowMessage)
    {
        BigInteger quotient = BigInteger.DivRem(numerator, denominator, out BigInteger remainder);
        if (remainder * 2 >= denominator)
        {
            quotient += BigInteger.One;
        }

        if (quotient < BigInteger.One)
        {
            throw new ArgumentOutOfRangeException(
                nameof(numerator), "The requested size rounds below one pixel.");
        }

        if (quotient > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(numerator), overflowMessage);
        }

        return (int)quotient;
    }

    private static void Positive(int pixels, string parameterName)
    {
        if (pixels <= 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName, pixels, "A target-edge calculation needs positive source pixels.");
        }
    }
}
