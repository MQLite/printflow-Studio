using PrintFlow.Infrastructure.AiDesign;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using PrintFlow.Domain.Results;

namespace PrintFlow.NativeHost;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if(args.Length is <1 or >2||!Regex.IsMatch(args[0],@"\Achrome-extension://[a-p]{32}/\z",RegexOptions.CultureInvariant)
            ||(args.Length==2&&!Regex.IsMatch(args[1],@"\A--parent-window=\d+\z",RegexOptions.CultureInvariant)))
        {Console.Error.WriteLine("BridgeOriginMismatch");return 2;}
        var configuration=await AiDesignBridgeConfiguration.LoadAsync(AiDesignBridgeConfiguration.ConfigurationPath,CancellationToken.None);
        if(configuration.IsFailure){Console.Error.WriteLine("BridgeNotConfigured");return 2;}
        int code=await RunAsync(Console.OpenStandardInput(),Console.OpenStandardOutput(),configuration.Value,args[0],AiDesignBridgeConfiguration.InstallationDirectory,CancellationToken.None);
        if(code!=0)Console.Error.WriteLine("BridgeSessionRejected");return code;
    }
    public static async Task<int> RunAsync(Stream input,Stream output,AiDesignBridgeConfiguration config,string origin,string installation,CancellationToken ct)
    {
        using var lifetime=CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var stdoutGate=new SemaphoreSlim(1,1);
        await using var session=new NativeHostSession(config,origin,installation);
        async Task<OperationResult<Unit>> Send(NativeMessageEnvelope m,CancellationToken token)
        {await stdoutGate.WaitAsync(token);try{return await NativeMessageCodec.WriteAsync(output,m,token);}finally{stdoutGate.Release();}}
        Task pump=session.PumpAsync(Send,lifetime.Token);
        try
        {
            while(true)
            {
                var reading=NativeMessageCodec.ReadAsync(input,lifetime.Token);
                if(await Task.WhenAny(reading,pump)==pump)return 2;
                var read=await reading;
                if(read.IsFailure)return 2;if(read.Value is null)return 0;
                var handled=await session.HandleAsync(read.Value,lifetime.Token);
                var reply=handled.IsSuccess?handled.Value:new NativeMessageEnvelope(1,Guid.NewGuid(),read.Value.RequestId,read.Value.AttemptId,
                    read.Value.StateVersion,read.Value.Token,"Failure",JsonSerializer.SerializeToElement(new{code=handled.Failure.TechnicalDetail},NativeMessageCodec.JsonOptions));
                if((await Send(reply,lifetime.Token)).IsFailure)return 2;
            }
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or OperationCanceledException or JsonException){return 2;}
        finally
        {
            lifetime.Cancel();try{await pump;}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or OperationCanceledException or JsonException){}
        }
    }
}
