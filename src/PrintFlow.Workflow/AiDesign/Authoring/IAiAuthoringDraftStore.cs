using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Domain.Results;

namespace PrintFlow.Workflow.AiDesign.Authoring;

public interface IAiAuthoringDraftStore
{
    Task<OperationResult<AiAuthoringDraftListing>> ListAsync(CancellationToken ct);
    Task<OperationResult<AiAuthoringDraft>> ReadAsync(Guid id, CancellationToken ct);
    /// <summary>Newest first, at most 50 committed revisions, reached only from the current pointer.</summary>
    Task<OperationResult<IReadOnlyList<AiAuthoringDraft>>> ReadHistoryAsync(Guid id, CancellationToken ct);
    Task<OperationResult<AiAuthoringDraft>> SaveAsync(AiAuthoringDraft value, long? expectedRevision, CancellationToken ct);
    /// <summary>Runs a publication to a different owner or store while this exact draft revision is current.</summary>
    Task<OperationResult<T>> WithCurrentRevisionAsync<T>(Guid id, long expectedRevision,
        Func<AiAuthoringDraft, Task<OperationResult<T>>> publish, CancellationToken ct);
}
