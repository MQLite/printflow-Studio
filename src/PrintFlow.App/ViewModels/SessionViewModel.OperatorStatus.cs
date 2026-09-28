using System.Windows.Input;
using PrintFlow.App.Resources;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Services;

namespace PrintFlow.App.ViewModels;

public partial class SessionViewModel
{
    private bool IsAutomaticallyProcessing => _runtime.State != AutomationRuntimeState.Idle ||
        _session?.CurrentStep?.State == StepState.Processing;

    private bool IsStoppedStep => _session?.CurrentStep?.State is
        StepState.Failed or StepState.Interrupted or StepState.RetryRequired;

    public string StatusHeading => _session?.State switch
    {
        SessionState.HandedOff => Strings.Session_StatusHandedOff,
        SessionState.Completed => Strings.Session_StatusCompleted,
        SessionState.Abandoned => Strings.Session_StatusStopped,
        _ when IsAutomaticallyProcessing => Strings.Session_StatusProcessing,
        _ when IsCropping => Strings.Session_StatusInput,
        _ when IsReviewRequired => Strings.Session_StatusReview,
        _ when IsStoppedStep => Strings.Session_StatusStopped,
        _ => Strings.Session_StatusInput,
    };

    public string StatusReason => !IsStoppedStep || IsHandedOff || IsCropping ? string.Empty
        : _session?.CurrentStepFailure is { } failure ? DisplayNames.Failure(failure)
        : _session?.LastAutomationStop?.Mode == PrintFlow.Workflow.Ports.AutomationStopMode.StopOperation
            ? Strings.Failure_AutomationStopped : string.Empty;

    public string NextStepText
    {
        get
        {
            // A job waiting for a colleague says so, and names the one way back (SCRUM-11148).
            if (IsHandedOff && ShowsCorrectionPanel)
                return CorrectionActivity == CorrectionActivity.Checking ? CorrectionActivityText
                    : FormatNext(Strings.Session_NextCorrectionWait, CorrectionImportLabel);
            if (IsHandedOff) return HandedOffNotice;
            // Completed is never presented as Saved (SCRUM-11145).
            if (_session?.State == SessionState.Completed)
                return IsFinalSaveUnsaved ? string.Join(" ", new[] { Strings.Session_NextCompletedNotSaved, FinalSaveActionText }.Where(s => s is { Length: > 0 }))
                    : SaveDisplay.Kind == FinalSaveKind.Uncertain ? FinalSaveStatus : Strings.Session_NextCompleted;
            if (_session?.State == SessionState.Abandoned) return Strings.Session_NextAbandoned;
            if (IsAutomaticallyProcessing) return StoppingNotice ?? FormatNext(Strings.Session_NextProcessing, CurrentStep);
            if (IsAdjustingTrim) return IsBusy ? Strings.Session_TrimAdjustBusy : FormatNext(Strings.Session_NextTrimAdjust, UseThisTrimLabel);
            if (IsCropping) return FormatNext(Strings.Session_NextCrop, ApplyManualCropLabel);
            if (IsAskingColleague) return IsCorrectionBusy ? CorrectionActivityText : FormatNext(Strings.Session_NextAction, CorrectionPrepareLabel);
            // The imported correction under review names the step after background removal, from
            // the workflow definition; background removal is not offered again (design §8.1).
            if (IsReviewRequired && _session?.CorrectionReturn is { NextStep: { } afterCorrection })
                return FormatNext(Strings.Session_CorrectionReview, DisplayNames.Step(afterCorrection));
            if (IsReviewRequired && SaveDisplay.Kind == FinalSaveKind.Pending)
                return CanConfirmAndSave ? FormatNext(Strings.Session_NextFinalReview, ConfirmAndSaveLabel, ApproveLabel)
                    : FormatNext(Strings.Session_NextFinalReviewNeedsDraft, ApproveLabel);
            if (IsReviewRequired) return FormatNext(Strings.Session_NextReview, ApproveLabel, RejectLabel);
            if (CanRetry) return FormatNext(Strings.Session_NextRetry, RetryLabel);
            if (CanManualCrop) return FormatNext(Strings.Session_NextAction, BeginManualCropLabel);
            if (IsStoppedStep) return Strings.Session_NextStopped;
            if (CanConfirmOriginal) return FormatNext(Strings.Session_NextOriginal, ConfirmOriginalLabel);
            if (CanRunStep) return FormatNext(Strings.Session_NextAction, RunStepLabel);
            // The status panel repeats what the save section says, so the two never disagree.
            if (FinalSaveNextText is { } saveText) return saveText;
            if (CanComplete) return FormatNext(Strings.Session_NextAction, CompleteLabel);
            if (CanSelectWhiteUnderbase) return Strings.Session_NextWhiteInk;
            if (_session?.CurrentStep?.Step == StepKind.PrintDimensions) return Strings.Session_NextDimensions;
            if (_session?.CurrentStep?.Step == StepKind.BackgroundRemoval) return Strings.Session_NextBackground;
            return Strings.Session_NextInput;
        }
    }

    /// <summary>Styling only; existing visibility and command eligibility remain authoritative.</summary>
    public ICommand? RecommendedCommand
    {
        get
        {
            if (IsBusy) return null;
            if (IsHandedOff && ShowsCorrectionPanel) return CanImportCorrectedImage ? ImportCorrectedImageCommand : null;
            if (IsHandedOff) return CanReenterAutomation ? ReenterAutomationCommand : null;
            if (_session?.State == SessionState.Completed) return FinalSaveRecommendation;
            if (_session?.State is SessionState.Abandoned || IsAutomaticallyProcessing) return null;
            if (IsAdjustingTrim) return CanUseThisTrim ? UseThisTrimCommand : null;
            if (IsCropping) return CanApplyManualCrop ? ApplyManualCropCommand : null;
            if (IsAskingColleague) return CanPrepareCorrection ? PrepareCorrectionFilesCommand : null;
            if (CanConfirmAndSave) return ConfirmAndSaveCommand;
            if (CanApprove) return ApproveCommand;
            if (CanRetry) return RetryCommand;
            if (CanManualCrop) return BeginManualCropCommand;
            if (IsStoppedStep) return null;
            if (CanConfirmOriginal) return ConfirmOriginalCommand;
            if (CanRunStep) return RunStepCommand;
            if (_session?.CurrentStep is null && FinalSaveRecommendation is { } save) return save;
            if (CanComplete) return CompleteCommand;
            return null;
        }
    }

    /// <summary>The one save-section action worth emphasising for the displayed save state.</summary>
    private ICommand? FinalSaveRecommendation => !HasFinalSave ? null : SaveDisplay.Kind switch
    {
        FinalSaveKind.Collision when CanUseSuggestedName => UseSuggestedNameCommand,
        FinalSaveKind.Uncertain when CanCheckSaveAgain => CheckSaveAgainCommand,
        FinalSaveKind.ApprovedNotSaved when CanRetryRecordedSave => RetryRecordedSaveCommand,
        FinalSaveKind.ApprovedNotSaved or FinalSaveKind.Failed or FinalSaveKind.Collision when CanSaveApproved => SaveApprovedCommand,
        _ => null,
    };

    private string? FinalSaveActionText => FinalSaveRecommendation switch
    {
        null => null,
        var c when ReferenceEquals(c, UseSuggestedNameCommand) => FormatNext(Strings.Session_NextAction, UseSuggestionLabel),
        var c when ReferenceEquals(c, CheckSaveAgainCommand) => FormatNext(Strings.Session_NextAction, CheckAgainLabel),
        var c when ReferenceEquals(c, RetryRecordedSaveCommand) => FormatNext(Strings.Session_NextAction, RetryRecordedLabel),
        _ => FormatNext(Strings.Session_NextAction, SaveApprovedLabel),
    };

    /// <summary>Save states that need the operator, stated exactly as the save section states them.</summary>
    private string? FinalSaveNextText => HasFinalSave && _session?.CurrentStep is null && SaveDisplay.Kind is FinalSaveKind.Failed or FinalSaveKind.Collision or
        FinalSaveKind.Uncertain or FinalSaveKind.ApprovedNotSaved or FinalSaveKind.Missing or FinalSaveKind.Changed or FinalSaveKind.Unavailable
        ? string.Join(" ", new[] { FinalSaveStatus, FinalSaveActionText }.Where(s => s is { Length: > 0 }))
        : null;

    /// <summary>Transient identity for UI focus and input, never stored or used as workflow authority.</summary>
    public string? ReviewTargetIdentity => _session is { State: SessionState.Active, CurrentStep: { } step,
        CurrentArtefact: { IsCurrentStepResult: true } artefact } && IsReviewRequired
        ? $"{_session.Id}|{step.Step}|{artefact.RevisionId}|{artefact.Sha256.Value}"
        : null;

    private static string FormatNext(string format, params object[] arguments) =>
        string.Format(OperatorCulture.Current, format, arguments);

    private void NotifyOperatorStatusChanged()
    {
        OnPropertyChanged(nameof(StatusHeading));
        OnPropertyChanged(nameof(NextStepText));
        OnPropertyChanged(nameof(StatusReason));
        OnPropertyChanged(nameof(RecommendedCommand));
        OnPropertyChanged(nameof(ReviewTargetIdentity));
        NotifyReviewGuidanceChanged();
    }
}
