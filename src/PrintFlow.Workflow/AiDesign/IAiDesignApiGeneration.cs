using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Domain.Results;

namespace PrintFlow.Workflow.AiDesign;

public interface IAiDesignApiGeneration
{
    bool UsesCloudReview => false;
    OperationFailure? GetUnavailable();
    Task<OperationResult<Guid>> GenerateAsync(AiAuthoringDraft draft, CancellationToken cancellationToken);
}
