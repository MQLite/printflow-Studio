using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using PrintFlow.App.Controls;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class GuidedShellLayoutTests
{
    [Theory]
    [InlineData(WorkflowType.PrepareAsset, 1000, 700)]
    [InlineData(WorkflowType.PrepareAsset, 984, 661)]
    [InlineData(WorkflowType.PrepareAsset, 1280, 720)]
    [InlineData(WorkflowType.PrepareAsset, 1920, 1080)]
    [InlineData(WorkflowType.PrepareCustomerDesign, 1000, 700)]
    [InlineData(WorkflowType.PrepareCustomerDesign, 1280, 720)]
    [InlineData(WorkflowType.PrepareCustomerDesign, 1920, 1080)]
    [InlineData(WorkflowType.GeneratePrintTiff, 1000, 700)]
    [InlineData(WorkflowType.GeneratePrintTiff, 1280, 720)]
    [InlineData(WorkflowType.GeneratePrintTiff, 1920, 1080)]
    public async Task Current_step_and_one_right_primary_survive_long_task_names(WorkflowType route, int width, int height)
    {
        using OperatorCultureScope culture = new("zh-CN");
        using HomeScreenHarness h = new();
        string name = "客户设计图—请保留花卉标志与全部中文说明—最终确认用稿—请勿与历史版本混淆";
        var imported = (await h.Sessions.ImportAsync(route, h.Inner.WriteBorderedSourcePng("shell.png"), name, "tester", default)).Value;
        var model = h.Session(new RecordingNavigation());
        model.Open(imported); await model.PreviewsLoaded;
        WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = model }, new(width, height), tree =>
        {
            var heading = GuidedSessionShellTests.Find<HeaderedContentControl>(tree, "Session.CurrentStepTitle");
            heading.Header.ShouldBe(model.GuidedCurrentTitle);
            var summary = GuidedSessionShellTests.Find<TextBlock>(tree, "Session.TaskSummary");
            summary.Text.ShouldBe(model.SessionName);
            summary.FontSize.ShouldBeLessThan(20);
            summary.TextTrimming.ShouldBe(TextTrimming.CharacterEllipsis);
            tree.OfType<TextBlock>().ShouldContain(t => t.Text == model.GuidedCurrentTitle && t.FontSize >= 24 && t.FontSize <= 30);
            Button primary = tree.OfType<Button>().Where(Shown).Single(b => ReferenceEquals(b.Command, model.RecommendedCommand));
            var back = GuidedSessionShellTests.Find<Button>(tree, "Session.BackToHome");
            Rect p = Bounds(primary, tree.Root), b = Bounds(back, tree.Root);
            p.Left.ShouldBeGreaterThan(width * .55);
            b.Right.ShouldBeLessThanOrEqualTo(p.Left);
            p.Right.ShouldBeLessThanOrEqualTo(width); p.Bottom.ShouldBeLessThanOrEqualTo(height);
            tree.OfType<Button>().Where(Shown).Count(x => x.Command is not null && x.Background is SolidColorBrush brush && brush.Color == Color.FromRgb(0x16, 0x76, 0x5D)).ShouldBe(1);
            var actions = GuidedSessionShellTests.Find<FrameworkElement>(tree, "Session.ActionBar");
            foreach (string id in new[] { "Session.Settings", "Session.Environment" })
                GuidedSessionShellTests.Ancestors(GuidedSessionShellTests.Find<Button>(tree, id)).ShouldNotContain(actions);
            var steps = GuidedSessionShellTests.Find<ItemsControl>(tree, "Session.StepList");
            typeof(GuidedStepItem).GetProperty("Number").ShouldNotBeNull();
            model.GuidedSteps.Select(s => (int)typeof(GuidedStepItem).GetProperty("Number")!.GetValue(s)!).ShouldBe(Enumerable.Range(1, model.GuidedSteps.Count));
            if (route == WorkflowType.PrepareAsset)
                model.GuidedSteps.ShouldNotContain(s => s.Id == "PrintDimensions" || s.Id == "PhotoshopOutput");
            if (route == WorkflowType.GeneratePrintTiff)
                model.GuidedSteps.ShouldNotContain(s => s.Id == "Enhancement" || s.Id == "BackgroundRemoval" || s.Id == "Trim");
            var current = tree.OfType<TextBlock>().Single(t => t.Name == "CompactStepTitle" && t.DataContext is GuidedStepItem { State: GuidedStepState.Current });
            Bounds(current, tree.Root).Bottom.ShouldBeLessThanOrEqualTo(height);
            steps.Focusable.ShouldBeFalse();
            primary.IsDefault.ShouldBeFalse();
            return 0;
        });
        string? directory = Environment.GetEnvironmentVariable("PF_GUIDED_SHELL_CAPTURE_DIR");
        if (!string.IsNullOrWhiteSpace(directory))
            WpfRendering.CapturePng(() => new SessionScreenView { DataContext = model }, new(width, height),
                Path.Combine(directory, $"{route}-{width}x{height}.png"));
    }

    [Fact]
    public void Compact_future_steps_have_no_executable_automation_or_tab_surface()
    {
        WpfRendering.RenderExpectingNoBindingErrors(() =>
        {
            ItemsControl list = new() { ItemsSource = new[] { new GuidedStepItem("future", "生成打印文件", "尚未到达", "待进行", GuidedStepState.Pending) } };
            UserControl host = new() { Content = list };
            host.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/PrintFlow.App;component/Themes/WpfUiTheme.xaml", UriKind.Relative) });
            list.SetResourceReference(FrameworkElement.StyleProperty, "PF.StepNavigation.Compact");
            return host;
        }, new Size(190, 200), tree =>
        {
            tree.OfType<TextBlock>().ShouldContain(t => t.Name == "CompactStepTitle" && t.Text == "生成打印文件");
            tree.OfType<TextBlock>().ShouldNotContain(t => t.Text == "尚未到达" && Shown(t));
            tree.OfType<Button>().ShouldBeEmpty();
            tree.OfType<UIElement>().ShouldNotContain(e => e.Focusable);
            foreach (UIElement element in tree.OfType<UIElement>())
            {
                var peer = UIElementAutomationPeer.CreatePeerForElement(element);
                peer?.GetPattern(PatternInterface.Invoke).ShouldBeNull();
                peer?.GetPattern(PatternInterface.SelectionItem).ShouldBeNull();
            }
            return 0;
        });
    }

    [Theory]
    [InlineData(1000, 700)]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    public async Task Long_save_paths_keep_full_value_and_fixed_save_action(int width, int height)
    {
        using OperatorCultureScope culture = new("zh-CN");
        using SessionServiceHarness h = new();
        var workflow = FinalSaveFixtures.TiffService(h);
        var review = await FinalSaveFixtures.TiffAtFinalReviewAsync(h, workflow);
        string root = FinalSaveFixtures.NewFolder("shell-long-path");
        string folder = Path.Combine(root, new string('长', 40), new string('路', 40));
        Directory.CreateDirectory(folder);
        try
        {
            var model = await GuidedCompletionSaveTests.Open(h, workflow, FinalSaveFixtures.Delivery(h), new GuidedCompletionSaveTests.Picker(folder), review);
            await model.ApproveCommand.ExecuteAsync(null); await model.PreviewsLoaded; await model.FinalSaveFactsLoaded;
            model.ChangeLocationCommand.Execute(null);
            model.SaveFileNameText = "客户花卉图案—保留完整设计边缘—最终打印结果.tif";
            model.CanSaveApproved.ShouldBeTrue();
            WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = model }, new(width, height), tree =>
            {
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.SystemIdle); tree.Root.UpdateLayout();
                var path = GuidedSessionShellTests.Find<TextBox>(tree, "Session.FinalSave.Folder");
                path.Text.ShouldBe(folder); path.ToolTip.ShouldBe(folder);
                var action = GuidedSessionShellTests.Find<ReviewApprovalButton>(tree, "Session.FinalSave.SaveApproved");
                action.Command.ShouldBeSameAs(model.SaveApprovedCommand); action.TargetIdentity.ShouldBe(model.SaveApprovedIdentity);
                Bounds(action, tree.Root).Left.ShouldBeGreaterThan(width * .55);
                Bounds(action, tree.Root).Right.ShouldBeLessThanOrEqualTo(width);
                Bounds(action, tree.Root).Bottom.ShouldBeLessThanOrEqualTo(height);
                Bounds(path, tree.Root).Bottom.ShouldBeLessThanOrEqualTo(Bounds(action, tree.Root).Top);
                return 0;
            });
            string? directory = Environment.GetEnvironmentVariable("PF_GUIDED_SHELL_CAPTURE_DIR");
            if (!string.IsNullOrWhiteSpace(directory))
                WpfRendering.CapturePng(() => new SessionScreenView { DataContext = model }, new(width, height), Path.Combine(directory, $"long-path-save-{width}x{height}.png"));
        }
        finally { FinalSaveFixtures.Remove(root); }
    }

    [Fact]
    public void Current_attention_step_is_scrolled_into_view_without_focus_or_command()
    {
        int calls = 0;
        WpfRendering.RenderExpectingNoBindingErrors(() =>
        {
            var list = new GuidedStepList { ItemsSource = Enumerable.Range(1, 12).Select(i =>
                new GuidedStepItem(i.ToString(), "当前路线步骤" + i, "按需查看的说明", "需要注意", GuidedStepState.Attention)
                    { Number = i, IsCurrent = i == 12 }).ToArray() };
            list.SetResourceReference(FrameworkElement.StyleProperty, "PF.StepNavigation.Compact");
            Grid grid = new(); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(180) }); grid.RowDefinitions.Add(new RowDefinition());
            grid.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            Button neutral = new() { Content = "原有焦点", Command = new RelayCommand(() => calls++) };
            Grid.SetRow(neutral, 1); grid.Children.Add(neutral);
            UserControl host = new() { Content = grid };
            host.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/PrintFlow.App;component/Themes/WpfUiTheme.xaml", UriKind.Relative) });
            return host;
        }, new Size(220, 240), tree =>
        {
            var focus = tree.OfType<Button>().Single();
            FocusManager.SetFocusedElement(tree.Root, focus);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.SystemIdle);
            tree.Root.UpdateLayout();
            var list = tree.OfType<GuidedStepList>().Single();
            var scroll = tree.OfType<ScrollViewer>().First(s => ReferenceEquals(s.Content, list));
            var current = (FrameworkElement)list.ItemContainerGenerator.ContainerFromIndex(11);
            Rect box = Bounds(current, scroll);
            box.Top.ShouldBeGreaterThanOrEqualTo(-.5); box.Bottom.ShouldBeLessThanOrEqualTo(scroll.ViewportHeight + .5);
            scroll.VerticalOffset.ShouldBeGreaterThan(0);
            FocusManager.GetFocusedElement(tree.Root).ShouldBeSameAs(focus);
            calls.ShouldBe(0);
            return 0;
        });
    }

    [Theory]
    [InlineData(300)]
    [InlineData(550)]
    public void Fixed_bar_reflows_and_recommendation_updates_do_not_invoke_commands(int width)
    {
        int calls = 0;
        ICommand first = new RelayCommand(() => calls++), second = new RelayCommand(() => calls++);
        WpfRendering.RenderExpectingNoBindingErrors(() =>
        {
            var panel = new GuidedActionPanel { PrimaryCommand = first };
            panel.Children.Add(new Button { Content = "返回并保留任务", Width = 120, Height = 42 });
            panel.Children.Add(new Button { Content = "确认此结果", Command = first, Width = 120, Height = 42 });
            panel.Children.Add(new Button { Content = "核实本次保存", Command = second, Width = 120, Height = 42 });
            return new UserControl { Content = panel };
        }, new Size(width, 200), tree =>
        {
            var panel = tree.OfType<GuidedActionPanel>().Single();
            var next = tree.OfType<Button>().Single(b => ReferenceEquals(b.Command, second));
            panel.PrimaryCommand = second; tree.Root.UpdateLayout();
            Bounds(next, panel).Right.ShouldBe(width);
            var boxes = tree.OfType<Button>().Select(b => Bounds(b, panel)).ToArray();
            for (int i = 0; i < boxes.Length; i++)
            {
                boxes[i].Right.ShouldBeLessThanOrEqualTo(width);
                for (int j = i + 1; j < boxes.Length; j++)
                {
                    Rect overlap = Rect.Intersect(boxes[i], boxes[j]);
                    (overlap.Width > 0 && overlap.Height > 0).ShouldBeFalse("touching edges are allowed; overlapping controls are not");
                }
            }
            calls.ShouldBe(0);
            return 0;
        });
    }

    private static bool Shown(FrameworkElement e) => GuidedSessionProjectionTests.Shown(e);
    private static Rect Bounds(FrameworkElement e, Visual root) => GuidedSessionShellTests.Bounds(e, root);
}
