using System.Windows;
using System.Windows.Threading;
using PrintFlow.Domain.Ids;
using PrintFlow.Workflow.AiDesign;

namespace PrintFlow.App.ViewModels.AiDesign;

/// <summary>In-process hints scoped to the exact persisted Work attempt and state version.</summary>
public sealed class WorkTaskTracker : IWorkResultProgressObserver, IWorkResultProgressReader
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, WorkResultObservation> observations = [];
    private readonly Action<Action> dispatch;

    public WorkTaskTracker(Action<Action>? dispatch = null) => this.dispatch = dispatch ?? DispatchToUi;
    public event Action<Guid>? TaskChanged;
    public event Action<SessionId>? ResultImported;

    public WorkResultObservation? Find(Guid requestId)
    {
        lock (gate) return observations.GetValueOrDefault(requestId);
    }

    public void Report(WorkResultObservation observation)
    {
        if (observation.AttemptId == Guid.Empty) return;
        lock (gate)
        {
            if (observations.TryGetValue(observation.RequestId, out var previous) &&
                (previous.StateVersion > observation.StateVersion ||
                 (previous.StateVersion == observation.StateVersion &&
                  (previous.AttemptId != observation.AttemptId || previous.Stage >= observation.Stage)))) return;
            observations[observation.RequestId] = observation;
        }

        dispatch(() =>
        {
            if (observation.Stage == WorkResultStage.Imported && observation.ImportedSessionId is { } session)
                ResultImported?.Invoke(session);
            TaskChanged?.Invoke(observation.RequestId);
        });
    }

    public void RequestChanged(Guid requestId) => dispatch(() => TaskChanged?.Invoke(requestId));

    private static void DispatchToUi(Action callback)
    {
        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
            dispatcher.BeginInvoke(DispatcherPriority.DataBind, callback);
        else callback();
    }
}
