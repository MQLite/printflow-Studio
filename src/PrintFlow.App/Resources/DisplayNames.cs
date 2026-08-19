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
