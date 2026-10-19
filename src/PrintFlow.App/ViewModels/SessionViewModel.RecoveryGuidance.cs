using PrintFlow.App.Resources;

namespace PrintFlow.App.ViewModels;

/// <summary>Recovery guidance reads current offers; it never grants an action or restarts a run.</summary>
public sealed partial class SessionViewModel
{
    public bool ShowsGuidedRecovery => !IsViewingHistory && GuidedPage is
        GuidedSessionPage.Correction or GuidedSessionPage.Recovery or GuidedSessionPage.Failure;
    public bool ShowsGuidedCorrectionStages => ShowsGuidedRecovery && GuidedPage == GuidedSessionPage.Correction;
    public bool ShowsGuidedCorrectionReview => !IsViewingHistory && IsReviewRequired && _session?.CorrectionReturn is not null;
    public string GuidedCorrectionStages => Strings.Resolve("Session_RecoveryCorrectionStages");
    public string GuidedCorrectionReview => Strings.Resolve("Session_RecoveryCorrectionReview");
    public string GuidedRecoveryHeading => Strings.Resolve(IsAskingColleague
        ? "Session_RecoveryPrepare" : ShowsCorrectionPanel ? "Session_RecoveryWaiting"
        : IsHandedOff ? "Session_RecoveryHandedOff" : "Session_RecoveryStopped");
    public string GuidedRecoveryPreserved => Strings.Resolve(IsAskingColleague
        ? "Session_RecoveryPreparePreserved" : ShowsCorrectionPanel ? "Session_RecoveryWaitingPreserved"
        : UsesPhotoshop && IsHandedOff ? "Session_RecoveryPhotoshopPreserved" : "Session_RecoveryPreserved");
    public string GuidedRecoveryNext
    {
        get
        {
            if (IsCorrectionBusy) return CorrectionActivityText;
            if (!CanInteractWithLivePage || IsBusy) return Strings.Resolve("Session_RecoveryBusy");
            List<string> actions = [];
            if (CanPrepareCorrection) actions.Add(CorrectionPrepareLabel);
            if (CanImportCorrectedImage) actions.Add(CorrectionImportLabel);
            if (CanRepairCorrectionFiles) actions.Add(CorrectionRepairLabel);
            if (CanReenterAutomation) actions.Add(ReenterAutomationLabel);
            if (CanRetry) actions.Add(RetryLabel);
            if (CanSubmitManualResult) actions.Add(SubmitManualResultLabel);
            if (CanManualCrop) actions.Add(BeginManualCropLabel);
            return actions.Count == 0 ? Strings.Resolve("Session_RecoveryNoAction")
                : string.Format(System.Globalization.CultureInfo.CurrentCulture,
                    Strings.Resolve("Session_RecoveryNext"), string.Join(" / ", actions));
        }
    }

    private void NotifyGuidedRecovery()
    {
        OnPropertyChanged(nameof(ShowsGuidedRecovery));
        OnPropertyChanged(nameof(ShowsGuidedCorrectionStages));
        OnPropertyChanged(nameof(ShowsGuidedCorrectionReview));
        OnPropertyChanged(nameof(GuidedCorrectionStages));
        OnPropertyChanged(nameof(GuidedCorrectionReview));
        OnPropertyChanged(nameof(GuidedRecoveryHeading));
        OnPropertyChanged(nameof(GuidedRecoveryPreserved));
        OnPropertyChanged(nameof(GuidedRecoveryNext));
    }
}
