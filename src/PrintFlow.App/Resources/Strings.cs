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
