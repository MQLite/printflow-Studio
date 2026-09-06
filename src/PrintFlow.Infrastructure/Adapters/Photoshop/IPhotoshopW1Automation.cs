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

/// <summary>Where the W1 channel in a prepared document came from.</summary>
/// <remarks>
/// A closed hierarchy — the private constructor means no fourth case can be declared elsewhere —
/// so a prepared document always names the origin of its white ink, and the TIFF saver can decide
/// what it will accept by asking rather than by assuming.
/// <para>
/// This replaces a flat <c>ActionInvocationOccurredExactlyOnce</c> flag alongside the branch and
/// action names. Those members were true of every prepared document the Product could produce,
/// and so they read as a guarantee that <i>all</i> valid W1 comes from the signed Action. SCRUM-11101
/// makes that false: an operator may choose to keep white ink the customer's own file arrived
/// with, which is equally valid and has an entirely different provenance. Modelling the origin as
/// a closed choice keeps the honest part of the old guarantee — the saver still refuses a document
/// whose W1 has no positively established origin — without the part that is no longer true.
/// </para>
/// </remarks>
public abstract record PhotoshopWhiteInkProvenance
{
    private PhotoshopWhiteInkProvenance() { }

    /// <summary>Created by the signed production Action, invoked exactly once on this document.</summary>
    public sealed record Generated(
        WhiteUnderbaseBranch Branch,
        string ActionSetName,
        string ActionName) : PhotoshopWhiteInkProvenance;

    /// <summary>
    /// The white ink the customer's own file arrived with, kept rather than regenerated.
    /// </summary>
    /// <param name="CarrierDocumentFullPath">
    /// The managed carrier the retained W1 was read from, so the retention is bound to a file that
    /// was actually observed rather than being a bare marker.
    /// </param>
    /// <param name="CarrierSha256">The carrier's bytes at the moment its W1 was classified.</param>
    /// <remarks>
    /// Deliberately carries no operator decision, no authority record and no classification
    /// contract: SCRUM-11101-A is a feasibility gate, and those belong to the implementation slice
    /// that introduces the decision itself. What this case asserts today is only that the W1 came
    /// in on a named managed carrier — which is exactly what the Retain TIFF smoke needs to prove
    /// the saver boundary, and no more.
    /// </remarks>
    public sealed record Retained(
        string CarrierDocumentFullPath,
        Sha256 CarrierSha256) : PhotoshopWhiteInkProvenance;
}

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
    PhotoshopWhiteInkProvenance Provenance,
    bool OtherDocumentsMayBeOpen,
    Sha256 BackingWorkingSha256);

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
