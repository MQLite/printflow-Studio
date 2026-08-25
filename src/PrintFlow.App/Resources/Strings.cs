using System.Globalization;
using System.Resources;

namespace PrintFlow.App.Resources;

/// <summary>
/// Typed access to the operator-visible strings in <c>Strings.resx</c>.
/// </summary>
/// <remarks>
/// Resolution follows <see cref="CultureInfo.CurrentUICulture"/>, so the <c>zh-CN</c>
/// satellite is picked up automatically on a Chinese workstation. A runtime language
/// switcher is a later slice (Epic 11100 plan §16.3).
///
/// Internal state names, failure codes and adapter identifiers deliberately stay outside
/// this file: they are stable English and are never localised (MVP design §13.4).
/// </remarks>
internal static class Strings
{
    private static readonly ResourceManager Manager =
        new("PrintFlow.App.Resources.Strings", typeof(Strings).Assembly);

    internal static string App_Title => Get(nameof(App_Title));

    internal static string Workflow_PrepareAsset => Get(nameof(Workflow_PrepareAsset));

    internal static string Workflow_PrepareCustomerDesign => Get(nameof(Workflow_PrepareCustomerDesign));

    internal static string Workflow_GeneratePrintTiff => Get(nameof(Workflow_GeneratePrintTiff));

    internal static string Step_Import => Get(nameof(Step_Import));

    internal static string Step_OriginalConfirmation => Get(nameof(Step_OriginalConfirmation));

    internal static string Step_Enhancement => Get(nameof(Step_Enhancement));

    internal static string Step_BackgroundRemoval => Get(nameof(Step_BackgroundRemoval));

    internal static string Step_Trim => Get(nameof(Step_Trim));

    internal static string Step_ApprovedPngExport => Get(nameof(Step_ApprovedPngExport));

    internal static string Step_PrintDimensions => Get(nameof(Step_PrintDimensions));

    internal static string Step_PhotoshopOutput => Get(nameof(Step_PhotoshopOutput));

    internal static string Flag_Skippable => Get(nameof(Flag_Skippable));

    internal static string Flag_RequiresReview => Get(nameof(Flag_RequiresReview));

    internal static string SessionState_Active => Get(nameof(SessionState_Active));

    internal static string SessionState_HandedOff => Get(nameof(SessionState_HandedOff));

    internal static string SessionState_Completed => Get(nameof(SessionState_Completed));

    internal static string SessionState_Abandoned => Get(nameof(SessionState_Abandoned));

    internal static string StepState_Waiting => Get(nameof(StepState_Waiting));

    internal static string StepState_Processing => Get(nameof(StepState_Processing));

    internal static string StepState_ReviewRequired => Get(nameof(StepState_ReviewRequired));

    internal static string StepState_Approved => Get(nameof(StepState_Approved));

    internal static string StepState_RetryRequired => Get(nameof(StepState_RetryRequired));

    internal static string StepState_Skipped => Get(nameof(StepState_Skipped));

    internal static string StepState_Failed => Get(nameof(StepState_Failed));

    internal static string StepState_Interrupted => Get(nameof(StepState_Interrupted));

    internal static string Startup_AlreadyRunning => Get(nameof(Startup_AlreadyRunning));

    internal static string Startup_Failed => Get(nameof(Startup_Failed));

    internal static string Startup_RecoveryNotRun => Get(nameof(Startup_RecoveryNotRun));

    internal static string Startup_RecoveryClean => Get(nameof(Startup_RecoveryClean));

    /// <summary>Composite format: interrupted attempts, released locks, quarantined files.</summary>
    internal static string Startup_RecoverySummary => Get(nameof(Startup_RecoverySummary));

    internal static string Preset_Verified => Get(nameof(Preset_Verified));

    internal static string Preset_NotVerified => Get(nameof(Preset_NotVerified));

    internal static string Nav_BackToHome => Get(nameof(Nav_BackToHome));

    internal static string Home_ImportHeading => Get(nameof(Home_ImportHeading));

    internal static string Home_ImportHint => Get(nameof(Home_ImportHint));

    internal static string Home_ChooseFile => Get(nameof(Home_ChooseFile));

    /// <summary>Windows common-dialog filter; the extension lists are not localised.</summary>
    internal static string Home_ImportFilter => Get(nameof(Home_ImportFilter));

    internal static string Home_RecentHeading => Get(nameof(Home_RecentHeading));

    internal static string Home_Refresh => Get(nameof(Home_Refresh));

    internal static string Home_Resume => Get(nameof(Home_Resume));

    internal static string Home_Details => Get(nameof(Home_Details));

    internal static string Home_Abandon => Get(nameof(Home_Abandon));

    internal static string Home_NoRecentSessions => Get(nameof(Home_NoRecentSessions));

    internal static string Home_DropNothing => Get(nameof(Home_DropNothing));

    /// <summary>Composite format: how many files were dropped.</summary>
    internal static string Home_DropSingleFileOnly => Get(nameof(Home_DropSingleFileOnly));

    /// <summary>Composite format: the stable failure code.</summary>
    internal static string Home_ImportFailed => Get(nameof(Home_ImportFailed));

    /// <summary>Composite format: the stable failure code.</summary>
    internal static string Home_ResumeFailed => Get(nameof(Home_ResumeFailed));

    /// <summary>Composite format: the stable failure code.</summary>
    internal static string Home_AbandonFailed => Get(nameof(Home_AbandonFailed));

    /// <summary>Composite format: the abandoned session's output name.</summary>
    internal static string Home_AbandonDone => Get(nameof(Home_AbandonDone));

    /// <summary>Composite format: the stable failure code.</summary>
    internal static string Home_RecentUnavailable => Get(nameof(Home_RecentUnavailable));

    internal static string WorkflowSelection_Heading => Get(nameof(WorkflowSelection_Heading));

    internal static string WorkflowSelection_Hint => Get(nameof(WorkflowSelection_Hint));

    internal static string WorkflowSelection_Select => Get(nameof(WorkflowSelection_Select));

    internal static string WorkflowSelection_Locked => Get(nameof(WorkflowSelection_Locked));

    /// <summary>Composite format: the stable failure code.</summary>
    internal static string WorkflowSelection_Refused => Get(nameof(WorkflowSelection_Refused));

    internal static string Session_StepsHeading => Get(nameof(Session_StepsHeading));

    internal static string Session_PlaceholderNotice => Get(nameof(Session_PlaceholderNotice));

    internal static string Session_AllStepsFinished => Get(nameof(Session_AllStepsFinished));

    // --- Part 3C3A: processing and review controls ---------------------------------------

    internal static string Session_ConfirmOriginal => Get(nameof(Session_ConfirmOriginal));

    internal static string Session_RunStep => Get(nameof(Session_RunStep));

    internal static string Session_Approve => Get(nameof(Session_Approve));

    internal static string Session_Reject => Get(nameof(Session_Reject));

    internal static string Session_Retry => Get(nameof(Session_Retry));

    internal static string Session_Skip => Get(nameof(Session_Skip));

    internal static string Session_HandOff => Get(nameof(Session_HandOff));

    /// <summary>The unmissable warning that results are synthetic (Part 3C3A §8).</summary>
    internal static string Session_FakeModeNotice => Get(nameof(Session_FakeModeNotice));

    internal static string Session_HandedOffNotice => Get(nameof(Session_HandedOffNotice));

    internal static string Session_ReviewHeading => Get(nameof(Session_ReviewHeading));

    internal static string Session_RejectReasonLabel => Get(nameof(Session_RejectReasonLabel));

    internal static string Session_RejectNotesLabel => Get(nameof(Session_RejectNotesLabel));

    internal static string Session_ArtefactHeading => Get(nameof(Session_ArtefactHeading));

    internal static string Session_ArtefactNone => Get(nameof(Session_ArtefactNone));

    /// <summary>Says the artefact shown is the step's input rather than its result.</summary>
    internal static string Session_ArtefactIsInput => Get(nameof(Session_ArtefactIsInput));

    internal static string Session_LabelFileName => Get(nameof(Session_LabelFileName));

    internal static string Session_LabelFormat => Get(nameof(Session_LabelFormat));

    internal static string Session_LabelPixels => Get(nameof(Session_LabelPixels));

    internal static string Session_LabelDpi => Get(nameof(Session_LabelDpi));

    internal static string Session_LabelHash => Get(nameof(Session_LabelHash));

    internal static string Session_LabelRevision => Get(nameof(Session_LabelRevision));

    /// <summary>Shown where a structural fact was legitimately not determined.</summary>
    internal static string Session_ValueUnknown => Get(nameof(Session_ValueUnknown));

    /// <summary>Composite format: the stable failure code.</summary>
    internal static string Session_ActionFailed => Get(nameof(Session_ActionFailed));

    // --- Part 3C3B: dimensions, W1 and production output ---------------------------------

    internal static string Session_DimensionsHeading => Get(nameof(Session_DimensionsHeading));

    internal static string Session_DimensionsHint => Get(nameof(Session_DimensionsHint));

    internal static string Session_LabelWidthMm => Get(nameof(Session_LabelWidthMm));

    internal static string Session_LabelHeightMm => Get(nameof(Session_LabelHeightMm));

    internal static string Session_DimensionsConfirm => Get(nameof(Session_DimensionsConfirm));

    /// <summary>Shown when the typed millimetres are not a size the domain will accept.</summary>
    internal static string Session_DimensionsInvalid => Get(nameof(Session_DimensionsInvalid));

    /// <summary>Composite format: width mm, height mm, pixel width, pixel height, DPI.</summary>
    internal static string Session_DimensionsSummary => Get(nameof(Session_DimensionsSummary));

    internal static string Session_DimensionsNotSet => Get(nameof(Session_DimensionsNotSet));

    internal static string Session_PresetsLabel => Get(nameof(Session_PresetsLabel));

    internal static string Session_PresetHint => Get(nameof(Session_PresetHint));

    internal static string Preset_A3Landscape => Get(nameof(Preset_A3Landscape));

    internal static string Preset_A3Portrait => Get(nameof(Preset_A3Portrait));

    internal static string Preset_A4 => Get(nameof(Preset_A4));

    internal static string Preset_A5 => Get(nameof(Preset_A5));

    internal static string Preset_Custom => Get(nameof(Preset_Custom));

    internal static string Session_W1Heading => Get(nameof(Session_W1Heading));

    /// <summary>Operator guidance for the W1 branches. Guidance only — never a suggestion the app acts on.</summary>
    internal static string Session_W1Hint => Get(nameof(Session_W1Hint));

    internal static string Session_W1Confirm => Get(nameof(Session_W1Confirm));

    internal static string Session_W1NotChosen => Get(nameof(Session_W1NotChosen));

    internal static string W1_0px => Get(nameof(W1_0px));

    internal static string W1_1px => Get(nameof(W1_1px));

    internal static string W1_2px => Get(nameof(W1_2px));

    /// <summary>
    /// The extra warning shown for a synthetic production TIFF (Part 3C3B §10).
    /// </summary>
    internal static string Session_FakeTiffNotice => Get(nameof(Session_FakeTiffNotice));

    internal static string Session_Complete => Get(nameof(Session_Complete));

    internal static string Session_AddAnotherSize => Get(nameof(Session_AddAnotherSize));

    internal static string Session_OutputsHeading => Get(nameof(Session_OutputsHeading));

    internal static string Session_LabelBranch => Get(nameof(Session_LabelBranch));

    internal static string Session_LabelReview => Get(nameof(Session_LabelReview));

    internal static string Session_OutputValid => Get(nameof(Session_OutputValid));

    internal static string Session_OutputInvalid => Get(nameof(Session_OutputInvalid));

    internal static string ReviewState_NotReviewed => Get(nameof(ReviewState_NotReviewed));

    internal static string ReviewState_Approved => Get(nameof(ReviewState_Approved));

    internal static string ReviewState_Rejected => Get(nameof(ReviewState_Rejected));

    internal static string Failure_OutputMissing => Get(nameof(Failure_OutputMissing));

    internal static string Failure_OutputUnreadable => Get(nameof(Failure_OutputUnreadable));

    internal static string Failure_OutputValidationFailed => Get(nameof(Failure_OutputValidationFailed));

    internal static string Failure_Timeout => Get(nameof(Failure_Timeout));

    internal static string Failure_Cancelled => Get(nameof(Failure_Cancelled));

    internal static string Failure_RevisionIntegrityMismatch => Get(nameof(Failure_RevisionIntegrityMismatch));

    internal static string Failure_EnvironmentNotVerified => Get(nameof(Failure_EnvironmentNotVerified));

    internal static string Failure_AdapterUnavailable => Get(nameof(Failure_AdapterUnavailable));

    internal static string Failure_PresetHashMismatch => Get(nameof(Failure_PresetHashMismatch));

    internal static string Failure_UnknownDialog => Get(nameof(Failure_UnknownDialog));

    internal static string Failure_WorkspaceError => Get(nameof(Failure_WorkspaceError));

    internal static string Failure_PersistenceError => Get(nameof(Failure_PersistenceError));

    internal static string Failure_PreconditionNotMet => Get(nameof(Failure_PreconditionNotMet));

    internal static string Failure_ManualCropRequired => Get(nameof(Failure_ManualCropRequired));

    internal static string Failure_MeituNotInstalled => Get(nameof(Failure_MeituNotInstalled));

    internal static string Failure_MeituLaunchFailed => Get(nameof(Failure_MeituLaunchFailed));

    internal static string Failure_MeituWindowNotFound => Get(nameof(Failure_MeituWindowNotFound));

    internal static string Failure_MeituTargetLost => Get(nameof(Failure_MeituTargetLost));

    internal static string Failure_MeituUnknownState => Get(nameof(Failure_MeituUnknownState));

    internal static string Failure_MeituBlockingDialog => Get(nameof(Failure_MeituBlockingDialog));

    internal static string Failure_MeituOpenInputFailed => Get(nameof(Failure_MeituOpenInputFailed));

    internal static string Rejection_InsufficientResult => Get(nameof(Rejection_InsufficientResult));

    internal static string Rejection_EdgeError => Get(nameof(Rejection_EdgeError));

    internal static string Rejection_MissingContent => Get(nameof(Rejection_MissingContent));

    internal static string Rejection_ColourIssue => Get(nameof(Rejection_ColourIssue));

    internal static string Rejection_DimensionIssue => Get(nameof(Rejection_DimensionIssue));

    internal static string Rejection_WhiteInkIssue => Get(nameof(Rejection_WhiteInkIssue));

    internal static string Rejection_Other => Get(nameof(Rejection_Other));

    internal static string Format_Png => Get(nameof(Format_Png));

    internal static string Format_Jpeg => Get(nameof(Format_Jpeg));

    internal static string Format_Tiff => Get(nameof(Format_Tiff));

    internal static string Format_Psd => Get(nameof(Format_Psd));

    internal static string Format_Pdf => Get(nameof(Format_Pdf));

    internal static string Format_Unknown => Get(nameof(Format_Unknown));

    // --- Epic 11200 Part C1: image preview, comparison and zoom ---------------------------

    internal static string Session_PreviewHeading => Get(nameof(Session_PreviewHeading));

    /// <summary>Heading of the single pane when there is nothing to compare against.</summary>
    internal static string Session_PreviewCurrent => Get(nameof(Session_PreviewCurrent));

    /// <summary>Heading of the upstream half of a comparison.</summary>
    internal static string Session_PreviewBefore => Get(nameof(Session_PreviewBefore));

    /// <summary>Heading of the step-result half of a comparison.</summary>
    internal static string Session_PreviewAfter => Get(nameof(Session_PreviewAfter));

    /// <summary>Composite format: pixel width, pixel height.</summary>
    internal static string Session_PreviewPixels => Get(nameof(Session_PreviewPixels));

    /// <summary>Says the preview is a reduced stand-in rather than every pixel.</summary>
    internal static string Session_PreviewReduced => Get(nameof(Session_PreviewReduced));

    /// <summary>Shown when no preview could be produced. Never a workflow failure.</summary>
    internal static string Session_PreviewUnavailable => Get(nameof(Session_PreviewUnavailable));

    /// <summary>Shown when the file exists but this workstation's decoder cannot display it.</summary>
    internal static string Session_PreviewLoadFailed => Get(nameof(Session_PreviewLoadFailed));

    internal static string Session_ZoomIn => Get(nameof(Session_ZoomIn));

    internal static string Session_ZoomOut => Get(nameof(Session_ZoomOut));

    internal static string Session_ZoomReset => Get(nameof(Session_ZoomReset));

    /// <summary>The zoom read-out while the whole image is fitted to the viewport.</summary>
    internal static string Session_ZoomFit => Get(nameof(Session_ZoomFit));

    /// <summary>Composite format: the zoom percentage.</summary>
    internal static string Session_ZoomPercent => Get(nameof(Session_ZoomPercent));

    /// <summary>Says a trim needs a human and what to do next (Part C1 §17; Part C2 §34).</summary>
    internal static string Session_ManualCropRequiredNotice => Get(nameof(Session_ManualCropRequiredNotice));

    // --- Epic 11200 Part C2: manual crop --------------------------------------------------

    /// <summary>Heading of the crop surface.</summary>
    internal static string Session_ManualCropHeading => Get(nameof(Session_ManualCropHeading));

    /// <summary>The button that opens the crop surface.</summary>
    internal static string Session_ManualCropBegin => Get(nameof(Session_ManualCropBegin));

    /// <summary>Tells the operator to drag on the image to draw the crop area.</summary>
    internal static string Session_ManualCropInstructions => Get(nameof(Session_ManualCropInstructions));

    /// <summary>The button that submits the drawn rectangle.</summary>
    internal static string Session_ManualCropApply => Get(nameof(Session_ManualCropApply));

    /// <summary>The button that leaves crop mode without changing anything.</summary>
    internal static string Session_ManualCropCancel => Get(nameof(Session_ManualCropCancel));

    /// <summary>Shown when a drag selected nothing that overlaps the artwork.</summary>
    internal static string Session_ManualCropInvalid => Get(nameof(Session_ManualCropInvalid));

    /// <summary>Composite format: left, top, width, height — all in source image pixels.</summary>
    internal static string Session_ManualCropSelection => Get(nameof(Session_ManualCropSelection));

    /// <summary>Shown in place of the selection summary before anything has been drawn.</summary>
    internal static string Session_ManualCropNoSelection => Get(nameof(Session_ManualCropNoSelection));

    // --- Return to an earlier step (Epic 11200 Part C3 §3, §5) ---------------------------

    internal static string Session_ReturnHeading => Get(nameof(Session_ReturnHeading));

    internal static string Session_ReturnHint => Get(nameof(Session_ReturnHint));

    internal static string Session_ReturnTargetLabel => Get(nameof(Session_ReturnTargetLabel));

    internal static string Session_ReturnBegin => Get(nameof(Session_ReturnBegin));

    /// <summary>
    /// What the operator is asked to confirm before anything is invalidated (§5).
    /// </summary>
    /// <remarks>
    /// It says results become invalid and that history is kept, and it deliberately does not
    /// say anything about files — because nothing is deleted. <c>ReturnToStep</c> invalidates
    /// Revisions and PrintOutputs; the bytes stay on disk and every review decision stays
    /// queryable. A warning about deletion would be a warning about something that does not
    /// happen (§5, §6).
    /// </remarks>
    internal static string Session_ReturnConfirmQuestion => Get(nameof(Session_ReturnConfirmQuestion));

    internal static string Session_ReturnConfirm => Get(nameof(Session_ReturnConfirm));

    internal static string Session_ReturnCancel => Get(nameof(Session_ReturnCancel));

    // --- Deterministic trim margin (Epic 11200 Part C3 §9–§12, §18) ----------------------

    internal static string Session_TrimHeading => Get(nameof(Session_TrimHeading));

    internal static string Session_TrimHint => Get(nameof(Session_TrimHint));

    internal static string Session_TrimModeTight => Get(nameof(Session_TrimModeTight));

    internal static string Session_TrimModeUniform => Get(nameof(Session_TrimModeUniform));

    internal static string Session_TrimModeEdgeSpecific => Get(nameof(Session_TrimModeEdgeSpecific));

    internal static string Session_TrimMarginLabel => Get(nameof(Session_TrimMarginLabel));

    internal static string Session_TrimTopLabel => Get(nameof(Session_TrimTopLabel));

    internal static string Session_TrimRightLabel => Get(nameof(Session_TrimRightLabel));

    internal static string Session_TrimBottomLabel => Get(nameof(Session_TrimBottomLabel));

    internal static string Session_TrimLeftLabel => Get(nameof(Session_TrimLeftLabel));

    internal static string Session_TrimApply => Get(nameof(Session_TrimApply));

    /// <summary>Shown when the typed pixels are not a whole non-negative number (§11).</summary>
    internal static string Session_TrimMarginInvalid => Get(nameof(Session_TrimMarginInvalid));

    internal static string Session_TrimCurrentLabel => Get(nameof(Session_TrimCurrentLabel));

    internal static string Session_TrimSummaryTight => Get(nameof(Session_TrimSummaryTight));

    /// <summary>Composite format: the single margin in pixels.</summary>
    internal static string Session_TrimSummaryUniform => Get(nameof(Session_TrimSummaryUniform));

    /// <summary>Composite format: top, right, bottom, left — all in pixels.</summary>
    internal static string Session_TrimSummaryEdges => Get(nameof(Session_TrimSummaryEdges));

    // --- Background removal authority (Epic 11300 Part C2B2 §3, §9, §11, §14) -----------

    internal static string Session_BackgroundRemovalHeading => Get(nameof(Session_BackgroundRemovalHeading));

    internal static string Session_BackgroundRemovalHint => Get(nameof(Session_BackgroundRemovalHint));

    internal static string Session_BackgroundRemovalAuthorise => Get(nameof(Session_BackgroundRemovalAuthorise));

    /// <summary>Scoped to the displayed image, and claiming nothing about cutout quality (§11).</summary>
    internal static string Session_BackgroundRemovalConfirmQuestion =>
        Get(nameof(Session_BackgroundRemovalConfirmQuestion));

    internal static string Session_BackgroundRemovalConfirm => Get(nameof(Session_BackgroundRemovalConfirm));

    internal static string Session_BackgroundRemovalCancel => Get(nameof(Session_BackgroundRemovalCancel));

    /// <summary>Composite format: the short form of the authorised Revision (§9).</summary>
    internal static string Session_BackgroundRemovalAuthorised => Get(nameof(Session_BackgroundRemovalAuthorised));

    internal static string Session_BackgroundRemovalNotAuthorised =>
        Get(nameof(Session_BackgroundRemovalNotAuthorised));

    internal static string Session_BackgroundRemovalRunnable => Get(nameof(Session_BackgroundRemovalRunnable));

    /// <summary>Composite format: the short form of the Revision the attempt was authorised over (§14).</summary>
    internal static string Session_BackgroundRemovalAttemptAudit =>
        Get(nameof(Session_BackgroundRemovalAttemptAudit));

    /// <summary>
    /// Returns the resource for <paramref name="key"/>, falling back to the key itself.
    /// </summary>
    /// <remarks>
    /// A missing string is a translation gap, not a reason to fail startup, so the key is
    /// shown instead — visible in the UI and therefore hard to leave unfixed.
    /// </remarks>
    private static string Get(string key) =>
        Manager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
}
