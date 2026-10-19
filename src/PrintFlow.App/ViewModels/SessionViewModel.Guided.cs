using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using PrintFlow.App.Controls;
using PrintFlow.App.Resources;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Definitions;

namespace PrintFlow.App.ViewModels;

/// <summary>Presentation pages, not workflow nodes or command qualifications.</summary>
public enum GuidedSessionPage
{
    Source, SourcePreparation, Ready, Processing, Review, Dimensions, WhiteInk, Preflight,
    Crop, Correction, Recovery, Failure, Save, Completed,
}

public sealed partial class SessionViewModel
{
    private bool _isChoosingGuidedWhiteInk;
    public ObservableCollection<GuidedStepItem> GuidedSteps { get; } = [];
    public GuidedSessionPage GuidedPage => _session switch
    {
        null => GuidedSessionPage.Source,
        { State: SessionState.Completed or SessionState.Abandoned } => GuidedSessionPage.Completed,
        _ when IsAskingColleague || IsHandedOff && ShowsCorrectionPanel => GuidedSessionPage.Correction,
        _ when IsHandedOff => GuidedSessionPage.Recovery,
        _ when IsAutomaticallyProcessing => GuidedSessionPage.Processing,
        _ when IsCropping || IsAdjustingTrim => GuidedSessionPage.Crop,
        _ when IsReviewRequired => GuidedSessionPage.Review,
        _ when IsStoppedStep => GuidedSessionPage.Failure,
        { CurrentStep: null } => GuidedSessionPage.Save,
        { CurrentStep.Step: StepKind.OriginalConfirmation, OriginalSourceFormat: ImageFormat.Pdf or ImageFormat.Psd }
            => GuidedSessionPage.SourcePreparation,
        { CurrentStep.Step: StepKind.OriginalConfirmation } => GuidedSessionPage.Source,
        { CurrentStep.Step: StepKind.PrintDimensions } => GuidedSessionPage.Dimensions,
        { CurrentStep.Step: StepKind.PhotoshopOutput, WhiteUnderbaseBranch: null } => GuidedSessionPage.WhiteInk,
        { CurrentStep.Step: StepKind.PhotoshopOutput } when _isChoosingGuidedWhiteInk => GuidedSessionPage.WhiteInk,
        { CurrentStep.Step: StepKind.PhotoshopOutput } => GuidedSessionPage.Preflight,
        _ => GuidedSessionPage.Ready,
    };

    public string GuidedCurrentTitle => GuidedPage switch
    {
        GuidedSessionPage.WhiteInk => WhiteUnderbaseHeading,
        GuidedSessionPage.SourcePreparation => RunStepLabel,
        GuidedSessionPage.Preflight => Strings.Session_GuidedPreflight,
        GuidedSessionPage.Save => FinalSaveHeading,
        _ => CurrentStep.Length > 0 ? CurrentStep : StatusHeading,
    };
    public string GuidedCurrentStatus => StatusHeading;
    public string GuidedTechnicalDetailsLabel => Strings.Session_GuidedTechnicalDetails;
    public bool ShowsGuidedDimensions => GuidedPage == GuidedSessionPage.Dimensions && CanChooseFlexibleSize;
    public bool ShowsGuidedCustomSizeAction => ShowsGuidedDimensions && IsChoosingCustomSize;
    public bool ShowsGuidedWhiteInk => GuidedPage == GuidedSessionPage.WhiteInk && CanSelectWhiteUnderbase;
    public bool ShowsGuidedPreparation => HasPreparationPlan && GuidedPage is
        GuidedSessionPage.Dimensions or GuidedSessionPage.WhiteInk or GuidedSessionPage.Preflight;
    public bool ShowsGuidedPreflight => HasPrintDimensionsPreflight && GuidedPage is
        GuidedSessionPage.Dimensions or GuidedSessionPage.WhiteInk or GuidedSessionPage.Preflight;
    public bool ShowsGuidedBackgroundDecision => _session?.CurrentStep?.Step == StepKind.BackgroundRemoval &&
        GuidedPage == GuidedSessionPage.Ready && CanAuthoriseAutomaticSelection;
    public bool ShowsGuidedTrimParameters => _session?.CurrentStep?.Step == StepKind.Trim &&
        GuidedPage == GuidedSessionPage.Ready && CanSetTrimParameters;
    public bool ShowsGuidedRunStep => CanRunStep && GuidedPage != GuidedSessionPage.WhiteInk;
    public bool ShowsGuidedAutomaticBegin => ShowsGuidedBackgroundDecision && IsAutomaticSelectionPending && !IsConfirmingAutomaticSelection;
    public bool ShowsGuidedAutomaticConfirmation => ShowsGuidedBackgroundDecision && IsConfirmingAutomaticSelection;
    public bool ShowsGuidedEnlargement => ShowsGuidedPreparation && NeedsEnlargementAuthority;
    public bool CanChangeGuidedWhiteInk => GuidedPage == GuidedSessionPage.Preflight && CanSelectWhiteUnderbase && !IsBusy;
    public string ChangeGuidedWhiteInkLabel => Strings.Session_GuidedChangeWhiteInk;

    [RelayCommand(CanExecute = nameof(CanChangeGuidedWhiteInk))]
    private void ChangeGuidedWhiteInk()
    {
        if (!CanChangeGuidedWhiteInk) return;
        _isChoosingGuidedWhiteInk = true;
        SelectedWhiteUnderbaseChoice = null;
        NotifyGuidedPresentation();
        NotifyOperatorStatusChanged();
    }

    private void RefreshGuidedPresentation()
    {
        _isChoosingGuidedWhiteInk = false;
        GuidedSteps.Clear();
        if (_session is not null)
        {
            // Catalog ordering and persisted current-step selection are read, never recomputed.
            foreach (var definition in WorkflowCatalog.For(_session.WorkflowType).Steps)
            {
                var step = _session.Steps.FirstOrDefault(s => s.Step == definition.Kind);
                if (step is null) continue;
                GuidedStepState state = step.State switch
                {
                    StepState.Skipped => GuidedStepState.Skipped,
                    StepState.Approved => GuidedStepState.Completed,
                    StepState.Failed or StepState.Interrupted or StepState.RetryRequired => GuidedStepState.Attention,
                    _ when step.Step == _session.CurrentStep?.Step => GuidedStepState.Current,
                    _ => GuidedStepState.Pending,
                };
                GuidedSteps.Add(new GuidedStepItem(step.Step.ToString(), DisplayNames.Step(step.Step),
                    step.Step == _session.CurrentStep?.Step ? Strings.Session_GuidedCurrent
                        : step.IsFinished ? Strings.Session_GuidedHistoryAvailable : Strings.Session_GuidedFuture,
                    step.Step == StepKind.Trim && step.State == StepState.Skipped ? Strings.Session_OriginalExtentRetained
                        : DisplayNames.StepState(step.State), state));
            }
        }
        NotifyGuidedPresentation();
    }

    private void NotifyGuidedPresentation()
    {
        OnPropertyChanged(nameof(GuidedPage));
        OnPropertyChanged(nameof(GuidedCurrentTitle));
        OnPropertyChanged(nameof(GuidedCurrentStatus));
        OnPropertyChanged(nameof(ShowsGuidedCustomSizeAction));
        OnPropertyChanged(nameof(ShowsGuidedDimensions));
        OnPropertyChanged(nameof(ShowsGuidedWhiteInk));
        OnPropertyChanged(nameof(ShowsGuidedPreparation));
        OnPropertyChanged(nameof(ShowsGuidedPreflight));
        OnPropertyChanged(nameof(ShowsGuidedBackgroundDecision));
        OnPropertyChanged(nameof(ShowsGuidedTrimParameters));
        OnPropertyChanged(nameof(ShowsGuidedRunStep));
        OnPropertyChanged(nameof(ShowsGuidedAutomaticBegin));
        OnPropertyChanged(nameof(ShowsGuidedAutomaticConfirmation));
        OnPropertyChanged(nameof(ShowsGuidedEnlargement));
        OnPropertyChanged(nameof(CanChangeGuidedWhiteInk));
        ChangeGuidedWhiteInkCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsConfirmingAutomaticSelectionChanged(bool value) => NotifyOperatorStatusChanged();
}
