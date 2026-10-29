using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class GuidedImportHomeTests
{
    [Theory]
    [InlineData("resume")]
    [InlineData("open-recovery")]
    [InlineData("restart-recovery")]
    public async Task Late_continue_or_recovery_cannot_reopen_a_task_after_leaving_home(string action)
    {
        using HomeScreenHarness h = new();
        if (action == "resume")
            (await h.Sessions.ImportAsync(PrintFlow.Domain.Sessions.WorkflowType.PrepareAsset,
                h.WriteSourceFile("late-continue.png"), "late continue", "tester", default)).IsSuccess.ShouldBeTrue();
        else await RecoverySurfaceTests.Seed(h.Inner, "late recovery");
        var service = DispatchProxy.Create<ISessionService, GuidedSessionInputTests.DeferredService>();
        var proxy = (GuidedSessionInputTests.DeferredService)service; proxy.Inner = h.Sessions;
        var m = new HomeViewModel(service, h.Previews, h.Navigation, h.FilePicker, h.StartupStatus, h.Readiness);
        await m.RefreshCommand.ExecuteAsync(null);
        var gate = proxy.HoldNext(action == "restart-recovery" ? nameof(ISessionService.ResolveRecoveryAsync) : nameof(ISessionService.LoadAsync));
        Task pending = action switch
        {
            "resume" => m.ResumeCommand.ExecuteAsync(m.RecentSessions.Single()),
            "open-recovery" => m.OpenRecoveryCommand.ExecuteAsync(m.RecoverySessions.Single()),
            _ => m.RestartRecoveryCommand.ExecuteAsync(m.RecoverySessions.Single()),
        };
        await gate.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await m.ShowSettingsCommand.ExecuteAsync(null);
        gate.Release.SetResult(); await pending;
        h.Navigation.SettingsCount.ShouldBe(1);
        h.Navigation.SessionFor.ShouldBeNull();
        m.Notice.ShouldBeNull();
    }

    [Fact]
    public async Task Import_refusal_remains_visible_beside_the_import_action_without_scrolling()
    {
        using HomeScreenHarness h = new(); await h.Home.DropFilesCommand.ExecuteAsync(["one", "two"]);
        WpfRendering.RenderExpectingNoBindingErrors(() => new HomeView { DataContext = h.Home }, new Size(1000, 700), tree =>
        {
            var notice = tree.OfType<TextBlock>().Single(t => t.Text == h.Home.Notice);
            var scroll = GuidedSessionShellTests.Ancestors(notice).OfType<ScrollViewer>().First();
            var bounds = notice.TransformToAncestor(scroll).TransformBounds(new Rect(notice.RenderSize));
            bounds.Top.ShouldBeGreaterThanOrEqualTo(0); bounds.Bottom.ShouldBeLessThanOrEqualTo(scroll.ViewportHeight + 0.5);
            return true;
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Late_import_after_leaving_or_cancelling_never_navigates_to_route(bool cancel)
    {
        using HomeScreenHarness h = new();
        var service = DispatchProxy.Create<ISessionService, GuidedSessionInputTests.DeferredService>();
        var proxy = (GuidedSessionInputTests.DeferredService)service; proxy.Inner = h.Sessions;
        var m = new HomeViewModel(service, h.Previews, h.Navigation, h.FilePicker, h.StartupStatus, h.Readiness);
        h.FilePicker.Path = h.WriteSourceFile("late-home.png");
        var gate = proxy.HoldNext(nameof(ISessionService.ImportAsync));
        var pending = m.ChooseFileCommand.ExecuteAsync(null);
        await gate.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        m.IsBusy.ShouldBeTrue();
        if (cancel) m.ChooseFileCommand.Cancel();
        else await m.ShowSettingsCommand.ExecuteAsync(null);
        gate.Release.SetResult(); await pending;
        h.Navigation.WorkflowSelectionFor.ShouldBeNull();
        m.IsBusy.ShouldBeFalse();
        if (cancel) m.Notice.ShouldNotBeNullOrWhiteSpace();
        // The committed import is still recoverable; cancellation never deletes the snapshot.
        (await h.Sessions.ListRecentAsync(default)).Value.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Import_is_primary_and_above_workstation_information()
    {
        using HomeScreenHarness h = new();
        await h.Home.RefreshCommand.ExecuteAsync(null);
        WpfRendering.RenderExpectingNoBindingErrors(() => new HomeView { DataContext = h.Home }, new Size(1000, 700), tree =>
        {
            var choose = Find<Button>(tree, "Home.ChooseFile");
            var readiness = Find<TextBlock>(tree, "Home.Readiness.Status");
            choose.TransformToAncestor(tree.Root).Transform(new Point()).Y
                .ShouldBeLessThan(readiness.TransformToAncestor(tree.Root).Transform(new Point()).Y);
            choose.IsEnabled.ShouldBeTrue();
            return true;
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Retained_home_invoke_cannot_cross_rebinding_or_unload(bool unload)
    {
        using HomeScreenHarness a = new(); using HomeScreenHarness b = new();
        WpfRendering.RenderExpectingNoBindingErrors(() => new HomeView { DataContext = a.Home }, new Size(1000, 700), tree =>
        {
            var choose = Find<Button>(tree, "Home.ChooseFile");
            int calls = 0; choose.Command = new RelayCommand(() => calls++);
            var provider = GuidedSessionInputTests.Provider(choose);
            if (unload) tree.Root.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            else tree.Root.DataContext = b.Home;
            tree.Root.UpdateLayout();
            provider.Invoke(); Drain(); calls.ShouldBe(0);
            return true;
        });
    }

    internal static T Find<T>(RenderedTree tree, string id) where T : FrameworkElement =>
        tree.OfType<T>().Single(x => AutomationProperties.GetAutomationId(x) == id);
    internal static void Drain() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.SystemIdle);
}
