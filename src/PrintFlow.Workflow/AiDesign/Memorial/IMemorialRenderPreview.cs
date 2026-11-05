using PrintFlow.Domain.AiDesign.Memorial;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Workflow.AiDesign.Memorial;

public interface IMemorialRenderPreview
{
    Task<OperationResult<DecodedPreview>> LoadAsync(MemorialRenderResult expected, CancellationToken ct);
}
