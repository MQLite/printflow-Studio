using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Tests.Fixtures;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class GuidedThemeVisualEvidenceTests
{
    public static IEnumerable<object[]> ComponentCases =>
        from state in new[] { "normal", "disabled", "error", "loading", "long-text" }
        from size in new[] { (1000, 700), (1280, 720), (1920, 1080) }
        select new object[] { state, size.Item1, size.Item2 };

    [Theory]
    [MemberData(nameof(ComponentCases))]
    public void All_component_states_fit_with_wrapping_and_visible_actions(string state, int width, int height)
    {
        using OperatorCultureScope culture = new("zh-CN");
        Size viewport = new(width, height);
        Func<UserControl> create = () => GuidedComponentCatalog.Create(state);
        var layout = WpfRendering.RenderExpectingNoBindingErrors(create, viewport, tree =>
        {
            AssertTextWidth(tree, width);
            ContentControl bar = Find<ContentControl>(tree, "GuidedCatalog.ActionBar");
            ScrollViewer content = Find<ScrollViewer>(tree, "GuidedCatalog.Content");
            Rect barBounds = Bounds(bar, tree.Root);
            AssertInside(barBounds, viewport);
            Bounds(content, tree.Root).Bottom.ShouldBeLessThanOrEqualTo(barBounds.Top);
            Button primary = Find<Button>(tree, "GuidedCatalog.Continue");
            AssertInside(Bounds(primary, tree.Root), viewport);
            primary.IsEnabled.ShouldBe(state is "normal" or "long-text");
            if (state == "loading")
            {
                Button stop = Find<Button>(tree, "GuidedCatalog.Stop");
                stop.IsEnabled.ShouldBeTrue();
                AssertInside(Bounds(stop, tree.Root), viewport);
                tree.OfType<ProgressBar>().Single().IsIndeterminate.ShouldBeTrue();
            }

            Expander disclosure = tree.OfType<Expander>().Single();
            Button hidden = Find<Button>(tree, "GuidedCatalog.CollapsedAction");
            disclosure.IsExpanded.ShouldBeFalse();
            // WPF may defer the collapsed content's visual tree entirely; either case removes it from Tab layout.
            (VisualTreeHelper.GetParent(hidden) is null || Ancestors(hidden).OfType<UIElement>()
                .Any(a => a.Visibility == Visibility.Collapsed)).ShouldBeTrue();
            var peer = UIElementAutomationPeer.CreatePeerForElement(disclosure)!;
            Descendants(peer).ShouldNotContain(p => p.GetAutomationId() == "GuidedCatalog.CollapsedAction");
            GuidedCatalogActions actions = (GuidedCatalogActions)tree.Root.Tag;
            actions.ContinueCount.ShouldBe(0);
            actions.StopCount.ShouldBe(0);
            return new { ActionBar = Measure(barBounds), Content = Measure(Bounds(content, tree.Root)),
                ContinueEnabled = primary.IsEnabled, StopEnabled = state == "loading", CommandExecutions = 0 };
        });
        Capture(create, viewport, $"components-{state}", layout.Facts, sourceHash: null);
    }

    [Theory]
    [InlineData(1000, 700)]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    public async Task Representative_views_capture_the_same_synthetic_sources(int width, int height)
    {
        using OperatorCultureScope culture = new("zh-CN");
        using HomeScreenHarness harness = new();
        string source = harness.Inner.WriteBorderedSourcePng("透明边缘—合成检查图片.png");
        string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));
        harness.FilePicker.Path = source;
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);
        WorkflowSelectionViewModel route = harness.WorkflowSelection(new RecordingNavigation());
        route.Open(harness.Navigation.WorkflowSelectionFor!);
        await route.PreviewLoaded;
        await harness.Home.RefreshCommand.ExecuteAsync(null);
        await harness.Home.ThumbnailsLoaded;
        Size viewport = new(width, height);
        foreach (string page in new[] { "home", "workflow-selection" })
        {
            Func<UserControl> create = page == "home"
                ? () => new HomeView { DataContext = harness.Home }
                : () => new WorkflowSelectionView { DataContext = route };
            var layout = WpfRendering.RenderExpectingNoBindingErrors(create, viewport, tree =>
            {
                Settle(tree.Root);
                AssertTextWidth(tree, width);
                tree.OfType<Image>().ShouldContain(i => i.Source != null);
                Button action = Find<Button>(tree, page == "home" ? "Home.ChooseFile" : "WorkflowSelection.Back");
                action.Command.ShouldBeSameAs(page == "home" ? harness.Home.ChooseFileCommand : route.BackToHomeCommand);
                if (page == "home") { action.BringIntoView(); Settle(tree.Root); }
                AssertInside(Bounds(action, tree.Root), viewport);
                return new { MainAction = Measure(Bounds(action, tree.Root)), ActionEnabled = action.IsEnabled,
                    PreviewCount = tree.OfType<Image>().Count(i => i.Source != null), NoProcessingStarted = harness.Meitu.CallCount == 0 };
            });
            Capture(create, viewport, page, layout.Facts, hash, tree =>
            {
                Settle(tree.Root);
                if (page == "home") { Find<Button>(tree, "Home.ChooseFile").BringIntoView(); Settle(tree.Root); }
            });
        }
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))).ShouldBe(hash);
        harness.Meitu.CallCount.ShouldBe(0);
        string? directory = Environment.GetEnvironmentVariable("PF_11177_CAPTURE_DIR");
        if (!string.IsNullOrWhiteSpace(directory)) File.Copy(source, Path.Combine(directory, "synthetic-source.png"), overwrite: true);
    }

    private static void Capture<T>(Func<UserControl> create, Size viewport, string state, T measurements,
        string? sourceHash, Action<RenderedTree>? prepare = null)
    {
        string? directory = Environment.GetEnvironmentVariable("PF_11177_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return; // Layout still runs; screenshot generation is NOT RUN.
        string stem = $"{state}-{viewport.Width:0}x{viewport.Height:0}";
        string path = Path.Combine(directory, stem + ".png");
        WpfRendering.CapturePng(create, viewport, path, prepare);
        File.WriteAllText(Path.Combine(directory, stem + ".json"), JsonSerializer.Serialize(new
        {
            State = state, Language = "zh-CN", WidthDip = viewport.Width, HeightDip = viewport.Height,
            Dpi = 96, CaptureKind = "offscreen WPF; no Window.Show or desktop input", SourceSha256 = sourceHash,
            ScreenshotSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
            Measurements = measurements,
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static T Find<T>(RenderedTree tree, string id) where T : FrameworkElement =>
        tree.OfType<T>().Single(e => AutomationProperties.GetAutomationId(e) == id);

    private static Rect Bounds(FrameworkElement element, Visual ancestor) =>
        element.TransformToAncestor(ancestor).TransformBounds(new Rect(element.RenderSize));

    private static object Measure(Rect bounds) => new { bounds.X, bounds.Y, bounds.Width, bounds.Height };

    private static void AssertInside(Rect bounds, Size viewport)
    {
        bounds.Left.ShouldBeGreaterThanOrEqualTo(-0.5);
        bounds.Top.ShouldBeGreaterThanOrEqualTo(-0.5);
        bounds.Right.ShouldBeLessThanOrEqualTo(viewport.Width + 0.5);
        bounds.Bottom.ShouldBeLessThanOrEqualTo(viewport.Height + 0.5);
        bounds.Height.ShouldBeGreaterThan(0);
    }

    private static void AssertTextWidth(RenderedTree tree, double width)
    {
        foreach (TextBlock text in tree.OfType<TextBlock>().Where(t => t.ActualWidth > 0 && t.ActualHeight > 0 &&
                     t.Visibility == Visibility.Visible && !Ancestors(t).OfType<UIElement>().Any(a => a.Visibility != Visibility.Visible)))
        {
            Rect bounds = Bounds(text, tree.Root);
            bounds.Left.ShouldBeGreaterThanOrEqualTo(-0.5, text.Text);
            bounds.Right.ShouldBeLessThanOrEqualTo(width + 0.5, text.Text);
        }
    }

    private static IEnumerable<DependencyObject> Ancestors(DependencyObject node)
    {
        while ((node = VisualTreeHelper.GetParent(node)) is not null) yield return node;
    }

    private static IEnumerable<AutomationPeer> Descendants(AutomationPeer peer)
    {
        foreach (AutomationPeer child in peer.GetChildren() ?? [])
        {
            yield return child;
            foreach (AutomationPeer descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void Settle(UserControl root)
    {
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.SystemIdle);
        root.UpdateLayout();
    }
}
