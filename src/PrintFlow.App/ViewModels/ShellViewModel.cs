using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using PrintFlow.App.Resources;
using PrintFlow.App.Startup;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Definitions;

namespace PrintFlow.App.ViewModels;

/// <summary>
/// One workflow, flattened for display.
/// </summary>
public sealed class WorkflowSummary
{
    public WorkflowSummary(WorkflowDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        Title = LocalisedWorkflowName(definition.Type);
        Steps = new ReadOnlyCollection<string>(
            definition.Steps.Select(Describe).ToList());
    }

    public string Title { get; }

    public IReadOnlyList<string> Steps { get; }

    private static string Describe(StepDefinition step)
    {
        List<string> flags = [];
        if (step.IsSkippable)
        {
            flags.Add(Strings.Flag_Skippable);
        }

        if (step.RequiresReview)
        {
            flags.Add(Strings.Flag_RequiresReview);
        }

        string name = LocalisedStepName(step.Kind);
        return flags.Count == 0
            ? string.Create(CultureInfo.CurrentCulture, $"{step.Ordinal + 1}. {name}")
            : string.Create(CultureInfo.CurrentCulture, $"{step.Ordinal + 1}. {name} ({string.Join(", ", flags)})");
    }

    private static string LocalisedWorkflowName(WorkflowType type) => type switch
    {
        WorkflowType.PrepareAsset => Strings.Workflow_PrepareAsset,
        WorkflowType.PrepareCustomerDesign => Strings.Workflow_PrepareCustomerDesign,
        WorkflowType.GeneratePrintTiff => Strings.Workflow_GeneratePrintTiff,
        _ => type.ToString(),
    };

    private static string LocalisedStepName(StepKind kind) => kind switch
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
}

/// <summary>
/// The shell view model for the Part 1 slice.
/// </summary>
/// <remarks>
/// Its only job is to prove that the projects compose and that the workflow definitions are
/// reachable from the UI layer through <c>PrintFlow.Workflow</c> alone. It holds no session,
/// touches no file, and issues no command — the Studio UI is a later slice
/// (Epic 11100 plan §16.2).
///
/// Every operator-visible string comes from <c>Strings.resx</c> so the Chinese-first
/// localisation in a later slice is a translation job, not a string-extraction refactor.
/// </remarks>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly StartupStatusAccessor _startupStatus;

    public ShellViewModel(StartupStatusAccessor startupStatus)
    {
        ArgumentNullException.ThrowIfNull(startupStatus);

        _startupStatus = startupStatus;
        Workflows = new ReadOnlyCollection<WorkflowSummary>(
            WorkflowCatalog.All.Select(definition => new WorkflowSummary(definition)).ToList());
    }

    public string Heading => Strings.Shell_Heading;

    public string Subheading => Strings.Shell_Subheading;

    public string WorkflowsHeading => Strings.Shell_WorkflowsHeading;

    public string FoundationNotice => Strings.Shell_FoundationNotice;

    /// <summary>The three fixed workflows, straight from the catalogue.</summary>
    public IReadOnlyList<WorkflowSummary> Workflows { get; }

    /// <summary>
    /// One line describing what startup recovery did, so a restart after a crash says so
    /// visibly rather than only in a report object (Epic 11100 Part 3C1 §6).
    /// </summary>
    /// <remarks>
    /// Deliberately a summary and nothing more — the diagnostics surface that lists individual
    /// recovery entries is a later slice.
    /// </remarks>
    public string StartupSummary
    {
        get
        {
            StartupStatus? status = _startupStatus.Status;
            if (status is null || !status.RecoveryExecuted)
            {
                return Strings.Startup_RecoveryNotRun;
            }

            if (status.RecoveryReport is { IsNoOp: true })
            {
                return Strings.Startup_RecoveryClean;
            }

            return string.Format(
                CultureInfo.CurrentCulture,
                Strings.Startup_RecoverySummary,
                status.RecoveredAttemptCount,
                status.ReleasedStaleLockCount,
                status.QuarantinedFileCount);
        }
    }
}
