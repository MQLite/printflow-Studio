using System.Windows.Threading;

namespace PrintFlow.WorkstationEntry;

/// <summary>
/// Turns an unexpected dispatcher fault into an owned stop rather than an escape past the settled
/// shutdown path (SCRUM-11154 F-V7).
/// </summary>
/// <remarks>
/// The first fault is recorded and ends the owned wait at once; the run is then torn down by the
/// existing bounded finally (quiescence or exit 4) and reports <see cref="ExitCode"/>. Marking the
/// fault handled exists only so that path can run on this dispatcher: the run is ended, never
/// continued, no prepared record is written after a fault, and success is never reported.
/// </remarks>
public sealed class HostFaultGate : IDisposable
{
    /// <summary>The host's exit code after an unexpected fault (0 ok, 2 refused, 3 partial, 4 nonquiescent).</summary>
    public const int ExitCode = 5;

    private readonly Dispatcher dispatcher;
    private readonly TaskCompletionSource<Exception> first = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int count;
    private CancellationTokenSource? run;
    private System.Windows.UIElement? input;

    /// <summary>The run's own cancellation, cancelled on the first fault in every mode, so no
    /// noninteractive await keeps executing after it.</summary>
    public void CancelOnFault(CancellationTokenSource source) => run = source;

    /// <summary>The visible content, disabled synchronously on the first fault so no further
    /// operator input reaches the product before teardown.</summary>
    public void DisableOnFault(System.Windows.UIElement element) => input = element;

    public HostFaultGate(Dispatcher dispatcher)
    {
        this.dispatcher = dispatcher;
        dispatcher.UnhandledException += OnUnhandled;
    }

    public Task<Exception> Faulted => first.Task;

    public Exception? Fault => first.Task.IsCompleted ? first.Task.Result : null;

    public int FaultCount => Volatile.Read(ref count);

    /// <summary>Completes on a normal close or on the first fault, whichever happens first.</summary>
    public Task WhenClosedOrFaulted(Task closed) => Task.WhenAny(closed, first.Task);

    /// <summary>The code to report: <paramref name="normal"/> only when no fault was ever recorded.</summary>
    public int Outcome(int normal) => Fault is null ? normal : ExitCode;

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Interlocked.Increment(ref count);
        first.TrySetResult(e.Exception);
        e.Handled = true;
        // Stop first, then let the owned path tear down. Failures here cannot revive the run:
        // the fault is already recorded and Outcome can no longer report success.
        if (input is { } element) element.IsEnabled = false;
        try { run?.Cancel(); }
        catch (Exception ex) when (ex is ObjectDisposedException or AggregateException) { }
    }

    public void Dispose() => dispatcher.UnhandledException -= OnUnhandled;
}
