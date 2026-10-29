using PrintFlow.Domain.Files;
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

        // Promotion preserves the approved trim bytes. The workflow projects the actual
        // pre-trim input so the shell never reconstructs revision lineage from history.
        if (session.ApprovedPngReviewInput is not { } before)
            return [];

        var beforePane = await BuildPaneAsync(session.Id, BeforeLabel, before, cancellationToken).ConfigureAwait(true);
        var afterPane = await BuildPaneAsync(session.Id, AfterLabel, reviewed, cancellationToken).ConfigureAwait(true);
        return [beforePane, afterPane];
    }
}
