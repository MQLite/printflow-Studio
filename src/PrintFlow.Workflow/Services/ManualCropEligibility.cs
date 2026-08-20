using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Engine;

namespace PrintFlow.Workflow.Services;

/// <summary>
/// The one definition of "may this session be cropped by hand right now"
/// (Epic 11200 Part C2 §3, §13, §21).
/// </summary>
/// <remarks>
/// It lives here, in the workflow layer, because both callers that need it are here and
/// neither may be allowed to have its own copy: <see cref="SessionView"/> reports it so the
/// screen can offer the control, and <see cref="SessionService"/> enforces it so the command
/// is refused whatever the screen offered. A view model that worked the rule out for itself
/// would be a second answer that could disagree with the enforcing one — which is the exact
/// shape of bug where a disabled button is the only thing standing between an operator and an
/// illegal state change.
/// <para>
/// The rule cannot live in <see cref="WorkflowEngine"/> because it is a question about attempt
/// <i>history</i>, and a <see cref="WorkflowSnapshot"/> deliberately carries none. The engine
/// therefore owns the state-machine half (session Active, Trim is current, the step state
/// permits it) and this owns the historical half. Both run.
/// </para>
/// </remarks>
public static class ManualCropEligibility
{
    /// <summary>
    /// True exactly when an operator-selected crop is a legal next action.
    /// </summary>
    /// <remarks>
    /// Three openings, and only three:
    /// <list type="bullet">
    ///   <item>the deterministic trim ran and reported <see cref="FailureCode.ManualCropRequired"/>
    ///         — the original case, where no automatic crop is honest (§3);</item>
    ///   <item>a manual crop was produced and the operator rejected it (§21);</item>
    ///   <item>a manual crop was attempted and did not produce a file at all — a rectangle the
    ///         processor refused, a write that failed.</item>
    /// </list>
    /// The last two are the same rule stated for the two ways a manual crop can end: once this
    /// file is known to be one the deterministic trim cannot handle, the corrective path stays
    /// manual. Sending the operator back through the alpha scan would make them press a button
    /// that is guaranteed to refuse again before they could draw a second rectangle (§21).
    /// <para>
    /// Everything else is refused, including a Trim that failed for an unrelated reason, a Trim
    /// awaiting an ordinary review, a successful Trim, a rejected <i>deterministic</i> trim, and
    /// any step that is not Trim.
    /// </para>
    /// </remarks>
    public static bool IsEligible(WorkflowSnapshot state, IReadOnlyList<ProcessingAttempt> attempts)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(attempts);

        if (!SessionStateRules.AllowsProgress(state.SessionState))
        {
            return false;
        }

        if (state.CurrentStep is not { Step: StepKind.Trim } trim)
        {
            return false;
        }

        if (LatestEndedAttempt(attempts, StepKind.Trim) is not { } latest)
        {
            return false;
        }

        return trim.State switch
        {
            // Either the alpha scan said a human must decide, or a manual crop was already
            // tried and produced nothing — a rectangle the processor refused, say. Both leave
            // the operator with a rectangle still to draw.
            StepState.Failed =>
                latest.Failure?.Code == FailureCode.ManualCropRequired ||
                latest.Operation == OperationKind.ManualImport,

            // A rejected manual crop leaves no attempt behind of its own — a rejection is a
            // ReviewDecision, not an attempt — so the newest ended attempt is still the crop
            // that was rejected. Requiring it to have succeeded is what distinguishes this from
            // a rejected deterministic trim, which is an ordinary Retry case.
            StepState.RetryRequired =>
                latest is { Operation: OperationKind.ManualImport, Status: AttemptStatus.Succeeded },

            _ => false,
        };
    }

    /// <summary>
    /// The code the current step's newest ended attempt failed with, while the step is Failed.
    /// </summary>
    /// <remarks>
    /// Guarded on <see cref="StepState.Failed"/> rather than reported for any failed attempt in
    /// the history: a step that failed once and then succeeded is not a failed step, and a
    /// screen showing a stale code beside a good result would be worse than showing none
    /// (Epic 11200 Part C1 §17).
    /// </remarks>
    public static FailureCode? CurrentStepFailure(
        WorkflowSnapshot state, IReadOnlyList<ProcessingAttempt> attempts)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(attempts);

        return state.CurrentStep is { State: StepState.Failed } step
            ? LatestEndedAttempt(attempts, step.Step)?.Failure?.Code
            : null;
    }

    /// <summary>
    /// The most recent attempt for <paramref name="step"/> that has finished, of any outcome.
    /// </summary>
    /// <remarks>
    /// Ordered by <see cref="ProcessingAttempt.RetrySequence"/> first and only then by time.
    /// The sequence is the step's own attempt counter, so it is strictly increasing and cannot
    /// tie; <c>EndedAtUtc</c> can, whenever two attempts complete inside the same clock tick —
    /// which is not a theoretical case, because a deterministic trim that refuses and the manual
    /// crop that follows it can easily share a timestamp. Reading "latest" off a tied comparison
    /// would make the eligibility rule depend on row order.
    /// </remarks>
    private static ProcessingAttempt? LatestEndedAttempt(
        IReadOnlyList<ProcessingAttempt> attempts, StepKind step)
    {
        ProcessingAttempt? newest = null;
        foreach (ProcessingAttempt attempt in attempts)
        {
            if (attempt.Step != step || attempt.EndedAtUtc is null)
            {
                continue;
            }

            if (newest is null || IsNewer(attempt, newest))
            {
                newest = attempt;
            }
        }

        return newest;
    }

    private static bool IsNewer(ProcessingAttempt candidate, ProcessingAttempt incumbent) =>
        candidate.RetrySequence != incumbent.RetrySequence
            ? candidate.RetrySequence > incumbent.RetrySequence
            : candidate.EndedAtUtc > incumbent.EndedAtUtc;
}
