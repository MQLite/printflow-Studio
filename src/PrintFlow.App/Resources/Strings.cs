using System.Globalization;
using System.Resources;

namespace PrintFlow.App.Resources;

/// <summary>
/// Typed access to the operator-visible strings in <c>Strings.resx</c>.
/// </summary>
/// <remarks>
/// Resolution follows <see cref="OperatorCulture"/> and is read afresh on every access, which is
/// what makes the runtime language switch possible at all: the culture is set by
/// <c>ILocalisationService</c> — the one authority for which language the interface is in — and
/// every string on the visible screen answers the next read in the new language, with no restart
/// and no per-label rebinding (SCRUM-11119; Epic 11100 plan §16.3). Until a language has been
/// selected, <see cref="OperatorCulture"/> is <see cref="CultureInfo.CurrentUICulture"/>, so the
/// <c>zh-CN</c> satellite is still picked up automatically on a Chinese workstation.
///
/// Internal state names, failure codes and adapter identifiers deliberately stay outside
/// this file: they are stable English and are never localised (MVP design §13.4).
/// </remarks>
internal static class Strings
{
    internal static string Session_NextCrop => Get(nameof(Session_NextCrop));
    internal static string Session_StatusHandedOff => Get(nameof(Session_StatusHandedOff));
    internal static string Session_StatusCompleted => Get(nameof(Session_StatusCompleted));
    internal static string Session_StatusStopped => Get(nameof(Session_StatusStopped));
    internal static string Session_StatusProcessing => Get(nameof(Session_StatusProcessing));
    internal static string Session_StatusReview => Get(nameof(Session_StatusReview));
    internal static string Session_StatusInput => Get(nameof(Session_StatusInput));
    internal static string Home_RecentStatusUnknown => Get(nameof(Home_RecentStatusUnknown));
    internal static string Home_RecentSaveHistoryUnavailable => Get(nameof(Home_RecentSaveHistoryUnavailable));
    internal static string Home_RecentPngSavedPreviously => Get(nameof(Home_RecentPngSavedPreviously));
    internal static string Home_RecentTiffSavedPreviously => Get(nameof(Home_RecentTiffSavedPreviously));
    internal static string Session_NextCompleted => Get(nameof(Session_NextCompleted));
    internal static string Session_NextAbandoned => Get(nameof(Session_NextAbandoned));
    internal static string Session_NextProcessing => Get(nameof(Session_NextProcessing));
    internal static string Session_NextReview => Get(nameof(Session_NextReview));
    internal static string Session_NextRetry => Get(nameof(Session_NextRetry));
    internal static string Session_NextAction => Get(nameof(Session_NextAction));
    internal static string Session_NextStopped => Get(nameof(Session_NextStopped));
    internal static string Session_NextOriginal => Get(nameof(Session_NextOriginal));
    internal static string Session_NextWhiteInk => Get(nameof(Session_NextWhiteInk));
    internal static string Session_NextDimensions => Get(nameof(Session_NextDimensions));
    internal static string Session_NextBackground => Get(nameof(Session_NextBackground));
    internal static string Session_NextInput => Get(nameof(Session_NextInput));
    internal static string Session_PhotoshopRetainedNotice => Get(nameof(Session_PhotoshopRetainedNotice));
    internal static string Session_PhotoshopTakeOver => Get(nameof(Session_PhotoshopTakeOver));
    internal static string Session_PhotoshopStopHint => Get(nameof(Session_PhotoshopStopHint));
    internal static string Session_PhotoshopTakeOverConfirmQuestion => Get(nameof(Session_PhotoshopTakeOverConfirmQuestion));
    internal static string Session_PhotoshopStoppingNotice => Get(nameof(Session_PhotoshopStoppingNotice));
    internal static string Session_PhotoshopTakingOverNotice => Get(nameof(Session_PhotoshopTakingOverNotice));
    internal static string Session_PhotoshopHandedOffNotice => Get(nameof(Session_PhotoshopHandedOffNotice));
    internal static string Session_PhotoshopReturnGuidance => Get(nameof(Session_PhotoshopReturnGuidance));
    internal static string Session_PreflightHeading => Get(nameof(Session_PreflightHeading));
    internal static string Session_PreflightSourcePixels => Get(nameof(Session_PreflightSourcePixels));
    internal static string Session_PreflightArtworkContent => Get(nameof(Session_PreflightArtworkContent));
    internal static string Session_PreflightSelectedArtwork => Get(nameof(Session_PreflightSelectedArtwork));
    internal static string Session_PreflightFinalCanvas => Get(nameof(Session_PreflightFinalCanvas));
    internal static string Session_PreflightGraphicBounds => Get(nameof(Session_PreflightGraphicBounds));
    internal static string Session_PreflightFullOriginalCanvas => Get(nameof(Session_PreflightFullOriginalCanvas));
    internal static string Session_PreflightNoCrop => Get(nameof(Session_PreflightNoCrop));
    internal static string Session_PreflightGeometryUnavailable => Get(nameof(Session_PreflightGeometryUnavailable));
    internal static string Session_PreflightPrintSize => Get(nameof(Session_PreflightPrintSize));
    internal static string Session_PreflightOutputPixels => Get(nameof(Session_PreflightOutputPixels));
    internal static string Session_PreflightOutputDpi => Get(nameof(Session_PreflightOutputDpi));
    internal static string Session_PreflightStatus => Get(nameof(Session_PreflightStatus));
    internal static string Session_PreflightNoEnlargement => Get(nameof(Session_PreflightNoEnlargement));
    internal static string Session_PreflightEnlargementRequired => Get(nameof(Session_PreflightEnlargementRequired));
    internal static string Session_PreflightEnlargementAuthorised => Get(nameof(Session_PreflightEnlargementAuthorised));
    internal static string Session_PreflightDraftHint => Get(nameof(Session_PreflightDraftHint));
    internal static string Session_PreflightPpi => Get(nameof(Session_PreflightPpi));
    internal static string Session_PreflightOutputPpi => Get(nameof(Session_PreflightOutputPpi));
    internal static string ErrorDetails_ExportDiagnosticPackage => Get(nameof(ErrorDetails_ExportDiagnosticPackage));
    internal static string DiagnosticPackage_Heading => Get(nameof(DiagnosticPackage_Heading));
    internal static string DiagnosticPackage_LocalOnlyNotice => Get(nameof(DiagnosticPackage_LocalOnlyNotice));
    internal static string DiagnosticPackage_Subject => Get(nameof(DiagnosticPackage_Subject));
    internal static string DiagnosticPackage_ProcessingName => Get(nameof(DiagnosticPackage_ProcessingName));
    internal static string DiagnosticPackage_FailureReference => Get(nameof(DiagnosticPackage_FailureReference));
    internal static string DiagnosticPackage_Included => Get(nameof(DiagnosticPackage_Included));
    internal static string DiagnosticPackage_Unavailable => Get(nameof(DiagnosticPackage_Unavailable));
    internal static string DiagnosticPackage_ExcludedByPolicy => Get(nameof(DiagnosticPackage_ExcludedByPolicy));
    internal static string DiagnosticPackage_LocalPaths => Get(nameof(DiagnosticPackage_LocalPaths));
    internal static string DiagnosticPackage_Destination => Get(nameof(DiagnosticPackage_Destination));
    internal static string DiagnosticPackage_DestinationHint => Get(nameof(DiagnosticPackage_DestinationHint));
    internal static string DiagnosticPackage_NoOverwrite => Get(nameof(DiagnosticPackage_NoOverwrite));
    internal static string DiagnosticPackage_Save => Get(nameof(DiagnosticPackage_Save));
    internal static string DiagnosticPackage_Back => Get(nameof(DiagnosticPackage_Back));
    internal static string DiagnosticPackage_SaveDialogTitle => Get(nameof(DiagnosticPackage_SaveDialogTitle));
    internal static string DiagnosticPackage_SaveDialogFilter => Get(nameof(DiagnosticPackage_SaveDialogFilter));
    internal static string DiagnosticPackage_PreviewFailed => Get(nameof(DiagnosticPackage_PreviewFailed));
    internal static string DiagnosticPackage_SaveFailed => Get(nameof(DiagnosticPackage_SaveFailed));
    internal static string DiagnosticPackage_Saved => Get(nameof(DiagnosticPackage_Saved));
    internal static string DiagnosticPackage_EnvironmentReady => Get(nameof(DiagnosticPackage_EnvironmentReady));
    internal static string DiagnosticPackage_EnvironmentNotReady => Get(nameof(DiagnosticPackage_EnvironmentNotReady));
    internal static string DiagnosticPackage_ItemManifest => Get(nameof(DiagnosticPackage_ItemManifest));
    internal static string DiagnosticPackage_ItemFailureSummary => Get(nameof(DiagnosticPackage_ItemFailureSummary));
    internal static string DiagnosticPackage_ItemAutomationLog => Get(nameof(DiagnosticPackage_ItemAutomationLog));
    internal static string DiagnosticPackage_ItemLogUnavailable => Get(nameof(DiagnosticPackage_ItemLogUnavailable));
    internal static string DiagnosticPackage_ItemLogNotRecorded => Get(nameof(DiagnosticPackage_ItemLogNotRecorded));
    internal static string DiagnosticPackage_ItemEnvironment => Get(nameof(DiagnosticPackage_ItemEnvironment));
    internal static string DiagnosticPackage_ItemApplication => Get(nameof(DiagnosticPackage_ItemApplication));
    internal static string DiagnosticPackage_ItemLocalPaths => Get(nameof(DiagnosticPackage_ItemLocalPaths));
    internal static string DiagnosticPackage_ItemFailureScreenshot => Get(nameof(DiagnosticPackage_ItemFailureScreenshot));
    internal static string DiagnosticPackage_ItemScreenshotUnavailable => Get(nameof(DiagnosticPackage_ItemScreenshotUnavailable));
    internal static string DiagnosticPackage_ItemScreenshotExcluded => Get(nameof(DiagnosticPackage_ItemScreenshotExcluded));
    internal static string DiagnosticPackage_ItemCustomerSource => Get(nameof(DiagnosticPackage_ItemCustomerSource));
    internal static string DiagnosticPackage_ItemInputSnapshot => Get(nameof(DiagnosticPackage_ItemInputSnapshot));
    internal static string DiagnosticPackage_ItemRevisionArtwork => Get(nameof(DiagnosticPackage_ItemRevisionArtwork));
    internal static string DiagnosticPackage_ItemApprovedOutput => Get(nameof(DiagnosticPackage_ItemApprovedOutput));
    internal static string DiagnosticPackage_ItemProductionOutput => Get(nameof(DiagnosticPackage_ItemProductionOutput));
    internal static string DiagnosticPackage_ItemManualArtwork => Get(nameof(DiagnosticPackage_ItemManualArtwork));
    internal static string DiagnosticPackage_ItemRecoveryEvidence => Get(nameof(DiagnosticPackage_ItemRecoveryEvidence));
    internal static string DiagnosticPackage_ItemUnknownEvidence => Get(nameof(DiagnosticPackage_ItemUnknownEvidence));
    internal static string DiagnosticPackage_ItemDiagnosticDatabase => Get(nameof(DiagnosticPackage_ItemDiagnosticDatabase));
    internal static string DiagnosticPackage_PathManagedInput => Get(nameof(DiagnosticPackage_PathManagedInput));
    internal static string DiagnosticPackage_PathExpectedOutput => Get(nameof(DiagnosticPackage_PathExpectedOutput));
    internal static string DiagnosticPackage_PathScreenshot => Get(nameof(DiagnosticPackage_PathScreenshot));
    internal static string DiagnosticPackage_PathLocalLog => Get(nameof(DiagnosticPackage_PathLocalLog));
    internal static string DiagnosticPackage_PathScreenshotFolder => Get(nameof(DiagnosticPackage_PathScreenshotFolder));
    internal static string ErrorDetails_ManualProcessing => Get(nameof(ErrorDetails_ManualProcessing));
    internal static string ErrorDetails_Heading => Get(nameof(ErrorDetails_Heading));
    internal static string ErrorDetails_WhatHappened => Get(nameof(ErrorDetails_WhatHappened));
    internal static string ErrorDetails_Processing => Get(nameof(ErrorDetails_Processing));
    internal static string ErrorDetails_Workflow => Get(nameof(ErrorDetails_Workflow));
    internal static string ErrorDetails_Step => Get(nameof(ErrorDetails_Step));
    internal static string ErrorDetails_Code => Get(nameof(ErrorDetails_Code));
    internal static string ErrorDetails_CodeHint => Get(nameof(ErrorDetails_CodeHint));
    internal static string ErrorDetails_RetryInformation => Get(nameof(ErrorDetails_RetryInformation));
    internal static string ErrorDetails_InputPath => Get(nameof(ErrorDetails_InputPath));
    internal static string ErrorDetails_ExpectedOutputPath => Get(nameof(ErrorDetails_ExpectedOutputPath));
    internal static string ErrorDetails_Evidence => Get(nameof(ErrorDetails_Evidence));
    internal static string ErrorDetails_ScreenshotPath => Get(nameof(ErrorDetails_ScreenshotPath));
    internal static string ErrorDetails_TechnicalDetail => Get(nameof(ErrorDetails_TechnicalDetail));
    internal static string ErrorDetails_Back => Get(nameof(ErrorDetails_Back));
    internal static string ErrorDetails_NotEstablished => Get(nameof(ErrorDetails_NotEstablished));
    internal static string ErrorDetails_NotRecorded => Get(nameof(ErrorDetails_NotRecorded));
    internal static string ErrorDetails_PathUnavailable => Get(nameof(ErrorDetails_PathUnavailable));
    internal static string ErrorDetails_NotCaptured => Get(nameof(ErrorDetails_NotCaptured));
    internal static string ErrorDetails_ImageUnavailable => Get(nameof(ErrorDetails_ImageUnavailable));
    internal static string ErrorDetails_Historical => Get(nameof(ErrorDetails_Historical));
    internal static string ErrorDetails_Interrupted => Get(nameof(ErrorDetails_Interrupted));
    internal static string ErrorDetails_Cancelled => Get(nameof(ErrorDetails_Cancelled));
    internal static string Home_RecoveryHeading => Get(nameof(Home_RecoveryHeading));
    internal static string Home_RecoveryPending => Get(nameof(Home_RecoveryPending));
    internal static string Home_RecoveryUnfinishedImport => Get(nameof(Home_RecoveryUnfinishedImport));
    internal static string Home_RecoveryDescription => Get(nameof(Home_RecoveryDescription));
    internal static string Home_RecoveryRestart => Get(nameof(Home_RecoveryRestart));
    internal static string Home_RecoveryManualResult => Get(nameof(Home_RecoveryManualResult));
    internal static string Home_RecoveryNoActions => Get(nameof(Home_RecoveryNoActions));
    internal static string Home_RecoveryFailed => Get(nameof(Home_RecoveryFailed));
    internal static string Home_RecoveryUnavailable => Get(nameof(Home_RecoveryUnavailable));
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
    internal static string Failure_SourceFormatUnsupported => Get(nameof(Failure_SourceFormatUnsupported));
    internal static string Failure_SourceImageUnreadable => Get(nameof(Failure_SourceImageUnreadable));

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

    /// <summary>Composite format: the refusal sentence, then the stable failure code.</summary>
    internal static string Home_ImportRefused => Get(nameof(Home_ImportRefused));

    /// <summary>How the accepted input formats are joined in one sentence.</summary>
    internal static string Home_ImportFormatSeparator => Get(nameof(Home_ImportFormatSeparator));

    /// <summary>Composite format: the detected format, then the accepted format list.</summary>
    internal static string Home_ImportUnsupportedFormat => Get(nameof(Home_ImportUnsupportedFormat));

    /// <summary>Composite format: the chosen file name, then the accepted format list.</summary>
    internal static string Home_ImportUnrecognisedFile => Get(nameof(Home_ImportUnrecognisedFile));

    /// <summary>Composite format: the chosen file name, then its detected format.</summary>
    internal static string Home_ImportUnreadableImage => Get(nameof(Home_ImportUnreadableImage));

    /// <summary>The label for taking a finished job's record off Recent Processing.</summary>
    internal static string Home_RemoveRecord => Get(nameof(Home_RemoveRecord));

    /// <summary>Composite format: the removed job's output name.</summary>
    internal static string Home_RemoveDone => Get(nameof(Home_RemoveDone));

    /// <summary>Composite format: the stable failure code.</summary>
    internal static string Home_RemoveFailed => Get(nameof(Home_RemoveFailed));

    /// <summary>The neutral state a row shows in place of a picture.</summary>
    internal static string Home_RecentNoThumbnail => Get(nameof(Home_RecentNoThumbnail));

    /// <summary>Composite format: the job's output name, as an accessible picture label.</summary>
    internal static string Home_RecentThumbnailOf => Get(nameof(Home_RecentThumbnailOf));

    /// <summary>Composite format: the stable failure code.</summary>
    internal static string Home_ResumeFailed => Get(nameof(Home_ResumeFailed));

    /// <summary>Composite format: the stable failure code.</summary>
    internal static string Home_AbandonFailed => Get(nameof(Home_AbandonFailed));

    /// <summary>Composite format: the abandoned session's output name.</summary>
    internal static string Home_AbandonDone => Get(nameof(Home_AbandonDone));

    /// <summary>Composite format: the stable failure code.</summary>
    internal static string Home_RecentUnavailable => Get(nameof(Home_RecentUnavailable));

    internal static string WorkflowSelection_Heading => Get(nameof(WorkflowSelection_Heading));

    internal static string WorkflowPurpose_Asset => Get(nameof(WorkflowPurpose_Asset));
    internal static string WorkflowPurpose_Customer => Get(nameof(WorkflowPurpose_Customer));
    internal static string WorkflowPurpose_Print => Get(nameof(WorkflowPurpose_Print));
    internal static string WorkflowResult_Png => Get(nameof(WorkflowResult_Png));
    internal static string WorkflowResult_Tiff => Get(nameof(WorkflowResult_Tiff));
    internal static string WorkflowDimensions_None => Get(nameof(WorkflowDimensions_None));
    internal static string WorkflowDimensions_Required => Get(nameof(WorkflowDimensions_Required));

    internal static string WorkflowSelection_Hint => Get(nameof(WorkflowSelection_Hint));

    internal static string WorkflowSelection_Select => Get(nameof(WorkflowSelection_Select));

    internal static string WorkflowSelection_Locked => Get(nameof(WorkflowSelection_Locked));

    /// <summary>Composite format: the stable failure code.</summary>
    internal static string WorkflowSelection_Refused => Get(nameof(WorkflowSelection_Refused));

    internal static string WorkflowSelection_SourceFileLabel => Get(nameof(WorkflowSelection_SourceFileLabel));

    internal static string WorkflowSelection_OutputNameLabel => Get(nameof(WorkflowSelection_OutputNameLabel));

    internal static string WorkflowSelection_OutputNameHint => Get(nameof(WorkflowSelection_OutputNameHint));

    /// <summary>Composite format: the forbidden characters, then the length cap.</summary>
    internal static string WorkflowSelection_OutputNameRejected => Get(nameof(WorkflowSelection_OutputNameRejected));

    /// <summary>Composite format: the stable failure code.</summary>
    internal static string WorkflowSelection_OutputNameRefused => Get(nameof(WorkflowSelection_OutputNameRefused));

    internal static string WorkflowSelection_PreviewHeading => Get(nameof(WorkflowSelection_PreviewHeading));

    internal static string WorkflowSelection_PreviewNeedsPreparation =>
        Get(nameof(WorkflowSelection_PreviewNeedsPreparation));

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

    /// <summary>Composite format: the failure's own sentence. The stable code sits under Error details (SCRUM-11151).</summary>
    internal static string Session_ActionFailed => Get(nameof(Session_ActionFailed));

    /// <summary>
    /// Composite format: the failure's own sentence, for a record that could not be read or saved.
    /// </summary>
    /// <remarks>
    /// A persistence failure does not prove that nothing was saved: an operation with more than one
    /// commit can fail after an earlier one landed. So this says the outcome is unconfirmed rather
    /// than that the action did not complete (SCRUM-11151).
    /// </remarks>
    internal static string Session_ActionUnconfirmed => Get(nameof(Session_ActionUnconfirmed));

    /// <summary>The help line after a failed action once the screen shows the job's current state.</summary>
    /// <remarks>
    /// It names no action of its own: the status line is built from the commands that are legal now,
    /// so Retry is named there only when Retry is offered (SCRUM-11151).
    /// </remarks>
    internal static string Session_ActionFailedNext => Get(nameof(Session_ActionFailedNext));

    /// <summary>The help line after a failed action when re-reading the job afterwards failed.</summary>
    internal static string Session_ActionFailedNextStale => Get(nameof(Session_ActionFailedNextStale));

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
    internal static string Session_TargetSizeUnavailable => Get(nameof(Session_TargetSizeUnavailable));
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

    internal static string Environment_RunLiveChecks => Get(nameof(Environment_RunLiveChecks));

    internal static string Environment_RunLiveChecksHint => Get(nameof(Environment_RunLiveChecksHint));

    internal static string Environment_Checking => Get(nameof(Environment_Checking));

    internal static string Environment_AutomaticChecksHeading => Get(nameof(Environment_AutomaticChecksHeading));

    internal static string Environment_LiveChecksHeading => Get(nameof(Environment_LiveChecksHeading));

    internal static string Environment_Expected => Get(nameof(Environment_Expected));

    internal static string Environment_Current => Get(nameof(Environment_Current));

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

    internal static string Environment_StatusBlocked => Get(nameof(Environment_StatusBlocked));

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

    // Settings and the operator language selection (SCRUM-11118, SCRUM-11119). The language
    // names themselves are deliberately absent: a language is always offered in its own
    // language, so "English" and the Chinese endonym are constants in the view model rather
    // than translated resources.

    internal static string Settings_Open => Get(nameof(Settings_Open));
    internal static string Settings_Heading => Get(nameof(Settings_Heading));
    internal static string Settings_GeneralHeading => Get(nameof(Settings_GeneralHeading));
    internal static string Settings_ProductionHeading => Get(nameof(Settings_ProductionHeading));
    internal static string Settings_ProductionHint => Get(nameof(Settings_ProductionHint));
    internal static string Settings_DiagnosticsHeading => Get(nameof(Settings_DiagnosticsHeading));
    internal static string Settings_Language => Get(nameof(Settings_Language));
    internal static string Settings_OutputRoot => Get(nameof(Settings_OutputRoot));
    internal static string Settings_OutputRootHint => Get(nameof(Settings_OutputRootHint));
    internal static string Settings_OutputRootUnavailable => Get(nameof(Settings_OutputRootUnavailable));
    internal static string Settings_TrimMargin => Get(nameof(Settings_TrimMargin));
    internal static string Settings_TrimMarginHint => Get(nameof(Settings_TrimMarginHint));
    internal static string Settings_TrimMarginInvalid => Get(nameof(Settings_TrimMarginInvalid));
    internal static string Settings_ProductionDpi => Get(nameof(Settings_ProductionDpi));
    internal static string Settings_ProductionDpiValue => Get(nameof(Settings_ProductionDpiValue));
    internal static string Settings_Preset => Get(nameof(Settings_Preset));
    internal static string Settings_ColourSetup => Get(nameof(Settings_ColourSetup));
    internal static string Settings_ColourSetupHint => Get(nameof(Settings_ColourSetupHint));
    internal static string Settings_ColourConfirmed => Get(nameof(Settings_ColourConfirmed));
    internal static string Settings_ColourMismatch => Get(nameof(Settings_ColourMismatch));
    internal static string Settings_ColourNotVerified => Get(nameof(Settings_ColourNotVerified));
    internal static string Settings_LogRetention => Get(nameof(Settings_LogRetention));
    internal static string Settings_LogRetentionHint => Get(nameof(Settings_LogRetentionHint));
    internal static string Settings_LocalLogLocation => Get(nameof(Settings_LocalLogLocation));
    internal static string Settings_LocalLogLocationHint => Get(nameof(Settings_LocalLogLocationHint));
    internal static string Settings_ScreenshotLocation => Get(nameof(Settings_ScreenshotLocation));
    internal static string Settings_ScreenshotLocationHint => Get(nameof(Settings_ScreenshotLocationHint));
    internal static string Startup_DiagnosticRetentionWarning => Get(nameof(Startup_DiagnosticRetentionWarning));
    internal static string Settings_LogRetentionInvalid => Get(nameof(Settings_LogRetentionInvalid));
    internal static string Settings_Apply => Get(nameof(Settings_Apply));
    internal static string Settings_Saved => Get(nameof(Settings_Saved));
    internal static string Settings_SaveFailed => Get(nameof(Settings_SaveFailed));
    internal static string Settings_LoadFailed => Get(nameof(Settings_LoadFailed));

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
    internal static string FinalSave_Heading => Get(nameof(FinalSave_Heading));
    internal static string FinalSave_TargetLabel => Get(nameof(FinalSave_TargetLabel));
    internal static string FinalSave_TargetPending => Get(nameof(FinalSave_TargetPending));
    internal static string FinalSave_TargetApproved => Get(nameof(FinalSave_TargetApproved));
    internal static string FinalSave_FileNameLabel => Get(nameof(FinalSave_FileNameLabel));
    internal static string FinalSave_FolderLabel => Get(nameof(FinalSave_FolderLabel));
    internal static string FinalSave_NoFolderValue => Get(nameof(FinalSave_NoFolderValue));
    internal static string FinalSave_ChangeLocation => Get(nameof(FinalSave_ChangeLocation));
    internal static string FinalSave_PickerTitle => Get(nameof(FinalSave_PickerTitle));
    internal static string FinalSave_NoFolder => Get(nameof(FinalSave_NoFolder));
    internal static string FinalSave_RememberedFolder => Get(nameof(FinalSave_RememberedFolder));
    internal static string FinalSave_PreferenceUnavailable => Get(nameof(FinalSave_PreferenceUnavailable));
    internal static string FinalSave_SaveAs => Get(nameof(FinalSave_SaveAs));
    internal static string FinalSave_TypePng => Get(nameof(FinalSave_TypePng));
    internal static string FinalSave_TypeTiff => Get(nameof(FinalSave_TypeTiff));
    internal static string FinalSave_TypeTiffSizePending => Get(nameof(FinalSave_TypeTiffSizePending));
    internal static string FinalSave_FolderValue => Get(nameof(FinalSave_FolderValue));
    internal static string FinalSave_NameExtensionAdded => Get(nameof(FinalSave_NameExtensionAdded));
    internal static string FinalSave_NameInvalid => Get(nameof(FinalSave_NameInvalid));
    internal static string FinalSave_NameWrongType => Get(nameof(FinalSave_NameWrongType));
    internal static string FinalSave_ConfirmAndSave => Get(nameof(FinalSave_ConfirmAndSave));
    internal static string FinalSave_SaveApproved => Get(nameof(FinalSave_SaveApproved));
    internal static string FinalSave_RetrySave => Get(nameof(FinalSave_RetrySave));
    internal static string FinalSave_CheckAgain => Get(nameof(FinalSave_CheckAgain));
    internal static string FinalSave_CheckSaved => Get(nameof(FinalSave_CheckSaved));
    internal static string FinalSave_OpenFolder => Get(nameof(FinalSave_OpenFolder));
    internal static string FinalSave_SaveAnotherCopy => Get(nameof(FinalSave_SaveAnotherCopy));
    internal static string FinalSave_UseSuggestion => Get(nameof(FinalSave_UseSuggestion));
    internal static string FinalSave_CancelSave => Get(nameof(FinalSave_CancelSave));
    internal static string FinalSave_AwaitingConfirmation => Get(nameof(FinalSave_AwaitingConfirmation));
    internal static string FinalSave_ApproveOnlyHint => Get(nameof(FinalSave_ApproveOnlyHint));
    internal static string FinalSave_StageChecking => Get(nameof(FinalSave_StageChecking));
    internal static string FinalSave_StageApproving => Get(nameof(FinalSave_StageApproving));
    internal static string FinalSave_StagePreparingPng => Get(nameof(FinalSave_StagePreparingPng));
    internal static string FinalSave_StageSaving => Get(nameof(FinalSave_StageSaving));
    internal static string FinalSave_StageFinishing => Get(nameof(FinalSave_StageFinishing));
    internal static string FinalSave_ApprovedNotSaved => Get(nameof(FinalSave_ApprovedNotSaved));
    internal static string FinalSave_ApprovedNotSavedReason => Get(nameof(FinalSave_ApprovedNotSavedReason));
    internal static string FinalSave_Saved => Get(nameof(FinalSave_Saved));
    internal static string FinalSave_SavedPreviously => Get(nameof(FinalSave_SavedPreviously));
    internal static string FinalSave_Uncertain => Get(nameof(FinalSave_Uncertain));
    internal static string FinalSave_Collision => Get(nameof(FinalSave_Collision));
    internal static string FinalSave_CollisionNoSuggestion => Get(nameof(FinalSave_CollisionNoSuggestion));
    internal static string FinalSave_Missing => Get(nameof(FinalSave_Missing));
    internal static string FinalSave_Changed => Get(nameof(FinalSave_Changed));
    internal static string FinalSave_Unavailable => Get(nameof(FinalSave_Unavailable));
    internal static string FinalSave_HistoryUnavailable => Get(nameof(FinalSave_HistoryUnavailable));
    internal static string FinalSave_PngPreparationPending => Get(nameof(FinalSave_PngPreparationPending));
    internal static string FinalSave_ApprovalRefused => Get(nameof(FinalSave_ApprovalRefused));
    internal static string FinalSave_ApprovalUnknown => Get(nameof(FinalSave_ApprovalUnknown));
    internal static string FinalSave_DraftRefused => Get(nameof(FinalSave_DraftRefused));
    internal static string FinalSave_Ineligible => Get(nameof(FinalSave_Ineligible));
    internal static string FinalSave_UncertainElsewhere => Get(nameof(FinalSave_UncertainElsewhere));
    internal static string FinalSave_ReasonApprovalEvidenceMissing => Get(nameof(FinalSave_ReasonApprovalEvidenceMissing));
    internal static string FinalSave_ReasonPending => Get(nameof(FinalSave_ReasonPending));
    internal static string FinalSave_ReasonRejected => Get(nameof(FinalSave_ReasonRejected));
    internal static string FinalSave_ReasonInvalidated => Get(nameof(FinalSave_ReasonInvalidated));
    internal static string FinalSave_ReasonRecycled => Get(nameof(FinalSave_ReasonRecycled));
    internal static string FinalSave_ReasonObsolete => Get(nameof(FinalSave_ReasonObsolete));
    internal static string FinalSave_ReasonInconsistent => Get(nameof(FinalSave_ReasonInconsistent));
    internal static string FinalSave_ReasonMissing => Get(nameof(FinalSave_ReasonMissing));
    internal static string FinalSave_ReasonSourceChanged => Get(nameof(FinalSave_ReasonSourceChanged));
    internal static string FinalSave_FailCancelled => Get(nameof(FinalSave_FailCancelled));
    internal static string FinalSave_FailNotWritable => Get(nameof(FinalSave_FailNotWritable));
    internal static string FinalSave_FailUnavailable => Get(nameof(FinalSave_FailUnavailable));
    internal static string FinalSave_FailUnsupported => Get(nameof(FinalSave_FailUnsupported));
    internal static string FinalSave_FailProtected => Get(nameof(FinalSave_FailProtected));
    internal static string FinalSave_FailCopy => Get(nameof(FinalSave_FailCopy));
    internal static string FinalSave_FailVerify => Get(nameof(FinalSave_FailVerify));
    internal static string FinalSave_FailPersistence => Get(nameof(FinalSave_FailPersistence));
    internal static string FinalSave_FailInvalidName => Get(nameof(FinalSave_FailInvalidName));
    internal static string FinalSave_FailConflict => Get(nameof(FinalSave_FailConflict));
    internal static string FinalSave_FailOther => Get(nameof(FinalSave_FailOther));
    internal static string FinalSave_OpenDispatched => Get(nameof(FinalSave_OpenDispatched));
    internal static string FinalSave_OpenMissing => Get(nameof(FinalSave_OpenMissing));
    internal static string FinalSave_OpenChanged => Get(nameof(FinalSave_OpenChanged));
    internal static string FinalSave_OpenUnavailable => Get(nameof(FinalSave_OpenUnavailable));
    internal static string FinalSave_OpenIneligible => Get(nameof(FinalSave_OpenIneligible));
    internal static string FinalSave_OpenUncertain => Get(nameof(FinalSave_OpenUncertain));
    internal static string FinalSave_OpenShellFailed => Get(nameof(FinalSave_OpenShellFailed));
    internal static string FinalSave_FullPathLabel => Get(nameof(FinalSave_FullPathLabel));
    internal static string FinalSave_DetailsLabel => Get(nameof(FinalSave_DetailsLabel));
    internal static string Session_NextFinalReview => Get(nameof(Session_NextFinalReview));
    internal static string Session_NextCompletedNotSaved => Get(nameof(Session_NextCompletedNotSaved));
    internal static string FinalSave_StageVerifyingSaved => Get(nameof(FinalSave_StageVerifyingSaved));
    internal static string FinalSave_StageApproved => Get(nameof(FinalSave_StageApproved));
    internal static string FinalSave_ActionIncomplete => Get(nameof(FinalSave_ActionIncomplete));
    internal static string FinalSave_HistoryLoading => Get(nameof(FinalSave_HistoryLoading));
    internal static string FinalSave_HistoryNotRefreshed => Get(nameof(FinalSave_HistoryNotRefreshed));
    internal static string FinalSave_RecordsLabel => Get(nameof(FinalSave_RecordsLabel));
    internal static string FinalSave_RecordSaved => Get(nameof(FinalSave_RecordSaved));
    internal static string FinalSave_RecordUnverified => Get(nameof(FinalSave_RecordUnverified));
    internal static string FinalSave_RecordNotSaved => Get(nameof(FinalSave_RecordNotSaved));
    internal static string Session_NextFinalReviewNeedsDraft => Get(nameof(Session_NextFinalReviewNeedsDraft));

    // --- SCRUM-11147 trim adjustment ---------------------------------------------------
    internal static string Session_TrimAdjustBegin => Get(nameof(Session_TrimAdjustBegin));
    internal static string Session_TrimAdjustHeading => Get(nameof(Session_TrimAdjustHeading));
    internal static string Session_TrimAdjustInstructions => Get(nameof(Session_TrimAdjustInstructions));
    internal static string Session_TrimAdjustPrintSizeLater => Get(nameof(Session_TrimAdjustPrintSizeLater));
    internal static string Session_TrimAdjustKeyboard => Get(nameof(Session_TrimAdjustKeyboard));
    internal static string Session_TrimAdjustUse => Get(nameof(Session_TrimAdjustUse));
    internal static string Session_TrimAdjustRestore => Get(nameof(Session_TrimAdjustRestore));
    internal static string Session_TrimAdjustNoSuggestion => Get(nameof(Session_TrimAdjustNoSuggestion));
    internal static string Session_TrimAdjustCancel => Get(nameof(Session_TrimAdjustCancel));
    internal static string Session_TrimAdjustAdjustView => Get(nameof(Session_TrimAdjustAdjustView));
    internal static string Session_TrimAdjustCompareView => Get(nameof(Session_TrimAdjustCompareView));
    internal static string Session_TrimAdjustCurrent => Get(nameof(Session_TrimAdjustCurrent));
    internal static string Session_TrimAdjustProposed => Get(nameof(Session_TrimAdjustProposed));
    internal static string Session_TrimAdjustKept => Get(nameof(Session_TrimAdjustKept));
    internal static string Session_TrimAdjustSuggestion => Get(nameof(Session_TrimAdjustSuggestion));
    internal static string Session_TrimAdjustInvalid => Get(nameof(Session_TrimAdjustInvalid));
    internal static string Session_TrimAdjustUnchanged => Get(nameof(Session_TrimAdjustUnchanged));
    internal static string Session_TrimAdjustBusy => Get(nameof(Session_TrimAdjustBusy));
    internal static string Session_TrimAdjustReady => Get(nameof(Session_TrimAdjustReady));
    internal static string Session_TrimAdjustSourceUnavailable => Get(nameof(Session_TrimAdjustSourceUnavailable));
    internal static string Session_TrimAdjustClosed => Get(nameof(Session_TrimAdjustClosed));
    internal static string Session_NextTrimAdjust => Get(nameof(Session_NextTrimAdjust));
    internal static string FinalSave_TrimAdjustOpen => Get(nameof(FinalSave_TrimAdjustOpen));
    internal static string Session_TrimHandleLeft => Get(nameof(Session_TrimHandleLeft));
    internal static string Session_TrimHandleTop => Get(nameof(Session_TrimHandleTop));
    internal static string Session_TrimHandleRight => Get(nameof(Session_TrimHandleRight));
    internal static string Session_TrimHandleBottom => Get(nameof(Session_TrimHandleBottom));
    internal static string Session_TrimHandleTopLeft => Get(nameof(Session_TrimHandleTopLeft));
    internal static string Session_TrimHandleTopRight => Get(nameof(Session_TrimHandleTopRight));
    internal static string Session_TrimHandleBottomLeft => Get(nameof(Session_TrimHandleBottomLeft));
    internal static string Session_TrimHandleBottomRight => Get(nameof(Session_TrimHandleBottomRight));

    internal static string Session_NextCorrectionWait => Get(nameof(Session_NextCorrectionWait));
    internal static string Session_AskColleague => Get(nameof(Session_AskColleague));
    internal static string Session_AskColleagueHint => Get(nameof(Session_AskColleagueHint));
    internal static string Session_CorrectionNoteLabel => Get(nameof(Session_CorrectionNoteLabel));
    internal static string Session_CorrectionPrepare => Get(nameof(Session_CorrectionPrepare));
    internal static string Session_CorrectionTryAgain => Get(nameof(Session_CorrectionTryAgain));
    internal static string Session_CorrectionCancel => Get(nameof(Session_CorrectionCancel));
    internal static string Session_CorrectionPreparing => Get(nameof(Session_CorrectionPreparing));
    internal static string Session_CorrectionPrepareFailed => Get(nameof(Session_CorrectionPrepareFailed));
    internal static string Session_CorrectionStatus => Get(nameof(Session_CorrectionStatus));
    internal static string Session_CorrectionStep1 => Get(nameof(Session_CorrectionStep1));
    internal static string Session_CorrectionReference => Get(nameof(Session_CorrectionReference));
    internal static string Session_CorrectionWorking => Get(nameof(Session_CorrectionWorking));
    internal static string Session_CorrectionFolder => Get(nameof(Session_CorrectionFolder));
    internal static string Session_CorrectionOpenFolder => Get(nameof(Session_CorrectionOpenFolder));
    internal static string Session_CorrectionStep2 => Get(nameof(Session_CorrectionStep2));
    internal static string Session_CorrectionNoNote => Get(nameof(Session_CorrectionNoNote));
    internal static string Session_CorrectionStep3 => Get(nameof(Session_CorrectionStep3));
    internal static string Session_CorrectionStep4 => Get(nameof(Session_CorrectionStep4));
    internal static string Session_CorrectionImport => Get(nameof(Session_CorrectionImport));
    internal static string Session_CorrectionPickerTitle => Get(nameof(Session_CorrectionPickerTitle));
    internal static string Session_CorrectionChecking => Get(nameof(Session_CorrectionChecking));
    internal static string Session_CorrectionRefused => Get(nameof(Session_CorrectionRefused));
    internal static string Session_CorrectionIsReference => Get(nameof(Session_CorrectionIsReference));
    internal static string Session_CorrectionChanged => Get(nameof(Session_CorrectionChanged));
    internal static string Session_CorrectionMissingFiles => Get(nameof(Session_CorrectionMissingFiles));
    internal static string Session_CorrectionRepair => Get(nameof(Session_CorrectionRepair));
    internal static string Session_CorrectionLastImportFailed => Get(nameof(Session_CorrectionLastImportFailed));
    internal static string Session_CorrectionReview => Get(nameof(Session_CorrectionReview));
    internal static string Session_CorrectionIdentical => Get(nameof(Session_CorrectionIdentical));
    internal static string Session_CorrectionOtherComputer => Get(nameof(Session_CorrectionOtherComputer));
    internal static string Session_CorrectionObsolete => Get(nameof(Session_CorrectionObsolete));
    internal static string Session_CorrectionUseImport => Get(nameof(Session_CorrectionUseImport));
    internal static string Session_CorrectionNotAvailable => Get(nameof(Session_CorrectionNotAvailable));
    internal static string Session_CorrectionResultChanged => Get(nameof(Session_CorrectionResultChanged));
    internal static string Session_CorrectionAlreadyImported => Get(nameof(Session_CorrectionAlreadyImported));
    internal static string Session_CorrectionNameTaken => Get(nameof(Session_CorrectionNameTaken));
    internal static string Session_CorrectionOpenFolderFailed => Get(nameof(Session_CorrectionOpenFolderFailed));
    internal static string Session_CorrectionOtherOptions => Get(nameof(Session_CorrectionOtherOptions));
    internal static string Session_CorrectionFileReference => Get(nameof(Session_CorrectionFileReference));
    internal static string Session_CorrectionFileWorking => Get(nameof(Session_CorrectionFileWorking));
    internal static string Session_CorrectionFileReturn => Get(nameof(Session_CorrectionFileReturn));
    internal static string Session_CorrectionInstructionsTitle => Get(nameof(Session_CorrectionInstructionsTitle));
    internal static string Home_RecentWaitingForCorrection => Get(nameof(Home_RecentWaitingForCorrection));
    internal static string Home_RecoveryOpenCorrection => Get(nameof(Home_RecoveryOpenCorrection));
    internal static string Session_GuidanceHeading => Get(nameof(Session_GuidanceHeading));
    internal static string Session_GuidanceEnhancementCompare => Get(nameof(Session_GuidanceEnhancementCompare));
    internal static string Session_GuidanceEnhancementDetail => Get(nameof(Session_GuidanceEnhancementDetail));
    internal static string Session_GuidanceEnhancementArtefacts => Get(nameof(Session_GuidanceEnhancementArtefacts));
    internal static string Session_GuidanceBackgroundColours => Get(nameof(Session_GuidanceBackgroundColours));
    internal static string Session_GuidanceBackgroundEdges => Get(nameof(Session_GuidanceBackgroundEdges));
    internal static string Session_GuidanceBackgroundKept => Get(nameof(Session_GuidanceBackgroundKept));
    internal static string Session_GuidanceTrimCompare => Get(nameof(Session_GuidanceTrimCompare));
    internal static string Session_GuidanceTrimEmpty => Get(nameof(Session_GuidanceTrimEmpty));
    internal static string Session_GuidanceTrimEdges => Get(nameof(Session_GuidanceTrimEdges));
    internal static string Session_GuidanceTiffWhiteInk => Get(nameof(Session_GuidanceTiffWhiteInk));
    internal static string Session_GuidanceTiffOverlay => Get(nameof(Session_GuidanceTiffOverlay));
    internal static string Session_GuidanceTiffColour => Get(nameof(Session_GuidanceTiffColour));
    internal static string Session_GuidanceTiffSize => Get(nameof(Session_GuidanceTiffSize));
    internal static string Session_GuidanceApprove => Get(nameof(Session_GuidanceApprove));
    internal static string Session_GuidanceApproveTiff => Get(nameof(Session_GuidanceApproveTiff));
    internal static string Session_GuidanceReject => Get(nameof(Session_GuidanceReject));
    internal static string Session_GuidanceRejectTiff => Get(nameof(Session_GuidanceRejectTiff));
    internal static string Session_GuidanceHelpEnhancement => Get(nameof(Session_GuidanceHelpEnhancement));
    internal static string Session_GuidanceHelpAskColleague => Get(nameof(Session_GuidanceHelpAskColleague));
    internal static string Session_GuidanceHelpNoColleague => Get(nameof(Session_GuidanceHelpNoColleague));
    internal static string Session_GuidanceHelpAdjustTrim => Get(nameof(Session_GuidanceHelpAdjustTrim));
    internal static string Session_GuidanceHelpNoAdjustTrim => Get(nameof(Session_GuidanceHelpNoAdjustTrim));
    internal static string Session_GuidanceHelpTiff => Get(nameof(Session_GuidanceHelpTiff));
    internal static string Session_GuidanceHelpTiffReturn => Get(nameof(Session_GuidanceHelpTiffReturn));

    // Print-size guidance (SCRUM-11150): mode help, the live summary and plain enlargement wording.
    internal static string Session_SizeHelpPreset => Get(nameof(Session_SizeHelpPreset));
    internal static string Session_SizeHelpCustom => Get(nameof(Session_SizeHelpCustom));
    internal static string Session_SizeSummaryDraft => Get(nameof(Session_SizeSummaryDraft));
    internal static string Session_SizeSummaryCurrent => Get(nameof(Session_SizeSummaryCurrent));
    internal static string Session_SizeGovernorWithinLimits => Get(nameof(Session_SizeGovernorWithinLimits));
    internal static string Session_SizeGovernorLimitWidth => Get(nameof(Session_SizeGovernorLimitWidth));
    internal static string Session_SizeGovernorLimitHeight => Get(nameof(Session_SizeGovernorLimitHeight));
    internal static string Session_SizeGovernorEdgeWidth => Get(nameof(Session_SizeGovernorEdgeWidth));
    internal static string Session_SizeGovernorEdgeHeight => Get(nameof(Session_SizeGovernorEdgeHeight));
    internal static string Session_SizeGovernorLongEdgeWidth => Get(nameof(Session_SizeGovernorLongEdgeWidth));
    internal static string Session_SizeGovernorLongEdgeHeight => Get(nameof(Session_SizeGovernorLongEdgeHeight));
    internal static string Session_SizeSummaryProportions => Get(nameof(Session_SizeSummaryProportions));
    internal static string Session_SizeDraftEnlargement => Get(nameof(Session_SizeDraftEnlargement));
    internal static string Session_SizeEnlargementPlain => Get(nameof(Session_SizeEnlargementPlain));
    internal static string Session_SizeTechnicalDetails => Get(nameof(Session_SizeTechnicalDetails));

    /// <summary>A resource in a named culture, for text written once in both languages (SCRUM-11148).</summary>
    internal static string InCulture(string key, CultureInfo culture) =>
        Manager.GetString(key, culture) ?? key;

    private static string Get(string key) =>
        Manager.GetString(key, OperatorCulture.Current) ?? key;
}
