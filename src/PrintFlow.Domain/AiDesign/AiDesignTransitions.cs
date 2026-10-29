using PrintFlow.Domain.Results;

namespace PrintFlow.Domain.AiDesign;

public static class AiDesignTransitions
{
    public static bool IsRunning(AiDesignState state) => state is >= AiDesignState.Queued and <= AiDesignState.Importing;
    public static bool CanStop(AiDesignState state) => state is >= AiDesignState.Queued and <= AiDesignState.Validating;
    public static OperationResult<AiDesignRequest> Apply(AiDesignRequest r, AiDesignEvent change, DateTimeOffset now)
    {
        AiDesignRequest? next = null;
        if (r.DeadlineUtc is { } deadline && now >= deadline && change is not (AiDesignEvent.StopAccepted or AiDesignEvent.Interrupted or AiDesignEvent.Failed))
            return Reject("AI stage deadline expired.");
        switch (change)
        {
            case AiDesignEvent.DraftSaved d when r.State is AiDesignState.PromptReady or AiDesignState.PromptOnly or AiDesignState.Cancelled or AiDesignState.Interrupted:
                if (r.Candidate is not null || r.ImportedSessionId is not null) return Reject("Candidate-bearing request cannot replace inputs.");
                next = r with { Prompt = FreezePrompt(d.Prompt), Attachments = d.Attachments.ToArray(), NextAttachmentOrder = Math.Max(r.NextAttachmentOrder, d.NextOrder), State = AiDesignState.PromptReady };
                break;
            case AiDesignEvent.Queued q when r.State is AiDesignState.PromptReady or AiDesignState.PromptOnly or AiDesignState.Cancelled or AiDesignState.Interrupted:
                if (q.AttemptId == Guid.Empty || q.AttemptId == r.AttemptId || string.IsNullOrWhiteSpace(q.Token) || q.Token == r.Token || q.ConnectionId == Guid.Empty || r.ImportedSessionId is not null)
                    return Reject("New attempt identity required.");
                next = r with { State = AiDesignState.Queued, AttemptId = q.AttemptId, Token = q.Token,
                    OwnerProcessId = q.OwnerProcessId, OwnerProcessStartUtcTicks = q.OwnerProcessStartUtcTicks, ConnectionId = q.ConnectionId,
                    UploadReceipts = [], SubmissionReservedAtUtc = null, TabId = null, ConversationId = null, Download = null,
                    Candidate = null, RetainedCandidates = r.Candidate is null ? r.RetainedCandidates : r.RetainedCandidates.Append(r.Candidate).ToArray(),
                    Failure = null, DeadlineUtc = now.AddMinutes(r.Attachments.Count > 0 ? 5 : 10) };
                break;
            case AiDesignEvent.UploadStarted u when r.State == AiDesignState.Queued:
                if (!Identity(u.TabId, u.ConversationId)) return Reject("Conversation identity required.");
                next = r with { State = r.Attachments.Count > 0 ? AiDesignState.Uploading : AiDesignState.Queued, TabId = u.TabId, ConversationId = u.ConversationId };
                break;
            case AiDesignEvent.UploadConfirmed u when r.State == AiDesignState.Uploading:
                var receipt = u.Receipt;
                if (receipt.AttemptId != r.AttemptId || receipt.ConversationId != r.ConversationId || string.IsNullOrWhiteSpace(receipt.EvidenceKey)
                    || !r.Attachments.Any(a => a.Id == receipt.AttachmentId) || receipt.Status != AiDesignUploadStatus.Uploaded
                    || r.UploadReceipts.Any(a => a.AttachmentId == receipt.AttachmentId)) return Reject("Upload receipt identity mismatch.");
                next = r with { UploadReceipts = r.UploadReceipts.Append(receipt).ToArray() };
                break;
            case AiDesignEvent.SubmitReserved s when r.State is AiDesignState.Queued or AiDesignState.Uploading:
                if (r.SubmissionReservedAtUtc is not null || !Identity(s.TabId, s.ConversationId)
                    || (r.Attachments.Count > 0 && (r.State != AiDesignState.Uploading || r.TabId != s.TabId || r.ConversationId != s.ConversationId
                        || !r.Attachments.All(a => r.UploadReceipts.Any(u => u.AttachmentId == a.Id && u.AttemptId == r.AttemptId
                            && u.ConversationId == s.ConversationId && u.Status == AiDesignUploadStatus.Uploaded))))) return Reject("All required attachments must be uploaded before one submit reservation.");
                next = r with { SubmissionReservedAtUtc = now, TabId = s.TabId, ConversationId = s.ConversationId };
                break;
            case AiDesignEvent.SubmitConfirmed when r.State is AiDesignState.Queued or AiDesignState.Uploading && r.SubmissionReservedAtUtc is not null:
                next = r with { State = AiDesignState.Submitted, DeadlineUtc = now.AddMinutes(10) };
                break;
            case AiDesignEvent.GenerationStarted when r.State == AiDesignState.Submitted:
                next = r with { State = AiDesignState.Generating };
                break;
            case AiDesignEvent.DownloadRegistered d when r.State == AiDesignState.Generating:
                if (!Matches(r, d.Download) || d.Download.IsComplete || d.Download.DownloadId < 0 || string.IsNullOrWhiteSpace(d.Download.ResultId)) return Reject("Download identity mismatch.");
                next = r with { State = AiDesignState.Receiving, Download = d.Download, DeadlineUtc = now.AddMinutes(2) };
                break;
            case AiDesignEvent.DownloadCompleted d when r.State == AiDesignState.Receiving:
                if (r.Download is null || !Matches(r, d.Download) || !d.Download.IsComplete || d.Download.DownloadId != r.Download.DownloadId
                    || d.Download.ResultId != r.Download.ResultId) return Reject("Unregistered download.");
                next = r with { Download = d.Download };
                break;
            case AiDesignEvent.ValidationStarted when r.State == AiDesignState.Receiving && r.Download?.IsComplete == true:
                next = r with { State = AiDesignState.Validating, DeadlineUtc = null };
                break;
            case AiDesignEvent.CandidatePublished c when r.State == AiDesignState.Validating && c.Candidate.File.RequestId == r.RequestId:
                next = r with { Candidate = c.Candidate };
                break;
            case AiDesignEvent.ImportStarted when r.State == AiDesignState.Validating && r.Candidate is not null:
                next = r with { State = AiDesignState.Importing, DeadlineUtc = null };
                break;
            case AiDesignEvent.ImportCompleted i when r.State == AiDesignState.Importing && i.SessionId.Value != Guid.Empty:
                next = r with { State = AiDesignState.Imported, ImportedSessionId = i.SessionId, Token = NewToken(), OwnerProcessId = null, OwnerProcessStartUtcTicks = null };
                break;
            case AiDesignEvent.Failed f when IsRunning(r.State) || r.State == AiDesignState.PromptReady:
                if (string.IsNullOrWhiteSpace(f.Code) || (f.ImportFailure && r.State != AiDesignState.Importing)) return Reject("Failure stage mismatch.");
                next = r with { State = r.State == AiDesignState.Importing ? AiDesignState.ImportFailed : AiDesignState.PromptOnly,
                    ImportedSessionId = f.SessionId ?? r.ImportedSessionId, Token = NewToken(), DeadlineUtc = null,
                    Failure = new(f.Code, r.State, [], r.LastSuccessfulStage), OwnerProcessId = null, OwnerProcessStartUtcTicks = null };
                break;
            case AiDesignEvent.StopAccepted when CanStop(r.State):
                next = r with { State = AiDesignState.Cancelled, Token = NewToken(), DeadlineUtc = null,
                    Failure = new("Cancelled", r.State, [], r.LastSuccessfulStage), OwnerProcessId = null, OwnerProcessStartUtcTicks = null };
                break;
            case AiDesignEvent.Interrupted when IsRunning(r.State):
                next = r with { State = AiDesignState.Interrupted, Token = NewToken(), DeadlineUtc = null,
                    Failure = new("Interrupted", r.State, [], r.LastSuccessfulStage), OwnerProcessId = null, OwnerProcessStartUtcTicks = null };
                break;
            case AiDesignEvent.RemoteStopConfirmed when r.State == AiDesignState.Cancelled && r.Failure is not null:
                next = r with { Failure = r.Failure with { RemoteStopConfirmed = true } };
                break;
        }
        if (next is null) return Reject("AI event is not allowed in this stage.");
        return next with { StateVersion = checked(r.StateVersion + 1), UpdatedAtUtc = now,
            LastSuccessfulStage = next.Failure is null ? next.State : r.LastSuccessfulStage };
    }
    public static AiDesignPrompt FreezePrompt(AiDesignPrompt p) => p with
    { Fields = new Dictionary<string, string>(p.Fields, StringComparer.Ordinal), AttachmentReferences = p.AttachmentReferences.ToArray() };
    public static string NewToken() => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
    private static bool Identity(string tab, string conversation) => !string.IsNullOrWhiteSpace(tab) && !string.IsNullOrWhiteSpace(conversation);
    private static bool Matches(AiDesignRequest r, AiDesignDownload d) => d.RequestId == r.RequestId && d.AttemptId == r.AttemptId && d.TabId == r.TabId && d.ConversationId == r.ConversationId;
    private static OperationResult<AiDesignRequest> Reject(string detail) => OperationResult.Fail<AiDesignRequest>(FailureCode.PreconditionNotMet, detail);
}
