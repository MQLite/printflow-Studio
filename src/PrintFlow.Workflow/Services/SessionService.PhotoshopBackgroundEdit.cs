using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Settings;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Workflow.Services;

public sealed partial class SessionService
{
    public async Task<OperationResult<PhotoshopBackgroundEditSource>> GetPhotoshopBackgroundEditSourceAsync(
        SessionId id, PhotoshopBackgroundEditTarget target, CancellationToken cancellationToken)
    {
        var loaded = await _repository.LoadAsync(id, cancellationToken);
        if (loaded.IsFailure) return OperationResult.Fail<PhotoshopBackgroundEditSource>(loaded.Failure);
        if (_manualResults is null || loaded.Value is not { } aggregate ||
            PhotoshopBackgroundCandidate(aggregate.ToSnapshot(), aggregate.Revisions) is not { } candidate ||
            candidate.Target != target)
            return OperationResult.Fail<PhotoshopBackgroundEditSource>(PhotoshopTargetChanged());

        return OperationResult.Ok(new PhotoshopBackgroundEditSource(
            aggregate.Session.Workspace, candidate.Image.File, candidate.Image.Facts, _idGenerator.NewId()));
    }

    public async Task<OperationResult<SessionView>> ImportPhotoshopBackgroundEditAsync(
        SessionId id, PhotoshopBackgroundEditTarget target, PhotoshopBackgroundSave save, string? operatorName,
        CancellationToken cancellationToken)
    {
        using IDisposable lease = await SessionCompletionGate.EnterAsync(id, cancellationToken);
        var loaded = await _repository.LoadAsync(id, cancellationToken);
        if (loaded.IsFailure) return OperationResult.Fail<SessionView>(loaded.Failure);
        if (loaded.Value is not { } aggregate)
            return OperationResult.Fail<SessionView>(PhotoshopTargetChanged());

        WorkflowSnapshot snapshot = aggregate.ToSnapshot(ConfiguredRecommendations());
        if (_manualResults is null || PhotoshopBackgroundCandidate(snapshot, aggregate.Revisions) is not { } candidate ||
            candidate.Target != target ||
            FindRevision(aggregate, snapshot.UpstreamRevisionOf(StepKind.BackgroundRemoval)) is not { } input)
            return OperationResult.Fail<SessionView>(PhotoshopTargetChanged());

        Revision shown = candidate.Image;
        var preflight = await _manualResults.PreflightAsync(StepKind.BackgroundRemoval, shown.Facts, save.PreviewPath, cancellationToken);
        if (preflight.IsFailure) return OperationResult.Fail<SessionView>(preflight.Failure);

        var verified = await _integrityGuard.VerifyAsync(shown, cancellationToken);
        if (verified.IsFailure) return OperationResult.Fail<SessionView>(verified.Failure);
        var context = CommandContext.Create(_timeProvider, _idGenerator, operatorName);
        var imported = await _manualResults.ImportAsync(aggregate.Session.Workspace, context.NewAttemptId,
            StepKind.BackgroundRemoval, shown.Facts, save.PreviewPath, preflight.Value.SelectedSha256, cancellationToken);
        if (imported.IsFailure) return OperationResult.Fail<SessionView>(imported.Failure);

        ManualResult result = imported.Value;
        Revision next = Revision.Create(RevisionId.From(_idGenerator.NewId()), id, input.Id,
            OperationKind.ManualResultImport, result.File, result.Facts, _timeProvider.GetUtcNow());
        var finished = _engine.Apply(snapshot, new WorkflowCommand.System.ApplyPhotoshopBackgroundEdit(
            target, context.NewAttemptId, next.Id, next.Sha256), context.At(next.CreatedAtUtc));
        if (finished.IsRejected)
        {
            _workspace.QuarantineWorkingFile(result.File, "Photoshop edit did not pass the review transition.");
            return OperationResult.Fail<SessionView>(MapRejection(finished.Rejection!));
        }

        var previous = target.Step.State == StepState.ReviewRequired
            ? aggregate.Attempts.SingleOrDefault(a => a.OutputRevisionId == shown.Id)
            : aggregate.Attempts.Where(a => a.Step == target.Step.Step)
                .OrderByDescending(a => a.RetrySequence).ThenByDescending(a => a.StartedAtUtc).FirstOrDefault();
        ProcessingAttempt attempt = ProcessingAttempt.Start(context.NewAttemptId, id, StepKind.BackgroundRemoval,
            input.Id, OperationKind.ManualResultImport, "photoshop-background-edit-v1", context.NowUtc,
            previous?.Id, target.Step.AttemptCount)
            .WithManualResultSourcePath(save.PsdPath)
            .WithBackgroundRemovalAuthority(BackgroundRemovalAuthority.For(
                BackgroundRemovalDecision.ManualResultForReviewedContent, input.Id, input.Sha256)) with
        {
            ImageProcessingDetails = new ImageProcessingRunDetails(ImageProcessingMethod.ManualResult, input.Sha256, null),
        };
        attempt = attempt.Succeed(next.Id, next.CreatedAtUtc,
            $"Photoshop edited image {shown.Id} ({shown.Sha256}); saved PSD SHA256 {save.PsdHash}; operator {context.Operator}");

        // Publish the result and its history together. A refused save leaves the old review current.
        var updated = MergeSession(aggregate.Session, finished.State, finished.Effects, next.CreatedAtUtc);
        var mutation = BuildMetadataMutation(aggregate, updated, finished.State, finished.Effects, context,
            newRevisions: [next], upsertAttempts: [attempt]) with
        {
            ImageProcessingGuard = new(aggregate.Session, aggregate.Steps, shown),
            LockChange = null,
        };
        if (cancellationToken.IsCancellationRequested)
        {
            _workspace.QuarantineWorkingFile(result.File, "Photoshop edit was cancelled before publication.");
            cancellationToken.ThrowIfCancellationRequested();
        }
        var commit = await _repository.CommitAsync(mutation, CancellationToken.None);
        if (commit.IsFailure)
        {
            var observed = await _repository.LoadAsync(id, CancellationToken.None);
            if (observed.IsSuccess)
            {
                if (observed.Value?.Revisions.Any(r => r.Id == next.Id) == true)
                    return await LoadAsync(id, CancellationToken.None);
                _workspace.QuarantineWorkingFile(result.File, "Photoshop edit was not committed.");
            }
            return OperationResult.Fail<SessionView>(commit.Failure);
        }
        return ViewOf(finished.State, [.. aggregate.Revisions, next], aggregate.Outputs,
            [.. aggregate.Attempts, attempt], aggregate.CorrectionRequests);
    }

    private static (PhotoshopBackgroundEditTarget Target, Revision Image)? PhotoshopBackgroundCandidate(
        WorkflowSnapshot snapshot, IReadOnlyList<Revision> revisions)
    {
        if (PhotoshopBackgroundEditTarget.From(snapshot) is not { } target)
            return null;

        Revision? image = revisions.SingleOrDefault(r => r.Id == target.SourceRevision &&
            r.Sha256 == target.SourceHash && r.IsValid && r.RetentionReleasedAtUtc is null);
        return image?.Facts is { HasPixelDimensions: true, Format: ImageFormat.Png or ImageFormat.Jpeg or ImageFormat.Tiff }
            ? (target, image) : null;
    }

    private static OperationFailure PhotoshopTargetChanged() => OperationFailure.Create(FailureCode.PreconditionNotMet,
        "The Photoshop save no longer corresponds to the current background-removal position.",
        messageKey: "Session_PhotoshopEditReviewChanged");
}
