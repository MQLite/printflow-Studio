using System.Collections.Immutable;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;

namespace PrintFlow.Infrastructure.Adapters.Photoshop;

/// <summary>Factual properties of the one W1 channel returned by Photoshop.</summary>
public sealed record PhotoshopW1ChannelFacts(
    string Name,
    string Type,
    bool IsNonEmpty,
    long NonWhitePixelCount,
    double? SolidityPercent,
    ImmutableArray<double> SpotColourComponents);

/// <summary>
/// The factual in-memory CMYK + W1 result. It is not an AdapterOutput, PrintOutput or Revision.
/// </summary>
public sealed record PhotoshopW1PreparedDocument(
    string DocumentFullPath,
    int PixelWidth,
    int PixelHeight,
    double ResolutionPpi,
    double PhysicalWidthMm,
    double PhysicalHeightMm,
    string ColourMode,
    string BitDepth,
    ImmutableArray<PhotoshopChannelFact> ProcessChannels,
    PhotoshopW1ChannelFacts W1,
    WhiteUnderbaseBranch Branch,
    string ActionSetName,
    string ActionName,
    bool OtherDocumentsMayBeOpen,
    Sha256 BackingWorkingSha256,
    bool ActionInvocationOccurredExactlyOnce);

/// <summary>
/// The closed Infrastructure-only CMYK + W1 operation. The only caller-selected action input is
/// <see cref="WhiteUnderbaseBranch"/>; set names, action names and script are verified evidence.
/// </summary>
public interface IPhotoshopW1Automation : IPhotoshopPreparationAutomation
{
    Task<OperationResult<PhotoshopW1PreparedDocument>> ExecuteW1Async(
        PhotoshopOpenedDocument opened,
        PhotoshopPreparedDocument prepared,
        WhiteUnderbaseBranch branch,
        CancellationToken cancellationToken);
}
