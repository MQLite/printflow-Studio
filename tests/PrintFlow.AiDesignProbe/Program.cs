using System.Diagnostics;
using System.Text.Json;
using PrintFlow.Domain.AiDesign;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.AiDesign;

namespace PrintFlow.AiDesignProbe;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25)); var ct = timeout.Token;
        if(args[0]=="native-run")
        {
            const string origin="chrome-extension://aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/";
            var config=new AiDesignBridgeConfiguration(1,"1.0.0",origin,args[1],args[1],new string('A',64));
            return await PrintFlow.NativeHost.Program.RunAsync(Console.OpenStandardInput(),new PacedOutput(Console.OpenStandardOutput()),config,origin,args[2],ct);
        }
        if(args[0] is "native-transfer" or "native-transfer-stop")
        {
            const string origin="chrome-extension://aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/";
            var config=new AiDesignBridgeConfiguration(1,"1.0.0",origin,args[1],args[1]);
            await using var native=new PrintFlow.NativeHost.NativeHostSession(config,origin,args[4]);var transportStore=new FileAiDesignRequestStore(args[1]);
            var hello=new NativeMessageEnvelope(1,Guid.NewGuid(),Guid.Empty,Guid.Empty,0,"","Hello",JsonSerializer.SerializeToElement(new{hostVersion="1.0.0",workspaceIdentity=transportStore.WorkspaceIdentity,workContractHash=""}));
            var ready=await native.HandleAsync(hello,ct);if(ready.IsFailure)return 2;
            Stream output=Console.OpenStandardOutput();if((await NativeMessageCodec.WriteAsync(output,ready.Value,ct)).IsFailure)return 2;
            var request=(await transportStore.ReadAsync(Guid.Parse(args[2]),ct)).Value;
            request=(await transportStore.ChangeAsync(request.Fence,new AiDesignEvent.Queued(Guid.NewGuid(),AiDesignTransitions.NewToken(),Environment.ProcessId,Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks,native.ConnectionId),ct)).Value;
            var wire=new NativeMessageEnvelope(1,Guid.NewGuid(),request.RequestId,request.AttemptId,request.StateVersion,request.Token,"Request",JsonSerializer.SerializeToElement(new{attachments=request.Attachments.Select(a=>new{a.Id,a.Order,a.UploadName,a.MediaType,a.ByteLength,sha256=a.Sha256.Value})},NativeMessageCodec.JsonOptions));
            if((await NativeMessageCodec.WriteAsync(output,wire,ct)).IsFailure)return 2;int frames=0;
            async Task<OperationResult<Unit>> Emit(NativeMessageEnvelope message,CancellationToken token)
            {
                var result=await NativeMessageCodec.WriteAsync(output,message,token);frames++;
                if(args[0]=="native-transfer-stop"&&frames==1)await transportStore.ChangeAsync(request.Fence,new AiDesignEvent.StopAccepted(),token);
                return result;
            }
            var transfer=await native.StreamAttachmentAsync(request.Fence,Guid.Parse(args[3]),Emit,ct);
            return args[0]=="native-transfer-stop" ? transfer.IsFailure&&frames==1?0:2 : transfer.IsSuccess?0:2;
        }
        if (args[0] == "lease")
        {
            var result = new AiDesignBrowserLease(args[1]).TryAcquire(Guid.NewGuid());
            if (result.IsFailure) return 2;
            using var lease = result.Value; await File.WriteAllTextAsync(args[2], "held", ct);
            while (!File.Exists(args[3])) await Task.Delay(20, ct); return 0;
        }
        var store = new FileAiDesignRequestStore(args[1]); var id = Guid.Parse(args[2]);
        var r = (await store.ReadAsync(id, ct)).Value;
        await File.WriteAllTextAsync(args[3], "ready", ct);
        while (!File.Exists(args[4])) await Task.Delay(20, ct);
        if (args[0] == "write-crash")
        {
            for (int i = 0; i < 10000; i++)
            {
                r = (await store.ChangeAsync(r.Fence, new AiDesignEvent.DraftSaved(r.Prompt with { Text = "version:" + (r.StateVersion + 1) }, [], 1), ct)).Value;
            }
            return 0;
        }
        if (args[0] == "cas")
        {
            var result = await store.ChangeAsync(r.Fence, new AiDesignEvent.Queued(Guid.NewGuid(), AiDesignTransitions.NewToken(),
                Environment.ProcessId, Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks, Guid.NewGuid()), ct);
            return result.IsSuccess ? 0 : 2;
        }
        if (args[0] == "submit-crash")
        {
            var result = await store.SubmitOnceAsync(r.Fence, async token =>
            {
                await File.WriteAllTextAsync(args[5], "reserved", token);
                await Task.Delay(Timeout.InfiniteTimeSpan, token); return OperationResult.Ok();
            }, ct);
            return result.IsSuccess ? 0 : 2;
        }
        return 3;
    }
    // Synthetic stdout backpressure gives the parent a deterministic stop failure window.
    private sealed class PacedOutput(Stream output):Stream
    {
        public override bool CanRead=>false;public override bool CanSeek=>false;public override bool CanWrite=>true;
        public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override void Flush()=>output.Flush();public override Task FlushAsync(CancellationToken ct)=>output.FlushAsync(ct);
        public override void Write(byte[] b,int o,int c)=>output.Write(b,o,c);
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> b,CancellationToken ct=default)
        {await output.WriteAsync(b,ct);if(b.Length>4){await output.FlushAsync(ct);await Task.Delay(80,ct);}}
        public override int Read(byte[] b,int o,int c)=>throw new NotSupportedException();
        public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();public override void SetLength(long n)=>throw new NotSupportedException();
    }
}
