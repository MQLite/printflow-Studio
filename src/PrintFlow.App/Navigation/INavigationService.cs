using PrintFlow.Workflow.Services;

namespace PrintFlow.App.Navigation;

/// <summary>
/// The whole navigation model: four destinations and one current view model
/// (Epic 11100 Part 3C2 §16).
/// </summary>
/// <remarks>
/// Deliberately not a navigation framework. There is no journal, no back stack, no URI routing
/// and no region manager, because the operator's route is a short loop —
/// <c>Home → Workflow Selection → Session → Home</c> — and anything larger would be structure
/// without a requirement behind it.
/// <para>
/// Each session destination takes the <see cref="SessionView"/> it is about, so a screen never
/// re-reads state the caller already holds and never invents its own idea of which session is
/// open. Production Readiness is the one destination that is about no session at all: it reports
/// on the workstation, which is why it hangs off Home rather than off a piece of work
/// (Epic 11500 Part C §3).
/// </para>
/// </remarks>
public interface INavigationService
{
    /// <summary>The view model currently shown. Null until the first navigation.</summary>
    object? Current { get; }

    /// <summary>Raised after <see cref="Current"/> changes.</summary>
    event EventHandler? CurrentChanged;

    /// <summary>Shows Home and refreshes its Recent Processing list.</summary>
    Task GoHomeAsync(CancellationToken cancellationToken);

    /// <summary>Shows Workflow Selection for a session that has just been imported.</summary>
    void GoToWorkflowSelection(SessionView session);

    /// <summary>Shows the session screen for <paramref name="session"/>.</summary>
    void GoToSession(SessionView session);

    /// <summary>
    /// Shows Production Readiness and takes its first reading (Epic 11500 Part C §3).
    /// </summary>
    /// <remarks>
    /// Asynchronous for the same reason <see cref="GoHomeAsync"/> is: the screen is shown and
    /// then populated, so the shell does not wait on a reading before it can draw anything.
    /// </remarks>
    Task GoToEnvironmentReadinessAsync(CancellationToken cancellationToken);
}
