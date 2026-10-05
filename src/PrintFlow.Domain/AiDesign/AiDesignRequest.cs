using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;

namespace PrintFlow.Domain.AiDesign;

public enum AiDesignState { PromptReady, Queued, Uploading, Submitted, Generating, Receiving, Validating, Importing, Imported, PromptOnly, Cancelled, Interrupted, ImportFailed }
public enum AiDesignUploadStatus { Pending, Uploading, Uploaded, Failed, Cancelled }
public sealed record AiDesignFence(Guid RequestId, Guid AttemptId, long StateVersion, string Token);
public sealed record AiDesignFileRef(Guid RequestId, string RelativePath);
public sealed record AiDesignAttachment(Guid Id, int Order, string OriginalName, string UploadName, string Purpose,
    string MediaType, long ByteLength, Sha256 Sha256, AiDesignFileRef Snapshot);
public sealed record AiDesignUploadReceipt(Guid AttachmentId, Guid AttemptId, string ConversationId,
    AiDesignUploadStatus Status, string EvidenceKey);
public sealed record AiDesignPrompt(string TemplateId, int TemplateVersion, IReadOnlyDictionary<string, string> Fields,
    string Text, IReadOnlyList<Guid> AttachmentReferences, string HandoffInstructions);
public sealed record AiDesignCandidate(AiDesignFileRef File, FileFacts Facts);
public sealed record AiDesignDownload(Guid RequestId, Guid AttemptId, string TabId, string ConversationId,
    string ResultId, long DownloadId, string AbsolutePath, bool IsComplete);
public sealed record AiDesignFailure(string Code, AiDesignState Stage, IReadOnlyList<string> DetailArguments,
    AiDesignState LastSuccessfulStage, bool RemoteStopConfirmed = false);
public sealed record AiDesignRequest
{
    public required Guid RequestId { get; init; }
    public required string WorkspaceIdentity { get; init; }
    public required AiDesignPrompt Prompt { get; init; }
    public IReadOnlyList<AiDesignAttachment> Attachments { get; init; } = [];
    public IReadOnlyList<AiDesignUploadReceipt> UploadReceipts { get; init; } = [];
    public int NextAttachmentOrder { get; init; } = 1;
    public AiDesignState State { get; init; } = AiDesignState.PromptReady;
    public AiDesignState LastSuccessfulStage { get; init; } = AiDesignState.PromptReady;
    public Guid AttemptId { get; init; }
    public long StateVersion { get; init; }
    public required string Token { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
    public DateTimeOffset? DeadlineUtc { get; init; }
    public DateTimeOffset? SubmissionReservedAtUtc { get; init; }
    public int? OwnerProcessId { get; init; }
    public long? OwnerProcessStartUtcTicks { get; init; }
    public Guid? ConnectionId { get; init; }
    public string? TabId { get; init; }
    public string? ConversationId { get; init; }
    public AiDesignDownload? Download { get; init; }
    public AiDesignCandidate? Candidate { get; init; }
    public IReadOnlyList<AiDesignCandidate> RetainedCandidates { get; init; } = [];
    public SessionId? ImportedSessionId { get; init; }
    public AiDesignFailure? Failure { get; init; }
    public AiDesignFence Fence => new(RequestId, AttemptId, StateVersion, Token);
}

public abstract record AiDesignEvent
{
    public sealed record DraftSaved(AiDesignPrompt Prompt, IReadOnlyList<AiDesignAttachment> Attachments, int NextOrder) : AiDesignEvent;
    public sealed record Queued(Guid AttemptId, string Token, int OwnerProcessId, long OwnerProcessStartUtcTicks, Guid ConnectionId) : AiDesignEvent;
    public sealed record UploadStarted(string TabId, string ConversationId) : AiDesignEvent;
    public sealed record UploadConfirmed(AiDesignUploadReceipt Receipt) : AiDesignEvent;
    public sealed record SubmitReserved(string TabId, string ConversationId) : AiDesignEvent;
    public sealed record SubmitConfirmed : AiDesignEvent;
    public sealed record GenerationStarted : AiDesignEvent;
    public sealed record DownloadRegistered(AiDesignDownload Download) : AiDesignEvent;
    public sealed record DownloadCompleted(AiDesignDownload Download) : AiDesignEvent;
    public sealed record ValidationStarted : AiDesignEvent;
    public sealed record CandidatePublished(AiDesignCandidate Candidate) : AiDesignEvent;
    public sealed record ImportStarted : AiDesignEvent;
    public sealed record ImportCompleted(SessionId SessionId) : AiDesignEvent;
    public sealed record Failed(string Code, bool ImportFailure = false, SessionId? SessionId = null) : AiDesignEvent;
    public sealed record StopAccepted : AiDesignEvent;
    public sealed record RemoteStopConfirmed : AiDesignEvent;
    public sealed record Interrupted : AiDesignEvent;
}
