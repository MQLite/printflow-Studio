using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using PrintFlow.App.Views;
using PrintFlow.Tests.Fixtures;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class SidebarUtilityLayoutTests
{
    // Break caught: a utility caption is clipped inside the fixed narrow sidebar,
    // even though the button itself fits the viewport and its UIA name is complete.
    [Theory]
    [InlineData("en-US", 984, 661)]
    [InlineData("en-US", 1000, 700)]
    [InlineData("en-US", 1280, 720)]
    [InlineData("en-US", 1920, 1080)]
    [InlineData("zh-CN", 984, 661)]
    [InlineData("zh-CN", 1000, 700)]
    [InlineData("zh-CN", 1280, 720)]
    [InlineData("zh-CN", 1920, 1080)]
    public async Task Full_utility_captions_fit_without_covering_steps_or_losing_commands(string language, int width, int height)
    {
        using OperatorCultureScope culture = new(language);
        using HomeScreenHarness harness = new();
        var model = await GuidedSessionShellTests.Open(harness);
        Size viewport = new(width, height);
        var result = WpfRendering.RenderExpectingNoBindingErrors(
            () => new SessionScreenView { DataContext = model }, viewport, tree =>
            {
                var settings = GuidedSessionShellTests.Find<SessionActionButton>(tree, "Session.Settings");
                var environment = GuidedSessionShellTests.Find<SessionActionButton>(tree, "Session.Environment");
                settings.Command.ShouldBeSameAs(model.OpenSettingsCommand);
                environment.Command.ShouldBeSameAs(model.OpenEnvironmentCommand);
                var steps = GuidedSessionShellTests.Find<ItemsControl>(tree, "Session.StepList");
                var stepScroll = GuidedSessionShellTests.Ancestors(steps).OfType<ScrollViewer>().First();
                var rows = new[] { settings, environment }.Select(button =>
                {
                    string label = (string)button.Content;
                    var text = tree.OfType<TextBlock>().Single(t => t.Text == label && button.IsAncestorOf(t));
                    var formatted = new FormattedText(label, CultureInfo.GetCultureInfo(language), text.FlowDirection,
                        new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch),
                        text.FontSize, text.Foreground, VisualTreeHelper.GetDpi(text).PixelsPerDip);
                    double unwrappedWidth = formatted.WidthIncludingTrailingWhitespace;
                    if (text.TextWrapping != TextWrapping.NoWrap) formatted.MaxTextWidth = text.ActualWidth;
                    var peer = UIElementAutomationPeer.CreatePeerForElement(button)!;
                    return new
                    {
                        Id = AutomationProperties.GetAutomationId(button), Label = label,
                        Button = GuidedSessionShellTests.Bounds(button, tree.Root),
                        Text = GuidedSessionShellTests.Bounds(text, tree.Root),
                        text.FontSize, UnwrappedWidth = unwrappedWidth, RequiredHeight = formatted.Height,
                        Wraps = text.TextWrapping != TextWrapping.NoWrap,
                        Name = peer.GetName(), button.IsEnabled, button.Focusable, button.IsTabStop,
                    };
                }).ToArray();
                return new { Rows = rows, StepScroll = GuidedSessionShellTests.Bounds(stepScroll, tree.Root) };
            });

        if (Environment.GetEnvironmentVariable("PF_U09_SIDEBAR_CAPTURE_DIR") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            string stem = Path.Combine(directory, $"sidebar-{language}-{width}x{height}");
            WpfRendering.CapturePng(() => new SessionScreenView { DataContext = model }, viewport, stem + ".png");
            File.WriteAllText(stem + ".json", JsonSerializer.Serialize(result.Facts));
        }

        foreach (var row in result.Facts.Rows)
        {
            row.Name.ShouldBe(row.Label);
            row.IsEnabled.ShouldBeTrue(); row.Focusable.ShouldBeTrue(); row.IsTabStop.ShouldBeTrue();
            row.FontSize.ShouldBeGreaterThanOrEqualTo(16);
            row.Button.Height.ShouldBeGreaterThanOrEqualTo(42);
            row.Button.Left.ShouldBeGreaterThanOrEqualTo(0);
            row.Button.Right.ShouldBeLessThanOrEqualTo(width);
            row.Button.Bottom.ShouldBeLessThanOrEqualTo(height);
            row.Text.Left.ShouldBeGreaterThanOrEqualTo(row.Button.Left);
            row.Text.Right.ShouldBeLessThanOrEqualTo(row.Button.Right);
            row.Text.Bottom.ShouldBeLessThanOrEqualTo(row.Button.Bottom);
            if (!row.Wraps)
                row.UnwrappedWidth.ShouldBeLessThanOrEqualTo(row.Text.Width + .5, row.Label + " must be fully readable");
            row.RequiredHeight.ShouldBeLessThanOrEqualTo(row.Text.Height + .5, row.Label + " must show every wrapped line");
        }
        result.Facts.StepScroll.Height.ShouldBeGreaterThan(100);
        result.Facts.StepScroll.Bottom.ShouldBeLessThanOrEqualTo(result.Facts.Rows[0].Button.Top);
        result.Facts.Rows[0].Button.Bottom.ShouldBeLessThanOrEqualTo(result.Facts.Rows[1].Button.Top);
    }
}
