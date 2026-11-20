using CommunityToolkit.Mvvm.Input;
using PrintFlow.App.Resources;
using PrintFlow.Workflow.AiDesign;
using PrintFlow.Workflow.Services;

namespace PrintFlow.App.ViewModels.AiDesign;

public sealed partial class AiPromptWorkbenchViewModel
{
    private bool workSubscribed;
    private readonly IWorkTaskQuery? workTasks;
    private readonly WorkTaskTracker? workTracker;
    private readonly IArtefactPreviewService? workPreviews;
    private readonly IWorkConversationLauncher? workLauncher;
    private CancellationTokenSource? workQueryCancellation, workThumbnailCancellation;
    private bool isLoadingWorkCard, workCardUnavailable;
    public string? WorkCardNotice => workCardUnavailable ? Strings.Resolve("WorkTask_LoadFailed") : null;
    private WorkTaskRow? workCard;

    public WorkTaskRow? WorkCard
    {
        get => workCard;
        private set
        {
            if (!SetProperty(ref workCard, value)) return;
            OnPropertyChanged(nameof(HasWorkCard));
            NotifyGenerationProgress();
            NotifyWorkActions();
        }
    }

    public IAsyncRelayCommand ShowWorkTasksCommand { get; }
    public string WorkTasksLabel => Strings.Resolve("WorkTask_HistoryTitle");
    public bool HasWorkCard => WorkCard is not null;
    public string ViewWorkLabel => Strings.Resolve("WorkTask_ViewWork");
    public IRelayCommand ViewWorkCommand { get; }
    internal Task WorkCardRefresh { get; private set; } = Task.CompletedTask;
    internal Task WorkCardThumbnail { get; private set; } = Task.CompletedTask;

    private void SubscribeToWorkResults()
    {
        if (!workSubscribed && workTracker is not null)
        { workTracker.TaskChanged += WorkRequestChanged; workSubscribed = true; }
    }

    private void UnsubscribeFromWorkResults()
    {
        if (workSubscribed && workTracker is not null)
        { workTracker.TaskChanged -= WorkRequestChanged; workSubscribed = false; }
    }

    private void WorkRequestChanged(Guid requestId) => QueueWorkCardRefresh();

    private void ResetWorkCard()
    {
        workQueryCancellation?.Cancel();
        workThumbnailCancellation?.Cancel();
        workThumbnailCancellation?.Dispose();
        workThumbnailCancellation = null;
        CloseWorkPreview();
        CloseFollowUpEditor();
        submittedWorkRequest = null;
        generationStarted = submittingGeneration = false;
        generationFeedback = null;
        SetWorkCardUnavailable(false);
        isLoadingWorkCard = active && Draft is not null && workTasks is not null;
        WorkCard = null;
        NotifyGenerationProgress();
        NotifyWorkActions();
    }

    private void NotifyWorkActions()
    {
        OnPropertyChanged(nameof(IsWaitingForWorkResult));
        OnPropertyChanged(nameof(CanStopReceiving));
        AutoGenerateCommand.NotifyCanExecuteChanged();
    }

    private void SetWorkCardUnavailable(bool unavailable)
    {
        if (workCardUnavailable == unavailable) return;
        workCardUnavailable = unavailable;
        OnPropertyChanged(nameof(WorkCardNotice));
    }

    private void QueueWorkCardRefresh()
    {
        if (!active || Draft is not { } current || workTasks is null) return;
        workQueryCancellation?.Cancel();
        var query = new CancellationTokenSource();
        workQueryCancellation = query;
        WorkCardRefresh = RefreshWorkCardAsync(current.Id, workTasks, query);
    }

    private async Task RefreshWorkCardAsync(Guid draftId, IWorkTaskQuery tasks, CancellationTokenSource query)
    {
        try
        {
            var listed = await tasks.ListAsync(query.Token);
            if (query.IsCancellationRequested || !active || Draft?.Id != draftId) return;
            if (listed.IsFailure)
            {
                SetWorkCardUnavailable(true);
                return;
            }
            SetWorkCardUnavailable(false);
            var matching = listed.Value.Where(x => x.DraftId == draftId)
                .OrderByDescending(x => x.UpdatedAtUtc).ToArray();
            var next = submittedWorkRequest is { } submitted
                ? matching.FirstOrDefault(x => x.RequestId == submitted)
                : matching.FirstOrDefault(x => x.BlocksNewSubmission) ?? (generationStarted ? null : matching.FirstOrDefault());
            if (next is { Stage: WorkTaskStage.Cancelled, FollowUpSourceRequestId: { } source })
                next = matching.FirstOrDefault(x => x.RequestId == source && x.Stage == WorkTaskStage.Imported) ?? next;
            if (next == WorkCard?.Snapshot) return;
            var previous = WorkCard;
            workThumbnailCancellation?.Cancel();
            workThumbnailCancellation?.Dispose();
            workThumbnailCancellation = null;
            WorkCard = next is null ? null : new WorkTaskRow(next);
            if (next?.ImportedSessionId is { } session && previous is { HasThumbnail: true } &&
                session == previous.Snapshot.ImportedSessionId)
                WorkCard!.SetThumbnail(previous.Thumbnail);
            else if (WorkCard is { CanContinue: true } card && workPreviews is not null)
            {
                workThumbnailCancellation = new();
                WorkCardThumbnail = card.LoadThumbnailAsync(workPreviews, workThumbnailCancellation.Token);
            }
        }
        catch (OperationCanceledException) when (query.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(workQueryCancellation, query))
            {
                workQueryCancellation = null;
                isLoadingWorkCard = false;
                NotifyWorkActions();
            }
            query.Dispose();
        }
    }
}
