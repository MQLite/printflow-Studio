using System.Collections.Immutable;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;

namespace PrintFlow.Infrastructure.Adapters.Photoshop;

/// <summary>One factual channel observed on the exact managed Photoshop document.</summary>
public sealed record PhotoshopChannelFact(string Name, string Type);

/// <summary>Properties Photoshop itself reported for the active managed document.</summary>
/// <remarks>
/// This is an in-memory observation, not projected planning evidence and not a produced file.
/// It intentionally carries no AdapterOutput, Revision or PrintOutput capability.
/// </remarks>
public sealed record PhotoshopDocumentFacts(
    string DocumentFullPath,
    int PixelWidth,
    int PixelHeight,
    double ResolutionPpi,
    double PhysicalWidthMm,
    double PhysicalHeightMm,
    string ColourMode,
    string BitDepth,
    ImmutableArray<PhotoshopChannelFact> Channels,
    bool W1Exists,
    int DocumentCount);

/// <summary>The factual result of preparing one in-memory managed document.</summary>
public sealed record PhotoshopPreparedDocument(
    PhotoshopDocumentFacts Before,
    PhotoshopDocumentFacts Actual,
    ResizeDirection AppliedDirection,
    PhotoshopResizeMode AppliedResizePolicy,
    LimitingEdge CommandedEdge,
    bool OtherDocumentsMayBeOpen,
    Sha256 BackingWorkingSha256);

/// <summary>
/// The closed Epic 11400 B1A.3 operation: prepare the exact managed document from an already
/// authorised immutable preparation and read back what Photoshop actually did.
/// </summary>
/// <remarks>
/// This seam exposes neither arbitrary script nor generic width/height/method parameters. Its
/// only operation consumes the closed <see cref="PhotoshopPreparation"/> union. It is kept out
/// of Workflow and Session services so resize alone cannot become workflow output success.
/// </remarks>
public interface IPhotoshopPreparationAutomation : IPhotoshopAutomationFoundation
{
    Task<OperationResult<PhotoshopPreparedDocument>> PrepareDocumentAsync(
        PhotoshopOpenedDocument document,
        PhotoshopPreparation preparation,
        CancellationToken cancellationToken);
}
