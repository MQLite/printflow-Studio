using PrintFlow.Domain.AiDesign;
using PrintFlow.Domain.Results;

namespace PrintFlow.Workflow.AiDesign;

public interface IAiDesignAttachments
{
    Task<OperationResult<IReadOnlyList<AiDesignAttachment>>> SnapshotAsync(Guid requestId, IReadOnlyList<SelectedAiDesignFile> selected, IReadOnlyList<AiDesignAttachment> existing, CancellationToken ct);
    Task<OperationResult<Unit>> VerifyAsync(Guid requestId, IReadOnlyList<AiDesignAttachment> attachments, CancellationToken ct);
}
public interface IAiDesignReceiver
{
    Task<OperationResult<AiDesignCandidate>> ReceiveAsync(AiDesignFence fence, AiDesignDownload download, CancellationToken ct);
    Task<OperationResult<AiDesignCandidate>> ReceiveManualAsync(AiDesignFence fence, string selectedAbsolutePath, CancellationToken ct);
}
public interface IAiDesignCandidateResolver
{
    Task<OperationResult<string>> ResolveVerifiedAsync(Guid requestId, AiDesignCandidate candidate, CancellationToken ct);
}
public interface IAiDesignAttachmentShell
{
    Task<OperationResult<Unit>> RevealAsync(Guid requestId, AiDesignAttachment attachment, CancellationToken ct);
}
