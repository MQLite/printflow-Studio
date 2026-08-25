using PrintFlow.Domain.Results;

namespace PrintFlow.Infrastructure.Adapters.Meitu;

/// <summary>Exact alpha facts read from every pixel of an exported cutout.</summary>
public sealed record MeituTransparencyFacts(
    long PixelCount,
    long TransparentPixelCount,
    long VisiblePixelCount)
{
    public bool HasTransparentPixels => TransparentPixelCount > 0;

    public bool HasVisiblePixels => VisiblePixelCount > 0;
}

/// <summary>Reads actual alpha values from an image without changing it.</summary>
public interface IMeituTransparencyInspector
{
    Task<OperationResult<MeituTransparencyFacts>> InspectAsync(
        string absolutePath, CancellationToken cancellationToken);
}

/// <summary>The exact Background Removal alpha semantics from Epic 11300 Part C2A.</summary>
public static class MeituTransparencyRule
{
    /// <summary>
    /// Counts pixels using the literal alpha endpoints: anything below 255 is transparent to
    /// some degree, and anything above zero leaves visible foreground. No midpoint threshold is
    /// involved, so anti-aliased edge pixels remain meaningful evidence.
    /// </summary>
    public static MeituTransparencyFacts Summarise(ReadOnlySpan<byte> alphaPlane)
    {
        long transparent = 0;
        long visible = 0;

        foreach (byte alpha in alphaPlane)
        {
            if (alpha < byte.MaxValue)
            {
                transparent++;
            }

            if (alpha > byte.MinValue)
            {
                visible++;
            }
        }

        return new MeituTransparencyFacts(alphaPlane.Length, transparent, visible);
    }

    /// <summary>Requires both removed background and surviving foreground.</summary>
    public static OperationResult<Unit> Validate(MeituTransparencyFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        if (facts.PixelCount <= 0)
        {
            return OperationResult.Fail<Unit>(
                FailureCode.OutputUnreadable,
                "The exported cutout decoded to no pixels, so transparency cannot be established.");
        }

        if (!facts.HasTransparentPixels)
        {
            return OperationResult.Fail<Unit>(OperationFailure.Create(
                FailureCode.OutputValidationFailed,
                "The exported PNG is completely opaque: every decoded alpha value is 255. " +
                "Background Removal did not produce a transparent cutout.",
                isRetryable: false,
                context: new Dictionary<string, string>
                {
                    ["pixelCount"] = facts.PixelCount.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    ["transparentPixels"] = "0",
                }));
        }

        if (!facts.HasVisiblePixels)
        {
            return OperationResult.Fail<Unit>(OperationFailure.Create(
                FailureCode.OutputValidationFailed,
                "The exported PNG is completely transparent: every decoded alpha value is 0. " +
                "No visible foreground remains.",
                isRetryable: false,
                context: new Dictionary<string, string>
                {
                    ["pixelCount"] = facts.PixelCount.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    ["visiblePixels"] = "0",
                }));
        }

        return OperationResult.Ok();
    }
}
