using System.IO;
using System.Text.Json;
using System.Text;
using System.Text.Encodings.Web;
using PrintFlow.Domain.Results;

namespace PrintFlow.Infrastructure.AiDesign;

public sealed record NativeMessageEnvelope(int ProtocolVersion, Guid MessageId, Guid RequestId, Guid AttemptId,
    long StateVersion, string Token, string Kind, JsonElement Payload);
public static class NativeMessageCodec
{
    public const int MaxFrameLength=1024*1024, MaxRawChunkLength=256*1024;
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web) { Encoder=JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly HashSet<string> Kinds = ["Hello","Ready","Request","AttachmentChunk","AttachmentComplete","UploadReceipt","SubmitPermit","SubmitReceipt","Progress","DownloadRegistered","DownloadComplete","Stop","Failure","Ack"];
    public static bool IsValid(NativeMessageEnvelope m) => m.ProtocolVersion==1 && m.MessageId!=Guid.Empty && Kinds.Contains(m.Kind)
        && m.Token is not null && m.StateVersion>=0 && m.Payload.ValueKind==JsonValueKind.Object;
    public static async Task<OperationResult<NativeMessageEnvelope?>> ReadAsync(Stream input,CancellationToken ct)
    {
        try
        {
            byte[] header=new byte[4]; int first=await input.ReadAsync(header.AsMemory(0,1),ct);
            if(first==0)return OperationResult.Ok<NativeMessageEnvelope?>(null);
            await input.ReadExactlyAsync(header.AsMemory(1),ct);uint length=BitConverter.ToUInt32(header);
            if(length==0||length>MaxFrameLength)return BadRead();
            byte[] bytes=new byte[(int)length];await input.ReadExactlyAsync(bytes,ct);
            string json=new UTF8Encoding(false,true).GetString(bytes);using var document=JsonDocument.Parse(json);
            if(document.RootElement.ValueKind!=JsonValueKind.Object)return BadRead();
            var keys=document.RootElement.EnumerateObject().Select(p=>p.Name).ToArray();
            if(keys.Length!=8||keys.Distinct(StringComparer.Ordinal).Count()!=8)return BadRead();
            var m=JsonSerializer.Deserialize<NativeMessageEnvelope>(json,JsonOptions);
            return m is not null&&IsValid(m)?OperationResult.Ok<NativeMessageEnvelope?>(m):BadRead();
        }
        catch(Exception ex) when(ex is IOException or JsonException or DecoderFallbackException or OperationCanceledException)
        { return BadRead(); }
    }
    public static async Task<OperationResult<Unit>> WriteAsync(Stream output,NativeMessageEnvelope message,CancellationToken ct)
    {
        try
        {
            if(!IsValid(message))return BadWrite();byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(message,JsonOptions);
            if(bytes.Length==0||bytes.Length>MaxFrameLength)return BadWrite();
            await output.WriteAsync(BitConverter.GetBytes((uint)bytes.Length),ct);await output.WriteAsync(bytes,ct);await output.FlushAsync(ct);return OperationResult.Ok();
        }
        catch(Exception ex) when(ex is IOException or JsonException or OperationCanceledException) { return BadWrite(); }
    }
    private static OperationResult<NativeMessageEnvelope?> BadRead()=>OperationResult.Fail<NativeMessageEnvelope?>(FailureCode.PreconditionNotMet,"NativeFrameInvalid");
    private static OperationResult<Unit> BadWrite()=>OperationResult.Fail<Unit>(FailureCode.PreconditionNotMet,"NativeFrameInvalid");
}
