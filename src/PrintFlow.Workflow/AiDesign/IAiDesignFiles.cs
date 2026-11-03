using PrintFlow.Domain.AiDesign;
using PrintFlow.Domain.Results;

namespace PrintFlow.Workflow.AiDesign;

public static class AiDesignAttachmentLimits
{
    public const int MaximumFiles = 8;
    public const long ImageBytes = 20L * 1024 * 1024, DocumentBytes = 40L * 1024 * 1024, TotalBytes = 80L * 1024 * 1024;
}

public interface IAiDesignAttachments
{
    Task<OperationResult<IReadOnlyList<AiDesignAttachment>>> SnapshotAsync(Guid requestId, IReadOnlyList<SelectedAiDesignFile> selected, IReadOnlyList<AiDesignAttachment> existing, CancellationToken ct);
    Task<OperationResult<Unit>> VerifyAsync(Guid requestId, IReadOnlyList<AiDesignAttachment> attachments, CancellationToken ct);
    // General reference documents retain their existing verification contract. Photo binding
    // additionally requires a supported container on the verified managed byte stream.
    Task<OperationResult<Unit>> VerifyImagesAsync(Guid ownerId, IReadOnlyList<AiDesignAttachment> images, CancellationToken ct) =>
        Task.FromResult(OperationResult.Fail<Unit>(FailureCode.AdapterUnavailable, "Verified image reads are unavailable."));
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
