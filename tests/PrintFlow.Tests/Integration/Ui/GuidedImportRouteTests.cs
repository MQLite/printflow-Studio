using System.Reflection;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class GuidedImportRouteTests
{
    [Fact]
    public async Task Busy_selection_does_not_claim_a_derived_result_locked_the_route()
    {
        using HomeScreenHarness h = new();
        var service = DispatchProxy.Create<ISessionService, GuidedSessionInputTests.DeferredService>();
        var proxy = (GuidedSessionInputTests.DeferredService)service; proxy.Inner = h.Sessions;
        var m = new WorkflowSelectionViewModel(service, h.Previews, h.Navigation);
        m.Open(await Import(h, "busy-route.png"));
        string before = m.RouteChangeHint;
        var gate = proxy.HoldNext(); var running = m.SelectCommand.ExecuteAsync(m.Workflows.First());
        await gate.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try { m.CanSelect.ShouldBeFalse(); m.RouteChangeHint.ShouldBe(before); }
        finally { gate.Release.SetResult(); await running; }
    }

    [Fact]
    public async Task All_three_route_actions_are_visible_together_at_minimum_viewport()
    {
        using HomeScreenHarness h = new();
        var m = h.WorkflowSelection(h.Navigation); m.Open(await Import(h, "route-cards.png")); await m.PreviewLoaded;
        WpfRendering.RenderExpectingNoBindingErrors(() => new WorkflowSelectionView { DataContext = m }, new Size(1000, 700), tree =>
        {
            foreach (var choice in m.Workflows)
            {
                var button = GuidedImportHomeTests.Find<Button>(tree, choice.AutomationId);
                var bounds = button.TransformToAncestor(tree.Root).TransformBounds(new Rect(button.RenderSize));
                bounds.Bottom.ShouldBeLessThanOrEqualTo(700);
                bounds.Right.ShouldBeLessThanOrEqualTo(1000);
                var scroll = GuidedSessionShellTests.Ancestors(button).OfType<ScrollViewer>().First();
                var clipped = button.TransformToAncestor(scroll).TransformBounds(new Rect(button.RenderSize));
                clipped.Bottom.ShouldBeLessThanOrEqualTo(scroll.ViewportHeight + 0.5, "the whole action must be visible without scrolling");
                button.IsEnabled.ShouldBeTrue();
                tree.OfType<TextBlock>().ShouldContain(x => x.Text == choice.Purpose);
                tree.OfType<TextBlock>().ShouldContain(x => x.Text == choice.Result);
                tree.OfType<TextBlock>().ShouldContain(x => x.Text == choice.Dimensions);
            }
            return true;
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Late_rename_or_selection_cannot_replace_new_task_or_navigate_after_leaving(bool rename)
    {
        using HomeScreenHarness h = new();
        var a = await Import(h, "route-a.png"); var b = await Import(h, "route-b.png");
        var service = DispatchProxy.Create<ISessionService, GuidedSessionInputTests.DeferredService>();
        var proxy = (GuidedSessionInputTests.DeferredService)service; proxy.Inner = h.Sessions;
        var m = new WorkflowSelectionViewModel(service, h.Previews, h.Navigation); m.Open(a);
        if (rename) m.OutputName = "renamed-a";
        var gate = proxy.HoldNext();
        Task pending = m.SelectCommand.ExecuteAsync(m.Workflows.Last());
        await gate.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        if (rename) { m.Open(b); m.Notice = "B remains current"; }
        else await m.BackToHomeCommand.ExecuteAsync(null);
        gate.Release.SetResult(); await pending;
        h.Navigation.SessionFor.ShouldBeNull();
        if (rename)
        {
            m.SourceFileName.ShouldBe("route-b.png"); m.OutputName.ShouldBe("route-b");
            m.Notice.ShouldBe("B remains current");
            (await h.Sessions.LoadAsync(a.Id, default)).Value.WorkflowType.ShouldBe(WorkflowType.PrepareAsset);
        }
        m.IsBusy.ShouldBeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retained_route_action_and_name_provider_cannot_operate_reopened_task(bool queueBefore)
    {
        using HomeScreenHarness h = new();
        var a = await Import(h, "provider-a.png"); var b = await Import(h, "provider-b.png");
        var m = h.WorkflowSelection(h.Navigation); m.Open(a); await m.PreviewLoaded;
        WpfRendering.RenderExpectingNoBindingErrors(() => new WorkflowSelectionView { DataContext = m }, new Size(1000, 700), tree =>
        {
            var button = GuidedImportHomeTests.Find<Button>(tree, m.Workflows.First().AutomationId);
            var editor = GuidedImportHomeTests.Find<TextBox>(tree, "WorkflowSelection.OutputName");
            int calls = 0; button.Command = new RelayCommand(() => calls++);
            var invoke = GuidedSessionInputTests.Provider(button);
            var value = (IValueProvider)UIElementAutomationPeer.CreatePeerForElement(editor)!.GetPattern(PatternInterface.Value)!;
            if (queueBefore) invoke.Invoke();
            m.Open(b); tree.Root.UpdateLayout();
            if (!queueBefore) invoke.Invoke();
            value.SetValue("stale name"); GuidedImportHomeTests.Drain();
            calls.ShouldBe(0); m.OutputName.ShouldBe("provider-b");
            GuidedSessionInputTests.Provider(button).Invoke(); GuidedImportHomeTests.Drain(); calls.ShouldBe(1);
            return true;
        });
    }

    [Theory]
    [InlineData(Key.Enter)]
    [InlineData(Key.Space)]
    public async Task Held_route_key_cannot_cross_reopened_task(Key key)
    {
        using HomeScreenHarness h = new(); var a = await Import(h, "key-a.png"); var b = await Import(h, "key-b.png");
        var m = h.WorkflowSelection(h.Navigation); m.Open(a); await m.PreviewLoaded;
        WpfRendering.RenderExpectingNoBindingErrors(() => new WorkflowSelectionView { DataContext = m }, new Size(1000, 700), tree =>
        {
            var button = GuidedImportHomeTests.Find<Button>(tree, m.Workflows.First().AutomationId);
            int calls = 0; button.Command = new RelayCommand(() => calls++);
            var source = new HeadlessSource(); Send(key, true);
            int before = calls; m.Open(b); tree.Root.UpdateLayout(); Send(key, false);
            calls.ShouldBe(before);
            Send(key, true); Send(key, false); calls.ShouldBe(before + 1);
            return true;
            void Send(Key k, bool down) => button.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, k)
                { RoutedEvent = down ? Keyboard.KeyDownEvent : Keyboard.KeyUpEvent });
        });
    }

    internal static async Task<SessionView> Import(HomeScreenHarness h, string name) =>
        (await h.Sessions.ImportAsync(WorkflowType.PrepareAsset, h.WriteSourceFile(name), null, "tester", default)).Value;
    private sealed class HeadlessSource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }
}
