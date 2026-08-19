using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrintFlow.App.Navigation;
using PrintFlow.App.Resources;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Services;

namespace PrintFlow.App.ViewModels;

/// <summary>One step of the open session, flattened for display.</summary>
public sealed class SessionStepRow
{
    internal SessionStepRow(SessionStep step, bool isCurrent)
    {
        ArgumentNullException.ThrowIfNull(step);

        Ordinal = step.Ordinal + 1;
        Name = DisplayNames.Step(step.Step);
        State = DisplayNames.StepState(step.State);
        IsCurrent = isCurrent;
    }

    public int Ordinal { get; }

    public string Name { get; }

    public string State { get; }

    /// <summary>Whether this is the step the operator is expected to act on.</summary>
    public bool IsCurrent { get; }
}

/// <summary>
/// The session screen for this slice: it shows what was actually loaded, and no controls
/// (Epic 11100 Part 3C2 §9, §19).
/// </summary>
/// <remarks>
/// Deliberately read-only. Run Step, review, approve/reject, retry, print dimensions and the
/// white-underbase choice are Part 3C3 and are not stubbed here — an inert button that looks
/// like it works is worse than a screen that plainly has none yet.
/// <para>
/// What it does prove is that resume is real: everything shown comes from the
/// <see cref="SessionView"/> that <see cref="ISessionService.LoadAsync"/> reconstructed from
/// SQLite, so a session opened after a restart shows the state that was persisted rather than
/// anything this process remembered.
/// </para>
/// </remarks>
public sealed partial class SessionViewModel : ObservableObject
{
    private readonly INavigationService _navigation;

    private SessionView? _session;

    public SessionViewModel(INavigationService navigation)
    {
        ArgumentNullException.ThrowIfNull(navigation);

        _navigation = navigation;
    }

    /// <summary>The open session's steps, in workflow order.</summary>
    public ObservableCollection<SessionStepRow> Steps { get; } = [];

    public string BackLabel => Strings.Nav_BackToHome;

    public string StepsHeading => Strings.Session_StepsHeading;

    public string PlaceholderNotice => Strings.Session_PlaceholderNotice;

    /// <summary>The output name of the open session.</summary>
    public string SessionName => _session?.OutputName.Value ?? string.Empty;

    /// <summary>The localised workflow of the open session.</summary>
    public string Workflow => _session is null ? string.Empty : DisplayNames.Workflow(_session.WorkflowType);

    /// <summary>The localised session state of the open session.</summary>
    public string State => _session is null ? string.Empty : DisplayNames.SessionState(_session.State);

    /// <summary>The localised step the session is waiting on, or a "finished" line.</summary>
    public string CurrentStep => _session?.CurrentStep is { } step
        ? DisplayNames.Step(step.Step)
        : Strings.Session_AllStepsFinished;

    /// <summary>
    /// True for a completed or abandoned session, which is a record to read rather than work
    /// to continue (Part 3C2 §11).
    /// </summary>
    public bool IsReadOnly => _session?.CanContinueProcessing != true;

    /// <summary>Shows <paramref name="session"/> exactly as the service returned it.</summary>
    public void Open(SessionView session)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;

        Steps.Clear();
        foreach (SessionStep step in session.Steps)
        {
            Steps.Add(new SessionStepRow(step, isCurrent: step == session.CurrentStep));
        }

        OnPropertyChanged(nameof(SessionName));
        OnPropertyChanged(nameof(Workflow));
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(CurrentStep));
        OnPropertyChanged(nameof(IsReadOnly));
    }

    [RelayCommand]
    private async Task BackToHomeAsync(CancellationToken cancellationToken) =>
        await _navigation.GoHomeAsync(cancellationToken).ConfigureAwait(true);
}
