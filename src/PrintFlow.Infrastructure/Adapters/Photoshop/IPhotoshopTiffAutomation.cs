using System.Collections.Immutable;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;

namespace PrintFlow.Infrastructure.Adapters.Photoshop;

/// <summary>The one accepted Photoshop CC 2019 production-TIFF option set.</summary>
/// <remarks>
/// This is evidence returned by the closed operation, not configuration. No caller can supply
/// any of these values or select another format.
/// </remarks>
public sealed record PhotoshopProductionTiffSaveSettings(
    string ImageCompression,
    string PixelOrder,
    string ByteOrder,
    bool SaveLayers,
    string LayerCompression,
    bool SaveImagePyramid,
    bool StoreTransparency,
    bool SaveAlphaChannels,
    bool SaveSpotColours,
    bool EmbedColourProfile,
    bool AsCopy,
    string ExtensionType)
{
    /// <summary>The immutable production contract applied by the native bridge.</summary>
    public static readonly PhotoshopProductionTiffSaveSettings Accepted = new(
        ImageCompression: "TIFFEncoding.NONE",
        PixelOrder: "Interleaved",
        ByteOrder: "ByteOrder.IBM",
        SaveLayers: true,
        LayerCompression: "LayerCompression.RLE",
        SaveImagePyramid: false,
        StoreTransparency: false,
        SaveAlphaChannels: false,
        SaveSpotColours: true,
        EmbedColourProfile: true,
        AsCopy: true,
        ExtensionType: "Extension.LOWERCASE");
}

/// <summary>One bounded, read-only observation made while the TIFF settled.</summary>
public readonly record struct PhotoshopTiffSettleObservation(
    TimeSpan Elapsed,
    bool Exists,
    long ByteLength,
    long BytesRead,
    bool CompletelyReadable);

/// <summary>Independent facts read from the first TIFF IFD and its production samples.</summary>
public sealed record ProductionTiffFacts(
    string ByteOrder,
    int PixelWidth,
    int PixelHeight,
    double XResolutionDpi,
    double YResolutionDpi,
    ushort ResolutionUnit,
    ImmutableArray<ushort> BitsPerSample,
    ushort SamplesPerPixel,
    ushort PhotometricInterpretation,
    ushort Compression,
    ushort PlanarConfiguration,
    ImmutableArray<ushort> ExtraSamples,
    ImmutableArray<string> ExtraChannelNames,
    bool W1IsPhotoshopSpotChannel,
    long W1NonWhiteSampleCount,
    bool HasAlphaOrTransparencySample,
    bool HasImagePyramid,
    bool HasPhotoshopImageSourceData,
    int PhotoshopLayerCount,
    bool AllPhotoshopLayerChannelsUseRle,
    int ImageFileDirectoryCount,
    ImmutableArray<string> ValidationLimitations);

/// <summary>
/// A settled, independently validated Working TIFF. It is deliberately not AdapterOutput,
/// PrintOutput, Revision, or workflow success.
/// </summary>
public sealed record PhotoshopValidatedTiffCandidate(
    WorkspaceFileRef Tiff,
    Sha256 Sha256,
    long ByteLength,
    ProductionTiffFacts Facts,
    PhotoshopProductionTiffSaveSettings SaveSettings,
    Sha256 BackingWorkingSha256,
    string DocumentFullPathBefore,
    string DocumentFullPathAfter,
    TimeSpan Elapsed,
    ImmutableArray<PhotoshopTiffSettleObservation> SettlingObservations,
    ImmutableArray<string> ValidationLimitations);

/// <summary>
/// Closed Infrastructure-only production-TIFF operation. The caller supplies only a factual B1B
/// result and the exact managed Working destination PrintFlow already named.
/// </summary>
public interface IPhotoshopTiffAutomation : IPhotoshopW1Automation
{
    Task<OperationResult<PhotoshopValidatedTiffCandidate>> SaveProductionTiffAsync(
        PhotoshopOpenedDocument opened,
        PhotoshopW1PreparedDocument prepared,
        WorkspaceFileRef expectedOutput,
        CancellationToken cancellationToken);
}
