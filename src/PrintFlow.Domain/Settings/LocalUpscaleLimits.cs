namespace PrintFlow.Domain.Settings;

public static class LocalUpscaleLimits
{
    public const int RecommendedPixels = 1024 * 1024;
    public const int MaximumEdge = 4096;
    public const int MaximumPixels = 2048 * 2048;

    public static bool Fits(long width, long height) =>
        width is > 0 and <= MaximumEdge && height is > 0 and <= MaximumEdge && width * height <= MaximumPixels;
}
