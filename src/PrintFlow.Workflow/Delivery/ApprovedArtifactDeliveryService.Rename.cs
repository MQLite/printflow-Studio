using PrintFlow.Domain.Results;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Workflow.Delivery;

public sealed partial class ApprovedArtifactDeliveryService
{
    public async Task<DeliveryOutcome> RenameDeliveredAsync(Guid deliveryId, string expectedPath, string fileName,
        CancellationToken cancellationToken)
    {
        OperationResult<DeliveryJournalEntry?> found = await _deliveries.FindByIdAsync(deliveryId, cancellationToken);
        if (found.IsFailure || found.Value is null) return EmptyOutcome(DeliveryCode.NotDelivered, deliveryId);
        using IDisposable sessionGate = await SessionCompletionGate.EnterAsync(found.Value.Delivery.Artifact.SessionId, cancellationToken);
        found = await _deliveries.FindByIdAsync(deliveryId, cancellationToken);
        if (found.IsFailure || found.Value is null) return EmptyOutcome(DeliveryCode.NotDelivered, deliveryId);
        DeliveryRecord delivery = found.Value.Delivery;
        if (delivery.FinalPath != expectedPath && delivery.RequestedFileName != fileName)
            return Outcome(RequestFor(delivery), DeliveryCode.RequestConflict, deliveryId);
        return await RenameUnderGateAsync(found.Value, fileName, cancellationToken);
    }

    private async Task<DeliveryOutcome> RenameUnderGateAsync(DeliveryJournalEntry entry, string? requestedLeaf,
        CancellationToken cancellationToken)
    {
        DeliveryRecord delivery = entry.Delivery;
        DeliveryRequest invocation = RequestFor(delivery);
        DeliveryOutcome Result(DeliveryCode code, string? path = null, string? detail = null) =>
            Outcome(invocation, code, delivery.DeliveryId, path, detail: detail);

        if (delivery.Status != "Delivered" || delivery.FinalFileId is null || delivery.FinalCreationUtc is null)
            return Result(DeliveryCode.NotDelivered);
        if (entry.PendingRename is { } pending)
            return await RecoverRenameAsync(delivery, pending, requestedLeaf);
        OperationResult<SessionAggregate?> loaded = await _sessions.LoadAsync(delivery.Artifact.SessionId, cancellationToken);
        if (loaded.IsFailure || loaded.Value is null) return Result(DeliveryCode.PersistenceFailed);
        ArtifactResolution resolved = ApprovedArtifactResolver.Resolve(loaded.Value, delivery.Artifact);
        if (!resolved.IsEligible) return Result(RefusalCode(resolved.Refusal));
        ApprovedArtifact artifact = resolved.Artifact!;
        if (artifact.ReviewId != delivery.ReviewId || artifact.ApprovedSha256 != delivery.ApprovedSha256 ||
            artifact.ApprovedLength != delivery.ApprovedLength)
            return Result(DeliveryCode.RequestConflict);

        string source = _workspace.ResolveAbsolute(artifact.File);
        DeliveryFileResult<IDeliveryFileGuard> Open(string leaf) => _files.Open(source, artifact.ApprovedSha256,
            artifact.ApprovedLength, delivery.RequestedFolder, leaf, artifact.Key.Kind, _protectedRoots,
            loaded.Value.Snapshot?.OriginalSourcePath);
        DeliveryFileIdentity identity = FinalIdentity(delivery);
        DeliveryFileResult<IDeliveryFileGuard> original = Open(delivery.RequestedFileName);
        if (!original.IsSuccess) return Result(original.Code, detail: original.Detail);
        using IDeliveryFileGuard oldGuard = original.Value!;
        if (!SameRenameFolder(delivery, oldGuard) || oldGuard.DestinationKey != delivery.DestinationKey)
            return Result(DeliveryCode.DestinationUnavailable);

        if (requestedLeaf is null || requestedLeaf == delivery.RequestedFileName)
            return CurrentDeliveredOutcome(invocation, delivery, oldGuard.CheckFinal(identity));
        DeliveryFileResult<IDeliveryFileGuard> destination = Open(requestedLeaf);
        if (!destination.IsSuccess) return Result(destination.Code, detail: destination.Detail);
        using IDeliveryFileGuard newGuard = destination.Value!;
        if (!SameRenameFolder(delivery, newGuard))
            return Result(DeliveryCode.DestinationUnavailable);

        if (oldGuard.DestinationKey != newGuard.DestinationKey)
        {
            DeliveryFinalCheck atNewName = newGuard.CheckFinal();
            if (atNewName.Presence == DeliveryFilePresence.Present) return Result(DeliveryCode.Collision);
            if (atNewName.Presence == DeliveryFilePresence.Unavailable) return Result(DeliveryCode.Unavailable, detail: atNewName.Detail);
        }
        var rename = new DeliveryRename(Guid.NewGuid(), delivery.DeliveryId, requestedLeaf, newGuard.FinalPath, newGuard.DestinationKey);
        bool intentRecorded = false;
        DeliveryFileResult<DeliveryFileIdentity> moved = await oldGuard.RenameFinalNoReplaceAsync(requestedLeaf, artifact.Key.Kind,
            identity, artifact.ApprovedSha256, artifact.ApprovedLength, async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                OperationResult<bool> began = await _deliveries.BeginRenameAsync(delivery, rename);
                if (began.IsFailure)
                    return DeliveryFileResult<bool>.Fail(DeliveryCode.PersistenceFailed, began.Failure.TechnicalDetail);
                intentRecorded = began.Value;
                return intentRecorded ? DeliveryFileResult<bool>.Ok(true) :
                    DeliveryFileResult<bool>.Fail(DeliveryCode.RequestConflict, "The delivery or destination changed.");
            });
        if (!intentRecorded || moved.Code == DeliveryCode.NeedsReconciliation) return Result(moved.Code, detail: moved.Detail);
        return await FinishRenameAsync(delivery, rename, moved.Value?.CreatedAtUtc, moved.Code,
            moved.IsSuccess ? newGuard.FinalPath : null, moved.Detail);
    }

    private async Task<DeliveryOutcome> RecoverRenameAsync(DeliveryRecord delivery, DeliveryRename rename, string? requestedLeaf)
    {
        DeliveryOutcome uncertain = Outcome(RequestFor(delivery), DeliveryCode.NeedsReconciliation, delivery.DeliveryId);
        if (requestedLeaf is not null && requestedLeaf != rename.FileName) return uncertain;

        // The immutable delivery and pending intent authorize verification after a move, even
        // if the managed source was retired. Recovery only reads files and settles the journal.
        DeliveryFileResult<IDeliveryDestinationGuard> Open(string leaf) => _files.OpenForRenameRecovery(
            delivery.RequestedFolder, leaf, delivery.Artifact.Kind, _protectedRoots);
        DeliveryFileResult<IDeliveryDestinationGuard> destination = Open(rename.FileName);
        if (!destination.IsSuccess) return uncertain;
        using IDeliveryDestinationGuard newGuard = destination.Value!;
        if (!SameRenameFolder(delivery, newGuard) || newGuard.DestinationKey != rename.DestinationKey) return uncertain;

        DeliveryFileIdentity identity = FinalIdentity(delivery);
        DeliveryFinalCheck atNewName = newGuard.CheckFinal();
        bool matches = SameFileObject(atNewName.Identity, identity) && atNewName.Length == delivery.ApprovedLength &&
                       atNewName.Hash == delivery.ApprovedSha256;
        DeliveryCode aborted = requestedLeaf is null ? DeliveryCode.AlreadyDelivered : DeliveryCode.RequestConflict;
        if (rename.DestinationKey == delivery.DestinationKey)
        {
            // Both spellings open the same NTFS entry. The held handle supplies its actual name.
            if (!matches) return uncertain;
            string? actualLeaf = newGuard.ReadHeldFileName();
            if (actualLeaf == rename.FileName)
                return await FinishRenameAsync(delivery, rename, atNewName.Identity!.CreatedAtUtc, DeliveryCode.Delivered, newGuard.FinalPath);
            return actualLeaf == delivery.RequestedFileName
                ? await FinishRenameAsync(delivery, rename, null, aborted, delivery.FinalPath) : uncertain;
        }

        DeliveryFileResult<IDeliveryDestinationGuard> original = Open(delivery.RequestedFileName);
        if (!original.IsSuccess) return uncertain;
        using IDeliveryDestinationGuard oldGuard = original.Value!;
        if (!SameRenameFolder(delivery, oldGuard) || oldGuard.DestinationKey != delivery.DestinationKey) return uncertain;
        DeliveryFinalCheck atOldName = oldGuard.CheckFinal(identity);
        if (matches && atOldName.Presence != DeliveryFilePresence.Unavailable && !SameFileObject(atOldName.Identity, identity))
            return await FinishRenameAsync(delivery, rename, atNewName.Identity!.CreatedAtUtc, DeliveryCode.Delivered, newGuard.FinalPath);
        if (SameFileObject(atNewName.Identity, identity) || Availability(atOldName, delivery) != DeliveryAvailability.VerifiedNow)
            return uncertain;
        return await FinishRenameAsync(delivery, rename, null, aborted, oldGuard.FinalPath);
    }

    private async Task<DeliveryOutcome> FinishRenameAsync(DeliveryRecord delivery, DeliveryRename rename,
        DateTimeOffset? creation, DeliveryCode code, string? path, string? detail = null)
    {
        OperationResult<bool> ended = await _deliveries.EndRenameAsync(rename, creation);
        DeliveryRequest invocation = RequestFor(delivery);
        return ended.IsSuccess && ended.Value ? Outcome(invocation, code, delivery.DeliveryId, path, detail: detail) :
            Outcome(invocation, DeliveryCode.NeedsReconciliation, delivery.DeliveryId);
    }

    private static bool SameRenameFolder(DeliveryRecord delivery, IDeliveryDestinationGuard guard) =>
        guard.VolumeId == delivery.VolumeId && guard.DirectoryId == delivery.DirectoryId;
}
