using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
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
}
