using System.Collections.ObjectModel;
using PrintFlow.App.Controls;
using PrintFlow.App.Resources;
using PrintFlow.Domain.Sessions;

namespace PrintFlow.App.ViewModels;

public sealed partial class SessionViewModel
{
    public ObservableCollection<GuidedStepItem> GuidedSteps { get; } = [];
    public string GuidedCurrentTitle => CurrentStep.Length > 0 ? CurrentStep : StatusHeading;
    public string GuidedCurrentStatus => StatusHeading;
    public string GuidedTechnicalDetailsLabel => Strings.Session_GuidedTechnicalDetails;
    public bool ShowsGuidedCustomSizeAction => CanChooseFlexibleSize && IsChoosingCustomSize;

    private void RefreshGuidedPresentation()
    {
        GuidedSteps.Clear();
        if (_session is not null)
        {
            foreach (var step in _session.Steps)
            {
                GuidedStepState state = step.State switch
                {
                    StepState.Skipped => GuidedStepState.Skipped,
                    StepState.Approved => GuidedStepState.Completed,
                    StepState.Failed or StepState.Interrupted or StepState.RetryRequired => GuidedStepState.Attention,
                    _ when step.Step == _session.CurrentStep?.Step => GuidedStepState.Current,
                    _ => GuidedStepState.Pending,
                };
                GuidedSteps.Add(new GuidedStepItem(step.Step.ToString(), DisplayNames.Step(step.Step),
                    string.Empty, DisplayNames.StepState(step.State), state));
            }
        }
        OnPropertyChanged(nameof(GuidedCurrentTitle));
        OnPropertyChanged(nameof(GuidedCurrentStatus));
        OnPropertyChanged(nameof(ShowsGuidedCustomSizeAction));
    }
}
