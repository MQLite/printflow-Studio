using System.Diagnostics;
using System.IO;
using System.Text.Json;
using PrintFlow.Domain.AiDesign;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Workflow.AiDesign;

namespace PrintFlow.Infrastructure.AiDesign;

/// <summary>Local persistent-port handoff. A registry entry alone can never establish channel readiness.</summary>
public sealed class FileAiDesignChannel(AiDesignBridgeConfiguration? configuration,string? installationDirectory=null,TimeProvider? clock=null):IAiDesignChannel
{
    private readonly string _installation=Path.GetFullPath(installationDirectory??AiDesignBridgeConfiguration.InstallationDirectory);
    private readonly TimeProvider _clock=clock??TimeProvider.System;
    public async Task<OperationResult<AiDesignBridgeReadiness>> CheckAsync(CancellationToken ct)
    {
        string reason="BridgeNotConfigured",identity="";
        if(configuration is null)return Unavailable(reason,identity);
        var valid=configuration.Validate(configuration.ExtensionOrigin);if(valid.IsFailure)return Unavailable(valid.Failure.TechnicalDetail,identity);identity=valid.Value;
        try
        {
            string path=Path.Combine(_installation,"connection.local.json");ReparsePointGuard.RefuseAncestry(path);
            await using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete);
            if(input.Length>65536)return Unavailable("BridgeConnectionInvalid",identity);
            var connection=await JsonSerializer.DeserializeAsync<AiDesignBridgeConnection>(input,NativeMessageCodec.JsonOptions,ct);
            if(connection is null||connection.ConnectionId==Guid.Empty||connection.WorkspaceIdentity!=identity||connection.ExtensionOrigin!=configuration.ExtensionOrigin
                ||connection.ProtocolVersion!=1||connection.HostVersion!=AiDesignBridgeConfiguration.CurrentHostVersion)return Unavailable("BridgeVersionOrRootMismatch",identity);
            using var process=Process.GetProcessById(connection.ProcessId);
            if(process.HasExited||process.StartTime.ToUniversalTime().Ticks!=connection.ProcessStartUtcTicks||_clock.GetUtcNow()-connection.LastSeenUtc>TimeSpan.FromSeconds(10))return Unavailable("BridgeDisconnected",identity);
            string gatePath=Path.Combine(_installation,"connection.lock");ReparsePointGuard.RefuseAncestry(gatePath);
            bool held;
            try{using var gate=new FileStream(gatePath,FileMode.Open,FileAccess.ReadWrite,FileShare.None);held=false;}
            catch(IOException) when(File.Exists(gatePath)){held=true;}
            if(!held)return Unavailable("BridgeDisconnected",identity);
            bool verified=configuration.VerifiedWorkContractHash is not null&&connection.WorkContractHash==configuration.VerifiedWorkContractHash&&connection.Available;
            return OperationResult.Ok(new AiDesignBridgeReadiness(verified,1,configuration.HostVersion,configuration.ExtensionOrigin,identity,
                verified?null:"WorkContractUnverified",connection.ConnectionId));
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or System.ComponentModel.Win32Exception or OperationCanceledException)
        {return Unavailable("BridgeDisconnected",identity);}
    }
    public async Task<OperationResult<Unit>> EnqueueAsync(AiDesignRequest request,CancellationToken ct)
    {
        var readiness=await CheckAsync(ct);
        if(readiness.IsFailure||!readiness.Value.Available||configuration is null)return OperationResult.Fail<Unit>(FailureCode.AdapterUnavailable,"WorkBridgeUnavailable");
        var store=new FileAiDesignRequestStore(configuration.WorkspaceRoot);var read=await store.ReadAsync(request.RequestId,ct);
        if(read.IsFailure||!FileAiDesignRequestStore.Matches(read.Value,request.Fence)||request.State!=AiDesignState.Queued||request.ConnectionId!=readiness.Value.ConnectionId
            ||request.WorkspaceIdentity!=readiness.Value.WorkspaceIdentity)return OperationResult.Fail<Unit>(FailureCode.PreconditionNotMet,"BridgeFenceMismatch");
        try
        {
            string path=Path.Combine(store.RequestsRoot,request.RequestId.ToString("D"),"bridge-queue.local.json");ReparsePointGuard.RefuseAncestry(path);
            string gatePath=Path.Combine(Path.GetDirectoryName(path)!,".bridge-queue.lock");ReparsePointGuard.RefuseAncestry(gatePath);
            await using var gate=new FileStream(gatePath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            if(File.Exists(path))
            {
                await using var previous=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete);
                if(previous.Length>65536)return OperationResult.Fail<Unit>(FailureCode.PreconditionNotMet,"BridgeQueueInvalid");
                var old=await JsonSerializer.DeserializeAsync<BridgeQueueEntry>(previous,NativeMessageCodec.JsonOptions,ct);
                if(old is null||old.Fence.RequestId!=request.RequestId||old.WorkspaceIdentity!=request.WorkspaceIdentity)return OperationResult.Fail<Unit>(FailureCode.PreconditionNotMet,"BridgeQueueInvalid");
                if(old.Fence.AttemptId==request.AttemptId)return OperationResult.Fail<Unit>(FailureCode.PreconditionNotMet,"BridgeAttemptAlreadyQueued");
            }
            using var staged=AiDesignOwnedStagingFiles.CreateRequest(configuration.WorkspaceRoot,request.RequestId,AiDesignOwnedStagingFiles.RequestPurpose.BridgeQueue);
            await JsonSerializer.SerializeAsync(staged.Output,new BridgeQueueEntry(request.Fence,request.ConnectionId!.Value,request.WorkspaceIdentity),NativeMessageCodec.JsonOptions,ct);
            await staged.PublishAsync(ct);
            return OperationResult.Ok();
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException or OperationCanceledException)
        {return OperationResult.Fail<Unit>(FailureCode.AdapterUnavailable,"BridgeQueueFailed");}
    }
    // Success here means actual page-level stop confirmation. A local queue write, protocol Ack,
    // registry record or disconnected port cannot supply that evidence. T07 will provide the adapter.
    public Task<OperationResult<Unit>> StopAsync(AiDesignFence fence,CancellationToken ct)
        =>Task.FromResult(OperationResult.Fail<Unit>(FailureCode.AdapterUnavailable,"RemoteStopUnverified"));
    private OperationResult<AiDesignBridgeReadiness> Unavailable(string reason,string identity)=>OperationResult.Ok(new AiDesignBridgeReadiness(false,
        configuration?.ProtocolVersion??1,configuration?.HostVersion??AiDesignBridgeConfiguration.CurrentHostVersion,configuration?.ExtensionOrigin??"",identity,reason));
}
public sealed record BridgeQueueEntry(AiDesignFence Fence,Guid ConnectionId,string WorkspaceIdentity);
