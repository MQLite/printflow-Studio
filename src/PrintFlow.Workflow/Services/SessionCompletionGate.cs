using System.Collections.Concurrent;
using PrintFlow.Domain.Ids;

namespace PrintFlow.Workflow.Services;

/// <summary>
/// Serialises completion maintenance with AddAnotherSize within the single application
/// instance. Startup already runs behind ApplicationStartup's process-wide single-instance
/// guard, before operator commands. This is independent of the external automation lock.
/// </summary>
internal static class SessionCompletionGate
{
    private static readonly ConcurrentDictionary<SessionId, SemaphoreSlim> Gates = new();

    public static async Task<IDisposable> EnterAsync(SessionId id, CancellationToken cancellationToken)
    {
        SemaphoreSlim gate = Gates.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Lease(gate);
    }

    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}
