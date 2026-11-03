using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Domain.Results;

namespace PrintFlow.Workflow.AiDesign.Authoring;

public interface IAiTemplateLibraryStore
{
    Task<OperationResult<AiTemplateLibrary>> ReadAsync(CancellationToken ct);
    Task<OperationResult<AiTemplateLibrary>> SaveAsync(AiTemplateLibrary value, long expectedRevision, CancellationToken ct);
}
