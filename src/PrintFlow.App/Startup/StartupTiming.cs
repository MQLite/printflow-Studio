using System.Diagnostics;
using System.Diagnostics.Tracing;

namespace PrintFlow.App.Startup;

[EventSource(Name = "PrintFlow-Studio-Startup", Guid = "650da111-e3f0-4055-819d-46c2bfcbfd07")]
internal sealed class StartupTiming : EventSource
{
    internal static readonly StartupTiming Log = new();
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();

    [NonEvent]
    internal void Mark(string stage)
    {
        if (IsEnabled()) StageReached(stage, _elapsed.ElapsedMilliseconds);
    }

    [Event(1, Level = EventLevel.Informational)]
    public void StageReached(string stage, long elapsedMilliseconds) => WriteEvent(1, stage, elapsedMilliseconds);
}
