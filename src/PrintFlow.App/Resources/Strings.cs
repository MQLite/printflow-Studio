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
    internal static string Session_CompletionCleanupPending => Get(nameof(Session_CompletionCleanupPending));
    internal static string Session_KeepOriginalExtent => Get(nameof(Session_KeepOriginalExtent));
    internal static string Session_KeepOriginalExtentHint => Get(nameof(Session_KeepOriginalExtentHint));
    internal static string Session_OriginalExtentRetained => Get(nameof(Session_OriginalExtentRetained));
    internal static string Session_ReviewModeSideBySide => Get(nameof(Session_ReviewModeSideBySide));
    internal static string Session_ReviewModeSlider => Get(nameof(Session_ReviewModeSlider));
    internal static string Session_ReviewBackgroundCheckerboard => Get(nameof(Session_ReviewBackgroundCheckerboard));
    internal static string Session_ReviewBackgroundWhite => Get(nameof(Session_ReviewBackgroundWhite));
    internal static string Session_ReviewBackgroundBlack => Get(nameof(Session_ReviewBackgroundBlack));
    internal static string Session_ReviewComparisonSlider => Get(nameof(Session_ReviewComparisonSlider));
    internal static string Session_ManualBackgroundRemovalAudit => Get(nameof(Session_ManualBackgroundRemovalAudit));
    internal static string Failure_ManualResultCanvas => Get(nameof(Failure_ManualResultCanvas));
    internal static string Session_SubmitManualResult => Get(nameof(Session_SubmitManualResult));
    internal static string Session_ManualProcessingResult => Get(nameof(Session_ManualProcessingResult));
    internal static string Session_ManualCutoutFilter => Get(nameof(Session_ManualCutoutFilter));
    internal static string Session_ManualEnhancementFilter => Get(nameof(Session_ManualEnhancementFilter));
    internal static string Failure_ManualResultInvalid => Get(nameof(Failure_ManualResultInvalid));
    internal static string Failure_ManualResultImport => Get(nameof(Failure_ManualResultImport));
    internal static string Failure_ManualResultTransparency => Get(nameof(Failure_ManualResultTransparency));

    internal static string Failure_PdfUnreadable => Get(nameof(Failure_PdfUnreadable));

internal static string Failure_PdfEncrypted => Get(nameof(Failure_PdfEncrypted));

internal static string Failure_PdfMultiplePages => Get(nameof(Failure_PdfMultiplePages));

internal static string Failure_PdfPreparationFailed => Get(nameof(Failure_PdfPreparationFailed));

internal static string Session_PreparePdf => Get(nameof(Session_PreparePdf));

internal static string Session_PdfPending => Get(nameof(Session_PdfPending));

internal static string Session_PdfPrepared => Get(nameof(Session_PdfPrepared));
    internal static string Session_PreparePsd => Get(nameof(Session_PreparePsd));
    internal static string Session_PsdPending => Get(nameof(Session_PsdPending));
    internal static string Session_PsdPrepared => Get(nameof(Session_PsdPrepared));
    internal static string Failure_PsdUnsupported => Get(nameof(Failure_PsdUnsupported));
    internal static string Failure_PsdCompositeMissing => Get(nameof(Failure_PsdCompositeMissing));
    internal static string Failure_PsdUnreadable => Get(nameof(Failure_PsdUnreadable));
    internal static string Failure_PsdPreparationFailed => Get(nameof(Failure_PsdPreparationFailed));

    // --- Production TIFF final review (SCRUM-11104) ---------------------------------------

    internal static string Session_TiffModeHeading => Get(nameof(Session_TiffModeHeading));
    internal static string Session_TiffModeColour => Get(nameof(Session_TiffModeColour));
    internal static string Session_TiffModeWhiteInk => Get(nameof(Session_TiffModeWhiteInk));
    internal static string Session_TiffModeOverlay => Get(nameof(Session_TiffModeOverlay));
    internal static string Session_TiffColourPreviewName => Get(nameof(Session_TiffColourPreviewName));
    internal static string Session_TiffWhiteInkPreviewName => Get(nameof(Session_TiffWhiteInkPreviewName));
    internal static string Session_TiffOverlayPreviewName => Get(nameof(Session_TiffOverlayPreviewName));
    internal static string Session_TiffColourLegend => Get(nameof(Session_TiffColourLegend));
    internal static string Session_TiffWhiteInkLegend => Get(nameof(Session_TiffWhiteInkLegend));
    internal static string Session_TiffOverlayLegend => Get(nameof(Session_TiffOverlayLegend));
    internal static string Session_TiffProductionHeading => Get(nameof(Session_TiffProductionHeading));
    internal static string Session_TiffLabelOutputFile => Get(nameof(Session_TiffLabelOutputFile));
    internal static string Session_TiffLabelOutputPath => Get(nameof(Session_TiffLabelOutputPath));
    internal static string Session_TiffLabelPixelDimensions => Get(nameof(Session_TiffLabelPixelDimensions));
    internal static string Session_TiffLabelPhysicalSize => Get(nameof(Session_TiffLabelPhysicalSize));
    internal static string Session_TiffLabelResolution => Get(nameof(Session_TiffLabelResolution));
    internal static string Session_TiffLabelEffectiveDpi => Get(nameof(Session_TiffLabelEffectiveDpi));
    internal static string Session_TiffLabelColourMode => Get(nameof(Session_TiffLabelColourMode));
    internal static string Session_TiffLabelWhiteInk => Get(nameof(Session_TiffLabelWhiteInk));
    internal static string Session_TiffLabelEnlargement => Get(nameof(Session_TiffLabelEnlargement));
    internal static string Session_TiffLabelHash => Get(nameof(Session_TiffLabelHash));
    internal static string Session_TiffLabelPreset => Get(nameof(Session_TiffLabelPreset));
    internal static string Session_TiffValuePreset => Get(nameof(Session_TiffValuePreset));
    internal static string Session_TiffValuePixels => Get(nameof(Session_TiffValuePixels));
    internal static string Session_TiffValuePhysical => Get(nameof(Session_TiffValuePhysical));
    internal static string Session_TiffValueResolution => Get(nameof(Session_TiffValueResolution));
    internal static string Session_TiffValueEffectiveDpi => Get(nameof(Session_TiffValueEffectiveDpi));
    internal static string Session_TiffValueBelowProduction => Get(nameof(Session_TiffValueBelowProduction));
    internal static string Session_TiffValueColourMode => Get(nameof(Session_TiffValueColourMode));
    internal static string Session_TiffValueWhiteInk => Get(nameof(Session_TiffValueWhiteInk));
    internal static string Session_TiffValueEnlargementAuthorised => Get(nameof(Session_TiffValueEnlargementAuthorised));
    internal static string Session_TiffValueEnlargementUnauthorised => Get(nameof(Session_TiffValueEnlargementUnauthorised));
    internal static string Session_TiffPreviewUnavailable => Get(nameof(Session_TiffPreviewUnavailable));
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

    // --- Stop and Take Over (Epic 11300 Part D2A §37) -------------------------------------

    internal static string Session_Stop => Get(nameof(Session_Stop));

    internal static string Session_StopHint => Get(nameof(Session_StopHint));

    internal static string Session_StoppingNotice => Get(nameof(Session_StoppingNotice));

    internal static string Session_TakeOver => Get(nameof(Session_TakeOver));

    internal static string Session_TakeOverHint => Get(nameof(Session_TakeOverHint));

    internal static string Session_TakingOverNotice => Get(nameof(Session_TakingOverNotice));

    internal static string Session_TakeOverConfirmQuestion => Get(nameof(Session_TakeOverConfirmQuestion));

    internal static string Session_TakeOverConfirm => Get(nameof(Session_TakeOverConfirm));

    internal static string Session_TakeOverCancel => Get(nameof(Session_TakeOverCancel));

    internal static string Session_RetainedOperationRunning => Get(nameof(Session_RetainedOperationRunning));

    internal static string Session_RetainedProcessedResult => Get(nameof(Session_RetainedProcessedResult));

    internal static string Session_RetainedUnknown => Get(nameof(Session_RetainedUnknown));

    internal static string Session_ReenterAutomation => Get(nameof(Session_ReenterAutomation));

    internal static string Session_ReenterAutomationHint => Get(nameof(Session_ReenterAutomationHint));

    internal static string Failure_AutomationStopped => Get(nameof(Failure_AutomationStopped));

    internal static string Failure_AutomationHandedOff => Get(nameof(Failure_AutomationHandedOff));

    internal static string Session_ReviewHeading => Get(nameof(Session_ReviewHeading));

    internal static string Session_TiffReviewHeading => Get(nameof(Session_TiffReviewHeading));

    internal static string Session_TiffReviewSummary => Get(nameof(Session_TiffReviewSummary));

    internal static string Session_TiffReviewCaveat => Get(nameof(Session_TiffReviewCaveat));

    internal static string Session_ApproveTiff => Get(nameof(Session_ApproveTiff));

    internal static string Session_RejectTiff => Get(nameof(Session_RejectTiff));

    internal static string OutputLocation_Working => Get(nameof(OutputLocation_Working));

    internal static string OutputLocation_Approved => Get(nameof(OutputLocation_Approved));

    internal static string OutputLocation_Recycled => Get(nameof(OutputLocation_Recycled));

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

    // The wording is maximum-bound throughout (Epic 11400 Part B1A.2B §4): the two millimetre
    // boxes are limits the image is fitted inside, not two exact output dimensions. Nothing here
    // names an axis to choose or a resampling method — neither is an operator decision (§9).

    internal static string Session_MaxBoundsHeading => Get(nameof(Session_MaxBoundsHeading));

    internal static string Session_MaxBoundsHint => Get(nameof(Session_MaxBoundsHint));

    internal static string Session_LabelMaxWidthMm => Get(nameof(Session_LabelMaxWidthMm));

    internal static string Session_LabelMaxHeightMm => Get(nameof(Session_LabelMaxHeightMm));

    internal static string Session_LabelMaxLongEdgeMm => Get(nameof(Session_LabelMaxLongEdgeMm));

    internal static string Session_MaxBoundsConfirm => Get(nameof(Session_MaxBoundsConfirm));

    /// <summary>Shown when the typed millimetres are not limits the domain will accept.</summary>
    internal static string Session_MaxBoundsInvalid => Get(nameof(Session_MaxBoundsInvalid));

    /// <summary>Composite format: maximum width mm, maximum height mm.</summary>
    internal static string Session_MaxBoundsSummary => Get(nameof(Session_MaxBoundsSummary));

    /// <summary>Composite format: the one long-edge limit, in millimetres.</summary>
    internal static string Session_MaxLongEdgeSummary => Get(nameof(Session_MaxLongEdgeSummary));

    internal static string Session_MaxShortEdgeSummary => Get(nameof(Session_MaxShortEdgeSummary));

    /// <summary>Composite format: width mm, height mm, pixel width, pixel height, DPI.</summary>
    internal static string Session_DimensionsSummary => Get(nameof(Session_DimensionsSummary));

    internal static string Session_DimensionsNotSet => Get(nameof(Session_DimensionsNotSet));

    // --- Part B1A.2B: the projected preparation plan and the review it may need ------------

    internal static string Session_PreparationHeading => Get(nameof(Session_PreparationHeading));

    internal static string Session_PreparationResolutionOnly =>
        Get(nameof(Session_PreparationResolutionOnly));

    internal static string Session_PreparationProportionalShrink =>
        Get(nameof(Session_PreparationProportionalShrink));

    /// <summary>Composite format: the localised limiting edge.</summary>
    internal static string Session_PreparationLimitingEdge =>
        Get(nameof(Session_PreparationLimitingEdge));

    /// <summary>Composite format: projected pixel width, projected pixel height.</summary>
    internal static string Session_PreparationProjectedSize =>
        Get(nameof(Session_PreparationProjectedSize));

    /// <summary>Composite format: the fixed production resolution.</summary>
    internal static string Session_PreparationResolution => Get(nameof(Session_PreparationResolution));

    internal static string Session_PreparationAttemptHeading =>
        Get(nameof(Session_PreparationAttemptHeading));

    /// <summary>Composite format: maximum width mm, maximum height mm.</summary>
    internal static string Session_PreparationAttemptBounds =>
        Get(nameof(Session_PreparationAttemptBounds));

    /// <summary>Composite format: projected pixel width, projected pixel height, PPI.</summary>
    internal static string Session_PreparationAttemptProjected =>
        Get(nameof(Session_PreparationAttemptProjected));

    /// <summary>Says plainly that no Photoshop run produced these figures (§19, §21).</summary>
    internal static string Session_PreparationAttemptProjectionNotice =>
        Get(nameof(Session_PreparationAttemptProjectionNotice));

    internal static string Session_RunReady => Get(nameof(Session_RunReady));

    internal static string Session_RunNotReady => Get(nameof(Session_RunNotReady));

    /// <summary>The fail-closed warning over dimensions that cannot be executed (§11).</summary>
    internal static string Session_DimensionReviewRequired =>
        Get(nameof(Session_DimensionReviewRequired));

    internal static string Session_ReviewMaximumBounds => Get(nameof(Session_ReviewMaximumBounds));

    /// <summary>Labels retained millimetres as history rather than as active limits (§15).</summary>
    internal static string Session_HistoricalBoundsLabel => Get(nameof(Session_HistoricalBoundsLabel));

    internal static string PreparationMode_ResolutionOnly => Get(nameof(PreparationMode_ResolutionOnly));

    internal static string PreparationMode_ProportionalShrink =>
        Get(nameof(PreparationMode_ProportionalShrink));

    internal static string LimitingEdge_None => Get(nameof(LimitingEdge_None));

    internal static string LimitingEdge_Width => Get(nameof(LimitingEdge_Width));

    internal static string LimitingEdge_Height => Get(nameof(LimitingEdge_Height));

    internal static string Session_PresetsLabel => Get(nameof(Session_PresetsLabel));

    internal static string Session_PresetHint => Get(nameof(Session_PresetHint));

    internal static string Session_SizeHeading => Get(nameof(Session_SizeHeading));
    internal static string Session_UsePreset => Get(nameof(Session_UsePreset));
    internal static string Session_CustomSize => Get(nameof(Session_CustomSize));
    internal static string Session_AdjustSize => Get(nameof(Session_AdjustSize));
    internal static string Session_RecommendedMaximum => Get(nameof(Session_RecommendedMaximum));
    internal static string Session_RecommendedLongEdge => Get(nameof(Session_RecommendedLongEdge));

    internal static string Session_RecommendedShortEdge => Get(nameof(Session_RecommendedShortEdge));
    internal static string Session_TargetEdge => Get(nameof(Session_TargetEdge));
    internal static string Session_TargetSizeMm => Get(nameof(Session_TargetSizeMm));
    internal static string Session_TargetSizeInvalid => Get(nameof(Session_TargetSizeInvalid));
    internal static string Session_ConfirmCustomSize => Get(nameof(Session_ConfirmCustomSize));
    internal static string TargetEdge_Width => Get(nameof(TargetEdge_Width));
    internal static string TargetEdge_Height => Get(nameof(TargetEdge_Height));
    internal static string TargetEdge_LongEdge => Get(nameof(TargetEdge_LongEdge));
    internal static string Session_BasedOnPreset => Get(nameof(Session_BasedOnPreset));
    internal static string Session_PresetExceeded => Get(nameof(Session_PresetExceeded));
    internal static string Session_CustomResolutionOnly => Get(nameof(Session_CustomResolutionOnly));
    internal static string Session_CustomShrink => Get(nameof(Session_CustomShrink));
    internal static string Session_EnlargementWarning => Get(nameof(Session_EnlargementWarning));
    internal static string Session_ChangeSize => Get(nameof(Session_ChangeSize));
    internal static string Session_ContinueWithSize => Get(nameof(Session_ContinueWithSize));
    internal static string Session_EnlargementConfirmed => Get(nameof(Session_EnlargementConfirmed));
    internal static string Session_CurrentPreset => Get(nameof(Session_CurrentPreset));
    internal static string Session_CurrentCustomTarget => Get(nameof(Session_CurrentCustomTarget));
    internal static string Session_PresetOverrideYes => Get(nameof(Session_PresetOverrideYes));
    internal static string Session_ProjectedResize => Get(nameof(Session_ProjectedResize));
    internal static string ResizeDirection_ResolutionOnly => Get(nameof(ResizeDirection_ResolutionOnly));
    internal static string ResizeDirection_Shrink => Get(nameof(ResizeDirection_Shrink));
    internal static string ResizeDirection_Enlarge => Get(nameof(ResizeDirection_Enlarge));
    internal static string Session_ProportionalFit => Get(nameof(Session_ProportionalFit));
    internal static string Session_EnlargementExplicitlyConfirmed => Get(nameof(Session_EnlargementExplicitlyConfirmed));
    internal static string Session_ProjectedPlan => Get(nameof(Session_ProjectedPlan));
    internal static string Session_PhotoshopNotRun => Get(nameof(Session_PhotoshopNotRun));

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

    /// <summary>
    /// What an operator is told when a producing step ended with an unhandled fault
    /// (Epic 11600 Part A §9).
    /// </summary>
    /// <remarks>
    /// Separate wording from <see cref="Failure_AdapterUnavailable"/>, which the fault shares a
    /// <c>FailureCode</c> with. "The required application is unavailable or already in use" is a
    /// statement about the environment and would send the operator to check an installation
    /// that is perfectly fine. What actually happened is that PrintFlow's own run broke, and the
    /// operator's next move is to look at what Photoshop or Meitu is showing.
    /// </remarks>
    internal static string Failure_OperationFaulted => Get(nameof(Failure_OperationFaulted));

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

    internal static string Failure_MeituClosed => Get(nameof(Failure_MeituClosed));

    internal static string Failure_MeituInterrupted => Get(nameof(Failure_MeituInterrupted));

    internal static string Failure_PhotoshopNotInstalled => Get(nameof(Failure_PhotoshopNotInstalled));

    internal static string Failure_PhotoshopLaunchFailed => Get(nameof(Failure_PhotoshopLaunchFailed));

    internal static string Failure_PhotoshopWindowNotFound => Get(nameof(Failure_PhotoshopWindowNotFound));

    internal static string Failure_PhotoshopTargetLost => Get(nameof(Failure_PhotoshopTargetLost));

    internal static string Failure_PhotoshopUnknownState => Get(nameof(Failure_PhotoshopUnknownState));

    internal static string Failure_PhotoshopBlockingDialog => Get(nameof(Failure_PhotoshopBlockingDialog));

    internal static string Failure_PhotoshopOpenInputFailed => Get(nameof(Failure_PhotoshopOpenInputFailed));

    internal static string Failure_PhotoshopDocumentIdentityUnconfirmed =>
        Get(nameof(Failure_PhotoshopDocumentIdentityUnconfirmed));

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

    internal static string Session_TrimBoundsDetectedHeading => Get(nameof(Session_TrimBoundsDetectedHeading));

    internal static string Session_TrimBoundsAppliedHeading => Get(nameof(Session_TrimBoundsAppliedHeading));

    internal static string Session_TrimBoundsOrigin => Get(nameof(Session_TrimBoundsOrigin));

    internal static string Session_TrimBoundsExtent => Get(nameof(Session_TrimBoundsExtent));

    internal static string Session_TrimBoundsSize => Get(nameof(Session_TrimBoundsSize));

    internal static string Session_TrimBoundsCaption => Get(nameof(Session_TrimBoundsCaption));

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

    // --- Production readiness diagnostics (Epic 11500 Part C §3, §5, §8) ----------------

    internal static string Environment_Heading => Get(nameof(Environment_Heading));

    internal static string Environment_Hint => Get(nameof(Environment_Hint));

    /// <summary>The Home entry point onto the readiness screen.</summary>
    internal static string Environment_Open => Get(nameof(Environment_Open));

    /// <summary>Re-observes the dynamic workstation facts. It never re-reads a cached file (§8).</summary>
    internal static string Environment_Refresh => Get(nameof(Environment_Refresh));

    internal static string Environment_Preset => Get(nameof(Environment_Preset));

    internal static string Environment_PresetUnavailable => Get(nameof(Environment_PresetUnavailable));

    internal static string Environment_ChecksHeading => Get(nameof(Environment_ChecksHeading));

    internal static string Environment_BlockingHeading => Get(nameof(Environment_BlockingHeading));

    internal static string Environment_NoBlockingFailures => Get(nameof(Environment_NoBlockingFailures));

    /// <summary>Shown beside a Ready state, so an advisory reads as a note and not as a refusal (§5).</summary>
    internal static string Environment_AdvisoriesPresent => Get(nameof(Environment_AdvisoriesPresent));

    internal static string Environment_AdvisoriesNone => Get(nameof(Environment_AdvisoriesNone));

    internal static string Environment_StatusPassed => Get(nameof(Environment_StatusPassed));

    internal static string Environment_StatusFailed => Get(nameof(Environment_StatusFailed));

    internal static string Environment_StatusAdvisory => Get(nameof(Environment_StatusAdvisory));

    internal static string Environment_Blocking => Get(nameof(Environment_Blocking));

    internal static string Environment_Advisory => Get(nameof(Environment_Advisory));

    /// <summary>The restart boundary the retained trust model obliges the shell to state (§8).</summary>
    internal static string Environment_RestartRequired => Get(nameof(Environment_RestartRequired));

    /// <summary>What Check again actually re-reads, so it never implies more than it does (§8).</summary>
    internal static string Environment_RefreshScope => Get(nameof(Environment_RefreshScope));

    internal static string Environment_Verified => Get(nameof(Environment_Verified));

    internal static string Environment_NotVerified => Get(nameof(Environment_NotVerified));

    internal static string Environment_Advisories => Get(nameof(Environment_Advisories));

    internal static string Environment_ObservedAt => Get(nameof(Environment_ObservedAt));

    /// <summary>
    /// Returns the resource for <paramref name="key"/>, falling back to the key itself.
    /// </summary>
    /// <remarks>
    /// A missing string is a translation gap, not a reason to fail startup, so the key is
    /// shown instead — visible in the UI and therefore hard to leave unfixed.
    /// </remarks>
    internal static string Resolve(string key) => Get(key);

    internal static string Session_ManualCropAdjustment => Get(nameof(Session_ManualCropAdjustment));
    internal static string Session_ManualCropTight => Get(nameof(Session_ManualCropTight));
    internal static string Session_ManualCropUniform => Get(nameof(Session_ManualCropUniform));
    internal static string Session_ManualCropPerEdge => Get(nameof(Session_ManualCropPerEdge));
    internal static string Session_ManualCropSelected => Get(nameof(Session_ManualCropSelected));
    internal static string Session_ManualCropApplied => Get(nameof(Session_ManualCropApplied));
    internal static string Session_ManualCropMarginInvalid => Get(nameof(Session_ManualCropMarginInvalid));

    private static string Get(string key) =>
        Manager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
}
