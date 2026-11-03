using PrintFlow.Domain.AiDesign;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Workflow.AiDesign.Authoring;

public interface IAiAuthoringAssetPreview
{
    Task<OperationResult<DecodedPreview>> LoadAsync(Guid ownerId, AiDesignAttachment asset, CancellationToken ct);
}
