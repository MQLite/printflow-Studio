using System.Collections.Concurrent;
using PrintFlow.Domain.Ids;

namespace PrintFlow.Workflow.Services;

/// <summary>
/// Serialises one session's workflow mutation, completion/retention, recovery and external
/// delivery authority checks within the single application instance. This is independent of
/// the workstation automation lock; stop signaling deliberately remains outside this gate.
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
