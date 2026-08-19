using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Reviews;
using PrintFlow.Domain.Sessions;

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
        _ => code.ToString(),
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
