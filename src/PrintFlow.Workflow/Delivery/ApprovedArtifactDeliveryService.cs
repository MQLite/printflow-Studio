using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Workflow.Delivery;

/// <summary>Serializes exact approved authority with durable external publication.</summary>
public sealed class ApprovedArtifactDeliveryService : IApprovedArtifactDeliveryService
{
    private readonly ISessionRepository _sessions;
    private readonly IDeliveryRepository _deliveries;
    private readonly IWorkspace _workspace;
    private readonly IDeliveryFileSystem _files;
    private readonly IReadOnlyList<string> _protectedRoots;
    private readonly TimeProvider _time;
    private readonly ITiffReviewDecoder? _tiffDecoder;
    private readonly ConcurrentDictionary<ArtifactKey, Guid> _offers = new();

    public ApprovedArtifactDeliveryService(ISessionRepository sessions, IDeliveryRepository deliveries,
        IWorkspace workspace, IDeliveryFileSystem files, IReadOnlyList<string> protectedRoots,
        TimeProvider time, ITiffReviewDecoder? tiffDecoder = null)
    {
        _sessions = sessions; _deliveries = deliveries; _workspace = workspace;
        _files = files; _protectedRoots = protectedRoots; _time = time; _tiffDecoder = tiffDecoder;
    }

    public async Task<OperationResult<IReadOnlyList<DeliveryState>>> GetDeliveryStateAsync(
        SessionId sessionId, ArtifactKey? artifact, CancellationToken cancellationToken)
    {
        if (artifact is { } key && key.SessionId != sessionId)
            return OperationResult.Fail<IReadOnlyList<DeliveryState>>(FailureCode.PreconditionNotMet,
                "Artifact filter belongs to another session.");
        OperationResult<IReadOnlyList<DeliveryJournalEntry>> listed =
            await _deliveries.ListAsync(sessionId, artifact, cancellationToken);
        if (listed.IsFailure) return OperationResult.Fail<IReadOnlyList<DeliveryState>>(listed.Failure);
        return OperationResult.Ok<IReadOnlyList<DeliveryState>>(listed.Value.Select(ToState).ToList());
    }

    public Task<OperationResult<DeliveryDestinationPreferenceView?>> GetLastSuccessfulDestinationAsync(
        CancellationToken cancellationToken) => _deliveries.ReadDestinationPreferenceAsync(cancellationToken);

    public DeliveryDraftCheck CheckDraft(string folder, string fileName, ArtifactKind kind)
    {
        DeliveryFileResult<string> leaf = _files.ValidateLeaf(fileName ?? string.Empty, kind);
        if (!leaf.IsSuccess) return new(DeliveryCode.InvalidName, null, leaf.Detail);
        // A completed or corrected name is shown, never silently substituted.
        if (leaf.Value != fileName)
            return new(DeliveryCode.InvalidName, leaf.Value, "Confirm the effective full filename before saving.");
        DeliveryFileResult<string> checkedFolder = _files.CheckFolder(folder ?? string.Empty, _protectedRoots);
        return checkedFolder.IsSuccess
            ? new(null, leaf.Value)
            : new(checkedFolder.Code, leaf.Value, checkedFolder.Detail);
    }

    public async Task<(DeliveryOffer? Offer, ArtifactRefusal Refusal)> GetOfferAsync(
        ArtifactKey artifact, CancellationToken cancellationToken)
    {
        OperationResult<SessionAggregate?> loaded = await _sessions.LoadAsync(artifact.SessionId, cancellationToken);
        if (loaded.IsFailure || loaded.Value is null) return (null, ArtifactRefusal.Missing);
        ArtifactResolution resolved = ApprovedArtifactResolver.Resolve(loaded.Value, artifact);
        if (!resolved.IsEligible) return (null, resolved.Refusal);
        ApprovedArtifact current = resolved.Artifact!;
        if (artifact.Kind == ArtifactKind.ApprovedPrintTiff && current.PhysicalWidthMm is null)
        {
            if (_tiffDecoder is null) return (null, ArtifactRefusal.InconsistentLineage);
            OperationResult<DecodedTiffReview> decoded = await _tiffDecoder.DecodeAsync(
                current.File, current.ApprovedSha256, cancellationToken);
            if (decoded.IsFailure || decoded.Value.XResolutionDpi <= 0 || decoded.Value.YResolutionDpi <= 0)
                return (null, ArtifactRefusal.InconsistentLineage);
            current = current with
            {
                PhysicalWidthMm = decoded.Value.PixelWidth * 25.4 / decoded.Value.XResolutionDpi,
                PhysicalHeightMm = decoded.Value.PixelHeight * 25.4 / decoded.Value.YResolutionDpi,
            };
        }
        Guid version = Guid.NewGuid();
        _offers[artifact] = version;
        return (new DeliveryOffer(current, version), ArtifactRefusal.None);
    }

    public async Task<DeliveryOutcome> DeliverAsync(
        DeliveryRequest request, IProgress<DeliveryProgress>? progress, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Outcome(request, DeliveryCode.Cancelled);
        if (request.RequestId == Guid.Empty || request.SelectionVersion == Guid.Empty ||
            !_offers.TryGetValue(request.Artifact, out Guid offered) || offered != request.SelectionVersion)
            return Outcome(request, DeliveryCode.RequestConflict, detail: "Refresh the exact selected result before saving.");
        try
        {
            using IDisposable sessionGate = await SessionCompletionGate.EnterAsync(request.Artifact.SessionId, cancellationToken);
            return await DeliverUnderGateAsync(request, replacementOf: null, progress, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { return Outcome(request, DeliveryCode.Cancelled); }
    }

    private async Task<DeliveryOutcome> DeliverUnderGateAsync(
        DeliveryRequest request, Guid? replacementOf, IProgress<DeliveryProgress>? progress,
        CancellationToken cancellationToken)
    {
        Report(request, progress, DeliveryPhase.Validating);
        OperationResult<SessionAggregate?> loaded = await _sessions.LoadAsync(request.Artifact.SessionId, cancellationToken);
        if (loaded.IsFailure || loaded.Value is null)
            return Outcome(request, DeliveryCode.PersistenceFailed, detail: loaded.IsFailure ? loaded.Failure.TechnicalDetail : "Session is absent.");
        ArtifactResolution resolved = ApprovedArtifactResolver.Resolve(loaded.Value, request.Artifact);
        if (!resolved.IsEligible) return Outcome(request, RefusalCode(resolved.Refusal), detail: resolved.Refusal.ToString());
        ApprovedArtifact artifact = resolved.Artifact!;
        if (request.ExpectedSha256 != artifact.ApprovedSha256 || request.ExpectedReviewId != artifact.ReviewId)
            return Outcome(request, DeliveryCode.RequestConflict, detail: "The selected approval or bytes changed.");
        DeliveryFileResult<string> leaf = _files.ValidateLeaf(request.FileName, request.Artifact.Kind);
        if (!leaf.IsSuccess || leaf.Value != request.FileName)
            return Outcome(request, DeliveryCode.InvalidName, suggestion: leaf.Value,
                detail: leaf.Detail ?? "Confirm the effective full filename before saving.");
        string sourcePath = _workspace.ResolveAbsolute(artifact.File);

        // Durable RequestId is checked before any new copy. Reopen/retry keeps the original
        // folder even if the remembered successful preference has since changed.
        OperationResult<DeliveryJournalEntry?> byRequest = await _deliveries.FindByRequestAsync(request.RequestId, cancellationToken);
        if (byRequest.IsFailure) return Outcome(request, DeliveryCode.PersistenceFailed, detail: byRequest.Failure.TechnicalDetail);
        if (byRequest.Value is { } previous &&
            (previous.Delivery.Artifact != request.Artifact || previous.Delivery.ReviewId != artifact.ReviewId ||
             previous.Delivery.ApprovedSha256 != artifact.ApprovedSha256 ||
             !string.Equals(previous.RequestBinding?.RequestedFolder ?? previous.Delivery.RequestedFolder,
                 request.Folder, StringComparison.Ordinal) ||
             !string.Equals(previous.RequestBinding?.RequestedFileName ?? previous.Delivery.RequestedFileName,
                 request.FileName, StringComparison.Ordinal)))
            return Outcome(request, DeliveryCode.RequestConflict, detail: "RequestId already names a different selection.");
        string folder = byRequest.Value?.Delivery.RequestedFolder ?? request.Folder;
        string finalLeaf = byRequest.Value?.Delivery.RequestedFileName ?? request.FileName;
        DeliveryFileResult<IDeliveryFileGuard> opened = _files.Open(sourcePath, artifact.ApprovedSha256,
            artifact.ApprovedLength, folder, finalLeaf, request.Artifact.Kind,
            _protectedRoots, loaded.Value.Snapshot?.OriginalSourcePath);
        if (!opened.IsSuccess)
            return Outcome(request, opened.Code, byRequest.Value?.Delivery.DeliveryId, detail: opened.Detail);
        using IDeliveryFileGuard guard = opened.Value!;
        if (byRequest.Value is { } persisted &&
            (persisted.Delivery.VolumeId != guard.VolumeId ||
             persisted.Delivery.DirectoryId != guard.DirectoryId ||
             persisted.Delivery.DestinationKey != guard.DestinationKey))
            return Outcome(request, DeliveryCode.NeedsReconciliation, persisted.Delivery.DeliveryId,
                detail: "Recorded destination identity changed.");

        DeliveryJournalEntry entry;
        bool freshIntent = false;
        if (byRequest.Value is { } known) entry = known;
        else
        {
            Guid deliveryId = Guid.NewGuid();
            Guid attemptId = Guid.NewGuid();
            DeliveryRecord proposed = NewRecord(deliveryId, request, artifact, guard, replacementOf);
            DeliveryAttemptRecord intent = NewAttempt(attemptId, proposed);
            OperationResult<DeliveryJournalEntry> inserted = await _deliveries.CreateOrCoalesceAsync(
                proposed, intent, cancellationToken);
            if (inserted.IsFailure)
                return Outcome(request, DeliveryCode.PersistenceFailed, detail: inserted.Failure.TechnicalDetail);
            entry = inserted.Value;
            freshIntent = entry.Attempt?.AttemptId == attemptId;
        }
        return await ContinueUnderGateAsync(request, entry, artifact, guard, progress, cancellationToken, freshIntent);
    }

    private async Task<DeliveryOutcome> ContinueUnderGateAsync(DeliveryRequest invocation,
        DeliveryJournalEntry entry, ApprovedArtifact artifact, IDeliveryFileGuard guard,
        IProgress<DeliveryProgress>? progress, CancellationToken cancellationToken,
        bool freshIntent = false)
    {
        DeliveryRecord delivery = entry.Delivery;
        if (delivery.Artifact != artifact.Key || delivery.ReviewId != artifact.ReviewId ||
            delivery.ApprovedSha256 != artifact.ApprovedSha256 || delivery.ApprovedLength != artifact.ApprovedLength ||
            delivery.VolumeId != guard.VolumeId || delivery.DirectoryId != guard.DirectoryId ||
            delivery.DestinationKey != guard.DestinationKey)
            return Outcome(invocation, DeliveryCode.RequestConflict, delivery.DeliveryId,
                detail: "Persisted delivery does not match the current exact authority or folder.");
        if (delivery.Status == "Delivered")
        {
            DeliveryFileIdentity expected = FinalIdentity(delivery);
            DeliveryFinalCheck current = guard.CheckFinal(expected);
            return CurrentDeliveredOutcome(invocation, delivery, current);
        }
        if (entry.Attempt is { } attempted &&
            (attempted.State != DeliveryAttemptState.Intent || !freshIntent))
        {
            Report(invocation, progress, DeliveryPhase.Reconciling);
            DeliveryOutcome reconciled = await ReconcileAttemptUnderGateAsync(invocation, entry, artifact,
                guard, progress, cancellationToken);
            if (reconciled.Code is not DeliveryCode.NotDelivered) return reconciled;
            OperationResult<DeliveryAttemptRecord> next = await _deliveries.BeginNextAttemptAsync(
                delivery.DeliveryId, guard.DirectoryId, guard.DestinationKey, cancellationToken);
            if (next.IsFailure)
                return Outcome(invocation, DeliveryCode.PersistenceFailed, delivery.DeliveryId, detail: next.Failure.TechnicalDetail);
            attempted = next.Value;
        }
        else if (entry.Attempt is null)
        {
            OperationResult<DeliveryAttemptRecord> next = await _deliveries.BeginNextAttemptAsync(
                delivery.DeliveryId, guard.DirectoryId, guard.DestinationKey, cancellationToken);
            if (next.IsFailure)
                return Outcome(invocation, DeliveryCode.PersistenceFailed, delivery.DeliveryId, detail: next.Failure.TechnicalDetail);
            attempted = next.Value;
        }
        else attempted = entry.Attempt;

        DeliveryFinalCheck existingFinal = guard.CheckFinal();
        if (existingFinal.Presence == DeliveryFilePresence.Unavailable)
            return Outcome(invocation, DeliveryCode.DestinationUnavailable, delivery.DeliveryId, detail: existingFinal.Detail);
        if (existingFinal.Presence == DeliveryFilePresence.Present)
            return Collision(invocation, delivery.DeliveryId, guard);
        DeliveryFileResult<DeliveryFileIdentity> staged = guard.CreateStaging(attempted.StagingLeaf);
        if (!staged.IsSuccess)
            return Outcome(invocation, staged.Code, delivery.DeliveryId, detail: staged.Detail);
        DeliveryFileIdentity stageIdentity = staged.Value!;
        OperationResult<DeliveryAttemptRecord> markedStage = await _deliveries.MarkStagingAsync(
            attempted.AttemptId, stageIdentity.FileId, stageIdentity.CreatedAtUtc, cancellationToken);
        if (markedStage.IsFailure)
        {
            guard.DeleteHeldStaging(stageIdentity);
            return Outcome(invocation, DeliveryCode.PersistenceFailed, delivery.DeliveryId,
                detail: markedStage.Failure.TechnicalDetail);
        }
        Report(invocation, progress, DeliveryPhase.Copying, 0, artifact.ApprovedLength);
        IProgress<long>? bytes = progress is null ? null : new Progress<long>(copied =>
            Report(invocation, progress, DeliveryPhase.Copying, copied, artifact.ApprovedLength));
        DeliveryFileResult<bool> copiedStage = await guard.CopyAndVerifyStageAsync(
            artifact.ApprovedSha256, artifact.ApprovedLength, bytes, cancellationToken);
        if (!copiedStage.IsSuccess)
            return await FailBeforePublication(invocation, attempted.AttemptId, delivery.DeliveryId,
                stageIdentity, guard, copiedStage.Code, copiedStage.Detail, cancellationToken);
        Report(invocation, progress, DeliveryPhase.Verifying);
        if (!guard.VerifySource().IsSuccess ||
            !await StillApprovedAsync(artifact, CancellationToken.None))
            return await FailBeforePublication(invocation, attempted.AttemptId, delivery.DeliveryId,
                stageIdentity, guard, DeliveryCode.SourceChanged, "Exact approval changed before publication.",
                CancellationToken.None);
        OperationResult<DeliveryAttemptRecord> ready = await _deliveries.MarkReadyAsync(
            attempted.AttemptId, _time.GetUtcNow(), CancellationToken.None);
        if (ready.IsFailure)
        {
            // An unknown SQLite acknowledgement is resolved by readback. Without a durable
            // Ready checkpoint, publication is forbidden.
            OperationResult<DeliveryJournalEntry?> readback = await _deliveries.FindByIdAsync(delivery.DeliveryId, CancellationToken.None);
            if (readback.IsFailure || readback.Value?.Attempt?.AttemptId != attempted.AttemptId ||
                readback.Value.Attempt.State != DeliveryAttemptState.ReadyToPublish)
                return Outcome(invocation, DeliveryCode.PersistenceFailed, delivery.DeliveryId,
                    detail: "Ready checkpoint could not be established by durable readback.");
        }
        if (cancellationToken.IsCancellationRequested)
            return await FailBeforePublication(invocation, attempted.AttemptId, delivery.DeliveryId,
                stageIdentity, guard, DeliveryCode.Cancelled, "Cancelled before publication.", CancellationToken.None);
        Report(invocation, progress, DeliveryPhase.Publishing);
        DeliveryFileResult<bool> published = guard.PublishNoReplace(attempted.StagingLeaf, stageIdentity);
        if (!published.IsSuccess)
        {
            if (published.Code == DeliveryCode.Collision)
            {
                await _deliveries.EndAttemptAsync(attempted.AttemptId, DeliveryAttemptState.Failed,
                    "Collision", CancellationToken.None);
                guard.DeleteHeldStaging(stageIdentity);
                return Collision(invocation, delivery.DeliveryId, guard);
            }
            await _deliveries.EndAttemptAsync(attempted.AttemptId, DeliveryAttemptState.NeedsReconciliation,
                published.Code.ToString(), CancellationToken.None);
            return Outcome(invocation, DeliveryCode.NeedsReconciliation, delivery.DeliveryId,
                detail: published.Detail);
        }
        DeliveryFinalCheck final = guard.CheckFinal(stageIdentity);
        if (final.Presence != DeliveryFilePresence.Present || !SameFileObject(final.Identity, stageIdentity) ||
            final.Length != artifact.ApprovedLength || final.Hash != artifact.ApprovedSha256)
        {
            await _deliveries.EndAttemptAsync(attempted.AttemptId, DeliveryAttemptState.NeedsReconciliation,
                "FinalVerification", CancellationToken.None);
            return Outcome(invocation, DeliveryCode.NeedsReconciliation, delivery.DeliveryId,
                detail: "Published final name has not passed object and byte verification.");
        }
        Report(invocation, progress, DeliveryPhase.Recording);
        OperationResult<DeliveryJournalEntry> recorded = await _deliveries.MarkDeliveredAsync(
            delivery.DeliveryId, attempted.AttemptId, final.Identity!.FileId,
            final.Identity.CreatedAtUtc, _time.GetUtcNow(), CancellationToken.None);
        if (recorded.IsFailure)
        {
            OperationResult<DeliveryJournalEntry?> readback = await _deliveries.FindByIdAsync(delivery.DeliveryId, CancellationToken.None);
            if (readback.IsSuccess && readback.Value?.Delivery.Status == "Delivered" &&
                readback.Value.Delivery.WinningAttemptId == attempted.AttemptId)
                return Outcome(invocation, DeliveryCode.Delivered, delivery.DeliveryId, guard.FinalPath);
            return Outcome(invocation, DeliveryCode.NeedsReconciliation, delivery.DeliveryId,
                detail: "Final file exists, but durable delivery acknowledgement is uncertain.");
        }
        return Outcome(invocation, DeliveryCode.Delivered, delivery.DeliveryId, guard.FinalPath);
    }

    public async Task<DeliveryOutcome> ReconcileAsync(Guid deliveryId,
        IProgress<DeliveryProgress>? progress, CancellationToken cancellationToken)
    {
        OperationResult<DeliveryJournalEntry?> found = await _deliveries.FindByIdAsync(deliveryId, cancellationToken);
        if (found.IsFailure || found.Value is null)
            return EmptyOutcome(DeliveryCode.NotDelivered, deliveryId);
        DeliveryRecord seed = found.Value.Delivery;
        DeliveryRequest invocation = RequestFor(seed);
        using IDisposable sessionGate = await SessionCompletionGate.EnterAsync(seed.Artifact.SessionId, cancellationToken);
        OperationResult<DeliveryJournalEntry?> reread = await _deliveries.FindByIdAsync(deliveryId, cancellationToken);
        if (reread.IsFailure || reread.Value is null)
            return Outcome(invocation, DeliveryCode.PersistenceFailed, deliveryId);
        OperationResult<SessionAggregate?> loaded = await _sessions.LoadAsync(seed.Artifact.SessionId, cancellationToken);
        if (loaded.IsFailure || loaded.Value is null)
            return Outcome(invocation, DeliveryCode.PersistenceFailed, deliveryId);
        ArtifactResolution resolved = ApprovedArtifactResolver.Resolve(loaded.Value, seed.Artifact);
        if (!resolved.IsEligible) return Outcome(invocation, RefusalCode(resolved.Refusal), deliveryId);
        ApprovedArtifact artifact = resolved.Artifact!;
        DeliveryFileResult<IDeliveryFileGuard> opened = _files.Open(_workspace.ResolveAbsolute(artifact.File),
            artifact.ApprovedSha256, artifact.ApprovedLength, seed.RequestedFolder, seed.RequestedFileName,
            seed.Artifact.Kind, _protectedRoots, loaded.Value.Snapshot?.OriginalSourcePath);
        if (!opened.IsSuccess) return Outcome(invocation, opened.Code, deliveryId, detail: opened.Detail);
        using IDeliveryFileGuard guard = opened.Value!;
        if (guard.DestinationKey != seed.DestinationKey || guard.DirectoryId != seed.DirectoryId ||
            guard.VolumeId != seed.VolumeId)
            return Outcome(invocation, DeliveryCode.NeedsReconciliation, deliveryId,
                detail: "Recorded destination identity changed.");
        return await ContinueUnderGateAsync(invocation, reread.Value, artifact, guard, progress, cancellationToken);
    }

    public async Task<DeliveryFileObservation> CheckDeliveredFileAsync(Guid deliveryId, CancellationToken cancellationToken)
    {
        OperationResult<DeliveryJournalEntry?> found = await _deliveries.FindByIdAsync(deliveryId, cancellationToken);
        if (found.IsFailure || found.Value is null)
            return new(deliveryId, Guid.Empty, default, DeliveryAvailability.Uncertain, _time.GetUtcNow(), "",
                "Delivery record unavailable.");
        DeliveryRecord seed = found.Value.Delivery;
        using IDisposable sessionGate = await SessionCompletionGate.EnterAsync(seed.Artifact.SessionId, cancellationToken);
        OperationResult<SessionAggregate?> loaded = await _sessions.LoadAsync(seed.Artifact.SessionId, cancellationToken);
        ArtifactResolution resolved = loaded.IsSuccess && loaded.Value is not null
            ? ApprovedArtifactResolver.Resolve(loaded.Value, seed.Artifact)
            : ArtifactResolution.Refused(ArtifactRefusal.Missing);
        if (!resolved.IsEligible)
            return Observe(seed, DeliveryAvailability.Ineligible, resolved.Refusal.ToString());
        ApprovedArtifact artifact = resolved.Artifact!;
        DeliveryFileResult<IDeliveryFileGuard> opened = _files.Open(_workspace.ResolveAbsolute(artifact.File),
            artifact.ApprovedSha256, artifact.ApprovedLength, seed.RequestedFolder, seed.RequestedFileName,
            seed.Artifact.Kind, _protectedRoots, loaded.Value!.Snapshot?.OriginalSourcePath);
        if (!opened.IsSuccess)
            return Observe(seed, DeliveryAvailability.Unavailable, opened.Detail);
        using IDeliveryFileGuard guard = opened.Value!;
        if (guard.DestinationKey != seed.DestinationKey || guard.DirectoryId != seed.DirectoryId ||
            guard.VolumeId != seed.VolumeId)
            return Observe(seed, DeliveryAvailability.Unavailable, "Recorded destination identity changed.");
        if (seed.Status != "Delivered" || seed.FinalFileId is null || seed.FinalCreationUtc is null)
            return Observe(seed, DeliveryAvailability.Uncertain, "Delivery has no verified final evidence.");
        DeliveryFinalCheck check = guard.CheckFinal(FinalIdentity(seed));
        return Observe(seed, Availability(check, seed), check.Detail);
    }

    public async Task<DeliveryOutcome> ReplaceMissingAsync(MissingDeliveryReplacementRequest request,
        IProgress<DeliveryProgress>? progress, CancellationToken cancellationToken)
    {
        OperationResult<DeliveryJournalEntry?> priorRead = await _deliveries.FindByIdAsync(request.PriorDeliveryId, cancellationToken);
        if (priorRead.IsFailure || priorRead.Value is null)
            return EmptyOutcome(DeliveryCode.NotDelivered, request.PriorDeliveryId);
        DeliveryRecord prior = priorRead.Value.Delivery;
        DeliveryRequest invocation = new(request.RequestId, prior.Artifact, prior.ApprovedSha256,
            prior.ReviewId, prior.RequestedFolder, prior.RequestedFileName, request.SelectionVersion);
        if (request.RequestId == Guid.Empty || request.SelectionVersion == Guid.Empty ||
            !_offers.TryGetValue(prior.Artifact, out Guid version) || version != request.SelectionVersion ||
            request.MissingObservation.DeliveryId != prior.DeliveryId ||
            request.MissingObservation.Availability != DeliveryAvailability.Missing)
            return Outcome(invocation, DeliveryCode.RequestConflict, prior.DeliveryId);
        using IDisposable sessionGate = await SessionCompletionGate.EnterAsync(prior.Artifact.SessionId, cancellationToken);
        OperationResult<DeliveryJournalEntry?> refreshed = await _deliveries.FindByIdAsync(prior.DeliveryId, cancellationToken);
        if (refreshed.IsFailure || refreshed.Value?.Delivery.Status != "Delivered")
            return Outcome(invocation, DeliveryCode.NotDelivered, prior.DeliveryId);
        prior = refreshed.Value.Delivery;
        // A recorded successor wins before checking the old path, even when it is absent now.
        OperationResult<DeliveryJournalEntry?> successor = await _deliveries.FindSuccessorAsync(prior.DeliveryId, cancellationToken);
        if (successor.IsFailure) return Outcome(invocation, DeliveryCode.PersistenceFailed, prior.DeliveryId);
        if (successor.Value is not null)
        {
            DeliveryRequest existing = invocation with { Folder = successor.Value.Delivery.RequestedFolder,
                FileName = successor.Value.Delivery.RequestedFileName };
            return await DeliverUnderGateAsync(existing, prior.DeliveryId, progress, cancellationToken);
        }
        OperationResult<SessionAggregate?> loaded = await _sessions.LoadAsync(prior.Artifact.SessionId, cancellationToken);
        if (loaded.IsFailure || loaded.Value is null)
            return Outcome(invocation, DeliveryCode.PersistenceFailed, prior.DeliveryId);
        ArtifactResolution resolved = ApprovedArtifactResolver.Resolve(loaded.Value, prior.Artifact);
        if (!resolved.IsEligible) return Outcome(invocation, RefusalCode(resolved.Refusal), prior.DeliveryId);
        ApprovedArtifact artifact = resolved.Artifact!;
        if (artifact.ApprovedSha256 != prior.ApprovedSha256 || artifact.ReviewId != prior.ReviewId)
            return Outcome(invocation, DeliveryCode.RequestConflict, prior.DeliveryId,
                detail: "Approval changed since the original delivery.");
        DeliveryFileResult<IDeliveryFileGuard> opened = _files.Open(_workspace.ResolveAbsolute(artifact.File),
            artifact.ApprovedSha256, artifact.ApprovedLength, prior.RequestedFolder, prior.RequestedFileName,
            prior.Artifact.Kind, _protectedRoots, loaded.Value.Snapshot?.OriginalSourcePath);
        if (!opened.IsSuccess) return Outcome(invocation, opened.Code, prior.DeliveryId, detail: opened.Detail);
        using IDeliveryFileGuard guard = opened.Value!;
        if (guard.DestinationKey != prior.DestinationKey || guard.DirectoryId != prior.DirectoryId ||
            guard.VolumeId != prior.VolumeId)
            return Outcome(invocation, DeliveryCode.DestinationUnavailable, prior.DeliveryId);
        DeliveryFinalCheck old = guard.CheckFinal(FinalIdentity(prior));
        if (old.Presence == DeliveryFilePresence.Unavailable)
            return Outcome(invocation, DeliveryCode.DestinationUnavailable, prior.DeliveryId);
        if (old.Presence == DeliveryFilePresence.Present)
            return Outcome(invocation, DeliveryCode.RequestConflict, prior.DeliveryId,
                detail: "The old final path is present again; refresh availability.");
        Guid deliveryId = Guid.NewGuid(); Guid attemptId = Guid.NewGuid();
        DeliveryRecord proposed = NewRecord(deliveryId, invocation, artifact, guard, prior.DeliveryId);
        OperationResult<DeliveryJournalEntry> made = await _deliveries.CreateOrCoalesceAsync(
            proposed, NewAttempt(attemptId, proposed), cancellationToken);
        if (made.IsFailure) return Outcome(invocation, DeliveryCode.PersistenceFailed, prior.DeliveryId,
            detail: made.Failure.TechnicalDetail);
        return await ContinueUnderGateAsync(invocation, made.Value, artifact, guard, progress,
            cancellationToken, made.Value.Attempt?.AttemptId == attemptId);
    }

    public async Task<SelectionLeaseResult> AcquireDeliveredSelectionAsync(Guid deliveryId,
        CancellationToken cancellationToken)
    {
        OperationResult<DeliveryJournalEntry?> found = await _deliveries.FindByIdAsync(deliveryId, cancellationToken);
        if (found.IsFailure || found.Value?.Delivery is not { Status: "Delivered" } seed ||
            seed.FinalFileId is null || seed.FinalCreationUtc is null)
            return new(DeliveryAvailability.Uncertain, null, "No verified delivered evidence.");
        using IDisposable sessionGate = await SessionCompletionGate.EnterAsync(seed.Artifact.SessionId, cancellationToken);
        OperationResult<SessionAggregate?> loaded = await _sessions.LoadAsync(seed.Artifact.SessionId, cancellationToken);
        ArtifactResolution resolved = loaded.IsSuccess && loaded.Value is not null
            ? ApprovedArtifactResolver.Resolve(loaded.Value, seed.Artifact)
            : ArtifactResolution.Refused(ArtifactRefusal.Missing);
        if (!resolved.IsEligible) return new(DeliveryAvailability.Ineligible, null, resolved.Refusal.ToString());
        ApprovedArtifact artifact = resolved.Artifact!;
        DeliveryFileResult<IDeliveryFileGuard> opened = _files.Open(_workspace.ResolveAbsolute(artifact.File),
            artifact.ApprovedSha256, artifact.ApprovedLength, seed.RequestedFolder, seed.RequestedFileName,
            seed.Artifact.Kind, _protectedRoots, loaded.Value!.Snapshot?.OriginalSourcePath);
        if (!opened.IsSuccess) return new(DeliveryAvailability.Unavailable, null, opened.Detail);
        IDeliveryFileGuard guard = opened.Value!;
        if (guard.DestinationKey != seed.DestinationKey || guard.DirectoryId != seed.DirectoryId ||
            guard.VolumeId != seed.VolumeId)
        { guard.Dispose(); return new(DeliveryAvailability.Unavailable, null, "Destination identity changed."); }
        SelectionLeaseResult result = guard.AcquireSelection(deliveryId, FinalIdentity(seed),
            seed.ApprovedSha256, seed.ApprovedLength);
        if (result.Lease is null) guard.Dispose();
        return result;
    }

    private async Task<DeliveryOutcome> ReconcileAttemptUnderGateAsync(DeliveryRequest invocation,
        DeliveryJournalEntry entry, ApprovedArtifact artifact, IDeliveryFileGuard guard,
        IProgress<DeliveryProgress>? progress, CancellationToken cancellationToken)
    {
        DeliveryAttemptRecord attempt = entry.Attempt!;
        DeliveryRecord delivery = entry.Delivery;
        if (attempt.State is DeliveryAttemptState.Failed or DeliveryAttemptState.Cancelled)
            return Outcome(invocation, DeliveryCode.NotDelivered, delivery.DeliveryId);
        if (attempt.State is DeliveryAttemptState.Intent or DeliveryAttemptState.Staging)
        {
            // Neither prepublication state is allowed to publish. A previous process may
            // have created a stage without recording its identity; leave that name alone.
            DeliveryFinalCheck beforeRetry = guard.CheckFinal();
            if (beforeRetry.Presence == DeliveryFilePresence.Unavailable)
                return Outcome(invocation, DeliveryCode.DestinationUnavailable, delivery.DeliveryId,
                    detail: beforeRetry.Detail);
            if (beforeRetry.Presence == DeliveryFilePresence.Present)
                return Outcome(invocation, DeliveryCode.NeedsReconciliation, delivery.DeliveryId,
                    detail: "Final name exists during an interrupted prepublication attempt.");
            if (attempt.State == DeliveryAttemptState.Intent)
            {
                OperationResult<DeliveryAttemptRecord> endedIntent = await _deliveries.EndAttemptAsync(
                    attempt.AttemptId, DeliveryAttemptState.Failed, "InterruptedUnownedIntent", CancellationToken.None);
                return endedIntent.IsSuccess
                    ? Outcome(invocation, DeliveryCode.NotDelivered, delivery.DeliveryId)
                    : Outcome(invocation, DeliveryCode.PersistenceFailed, delivery.DeliveryId,
                        detail: endedIntent.Failure.TechnicalDetail);
            }
            if (attempt.StagingFileId is null || attempt.StagingCreationUtc is null)
                return Outcome(invocation, DeliveryCode.NeedsReconciliation, delivery.DeliveryId);
            DeliveryFileIdentity stage = new(delivery.VolumeId, attempt.StagingFileId, attempt.StagingCreationUtc.Value);
            DeliveryFileResult<DeliveryFileIdentity> held = guard.OpenOwnedStaging(attempt.StagingLeaf, stage);
            if (!held.IsSuccess && held.Code != DeliveryCode.SourceMissing)
                return Outcome(invocation, DeliveryCode.NeedsReconciliation, delivery.DeliveryId, detail: held.Detail);
            if (held.IsSuccess)
            {
                DeliveryFileResult<bool> removed = guard.DeleteHeldStaging(stage);
                if (!removed.IsSuccess)
                    return Outcome(invocation, DeliveryCode.NeedsReconciliation, delivery.DeliveryId, detail: removed.Detail);
            }
            OperationResult<DeliveryAttemptRecord> endedStage = await _deliveries.EndAttemptAsync(attempt.AttemptId,
                DeliveryAttemptState.Failed,
                "InterruptedStaging", CancellationToken.None);
            return endedStage.IsSuccess
                ? Outcome(invocation, DeliveryCode.NotDelivered, delivery.DeliveryId)
                : Outcome(invocation, DeliveryCode.PersistenceFailed, delivery.DeliveryId,
                    detail: endedStage.Failure.TechnicalDetail);
        }
        if (attempt.StagingFileId is null || attempt.StagingCreationUtc is null ||
            attempt.StageVerifiedAtUtc is null)
            return Outcome(invocation, DeliveryCode.NeedsReconciliation, delivery.DeliveryId,
                detail: "Ready checkpoint lacks staged ownership metadata.");
        DeliveryFileIdentity expected = new(delivery.VolumeId, attempt.StagingFileId, attempt.StagingCreationUtc.Value);
        DeliveryFinalCheck final = guard.CheckFinal(expected);
        if (final.Presence == DeliveryFilePresence.Present)
        {
            // No staged handle survives process restart. If NTFS tunneling changed creation
            // time before a crash, the stage-to-final lineage is ambiguous: fail closed.
            if (final.Identity != expected || final.Hash != artifact.ApprovedSha256 ||
                final.Length != artifact.ApprovedLength)
                return Outcome(invocation, DeliveryCode.NeedsReconciliation, delivery.DeliveryId,
                    detail: "Final path does not match the ready checkpoint.");
            DeliveryFileResult<DeliveryFileIdentity> oldStage = guard.OpenOwnedStaging(attempt.StagingLeaf, expected);
            if (oldStage.IsSuccess || oldStage.Code != DeliveryCode.SourceMissing)
                return Outcome(invocation, oldStage.Code == DeliveryCode.Unavailable
                    ? DeliveryCode.DestinationUnavailable : DeliveryCode.NeedsReconciliation,
                    delivery.DeliveryId, detail: "Final and recorded staging name cannot prove final-only ownership: " + oldStage.Detail);
            return await CompleteVerifiedFinalAsync(invocation, delivery, attempt, final.Identity!, guard, cancellationToken);
        }
        if (final.Presence == DeliveryFilePresence.Unavailable)
            return Outcome(invocation, DeliveryCode.DestinationUnavailable, delivery.DeliveryId, detail: final.Detail);
        DeliveryFileResult<DeliveryFileIdentity> staged = guard.OpenOwnedStaging(attempt.StagingLeaf, expected);
        if (!staged.IsSuccess)
            return Outcome(invocation, DeliveryCode.NeedsReconciliation, delivery.DeliveryId,
                detail: "Ready staged object is not safely available: " + staged.Detail);
        DeliveryFileResult<bool> verified = guard.VerifyStage(artifact.ApprovedSha256, artifact.ApprovedLength);
        if (!verified.IsSuccess)
            return Outcome(invocation, DeliveryCode.NeedsReconciliation, delivery.DeliveryId, detail: verified.Detail);
        if (!guard.VerifySource().IsSuccess || !await StillApprovedAsync(artifact, CancellationToken.None))
            return Outcome(invocation, DeliveryCode.SourceChanged, delivery.DeliveryId);
        if (cancellationToken.IsCancellationRequested)
            return Outcome(invocation, DeliveryCode.Cancelled, delivery.DeliveryId);
        Report(invocation, progress, DeliveryPhase.Publishing);
        DeliveryFileResult<bool> published = guard.PublishNoReplace(attempt.StagingLeaf, expected);
        if (!published.IsSuccess)
            return published.Code == DeliveryCode.Collision
                ? Collision(invocation, delivery.DeliveryId, guard)
                : Outcome(invocation, DeliveryCode.NeedsReconciliation, delivery.DeliveryId, detail: published.Detail);
        final = guard.CheckFinal(expected);
        if (final.Presence != DeliveryFilePresence.Present || !SameFileObject(final.Identity, expected) ||
            final.Hash != artifact.ApprovedSha256 || final.Length != artifact.ApprovedLength)
            return Outcome(invocation, DeliveryCode.NeedsReconciliation, delivery.DeliveryId);
        return await CompleteVerifiedFinalAsync(invocation, delivery, attempt, final.Identity!, guard, cancellationToken);
    }

    private async Task<DeliveryOutcome> CompleteVerifiedFinalAsync(DeliveryRequest invocation,
        DeliveryRecord delivery, DeliveryAttemptRecord attempt, DeliveryFileIdentity final,
        IDeliveryFileGuard guard, CancellationToken cancellationToken)
    {
        OperationResult<DeliveryJournalEntry> marked = await _deliveries.MarkDeliveredAsync(delivery.DeliveryId,
            attempt.AttemptId, final.FileId, final.CreatedAtUtc, _time.GetUtcNow(), CancellationToken.None);
        if (marked.IsSuccess) return Outcome(invocation, DeliveryCode.Delivered, delivery.DeliveryId, guard.FinalPath);
        OperationResult<DeliveryJournalEntry?> readback = await _deliveries.FindByIdAsync(delivery.DeliveryId, CancellationToken.None);
        return readback.IsSuccess && readback.Value?.Delivery.WinningAttemptId == attempt.AttemptId
            ? Outcome(invocation, DeliveryCode.Delivered, delivery.DeliveryId, guard.FinalPath)
            : Outcome(invocation, DeliveryCode.NeedsReconciliation, delivery.DeliveryId,
                detail: "Final verified, but durable acknowledgement is uncertain.");
    }

    private async Task<DeliveryOutcome> FailBeforePublication(DeliveryRequest request, Guid attemptId,
        Guid deliveryId, DeliveryFileIdentity stage, IDeliveryFileGuard guard, DeliveryCode code,
        string? detail, CancellationToken cancellationToken)
    {
        DeliveryFileResult<bool> deleted = guard.DeleteHeldStaging(stage);
        if (!deleted.IsSuccess)
        {
            await _deliveries.EndAttemptAsync(attemptId, DeliveryAttemptState.NeedsReconciliation,
                "CleanupUncertain", CancellationToken.None);
            return Outcome(request, DeliveryCode.NeedsReconciliation, deliveryId, detail: deleted.Detail);
        }
        OperationResult<DeliveryAttemptRecord> ended = await _deliveries.EndAttemptAsync(attemptId,
            code == DeliveryCode.Cancelled ? DeliveryAttemptState.Cancelled : DeliveryAttemptState.Failed,
            code.ToString(), CancellationToken.None);
        return ended.IsFailure ? Outcome(request, DeliveryCode.PersistenceFailed, deliveryId,
            detail: ended.Failure.TechnicalDetail) : Outcome(request, code, deliveryId, detail: detail);
    }

    private async Task<bool> StillApprovedAsync(ApprovedArtifact artifact, CancellationToken cancellationToken)
    {
        OperationResult<SessionAggregate?> fresh = await _sessions.LoadAsync(artifact.Key.SessionId, cancellationToken);
        if (fresh.IsFailure || fresh.Value is null) return false;
        ArtifactResolution current = ApprovedArtifactResolver.Resolve(fresh.Value, artifact.Key);
        return current.IsEligible && current.Artifact!.ApprovedSha256 == artifact.ApprovedSha256 &&
            current.Artifact.ApprovedLength == artifact.ApprovedLength && current.Artifact.ReviewId == artifact.ReviewId &&
            current.Artifact.File == artifact.File;
    }

    private DeliveryOutcome Collision(DeliveryRequest request, Guid deliveryId, IDeliveryFileGuard guard)
    {
        string? alternative = _files.SuggestAlternative(request.FileName,
            leaf => File.Exists(Path.Combine(guard.ResolvedFolder, leaf)));
        return Outcome(request, DeliveryCode.Collision, deliveryId, suggestion: alternative);
    }

    private static DeliveryAvailability Availability(DeliveryFinalCheck check, DeliveryRecord delivery) =>
        check.Presence switch
        {
            DeliveryFilePresence.Absent => DeliveryAvailability.Missing,
            DeliveryFilePresence.Unavailable => DeliveryAvailability.Unavailable,
            _ when check.Identity == FinalIdentity(delivery) && check.Hash == delivery.ApprovedSha256 &&
                   check.Length == delivery.ApprovedLength => DeliveryAvailability.VerifiedNow,
            _ => DeliveryAvailability.Changed,
        };

    private DeliveryFileObservation Observe(DeliveryRecord delivery, DeliveryAvailability availability,
        string? detail = null) => new(delivery.DeliveryId, delivery.RequestId, delivery.Artifact, availability,
            _time.GetUtcNow(), delivery.FinalPath, detail);

    private static DeliveryOutcome CurrentDeliveredOutcome(DeliveryRequest request, DeliveryRecord delivery,
        DeliveryFinalCheck check) => Availability(check, delivery) switch
        {
            DeliveryAvailability.VerifiedNow => Outcome(request, DeliveryCode.AlreadyDelivered,
                delivery.DeliveryId, delivery.FinalPath),
            DeliveryAvailability.Missing => Outcome(request, DeliveryCode.DeliveredFileMissing, delivery.DeliveryId),
            DeliveryAvailability.Changed => Outcome(request, DeliveryCode.DeliveredFileChanged, delivery.DeliveryId),
            _ => Outcome(request, DeliveryCode.Unavailable, delivery.DeliveryId, detail: check.Detail),
        };

    private static DeliveryFileIdentity FinalIdentity(DeliveryRecord delivery) =>
        new(delivery.VolumeId, delivery.FinalFileId!, delivery.FinalCreationUtc!.Value);

    // NTFS file-system tunneling can preserve a recently removed final name's creation time
    // when a held staged object is renamed into that name. Volume + FileId identify the object;
    // record the final handle's actual creation time separately from the staging checkpoint.
    private static bool SameFileObject(DeliveryFileIdentity? actual, DeliveryFileIdentity expected) =>
        actual is not null && actual.VolumeId == expected.VolumeId && actual.FileId == expected.FileId;

    private static DeliveryRequest RequestFor(DeliveryRecord delivery) =>
        new(delivery.RequestId, delivery.Artifact, delivery.ApprovedSha256, delivery.ReviewId,
            delivery.RequestedFolder, delivery.RequestedFileName, Guid.Empty);

    private DeliveryRecord NewRecord(Guid id, DeliveryRequest request, ApprovedArtifact artifact,
        IDeliveryFileGuard guard, Guid? replacementOf) =>
        new(id, request.RequestId, 0, artifact.Key, artifact.ReviewId,
            artifact.ApprovalSubjectKind.ToString(), artifact.ApprovalSubjectId,
            artifact.SourceRevisionId, artifact.ApprovedSha256, artifact.ApprovedLength,
            request.Folder, request.FileName, guard.ResolvedFolder, guard.FinalPath,
            guard.VolumeId, guard.DirectoryId, guard.DestinationKey, replacementOf,
            "Pending", _time.GetUtcNow());

    private DeliveryAttemptRecord NewAttempt(Guid id, DeliveryRecord delivery) =>
        new(id, delivery.DeliveryId, 1, delivery.DestinationKey, DeliveryAttemptState.Intent,
            ".printflow-" + id.ToString("N") + ".partial", delivery.DirectoryId,
            null, null, delivery.ApprovedSha256, delivery.ApprovedLength,
            null, null, _time.GetUtcNow(), null);

    private static DeliveryState ToState(DeliveryJournalEntry entry)
    {
        DeliveryRecord d = entry.Delivery;
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{d.DeliveryId:D}|{d.Status}|{entry.Attempt?.State}|{d.VerifiedAtUtc:O}"));
        return new(d.DeliveryId, d.RequestId, d.Artifact, d.ReviewId, d.RequestedFolder,
            d.RequestedFileName, d.ResolvedFolder, d.FinalPath, d.Status, entry.Attempt?.State,
            entry.Attempt?.AttemptId, d.VerifiedAtUtc, d.ReplacementOfDeliveryId,
            new Guid(bytes.AsSpan(0, 16)));
    }

    private static DeliveryCode RefusalCode(ArtifactRefusal refusal) =>
        refusal == ArtifactRefusal.ApprovalEvidenceMissing ? DeliveryCode.ApprovalEvidenceMissing :
            DeliveryCode.IneligibleArtifact;

    private static void Report(DeliveryRequest request, IProgress<DeliveryProgress>? progress,
        DeliveryPhase phase, long copied = 0, long total = 0) =>
        progress?.Report(new(request.RequestId, request.Artifact, request.SelectionVersion,
            phase, copied, total));

    private static DeliveryOutcome Outcome(DeliveryRequest request, DeliveryCode code,
        Guid? deliveryId = null, string? path = null, string? suggestion = null, string? detail = null) =>
        new(request.RequestId, request.Artifact, request.SelectionVersion, code,
            deliveryId, path, suggestion, detail);

    private static DeliveryOutcome EmptyOutcome(DeliveryCode code, Guid deliveryId) =>
        new(Guid.Empty, default, Guid.Empty, code, deliveryId);
}
