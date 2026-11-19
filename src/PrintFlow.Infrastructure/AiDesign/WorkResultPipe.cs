using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;

namespace PrintFlow.Infrastructure.AiDesign;

internal sealed record WorkResultPipeRequest(string Operation, string Bearer, Guid RequestId, Guid AttemptId,
    string? ReadTicket = null, Guid? ObjectVersion = null, string[]? ReferenceSha256 = null,
    Guid? WorkConversationId = null, string? ReturnMediaType = null);

internal interface IWorkResultPipe
{
    Task<byte[]> ExchangeAsync(string name, WorkResultPipeRequest request, int limit, CancellationToken ct);
}

internal sealed class WorkResultPipe : IWorkResultPipe
{
    internal static bool IsName(string? name) => name is { Length: >= 12 and <= 80 } &&
        name.StartsWith("PrintFlow-", StringComparison.Ordinal) &&
        name.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');

    public async Task<byte[]> ExchangeAsync(string name, WorkResultPipeRequest request, int limit, CancellationToken ct)
    {
        if (!IsName(name)) throw new IOException("Invalid Work result pipe.");
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(3000, ct);
        byte[] message = JsonSerializer.SerializeToUtf8Bytes(request, FileAiDesignRequestStore.JsonOptions);
        await client.WriteAsync(message, ct);
        await client.WriteAsync("\n"u8.ToArray(), ct);

        byte[] header = new byte[sizeof(int)];
        await client.ReadExactlyAsync(header, ct);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length < 1 || length > limit) throw new IOException("Invalid Work receiver response.");
        byte[] payload = new byte[length];
        await client.ReadExactlyAsync(payload, ct);
        return payload;
    }
}
