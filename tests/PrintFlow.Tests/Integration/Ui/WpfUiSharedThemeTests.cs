using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using PrintFlow.App.Views;
using PrintFlow.Tests.Fixtures;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class WpfUiSharedThemeTests
{
    private static ResourceDictionary Resources() => new()
    {
        Source = new Uri("/PrintFlow.App;component/Themes/WpfUiTheme.xaml", UriKind.Relative),
    };

    private static UserControl Host(FrameworkElement content)
    {
        UserControl host = new() { Content = content };
        host.Resources.MergedDictionaries.Add(Resources());
        host.SetResourceReference(Control.FontFamilyProperty, "PF.Font.Family");
        host.SetResourceReference(Control.FontSizeProperty, "PF.Font.Body");
        host.SetResourceReference(Control.ForegroundProperty, "PF.Brush.Ink");
        return host;
    }

    [Fact]
    public void Shared_contract_loads_on_independent_STA_without_Application() => WpfRendering.OnStaThread(() =>
    {
        Application.Current.ShouldBeNull();
        var resources = Resources();
        ((double)resources["PF.Font.Body"]).ShouldBe(16);
        ((double)resources["PF.Font.Title"]).ShouldBeInRange(24, 30);
        ((double)resources["PF.Control.MinHeight"]).ShouldBeGreaterThanOrEqualTo(42);
        resources["ControlCornerRadius"].ShouldBe(resources["PF.Corner.Control"]);
        resources["SystemAccentColorPrimary"].ShouldBe(resources["PF.Color.Primary"]);
        foreach ((string key, Type type) in new (string, Type)[] {
            ("Button.Primary", typeof(Button)), ("Button.Secondary", typeof(Button)),
            ("Button.Danger", typeof(Button)), ("Input.TextBox", typeof(TextBox)),
            ("Input.ComboBox", typeof(ComboBox)), ("Input.CheckBox", typeof(CheckBox)),
            ("Input.RadioButton", typeof(RadioButton)), ("Disclosure", typeof(Expander)),
            ("Progress", typeof(ProgressBar)), ("PageHeader", typeof(HeaderedContentControl)),
            ("Card", typeof(Border)), ("Dialog", typeof(HeaderedContentControl)) })
            ((Style)resources["PF." + key]).TargetType.ShouldBe(type);
        resources["PF.Focus"].ShouldBeOfType<Style>();
        resources["PF.Icon"].ShouldBeOfType<Style>();
    });

    [Fact]
    public void Opt_in_library_scope_does_not_change_legacy_or_sibling_controls() => WpfRendering.OnStaThread(() =>
    {
        Application.Current.ShouldBeNull();
        Stopwatch timer = Stopwatch.StartNew();
        var legacy = GuidedThemeFixture.Host(new Button { Content = "未迁移页面" });
        double legacyMs = timer.Elapsed.TotalMilliseconds;
        ((double)legacy.FindResource("PF.Font.Body")).ShouldBe(14);
        legacy.TryFindResource("DefaultButtonStyle").ShouldBeNull();
        timer.Restart();
        var migrated = Host(new Button { Content = "显式接入页面" });
        double sharedMs = timer.Elapsed.TotalMilliseconds;
        ((double)migrated.FindResource("PF.Font.Body")).ShouldBe(16);
        migrated.FindResource("DefaultButtonStyle").ShouldBeOfType<Style>();
        legacy.TryFindResource("DefaultButtonStyle").ShouldBeNull();
        ((double)legacy.FindResource("PF.Font.Body")).ShouldBe(14);
        string? facts = Environment.GetEnvironmentVariable("PF_SHARED_THEME_CAPTURE_DIR");
        if (!string.IsNullOrWhiteSpace(facts))
        {
            Directory.CreateDirectory(facts);
            File.WriteAllText(Path.Combine(facts, "scope-load-observation.json"), JsonSerializer.Serialize(new {
                LegacyLoadMilliseconds = legacyMs, SharedLoadMilliseconds = sharedMs,
                ApplicationCreated = Application.Current is not null,
                Scope = "One STA test observation; includes assembly/resource cache effects; not a startup benchmark" }));
        }
    });

    [Theory]
    [InlineData(1000, 700)]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    public void Shared_components_keep_controls_notices_and_cancel_reachable(int width, int height)
    {
        WpfRendering.RenderExpectingNoBindingErrors(Gallery, new(width, height), tree =>
        {
            Application.Current.ShouldBeNull();
            foreach (Control control in tree.OfType<Control>().Where(c => Equals(c.Tag, "Shared.Contract") && c is Button or TextBox or ComboBox or CheckBox or RadioButton))
            {
                control.MinHeight.ShouldBeGreaterThanOrEqualTo(42);
                control.FontSize.ShouldBe(16);
                control.FocusVisualStyle.ShouldNotBeNull();
                Rect box = control.TransformToAncestor(tree.Root).TransformBounds(new Rect(control.RenderSize));
                box.Bottom.ShouldBeLessThanOrEqualTo(height + .5);
            }
            tree.OfType<ProgressBar>().Single().IsIndeterminate.ShouldBeTrue();
            var disabled = tree.OfType<Button>().Single(b => Equals(b.Content, "先完成检查"));
            disabled.IsEnabled.ShouldBeFalse();
            tree.OfType<CheckBox>().Single().IsChecked.ShouldBe(true);
            tree.OfType<TextBlock>().ShouldContain(t => t.Text == "结果尚未保存，请先核实本次保存。");
            var cancel = tree.OfType<Button>().Single(b => Equals(b.Content, "取消"));
            cancel.IsEnabled.ShouldBeTrue();
            tree.OfType<Button>().All(b => !b.IsDefault).ShouldBeTrue();
            return 0;
        });
        string? capture = Environment.GetEnvironmentVariable("PF_SHARED_THEME_CAPTURE_DIR");
        if (!string.IsNullOrWhiteSpace(capture))
            WpfRendering.CapturePng(Gallery, new(width, height), Path.Combine(capture, $"components-{width}x{height}.png"));
    }

    [Fact]
    public void Fluent_approval_style_preserves_command_identity_and_fresh_gestures()
    {
        int calls = 0;
        object parameter = new();
        RelayCommand<object> command = new(p => { p.ShouldBeSameAs(parameter); calls++; });
        WpfRendering.RenderExpectingNoBindingErrors(() =>
        {
            ReviewApprovalButton button = Styled(new ReviewApprovalButton {
                Content = "确认当前图片", TargetIdentity = "result-1", IsGestureGuarded = true,
                Command = command, CommandParameter = parameter }, "PF.Button.Primary");
            AutomationProperties.SetAutomationId(button, "Existing.Approve");
            AutomationProperties.SetName(button, "确认当前图片");
            return Host(button);
        }, new Size(400, 140), tree =>
        {
            var button = tree.OfType<Button>().Single().ShouldBeOfType<ReviewApprovalButton>();
            button.Command.ShouldBeSameAs(command); button.CommandParameter.ShouldBeSameAs(parameter);
            button.TargetIdentity.ShouldBe("result-1"); button.IsGestureGuarded.ShouldBeTrue();
            AutomationProperties.GetAutomationId(button).ShouldBe("Existing.Approve");
            AutomationProperties.GetName(button).ShouldBe("确认当前图片");
            using HwndSource source = new(new HwndSourceParameters("Shared theme isolated input") { WindowStyle = 0 });
            foreach (Key key in new[] { Key.Enter, Key.Space })
            {
                SendKey(button, source, key, true); button.TargetIdentity = Guid.NewGuid().ToString();
                SendKey(button, source, key, true, true); SendKey(button, source, key, false);
            }
            button.RaiseEvent(MouseEvent(UIElement.MouseLeftButtonDownEvent, 1));
            button.TargetIdentity = "result-3";
            button.RaiseEvent(MouseEvent(UIElement.MouseLeftButtonUpEvent, 1));
            button.RaiseEvent(MouseEvent(UIElement.MouseLeftButtonDownEvent, 2));
            button.RaiseEvent(MouseEvent(UIElement.MouseLeftButtonUpEvent, 2));
            calls.ShouldBe(0);
            SendKey(button, source, Key.Enter, true); SendKey(button, source, Key.Enter, false);
            calls.ShouldBe(1);
            return 0;
        });
    }

    private static UserControl Gallery()
    {
        StackPanel panel = new() { Margin = new Thickness(24) };
        panel.Children.Add(Styled(new HeaderedContentControl { Header = "共享组件 · 检查当前图片",
            Content = new TextBlock { Text = "合成组件示例，不代表实际任务已通过审核。", TextWrapping = TextWrapping.Wrap } }, "PF.PageHeader"));
        WrapPanel fields = new() { Margin = new Thickness(0, 12, 0, 12) };
        fields.Children.Add(Styled(new TextBox { Text = "客户设计素材.png", Width = 240 }, "PF.Input.TextBox"));
        fields.Children.Add(Styled(new ComboBox { ItemsSource = new[] { "当前结果", "历史结果" }, SelectedIndex = 0, Width = 200 }, "PF.Input.ComboBox"));
        fields.Children.Add(Styled(new CheckBox { Content = "已核对图片", IsChecked = true }, "PF.Input.CheckBox"));
        fields.Children.Add(Styled(new RadioButton { Content = "明确选择此项", IsChecked = true }, "PF.Input.RadioButton"));
        panel.Children.Add(fields);
        panel.Children.Add(Styled(new HeaderedContentControl { Header = "保存需要核实",
            Content = new TextBlock { Text = "结果尚未保存，请先核实本次保存。", TextWrapping = TextWrapping.Wrap } }, "PF.Notice.Warning"));
        panel.Children.Add(Styled(new ProgressBar { IsIndeterminate = true, Margin = new Thickness(0, 12, 0, 12) }, "PF.Progress"));
        WrapPanel actions = new();
        actions.Children.Add(Styled(new SessionActionButton { Content = "停止当前处理" }, "PF.Button.Secondary"));
        actions.Children.Add(Styled(new ReviewApprovalButton { Content = "先完成检查", IsEnabled = false }, "PF.Button.Primary"));
        actions.Children.Add(Styled(new Button { Content = "取消" }, "PF.Button.Secondary"));
        panel.Children.Add(Styled(new HeaderedContentControl { Header = "确认当前操作", Content = actions }, "PF.Dialog"));
        return Host(panel);
    }

    private static T Styled<T>(T control, string key) where T : FrameworkElement
    { control.Tag = "Shared.Contract"; control.SetResourceReference(FrameworkElement.StyleProperty, key); return control; }
    private static MouseButtonEventArgs MouseEvent(RoutedEvent routed, int clicks)
    {
        MouseButtonEventArgs input = new(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = routed };
        typeof(MouseButtonEventArgs).GetProperty(nameof(MouseButtonEventArgs.ClickCount))!.SetValue(input, clicks);
        return input;
    }
    private static void SendKey(Button button, PresentationSource source, Key key, bool down, bool repeat = false)
    {
        KeyEventArgs input = new(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = down ? Keyboard.KeyDownEvent : Keyboard.KeyUpEvent };
        if (repeat) typeof(KeyEventArgs).GetMethod("SetRepeat", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(input, [true]);
        button.RaiseEvent(input);
    }
}
