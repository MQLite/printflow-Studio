using CommunityToolkit.Mvvm.Input;
using PrintFlow.App.Resources;
using PrintFlow.Workflow.Services;

namespace PrintFlow.App.ViewModels;

public sealed partial class SessionViewModel
{
    private bool _isSuspendedForUtility;
    public bool CanOpenUtilities => _session is not null && CanInteractWithLivePage && !IsBusy &&
        !IsAutomaticallyProcessing && _finalSaveOperation is null;
    public string OpenSettingsLabel => Strings.Settings_Heading;
    public string OpenEnvironmentLabel => Strings.Environment_Open;

    [RelayCommand(CanExecute = nameof(CanOpenUtilities))]
    private async Task OpenSettingsAsync(CancellationToken cancellationToken)
    {
        if (CanOpenUtilities) await _navigation.GoToSettingsAsync(cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanOpenUtilities))]
    private async Task OpenEnvironmentAsync(CancellationToken cancellationToken)
    {
        if (CanOpenUtilities) await _navigation.GoToEnvironmentReadinessAsync(cancellationToken).ConfigureAwait(true);
    }

    internal void SuspendForUtility()
    {
        _isSuspendedForUtility = true;
        _openGeneration++;
        KeepDraft();
        ClearReturnState();
        IsConfirmingAutomaticSelection = false;
        IsConfirmingTakeOver = false;
        NotifyNavigation();
    }

    internal void ResumeFromUtility(SessionView current)
    {
        Open(current);
        _isSuspendedForUtility = false;
        NotifyNavigation();
    }

    private void NotifyUtilities()
    {
        OnPropertyChanged(nameof(CanOpenUtilities));
        OpenSettingsCommand.NotifyCanExecuteChanged();
        OpenEnvironmentCommand.NotifyCanExecuteChanged();
    }
}
