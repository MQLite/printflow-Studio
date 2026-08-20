using System.Globalization;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using PrintFlow.App.Composition;
using PrintFlow.App.Navigation;
using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Startup;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

/// <summary>
/// The smoke passes for Part 3C3A (§21) and Part 3C3B (§22), driven through the <b>real
/// composed application graph</b>.
/// </summary>
/// <remarks>
/// These are the operator journeys the slices are signed off against, walked end to end from
/// Home through Workflow Selection to the session screen, on synthetic files only: Smoke A–D
/// are the PREPARE_ASSET paths — success, reject/retry, skip, hand-off — and the three
/// <c>Production_smoke</c> passes below are the TIFF paths added in Part 3C3B.
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
    // Part 3C3B Smoke A — Generate Print TIFF, end to end (§22)
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// GENERATE_PRINT_TIFF from import to Completed, on synthetic files only
    /// (Epic 11100 Part 3C3B §22, Smoke A).
    /// </summary>
    /// <remarks>
    /// The journey a production operator actually walks, through the real composed graph:
    /// confirm the original, enter a size, classify the design for W1, generate, review the
    /// TIFF, complete. Every step goes through the screen's own controls, so this is a smoke
    /// pass over the wiring rather than a second copy of the service tests.
    /// </remarks>
    [Fact]
    public async Task Production_smoke_A_generate_print_tiff_from_import_to_completed()
    {
        using SmokeApplication app = await SmokeApplication.StartAsync();

        SessionViewModel session =
            await app.ImportAndChooseAsync("smoke-tiff-a.png", WorkflowType.GeneratePrintTiff);
        SessionId id = app.OpenSessionId;

        await session.ConfirmOriginalCommand.ExecuteAsync(null);
        (await app.LoadAsync(id)).ToSnapshot().CurrentStep!.Step.ShouldBe(StepKind.PrintDimensions);

        session.CanSetDimensions.ShouldBeTrue();
        session.SelectedWhiteUnderbaseChoice.ShouldBeNull();     // no default, ever

        await ConfirmSizeAndBranchAsync(session, widthMm: 200, WhiteUnderbaseBranch.W1_1px);

        session.CanRunStep.ShouldBeTrue();
        await session.RunStepCommand.ExecuteAsync(null);

        session.IsReviewRequired.ShouldBeTrue();
        session.IsFakeTiffOutput.ShouldBeTrue();                 // the synthetic-TIFF warning
        session.CanComplete.ShouldBeFalse();

        await session.ApproveCommand.ExecuteAsync(null);
        session.CanComplete.ShouldBeTrue();

        await session.CompleteCommand.ExecuteAsync(null);
        session.Notice.ShouldBeNull();

        SessionAggregate persisted = await app.LoadAsync(id);
        persisted.Session.State.ShouldBe(SessionState.Completed);

        PrintOutput output = persisted.Outputs.ShouldHaveSingleItem();
        output.ReviewState.ShouldBe(ReviewState.Approved);
        output.Dimensions.WidthMm.ShouldBe(200);
        output.Branch.ShouldBe(WhiteUnderbaseBranch.W1_1px);
        File.Exists(app.Workspace.ResolveAbsolute(output.File)).ShouldBeTrue();

        session.Outputs.ShouldHaveSingleItem();
    }

    // -------------------------------------------------------------------------------------
    // Part 3C3B Smoke B — another size from a completed session (§22)
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task Production_smoke_B_adding_another_size_leaves_the_first_output_in_place()
    {
        using SmokeApplication app = await SmokeApplication.StartAsync();

        SessionViewModel session =
            await app.ImportAndChooseAsync("smoke-tiff-b.png", WorkflowType.GeneratePrintTiff);
        SessionId id = app.OpenSessionId;

        await session.ConfirmOriginalCommand.ExecuteAsync(null);
        await ConfirmSizeAndBranchAsync(session, widthMm: 200, WhiteUnderbaseBranch.W1_1px);
        await session.RunStepCommand.ExecuteAsync(null);
        await session.ApproveCommand.ExecuteAsync(null);
        await session.CompleteCommand.ExecuteAsync(null);
        session.Notice.ShouldBeNull();

        session.CanAddAnotherSize.ShouldBeTrue();
        await session.AddAnotherSizeCommand.ExecuteAsync(null);

        // Reopened with both decisions cleared: the second output makes its own.
        session.CanSetDimensions.ShouldBeTrue();
        session.SelectedWhiteUnderbaseChoice.ShouldBeNull();
        (await app.LoadAsync(id)).Session.WhiteUnderbaseBranch.ShouldBeNull();

        await ConfirmSizeAndBranchAsync(session, widthMm: 150, WhiteUnderbaseBranch.W1_2px);
        await session.RunStepCommand.ExecuteAsync(null);
        await session.ApproveCommand.ExecuteAsync(null);
        session.Notice.ShouldBeNull();

        SessionAggregate persisted = await app.LoadAsync(id);
        persisted.Outputs.Count.ShouldBe(2);
        persisted.Outputs.ShouldAllBe(o => o.IsValid && o.ReviewState == ReviewState.Approved);

        // Both files are still on disk, and both are on screen.
        foreach (PrintOutput output in persisted.Outputs)
        {
            File.Exists(app.Workspace.ResolveAbsolute(output.File)).ShouldBeTrue();
        }

        session.Outputs.Count.ShouldBe(2);
    }

    // -------------------------------------------------------------------------------------
    // Part 3C3B Smoke C — the customer-design production tail (§22)
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task Production_smoke_C_customer_design_reaches_a_completed_tiff_from_an_approved_trim()
    {
        using SmokeApplication app = await SmokeApplication.StartAsync();

        SessionViewModel session =
            await app.ImportAndChooseAsync("smoke-tiff-c.png", WorkflowType.PrepareCustomerDesign);
        SessionId id = app.OpenSessionId;

        await session.ConfirmOriginalCommand.ExecuteAsync(null);
        await session.RunStepCommand.ExecuteAsync(null);          // Enhancement
        await session.ApproveCommand.ExecuteAsync(null);
        await session.RunStepCommand.ExecuteAsync(null);          // BackgroundRemoval
        await session.ApproveCommand.ExecuteAsync(null);
        await session.RunStepCommand.ExecuteAsync(null);          // Trim
        await session.ApproveCommand.ExecuteAsync(null);
        session.Notice.ShouldBeNull();

        SessionAggregate atDimensions = await app.LoadAsync(id);
        atDimensions.ToSnapshot().CurrentStep!.Step.ShouldBe(StepKind.PrintDimensions);
        atDimensions.Steps.Single(s => s.Step == StepKind.Trim).State.ShouldBe(StepState.Approved);

        await ConfirmSizeAndBranchAsync(session, widthMm: 240, WhiteUnderbaseBranch.W1_0px);
        await session.RunStepCommand.ExecuteAsync(null);
        await session.ApproveCommand.ExecuteAsync(null);
        await session.CompleteCommand.ExecuteAsync(null);
        session.Notice.ShouldBeNull();

        SessionAggregate persisted = await app.LoadAsync(id);
        persisted.Session.State.ShouldBe(SessionState.Completed);

        PrintOutput output = persisted.Outputs.ShouldHaveSingleItem();
        output.ReviewState.ShouldBe(ReviewState.Approved);
        output.Branch.ShouldBe(WhiteUnderbaseBranch.W1_0px);
        output.Dimensions.WidthMm.ShouldBe(240);
    }

    /// <summary>Enters a size and classifies the design, through the screen's own controls.</summary>
    private static async Task ConfirmSizeAndBranchAsync(
        SessionViewModel session, double widthMm, WhiteUnderbaseBranch branch)
    {
        session.WidthMmText = widthMm.ToString(CultureInfo.CurrentCulture);
        session.HeightMmText = 150d.ToString(CultureInfo.CurrentCulture);
        await session.SetDimensionsCommand.ExecuteAsync(null);
        session.Notice.ShouldBeNull();

        session.SelectedWhiteUnderbaseChoice =
            session.WhiteUnderbaseChoices.Single(choice => choice.Branch == branch);
        await session.SelectWhiteUnderbaseCommand.ExecuteAsync(null);
        session.Notice.ShouldBeNull();
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

        /// <summary>
        /// The application's own workspace, for the one thing a smoke pass must check on
        /// disk: that the file a produced output names is really there.
        /// </summary>
        public IWorkspace Workspace => Services.GetRequiredService<IWorkspace>();

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
