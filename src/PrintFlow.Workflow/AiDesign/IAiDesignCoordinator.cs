using PrintFlow.Domain.AiDesign;
using PrintFlow.Domain.Results;

namespace PrintFlow.Workflow.AiDesign;

public interface IAiDesignCoordinator
{
    OperationResult<AiDesignPrompt> ComposePrompt(string templateId, IReadOnlyDictionary<string,string> fields, IReadOnlyList<AiDesignAttachment> attachments, string language);
    Task<OperationResult<AiDesignView>> CreateDraftAsync(AiDesignPrompt prompt, CancellationToken ct);
    Task<OperationResult<AiDesignView>> SaveDraftAsync(Guid requestId, AiDesignDraft draft, CancellationToken ct);
    Task<OperationResult<AiDesignView>> AddAttachmentsAsync(Guid requestId, IReadOnlyList<SelectedAiDesignFile> files, CancellationToken ct);
    Task<OperationResult<AiDesignView>> StartAsync(Guid requestId, CancellationToken ct);
    Task<OperationResult<AiDesignView>> StopAsync(Guid requestId, CancellationToken ct);
    Task<OperationResult<AiDesignView>> RetryAsync(Guid requestId, CancellationToken ct);
    Task<OperationResult<AiDesignView>> ImportManualAsync(Guid requestId, string selectedAbsolutePath, CancellationToken ct);
    Task<OperationResult<AiDesignView>> ResumeCandidateAsync(Guid requestId, CancellationToken ct);
    Task<OperationResult<AiDesignView>> ReadAsync(Guid requestId, CancellationToken ct);
    Task<OperationResult<Unit>> RecoverAsync(CancellationToken ct);
    Task<OperationResult<Unit>> RevealAttachmentAsync(Guid requestId, Guid attachmentId, CancellationToken ct);
}
