using PrintFlow.App.Navigation;
using PrintFlow.App.Startup;
using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Fixtures;

/// <summary>
/// Records where the UI navigated, without a window (Epic 11100 Part 3C2 §17).
/// </summary>
/// <remarks>
/// The real <see cref="NavigationService"/> resolves screens from the container and is exercised
/// end to end by the startup test that drives the real graph. Here the interesting question is
/// only "did Home send the operator to Workflow Selection, and with which session", so the
/// destination is recorded rather than constructed.
/// </remarks>
internal sealed class RecordingNavigation : INavigationService
{
    public object? Current { get; private set; }

    public event EventHandler? CurrentChanged;

    /// <summary>The session Workflow Selection was opened for, if it was.</summary>
    public SessionView? WorkflowSelectionFor { get; private set; }

    /// <summary>The session the session screen was opened for, if it was.</summary>
    public SessionView? SessionFor { get; private set; }

    public int GoHomeCount { get; private set; }

    public Task GoHomeAsync(CancellationToken cancellationToken)
    {
        GoHomeCount++;
        return Task.CompletedTask;
    }

    public void GoToWorkflowSelection(SessionView session)
    {
        WorkflowSelectionFor = session;
        Current = session;
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void GoToSession(SessionView session)
    {
        SessionFor = session;
        Current = session;
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>An <see cref="IFilePicker"/> that answers with a scripted path instead of a dialog.</summary>
internal sealed class StubFilePicker : IFilePicker
{
    public StubFilePicker(string? path = null) => Path = path;

    /// <summary>What the next pick returns. Null means the operator cancelled.</summary>
    public string? Path { get; set; }

    public int CallCount { get; private set; }

    public string? PickSingleFile(string dialogTitle, string filter)
    {
        CallCount++;
        return Path;
    }
}

/// <summary>
/// Captures the arguments <see cref="SessionService.ListRecentAsync"/> passes down, so the
/// "up to 100 sessions from the last 30 days" policy can be asserted without creating a
/// hundred real sessions.
/// </summary>
internal sealed class RecordingSessionRepository : ISessionRepository
{
    public int? RequestedMaxCount { get; private set; }

    public DateTimeOffset? RequestedSince { get; private set; }

    public Task<OperationResult<IReadOnlyList<SessionListItem>>> ListRecentAsync(
        int maxCount, DateTimeOffset since, CancellationToken cancellationToken)
    {
        RequestedMaxCount = maxCount;
        RequestedSince = since;
        return Task.FromResult(OperationResult.Ok<IReadOnlyList<SessionListItem>>([]));
    }

    public Task<OperationResult<SessionAggregate?>> LoadAsync(SessionId id, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<OperationResult<PrintFlow.Domain.Results.Unit>> CommitAsync(SessionMutation mutation, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<OperationResult<IReadOnlyList<ProcessingAttempt>>> FindRunningAttemptsAsync(
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<OperationResult<AutomationLockState>> GetAutomationLockAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

/// <summary>
/// A Home screen wired to the real session service, real workspace and real SQLite database.
/// </summary>
/// <remarks>
/// Nothing about PrintFlow's own behaviour is mocked: the view model under test drives the same
/// <see cref="SessionService"/>, <c>FileWorkspace</c> and <c>SqliteSessionRepository</c> the
/// application uses. Only the two things a test cannot have — a modal file dialog and a
/// window — are substituted (Part 3C2 §17, §18).
/// </remarks>
internal sealed class HomeScreenHarness : IDisposable
{
    private readonly SessionServiceHarness _harness = new();

    public HomeScreenHarness()
    {
        Sessions = _harness.CreateService();
        Home = new HomeViewModel(Sessions, Navigation, FilePicker, StartupStatus);
    }

    public SessionServiceHarness Inner => _harness;

    public ISessionService Sessions { get; }

    public RecordingNavigation Navigation { get; } = new();

    public StubFilePicker FilePicker { get; } = new();

    public StartupStatusAccessor StartupStatus { get; } = new();

    public HomeViewModel Home { get; }

    /// <summary>Writes a synthetic PNG the operator could plausibly have chosen.</summary>
    public string WriteSourceFile(string fileName) => _harness.WriteSourcePng(fileName);

    /// <summary>Moves the shared clock on, so successive sessions get distinct update times.</summary>
    public void AdvanceClock(TimeSpan by) => _harness.Clock.Advance(by);

    /// <summary>
    /// A second Home over the same database and workspace, with a freshly built session
    /// service — what "close the application and open it again" looks like from a test.
    /// </summary>
    public HomeViewModel RestartHome(RecordingNavigation navigation) =>
        new(_harness.CreateService(), navigation, new StubFilePicker(), new StartupStatusAccessor());

    /// <summary>A Workflow Selection screen over the same service.</summary>
    public WorkflowSelectionViewModel WorkflowSelection(RecordingNavigation navigation) =>
        new(Sessions, navigation);

    /// <summary>A session screen over the same service (Epic 11100 Part 3C3A §19).</summary>
    public SessionViewModel Session(RecordingNavigation navigation) => new(Sessions, navigation);

    /// <summary>
    /// The absolute path of a file inside the workspace, for a test that needs to corrupt one.
    /// </summary>
    /// <remarks>
    /// Only a test resolves a workspace-relative reference this way. Production code goes
    /// through <c>IWorkspace.ResolveAbsolute</c>, and the view models never see a path at all.
    /// </remarks>
    public string ResolveInWorkspace(string relativePath) =>
        System.IO.Path.Combine(_harness.Workspace.Root, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));

    public void Dispose() => _harness.Dispose();
}
