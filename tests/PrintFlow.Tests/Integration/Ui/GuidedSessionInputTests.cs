using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Adapters.Fake;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Tests.Integration.Persistence;
using PrintFlow.Workflow.Services;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class GuidedSessionInputTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retained_invoke_provider_cannot_operate_the_next_task(bool queueBeforeTransition)
    {
        using HomeScreenHarness h = new();
        var first = await GuidedSessionProjectionTests.Open(h, WorkflowType.PrepareAsset);
        var second = await GuidedSessionProjectionTests.Open(h, WorkflowType.GeneratePrintTiff);
        WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = first }, new Size(1000, 700), tree =>
        {
            var button = GuidedSessionShellTests.Find<Button>(tree, "Session.ConfirmOriginal");
            int calls = 0;
            button.Command = new RelayCommand(() => calls++);
            var oldProvider = Provider(button);
            if (queueBeforeTransition) oldProvider.Invoke();
            tree.Root.DataContext = second;
            tree.Root.UpdateLayout();
            if (!queueBeforeTransition) oldProvider.Invoke();
            Drain();
            calls.ShouldBe(0);
            Provider(button).Invoke(); Drain();
            calls.ShouldBe(1, "a freshly acquired provider on the current screen is still usable");
            return true;
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Hidden_or_unloaded_controls_refuse_retained_invocation(bool unload)
    {
        using HomeScreenHarness h = new();
        var m = await GuidedSessionProjectionTests.Open(h, WorkflowType.PrepareAsset);
        WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = m }, new Size(1000, 700), tree =>
        {
            var button = GuidedSessionShellTests.Find<Button>(tree, "Session.ConfirmOriginal");
            int calls = 0; button.Command = new RelayCommand(() => calls++);
            var provider = Provider(button);
            if (unload) tree.Root.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            else GuidedSessionShellTests.Find<FrameworkElement>(tree, "Session.ActionBar").Visibility = Visibility.Collapsed;
            provider.Invoke(); Drain();
            calls.ShouldBe(0);
            if (unload)
            {
                tree.Root.DataContext = null; tree.Root.DataContext = m; tree.Root.UpdateLayout();
                button.Command = new RelayCommand(() => calls++);
                Provider(button).Invoke(); Drain();
                calls.ShouldBe(0, "rebinding an unloaded view cannot reactivate its action surface");
            }
            return true;
        });
    }

    [Theory]
    [InlineData(Key.Enter)]
    [InlineData(Key.Space)]
    public async Task Held_key_release_and_double_click_do_not_activate_new_context(Key key)
    {
        using HomeScreenHarness h = new();
        var first = await GuidedSessionProjectionTests.Open(h, WorkflowType.PrepareAsset);
        var second = await GuidedSessionProjectionTests.Open(h, WorkflowType.GeneratePrintTiff);
        WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = first }, new Size(1000, 700), tree =>
        {
            var button = GuidedSessionShellTests.Find<Button>(tree, "Session.ConfirmOriginal");
            int calls = 0; button.Command = new RelayCommand(() => calls++);
            var source = new HeadlessSource();
            SendKey(button, source, key, true);
            int before = calls;
            tree.Root.DataContext = second; tree.Root.UpdateLayout();
            SendKey(button, source, key, true, repeat: true);
            SendKey(button, source, key, false);
            calls.ShouldBe(before, "a down/repeat/release cannot cross the task boundary");
            var down = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                { RoutedEvent = UIElement.MouseLeftButtonDownEvent };
            typeof(MouseButtonEventArgs).GetProperty(nameof(MouseButtonEventArgs.ClickCount))!.SetValue(down, 2);
            button.RaiseEvent(down);
            down.Handled.ShouldBeTrue();
            SendKey(button, source, key, true); SendKey(button, source, key, false);
            calls.ShouldBe(before + 1);
            return true;
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Late_success_or_failure_cannot_replace_new_task_or_its_notice(bool failure)
    {
        using HomeScreenHarness h = new();
        var imported = (await h.Sessions.ImportAsync(WorkflowType.PrepareAsset,
            h.Inner.WriteBorderedSourcePng("async-a.png"), "a", "tester", default)).Value;
        var other = (await h.Sessions.ImportAsync(WorkflowType.GeneratePrintTiff,
            h.Inner.WriteBorderedSourcePng("async-b.png"), "b", "tester", default)).Value;
        var service = DispatchProxy.Create<ISessionService, DeferredService>();
        var proxy = (DeferredService)service; proxy.Inner = h.Sessions;
        var m = new SessionViewModel(service, h.Previews, h.TiffReviews, new RecordingNavigation());
        m.Open(imported); await m.ConfirmOriginalCommand.ExecuteAsync(null);
        if (failure) h.Inner.FakeMeitu.SetScenario(FakeAdapterScenario.Timeout);
        var gate = proxy.HoldNext();
        Task pending = m.RunStepCommand.ExecuteAsync(null);
        await gate.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        int starts = h.Meitu.CallCount;
        await m.RunStepCommand.ExecuteAsync(null);
        h.Meitu.CallCount.ShouldBe(starts, "a duplicate command cannot start another attempt");
        m.Open(other);
        m.Notice = "new task notice";
        gate.Release.SetResult(); await pending;
        m.Id.ShouldBe(other.Id);
        m.Notice.ShouldBe("new task notice");
        m.GuidedPage.ShouldBe(GuidedSessionPage.Source);
        m.IsBusy.ShouldBeFalse();
    }

    [Fact]
    public async Task Old_finally_cannot_clear_new_task_busy_operation()
    {
        using HomeScreenHarness h = new();
        var a = (await h.Sessions.ImportAsync(WorkflowType.PrepareAsset,
            h.Inner.WriteBorderedSourcePng("busy-a.png"), "a", "tester", default)).Value;
        var b = (await h.Sessions.ImportAsync(WorkflowType.GeneratePrintTiff,
            h.Inner.WriteBorderedSourcePng("busy-b.png"), "b", "tester", default)).Value;
        var service = DispatchProxy.Create<ISessionService, DeferredService>();
        var proxy = (DeferredService)service; proxy.Inner = h.Sessions;
        var m = new SessionViewModel(service, h.Previews, h.TiffReviews, new RecordingNavigation());
        m.Open(a); await m.ConfirmOriginalCommand.ExecuteAsync(null);
        var old = proxy.HoldNext(); Task oldRun = m.RunStepCommand.ExecuteAsync(null);
        await old.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        m.Open(b);
        m.IsBusy.ShouldBeFalse("opening B retires only A's screen busy ownership");
        var current = proxy.HoldNext(); Task currentRun = m.ConfirmOriginalCommand.ExecuteAsync(null);
        await current.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        old.Release.SetResult(); await oldRun;
        m.Id.ShouldBe(b.Id); m.IsBusy.ShouldBeTrue();
        current.Release.SetResult(); await currentRun;
        m.IsBusy.ShouldBeFalse(); m.GuidedPage.ShouldBe(GuidedSessionPage.Dimensions);
    }

    [Fact]
    public async Task Delayed_failure_reload_cannot_settle_a_notice_on_another_task()
    {
        using HomeScreenHarness h = new();
        var a = (await h.Sessions.ImportAsync(WorkflowType.PrepareAsset,
            h.Inner.WriteBorderedSourcePng("reload-a.png"), "a", "tester", default)).Value;
        var b = (await h.Sessions.ImportAsync(WorkflowType.GeneratePrintTiff,
            h.Inner.WriteBorderedSourcePng("reload-b.png"), "b", "tester", default)).Value;
        var service = DispatchProxy.Create<ISessionService, DeferredService>();
        var proxy = (DeferredService)service; proxy.Inner = h.Sessions;
        var m = new SessionViewModel(service, h.Previews, h.TiffReviews, new RecordingNavigation());
        m.Open(a); await m.ConfirmOriginalCommand.ExecuteAsync(null);
        h.Inner.FakeMeitu.SetScenario(FakeAdapterScenario.Timeout);
        var gate = proxy.HoldNext(nameof(ISessionService.LoadAsync));
        Task pending = m.RunStepCommand.ExecuteAsync(null);
        await gate.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        m.Open(b); m.Notice = "B remains current";
        gate.Release.SetResult(); await pending;
        m.Id.ShouldBe(b.Id); m.Notice.ShouldBe("B remains current"); m.IsBusy.ShouldBeFalse();
    }

    [Theory]
    [InlineData("source", WorkflowType.PrepareAsset, GuidedSessionPage.Source)]
    [InlineData("dimensions", WorkflowType.GeneratePrintTiff, GuidedSessionPage.Dimensions)]
    [InlineData("white", WorkflowType.GeneratePrintTiff, GuidedSessionPage.WhiteInk)]
    [InlineData("review", WorkflowType.PrepareAsset, GuidedSessionPage.Review)]
    public async Task Reopened_task_projects_persisted_page_without_starting_an_adapter(string stage, WorkflowType route, GuidedSessionPage page)
    {
        using HomeScreenHarness h = new();
        var m = await GuidedSessionProjectionTests.Open(h, route);
        if (stage != "source") await m.ConfirmOriginalCommand.ExecuteAsync(null);
        if (stage == "white")
        {
            m.WidthMmText = "200"; m.HeightMmText = "150";
            await m.SetMaximumBoundsCommand.ExecuteAsync(null);
        }
        if (stage == "review") await m.RunStepCommand.ExecuteAsync(null);
        string before = JsonSerializer.Serialize((await h.Inner.Repository.LoadAsync(m.Id, default)).Value);
        var meitu = new CountingMeituProcessor(h.Inner.FakeMeitu);
        var restarted = h.Inner.CreateServiceWithMeitu(meitu);
        var fresh = new SessionViewModel(restarted, h.Previews, h.TiffReviews, new RecordingNavigation());
        fresh.Open((await restarted.LoadAsync(m.Id, default)).Value); await fresh.PreviewsLoaded;
        fresh.GuidedPage.ShouldBe(page); fresh.IsBusy.ShouldBeFalse(); meitu.CallCount.ShouldBe(0);
        JsonSerializer.Serialize((await h.Inner.Repository.LoadAsync(m.Id, default)).Value).ShouldBe(before);
    }

    [Fact]
    public async Task Reopened_interruption_points_to_failure_without_restarting_work()
    {
        using SessionServiceHarness h = new();
        var id = await RecoverySurfaceTests.Seed(h, "guided-interrupted");
        string before = JsonSerializer.Serialize((await h.Repository.LoadAsync(id, default)).Value);
        var meitu = new CountingMeituProcessor(h.FakeMeitu);
        var service = h.CreateServiceWithMeitu(meitu);
        var m = new SessionViewModel(service, h.Previews, h.TiffReviews, new RecordingNavigation());
        m.Open((await service.LoadAsync(id, default)).Value); await m.PreviewsLoaded;
        m.GuidedPage.ShouldBe(GuidedSessionPage.Failure); meitu.CallCount.ShouldBe(0);
        JsonSerializer.Serialize((await h.Repository.LoadAsync(id, default)).Value).ShouldBe(before);
    }

    [Fact]
    public async Task Exact_review_provider_and_neutral_focus_follow_the_new_result()
    {
        using HomeScreenHarness h = new();
        var m = await GuidedSessionProjectionTests.Open(h, WorkflowType.PrepareAsset);
        await m.ConfirmOriginalCommand.ExecuteAsync(null); await m.RunStepCommand.ExecuteAsync(null);
        var review = (await h.Sessions.LoadAsync(m.Id, default)).Value;
        WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = m }, new Size(1000, 700), tree =>
        {
            var button = GuidedSessionShellTests.Find<Button>(tree, "Session.Approve");
            int calls = 0; button.Command = new RelayCommand(() => calls++);
            var old = Provider(button);
            FocusManager.SetFocusedElement(FocusManager.GetFocusScope(tree.Root), button);
            m.Open(review with { CurrentArtefact = review.CurrentArtefact! with { RevisionId = new RevisionId(Guid.NewGuid()) } });
            tree.Root.UpdateLayout();
            old.Invoke(); Drain(); calls.ShouldBe(0);
            FocusManager.GetFocusedElement(FocusManager.GetFocusScope(tree.Root)).ShouldBe(tree.Root.FindName("OperatorStatusPanel"));
            Provider(button).Invoke(); Drain(); calls.ShouldBe(1);
            return true;
        });
    }

    [Fact]
    public async Task Foreign_runtime_event_does_not_clear_current_stop_or_takeover()
    {
        using HomeScreenHarness h = new();
        var m = await GuidedSessionProjectionTests.Open(h, WorkflowType.PrepareAsset);
        await m.ConfirmOriginalCommand.ExecuteAsync(null);
        h.Inner.FakeMeitu.SetScenario(FakeAdapterScenario.WaitForStopAt(ExternalOperationPhase.Busy));
        Task running = m.RunStepCommand.ExecuteAsync(null);
        await h.Inner.FakeMeitu.HangStarted;
        try
        {
            m.CanStopAutomation.ShouldBeTrue(); m.CanTakeOverAutomation.ShouldBeTrue();
            var foreign = new AutomationRuntimeView(new SessionId(Guid.NewGuid()), null, StepKind.Enhancement,
                AutomationRuntimeState.Running, ExternalOperationPhase.Busy, true);
            typeof(SessionViewModel).GetMethod("OnAutomationRuntimeChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(m, [null, foreign]);
            m.CanStopAutomation.ShouldBeTrue(); m.CanTakeOverAutomation.ShouldBeTrue();
        }
        finally { m.StopAutomationCommand.Execute(null); await running; }
    }

    internal static IInvokeProvider Provider(Button button) => (IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(button)!.GetPattern(PatternInterface.Invoke)!;
    private static void Drain() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.SystemIdle);
    private static void SendKey(Button button, PresentationSource source, Key key, bool down, bool repeat = false)
    {
        var input = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
            { RoutedEvent = down ? Keyboard.KeyDownEvent : Keyboard.KeyUpEvent };
        if (repeat) typeof(KeyEventArgs).GetMethod("SetRepeat", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(input, [true]);
        button.RaiseEvent(input);
    }
    private sealed class HeadlessSource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }

    public class DeferredService : DispatchProxy
    {
        public ISessionService Inner { get; set; } = null!;
        private Gate? _next;
        private string _method = nameof(ISessionService.ExecuteAsync);
        public Gate HoldNext(string method = nameof(ISessionService.ExecuteAsync)) { _method = method; return _next = new Gate(); }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            object? result = method!.Invoke(Inner, args);
            if (method.Name == _method && _next is { } gate)
            {
                _next = null;
                return Hold((Task<OperationResult<SessionView>>)result!, gate);
            }
            return result;
        }
        private static async Task<OperationResult<SessionView>> Hold(Task<OperationResult<SessionView>> task, Gate gate)
        {
            var result = await task.ConfigureAwait(false);
            gate.Started.TrySetResult(); await gate.Release.Task.ConfigureAwait(false);
            return result;
        }
    }
    public sealed class Gate
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
