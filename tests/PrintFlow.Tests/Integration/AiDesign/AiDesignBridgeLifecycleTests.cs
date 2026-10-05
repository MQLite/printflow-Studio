using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using PrintFlow.Domain.AiDesign;
using PrintFlow.Infrastructure.AiDesign;
using PrintFlow.NativeHost;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Tests.Unit.AiDesign;

namespace PrintFlow.Tests.Integration.AiDesign;

public sealed class AiDesignBridgeLifecycleTests
{
    private static AiDesignBridgeConfiguration Config(string root)=>new(1,"1.0.0",NativeHostBoundaryTests.Origin,root,root,new string('A',64));
    private static NativeMessageEnvelope Hello(string root)=>NativeMessageCodecTests.Message(payload:new{hostVersion="1.0.0",workspaceIdentity=new FileAiDesignRequestStore(root).WorkspaceIdentity,workContractHash=new string('A',64)});
    private static async Task<AiDesignRequest> Queue(FileAiDesignRequestStore store,Guid connection)
    {
        var d=AiDesignTransitionsTests.Draft();var r=(await store.CreateAsync(d.RequestId,new(d.Prompt,[]),default)).Value;
        return (await store.ChangeAsync(r.Fence,new AiDesignEvent.Queued(Guid.NewGuid(),AiDesignTransitions.NewToken(),Environment.ProcessId,
            System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks,connection),default)).Value;
    }
    [Fact]
    public async Task Retry_replaces_only_old_attempt_mailbox_and_same_attempt_never_replays()
    {
        using TempWorkspace root=new();using TempWorkspace installation=new();await using var session=new NativeHostSession(Config(root.Root),NativeHostBoundaryTests.Origin,installation.Root);
        (await session.HandleAsync(Hello(root.Root),default)).IsSuccess.ShouldBeTrue();var store=new FileAiDesignRequestStore(root.Root);var channel=new FileAiDesignChannel(Config(root.Root),installation.Root);
        var r=await Queue(store,session.ConnectionId);(await channel.EnqueueAsync(r,default)).IsSuccess.ShouldBeTrue();
        (await channel.EnqueueAsync(r,default)).IsFailure.ShouldBeTrue();
        r=(await store.ChangeAsync(r.Fence,new AiDesignEvent.StopAccepted(),default)).Value;
        r=(await store.ChangeAsync(r.Fence,new AiDesignEvent.Queued(Guid.NewGuid(),AiDesignTransitions.NewToken(),Environment.ProcessId,
            System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks,session.ConnectionId),default)).Value;
        (await channel.EnqueueAsync(r,default)).IsSuccess.ShouldBeTrue();
        string path=Path.Combine(root.Root,"AiDesign","Requests",r.RequestId.ToString("D"),"bridge-queue.local.json");
        var saved=JsonSerializer.Deserialize<BridgeQueueEntry>(await File.ReadAllTextAsync(path),NativeMessageCodec.JsonOptions)!;saved.Fence.ShouldBe(r.Fence);
    }
    [Fact]
    public async Task Concurrent_mailbox_reader_observes_only_complete_published_entries()
    {
        using TempWorkspace root=new();using TempWorkspace installation=new();await using var session=new NativeHostSession(Config(root.Root),NativeHostBoundaryTests.Origin,installation.Root);
        (await session.HandleAsync(Hello(root.Root),default)).IsSuccess.ShouldBeTrue();var store=new FileAiDesignRequestStore(root.Root);var channel=new FileAiDesignChannel(Config(root.Root),installation.Root);
        for(int i=0;i<30;i++)
        {
            var r=await Queue(store,session.ConnectionId);string path=Path.Combine(root.Root,"AiDesign","Requests",r.RequestId.ToString("D"),"bridge-queue.local.json");
            var write=channel.EnqueueAsync(r,default);
            while(!write.IsCompleted)
            {
                if(File.Exists(path))
                {
                    await using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete);
                    var observed=await JsonSerializer.DeserializeAsync<BridgeQueueEntry>(input,NativeMessageCodec.JsonOptions);observed!.Fence.ShouldBe(r.Fence);
                }
                await Task.Yield();
            }
            (await write).IsSuccess.ShouldBeTrue();
        }
    }
    [Fact]
    public async Task Pump_fault_disconnects_stdio_and_interrupts_owned_request_without_waiting_for_input()
    {
        using TempWorkspace root=new();using TempWorkspace installation=new();using var input=new AnonymousPipeServerStream(PipeDirection.In,HandleInheritability.None);
        using var writer=new AnonymousPipeClientStream(PipeDirection.Out,input.GetClientHandleAsString());using var output=new FailAfterReady();using var cancellation=new CancellationTokenSource();
        var run=Program.RunAsync(input,output,Config(root.Root),NativeHostBoundaryTests.Origin,installation.Root,cancellation.Token);
        AiDesignRequest? r=null;
        try
        {
            (await NativeMessageCodec.WriteAsync(writer,Hello(root.Root),default)).IsSuccess.ShouldBeTrue();
            var channel=new FileAiDesignChannel(Config(root.Root),installation.Root);PrintFlow.Workflow.AiDesign.AiDesignBridgeReadiness? ready=null;
            var until=DateTime.UtcNow.AddSeconds(3);
            while(DateTime.UtcNow<until){var check=await channel.CheckAsync(default);if(check.Value.Available){ready=check.Value;break;}await Task.Delay(10);}
            ready.ShouldNotBeNull();var store=new FileAiDesignRequestStore(root.Root);r=await Queue(store,ready!.ConnectionId);
            (await channel.EnqueueAsync(r,default)).IsSuccess.ShouldBeTrue();
            (await run.WaitAsync(TimeSpan.FromSeconds(3))).ShouldBe(2);
            (await store.ReadAsync(r.RequestId,default)).Value.State.ShouldBe(AiDesignState.Interrupted);
            (await channel.CheckAsync(default)).Value.Available.ShouldBeFalse();
        }
        finally{cancellation.Cancel();writer.Dispose();await run.WaitAsync(TimeSpan.FromSeconds(3));}
    }
    private sealed class FailAfterReady:MemoryStream
    {
        private int _writes;
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,CancellationToken cancellationToken=default)
        {if(Interlocked.Increment(ref _writes)>2)throw new IOException("Synthetic stdout failure");return base.WriteAsync(buffer,cancellationToken);}
    }
}
