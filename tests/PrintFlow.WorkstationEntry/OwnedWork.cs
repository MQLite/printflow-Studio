using PrintFlow.App.ViewModels;

namespace PrintFlow.WorkstationEntry;

public sealed class OwnedWork
{
    private readonly HashSet<Task> tasks = [];
    private readonly HashSet<object> screens = [];
    private readonly object sync = new();
    private bool closed;
    private bool lateRegistration;
    public void Track(Task task) { lock (sync) if (tasks.Add(task) && closed) lateRegistration = true; }
    public void ObserveScreen(object? screen)
    {
        if (screen is null) return;
        lock (sync)
        {
            screens.Add(screen);
            switch (screen)
            {
                case HomeViewModel home: Track(home.ThumbnailsLoaded); break;
                case WorkflowSelectionViewModel selection: Track(selection.PreviewLoaded); break;
                case SessionViewModel session:
                    Track(session.PreviewsLoaded); Track(session.PreflightLoaded); Track(session.FinalSaveFactsLoaded); break;
            }
        }
    }
    public void CloseRegistration()
    {
        lock (sync)
        {
            foreach (object screen in screens.ToArray()) ObserveScreen(screen);
            closed = true;
        }
    }
    public async Task<bool> WaitForQuiescenceAsync(TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            Task[] pending;
            lock (sync)
            {
                foreach (object screen in screens.ToArray()) ObserveScreen(screen);
                pending = tasks.Where(task => !task.IsCompleted).ToArray();
            }
            if (pending.Length == 0) { lock (sync) return closed && !lateRegistration; }
            TimeSpan remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) return false;
            try { await Task.WhenAll(pending).WaitAsync(remaining); }
            catch (TimeoutException) { return false; }
            catch (OperationCanceledException) { }
            catch (Exception) when (pending.All(task => task.IsCompleted)) { }
            // Re-snapshot after awaiting: a completing operation can register a successor.
        }
    }
    public int PendingCount { get { lock (sync) return tasks.Count(task => !task.IsCompleted); } }
    public bool LateRegistration { get { lock (sync) return lateRegistration; } }
}
