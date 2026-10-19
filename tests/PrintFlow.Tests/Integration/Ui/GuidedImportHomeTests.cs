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
