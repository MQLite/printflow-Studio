using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Ids;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class SharedReviewSurfaceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Real_scroll_events_synchronize_unequal_extents_bidirectionally_without_oscillation(bool fromAfter)
    {
        WithSurface((model, view) =>
        {
            ScrollViewer[] panes = Descendants<ScrollViewer>(view).ToArray();
            panes.Length.ShouldBe(2);
            panes[0].ScrollableWidth.ShouldNotBe(panes[1].ScrollableWidth);
            int events = 0;
            foreach (ScrollViewer pane in panes) pane.ScrollChanged += (_, _) => events++;
            ScrollViewer source = panes[fromAfter ? 1 : 0];
            source.ScrollToHorizontalOffset(source.ScrollableWidth * 0.7);
            source.ScrollToVerticalOffset(source.ScrollableHeight * 0.3);
            Settle(view);
            foreach (ScrollViewer pane in panes)
            {
                (pane.HorizontalOffset / pane.ScrollableWidth).ShouldBe(0.7, 0.005);
                (pane.VerticalOffset / pane.ScrollableHeight).ShouldBe(0.3, 0.005);
            }
            events.ShouldBeLessThan(12);
            int settled = events;
            Settle(view);
            events.ShouldBe(settled);
        });
    }

    [Fact]
    public void Resize_zoom_fit_and_reset_retain_bounded_corresponding_positions()
    {
        WithSurface((model, view) =>
        {
            model.ReviewViewport.HorizontalPosition = 0.8;
            model.ReviewViewport.VerticalPosition = 0.2;
            Settle(view);
            Arrange(view, 640, 350);
            model.ZoomScale = 2;
            Settle(view);
            AssertPositions(view, 0.8, 0.2);
            model.IsFitToViewport = true;
            Settle(view);
            Descendants<ScrollViewer>(view).ShouldAllBe(s => s.ScrollableWidth == 0 && s.ScrollableHeight == 0);
            model.ReviewViewport.HorizontalPosition.ShouldBe(0.8);
            model.IsFitToViewport = false;
            Settle(view);
            AssertPositions(view, 0.8, 0.2);
            model.ReviewViewport.Background = ReviewInspectionBackground.Black;
            model.ResetZoomCommand.Execute(null);
            Settle(view);
            model.IsFitToViewport.ShouldBeTrue();
            model.ReviewViewport.HorizontalPosition.ShouldBe(0.5);
            model.ReviewViewport.VerticalPosition.ShouldBe(0.5);
            model.ReviewViewport.Background.ShouldBe(ReviewInspectionBackground.Black);
        });
    }

    [Fact]
    public void One_fitting_pane_does_not_erase_position_of_the_scrollable_pane()
    {
        WithSurface((model, view) =>
        {
            model.ZoomScale = 0.2;
            Settle(view);
            ScrollViewer[] panes = Descendants<ScrollViewer>(view).ToArray();
            panes[1].ScrollableWidth.ShouldBe(0);
            panes[0].ScrollToHorizontalOffset(panes[0].ScrollableWidth * 0.75);
            Settle(view);
            model.ZoomScale = 2;
            Settle(view);
            AssertPositions(view, 0.75, model.ReviewViewport.VerticalPosition);
        });
    }

    [Theory]
    [InlineData(ReviewInspectionBackground.Checkerboard)]
    [InlineData(ReviewInspectionBackground.White)]
    [InlineData(ReviewInspectionBackground.Black)]
    public void Background_renders_behind_alpha_and_reuses_the_same_bitmaps(ReviewInspectionBackground background)
    {
        WithSurface((model, view) =>
        {
            Image[] images = Descendants<Image>(view).ToArray();
            ImageSource[] sources = images.Select(i => i.Source).ToArray();
            ArtefactPreviewPane[] panes = model.PreviewPanes.ToArray();
            model.ReviewViewport.HorizontalPosition = 0;
            model.ReviewViewport.VerticalPosition = 0;
            model.ReviewViewport.Background = background;
            Settle(view);
            for (int i = 0; i < images.Length; i++)
            {
                images[i].Source.ShouldBeSameAs(sources[i]);
                model.PreviewPanes[i].ShouldBeSameAs(panes[i]);
                BitmapSource bitmap = (BitmapSource)sources[i];
                byte[] pixel = new byte[4];
                bitmap.CopyPixels(new Int32Rect(0, 0, 1, 1), pixel, 4, 0);
                pixel[3].ShouldBe((byte)0);
                Grid canvas = (Grid)images[i].Parent;
                Color rendered = Pixel(canvas, 1, 1);
                if (background == ReviewInspectionBackground.Black) rendered.ShouldBe(Colors.Black);
                else if (background == ReviewInspectionBackground.White) rendered.ShouldBe(Colors.White);
                else canvas.Background.ShouldBeOfType<DrawingBrush>();
            }
            RadioButton selected = Descendants<RadioButton>(view).Single(b => AutomationProperties.GetAutomationId(b) == "Session.ReviewBackground" + background);
            selected.IsChecked.ShouldBe(true);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    [InlineData(50)]
    [InlineData(75)]
    [InlineData(100)]
    public void Slider_clips_aligned_natural_aspects_after_zoom_pan_and_round_trip(int position)
    {
        WithSurface((model, view) =>
        {
            ImageSource[] sources = Descendants<Image>(view).Select(i => i.Source).ToArray();
            model.ReviewViewport.HorizontalPosition = 0.7;
            model.ReviewViewport.VerticalPosition = 0.3;
            model.ReviewViewport.Mode = ReviewComparisonMode.Slider;
            model.ReviewViewport.SliderPosition = position;
            model.ZoomScale = 1.5;
            Settle(view);
            Grid overlay = Descendants<Grid>(view).Single(g => g.Clip is RectangleGeometry);
            ((RectangleGeometry)overlay.Clip).Rect.Width.ShouldBe(overlay.Width * position / 100, 0.001);
            Image[] images = Descendants<Image>(view).ToArray();
            for (int i = 0; i < images.Length; i++)
            {
                images[i].Source.ShouldBeSameAs(sources[i]);
                (images[i].ActualWidth / images[i].ActualHeight).ShouldBe((double)model.PreviewPanes[i].PayloadPixelWidth / model.PreviewPanes[i].PayloadPixelHeight, 0.001);
                images[i].TranslatePoint(new Point(), (UIElement)overlay.Parent).ShouldBe(new Point());
            }
            AssertPositions(view, 0.7, 0.3);
            model.ReviewViewport.Mode = ReviewComparisonMode.SideBySide;
            Settle(view);
            AssertPositions(view, 0.7, 0.3);
            model.ZoomScale.ShouldBe(1.5);
        });
    }

    [Theory]
    [InlineData("en-US", "Comparison slider", "White")]
    [InlineData("zh-CN", "比较滑块", "白色")]
    public void Localized_controls_have_stable_ids_and_standard_keyboard_UIA_patterns(string culture, string sliderName, string whiteName)
    {
        WithSurface((model, view) =>
        {
            model.ReviewViewport.Mode = ReviewComparisonMode.Slider;
            Settle(view);
            RadioButton[] choices = Descendants<RadioButton>(view).ToArray();
            choices.Length.ShouldBe(5);
            foreach (RadioButton choice in choices)
            {
                choice.Focusable.ShouldBeTrue();
                choice.IsTabStop.ShouldBeTrue();
                AutomationProperties.GetAutomationId(choice).ShouldStartWith("Session.Review");
                new RadioButtonAutomationPeer(choice).GetPattern(PatternInterface.SelectionItem).ShouldBeAssignableTo<ISelectionItemProvider>();
            }
            AutomationProperties.GetName(choices.Single(b => AutomationProperties.GetAutomationId(b) == "Session.ReviewBackgroundWhite")).ShouldBe(whiteName);
            Slider slider = Descendants<Slider>(view).Single();
            SliderAutomationPeer peer = new(slider);
            peer.GetAutomationId().ShouldBe("Session.ReviewComparisonSlider");
            peer.GetName().ShouldBe(sliderName);
            IRangeValueProvider provider = (IRangeValueProvider)peer.GetPattern(PatternInterface.RangeValue);
            provider.SetValue(25);
            model.ReviewViewport.SliderPosition.ShouldBe(25);
            Descendants<Image>(view).ShouldAllBe(i => !i.Focusable);
        }, culture);
    }

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(50, true, false)]
    [InlineData(100, true, true)]
    public void Slider_boundaries_render_before_and_after_pixels(int position, bool afterLeft, bool afterRight)
    {
        WithSurface((model, view) =>
        {
            model.PreviewPanes.Clear();
            foreach (var (heading, colour) in new[] { (model.BeforeLabel, (R: (byte)255, G: (byte)0, B: (byte)0)), (model.AfterLabel, (R: (byte)0, G: (byte)255, B: (byte)0)) })
            {
                model.PreviewPanes.Add(ArtefactPreviewPane.From(heading, "colour.png", new ImagePreview(new RevisionId(Guid.NewGuid()), 1800, 1200, 1800, 1200, false,
                    SyntheticImages.OpaqueRgbPng(1800, 1200, (_, _) => colour, 96))));
            }
            model.ReviewViewport.HorizontalPosition = 0;
            model.ReviewViewport.VerticalPosition = 0;
            model.ReviewViewport.Mode = ReviewComparisonMode.Slider;
            model.ReviewViewport.SliderPosition = position;
            Settle(view);
            Grid canvas = (Grid)Descendants<ScrollViewer>(view).Single().Content;
            Pixel(canvas, 10, 10).ShouldBe(afterLeft ? Colors.Lime : Colors.Red);
            Pixel(canvas, 1500, 10).ShouldBe(afterRight ? Colors.Lime : Colors.Red);
        });
    }

    [Theory]
    [InlineData(ReviewInspectionBackground.White)]
    [InlineData(ReviewInspectionBackground.Black)]
    [InlineData(ReviewInspectionBackground.Checkerboard)]
    public void Transparent_white_dark_and_partial_alpha_edges_render_over_inspection_brush(ReviewInspectionBackground background)
    {
        WithSurface((model, view) =>
        {
            byte[] pixels = new byte[64 * 32 * 4];
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 64; x++)
                {
                    int offset = (y * 64 + x) * 4;
                    byte colour = x < 32 ? (byte)255 : (byte)0;
                    pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = colour;
                    pixels[offset + 3] = x < 16 ? (byte)0 : (byte)128;
                }
            BitmapSource source = BitmapSource.Create(64, 32, 96, 96, PixelFormats.Bgra32, null, pixels, 64 * 4);
            PngBitmapEncoder encoder = new();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using System.IO.MemoryStream stream = new();
            encoder.Save(stream);
            model.PreviewPanes.Clear();
            model.PreviewPanes.Add(ArtefactPreviewPane.From(model.BeforeLabel, "halos.png", new ImagePreview(new RevisionId(Guid.NewGuid()), 64, 32, 64, 32, true, stream.ToArray())));
            model.ReviewViewport.Background = background;
            Settle(view);
            Grid canvas = (Grid)Descendants<Image>(view).Single().Parent;
            // Remove only the layout offset for a deterministic offscreen pixel capture.
            DrawingVisual visual = new();
            using (DrawingContext drawing = visual.RenderOpen())
                drawing.DrawRectangle(new VisualBrush(canvas) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, 64, 32));
            Color clear = Pixel(visual, 1, 1);
            Color whiteHalo = Pixel(visual, 17, 1);
            Color darkHalo = Pixel(visual, 33, 1);
            if (background == ReviewInspectionBackground.White)
            {
                clear.ShouldBe(Colors.White);
                whiteHalo.ShouldBe(Colors.White);
                darkHalo.R.ShouldBeInRange((byte)126, (byte)128);
            }
            else if (background == ReviewInspectionBackground.Black)
            {
                clear.ShouldBe(Colors.Black);
                whiteHalo.R.ShouldBeInRange((byte)127, (byte)129);
                darkHalo.ShouldBe(Colors.Black);
            }
            else
            {
                clear.R.ShouldBeInRange((byte)200, (byte)220);
                whiteHalo.R.ShouldBeGreaterThan(clear.R);
                darkHalo.R.ShouldBeLessThan(clear.R);
            }
        });
    }
    private static void WithSurface(Action<SessionViewModel, SharedReviewSurface> inspect, string culture = "en-US")
    {
        using HomeScreenHarness harness = new();
        WpfRendering.OnStaThread(() =>
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            SessionViewModel model = harness.Session(new RecordingNavigation());
            model.IsFitToViewport = false;
            model.PreviewPanes.Add(Pane(model.BeforeLabel, 1800, 1200));
            model.PreviewPanes.Add(Pane(model.AfterLabel, 1000, 800));
            SharedReviewSurface view = new() { DataContext = model };
            Arrange(view, 700, 400);
            try { inspect(model, view); }
            finally { view.DataContext = null; }
        });
    }

    private static ArtefactPreviewPane Pane(string heading, int width, int height) => ArtefactPreviewPane.From(heading, "synthetic.png",
        new ImagePreview(new RevisionId(Guid.NewGuid()), width, height, width, height, true, SyntheticImages.PngWithAlpha(width, height, (x, _) => x < 20 ? (byte)0 : (byte)128, dpi: 96)));

    internal static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (T child in Descendants<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    internal static void Settle(FrameworkElement view)
    {
        for (int i = 0; i < 4; i++)
        {
            view.UpdateLayout();
            DispatcherFrame frame = new();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
    }

    private static void Arrange(FrameworkElement view, double width, double height)
    {
        view.Measure(new Size(width, height));
        view.Arrange(new Rect(0, 0, width, height));
        Settle(view);
    }

    private static void AssertPositions(DependencyObject view, double x, double y)
    {
        foreach (ScrollViewer pane in Descendants<ScrollViewer>(view))
        {
            if (pane.ScrollableWidth > 0) (pane.HorizontalOffset / pane.ScrollableWidth).ShouldBe(x, 0.005);
            if (pane.ScrollableHeight > 0) (pane.VerticalOffset / pane.ScrollableHeight).ShouldBe(y, 0.005);
            double.IsFinite(pane.HorizontalOffset).ShouldBeTrue();
            double.IsFinite(pane.VerticalOffset).ShouldBeTrue();
        }
    }

    private static Color Pixel(Visual surface, int x, int y)
    {
        RenderTargetBitmap bitmap = new(Math.Max(64, x + 1), Math.Max(32, y + 1), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(surface);
        byte[] pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }
}
