using System.IO;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Workspace;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace PrintFlow.Infrastructure.AiDesign;

public sealed record AiDesignBridgeConfiguration(int ProtocolVersion,string HostVersion,string ExtensionOrigin,string WorkspaceRoot,
    string DownloadRoot,string? VerifiedWorkContractHash=null)
{
    public const string CurrentHostVersion="1.0.0", HostName="com.printflow.ai_design";
    public static string InstallationDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PrintFlowStudio","AiDesignBridge");
    public static string ConfigurationPath => Path.Combine(InstallationDirectory,"bridge.local.json");
    public OperationResult<string> Validate(string callerOrigin)
    {
        try
        {
            if(ProtocolVersion!=1||HostVersion!=CurrentHostVersion)return OperationResult.Fail<string>(FailureCode.AdapterUnavailable,"BridgeVersionMismatch");
            if(!Regex.IsMatch(ExtensionOrigin,@"\Achrome-extension://[a-p]{32}/\z",RegexOptions.CultureInvariant)||callerOrigin!=ExtensionOrigin)
                return OperationResult.Fail<string>(FailureCode.AdapterUnavailable,"BridgeOriginMismatch");
            foreach(string root in new[]{WorkspaceRoot,DownloadRoot})
            {
                if(!Path.IsPathFullyQualified(root)||!Directory.Exists(root))return OperationResult.Fail<string>(FailureCode.AdapterUnavailable,"BridgeRootInvalid");
                ReparsePointGuard.RefuseAncestry(root);
            }
            if(VerifiedWorkContractHash is not null&&!PrintFlow.Domain.Files.Sha256.TryParse(VerifiedWorkContractHash,out _))
                return OperationResult.Fail<string>(FailureCode.AdapterUnavailable,"WorkContractUnverified");
            return OperationResult.Ok(new FileAiDesignRequestStore(WorkspaceRoot).WorkspaceIdentity);
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException)
        {return OperationResult.Fail<string>(FailureCode.AdapterUnavailable,"BridgeConfigurationInvalid");}
    }
    public static async Task<OperationResult<AiDesignBridgeConfiguration>> LoadAsync(string path,CancellationToken ct)
    {
        try
        {
            ReparsePointGuard.RefuseAncestry(path);await using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
            if(input.Length>65536)return OperationResult.Fail<AiDesignBridgeConfiguration>(FailureCode.AdapterUnavailable,"BridgeConfigurationInvalid");
            var config=await JsonSerializer.DeserializeAsync<AiDesignBridgeConfiguration>(input,NativeMessageCodec.JsonOptions,ct);
            if(config is null)return OperationResult.Fail<AiDesignBridgeConfiguration>(FailureCode.AdapterUnavailable,"BridgeConfigurationInvalid");
            var valid=config.Validate(config.ExtensionOrigin);return valid.IsSuccess?OperationResult.Ok(config):OperationResult.Fail<AiDesignBridgeConfiguration>(valid.Failure);
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException or OperationCanceledException or ArgumentException)
        {return OperationResult.Fail<AiDesignBridgeConfiguration>(FailureCode.AdapterUnavailable,"BridgeNotConfigured");}
    }
}
public sealed record AiDesignBridgeConnection(Guid ConnectionId,int ProcessId,long ProcessStartUtcTicks,int ProtocolVersion,string HostVersion,
    string ExtensionOrigin,string WorkspaceIdentity,string? WorkContractHash,bool Available,DateTimeOffset LastSeenUtc);
