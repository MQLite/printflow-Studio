using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PrintFlow.App.Startup;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Startup;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class WpfUiSamplePageTests
{
    [Theory]
    [InlineData(1000, 700)]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    public async Task Home_import_is_prominent_without_losing_resume_or_environment(int width, int height)
    {
        using OperatorCultureScope culture = new("zh-CN");
        using HomeScreenHarness h = new();
        h.StartupStatus.Publish(StartupStatus.Started(true, StartupRecoveryReport.Empty));
        await h.Sessions.ImportAsync(WorkflowType.PrepareAsset, h.Inner.WriteBorderedSourcePng("sample.png"), "样板任务", "tester", default);
        await h.Home.RefreshCommand.ExecuteAsync(null);
        await h.Home.ThumbnailsLoaded;
        WpfRendering.RenderExpectingNoBindingErrors(() => new HomeView { DataContext = h.Home }, new(width, height), tree =>
        {
            var choose = Find<Button>(tree, "Home.ChooseFile");
            Rect box = Bounds(choose, tree.Root);
            box.Width.ShouldBeGreaterThanOrEqualTo(170);
            box.Left.ShouldBeGreaterThan(width * .35, "the import action belongs in the central drop area");
            VisibleInside(choose, tree.Root, width, height);
            VisibleInside(Find<Button>(tree, "Home.ResumeSession"), tree.Root, width, height);
            VisibleInside(Find<Button>(tree, "Home.ShowEnvironment"), tree.Root, width, height);
            choose.Command.ShouldBeSameAs(h.Home.ChooseFileCommand);
            return true;
        });
    }

    [Theory]
    [InlineData(1000, 700)]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    public async Task Save_keeps_guarded_primary_action_at_right_and_destination_readable(int width, int height)
    {
        using OperatorCultureScope culture = new("zh-CN");
        using SessionServiceHarness h = new();
        var workflow = FinalSaveFixtures.TiffService(h);
        var review = await FinalSaveFixtures.TiffAtFinalReviewAsync(h, workflow);
        string folder = FinalSaveFixtures.NewFolder("wpf-ui-layout");
        try
        {
        var model = await GuidedCompletionSaveTests.Open(h, workflow, FinalSaveFixtures.Delivery(h), new GuidedCompletionSaveTests.Picker(folder), review);
        await model.ApproveCommand.ExecuteAsync(null);
        await model.PreviewsLoaded; await model.FinalSaveFactsLoaded;
        model.ChangeLocationCommand.Execute(null);
        model.CanSaveApproved.ShouldBeTrue();
        model.GuidedPage.ShouldBe(GuidedSessionPage.Save);
        WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = model }, new(width, height), tree =>
        {
            try
            {
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.SystemIdle);
                tree.Root.UpdateLayout();
                var action = Find<ReviewApprovalButton>(tree, "Session.FinalSave.SaveApproved");
                action.Command.ShouldBeSameAs(model.SaveApprovedCommand);
                action.TargetIdentity.ShouldBe(model.SaveApprovedIdentity);
                Bounds(action, tree.Root).Left.ShouldBeGreaterThan(width * .7);
                VisibleInside(action, tree.Root, width, height);
                var name = Find<TextBox>(tree, "Session.FinalSave.FileName");
                name.ActualHeight.ShouldBeGreaterThanOrEqualTo(42);
                name.Text.ShouldBe(model.SaveFileNameText);
                VisibleInside(name, tree.Root, width, height);
                VisibleInside(Find<TextBox>(tree, "Session.FinalSave.Folder"), tree.Root, width, height);
                tree.OfType<Image>().Where(i => i.Source is not null && GuidedSessionProjectionTests.Shown(i))
                    .Max(i => i.ActualHeight).ShouldBeGreaterThan(180);
                var surface = tree.OfType<SharedReviewSurface>().Single();
                var comparison = model.ReviewViewport.Mode;
                var inspection = (model.ReviewViewport.Background, model.ReviewViewport.HorizontalPosition,
                    model.ReviewViewport.VerticalPosition, model.ZoomScale, model.IsFitToViewport);
                surface.ResultOnly.ShouldBeTrue();
                GuidedCompletionSaveTests.Descendants(surface).OfType<Image>().Count().ShouldBe(1);
                surface.ResultOnly = false;
                tree.Root.UpdateLayout();
                GuidedCompletionSaveTests.Descendants(surface).OfType<Image>().Count().ShouldBe(model.PreviewPanes.Count);
                model.ReviewViewport.Mode.ShouldBe(comparison, "save presentation must not mutate the review comparison state");
                (model.ReviewViewport.Background, model.ReviewViewport.HorizontalPosition,
                    model.ReviewViewport.VerticalPosition, model.ZoomScale, model.IsFitToViewport).ShouldBe(inspection);
                surface.ResultOnly = true;
                model.GuidedCompletionState.ShouldBe("任务尚未完成");
                return true;
            }
            finally { tree.Root.DataContext = null; tree.Root.UpdateLayout(); }
        });
        }
        finally { FinalSaveFixtures.Remove(folder); }
    }

    private static T Find<T>(RenderedTree tree, string id) where T : FrameworkElement =>
        tree.OfType<T>().Single(e => AutomationProperties.GetAutomationId(e) == id);
    private static Rect Bounds(FrameworkElement e, Visual root) => e.TransformToAncestor(root).TransformBounds(new Rect(e.RenderSize));
    private static void VisibleInside(FrameworkElement e, Visual root, int width, int height)
    {
        GuidedSessionProjectionTests.Shown(e).ShouldBeTrue();
        var b = Bounds(e, root);
        b.Left.ShouldBeGreaterThanOrEqualTo(0); b.Top.ShouldBeGreaterThanOrEqualTo(0);
        b.Right.ShouldBeLessThanOrEqualTo(width + .5); b.Bottom.ShouldBeLessThanOrEqualTo(height + .5);
        for (DependencyObject? p = VisualTreeHelper.GetParent(e); p is not null; p = VisualTreeHelper.GetParent(p))
            if (p is ScrollViewer scroll)
            {
                var within = Bounds(e, scroll);
                within.Top.ShouldBeGreaterThanOrEqualTo(-.5);
                within.Bottom.ShouldBeLessThanOrEqualTo(scroll.ViewportHeight + .5);
            }
    }
}
