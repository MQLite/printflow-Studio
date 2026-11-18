using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Settings;

namespace PrintFlow.Workflow.Ports;

public sealed record ImageProcessingCapability(
    string AdapterId, string AlgorithmVersion, OperationFailure? Unavailable = null);

public sealed record ImageProcessingRequest(
    ImageProcessingMethod Method,
    OperationKind Operation,
    string AlgorithmVersion,
    RevisionId InputRevision,
    Sha256 InputSha256,
    WorkspaceFileRef Input,
    WorkspaceFileRef ExpectedOutput,
    BackgroundRemovalAuthority? BackgroundRemovalAuthority);

/// <summary>A provider for non-desktop image processing. Null capability means the method is not implemented.</summary>
public interface IImageProcessingBackend
{
    ImageProcessingCapability? GetCapability(ImageProcessingMethod method);
    Task<OperationResult<AdapterOutput>> ProcessAsync(ImageProcessingRequest request, CancellationToken cancellationToken);
}
