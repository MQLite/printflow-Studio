using PrintFlow.App.Resources;
using PrintFlow.Domain.Sessions;

namespace PrintFlow.App.ViewModels;

public sealed record BackgroundRemovalModeChoice(BackgroundRemovalMode Mode, string Label);

public sealed partial class SessionViewModel
{
    private BackgroundRemovalMode _selectedBackgroundRemovalMode;

    public BackgroundRemovalMode SelectedBackgroundRemovalMode
    {
        get => _selectedBackgroundRemovalMode;
        set
        {
            if (!Enum.IsDefined(value) || !SetProperty(ref _selectedBackgroundRemovalMode, value)) return;
            // Confirmation is about a recogniser as well as the displayed image.
            IsConfirmingAutomaticSelection = false;
            NotifyBackgroundRemovalMode();
        }
    }

    public IReadOnlyList<BackgroundRemovalModeChoice> BackgroundRemovalModes =>
        Enum.GetValues<BackgroundRemovalMode>().Select(mode => new BackgroundRemovalModeChoice(mode, BackgroundRemovalModeLabel(mode))).ToArray();

    public string BackgroundRemovalModeHeading => Strings.Resolve("Session_BackgroundRemovalModeHeading");
    public string SelectedBackgroundRemovalModeLabel => BackgroundRemovalModeLabel(SelectedBackgroundRemovalMode);
    public bool HasRecordedBackgroundRemovalAuthorisation =>
        _session?.BackgroundRemovalDecision == BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent;

    // Engine readiness still applies. This extra presentation gate prevents a provisional
    // picker value from executing the previous persisted recogniser behind the operator's back.
    private bool HasUnconfirmedBackgroundRemovalMode =>
        _session?.CurrentStep?.Step == StepKind.BackgroundRemoval &&
        SelectedBackgroundRemovalMode != _session.BackgroundRemovalMode;

    private static string BackgroundRemovalModeLabel(BackgroundRemovalMode mode) =>
        Strings.Resolve($"Session_BackgroundRemovalMode_{mode}");

    private void ShowBackgroundRemovalMode()
    {
        _selectedBackgroundRemovalMode = _session?.BackgroundRemovalMode ?? BackgroundRemovalMode.Automatic;
        OnPropertyChanged(nameof(SelectedBackgroundRemovalMode));
        NotifyBackgroundRemovalMode();
    }

    private void NotifyBackgroundRemovalMode()
    {
        OnPropertyChanged(nameof(SelectedBackgroundRemovalModeLabel));
        OnPropertyChanged(nameof(AutomaticSelectionConfirmQuestion));
        OnPropertyChanged(nameof(IsAutomaticSelectionAuthorised));
        OnPropertyChanged(nameof(IsAutomaticSelectionPending));
        OnPropertyChanged(nameof(HasRecordedBackgroundRemovalAuthorisation));
        OnPropertyChanged(nameof(CanRunBackgroundRemoval));
        OnPropertyChanged(nameof(CanRunStep));
        NotifyGuidedPresentation();
        NotifyOperatorStatusChanged();
    }
}
