using System.IO;
using Microsoft.Extensions.DependencyInjection;
using PrintFlow.App.Composition;
using PrintFlow.App.Navigation;
using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Startup;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

/// <summary>
/// Home and Workflow Selection driven against the real session service, workspace and database
/// (Epic 11100 Part 3C2 §17).
/// </summary>
/// <remarks>
/// Deliberately a short list. These tests answer the questions this slice actually introduces —
/// does one file get in, do several get refused, is the recent list real, does resume come from
/// SQLite, does abandon go through the command path, is the workflow lock respected — and stop
/// there. A combinatorial suite over every button state would mostly re-test the engine, which
/// already has one.
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class HomeAndWorkflowSelectionTests
{
    // -------------------------------------------------------------------------------------
    // §8: Recent Processing
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task Recent_processing_asks_persistence_for_at_most_100_sessions_from_the_last_30_days()
    {
        using SessionServiceHarness harness = new();
        RecordingSessionRepository recording = new();

        SessionService service = new(
            WorkflowEngine.Instance,
            recording,
            harness.FileWorkspace,
            harness.FileInspector,
            harness.FakeMeitu,
            harness.FakePhotoshop,
            harness.Preset,
            harness.EnvironmentGate,
            SystemIdGenerator.Instance,
            harness.Clock);

        await service.ListRecentAsync(CancellationToken.None);

        recording.RequestedMaxCount.ShouldBe(100);
        (harness.Clock.GetUtcNow() - recording.RequestedSince!.Value).ShouldBe(TimeSpan.FromDays(30));
    }

    [Fact]
    public async Task Home_loads_recent_sessions_newest_first()
    {
        using HomeScreenHarness harness = new();

        await ImportAsync(harness, "oldest.png");
        harness.AdvanceClock(TimeSpan.FromMinutes(5));
        await ImportAsync(harness, "middle.png");
        harness.AdvanceClock(TimeSpan.FromMinutes(5));
        await ImportAsync(harness, "newest.png");

        await harness.Home.RefreshCommand.ExecuteAsync(null);

        harness.Home.RecentSessions
            .Select(row => row.DisplayName)
            .ShouldBe(["newest", "middle", "oldest"]);
        harness.Home.HasNoRecentSessions.ShouldBeFalse();
    }

    [Fact]
    public async Task A_recent_row_shows_operator_information_and_no_storage_detail()
    {
        using HomeScreenHarness harness = new();
        string source = await ImportAsync(harness, "design.png");

        await harness.Home.RefreshCommand.ExecuteAsync(null);

        RecentSessionRow row = harness.Home.RecentSessions.Single();
        row.DisplayName.ShouldBe("design");
        row.Workflow.ShouldNotBeNullOrWhiteSpace();
        row.CurrentStep.ShouldNotBeNullOrWhiteSpace();
        row.State.ShouldNotBeNullOrWhiteSpace();
        row.UpdatedAt.ShouldNotBeNullOrWhiteSpace();

        // The operator's own path never reaches the screen.
        string[] displayed = [row.DisplayName, row.Workflow, row.CurrentStep, row.State, row.UpdatedAt];
        displayed.ShouldAllBe(text => !text.Contains(Path.DirectorySeparatorChar));
        displayed.ShouldAllBe(text => !text.Contains(source, StringComparison.OrdinalIgnoreCase));
    }

    // -------------------------------------------------------------------------------------
    // §4: exactly one input file
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task One_dropped_file_is_accepted_and_starts_a_session()
    {
        using HomeScreenHarness harness = new();
        string source = harness.WriteSourceFile("dropped.png");

        await harness.Home.DropFilesCommand.ExecuteAsync(new[] { source });

        harness.Navigation.WorkflowSelectionFor.ShouldNotBeNull();
        harness.Home.Notice.ShouldBeNull();

        OperationResult<IReadOnlyList<SessionListItem>> listed =
            await harness.Sessions.ListRecentAsync(CancellationToken.None);
        listed.Value.Single().OutputName.Value.ShouldBe("dropped");
    }

    [Fact]
    public async Task Dropping_more_than_one_file_is_refused_and_creates_no_session()
    {
        using HomeScreenHarness harness = new();
        string first = harness.WriteSourceFile("first.png");
        string second = harness.WriteSourceFile("second.png");

        await harness.Home.DropFilesCommand.ExecuteAsync(new[] { first, second });

        // Refused, and refused visibly — not quietly reduced to the first file.
        harness.Home.Notice.ShouldNotBeNullOrWhiteSpace();
        harness.Home.Notice!.ShouldContain("2");
        harness.Navigation.WorkflowSelectionFor.ShouldBeNull();
        harness.Navigation.SessionFor.ShouldBeNull();

        OperationResult<IReadOnlyList<SessionListItem>> listed =
            await harness.Sessions.ListRecentAsync(CancellationToken.None);
        listed.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_empty_drop_is_refused_without_creating_a_session()
    {
        using HomeScreenHarness harness = new();

        await harness.Home.DropFilesCommand.ExecuteAsync(Array.Empty<string>());

        harness.Home.Notice.ShouldNotBeNullOrWhiteSpace();
        (await harness.Sessions.ListRecentAsync(CancellationToken.None)).Value.ShouldBeEmpty();
    }

    // -------------------------------------------------------------------------------------
    // §5, §6: import, then Workflow Selection
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task Choosing_one_file_persists_a_real_session_and_opens_workflow_selection()
    {
        using HomeScreenHarness harness = new();
        harness.FilePicker.Path = harness.WriteSourceFile("chosen.png");

        await harness.Home.ChooseFileCommand.ExecuteAsync(null);

        harness.FilePicker.CallCount.ShouldBe(1);
        SessionView imported = harness.Navigation.WorkflowSelectionFor.ShouldNotBeNull();

        // "Real Session persisted" means the database says so, not the view model.
        OperationResult<SessionAggregate?> stored =
            await harness.Inner.Repository.LoadAsync(imported.Id, CancellationToken.None);
        stored.Value.ShouldNotBeNull();
        stored.Value!.Session.OutputName.Value.ShouldBe("chosen");
        stored.Value.Revisions.Count.ShouldBe(1);
        stored.Value.Snapshot.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_cancelled_file_dialog_starts_nothing()
    {
        using HomeScreenHarness harness = new();
        harness.FilePicker.Path = null;

        await harness.Home.ChooseFileCommand.ExecuteAsync(null);

        harness.Navigation.WorkflowSelectionFor.ShouldBeNull();
        (await harness.Sessions.ListRecentAsync(CancellationToken.None)).Value.ShouldBeEmpty();
    }

    // -------------------------------------------------------------------------------------
    // §7: workflow selection goes through the existing command path
    // -------------------------------------------------------------------------------------

    [Theory]
    [InlineData(WorkflowType.PrepareAsset)]
    [InlineData(WorkflowType.PrepareCustomerDesign)]
    [InlineData(WorkflowType.GeneratePrintTiff)]
    public async Task Each_workflow_choice_is_persisted_through_the_session_service(WorkflowType chosen)
    {
        using HomeScreenHarness harness = new();
        harness.FilePicker.Path = harness.WriteSourceFile("choice.png");
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);

        RecordingNavigation navigation = new();
        WorkflowSelectionViewModel selection = harness.WorkflowSelection(navigation);
        selection.Open(harness.Navigation.WorkflowSelectionFor!);
        selection.CanSelect.ShouldBeTrue();

        WorkflowChoice choice = selection.Workflows.Single(w => w.Type == chosen);
        await selection.SelectCommand.ExecuteAsync(choice);

        selection.Notice.ShouldBeNull();
        SessionView opened = navigation.SessionFor.ShouldNotBeNull();
        opened.WorkflowType.ShouldBe(chosen);

        OperationResult<SessionAggregate?> stored =
            await harness.Inner.Repository.LoadAsync(opened.Id, CancellationToken.None);
        stored.Value!.Session.WorkflowType.ShouldBe(chosen);

        // The import survives the re-shape: choosing a workflow must not discard the file.
        stored.Value.Steps.Single(s => s.Step == StepKind.Import).State.ShouldBe(StepState.Approved);
        stored.Value.Revisions.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_locked_workflow_is_refused_rather_than_reassigned()
    {
        using HomeScreenHarness harness = new();
        harness.FilePicker.Path = harness.WriteSourceFile("locked.png");
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);
        SessionId id = harness.Navigation.WorkflowSelectionFor!.Id;

        // Produce a derived Revision — the fact that freezes the workflow choice.
        await Must(harness.Sessions.ExecuteAsync(
            id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None));
        await Must(harness.Sessions.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None));

        OperationResult<SessionView> reloaded = await harness.Sessions.LoadAsync(id, CancellationToken.None);
        RecordingNavigation navigation = new();
        WorkflowSelectionViewModel selection = harness.WorkflowSelection(navigation);
        selection.Open(reloaded.Value);

        // The engine's own AvailableCommands is what disables the buttons.
        selection.CanSelect.ShouldBeFalse();
        selection.Notice.ShouldNotBeNullOrWhiteSpace();

        // And a selection attempted anyway is refused by the command path, not silently applied.
        await selection.SelectCommand.ExecuteAsync(
            selection.Workflows.Single(w => w.Type == WorkflowType.GeneratePrintTiff));

        navigation.SessionFor.ShouldBeNull();
        OperationResult<SessionAggregate?> stored =
            await harness.Inner.Repository.LoadAsync(id, CancellationToken.None);
        stored.Value!.Session.WorkflowType.ShouldBe(WorkflowType.PrepareAsset);
    }

    // -------------------------------------------------------------------------------------
    // §9: resume
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task Resume_restores_the_session_from_the_database_after_a_restart()
    {
        using HomeScreenHarness harness = new();
        harness.FilePicker.Path = harness.WriteSourceFile("resumed.png");
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);
        SessionId id = harness.Navigation.WorkflowSelectionFor!.Id;

        // Progress the session past import, so "resumed from memory" and "resumed from SQLite"
        // could not produce the same answer.
        await Must(harness.Sessions.ExecuteAsync(
            id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None));

        // A fresh Home over a fresh session service: nothing survives except the database.
        RecordingNavigation navigation = new();
        HomeViewModel restarted = harness.RestartHome(navigation);
        await restarted.RefreshCommand.ExecuteAsync(null);

        RecentSessionRow row = restarted.RecentSessions.Single();
        row.CanContinueProcessing.ShouldBeTrue();
        row.OpenActionLabel.ShouldNotBeNullOrWhiteSpace();

        await restarted.ResumeCommand.ExecuteAsync(row);

        SessionView resumed = navigation.SessionFor.ShouldNotBeNull();
        resumed.Id.ShouldBe(id);
        resumed.State.ShouldBe(SessionState.Active);
        resumed.Steps.Single(s => s.Step == StepKind.Import).State.ShouldBe(StepState.Approved);
        resumed.Steps.Single(s => s.Step == StepKind.OriginalConfirmation).State.ShouldBe(StepState.Approved);
        resumed.CurrentStep!.Step.ShouldBe(StepKind.Enhancement);
    }

    [Fact]
    public async Task A_finished_session_offers_details_rather_than_resume()
    {
        using HomeScreenHarness harness = new();
        harness.FilePicker.Path = harness.WriteSourceFile("done.png");
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);
        SessionId id = harness.Navigation.WorkflowSelectionFor!.Id;

        await Must(harness.Sessions.ExecuteAsync(
            id, new WorkflowCommand.AbandonSession("test"), "tester", CancellationToken.None));

        await harness.Home.RefreshCommand.ExecuteAsync(null);

        RecentSessionRow row = harness.Home.RecentSessions.Single();
        row.CanContinueProcessing.ShouldBeFalse();
        row.CanAbandon.ShouldBeFalse();
        row.OpenActionLabel.ShouldNotBeNullOrWhiteSpace();

        // It still opens, read-only, from real persisted state.
        await harness.Home.ResumeCommand.ExecuteAsync(row);
        harness.Navigation.SessionFor!.State.ShouldBe(SessionState.Abandoned);
    }

    // -------------------------------------------------------------------------------------
    // §10: abandon
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task Abandon_persists_the_decision_and_keeps_the_source_and_the_history()
    {
        using HomeScreenHarness harness = new();
        string source = harness.WriteSourceFile("abandoned.png");
        harness.FilePicker.Path = source;
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);
        SessionId id = harness.Navigation.WorkflowSelectionFor!.Id;

        await harness.Home.RefreshCommand.ExecuteAsync(null);
        RecentSessionRow row = harness.Home.RecentSessions.Single();
        row.CanAbandon.ShouldBeTrue();

        await harness.Home.AbandonCommand.ExecuteAsync(row);

        OperationResult<SessionAggregate?> stored =
            await harness.Inner.Repository.LoadAsync(id, CancellationToken.None);
        SessionAggregate aggregate = stored.Value!;
        aggregate.Session.State.ShouldBe(SessionState.Abandoned);
        aggregate.Session.AbandonReason.ShouldNotBeNullOrWhiteSpace();

        // Nothing was deleted: the imported snapshot, its Revision and the attempt history stay.
        aggregate.Snapshot.ShouldNotBeNull();
        aggregate.Revisions.Count.ShouldBe(1);
        aggregate.Attempts.ShouldNotBeEmpty();
        File.Exists(harness.Inner.FileWorkspace.ResolveAbsolute(aggregate.Revisions[0].File)).ShouldBeTrue();

        // The operator's own file is untouched, as it was before.
        File.Exists(source).ShouldBeTrue();

        // And Recent Processing reflects the new state without a second refresh.
        RecentSessionRow after = harness.Home.RecentSessions.Single();
        after.CanAbandon.ShouldBeFalse();
        after.State.ShouldNotBe(row.State);
    }

    // -------------------------------------------------------------------------------------
    // §2, §16: the real composed application, from startup to the session screen
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task The_real_graph_navigates_Home_to_workflow_selection_to_the_session_screen()
    {
        using TempApplication application = new();
        using FakeSingleInstanceGuard guard = new(SingleInstanceOutcome.Acquired);
        StubFilePicker picker = new();

        using StartupResult started = await new ApplicationStartup(
                guard,
                application.ConfigurationFilePath,
                services => services.AddSingleton<IFilePicker>(picker))
            .RunAsync(CancellationToken.None);

        started.Status.CanShowShell.ShouldBeTrue();

        ShellViewModel shell = started.Services!.GetRequiredService<ShellViewModel>();
        INavigationService navigation = started.Services!.GetRequiredService<INavigationService>();

        // Nothing is shown until startup says the shell may open.
        shell.Current.ShouldBeNull();

        await navigation.GoHomeAsync(CancellationToken.None);
        HomeViewModel home = shell.Current.ShouldBeOfType<HomeViewModel>();
        home.RecentSessions.ShouldBeEmpty();

        picker.Path = Path.Combine(application.WorkspaceRoot, "smoke-source.png");
        await File.WriteAllBytesAsync(picker.Path, SyntheticImages.Png(8, 6, alpha: true));

        await home.ChooseFileCommand.ExecuteAsync(null);
        WorkflowSelectionViewModel selection = shell.Current.ShouldBeOfType<WorkflowSelectionViewModel>();
        selection.CanSelect.ShouldBeTrue();

        await selection.SelectCommand.ExecuteAsync(
            selection.Workflows.Single(w => w.Type == WorkflowType.PrepareCustomerDesign));

        SessionViewModel session = shell.Current.ShouldBeOfType<SessionViewModel>();
        session.Steps.ShouldNotBeEmpty();
        session.IsReadOnly.ShouldBeFalse();

        // Back to Home, and the work that was just started is listed.
        await session.BackToHomeCommand.ExecuteAsync(null);
        HomeViewModel returned = shell.Current.ShouldBeOfType<HomeViewModel>();
        returned.RecentSessions.Single().DisplayName.ShouldBe("smoke-source");
    }

    // -------------------------------------------------------------------------------------

    private static async Task<string> ImportAsync(HomeScreenHarness harness, string fileName)
    {
        string source = harness.WriteSourceFile(fileName);
        harness.FilePicker.Path = source;
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);
        return source;
    }

    private static async Task Must(Task<OperationResult<SessionView>> resultTask)
    {
        OperationResult<SessionView> result = await resultTask;
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : "");
    }
}
