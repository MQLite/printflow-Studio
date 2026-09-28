using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Engine;

namespace PrintFlow.Workflow.Services;

/// <summary>How an eligible correction request is imported (SCRUM-11148, design §6.4).</summary>
public enum CorrectionImportMode
{
    /// <summary>R is still under review; the import uses <c>ImportCorrectedImage</c>.</summary>
    Review,

    /// <summary>
    /// The last bound import did not finish; the import uses the existing <c>SubmitManualResult</c>
    /// with the request's context.
    /// </summary>
    AfterUnfinishedImport,
}

/// <summary>An open request that currently grants an import, and how.</summary>
public sealed record CorrectionEligibility(CorrectionRequest Request, CorrectionImportMode Mode);

/// <summary>The exact result a new request would hand out (R) and its input (U).</summary>
public sealed record CorrectionCandidate(Revision HandedOut, Revision Reference);

/// <summary>
/// The workflow-layer half of colleague correction that the pure engine cannot see: which request,
/// if any, grants an import, whether a new request may be made, and whether a closing attempt is
/// bound to a request (SCRUM-11148, design §6.2, §6.4; addendum §3.2).
/// </summary>
/// <remarks>
/// Nothing here infers a request from <c>HandOffReason</c> text, a file or folder name, or a display
/// flag: every answer starts from a persisted row and the exact ids and hashes it binds.
/// </remarks>
public static class CorrectionRequestEligibility
{
    /// <summary>
    /// The open READY request that currently grants an import, with its mode, or null.
    /// </summary>
    /// <remarks>
    /// A READY row that no longer matches the session — R rejected, a new result, a retry, a
    /// return elsewhere — resolves to null and is history. A step still <c>Processing</c> (an import
    /// running, or its closing commit lost) resolves to null too: nothing may be imported until the
    /// last import has been closed.
    /// </remarks>
    public static CorrectionEligibility? Resolve(
        WorkflowSnapshot state,
        IReadOnlyList<CorrectionRequest> requests,
        IReadOnlyList<ProcessingAttempt> attempts)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(attempts);

        if (state.SessionState != SessionState.HandedOff ||
            requests.SingleOrDefault(r => r.Status == CorrectionRequestStatus.Ready) is not { } request ||
            request.SessionId != state.SessionId || request.StepKind != StepKind.BackgroundRemoval ||
            state.UpstreamResultOf(StepKind.BackgroundRemoval) is not { } reference ||
            reference.Id != request.ReferenceRevisionId || reference.Sha256 != request.ReferenceSha256)
        {
            return null;
        }

        switch (state.CurrentStep)
        {
            case { Step: StepKind.BackgroundRemoval, State: StepState.ReviewRequired } review
                when review.CurrentRevisionId == request.HandedOutRevisionId &&
                     review.CurrentRevisionSha256 == request.HandedOutSha256:
                return new CorrectionEligibility(request, CorrectionImportMode.Review);

            case { Step: StepKind.BackgroundRemoval, State: StepState.Failed or StepState.Interrupted }
                when request.LastImportAttemptId is { } last &&
                     LatestAttemptOf(attempts, StepKind.BackgroundRemoval) is
                     {
                         Status: AttemptStatus.Failed or AttemptStatus.Interrupted or AttemptStatus.Cancelled,
                     } latest &&
                     latest.Id == last:
                return new CorrectionEligibility(request, CorrectionImportMode.AfterUnfinishedImport);

            default:
                return null;
        }
    }

    /// <summary>
    /// The exact R and U a new request would bind, or null when a request may not be made
    /// (design §6.2 step 2, the parts that need Revisions and outputs).
    /// </summary>
    /// <remarks>
    /// The session is Active; background removal is under review with R current; U is its input and
    /// R's recorded source; both are valid and not retention-released; R is a PNG and U a raster of
    /// R's exact pixel size; and nothing — no Revision, no print output — descends from R. The last
    /// rule is proven here rather than assumed, because the feature never invalidates R.
    /// </remarks>
    public static CorrectionCandidate? RequestCandidate(
        WorkflowSnapshot state,
        IReadOnlyList<Revision> revisions,
        IReadOnlyList<PrintOutput> outputs)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(revisions);
        ArgumentNullException.ThrowIfNull(outputs);

        if (state.SessionState != SessionState.Active ||
            state.CurrentStep is not
            {
                Step: StepKind.BackgroundRemoval, State: StepState.ReviewRequired,
                CurrentRevisionId: RevisionId handedOutId, CurrentRevisionSha256: Sha256 handedOutHash,
            } ||
            state.UpstreamResultOf(StepKind.BackgroundRemoval) is not { } input)
        {
            return null;
        }

        Revision? handedOut = revisions.FirstOrDefault(r => r.Id == handedOutId);
        Revision? reference = revisions.FirstOrDefault(r => r.Id == input.Id);
        if (handedOut is not { IsValid: true, RetentionReleasedAtUtc: null } || handedOut.Sha256 != handedOutHash ||
            reference is not { IsValid: true, RetentionReleasedAtUtc: null } || reference.Sha256 != input.Sha256 ||
            handedOut.SourceRevisionId != reference.Id || handedOut.Id == reference.Id ||
            handedOut.Facts.Format != ImageFormat.Png ||
            handedOut.Facts is not { PixelWidth: int width and > 0, PixelHeight: int height and > 0 } ||
            reference.Facts.PixelWidth != width || reference.Facts.PixelHeight != height)
        {
            return null;
        }

        if (revisions.Any(r => r.SourceRevisionId == handedOut.Id) ||
            outputs.Any(o => o.SourceRevisionId == handedOut.Id || o.Id.Value == handedOut.Id.Value))
        {
            return null;
        }

        return new CorrectionCandidate(handedOut, reference);
    }

    /// <summary>
    /// <c>BoundCorrectionClose(A, r)</c> (addendum §3.2): whether <paramref name="attempt"/>, being
    /// closed now in <paramref name="session"/>, is the import bound to <paramref name="request"/>.
    /// </summary>
    public static bool IsBound(ProcessingAttempt attempt, SessionId session, CorrectionRequest? request) =>
        request is not null &&
        attempt is
        {
            Operation: OperationKind.ManualResultImport,
            Step: StepKind.BackgroundRemoval,
            Status: AttemptStatus.Running,
        } &&
        attempt.SessionId == session &&
        request.SessionId == session &&
        request.StepKind == StepKind.BackgroundRemoval &&
        request.Status == CorrectionRequestStatus.Ready &&
        request.LastImportAttemptId == attempt.Id &&
        attempt.InputRevisionId == request.ReferenceRevisionId;

    /// <summary>
    /// For startup recovery, which has no live producing work: the unique persisted READY row whose
    /// <c>LastImportAttemptId</c> names <paramref name="attempt"/>, or null.
    /// </summary>
    public static CorrectionRequest? PersistedBindingOf(ProcessingAttempt attempt, IReadOnlyList<CorrectionRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(requests);

        List<CorrectionRequest> matches = requests
            .Where(r => r.Status == CorrectionRequestStatus.Ready && r.LastImportAttemptId == attempt.Id)
            .Take(2)
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    /// <summary>The newest attempt of <paramref name="step"/>, by retry sequence then start time.</summary>
    public static ProcessingAttempt? LatestAttemptOf(IReadOnlyList<ProcessingAttempt> attempts, StepKind step) =>
        attempts.Where(a => a.Step == step)
            .OrderByDescending(a => a.RetrySequence)
            .ThenByDescending(a => a.StartedAtUtc)
            .FirstOrDefault();
}

/// <summary>
/// What the Session screen shows about colleague correction (SCRUM-11148, design §6.4).
/// </summary>
/// <remarks>
/// Built by the service from the persisted request and the same eligibility the service enforces;
/// the screen decides nothing. Paths are for display and for the folder port only — no identity
/// here authorises anything.
/// </remarks>
/// <param name="RequestId">The request this panel is about.</param>
/// <param name="Mode">How it imports, or null when it is shown as history only.</param>
/// <param name="HandedOutRevisionId">R.</param>
/// <param name="HandedOutSha256">R's hash.</param>
/// <param name="FolderPath">The managed correction folder, resolved for display.</param>
/// <param name="ReferenceFileName">The reference copy (do not edit).</param>
/// <param name="WorkingFileName">The copy to correct.</param>
/// <param name="SuggestedReturnName">An example name to save the corrected picture as.</param>
/// <param name="Note">The operator's note, or null.</param>
/// <param name="RequiredPixelWidth">U's width: the returned picture must match it.</param>
/// <param name="RequiredPixelHeight">U's height.</param>
/// <param name="MissingFiles">Whether a prepared file is missing from the folder (Repair offered).</param>
public sealed record CorrectionHandoffView(
    Guid RequestId,
    CorrectionImportMode? Mode,
    RevisionId HandedOutRevisionId,
    Sha256 HandedOutSha256,
    string FolderPath,
    string ReferenceFileName,
    string WorkingFileName,
    string SuggestedReturnName,
    string? Note,
    int RequiredPixelWidth,
    int RequiredPixelHeight,
    bool MissingFiles)
{
    /// <summary>Whether Import corrected image is offered.</summary>
    public bool CanImport => Mode is not null;

    /// <summary>Whether the request no longer grants anything and is shown as history.</summary>
    public bool IsObsolete => Mode is null;
}

/// <summary>What the review of an imported correction (R2) shows (design §8.1).</summary>
/// <param name="RequestId">The RETURNED request that produced the result under review.</param>
/// <param name="IdenticalToSent">Whether R2's bytes equal the working copy that was sent.</param>
/// <param name="NextStep">The step after background removal in this workflow, or null.</param>
public sealed record CorrectionReturnView(Guid RequestId, bool IdenticalToSent, StepKind? NextStep);
