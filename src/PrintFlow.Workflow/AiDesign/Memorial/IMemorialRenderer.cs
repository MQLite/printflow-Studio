using PrintFlow.Domain.AiDesign.Memorial;
using PrintFlow.Domain.Results;

namespace PrintFlow.Workflow.AiDesign.Memorial;

public interface IMemorialRenderer
{
    Task<OperationResult<MemorialRenderResult>> RenderAsync(MemorialRenderPlan plan, CancellationToken ct);
}
