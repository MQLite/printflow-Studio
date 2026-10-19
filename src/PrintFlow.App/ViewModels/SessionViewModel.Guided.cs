using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using PrintFlow.App.Controls;
using PrintFlow.App.Resources;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Definitions;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.App.ViewModels;

/// <summary>Presentation pages, not workflow nodes or command qualifications.</summary>
public enum GuidedSessionPage
{
    Source, SourcePreparation, Ready, Processing, Review, Dimensions, WhiteInk, Preflight,
    Crop, Correction, Recovery, Failure, Save, Completed,
}

public sealed partial class SessionViewModel
{
    public bool ShowsGuidedProcessing => GuidedPage == GuidedSessionPage.Processing;
    public bool ShowsGuidedImageWorkspace => !ShowsGuidedProcessing;
    public bool ShowsGuidedDetailsColumn => !ShowsGuidedProcessing && GuidedPage != GuidedSessionPage.Crop;
    public bool ShowsGuidedTechnicalDetails => ShowsGuidedDetailsColumn;
    public int GuidedImageColumnSpan => GuidedPage == GuidedSessionPage.Crop ? 2 : 1;
    public bool ExpandsGuidedFinalSave => GuidedPage != GuidedSessionPage.Review;
    public string GuidedProcessingApplication => UsesPhotoshop ? "Photoshop"
        : _runtime.DrivesExternalApplication ? Strings.Resolve("Session_GuidedMeitu") : "PrintFlow";
    public string GuidedProcessingState => _runtime.State switch
    {
        AutomationRuntimeState.StopRequested => Strings.Resolve("Session_GuidedStopping"),
        AutomationRuntimeState.TakeOverRequested => Strings.Resolve("Session_GuidedTakingOver"),
        _ => Strings.Session_StatusProcessing,
    };
    public string GuidedProcessingNotice => StoppingNotice ?? (_runtime.Phase switch
    {
        ExternalOperationPhase.OperationRequested => Strings.Resolve("Session_GuidedPhaseRequested"),
        ExternalOperationPhase.Busy => Strings.Resolve("Session_GuidedPhaseBusy"),
        ExternalOperationPhase.CompletedBeforeExport => Strings.Resolve("Session_GuidedPhaseProcessed"),
        ExternalOperationPhase.ExportPrepared => Strings.Resolve("Session_GuidedPhaseExportPrepared"),
        ExternalOperationPhase.ExportConfirmed => Strings.Resolve("Session_GuidedPhaseWriting"),
        ExternalOperationPhase.OutputValidated => Strings.Resolve("Session_GuidedPhaseValidated"),
        ExternalOperationPhase.UnknownOrBlocked => Strings.Resolve("Session_GuidedPhaseUnknown"),
        _ => Strings.Resolve("Session_GuidedPhaseStarting"),
    });
    public string GuidedProcessingWait => Strings.Resolve("Session_GuidedProcessingWait");
    public bool ShowsGuidedSourceDetails => !IsViewingHistory && _session?.CurrentStep?.Step == StepKind.OriginalConfirmation;
    public bool ShowsGuidedPdfSource => ShowsGuidedSourceDetails && HasPdfSource;
    public bool ShowsGuidedPsdSource => ShowsGuidedSourceDetails && HasPsdSource;
    public string OriginalSourceSummary => string.Format(System.Globalization.CultureInfo.CurrentCulture,
        Strings.Resolve("Session_GuidedOriginalFile"), _session?.SourceFileName ?? string.Empty);
    public bool CanChangeRoute => !IsBusy && Allows(CommandKind.SelectWorkflow);
    public string ChangeRouteLabel => Strings.Resolve("Session_GuidedChangeRoute");

    [RelayCommand(CanExecute = nameof(CanChangeRoute))]
    private void ChangeRoute()
    {
        if (!CanChangeRoute || _session is not { } session) return;
        RequestLeave(() => { ClearPreviews(); _navigation.GoToWorkflowSelection(session); });
    }
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
    public string GuidedInteractionIdentity => $"{NavigationContext(_session)}|{GuidedPage}|{IsViewingHistory}|{SelectedHistory?.Step}|{IsConfirmingDraftLeave}|{IsConfirmingReturn}|{IsConfirmingAutomaticSelection}|{IsChoosingCustomSize}|{IsComparingTrim}|{SelectedSave?.Key}";
    public string GuidedTechnicalDetailsLabel => Strings.Session_GuidedTechnicalDetails;
    public bool ShowsGuidedDimensions => GuidedPage == GuidedSessionPage.Dimensions && CanChooseFlexibleSize;
    public bool ShowsGuidedCustomSizeAction => ShowsGuidedDimensions && IsChoosingCustomSize;
    public bool ShowsGuidedCustomSizeEntry => ShowsGuidedDimensions && !IsChoosingCustomSize;
    public bool ShowsGuidedWhiteInk => GuidedPage == GuidedSessionPage.WhiteInk && CanSelectWhiteUnderbase;
    public bool ShowsGuidedPreparation => HasPreparationPlan && GuidedPage is
        GuidedSessionPage.Dimensions or GuidedSessionPage.Preflight;
    public bool ShowsGuidedPreflight => HasPrintDimensionsPreflight && GuidedPage is
        GuidedSessionPage.Dimensions or GuidedSessionPage.Preflight;
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
        OnPropertyChanged(nameof(ShowsGuidedProcessing));
        OnPropertyChanged(nameof(ShowsGuidedImageWorkspace));
        OnPropertyChanged(nameof(ShowsGuidedDetailsColumn));
        OnPropertyChanged(nameof(ShowsGuidedTechnicalDetails));
        OnPropertyChanged(nameof(GuidedImageColumnSpan));
        OnPropertyChanged(nameof(ExpandsGuidedFinalSave));
        OnPropertyChanged(nameof(GuidedProcessingApplication));
        OnPropertyChanged(nameof(GuidedProcessingState));
        OnPropertyChanged(nameof(GuidedProcessingNotice));
        OnPropertyChanged(nameof(GuidedProcessingWait));
        OnPropertyChanged(nameof(GuidedPage));
        OnPropertyChanged(nameof(GuidedCurrentTitle));
        OnPropertyChanged(nameof(GuidedCurrentStatus));
        OnPropertyChanged(nameof(GuidedInteractionIdentity));
        OnPropertyChanged(nameof(ShowsGuidedSourceDetails));
        OnPropertyChanged(nameof(ShowsGuidedPdfSource));
        OnPropertyChanged(nameof(ShowsGuidedPsdSource));
        OnPropertyChanged(nameof(OriginalSourceSummary));
        OnPropertyChanged(nameof(CanChangeRoute));
        ChangeRouteCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ShowsGuidedCustomSizeAction));
        OnPropertyChanged(nameof(ShowsGuidedCustomSizeEntry));
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
    partial void OnIsConfirmingReturnChanged(bool value) => NotifyNavigation();
}
