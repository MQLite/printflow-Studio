using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Collections.Concurrent;
using PrintFlow.Domain.AiDesign;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.AiDesign;

namespace PrintFlow.NativeHost;

/// <summary>A persistent browser port owns this session; no registry/configuration entry can impersonate it.</summary>
public sealed class NativeHostSession : IAsyncDisposable
{
    private readonly AiDesignBridgeConfiguration _configuration;
    private readonly string _origin,_installation;
    private readonly FileAiDesignRequestStore _store;
    private readonly FileAiDesignAttachments _attachments;
    private readonly HashSet<(Guid,Guid)> _dispatched=[];
    private readonly ConcurrentDictionary<Guid,SemaphoreSlim> _requestGates=new();
    private FileStream? _connectionLock;
    private bool _verified;
    private string? _contractHash;
    public Guid ConnectionId {get;}=Guid.NewGuid();
    public NativeHostSession(AiDesignBridgeConfiguration configuration,string callerOrigin,string installationDirectory)
    {
        _configuration=configuration;_origin=callerOrigin;_installation=Path.GetFullPath(installationDirectory);
        _store=new(configuration.WorkspaceRoot);_attachments=new(configuration.WorkspaceRoot);
    }
    public async Task<OperationResult<NativeMessageEnvelope>> HandleAsync(NativeMessageEnvelope m,CancellationToken ct)
    {
        SemaphoreSlim? gate=null;bool held=false;
        try
        {
            var config=_configuration.Validate(_origin);if(config.IsFailure)return OperationResult.Fail<NativeMessageEnvelope>(config.Failure);
            if(!NativeMessageCodec.IsValid(m))return Fail("NativeEnvelopeInvalid");
            if(m.Kind=="Hello")
            {
                if(_connectionLock is not null)return Fail("DuplicateHello");
                string version=m.Payload.GetProperty("hostVersion").GetString()??"";
                string root=m.Payload.GetProperty("workspaceIdentity").GetString()??"";
                if(version!=AiDesignBridgeConfiguration.CurrentHostVersion)return Fail("BridgeVersionMismatch");
                if(root.Length>0&&root!=config.Value)return Fail("BridgeRootMismatch");
                RefuseAncestry(_installation);Directory.CreateDirectory(_installation);string path=Path.Combine(_installation,"connection.lock");RefuseAncestry(path);
                _connectionLock=new(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
                _contractHash=m.Payload.GetProperty("workContractHash").GetString();
                _verified=_configuration.VerifiedWorkContractHash is not null&&_contractHash==_configuration.VerifiedWorkContractHash;
                await InterruptDisconnectedAsync(ct);await HeartbeatAsync(ct);
                return Reply(m,"Ready",new{available=_verified,protocolVersion=1,hostVersion=AiDesignBridgeConfiguration.CurrentHostVersion,
                    extensionOrigin=_origin,workspaceIdentity=config.Value,connectionId=ConnectionId,reason=_verified?(string?)null:"WorkContractUnverified"});
            }
            if(_connectionLock is null)return Fail("BridgeHelloRequired");
            if(m.Kind is "Request" or "AttachmentChunk" or "AttachmentComplete" or "Ready" or "SubmitPermit")return Fail("NativeDirectionInvalid");
            gate=_requestGates.GetOrAdd(m.RequestId,_=>new(1,1));await gate.WaitAsync(ct);held=true;
            var read=await _store.ReadAsync(m.RequestId,ct);
            var fence=new AiDesignFence(m.RequestId,m.AttemptId,m.StateVersion,m.Token);
            if(read.IsFailure||!FileAiDesignRequestStore.Matches(read.Value,fence)||read.Value.ConnectionId!=ConnectionId||read.Value.AttemptId==Guid.Empty)return Fail("BridgeFenceMismatch");
            if(m.Kind=="Ack")return Reply(m,"Ack",new{stateVersion=read.Value.StateVersion});
            if(!_verified)return Fail("WorkContractUnverified");
            AiDesignEvent? change=m.Kind switch
            {
                "Progress" when m.Payload.GetProperty("stage").GetString()=="UploadStarted" =>new AiDesignEvent.UploadStarted(m.Payload.GetProperty("tabId").GetString()!,m.Payload.GetProperty("conversationId").GetString()!),
                "Progress" when m.Payload.GetProperty("stage").GetString()=="GenerationStarted" =>new AiDesignEvent.GenerationStarted(),
                "UploadReceipt"=>new AiDesignEvent.UploadConfirmed(new(m.Payload.GetProperty("attachmentId").GetGuid(),m.AttemptId,
                    m.Payload.GetProperty("conversationId").GetString()!,AiDesignUploadStatus.Uploaded,m.Payload.GetProperty("evidenceKey").GetString()!)),
                // Page submit authority and result interpretation are implemented only after the T02/T07 gate.
                _=>null
            };
            if(change is null)return OperationResult.Fail<NativeMessageEnvelope>(FailureCode.AdapterUnavailable,"WorkOperationUnavailable");
            var changed=await _store.ChangeAsync(fence,change,ct);if(changed.IsFailure)return OperationResult.Fail<NativeMessageEnvelope>(changed.Failure);
            return Reply(changed.Value,"Ack",new{stateVersion=changed.Value.StateVersion});
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException or OperationCanceledException)
        {return Fail("NativeBoundaryRejected");}
        finally{if(held)gate!.Release();}
    }
    public async Task<OperationResult<Unit>> StreamAttachmentAsync(AiDesignFence fence,Guid attachmentId,
        Func<NativeMessageEnvelope,CancellationToken,Task<OperationResult<Unit>>> emit,CancellationToken ct,bool refreshCurrentFence=false)
    {
        var gate=_requestGates.GetOrAdd(fence.RequestId,_=>new(1,1));bool held=false;
        try
        {
            await gate.WaitAsync(ct);held=true;
            if(refreshCurrentFence)
            {
                // This is the host's trusted queue reader, not acceptance of a stale browser event.
                var latest=await _store.ReadAsync(fence.RequestId,ct);
                if(latest.IsFailure||latest.Value.AttemptId!=fence.AttemptId||latest.Value.Token!=fence.Token||latest.Value.ConnectionId!=ConnectionId)
                    return OperationResult.Fail<Unit>(FailureCode.PreconditionNotMet,"BridgeFenceMismatch");
                // Always publish the trusted fence before a new attachment. The handler's
                // mutation may be committed while its separate stdout Ack is still waiting.
                fence=latest.Value.Fence;
                var ack=await emit(Reply(latest.Value,"Ack",new{stateVersion=latest.Value.StateVersion}),ct);if(ack.IsFailure)return ack;
            }
            var request=await AuthorizedAsync(fence,ct);if(request.IsFailure)return OperationResult.Fail<Unit>(request.Failure);
            var a=request.Value.Attachments.SingleOrDefault(a=>a.Id==attachmentId);
            if(a is null)return OperationResult.Fail<Unit>(FailureCode.PreconditionNotMet,"AttachmentNotSelected");
            var opened=await _attachments.OpenVerifiedAsync(fence.RequestId,a,ct);if(opened.IsFailure)return OperationResult.Fail<Unit>(opened.Failure);
            await using var lease=opened.Value;var input=lease.Stream;int count=(int)((a.ByteLength+NativeMessageCodec.MaxRawChunkLength-1)/NativeMessageCodec.MaxRawChunkLength),index=0;
            byte[] buffer=new byte[NativeMessageCodec.MaxRawChunkLength];long sent=0;
            while(sent<a.ByteLength)
            {
                ct.ThrowIfCancellationRequested();var current=await AuthorizedAsync(fence,ct);if(current.IsFailure)return OperationResult.Fail<Unit>(current.Failure);
                int length=(int)Math.Min(buffer.Length,a.ByteLength-sent);await input.ReadExactlyAsync(buffer.AsMemory(0,length),ct);
                var chunk=Reply(current.Value,"AttachmentChunk",new{attachmentId=a.Id,order=a.Order,chunkIndex=index,chunkCount=count,
                    rawLength=length,totalLength=a.ByteLength,sha256=a.Sha256.Value,data=Convert.ToBase64String(buffer,0,length)});
                var emitted=await emit(chunk,ct);if(emitted.IsFailure)return emitted;sent+=length;index++;
            }
            var final=await AuthorizedAsync(fence,ct);if(final.IsFailure)return OperationResult.Fail<Unit>(final.Failure);
            if(input.Length!=a.ByteLength||input.Position!=a.ByteLength)return OperationResult.Fail<Unit>(FailureCode.RevisionIntegrityMismatch,"AttachmentLengthMismatch");
            return await emit(Reply(final.Value,"AttachmentComplete",new{attachmentId=a.Id,order=a.Order,chunkCount=count,totalLength=a.ByteLength,sha256=a.Sha256.Value}),ct);
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {return OperationResult.Fail<Unit>(FailureCode.Cancelled,"AttachmentTransferInterrupted");}
        finally{if(held)gate.Release();}
    }
    public async Task PumpAsync(Func<NativeMessageEnvelope,CancellationToken,Task<OperationResult<Unit>>> emit,CancellationToken ct)
    {
        while(!ct.IsCancellationRequested)
        {
            await Task.Delay(500,ct);if(_connectionLock is null)continue;await HeartbeatAsync(ct);
            if(!_verified)continue;
            var list=await _store.ListAsync(ct);if(list.IsFailure)continue;
            foreach(var r in list.Value.Where(r=>r.State==AiDesignState.Queued&&r.ConnectionId==ConnectionId))
            {
                if(_dispatched.Contains((r.RequestId,r.AttemptId)))continue;
                string path=Path.Combine(_store.RequestsRoot,r.RequestId.ToString("D"),"bridge-queue.local.json");
                if(!File.Exists(path))continue;RefuseAncestry(path);
                BridgeQueueEntry? queued;
                await using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete))
                {if(file.Length>65536)continue;queued=await JsonSerializer.DeserializeAsync<BridgeQueueEntry>(file,NativeMessageCodec.JsonOptions,ct);}
                if(queued is null||queued.ConnectionId!=ConnectionId||queued.WorkspaceIdentity!=r.WorkspaceIdentity||queued.Fence!=r.Fence)continue;
                var current=await AuthorizedAsync(r.Fence,ct);if(current.IsFailure)continue;
                _dispatched.Add((r.RequestId,r.AttemptId));
                var sent=await emit(Reply(r,"Request",new{connectionId=ConnectionId,workspaceIdentity=r.WorkspaceIdentity,promptText=r.Prompt.Text,
                    handoffInstructions=r.Prompt.HandoffInstructions,attachments=r.Attachments.Select(a=>new{a.Id,a.Order,a.OriginalName,a.UploadName,a.Purpose,a.MediaType,a.ByteLength,sha256=a.Sha256.Value}).ToArray()}),ct);
                if(sent.IsFailure)throw new IOException("Native request frame failed.");
                foreach(var attachment in r.Attachments.OrderBy(a=>a.Order))
                {
                    var latest=await _store.ReadAsync(r.RequestId,ct);
                    if(latest.IsFailure||latest.Value.AttemptId!=r.AttemptId||latest.Value.Token!=r.Token)break;
                    var transfer=await StreamAttachmentAsync(latest.Value.Fence,attachment.Id,emit,ct,refreshCurrentFence:true);
                    if(transfer.IsFailure)
                    {var failed=await _store.ReadAsync(r.RequestId,ct);if(failed.IsSuccess&&AiDesignTransitions.CanStop(failed.Value.State))await _store.ChangeAsync(failed.Value.Fence,new AiDesignEvent.Interrupted(),ct);break;}
                }
            }
        }
    }
    private async Task<OperationResult<AiDesignRequest>> AuthorizedAsync(AiDesignFence fence,CancellationToken ct)
    {
        if(_connectionLock is null)return OperationResult.Fail<AiDesignRequest>(FailureCode.AdapterUnavailable,"BridgeHelloRequired");
        var config=_configuration.Validate(_origin);if(config.IsFailure)return OperationResult.Fail<AiDesignRequest>(config.Failure);
        var read=await _store.ReadAsync(fence.RequestId,ct);
        return read.IsSuccess&&FileAiDesignRequestStore.Matches(read.Value,fence)&&read.Value.ConnectionId==ConnectionId
            &&read.Value.AttemptId!=Guid.Empty&&AiDesignTransitions.CanStop(read.Value.State)?read:OperationResult.Fail<AiDesignRequest>(FailureCode.PreconditionNotMet,"BridgeFenceMismatch");
    }
    private async Task HeartbeatAsync(CancellationToken ct)
    {
        var connection=new AiDesignBridgeConnection(ConnectionId,Environment.ProcessId,Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks,
            1,AiDesignBridgeConfiguration.CurrentHostVersion,_origin,_store.WorkspaceIdentity,_contractHash,_verified,DateTimeOffset.UtcNow);
        using var staged=AiDesignOwnedStagingFiles.CreateConnection(_installation);
        await JsonSerializer.SerializeAsync(staged.Output,connection,NativeMessageCodec.JsonOptions,ct);
        await staged.PublishAsync(ct);
    }
    private async Task InterruptDisconnectedAsync(CancellationToken ct)
    {
        var list=await _store.ListAsync(ct);if(list.IsFailure)return;
        foreach(var r in list.Value.Where(r=>AiDesignTransitions.CanStop(r.State)&&r.ConnectionId!=ConnectionId))
            await _store.ChangeAsync(r.Fence,new AiDesignEvent.Interrupted(),ct);
    }
    public async ValueTask DisposeAsync()
    {
        if(_connectionLock is null)return;
        try
        {
            var list=await _store.ListAsync(CancellationToken.None);
            if(list.IsSuccess)foreach(var r in list.Value.Where(r=>r.ConnectionId==ConnectionId&&AiDesignTransitions.CanStop(r.State)))
                await _store.ChangeAsync(r.Fence,new AiDesignEvent.Interrupted(),CancellationToken.None);
        }
        finally{await _connectionLock.DisposeAsync();_connectionLock=null;}
    }
    private static NativeMessageEnvelope Reply(NativeMessageEnvelope m,string kind,object payload)=>new(1,Guid.NewGuid(),m.RequestId,m.AttemptId,m.StateVersion,m.Token,kind,JsonSerializer.SerializeToElement(payload,NativeMessageCodec.JsonOptions));
    private static NativeMessageEnvelope Reply(AiDesignRequest r,string kind,object payload)=>new(1,Guid.NewGuid(),r.RequestId,r.AttemptId,r.StateVersion,r.Token,kind,JsonSerializer.SerializeToElement(payload,NativeMessageCodec.JsonOptions));
    private static OperationResult<NativeMessageEnvelope> Fail(string code)=>OperationResult.Fail<NativeMessageEnvelope>(FailureCode.PreconditionNotMet,code);
    private static void RefuseAncestry(string path)
    {
        for(string? current=path;current is not null;current=Path.GetDirectoryName(current))
        {try{if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new IOException("Bridge reparse traversal refused.");}
            catch(FileNotFoundException){}catch(DirectoryNotFoundException){}}
    }
}
