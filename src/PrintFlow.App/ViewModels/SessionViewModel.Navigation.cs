using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrintFlow.App.Resources;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Services;

namespace PrintFlow.App.ViewModels;

public sealed partial class SessionViewModel
{
    private sealed record ReturnIntent(string Context, ReturnTargetRow Target,
        SizePreset? Preset = null, TargetEdge? Edge = null, decimal? Millimetres = null, bool EditSize = false);
    private sealed record LeaveIntent(string Context, Func<CancellationToken, Task> Continue);
    private ReturnIntent? _returnIntent;
    private LeaveIntent? _leaveIntent;
    private (string Signature, Action Restore)? _cleanDraft;

    [ObservableProperty] private SessionStepRow? _selectedHistoryStep;
    [ObservableProperty] private bool _isViewingHistory;
    [ObservableProperty] private bool _isConfirmingDraftLeave;
    public SessionHistoryView? SelectedHistory { get; private set; }
    public IEnumerable<SessionStepRow> HistorySteps => Steps.Where(row => _session?.History.Any(history =>
        history.Step == row.Kind && (history.Revisions.Count > 0 || history.State == StepState.Skipped)) == true);
    public ArtefactPreviewPane? HistoryPreview { get; private set; }
    public Task HistoryPreviewLoaded { get; private set; } = Task.CompletedTask;
    private int _historyGeneration;
    public IReadOnlyList<string> HistoryFacts => SelectedHistory?.Revisions.Select(r =>
        $"{r.FileName} · {r.Facts.PixelWidth} × {r.Facts.PixelHeight} · {r.Facts.Format} · {r.Facts.Sha256}\n" +
        $"{DisplayNames.ReviewState(r.ReviewState)} · {(r.IsValid ? Strings.Session_HistoryValid : Strings.Session_HistoryInvalid)}" +
        (r.BytesReleased ? " · " + Strings.Session_HistoryBytesReleased : string.Empty)).ToArray() ?? [];
    public string HistoryHeading => Strings.Session_HistoryHeading;
    public string HistoryNotice => Strings.Session_HistoryNotice;
    public string HistoryLabel => Strings.Session_HistoryOpen;
    public string HistoryCloseLabel => Strings.Session_HistoryClose;
    public string HistoryStepName => SelectedHistory is { } history ? DisplayNames.Step(history.Step) : string.Empty;
    public string DraftLeaveHeading => Strings.Session_DraftLeaveHeading;
    public string DraftLeaveNotice => Strings.Session_DraftLeaveNotice;
    public string KeepDraftLabel => Strings.Session_KeepDraft;
    public string DiscardDraftLabel => Strings.Session_DiscardDraft;
    public bool ShowsLiveWorkspace => !IsViewingHistory;
    public bool IsLiveInteractionAllowed => !IsViewingHistory && !IsConfirmingDraftLeave;
    public bool CanInteractWithLivePage => IsLiveInteractionAllowed && !IsConfirmingReturn;
    public string ReturnImpact => _returnIntent is { } intent && _session is { } session
        ? string.Format(Strings.Session_ReturnImpact, DisplayNames.Step(intent.Target.Step),
            string.Join(" → ", session.Steps.Where(s => s.Ordinal >= intent.Target.Ordinal - 1).Select(s => DisplayNames.Step(s.Step))))
        : string.Empty;

    // A UI context, not an approval identity. Services still validate their exact own identities.
    private static string NavigationContext(SessionView? view) => view is null ? string.Empty :
        $"{view.Id}|{view.State}|{view.CurrentStep?.Step}|{view.CurrentStep?.State}|{view.CurrentStep?.CurrentRevisionId}|{view.CurrentStep?.AttemptCount}|{view.CurrentArtefact?.RevisionId}|{view.Dimensions}|{view.WhiteUnderbaseBranch}|{view.Correction?.RequestId}";

    [RelayCommand]
    private void ViewHistory(SessionStepRow? row)
    {
        row ??= SelectedHistoryStep;
        if (row is null || _session is null || IsBusy || IsConfirmingReturn || _finalSaveOperation is not null ||
            !_session.History.Any(h => h.Step == row.Kind && (h.Revisions.Count > 0 || h.State == StepState.Skipped))) return;
        SessionHistoryView history = _session.History.First(h => h.Step == row.Kind);
        RequestLeave(() =>
        {
            SelectedHistory = history;
            IsViewingHistory = true;
            HistoryPreview = null;
            HistoryPreviewLoaded = LoadHistoryPreviewAsync(history, ++_historyGeneration);
            NotifyNavigation();
        });
    }

    [RelayCommand]
    private void CloseHistory()
    {
        IsViewingHistory = false;
        SelectedHistory = null;
        _historyGeneration++;
        HistoryPreview = null;
        NotifyNavigation();
    }

    private async Task LoadHistoryPreviewAsync(SessionHistoryView history, int generation)
    {
        if (_session is not { } session || history.Revisions.LastOrDefault() is not { BytesReleased: false } revision) return;
        var result = await _previews.GetPreviewAsync(session.Id, revision.Id, CancellationToken.None).ConfigureAwait(true);
        if (generation != _historyGeneration || !IsViewingHistory || _session?.Id != session.Id) return;
        HistoryPreview = result.IsSuccess ? ArtefactPreviewPane.From(HistoryHeading, revision.FileName, result.Value)
            : ArtefactPreviewPane.Unreadable(HistoryHeading, revision.FileName, result.Failure);
        OnPropertyChanged(nameof(HistoryPreview));
    }

    private void CaptureReturn(ReturnTargetRow target, SizePreset? preset = null, TargetEdge? edge = null,
        decimal? millimetres = null, bool editSize = false)
    {
        if (!IsLiveInteractionAllowed || IsBusy || _finalSaveOperation is not null ||
            _session?.ReturnTargets.Any(offered => offered.Step == target.Step) != true) return;
        RequestLeave(() =>
        {
            SelectedReturnTarget = target;
            _returnIntent = new ReturnIntent(NavigationContext(_session), target, preset, edge, millimetres, editSize);
            IsConfirmingReturn = true;
            OnPropertyChanged(nameof(ReturnImpact));
            OnPropertyChanged(nameof(GuidedInteractionIdentity));
        });
    }

    private void RequestLeave(Action continuation)
    {
        if (IsBusy || _finalSaveOperation is not null || IsConfirmingDraftLeave) return;
        if (HasPageDraft())
        {
            _leaveIntent = new LeaveIntent(NavigationContext(_session), _ => { continuation(); return Task.CompletedTask; });
            IsConfirmingDraftLeave = true;
            NotifyNavigation();
            return;
        }
        continuation();
    }

    private bool HasPageDraft() => _cleanDraft is { } clean && CapturePageDraft().Signature != clean.Signature ||
        IsCropping || IsAdjustingTrim || IsAskingColleague ||
        SelectedSave is { } save && (save.NameEdited || save.FolderEdited) &&
            (save.Captured is null || save.Captured.Value.Generation != save.Generation);

    [RelayCommand]
    private void KeepDraft()
    {
        _leaveIntent = null;
        IsConfirmingDraftLeave = false;
        NotifyNavigation();
    }

    [RelayCommand]
    private async Task DiscardDraftAndLeaveAsync(CancellationToken cancellationToken)
    {
        if (_leaveIntent is not { } intent || intent.Context != NavigationContext(_session) || IsBusy || _finalSaveOperation is not null) return;
        KeepDraft();
        _cleanDraft?.Restore();
        ClearCropState();
        if (IsAskingColleague) CancelAskColleague();
        if (SelectedSave is { } save && save.NavigationDraft is { } draft)
        {
            save.FileName = draft.Name;
            save.Folder = draft.Folder;
            save.NameEdited = false;
            save.FolderEdited = false;
            save.NavigationDraft = null;
            save.Generation++;
            NotifyFinalSaveChanged();
        }
        await intent.Continue(cancellationToken).ConfigureAwait(true);
    }

    private (string Signature, Action Restore) CapturePageDraft()
    {
        var width = WidthMmText; var height = HeightMmText; var custom = CustomMillimetresText;
        var edge = SelectedTargetEdgeChoice; var preset = _customPresetContext; var pending = _pendingPreset;
        var choosing = IsChoosingCustomSize; var white = SelectedWhiteUnderbaseChoice; var whiteEditing = _isChoosingGuidedWhiteInk;
        var mode = SelectedTrimMode; var uniform = UniformMarginText; var top = TopMarginText;
        var right = RightMarginText; var bottom = BottomMarginText; var left = LeftMarginText;
        var reason = SelectedRejectionReason; var notes = RejectionNotes; var correction = CorrectionNote;
        return (string.Join("\u001f", width, height, custom, edge?.Edge.ToString(), preset?.Preset.ToString(),
            pending.ToString(), choosing.ToString(), white?.Branch.ToString(), whiteEditing.ToString(), mode?.Mode.ToString(),
            uniform, top, right, bottom, left, reason?.Reason.ToString(), notes, correction), () =>
        {
            WidthMmText = width; HeightMmText = height; CustomMillimetresText = custom;
            SelectedTargetEdgeChoice = edge; _customPresetContext = preset; _pendingPreset = pending;
            IsChoosingCustomSize = choosing; SelectedWhiteUnderbaseChoice = white; _isChoosingGuidedWhiteInk = whiteEditing;
            SelectedTrimMode = mode!; UniformMarginText = uniform; TopMarginText = top;
            RightMarginText = right; BottomMarginText = bottom; LeftMarginText = left;
            SelectedRejectionReason = reason!; RejectionNotes = notes; CorrectionNote = correction;
            NotifyCustomSizeStateChanged(); NotifyGuidedPresentation();
        });
    }

    private void NotifyNavigation()
    {
        OnPropertyChanged(nameof(ShowsLiveWorkspace));
        OnPropertyChanged(nameof(IsLiveInteractionAllowed));
        OnPropertyChanged(nameof(CanInteractWithLivePage));
        OnPropertyChanged(nameof(SelectedHistory));
        OnPropertyChanged(nameof(HistoryFacts));
        OnPropertyChanged(nameof(HistoryStepName));
        OnPropertyChanged(nameof(HistorySteps));
        OnPropertyChanged(nameof(HistoryPreview));
        OnPropertyChanged(nameof(CanApprove)); OnPropertyChanged(nameof(CanReject));
        OnPropertyChanged(nameof(CanAskColleague)); OnPropertyChanged(nameof(CanPrepareCorrection));
        OnPropertyChanged(nameof(CanImportCorrectedImage)); OnPropertyChanged(nameof(CanRepairCorrectionFiles));
        NotifyOperatorStatusChanged();
        NotifyFinalSaveChanged();
    }
}
