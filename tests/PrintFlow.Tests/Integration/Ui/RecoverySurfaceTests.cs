using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using PrintFlow.App.Navigation;
using PrintFlow.App.Startup;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class RecoverySurfaceTests
{
    [Fact]
    public async Task Failed_manual_terminal_commit_keeps_visible_pending_entry_until_startup_repairs_it()
    {
        using SessionServiceHarness h = new();
        SessionId id = await Seed(h, "manual-commit-fault");
        SessionAggregate before = await Load(h, id);
        string selected = h.WriteSourcePng("saved-result.png");
        byte[] selectedBytes = File.ReadAllBytes(selected);
        FaultingRepository repository = new(h.Repository) { FailFromCommit = 3 };
        var home = Home(h.CreateService(repository: repository), new StubFilePicker(selected));
        await home.RefreshCommand.ExecuteAsync(null);
        await home.ImportRecoveryCommand.ExecuteAsync(home.RecoverySessions.Single());
        var row = home.RecoverySessions.Single();
        row.HasUnfinishedManualImport.ShouldBeTrue();
        row.HasNoActions.ShouldBeTrue("a persisted Running import could still be live; recovery cannot abandon or retry it");
        home.Notice.ShouldNotBeNullOrWhiteSpace();
        var saved = await Load(h, id);
        saved.Revisions.ShouldBe(before.Revisions);
        saved.Attempts.Single(a => a.Status == AttemptStatus.Interrupted).ShouldBe(before.Attempts.Single(a => a.Status == AttemptStatus.Interrupted));
        saved.Attempts.Single(a => a.Operation == OperationKind.ManualResultImport).Status.ShouldBe(AttemptStatus.Running);
        File.ReadAllBytes(selected).ShouldBe(selectedBytes);
        (await h.CreateService().ListRecoveryAsync(default)).Value.Single().Actions.ShouldBeEmpty();
        (await h.CreateRecoveryService(new FakeProcessLiveness(ProcessLiveness.Dead)).RecoverAsync(default)).IsSuccess.ShouldBeTrue();
        var recovered = (await h.CreateService().ListRecoveryAsync(default)).Value.Single();
        recovered.StepState.ShouldBe(StepState.Interrupted);
        recovered.Actions.ShouldContain(RecoveryAction.Restart);
    }

    [Fact]
    public async Task Failed_restart_commit_preserves_entry_then_later_ordinary_failure_does_not_resurrect_resolved_interruption()
    {
        using SessionServiceHarness h = new();
        SessionId id = await Seed(h, "fault");
        SessionAggregate before = await Load(h, id);
        FaultingRepository repository = new(h.Repository) { FailFromCommit = 1 };
        var home = Home(h.CreateService(repository: repository));
        await home.RefreshCommand.ExecuteAsync(null);
        await home.RestartRecoveryCommand.ExecuteAsync(home.RecoverySessions.Single());
        home.RecoverySessions.Count.ShouldBe(1);
        (await Load(h, id)).Attempts.ShouldBe(before.Attempts);
        (await Load(h, id)).ToSnapshot().CurrentStep!.State.ShouldBe(StepState.Interrupted);
        var service = h.CreateService();
        (await service.ResolveRecoveryAsync(id, RecoveryAction.Restart, null, "test", default)).IsSuccess.ShouldBeTrue();
        h.FakeMeitu.SetScenario(PrintFlow.Infrastructure.Adapters.Fake.FakeAdapterScenario.Timeout);
        (await service.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.Enhancement), "test", default)).IsFailure.ShouldBeTrue();
        (await service.ListRecoveryAsync(default)).Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task Unresolved_recovery_survives_restart_and_age_window_then_restart_is_clean_and_durable()
    {
        using SessionServiceHarness h = new();
        SessionId id = await Seed(h, "recover-old");
        SessionAggregate interrupted = await Load(h, id);
        h.Clock.Advance(TimeSpan.FromDays(40));
        var service = h.CreateService();
        (await service.ListRecentAsync(default)).Value.ShouldBeEmpty();
        var entries = (await service.ListRecoveryAsync(default)).Value;
        entries.Single().Id.ShouldBe(id);
        var reopened = (await h.CreateService().ListRecoveryAsync(default)).Value.Single();
        reopened.Actions.ShouldBe(entries.Single().Actions);
        (reopened with { Actions = entries.Single().Actions }).ShouldBe(entries.Single());
        (await Load(h, id)).Attempts.ShouldBe(interrupted.Attempts);
        string ordinary = h.WriteSourcePng("ordinary.png");
        (await service.ImportAsync(WorkflowType.PrepareAsset, ordinary, "ordinary", "test", default)).IsSuccess.ShouldBeTrue();
        (await service.ListRecoveryAsync(default)).Value.Count.ShouldBe(1);
        HomeViewModel home = Home(service);
        await home.RefreshCommand.ExecuteAsync(null);
        home.RecentSessions.ShouldNotContain(row => row.Id == id);
        await home.RestartRecoveryCommand.ExecuteAsync(home.RecoverySessions.Single());
        home.RecoverySessions.ShouldBeEmpty();
        SessionAggregate restarted = await Load(h, id);
        restarted.ToSnapshot().CurrentStep!.State.ShouldBe(StepState.Waiting);
        restarted.Attempts.ShouldBe(interrupted.Attempts);
        (await h.CreateService().ListRecoveryAsync(default)).Value.ShouldBeEmpty();
        (await h.Repository.GetAutomationLockAsync(default)).Value.IsHeld.ShouldBeFalse();
    }

    [Fact]
    public async Task Cancel_and_invalid_manual_result_remain_unresolved_then_valid_import_is_reviewable_and_durable()
    {
        using SessionServiceHarness h = new();
        SessionId id = await Seed(h, "manual");
        SessionAggregate original = await Load(h, id);
        StubFilePicker picker = new();
        RecordingNavigation navigation = new();
        var service = h.CreateService();
        HomeViewModel home = Home(service, picker, navigation);
        await home.RefreshCommand.ExecuteAsync(null);
        home.RecoverySessions.Single().CanImport.ShouldBeTrue();
        await home.ImportRecoveryCommand.ExecuteAsync(home.RecoverySessions.Single());
        (await Load(h, id)).Attempts.ShouldBe(original.Attempts);
        (await Load(h, id)).Session.State.ShouldBe(SessionState.Active);
        picker.Path = h.Workspace.CreateSourceFile("invalid.png", [1, 2, 3]);
        await home.ImportRecoveryCommand.ExecuteAsync(home.RecoverySessions.Single());
        home.RecoverySessions.Count.ShouldBe(1);
        (await h.CreateService().ListRecoveryAsync(default)).Value.Single().Actions.ShouldContain(RecoveryAction.Restart);
        picker.Path = h.WriteSourcePng("saved.png");
        byte[] bytes = File.ReadAllBytes(picker.Path);
        await home.ImportRecoveryCommand.ExecuteAsync(home.RecoverySessions.Single());
        home.RecoverySessions.ShouldBeEmpty();
        navigation.SessionFor!.Steps.Single(s => s.Step == StepKind.Enhancement).State.ShouldBe(StepState.ReviewRequired);
        SessionAggregate imported = await Load(h, id);
        Revision revision = imported.Revisions.Single(r => r.Operation == OperationKind.ManualResultImport);
        h.FileWorkspace.ResolveAbsolute(revision.File).ShouldNotBe(picker.Path);
        File.ReadAllBytes(picker.Path).ShouldBe(bytes);
        File.ReadAllBytes(h.FileWorkspace.ResolveAbsolute(revision.File)).ShouldBe(bytes);
        imported.Attempts.Single(a => a.Status == AttemptStatus.Interrupted).ShouldBe(original.Attempts.Single(a => a.Status == AttemptStatus.Interrupted));
        var reloaded = (await h.CreateService().LoadAsync(id, default)).Value;
        reloaded.CurrentArtefact!.RevisionId.ShouldBe(revision.Id);
        reloaded.CurrentArtefact.Sha256.ShouldBe(revision.Sha256);
        (await h.CreateService().ListRecoveryAsync(default)).Value.ShouldBeEmpty();
        (await service.ExecuteAsync(id, new WorkflowCommand.Reject(StepKind.Enhancement, revision.Sha256,
            PrintFlow.Domain.Reviews.RejectionReason.InsufficientResult, null), "test", default)).IsSuccess.ShouldBeTrue();
        (await h.CreateService().ListRecoveryAsync(default)).Value.ShouldBeEmpty("a successful import resolved the interruption even if its review is later rejected");
    }

    [Fact]
    public async Task Unsupported_step_has_no_import_and_abandon_preserves_source_snapshot_and_history()
    {
        using SessionServiceHarness h = new();
        SessionId id = await Seed(h, "trim", StepKind.Trim);
        SessionAggregate before = await Load(h, id);
        var root = before.Revisions.First(r => r.SourceRevisionId is null);
        byte[] snapshot = File.ReadAllBytes(h.FileWorkspace.ResolveAbsolute(root.File));
        string source = Path.Combine(h.Workspace.Root, "trim.png");
        byte[] sourceBytes = File.ReadAllBytes(source);
        var service = h.CreateService();
        (await service.ListRecoveryAsync(default)).Value.Single().Actions.ShouldNotContain(RecoveryAction.ManualResult);
        (await service.ResolveRecoveryAsync(id, RecoveryAction.ManualResult, source, "test", default)).IsFailure.ShouldBeTrue();
        HomeViewModel home = Home(service);
        await home.RefreshCommand.ExecuteAsync(null);
        await home.AbandonRecoveryCommand.ExecuteAsync(home.RecoverySessions.Single());
        home.RecoverySessions.ShouldBeEmpty();
        SessionAggregate after = await Load(h, id);
        after.Session.State.ShouldBe(SessionState.Abandoned);
        after.Attempts.ShouldBe(before.Attempts);
        File.ReadAllBytes(source).ShouldBe(sourceBytes);
        File.ReadAllBytes(h.FileWorkspace.ResolveAbsolute(root.File)).ShouldBe(snapshot);
        (await h.CreateService().ListRecoveryAsync(default)).Value.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("en-US", "Recovery needed", "Restart step", "Import manual result", "Abandon")]
    [InlineData("zh-CN", "需要恢复", "重新开始步骤", "导入手动结果", "放弃")]
    public async Task Home_renders_localised_recovery_and_stable_action_ids(string culture, string heading, string restart, string manual, string abandon)
    {
        using SessionServiceHarness h = new();
        await Seed(h, "render");
        var result = WpfRendering.Render(() =>
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var home = Home(h.CreateService());
            home.RefreshCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            home.RecoveryHeading.ShouldBe(heading);
            return new HomeView { DataContext = home };
        }, new Size(1000, 700), tree => tree.OfType<Button>()
            .Where(b => AutomationProperties.GetAutomationId(b).StartsWith("Home.Recovery.", StringComparison.Ordinal))
            .Select(b => (AutomationProperties.GetAutomationId(b), b.Content?.ToString())).ToArray());
        result.BindingErrors.ShouldBeEmpty();
        result.Facts.ShouldContain(("Home.Recovery.Restart", restart));
        result.Facts.ShouldContain(("Home.Recovery.ManualResult", manual));
        result.Facts.ShouldContain(("Home.Recovery.Abandon", abandon));
    }

    internal static HomeViewModel Home(ISessionService service, IFilePicker? picker = null, RecordingNavigation? navigation = null) =>
        new(service, navigation ?? new RecordingNavigation(), picker ?? new StubFilePicker(), new StartupStatusAccessor());

    internal static async Task<SessionAggregate> Load(SessionServiceHarness h, SessionId id) =>
        (await h.Repository.LoadAsync(id, default)).Value!;

    /// <summary>Real import and workflow metadata; synthetic persisted process death before any external adapter runs.</summary>
    internal static async Task<SessionId> Seed(SessionServiceHarness h, string name, StepKind step = StepKind.Enhancement)
    {
        var service = h.CreateService();
        var imported = await service.ImportAsync(WorkflowType.PrepareAsset, h.WriteSourcePng(name + ".png"), name, "test", default);
        imported.IsSuccess.ShouldBeTrue();
        SessionId id = imported.Value.Id;
        (await service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "test", default)).IsSuccess.ShouldBeTrue();
        if (step == StepKind.Trim)
        {
            (await service.ExecuteAsync(id, new WorkflowCommand.Skip(StepKind.Enhancement), "test", default)).IsSuccess.ShouldBeTrue();
            (await service.ExecuteAsync(id, new WorkflowCommand.Skip(StepKind.BackgroundRemoval), "test", default)).IsSuccess.ShouldBeTrue();
        }
        SessionAggregate aggregate = await Load(h, id);
        var context = new PrintFlow.Workflow.Commands.CommandContext(h.Clock.GetUtcNow(), "synthetic crash", ReviewId.From(Guid.NewGuid()), AttemptId.From(Guid.NewGuid()));
        var started = PrintFlow.Workflow.Engine.WorkflowEngine.Instance.Apply(aggregate.ToSnapshot(), new WorkflowCommand.StartStep(step), context);
        started.IsAccepted.ShouldBeTrue();
        ProcessingAttempt running = ProcessingAttempt.Start(context.NewAttemptId, id, step,
            aggregate.ToSnapshot().UpstreamRevisionOf(step), step == StepKind.Trim ? OperationKind.Trim : OperationKind.Enhance,
            "synthetic-no-external-app", h.Clock.GetUtcNow());
        (await h.Repository.CommitAsync(new SessionMutation(aggregate.Session, started.State.Steps, [], [], [running], [], [], null, null), default)).IsSuccess.ShouldBeTrue();
        (await h.CreateRecoveryService(new FakeProcessLiveness(ProcessLiveness.Dead)).RecoverAsync(default)).IsSuccess.ShouldBeTrue();
        return id;
    }
}
