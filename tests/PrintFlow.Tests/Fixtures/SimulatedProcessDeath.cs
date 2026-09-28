using System.Collections.Concurrent;
using System.Reflection;
using PrintFlow.Domain.Ids;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Fixtures;

/// <summary>
/// Existing crash tests abandon a never-completing fake adapter Task, then model the next
/// process in this same testhost. A true process death would discard static semaphores.
/// Remove only gates for attempts durably interrupted by that simulated startup recovery;
/// the abandoned fake Task still cannot resume or commit because it waits forever.
/// Product gate identity stays stable for the lifetime of a real process.
/// </summary>
internal static class SimulatedProcessDeath
{
    public static void BeginRecoveredProcess(StartupRecoveryReport report)
    {
        FieldInfo field = typeof(SessionCompletionGate).GetField("Gates",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var gates = (ConcurrentDictionary<SessionId, SemaphoreSlim>)field.GetValue(null)!;
        foreach (SessionId id in report.Entries
            .Where(e => e.Action == StartupRecoveryAction.AttemptInterrupted && e.SessionId is not null)
            .Select(e => e.SessionId!.Value).Distinct())
            gates.TryRemove(id, out _);
    }
}
