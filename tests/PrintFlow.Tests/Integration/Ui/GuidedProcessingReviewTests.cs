using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using PrintFlow.App.Controls;
using PrintFlow.App.Views;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class GuidedProcessingReviewTests
{
    [Fact]
    public async Task Source_keeps_the_accepted_proportional_workspace_instead_of_review_only_geometry()
    {
        using HomeScreenHarness h = new();
        var model = await GuidedSessionProjectionTests.Open(h, WorkflowType.PrepareAsset);
        var result = WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = model }, new Size(1280, 720), tree =>
        {
            var workspace = GuidedSessionShellTests.Find<Grid>(tree, "Session.CurrentWorkspace");
            return workspace.ColumnDefinitions[1].ActualWidth / workspace.ActualWidth;
        });
        result.Facts.ShouldBe(0.4, 0.001, "the E4 review sizing must not shrink unrelated accepted source forms");
    }

    [Theory]
    [InlineData("enhancement")]
    [InlineData("background")]
    [InlineData("trim")]
    [InlineData("tiff")]
    public async Task Common_review_reserves_the_majority_for_the_image_and_exposes_pending_final_save_fields(string step)
    {
        using OperatorCultureScope culture = new("zh-CN");
        using SessionServiceHarness h = new();
        var model = (await ReviewGuidanceSetup.OpenAtAsync(h, step)).Screen;
        var identity = model.ReviewTargetIdentity;
        var result = WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = model }, new Size(1280, 720), tree =>
        {
            var workspace = GuidedSessionShellTests.Find<Grid>(tree, "Session.CurrentWorkspace");
            var guidance = GuidedSessionShellTests.Find<FrameworkElement>(tree, "Session.ReviewGuidance");
            var approve = GuidedSessionShellTests.Find<ReviewApprovalButton>(tree, "Session.Approve");
            return new
            {
                ImageShare = workspace.ColumnDefinitions[0].ActualWidth / workspace.ActualWidth,
                Checks = model.ReviewGuidanceChecks.Count,
                Guidance = GuidedSessionProjectionTests.Shown(guidance),
                SaveDraftShown = tree.OfType<TextBox>().Where(t => AutomationProperties.GetAutomationId(t) == "Session.FinalSave.FileName").Any(GuidedSessionProjectionTests.Shown),
                approve.TargetIdentity,
                Approve = GuidedSessionProjectionTests.Shown(approve),
            };
        });
        result.Facts.ImageShare.ShouldBeGreaterThan(0.65, "the exact result must dominate the current workspace");
        result.Facts.Checks.ShouldBeInRange(2, 4);
        result.Facts.Guidance.ShouldBeTrue();
        result.Facts.SaveDraftShown.ShouldBe(step is "trim" or "tiff",
            "only pending final reviews expose the filename and destination before approval");
        result.Facts.TargetIdentity.ShouldBe(identity);
        result.Facts.Approve.ShouldBeTrue();
        model.ReviewGuidanceApprove.ShouldContain("不会把文件保存");
    }

    [Fact]
    public async Task Collapsing_the_pending_save_form_restores_full_height_review_guidance()
    {
        using SessionServiceHarness h = new();
        var model = (await ReviewGuidanceSetup.OpenAtAsync(h, "tiff")).Screen;
        var result = WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = model },
            WpfRendering.ReviewViewport, tree =>
            {
                var guidance = tree.OfType<ReviewGuidanceScrollViewer>().Single();
                var save = GuidedSessionShellTests.Find<Expander>(tree, "Session.FinalSave.Options");
                bool scrollableWhileExpanded = guidance.IsTabStop && guidance.ScrollableHeight > 0;

                save.SetCurrentValue(Expander.IsExpandedProperty, false);
                tree.Root.UpdateLayout();
                bool guidanceExpands = !guidance.IsTabStop && double.IsPositiveInfinity(guidance.MaxHeight);

                save.SetCurrentValue(Expander.IsExpandedProperty, true);
                tree.Root.UpdateLayout();
                bool canScrollAgain = guidance.IsTabStop && guidance.ScrollableHeight > 0;
                return (scrollableWhileExpanded, guidanceExpands, canScrollAgain);
            });

        result.Facts.ShouldBe((true, true, true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Single_or_missing_preview_does_not_offer_comparison(bool missing)
    {
        using HomeScreenHarness h = new();
        var model = await GuidedSessionProjectionTests.Open(h, WorkflowType.PrepareAsset);
        var result = WpfRendering.RenderExpectingNoBindingErrors(() =>
        {
            if (missing) model.PreviewPanes.Clear();
            return new SessionScreenView { DataContext = model };
        }, new Size(1000, 700), tree => new
        {
            Comparison = tree.OfType<RadioButton>().Where(b => AutomationProperties.GetAutomationId(b).StartsWith("Session.ReviewMode")).Any(GuidedSessionProjectionTests.Shown),
            Background = tree.OfType<RadioButton>().Where(b => AutomationProperties.GetAutomationId(b).StartsWith("Session.ReviewBackground")).Any(GuidedSessionProjectionTests.Shown),
        });
        result.Facts.Comparison.ShouldBeFalse("a before/after tool requires two available previews");
        if (missing) result.Facts.Background.ShouldBeFalse();
    }
}
