using PrintFlow.Domain.AiDesign;
using PrintFlow.Domain.Results;

namespace PrintFlow.Workflow.AiDesign;

public sealed record AiDesignDraft(AiDesignPrompt Prompt, IReadOnlyList<AiDesignAttachment> Attachments);
public sealed record SelectedAiDesignFile(string AbsolutePath, string Purpose);
public sealed record AiDesignRequestView(Guid RequestId, Guid AttemptId, long StateVersion, AiDesignPrompt Prompt,
    IReadOnlyList<AiDesignAttachment> Attachments, IReadOnlyList<AiDesignUploadReceipt> UploadReceipts, AiDesignState State,
    AiDesignState LastSuccessfulStage, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc,
    AiDesignCandidate? Candidate, PrintFlow.Domain.Ids.SessionId? ImportedSessionId, AiDesignFailure? Failure);
public sealed record AiDesignView(AiDesignRequestView Request, bool CanEditAttachments, bool CanStartAutomatically,
    bool CanStop, string? BridgeUnavailableReason, string ManualHandoffText);
public sealed record AiDesignBridgeReadiness(bool Available, int ProtocolVersion, string HostVersion,
    string ExtensionOrigin, string WorkspaceIdentity, string? Reason, Guid ConnectionId = default);
public interface IAiDesignBrowserLease
{
    OperationResult<IDisposable> TryAcquire(Guid requestId);
}
