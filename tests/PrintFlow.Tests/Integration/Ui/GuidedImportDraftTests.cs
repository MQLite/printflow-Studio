using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class GuidedImportDraftTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Untouched_or_reverted_names_leave_without_a_draft_decision(bool editThenRevert)
    {
        using HomeScreenHarness h = new();
        var m = h.WorkflowSelection(h.Navigation); m.Open(await GuidedImportRouteTests.Import(h, "clean-name.png"));
        if (editThenRevert) { m.OutputName = "temporary draft"; m.OutputName = m.SessionName; }
        await m.BackToHomeCommand.ExecuteAsync(null);
        m.PendingLeave.ShouldBeNull(); h.Navigation.GoHomeCount.ShouldBe(1);
    }

    [Fact]
    public async Task Keep_preserves_the_draft_and_only_the_current_discard_request_may_leave_without_writing()
    {
        using HomeScreenHarness h = new();
        var imported = await GuidedImportRouteTests.Import(h, "kept-name.png");
        var before = JsonSerializer.Serialize((await h.Inner.Repository.LoadAsync(imported.Id, default)).Value);
        var m = h.WorkflowSelection(h.Navigation); m.Open(imported); m.OutputName = "kept draft";
        await m.BackToHomeCommand.ExecuteAsync(null); var old = m.PendingLeave.ShouldNotBeNull();
        m.KeepDraftCommand.Execute(old);
        m.OutputName.ShouldBe("kept draft"); m.IsConfirmingDraftLeave.ShouldBeFalse(); h.Navigation.GoHomeCount.ShouldBe(0);
        await m.BackToHomeCommand.ExecuteAsync(null); var current = m.PendingLeave.ShouldNotBeNull();
        await m.DiscardDraftAndLeaveCommand.ExecuteAsync(old);
        h.Navigation.GoHomeCount.ShouldBe(0); ReferenceEquals(m.PendingLeave, current).ShouldBeTrue();
        await m.DiscardDraftAndLeaveCommand.ExecuteAsync(current);
        h.Navigation.GoHomeCount.ShouldBe(1); m.OutputName.ShouldBe(imported.OutputName.Value);
        JsonSerializer.Serialize((await h.Inner.Repository.LoadAsync(imported.Id, default)).Value).ShouldBe(before);
    }

    [Fact]
    public async Task Old_leave_request_and_retained_provider_cannot_discard_the_reopened_tasks_name()
    {
        using HomeScreenHarness h = new();
        var a = await GuidedImportRouteTests.Import(h, "draft-a.png"); var b = await GuidedImportRouteTests.Import(h, "draft-b.png");
        var m = h.WorkflowSelection(h.Navigation); m.Open(a); m.OutputName = "A draft"; await m.BackToHomeCommand.ExecuteAsync(null);
        var old = m.PendingLeave.ShouldNotBeNull();
        WpfRendering.RenderExpectingNoBindingErrors(() => new WorkflowSelectionView { DataContext = m }, new Size(1000, 700), tree =>
        {
            var discard = GuidedImportHomeTests.Find<Button>(tree, "WorkflowSelection.DiscardDraft");
            var provider = GuidedSessionInputTests.Provider(discard);
            m.Deactivate(); m.Open(b); m.OutputName = "B draft"; m.BackToHomeCommand.Execute(null); tree.Root.UpdateLayout();
            var current = m.PendingLeave.ShouldNotBeNull();
            m.KeepDraftCommand.Execute(old); m.DiscardDraftAndLeaveCommand.Execute(old);
            provider.Invoke(); GuidedImportHomeTests.Drain();
            h.Navigation.GoHomeCount.ShouldBe(0); m.OutputName.ShouldBe("B draft"); ReferenceEquals(current, m.PendingLeave).ShouldBeTrue();
            m.KeepDraftCommand.Execute(current); tree.Root.UpdateLayout();
            GuidedImportHomeTests.Find<TextBox>(tree, "WorkflowSelection.OutputName").IsEnabled.ShouldBeTrue();
            return true;
        });
        (await h.Sessions.LoadAsync(a.Id, default)).Value.OutputName.Value.ShouldBe(a.OutputName.Value);
        (await h.Sessions.LoadAsync(b.Id, default)).Value.OutputName.Value.ShouldBe(b.OutputName.Value);
    }

    [Fact]
    public async Task Draft_decision_blocks_ordinary_input_and_moves_focus_to_a_non_action_heading()
    {
        using HomeScreenHarness h = new();
        var m = h.WorkflowSelection(h.Navigation); m.Open(await GuidedImportRouteTests.Import(h, "modal-input.png"));
        WpfRendering.RenderExpectingNoBindingErrors(() => new WorkflowSelectionView { DataContext = m }, new Size(1000, 700), tree =>
        {
            var route = GuidedImportHomeTests.Find<Button>(tree, m.Workflows.Last().AutomationId);
            var ordinary = GuidedSessionInputTests.Provider(route);
            var editor = GuidedImportHomeTests.Find<TextBox>(tree, "WorkflowSelection.OutputName");
            var value = (IValueProvider)UIElementAutomationPeer.CreatePeerForElement(editor)!.GetPattern(PatternInterface.Value)!;
            FocusManager.SetFocusedElement(FocusManager.GetFocusScope(tree.Root), route);
            m.OutputName = "still editing"; m.BackToHomeCommand.Execute(null); tree.Root.UpdateLayout();
            ordinary.Invoke(); value.SetValue("stale edit"); m.SelectCommand.Execute(m.Workflows.Last()); m.BackToHomeCommand.Execute(null);
            GuidedImportHomeTests.Drain();
            m.OutputName.ShouldBe("still editing"); h.Navigation.GoHomeCount.ShouldBe(0); h.Navigation.SessionFor.ShouldBeNull();
            FocusManager.GetFocusedElement(FocusManager.GetFocusScope(tree.Root)).ShouldBe(tree.Root.FindName("DraftLeaveHeadingText"));
            m.KeepDraftCommand.Execute(m.PendingLeave); tree.Root.UpdateLayout();
            FocusManager.GetFocusedElement(FocusManager.GetFocusScope(tree.Root)).ShouldBe(tree.Root.FindName("RouteHeading"));
            return true;
        });
    }

    [Fact]
    public async Task A_successfully_committed_name_is_clean_even_if_the_subsequent_route_choice_is_refused()
    {
        using HomeScreenHarness h = new(); var imported = await GuidedImportRouteTests.Import(h, "saved-name.png");
        var service = DispatchProxy.Create<ISessionService, GuidedSessionInputTests.DeferredService>();
        var proxy = (GuidedSessionInputTests.DeferredService)service; proxy.Inner = h.Sessions;
        var m = new WorkflowSelectionViewModel(service, h.Previews, h.Navigation); m.Open(imported); m.OutputName = "committed-name";
        var gate = proxy.HoldNext(); var pending = m.SelectCommand.ExecuteAsync(m.Workflows.Last());
        await gate.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            (await h.Sessions.ExecuteAsync(imported.Id, new WorkflowCommand.ConfirmOriginal(), "tester", default)).IsSuccess.ShouldBeTrue();
            (await h.Sessions.ExecuteAsync(imported.Id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", default)).IsSuccess.ShouldBeTrue();
        }
        finally { gate.Release.SetResult(); await pending; }
        h.Navigation.SessionFor.ShouldBeNull(); m.Notice.ShouldNotBeNullOrWhiteSpace(); m.SessionName.ShouldBe("committed-name");
        await m.BackToHomeCommand.ExecuteAsync(null);
        m.PendingLeave.ShouldBeNull(); h.Navigation.GoHomeCount.ShouldBe(1);
        (await h.Sessions.LoadAsync(imported.Id, default)).Value.OutputName.Value.ShouldBe("committed-name");
    }

    [Fact]
    public async Task Leaving_an_edited_route_name_waits_for_a_draft_decision_without_saving()
    {
        using HomeScreenHarness h = new();
        var imported = await GuidedImportRouteTests.Import(h, "unsaved-route-name.png");
        var m = h.WorkflowSelection(h.Navigation); m.Open(imported); await m.PreviewLoaded;
        string before = JsonSerializer.Serialize((await h.Inner.Repository.LoadAsync(imported.Id, default)).Value);
        m.OutputName = "operator-new-output-name";
        await m.BackToHomeCommand.ExecuteAsync(null);
        h.Navigation.GoHomeCount.ShouldBe(0);
        m.OutputName.ShouldBe("operator-new-output-name");
        JsonSerializer.Serialize((await h.Inner.Repository.LoadAsync(imported.Id, default)).Value).ShouldBe(before);
        WpfRendering.RenderExpectingNoBindingErrors(() => new WorkflowSelectionView { DataContext = m }, new Size(1000, 700), tree =>
        {
            GuidedSessionProjectionTests.Shown(GuidedImportHomeTests.Find<Border>(tree, "WorkflowSelection.DraftLeave")).ShouldBeTrue();
            GuidedImportHomeTests.Find<TextBox>(tree, "WorkflowSelection.OutputName").IsEnabled.ShouldBeFalse();
            foreach (var choice in m.Workflows) GuidedImportHomeTests.Find<Button>(tree, choice.AutomationId).IsEnabled.ShouldBeFalse();
            return true;
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_busy_route_submission_cannot_open_a_leave_decision_or_navigate_away(bool rename)
    {
        using HomeScreenHarness h = new();
        var service = DispatchProxy.Create<ISessionService, GuidedSessionInputTests.DeferredService>();
        var proxy = (GuidedSessionInputTests.DeferredService)service; proxy.Inner = h.Sessions;
        var m = new WorkflowSelectionViewModel(service, h.Previews, h.Navigation);
        m.Open(await GuidedImportRouteTests.Import(h, "pending-route-name.png"));
        if (rename) m.OutputName = "submitted-name";
        var gate = proxy.HoldNext(); var pending = m.SelectCommand.ExecuteAsync(m.Workflows.Last());
        await gate.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await m.BackToHomeCommand.ExecuteAsync(null);
            h.Navigation.GoHomeCount.ShouldBe(0); m.IsBusy.ShouldBeTrue();
            WpfRendering.RenderExpectingNoBindingErrors(() => new WorkflowSelectionView { DataContext = m }, new Size(1000, 700), tree =>
            {
                GuidedImportHomeTests.Find<Button>(tree, "WorkflowSelection.Back").IsEnabled.ShouldBeFalse();
                return true;
            });
        }
        finally { gate.Release.SetResult(); await pending; }
        h.Navigation.SessionFor.ShouldNotBeNull();
    }
}
