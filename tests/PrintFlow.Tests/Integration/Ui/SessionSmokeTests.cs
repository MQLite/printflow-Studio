using System.IO;
using Microsoft.Extensions.DependencyInjection;
using PrintFlow.App.Composition;
using PrintFlow.App.Navigation;
using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Startup;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

/// <summary>
/// The Part 3C3A smoke passes (§21), driven through the <b>real composed application graph</b>.
/// </summary>
/// <remarks>
/// These are the four operator journeys the slice is signed off against — success,
/// reject/retry, skip, hand-off — walked end to end from Home through Workflow Selection to the
/// session screen, on synthetic files only.
/// <para>
/// What makes them a smoke pass rather than another unit of the suite above is what is
/// <i>not</i> substituted: the whole graph comes from <see cref="ApplicationStartup"/> — the
/// same configuration load, directory creation, migration run, preset verification, crash
/// recovery and <c>ServiceRegistration</c> the shipped application performs — and navigation is
/// the real <see cref="NavigationService"/> resolving real screens from the container. Only the
/// two things a test cannot have are stood in for: the single-instance guard, and the modal
/// file dialog.
/// </para>
/// <para>
/// The remaining manual step is looking at the window, which
/// <c>ViewRenderingTests</c> covers by rendering the screens for real and failing on any
/// binding error.
/// </para>
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class SessionSmokeTests
{
    // -------------------------------------------------------------------------------------
    // Smoke A — success: Home -> PREPARE_ASSET -> Confirm -> Run -> Review -> Approve
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task Smoke_A_import_confirm_run_enhancement_and_approve()
    {
        using SmokeApplication app = await SmokeApplication.StartAsync();

        SessionViewModel session = await app.ImportAndChooseAsync("smoke-a.png", WorkflowType.PrepareAsset);
        SessionId id = app.OpenSessionId;

        session.CanConfirmOriginal.ShouldBeTrue();
        await session.ConfirmOriginalCommand.ExecuteAsync(null);

        session.CanRunStep.ShouldBeTrue();
        await session.RunStepCommand.ExecuteAsync(null);

        // The review state the operator would be looking at.
        session.IsReviewRequired.ShouldBeTrue();
        session.HasArtefact.ShouldBeTrue();
        session.ArtefactIsInput.ShouldBeFalse();
        session.IsFakeProcessing.ShouldBeTrue();

        await session.ApproveCommand.ExecuteAsync(null);
        session.Notice.ShouldBeNull();

        // Displayed and persisted agree.
        SessionAggregate persisted = await app.LoadAsync(id);
        persisted.Steps.Single(s => s.Step == StepKind.Enhancement).State.ShouldBe(StepState.Approved);
        persisted.Reviews.Single(r => r.Step == StepKind.Enhancement).IsApproved.ShouldBeTrue();
        persisted.ToSnapshot().CurrentStep!.Step.ShouldBe(StepKind.BackgroundRemoval);
    }

    // -------------------------------------------------------------------------------------
    // Smoke B — reject/retry: Run -> Reject -> Retry -> Run -> Approve, on a new attempt
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task Smoke_B_reject_retry_and_approve_background_removal_on_a_fresh_attempt()
    {
        using SmokeApplication app = await SmokeApplication.StartAsync();

        SessionViewModel session = await app.ImportAndChooseAsync("smoke-b.png", WorkflowType.PrepareAsset);
        SessionId id = app.OpenSessionId;

        await session.ConfirmOriginalCommand.ExecuteAsync(null);
        await session.SkipCommand.ExecuteAsync(null);        // past Enhancement, onto BackgroundRemoval

        (await app.LoadAsync(id)).ToSnapshot().CurrentStep!.Step.ShouldBe(StepKind.BackgroundRemoval);

        await session.RunStepCommand.ExecuteAsync(null);
        session.IsReviewRequired.ShouldBeTrue();

        await session.RejectCommand.ExecuteAsync(null);
        (await app.LoadAsync(id)).Steps.Single(s => s.Step == StepKind.BackgroundRemoval)
            .State.ShouldBe(StepState.RetryRequired);

        AttemptId firstAttempt = (await app.LoadAsync(id))
            .Attempts.Single(a => a.Step == StepKind.BackgroundRemoval).Id;

        await session.RetryCommand.ExecuteAsync(null);
        (await app.LoadAsync(id)).Steps.Single(s => s.Step == StepKind.BackgroundRemoval)
            .State.ShouldBe(StepState.Waiting);

        await session.RunStepCommand.ExecuteAsync(null);
        await session.ApproveCommand.ExecuteAsync(null);
        session.Notice.ShouldBeNull();

        SessionAggregate persisted = await app.LoadAsync(id);
        persisted.Steps.Single(s => s.Step == StepKind.BackgroundRemoval).State.ShouldBe(StepState.Approved);

        // A genuinely new attempt, and both decisions survive as history.
        List<ProcessingAttempt> attempts =
            [.. persisted.Attempts.Where(a => a.Step == StepKind.BackgroundRemoval)];
        attempts.Count.ShouldBe(2);
        attempts.ShouldContain(a => a.Id != firstAttempt);

        persisted.Reviews.Count(r => r.Step == StepKind.BackgroundRemoval).ShouldBe(2);
    }

    // -------------------------------------------------------------------------------------
    // Smoke C — skip: a fresh session, both skippable steps skipped, no Revisions created
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task Smoke_C_skipping_both_skippable_steps_creates_no_revisions()
    {
        using SmokeApplication app = await SmokeApplication.StartAsync();

        SessionViewModel session = await app.ImportAndChooseAsync("smoke-c.png", WorkflowType.PrepareAsset);
        SessionId id = app.OpenSessionId;

        await session.ConfirmOriginalCommand.ExecuteAsync(null);
        await session.SkipCommand.ExecuteAsync(null);        // Enhancement
        await session.SkipCommand.ExecuteAsync(null);        // BackgroundRemoval
        session.Notice.ShouldBeNull();

        SessionAggregate persisted = await app.LoadAsync(id);
        persisted.Steps.Single(s => s.Step == StepKind.Enhancement).State.ShouldBe(StepState.Skipped);
        persisted.Steps.Single(s => s.Step == StepKind.BackgroundRemoval).State.ShouldBe(StepState.Skipped);

        // No Revision for either skipped step: the only one is the imported original, and it
        // is what Trim will consume (MVP design §7.2).
        persisted.Revisions.Count.ShouldBe(1);
        persisted.Revisions.Single().IsRoot.ShouldBeTrue();
        persisted.Attempts.Count(a => a.Step is StepKind.Enhancement or StepKind.BackgroundRemoval).ShouldBe(0);

        persisted.ToSnapshot().CurrentStep!.Step.ShouldBe(StepKind.Trim);
    }

    // -------------------------------------------------------------------------------------
    // Smoke D — hand-off: automation ends and the run actions disappear
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task Smoke_D_handing_off_ends_automation_and_withdraws_the_run_actions()
    {
        using SmokeApplication app = await SmokeApplication.StartAsync();

        SessionViewModel session = await app.ImportAndChooseAsync("smoke-d.png", WorkflowType.PrepareAsset);
        SessionId id = app.OpenSessionId;

        await session.ConfirmOriginalCommand.ExecuteAsync(null);
        await session.RunStepCommand.ExecuteAsync(null);
        session.IsReviewRequired.ShouldBeTrue();

        session.CanHandOff.ShouldBeTrue();
        await session.HandOffCommand.ExecuteAsync(null);
        session.Notice.ShouldBeNull();

        session.IsHandedOff.ShouldBeTrue();
        session.CanRunStep.ShouldBeFalse();
        session.CanApprove.ShouldBeFalse();
        session.CanReject.ShouldBeFalse();
        session.CanRetry.ShouldBeFalse();
        session.CanSkip.ShouldBeFalse();

        SessionAggregate persisted = await app.LoadAsync(id);
        persisted.Session.State.ShouldBe(SessionState.HandedOff);

        AutomationLockState automationLock =
            (await app.Repository.GetAutomationLockAsync(CancellationToken.None)).Value;
        automationLock.IsHeld.ShouldBeFalse();

        // Home still lists it, and still offers a way in — hand-off ended automation, not the
        // record (MVP design §6.5).
        HomeViewModel home = app.Services.GetRequiredService<HomeViewModel>();
        await home.RefreshCommand.ExecuteAsync(null);
        RecentSessionRow row = home.RecentSessions.Single(r => r.Id == id);
        row.CanContinueProcessing.ShouldBeFalse();
        row.CanAbandon.ShouldBeTrue();
    }

    // -------------------------------------------------------------------------------------

    /// <summary>
    /// A started application: the real startup sequence, the real container, the real
    /// navigation service, and a scripted file dialog.
    /// </summary>
    private sealed class SmokeApplication : IDisposable
    {
        private readonly TempApplication _layout;
        private readonly FakeSingleInstanceGuard _guard;
        private readonly StartupResult _startup;
        private readonly StubFilePicker _picker;

        private SmokeApplication(
            TempApplication layout, FakeSingleInstanceGuard guard, StartupResult startup, StubFilePicker picker)
        {
            _layout = layout;
            _guard = guard;
            _startup = startup;
            _picker = picker;
        }

        public ServiceProvider Services => _startup.Services!;

        public ISessionRepository Repository => Services.GetRequiredService<ISessionRepository>();

        /// <summary>The session the navigation service currently has a screen open for.</summary>
        public SessionId OpenSessionId { get; private set; }

        public static async Task<SmokeApplication> StartAsync()
        {
            TempApplication layout = new();
            FakeSingleInstanceGuard guard = new(SingleInstanceOutcome.Acquired);
            StubFilePicker picker = new();

            StartupResult startup = await new ApplicationStartup(
                    guard,
                    layout.ConfigurationFilePath,
                    services => services.AddSingleton<IFilePicker>(picker))
                .RunAsync(CancellationToken.None);

            startup.Status.CanShowShell.ShouldBeTrue(
                startup.Status.Failure?.ToString() ?? "startup refused to show the shell");

            return new SmokeApplication(layout, guard, startup, picker);
        }

        /// <summary>
        /// Walks Home to Workflow Selection to the session screen, exactly as an operator
        /// would, and returns the live session view model the navigation service resolved.
        /// </summary>
        public async Task<SessionViewModel> ImportAndChooseAsync(string fileName, WorkflowType workflow)
        {
            INavigationService navigation = Services.GetRequiredService<INavigationService>();
            await navigation.GoHomeAsync(CancellationToken.None);

            HomeViewModel home = (HomeViewModel)navigation.Current!;
            _picker.Path = WriteSyntheticFile(fileName);
            await home.ChooseFileCommand.ExecuteAsync(null);
            home.Notice.ShouldBeNull();

            WorkflowSelectionViewModel selection = navigation.Current.ShouldBeOfType<WorkflowSelectionViewModel>();
            selection.CanSelect.ShouldBeTrue();
            await selection.SelectCommand.ExecuteAsync(
                selection.Workflows.Single(choice => choice.Type == workflow));
            selection.Notice.ShouldBeNull();

            SessionViewModel session = navigation.Current.ShouldBeOfType<SessionViewModel>();

            await home.RefreshCommand.ExecuteAsync(null);
            OpenSessionId = home.RecentSessions[0].Id;

            return session;
        }

        public async Task<SessionAggregate> LoadAsync(SessionId id) =>
            (await Repository.LoadAsync(id, CancellationToken.None)).Value!;

        public void Dispose()
        {
            _startup.Dispose();
            _guard.Dispose();
            _layout.Dispose();
        }

        /// <summary>
        /// A synthetic PNG outside the workspace, standing in for the operator's own file.
        /// </summary>
        /// <remarks>
        /// Written under the OS temp directory and never committed: no customer or production
        /// file is involved in any smoke pass (§21, task §50).
        /// </remarks>
        private static string WriteSyntheticFile(string fileName)
        {
            string directory = Path.Combine(Path.GetTempPath(), "PrintFlowTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);

            string path = Path.Combine(directory, fileName);
            File.WriteAllBytes(path, SyntheticImages.Png(8, 6, alpha: true));
            return path;
        }
    }
}
