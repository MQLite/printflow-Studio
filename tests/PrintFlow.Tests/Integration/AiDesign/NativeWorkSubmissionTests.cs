using System.Diagnostics;
using System.Text.Json;
using PrintFlow.Domain.AiDesign;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.AiDesign;
using PrintFlow.NativeHost;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Tests.Unit.AiDesign;
using AiUnit = PrintFlow.Domain.Results.Unit;

namespace PrintFlow.Tests.Integration.AiDesign;

public sealed class NativeWorkSubmissionTests
{
    [Fact]
    public async Task One_durable_permit_accepts_only_the_bound_page_receipt()
    {
        using TempWorkspace workspace = new();
        using TempWorkspace installation = new();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        CancellationToken ct = timeout.Token;
        string contract = AiDesignBridgeConfiguration.TextSubmissionTrialContract;
        var configuration = new AiDesignBridgeConfiguration(1, "1.0.0", NativeHostBoundaryTests.Origin,
            workspace.Root, workspace.Root, contract, "Default");
        await using var session = new NativeHostSession(configuration, configuration.ExtensionOrigin, installation.Root);
        var store = new FileAiDesignRequestStore(workspace.Root);
        var hello = NativeMessageCodecTests.Message(payload: new
        {
            hostVersion = "1.0.0", workspaceIdentity = store.WorkspaceIdentity, workContractHash = contract, launchNonce = "0123456789ABCDEF0123456789ABCDEF"
        });
        (await session.HandleAsync(hello, ct)).Value.Payload.GetProperty("available").GetBoolean().ShouldBeTrue();
        (await new FileAiDesignChannel(configuration, installation.Root, expectedLaunchNonce: new string('A', 32))
            .CheckAsync(ct)).Value.Available.ShouldBeFalse();
        (await new FileAiDesignChannel(configuration, installation.Root, expectedLaunchNonce: "0123456789ABCDEF0123456789ABCDEF")
            .CheckAsync(ct)).Value.Available.ShouldBeTrue();
        var draft = AiDesignTransitionsTests.Draft();
        var request = (await store.CreateAsync(draft.RequestId, new(draft.Prompt, []), ct)).Value;
        using var process = Process.GetCurrentProcess();
        request = (await store.ChangeAsync(request.Fence, new AiDesignEvent.Queued(Guid.NewGuid(), "token",
            process.Id, process.StartTime.ToUniversalTime().Ticks, session.ConnectionId), ct)).Value;
        request = (await store.ChangeAsync(request.Fence, new AiDesignEvent.UploadStarted("tab-1", "local-conversation"), ct)).Value;
        var ready = Frame(request.Fence, "Progress", new { stage = "SubmitReady", tabId = "tab-1", conversationId = "local-conversation" });
        (await session.HandleAsync(ready, ct)).IsFailure.ShouldBeTrue();
        using var installationLease = new AiDesignBrowserLease(installation.Root).TryAcquire(request.RequestId).Value;
        (await session.HandleAsync(ready with
        {
            Payload = JsonSerializer.SerializeToElement(new { stage = "SubmitReady", tabId = "different", conversationId = "local-conversation" })
        }, ct)).IsFailure.ShouldBeTrue();
        (await session.HandleAsync(ready, ct)).IsSuccess.ShouldBeTrue();
        (await session.HandleAsync(ready with { MessageId = Guid.NewGuid() }, ct)).IsFailure.ShouldBeTrue();

        var permit = new TaskCompletionSource<NativeMessageEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource<NativeMessageEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        int permitCount = 0;
        Task<OperationResult<AiUnit>> Emit(NativeMessageEnvelope frame, CancellationToken _)
        {
            if (frame.Kind == "SubmitPermit")
            {
                Interlocked.Increment(ref permitCount);
                permit.TrySetResult(frame);
            }
            if (frame.Kind == "Ack" && frame.Payload.TryGetProperty("stage", out var stage) && stage.GetString() == "Submitted")
                completion.TrySetResult(frame);
            return Task.FromResult(OperationResult.Ok());
        }
        Task pump = session.PumpAsync(Emit, ct);
        try
        {
            var sent = await permit.Task.WaitAsync(ct);
            sent.StateVersion.ShouldBe(request.StateVersion + 1);
            (await session.HandleAsync(ready, ct).WaitAsync(TimeSpan.FromSeconds(1))).Failure.TechnicalDetail.ShouldBe("SubmitInProgress");
            var permitId = sent.Payload.GetProperty("permitId").GetGuid();
            var reservedFence = request.Fence with { StateVersion = sent.StateVersion };
            var wrong = Frame(reservedFence, "SubmitReceipt", new { permitId, tabId = "other", conversationId = "local-conversation" });
            (await session.HandleAsync(wrong, ct)).IsFailure.ShouldBeTrue();
            var workConversationId = Guid.NewGuid();
            var receipt = Frame(reservedFence, "SubmitReceipt", new { permitId, tabId = "tab-1", conversationId = "local-conversation", confirmed = true, workConversationId });
            (await session.HandleAsync(receipt, ct)).IsSuccess.ShouldBeTrue();
            (await completion.Task.WaitAsync(ct)).StateVersion.ShouldBe(sent.StateVersion + 1);
            var persisted = (await store.ReadAsync(request.RequestId, ct)).Value;
            persisted.State.ShouldBe(AiDesignState.Submitted);
            persisted.SubmissionReservedAtUtc.ShouldNotBeNull();
            persisted.WorkConversationId.ShouldBe(workConversationId);
            (await session.HandleAsync(receipt with { MessageId = Guid.NewGuid() }, ct)).IsFailure.ShouldBeTrue();
            permitCount.ShouldBe(1);
        }
        finally
        {
            await timeout.CancelAsync();
            try { await pump; }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }
        }
    }

    private static NativeMessageEnvelope Frame(AiDesignFence fence, string kind, object payload) =>
        new(1, Guid.NewGuid(), fence.RequestId, fence.AttemptId, fence.StateVersion, fence.Token, kind,
            JsonSerializer.SerializeToElement(payload, NativeMessageCodec.JsonOptions));
}
