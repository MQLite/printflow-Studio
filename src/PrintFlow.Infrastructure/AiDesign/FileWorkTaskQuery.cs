using PrintFlow.Domain.AiDesign;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.AiDesign;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Infrastructure.AiDesign;

public sealed class FileWorkTaskQuery(FileAiDesignRequestStore store, ISessionService sessions, IWorkResultProgressReader progress)
    : IWorkTaskQuery
{
    public async Task<OperationResult<IReadOnlyList<WorkTaskSnapshot>>> ListAsync(CancellationToken ct)
    {
        var listed = await store.ListSnapshotsAsync(ct).ConfigureAwait(false);
        if (listed.IsFailure) return OperationResult.Fail<IReadOnlyList<WorkTaskSnapshot>>(listed.Failure);
        var tasks = new List<WorkTaskSnapshot>();
        foreach (var request in listed.Value)
        {
            ct.ThrowIfCancellationRequested();
            if (await ProjectAsync(request, ct).ConfigureAwait(false) is { } task) tasks.Add(task);
        }
        return OperationResult.Ok<IReadOnlyList<WorkTaskSnapshot>>(tasks
            .OrderByDescending(task => task.UpdatedAtUtc).ThenBy(task => task.RequestId).ToArray());
    }

    public async Task<OperationResult<WorkTaskSnapshot?>> FindAsync(Guid requestId, CancellationToken ct)
    {
        var read = await store.FindSnapshotAsync(requestId, ct).ConfigureAwait(false);
        if (read.IsFailure) return OperationResult.Fail<WorkTaskSnapshot?>(read.Failure);
        return OperationResult.Ok(read.Value is { } request ? await ProjectAsync(request, ct).ConfigureAwait(false) : null);
    }

    private async Task<WorkTaskSnapshot?> ProjectAsync(AiDesignRequest request, CancellationToken ct)
    {
        var conversation = request.WorkConversationId is { } id && id != Guid.Empty ? id : (Guid?)null;
        var ticket = store.HasWorkResultTicket(request.RequestId);
        if (request.WorkDraft is null && conversation is null && !ticket) return null;

        var task = new WorkTaskSnapshot(request.RequestId, request.AttemptId, request.StateVersion, request.WorkDraft?.DraftId,
            request.WorkDraft?.Title, request.UpdatedAtUtc, WorkTaskStage.NeedsAttention, conversation,
            FollowUpSourceRequestId: request.WorkFollowUp?.RequestId, Provider: request.Provider);
        if (request.State == AiDesignState.Cancelled) return task with { Stage = WorkTaskStage.Cancelled };
        if (request.State == AiDesignState.Imported)
        {
            if (request.ImportedSessionId is not { } session || request.Candidate is not { } candidate ||
                (await sessions.LoadAsync(session, ct).ConfigureAwait(false)).IsFailure)
                return task with { Failure = WorkTaskFailure.ResultUnavailable };

            var facts = candidate.Facts;
            return task with { Stage = WorkTaskStage.Imported, ImportedSessionId = session,
                Format = facts.Format, PixelWidth = facts.PixelWidth, PixelHeight = facts.PixelHeight,
                OpenAiReceipt = request.OpenAiReceipt, CloudDesignAttempts = request.CloudReceipt?.Attempts.Length ?? 0 };
        }
        if (request.Failure?.Code == "TrialResultHandoffUnavailable" && conversation is not null && !ticket)
            return task with { Stage = WorkTaskStage.SubmittedOnly };
        if ((ticket || request.Failure?.Code == "WorkResultPending") && AiDesignTransitions.CanResumeWorkResult(request))
        {
            var awaiting = request.State == AiDesignState.AwaitingWorkFollowUp ?
                WorkTaskStage.AwaitingFollowUp : WorkTaskStage.AwaitingReturn;
            task = task with { CanStopReceiving = request.State == AiDesignState.AwaitingWorkFollowUp ||
                request.Failure?.Code is "WorkResultPending" or "Interrupted" };
            var observed = progress.Find(request.RequestId);
            if (observed is not null && observed.AttemptId == request.AttemptId && observed.StateVersion == request.StateVersion)
                return observed.Stage switch
                {
                    WorkResultStage.AwaitingVerification => task with { Stage = WorkTaskStage.AwaitingVerification },
                    WorkResultStage.Validating => task with { Stage = WorkTaskStage.Validating },
                    WorkResultStage.NeedsAttention => task with { Failure = observed.Problem switch
                    {
                        WorkResultProblem.TimedOut => WorkTaskFailure.TimedOut,
                        WorkResultProblem.ValidationFailed => WorkTaskFailure.ValidationFailed,
                        WorkResultProblem.RecoveryDeferred => WorkTaskFailure.RecoveryDeferred,
                        WorkResultProblem.ImageProcessingSettings => WorkTaskFailure.ImageProcessingSettings,
                        _ => WorkTaskFailure.ReceiverUnavailable
                    }},
                    _ => task with { Stage = awaiting }
                };
            return task with { Stage = awaiting };
        }
        if (request.State == AiDesignState.Submitted && conversation is not null && ticket)
            return task with { Stage = WorkTaskStage.AwaitingReturn };
        if (AiDesignTransitions.IsRunning(request.State) && LoopbackWorkResultImport.IsOwnerAlive(request))
            return task with { Stage = WorkTaskStage.Submitting };
        return task with { Failure = request.Failure?.Code switch
        {
            "WorkUploadFailed" or "WorkUploadReceiptInvalid" => WorkTaskFailure.UploadFailed,
            "WorkReceiverUnavailable" => WorkTaskFailure.ReceiverUnavailable,
            "BridgeUnavailable" or "WorkModeUnavailable" => WorkTaskFailure.Setup,
            _ => WorkTaskFailure.Unconfirmed
        }};
    }

}
