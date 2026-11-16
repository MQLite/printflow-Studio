using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using PrintFlow.Domain.AiDesign;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.AiDesign;

namespace PrintFlow.NativeHost;

public sealed partial class NativeHostSession
{
    // The installation lease permits only one request to reach this connection at a time.
    private PendingSubmission? _submission;

    private OperationResult<NativeMessageEnvelope> AcceptSubmitReady(NativeMessageEnvelope message, AiDesignRequest request)
    {
        if (!IsWorkTrial || _configuration.ChromeProfileDirectory is null || !HasLiveOwner(request))
            return Fail("SubmitNotReady");

        string? tab = message.Payload.GetProperty("tabId").GetString();
        string? conversation = message.Payload.GetProperty("conversationId").GetString();
        if (tab is null || conversation is null || tab != request.TabId || conversation != request.ConversationId)
            return Fail("SubmitPageMismatch");

        // The store repeats this check under its lock before issuing a permit.
        var reservation = AiDesignTransitions.Apply(request, new AiDesignEvent.SubmitReserved(tab, conversation), DateTimeOffset.UtcNow);
        if (reservation.IsFailure)
            return Fail("SubmitNotReady");

        var pending = new PendingSubmission(message, request.Fence, tab, conversation);
        if (Interlocked.CompareExchange(ref _submission, pending, null) is not null)
            return Fail("SubmitAlreadyPending");

        return Reply(request, "Ack", new { stateVersion = request.StateVersion, stage = "SubmitReady" });
    }

    private OperationResult<NativeMessageEnvelope> AcceptSubmitReceipt(NativeMessageEnvelope message)
    {
        if (!_verified)
            return Fail("WorkContractUnverified");

        var pending = Volatile.Read(ref _submission);
        var permit = pending?.CurrentPermit;
        if (pending is null || permit is null || message.RequestId != permit.RequestId ||
            message.AttemptId != permit.AttemptId || message.StateVersion != permit.StateVersion ||
            !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(message.Token), System.Text.Encoding.UTF8.GetBytes(permit.Token)) ||
            !message.Payload.GetProperty("permitId").TryGetGuid(out var id) || id != pending.Id ||
            message.Payload.GetProperty("tabId").GetString() != pending.Tab ||
            message.Payload.GetProperty("conversationId").GetString() != pending.Conversation)
            return Fail("SubmitPermitMismatch");
        if (!message.Payload.TryGetProperty("confirmed", out var confirmed) ||
            confirmed.ValueKind is not (System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False))
            return Fail("SubmitReceiptInvalid");
        Guid? workConversationId = null;
        if (confirmed.GetBoolean())
        {
            if (!message.Payload.TryGetProperty("workConversationId", out var value) ||
                !value.TryGetGuid(out var idValue) || idValue == Guid.Empty)
                return Fail("SubmitReceiptInvalid");
            workConversationId = idValue;
        }
        if (!pending.Receipt.TrySetResult(workConversationId))
            return Fail("SubmitPermitMismatch");

        // Do not read the request here. SubmitOnceAsync owns its lock while awaiting this receipt.
        return Reply(message, "Ack", new { stateVersion = message.StateVersion, stage = "SubmitReceipt" });
    }

    private async Task ProcessSubmissionAsync(Func<NativeMessageEnvelope, CancellationToken, Task<OperationResult<Unit>>> emit, CancellationToken ct)
    {
        var pending = Volatile.Read(ref _submission);
        if (pending is null)
            return;

        try
        {
            var outcome = await _store.SubmitOnceAsync(pending.Fence, async (reserved, token) =>
            {
                if (!HasLiveOwner(reserved))
                    return OperationResult.Fail<Guid?>(FailureCode.AdapterUnavailable, "WorkOwnerUnavailable");

                pending.SetPermit(reserved.Fence);
                var sent = await emit(Reply(reserved, "SubmitPermit", new
                {
                    permitId = pending.Id, tabId = pending.Tab, conversationId = pending.Conversation,
                    expiresAt = DateTimeOffset.UtcNow.AddSeconds(5).ToUnixTimeMilliseconds()
                }), token);
                if (sent.IsFailure)
                    return OperationResult.Fail<Guid?>(sent.Failure);
                var id = await pending.Receipt.Task.WaitAsync(token);
                return id.HasValue ? OperationResult.Ok(id) : OperationResult.Fail<Guid?>(FailureCode.AdapterUnavailable, "SubmitUnconfirmed");
            }, ct);

            NativeMessageEnvelope report;
            if (outcome.IsFailure)
                report = Reply(pending.Ready, "Failure", new { code = "SubmitNotReady" });
            else if (outcome.Value.State == AiDesignState.Submitted)
                report = Reply(outcome.Value, "Ack", new { stateVersion = outcome.Value.StateVersion, stage = "Submitted" });
            else
                report = Reply(pending.Ready, "Failure", new { code = "SubmitUnconfirmed" });

            if ((await emit(report, ct)).IsFailure)
                throw new IOException("Native submit result could not be sent.");
        }
        finally
        {
            Interlocked.CompareExchange(ref _submission, null, pending);
        }
    }

    private bool HasLiveOwner(AiDesignRequest request)
    {
        if (request.OwnerProcessId is not { } processId || request.OwnerProcessStartUtcTicks is not { } start)
            return false;
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited || process.StartTime.ToUniversalTime().Ticks != start) return false;
            string lease = Path.Combine(_installation, "browser.lock");
            RefuseAncestry(lease);
            try
            {
                using var unused = new FileStream(lease, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                return false;
            }
            catch (IOException ex) when ((ex.HResult & 0xFFFF) == 32) // ERROR_SHARING_VIOLATION
            {
                return true;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private sealed class PendingSubmission(NativeMessageEnvelope ready, AiDesignFence fence, string tab, string conversation)
    {
        private AiDesignFence? _permit;
        public NativeMessageEnvelope Ready { get; } = ready;
        public AiDesignFence Fence { get; } = fence;
        public string Tab { get; } = tab;
        public string Conversation { get; } = conversation;
        public Guid Id { get; } = Guid.NewGuid();
        public TaskCompletionSource<Guid?> Receipt { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public AiDesignFence? CurrentPermit => Volatile.Read(ref _permit);
        public void SetPermit(AiDesignFence permit) => Volatile.Write(ref _permit, permit);
    }
}
