using System.Diagnostics;
using System.IO;
using System.Text.Json;
using PrintFlow.Infrastructure.AiDesign;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Tests.Unit.AiDesign;
using PrintFlow.Domain.AiDesign;

namespace PrintFlow.Tests.Integration.AiDesign;

public sealed class NativeHostProcessTransferTests
{
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Product_program_pump_and_enqueue_deliver_framed_bytes_and_stop(bool stop)
    {
        using TempWorkspace root=new();using TempWorkspace selected=new();using TempWorkspace installation=new();
        string source=selected.CreateSourceFile("reference.png",Enumerable.Repeat((byte)9,2*256*1024+3).ToArray());var d=AiDesignTransitionsTests.Draft();
        var store=new FileAiDesignRequestStore(root.Root);
        var attachments=(await new FileAiDesignAttachments(root.Root).SnapshotAsync(d.RequestId,[new(source,"reference")],[],default)).Value;
        (await store.CreateAsync(d.RequestId,new(d.Prompt,attachments),default)).IsSuccess.ShouldBeTrue();
        var hostInfo=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        hostInfo.ArgumentList.Add(typeof(PrintFlow.AiDesignProbe.Program).Assembly.Location);
        foreach(var arg in new[]{"native-run",root.Root,installation.Root})hostInfo.ArgumentList.Add(arg);
        var nodeInfo=new ProcessStartInfo("node"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        nodeInfo.ArgumentList.Add(Path.Combine(FindRepository(),"extensions","printflow-work","tests","native-frame-receiver.mjs"));
        using var host=Process.Start(hostInfo)!;using var node=Process.Start(nodeInfo)!;using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));var ct=timeout.Token;
        try
        {
            var hello=NativeMessageCodecTests.Message(payload:new{hostVersion="1.0.0",workspaceIdentity=store.WorkspaceIdentity,workContractHash=new string('A',64)});
            (await NativeMessageCodec.WriteAsync(host.StandardInput.BaseStream,hello,ct)).IsSuccess.ShouldBeTrue();
            var ready=(await NativeMessageCodec.ReadAsync(host.StandardOutput.BaseStream,ct)).Value!;
            ready.Kind.ShouldBe("Ready");ready.Payload.GetProperty("available").GetBoolean().ShouldBeTrue();
            (await NativeMessageCodec.WriteAsync(node.StandardInput.BaseStream,ready,ct)).IsSuccess.ShouldBeTrue();
            var r=(await store.ReadAsync(d.RequestId,ct)).Value;
            r=(await store.ChangeAsync(r.Fence,new AiDesignEvent.Queued(Guid.NewGuid(),AiDesignTransitions.NewToken(),Environment.ProcessId,Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks,ready.Payload.GetProperty("connectionId").GetGuid()),ct)).Value;
            var config=new AiDesignBridgeConfiguration(1,"1.0.0",NativeHostBoundaryTests.Origin,root.Root,root.Root,new string('A',64));
            (await new FileAiDesignChannel(config,installation.Root).EnqueueAsync(r,ct)).IsSuccess.ShouldBeTrue();
            int chunks=0;bool finished=false;
            while(!finished)
            {
                var m=(await NativeMessageCodec.ReadAsync(host.StandardOutput.BaseStream,ct)).Value!;
                (await NativeMessageCodec.WriteAsync(node.StandardInput.BaseStream,m,ct)).IsSuccess.ShouldBeTrue();
                if(m.Kind=="AttachmentChunk")
                {
                    chunks++;
                    if(stop){(await store.ChangeAsync(r.Fence,new AiDesignEvent.StopAccepted(),ct)).IsSuccess.ShouldBeTrue();finished=true;}
                }
                if(m.Kind=="AttachmentComplete")finished=true;
            }
            await Task.Delay(180,ct);host.StandardInput.Close();
            var remainder=new MemoryStream();await host.StandardOutput.BaseStream.CopyToAsync(remainder,ct);
            if(stop)remainder.Length.ShouldBe(0);else chunks.ShouldBe(3);
            node.StandardInput.Close();await host.WaitForExitAsync(ct);await node.WaitForExitAsync(ct);
            host.ExitCode.ShouldBe(0,await host.StandardError.ReadToEndAsync());node.ExitCode.ShouldBe(stop?2:0,await node.StandardError.ReadToEndAsync());
            if(!stop){using var result=JsonDocument.Parse(await node.StandardOutput.ReadToEndAsync());result.RootElement.GetProperty("sha256").GetString().ShouldBe(attachments[0].Sha256.Value);}
            else (await store.ReadAsync(d.RequestId,ct)).Value.State.ShouldBe(AiDesignState.Cancelled);
        }
        finally{if(!host.HasExited)host.Kill(true);if(!node.HasExited)node.Kill(true);}
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Child_host_stdio_reaches_real_Node_assembler_and_partial_stop_is_rejected(bool stop)
    {
        using TempWorkspace root=new();using TempWorkspace selected=new();using TempWorkspace installation=new();
        string source=selected.CreateSourceFile("reference.png",Enumerable.Repeat((byte)7,2*256*1024+3).ToArray());var d=AiDesignTransitionsTests.Draft();
        var attachments=(await new FileAiDesignAttachments(root.Root).SnapshotAsync(d.RequestId,[new(source,"reference")],[],default)).Value;
        (await new FileAiDesignRequestStore(root.Root).CreateAsync(d.RequestId,new(d.Prompt,attachments),default)).IsSuccess.ShouldBeTrue();
        var hostInfo=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        hostInfo.ArgumentList.Add(typeof(PrintFlow.AiDesignProbe.Program).Assembly.Location);
        foreach(var arg in new[]{stop?"native-transfer-stop":"native-transfer",root.Root,d.RequestId.ToString(),attachments[0].Id.ToString(),installation.Root})hostInfo.ArgumentList.Add(arg);
        string repository=FindRepository();var nodeInfo=new ProcessStartInfo("node"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        nodeInfo.ArgumentList.Add(Path.Combine(repository,"extensions","printflow-work","tests","native-frame-receiver.mjs"));
        using var host=Process.Start(hostInfo)!;using var node=Process.Start(nodeInfo)!;
        try
        {
            await host.StandardOutput.BaseStream.CopyToAsync(node.StandardInput.BaseStream).WaitAsync(TimeSpan.FromSeconds(10));node.StandardInput.Close();
            await host.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));await node.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            host.ExitCode.ShouldBe(0,await host.StandardError.ReadToEndAsync());node.ExitCode.ShouldBe(stop?2:0,await node.StandardError.ReadToEndAsync());
            if(!stop)
            {
                using var result=JsonDocument.Parse(await node.StandardOutput.ReadToEndAsync());var value=result.RootElement;
                value.GetProperty("byteLength").GetInt64().ShouldBe(2*256*1024+3);value.GetProperty("uploadName").GetString().ShouldBe("attachment-01.png");
                value.GetProperty("sha256").GetString().ShouldBe(attachments[0].Sha256.Value);value.GetProperty("frameCount").GetInt32().ShouldBe(6);
            }
            else (await node.StandardOutput.ReadToEndAsync()).ShouldBeEmpty();
        }
        finally{if(!host.HasExited)host.Kill(true);if(!node.HasExited)node.Kill(true);}
    }
    private static string FindRepository()
    {
        for(var dir=new DirectoryInfo(AppContext.BaseDirectory);dir is not null;dir=dir.Parent)
            if(File.Exists(Path.Combine(dir.FullName,"PrintFlowStudio.sln")))return dir.FullName;
        throw new InvalidOperationException("Repository root not found.");
    }
}
