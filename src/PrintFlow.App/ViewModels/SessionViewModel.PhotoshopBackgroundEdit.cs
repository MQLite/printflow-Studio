using CommunityToolkit.Mvvm.Input;
using PrintFlow.App.Resources;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.App.ViewModels;

public sealed partial class SessionViewModel
{
    private readonly IPhotoshopBackgroundEditor? _photoshopBackgroundEditor;
    private PhotoshopEditTarget? _photoshopEdit;
    private CancellationTokenSource? _waitingForPhotoshop;
    private OperationFailure? _photoshopSaveFailure;
    private string? _photoshopPsdFileName;

    private sealed record PhotoshopEditTarget(SessionId Session, PhotoshopBackgroundEditTarget Position);

    private bool HasPhotoshopEdit => _photoshopEdit is not null;
    public bool CanBeginPhotoshopEdit => _photoshopBackgroundEditor is not null &&
        _session?.PhotoshopBackgroundEdit is not null && CanInteractWithLivePage &&
        !HasPhotoshopEdit && !IsWaitingForPhotoshop && !IsBusy && !IsAskingColleague;
    public bool IsChoosingPhotoshopEdit => HasPhotoshopEdit && !IsWaitingForPhotoshop;
    public bool IsWaitingForPhotoshop => _waitingForPhotoshop is not null;

    public string ManualPhotoshopEditLabel => Strings.Resolve("Session_PhotoshopEditManual");
    public string OpenPhotoshopEditLabel => Strings.Resolve("Session_PhotoshopEditOpen");
    public string CancelPhotoshopEditLabel => IsWaitingForPhotoshop
        ? Strings.Resolve("Session_PhotoshopEditStop")
        : Strings.Resolve("Session_PhotoshopEditCancel");

    private string PhotoshopEditInstructions => IsChoosingPhotoshopEdit
        ? Strings.Resolve("Session_PhotoshopEditQuestion")
        : _photoshopSaveFailure is { } failure
            ? DisplayNames.Failure(failure)
            : _photoshopPsdFileName is { } fileName
                ? Format(Strings.Resolve("Session_PhotoshopEditWaiting"), fileName)
                : Strings.Resolve("Session_PhotoshopEditPreparing");

    [RelayCommand]
    private void BeginPhotoshopEdit()
    {
        if (!CanBeginPhotoshopEdit || _session is not { PhotoshopBackgroundEdit: { } position } session) return;
        _photoshopEdit = new(session.Id, position);
        CancelAutomaticSelection();
        Notice = null;
        NotifyPhotoshopEdit();
    }

    [RelayCommand]
    private async Task OpenPhotoshopEditAsync(CancellationToken cancellationToken)
    {
        if (!IsChoosingPhotoshopEdit || !CanInteractWithLivePage || IsBusy ||
            _photoshopEdit is not { } target || _photoshopBackgroundEditor is not { } editor ||
            !MatchesPhotoshopPosition())
            return;

        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _waitingForPhotoshop = waiting;
        _navigation.CurrentChanged += CancelPhotoshopEditOnNavigation;
        NotifyPhotoshopEdit();

        try
        {
            var source = await _sessions.GetPhotoshopBackgroundEditSourceAsync(
                target.Session, target.Position, waiting.Token);
            if (waiting.IsCancellationRequested || !MatchesPhotoshopPosition()) return;
            if (source.IsFailure)
            {
                Notice = DisplayNames.Failure(source.Failure);
                return;
            }

            _photoshopPsdFileName = source.Value.PsdFileName;
            NotifyPhotoshopEdit();
            await foreach (var save in editor.WatchSavesAsync(source.Value, waiting.Token))
            {
                if (waiting.IsCancellationRequested || !MatchesPhotoshopPosition() || !CanInteractWithLivePage) return;
                if (save.IsFailure)
                {
                    _photoshopSaveFailure = save.Failure;
                    NotifyPhotoshopEdit();
                    continue;
                }

                IsBusy = true;
                try
                {
                    var imported = await _sessions.ImportPhotoshopBackgroundEditAsync(
                        target.Session, target.Position, save.Value, Environment.UserName, waiting.Token);
                    if (waiting.IsCancellationRequested || !MatchesPhotoshopPosition()) return;
                    if (imported.IsSuccess)
                    {
                        Show(imported.Value);
                        await PreviewsLoaded;
                        return;
                    }

                    _photoshopSaveFailure = imported.Failure;
                    if (imported.Failure.Code == FailureCode.PreconditionNotMet) break;
                }
                finally
                {
                    IsBusy = false;
                    NotifyPhotoshopEdit();
                }
            }

            if (_photoshopSaveFailure is { } failure) Notice = DisplayNames.Failure(failure);
        }
        catch (OperationCanceledException) when (waiting.IsCancellationRequested)
        {
        }
        finally
        {
            _navigation.CurrentChanged -= CancelPhotoshopEditOnNavigation;
            _waitingForPhotoshop = null;
            CancelPhotoshopEdit();
            NotifyPhotoshopEdit();
        }
    }

    [RelayCommand]
    private void StopPhotoshopEdit()
    {
        if (!IsBusy) CancelPhotoshopEdit();
    }

    private void CancelPhotoshopEditOnNavigation(object? sender, EventArgs e) => CancelPhotoshopEdit();

    private void CancelPhotoshopEdit()
    {
        if (!HasPhotoshopEdit) return;
        _photoshopEdit = null;
        _photoshopSaveFailure = null;
        _photoshopPsdFileName = null;
        _waitingForPhotoshop?.Cancel();
        NotifyPhotoshopEdit();
    }

    private bool MatchesPhotoshopPosition() => _photoshopEdit is { } target &&
        _session is { } session && session.Id == target.Session && session.PhotoshopBackgroundEdit == target.Position;

    private void NotifyPhotoshopEdit()
    {
        OnPropertyChanged(nameof(CanBeginPhotoshopEdit));
        OnPropertyChanged(nameof(IsChoosingPhotoshopEdit));
        OnPropertyChanged(nameof(IsWaitingForPhotoshop));
        OnPropertyChanged(nameof(CancelPhotoshopEditLabel));
        OnPropertyChanged(nameof(CanApprove));
        OnPropertyChanged(nameof(CanReject));
        OnPropertyChanged(nameof(CanAskColleague));
        OnPropertyChanged(nameof(CanReturnToStep));
        OnPropertyChanged(nameof(CanBeginReturn));
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(CanSkip));
        OnPropertyChanged(nameof(CanHandOff));
        OnPropertyChanged(nameof(CanRunStep));
        OnPropertyChanged(nameof(ShowsGuidedRunStep));
        OnPropertyChanged(nameof(CanAuthoriseAutomaticSelection));
        OnPropertyChanged(nameof(CanBeginAutomaticSelection));
        OnPropertyChanged(nameof(ShowsGuidedBackgroundDecision));
        OnPropertyChanged(nameof(ShowsGuidedAutomaticBegin));
        OnPropertyChanged(nameof(ShowsGuidedAutomaticConfirmation));
        OnPropertyChanged(nameof(CanChangeImageProcessingMethod));
        OnPropertyChanged(nameof(ShowsCombinedImageProcessingChoice));
        NotifyOperatorStatusChanged();
    }
}
