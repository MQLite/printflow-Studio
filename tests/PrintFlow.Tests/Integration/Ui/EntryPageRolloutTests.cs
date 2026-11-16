using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Tests.Integration.Persistence;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class EntryPageRolloutTests
{
    public static TheoryData<string, int, int> Viewports()
    {
        TheoryData<string, int, int> data = new();
        foreach (string language in new[] { "zh-CN", "en-US" })
            foreach ((int width, int height) in new[] { (984, 661), (1000, 700), (1280, 720), (1920, 1080) })
                data.Add(language, width, height);
        return data;
    }

    [Theory]
    [MemberData(nameof(Viewports))]
    public async Task Route_cards_align_decision_facts_and_keep_names_actions_and_source(string language, int width, int height)
    {
        using OperatorCultureScope culture = new(language);
        using HomeScreenHarness h = new();
        h.FilePicker.Path = h.WriteSourceFile("本次导入的图片.png");
        await h.Home.ChooseFileCommand.ExecuteAsync(null);
        WorkflowSelectionViewModel model = new(h.Sessions, h.Previews, h.Navigation);
        model.Open(h.Navigation.WorkflowSelectionFor!);
        await model.PreviewLoaded;
        Size size = new(width, height);
        Capture(() => new WorkflowSelectionView { DataContext = model }, size, "route-" + language);
        WpfRendering.RenderExpectingNoBindingErrors(() => new WorkflowSelectionView { DataContext = model }, size, tree =>
        {
            tree.Root.FontSize.ShouldBe(16);
            tree.OfType<Wpf.Ui.Controls.Card>().Count().ShouldBe(4);
            foreach (WorkflowChoice choice in model.Workflows)
            {
                Button action = Find<Button>(tree, choice.AutomationId);
                action.Command.ShouldBeSameAs(model.SelectCommand);
                action.CommandParameter.ShouldBeSameAs(choice);
                new ButtonAutomationPeer(action).GetName().ShouldBe(choice.Title);
                action.ActualHeight.ShouldBeGreaterThanOrEqualTo(42);
                action.IsDefault.ShouldBeFalse();
                action.IsTabStop.ShouldBeTrue();
                Reachable(action, tree.Root);
            }
            foreach (Func<WorkflowChoice, string> read in new Func<WorkflowChoice, string>[] { c => c.Purpose, c => c.Result, c => c.Dimensions })
            {
                double[] tops = model.Workflows.Select(choice => tree.OfType<TextBlock>()
                    .First(t => t.Text == read(choice)).TransformToAncestor(tree.Root).Transform(new Point()).Y).ToArray();
                (tops.Max() - tops.Min()).ShouldBeLessThan(0.5, "the same decision fact occupies the same row in all three routes");
            }
            var source = Find<TextBlock>(tree, "WorkflowSelection.SourceFile");
            source.ToolTip.ShouldBe(model.SourceFileName);
            Find<TextBox>(tree, "WorkflowSelection.OutputName").Text.ShouldBe(model.OutputName);
            Reachable(Find<Button>(tree, "WorkflowSelection.Back"), tree.Root);
            return true;
        });
    }

    [Theory]
    [MemberData(nameof(Viewports))]
    public async Task Home_keeps_diagnostics_in_the_header_and_continue_reachable(string language, int width, int height)
    {
        using OperatorCultureScope culture = new(language);
        using HomeScreenHarness h = new();
        await h.Sessions.ImportAsync(WorkflowType.PrepareAsset, h.WriteSourceFile("continue.png"), "继续这项任务", "tester", default);
        await h.Home.RefreshCommand.ExecuteAsync(null);
        await h.Home.ThumbnailsLoaded;
        Size size = new(width, height);
        Capture(() => new HomeView { DataContext = h.Home }, size, "home-" + language);
        WpfRendering.RenderExpectingNoBindingErrors(() => new HomeView { DataContext = h.Home }, size, tree =>
        {
            var upper = Find<ScrollViewer>(tree, "Home.Upper");
            var diagnostics = Find<Button>(tree, "Home.ShowEnvironment");
            GuidedSessionShellTests.Ancestors(diagnostics).ShouldNotContain(upper);
            tree.OfType<TextBlock>().ShouldNotContain(text => AutomationProperties.GetAutomationId(text) == "Home.Readiness.Status");
            InViewport(diagnostics, tree.Root);
            Reachable(Find<Button>(tree, "Home.ChooseFile"), tree.Root);
            Reachable(diagnostics, tree.Root);
            Reachable(Find<Button>(tree, "Home.ResumeSession"), tree.Root);
            var hint = tree.OfType<TextBlock>().Single(t => t.Text == h.Home.ImportHint);
            hint.FontSize.ShouldBeGreaterThanOrEqualTo(16);
            return true;
        });
    }

    [Theory]
    [MemberData(nameof(Viewports))]
    public async Task Original_has_persistent_checks_a_large_preview_and_the_existing_confirm_action(string language, int width, int height)
    {
        using OperatorCultureScope culture = new(language);
        using HomeScreenHarness h = new();
        var model = await GuidedSessionProjectionTests.Open(h, WorkflowType.PrepareAsset);
        await model.PreviewsLoaded;
        Size size = new(width, height);
        Capture(() => new SessionScreenView { DataContext = model }, size, "original-" + language);
        WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = model }, size, tree =>
        {
            var checks = Find<FrameworkElement>(tree, "Session.OriginalChecks");
            GuidedSessionProjectionTests.Shown(checks).ShouldBeTrue();
            InViewport(checks, tree.Root);
            GuidedSessionShellTests.Ancestors(checks).OfType<Expander>().ShouldBeEmpty();
            var checkText = Find<TextBlock>(tree, "Session.OriginalChecks.Text");
            checkText.FontSize.ShouldBe(16);
            checkText.Text.ShouldNotBeNullOrWhiteSpace();
            var images = tree.OfType<Image>().Where(i => i.Source is not null && GuidedSessionProjectionTests.Shown(i)).ToArray();
            images.ShouldNotBeEmpty();
            images.Max(i => i.ActualHeight).ShouldBeGreaterThan(150);
            var confirm = Find<Button>(tree, "Session.ConfirmOriginal");
            confirm.Command.ShouldBeSameAs(model.ConfirmOriginalCommand);
            new ButtonAutomationPeer(confirm).GetName().ShouldBe(model.ConfirmOriginalLabel);
            confirm.IsDefault.ShouldBeFalse();
            Reachable(confirm, tree.Root);
            return true;
        });
    }

    [Theory]
    [InlineData(984, 661)]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    public async Task A_blocked_diagnostic_does_not_push_import_or_continue_out_of_reach(int width, int height)
    {
        using OperatorCultureScope culture = new("zh-CN");
        using HomeScreenHarness h = new();
        await h.Sessions.ImportAsync(WorkflowType.PrepareAsset, h.WriteSourceFile("blocked-continue.png"), "环境受阻时仍可打开的任务", "tester", default);
        h.Readiness.Complete(h.Readiness.Begin(), new EnvironmentReadinessReport(false, null, DateTimeOffset.UtcNow,
            [new("InteractiveSession", EnvironmentCheckStatus.Failed, true, "EnvironmentCheck_InteractiveSession", "Synthetic blocker") ]));
        await h.Home.RefreshCommand.ExecuteAsync(null);
        Size size = new(width, height);
        Capture(() => new HomeView { DataContext = h.Home }, size, "home-blocked");
        WpfRendering.RenderExpectingNoBindingErrors(() => new HomeView { DataContext = h.Home }, size, tree =>
        {
            var diagnostics = Find<Button>(tree, "Home.ShowEnvironment");
            diagnostics.Command.ShouldBeSameAs(h.Home.ShowEnvironmentCommand);
            AutomationProperties.GetHelpText(diagnostics).ShouldBe(h.Home.Readiness.StatusText);
            InViewport(diagnostics, tree.Root);
            var import = Find<Button>(tree, "Home.ChooseFile");
            var upper = Find<ScrollViewer>(tree, "Home.Upper");
            var initial = import.TransformToAncestor(upper).TransformBounds(new Rect(import.RenderSize));
            initial.Bottom.ShouldBeLessThanOrEqualTo(upper.ViewportHeight + 0.5, "the import action is visible before scrolling even with a blocking reason");
            Reachable(Find<Button>(tree, "Home.ChooseFile"), tree.Root);
            Reachable(Find<Button>(tree, "Home.ResumeSession"), tree.Root);
            return true;
        });
    }

    [Theory]
    [InlineData("zh-CN", 984, 661)]
    [InlineData("zh-CN", 1280, 720)]
    [InlineData("zh-CN", 1920, 1080)]
    [InlineData("en-US", 984, 661)]
    [InlineData("en-US", 1280, 720)]
    [InlineData("en-US", 1920, 1080)]
    public async Task Expanded_startup_details_and_recovery_do_not_clip_either_abandon_confirmation_action(string language, int width, int height)
    {
        using OperatorCultureScope culture = new(language);
        using HomeScreenHarness h = new();
        await RecoverySurfaceTests.Seed(h.Inner, "needs-recovery");
        string name = string.Concat(Enumerable.Repeat("客户的完整长任务名称", 5));
        await h.Sessions.ImportAsync(WorkflowType.PrepareAsset, h.WriteSourceFile("confirm.png"), name, "tester", default);
        h.Readiness.Complete(h.Readiness.Begin(), new EnvironmentReadinessReport(false, null, DateTimeOffset.UtcNow,
            [new("InteractiveSession", EnvironmentCheckStatus.Failed, true, "EnvironmentCheck_InteractiveSession", "Synthetic blocker") ]));
        await h.Home.RefreshCommand.ExecuteAsync(null);
        h.Home.Notice = string.Concat(Enumerable.Repeat("当前操作未完成。原件和已检查结果保留，请检查原因后继续。", 5));
        h.Home.RecoverySessions.ShouldNotBeEmpty();
        await h.Home.AbandonCommand.ExecuteAsync(h.Home.ContinueSessions.Single());
        h.Home.PendingAbandon.ShouldNotBeNull();
        Size size = new(width, height);
        void Expand(RenderedTree tree)
        {
            foreach (var e in tree.OfType<Expander>()) e.IsExpanded = true;
            Settle(tree.Root);
        }
        Capture(() => new HomeView { DataContext = h.Home }, size, "home-confirm-expanded-" + language, Expand);
        WpfRendering.RenderExpectingNoBindingErrors(() => new HomeView { DataContext = h.Home }, size, tree =>
        {
            Expand(tree);
            var panel = Find<FrameworkElement>(tree, "Home.AbandonConfirmation");
            foreach (string id in new[] { "Home.AbandonConfirmation.Keep", "Home.AbandonConfirmation.Confirm" })
            {
                var action = Find<Button>(tree, id);
                Reachable(action, tree.Root);
                Rect b = action.TransformToAncestor(panel).TransformBounds(new Rect(action.RenderSize));
                b.Bottom.ShouldBeLessThanOrEqualTo(panel.ActualHeight + 0.5, "both actions fit inside the actual confirmation allocation");
                action.IsDefault.ShouldBeFalse();
            }
            Find<Button>(tree, "Home.AbandonConfirmation.Keep").IsCancel.ShouldBeTrue();
            var confirm = Find<ReviewApprovalButton>(tree, "Home.AbandonConfirmation.Confirm");
            confirm.IsGestureGuarded.ShouldBeTrue();
            confirm.TargetIdentity.ShouldBe(h.Home.PendingAbandon!.GestureTarget);
            Find<Button>(tree, "Home.ChooseFile").IsEnabled.ShouldBeFalse();
            return true;
        });
    }

    [Theory]
    [InlineData("pdf", 984, 661)]
    [InlineData("pdf", 1280, 720)]
    [InlineData("pdf", 1920, 1080)]
    [InlineData("psd", 984, 661)]
    [InlineData("psd", 1280, 720)]
    [InlineData("psd", 1920, 1080)]
    public async Task Unprepared_sources_show_the_truth_without_an_invented_preview(string extension, int width, int height)
    {
        using OperatorCultureScope culture = new("zh-CN");
        using HomeScreenHarness h = new();
        byte[] bytes = extension == "pdf" ? PdfFixtures.Read("single") : PsdInputPreparationTests.RgbCompositePsd();
        string path = h.Inner.Workspace.CreateSourceFile("尚未准备." + extension, bytes);
        h.FilePicker.Path = path; await h.Home.ChooseFileCommand.ExecuteAsync(null);
        var imported = h.Navigation.WorkflowSelectionFor!;
        WorkflowSelectionViewModel route = new(h.Sessions, h.Previews, h.Navigation);
        route.Open(imported); await route.PreviewLoaded;
        route.Preview!.HasImage.ShouldBeFalse();
        Size size = new(width, height);
        Capture(() => new WorkflowSelectionView { DataContext = route }, size, "route-unprepared-" + extension);
        WpfRendering.RenderExpectingNoBindingErrors(() => new WorkflowSelectionView { DataContext = route }, size, tree =>
        {
            tree.OfType<Image>().ShouldAllBe(i => i.Source == null);
            var unavailable = tree.OfType<TextBlock>().Single(t => t.Text == route.Preview.Unavailable);
            InViewport(unavailable, tree.Root);
            unavailable.ActualHeight.ShouldBeLessThanOrEqualTo(unavailable.Parent is FrameworkElement p ? p.ActualHeight : 0);
            return true;
        });
        await route.SelectCommand.ExecuteAsync(route.Workflows.Single(c => c.Type == WorkflowType.GeneratePrintTiff));
        var model = h.Session(new RecordingNavigation()); model.Open(h.Navigation.SessionFor!); await model.PreviewsLoaded;
        model.GuidedPage.ShouldBe(GuidedSessionPage.SourcePreparation);
        model.CanConfirmOriginal.ShouldBeFalse();
        model.ShowsGuidedOriginalChecks.ShouldBeFalse();
        Capture(() => new SessionScreenView { DataContext = model }, size, "source-unprepared-" + extension);
        WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = model }, size, tree =>
        {
            tree.OfType<Image>().Where(GuidedSessionProjectionTests.Shown).ShouldAllBe(i => i.Source == null);
            var notice = Find<TextBlock>(tree, extension == "pdf" ? "Session.PdfSourceNotice" : "Session.PsdSourceNotice");
            notice.Text.ShouldNotBeNullOrWhiteSpace(); InViewport(notice, tree.Root);
            Reachable(Find<Button>(tree, "Session.RunStep"), tree.Root);
            return true;
        });
        var stored = (await h.Inner.Repository.LoadAsync(imported.Id, default)).Value!;
        stored.Snapshot.ShouldNotBeNull();
        stored.Revisions.Count.ShouldBe(1);
        File.ReadAllBytes(path).ShouldBe(bytes);
        File.ReadAllBytes(h.Inner.FileWorkspace.ResolveAbsolute(stored.Revisions.Single().File)).ShouldBe(bytes);
    }

    [Theory]
    [InlineData("pdf", 984, 661)]
    [InlineData("pdf", 1280, 720)]
    [InlineData("pdf", 1920, 1080)]
    [InlineData("psd", 984, 661)]
    [InlineData("psd", 1280, 720)]
    [InlineData("psd", 1920, 1080)]
    public async Task A_prepared_source_displays_its_own_page_and_still_requires_exact_review(string extension, int width, int height)
    {
        using OperatorCultureScope culture = new("zh-CN");
        using SessionServiceHarness h = new();
        ISessionService service = extension == "pdf"
            ? PdfPreparationWorkflowTests.Service(h, new WindowsPdfPreparationProcessor(h.FileWorkspace, h.FileInspector))
            : h.CreateServiceWithPhotoshop(new PsdPreparationWorkflowTests.PsdProcessor(h.FileWorkspace));
        byte[] bytes = extension == "pdf" ? PdfFixtures.Read("single") : PsdInputPreparationTests.RgbCompositePsd();
        string path = h.Workspace.CreateSourceFile("已准备的合成原图." + extension, bytes);
        var imported = (await service.ImportAsync(WorkflowType.GeneratePrintTiff, path, "原图检查", "tester", default)).Value;
        SessionViewModel driver = new(service, h.Previews, h.TiffReviews, new RecordingNavigation()); driver.Open(imported);
        await driver.RunStepCommand.ExecuteAsync(null);
        SessionView state = (await service.LoadAsync(imported.Id, default)).Value;
        state.CurrentStep!.State.ShouldBe(StepState.ReviewRequired);
        if (extension == "pdf") state.PdfInspection!.PreparedPageNumber.ShouldBe(1);
        state.CurrentArtefact!.Facts.Format.ShouldBe(ImageFormat.Png);
        SessionViewModel model = new(service, h.Previews, h.TiffReviews, new RecordingNavigation()); model.Open(state); await model.PreviewsLoaded;
        model.CanApprove.ShouldBeTrue(); model.CanConfirmOriginal.ShouldBeFalse();
        model.ReviewTargetIdentity.ShouldNotBeNull();
        Capture(() => new SessionScreenView { DataContext = model }, new(width, height), "source-prepared-" + extension);
        WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = model }, new Size(width, height), tree =>
        {
            var picture = tree.OfType<Image>().Single(i => i.Source != null && GuidedSessionProjectionTests.Shown(i));
            picture.ActualHeight.ShouldBeGreaterThanOrEqualTo(120, "the actual prepared page is an inspection surface, not a thumbnail");
            tree.OfType<TextBlock>().ShouldNotContain(t => GuidedSessionProjectionTests.Shown(t) && t.Text == model.PreviewPanes[0].Unavailable);
            var identity = model.ReviewTargetIdentity;
            model.IsFitToViewport = false; model.ZoomScale = 0.75; tree.Root.UpdateLayout();
            ((ScaleTransform)picture.LayoutTransform).ScaleX.ShouldBe(0.75, 0.001, "source inspection still permits actual zoom rather than forced save-page fit");
            model.ReviewTargetIdentity.ShouldBe(identity);
            InViewport(Find<FrameworkElement>(tree, "Session.OriginalChecks"), tree.Root);
            Reachable(Find<Button>(tree, "Session.Approve"), tree.Root);
            return true;
        });
        var stored = (await h.Repository.LoadAsync(imported.Id, default)).Value!;
        stored.Snapshot.ShouldNotBeNull();
        stored.Reviews.ShouldBeEmpty();
        File.ReadAllBytes(path).ShouldBe(bytes);
        File.ReadAllBytes(h.FileWorkspace.ResolveAbsolute(stored.Revisions.Single(r => r.Id == stored.Snapshot!.RootRevisionId).File)).ShouldBe(bytes);
    }

    private static T Find<T>(RenderedTree tree, string id) where T : FrameworkElement =>
        tree.OfType<T>().Single(e => AutomationProperties.GetAutomationId(e) == id);

    private static void Reachable(FrameworkElement element, FrameworkElement root)
    {
        element.BringIntoView(); root.UpdateLayout();
        InViewport(element, root);
        foreach (ScrollViewer scroll in GuidedSessionShellTests.Ancestors(element).OfType<ScrollViewer>())
        {
            Rect b = element.TransformToAncestor(scroll).TransformBounds(new Rect(element.RenderSize));
            b.Top.ShouldBeGreaterThanOrEqualTo(-0.5);
            b.Bottom.ShouldBeLessThanOrEqualTo(scroll.ViewportHeight + 0.5);
        }
    }

    private static void InViewport(FrameworkElement element, FrameworkElement root)
    {
        Rect b = element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
        b.Left.ShouldBeGreaterThanOrEqualTo(-0.5);
        b.Top.ShouldBeGreaterThanOrEqualTo(-0.5);
        b.Right.ShouldBeLessThanOrEqualTo(root.ActualWidth + 0.5);
        b.Bottom.ShouldBeLessThanOrEqualTo(root.ActualHeight + 0.5);
    }

    private static void Settle(FrameworkElement root)
    {
        root.Dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
        root.UpdateLayout();
    }

    internal static void Capture(Func<UserControl> create, Size size, string name, Action<RenderedTree>? prepare = null)
    {
        string? folder = Environment.GetEnvironmentVariable("PF_U04_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(folder)) return;
        WpfRendering.CapturePng(create, size, Path.Combine(folder, $"{name}-{size.Width:0}x{size.Height:0}.png"), prepare);
    }
}
