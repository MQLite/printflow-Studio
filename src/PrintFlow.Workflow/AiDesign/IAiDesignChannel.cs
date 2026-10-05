using PrintFlow.Domain.AiDesign;
using PrintFlow.Domain.Results;

namespace PrintFlow.Workflow.AiDesign;

public interface IAiDesignChannel
{
    Task<OperationResult<AiDesignBridgeReadiness>> CheckAsync(CancellationToken ct);
    Task<OperationResult<Unit>> EnqueueAsync(AiDesignRequest request, CancellationToken ct);
    Task<OperationResult<Unit>> StopAsync(AiDesignFence fence, CancellationToken ct);
}
