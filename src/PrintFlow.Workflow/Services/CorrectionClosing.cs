using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Ids;

namespace PrintFlow.Workflow.Services;

/// <summary>What an unfinished (Failed or Cancelled) correction-import close must do.</summary>
/// <param name="HandOffReason">The reason the closing HandOff records.</param>
/// <param name="Changes">The request changes committed with the close: the assertion, or none.</param>
/// <param name="IsBound">Whether <c>BoundCorrectionClose</c> held.</param>
internal sealed record UnfinishedCorrectionClose(
    string HandOffReason, IReadOnlyList<CorrectionRequestChange> Changes, bool IsBound);

/// <summary>What a successful close must do about correction and about a pending takeover.</summary>
/// <param name="Changes">The request changes committed with the success: RETURNED, or none.</param>
/// <param name="MayHandOffAfterSuccess">Whether today's TakeOver-after-success handoff still applies.</param>
/// <param name="IsBound">Whether <c>BoundCorrectionClose</c> held.</param>
internal sealed record SucceededCorrectionClose(
    IReadOnlyList<CorrectionRequestChange> Changes, bool MayHandOffAfterSuccess, bool IsBound);

/// <summary>
/// The closing builder for correction-bound imports (SCRUM-11148, addendum §3.2–§3.3): one pure
/// decision, used by every live closing seam, from the request id the dedicated entry threaded into
/// the producing work and the <b>post-opening</b> aggregate.
/// </summary>
/// <remarks>
/// Three outcomes, never more: bound (the predicate holds), fail-closed (an id was threaded but the
/// predicate fails — an internal invariant is broken), and unbound (no id: every generic import,
/// which keeps exactly today's path). The builder decides wording and request changes only; the
/// caller removes lock releases for every correction import and commits everything in one
/// transaction.
/// </remarks>
internal static class CorrectionClosing
{
    /// <summary>The neutral reason for a correction import whose binding no longer holds.</summary>
    public const string UnfinishedReason =
        "The correction import did not finish; manual processing remains authorised.";

    /// <summary>
    /// The close of a correction import that failed, or that was stopped and then failed; null for
    /// an unbound (generic) import, which keeps today's behaviour.
    /// </summary>
    public static UnfinishedCorrectionClose? Unfinished(
        Guid? requestId, SessionAggregate afterStart, ProcessingAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(afterStart);
        ArgumentNullException.ThrowIfNull(attempt);

        if (requestId is not { } id)
        {
            return null;
        }

        return BoundRequest(id, afterStart, attempt) is { } bound
            ? new UnfinishedCorrectionClose(
                bound.EffectiveReason, [new CorrectionRequestChange.AssertBound(bound.Id, attempt.Id)], IsBound: true)
            : new UnfinishedCorrectionClose(UnfinishedReason, [], IsBound: false);
    }

    /// <summary>
    /// The close of an import that succeeded. A correction import — bound or fail-closed — is never
    /// handed off after success; only a bound one returns its request.
    /// </summary>
    public static SucceededCorrectionClose Succeeded(
        Guid? requestId, SessionAggregate afterStart, ProcessingAttempt attempt, RevisionId result, DateTimeOffset atUtc)
    {
        ArgumentNullException.ThrowIfNull(afterStart);
        ArgumentNullException.ThrowIfNull(attempt);

        if (requestId is not { } id)
        {
            return new SucceededCorrectionClose([], MayHandOffAfterSuccess: true, IsBound: false);
        }

        return BoundRequest(id, afterStart, attempt) is { } bound
            ? new SucceededCorrectionClose(
                [new CorrectionRequestChange.MarkReturned(bound.Id, attempt.Id, result, atUtc)],
                MayHandOffAfterSuccess: false, IsBound: true)
            : new SucceededCorrectionClose([], MayHandOffAfterSuccess: false, IsBound: false);
    }

    /// <summary>The request row named by <paramref name="requestId"/>, when the binding predicate holds.</summary>
    private static CorrectionRequest? BoundRequest(Guid requestId, SessionAggregate afterStart, ProcessingAttempt attempt)
    {
        CorrectionRequest? row = afterStart.CorrectionRequests.SingleOrDefault(r => r.Id == requestId);
        return CorrectionRequestEligibility.IsBound(attempt, afterStart.Session.Id, row) ? row : null;
    }
}
