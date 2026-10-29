using System.IO;
using System.Text;
using System.Text.Json;
using PrintFlow.Infrastructure.AiDesign;

namespace PrintFlow.Tests.Integration.AiDesign;

public sealed class NativeMessageCodecTests
{
    internal static NativeMessageEnvelope Message(string kind="Hello",object? payload=null)=>new(1,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),3,"token",kind,
        JsonSerializer.SerializeToElement(payload??new{ text="中文 🎨" },NativeMessageCodec.JsonOptions));
    [Fact]
    public async Task UTF8_JSON_has_native_32bit_length_and_partial_stream_reads_reassemble()
    {
        using var output=new MemoryStream(); var envelope=Message();
        (await NativeMessageCodec.WriteAsync(output,envelope,default)).IsSuccess.ShouldBeTrue(); byte[] frame=output.ToArray();
        BitConverter.ToUInt32(frame,0).ShouldBe((uint)(frame.Length-4));
        JsonDocument.Parse(frame.AsMemory(4)).RootElement.GetProperty("payload").GetProperty("text").GetString().ShouldBe("中文 🎨");
        using var input=new SmallReads(frame); var read=await NativeMessageCodec.ReadAsync(input,default);
        read.IsSuccess.ShouldBeTrue(); read.Value!.MessageId.ShouldBe(envelope.MessageId);
        (await NativeMessageCodec.ReadAsync(input,default)).Value.ShouldBeNull();
    }
    [Theory]
    [InlineData(0)][InlineData(-1)][InlineData(1048577)]
    public async Task Zero_negative_and_oversized_lengths_are_rejected(int length)
    { using var input=new MemoryStream(BitConverter.GetBytes(length)); (await NativeMessageCodec.ReadAsync(input,default)).IsFailure.ShouldBeTrue(); }
    [Theory]
    [InlineData(1)][InlineData(3)][InlineData(5)]
    public async Task Truncated_prefix_or_body_is_rejected(int bytes)
    { byte[] data=BitConverter.GetBytes(100).Concat(new byte[]{(byte)'{'}).Take(bytes).ToArray();using var input=new MemoryStream(data);(await NativeMessageCodec.ReadAsync(input,default)).IsFailure.ShouldBeTrue(); }
    [Fact]
    public async Task Invalid_kind_and_non_JSON_payload_never_emit_a_frame()
    {
        using var output=new MemoryStream();(await NativeMessageCodec.WriteAsync(output,Message("ExecuteCommand"),default)).IsFailure.ShouldBeTrue();output.Length.ShouldBe(0);
        using var input=new MemoryStream(BitConverter.GetBytes(2).Concat(new byte[]{0xff,0xff}).ToArray());(await NativeMessageCodec.ReadAsync(input,default)).IsFailure.ShouldBeTrue();
    }
    [Fact]
    public async Task Oversized_serialized_JSON_is_rejected_before_stdout_write()
    { using var output=new MemoryStream();(await NativeMessageCodec.WriteAsync(output,Message(payload:new{padding=new string('x',1048576)}),default)).IsFailure.ShouldBeTrue();output.Length.ShouldBe(0); }
    private sealed class SmallReads(byte[] data):MemoryStream(data)
    { public override ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken cancellationToken=default)=>base.ReadAsync(buffer[..Math.Min(2,buffer.Length)],cancellationToken); }
}
