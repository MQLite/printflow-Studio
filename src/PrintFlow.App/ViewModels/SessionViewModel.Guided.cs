using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using PrintFlow.App.Controls;
using PrintFlow.App.Resources;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Settings;
using PrintFlow.Workflow.Commands;
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
    public bool ShowsGuidedPreviewTools => GuidedPage is not
        (GuidedSessionPage.Dimensions or GuidedSessionPage.WhiteInk or GuidedSessionPage.Preflight);
    public bool ShowsGuidedDetailsColumn => !ShowsGuidedProcessing && GuidedPage != GuidedSessionPage.Crop;
    public bool ShowsGuidedTechnicalDetails => ShowsGuidedDetailsColumn;
    public int GuidedImageColumnSpan => GuidedPage == GuidedSessionPage.Crop ? 2 : 1;
    public System.Windows.GridLength GuidedDetailsWidth => GuidedPage switch
    {
        GuidedSessionPage.Review => new(280),
        GuidedSessionPage.Dimensions or GuidedSessionPage.WhiteInk or GuidedSessionPage.Preflight => new(340),
        GuidedSessionPage.Save or GuidedSessionPage.Completed when HasTiffReview => new(2, System.Windows.GridUnitType.Star),
        GuidedSessionPage.Save or GuidedSessionPage.Completed or GuidedSessionPage.Correction or
            GuidedSessionPage.Recovery or GuidedSessionPage.Failure => new(4, System.Windows.GridUnitType.Star),
        _ => new(2, System.Windows.GridUnitType.Star),
    };
    public bool ExpandsGuidedFinalSave => GuidedPage != GuidedSessionPage.Review ||
        FinalSaveTargets.Any(row => row.Target.PendingReview is not null);
    public string GuidedProcessingApplication => UsesMagickNetTiff ? MagickNetTiffLabel : UsesPhotoshop ? PhotoshopTiffLabel
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
    public bool ShowsGuidedOriginalChecks => ShowsGuidedSourceDetails && (CanConfirmOriginal || IsReviewRequired);
    public string GuidedOriginalChecksHeading => Strings.Resolve("Session_GuidedOriginalChecksHeading");
    public string GuidedOriginalChecks => Strings.Resolve("Session_GuidedOriginalChecks");
    public bool ShowsGuidedPdfSource => ShowsGuidedSourceDetails && HasPdfSource;
    public bool ShowsGuidedPsdSource => ShowsGuidedSourceDetails && HasPsdSource;
    public string OriginalSourceSummary => string.Format(System.Globalization.CultureInfo.CurrentCulture,
        Strings.Resolve("Session_GuidedOriginalFile"), _session?.SourceFileName ?? string.Empty);
    public bool CanChangeRoute => !IsBusy && !IsAutomaticallyProcessing && Allows(CommandKind.SelectWorkflow);
    public string ChangeRouteLabel => Strings.Resolve("Session_GuidedChangeRoute");

    [RelayCommand(CanExecute = nameof(CanChangeRoute))]
    private void ChangeRoute()
    {
        if (!CanChangeRoute || _session is not { } session) return;
        RequestLeave(() => { ClearPreviews(); _navigation.GoToWorkflowSelection(session); });
    }
    private WhiteUnderbaseChoice? _pendingGuidedWhiteInk;
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
        { CurrentStep.Step: StepKind.ApprovedPngExport } => GuidedSessionPage.Save,
        { CurrentStep: null } => GuidedSessionPage.Save,
        { CurrentStep.Step: StepKind.OriginalConfirmation, OriginalSourceFormat: ImageFormat.Pdf or ImageFormat.Psd }
            => GuidedSessionPage.SourcePreparation,
        { CurrentStep.Step: StepKind.OriginalConfirmation } => GuidedSessionPage.Source,
        { CurrentStep.Step: StepKind.PrintDimensions } => GuidedSessionPage.Dimensions,
        { CurrentStep.Step: StepKind.PhotoshopOutput, WhiteUnderbaseBranch: null } => GuidedSessionPage.WhiteInk,
        { CurrentStep.Step: StepKind.PhotoshopOutput } => GuidedSessionPage.Preflight,
        _ => GuidedSessionPage.Ready,
    };

    public string GuidedCurrentTitle => GuidedPage switch
    {
        GuidedSessionPage.WhiteInk => WhiteUnderbaseHeading,
        GuidedSessionPage.SourcePreparation => RunStepLabel,
        GuidedSessionPage.Preflight => Strings.Session_GuidedPreflight,
        GuidedSessionPage.Completed when !HasFinalSave => GuidedCompletionState,
        GuidedSessionPage.Save or GuidedSessionPage.Completed when HasFinalSave => GuidedCompletionHeading,
        _ => CurrentStep.Length > 0 ? CurrentStep : StatusHeading,
    };
    public string GuidedCurrentStatus => StatusHeading;
    public string GuidedInteractionIdentity => $"{NavigationContext(_session)}|{GuidedPage}|{IsViewingHistory}|{SelectedHistory?.Step}|{IsConfirmingDraftLeave}|{IsConfirmingReturn}|{IsChoosingCustomSize}|{IsComparingTrim}|{SelectedSave?.Key}";
    public string CropDetailsLabel => Strings.Resolve("Session_CropDetailsLabel");
    public bool ShowsManualCropEntry => CanManualCrop && !IsCropping && !UsesImageProcessingAdvance;
    public bool ShowsManualCropEntryNotice => IsManualCropRequired && !IsCropping;
    public string GuidedTechnicalDetailsLabel => Strings.Session_GuidedTechnicalDetails;
    public bool ShowsGuidedDimensions => GuidedPage == GuidedSessionPage.Dimensions && CanChooseFlexibleSize;
    public bool ShowsGuidedCustomSizeAction => ShowsGuidedDimensions && IsChoosingCustomSize;
    public bool ShowsGuidedCustomSizeEntry => ShowsGuidedDimensions && !IsChoosingCustomSize;
    public bool ShowsGuidedWhiteInk => GuidedPage == GuidedSessionPage.WhiteInk && CanSelectWhiteUnderbase;
    public bool ShowsGuidedWhiteInkOptions => ShowsGuidedWhiteInk || GuidedPage == GuidedSessionPage.Preflight;
    public bool ShowsGuidedPreparation => HasPreparationPlan && GuidedPage is
        GuidedSessionPage.Dimensions or GuidedSessionPage.Preflight;
    public bool ShowsGuidedPreflight => HasPrintDimensionsPreflight && GuidedPage is
        GuidedSessionPage.Dimensions or GuidedSessionPage.Preflight;
    public bool ShowsGuidedBackgroundDecision => _session?.CurrentStep?.Step == StepKind.BackgroundRemoval &&
        _session.CurrentArtefact is not null && GuidedPage == GuidedSessionPage.Ready && CanAuthoriseAutomaticSelection;
    public bool ShowsGuidedTrimParameters => _session?.CurrentStep?.Step == StepKind.Trim &&
        GuidedPage == GuidedSessionPage.Ready &&
        (SelectedImageProcessingMethod is null or ImageProcessingMethod.AlphaTrim) &&
        (CanSetTrimParameters || _session.ImageProcessingExecution?.Method != ImageProcessingMethod.AlphaTrim &&
            Offers(CommandKind.SetImageProcessingSelection));
    public bool ShowsGuidedRunStep => ShowsImageProcessingAdvance ||
        !UsesImageProcessingAdvance && GuidedPage != GuidedSessionPage.WhiteInk &&
        (CanRunStep || HasPendingGuidedWhiteInk && Allows(CommandKind.StartStep));
    public bool CanRunGuidedStep => ShowsImageProcessingAdvance
        ? CanAdvanceImageProcessing() : !IsBusy && CanRunStep;
    public bool ShowsGuidedAutomaticConfirmation => ShowsGuidedBackgroundDecision && IsAutomaticSelectionPending;
    public bool ShowsGuidedEnlargement => ShowsGuidedPreparation && NeedsEnlargementAuthority;
    public bool CanEditGuidedWhiteInk => ShowsGuidedWhiteInkOptions && CanSelectWhiteUnderbase && !IsBusy;
    public bool HasPendingGuidedWhiteInk => GuidedPage == GuidedSessionPage.Preflight && _pendingGuidedWhiteInk is not null;
    public bool CanApplyGuidedWhiteInk => HasPendingGuidedWhiteInk && CanEditGuidedWhiteInk;
    public string ApplyGuidedWhiteInkLabel => Strings.Resolve("Session_GuidedWhiteInkRetry");
    private string GuidedWhiteInkPendingInstruction => Strings.Resolve(IsBusy
        ? "Session_GuidedWhiteInkSaving" : "Session_GuidedWhiteInkPending");

    public WhiteUnderbaseChoice? GuidedWhiteInkSelection
    {
        get => GuidedPage == GuidedSessionPage.Preflight
            ? _pendingGuidedWhiteInk ?? WhiteUnderbaseChoices.FirstOrDefault(c => c.Branch == _session?.WhiteUnderbaseBranch)
            : SelectedWhiteUnderbaseChoice;
        set
        {
            if (!CanEditGuidedWhiteInk) return;
            if (GuidedPage == GuidedSessionPage.WhiteInk)
            {
                SelectedWhiteUnderbaseChoice = value;
                return;
            }
            if (value is null || value.Branch == GuidedWhiteInkSelection?.Branch) return;

            _pendingGuidedWhiteInk = value.Branch == _session?.WhiteUnderbaseBranch ? null : value;
            NotifyOperatorStatusChanged();
            if (HasPendingGuidedWhiteInk) ApplyGuidedWhiteInkCommand.Execute(null);
        }
    }

    [RelayCommand(CanExecute = nameof(CanApplyGuidedWhiteInk))]
    private Task ApplyGuidedWhiteInkAsync(CancellationToken cancellationToken) =>
        CanApplyGuidedWhiteInk && _pendingGuidedWhiteInk is { } choice
            ? ApplyWhiteUnderbaseChoiceAsync(choice, cancellationToken)
            : Task.CompletedTask;

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
                        : DisplayNames.StepState(step.State), state)
                {
                    Number = GuidedSteps.Count + 1,
                    IsCurrent = step.Step == _session.CurrentStep?.Step,
                });
            }
        }
        NotifyGuidedPresentation();
    }

    private void NotifyGuidedPresentation()
    {
        NotifyUtilities();
        NotifyGuidedCompletion();
        NotifyGuidedRecovery();
        OnPropertyChanged(nameof(SaveManualHandoffCopyLabel));
        OnPropertyChanged(nameof(ShowsManualHandoffCopy));
        OnPropertyChanged(nameof(CanSaveManualHandoffCopy));
        OnPropertyChanged(nameof(ShowsGuidedProcessing));
        OnPropertyChanged(nameof(ShowsManualCropEntry));
        OnPropertyChanged(nameof(ShowsManualCropEntryNotice));
        OnPropertyChanged(nameof(ShowsGuidedImageWorkspace));
        OnPropertyChanged(nameof(ShowsGuidedPreviewTools));
        OnPropertyChanged(nameof(ShowsGuidedDetailsColumn));
        OnPropertyChanged(nameof(ShowsGuidedTechnicalDetails));
        OnPropertyChanged(nameof(GuidedImageColumnSpan));
        OnPropertyChanged(nameof(GuidedDetailsWidth));
        NotifyGuidedPrintSummary();
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
        OnPropertyChanged(nameof(ShowsGuidedOriginalChecks));
        OnPropertyChanged(nameof(GuidedOriginalChecksHeading));
        OnPropertyChanged(nameof(GuidedOriginalChecks));
        OnPropertyChanged(nameof(ShowsGuidedPdfSource));
        OnPropertyChanged(nameof(ShowsGuidedPsdSource));
        OnPropertyChanged(nameof(OriginalSourceSummary));
        OnPropertyChanged(nameof(CanChangeRoute));
        ChangeRouteCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ShowsGuidedCustomSizeAction));
        OnPropertyChanged(nameof(ShowsGuidedCustomSizeEntry));
        OnPropertyChanged(nameof(ShowsGuidedDimensions));
        OnPropertyChanged(nameof(ShowsGuidedWhiteInk));
        OnPropertyChanged(nameof(ShowsGuidedWhiteInkOptions));
        OnPropertyChanged(nameof(GuidedWhiteInkSelection));
        OnPropertyChanged(nameof(ShowsGuidedPreparation));
        OnPropertyChanged(nameof(ShowsGuidedPreflight));
        NotifyTiffBackendSelection();
        OnPropertyChanged(nameof(ShowsGuidedBackgroundDecision));
        OnPropertyChanged(nameof(ShowsGuidedTrimParameters));
        OnPropertyChanged(nameof(ShowsGuidedRunStep));
        OnPropertyChanged(nameof(CanRunGuidedStep));
        OnPropertyChanged(nameof(CanRunStep));
        OnPropertyChanged(nameof(HasRunReadinessNotice));
        OnPropertyChanged(nameof(CanChangeImageProcessingMethod));
        NotifyImageProcessingAdvance();
        OnPropertyChanged(nameof(CanReturnToSeparateProcessing));
        OnPropertyChanged(nameof(ShowsGuidedAutomaticConfirmation));
        OnPropertyChanged(nameof(CanConfirmAutomaticSelection));
        OnPropertyChanged(nameof(CanChangeRecognitionMode));
        OnPropertyChanged(nameof(ShowsGuidedEnlargement));
        OnPropertyChanged(nameof(CanEditGuidedWhiteInk));
        OnPropertyChanged(nameof(HasPendingGuidedWhiteInk));
        OnPropertyChanged(nameof(CanApplyGuidedWhiteInk));
        ApplyGuidedWhiteInkCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsConfirmingReturnChanged(bool value) => NotifyNavigation();
}
