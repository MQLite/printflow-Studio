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
/// SCRUM-11150 layout: the print-size guidance lives in the right-hand details column, wraps
/// inside it, stays reachable by scrolling, and the picture keeps exactly the area it had before
/// the guidance existed. Off-screen WPF at 96 DPI only; no window, UIA or input.
/// </summary>
[Collection(SqliteCollection.Name)]
public sealed class PrintSizeGuidanceLayoutTests
{
    public static TheoryData<string, string, double, double> Cases()
    {
        TheoryData<string, string, double, double> cases = new();
        foreach (string state in PrintSizeGuidanceSetup.States.Append("details"))
            foreach (string language in new[] { "en", "zh-CN" })
                foreach ((double w, double h) in new[] { (1000d, 700d), (1920d, 1040d) })
                    cases.Add(state, language, w, h);
        return cases;
    }

    /// <summary>
    /// The picture's area on the unchanged baseline (fe38160), measured with the same fixtures and
    /// viewports; identical for every sizing state (artifacts/pf-opux-scrum11150/layout-before).
    /// </summary>
    private static readonly Dictionary<double, Rect> BaselineImageArea = new()
    {
        [1000] = new(24, 374.28, 572, 258.48),
        [1920] = new(24, 359.04, 1492, 613.72),
    };

    private static readonly string[] GuidanceIds =
    [
        "Session.PrintDimensions.PresetHelp",
        "Session.PrintDimensions.CustomHelp",
        "Session.PrintDimensions.TargetSizeHint",
        "Session.PrintDimensions.Summary",
        "Session.PrintDimensions.DraftEnlargementNote",
        "Session.PrintDimensions.EnlargementWarning",
        "Session.PrintDimensions.TechnicalDetails",
        "Session.Preparation.TechnicalDetails",
    ];

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task The_picture_keeps_its_area_and_the_guidance_wraps_reachably_in_the_details_column(
        string state, string language, double width, double height)
    {
        using OperatorCultureScope culture = new(language);
        using HomeScreenHarness h = new();
        SessionViewModel screen = await PrintSizeGuidanceSetup.OpenStateAsync(h, state == "details" ? "preset" : state);
        Size viewport = new(width, height);
        bool expand = state == "details";

        var facts = WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = screen }, viewport, tree =>
        {
            Settle(tree.Root, expand);
            Rect image = ReviewGuidanceLayoutTests.Bounds(tree.OfType<SharedReviewSurface>().Single(), tree.Root);
            ScrollViewer details = PrintSizeGuidanceUiTests.Descendants<ScrollViewer>(tree.Root)
                .Single(s => Grid.GetColumn(s) == 1 && s.Parent is Grid);
            FrameworkElement content = (FrameworkElement)details.Content;
            Rect column = ReviewGuidanceLayoutTests.Bounds(details, tree.Root);

            List<(string Id, Rect InContent, bool InColumn, bool InExpander)> shown = [];
            foreach (FrameworkElement element in PrintSizeGuidanceUiTests.Descendants<FrameworkElement>(tree.Root))
            {
                string id = AutomationProperties.GetAutomationId(element);
                if (!GuidanceIds.Contains(id) || !ReviewGuidanceLayoutTests.Shown(element)) continue;
                Rect inContent = element.TransformToAncestor(content)
                    .TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
                shown.Add((id, inContent, ReviewGuidanceLayoutTests.IsInside(element, details), InsideExpander(element)));
            }

            // Every shown line in the column wraps inside it and is laid out at its full height.
            // Scoped to the three print-size sections (size choice, preflight, preparation); the
            // desired height excludes the margin, which DesiredSize includes.
            Rect contentBounds = ReviewGuidanceLayoutTests.Bounds(content, tree.Root);
            List<Border> sections = [.. PrintSizeGuidanceUiTests.Descendants<Border>(content)
                .Where(b => ReferenceEquals(VisualTreeHelper.GetParent(b), content) && ReviewGuidanceLayoutTests.Shown(b) &&
                    PrintSizeGuidanceUiTests.Descendants<FrameworkElement>(b).Any(e => GuidanceIds.Contains(AutomationProperties.GetAutomationId(e))))];
            List<string> clipped = [.. sections.SelectMany(PrintSizeGuidanceUiTests.Descendants<TextBlock>)
                .Where(t => ReviewGuidanceLayoutTests.Shown(t) && t.ActualWidth > 0 &&
                    (ReviewGuidanceLayoutTests.Bounds(t, tree.Root).Right > contentBounds.Right + 0.5
                     || t.DesiredSize.Height - t.Margin.Top - t.Margin.Bottom > t.ActualHeight + 0.5))
                .Select(t => $"{t.Text[..Math.Min(40, t.Text.Length)]} ({t.ActualWidth:0.#}x{t.ActualHeight:0.#}, desired {t.DesiredSize.Height:0.#})")];
            int rows = PrintSizeGuidanceUiTests.Descendants<TextBlock>(content)
                .Count(t => ReviewGuidanceLayoutTests.Shown(t) &&
                    AutomationProperties.GetAutomationId(t).StartsWith("Session.PrintDimensions.", StringComparison.Ordinal) &&
                    screen.PreflightRows.Any(r => r.AutomationId == AutomationProperties.GetAutomationId(t)));
            return (Image: image, Column: column, Shown: shown, Clipped: clipped, Sections: sections.Count, Rows: rows,
                ExtentHeight: details.ExtentHeight, ViewportHeight: details.ViewportHeight);
        });

        Record(state, language, width, height, new
        {
            Image = Plain(facts.Facts.Image), Column = Plain(facts.Facts.Column), facts.Facts.ExtentHeight, facts.Facts.ViewportHeight,
            facts.Facts.Sections, facts.Facts.Rows, facts.Facts.Clipped,
            Shown = facts.Facts.Shown.Select(e => new { e.Id, InContent = Plain(e.InContent), e.InColumn, e.InExpander }),
        });

        Rect before = BaselineImageArea[width];
        foreach ((double actual, double expected) in new[] { (facts.Facts.Image.X, before.X), (facts.Facts.Image.Y, before.Y),
                     (facts.Facts.Image.Width, before.Width), (facts.Facts.Image.Height, before.Height) })
            actual.ShouldBe(expected, 0.01, $"the picture keeps its {width:0}×{height:0} area");

        facts.Facts.Clipped.ShouldBeEmpty("every line in the print-size sections wraps inside the column");
        facts.Facts.Sections.ShouldBeGreaterThan(0);
        foreach (var element in facts.Facts.Shown)
        {
            element.InColumn.ShouldBeTrue($"{element.Id} lives in the scrollable details column");
            element.InContent.Bottom.ShouldBeLessThanOrEqualTo(facts.Facts.ExtentHeight + 0.5, $"{element.Id} can be scrolled to");
            element.InContent.Width.ShouldBeLessThanOrEqualTo(facts.Facts.Column.Width + 0.5);
        }

        string[] ids = [.. facts.Facts.Shown.Select(e => e.Id)];
        // A recorded preset closes the size choice (existing CanChooseFlexibleSize), and its help with it.
        string[] required = state switch
        {
            "preset" or "details" => ["Session.PrintDimensions.Summary", "Session.PrintDimensions.TechnicalDetails",
                "Session.Preparation.TechnicalDetails"],
            "draft" => ["Session.PrintDimensions.PresetHelp", "Session.PrintDimensions.CustomHelp",
                "Session.PrintDimensions.Summary", "Session.PrintDimensions.TechnicalDetails"],
            "invalid" => ["Session.PrintDimensions.PresetHelp", "Session.PrintDimensions.CustomHelp",
                "Session.PrintDimensions.TargetSizeHint"],
            _ => ["Session.PrintDimensions.EnlargementWarning", "Session.Preparation.TechnicalDetails"],
        };
        ids.ShouldBeSubsetOf(GuidanceIds);
        required.ShouldBeSubsetOf(ids, $"the {state} state shows its guidance");
        if (state == "invalid")
            ids.ShouldNotContain("Session.PrintDimensions.Summary", "an invalid size shows no summary");
        if (state == "enlargement")
            facts.Facts.Shown.Single(e => e.Id == "Session.PrintDimensions.EnlargementWarning").InExpander
                .ShouldBeFalse("the warning stays in the main flow, not under the details");
        if (state == "details")
            facts.Facts.Rows.ShouldBe(screen.PreflightRows.Count, "every technical row is shown once the details are open");
        else
            facts.Facts.Rows.ShouldBe(0, "the technical rows start collapsed");

        Capture(screen, viewport, expand, $"size-{state}-{language}-{width:0}x{height:0}.png");
    }

    private static void Settle(FrameworkElement root, bool expand)
    {
        if (expand)
            foreach (Expander expander in PrintSizeGuidanceUiTests.Descendants<Expander>(root)) expander.IsExpanded = true;
        root.Dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
        root.UpdateLayout();
    }

    private static bool InsideExpander(DependencyObject element)
    {
        for (DependencyObject? node = VisualTreeHelper.GetParent(element); node is not null; node = VisualTreeHelper.GetParent(node))
            if (node is Expander) return true;
        return false;
    }

    private static object Plain(Rect r) => new { r.X, r.Y, r.Width, r.Height };

    private static void Record(string state, string language, double width, double height, object facts)
    {
        string? destination = Environment.GetEnvironmentVariable("PF_11150_LAYOUT_DIR");
        if (string.IsNullOrWhiteSpace(destination)) return;
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(destination, $"layout-{state}-{language}-{width:0}x{height:0}.json"),
            JsonSerializer.Serialize(new { state, language, width, height, facts }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void Capture(SessionViewModel screen, Size viewport, bool expand, string name)
    {
        string? destination = Environment.GetEnvironmentVariable("PF_11150_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(destination)) return;

        // Each state is captured scrolled to what it is about, as an operator scrolling the
        // details column would see it; the mode help is captured on its own as well.
        string state = name.Split('-')[1];
        string[] focus = state switch
        {
            "details" => ["Session.PrintDimensions.TechnicalDetails"],
            "invalid" => ["Session.PrintDimensions.TargetSizeHint", "Session.PrintDimensions.CustomHelp"],
            "enlargement" => ["Session.PrintDimensions.EnlargementWarning"],
            "draft" => ["Session.PrintDimensions.Summary", "Session.PrintDimensions.CustomHelp"],
            _ => ["Session.PrintDimensions.Summary"],
        };
        foreach (string id in focus)
        {
            string file = id == focus[0] ? name : name.Replace(".png", "-help.png", StringComparison.Ordinal);
            WpfRendering.CapturePng(() => new SessionScreenView { DataContext = screen }, viewport, Path.Combine(destination, file),
                tree =>
                {
                    Settle(tree.Root, expand);
                    PrintSizeGuidanceUiTests.Descendants<FrameworkElement>(tree.Root)
                        .FirstOrDefault(e => AutomationProperties.GetAutomationId(e) == id && ReviewGuidanceLayoutTests.Shown(e))
                        ?.BringIntoView();
                    tree.Root.UpdateLayout();
                });
        }
    }
}
