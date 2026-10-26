using System.Globalization;
using PrintFlow.App.Resources;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Delivery;

namespace PrintFlow.App.ViewModels;

/// <summary>Three independent read-only facts; none of these labels grants delivery or completion.</summary>
public sealed partial class SessionViewModel
{
    public bool ShowsGuidedCompletion => GuidedPage == GuidedSessionPage.Completed || HasFinalSave && GuidedPage == GuidedSessionPage.Save;
    public string GuidedCompletionHeading => Strings.Resolve(_session?.State switch
    {
        SessionState.Completed => "Session_CompletionFinishedHeading",
        SessionState.Abandoned => "Session_CompletionAbandoned",
        _ => "Session_CompletionSaveHeading",
    });
    public string GuidedCompletionNotice => Strings.Resolve("Session_CompletionNotice");
    // A durable delivery record is not evidence that the external file still exists.
    public string GuidedSavedCount => SelectedSave is not { History: { } history } target
        ? Strings.Resolve("Session_CompletionRecordsUnknown")
        : string.Format(CultureInfo.CurrentCulture, Strings.Resolve("Session_CompletionSavedCount"),
            history.Count(record => record.Status == "Delivered")) +
            (HistoryIsDated(target) ? " " + Strings.Resolve("Session_CompletionRecordsUnknown") : string.Empty);
    public string GuidedApprovalState => Strings.Resolve(SelectedSave switch
    {
        { PngPreparationPending: true } => "Session_CompletionPngPreparing",
        { PendingReview: not null } => "Session_CompletionNotApproved",
        { Artifact: not null, Refusal: ArtifactRefusal.None } => "Session_CompletionApproved",
        null when _session?.CurrentArtefact is { } current &&
            (_session.History.SelectMany(step => step.Revisions)
                .Any(revision => revision.Id == current.RevisionId && revision.IsValid && revision.ReviewState == ReviewState.Approved) ||
             _session.Outputs.Any(output => output.Id.Value == current.RevisionId.Value && output.IsValid && output.ReviewState == ReviewState.Approved))
            => "Session_CompletionApproved",
        _ => "Session_CompletionUnavailable",
    });
    public string GuidedDeliveryState => DeliveryStateLabel(SaveDisplay);
    public string GuidedCompletionState => Strings.Resolve(_session?.State switch
    {
        SessionState.Completed => "Session_CompletionFinished",
        SessionState.Abandoned => "Session_CompletionAbandoned",
        _ => "Session_CompletionNotFinished",
    });

    // Reuses the same interpretation as the full save panel, independently for each exact target.
    private string TargetDeliveryStatus(FinalSaveTarget target) => DeliveryStateLabel(DescribeFinalSave(target));
    private static string DeliveryStateLabel(FinalSaveDisplay display) => Strings.Resolve(display.Kind switch
    {
        FinalSaveKind.Pending => "Session_CompletionNotApproved",
        FinalSaveKind.PngPreparation => "Session_CompletionPngPreparing",
        FinalSaveKind.ApprovedNotSaved => "Session_CompletionNotSaved",
        FinalSaveKind.Working => "Session_CompletionWorking",
        FinalSaveKind.Saved => "Session_CompletionSaved",
        FinalSaveKind.SavedPreviously => "Session_CompletionSavedPreviously",
        FinalSaveKind.Failed => "Session_CompletionSaveFailed",
        FinalSaveKind.Collision => "Session_CompletionCollision",
        FinalSaveKind.Uncertain => "Session_CompletionUncertain",
        FinalSaveKind.Missing => "Session_CompletionMissing",
        FinalSaveKind.Changed => "Session_CompletionChanged",
        FinalSaveKind.Unavailable or FinalSaveKind.HistoryUnknown => "Session_CompletionUnknown",
        _ => "Session_CompletionUnavailable",
    });

    private void NotifyGuidedCompletion()
    {
        OnPropertyChanged(nameof(ShowsGuidedCompletion));
        OnPropertyChanged(nameof(GuidedCompletionHeading));
        OnPropertyChanged(nameof(GuidedCompletionNotice));
        OnPropertyChanged(nameof(GuidedSavedCount));
        OnPropertyChanged(nameof(GuidedApprovalState));
        OnPropertyChanged(nameof(GuidedDeliveryState));
        OnPropertyChanged(nameof(GuidedCompletionState));
    }
}
