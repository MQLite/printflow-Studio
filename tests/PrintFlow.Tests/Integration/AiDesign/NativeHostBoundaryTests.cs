using System.IO;
using System.Text.Json;
using PrintFlow.Infrastructure.AiDesign;
using PrintFlow.NativeHost;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Domain.AiDesign;
using PrintFlow.Tests.Unit.AiDesign;
using PrintFlow.Domain.Results;
using AiUnit=PrintFlow.Domain.Results.Unit;

namespace PrintFlow.Tests.Integration.AiDesign;

public sealed class NativeHostBoundaryTests
{
    internal const string Origin="chrome-extension://aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/";
    [Fact]
    public async Task Live_port_rejects_each_stale_authority_field_and_unverified_page_events()
    {
        using TempWorkspace root=new();using TempWorkspace installation=new();
        var config=new AiDesignBridgeConfiguration(1,"1.0.0",Origin,root.Root,root.Root);
        await using var session=new NativeHostSession(config,Origin,installation.Root);
        var store=new FileAiDesignRequestStore(root.Root);
        var hello=NativeMessageCodecTests.Message(payload:new{hostVersion="1.0.0",workspaceIdentity=store.WorkspaceIdentity,workContractHash=""});
        (await session.HandleAsync(hello,default)).IsSuccess.ShouldBeTrue();
        var d=AiDesignTransitionsTests.Draft();var r=(await store.CreateAsync(d.RequestId,new(d.Prompt,[]),default)).Value;
        r=(await store.ChangeAsync(r.Fence,new AiDesignEvent.Queued(Guid.NewGuid(),"token",Environment.ProcessId,System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks,session.ConnectionId),default)).Value;
        var m=new NativeMessageEnvelope(1,Guid.NewGuid(),r.RequestId,r.AttemptId,r.StateVersion,r.Token,"Ack",JsonSerializer.SerializeToElement(new{}));
        foreach(var stale in new[]{m with{RequestId=Guid.NewGuid()},m with{AttemptId=Guid.NewGuid()},m with{StateVersion=m.StateVersion-1},m with{Token="old"}})
            (await session.HandleAsync(stale,default)).Failure.TechnicalDetail.ShouldBe("BridgeFenceMismatch");
        (await session.HandleAsync(m,default)).IsSuccess.ShouldBeTrue();
        (await session.HandleAsync(m with{Kind="DownloadComplete",Payload=JsonSerializer.SerializeToElement(new{absolutePath="C:\\private\\file.png"})},default)).Failure.TechnicalDetail.ShouldBe("WorkContractUnverified");
        (await store.ReadAsync(r.RequestId,default)).Value.Fence.ShouldBe(r.Fence);
        (await new FileAiDesignChannel(null,installation.Root).CheckAsync(default)).Value.Available.ShouldBeFalse();
        (await new FileAiDesignChannel(config with{HostVersion="wrong"},installation.Root).CheckAsync(default)).Value.Reason.ShouldBe("BridgeVersionMismatch");
    }
    [Fact]
    public async Task Hello_pins_origin_version_root_and_requires_verified_page_contract_for_readiness()
    {
        using TempWorkspace root=new(); using TempWorkspace downloads=new(); using TempWorkspace installation=new();
        var config=new AiDesignBridgeConfiguration(1,"1.0.0",Origin,root.Root,downloads.Root);
        config.Validate(Origin).IsSuccess.ShouldBeTrue();config.Validate(Origin.Replace("a","b",StringComparison.Ordinal)).IsFailure.ShouldBeTrue();
        await using var session=new NativeHostSession(config,Origin,installation.Root);
        var hello=NativeMessageCodecTests.Message(payload:new{hostVersion="1.0.0",workspaceIdentity=new FileAiDesignRequestStore(root.Root).WorkspaceIdentity,workContractHash=""});
        var result=await session.HandleAsync(hello,default);result.IsSuccess.ShouldBeTrue();result.Value.Kind.ShouldBe("Ready");
        result.Value.Payload.GetProperty("available").GetBoolean().ShouldBeFalse();
        result.Value.Payload.GetProperty("reason").GetString().ShouldBe("WorkContractUnverified");
        (await session.HandleAsync(hello with{ProtocolVersion=2},default)).IsFailure.ShouldBeTrue();
        var wrongRoot=hello with{Payload=JsonSerializer.SerializeToElement(new{hostVersion="1.0.0",workspaceIdentity="other-root",workContractHash=""})};
        await using var other=new NativeHostSession(config,Origin,installation.Root);
        (await other.HandleAsync(wrongRoot,default)).IsFailure.ShouldBeTrue();
    }
    [Fact]
    public async Task Upload_progress_waits_for_chunk_boundary_and_ack_supplies_the_new_fence()
    {
        using TempWorkspace root=new();using TempWorkspace selected=new();using TempWorkspace installation=new();
        string source=selected.CreateSourceFile("reference.png",Enumerable.Repeat((byte)7,256*1024+3).ToArray());
        var config=new AiDesignBridgeConfiguration(1,"1.0.0",Origin,root.Root,root.Root,new string('A',64));
        await using var session=new NativeHostSession(config,Origin,installation.Root);
        (await session.HandleAsync(NativeMessageCodecTests.Message(payload:new{hostVersion="1.0.0",workspaceIdentity=new FileAiDesignRequestStore(root.Root).WorkspaceIdentity,workContractHash=new string('A',64)}),default)).IsSuccess.ShouldBeTrue();
        var d=AiDesignTransitionsTests.Draft();var attachments=(await new FileAiDesignAttachments(root.Root).SnapshotAsync(d.RequestId,[new(source,"reference")],[],default)).Value;
        var store=new FileAiDesignRequestStore(root.Root);var r=(await store.CreateAsync(d.RequestId,new(d.Prompt,attachments),default)).Value;
        r=(await store.ChangeAsync(r.Fence,new AiDesignEvent.Queued(Guid.NewGuid(),"token",Environment.ProcessId,System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks,session.ConnectionId),default)).Value;
        Task<OperationResult<NativeMessageEnvelope>>? progress=null;int count=0;
        async Task<OperationResult<AiUnit>> Emit(NativeMessageEnvelope m,CancellationToken ct)
        {
            count++;
            if(count==1)
            {
                progress=session.HandleAsync(new(1,Guid.NewGuid(),r.RequestId,r.AttemptId,r.StateVersion,r.Token,"Progress",JsonSerializer.SerializeToElement(new{stage="UploadStarted",tabId="tab",conversationId="conversation"})),ct);
                await Task.Delay(30,ct);progress.IsCompleted.ShouldBeFalse();
            }
            m.StateVersion.ShouldBe(r.StateVersion);return OperationResult.Ok();
        }
        (await session.StreamAttachmentAsync(r.Fence,attachments[0].Id,Emit,default)).IsSuccess.ShouldBeTrue();count.ShouldBe(3);
        var ack=(await progress!.WaitAsync(TimeSpan.FromSeconds(2))).Value;ack.Kind.ShouldBe("Ack");ack.StateVersion.ShouldBe(r.StateVersion+1);
        (await store.ReadAsync(r.RequestId,default)).Value.State.ShouldBe(AiDesignState.Uploading);
        // The trusted pump may already have read v2 before the handler's Ack reaches stdout.
        // A boundary Ack must precede v2 chunks even when its passed fence is already current.
        var latest=(await store.ReadAsync(r.RequestId,default)).Value;
        var nextFrames=new List<NativeMessageEnvelope>();
        Task<OperationResult<AiUnit>> Next(NativeMessageEnvelope m,CancellationToken ct)
        {nextFrames.Add(m);return Task.FromResult(OperationResult.Ok());}
        (await session.StreamAttachmentAsync(latest.Fence,attachments[0].Id,Next,default,refreshCurrentFence:true)).IsSuccess.ShouldBeTrue();
        nextFrames[0].Kind.ShouldBe("Ack");nextFrames[1].Kind.ShouldBe("AttachmentChunk");
        nextFrames.All(m=>m.StateVersion==latest.StateVersion).ShouldBeTrue();
    }
    [Fact]
    public async Task Full_attachment_frames_preserve_bytes_and_stop_cuts_further_frames()
    {
        using TempWorkspace root=new();using TempWorkspace selected=new();using TempWorkspace installation=new();
        string source=selected.CreateSourceFile("reference.png",Enumerable.Repeat((byte)7,2*256*1024+3).ToArray());
        var config=new AiDesignBridgeConfiguration(1,"1.0.0",Origin,root.Root,root.Root);
        await using var session=new NativeHostSession(config,Origin,installation.Root);
        (await session.HandleAsync(NativeMessageCodecTests.Message(payload:new{hostVersion="1.0.0",workspaceIdentity=new FileAiDesignRequestStore(root.Root).WorkspaceIdentity,workContractHash=""}),default)).IsSuccess.ShouldBeTrue();
        var d=AiDesignTransitionsTests.Draft();var attachments=(await new FileAiDesignAttachments(root.Root).SnapshotAsync(d.RequestId,[new(source,"reference")],[],default)).Value;
        var store=new FileAiDesignRequestStore(root.Root);var r=(await store.CreateAsync(d.RequestId,new(d.Prompt,attachments),default)).Value;
        r=(await store.ChangeAsync(r.Fence,new AiDesignEvent.Queued(Guid.NewGuid(),"token",Environment.ProcessId,System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks,session.ConnectionId),default)).Value;
        var frames=new List<NativeMessageEnvelope>();
        async Task<OperationResult<AiUnit>> Emit(NativeMessageEnvelope m,CancellationToken ct)
        {
            using var output=new MemoryStream();(await NativeMessageCodec.WriteAsync(output,m,ct)).IsSuccess.ShouldBeTrue();output.Position=0;
            frames.Add((await NativeMessageCodec.ReadAsync(output,ct)).Value!);return OperationResult.Ok();
        }
        (await session.StreamAttachmentAsync(r.Fence,attachments[0].Id,Emit,default)).IsSuccess.ShouldBeTrue();
        frames.Count.ShouldBe(4);frames[^1].Kind.ShouldBe("AttachmentComplete");
        var bytes=frames.Where(m=>m.Kind=="AttachmentChunk").SelectMany(m=>Convert.FromBase64String(m.Payload.GetProperty("data").GetString()!)).ToArray();
        bytes.ShouldBe(await File.ReadAllBytesAsync(source));
        frames.Clear();
        async Task<OperationResult<AiUnit>> StopAfterFirst(NativeMessageEnvelope m,CancellationToken ct)
        { frames.Add(m);(await store.ChangeAsync(r.Fence,new AiDesignEvent.StopAccepted(),ct)).IsSuccess.ShouldBeTrue();return OperationResult.Ok(); }
        (await session.StreamAttachmentAsync(r.Fence,attachments[0].Id,StopAfterFirst,default)).IsFailure.ShouldBeTrue();frames.Count.ShouldBe(1);
    }
    [Fact]
    public async Task Request_fence_and_arbitrary_path_messages_are_refused_before_handshake()
    {
        using TempWorkspace root=new();using TempWorkspace installation=new();
        await using var session=new NativeHostSession(new(1,"1.0.0",Origin,root.Root,root.Root),Origin,installation.Root);
        foreach(string kind in new[]{"UploadReceipt","DownloadComplete","Request","AttachmentChunk"})
            (await session.HandleAsync(NativeMessageCodecTests.Message(kind,new{absolutePath="C:\\private\\not-selected.txt"}),default)).IsFailure.ShouldBeTrue();
    }
}
