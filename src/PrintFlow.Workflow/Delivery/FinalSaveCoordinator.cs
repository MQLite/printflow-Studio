using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Workflow.Delivery;

/// <summary>The exact result an operator saw at a final review: never a hash alone.</summary>
public sealed record ReviewedResultIdentity(SessionId SessionId, StepKind Step, RevisionId RevisionId, Sha256 DisplayedHash);

public enum ApprovalOutcome { NotAttempted, Succeeded, Refused, Unknown }

public enum PngPreparationOutcome { NotApplicable, Prepared, Failed }

public enum FinalSaveStage { CheckingDraft, Approving, Approved, PreparingPng, Saving }

/// <summary>A progress observation echoing the operation it belongs to.</summary>
public sealed record FinalSaveObservation(Guid OperationId, Guid RequestId, FinalSaveStage Stage,
    DeliveryProgress? Delivery = null);

/// <summary>
/// One captured save intent. Exactly one of <see cref="PendingReview"/> (combined confirm and
/// save) or <see cref="ApprovedArtifact"/> (approved-only save or its retry) is set.
/// </summary>
public sealed record FinalSaveRequest(
    Guid OperationId, Guid RequestId, SessionId SessionId, ArtifactKind Kind,
    ReviewedResultIdentity? PendingReview, ArtifactKey? ApprovedArtifact, Sha256? ExpectedSha256,
    string Folder, string FileName, long DraftGeneration);

/// <summary>Approval, PNG preparation and delivery stay separately observable.</summary>
public sealed record FinalSaveResult(
    Guid OperationId, Guid RequestId, long DraftGeneration,
    ApprovalOutcome Approval, PngPreparationOutcome PngPreparation, ArtifactKey? Artifact,
    DeliveryDraftCheck? DraftRefusal, ArtifactRefusal OfferRefusal, DeliveryOutcome? Delivery,
    SessionView? Session, OperationFailure? ApprovalFailure = null)
{
    /// <summary>True when the request asked to approve a pending review before saving.</summary>
    public bool CombinedReview { get; init; }
}

public sealed record ShellDispatchResult(bool Dispatched, string? Detail = null);

public enum OpenFolderCode { Dispatched, Missing, Changed, Unavailable, Ineligible, Uncertain, ShellFailed }

public sealed record OpenFolderOutcome(Guid DeliveryId, OpenFolderCode Code, string? FinalPath, string? Detail = null);

/// <summary>
/// Wraps existing lawful commands around the 11144 delivery authority (delivery design §7.2).
/// </summary>
/// <remarks>
/// It never holds the session gate itself: every call below acquires it exactly once and
/// revalidates exact authority. Approval and PNG preparation are not cancellable once begun;
/// an uncertain approval commit is answered by reading authority back, never by approving again.
/// A save retry reaches only delivery/reconciliation — no Approve, Retry, Complete or processor.
/// </remarks>
public sealed class FinalSaveCoordinator
{
    private readonly ISessionService _sessions;
    private readonly IApprovedArtifactDeliveryService _delivery;
    private readonly string? _operatorName;

    public FinalSaveCoordinator(ISessionService sessions, IApprovedArtifactDeliveryService delivery,
        string? operatorName = null)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _delivery = delivery ?? throw new ArgumentNullException(nameof(delivery));
        _operatorName = operatorName ?? Environment.UserName;
    }

    public IApprovedArtifactDeliveryService Delivery => _delivery;

    public async Task<FinalSaveResult> ConfirmAndSaveAsync(FinalSaveRequest request,
        IProgress<FinalSaveObservation>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.PendingReview is not { } review || request.ApprovedArtifact is not null ||
            review.SessionId != request.SessionId ||
            request.Kind != (review.Step == StepKind.Trim ? ArtifactKind.ApprovedAssetPng : ArtifactKind.ApprovedPrintTiff) ||
            review.Step is not (StepKind.Trim or StepKind.PhotoshopOutput))
            return Result(request, ApprovalOutcome.NotAttempted, failure: Refusal("Not a final review target."));

        Report(progress, request, FinalSaveStage.CheckingDraft);
        DeliveryDraftCheck draft = await Task.Run(() => _delivery.CheckDraft(request.Folder, request.FileName, request.Kind));
        if (!draft.IsValid) return Result(request, ApprovalOutcome.NotAttempted, draft: draft);

        // Advisory shape check before touching authority. The exact revision + hash binding is
        // enforced again inside ApproveExactReviewAsync under the session gate.
        OperationResult<SessionView> before = await _sessions.LoadAsync(request.SessionId, CancellationToken.None);
        if (before.IsFailure || !IsFinalReview(before.Value, review))
            return Result(request, ApprovalOutcome.NotAttempted, session: before.IsSuccess ? before.Value : null,
                failure: before.IsFailure ? before.Failure : Refusal("The result on screen is no longer the final review."));

        Report(progress, request, FinalSaveStage.Approving);
        OperationResult<SessionView> approved;
        try
        {
            approved = await _sessions.ApproveExactReviewAsync(request.SessionId, review.Step, review.RevisionId,
                review.DisplayedHash, _operatorName, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            approved = OperationResult.Fail<SessionView>(FailureCode.PersistenceError, ex.Message);
        }

        SessionView? session = approved.IsSuccess ? approved.Value : null;
        if (approved.IsFailure)
        {
            (ApprovalOutcome readback, SessionView? current) = await ReadApprovalBackAsync(review);
            session = current;
            if (readback != ApprovalOutcome.Succeeded)
                return Result(request, readback, session: current, failure: approved.Failure);
        }
        Report(progress, request, FinalSaveStage.Approved);

        ArtifactKey key;
        if (review.Step == StepKind.Trim)
        {
            Report(progress, request, FinalSaveStage.PreparingPng);
            OperationResult<SessionView> promoted;
            try
            {
                promoted = await _sessions.PromoteReviewedPngAsync(request.SessionId, review.RevisionId,
                    review.DisplayedHash, _operatorName, CancellationToken.None);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                promoted = OperationResult.Fail<SessionView>(FailureCode.PersistenceError, ex.Message);
            }
            if (promoted.IsFailure ||
                promoted.Value.Steps.SingleOrDefault(s => s.Step == StepKind.ApprovedPngExport) is not
                    { State: StepState.Approved, CurrentRevisionId: { } promotedId })
            {
                OperationResult<SessionView> reread = await _sessions.LoadAsync(request.SessionId, CancellationToken.None);
                return Result(request, ApprovalOutcome.Succeeded, PngPreparationOutcome.Failed,
                    session: reread.IsSuccess ? reread.Value : session,
                    failure: promoted.IsFailure ? promoted.Failure : null);
            }
            session = promoted.Value;
            key = new ArtifactKey(request.SessionId, ArtifactKind.ApprovedAssetPng, promotedId.Value);
            return await DeliverAsync(request with { ApprovedArtifact = key, ExpectedSha256 = review.DisplayedHash },
                ApprovalOutcome.Succeeded, PngPreparationOutcome.Prepared, review.RevisionId, session, progress,
                cancellationToken);
        }

        // The TIFF PrintOutput shares the reviewed twin Revision's identity (SessionService.BuildPrintOutput).
        key = new ArtifactKey(request.SessionId, ArtifactKind.ApprovedPrintTiff, review.RevisionId.Value);
        return await DeliverAsync(request with { ApprovedArtifact = key, ExpectedSha256 = review.DisplayedHash },
            ApprovalOutcome.Succeeded, PngPreparationOutcome.NotApplicable, null, session, progress,
            cancellationToken);
    }

    /// <summary>Saves an already approved artifact; the retry path for the same captured intent.</summary>
    public async Task<FinalSaveResult> SaveApprovedAsync(FinalSaveRequest request,
        IProgress<FinalSaveObservation>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ApprovedArtifact is not { } key || request.PendingReview is not null ||
            key.SessionId != request.SessionId || key.Kind != request.Kind)
            return Result(request, ApprovalOutcome.NotAttempted, failure: Refusal("Not an approved save target."));
        Report(progress, request, FinalSaveStage.CheckingDraft);
        DeliveryDraftCheck draft = await Task.Run(() => _delivery.CheckDraft(request.Folder, request.FileName, request.Kind));
        if (!draft.IsValid) return Result(request, ApprovalOutcome.NotAttempted, artifact: key, draft: draft);
        return await DeliverAsync(request, ApprovalOutcome.NotAttempted, PngPreparationOutcome.NotApplicable,
            null, null, progress, cancellationToken);
    }

    /// <summary>Reconciles or retries a recorded delivery at its own original destination.</summary>
    public Task<DeliveryOutcome> ReconcileAsync(Guid deliveryId, IProgress<DeliveryProgress>? progress,
        CancellationToken cancellationToken) => _delivery.ReconcileAsync(deliveryId, progress, cancellationToken);

    public Task<DeliveryFileObservation> CheckAsync(Guid deliveryId, CancellationToken cancellationToken) =>
        _delivery.CheckDeliveredFileAsync(deliveryId, cancellationToken);

    /// <summary>
    /// Explicit consent to a new copy of a delivered file that is now missing. A fresh check
    /// must observe Missing; anything else creates nothing.
    /// </summary>
    public async Task<DeliveryOutcome> SaveAnotherCopyAsync(Guid priorDeliveryId, Guid requestId,
        IProgress<DeliveryProgress>? progress, CancellationToken cancellationToken)
    {
        DeliveryFileObservation observed = await _delivery.CheckDeliveredFileAsync(priorDeliveryId, cancellationToken);
        DeliveryCode? notMissing = observed.Availability switch
        {
            DeliveryAvailability.Missing => null,
            DeliveryAvailability.VerifiedNow => DeliveryCode.AlreadyDelivered,
            DeliveryAvailability.Changed => DeliveryCode.DeliveredFileChanged,
            DeliveryAvailability.Ineligible => DeliveryCode.IneligibleArtifact,
            _ => DeliveryCode.Unavailable,
        };
        if (notMissing is { } code)
            return new(requestId, observed.Artifact, Guid.Empty, code, priorDeliveryId,
                code == DeliveryCode.AlreadyDelivered ? observed.FinalPath : null, Detail: observed.Detail);
        (DeliveryOffer? offer, ArtifactRefusal refusal) = await _delivery.GetOfferAsync(observed.Artifact, cancellationToken);
        if (offer is null)
            return new(requestId, observed.Artifact, Guid.Empty,
                refusal == ArtifactRefusal.ApprovalEvidenceMissing ? DeliveryCode.ApprovalEvidenceMissing : DeliveryCode.IneligibleArtifact,
                priorDeliveryId, Detail: refusal.ToString());
        return await _delivery.ReplaceMissingAsync(
            new MissingDeliveryReplacementRequest(requestId, priorDeliveryId, observed, offer.OfferVersion),
            progress, cancellationToken);
    }

    /// <summary>
    /// Selects the exact delivered file through a live verified lease held across the whole
    /// synchronous shell dispatch and disposed on every path.
    /// </summary>
    public async Task<OpenFolderOutcome> OpenContainingFolderAsync(Guid deliveryId,
        Func<IDeliveredSelectionLease, ShellDispatchResult> dispatch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        SelectionLeaseResult acquired = await _delivery.AcquireDeliveredSelectionAsync(deliveryId, cancellationToken);
        if (acquired.Lease is not { } lease)
            return new(deliveryId, acquired.Availability switch
            {
                DeliveryAvailability.Missing => OpenFolderCode.Missing,
                DeliveryAvailability.Changed => OpenFolderCode.Changed,
                DeliveryAvailability.Ineligible => OpenFolderCode.Ineligible,
                DeliveryAvailability.Unavailable => OpenFolderCode.Unavailable,
                _ => OpenFolderCode.Uncertain,
            }, null, acquired.Detail);
        try
        {
            string path = lease.FinalPath;
            ShellDispatchResult shell;
            try { shell = dispatch(lease); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { shell = new(false, ex.Message); }
            return new(deliveryId, shell.Dispatched ? OpenFolderCode.Dispatched : OpenFolderCode.ShellFailed,
                path, shell.Detail);
        }
        finally { lease.Dispose(); }
    }

    private async Task<FinalSaveResult> DeliverAsync(FinalSaveRequest request, ApprovalOutcome approval,
        PngPreparationOutcome png, RevisionId? reviewedSource, SessionView? session,
        IProgress<FinalSaveObservation>? progress, CancellationToken cancellationToken)
    {
        ArtifactKey key = request.ApprovedArtifact!.Value;
        try
        {
            (DeliveryOffer? offer, ArtifactRefusal refusal) = await _delivery.GetOfferAsync(key, cancellationToken);
            if (offer is null)
                return Result(request, approval, png, key, session: session, offerRefusal: refusal);
            ApprovedArtifact artifact = offer.Artifact;
            // The exact approved result linked to what was reviewed; a newer or different
            // artifact is never substituted.
            if (request.ExpectedSha256 is { } expected && artifact.ApprovedSha256 != expected ||
                reviewedSource is { } source && artifact.SourceRevisionId != source)
                return Result(request, approval, png, key, session: session,
                    offerRefusal: ArtifactRefusal.InconsistentLineage);
            Report(progress, request, FinalSaveStage.Saving);
            IProgress<DeliveryProgress>? relay = progress is null ? null
                : new InlineProgress<DeliveryProgress>(p => progress.Report(
                    new(request.OperationId, request.RequestId, FinalSaveStage.Saving, p)));
            DeliveryOutcome outcome = await _delivery.DeliverAsync(new DeliveryRequest(request.RequestId, key,
                artifact.ApprovedSha256, artifact.ReviewId, request.Folder, request.FileName, offer.OfferVersion),
                relay, cancellationToken);
            return Result(request, approval, png, key, session: session, delivery: outcome);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result(request, approval, png, key, session: session,
                delivery: new DeliveryOutcome(request.RequestId, key, Guid.Empty, DeliveryCode.Cancelled));
        }
    }

    private async Task<(ApprovalOutcome, SessionView?)> ReadApprovalBackAsync(ReviewedResultIdentity review)
    {
        OperationResult<SessionView> reread;
        try { reread = await _sessions.LoadAsync(review.SessionId, CancellationToken.None); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return (ApprovalOutcome.Unknown, null); }
        if (reread.IsFailure) return (ApprovalOutcome.Unknown, null);
        SessionView view = reread.Value;
        bool approved = review.Step == StepKind.PhotoshopOutput
            ? view.Outputs.Any(o => o.Id.Value == review.RevisionId.Value && o.ReviewState == ReviewState.Approved &&
                                    o.IsValid && !o.IsRecycled)
            : view.Steps.Any(s => s.Step == review.Step && s.State == StepState.Approved &&
                                  s.CurrentRevisionId == review.RevisionId && s.CurrentRevisionSha256 == review.DisplayedHash);
        return (approved ? ApprovalOutcome.Succeeded : ApprovalOutcome.Refused, view);
    }

    private static bool IsFinalReview(SessionView view, ReviewedResultIdentity review) =>
        view.State == SessionState.Active &&
        view.CurrentStep is { State: StepState.ReviewRequired } step && step.Step == review.Step &&
        step.CurrentRevisionId == review.RevisionId && step.CurrentRevisionSha256 == review.DisplayedHash &&
        (review.Step == StepKind.Trim ? view.WorkflowType == WorkflowType.PrepareAsset : view.ProducesPrintOutput);

    private static OperationFailure Refusal(string detail) =>
        OperationFailure.Create(FailureCode.PreconditionNotMet, detail);

    private static void Report(IProgress<FinalSaveObservation>? progress, FinalSaveRequest request, FinalSaveStage stage) =>
        progress?.Report(new(request.OperationId, request.RequestId, stage));

    private static FinalSaveResult Result(FinalSaveRequest request, ApprovalOutcome approval,
        PngPreparationOutcome png = PngPreparationOutcome.NotApplicable, ArtifactKey? artifact = null,
        DeliveryDraftCheck? draft = null, ArtifactRefusal offerRefusal = ArtifactRefusal.None,
        DeliveryOutcome? delivery = null, SessionView? session = null, OperationFailure? failure = null) =>
        new(request.OperationId, request.RequestId, request.DraftGeneration, approval, png,
            artifact ?? request.ApprovedArtifact, draft, offerRefusal, delivery, session, failure)
        { CombinedReview = request.PendingReview is not null };

    /// <summary>Forwards synchronously; the caller's own progress decides any marshalling.</summary>
    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
