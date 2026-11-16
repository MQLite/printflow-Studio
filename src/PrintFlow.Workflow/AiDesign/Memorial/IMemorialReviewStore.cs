using PrintFlow.Domain.AiDesign.Memorial;
using PrintFlow.Domain.Results;

namespace PrintFlow.Workflow.AiDesign.Memorial;

public interface IMemorialReviewStore
{
    /// <summary>Returns OutputMissing only when this exact render has no committed review.</summary>
    Task<OperationResult<MemorialReviewState>> ReadAsync(MemorialRenderRef file, CancellationToken ct);
    Task<OperationResult<MemorialReviewState>> SaveAsync(MemorialReviewState value, long? expectedRevision, CancellationToken ct);
    Task<OperationResult<T>> WithCurrentRevisionAsync<T>(MemorialRenderRef file, long expectedRevision,
        Func<MemorialReviewState, Task<OperationResult<T>>> publish, CancellationToken ct);
}
