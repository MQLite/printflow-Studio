using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Tests.Integration.Persistence;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Services;
using Xunit.Abstractions;
using static PrintFlow.Tests.Integration.Persistence.KeepOriginalExtentPersistenceTests;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class KeepOriginalExtentUiTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("en-US", "waiting", "Keep original extent", "Original extent retained")]
    [InlineData("zh-CN", "waiting", "保留原始范围", "已保留原始范围")]
    [InlineData("en-US", "review", "Keep original extent", "Original extent retained")]
    [InlineData("zh-CN", "review", "保留原始范围", "已保留原始范围")]
    [InlineData("en-US", "manual", "Keep original extent", "Original extent retained")]
    [InlineData("zh-CN", "manual", "保留原始范围", "已保留原始范围")]
    public async Task Rendered_action_is_localized_and_Invoke_advances_real_session(string culture, string phase, string label, string retained)
    {
        using HomeScreenHarness h = new();
        SessionId id = await AtTrim(h.Inner, h.Sessions, WorkflowType.PrepareCustomerDesign, phase == "manual");
        if (phase != "waiting") await RunTrim(h.Sessions, id, phase == "manual");
        SessionView state = (await h.Sessions.LoadAsync(id, CancellationToken.None)).Value;
        WpfRendering.OnStaThread(() =>
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            SessionViewModel model = h.Session(new RecordingNavigation());
            model.Open(state);
            SessionScreenView view = new() { DataContext = model };
            view.Measure(new Size(1250, 850)); view.Arrange(new Rect(0, 0, 1250, 850));
            SharedReviewSurfaceTests.Settle(view);
            try
            {
                Button button = Button(view);
                button.Visibility.ShouldBe(Visibility.Visible);
                button.IsEnabled.ShouldBeTrue();
                button.Focusable.ShouldBeTrue();
                KeyboardNavigation.GetIsTabStop(button).ShouldBeTrue();
                ButtonAutomationPeer peer = new(button);
                peer.GetAutomationId().ShouldBe("Session.KeepOriginalExtent");
                peer.GetName().ShouldBe(label);
                ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)!).Invoke();
                PumpUntil(() => model.Steps.Single(s => s.Name == stateStepName()).State == retained);
                button.Visibility.ShouldBe(Visibility.Collapsed);
                model.CanKeepOriginalExtent.ShouldBeFalse();
                model.KeepOriginalExtentCommand.CanExecute(null).ShouldBeFalse();
                string stateStepName() => PrintFlow.App.Resources.DisplayNames.Step(StepKind.Trim);
            }
            finally { view.DataContext = null; }
        });
        SessionAggregate after = await Load(h.Inner, id);
        after.Steps.Single(s => s.Step == StepKind.Trim).State.ShouldBe(StepState.Skipped);
        after.ToSnapshot().UpstreamRevisionOf(StepKind.PrintDimensions).ShouldBe(after.Revisions.Single(r => r.IsRoot).Id);
    }

    [Fact]
    public async Task Rendered_action_is_hidden_on_unrelated_step()
    {
        using HomeScreenHarness h = new();
        SessionId id = await AtTrim(h.Inner, h.Sessions, WorkflowType.PrepareCustomerDesign);
        SessionView state = await Execute(h.Sessions, id, new WorkflowCommand.ReturnToStep(StepKind.Enhancement));
        WpfRendering.Render(() =>
        {
            SessionViewModel model = h.Session(new RecordingNavigation()); model.Open(state);
            return new SessionScreenView { DataContext = model };
        }, new Size(1250, 850), tree =>
        {
            Button(tree.Root).Visibility.ShouldBe(Visibility.Collapsed);
            ((SessionViewModel)tree.Root.DataContext).KeepOriginalExtentCommand.CanExecute(null).ShouldBeFalse();
            return true;
        }).BindingErrors.ShouldBeEmpty();
    }

    [Fact]
    public Task Live_synthetic_WPF_UIA_review() => RunLive("review");

    [Fact]
    public Task Live_synthetic_WPF_UIA_manual_crop_required() => RunLive("manual");

    [Fact]
    public Task Live_synthetic_WPF_UIA_waiting_keyboard() => RunLive("waiting-keyboard");

    private async Task RunLive(string phase)
    {
        if (Environment.GetEnvironmentVariable("PRINTFLOW_KEEP_EXTENT_LIVE") != "1") return;
        using HomeScreenHarness h = new();
        SessionId id = await AtTrim(h.Inner, h.Sessions, WorkflowType.PrepareCustomerDesign, phase == "manual");
        SessionView state = (await h.Sessions.LoadAsync(id, CancellationToken.None)).Value;
        SessionAggregate before = await Load(h.Inner, id);
        Revision upstream = before.Revisions.Single(r => r.Id == before.ToSnapshot().UpstreamRevisionOf(StepKind.Trim));
        byte[] originalBytes = await File.ReadAllBytesAsync(h.Inner.FileWorkspace.ResolveAbsolute(upstream.File));
        var lockBefore = (await h.Inner.Repository.GetAutomationLockAsync(CancellationToken.None)).Value;
        List<string> transcript = [$"Case {phase}; session {id}; pre-Trim Revision {upstream.Id}; SHA-256 {upstream.Sha256}."];
        SessionAggregate? offered = null;
        string[]? offeredFiles = null;
        WpfRendering.OnStaThread(() =>
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            SessionViewModel model = h.Session(new RecordingNavigation()); model.Open(state);
            SessionScreenView view = new() { DataContext = model };
            Window window = new() { Title = "PrintFlow synthetic keep original extent proof", Content = view, Width = 1250, Height = 850, WindowStartupLocation = WindowStartupLocation.CenterScreen };
            window.Show();
            SharedReviewSurfaceTests.Settle(view);
            model.CanRunStep.ShouldBeTrue();
            nint handle = new WindowInteropHelper(window).Handle;
            Dispatcher dispatcher = window.Dispatcher;
            Task drive = Task.Run(async () =>
            {
                AutomationElement root = AutomationElement.FromHandle(handle);
                AutomationElement Element(string name) => DesktopAutomation.Element(root, "Session." + name);
                if (phase != "waiting-keyboard")
                {
                    DesktopAutomation.Invoke(Element("RunStep"));
                    DesktopAutomation.Wait("real Trim result", () => dispatcher.Invoke(() => !model.IsBusy &&
                        (phase == "review" ? model.IsReviewRequired : model.CanManualCrop)));
                    if (phase == "review")
                    {
                        Element("ReviewBefore"); Element("ReviewAfter");
                        dispatcher.Invoke(() => model.PreviewPanes.Count.ShouldBe(2));
                        transcript.Add("Automatic Trim -> ReviewRequired; UIA Before/After present.");
                    }
                    else transcript.Add("Real no-alpha Trim -> ManualCropRequired.");
                }
                offered = await Load(h.Inner, id);
                offeredFiles = Directory.GetFiles(h.Inner.Workspace.Root, "*", SearchOption.AllDirectories).Order().ToArray();
                AutomationElement keep = Element("KeepOriginalExtent");
                keep.Current.Name.ShouldBe("Keep original extent");
                dispatcher.Invoke(() =>
                {
                    Button button = Button(view);
                    var start = SharedReviewSurfaceTests.Descendants<Button>(view).First(b => b.IsVisible && b.IsEnabled && b != button);
                    start.Focus().ShouldBeTrue();
                    for (int i = 0; i < 100 && !button.IsKeyboardFocused; i++)
                        ((UIElement)Keyboard.FocusedElement).MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                    button.IsKeyboardFocused.ShouldBeTrue();
                    transcript.Add("WPF keyboard traversal reaches Keep original extent.");
                    if (phase == "waiting-keyboard")
                        button.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(view), 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
                });
                if (phase != "waiting-keyboard") DesktopAutomation.Invoke(keep);
                DesktopAutomation.Wait("Print Dimensions reached", () => dispatcher.Invoke(() => !model.IsBusy && !model.CanKeepOriginalExtent));
                dispatcher.Invoke(() =>
                {
                    var target = SharedReviewSurfaceTests.Descendants<Button>(view).First(b => b.IsVisible && b.IsEnabled);
                    target.Focus().ShouldBeTrue();
                    target.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)).ShouldBeTrue();
                });
                transcript.Add(phase == "waiting-keyboard" ? "Enter activation advanced to Print Dimensions; downstream focus traversal works." : "UIA Invoke advanced to Print Dimensions; downstream focus traversal works.");
            });
            DispatcherFrame frame = new();
            _ = drive.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)), TaskScheduler.Default);
            try { Dispatcher.PushFrame(frame); drive.GetAwaiter().GetResult(); }
            finally { view.DataContext = null; window.Close(); }
        });
        SessionAggregate after = await Load(h.Inner, id);
        after.Steps.Single(s => s.Step == StepKind.Trim).State.ShouldBe(StepState.Skipped);
        after.Steps.Single(s => s.Step == StepKind.Trim).SkipReason.ShouldBe(WorkflowCommand.KeepOriginalExtent.Reason);
        after.Steps.Single(s => s.Step == StepKind.Trim).CurrentRevisionId.ShouldBeNull();
        after.ToSnapshot().CurrentStep!.Step.ShouldBe(StepKind.PrintDimensions);
        after.ToSnapshot().UpstreamRevisionOf(StepKind.PrintDimensions).ShouldBe(upstream.Id);
        after.Revisions.ShouldBe(offered!.Revisions);
        after.Attempts.ShouldBe(offered.Attempts);
        after.Reviews.ShouldBe(offered.Reviews);
        after.Revisions.Count(r => r.Operation == OperationKind.Trim).ShouldBe(phase == "review" ? 1 : 0);
        Directory.GetFiles(h.Inner.Workspace.Root, "*", SearchOption.AllDirectories).Order().ShouldBe(offeredFiles!);
        (await File.ReadAllBytesAsync(h.Inner.FileWorkspace.ResolveAbsolute(upstream.File))).ShouldBe(originalBytes);
        (await h.Inner.Repository.GetAutomationLockAsync(CancellationToken.None)).Value.ShouldBe(lockBefore);
        SessionView resumed = (await h.Inner.CreateService().LoadAsync(id, CancellationToken.None)).Value;
        resumed.CurrentArtefact!.RevisionId.ShouldBe(upstream.Id);
        resumed.CurrentArtefact.Facts.PixelWidth.ShouldBe(upstream.Facts.PixelWidth);
        resumed.CurrentArtefact.Facts.PixelHeight.ShouldBe(upstream.Facts.PixelHeight);
        transcript.Add($"Independent persistence/file readback: downstream {upstream.Id}, {upstream.Facts.PixelWidth}x{upstream.Facts.PixelHeight}; upstream bytes unchanged; no added files, attempts, Revisions or reviews; automation lock unchanged.");
        if (phase == "review") transcript.Add($"Historical Trim Revision {after.Revisions.Single(r => r.Operation == OperationKind.Trim).Id} retained and not authoritative.");
        string evidence = Path.Combine("evidence", "keep-extent-live-" + phase + ".txt");
        Directory.CreateDirectory("evidence"); await File.WriteAllLinesAsync(evidence, transcript);
        foreach (string line in transcript) output.WriteLine(line);
    }

    private static Button Button(DependencyObject view) => SharedReviewSurfaceTests.Descendants<Button>(view)
        .Single(button => AutomationProperties.GetAutomationId(button) == "Session.KeepOriginalExtent");

    private static void PumpUntil(Func<bool> predicate)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(15);
        while (!predicate() && DateTime.UtcNow < deadline)
        {
            DispatcherFrame frame = new();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
        predicate().ShouldBeTrue("The real workflow command must finish within 15 seconds.");
    }
}
