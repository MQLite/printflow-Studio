using PrintFlow.Domain.Results;

namespace PrintFlow.Workflow.Ports;

/// <summary>
/// What the operator asked PrintFlow to do about a running automated operation
/// (Epic 11300 Part D2A §3, §17, §27).
/// </summary>
/// <remarks>
/// Two values, deliberately not three. Neither of them means "terminate the external
/// application": there is no member here that an adapter could read as permission to end a
/// process, and no API exists that could carry out such an instruction if there were (§3).
/// Epic 11300 Part D2B rejects force termination as product policy: exact process identity does
/// not prove that the process contains only the current Attempt's document, so destructive
/// process control is not a third value quietly added to this enum.
/// <para>
/// The distinction between the two is <b>who owns the external application afterwards</b>, and
/// that is why they cannot be collapsed into one button (§27):
/// </para>
/// <list type="bullet">
///   <item><see cref="StopOperation"/> — PrintFlow stops its own orchestration and, where the
///   phase permits it and an exact signed control has been resolved, asks the external
///   application to abandon the work it is doing. Nobody takes over; the operation is being
///   called off.</item>
///   <item><see cref="TakeOver"/> — PrintFlow stops its own orchestration and does
///   <i>nothing else at all</i>. It sends no cancel, dismisses nothing, closes nothing. The
///   operator now owns the external application, including whatever it is in the middle of
///   (§19, §20).</item>
/// </list>
/// </remarks>
public enum AutomationStopMode
{
    /// <summary>Stop this PrintFlow operation safely, cancelling the external work if that can be proven safe.</summary>
    StopOperation,

    /// <summary>Stop PrintFlow automation and leave the external application entirely alone for the operator.</summary>
    TakeOver,
}

/// <summary>
/// How far one external operation has got, as the only thing Stop is allowed to branch on
/// (Epic 11300 Part D2A §4).
/// </summary>
/// <remarks>
/// §4 forbids one generic "cancel everything" implementation, and this enum is what makes that
/// structural rather than a matter of remembering: the adapter reports the phase it is actually
/// in, and <see cref="AutomationStopResolution"/> is a pure function of that phase plus the
/// requested <see cref="AutomationStopMode"/>. A phase nobody reported stays
/// <see cref="NotStarted"/>, which is the safest of the eight.
/// <para>
/// The values are stated in the order the phases occur, and the ordering is meaningful in
/// exactly one place — <see cref="AutomationStopPolicy"/>'s irreversibility rule — where
/// everything from <see cref="ExportConfirmed"/> onward is past the point PrintFlow can
/// unmake. <see cref="UnknownOrBlocked"/> sits outside that order on purpose: it is not "late",
/// it is "PrintFlow cannot say", and it is the one phase in which no input of any kind is
/// permitted (§10, §20).
/// </para>
/// </remarks>
public enum ExternalOperationPhase
{
    /// <summary>
    /// Phase A. Nothing has been asked of the external application's operation yet
    /// (§5).
    /// </summary>
    /// <remarks>
    /// The default, and deliberately so. An adapter that reports nothing is treated as having
    /// produced no operation input, which makes the conservative answer — stop the orchestration
    /// and touch nothing — the one a silent adapter gets.
    /// </remarks>
    NotStarted,

    /// <summary>Phase B. The operation was invoked, but Busy has not been positively observed.</summary>
    /// <remarks>
    /// Not a phase in which a cancel may be attempted. The signed cancel affordance is part of
    /// the Busy surface, so before Busy has been seen there is nothing whose structure has been
    /// proven — and §7 requires current-load Busy correlation before any cancel is eligible.
    /// </remarks>
    OperationRequested,

    /// <summary>Phase C. The operation is positively Busy — the only phase in which a cancel may be resolved (§9).</summary>
    Busy,

    /// <summary>Phase D. Processing finished; no export has been started (§13).</summary>
    CompletedBeforeExport,

    /// <summary>Phase E. An export surface or destination dialog is open; no irreversible confirm has happened (§14).</summary>
    ExportPrepared,

    /// <summary>Phase F. The irreversible export confirm was invoked; output may be being written (§15).</summary>
    ExportConfirmed,

    /// <summary>Phase G. A validated output already exists (§16).</summary>
    OutputValidated,

    /// <summary>Phase H. Unrecognised state or a blocking modal. Nothing may be invoked (§10, §20).</summary>
    UnknownOrBlocked,
}

/// <summary>
/// What PrintFlow believes it has left behind in the external application
/// (Epic 11300 Part D2A §7 of the report, §10, §13, §29).
/// </summary>
/// <remarks>
/// Reported rather than inferred, and honest about not knowing. There is no member meaning
/// "the external application is safe" or "finished", because PrintFlow cannot establish either
/// after it has stopped looking (§21).
/// </remarks>
public enum RetainedExternalState
{
    /// <summary>Nothing was asked of the external application, so it retains nothing from this attempt.</summary>
    None,

    /// <summary>PrintFlow stopped without proving what the external application is doing.</summary>
    Unknown,

    /// <summary>An operation was running and may still be running. Operator action may be required.</summary>
    OperationMayStillBeRunning,

    /// <summary>A signed cancel was invoked and the external application positively left Busy.</summary>
    OperationCancelled,

    /// <summary>The external application may still hold a processed, unexported result in memory (§13).</summary>
    ProcessedResultRetained,

    /// <summary>An irreversible write was confirmed before PrintFlow stopped (§15).</summary>
    OutputWriteConfirmed,
}

/// <summary>
/// What one Stop or Take Over request actually permits, decided from the phase alone
/// (Epic 11300 Part D2A §4–§16).
/// </summary>
/// <param name="MayInvokeOperationCancel">
/// Whether the exact signed Busy cancel may be resolved and invoked once. True in exactly one
/// phase, for exactly one mode.
/// </param>
/// <param name="MayCancelExportSurface">
/// Whether the already-signed export cancel/close route may be used to back out of a prepared
/// export that has not been confirmed (§14).
/// </param>
/// <param name="MayProduceAnyInput">
/// Whether any input at all is permitted. False for every Take Over and for every unknown or
/// blocked phase — the single flag that <see cref="AutomationStopPolicy"/> tests before either
/// of the two above can be honoured.
/// </param>
/// <param name="Retained">What PrintFlow will report about the external application's state.</param>
/// <param name="PreservesValidatedSuccess">
/// Whether an already-validated output must survive this stop. True from
/// <see cref="ExternalOperationPhase.OutputValidated"/>, which is the transaction boundary §16
/// forbids Stop from rewriting.
/// </param>
public sealed record AutomationStopResolution(
    bool MayInvokeOperationCancel,
    bool MayCancelExportSurface,
    bool MayProduceAnyInput,
    RetainedExternalState Retained,
    bool PreservesValidatedSuccess);

/// <summary>
/// The phase-specific Stop rules of Epic 11300 Part D2A §4–§16, as one pure function.
/// </summary>
/// <remarks>
/// This lives in <c>PrintFlow.Workflow</c> because Stop and Take Over are <b>policy</b>: what
/// stopping means at each phase is a product decision, and §38 places it above Infrastructure
/// deliberately. Infrastructure asks this type what it is permitted to do and then performs
/// only that; it does not decide, and it cannot widen the answer, because the only thing it
/// receives is the resolution record.
/// <para>
/// Pure and static: no clock, no window, no external application. Every rule §4 through §16
/// states is therefore a unit test that needs no desktop, which is the same arrangement
/// <c>MeituStateClassifier</c> uses for recognition.
/// </para>
/// </remarks>
public static class AutomationStopPolicy
{
    /// <summary>Resolves what <paramref name="mode"/> permits in <paramref name="phase"/>.</summary>
    public static AutomationStopResolution Resolve(AutomationStopMode mode, ExternalOperationPhase phase)
    {
        // Take Over first, and unconditionally: it is the one mode whose answer does not depend
        // on the phase at all. §19 and §20 both reduce to "produce no further input", including
        // from Busy and including from a blocking modal — which is precisely why a takeover is
        // available in states where a stop-with-cancel is not (§20).
        if (mode == AutomationStopMode.TakeOver)
        {
            return new AutomationStopResolution(
                MayInvokeOperationCancel: false,
                MayCancelExportSurface: false,
                MayProduceAnyInput: false,
                Retained: RetainedFor(phase, cancelled: false),
                PreservesValidatedSuccess: phase == ExternalOperationPhase.OutputValidated);
        }

        return phase switch
        {
            // §5. Nothing has been asked of the operation, so there is nothing to cancel and no
            // cancel affordance exists. PrintFlow cancels its own orchestration and stops.
            ExternalOperationPhase.NotStarted => new AutomationStopResolution(
                false, false, MayProduceAnyInput: false, RetainedExternalState.None, false),

            // §6, §7. Invoked but Busy never positively observed. The signed cancel belongs to
            // the Busy surface and is correlated to the current load; without that correlation
            // nothing is eligible, so no input is produced and the external state is reported
            // as possibly running rather than guessed at.
            ExternalOperationPhase.OperationRequested => new AutomationStopResolution(
                false, false, MayProduceAnyInput: false,
                RetainedExternalState.OperationMayStillBeRunning, false),

            // §9. The one phase in which the exact signed cancel may be resolved and invoked,
            // once. Whether it actually can be is still Infrastructure's question to answer
            // structurally (§10) — this only says it is permitted to ask.
            ExternalOperationPhase.Busy => new AutomationStopResolution(
                MayInvokeOperationCancel: true, MayCancelExportSurface: false,
                MayProduceAnyInput: true, RetainedExternalState.OperationMayStillBeRunning, false),

            // §13. Processing finished, export not started. Stop means do not export — and
            // nothing else. The processed result stays where it is; discarding it is not part
            // of stopping.
            ExternalOperationPhase.CompletedBeforeExport => new AutomationStopResolution(
                false, false, MayProduceAnyInput: false,
                RetainedExternalState.ProcessedResultRetained, false),

            // §14. The save surface is open and nothing irreversible has happened. The signed
            // cancel route for that exact surface already exists and is part of the validated
            // export path, so backing out through it is preferred to leaving a modal blocking.
            ExternalOperationPhase.ExportPrepared => new AutomationStopResolution(
                MayInvokeOperationCancel: false, MayCancelExportSurface: true,
                MayProduceAnyInput: true, RetainedExternalState.ProcessedResultRetained, false),

            // §15. The confirm has been invoked. PrintFlow cannot cancel a filesystem write and
            // does not claim to; it stops producing input and observes.
            ExternalOperationPhase.ExportConfirmed => new AutomationStopResolution(
                false, false, MayProduceAnyInput: false,
                RetainedExternalState.OutputWriteConfirmed, false),

            // §16. A validated output and its Revision already exist. Stop may end subsequent
            // cleanup; it may not rewrite that into failure.
            ExternalOperationPhase.OutputValidated => new AutomationStopResolution(
                false, false, MayProduceAnyInput: false,
                RetainedExternalState.OutputWriteConfirmed, PreservesValidatedSuccess: true),

            // §10, §20. Unknown or blocked. No guessing, no dismissing, no input.
            ExternalOperationPhase.UnknownOrBlocked => new AutomationStopResolution(
                false, false, MayProduceAnyInput: false, RetainedExternalState.Unknown, false),

            // An unrecognised phase is treated as unknown rather than as permission. A value
            // added to the enum without a rule here therefore fails closed.
            _ => new AutomationStopResolution(
                false, false, MayProduceAnyInput: false, RetainedExternalState.Unknown, false),
        };
    }

    /// <summary>What is left behind in the external application after a stop at this phase.</summary>
    /// <param name="cancelled">Whether a signed cancel was positively invoked and observed to take effect.</param>
    public static RetainedExternalState RetainedFor(ExternalOperationPhase phase, bool cancelled) =>
        cancelled ? RetainedExternalState.OperationCancelled : phase switch
        {
            ExternalOperationPhase.NotStarted => RetainedExternalState.None,
            ExternalOperationPhase.OperationRequested => RetainedExternalState.OperationMayStillBeRunning,
            ExternalOperationPhase.Busy => RetainedExternalState.OperationMayStillBeRunning,
            ExternalOperationPhase.CompletedBeforeExport => RetainedExternalState.ProcessedResultRetained,
            ExternalOperationPhase.ExportPrepared => RetainedExternalState.ProcessedResultRetained,
            ExternalOperationPhase.ExportConfirmed => RetainedExternalState.OutputWriteConfirmed,
            ExternalOperationPhase.OutputValidated => RetainedExternalState.OutputWriteConfirmed,
            _ => RetainedExternalState.Unknown,
        };
}

/// <summary>
/// What one stopped attempt recorded about the stop that ended it
/// (Epic 11300 Part D2A §29).
/// </summary>
/// <param name="Mode">Which of the two things the operator asked for.</param>
/// <param name="Phase">How far the external operation had got when they asked.</param>
/// <param name="SignedCancelInvoked">
/// Whether an exact signed cancel control was resolved and invoked. False whenever the control
/// could not be proven. Whether it took effect is expressed independently by
/// <paramref name="Retained"/> (and persisted as <c>meituLeftBusy</c>), so an unresponsive
/// Meitu is distinguishable from both success and an unavailable Cancel.
/// </param>
/// <param name="Retained">What the external application may still be holding or doing.</param>
/// <remarks>
/// Read back out of the stored <see cref="OperationFailure.Context"/> rather than out of a new
/// column, which is why <see cref="AutomationStopAudit"/> and the code that writes those keys
/// live in one file: the two halves of a persisted contract drift the moment they are
/// separated. No migration is involved — the context dictionary is already persisted whole as
/// the attempt's failure detail (§29).
/// </remarks>
public sealed record AutomationStopAudit(
    AutomationStopMode Mode,
    ExternalOperationPhase Phase,
    bool SignedCancelInvoked,
    RetainedExternalState Retained)
{
    /// <summary>The context key that marks a failure as an operator stop rather than a fault.</summary>
    public const string StopRequestedKey = "stopRequested";

    /// <summary>The context key naming which of the two modes was asked for.</summary>
    public const string ModeKey = "stopMode";

    /// <summary>The context key naming the phase the operation had reached.</summary>
    public const string PhaseKey = "operationPhaseAtStop";

    /// <summary>The context key recording whether a signed cancel control was actually invoked.</summary>
    public const string CancelInvokedKey = "meituCancelInvoked";

    /// <summary>The context key naming what the external application may still hold.</summary>
    public const string RetainedKey = "retainedExternalState";

    /// <summary>
    /// Reads the audit back out of a stored failure, or returns <c>null</c> when the failure is
    /// not an operator stop.
    /// </summary>
    /// <remarks>
    /// Deliberately strict about <see cref="StopRequestedKey"/>. <c>FailureCode.Cancelled</c>
    /// alone is not sufficient evidence — an orchestration cancelled by a shutdown carries it
    /// too — and reporting "the operator stopped this" about a run nobody stopped would be a
    /// worse error than reporting nothing.
    /// </remarks>
    public static AutomationStopAudit? Read(OperationFailure? failure)
    {
        if (failure is null ||
            !failure.Context.TryGetValue(StopRequestedKey, out string? requested) ||
            !string.Equals(requested, "true", StringComparison.Ordinal))
        {
            return null;
        }

        return new AutomationStopAudit(
            Parse(failure, ModeKey, AutomationStopMode.StopOperation),
            Parse(failure, PhaseKey, ExternalOperationPhase.NotStarted),
            failure.Context.TryGetValue(CancelInvokedKey, out string? invoked) &&
                string.Equals(invoked, "true", StringComparison.Ordinal),
            Parse(failure, RetainedKey, RetainedExternalState.Unknown));
    }

    /// <summary>Whether the operator still has something to attend to in the external application.</summary>
    public bool OperatorActionMayBeRequired => Retained
        is RetainedExternalState.OperationMayStillBeRunning
        or RetainedExternalState.ProcessedResultRetained
        or RetainedExternalState.Unknown;

    private static T Parse<T>(OperationFailure failure, string key, T fallback)
        where T : struct, Enum =>
        failure.Context.TryGetValue(key, out string? raw) && Enum.TryParse(raw, out T parsed)
            ? parsed
            : fallback;
}

/// <summary>
/// The channel through which a running adapter learns that the operator asked it to stop, and
/// through which it reports how far it has got (Epic 11300 Part D2A §4, §9, §19).
/// </summary>
/// <remarks>
/// Deliberately not a <see cref="CancellationToken"/>. A cancelled token says "produce no more
/// input", which is exactly the wrong instruction for the one phase in which stopping means
/// producing one further, exactly signed input — the Busy cancel (§9). Nor could a token carry
/// the mode: Stop and Take Over must be distinguishable at the point of action, because one may
/// invoke the cancel affordance and the other may never do so (§19, §27).
/// <para>
/// The signal is <b>two-way</b> and both directions matter. Downwards it carries the operator's
/// request; upwards it carries the phase the adapter has actually reached, which is the only
/// authority for what stopping is permitted to do. An adapter that reports no phase gets the
/// conservative answer, because <see cref="ExternalOperationPhase.NotStarted"/> is the default.
/// </para>
/// <para>
/// Implementations must be safe to read from the thread that requested the stop while the
/// adapter is writing to them from the thread running the operation.
/// </para>
/// </remarks>
public interface IAutomationStopSignal
{
    /// <summary>The mode the operator requested, or <c>null</c> when no stop has been requested.</summary>
    AutomationStopMode? RequestedMode { get; }

    /// <summary>The phase the adapter last reported. Never rewound by a caller.</summary>
    ExternalOperationPhase Phase { get; }

    /// <summary>
    /// Whether a signed operation cancel was invoked exactly once.
    /// </summary>
    /// <remarks>
    /// The single fact that separates §29's "Meitu Cancel positively invoked" from "Stop
    /// requested but Meitu Cancel unavailable". It is reported by the adapter that did it, and
    /// it is false until then — so a stop that could not resolve a cancel cannot accidentally
    /// be audited as one that did.
    /// </remarks>
    bool OperationCancelWasInvoked { get; }

    /// <summary>
    /// Whether Meitu positively left Busy after the signed cancel invocation.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="OperationCancelWasInvoked"/> because an unresponsive Meitu may
    /// accept the invocation without leaving Busy. In that case the audit must say both
    /// "invoked" and "may still be running"; collapsing the two would falsely report a clean
    /// cancellation (Epic 11300 Part D2B).
    /// </remarks>
    bool OperationLeftBusyAfterCancel { get; }

    /// <summary>Records the phase the running operation has reached.</summary>
    void ReportPhase(ExternalOperationPhase phase);

    /// <summary>
    /// Records that a signed operation cancel was invoked and whether the operation positively
    /// left Busy, so the audit can distinguish success, unresponsiveness and an unavailable
    /// cancel (§29; Epic 11300 Part D2B).
    /// </summary>
    void ReportOperationCancelOutcome(bool leftBusy);

    /// <summary>What the current request permits right now, or <c>null</c> when none was made.</summary>
    AutomationStopResolution? Resolve() =>
        RequestedMode is { } mode ? AutomationStopPolicy.Resolve(mode, Phase) : null;
}

/// <summary>
/// The stop signal used when nothing can request a stop — an unattended call, or a test that
/// is not about stopping.
/// </summary>
/// <remarks>
/// Exists so <c>MeituRequest.Stop</c> can be non-nullable and every adapter can read it
/// unconditionally. A null-object rather than a nullable member: an adapter written against
/// <c>Stop?.RequestedMode</c> would be one forgotten null-check away from ignoring a stop.
/// </remarks>
public sealed class InertAutomationStopSignal : IAutomationStopSignal
{
    /// <summary>The shared instance. It holds no state, so one is enough.</summary>
    public static readonly InertAutomationStopSignal Instance = new();

    private InertAutomationStopSignal()
    {
    }

    /// <inheritdoc />
    public AutomationStopMode? RequestedMode => null;

    /// <inheritdoc />
    public ExternalOperationPhase Phase => ExternalOperationPhase.NotStarted;

    /// <inheritdoc />
    public bool OperationCancelWasInvoked => false;

    /// <inheritdoc />
    public bool OperationLeftBusyAfterCancel => false;

    /// <inheritdoc />
    public void ReportPhase(ExternalOperationPhase phase)
    {
        // Nothing is listening. Discarding the report is the whole point of the null object.
    }

    /// <inheritdoc />
    public void ReportOperationCancelOutcome(bool leftBusy)
    {
    }
}
