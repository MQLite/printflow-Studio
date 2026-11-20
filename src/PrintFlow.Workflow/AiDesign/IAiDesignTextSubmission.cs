using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Workflow.AiDesign;

/// <summary>Submits the saved prompt and its required attachments to the configured Work trial.</summary>
public interface IAiDesignTextSubmission
{
    Task<OperationResult<AiDesignWorkSubmission>> SubmitAsync(AiAuthoringDraft draft, CancellationToken cancellationToken);
    Task<OperationResult<Unit>> StopReceivingAsync(Guid requestId, CancellationToken cancellationToken);
    Task<OperationResult<AiDesignWorkSubmission>> SubmitFollowUpAsync(Guid sourceRequestId, string prompt, CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult.Fail<AiDesignWorkSubmission>(FailureCode.AdapterUnavailable, "WorkFollowUpUnavailable"));
}

public sealed record AiDesignWorkSubmission(Guid RequestId, Guid WorkConversationId, Task<OperationResult<SessionView>>? Result);
