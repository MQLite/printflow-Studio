using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Delivery;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Workflow.Services;

/// <summary>Copies the exact image shown at manual handoff to an operator-selected file.</summary>
public sealed class ManualHandoffCopyService(ISessionRepository repository, IWorkspace workspace,
    IDeliveryFileSystem files, IReadOnlyList<string> protectedRoots)
{
    public async Task<DeliveryFileResult<string>> SaveAsync(SessionId id, StepKind step, RevisionId expectedRevision,
        string destination, CancellationToken cancellationToken)
    {
        using IDisposable lease = await SessionCompletionGate.EnterAsync(id, cancellationToken);
        var loaded = await repository.LoadAsync(id, cancellationToken);
        if (loaded.IsFailure || loaded.Value is not { } aggregate)
            return DeliveryFileResult<string>.Fail(DeliveryCode.Unavailable, "The session could not be loaded.");

        var snapshot = aggregate.ToSnapshot();
        if (snapshot.SessionState != SessionState.HandedOff || snapshot.CurrentStep?.Step != step ||
            !ManualResultEligibility.Supports(step) ||
            SessionView.ResolveArtefact(snapshot, aggregate.Revisions)?.RevisionId != expectedRevision ||
            aggregate.Revisions.SingleOrDefault(r => r.Id == expectedRevision) is not { IsValid: true, RetentionReleasedAtUtc: null } revision)
            return DeliveryFileResult<string>.Fail(DeliveryCode.IneligibleArtifact, "The handoff image is no longer current.");

        if (!Path.IsPathFullyQualified(destination) || Path.GetDirectoryName(destination) is not { } folder)
            return DeliveryFileResult<string>.Fail(DeliveryCode.InvalidName, "Choose an absolute destination file path.");
        var opened = files.OpenImageCopy(workspace.ResolveAbsolute(revision.File), revision.Facts,
            folder, Path.GetFileName(destination), protectedRoots);
        if (!opened.IsSuccess) return DeliveryFileResult<string>.Fail(opened.Code, opened.Detail!);

        using IDeliveryFileGuard guard = opened.Value!;
        string stage = $".printflow-{Guid.NewGuid():N}.partial";
        var created = guard.CreateStaging(stage);
        if (!created.IsSuccess) return DeliveryFileResult<string>.Fail(created.Code, created.Detail!);

        var copied = await guard.CopyAndVerifyStageAsync(revision.Sha256, revision.Facts.ByteLength, null, cancellationToken);
        if (copied.IsSuccess)
            copied = cancellationToken.IsCancellationRequested
                ? DeliveryFileResult<bool>.Fail(DeliveryCode.Cancelled, "Image copy cancelled before publication.")
                : guard.PublishNoReplace(stage, created.Value!);

        if (copied.IsSuccess) return DeliveryFileResult<string>.Ok(guard.FinalPath);
        if (copied.Code != DeliveryCode.NeedsReconciliation) guard.DeleteHeldStaging(created.Value!);
        return DeliveryFileResult<string>.Fail(copied.Code, copied.Detail!);
    }
}
