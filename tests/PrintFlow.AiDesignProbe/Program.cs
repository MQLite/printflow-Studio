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
}
