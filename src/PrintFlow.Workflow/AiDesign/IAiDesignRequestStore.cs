using PrintFlow.Domain.AiDesign;
using PrintFlow.Domain.Results;

namespace PrintFlow.Workflow.AiDesign;

public interface IAiDesignRequestStore
{
    string WorkspaceIdentity { get; }
    Task<OperationResult<AiDesignRequest>> CreateAsync(Guid requestId, AiDesignDraft draft, CancellationToken ct);
    Task<OperationResult<AiDesignRequest>> ReadAsync(Guid requestId, CancellationToken ct);
    Task<OperationResult<AiDesignRequest>> ChangeAsync(AiDesignFence expected, AiDesignEvent change, CancellationToken ct);
    Task<OperationResult<IReadOnlyList<AiDesignRequest>>> ListAsync(CancellationToken ct);
    Task<OperationResult<AiDesignRequest>> SubmitOnceAsync(AiDesignFence expected, Func<CancellationToken, Task<OperationResult<Unit>>> click, CancellationToken ct);
    Task<OperationResult<AiDesignRequest>> EditDraftAsync(AiDesignFence expected, Func<AiDesignRequest, CancellationToken, Task<OperationResult<AiDesignDraft>>> edit, CancellationToken ct);
}
