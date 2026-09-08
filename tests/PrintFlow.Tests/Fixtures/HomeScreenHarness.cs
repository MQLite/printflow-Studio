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

    /// <summary>How many times Home asked for Production Readiness (Epic 11500 Part C §3).</summary>
    public int EnvironmentReadinessCount { get; private set; }

    /// <summary>How many times a screen asked for Settings (SCRUM-11118).</summary>
    public int SettingsCount { get; private set; }

    public Task GoToSettingsAsync(CancellationToken cancellationToken)
    {
        SettingsCount++;
        return Task.CompletedTask;
    }

    public Task GoHomeAsync(CancellationToken cancellationToken)
    {
        GoHomeCount++;
        return Task.CompletedTask;
    }

    public Task GoToEnvironmentReadinessAsync(CancellationToken cancellationToken)
    {
        EnvironmentReadinessCount++;
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
    public Task<OperationResult<IReadOnlyList<SessionId>>> FindRecoveryCandidatesAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<OperationResult<IReadOnlyList<SessionId>>> FindCompletedSessionsAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException();

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
/// Wraps a real Meitu adapter and counts the calls (Epic 11100 Part 3C3B §17).
/// </summary>
/// <remarks>
/// "GENERATE_PRINT_TIFF makes no Meitu attempt" can be argued from the persisted attempt rows,
/// but only indirectly — that argument depends on the service always writing an attempt before
/// calling an adapter, which is the very kind of assumption a test should not lean on. Counting
/// the calls at the port says it outright. It delegates rather than stubs, so the session under
/// test still runs the ordinary fake pipeline.
/// </remarks>
internal sealed class CountingMeituProcessor : IMeituProcessor
{
    private readonly IMeituProcessor _inner;

    public CountingMeituProcessor(IMeituProcessor inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    public int CallCount { get; private set; }

    public string AdapterId => _inner.AdapterId;

    public AdapterExecutionMode Mode => _inner.Mode;

    public Task<OperationResult<AdapterOutput>> ProcessAsync(
        MeituRequest request, CancellationToken cancellationToken)
    {
        CallCount++;
        return _inner.ProcessAsync(request, cancellationToken);
    }
}

/// <summary>The service and screen a restart produced, so a test can drive both.</summary>
internal sealed record RestartedSession(ISessionService Sessions, SessionViewModel Screen);

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

    /// <param name="preset">
    /// A preset provider in place of the fixture-backed one, so a screen can be driven against
    /// naming patterns the renderer cannot honour (naming-contract fix §6, §11).
    /// </param>
    /// <param name="photoshop">
    /// A Photoshop adapter in place of the default fake, so a final-review screen test can be
    /// driven against a real accepted production TIFF (SCRUM-11104 §42). Held so
    /// <see cref="RestartSession"/> composes the restarted service with the same one — which is
    /// what lets a restart test prove no further Photoshop call happened (§39, §54).
    /// </param>
    public HomeScreenHarness(
        IWorkstationPresetProvider? preset = null,
        Func<IWorkspace, IPhotoshopOutputProcessor>? photoshop = null)
    {
        // A factory rather than an instance: the adapter needs the workspace, and the workspace
        // belongs to the harness being constructed. This is the only order in which a caller can
        // hold both the adapter it wants to assert on and the screens that drive it.
        _photoshop = photoshop?.Invoke(_harness.FileWorkspace);
        Meitu = new CountingMeituProcessor(_harness.FakeMeitu);
        Sessions = _harness.CreateServiceWithMeitu(Meitu, preset, photoshop: _photoshop);
        Home = new HomeViewModel(Sessions, Navigation, FilePicker, StartupStatus);
    }

    private readonly IPhotoshopOutputProcessor? _photoshop;

    /// <summary>The Photoshop adapter this harness composed, when a caller supplied one.</summary>
    public IPhotoshopOutputProcessor? Photoshop => _photoshop;

    /// <summary>
    /// The Meitu port the screens actually drive, counting its calls.
    /// </summary>
    /// <remarks>
    /// It delegates straight to <see cref="SessionServiceHarness.FakeMeitu"/>, so scripting a
    /// scenario on that instance still works and nothing about the pipeline changes. The count
    /// exists so "this workflow never reaches Meitu" can be asserted at the seam rather than
    /// inferred from the absence of a database row (Part 3C3B §17).
    /// </remarks>
    public CountingMeituProcessor Meitu { get; }

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
        new(Sessions, Previews, navigation);

    /// <summary>
    /// A session screen over a freshly built service — what "close the application and open it
    /// again" looks like from a test (Epic 11300 Part C2B2 §21).
    /// </summary>
    /// <remarks>
    /// The database, the workspace and the files are the same ones; only the service, the view
    /// model and every field either of them holds are new. That is what makes a restart test
    /// worth running: anything the screen remembered in memory is gone, so what it shows
    /// afterwards can only have come from persistence.
    /// </remarks>
    public RestartedSession RestartSession(RecordingNavigation navigation)
    {
        ISessionService restarted = _harness.CreateService(photoshop: _photoshop);
        return new RestartedSession(restarted, new SessionViewModel(restarted, Previews, TiffReviews, navigation));
    }

    /// <summary>A session screen over the same service (Epic 11100 Part 3C3A §19).</summary>

    public SessionViewModel Session(RecordingNavigation navigation) => new(Sessions, Previews, TiffReviews, navigation);

    /// <summary>The read-only image seam the session screen previews through (Part C1 §3).</summary>
    public IArtefactPreviewService Previews => _harness.Previews;

    /// <summary>The specialist production-TIFF review seam (SCRUM-11104 §43).</summary>
    public IProductionTiffReviewService TiffReviews => _harness.TiffReviews;

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
