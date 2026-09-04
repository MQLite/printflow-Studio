using System.Collections.Immutable;

namespace PrintFlow.Domain.Files;

/// <summary>Facts observed by Photoshop, alongside the PSD's explicit compatibility flag.</summary>
public sealed record PsdInspection(
    int PixelWidth,
    int PixelHeight,
    string OriginalMode,
    int BitDepth,
    bool HasRealMergedData,
    bool? HasTransparency,
    ImmutableArray<PsdChannel> Channels,
    string PhotoshopVersion)
{
    public bool HasSpots => Channels.Any(c => c.Kind == "SPOTCOLOR");
    public bool HasW1 => Channels.Any(c => string.Equals(c.Name, "W1", StringComparison.OrdinalIgnoreCase));
}

public sealed record PsdChannel(string Name, string Kind);
