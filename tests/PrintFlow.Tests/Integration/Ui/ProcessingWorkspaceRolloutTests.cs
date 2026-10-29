using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Adapters.Fake;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Delivery;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class ProcessingWorkspaceRolloutTests
{
    public static TheoryData<string, int, int> Viewports()
    {
        TheoryData<string, int, int> data = new();
        foreach (string language in new[] { "zh-CN", "en-US" })
            foreach ((int width, int height) in new[] { (984, 661), (1000, 700), (1280, 720), (1920, 1080) })
                data.Add(language, width, height);
        return data;
    }

    // Catches instructions taking the crop image's height or actions scrolling out with them.
    [Theory]
    [MemberData(nameof(Viewports))]
    public async Task Crop_keeps_a_large_canvas_and_actions_when_coordinate_help_expands(string language, int width, int height)
    {
        using OperatorCultureScope culture = new(language);
        using SessionServiceHarness h = new();
        var model = (await ReviewGuidanceSetup.OpenAtAsync(h, "trim")).Screen;
        model.BeginTrimAdjustCommand.Execute(null);
        var identity = model.UseThisTrimIdentity;
        var selection = model.CropSelection;
        Size size = new(width, height);
        WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = model }, size, tree =>
        {
            var help = Find<Expander>(tree, "Session.Crop.CoordinatesAndKeyboard");
            help.IsExpanded.ShouldBeFalse();
            foreach (bool expanded in new[] { false, true })
            {
                help.IsExpanded = expanded;
                Settle(tree.Root);
                var surface = Find<Canvas>(tree, "Session.ManualCropSurface");
                var viewport = GuidedSessionShellTests.Ancestors(surface).OfType<ScrollViewer>().First();
                viewport.ViewportHeight.ShouldBeGreaterThanOrEqualTo(180, "help must not consume the crop canvas");
                viewport.ViewportWidth.ShouldBeGreaterThanOrEqualTo(300);
                Inside(viewport, tree.Root);
                foreach (string id in new[] { "Session.TrimAdjust.Cancel", "Session.TrimAdjust.Restore", "Session.TrimAdjust.CompareToggle", "Session.TrimAdjust.Use" })
                    Inside(Find<Button>(tree, id), tree.Root);
                if (expanded)
                {
                    var instructions = All(tree.Root).OfType<TextBlock>().Single(t => AutomationProperties.GetAutomationId(t) == "Session.TrimAdjust.Instructions");
                    new System.Windows.Documents.TextRange(instructions.ContentStart, instructions.ContentEnd).Text.ShouldContain(model.TrimAdjustKeyboardHint);
                    instructions.ActualHeight.ShouldBeGreaterThan(20, "expanded keyboard instructions must actually render");
                    instructions.BringIntoView(); Settle(tree.Root); Inside(instructions, tree.Root);
                }
            }
            model.UseThisTrimIdentity.ShouldBe(identity);
            model.CropSelection.ShouldBe(selection);
            return true;
        });
        Capture(model, size, "crop-" + language);
        Capture(model, size, "crop-help-" + language, tree =>
        {
            Find<Expander>(tree, "Session.Crop.CoordinatesAndKeyboard").IsExpanded = true;
            Settle(tree.Root);
        });
    }

    // Catches fragmented tools, duplicated zoom actions, or layout changes approving a result.
    [Theory]
    [MemberData(nameof(Viewports))]
    public async Task Review_tools_share_one_surface_and_preserve_exact_result_while_comparing(string language, int width, int height)
    {
        using OperatorCultureScope culture = new(language);
        using SessionServiceHarness h = new();
        var model = (await ReviewGuidanceSetup.OpenAtAsync(h, "enhancement")).Screen;
        var identity = model.ReviewTargetIdentity;
        Size size = new(width, height);
        WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = model }, size, tree =>
        {
            var surface = tree.OfType<SharedReviewSurface>().Single();
            var zoom = Find<Button>(tree, "Session.ReviewZoomIn");
            surface.IsAncestorOf(zoom).ShouldBeTrue("zoom and comparison belong to one inspection toolbar");
            zoom.Command.ShouldBeSameAs(model.ZoomInCommand);
            Find<Button>(tree, "Session.ReviewZoomOut").Command.ShouldBeSameAs(model.ZoomOutCommand);
            Find<Button>(tree, "Session.ReviewResetZoom").Command.ShouldBeSameAs(model.ResetZoomCommand);
            zoom.ActualHeight.ShouldBeGreaterThanOrEqualTo(42);
            var zoomStatus = Find<TextBlock>(tree, "Session.ReviewZoomStatus");
            model.ZoomInCommand.Execute(null); Settle(tree.Root);
            zoomStatus.Text.ShouldBe(model.ZoomLabel);
            Inside(zoomStatus, tree.Root);
            model.ResetZoomCommand.Execute(null); Settle(tree.Root);
            zoomStatus.Text.ShouldBe(model.ZoomLabel);
            foreach (string id in new[] { "Session.ReviewZoomOut", "Session.ReviewZoomIn", "Session.ReviewResetZoom" })
                tree.OfType<Button>().Count(b => AutomationProperties.GetAutomationId(b) == id).ShouldBe(1);
            model.ReviewViewport.Mode = ReviewComparisonMode.Slider;
            model.ReviewViewport.SliderPosition = 35;
            model.ReviewViewport.Background = ReviewInspectionBackground.Black;
            Settle(tree.Root);
            var viewport = All(tree.Root).OfType<ScrollViewer>().Single(s => AutomationProperties.GetAutomationId(s) == "Session.ReviewOverlay");
            viewport.ViewportHeight.ShouldBeGreaterThanOrEqualTo(180);
            Inside(viewport, tree.Root);
            var approve = Find<ReviewApprovalButton>(tree, "Session.Approve");
            approve.TargetIdentity.ShouldBe(identity);
            approve.IsGestureGuarded.ShouldBeTrue();
            foreach (string id in new[] { "Session.Approve", "Session.Reject" }) Inside(Find<Button>(tree, id), tree.Root);
            model.ReviewTargetIdentity.ShouldBe(identity);
            model.ReviewViewport.SliderPosition.ShouldBe(35);
            model.ReviewViewport.Background.ShouldBe(ReviewInspectionBackground.Black);
            return true;
        });
        Capture(model, size, "review-slider-" + language);
        (await h.Repository.LoadAsync(model.Id, default)).Value!.Reviews.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Viewports))]
    public async Task Manual_crop_margin_errors_and_help_do_not_shrink_the_canvas_or_hide_cancel(string language, int width, int height)
    {
        using OperatorCultureScope culture = new(language);
        using HomeScreenHarness h = new();
        h.FilePicker.Path = h.Inner.WriteOpaqueSourcePng("manual-crop.png");
        await h.Home.ChooseFileCommand.ExecuteAsync(null);
        var model = h.Session(new RecordingNavigation());
        model.Open(h.Navigation.WorkflowSelectionFor!);
        await model.ConfirmOriginalCommand.ExecuteAsync(null);
        await model.SkipCommand.ExecuteAsync(null);
        await model.SkipCommand.ExecuteAsync(null);
        await model.RunStepCommand.ExecuteAsync(null);
        await model.PreviewsLoaded;
        model.BeginManualCropCommand.Execute(null);
        model.IsCropping.ShouldBeTrue();
        Size size = new(width, height);
        WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = model }, size, tree =>
        {
            model.IsManualCropPerEdge = true;
            model.ManualCropMarginLeft = "invalid";
            Find<Expander>(tree, "Session.Crop.CoordinatesAndKeyboard").IsExpanded = true;
            Settle(tree.Root);
            var viewport = GuidedSessionShellTests.Ancestors(Find<Canvas>(tree, "Session.ManualCropSurface")).OfType<ScrollViewer>().First();
            viewport.ViewportHeight.ShouldBeGreaterThanOrEqualTo(180);
            viewport.ViewportWidth.ShouldBeGreaterThanOrEqualTo(300);
            Inside(viewport, tree.Root);
            Inside(Find<Button>(tree, "Session.CancelManualCrop"), tree.Root);
            model.IsManualCropMarginInvalid.ShouldBeTrue();
            model.CanApplyManualCrop.ShouldBeFalse();
            foreach (string id in new[] { "Session.ManualCropMarginLeft", "Session.ManualCropMarginTop", "Session.ManualCropMarginRight", "Session.ManualCropMarginBottom" })
            {
                var input = Find<TextBox>(tree, id);
                input.BringIntoView(); Settle(tree.Root);
                Inside(input, tree.Root);
                input.ActualHeight.ShouldBeGreaterThanOrEqualTo(42);
            }
            return true;
        });
        Capture(model, size, "manual-invalid-" + language, tree =>
        {
            Find<Expander>(tree, "Session.Crop.CoordinatesAndKeyboard").IsExpanded = true;
            Settle(tree.Root);
            Find<TextBox>(tree, "Session.ManualCropMarginLeft").BringIntoView();
            Settle(tree.Root);
        });
        (await h.Inner.Repository.LoadAsync(model.Id, default)).Value!.Reviews.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Viewports))]
    public async Task Running_uses_the_observed_phase_and_keeps_legal_stop_and_takeover_visible(string language, int width, int height)
    {
        using OperatorCultureScope culture = new(language);
        using HomeScreenHarness h = new();
        var driver = await GuidedSessionProjectionTests.Open(h, WorkflowType.PrepareAsset);
        await driver.ConfirmOriginalCommand.ExecuteAsync(null);
        h.Inner.FakeMeitu.SetScenario(FakeAdapterScenario.WaitForStopAt(ExternalOperationPhase.Busy));
        Task run = driver.RunStepCommand.ExecuteAsync(null);
        await h.Inner.FakeMeitu.HangStarted;
        try
        {
            var model = h.Session(new RecordingNavigation());
            model.Open((await h.Sessions.LoadAsync(driver.Id, default)).Value);
            await model.PreviewsLoaded;
            model.CanRunStep.ShouldBeFalse();
            Size size = new(width, height);
            WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = model }, size, tree =>
            {
                var progress = Find<ProgressBar>(tree, "Session.Processing.Progress");
                progress.IsIndeterminate.ShouldBeTrue();
                progress.GetBindingExpression(System.Windows.Controls.Primitives.RangeBase.ValueProperty).ShouldBeNull();
                Inside(progress, tree.Root);
                Find<TextBlock>(tree, "Session.Processing.State").Text.ShouldBe(model.GuidedProcessingState);
                foreach (string id in new[] { "Session.Stop", "Session.TakeOver" })
                {
                    var action = Find<Button>(tree, id);
                    action.IsEnabled.ShouldBeTrue();
                    Inside(action, tree.Root);
                }
                return true;
            });
            Capture(model, size, "processing-" + language);
        }
        finally { driver.StopAutomationCommand.Execute(null); await run; }
        (await h.Inner.Repository.LoadAsync(driver.Id, default)).Value!.Reviews.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("zh-CN", 984, 661)]
    [InlineData("en-US", 984, 661)]
    [InlineData("zh-CN", 1280, 720)]
    [InlineData("en-US", 1280, 720)]
    public async Task Valid_crop_keeps_full_coordinates_and_each_margin_next_to_its_label(string language, int width, int height)
    {
        using OperatorCultureScope culture = new(language);
        using HomeScreenHarness h = new();
        h.FilePicker.Path = h.Inner.Workspace.CreateSourceFile("large-crop.png",
            SyntheticImages.OpaqueRgbPng(2048, 1536, (x, y) => ((byte)(x % 256), (byte)(y % 256), (byte)100)));
        await h.Home.ChooseFileCommand.ExecuteAsync(null);
        var model = h.Session(new RecordingNavigation()); model.Open(h.Navigation.WorkflowSelectionFor!);
        await model.ConfirmOriginalCommand.ExecuteAsync(null);
        await model.SkipCommand.ExecuteAsync(null); await model.SkipCommand.ExecuteAsync(null);
        await model.RunStepCommand.ExecuteAsync(null); await model.PreviewsLoaded;
        model.BeginManualCropCommand.Execute(null);
        Size size = new(width, height);
        WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = model }, size, tree =>
        {
            var canvas = Find<Canvas>(tree, "Session.ManualCropSurface");
            var pane = model.CropPane!;
            var layout = new CropSurfaceLayout(canvas.ActualWidth, canvas.ActualHeight, pane.PayloadPixelWidth, pane.PayloadPixelHeight,
                pane.SourcePixelWidth, pane.SourcePixelHeight, model.IsFitToViewport, model.ZoomScale);
            model.TrySetCropSelection(layout, layout.ToSurfaceX(100), layout.ToSurfaceY(200), layout.ToSurfaceX(1900), layout.ToSurfaceY(1400)).ShouldBeTrue();
            model.IsManualCropPerEdge = true;
            model.ManualCropMarginLeft = "10"; model.ManualCropMarginTop = "20";
            model.ManualCropMarginRight = "30"; model.ManualCropMarginBottom = "40";
            Find<Expander>(tree, "Session.Crop.CoordinatesAndKeyboard").IsExpanded = true;
            Settle(tree.Root);
            foreach (string id in new[] { "Session.ManualCropSelectedBounds", "Session.ManualCropAppliedBounds" })
            {
                var summary = Find<TextBlock>(tree, id);
                summary.Text.ShouldNotBeNullOrWhiteSpace();
                FormattedText formatted = new(summary.Text, System.Globalization.CultureInfo.CurrentCulture, summary.FlowDirection,
                    new Typeface(summary.FontFamily, summary.FontStyle, summary.FontWeight, summary.FontStretch), summary.FontSize, Brushes.Black, 1)
                    { MaxTextWidth = summary.ActualWidth };
                summary.ActualHeight.ShouldBeGreaterThanOrEqualTo(formatted.Height - 1, "complete coordinates must wrap instead of clipping");
                summary.BringIntoView(); Settle(tree.Root); Inside(summary, tree.Root);
            }
            foreach ((string id, string label) in new[] { ("Left", model.TrimLeftLabel), ("Top", model.TrimTopLabel), ("Right", model.TrimRightLabel), ("Bottom", model.TrimBottomLabel) })
            {
                var input = Find<TextBox>(tree, "Session.ManualCropMargin" + id);
                var caption = All(tree.Root).OfType<TextBlock>().Single(t => t.Text == label && GuidedSessionProjectionTests.Shown(t) &&
                    GuidedSessionShellTests.Ancestors(t).OfType<FrameworkElement>().Any(a => AutomationProperties.GetAutomationId(a) == "Session.Crop.Options"));
                input.BringIntoView(); Settle(tree.Root); Inside(input, tree.Root);
                Inside(caption, tree.Root);
                double inputCentre = input.TransformToAncestor(tree.Root).Transform(new Point(0, input.ActualHeight / 2)).Y;
                double labelCentre = caption.TransformToAncestor(tree.Root).Transform(new Point(0, caption.ActualHeight / 2)).Y;
                Math.Abs(inputCentre - labelCentre).ShouldBeLessThan(1, "a margin label must remain beside its own input after wrapping");
            }
            return true;
        });
        Capture(model, size, "manual-valid-" + language, tree =>
        {
            Find<Expander>(tree, "Session.Crop.CoordinatesAndKeyboard").IsExpanded = true;
            Settle(tree.Root);
            Find<TextBlock>(tree, "Session.ManualCropAppliedBounds").BringIntoView();
            Settle(tree.Root);
        });
    }

    private static T Find<T>(RenderedTree tree, string id) where T : FrameworkElement =>
        tree.OfType<T>().Single(e => AutomationProperties.GetAutomationId(e) == id);

    [Theory]
    [MemberData(nameof(Viewports))]
    public async Task Completed_preview_stays_large_and_inspection_tools_remain_available_without_new_work(string language, int width, int height)
    {
        using OperatorCultureScope culture = new(language);
        using SessionServiceHarness h = new();
        var service = FinalSaveFixtures.TiffService(h);
        var review = await FinalSaveFixtures.TiffAtFinalReviewAsync(h, service);
        string folder = FinalSaveFixtures.NewFolder("u05-completed");
        try
        {
            var delivery = FinalSaveFixtures.Delivery(h);
            var saved = await new FinalSaveCoordinator(service, delivery, "tester").ConfirmAndSaveAsync(
                FinalSaveFixtures.Confirm(review, folder, "done.tif"), null, default);
            saved.Delivery!.Code.ShouldBe(DeliveryCode.Delivered);
            var completed = (await service.ExecuteAsync(review.Id, new WorkflowCommand.Complete(), "tester", default)).Value;
            var model = await GuidedCompletionSaveTests.Open(h, service, delivery, new GuidedCompletionSaveTests.Picker(folder), completed);
            await model.PreviewsLoaded;
            var before = (await h.Repository.LoadAsync(model.Id, default)).Value!;
            Size size = new(width, height);
            WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = model }, size, tree =>
            {
                tree.OfType<Image>().Where(i => i.Source is not null && GuidedSessionProjectionTests.Shown(i))
                    .Max(i => i.ActualHeight).ShouldBeGreaterThan(100);
                var comparison = Find<Button>(tree, "Session.Save.ReviewResult");
                Inside(comparison, tree.Root);
                comparison.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Settle(tree.Root);
                var toggle = Find<Button>(tree, "Session.ReviewToolsToggle");
                toggle.BringIntoView(); Settle(tree.Root);
                Inside(toggle, tree.Root);
                var viewport = model.ReviewViewport;
                var mode = viewport.Mode;
                var background = viewport.Background;
                toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Settle(tree.Root);
                GuidedSessionProjectionTests.Shown(Find<Button>(tree, "Session.ReviewActualSize")).ShouldBeTrue();
                All(tree.Root).OfType<RadioButton>().Where(b => AutomationProperties.GetAutomationId(b).StartsWith("Session.ReviewBackground"))
                    .ShouldAllBe(b => GuidedSessionProjectionTests.Shown(b));
                // ReloadImages replaces the original visual nodes. Inspect the current tree,
                // and prove the expanded toolbar leaves a real, unclipped comparison picture.
                var images = All(tree.Root).OfType<Image>()
                    .Where(i => i.Source is not null && GuidedSessionProjectionTests.Shown(i)).ToArray();
                images.ShouldNotBeEmpty();
                images.Max(i => i.ActualWidth * i.ActualHeight).ShouldBeGreaterThanOrEqualTo(10_000,
                    "expanding inspection tools must preserve a usable comparison picture");
                foreach (var image in images) Inside(image, tree.Root);
                foreach (var tool in All(tree.Root).OfType<FrameworkElement>().Where(element =>
                    AutomationProperties.GetAutomationId(element) == "Session.ReviewActualSize" ||
                    AutomationProperties.GetAutomationId(element).StartsWith("Session.ReviewBackground")))
                {
                    tool.BringIntoView(); Settle(tree.Root); Inside(tool, tree.Root);
                }
                viewport.Mode.ShouldBe(mode); viewport.Background.ShouldBe(background);
                model.CanApprove.ShouldBeFalse();
                return true;
            });
            Capture(model, size, "completed-" + language, tree =>
            {
                Find<Button>(tree, "Session.Save.ReviewResult").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Settle(tree.Root);
                var tools = Find<Button>(tree, "Session.ReviewToolsToggle");
                tools.BringIntoView(); Settle(tree.Root);
                tools.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Settle(tree.Root);
            });
            var after = (await h.Repository.LoadAsync(model.Id, default)).Value!;
            after.Reviews.ShouldBe(before.Reviews); after.Revisions.ShouldBe(before.Revisions); after.Session.ShouldBe(before.Session);
        }
        finally { FinalSaveFixtures.Remove(folder); }
    }

    private static void Inside(FrameworkElement element, FrameworkElement root)
    {
        GuidedSessionProjectionTests.Shown(element).ShouldBeTrue();
        Rect bounds = element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
        bounds.Height.ShouldBeGreaterThan(0);
        bounds.Left.ShouldBeGreaterThanOrEqualTo(-0.5);
        bounds.Top.ShouldBeGreaterThanOrEqualTo(-0.5);
        bounds.Right.ShouldBeLessThanOrEqualTo(root.ActualWidth + 0.5);
        bounds.Bottom.ShouldBeLessThanOrEqualTo(root.ActualHeight + 0.5);
        foreach (var scroll in GuidedSessionShellTests.Ancestors(element).OfType<ScrollViewer>())
        {
            Rect inside = element.TransformToAncestor(scroll).TransformBounds(new Rect(element.RenderSize));
            inside.Top.ShouldBeGreaterThanOrEqualTo(-0.5);
            inside.Bottom.ShouldBeLessThanOrEqualTo(scroll.ActualHeight + 0.5);
        }
    }

    internal static void Settle(FrameworkElement root)
    {
        // Fluent disclosure/scroll animations continue after dispatcher idle. Pump a
        // bounded interval before measuring, rather than treating an in-flight frame
        // as a clipped layout or capturing blank newly expanded content.
        DispatcherFrame frame = new();
        DispatcherTimer timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(400) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
        // Expanding creates template content during layout; drain the bindings queued by
        // that layout as well, so screenshots and assertions inspect the actual content.
        for (int i = 0; i < 3; i++)
        {
            root.UpdateLayout();
            root.Dispatcher.Invoke(DispatcherPriority.SystemIdle, new Action(() => { }));
        }
        root.UpdateLayout();
    }
    private static IEnumerable<DependencyObject> All(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in All(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static void Capture(SessionViewModel model, Size size, string name, Action<RenderedTree>? prepare = null)
    {
        string? folder = Environment.GetEnvironmentVariable("PF_U05_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(folder)) return;
        WpfRendering.CapturePng(() => new SessionScreenView { DataContext = model }, size,
            Path.Combine(folder, $"{name}-{size.Width:0}x{size.Height:0}.png"), prepare);
    }
}
