using PrintFlow.Domain.Files;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Services;

namespace PrintFlow.App.ViewModels;

public sealed partial class SessionViewModel
{
    /// <summary>The reviewed derivation pair, separate from the current deliverable preview.</summary>
    public IReadOnlyList<ArtefactPreviewPane> SaveReviewPanes { get; private set; } = [];

    private async Task<IReadOnlyList<ArtefactPreviewPane>> BuildSaveReviewPanesAsync(
        SessionView session, IReadOnlyList<ArtefactPreviewPane> currentPanes, CancellationToken cancellationToken)
    {
        if (session.WorkflowType != WorkflowType.PrepareAsset || session.CurrentArtefact?.Facts.Format != ImageFormat.Png ||
            session.UpstreamArtefact is not { } reviewed ||
            !session.History.Any(step => step.Step == StepKind.ApprovedPngExport &&
                step.Revisions.Any(revision => revision.Id == session.CurrentArtefact.RevisionId)))
            return currentPanes;

        // Promotion preserves the approved trim bytes. Follow the trim's actual source edge;
        // comparing the trim with its export would omit the transformation the operator reviewed.
        if (!session.History.Any(step => step.Step == StepKind.Trim && step.Revisions.Any(revision =>
                revision.Id == reviewed.RevisionId && revision.ReviewState == ReviewState.Approved && revision.IsValid)) ||
            session.History.SelectMany(step => step.Revisions).FirstOrDefault(revision => revision.Id == reviewed.SourceRevisionId) is not { } source)
            return [];

        var before = new ArtefactView(source.Id, source.FileName, source.Facts, false, source.SourceId);
        var beforePane = await BuildPaneAsync(session.Id, BeforeLabel, before, cancellationToken).ConfigureAwait(true);
        var afterPane = await BuildPaneAsync(session.Id, AfterLabel, reviewed, cancellationToken).ConfigureAwait(true);
        return [beforePane, afterPane];
    }
}
