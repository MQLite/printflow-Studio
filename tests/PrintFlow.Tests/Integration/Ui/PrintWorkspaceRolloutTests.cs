using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class PrintWorkspaceRolloutTests
{
    public static TheoryData<string, int, int> Viewports()
    {
        TheoryData<string, int, int> cases = new();
        foreach (string language in new[] { "zh-CN", "en-US" })
            foreach ((int w, int h) in new[] { (984, 661), (1000, 700), (1280, 720), (1920, 1080) })
                cases.Add(language, w, h);
        return cases;
    }

    // A large print summary must not squeeze the image or hide the live size input.
    [Theory]
    [MemberData(nameof(Viewports))]
    public async Task Custom_size_and_invalid_input_keep_the_preview_and_confirmation_reachable(string language, int width, int height)
    {
        using OperatorCultureScope culture = new(language);
        using HomeScreenHarness h = new();
        var model = await PrintSizeGuidanceSetup.OpenStateAsync(h, "draft");
        var size = new Size(width, height);
        foreach (string input in new[] { "123.456789", "invalid" })
        {
            model.CustomMillimetresText = input;
            await model.PreflightLoaded;
            WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = model }, size, tree =>
            {
                var field = Find<TextBox>(tree, "Session.PrintDimensions.TargetMillimetres");
                field.FontSize.ShouldBeGreaterThanOrEqualTo(16);
                field.ActualHeight.ShouldBeGreaterThanOrEqualTo(42);
                field.BringIntoView(); ProcessingWorkspaceRolloutTests.Settle(tree.Root); Inside(field, tree.Root);
                var confirm = Find<Button>(tree, "Session.PrintDimensions.ConfirmSize");
                Inside(confirm, tree.Root);
                confirm.IsEnabled.ShouldBe(input != "invalid");
                Preview(tree).ActualHeight.ShouldBeGreaterThanOrEqualTo(180);
                if (input == "invalid")
                    tree.OfType<TextBlock>().Where(t => GuidedSessionProjectionTests.Shown(t))
                        .ShouldNotContain(t => AutomationProperties.GetAutomationId(t) == "Session.PrintSummary.Actual");
                return true;
            });
            model.CustomMillimetresText.ShouldBe(input);
            model.HasUsableEnlargementAuthority.ShouldBeFalse();
            Capture(model, size, input == "invalid" ? "invalid" : "dimensions");
        }
    }

    // Tests the selection widgets, not just the backing collection: no implicit choice,
    // all three reasons readable, original confirm command and current-result summary.
    [Theory]
    [MemberData(nameof(Viewports))]
    public async Task Both_print_routes_keep_white_ink_choices_readable_without_implicit_selection(string language, int width, int height)
    {
        using OperatorCultureScope culture = new(language);
        foreach (var route in new[] { WorkflowType.GeneratePrintTiff, WorkflowType.PrepareCustomerDesign })
        {
            using HomeScreenHarness h = new();
            var model = await GuidedSessionProjectionTests.Open(h, route);
            await model.ConfirmOriginalCommand.ExecuteAsync(null);
            if (route == WorkflowType.PrepareCustomerDesign)
            {
                await model.SkipCommand.ExecuteAsync(null); await model.SkipCommand.ExecuteAsync(null);
                await model.RunStepCommand.ExecuteAsync(null); await model.ApproveCommand.ExecuteAsync(null);
            }
            model.WidthMmText = "200"; model.HeightMmText = "150";
            await model.SetMaximumBoundsCommand.ExecuteAsync(null);
            var size = new Size(width, height);
            WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = model }, size, tree =>
            {
                var choices = Find<ListBox>(tree, "Session.WhiteUnderbaseChoices");
                choices.SelectedIndex.ShouldBe(-1);
                choices.FontSize.ShouldBeGreaterThanOrEqualTo(16);
                foreach (var value in model.WhiteUnderbaseChoices)
                {
                    choices.ScrollIntoView(value); ProcessingWorkspaceRolloutTests.Settle(tree.Root);
                    var item = (ListBoxItem)choices.ItemContainerGenerator.ContainerFromItem(value);
                    item.ActualHeight.ShouldBeGreaterThanOrEqualTo(42);
                    item.BringIntoView(); ProcessingWorkspaceRolloutTests.Settle(tree.Root); Inside(item, tree.Root);
                    var label = Descendants(item).OfType<TextBlock>().Single(t => t.Text == value.Label);
                    label.TextWrapping.ShouldBe(TextWrapping.Wrap);
                    label.ActualWidth.ShouldBeGreaterThan(100);
                }
                var confirm = tree.OfType<Button>().Single(b => ReferenceEquals(b.Command, model.SelectWhiteUnderbaseCommand));
                confirm.IsEnabled.ShouldBeFalse(); Inside(confirm, tree.Root);
                Preview(tree).ActualHeight.ShouldBeGreaterThanOrEqualTo(180);
                return true;
            });
            model.SelectedWhiteUnderbaseChoice.ShouldBeNull();
            Capture(model, size, "white-" + route);
            var id = model.Id;
            model = h.Session(new RecordingNavigation());
            model.Open((await h.Sessions.LoadAsync(id, default)).Value);
            await model.PreviewsLoaded;
            model.SelectedWhiteUnderbaseChoice = model.WhiteUnderbaseChoices[0];
            await model.SelectWhiteUnderbaseCommand.ExecuteAsync(null);
            var before = (await h.Sessions.LoadAsync(model.Id, default)).Value;
            WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = model }, size, tree =>
            {
                Preview(tree).ActualHeight.ShouldBeGreaterThanOrEqualTo(180);
                foreach (var id in new[] { "Requested", "Actual", "Pixels", "WhiteInk", "Contract" })
                {
                    var fact = Find<TextBlock>(tree, "Session.PrintSummary." + id);
                    fact.BringIntoView(); ProcessingWorkspaceRolloutTests.Settle(tree.Root); Inside(fact, tree.Root);
                }
                return true;
            });
            Capture(model, size, "preflight-" + route);
            Capture(model, size, "contract-" + route, tree =>
            {
                Find<TextBlock>(tree, "Session.PrintSummary.Contract").BringIntoView();
                ProcessingWorkspaceRolloutTests.Settle(tree.Root);
            });
            var after = (await h.Sessions.LoadAsync(model.Id, default)).Value;
            after.WhiteUnderbaseBranch.ShouldBe(before.WhiteUnderbaseBranch);
            after.CurrentStep.ShouldBe(before.CurrentStep);
        }
    }

    [Theory]
    [MemberData(nameof(Viewports))]
    public async Task Tiff_tools_share_the_specialist_surface_and_do_not_approve_while_switching_modes(string language, int width, int height)
    {
        using OperatorCultureScope culture = new(language);
        using HomeScreenHarness h = TiffFinalReviewFixture.Harness(out _);
        var review = await TiffFinalReviewFixture.ReviewRequiredAsync(h, "rollout-tiff.png");
        var model = review.Screen;
        var before = await review.ReloadAsync();
        string? identity = model.ReviewTargetIdentity;
        var size = new Size(width, height);
        Capture(model, size, "tiff-before-check");
        WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = model }, size, tree =>
        {
            var surface = tree.OfType<TiffReviewSurface>().Single();
            foreach (var (id, command) in new[] { ("TiffReviewZoomIn", model.ZoomInCommand),
                ("TiffReviewZoomOut", model.ZoomOutCommand), ("TiffReviewResetZoom", model.ResetZoomCommand) })
            {
                var button = Find<Button>(tree, "Session." + id);
                surface.IsAncestorOf(button).ShouldBeTrue("zoom and ink modes belong to the same specialist toolbar");
                button.Command.ShouldBeSameAs(command); Inside(button, tree.Root);
            }
            foreach (string id in new[] { "TiffReviewColour", "TiffReviewWhiteInk", "TiffReviewOverlay" })
            {
                var mode = Find<RadioButton>(tree, "Session." + id);
                mode.MinHeight.ShouldBeGreaterThanOrEqualTo(42);
                mode.IsChecked = true; ProcessingWorkspaceRolloutTests.Settle(tree.Root); Inside(mode, tree.Root);
                var viewport = Find<ScrollViewer>(tree, "Session.TiffReviewImage");
                var image = Descendants(viewport).OfType<Image>().Single();
                image.ActualWidth.ShouldBeGreaterThan(100, "a nonzero viewport must contain a visible fitted image");
                image.ActualHeight.ShouldBeGreaterThan(100);
                image.ActualWidth.ShouldBeLessThanOrEqualTo(viewport.ViewportWidth + 1);
                image.ActualHeight.ShouldBeLessThanOrEqualTo(viewport.ViewportHeight + 1);
                viewport.ViewportHeight.ShouldBeGreaterThanOrEqualTo(180);
                viewport.ViewportWidth.ShouldBeGreaterThanOrEqualTo(300);
            }
            Find<TextBlock>(tree, "Session.TiffReviewZoomStatus").Text.ShouldBe(model.ZoomLabel);
            model.ReviewTargetIdentity.ShouldBe(identity);
            return true;
        });
        Capture(model, size, "tiff");
        Capture(model, size, "tiff-output", tree =>
        {
            Find<ItemsControl>(tree, "Session.OutputList").BringIntoView();
            ProcessingWorkspaceRolloutTests.Settle(tree.Root);
        });
        var after = await review.ReloadAsync();
        after.Reviews.ShouldBe(before.Reviews); after.Revisions.ShouldBe(before.Revisions); after.Outputs.ShouldBe(before.Outputs);
        model.IsReviewRequired.ShouldBeTrue();
    }

    // The same mounted surface must preserve the inspected region through zoom, ink
    // switches and window resize. Separate initial renders cannot prove that contract.
    [Fact]
    public async Task Tiff_zoom_pan_resize_and_reset_keep_the_same_result_and_inspected_region()
    {
        using OperatorCultureScope culture = new("zh-CN");
        using HomeScreenHarness h = TiffFinalReviewFixture.Harness(out _);
        var review = await TiffFinalReviewFixture.ReviewRequiredAsync(h, "tiff-viewport.png");
        var model = review.Screen;
        string? identity = model.ReviewTargetIdentity;
        var before = await review.ReloadAsync();
        WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = model }, new Size(1000, 700), tree =>
        {
            var viewport = Find<ScrollViewer>(tree, "Session.TiffReviewImage");
            var image = Descendants(viewport).OfType<Image>().Single();
            var zoom = Find<Button>(tree, "Session.TiffReviewZoomIn");
            for (int i = 0; i < 8; i++) zoom.Command.Execute(null);
            ProcessingWorkspaceRolloutTests.Settle(tree.Root);
            model.IsFitToViewport.ShouldBeFalse();
            image.ActualWidth.ShouldBe(model.TiffPreviewPixelWidth);
            ((ScaleTransform)image.LayoutTransform).ScaleX.ShouldBe(model.ZoomScale);
            viewport.ScrollableWidth.ShouldBeGreaterThan(0); viewport.ScrollableHeight.ShouldBeGreaterThan(0);
            viewport.ScrollToHorizontalOffset(viewport.ScrollableWidth * 0.4);
            viewport.ScrollToVerticalOffset(viewport.ScrollableHeight * 0.6);
            ProcessingWorkspaceRolloutTests.Settle(tree.Root);
            model.ReviewViewport.HorizontalPosition.ShouldBe(0.4, 0.01);
            model.ReviewViewport.VerticalPosition.ShouldBe(0.6, 0.01);
            foreach (var size in new[] { new Size(1280, 720), new Size(984, 661) })
            {
                Find<RadioButton>(tree, "Session.TiffReviewWhiteInk").IsChecked = true;
                tree.Root.Measure(size); tree.Root.Arrange(new Rect(size));
                ProcessingWorkspaceRolloutTests.Settle(tree.Root);
                (viewport.HorizontalOffset / viewport.ScrollableWidth).ShouldBe(0.4, 0.01);
                (viewport.VerticalOffset / viewport.ScrollableHeight).ShouldBe(0.6, 0.01);
                model.ReviewTargetIdentity.ShouldBe(identity);
            }
            Find<Button>(tree, "Session.TiffReviewResetZoom").Command.Execute(null);
            ProcessingWorkspaceRolloutTests.Settle(tree.Root);
            model.IsFitToViewport.ShouldBeTrue();
            image.ActualWidth.ShouldBeGreaterThan(100);
            image.ActualWidth.ShouldBeLessThanOrEqualTo(viewport.ViewportWidth + 1);
            image.ActualHeight.ShouldBeLessThanOrEqualTo(viewport.ViewportHeight + 1);
            return true;
        });
        var after = await review.ReloadAsync();
        after.Reviews.ShouldBe(before.Reviews); after.Revisions.ShouldBe(before.Revisions); after.Outputs.ShouldBe(before.Outputs);
    }

    private static SharedReviewSurface Preview(RenderedTree tree) => tree.OfType<SharedReviewSurface>().Single();
    private static T Find<T>(RenderedTree tree, string id) where T : FrameworkElement => GuidedSessionShellTests.Find<T>(tree, id);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static void Inside(FrameworkElement element, FrameworkElement root)
    {
        GuidedSessionProjectionTests.Shown(element).ShouldBeTrue();
        var bounds = element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
        bounds.Width.ShouldBeGreaterThan(0); bounds.Height.ShouldBeGreaterThan(0);
        bounds.Left.ShouldBeGreaterThanOrEqualTo(-0.5); bounds.Top.ShouldBeGreaterThanOrEqualTo(-0.5);
        bounds.Right.ShouldBeLessThanOrEqualTo(root.ActualWidth + 0.5);
        bounds.Bottom.ShouldBeLessThanOrEqualTo(root.ActualHeight + 0.5);
        foreach (var scroll in GuidedSessionShellTests.Ancestors(element).OfType<ScrollViewer>())
        {
            var visible = element.TransformToAncestor(scroll).TransformBounds(new Rect(element.RenderSize));
            visible.Top.ShouldBeGreaterThanOrEqualTo(-0.5);
            visible.Bottom.ShouldBeLessThanOrEqualTo(scroll.ActualHeight + 0.5);
        }
    }
    private static void Capture(SessionViewModel model, Size size, string state, Action<RenderedTree>? prepare = null)
    {
        string? folder = Environment.GetEnvironmentVariable("PF_U06_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(folder)) return;
        WpfRendering.CapturePng(() => new SessionScreenView { DataContext = model }, size,
            Path.Combine(folder, $"{state}-{PrintFlow.App.Resources.OperatorCulture.Current.Name}-{size.Width}x{size.Height}.png"), prepare);
    }
}
