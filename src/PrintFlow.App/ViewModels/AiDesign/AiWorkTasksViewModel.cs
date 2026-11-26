using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrintFlow.App.Navigation;
using PrintFlow.App.Resources;
using PrintFlow.Workflow.AiDesign;
using PrintFlow.Workflow.Services;

namespace PrintFlow.App.ViewModels.AiDesign;

public enum WorkTaskFilter { All, Active, NeedsAttention }

public sealed class AiWorkTasksViewModel : ObservableObject, IEntryScreenLifetime
{
    private readonly IWorkTaskQuery query;
    private readonly WorkTaskTracker tracker;
    private readonly IArtefactPreviewService previews;
    private bool active, loading;
    private CancellationTokenSource? read, thumbnails;
    private WorkTaskFilter filter;
    private IReadOnlyList<WorkTaskRow> all = [];
    private string? error;
    private Guid? returnDraft;

    public AiWorkTasksViewModel(IWorkTaskQuery query, WorkTaskTracker tracker, IArtefactPreviewService previews,
        IAiDesignNavigation aiNavigation, INavigationService navigation, IWorkConversationLauncher launcher)
    {
        this.query = query; this.tracker = tracker; this.previews = previews;
        ReturnHomeCommand = new AsyncRelayCommand(navigation.GoHomeAsync);
        ReturnWorkbenchCommand = new AsyncRelayCommand(ct => aiNavigation.GoToPromptWorkbenchAsync(returnDraft, ct));
        ReloadCommand = new AsyncRelayCommand(ReloadAsync);
        SelectFilterCommand = new RelayCommand<WorkTaskFilter>(value => Filter = value);
        ViewWorkCommand = new RelayCommand<WorkTaskRow>(row =>
        {
            if (active && row?.CanViewWork == true && !row.OpenWork(launcher)) Error = L("OpenFailed");
        });
        ContinueCommand = new AsyncRelayCommand<WorkTaskRow>(async (row, ct) =>
        {
            if (!active || row?.CanContinue != true) return;
            var result = await aiNavigation.OpenWorkResultAsync(row.Snapshot.RequestId, ct);
            if (active && result.IsFailure) Error = L("OpenResultFailed");
        });
    }

    public IAsyncRelayCommand ReturnHomeCommand { get; }
    public IAsyncRelayCommand ReturnWorkbenchCommand { get; }
    public IAsyncRelayCommand ReloadCommand { get; }
    public IRelayCommand<WorkTaskFilter> SelectFilterCommand { get; }
    public IRelayCommand<WorkTaskRow> ViewWorkCommand { get; }
    public IAsyncRelayCommand<WorkTaskRow> ContinueCommand { get; }
    public IReadOnlyList<WorkTaskRow> Rows { get; private set; } = [];
    public string? Error { get => error; private set { if (SetProperty(ref error, value)) OnPropertyChanged(nameof(EmptyMessage)); } }
    public string? EmptyMessage => loading && Rows.Count == 0 ? L("Loading")
        : Rows.Count == 0 && Error is null ? L(all.Count == 0 ? "Empty" : "NoMatches") : null;
    public WorkTaskFilter Filter { get => filter; set { if (SetProperty(ref filter, value)) ApplyFilter(); } }
    public string Title => L("HistoryTitle");
    public string HomeLabel => Strings.Resolve("AiPrompt_ReturnHome");
    public string WorkbenchLabel => L("BackToWorkbench");
    public string ReloadLabel => L("Reload");
    public string AllLabel => L("All");
    public string ActiveLabel => L("Active");
    public string AttentionLabel => L("Attention");
    public string TaskColumn => L("ColumnTask");
    public string StatusColumn => L("ColumnStatus");
    public string UpdatedColumn => L("ColumnUpdated");
    public string ActionsColumn => L("ColumnActions");
    public string ViewWorkLabel => L("ViewWork");
    public string ContinueLabel => L("Continue");
    internal Task Refresh { get; private set; } = Task.CompletedTask;
    internal Task ThumbnailsLoaded { get; private set; } = Task.CompletedTask;

    internal Task OpenAsync(Guid? draft)
    {
        active = true;
        returnDraft = draft;
        tracker.TaskChanged += OnTaskChanged;
        return ReloadAsync();
    }

    public void Deactivate()
    {
        active = false;
        tracker.TaskChanged -= OnTaskChanged;
        read?.Cancel();
        CancelThumbnails();
    }

    internal void ShowError(string message) { if (active) Error = message; }

    private void OnTaskChanged(Guid requestId) => _ = ReloadAsync();
    private Task ReloadAsync() => Refresh = ReadAsync();

    private async Task ReadAsync()
    {
        if (!active) return;
        read?.Cancel();
        using var current = new CancellationTokenSource();
        read = current;
        loading = true;
        OnPropertyChanged(nameof(EmptyMessage));
        try
        {
            var result = await query.ListAsync(current.Token);
            if (!active || current.IsCancellationRequested) return;
            if (result.IsFailure) { Error = L("LoadFailed"); return; }
            Error = null;
            var old = all.ToDictionary(x => x.Snapshot.RequestId);
            all = result.Value.OrderByDescending(item => item.UpdatedAtUtc).ThenBy(item => item.RequestId)
                .Select(item => old.TryGetValue(item.RequestId, out var row) && row.Snapshot == item ? row : new WorkTaskRow(item))
                .ToArray();
            ApplyFilter();
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(read, current))
            {
                read = null;
                loading = false;
                OnPropertyChanged(nameof(EmptyMessage));
            }
        }
    }

    private void ApplyFilter()
    {
        var rows = all.Where(row => Filter switch
        {
            WorkTaskFilter.Active => row.Snapshot.IsActive,
            WorkTaskFilter.NeedsAttention => row.Snapshot.Stage is WorkTaskStage.NeedsAttention or WorkTaskStage.PortraitReview,
            _ => true
        }).ToArray();
        if (!Rows.SequenceEqual(rows))
        {
            Rows = rows;
            OnPropertyChanged(nameof(Rows));
            CancelThumbnails();
            if (active && rows.Any(row => row.CanContinue && !row.HasThumbnail))
            {
                thumbnails = new();
                ThumbnailsLoaded = LoadThumbnailsAsync(rows, thumbnails.Token);
            }
        }
        OnPropertyChanged(nameof(EmptyMessage));
    }

    private void CancelThumbnails()
    {
        thumbnails?.Cancel();
        thumbnails?.Dispose();
        thumbnails = null;
    }

    private async Task LoadThumbnailsAsync(IReadOnlyList<WorkTaskRow> rows, CancellationToken ct)
    {
        foreach (var row in rows)
        {
            if (ct.IsCancellationRequested) break;
            if (!row.HasThumbnail) await row.LoadThumbnailAsync(previews, ct);
        }
    }

    private static string L(string key) => Strings.Resolve("WorkTask_" + key);
}
