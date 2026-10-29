using Microsoft.Extensions.DependencyInjection;
using PrintFlow.App.ViewModels;
using PrintFlow.Workflow.Services;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;

namespace PrintFlow.App.Navigation;

/// <summary>
/// Resolves the destination view model from the container and publishes it as
/// <see cref="Current"/>.
/// </summary>
/// <remarks>
/// Resolution is deferred to the moment of navigation rather than injected up front, because
/// every screen depends on this service and constructing them eagerly would be a dependency
/// cycle. Destinations are transient except for one suspended source task during a utility
/// visit. Returning reloads that exact task before retaining any matching UI draft.
/// </remarks>
public sealed class NavigationService : INavigationService, IUtilityTaskNavigation
{
    private readonly IServiceProvider _services;
    private SessionViewModel? _utilitySource;
    public bool HasSourceTask => _utilitySource is not null;
    public bool IsCurrentUtility(object requester) => ReferenceEquals(Current, requester) &&
        requester is SettingsViewModel or EnvironmentReadinessViewModel;

    public async Task<OperationFailure?> ReturnFromUtilityAsync(object requester, CancellationToken cancellationToken)
    {
        if (!IsCurrentUtility(requester)) return null;
        if (_utilitySource is not { } source)
        {
            await GoHomeAsync(cancellationToken).ConfigureAwait(true);
            return null;
        }
        var loaded = await _services.GetRequiredService<ISessionService>().LoadAsync(source.Id, cancellationToken).ConfigureAwait(true);
        if (!IsCurrentUtility(requester) || !ReferenceEquals(_utilitySource, source)) return null;
        if (!loaded.IsSuccess) return loaded.Failure;
        source.ResumeFromUtility(loaded.Value);
        _utilitySource = null;
        Show(source);
        return null;
    }

    private bool CaptureUtilitySource()
    {
        if (Current is not SessionViewModel source) return true;
        if (!source.CanOpenUtilities) return false;
        source.SuspendForUtility();
        _utilitySource = source;
        return true;
    }

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
        _utilitySource = null;
        HomeViewModel home = _services.GetRequiredService<HomeViewModel>();
        Show(home);
        await home.RefreshCommand.ExecuteAsync(null).ConfigureAwait(true);
    }

    /// <inheritdoc />
    public void GoToWorkflowSelection(SessionView session)
    {
        _utilitySource = null;
        ArgumentNullException.ThrowIfNull(session);

        WorkflowSelectionViewModel selection = _services.GetRequiredService<WorkflowSelectionViewModel>();
        selection.Open(session);
        Show(selection);
    }

    /// <inheritdoc />
    public void GoToSession(SessionView session)
    {
        _utilitySource = null;
        ArgumentNullException.ThrowIfNull(session);

        SessionViewModel view = _services.GetRequiredService<SessionViewModel>();
        view.Open(session);
        Show(view);
    }

    /// <inheritdoc />
    public async Task GoToEnvironmentReadinessAsync(CancellationToken cancellationToken)
    {
        if (!CaptureUtilitySource()) return;
        EnvironmentReadinessViewModel readiness =
            _services.GetRequiredService<EnvironmentReadinessViewModel>();
        Show(readiness);
        await readiness.OpenAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <inheritdoc />
    public async Task GoToSettingsAsync(CancellationToken cancellationToken)
    {
        if (!CaptureUtilitySource()) return;
        SettingsViewModel settings = _services.GetRequiredService<SettingsViewModel>();
        Show(settings);
        await settings.OpenAsync(cancellationToken).ConfigureAwait(true);
    }

    public async Task GoToErrorDetailsAsync(
        SessionId sessionId, AttemptId attemptId, CancellationToken cancellationToken)
    {
        _utilitySource = null;
        ErrorDetailsViewModel details = _services.GetRequiredService<ErrorDetailsViewModel>();
        Show(details);
        await details.OpenAsync(sessionId, attemptId, cancellationToken).ConfigureAwait(true);
    }

    private void Show(object viewModel)
    {
        if (Current is IEntryScreenLifetime entry) entry.Deactivate();
        Current = viewModel;
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }
}
