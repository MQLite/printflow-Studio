using System.Windows.Automation;
using System.Windows.Controls;
using PrintFlow.App.Resources;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

/// <summary>
/// SCRUM-11154 F-V4, observed on the workstation: Recent "Abandon" acted on one click. Abandon now
/// opens an inline confirmation naming the exact job; only a fresh confirmation of that same
/// confirmation issues the one existing Abandon command. Remove from list is unchanged.
/// </summary>
[Collection(SqliteCollection.Name)]
public sealed class HomeAbandonConfirmationTests
{
    [Fact]
    public async Task Opening_and_keeping_the_job_reach_no_service_and_change_nothing()
    {
        using OperatorCultureScope culture = new("zh-CN");
        using SessionServiceHarness h = new();
        SessionId id = await FailureGuidanceUiTests.FailedEnhancementAsync(h);
        ScriptedFailureSessionService service = new(h.CreateService());
        HomeViewModel home = RecoverySurfaceTests.Home(h, service);
        await home.RefreshCommand.ExecuteAsync(null);
        RecentSessionRow row = home.RecentSessions.Single(r => r.Id == id);
        int before = service.MutatingCalls;

        await home.AbandonCommand.ExecuteAsync(row);

        home.IsConfirmingAbandon.ShouldBeTrue();
        home.PendingAbandon!.Id.ShouldBe(id);
        home.PendingAbandon.Question.ShouldContain("“" + row.DisplayName + "”");
        home.PendingAbandon.Question.ShouldContain("不会删除");
        home.AbandonKeepLabel.ShouldBe("保留任务");
        home.AbandonConfirmLabel.ShouldBe("确认放弃");
        service.MutatingCalls.ShouldBe(before, "opening the confirmation issues no command");

        home.KeepJobCommand.Execute(null);

        home.IsConfirmingAbandon.ShouldBeFalse();
        service.MutatingCalls.ShouldBe(before);
        (await RecoverySurfaceTests.Load(h, id)).Session.State.ShouldBe(SessionState.Active);
    }

    [Fact]
    public async Task A_fresh_confirmation_abandons_exactly_the_captured_job_once()
    {
        using OperatorCultureScope culture = new("zh-CN");
        using SessionServiceHarness h = new();
        SessionId first = await FailureGuidanceUiTests.FailedEnhancementAsync(h);
        SessionId second = await FailureGuidanceUiTests.FailedEnhancementAsync(h);
        ScriptedFailureSessionService service = new(h.CreateService());
        HomeViewModel home = RecoverySurfaceTests.Home(h, service);
        await home.RefreshCommand.ExecuteAsync(null);

        await home.AbandonCommand.ExecuteAsync(home.RecentSessions.Single(r => r.Id == first));
        AbandonConfirmation confirmation = home.PendingAbandon!;
        int before = service.ExecuteCalls;

        await home.ConfirmAbandonCommand.ExecuteAsync(confirmation);
        await home.ConfirmAbandonCommand.ExecuteAsync(confirmation); // A repeated activation of the same confirmation.

        service.ExecuteCalls.ShouldBe(before + 1);
        (await RecoverySurfaceTests.Load(h, first)).Session.State.ShouldBe(SessionState.Abandoned);
        (await RecoverySurfaceTests.Load(h, second)).Session.State.ShouldBe(SessionState.Active);
        home.Notice.ShouldBe(string.Format(Strings.Home_AbandonDone, confirmation.DisplayName));
        home.IsConfirmingAbandon.ShouldBeFalse();
    }

    [Fact]
    public async Task A_replaced_or_refreshed_confirmation_cannot_be_completed()
    {
        using OperatorCultureScope culture = new("zh-CN");
        using SessionServiceHarness h = new();
        SessionId first = await FailureGuidanceUiTests.FailedEnhancementAsync(h);
        SessionId second = await FailureGuidanceUiTests.FailedEnhancementAsync(h);
        ScriptedFailureSessionService service = new(h.CreateService());
        HomeViewModel home = RecoverySurfaceTests.Home(h, service);
        await home.RefreshCommand.ExecuteAsync(null);
        int before = service.MutatingCalls;

        await home.AbandonCommand.ExecuteAsync(home.RecentSessions.Single(r => r.Id == first));
        AbandonConfirmation stale = home.PendingAbandon!;
        // While it is open, another row's Abandon is inert: the lists moved under the pointer.
        await home.AbandonCommand.ExecuteAsync(home.RecentSessions.Single(r => r.Id == second));
        home.PendingAbandon.ShouldBeSameAs(stale);
        home.KeepJobCommand.Execute(null);
        await home.AbandonCommand.ExecuteAsync(home.RecentSessions.Single(r => r.Id == second));
        await home.ConfirmAbandonCommand.ExecuteAsync(stale);
        home.PendingAbandon!.Id.ShouldBe(second, "the stale event did not consume the current confirmation");

        AbandonConfirmation current = home.PendingAbandon;
        await home.RefreshCommand.ExecuteAsync(null);
        home.IsConfirmingAbandon.ShouldBeFalse("a rebuilt list withdraws the confirmation");
        await home.ConfirmAbandonCommand.ExecuteAsync(current);

        service.MutatingCalls.ShouldBe(before);
        (await RecoverySurfaceTests.Load(h, first)).Session.State.ShouldBe(SessionState.Active);
        (await RecoverySurfaceTests.Load(h, second)).Session.State.ShouldBe(SessionState.Active);
    }

    [Fact]
    public async Task A_job_that_became_ineligible_after_the_confirmation_opened_is_not_abandoned_again()
    {
        using OperatorCultureScope culture = new("zh-CN");
        using SessionServiceHarness h = new();
        SessionId id = await FailureGuidanceUiTests.FailedEnhancementAsync(h);
        ScriptedFailureSessionService service = new(h.CreateService());
        HomeViewModel home = RecoverySurfaceTests.Home(h, service);
        await home.RefreshCommand.ExecuteAsync(null);
        await home.AbandonCommand.ExecuteAsync(home.RecentSessions.Single(r => r.Id == id));
        AbandonConfirmation confirmation = home.PendingAbandon!;
        // Elsewhere (another screen or instance) the job stopped being abandonable.
        (await h.CreateService().ExecuteAsync(id, new WorkflowCommand.AbandonSession("Changed elsewhere."), "other", default)).IsSuccess.ShouldBeTrue();
        int before = service.ExecuteCalls;

        await home.ConfirmAbandonCommand.ExecuteAsync(confirmation);

        service.ExecuteCalls.ShouldBe(before, "the current eligibility re-read refused before any command");
        home.Notice.ShouldBe(string.Format(Strings.Resolve("Home_AbandonStale"), confirmation.DisplayName));
        (await RecoverySurfaceTests.Load(h, id)).Session.AbandonReason.ShouldBe("Changed elsewhere.");
    }

    [Fact]
    public async Task Recovery_abandon_is_confirmed_the_same_way()
    {
        using OperatorCultureScope culture = new("zh-CN");
        using SessionServiceHarness h = new();
        SessionId id = await RecoverySurfaceTests.Seed(h, "recovery-confirm");
        ScriptedFailureSessionService service = new(h.CreateService());
        HomeViewModel home = RecoverySurfaceTests.Home(h, service);
        await home.RefreshCommand.ExecuteAsync(null);

        await home.AbandonRecoveryCommand.ExecuteAsync(home.RecoverySessions.Single());
        home.PendingAbandon!.FromRecovery.ShouldBeTrue();
        home.KeepJobCommand.Execute(null);
        service.RecoveryCalls.ShouldBe(0);
        (await RecoverySurfaceTests.Load(h, id)).Session.State.ShouldBe(SessionState.Active);

        await home.AbandonRecoveryCommand.ExecuteAsync(home.RecoverySessions.Single());
        await home.ConfirmAbandonCommand.ExecuteAsync(home.PendingAbandon);
        service.RecoveryCalls.ShouldBe(1);
        (await RecoverySurfaceTests.Load(h, id)).Session.State.ShouldBe(SessionState.Abandoned);
    }

    [Fact]
    public async Task Remove_from_list_still_acts_directly_and_is_never_an_abandon()
    {
        using OperatorCultureScope culture = new("zh-CN");
        using SessionServiceHarness h = new();
        SessionId id = await FailureGuidanceUiTests.FailedEnhancementAsync(h);
        (await h.CreateService().ExecuteAsync(id, new WorkflowCommand.AbandonSession("Finished elsewhere."), "other", default)).IsSuccess.ShouldBeTrue();
        ScriptedFailureSessionService service = new(h.CreateService());
        HomeViewModel home = RecoverySurfaceTests.Home(h, service);
        await home.RefreshCommand.ExecuteAsync(null);
        RecentSessionRow row = home.RecentSessions.Single(r => r.Id == id);
        row.CanRemoveRecord.ShouldBeTrue();
        row.CanAbandon.ShouldBeFalse();

        await home.RemoveRecordCommand.ExecuteAsync(row);

        home.IsConfirmingAbandon.ShouldBeFalse();
        home.RecentSessions.ShouldNotContain(r => r.Id == id);
        service.ExecuteCalls.ShouldBe(0, "removing a record issues no workflow command");
    }

    [Fact]
    public async Task While_a_confirmation_is_open_the_lists_are_inert_and_their_actions_do_nothing()
    {
        using OperatorCultureScope culture = new("zh-CN");
        using SessionServiceHarness h = new();
        SessionId failed = await FailureGuidanceUiTests.FailedEnhancementAsync(h);
        SessionId finished = await FailureGuidanceUiTests.FailedEnhancementAsync(h);
        (await h.CreateService().ExecuteAsync(finished, new WorkflowCommand.AbandonSession("Finished elsewhere."), "other", default)).IsSuccess.ShouldBeTrue();
        await RecoverySurfaceTests.Seed(h, "inert-recovery");
        ScriptedFailureSessionService service = new(h.CreateService());
        RecordingNavigation navigation = new();
        HomeViewModel home = RecoverySurfaceTests.Home(h, service, navigation: navigation);
        await home.RefreshCommand.ExecuteAsync(null);
        await home.AbandonCommand.ExecuteAsync(home.RecentSessions.Single(r => r.Id == failed));
        int before = service.MutatingCalls;

        // A second click landing on a moved row: Remove, Resume, recovery Restart and Open.
        await home.RemoveRecordCommand.ExecuteAsync(home.RecentSessions.Single(r => r.Id == finished));
        await home.ResumeCommand.ExecuteAsync(home.RecentSessions.Single(r => r.Id == failed));
        await home.RestartRecoveryCommand.ExecuteAsync(home.RecoverySessions.Single());
        await home.OpenRecoveryCommand.ExecuteAsync(home.RecoverySessions.Single());

        service.MutatingCalls.ShouldBe(before);
        home.RecentSessions.ShouldContain(r => r.Id == finished, "the record was not removed");
        navigation.SessionFor.ShouldBeNull("nothing was opened");
        home.AreListsInteractive.ShouldBeFalse();
        bool listsEnabled = WpfRendering.RenderExpectingNoBindingErrors(() => new HomeView { DataContext = home }, WpfRendering.ReviewViewport,
            tree => ((Panel)tree.OfType<ScrollViewer>().Single(s => AutomationProperties.GetAutomationId(s) == "Home.Lists").Content).IsEnabled).Facts;
        listsEnabled.ShouldBeFalse();

        home.KeepJobCommand.Execute(null);
        home.AreListsInteractive.ShouldBeTrue();
    }

    [Fact]
    public async Task The_rendered_confirmation_has_no_default_action_and_targets_this_confirmation()
    {
        using OperatorCultureScope culture = new("zh-CN");
        using SessionServiceHarness h = new();
        SessionId id = await FailureGuidanceUiTests.FailedEnhancementAsync(h);
        HomeViewModel home = RecoverySurfaceTests.Home(h, h.CreateService());
        await home.RefreshCommand.ExecuteAsync(null);
        await home.AbandonCommand.ExecuteAsync(home.RecentSessions.Single(r => r.Id == id));
        string target = home.PendingAbandon!.GestureTarget;

        var facts = WpfRendering.RenderExpectingNoBindingErrors(() => new HomeView { DataContext = home }, WpfRendering.ReviewViewport, tree =>
        {
            Button keep = tree.OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "Home.AbandonConfirmation.Keep");
            ReviewApprovalButton confirm = tree.OfType<ReviewApprovalButton>().Single(b => AutomationProperties.GetAutomationId(b) == "Home.AbandonConfirmation.Confirm");
            TextBlock question = tree.OfType<TextBlock>().Single(t => AutomationProperties.GetAutomationId(t) == "Home.AbandonConfirmation.Question");
            return new
            {
                Visible = keep.IsVisible || keep.ActualWidth > 0,
                KeepIsCancel = keep.IsCancel, KeepIsDefault = keep.IsDefault, KeepName = AutomationProperties.GetName(keep),
                ConfirmIsDefault = confirm.IsDefault, ConfirmGuarded = confirm.IsGestureGuarded, confirm.TargetIdentity,
                ConfirmName = AutomationProperties.GetName(confirm), Question = question.Text,
            };
        }).Facts;

        facts.Visible.ShouldBeTrue();
        facts.KeepIsCancel.ShouldBeTrue("Esc keeps the job");
        facts.KeepIsDefault.ShouldBeFalse();
        facts.ConfirmIsDefault.ShouldBeFalse("Enter never abandons by default");
        facts.ConfirmGuarded.ShouldBeTrue();
        facts.TargetIdentity.ShouldBe(target);
        facts.KeepName.ShouldBe("保留任务");
        facts.ConfirmName.ShouldBe("确认放弃");
        facts.Question.ShouldBe(home.PendingAbandon.Question);
    }
}
