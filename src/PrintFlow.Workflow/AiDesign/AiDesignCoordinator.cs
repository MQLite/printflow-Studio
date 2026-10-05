using System.Diagnostics;
using PrintFlow.Domain.AiDesign;
using PrintFlow.Domain.Results;

namespace PrintFlow.Workflow.AiDesign;

/// <summary>Application-owned local orchestration. Browser parsing and session creation remain separate ports.</summary>
public sealed class AiDesignCoordinator(IAiDesignRequestStore store, IAiDesignChannel channel, IAiDesignBrowserLease lease, TimeProvider? clock = null, TimeSpan? bridgeCallTimeout = null) : IAiDesignCoordinator, IDisposable
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly TimeSpan _bridgeCallTimeout = bridgeCallTimeout ?? TimeSpan.FromSeconds(15);
    private readonly SemaphoreSlim _serial = new(1, 1);
    private readonly Dictionary<Guid, IDisposable> _leases = [];
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;
    public OperationResult<AiDesignPrompt> ComposePrompt(string templateId, IReadOnlyDictionary<string,string> fields, IReadOnlyList<AiDesignAttachment> attachments, string language)
        => OperationResult.Fail<AiDesignPrompt>(FailureCode.AdapterUnavailable, "Templates unavailable.");
    public Task<OperationResult<AiDesignView>> CreateDraftAsync(AiDesignPrompt prompt, CancellationToken ct) => Serial(async () =>
        await Project(await store.CreateAsync(Guid.NewGuid(), new(prompt, []), ct), ct), ct);
    public Task<OperationResult<AiDesignView>> SaveDraftAsync(Guid requestId, AiDesignDraft draft, CancellationToken ct) => Serial(async () =>
    {
        var r = await store.ReadAsync(requestId, ct); if (r.IsFailure) return OperationResult.Fail<AiDesignView>(r.Failure);
        return await Project(await store.ChangeAsync(r.Value.Fence, new AiDesignEvent.DraftSaved(draft.Prompt, draft.Attachments, r.Value.NextAttachmentOrder), ct), ct);
    }, ct);
    public Task<OperationResult<AiDesignView>> AddAttachmentsAsync(Guid requestId, IReadOnlyList<SelectedAiDesignFile> files, CancellationToken ct) => Unavailable("Attachment snapshots unavailable.");
    public Task<OperationResult<AiDesignView>> StartAsync(Guid requestId, CancellationToken ct) => Serial(async () =>
    {
        var read = await store.ReadAsync(requestId, ct); if (read.IsFailure) return OperationResult.Fail<AiDesignView>(read.Failure);
        var r = read.Value;
        if (r.State is not (AiDesignState.PromptReady or AiDesignState.PromptOnly or AiDesignState.Cancelled or AiDesignState.Interrupted) || r.ImportedSessionId is not null)
            return OperationResult.Fail<AiDesignView>(FailureCode.PreconditionNotMet, "Request cannot start automatically in this stage.");
        if (r.Prompt.AttachmentReferences.Any(id => !r.Attachments.Any(a => a.Id == id))) return OperationResult.Fail<AiDesignView>(FailureCode.PreconditionNotMet, "Missing attachment reference.");
        var readiness = await CheckBridge(ct);
        if (readiness.IsFailure || !readiness.Value.Available || readiness.Value.ProtocolVersion != 1 || readiness.Value.WorkspaceIdentity != store.WorkspaceIdentity || readiness.Value.ConnectionId == Guid.Empty)
        {
            if (r.State != AiDesignState.PromptReady) return await Project(read, ct);
            return await Project(await store.ChangeAsync(r.Fence, new AiDesignEvent.Failed("BridgeUnavailable"), ct), ct);
        }
        var occupied = lease.TryAcquire(requestId); if (occupied.IsFailure) return OperationResult.Fail<AiDesignView>(occupied.Failure);
        var queued = await store.ChangeAsync(r.Fence, new AiDesignEvent.Queued(Guid.NewGuid(), AiDesignTransitions.NewToken(), Environment.ProcessId,
            Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks, readiness.Value.ConnectionId), ct);
        if (queued.IsFailure) { occupied.Value.Dispose(); return OperationResult.Fail<AiDesignView>(queued.Failure); }
        _leases.Add(requestId, occupied.Value);
        OperationResult<Unit> result;
        using var enqueueBound = CancellationTokenSource.CreateLinkedTokenSource(ct); enqueueBound.CancelAfter(_bridgeCallTimeout);
        try { result = await channel.EnqueueAsync(queued.Value, enqueueBound.Token).WaitAsync(enqueueBound.Token); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            var latest = await store.ReadAsync(requestId, CancellationToken.None);
            var interrupted = latest.IsSuccess ? await store.ChangeAsync(latest.Value.Fence, new AiDesignEvent.Interrupted(), CancellationToken.None) : latest;
            Release(requestId); return await Project(interrupted, CancellationToken.None);
        }
        if (result.IsFailure)
        {
            var latest = await store.ReadAsync(requestId, CancellationToken.None);
            var failed = latest.IsSuccess ? await store.ChangeAsync(latest.Value.Fence, new AiDesignEvent.Failed("EnqueueFailed"), CancellationToken.None) : latest;
            Release(requestId); return await Project(failed, CancellationToken.None);
        }
        _ = MonitorAsync(requestId, _lifetime.Token);
        return await Project(queued, CancellationToken.None);
    }, ct);
    public Task<OperationResult<AiDesignView>> StopAsync(Guid requestId, CancellationToken ct) => Serial(async () =>
    {
        var read = await store.ReadAsync(requestId, ct); if (read.IsFailure) return OperationResult.Fail<AiDesignView>(read.Failure);
        var cancelled = await store.ChangeAsync(read.Value.Fence, new AiDesignEvent.StopAccepted(), ct);
        if (cancelled.IsFailure) return OperationResult.Fail<AiDesignView>(cancelled.Failure);
        // Local authority is revoked first. A remote stop result does not imply files were withdrawn.
        using var stopBound = CancellationTokenSource.CreateLinkedTokenSource(ct); stopBound.CancelAfter(_bridgeCallTimeout);
        try
        {
            var stopped = await channel.StopAsync(read.Value.Fence, stopBound.Token).WaitAsync(stopBound.Token);
            if (stopped.IsSuccess) cancelled = await store.ChangeAsync(cancelled.Value.Fence, new AiDesignEvent.RemoteStopConfirmed(), CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { /* local cancellation remains authoritative; remote state is unconfirmed */ }
        finally { Release(requestId); }
        return await Project(cancelled, CancellationToken.None);
    }, ct);
    public Task<OperationResult<AiDesignView>> RetryAsync(Guid requestId, CancellationToken ct) => StartAsync(requestId, ct);
    public Task<OperationResult<AiDesignView>> ImportManualAsync(Guid requestId, string selectedAbsolutePath, CancellationToken ct)
        => Unavailable("AI receiver/importer unavailable until the gated integration is implemented.");
    public Task<OperationResult<AiDesignView>> ResumeCandidateAsync(Guid requestId, CancellationToken ct)
        => Unavailable("AI receiver/importer unavailable until the gated integration is implemented.");
    public Task<OperationResult<AiDesignView>> ReadAsync(Guid requestId, CancellationToken ct) => Serial(async () =>
    {
        var r = await store.ReadAsync(requestId, ct);
        if (r.IsSuccess && AiDesignTransitions.CanStop(r.Value.State) && r.Value.DeadlineUtc is { } deadline && _clock.GetUtcNow() >= deadline)
            r = await store.ChangeAsync(r.Value.Fence, new AiDesignEvent.Failed("Timeout"), ct);
        if (r.IsSuccess && !AiDesignTransitions.IsRunning(r.Value.State)) Release(requestId);
        return await Project(r, ct);
    }, ct);
    public async Task<OperationResult<Unit>> RecoverAsync(CancellationToken ct)
    {
        await _serial.WaitAsync(ct);
        try
        {
            var list = await store.ListAsync(ct); if (list.IsFailure) return OperationResult.Fail<Unit>(list.Failure);
            foreach (var r in list.Value)
            {
                if (!AiDesignTransitions.IsRunning(r.State) || OwnerIsAlive(r)) continue;
                // Future transactional importer must first reconcile its unique DB association before this step.
                var changed = await store.ChangeAsync(r.Fence, new AiDesignEvent.Interrupted(), ct);
                if (changed.IsFailure) return OperationResult.Fail<Unit>(changed.Failure);
            }
            return OperationResult.Ok();
        }
        finally { _serial.Release(); }
    }
    public Task<OperationResult<Unit>> RevealAttachmentAsync(Guid requestId, Guid attachmentId, CancellationToken ct)
        => Task.FromResult(OperationResult.Fail<Unit>(FailureCode.AdapterUnavailable, "Attachment shell unavailable."));
    private async Task<OperationResult<AiDesignView>> Project(OperationResult<AiDesignRequest> result, CancellationToken ct)
    {
        if (result.IsFailure) return OperationResult.Fail<AiDesignView>(result.Failure);
        var r = result.Value; var readiness = await CheckBridge(ct);
        bool available = readiness.IsSuccess && readiness.Value.Available && readiness.Value.ProtocolVersion == 1
            && readiness.Value.WorkspaceIdentity == store.WorkspaceIdentity && readiness.Value.ConnectionId != Guid.Empty;
        bool editable = !AiDesignTransitions.IsRunning(r.State) && r.ImportedSessionId is null && r.Candidate is null && r.State != AiDesignState.ImportFailed;
        var view = new AiDesignRequestView(r.RequestId, r.AttemptId, r.StateVersion, AiDesignTransitions.FreezePrompt(r.Prompt), r.Attachments.ToArray(), r.UploadReceipts.ToArray(),
            r.State, r.LastSuccessfulStage, r.CreatedAtUtc, r.UpdatedAtUtc, r.Candidate, r.ImportedSessionId, r.Failure);
        string handoff = r.Prompt.Text + "\n\n" + r.Prompt.HandoffInstructions;
        if (r.Attachments.Count > 0) handoff += "\nUpload these required files manually before generating:\n" + string.Join("\n", r.Attachments.OrderBy(a => a.Order).Select(a => a.UploadName + ": " + a.Purpose));
        return new AiDesignView(view, editable, available && editable, AiDesignTransitions.CanStop(r.State), available ? null : readiness.IsSuccess ? readiness.Value.Reason ?? "BridgeUnavailable" : "BridgeUnavailable", handoff);
    }
    private async Task<OperationResult<AiDesignView>> Serial(Func<Task<OperationResult<AiDesignView>>> action, CancellationToken ct)
    {
        await _serial.WaitAsync(ct);
        try { if (_disposed) return OperationResult.Fail<AiDesignView>(FailureCode.AdapterUnavailable, "Coordinator closed."); return await action(); }
        finally { _serial.Release(); }
    }
    private async Task<OperationResult<AiDesignBridgeReadiness>> CheckBridge(CancellationToken ct)
    {
        try { return await channel.CheckAsync(ct).WaitAsync(_bridgeCallTimeout, ct); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { return OperationResult.Fail<AiDesignBridgeReadiness>(FailureCode.AdapterUnavailable, "Bridge check unavailable."); }
    }
    private async Task MonitorAsync(Guid id, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), _clock, ct);
                var read = await ReadAsync(id, ct); if (read.IsFailure || !AiDesignTransitions.IsRunning(read.Value.Request.State)) break;
            }
        }
        catch (OperationCanceledException) { }
    }
    private static bool OwnerIsAlive(AiDesignRequest r)
    {
        if (r.OwnerProcessId is not { } pid || r.OwnerProcessStartUtcTicks is not { } ticks) return false;
        try { using var process = Process.GetProcessById(pid); return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == ticks; }
        catch (ArgumentException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return true; } // inaccessible identity is uncertainty, not permission to kill another owner.
    }
    private void Release(Guid id) { if (_leases.Remove(id, out var held)) held.Dispose(); }
    public void Dispose()
    {
        _lifetime.Cancel(); _serial.Wait();
        try
        {
            if (_disposed) return; _disposed = true;
            foreach (var id in _leases.Keys.ToArray())
            {
                var r = store.ReadAsync(id, CancellationToken.None).GetAwaiter().GetResult();
                if (r.IsSuccess && AiDesignTransitions.CanStop(r.Value.State)) store.ChangeAsync(r.Value.Fence, new AiDesignEvent.Interrupted(), CancellationToken.None).GetAwaiter().GetResult();
                Release(id);
            }
        }
        finally { _serial.Release(); }
    }
    private static Task<OperationResult<AiDesignView>> Unavailable(string reason) => Task.FromResult(OperationResult.Fail<AiDesignView>(FailureCode.AdapterUnavailable, reason));
}
