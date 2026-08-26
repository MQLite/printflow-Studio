using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Workflow.Services;

/// <summary>
/// What PrintFlow's automation is doing right now (Epic 11300 Part D2A §28).
/// </summary>
/// <remarks>
/// Deliberately a small closed set. It answers "is there something to stop", not "what is the
/// external application showing" — the second question belongs to the adapter and its signed
/// evidence, and a shell that could ask it would be a shell that could act on the answer.
/// </remarks>
public enum AutomationRuntimeState
{
    /// <summary>No attempt is running. Neither Stop nor Take Over means anything.</summary>
    Idle,

    /// <summary>An attempt is running and has not been asked to stop.</summary>
    Running,

    /// <summary>Stop was requested and the run is unwinding.</summary>
    StopRequested,

    /// <summary>Take Over was requested and the run is unwinding, sending nothing further.</summary>
    TakeOverRequested,
}

/// <summary>
/// The authoritative live read model for the operator's Stop and Take Over controls
/// (Epic 11300 Part D2A §24, §25, §28).
/// </summary>
/// <remarks>
/// Separate from <see cref="SessionView"/> on purpose, and the separation is the point.
/// <see cref="SessionView"/> is built from committed metadata and is returned when a command
/// <i>finishes</i>; while an attempt is in flight there is by definition no newer one, so a
/// Stop control derived from it would be derived from the state before the run began. This
/// record is read from the run itself.
/// <para>
/// <see cref="CanStopAutomation"/> and <see cref="CanTakeOverAutomation"/> are computed here
/// rather than in XAML or a view model (§24, §28): a screen that worked out for itself when
/// stopping is meaningful would be a second copy of the rule, and the first thing such a copy
/// does is offer Stop on an idle review screen.
/// </para>
/// </remarks>
/// <param name="SessionId">The session the run belongs to, or <c>null</c> when idle.</param>
/// <param name="AttemptId">The attempt row the run is writing to, or <c>null</c> when idle.</param>
/// <param name="Step">The step being attempted, or <c>null</c> when idle.</param>
/// <param name="State">Whether anything is running and whether it has been asked to stop.</param>
/// <param name="Phase">How far the external operation has got, as the adapter last reported it.</param>
/// <param name="DrivesExternalApplication">
/// Whether this run drives an external application at all. False for a deterministic trim, a
/// promotion and a manual crop — none of which has anything for an operator to take over.
/// </param>
public sealed record AutomationRuntimeView(
    SessionId? SessionId,
    AttemptId? AttemptId,
    StepKind? Step,
    AutomationRuntimeState State,
    ExternalOperationPhase Phase,
    bool DrivesExternalApplication)
{
    /// <summary>The idle answer, for a session with nothing running.</summary>
    public static readonly AutomationRuntimeView Idle = new(
        null, null, null, AutomationRuntimeState.Idle, ExternalOperationPhase.NotStarted, false);

    /// <summary>
    /// Whether a Stop control should be offered (§24).
    /// </summary>
    /// <remarks>
    /// Only while something is genuinely running and has not already been asked to stop. An
    /// idle Waiting or ReviewRequired screen has nothing to stop, and a second Stop on a run
    /// that is already unwinding would invite a second cancel invocation — which §9 forbids.
    /// </remarks>
    public bool CanStopAutomation => State == AutomationRuntimeState.Running;

    /// <summary>
    /// Whether a Take Over control should be offered (§25).
    /// </summary>
    /// <remarks>
    /// Narrower than <see cref="CanStopAutomation"/> by one condition: takeover is about
    /// <i>ownership of an external application</i>, so it is offered only where external
    /// automation has actually begun. A deterministic trim has no Meitu to hand over, and
    /// offering the control there would be the "generic session button visible at all times"
    /// §25 rules out.
    /// </remarks>
    public bool CanTakeOverAutomation =>
        State == AutomationRuntimeState.Running && DrivesExternalApplication;

    /// <summary>What PrintFlow would report about the external application if it stopped now.</summary>
    public RetainedExternalState RetainedExternalState => DrivesExternalApplication
        ? AutomationStopPolicy.RetainedFor(Phase, cancelled: false)
        : Ports.RetainedExternalState.None;
}

/// <summary>
/// The one place that knows which attempt is currently driving automation, and the only route
/// by which an operator's Stop reaches it (Epic 11300 Part D2A §28, §32).
/// </summary>
/// <remarks>
/// A registry rather than a field on <see cref="SessionService"/> because the two accesses
/// happen on different threads by construction: the run holds a thread inside
/// <c>ExecuteAsync</c>, and the Stop arrives from the UI thread while that call has not
/// returned. Anything simpler would be a data race on the exact path whose correctness matters
/// most.
/// <para>
/// It is <b>not</b> the automation lock and does not replace it (§32). The lock is the
/// persisted, machine-wide, single-writer guarantee over Meitu and Photoshop; this is an
/// in-process record of "which run can be asked to stop", which necessarily disappears when
/// the process does. Startup recovery remains the authority for a lock left behind by a
/// process that died.
/// </para>
/// </remarks>
public sealed class AutomationRunRegistry
{
    private readonly object _gate = new();
    private RunningAutomation? _running;

    /// <summary>Raised whenever the runtime view changes, so a screen can re-read it.</summary>
    /// <remarks>
    /// Raised outside the lock. A handler that called back into the registry while it was held
    /// would deadlock, and the handler here is a view model that does exactly that kind of
    /// thing when it refreshes.
    /// </remarks>
    public event EventHandler<AutomationRuntimeView>? Changed;

    /// <summary>The current runtime view, for any session.</summary>
    public AutomationRuntimeView Current
    {
        get
        {
            lock (_gate)
            {
                return _running?.ToView() ?? AutomationRuntimeView.Idle;
            }
        }
    }

    /// <summary>The runtime view for one session, which is idle unless that session is the one running.</summary>
    public AutomationRuntimeView For(SessionId session)
    {
        lock (_gate)
        {
            return _running is { } running && running.SessionId == session
                ? running.ToView()
                : AutomationRuntimeView.Idle;
        }
    }

    /// <summary>
    /// Registers a run that is about to begin, and returns its stop signal.
    /// </summary>
    /// <remarks>
    /// Returns the signal rather than storing it somewhere the adapter must find, so the object
    /// the adapter reads and the object <see cref="RequestStop"/> writes are the same one by
    /// construction rather than by lookup.
    /// </remarks>
    public IAutomationStopSignal Begin(
        SessionId session, AttemptId attempt, StepKind step, bool drivesExternalApplication)
    {
        RunningAutomation run = new(session, attempt, step, drivesExternalApplication, Notify);
        AutomationRuntimeView view;
        lock (_gate)
        {
            _running = run;
            view = run.ToView();
        }

        Changed?.Invoke(this, view);
        return run;
    }

    /// <summary>Ends the run, whatever its outcome. Idempotent.</summary>
    public void End(AttemptId attempt)
    {
        bool ended;
        lock (_gate)
        {
            ended = _running?.AttemptId == attempt;
            if (ended)
            {
                _running = null;
            }
        }

        if (ended)
        {
            Changed?.Invoke(this, AutomationRuntimeView.Idle);
        }
    }

    /// <summary>
    /// Records the operator's Stop or Take Over request against the run in flight.
    /// </summary>
    /// <remarks>
    /// Refuses rather than silently succeeding when there is nothing running or the run belongs
    /// to another session. "Stop" that quietly does nothing is worse than a refusal: the
    /// operator is entitled to know that the thing they were trying to stop was not stopped by
    /// them.
    /// <para>
    /// A second request is refused too. §9 permits exactly one cancel invocation, and the way a
    /// second one gets invoked is an operator pressing Stop twice while the first is still
    /// unwinding.
    /// </para>
    /// </remarks>
    public OperationResult<Unit> RequestStop(SessionId session, AutomationStopMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            return OperationResult.Fail<Unit>(
                FailureCode.PreconditionNotMet, $"'{mode}' is not a stop mode PrintFlow understands.");
        }

        RunningAutomation? run;
        AutomationRuntimeView view;
        lock (_gate)
        {
            run = _running;
            if (run is null || run.SessionId != session)
            {
                return OperationResult.Fail<Unit>(
                    FailureCode.PreconditionNotMet,
                    "No automated operation is running for this session, so there is nothing to stop. " +
                    "Nothing was sent to any external application.");
            }

            if (run.RequestedMode is { } already)
            {
                return OperationResult.Fail<Unit>(
                    FailureCode.PreconditionNotMet,
                    $"A '{already}' request is already being carried out for this operation. " +
                    "PrintFlow does not repeat it; no further input was produced.");
            }

            run.Request(mode);
            view = run.ToView();
        }

        Changed?.Invoke(this, view);
        return OperationResult.Ok();
    }

    private void Notify(AutomationRuntimeView view) => Changed?.Invoke(this, view);

    /// <summary>
    /// One in-flight run: the stop signal the adapter reads, and the runtime state the screen
    /// reads, kept as a single object so the two can never disagree.
    /// </summary>
    private sealed class RunningAutomation(
        SessionId sessionId,
        AttemptId attemptId,
        StepKind step,
        bool drivesExternalApplication,
        Action<AutomationRuntimeView> notify) : IAutomationStopSignal
    {
        private readonly object _gate = new();
        private AutomationStopMode? _mode;
        private ExternalOperationPhase _phase = ExternalOperationPhase.NotStarted;
        private bool _cancelled;

        public SessionId SessionId { get; } = sessionId;

        public AttemptId AttemptId { get; } = attemptId;

        public AutomationStopMode? RequestedMode
        {
            get
            {
                lock (_gate)
                {
                    return _mode;
                }
            }
        }

        public ExternalOperationPhase Phase
        {
            get
            {
                lock (_gate)
                {
                    return _phase;
                }
            }
        }

        /// <inheritdoc />
        public bool OperationCancelWasInvoked
        {
            get
            {
                lock (_gate)
                {
                    return _cancelled;
                }
            }
        }

        public void Request(AutomationStopMode mode)
        {
            lock (_gate)
            {
                _mode = mode;
            }
        }

        public void ReportPhase(ExternalOperationPhase phase)
        {
            AutomationRuntimeView view;
            lock (_gate)
            {
                if (_phase == phase)
                {
                    return;
                }

                _phase = phase;
                view = ToView();
            }

            notify(view);
        }

        public void ReportOperationCancelled()
        {
            lock (_gate)
            {
                _cancelled = true;
            }
        }

        public AutomationRuntimeView ToView()
        {
            AutomationStopMode? mode;
            ExternalOperationPhase phase;
            lock (_gate)
            {
                mode = _mode;
                phase = _phase;
            }

            AutomationRuntimeState state = mode switch
            {
                AutomationStopMode.StopOperation => AutomationRuntimeState.StopRequested,
                AutomationStopMode.TakeOver => AutomationRuntimeState.TakeOverRequested,
                _ => AutomationRuntimeState.Running,
            };

            return new AutomationRuntimeView(
                SessionId, AttemptId, step, state, phase, drivesExternalApplication);
        }
    }
}
