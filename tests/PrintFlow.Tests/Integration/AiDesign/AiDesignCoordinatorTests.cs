using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Time.Testing;
using PrintFlow.Domain.AiDesign;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.AiDesign;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Tests.Unit.AiDesign;
using PrintFlow.Workflow.AiDesign;
using AiUnit = PrintFlow.Domain.Results.Unit;

namespace PrintFlow.Tests.Integration.AiDesign;

public sealed class AiDesignCoordinatorTests
{
    private sealed class Channel(string root, bool available = true) : IAiDesignChannel
    {
        public int Calls;
        public Func<CancellationToken, Task<OperationResult<AiUnit>>>? Enqueue;
        public Func<CancellationToken, Task<OperationResult<AiUnit>>>? Stop;
        public Task<OperationResult<AiDesignBridgeReadiness>> CheckAsync(CancellationToken ct) => Task.FromResult(OperationResult.Ok(new AiDesignBridgeReadiness(available, 1, "1", "chrome-extension://test/", root, available ? null : "NotConfigured", Guid.NewGuid())));
        public Task<OperationResult<AiUnit>> EnqueueAsync(AiDesignRequest r, CancellationToken ct) { Calls++; return Enqueue?.Invoke(ct) ?? Task.FromResult(OperationResult.Ok()); }
        public Task<OperationResult<AiUnit>> StopAsync(AiDesignFence f, CancellationToken ct) => Stop?.Invoke(ct) ?? Task.FromResult(OperationResult.Fail<AiUnit>(FailureCode.AdapterUnavailable, "Remote stop unverified"));
    }
    [Fact]
    public async Task Bridge_exceptions_after_queue_revoke_authority_and_release_lease()
    {
        using TempWorkspace files = new(); using TempWorkspace installation = new(); var store = new FileAiDesignRequestStore(files.Root);
        var channel = new Channel(store.WorkspaceIdentity) { Enqueue = _ => throw new OperationCanceledException() };
        using var coordinator = new AiDesignCoordinator(store, channel, new AiDesignBrowserLease(installation.Root));
        var r = (await coordinator.CreateDraftAsync(AiDesignTransitionsTests.Draft().Prompt, default)).Value.Request;
        var interrupted = (await coordinator.StartAsync(r.RequestId, default)).Value;
        interrupted.Request.State.ShouldBe(AiDesignState.Interrupted); interrupted.ManualHandoffText.ShouldContain(r.Prompt.Text);
        var held = new AiDesignBrowserLease(installation.Root).TryAcquire(Guid.NewGuid()); held.IsSuccess.ShouldBeTrue(); held.Value.Dispose();
        channel.Enqueue = _ => throw new IOException("synthetic bridge loss");
        (await coordinator.RetryAsync(r.RequestId, default)).Value.Request.State.ShouldBe(AiDesignState.Interrupted);
        channel.Enqueue = null;
        (await coordinator.RetryAsync(r.RequestId, default)).Value.Request.State.ShouldBe(AiDesignState.Queued);
    }
    [Fact]
    public async Task Hung_remote_stop_is_bounded_and_next_request_can_acquire()
    {
        using TempWorkspace files = new(); var store = new FileAiDesignRequestStore(files.Root);
        var completion = new TaskCompletionSource<OperationResult<AiUnit>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var channel = new Channel(store.WorkspaceIdentity) { Stop = _ => completion.Task };
        using var coordinator = new AiDesignCoordinator(store, channel, new AiDesignBrowserLease(files.Root), bridgeCallTimeout: TimeSpan.FromMilliseconds(30));
        var r = (await coordinator.CreateDraftAsync(AiDesignTransitionsTests.Draft().Prompt, default)).Value.Request;
        (await coordinator.StartAsync(r.RequestId,default)).IsSuccess.ShouldBeTrue();
        var stopped = coordinator.StopAsync(r.RequestId,default);
        try
        {
            var result = await stopped.WaitAsync(TimeSpan.FromSeconds(2)); result.Value.Request.State.ShouldBe(AiDesignState.Cancelled);
            result.Value.Request.Failure!.RemoteStopConfirmed.ShouldBeFalse();
            var held = new AiDesignBrowserLease(files.Root).TryAcquire(Guid.NewGuid()); held.IsSuccess.ShouldBeTrue(); held.Value.Dispose();
        }
        finally { completion.TrySetResult(OperationResult.Ok()); await stopped; }
    }
    [Fact]
    public async Task Throwing_remote_stop_does_not_keep_local_lease_or_break_read()
    {
        using TempWorkspace files = new(); var store = new FileAiDesignRequestStore(files.Root);
        var channel = new Channel(store.WorkspaceIdentity) { Stop = _ => throw new IOException("synthetic bridge loss") };
        using var coordinator = new AiDesignCoordinator(store, channel, new AiDesignBrowserLease(files.Root));
        var r = (await coordinator.CreateDraftAsync(AiDesignTransitionsTests.Draft().Prompt, default)).Value.Request;
        (await coordinator.StartAsync(r.RequestId, default)).IsSuccess.ShouldBeTrue();
        (await coordinator.StopAsync(r.RequestId, default)).Value.Request.State.ShouldBe(AiDesignState.Cancelled);
        var held = new AiDesignBrowserLease(files.Root).TryAcquire(Guid.NewGuid()); held.IsSuccess.ShouldBeTrue(); held.Value.Dispose();
        (await coordinator.ReadAsync(r.RequestId, default)).Value.Request.Failure!.RemoteStopConfirmed.ShouldBeFalse();
    }
    [Fact]
    public async Task Second_automatic_request_is_refused_across_workspace_roots()
    {
        using TempWorkspace a = new(); using TempWorkspace b = new(); using TempWorkspace installation = new();
        var sa = new FileAiDesignRequestStore(a.Root); var sb = new FileAiDesignRequestStore(b.Root);
        var ca = new Channel(sa.WorkspaceIdentity); var cb = new Channel(sb.WorkspaceIdentity);
        using var first = new AiDesignCoordinator(sa, ca, new AiDesignBrowserLease(installation.Root));
        using var second = new AiDesignCoordinator(sb, cb, new AiDesignBrowserLease(installation.Root));
        var ra = (await first.CreateDraftAsync(AiDesignTransitionsTests.Draft().Prompt, default)).Value;
        var rb = (await second.CreateDraftAsync(AiDesignTransitionsTests.Draft().Prompt, default)).Value;
        (await first.StartAsync(ra.Request.RequestId, default)).Value.Request.State.ShouldBe(AiDesignState.Queued);
        (await second.StartAsync(rb.Request.RequestId, default)).IsFailure.ShouldBeTrue(); cb.Calls.ShouldBe(0);
        (await first.StopAsync(ra.Request.RequestId, default)).Value.Request.Failure!.RemoteStopConfirmed.ShouldBeFalse();
        (await second.StartAsync(rb.Request.RequestId, default)).IsSuccess.ShouldBeTrue();
    }
    [Fact]
    public async Task Recovery_keeps_live_other_owner_and_interrupts_dead_owner_without_enqueuing()
    {
        using TempWorkspace files = new(); using TempWorkspace installation = new(); var store = new FileAiDesignRequestStore(files.Root);
        var channel = new Channel(store.WorkspaceIdentity); using var coordinator = new AiDesignCoordinator(store, channel, new AiDesignBrowserLease(installation.Root));
        var d = AiDesignTransitionsTests.Draft(); var r = (await store.CreateAsync(d.RequestId, new(d.Prompt, []), default)).Value;
        r = (await store.ChangeAsync(r.Fence, new AiDesignEvent.Queued(Guid.NewGuid(), "token", Environment.ProcessId, Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks, Guid.NewGuid()), default)).Value;
        (await coordinator.RecoverAsync(default)).IsSuccess.ShouldBeTrue(); (await store.ReadAsync(r.RequestId, default)).Value.State.ShouldBe(AiDesignState.Queued);
        // Same PID with different start identity is a dead owner, including PID reuse.
        var dead = AiDesignTransitionsTests.Draft(); var rd = (await store.CreateAsync(dead.RequestId, new(dead.Prompt, []), default)).Value;
        rd = (await store.ChangeAsync(rd.Fence, new AiDesignEvent.Queued(Guid.NewGuid(), "dead", Environment.ProcessId, 1, Guid.NewGuid()), default)).Value;
        (await coordinator.RecoverAsync(default)).IsSuccess.ShouldBeTrue();
        var recovered = (await store.ReadAsync(rd.RequestId, default)).Value;
        recovered.State.ShouldBe(AiDesignState.Interrupted); recovered.Prompt.Text.ShouldBe("edited literal prompt"); channel.Calls.ShouldBe(0);
    }
    [Fact]
    public async Task Unconfigured_bridge_preserves_prompt_and_unavailable_import_never_succeeds()
    {
        using TempWorkspace files = new(); var store = new FileAiDesignRequestStore(files.Root); var channel = new Channel(store.WorkspaceIdentity, false);
        using var coordinator = new AiDesignCoordinator(store, channel, new AiDesignBrowserLease(files.Root));
        var d = (await coordinator.CreateDraftAsync(AiDesignTransitionsTests.Draft().Prompt, default)).Value;
        var failed = (await coordinator.StartAsync(d.Request.RequestId, default)).Value;
        failed.Request.State.ShouldBe(AiDesignState.PromptOnly); failed.ManualHandoffText.ShouldContain("edited literal prompt");
        (await coordinator.ImportManualAsync(d.Request.RequestId, "some-file", default)).IsFailure.ShouldBeTrue();
        System.Text.Json.JsonSerializer.Serialize(failed).ShouldNotContain("token", Case.Insensitive);
    }
    [Theory]
    [InlineData(AiDesignState.Uploading, 5)]
    [InlineData(AiDesignState.Generating, 10)]
    [InlineData(AiDesignState.Receiving, 2)]
    public async Task Read_applies_stage_deadline_at_exact_boundary(AiDesignState state, int minutes)
    {
        using TempWorkspace files = new(); var clock = new FakeTimeProvider(AiDesignTransitionsTests.Now);
        var store = new FileAiDesignRequestStore(files.Root, clock); var channel = new Channel(store.WorkspaceIdentity);
        using var coordinator = new AiDesignCoordinator(store, channel, new AiDesignBrowserLease(files.Root), clock);
        var d = AiDesignTransitionsTests.Draft();
        var attachments = state == AiDesignState.Uploading ? new[] { AiDesignTransitionsTests.Attachment(d.RequestId, 1) } : [];
        var r = (await store.CreateAsync(d.RequestId, new(d.Prompt, attachments), default)).Value;
        r = (await store.ChangeAsync(r.Fence, new AiDesignEvent.Queued(Guid.NewGuid(), "token", Environment.ProcessId, 1, Guid.NewGuid()), default)).Value;
        r = (await store.ChangeAsync(r.Fence, new AiDesignEvent.UploadStarted("tab", "conversation"), default)).Value;
        if (state != AiDesignState.Uploading)
        {
            r = (await store.ChangeAsync(r.Fence, new AiDesignEvent.SubmitReserved("tab", "conversation"), default)).Value;
            r = (await store.ChangeAsync(r.Fence, new AiDesignEvent.SubmitConfirmed(), default)).Value;
            r = (await store.ChangeAsync(r.Fence, new AiDesignEvent.GenerationStarted(), default)).Value;
            if (state == AiDesignState.Receiving) r = (await store.ChangeAsync(r.Fence, new AiDesignEvent.DownloadRegistered(new(r.RequestId, r.AttemptId, "tab", "conversation", "result", 1, "", false)), default)).Value;
        }
        clock.Advance(TimeSpan.FromMinutes(minutes).Subtract(TimeSpan.FromTicks(1)));
        (await coordinator.ReadAsync(r.RequestId, default)).Value.Request.State.ShouldBe(state);
        clock.Advance(TimeSpan.FromTicks(1));
        var read = (await coordinator.ReadAsync(r.RequestId, default)).Value;
        read.Request.State.ShouldBe(AiDesignState.PromptOnly); read.Request.Failure!.Code.ShouldBe("Timeout"); read.ManualHandoffText.ShouldContain(d.Prompt.Text);
    }
}
