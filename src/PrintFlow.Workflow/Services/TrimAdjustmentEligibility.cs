using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Trimming;
using PrintFlow.Workflow.Engine;

namespace PrintFlow.Workflow.Services;

/// <summary>
/// What an adjustment of the trim under review would work on (SCRUM-11147).
/// </summary>
/// <param name="ResultRevisionId">The trim result under review (R1).</param>
/// <param name="ResultSha256">R1's recorded hash.</param>
/// <param name="PreTrimRevisionId">The pre-trim source R1 was cut from (U), never R1 itself.</param>
/// <param name="PreTrimSha256">U's recorded hash.</param>
/// <param name="SourcePixelWidth">U's recorded width; the draft is in U's pixels.</param>
/// <param name="SourcePixelHeight">U's recorded height.</param>
/// <param name="CurrentBounds">Where R1 was cut from U: the applied rectangle its attempt recorded.</param>
/// <param name="AutomaticSuggestion">
/// The applied rectangle of the most recent succeeded automatic trim of this same U, exactly as
/// that attempt recorded it (its margin already included), or null when there is none.
/// </param>
/// <param name="AutomaticSuggestionAttemptId">The attempt the suggestion was read from.</param>
public sealed record TrimAdjustmentView(
    RevisionId ResultRevisionId,
    Sha256 ResultSha256,
    RevisionId PreTrimRevisionId,
    Sha256 PreTrimSha256,
    int SourcePixelWidth,
    int SourcePixelHeight,
    TrimBounds CurrentBounds,
    TrimBounds? AutomaticSuggestion,
    AttemptId? AutomaticSuggestionAttemptId)
{
    /// <summary>
    /// The transient identity a screen's draft belongs to. Two views with the same identity
    /// describe the same review of the same source.
    /// </summary>
    public bool SameTargetAs(TrimAdjustmentView? other) =>
        other is not null &&
        other.ResultRevisionId == ResultRevisionId && other.ResultSha256 == ResultSha256 &&
        other.PreTrimRevisionId == PreTrimRevisionId && other.PreTrimSha256 == PreTrimSha256;
}

/// <summary>
/// The attempt-history half of "may the trim under review be adjusted" (SCRUM-11147).
/// </summary>
/// <remarks>
/// The engine can check that Trim is under review and that a command names the result and input
/// on offer. It cannot see where that result came from. This can, and it refuses rather than
/// guesses: the source is accepted only when the result's own lineage, the attempt that produced
/// it and the workflow's current trim input all name the same Revision, and only when every file
/// involved is still valid. There is no fallback to another Revision.
/// <para>
/// A result that already has descendants is refused too. Adjustment supersedes the offer without
/// invalidating anything, which is only truthful while nothing was derived from it.
/// </para>
/// </remarks>
public static class TrimAdjustmentEligibility
{
    /// <summary>Formats the manual-crop processor reads as a raster.</summary>
    private static readonly ImageFormat[] RasterFormats = [ImageFormat.Png, ImageFormat.Jpeg, ImageFormat.Tiff];

    public static TrimAdjustmentView? Resolve(
        WorkflowSnapshot state,
        IReadOnlyList<Revision> revisions,
        IReadOnlyList<ProcessingAttempt> attempts,
        IReadOnlyList<PrintOutput> outputs)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(revisions);
        ArgumentNullException.ThrowIfNull(attempts);
        ArgumentNullException.ThrowIfNull(outputs);

        if (!SessionStateRules.AllowsProgress(state.SessionState) ||
            state.CurrentStep is not
            {
                Step: StepKind.Trim, State: StepState.ReviewRequired,
                CurrentRevisionId: RevisionId reviewedId, CurrentRevisionSha256: Sha256 reviewedHash,
            } ||
            state.UpstreamResultOf(StepKind.Trim) is not { } input)
        {
            return null;
        }

        Revision? reviewed = revisions.FirstOrDefault(r => r.Id == reviewedId);
        Revision? source = revisions.FirstOrDefault(r => r.Id == input.Id);
        if (reviewed is not { IsValid: true, RetentionReleasedAtUtc: null } || reviewed.Sha256 != reviewedHash ||
            source is not { IsValid: true, RetentionReleasedAtUtc: null } || source.Sha256 != input.Sha256 ||
            reviewed.SourceRevisionId != source.Id ||
            !RasterFormats.Contains(source.Facts.Format) ||
            source.Facts is not { PixelWidth: int width and > 0, PixelHeight: int height and > 0 })
        {
            return null;
        }

        ProcessingAttempt? producing = attempts.FirstOrDefault(a => a.OutputRevisionId == reviewed.Id);
        if (producing is not { Step: StepKind.Trim, Status: AttemptStatus.Succeeded } ||
            producing.InputRevisionId != source.Id)
        {
            return null;
        }

        TrimBounds? current = producing.Operation switch
        {
            OperationKind.Trim => producing.TrimGeometry?.AppliedBounds,
            OperationKind.ManualImport => producing.ManualCropGeometry?.AppliedBounds,
            _ => null,
        };
        if (current is not { } currentBounds || !currentBounds.FitsWithin(width, height))
        {
            return null;
        }

        if (revisions.Any(r => r.SourceRevisionId == reviewed.Id) ||
            outputs.Any(o => o.SourceRevisionId == reviewed.Id))
        {
            return null;
        }

        ProcessingAttempt? automatic = LatestAutomaticTrim(attempts, source.Id);
        TrimBounds? suggestion = automatic?.TrimGeometry?.AppliedBounds is { } applied && applied.FitsWithin(width, height)
            ? applied
            : null;

        return new TrimAdjustmentView(
            reviewed.Id, reviewed.Sha256, source.Id, source.Sha256, width, height,
            currentBounds, suggestion, suggestion is null ? null : automatic!.Id);
    }

    /// <summary>
    /// The newest succeeded deterministic trim of <paramref name="source"/> that recorded its
    /// geometry. Read, never recomputed: the stored applied rectangle already includes the margin
    /// that attempt ran with.
    /// </summary>
    private static ProcessingAttempt? LatestAutomaticTrim(IReadOnlyList<ProcessingAttempt> attempts, RevisionId source)
    {
        ProcessingAttempt? newest = null;
        foreach (ProcessingAttempt attempt in attempts)
        {
            if (attempt is not { Step: StepKind.Trim, Operation: OperationKind.Trim, Status: AttemptStatus.Succeeded } ||
                attempt.InputRevisionId != source || attempt.TrimGeometry is null)
            {
                continue;
            }

            if (newest is null ||
                (attempt.RetrySequence != newest.RetrySequence
                    ? attempt.RetrySequence > newest.RetrySequence
                    : attempt.EndedAtUtc > newest.EndedAtUtc))
            {
                newest = attempt;
            }
        }

        return newest;
    }
}
