using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using PrintFlow.App.Views;
using PrintFlow.Tests.Fixtures;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class GuidedComponentTests
{
    [Theory]
    [InlineData("PF.PageHeader")]
    [InlineData("PF.TaskCard")]
    [InlineData("PF.FormSection")]
    public void Components_preserve_content_and_accessible_names(string style)
    {
        object caller = new { Caption = "原有操作的可访问名称" };
        int count = 0;
        RelayCommand command = new(() => count++);
        WpfRendering.RenderExpectingNoBindingErrors(() =>
        {
            Button child = new() { Content = "继续检查", Command = command };
            AutomationProperties.SetAutomationId(child, "Existing.Action");
            child.SetBinding(AutomationProperties.NameProperty, new Binding("Caption"));
            StackPanel content = new();
            content.Children.Add(new TextBlock { Text = "组件说明保留且允许换行", TextWrapping = TextWrapping.Wrap });
            content.Children.Add(child);
            HeaderedContentControl component = new() { Header = "组件标题", Content = content, DataContext = caller };
            component.SetResourceReference(FrameworkElement.StyleProperty, style);
            return GuidedThemeFixture.Host(component);
        }, new Size(600, 300), tree =>
        {
            HeaderedContentControl component = tree.OfType<HeaderedContentControl>().Single();
            component.Style.ShouldNotBeNull("the shared presentation component must actually be applied");
            component.IsTabStop.ShouldBeFalse("presentation headings must not add an action to the tab sequence");
            tree.OfType<TextBlock>().ShouldContain(t => t.Text == "组件标题" && t.ActualHeight > 0);
            tree.OfType<TextBlock>().ShouldContain(t => t.Text == "组件说明保留且允许换行");
            Button child = tree.OfType<Button>().Single();
            child.Command.ShouldBeSameAs(command);
            child.DataContext.ShouldBeSameAs(caller);
            AutomationProperties.GetAutomationId(child).ShouldBe("Existing.Action");
            AutomationProperties.GetName(child).ShouldBe("原有操作的可访问名称");
            child.IsDefault.ShouldBeFalse();
            return 0;
        });
        count.ShouldBe(0);
    }

    [Theory]
    [InlineData("Warning", "请先完成检查", "缺少必要条件，已有输入会保留。")]
    [InlineData("Error", "处理未完成", "请检查失败原因，再选择可用操作。")]
    public void Warning_and_error_have_persistent_text_beyond_colour(string kind, string heading, string explanation)
    {
        WpfRendering.RenderExpectingNoBindingErrors(() =>
        {
            HeaderedContentControl notice = new() { Header = heading, Content = explanation };
            notice.SetResourceReference(FrameworkElement.StyleProperty, "PF.Notice." + kind);
            return GuidedThemeFixture.Host(notice);
        }, new Size(500, 220), tree =>
        {
            tree.Root.UpdateLayout();
            tree.OfType<TextBlock>().ShouldContain(t => t.Text == heading && t.Visibility == Visibility.Visible && t.ActualHeight > 0);
            tree.OfType<TextBlock>().ShouldContain(t => t.Text == explanation && t.TextWrapping == TextWrapping.Wrap);
            tree.OfType<System.Windows.Shapes.Path>().ShouldContain(p => p.Data != null && p.ActualWidth > 0);
            return 0;
        });
    }

    [Fact]
    public void Loading_is_indeterminate_and_does_not_hide_legal_stop()
    {
        WpfRendering.RenderExpectingNoBindingErrors(() => GuidedComponentCatalog.Create("loading"), new Size(1000, 700), tree =>
        {
            tree.OfType<ProgressBar>().ShouldHaveSingleItem().IsIndeterminate.ShouldBeTrue();
            Button stop = tree.OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "GuidedCatalog.Stop");
            stop.IsEnabled.ShouldBeTrue();
            stop.Visibility.ShouldBe(Visibility.Visible);
            stop.IsTabStop.ShouldBeTrue();
            stop.IsDefault.ShouldBeFalse();
            GuidedCatalogActions actions = (GuidedCatalogActions)tree.Root.Tag;
            actions.ContinueCount.ShouldBe(0);
            actions.StopCount.ShouldBe(0);
            ((IInvokeProvider)new ButtonAutomationPeer(stop).GetPattern(PatternInterface.Invoke)!).Invoke();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.SystemIdle);
            actions.StopCount.ShouldBe(1);
            return 0;
        });
    }

    [Fact]
    public void Styled_review_approval_preserves_target_and_gesture_guard()
    {
        int approvals = 0;
        WpfRendering.RenderExpectingNoBindingErrors(() =>
        {
            ReviewApprovalButton button = new()
            {
                Content = "通过当前结果", TargetIdentity = "exact-result-1", IsGestureGuarded = true,
                Command = new RelayCommand(() => approvals++),
            };
            button.SetResourceReference(FrameworkElement.StyleProperty, "PF.Button.Primary");
            return GuidedThemeFixture.Host(button);
        }, new Size(340, 120), tree =>
        {
            ReviewApprovalButton button = tree.OfType<Button>().Single().ShouldBeOfType<ReviewApprovalButton>();
            button.TargetIdentity.ShouldBe("exact-result-1");
            button.IsGestureGuarded.ShouldBeTrue();
            button.IsDefault.ShouldBeFalse();
            using HwndSource source = new(new HwndSourceParameters("Guided theme isolated input") { WindowStyle = 0 });
            foreach (Key key in new[] { Key.Enter, Key.Space })
            {
                SendKey(button, source, key, true);
                button.TargetIdentity = Guid.NewGuid().ToString();
                SendKey(button, source, key, true, repeat: true);
                SendKey(button, source, key, false);
            }
            approvals.ShouldBe(0, "old key releases must not approve a changed target");
            button.RaiseEvent(MouseEvent(UIElement.MouseLeftButtonDownEvent, 1));
            button.TargetIdentity = "exact-result-3";
            MouseButtonEventArgs staleRelease = MouseEvent(UIElement.MouseLeftButtonUpEvent, 1);
            button.RaiseEvent(staleRelease);
            staleRelease.Handled.ShouldBeTrue();
            button.RaiseEvent(MouseEvent(UIElement.MouseLeftButtonDownEvent, 2));
            button.RaiseEvent(MouseEvent(UIElement.MouseLeftButtonUpEvent, 2));
            approvals.ShouldBe(0, "double-click remnants cannot approve a new result");
            SendKey(button, source, Key.Enter, true);
            SendKey(button, source, Key.Enter, false);
            approvals.ShouldBe(1, "a fresh explicit gesture still reaches the existing command");
            return 0;
        });
    }

    private static MouseButtonEventArgs MouseEvent(RoutedEvent routed, int clicks)
    {
        MouseButtonEventArgs input = new(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = routed };
        typeof(MouseButtonEventArgs).GetProperty(nameof(MouseButtonEventArgs.ClickCount))!.SetValue(input, clicks);
        return input;
    }

    private static void SendKey(Button button, PresentationSource source, Key key, bool down, bool repeat = false)
    {
        KeyEventArgs input = new(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
        { RoutedEvent = down ? Keyboard.KeyDownEvent : Keyboard.KeyUpEvent };
        if (repeat)
            typeof(KeyEventArgs).GetMethod("SetRepeat", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(input, [true]);
        button.RaiseEvent(input);
    }
}
