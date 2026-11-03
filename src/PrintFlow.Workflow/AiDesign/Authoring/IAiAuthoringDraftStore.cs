using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Domain.Results;

namespace PrintFlow.Workflow.AiDesign.Authoring;

public interface IAiAuthoringDraftStore
{
    Task<OperationResult<AiAuthoringDraft>> ReadAsync(Guid id, CancellationToken ct);
    Task<OperationResult<AiAuthoringDraft>> SaveAsync(AiAuthoringDraft value, long? expectedRevision, CancellationToken ct);
}
