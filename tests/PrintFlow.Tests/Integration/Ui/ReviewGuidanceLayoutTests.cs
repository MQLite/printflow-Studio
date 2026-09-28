using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Tests.Fixtures;

namespace PrintFlow.Tests.Integration.Ui;

/// <summary>
/// SCRUM-11149 layout: the "What to check" section sits in the right-hand details column and the
/// picture under review keeps exactly the area it had before the section existed. Off-screen WPF
/// at 96 DPI only; no window, UIA or input.
/// </summary>
[Collection(SqliteCollection.Name)]
public sealed class ReviewGuidanceLayoutTests
{
    public static TheoryData<string, string, double, double> Cases()
    {
        TheoryData<string, string, double, double> cases = new();
        foreach (string step in ReviewGuidanceSetup.Steps)
            foreach (string language in new[] { "en", "zh-CN" })
                foreach ((double w, double h) in new[] { (1000d, 700d), (1920d, 1040d) })
                    cases.Add(step, language, w, h);
        return cases;
    }

    /// <summary>
    /// The review picture's area, measured on the unchanged baseline (fddf243) with the same
    /// fixtures and viewports. The section must not take any of it.
    /// </summary>
    private static readonly Dictionary<(string Step, double Width), Rect> BaselineImageArea = new()
    {
        [("enhancement", 1000)] = new(24, 382.28, 572, 248.48),
        [("enhancement", 1920)] = new(24, 382.28, 1492, 588.48),
        [("background", 1000)] = new(24, 382.28, 572, 248.48),
        [("background", 1920)] = new(24, 382.28, 1492, 588.48),
        [("trim", 1000)] = new(24, 382.28, 572, 248.48),
        [("trim", 1920)] = new(24, 382.28, 1492, 588.48),
        [("tiff", 1000)] = new(24, 376.28, 572, 148.95),
        [("tiff", 1920)] = new(24, 361.04, 1492, 504.19),
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task The_review_picture_keeps_its_area_and_the_guidance_stays_in_the_details_column(
        string step, string language, double width, double height)
    {
        using OperatorCultureScope culture = new(language);
        using SessionServiceHarness h = new();
        SessionViewModel screen = (await ReviewGuidanceSetup.OpenAtAsync(h, step)).Screen;
        Size viewport = new(width, height);

        var facts = WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = screen }, viewport, tree =>
        {
            tree.Root.Dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
            tree.Root.UpdateLayout();
            FrameworkElement surface = step == "tiff"
                ? tree.OfType<TiffReviewSurface>().Single()
                : tree.OfType<SharedReviewSurface>().Single();
            Rect image = Bounds(surface, tree.Root);
            FrameworkElement? guidance = tree.OfType<FrameworkElement>()
                .SingleOrDefault(e => AutomationProperties.GetAutomationId(e) == "Session.ReviewGuidance" && Shown(e));
            ScrollViewer details = tree.OfType<ScrollViewer>().Single(s => Grid.GetColumn(s) == 1 && s.Parent is Grid);
            Rect column = Bounds(details, tree.Root);
            int tooWide = guidance is null ? 0 : tree.OfType<TextBlock>()
                .Count(t => Shown(t) && IsInside(t, guidance) && t.ActualWidth > guidance.ActualWidth + 0.5);
            return (Image: image, Column: column, Guidance: guidance is null ? (Rect?)null : Bounds(guidance, tree.Root),
                GuidanceInColumn: guidance is not null && IsInside(guidance, details), TooWide: tooWide,
                ExtentHeight: details.ExtentHeight, ViewportHeight: details.ViewportHeight);
        });

        Record(step, language, width, height, facts.Facts);
        facts.Facts.Image.Width.ShouldBeGreaterThan(0);
        facts.Facts.Image.Height.ShouldBeGreaterThan(0);
        Rect before = BaselineImageArea[(step, width)];
        foreach ((double actual, double expected) in new[] { (facts.Facts.Image.X, before.X), (facts.Facts.Image.Y, before.Y),
                     (facts.Facts.Image.Width, before.Width), (facts.Facts.Image.Height, before.Height) })
            actual.ShouldBe(expected, 0.01, $"the {step} picture keeps its {width:0}×{height:0} area");

        Rect guidance = facts.Facts.Guidance.ShouldNotBeNull("the section is shown beside every reviewed step");
        facts.Facts.GuidanceInColumn.ShouldBeTrue("the section lives in the scrollable details column");
        facts.Facts.TooWide.ShouldBe(0, "every line wraps inside the section");
        guidance.IntersectsWith(facts.Facts.Image).ShouldBeFalse("the section never covers the picture");
        guidance.Left.ShouldBe(facts.Facts.Column.Left, 0.5);
        guidance.Right.ShouldBeLessThanOrEqualTo(facts.Facts.Column.Right + 0.5);
        guidance.Top.ShouldBeLessThan(facts.Facts.Column.Bottom - 30, "its heading is visible without scrolling");

        Capture(screen, viewport, $"guidance-{step}-{language}-{width:0}x{height:0}.png");
    }

    internal static Rect Bounds(FrameworkElement element, Visual root) =>
        element.TransformToAncestor(root).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

    internal static bool Shown(DependencyObject element)
    {
        for (DependencyObject? node = element; node is not null; node = VisualTreeHelper.GetParent(node))
            if (node is UIElement { Visibility: not Visibility.Visible }) return false;
        return true;
    }

    internal static bool IsInside(DependencyObject element, DependencyObject root)
    {
        for (DependencyObject? node = element; node is not null; node = VisualTreeHelper.GetParent(node))
            if (ReferenceEquals(node, root)) return true;
        return false;
    }

    private static void Record(string step, string language, double width, double height, object facts)
    {
        string? destination = Environment.GetEnvironmentVariable("PF_11149_LAYOUT_DIR");
        if (string.IsNullOrWhiteSpace(destination)) return;
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(destination, $"layout-{step}-{language}-{width:0}x{height:0}.json"),
            JsonSerializer.Serialize(new { step, language, width, height, facts = facts.ToString() }));
    }

    internal static void Capture(SessionViewModel screen, Size viewport, string name)
    {
        string? destination = Environment.GetEnvironmentVariable("PF_11149_CAPTURE_DIR");
        if (!string.IsNullOrWhiteSpace(destination))
            WpfRendering.CapturePng(() => new SessionScreenView { DataContext = screen }, viewport, Path.Combine(destination, name),
                tree => tree.Root.Dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { })));
    }
}
