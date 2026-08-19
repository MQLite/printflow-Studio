using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrintFlow.App.Navigation;
using PrintFlow.App.Resources;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Definitions;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Services;

namespace PrintFlow.App.ViewModels;

/// <summary>
/// One of the three fixed workflows, flattened for display.
/// </summary>
/// <remarks>
/// <see cref="Type"/> is the internal value that is persisted; <see cref="Title"/> is the only
/// part an operator reads. There are exactly three of these and no way to add a fourth — the
/// catalogue is the configuration (MVP design §6.1).
/// </remarks>
public sealed class WorkflowChoice
{
    internal WorkflowChoice(WorkflowDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        Type = definition.Type;
        Title = DisplayNames.Workflow(definition.Type);
        Steps = new ReadOnlyCollection<string>(definition.Steps.Select(Describe).ToList());
    }

    /// <summary>The persisted workflow value. Never displayed.</summary>
    public WorkflowType Type { get; }

    /// <summary>The localised workflow name.</summary>
    public string Title { get; }

    /// <summary>The workflow's steps, so the choice is informed rather than a bare name.</summary>
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

        string name = DisplayNames.Step(step.Kind);
        return flags.Count == 0
            ? string.Create(CultureInfo.CurrentCulture, $"{step.Ordinal + 1}. {name}")
            : string.Create(CultureInfo.CurrentCulture, $"{step.Ordinal + 1}. {name} ({string.Join(", ", flags)})");
    }
}

/// <summary>
/// Workflow Selection: the screen shown immediately after a successful import
/// (Epic 11100 Part 3C2 §6, §7).
/// </summary>
/// <remarks>
/// The choice is applied with <see cref="WorkflowCommand.SelectWorkflow"/> and nothing else.
/// There is no assignment to a session's workflow anywhere in this file, and the workflow-lock
/// rule is not restated here: <see cref="CanSelect"/> reads the engine's own
/// <c>AvailableCommands</c>, so the buttons are enabled by exactly the rule that would accept
/// the click, and a refusal still comes back through the command path if the session changed
/// underneath (MVP design invariant 12).
/// </remarks>
public sealed partial class WorkflowSelectionViewModel : ObservableObject
{
    private readonly ISessionService _sessions;
    private readonly INavigationService _navigation;

    [ObservableProperty]
    private string? _notice;

    [ObservableProperty]
    private bool _isBusy;

    private SessionView? _session;

    public WorkflowSelectionViewModel(ISessionService sessions, INavigationService navigation)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(navigation);

        _sessions = sessions;
        _navigation = navigation;

        Workflows = new ReadOnlyCollection<WorkflowChoice>(
            WorkflowCatalog.All.Select(definition => new WorkflowChoice(definition)).ToList());
    }

    /// <summary>The three fixed workflows, in menu order, straight from the catalogue.</summary>
    public IReadOnlyList<WorkflowChoice> Workflows { get; }

    public string Heading => Strings.WorkflowSelection_Heading;

    public string Hint => Strings.WorkflowSelection_Hint;

    public string BackLabel => Strings.Nav_BackToHome;

    public string SelectLabel => Strings.WorkflowSelection_Select;

    /// <summary>The output name of the session being set up.</summary>
    public string SessionName => _session?.OutputName.Value ?? string.Empty;

    /// <summary>
    /// Whether the workflow may still be chosen, as the engine reports it.
    /// </summary>
    /// <remarks>
    /// False once a derived Revision exists — the workflow lock (MVP design §6.1). The rule
    /// lives in the engine; this is a reading of its answer, not a second copy of it.
    /// </remarks>
    public bool CanSelect =>
        _session?.AvailableCommands.Contains(CommandKind.SelectWorkflow) == true;

    /// <summary>Points the screen at the session it is choosing a workflow for.</summary>
    public void Open(SessionView session)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;
        Notice = CanSelect ? null : Strings.WorkflowSelection_Locked;

        OnPropertyChanged(nameof(SessionName));
        OnPropertyChanged(nameof(CanSelect));
    }

    [RelayCommand]
    private async Task SelectAsync(WorkflowChoice? choice, CancellationToken cancellationToken)
    {
        if (choice is null || _session is null || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            Notice = null;
            OperationResult<SessionView> selected = await _sessions.ExecuteAsync(
                _session.Id,
                new WorkflowCommand.SelectWorkflow(choice.Type),
                Environment.UserName,
                cancellationToken).ConfigureAwait(true);

            if (selected.IsFailure)
            {
                // Includes the workflow lock: the engine refuses SelectWorkflow once a derived
                // Revision exists, and that refusal is shown rather than pre-empted.
                Notice = string.Format(
                    CultureInfo.CurrentCulture, Strings.WorkflowSelection_Refused, selected.Failure.Code);
                return;
            }

            _navigation.GoToSession(selected.Value);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task BackToHomeAsync(CancellationToken cancellationToken) =>
        await _navigation.GoHomeAsync(cancellationToken).ConfigureAwait(true);
}
