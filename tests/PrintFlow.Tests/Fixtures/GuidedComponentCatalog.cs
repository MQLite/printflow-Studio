using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.Input;
using PrintFlow.App.Controls;

namespace PrintFlow.Tests.Fixtures;

internal sealed class GuidedCatalogActions
{
    public GuidedCatalogActions(bool canContinue, bool canStop)
    {
        Continue = new RelayCommand(() => ContinueCount++, () => canContinue);
        Stop = new RelayCommand(() => StopCount++, () => canStop);
    }

    public IRelayCommand Continue { get; }
    public IRelayCommand Stop { get; }
    public int ContinueCount { get; private set; }
    public int StopCount { get; private set; }
}

internal static class GuidedComponentCatalog
{
    public static UserControl Create(string state)
    {
        if (state is not ("normal" or "disabled" or "error" or "loading" or "long-text"))
            throw new ArgumentOutOfRangeException(nameof(state));

        bool loading = state == "loading";
        GuidedCatalogActions actions = new(state is "normal" or "long-text", loading);
        Grid layout = new() { Margin = new Thickness(24) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        HeaderedContentControl heading = Component("PF.PageHeader", "检查当前图片", "确认当前任务和输入，完成必要检查后再继续。");
        heading.Margin = new Thickness(0, 0, 0, 24);
        layout.Children.Add(heading);

        StackPanel sections = new();
        HeaderedContentControl task = Component("PF.TaskCard", "当前任务", Text(state == "long-text"
            ? "客户设计图—请保留所有中文说明与完整文件名，确认图片内容、透明边缘和当前任务一致，任何必要的检查或失败原因都应完整显示并能够通过滚动阅读。" +
              "当说明较长时，文字自然换行，底部操作仍保持可用。"
            : "夏日花卉设计.png · 合成演示素材\n原图保持不变，尚未执行处理或审批。"));
        task.Margin = new Thickness(0, 0, 0, 16);
        sections.Children.Add(task);

        (string style, string title, string body) = state switch
        {
            "disabled" => ("Warning", "请先完成检查", "请先确认图片与任务一致，继续操作当前不可用。"),
            "error" => ("Error", "处理未完成", "当前输入已保留。请检查失败原因，再选择可用操作。"),
            "loading" => ("Info", "正在处理", "处理进度暂不可估计，请等待。需要中断时可请求安全停止。"),
            _ => ("Success", "输入已准备", "可以检查当前图片，确认后再继续下一步。"),
        };
        StackPanel noticeContent = new();
        noticeContent.Children.Add(Text(body));
        if (loading)
        {
            ProgressBar progress = Styled(new ProgressBar { IsIndeterminate = true, Margin = new Thickness(0, 12, 0, 4) }, "PF.Progress");
            noticeContent.Children.Add(progress);
        }
        HeaderedContentControl notice = Component("PF.Notice." + style, title, noticeContent);
        notice.Margin = new Thickness(0, 0, 0, 16);
        sections.Children.Add(notice);

        StackPanel fields = new();
        fields.Children.Add(Text("任务名称"));
        TextBox name = Styled(new TextBox { Text = "夏日花卉设计", Margin = new Thickness(0, 8, 0, 12) }, "PF.Input.TextBox");
        AutomationProperties.SetName(name, "任务名称");
        fields.Children.Add(name);
        CheckBox confirmed = Styled(new CheckBox { Content = "我已核对当前图片", IsChecked = state == "normal" }, "PF.Input.CheckBox");
        fields.Children.Add(confirmed);
        Expander details = Styled(new Expander { Header = "辅助说明", Margin = new Thickness(0, 8, 0, 0) }, "PF.Disclosure");
        Button hidden = Styled(new Button { Content = "查看合成详情", Command = actions.Continue }, "PF.Button.Secondary");
        AutomationProperties.SetAutomationId(hidden, "GuidedCatalog.CollapsedAction");
        details.Content = hidden;
        fields.Children.Add(details);
        HeaderedContentControl form = Component("PF.FormSection", "检查内容", fields);
        sections.Children.Add(Component("PF.TaskCard", "本次输入", form));

        ScrollViewer scroll = new()
        {
            Content = sections, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, IsTabStop = false,
            Margin = new Thickness(0, 0, 0, 16),
        };
        AutomationProperties.SetAutomationId(scroll, "GuidedCatalog.Content");
        Grid workspace = new();
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition());
        ItemsControl steps = Styled(new ItemsControl
        {
            ItemsSource = new[]
            {
                new GuidedStepItem("import", "导入图片", "已保留原图", "已完成", GuidedStepState.Completed),
                new GuidedStepItem("check", "检查原图", "请核对当前内容", "当前步骤", GuidedStepState.Current),
                new GuidedStepItem("process", "处理图片", "等待前一步完成", "待进行", GuidedStepState.Pending),
                new GuidedStepItem("save", "核实保存", "需要检查保存结果", "需要注意", GuidedStepState.Attention),
                new GuidedStepItem("optional", "可选增强", "已选择保留当前图片", "已跳过", GuidedStepState.Skipped),
            },
        }, "PF.StepNavigation");
        ScrollViewer stepScroll = new()
        {
            Content = steps, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, IsTabStop = false,
            Margin = new Thickness(0, 0, 0, 16),
        };
        workspace.Children.Add(stepScroll);
        Grid.SetColumn(scroll, 2);
        workspace.Children.Add(scroll);
        Grid.SetRow(workspace, 1);
        layout.Children.Add(workspace);

        WrapPanel buttons = new();
        Button primary = Styled(new Button
        {
            Content = loading ? "正在处理，请等待" : "确认并继续", Command = actions.Continue,
            Margin = new Thickness(0, 0, 12, 0),
        }, "PF.Button.Primary");
        AutomationProperties.SetAutomationId(primary, "GuidedCatalog.Continue");
        buttons.Children.Add(primary);
        if (loading)
        {
            Button stop = Styled(new Button { Content = "请求安全停止", Command = actions.Stop }, "PF.Button.Danger");
            AutomationProperties.SetAutomationId(stop, "GuidedCatalog.Stop");
            AutomationProperties.SetName(stop, "请求安全停止");
            buttons.Children.Add(stop);
        }
        ContentControl actionBar = Styled(new ContentControl { Content = buttons }, "PF.ActionBar");
        AutomationProperties.SetAutomationId(actionBar, "GuidedCatalog.ActionBar");
        Grid.SetRow(actionBar, 2);
        layout.Children.Add(actionBar);

        Border canvas = new() { Child = layout };
        canvas.SetResourceReference(Border.BackgroundProperty, "PF.Brush.Canvas");
        UserControl host = GuidedThemeFixture.Host(canvas);
        host.Tag = actions;
        return host;
    }

    private static HeaderedContentControl Component(string style, string heading, object content) =>
        Styled(new HeaderedContentControl { Header = heading, Content = content }, style);

    private static TextBlock Text(string value) => new() { Text = value, TextWrapping = TextWrapping.Wrap };

    private static T Styled<T>(T element, string style) where T : FrameworkElement
    {
        element.SetResourceReference(FrameworkElement.StyleProperty, style);
        return element;
    }
}
