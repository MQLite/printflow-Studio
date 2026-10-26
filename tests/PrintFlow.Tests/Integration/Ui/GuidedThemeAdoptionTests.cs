using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class GuidedThemeAdoptionTests
{
    [Fact]
    public async Task Standalone_adopted_views_resolve_theme_without_application()
    {
        using OperatorCultureScope culture = new("zh-CN");
        using HomeScreenHarness harness = new();
        WorkflowSelectionViewModel route = await OpenRoute(harness, "主题合成图片.png");
        foreach (Func<UserControl> create in new Func<UserControl>[]
                 { () => new HomeView { DataContext = harness.Home }, () => new WorkflowSelectionView { DataContext = route } })
        {
            WpfRendering.RenderExpectingNoBindingErrors(create, new Size(1000, 700), tree =>
            {
                Application.Current.ShouldBeNull();
                tree.Root.TryFindResource("PF.Button.Primary").ShouldBeOfType<Style>();
                tree.Root.FontFamily.Source.ShouldBe("Microsoft YaHei, Noto Sans SC");
                // U04 also opts route selection into the shared 16-DIP body.
                tree.Root.FontSize.ShouldBe(16);
                tree.OfType<HeaderedContentControl>().ShouldContain(c => ReferenceEquals(c.Style, tree.Root.FindResource("PF.PageHeader")));
                return 0;
            });
        }
    }

    [Fact]
    public async Task Home_keeps_import_warnings_and_recent_actions()
    {
        using OperatorCultureScope culture = new("zh-CN");
        using HomeScreenHarness harness = new();
        await OpenRoute(harness, "当前任务.png");
        await harness.Home.RefreshCommand.ExecuteAsync(null);
        await harness.Home.ThumbnailsLoaded;
        RecentSessionRow row = harness.Home.RecentSessions.Single();
        WpfRendering.RenderExpectingNoBindingErrors(() => new HomeView { DataContext = harness.Home }, new Size(1000, 700), tree =>
        {
            Button import = Find<Button>(tree, "Home.ChooseFile");
            import.Command.ShouldBeSameAs(harness.Home.ChooseFileCommand);
            import.MinHeight.ShouldBeGreaterThanOrEqualTo(42);
            import.Style.ShouldBeSameAs(tree.Root.FindResource("PF.Button.Primary"));
            Find<Button>(tree, "Home.ShowEnvironment").Command.ShouldBeSameAs(harness.Home.ShowEnvironmentCommand);
            Find<Button>(tree, "Home.ShowSettings").Command.ShouldBeSameAs(harness.Home.ShowSettingsCommand);
            Button resume = Find<Button>(tree, "Home.ResumeSession");
            resume.Command.ShouldBeSameAs(harness.Home.ResumeCommand);
            resume.CommandParameter.ShouldBeSameAs(row);
            Button abandon = Find<Button>(tree, "Home.AbandonSession");
            abandon.Command.ShouldBeSameAs(harness.Home.AbandonCommand);
            abandon.CommandParameter.ShouldBeSameAs(row);
            AssertPersistentWarnings(tree);
            return 0;
        });
        await harness.Home.AbandonCommand.ExecuteAsync(row);
        WpfRendering.RenderExpectingNoBindingErrors(() => new HomeView { DataContext = harness.Home }, new Size(1000, 700), tree =>
        {
            ReviewApprovalButton confirm = Find<ReviewApprovalButton>(tree, "Home.AbandonConfirmation.Confirm");
            confirm.Command.ShouldBeSameAs(harness.Home.ConfirmAbandonCommand);
            confirm.CommandParameter.ShouldBeSameAs(harness.Home.PendingAbandon);
            confirm.TargetIdentity.ShouldBe(harness.Home.PendingAbandon!.GestureTarget);
            confirm.IsGestureGuarded.ShouldBeTrue();
            confirm.IsDefault.ShouldBeFalse();
            Find<Button>(tree, "Home.AbandonConfirmation.Keep").IsCancel.ShouldBeTrue();
            return 0;
        });
    }

    [Fact]
    public async Task Route_cards_keep_exact_commands_parameters_and_three_workflows()
    {
        using OperatorCultureScope culture = new("zh-CN");
        using HomeScreenHarness harness = new();
        WorkflowSelectionViewModel model = await OpenRoute(harness, "三条路线.png");
        WpfRendering.RenderExpectingNoBindingErrors(() => new WorkflowSelectionView { DataContext = model }, new Size(1000, 700), tree =>
        {
            Button[] choices = [.. tree.OfType<Button>().Where(b => b.CommandParameter is WorkflowChoice)];
            choices.Length.ShouldBe(3);
            choices.Select(b => ((WorkflowChoice)b.CommandParameter).Type).ShouldBe(new[]
                { WorkflowType.PrepareAsset, WorkflowType.PrepareCustomerDesign, WorkflowType.GeneratePrintTiff });
            for (int i = 0; i < choices.Length; i++)
            {
                choices[i].Command.ShouldBeSameAs(model.SelectCommand);
                choices[i].CommandParameter.ShouldBeSameAs(model.Workflows[i]);
                choices[i].IsEnabled.ShouldBe(model.CanSelect);
                choices[i].IsDefault.ShouldBeFalse();
                choices[i].MinHeight.ShouldBeGreaterThanOrEqualTo(42);
                AutomationProperties.GetAutomationId(choices[i]).ShouldBe(model.Workflows[i].AutomationId);
            }
            Find<Button>(tree, "WorkflowSelection.Back").Command.ShouldBeSameAs(model.BackToHomeCommand);
            Find<TextBox>(tree, "WorkflowSelection.OutputName").Text.ShouldBe(model.OutputName);
            tree.OfType<Image>().Count(i => i.Source != null).ShouldBe(1);
            tree.OfType<TextBox>().Count(t => !t.IsReadOnly).ShouldBe(1, "route selection has no print size or white-ink inputs");
            tree.OfType<ComboBox>().ShouldBeEmpty();
            tree.OfType<RadioButton>().ShouldBeEmpty();
            model.Workflows[0].Steps.ShouldAllBe(s => !s.Contains("白墨") && !s.Contains("TIFF"));
            return 0;
        });
        harness.Navigation.SessionFor.ShouldBeNull();
        harness.Meitu.CallCount.ShouldBe(0);
    }

    [Theory]
    [InlineData(1000, 700)]
    [InlineData(1280, 720)]
    public async Task Long_Chinese_source_and_failure_text_keep_actions_reachable(int width, int height)
    {
        using OperatorCultureScope culture = new("zh-CN");
        using HomeScreenHarness harness = new();
        string name = string.Concat(Enumerable.Repeat("客户提供的夏日花卉设计图片", 6)) + ".png";
        WorkflowSelectionViewModel model = await OpenRoute(harness, name);
        string failure = string.Concat(Enumerable.Repeat("当前操作没有完成，原始输入和已检查结果都已保留。请完整阅读说明，检查必要条件之后再选择可用操作。", 4));
        model.Notice = failure;
        harness.Home.Notice = failure;
        await harness.Home.RefreshCommand.ExecuteAsync(null);
        await harness.Home.ThumbnailsLoaded;
        Size viewport = new(width, height);
        WpfRendering.RenderExpectingNoBindingErrors(() => new HomeView { DataContext = harness.Home }, viewport, tree =>
        {
            Settle(tree.Root, viewport);
            AssertHorizontalBounds(tree, width);
            AssertPersistentWarnings(tree);
            Button import = Find<Button>(tree, "Home.ChooseFile");
            ScrollViewer upper = Find<ScrollViewer>(tree, "Home.Upper");
            import.BringIntoView();
            Settle(tree.Root, viewport);
            AssertInside(import, upper, upper.ViewportWidth, upper.ViewportHeight);
            import.MinHeight.ShouldBeGreaterThanOrEqualTo(42);
            return 0;
        });
        WpfRendering.RenderExpectingNoBindingErrors(() => new WorkflowSelectionView { DataContext = model }, viewport, tree =>
        {
            Settle(tree.Root, viewport);
            TextBlock source = Find<TextBlock>(tree, "WorkflowSelection.SourceFile");
            source.Text.ShouldBe(name);
            source.TextWrapping.ShouldBe(TextWrapping.Wrap);
            source.TextTrimming.ShouldBe(TextTrimming.None);
            AssertHorizontalBounds(tree, width);
            Button back = Find<Button>(tree, "WorkflowSelection.Back");
            AssertInside(back, tree.Root, width, height);
            Button last = tree.OfType<Button>().Where(b => b.CommandParameter is WorkflowChoice).Last();
            ScrollViewer scroll = tree.OfType<ScrollViewer>().Single(s => s.Content is StackPanel);
            last.BringIntoView();
            Settle(tree.Root, viewport);
            AssertInside(last, scroll, scroll.ViewportWidth, scroll.ViewportHeight);
            TextBlock notice = Find<TextBlock>(tree, "WorkflowSelection.Notice");
            notice.Text.ShouldBe(failure);
            notice.TextWrapping.ShouldBe(TextWrapping.Wrap);
            Ancestors(notice).ShouldNotContain(e => e is Expander);
            return 0;
        });
    }

    private static async Task<WorkflowSelectionViewModel> OpenRoute(HomeScreenHarness harness, string name)
    {
        harness.FilePicker.Path = harness.WriteSourceFile(name);
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);
        harness.Navigation.WorkflowSelectionFor.ShouldNotBeNull();
        WorkflowSelectionViewModel model = harness.WorkflowSelection(new RecordingNavigation());
        model.Open(harness.Navigation.WorkflowSelectionFor!);
        await model.PreviewLoaded;
        return model;
    }

    private static T Find<T>(RenderedTree tree, string id) where T : FrameworkElement =>
        tree.OfType<T>().Single(e => AutomationProperties.GetAutomationId(e) == id);

    private static void AssertPersistentWarnings(RenderedTree tree)
    {
        foreach (string id in new[] { "Home.Startup.PresetWarning", "Home.Startup.RecoveryNotRun" })
        {
            TextBlock warning = Find<TextBlock>(tree, id);
            warning.Visibility.ShouldBe(Visibility.Visible);
            warning.ActualHeight.ShouldBeGreaterThan(0);
            warning.TextWrapping.ShouldBe(TextWrapping.Wrap);
            Ancestors(warning).ShouldNotContain(e => e is Expander);
        }
    }

    private static IEnumerable<DependencyObject> Ancestors(DependencyObject node)
    {
        while ((node = VisualTreeHelper.GetParent(node)) is not null)
            yield return node;
    }

    private static void AssertHorizontalBounds(RenderedTree tree, double width)
    {
        foreach (TextBlock text in tree.OfType<TextBlock>().Where(t => t.ActualWidth > 0 && t.ActualHeight > 0 &&
                     t.Visibility == Visibility.Visible && !Ancestors(t).OfType<UIElement>().Any(a => a.Visibility != Visibility.Visible)))
        {
            Rect bounds = text.TransformToAncestor(tree.Root).TransformBounds(new Rect(text.RenderSize));
            bounds.Left.ShouldBeGreaterThanOrEqualTo(-0.5, text.Text);
            bounds.Right.ShouldBeLessThanOrEqualTo(width + 0.5, text.Text);
        }
    }

    private static void AssertInside(FrameworkElement element, Visual ancestor, double width, double height)
    {
        Rect bounds = element.TransformToAncestor(ancestor).TransformBounds(new Rect(element.RenderSize));
        bounds.Top.ShouldBeGreaterThanOrEqualTo(-0.5);
        bounds.Bottom.ShouldBeLessThanOrEqualTo(height + 0.5);
        bounds.Left.ShouldBeGreaterThanOrEqualTo(-0.5);
        bounds.Right.ShouldBeLessThanOrEqualTo(width + 0.5);
        bounds.Height.ShouldBeGreaterThan(0);
    }

    private static void Settle(UserControl root, Size viewport)
    {
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.SystemIdle);
        root.Measure(viewport);
        root.Arrange(new Rect(viewport));
        root.UpdateLayout();
    }
}
