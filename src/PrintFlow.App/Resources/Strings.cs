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

    internal static string Shell_Heading => Get(nameof(Shell_Heading));

    internal static string Shell_Subheading => Get(nameof(Shell_Subheading));

    internal static string Shell_WorkflowsHeading => Get(nameof(Shell_WorkflowsHeading));

    internal static string Shell_StepsHeading => Get(nameof(Shell_StepsHeading));

    internal static string Shell_FoundationNotice => Get(nameof(Shell_FoundationNotice));

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

    internal static string Startup_AlreadyRunning => Get(nameof(Startup_AlreadyRunning));

    internal static string Startup_Failed => Get(nameof(Startup_Failed));

    internal static string Startup_RecoveryNotRun => Get(nameof(Startup_RecoveryNotRun));

    internal static string Startup_RecoveryClean => Get(nameof(Startup_RecoveryClean));

    /// <summary>Composite format: interrupted attempts, released locks, quarantined files.</summary>
    internal static string Startup_RecoverySummary => Get(nameof(Startup_RecoverySummary));

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
