using Microsoft.Extensions.DependencyInjection;
using PrintFlow.App.ViewModels;
using PrintFlow.Workflow.Services;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.App.ViewModels.AiDesign;
using PrintFlow.App.Resources;
using PrintFlow.Workflow.AiDesign;

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
public sealed class NavigationService : INavigationService, IUtilityTaskNavigation, IAiDesignNavigation
{
    private readonly IServiceProvider _services;
    private SessionViewModel? _utilitySource;
    private bool _openingWorkResult;
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

    public NavigationService(IServiceProvider services, WorkTaskTracker? workTracker = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        _services = services;
        if (workTracker is not null) workTracker.ResultImported += OnWorkResultImported;
    }

    private void OnWorkResultImported(SessionId id)
    {
        if (Current is HomeViewModel home) home.OnWorkResultImported(id);
    }

    /// <inheritdoc />
    public object? Current { get; private set; }

    /// <inheritdoc />
    public event EventHandler? CurrentChanged;

    /// <inheritdoc />
    public async Task GoHomeAsync(CancellationToken cancellationToken)
    {
        if (!await CanLeaveAuthoringAsync(cancellationToken)) return;
        _utilitySource = null;
        HomeViewModel home = _services.GetRequiredService<HomeViewModel>();
        Show(home);
        await home.RefreshCommand.ExecuteAsync(null).ConfigureAwait(true);
    }

    /// <inheritdoc />
    public void GoToWorkflowSelection(SessionView session)
    {
        if (Current is IAiAuthoringCloseGuard) return;
        _utilitySource = null;
        ArgumentNullException.ThrowIfNull(session);

        WorkflowSelectionViewModel selection = _services.GetRequiredService<WorkflowSelectionViewModel>();
        selection.Open(session);
        Show(selection);
    }

    /// <inheritdoc />
    public void GoToSession(SessionView session)
    {
        if (Current is IAiAuthoringCloseGuard) return;
        _utilitySource = null;
        ArgumentNullException.ThrowIfNull(session);

        OpenSession(session);
    }

    private void OpenSession(SessionView session)
    {
        SessionViewModel view = _services.GetRequiredService<SessionViewModel>();
        view.Open(session);
        Show(view);
    }

    /// <inheritdoc />
    public async Task GoToEnvironmentReadinessAsync(CancellationToken cancellationToken)
    {
        if (!await CanLeaveAuthoringAsync(cancellationToken)) return;
        if (!CaptureUtilitySource()) return;
        EnvironmentReadinessViewModel readiness =
            _services.GetRequiredService<EnvironmentReadinessViewModel>();
        Show(readiness);
        await readiness.OpenAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <inheritdoc />
    public async Task GoToSettingsAsync(CancellationToken cancellationToken)
    {
        if (!await CanLeaveAuthoringAsync(cancellationToken)) return;
        if (!CaptureUtilitySource()) return;
        SettingsViewModel settings = _services.GetRequiredService<SettingsViewModel>();
        Show(settings);
        await settings.OpenAsync(cancellationToken).ConfigureAwait(true);
    }

    public async Task GoToErrorDetailsAsync(
        SessionId sessionId, AttemptId attemptId, CancellationToken cancellationToken)
    {
        if (!await CanLeaveAuthoringAsync(cancellationToken)) return;
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

    private async Task<bool> CanLeaveAuthoringAsync(CancellationToken ct)
    {
        var source = Current;
        return source is not IAiAuthoringCloseGuard guard || await guard.RequestCloseAsync(ct) && ReferenceEquals(Current, source);
    }

    public async Task<OperationResult<Unit>> OpenWorkResultAsync(Guid requestId, CancellationToken ct)
    {
        var source = Current;
        if (_openingWorkResult || source is not (AiPromptWorkbenchViewModel or AiWorkTasksViewModel) || ct.IsCancellationRequested)
            return OperationResult.Ok();

        var draftId = (source as AiPromptWorkbenchViewModel)?.Draft?.Id;
        bool IsCurrentSource() => ReferenceEquals(Current, source) &&
            (source is not AiPromptWorkbenchViewModel bench || bench.Draft?.Id == draftId);

        _openingWorkResult = true;
        try
        {
            var query = _services.GetRequiredService<IWorkTaskQuery>();
            var task = await query.FindAsync(requestId, ct);
            if (ct.IsCancellationRequested || !IsCurrentSource()) return OperationResult.Ok();
            if (task.IsFailure) return OperationResult.Fail<Unit>(task.Failure);
            if (task.Value is not { Stage: WorkTaskStage.Imported, ImportedSessionId: { } id })
                return OperationResult.Fail<Unit>(FailureCode.PreconditionNotMet, "There is no imported Work result.");

            var session = await _services.GetRequiredService<ISessionService>().LoadAsync(id, ct);
            if (ct.IsCancellationRequested || !IsCurrentSource()) return OperationResult.Ok();
            if (session.IsFailure) return OperationResult.Fail<Unit>(session.Failure);
            if (!await CanLeaveAuthoringAsync(ct) || ct.IsCancellationRequested || !IsCurrentSource())
                return OperationResult.Ok();

            _utilitySource = null;
            OpenSession(session.Value);
            return OperationResult.Ok();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return OperationResult.Ok(); }
        finally { _openingWorkResult = false; }
    }

    public async Task GoToAiWorkTasksAsync(Guid? returnDraftId, CancellationToken ct)
    {
        if (Current is not AiPromptWorkbenchViewModel source || !await CanLeaveAuthoringAsync(ct) ||
            ct.IsCancellationRequested || !ReferenceEquals(Current, source)) return;
        var tasks = _services.GetRequiredService<AiWorkTasksViewModel>();
        Show(tasks);
        await tasks.OpenAsync(source.Draft?.Id ?? returnDraftId);
    }

    public async Task GoToPromptWorkbenchAsync(Guid? draftId, CancellationToken ct)
    {
        var source = Current;
        if (source is HomeViewModel { CanOpenPromptWorkbench: false } ||
            source is not (HomeViewModel or AiWorkTasksViewModel)) return;
        var vm = _services.GetRequiredService<AiPromptWorkbenchViewModel>();
        try { await vm.OpenAsync(draftId, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { vm.Deactivate(); return; }
        if (ct.IsCancellationRequested || !ReferenceEquals(Current, source) || source is HomeViewModel { CanOpenPromptWorkbench: false })
        { vm.Deactivate(); return; }
        if ((vm.Draft is null || vm.Error is not null) && !(vm.IsSavedDraftsVisible && vm.SavedDrafts.Count > 0))
        {
            if (source is HomeViewModel home) home.Notice = vm.Error ?? Strings.AiPrompt_SaveFailed;
            if (source is AiWorkTasksViewModel tasks) tasks.ShowError(vm.Error ?? Strings.AiPrompt_SaveFailed);
            vm.Deactivate();
            return;
        }
        _utilitySource = null;
        Show(vm);
    }
}
