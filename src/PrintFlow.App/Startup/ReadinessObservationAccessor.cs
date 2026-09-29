using PrintFlow.Workflow.Ports;

namespace PrintFlow.App.Startup;

/// <summary>Where the latest readiness observation of this application run stands (SCRUM-11152).</summary>
public enum ReadinessObservationState
{
    /// <summary>Nothing has been observed since PrintFlow started.</summary>
    NotObserved,

    /// <summary>An observation has started and has not produced a report yet.</summary>
    InProgress,

    /// <summary>The latest observation produced <see cref="ReadinessObservation.Report"/>.</summary>
    Observed,

    /// <summary>The latest observation ended without a usable report: it threw or was cancelled.</summary>
    Unfinished,
}

/// <summary>
/// One snapshot of <see cref="ReadinessObservationAccessor"/>. A fact about what was seen, never
/// a permission.
/// </summary>
/// <param name="State">Where the latest observation stands.</param>
/// <param name="Report">The report the latest observation produced; null unless <see cref="ReadinessObservationState.Observed"/>.</param>
public sealed record ReadinessObservation(
    ReadinessObservationState State,
    EnvironmentReadinessReport? Report)
{
    public static ReadinessObservation None { get; } = new(ReadinessObservationState.NotObserved, null);
}

/// <summary>
/// The latest workstation readiness report the shell observed in this application run, so Home
/// can say what Production Readiness last found (SCRUM-11152).
/// </summary>
/// <remarks>
/// A registered singleton and nothing more: it lives in memory for one process, is never
/// persisted, and starts empty, so a new run shows "not checked yet" until something observes
/// the workstation again. The authority publishes its existing observations through the
/// Workflow port; this holder runs no check and decides nothing. The gate still re-asks the
/// verifier on every Production request and never consults this.
/// <para>
/// <b>Only the latest observation counts.</b> Each observation takes a ticket when it starts. A
/// result or an unfinished end is recorded only while its ticket is still the latest, so a slow
/// earlier reading that completes after a newer one has started cannot put its answer back on
/// screen. A started observation hides the previous report until it ends: an older pass is never
/// shown as current while a newer check is running or after one did not finish.
/// </para>
/// </remarks>
public sealed class ReadinessObservationAccessor : IEnvironmentReadinessObservations
{
    private readonly object _sync = new();
    private long _latestTicket;
    private ReadinessObservation _current = ReadinessObservation.None;

    /// <summary>A hint to re-read Current; invoked after state changes, never under its lock.</summary>
    public event EventHandler? Changed;

    /// <summary>The latest observation of this run.</summary>
    public ReadinessObservation Current
    {
        get { lock (_sync) return _current; }
    }

    /// <summary>Records that an observation has started and returns its ticket.</summary>
    public long Begin()
    {
        long ticket;
        lock (_sync)
        {
            _current = new ReadinessObservation(ReadinessObservationState.InProgress, null);
            ticket = ++_latestTicket;
        }
        Notify();
        return ticket;
    }

    /// <summary>Records the report an observation produced, unless a newer one has started.</summary>
    public void Complete(long ticket, EnvironmentReadinessReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        lock (_sync)
        {
            if (ticket != _latestTicket) return;
            _current = new ReadinessObservation(ReadinessObservationState.Observed, report);
        }
        Notify();
    }

    /// <summary>Records that an observation ended without a report, unless a newer one has started.</summary>
    public void Abandon(long ticket)
    {
        lock (_sync)
        {
            if (ticket != _latestTicket || _current.State != ReadinessObservationState.InProgress) return;
            _current = new ReadinessObservation(ReadinessObservationState.Unfinished, null);
        }
        Notify();
    }

    /// <summary>
    /// Withdraws an unchanged snapshot when a passive request was cancelled before its worker
    /// entered the authority. Never supersedes an observation that started in the meantime.
    /// </summary>
    public void AbandonIfUnchanged(ReadinessObservation captured)
    {
        lock (_sync)
        {
            if (!ReferenceEquals(_current, captured)) return;
            ++_latestTicket;
            _current = new ReadinessObservation(ReadinessObservationState.Unfinished, null);
        }
        Notify();
    }

    private void Notify()
    {
        foreach (EventHandler subscriber in Changed?.GetInvocationList() ?? [])
        {
            try { subscriber(this, EventArgs.Empty); }
            catch (Exception) { /* Presentation failure cannot affect the observation's source. */ }
        }
    }
}
