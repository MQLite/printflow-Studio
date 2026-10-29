using System.Text.Json;
using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Tests.Integration.Persistence;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class GuidedImportResumeTests
{
    [Theory]
    [InlineData(WorkflowType.PrepareAsset, false)]
    [InlineData(WorkflowType.PrepareAsset, true)]
    [InlineData(WorkflowType.PrepareCustomerDesign, true)]
    [InlineData(WorkflowType.GeneratePrintTiff, true)]
    public async Task Continuing_an_unconfirmed_original_requires_explicit_route_choice_without_guessing_prior_intent(WorkflowType route, bool previouslySelected)
    {
        using HomeScreenHarness h = new();
        var imported = await GuidedImportRouteTests.Import(h, "choose-before-continuing.png");
        if (previouslySelected)
            (await h.Sessions.ExecuteAsync(imported.Id, new WorkflowCommand.SelectWorkflow(route), "tester", default)).IsSuccess.ShouldBeTrue();
        var before = await Snapshot(h, imported.Id);
        await h.Home.RefreshCommand.ExecuteAsync(null);
        await h.Home.ResumeCommand.ExecuteAsync(h.Home.RecentSessions.Single());
        h.Navigation.SessionFor.ShouldBeNull();
        h.Navigation.WorkflowSelectionFor.ShouldNotBeNull().Id.ShouldBe(imported.Id);
        (await Snapshot(h, imported.Id)).ShouldBe(before, "navigation cannot infer intent or write a route");

        var selection = h.WorkflowSelection(h.Navigation); selection.Open(h.Navigation.WorkflowSelectionFor!);
        await selection.SelectCommand.ExecuteAsync(selection.Workflows.Single(w => w.Type == route));
        h.Navigation.SessionFor.ShouldNotBeNull().WorkflowType.ShouldBe(route);
    }

    [Theory]
    [InlineData(WorkflowType.PrepareAsset)]
    [InlineData(WorkflowType.PrepareCustomerDesign)]
    [InlineData(WorkflowType.GeneratePrintTiff)]
    public async Task Continuing_after_original_confirmation_preserves_the_actual_next_step_even_while_route_is_still_changeable(WorkflowType route)
    {
        using HomeScreenHarness h = new();
        var imported = (await h.Sessions.ImportAsync(route, h.WriteSourceFile("confirmed.png"), null, "tester", default)).Value!;
        var confirmed = (await h.Sessions.ExecuteAsync(imported.Id, new WorkflowCommand.ConfirmOriginal(), "tester", default)).Value!;
        confirmed.AvailableCommands.ShouldContain(CommandKind.SelectWorkflow);
        var before = await Snapshot(h, imported.Id);
        await h.Home.RefreshCommand.ExecuteAsync(null);
        await h.Home.ResumeCommand.ExecuteAsync(h.Home.RecentSessions.Single());
        h.Navigation.WorkflowSelectionFor.ShouldBeNull();
        h.Navigation.SessionFor.ShouldNotBeNull().CurrentStep.ShouldBe(confirmed.CurrentStep);
        (await Snapshot(h, imported.Id)).ShouldBe(before);
    }

    [Fact]
    public async Task Prepared_source_review_is_resumed_without_reselecting_a_locked_route()
    {
        using HomeScreenHarness h = new();
        var service = h.Inner.CreateServiceWithPhotoshop(new PsdPreparationWorkflowTests.PsdProcessor(h.Inner.FileWorkspace));
        string source = h.Inner.Workspace.CreateSourceFile("prepared.psd", PsdInputPreparationTests.RgbCompositePsd());
        var imported = (await service.ImportAsync(WorkflowType.PrepareAsset, source, null, "tester", default)).Value!;
        (await service.ExecuteAsync(imported.Id, new WorkflowCommand.StartStep(StepKind.OriginalConfirmation), "tester", default)).IsSuccess.ShouldBeTrue();
        var prepared = (await service.LoadAsync(imported.Id, default)).Value!;
        prepared.CurrentStep!.State.ShouldBe(StepState.ReviewRequired);
        prepared.AvailableCommands.ShouldNotContain(CommandKind.SelectWorkflow);
        var before = await Snapshot(h, imported.Id);
        var home = NewHome(h, service);
        await home.RefreshCommand.ExecuteAsync(null); await home.ResumeCommand.ExecuteAsync(home.RecentSessions.Single());
        h.Navigation.WorkflowSelectionFor.ShouldBeNull();
        h.Navigation.SessionFor.ShouldNotBeNull().CurrentStep.ShouldBe(prepared.CurrentStep);
        (await Snapshot(h, imported.Id)).ShouldBe(before);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Recovery_keeps_interrupted_work_in_place_and_rechecks_route_only_after_original_restart(bool original, bool restart)
    {
        using HomeScreenHarness h = new();
        SessionId id = original ? await SeedInterruptedSource(h) : await RecoverySurfaceTests.Seed(h.Inner, "interrupted-next-step");
        var before = (await h.Inner.Repository.LoadAsync(id, default)).Value!;
        var meitu = new CountingMeituProcessor(h.Inner.FakeMeitu);
        var home = NewHome(h, h.Inner.CreateServiceWithMeitu(meitu));
        await home.RefreshCommand.ExecuteAsync(null);
        if (restart) await home.RestartRecoveryCommand.ExecuteAsync(home.RecoverySessions.Single());
        else await home.OpenRecoveryCommand.ExecuteAsync(home.RecoverySessions.Single());
        if (original && restart)
        {
            h.Navigation.SessionFor.ShouldBeNull();
            h.Navigation.WorkflowSelectionFor.ShouldNotBeNull().Id.ShouldBe(id);
        }
        else
        {
            h.Navigation.WorkflowSelectionFor.ShouldBeNull();
            h.Navigation.SessionFor.ShouldNotBeNull().CurrentStep!.Step.ShouldBe(original ? StepKind.OriginalConfirmation : StepKind.Enhancement);
            h.Navigation.SessionFor!.CurrentStep!.State.ShouldBe(restart ? StepState.Waiting : StepState.Interrupted);
        }
        var after = (await h.Inner.Repository.LoadAsync(id, default)).Value!;
        after.Attempts.ShouldBe(before.Attempts); after.Revisions.ShouldBe(before.Revisions);
        if (!restart) JsonSerializer.Serialize(after).ShouldBe(JsonSerializer.Serialize(before));
        meitu.CallCount.ShouldBe(0);
    }

    private static HomeViewModel NewHome(HomeScreenHarness h, ISessionService service) =>
        new(service, h.Previews, h.Navigation, h.FilePicker, h.StartupStatus, h.Readiness);
    private static async Task<string> Snapshot(HomeScreenHarness h, SessionId id) =>
        JsonSerializer.Serialize((await h.Inner.Repository.LoadAsync(id, default)).Value);

    private static async Task<SessionId> SeedInterruptedSource(HomeScreenHarness h)
    {
        string source = h.Inner.Workspace.CreateSourceFile("interrupted-source.psd", PsdInputPreparationTests.RgbCompositePsd());
        var imported = (await h.Sessions.ImportAsync(WorkflowType.PrepareAsset, source, null, "tester", default)).Value!;
        var aggregate = (await h.Inner.Repository.LoadAsync(imported.Id, default)).Value!;
        var context = new CommandContext(h.Inner.Clock.GetUtcNow(), "synthetic interrupted source", ReviewId.From(Guid.NewGuid()), AttemptId.From(Guid.NewGuid()));
        var started = WorkflowEngine.Instance.Apply(aggregate.ToSnapshot(), new WorkflowCommand.StartStep(StepKind.OriginalConfirmation), context);
        started.IsAccepted.ShouldBeTrue();
        var running = ProcessingAttempt.Start(context.NewAttemptId, imported.Id, StepKind.OriginalConfirmation,
            aggregate.ToSnapshot().UpstreamRevisionOf(StepKind.OriginalConfirmation), OperationKind.PreparePsd,
            "synthetic-no-external-app", h.Inner.Clock.GetUtcNow());
        (await h.Inner.Repository.CommitAsync(new SessionMutation(aggregate.Session, started.State.Steps, [], [], [running], [], [], null, null), default)).IsSuccess.ShouldBeTrue();
        (await h.Inner.CreateRecoveryService(new FakeProcessLiveness(ProcessLiveness.Dead)).RecoverAsync(default)).IsSuccess.ShouldBeTrue();
        return imported.Id;
    }
}
