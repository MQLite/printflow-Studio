using System.Globalization;
using System.Windows.Input;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Threading;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Reviews;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Services;
using Xunit.Abstractions;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class SharedReviewAuthorityTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Presentation_preferences_leave_revision_bytes_and_review_binding_unchanged()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel model = await OpenReview(harness);
        SessionAggregate before = await Load(harness);
        var revision = before.Revisions.Single(r => r.Id == before.Steps.Single(s => s.Step == StepKind.Enhancement).CurrentRevisionId);
        byte[] bytes = await File.ReadAllBytesAsync(harness.Inner.FileWorkspace.ResolveAbsolute(revision.File));
        string hash = model.ArtefactHash;
        string id = model.ArtefactRevision;
        foreach (ReviewInspectionBackground background in Enum.GetValues<ReviewInspectionBackground>())
        {
            model.ReviewViewport.Background = background;
            model.ReviewViewport.Mode = ReviewComparisonMode.Slider;
            model.ReviewViewport.SliderPosition = 75;
            model.ZoomInCommand.Execute(null);
            model.ReviewViewport.HorizontalPosition = 0.75;
            model.ArtefactHash.ShouldBe(hash);
            model.ArtefactRevision.ShouldBe(id);
            SessionAggregate unchanged = await Load(harness);
            unchanged.Revisions.ShouldBe(before.Revisions);
            unchanged.Reviews.ShouldBe(before.Reviews);
            unchanged.Steps.ShouldBe(before.Steps);
        }
        (await File.ReadAllBytesAsync(harness.Inner.FileWorkspace.ResolveAbsolute(revision.File))).ShouldBe(bytes);
        await model.ApproveCommand.ExecuteAsync(null);
        SessionAggregate after = await Load(harness);
        ReviewDecision decision = after.Reviews.Single(r => r.Step == StepKind.Enhancement);
        decision.IsApproved.ShouldBeTrue();
        decision.SubjectId.ShouldBe(revision.Id.Value);
        decision.ReviewedSha256.ShouldBe(revision.Sha256);
    }

    /// <summary>Opt-in, bounded real HWND and Windows UIA client over the production Session view and real review service.</summary>
    [Fact]
    public async Task Live_synthetic_review_window_is_operable_through_Windows_UIA()
    {
        if (Environment.GetEnvironmentVariable("PRINTFLOW_SHARED_REVIEW_LIVE") != "1") return;
        using HomeScreenHarness harness = new();
        SessionViewModel model = await OpenReview(harness);
        SessionAggregate before = await Load(harness);
        var revision = before.Revisions.Single(r => r.Id == before.Steps.Single(s => s.Step == StepKind.Enhancement).CurrentRevisionId);
        List<string> transcript = [];
        WpfRendering.OnStaThread(() =>
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            SessionScreenView view = new() { DataContext = model };
            Window window = new() { Title = "PrintFlow synthetic shared review proof", Content = view, Width = 1250, Height = 850 };
            window.Show();
            nint handle = new WindowInteropHelper(window).Handle;
            Dispatcher dispatcher = window.Dispatcher;
            Task drive = Task.Run(() =>
            {
                AutomationElement root = AutomationElement.FromHandle(handle);
                AutomationElement Element(string id) => DesktopAutomation.Element(root, "Session." + id);
                void Invoke(string id) => DesktopAutomation.Invoke(Element(id));
                void Select(string id) => ((SelectionItemPattern)Element(id).GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
                ScrollPattern Scroll(string id) => (ScrollPattern)Element(id).GetCurrentPattern(ScrollPattern.Pattern);
                void Sync(double x, double y)
                {
                    DesktopAutomation.Wait("synchronized pan", () =>
                    {
                        ScrollPattern a = Scroll("ReviewAfter");
                        return Math.Abs(a.Current.HorizontalScrollPercent - x) < 0.5 && Math.Abs(a.Current.VerticalScrollPercent - y) < 0.5;
                    });
                }
                Invoke("ReviewActualSize");
                Scroll("ReviewBefore").SetScrollPercent(70, 30);
                Sync(70, 30);
                transcript.Add("Before ScrollPattern 70/30 -> After 70/30.");
                foreach (string background in new[] { "White", "Black", "Checkerboard" })
                {
                    Select("ReviewBackground" + background);
                    dispatcher.Invoke(() => model.ReviewViewport.Background.ToString().ShouldBe(background));
                    transcript.Add("Selected " + background + "; shared render state confirmed.");
                }
                Select("ReviewModeSlider");
                foreach (double value in new double[] { 25, 50, 75 })
                {
                    ((RangeValuePattern)Element("ReviewComparisonSlider").GetCurrentPattern(RangeValuePattern.Pattern)).SetValue(value);
                    dispatcher.Invoke(() => model.ReviewViewport.SliderPosition.ShouldBe(value));
                    transcript.Add("Comparison RangeValuePattern = " + value + ".");
                }
                dispatcher.Invoke(() =>
                {
                    var slider = SharedReviewSurfaceTests.Descendants<System.Windows.Controls.Slider>(view).Single();
                    var start = SharedReviewSurfaceTests.Descendants<System.Windows.Controls.RadioButton>(view)
                        .Single(b => AutomationProperties.GetAutomationId(b) == "Session.ReviewModeSlider");
                    start.Focus().ShouldBeTrue();
                    List<string> route = [];
                    for (int i = 0; i < 20 && !slider.IsKeyboardFocused; i++)
                    {
                        ((UIElement)Keyboard.FocusedElement).MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)).ShouldBeTrue();
                        route.Add(AutomationProperties.GetAutomationId((DependencyObject)Keyboard.FocusedElement));
                    }
                    slider.IsKeyboardFocused.ShouldBeTrue();
                    transcript.Add("WPF keyboard traversal to slider: " + string.Join(" -> ", route));
                    void KeyPress(Key key) => slider.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(view), 0, key) { RoutedEvent = Keyboard.KeyDownEvent });
                    KeyPress(Key.Right);
                    model.ReviewViewport.SliderPosition.ShouldBe(76);
                    KeyPress(Key.Home);
                    model.ReviewViewport.SliderPosition.ShouldBe(0);
                    KeyPress(Key.End);
                    model.ReviewViewport.SliderPosition.ShouldBe(100);
                    model.ReviewViewport.SliderPosition = 75;
                    transcript.Add("WPF routed keyboard Right/Home/End reached 76/0/100.");
                }); Invoke("ReviewZoomIn");
                Scroll("ReviewOverlay").SetScrollPercent(60, 40);
                dispatcher.Invoke(() =>
                {
                    SharedReviewSurfaceTests.Settle(view);
                    model.ReviewViewport.HorizontalPosition.ShouldBe(0.6, 0.005);
                    var overlay = SharedReviewSurfaceTests.Descendants<System.Windows.Controls.Grid>(view).Single(g => g.Clip is System.Windows.Media.RectangleGeometry);
                    ((System.Windows.Media.RectangleGeometry)overlay.Clip).Rect.Width.ShouldBe(overlay.Width * 0.75, 0.01);
                });
                Select("ReviewModeSideBySide");
                Sync(60, 40);
                transcript.Add("Zoom and overlay pan 60/40 preserve alignment and side-by-side return.");
                dispatcher.Invoke(() =>
                {
                    var choices = SharedReviewSurfaceTests.Descendants<System.Windows.Controls.RadioButton>(view).Where(b => AutomationProperties.GetAutomationId(b).StartsWith("Session.Review", StringComparison.Ordinal));
                    foreach (var choice in choices) { choice.Focus().ShouldBeTrue(); choice.IsKeyboardFocused.ShouldBeTrue(); }
                });
                transcript.Add("All comparison/background radio controls accept keyboard focus.");
                dispatcher.Invoke(() =>
                {
                    for (int i = 0; i < 40 && AutomationProperties.GetAutomationId((DependencyObject)Keyboard.FocusedElement) != "Session.Approve"; i++)
                        ((UIElement)Keyboard.FocusedElement).MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)).ShouldBeTrue();
                    AutomationProperties.GetAutomationId((DependencyObject)Keyboard.FocusedElement).ShouldBe("Session.Approve");
                    transcript.Add("WPF keyboard traversal reaches Approve after comparison controls.");
                });
                Invoke("Approve"); DesktopAutomation.Wait("normal review approval", () => dispatcher.Invoke(() => !model.IsReviewRequired));
                transcript.Add("Approve InvokePattern completed normally.");
            });
            DispatcherFrame frame = new();
            _ = drive.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)), TaskScheduler.Default);
            try { Dispatcher.PushFrame(frame); drive.GetAwaiter().GetResult(); }
            finally { view.DataContext = null; window.Close(); }
        });
        SessionAggregate after = await Load(harness);
        var decision = after.Reviews.Single(r => r.Step == StepKind.Enhancement);
        decision.SubjectId.ShouldBe(revision.Id.Value);
        decision.ReviewedSha256.ShouldBe(revision.Sha256);
        after.Revisions.ShouldBe(before.Revisions);
        transcript.Add($"Independent repository reload: Revision {revision.Id.Value}; SHA-256 {revision.Sha256}; approval binding unchanged.");
        Directory.CreateDirectory("evidence");
        await File.WriteAllLinesAsync("evidence/shared-review-live.txt", transcript);
        foreach (string line in transcript) output.WriteLine(line);
    }

    private static async Task<SessionAggregate> Load(HomeScreenHarness harness) =>
        (await harness.Inner.Repository.LoadAsync(harness.Navigation.WorkflowSelectionFor!.Id, CancellationToken.None)).Value!;

    private static async Task<SessionViewModel> OpenReview(HomeScreenHarness harness)
    {
        harness.FilePicker.Path = harness.WriteSourceFile("shared-review-synthetic.png");
        await File.WriteAllBytesAsync(harness.FilePicker.Path, SyntheticImages.PngWithAlpha(1800, 1200, (x, _) => x < 50 ? (byte)0 : (byte)128));
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);
        SessionViewModel model = harness.Session(new RecordingNavigation());
        model.Open(harness.Navigation.WorkflowSelectionFor!);
        await model.ConfirmOriginalCommand.ExecuteAsync(null);
        await model.RunStepCommand.ExecuteAsync(null);
        await model.PreviewsLoaded;
        model.IsReviewRequired.ShouldBeTrue();
        return model;
    }
}
