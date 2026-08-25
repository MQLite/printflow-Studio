using System.Globalization;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Reviews;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Trimming;

namespace PrintFlow.App.Resources;

/// <summary>
/// Turns the internal enums into operator text, in one place.
/// </summary>
/// <remarks>
/// The enum values themselves stay stable English and are what gets persisted
/// (MVP design §13.4); only the label an operator reads is translated. Keeping every
/// enum-to-label mapping here means a new language is a <c>.resx</c> file rather than a hunt
/// through view models.
/// <para>
/// Each mapping falls back to the enum's own name for an unmapped value, so a value added
/// later shows up visibly untranslated instead of throwing in front of the operator.
/// </para>
/// </remarks>
internal static class DisplayNames
{
    internal static string Workflow(WorkflowType type) => type switch
    {
        WorkflowType.PrepareAsset => Strings.Workflow_PrepareAsset,
        WorkflowType.PrepareCustomerDesign => Strings.Workflow_PrepareCustomerDesign,
        WorkflowType.GeneratePrintTiff => Strings.Workflow_GeneratePrintTiff,
        _ => type.ToString(),
    };

    internal static string Step(StepKind kind) => kind switch
    {
        StepKind.Import => Strings.Step_Import,
        StepKind.OriginalConfirmation => Strings.Step_OriginalConfirmation,
        StepKind.Enhancement => Strings.Step_Enhancement,
        StepKind.BackgroundRemoval => Strings.Step_BackgroundRemoval,
        StepKind.Trim => Strings.Step_Trim,
        StepKind.ApprovedPngExport => Strings.Step_ApprovedPngExport,
        StepKind.PrintDimensions => Strings.Step_PrintDimensions,
        StepKind.PhotoshopOutput => Strings.Step_PhotoshopOutput,
        _ => kind.ToString(),
    };

    internal static string SessionState(SessionState state) => state switch
    {
        Domain.Sessions.SessionState.Active => Strings.SessionState_Active,
        Domain.Sessions.SessionState.HandedOff => Strings.SessionState_HandedOff,
        Domain.Sessions.SessionState.Completed => Strings.SessionState_Completed,
        Domain.Sessions.SessionState.Abandoned => Strings.SessionState_Abandoned,
        _ => state.ToString(),
    };

    internal static string ImageFormat(ImageFormat format) => format switch
    {
        Domain.Files.ImageFormat.Png => Strings.Format_Png,
        Domain.Files.ImageFormat.Jpeg => Strings.Format_Jpeg,
        Domain.Files.ImageFormat.Tiff => Strings.Format_Tiff,
        Domain.Files.ImageFormat.Psd => Strings.Format_Psd,
        Domain.Files.ImageFormat.Pdf => Strings.Format_Pdf,
        Domain.Files.ImageFormat.Unknown => Strings.Format_Unknown,
        _ => format.ToString(),
    };

    /// <summary>
    /// The operator label for a quick rejection reason (MVP design §7.3).
    /// </summary>
    /// <remarks>
    /// The enum value is what gets persisted into <c>ReviewDecision.QuickReason</c> and read
    /// back as audit history; only this label is translated, so a decision recorded on a
    /// Chinese workstation still reads the same to anyone else.
    /// </remarks>
    internal static string RejectionReason(RejectionReason reason) => reason switch
    {
        Domain.Reviews.RejectionReason.InsufficientResult => Strings.Rejection_InsufficientResult,
        Domain.Reviews.RejectionReason.EdgeError => Strings.Rejection_EdgeError,
        Domain.Reviews.RejectionReason.MissingContent => Strings.Rejection_MissingContent,
        Domain.Reviews.RejectionReason.ColourIssue => Strings.Rejection_ColourIssue,
        Domain.Reviews.RejectionReason.DimensionIssue => Strings.Rejection_DimensionIssue,
        Domain.Reviews.RejectionReason.WhiteInkIssue => Strings.Rejection_WhiteInkIssue,
        Domain.Reviews.RejectionReason.Other => Strings.Rejection_Other,
        _ => reason.ToString(),
    };

    /// <summary>
    /// What a failure means to the operator, in one sentence (Part 3C3A §15).
    /// </summary>
    /// <remarks>
    /// Deliberately maps the <see cref="FailureCode"/> rather than
    /// <c>OperationFailure.TechnicalDetail</c>: the detail is English log text that can name a
    /// path, and is never shown. The code itself is still displayed alongside this sentence,
    /// because it is the stable identifier a support call can quote (MVP design §13.4).
    /// </remarks>
    internal static string Failure(FailureCode code) => code switch
    {
        FailureCode.OutputMissing => Strings.Failure_OutputMissing,
        FailureCode.OutputUnreadable => Strings.Failure_OutputUnreadable,
        FailureCode.OutputValidationFailed => Strings.Failure_OutputValidationFailed,
        FailureCode.Timeout => Strings.Failure_Timeout,
        FailureCode.Cancelled => Strings.Failure_Cancelled,
        FailureCode.RevisionIntegrityMismatch => Strings.Failure_RevisionIntegrityMismatch,
        FailureCode.EnvironmentNotVerified => Strings.Failure_EnvironmentNotVerified,
        FailureCode.AdapterUnavailable => Strings.Failure_AdapterUnavailable,
        FailureCode.PresetHashMismatch => Strings.Failure_PresetHashMismatch,
        FailureCode.UnknownDialog => Strings.Failure_UnknownDialog,
        FailureCode.WorkspaceError => Strings.Failure_WorkspaceError,
        FailureCode.PersistenceError => Strings.Failure_PersistenceError,
        FailureCode.PreconditionNotMet => Strings.Failure_PreconditionNotMet,
        FailureCode.ManualCropRequired => Strings.Failure_ManualCropRequired,
        FailureCode.MeituNotInstalled => Strings.Failure_MeituNotInstalled,
        FailureCode.MeituLaunchFailed => Strings.Failure_MeituLaunchFailed,
        FailureCode.MeituWindowNotFound => Strings.Failure_MeituWindowNotFound,
        FailureCode.MeituTargetLost => Strings.Failure_MeituTargetLost,
        FailureCode.MeituUnknownState => Strings.Failure_MeituUnknownState,
        FailureCode.MeituBlockingDialog => Strings.Failure_MeituBlockingDialog,
        FailureCode.MeituOpenInputFailed => Strings.Failure_MeituOpenInputFailed,
        _ => code.ToString(),
    };

    /// <summary>
    /// Resolves a failure's persisted message key, allowing one stable code to retain useful
    /// runtime distinctions such as foreground loss versus a closed Meitu process.
    /// </summary>
    internal static string Failure(OperationFailure failure) =>
        Strings.Resolve(failure.MessageKey);

    /// <summary>
    /// The operator label for a white-underbase branch, carrying its guidance (Part 3C3B §7).
    /// </summary>
    /// <remarks>
    /// The guidance — 0 px fine detail, 1 px ordinary, 2 px solid — is written into the label
    /// because it is what the operator classifies against. It stays advice: nothing here ranks
    /// the branches, marks one recommended, or looks at the image. The enum value is what gets
    /// persisted (MVP design §12, §13.4).
    /// </remarks>
    internal static string WhiteUnderbaseBranch(WhiteUnderbaseBranch branch) => branch switch
    {
        Domain.Outputs.WhiteUnderbaseBranch.W1_0px => Strings.W1_0px,
        Domain.Outputs.WhiteUnderbaseBranch.W1_1px => Strings.W1_1px,
        Domain.Outputs.WhiteUnderbaseBranch.W1_2px => Strings.W1_2px,
        _ => branch.ToString(),
    };

    /// <summary>
    /// The operator label for a size preset.
    /// </summary>
    /// <remarks>
    /// A label only. The millimetres behind each preset come from
    /// <c>PrintDimensions.NominalMillimetres</c>, so this file never states a size.
    /// </remarks>
    internal static string SizePreset(SizePreset preset) => preset switch
    {
        Domain.Outputs.SizePreset.A3Landscape => Strings.Preset_A3Landscape,
        Domain.Outputs.SizePreset.A3Portrait => Strings.Preset_A3Portrait,
        Domain.Outputs.SizePreset.A4 => Strings.Preset_A4,
        Domain.Outputs.SizePreset.A5 => Strings.Preset_A5,
        Domain.Outputs.SizePreset.Custom => Strings.Preset_Custom,
        _ => preset.ToString(),
    };

    /// <summary>
    /// The operator label for a trim mode, carrying what it means (Epic 11200 Part C3 §9).
    /// </summary>
    /// <remarks>
    /// Each label says what the mode does, because the three are otherwise distinguishable only
    /// by the boxes that appear underneath them. The enum value is what gets persisted, on both
    /// the session and the producing attempt (MVP design §13.4).
    /// </remarks>
    internal static string TrimMode(TrimMode mode) => mode switch
    {
        Domain.Trimming.TrimMode.TightCrop => Strings.Session_TrimModeTight,
        Domain.Trimming.TrimMode.UniformMargin => Strings.Session_TrimModeUniform,
        Domain.Trimming.TrimMode.EdgeSpecificMargin => Strings.Session_TrimModeEdgeSpecific,
        _ => mode.ToString(),
    };

    /// <summary>
    /// One concise line describing how a trim was parameterised (Epic 11200 Part C3 §18).
    /// </summary>
    /// <remarks>
    /// Shaped by the mode rather than by the numbers, so a uniform 0&#160;px reads as
    /// "Uniform 0 px" and not as "Tight": the two produce the same rectangle but the operator
    /// asked for different things, and a review line that collapsed them would misreport what
    /// was chosen (Part B, <c>TrimMode</c>).
    /// <para>
    /// Deliberately a sentence and not a screen. §18 asks for concise parameter information
    /// beside the result, and a general processing-history browser is explicitly out of scope
    /// (§28).
    /// </para>
    /// </remarks>
    internal static string TrimMargin(TrimMargin margin) => margin.Mode switch
    {
        Domain.Trimming.TrimMode.UniformMargin => string.Format(
            CultureInfo.CurrentCulture, Strings.Session_TrimSummaryUniform, margin.Top),
        Domain.Trimming.TrimMode.EdgeSpecificMargin => string.Format(
            CultureInfo.CurrentCulture, Strings.Session_TrimSummaryEdges,
            margin.Top, margin.Right, margin.Bottom, margin.Left),
        _ => Strings.Session_TrimSummaryTight,
    };

    /// <summary>The operator label for an output's cached review projection.</summary>
    internal static string ReviewState(ReviewState state) => state switch
    {
        Domain.Revisions.ReviewState.NotReviewed => Strings.ReviewState_NotReviewed,
        Domain.Revisions.ReviewState.Approved => Strings.ReviewState_Approved,
        Domain.Revisions.ReviewState.Rejected => Strings.ReviewState_Rejected,
        _ => state.ToString(),
    };

    internal static string StepState(StepState state) => state switch
    {
        Domain.Sessions.StepState.Waiting => Strings.StepState_Waiting,
        Domain.Sessions.StepState.Processing => Strings.StepState_Processing,
        Domain.Sessions.StepState.ReviewRequired => Strings.StepState_ReviewRequired,
        Domain.Sessions.StepState.Approved => Strings.StepState_Approved,
        Domain.Sessions.StepState.RetryRequired => Strings.StepState_RetryRequired,
        Domain.Sessions.StepState.Skipped => Strings.StepState_Skipped,
        Domain.Sessions.StepState.Failed => Strings.StepState_Failed,
        Domain.Sessions.StepState.Interrupted => Strings.StepState_Interrupted,
        _ => state.ToString(),
    };
}
