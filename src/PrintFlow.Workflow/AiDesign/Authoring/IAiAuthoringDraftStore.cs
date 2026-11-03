using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Domain.Results;

namespace PrintFlow.Workflow.AiDesign.Authoring;

public interface IAiAuthoringDraftStore
{
    Task<OperationResult<AiAuthoringDraft>> ReadAsync(Guid id, CancellationToken ct);
    /// <summary>Newest first, at most 50 committed revisions, reached only from the current pointer.</summary>
    Task<OperationResult<IReadOnlyList<AiAuthoringDraft>>> ReadHistoryAsync(Guid id, CancellationToken ct);
    Task<OperationResult<AiAuthoringDraft>> SaveAsync(AiAuthoringDraft value, long? expectedRevision, CancellationToken ct);
}
