using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using PrintFlow.App.Controls;
using PrintFlow.Tests.Fixtures;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class GuidedStepNavigationTests
{
    [Fact]
    public void All_five_states_show_text_and_distinct_current_marker()
    {
        GuidedStepItem[] items =
        [
            new("import", "导入图片", "已保留原图", "已完成", GuidedStepState.Completed),
            new("check", "检查原图", "请核对当前内容", "当前步骤", GuidedStepState.Current),
            new("process", "处理图片", "等待检查完成", "待进行", GuidedStepState.Pending),
            new("save", "核实保存", "需要确认保存结果", "需要注意", GuidedStepState.Attention),
            new("optional", "可选增强", "已选择保留当前图片", "已跳过", GuidedStepState.Skipped),
        ];
        WpfRendering.RenderExpectingNoBindingErrors(() => Host(items), new Size(260, 900), tree =>
        {
            tree.OfType<TextBlock>().Where(t => t.Name == "StepTitle").Select(t => t.Text)
                .ShouldBe(new[] { "导入图片", "检查原图", "处理图片", "核实保存", "可选增强" });
            foreach (GuidedStepItem item in items)
            {
                tree.OfType<TextBlock>().ShouldContain(t => t.Text == item.Detail && t.ActualHeight > 0);
                tree.OfType<TextBlock>().ShouldContain(t => t.Text == item.StatusText && t.ActualHeight > 0);
            }
            TextBlock current = tree.OfType<TextBlock>().Single(t => t.Name == "StepMarker" && ReferenceEquals(t.DataContext, items[1]));
            current.Text.ShouldBe("●");
            tree.OfType<TextBlock>().Count(t => t.Name == "StepMarker" && t.Text == "●").ShouldBe(1);
            return 0;
        });
    }

    [Fact]
    public void Future_steps_are_not_tab_stops_or_invokable()
    {
        WpfRendering.RenderExpectingNoBindingErrors(() => Host(
            [new("save", "保存结果", "等待前面步骤完成", "待进行", GuidedStepState.Pending)]), new Size(240, 700), tree =>
        {
            tree.OfType<TextBlock>().ShouldContain(t => t.Text == "待进行");
            tree.OfType<Button>().ShouldBeEmpty();
            tree.OfType<Control>().ShouldNotContain(c => c.IsTabStop && c.Focusable);
            tree.OfType<UIElement>().ShouldNotContain(e => e.Focusable);
            foreach (UIElement element in tree.OfType<UIElement>())
            {
                AutomationPeer? peer = UIElementAutomationPeer.CreatePeerForElement(element);
                peer?.GetPattern(PatternInterface.Invoke).ShouldBeNull();
                peer?.GetPattern(PatternInterface.Selection).ShouldBeNull();
                peer?.GetPattern(PatternInterface.SelectionItem).ShouldBeNull();
            }
            return 0;
        });
    }

    [Fact]
    public void Replacing_items_does_not_execute_or_change_business_state()
    {
        int executions = 0;
        string businessState = "awaiting-original-review";
        RelayCommand start = new(() => { executions++; businessState = "running"; });
        WpfRendering.RenderExpectingNoBindingErrors(() =>
        {
            UserControl host = Host([new("check", "检查原图", "请核对内容", "当前步骤", GuidedStepState.Current)]);
            host.DataContext = new { StartCommand = start };
            return host;
        }, new Size(260, 500), tree =>
        {
            ItemsControl steps = tree.OfType<ItemsControl>().Single();
            steps.Style.ShouldNotBeNull();
            steps.ItemsSource = new[] { new GuidedStepItem("check", "Check the image", "Review the current input", "Current", GuidedStepState.Current) };
            tree.Root.UpdateLayout();
            VisualDescendants(steps).OfType<TextBlock>().ShouldContain(t => t.Text == "Current");
            VisualDescendants(steps).OfType<TextBlock>().ShouldNotContain(t => t.Text == "当前步骤");
            steps.ItemsSource = new[] { new GuidedStepItem("check", "检查原图", "已检查", "已完成", GuidedStepState.Completed) };
            tree.Root.UpdateLayout();
            VisualDescendants(steps).OfType<TextBlock>().ShouldContain(t => t.Text == "已完成");
            return 0;
        });
        executions.ShouldBe(0);
        businessState.ShouldBe("awaiting-original-review");
        typeof(GuidedStepItem).GetProperties().ShouldNotContain(p => typeof(ICommand).IsAssignableFrom(p.PropertyType));
    }

    [Fact]
    public void Long_step_text_wraps_without_overlapping_neighbours()
    {
        GuidedStepItem[] items =
        [
            new("first", "检查客户提供的中文文件名和当前图片内容是否一致", "说明可以自然换行，请完整阅读所有必要检查内容，保留原图与已有处理结果。", "当前步骤，等待人工核对", GuidedStepState.Current),
            new("second", "核实这一任务的最终保存结果以及实际保存的位置", "先检查前一步的必要结果，再进行下一步；步骤状态由调用方提供。", "待进行，不会自动开始", GuidedStepState.Pending),
        ];
        WpfRendering.RenderExpectingNoBindingErrors(() => Host(items), new Size(240, 800), tree =>
        {
            Border[] rows = [.. tree.OfType<Border>().Where(b => b.Name == "StepSurface")];
            rows.Length.ShouldBe(2);
            foreach (TextBlock text in tree.OfType<TextBlock>().Where(t => t.Name is "StepTitle" or "StepDetail" or "StepStatus"))
            {
                text.TextWrapping.ShouldBe(TextWrapping.Wrap);
                Rect bounds = text.TransformToAncestor(tree.Root).TransformBounds(new Rect(text.RenderSize));
                bounds.Left.ShouldBeGreaterThanOrEqualTo(0);
                bounds.Right.ShouldBeLessThanOrEqualTo(240.5);
                text.ActualHeight.ShouldBeGreaterThanOrEqualTo(text.DesiredSize.Height - text.Margin.Top - text.Margin.Bottom - 0.5);
            }
            Rect first = rows[0].TransformToAncestor(tree.Root).TransformBounds(new Rect(rows[0].RenderSize));
            Rect second = rows[1].TransformToAncestor(tree.Root).TransformBounds(new Rect(rows[1].RenderSize));
            first.Bottom.ShouldBeLessThanOrEqualTo(second.Top);
            second.Bottom.ShouldBeLessThanOrEqualTo(800);
            return 0;
        });
    }

    private static UserControl Host(GuidedStepItem[] items)
    {
        ItemsControl steps = new() { ItemsSource = items };
        steps.SetResourceReference(FrameworkElement.StyleProperty, "PF.StepNavigation");
        return GuidedThemeFixture.Host(steps);
    }

    private static IEnumerable<DependencyObject> VisualDescendants(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (DependencyObject child in VisualDescendants(VisualTreeHelper.GetChild(root, i)))
                yield return child;
    }
}
