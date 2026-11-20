using PrintFlow.Domain.AiDesign;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;

namespace PrintFlow.Workflow.AiDesign;

public enum WorkTaskStage { Submitting, AwaitingReturn, AwaitingVerification, Validating, Imported, SubmittedOnly, Cancelled, NeedsAttention, AwaitingFollowUp }
public enum WorkTaskFailure { Unconfirmed, Setup, UploadFailed, ReceiverUnavailable, TimedOut, ValidationFailed, ResultUnavailable, RecoveryDeferred, ImageProcessingSettings }

public sealed record WorkTaskSnapshot(Guid RequestId, Guid AttemptId, long StateVersion,
    Guid? DraftId, string? Title, DateTimeOffset UpdatedAtUtc, WorkTaskStage Stage, Guid? WorkConversationId = null,
    SessionId? ImportedSessionId = null, ImageFormat? Format = null, int? PixelWidth = null, int? PixelHeight = null,
    WorkTaskFailure? Failure = null, bool CanStopReceiving = false, Guid? FollowUpSourceRequestId = null,
    AiDesignProvider Provider = AiDesignProvider.ChatGptWork)
{
    public OpenAiDesignReceipt? OpenAiReceipt { get; init; }
    public int CloudDesignAttempts { get; init; }
    public bool IsVerifyingResult => Stage is WorkTaskStage.AwaitingVerification or WorkTaskStage.Validating;
    public bool HasReturnedImage => IsVerifyingResult || Stage == WorkTaskStage.Imported;
    public bool BlocksNewSubmission => IsActive || CanStopReceiving;
    public bool IsActive => IsVerifyingResult ||
        Stage is WorkTaskStage.Submitting or WorkTaskStage.AwaitingFollowUp or WorkTaskStage.AwaitingReturn;
}

public interface IWorkConversationLauncher
{
    bool Open(Guid conversationId);
}

public interface IWorkTaskQuery
{
    Task<OperationResult<IReadOnlyList<WorkTaskSnapshot>>> ListAsync(CancellationToken ct);
    Task<OperationResult<WorkTaskSnapshot?>> FindAsync(Guid requestId, CancellationToken ct);
}
