using Microsoft.Extensions.DependencyInjection;
using PrintFlow.App.ViewModels;
using PrintFlow.Workflow.Services;

namespace PrintFlow.App.Navigation;

/// <summary>
/// Resolves the destination view model from the container and publishes it as
/// <see cref="Current"/>.
/// </summary>
/// <remarks>
/// Resolution is deferred to the moment of navigation rather than injected up front, because
/// every screen depends on this service and constructing them eagerly would be a dependency
/// cycle. It also means each visit starts from a fresh, transient view model, so a screen
/// cannot carry a previous session's state into the next one.
/// </remarks>
public sealed class NavigationService : INavigationService
{
    private readonly IServiceProvider _services;

    public NavigationService(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        _services = services;
    }

    /// <inheritdoc />
    public object? Current { get; private set; }

    /// <inheritdoc />
    public event EventHandler? CurrentChanged;

    /// <inheritdoc />
    public async Task GoHomeAsync(CancellationToken cancellationToken)
    {
        HomeViewModel home = _services.GetRequiredService<HomeViewModel>();
        Show(home);
        await home.RefreshCommand.ExecuteAsync(null).ConfigureAwait(true);
    }

    /// <inheritdoc />
    public void GoToWorkflowSelection(SessionView session)
    {
        ArgumentNullException.ThrowIfNull(session);

        WorkflowSelectionViewModel selection = _services.GetRequiredService<WorkflowSelectionViewModel>();
        selection.Open(session);
        Show(selection);
    }

    /// <inheritdoc />
    public void GoToSession(SessionView session)
    {
        ArgumentNullException.ThrowIfNull(session);

        SessionViewModel view = _services.GetRequiredService<SessionViewModel>();
        view.Open(session);
        Show(view);
    }

    private void Show(object viewModel)
    {
        Current = viewModel;
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }
}
