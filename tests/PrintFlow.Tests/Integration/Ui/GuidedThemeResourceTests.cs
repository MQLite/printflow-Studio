using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using PrintFlow.Tests.Fixtures;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class GuidedThemeResourceTests
{
    [Fact]
    public void Dictionary_loads_without_production_application() => WpfRendering.OnStaThread(() =>
    {
        Application.Current.ShouldBeNull();
        ResourceDictionary resources = GuidedThemeFixture.CreateResources();
        foreach (string key in new[] { "Primary", "Secondary", "Danger" })
            ((Style)resources[$"PF.Button.{key}"]).TargetType.ShouldBe(typeof(Button));
        foreach ((string key, Type type) in new (string, Type)[] {
            ("Input.TextBox", typeof(TextBox)), ("Input.ComboBox", typeof(ComboBox)),
            ("Input.CheckBox", typeof(CheckBox)), ("Input.RadioButton", typeof(RadioButton)),
            ("Disclosure", typeof(Expander)), ("Progress", typeof(ProgressBar)) })
            ((Style)resources[$"PF.{key}"]).TargetType.ShouldBe(type);
    });

    [Fact]
    public void Tokens_resolve_to_approved_values() => WpfRendering.OnStaThread(() =>
    {
        ResourceDictionary resources = GuidedThemeFixture.CreateResources();
        foreach ((string name, string hex) in new[] {
            ("Canvas", "#F6F7F4"), ("Surface", "#FFFFFF"), ("Ink", "#202E2A"),
            ("Muted", "#65706B"), ("Primary", "#16765D"), ("Selection", "#E8F4EF"),
            ("Border", "#DCE3DE"), ("Warning", "#FFF1C8") })
        {
            Color expected = (Color)ColorConverter.ConvertFromString(hex);
            ((Color)resources[$"PF.Color.{name}"]).ShouldBe(expected);
            ((SolidColorBrush)resources[$"PF.Brush.{name}"]).Color.ShouldBe(expected);
        }
        ((FontFamily)resources["PF.Font.Family"]).Source.ShouldBe("Microsoft YaHei, Noto Sans SC");
        ((double)resources["PF.Font.Title"]).ShouldBe(28);
        ((double)resources["PF.Font.Body"]).ShouldBe(14);
        ((double)resources["PF.Font.Auxiliary"]).ShouldBe(12);
        ((double)resources["PF.Control.MinHeight"]).ShouldBe(42);
        foreach (int space in new[] { 4, 8, 12, 16, 24, 32 })
            ((double)resources[$"PF.Space.{space}"]).ShouldBe(space);
        ((Thickness)resources["PF.Padding.Card"]).ShouldBe(new Thickness(24));
        ((Thickness)resources["PF.Padding.Field"]).ShouldBe(new Thickness(12, 8, 12, 8));
        ((Thickness)resources["PF.Margin.Section"]).ShouldBe(new Thickness(0, 0, 0, 16));
        ((CornerRadius)resources["PF.Corner.Card"]).ShouldBe(new CornerRadius(10));
        resources["PF.Image.Checkerboard"].ShouldBeOfType<DrawingBrush>();
    });

    [Fact]
    public void Focused_selected_and_disabled_controls_remain_distinguishable()
    {
        int executions = 0;
        var rendered = WpfRendering.RenderExpectingNoBindingErrors(() =>
        {
            Button button = new() { Content = "确认当前图片并继续下一步", Command = new RelayCommand(() => executions++, () => false) };
            button.SetResourceReference(FrameworkElement.StyleProperty, "PF.Button.Primary");
            StackPanel panel = new();
            panel.Children.Add(button);
            panel.Children.Add(new TextBlock { Text = "请先完成必要检查", TextWrapping = TextWrapping.Wrap });
            CheckBox selection = new() { Content = "已经明确选择", IsChecked = true };
            selection.SetResourceReference(FrameworkElement.StyleProperty, "PF.Input.CheckBox");
            panel.Children.Add(selection);
            Control focusPreview = new() { Height = 20, Margin = new Thickness(8) };
            focusPreview.SetResourceReference(FrameworkElement.StyleProperty, "PF.Focus");
            panel.Children.Add(focusPreview);
            return GuidedThemeFixture.Host(panel);
        }, new Size(420, 220), tree =>
        {
            Button button = tree.OfType<Button>().Single();
            button.MinHeight.ShouldBeGreaterThanOrEqualTo(42);
            button.FocusVisualStyle.ShouldNotBeNull();
            button.IsEnabled.ShouldBeFalse();
            var peer = new ButtonAutomationPeer(button);
            Should.Throw<System.Windows.Automation.ElementNotEnabledException>(() => ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)!).Invoke());
            tree.OfType<ContentPresenter>().Any(p => Equals(p.Content, button.Content)).ShouldBeTrue();
            tree.OfType<TextBlock>().Any(t => t.Text == "请先完成必要检查").ShouldBeTrue();
            CheckBox selection = tree.OfType<CheckBox>().Single();
            selection.FocusVisualStyle.ShouldNotBeNull();
            ((SolidColorBrush)selection.Background).Color.ShouldBe(Color.FromRgb(0xE8, 0xF4, 0xEF));
            tree.OfType<System.Windows.Shapes.Rectangle>().Any(r => r.Stroke is SolidColorBrush brush &&
                brush.Color == Color.FromRgb(0x24, 0x59, 0xAA) && r.StrokeThickness >= 2).ShouldBeTrue();
            return button.ActualHeight;
        });
        rendered.Facts.ShouldBeGreaterThanOrEqualTo(42);
        executions.ShouldBe(0);
    }

    [Fact]
    public void Long_button_labels_wrap_instead_of_losing_the_action()
    {
        var rendered = WpfRendering.RenderExpectingNoBindingErrors(() =>
        {
            Button button = new() { Content = "确认当前这张客户图片并继续完成下一步的必要检查" };
            button.SetResourceReference(FrameworkElement.StyleProperty, "PF.Button.Primary");
            return GuidedThemeFixture.Host(button);
        }, new Size(200, 150), tree =>
        {
            AccessText label = tree.OfType<AccessText>().Single();
            label.TextWrapping.ShouldBe(TextWrapping.Wrap);
            Rect bounds = label.TransformToAncestor(tree.Root).TransformBounds(new Rect(label.RenderSize));
            bounds.Right.ShouldBeLessThanOrEqualTo(200);
            label.ActualHeight.ShouldBeGreaterThan(28);
            return 0;
        });
        rendered.BindingErrors.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Selected_output_text_remains_readable_in_active_and_inactive_selection(bool active)
    {
        const string text = "客户输出名称 selected output name";
        Size viewport = new(500, 100);
        Func<UserControl> create = () =>
        {
            TextBox input = new() { Text = text, IsInactiveSelectionHighlightEnabled = true };
            input.SetResourceReference(FrameworkElement.StyleProperty, "PF.Input.TextBox");
            return GuidedThemeFixture.Host(new Border { Padding = new Thickness(20), Child = input });
        };
        var rendered = WpfRendering.RenderExpectingNoBindingErrors(create, viewport, tree =>
        {
            Application.Current.ShouldBeNull();
            TextBox input = tree.OfType<TextBox>().Single();
            PrepareSelection(input, active);
            tree.Root.UpdateLayout();
            input.IsSelectionActive.ShouldBe(active);
            input.SelectedText.ShouldBe(text);
            Rect first = input.GetRectFromCharacterIndex(0);
            Rect last = input.GetRectFromCharacterIndex(text.Length - 1, trailingEdge: true);
            Rect selected = input.TransformToAncestor(tree.Root).TransformBounds(
                new Rect(first.TopLeft, new Point(last.Right, first.Bottom)));
            selected.Width.ShouldBeGreaterThan(100);
            RenderTargetBitmap bitmap = new(500, 100, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(tree.Root);
            byte[] pixels = new byte[500 * 100 * 4];
            bitmap.CopyPixels(pixels, 500 * 4, 0);
            int inkPixels = 0;
            for (int y = (int)Math.Ceiling(selected.Top); y < Math.Floor(selected.Bottom); y++)
                for (int x = (int)Math.Ceiling(selected.Left); x < Math.Floor(selected.Right); x++)
                {
                    int i = ((y * 500) + x) * 4;
                    if (Math.Abs(pixels[i] - 0x2A) <= 20 && Math.Abs(pixels[i + 1] - 0x2E) <= 20 &&
                        Math.Abs(pixels[i + 2] - 0x20) <= 20 && pixels[i + 3] == 255) inkPixels++;
                }
            inkPixels.ShouldBeGreaterThan(100, "selected glyphs must remain dark and readable on the pale selection");
            input.SelectionTextBrush.ShouldBeOfType<SolidColorBrush>().Color.ShouldBe(Color.FromRgb(0x20, 0x2E, 0x2A));
            input.SelectionTextBrush.ShouldBeSameAs(tree.Root.FindResource("PF.Brush.Ink"));
            return new { Active = active, SelectedCharacters = input.SelectionLength, InkPixels = inkPixels,
                SelectionText = input.SelectionTextBrush.ToString(), SelectionSurface = input.SelectionBrush.ToString() };
        });

        string? directory = Environment.GetEnvironmentVariable("PF_11177_CAPTURE_DIR");
        if (!string.IsNullOrWhiteSpace(directory))
        {
            string stem = active ? "selection-active-native-default-after" : "selection-inactive-native-default-after";
            string path = Path.Combine(directory, stem + ".png");
            WpfRendering.CapturePng(create, viewport, path, tree =>
                PrepareSelection(tree.OfType<TextBox>().Single(), active));
            File.WriteAllText(Path.Combine(directory, stem + ".json"), JsonSerializer.Serialize(new
            {
                State = stem, WidthDip = 500, HeightDip = 100, Dpi = 96,
                ScreenshotSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
                Method = "offscreen WPF; native runtime defaults unchanged; synthetic selection/focus, no physical keyboard or Window.Show",
                rendered.Facts,
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private static void PrepareSelection(TextBox input, bool active)
    {
        // Set native read-only focus/selection flags only in this offscreen fixture; no desktop focus or input.
        input.SetValue((DependencyPropertyKey)typeof(UIElement)
            .GetField("IsKeyboardFocusedPropertyKey", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!, active);
        input.SelectAll();
        input.SetValue((DependencyPropertyKey)typeof(TextBoxBase)
            .GetField("IsSelectionActivePropertyKey", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!, active);
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
        object selection = typeof(TextBoxBase).GetProperty("TextSelectionInternal", flags)!.GetValue(input)!;
        selection.GetType().GetMethod("Highlight", flags)!.Invoke(selection, []);
        input.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.SystemIdle);
        input.UpdateLayout();
    }
}
