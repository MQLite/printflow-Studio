using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Settings;
using PrintFlow.Domain.Trimming;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using static PrintFlow.Tests.Integration.Persistence.KeepOriginalExtentPersistenceTests;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class GuidedProcessingCropTests
{
    [Fact]
    public async Task Alpha_crop_uses_the_configured_safety_margin_on_the_reviewable_png()
    {
        using SessionServiceHarness h = new();
        await h.Settings.UpsertAsync([SettingEntry.Integer(SettingKey.TrimSafetyMarginPixels, 2)], default);
        var service = h.CreateService();
        var id = await AtTrim(h, service, WorkflowType.PrepareAsset);
        var model = new SessionViewModel(service, h.Previews, h.TiffReviews, new RecordingNavigation());
        model.Open((await service.LoadAsync(id, default)).Value);

        model.ShowsGuidedTrimParameters.ShouldBeTrue();
        model.RecommendedCommand.ShouldBeSameAs(model.AdvanceImageProcessingCommand);
        await model.AdvanceImageProcessingCommand.ExecuteAsync(null);

        var review = (await service.LoadAsync(id, default)).Value;
        review.CurrentStep!.State.ShouldBe(StepState.ReviewRequired);
        review.CurrentTrimGeometry!.ContentBounds.ShouldBe(TrimBounds.FromEdges(3, 2, 8, 7));
        review.CurrentTrimGeometry.AppliedBounds.ShouldBe(TrimBounds.FromEdges(1, 0, 10, 9));
        review.CurrentArtefact!.Facts.Format.ShouldBe(ImageFormat.Png);
        review.CurrentArtefact.Facts.PixelWidth.ShouldBe(9);
        review.CurrentArtefact.Facts.PixelHeight.ShouldBe(9);
    }

    [Theory]
    [InlineData(ImageProcessingMethod.ManualCrop)]
    [InlineData(ImageProcessingMethod.KeepCanvas)]
    public async Task Advancing_uses_the_selected_crop_method(ImageProcessingMethod method)
    {
        using SessionServiceHarness h = new();
        var service = h.CreateService();
        var id = await AtTrim(h, service, WorkflowType.PrepareAsset, opaque: true);
        var model = new SessionViewModel(service, h.Previews, h.TiffReviews, new RecordingNavigation());
        model.Open((await service.LoadAsync(id, default)).Value);
        await model.PreviewsLoaded;
        model.DraftImageProcessingMethod = method;
        model.GuidedPage.ShouldBe(GuidedSessionPage.Ready);
        model.ShowsGuidedRunStep.ShouldBeTrue();
        model.ShowsGuidedTrimParameters.ShouldBeFalse();
        WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = model },
            WpfRendering.ReviewViewport, tree =>
            {
                var action = GuidedSessionShellTests.Find<Button>(tree, "Session.RunStep");
                GuidedSessionProjectionTests.Shown(action).ShouldBeTrue();
                action.IsEnabled.ShouldBeTrue();
                action.Command.ShouldBeSameAs(model.RecommendedCommand);
                Task completed = action.Dispatcher.InvokeAsync(async () =>
                {
                    await model.AdvanceImageProcessingCommand.ExecuteAsync(null);
                    if (method == ImageProcessingMethod.ManualCrop)
                    {
                        model.GuidedPage.ShouldBe(GuidedSessionPage.Crop);
                        model.CropPane!.SourcePixelWidth.ShouldBe(12);
                        model.CanApplyManualCrop.ShouldBeFalse();
                        model.TrySetCropSelection(new CropSurfaceLayout(12, 10, 12, 10, 12, 10, true, 1),
                            3, 2, 9, 7).ShouldBeTrue();
                        await model.ApplyManualCropCommand.ExecuteAsync(null);
                        model.GuidedPage.ShouldBe(GuidedSessionPage.Review);
                        model.CanApprove.ShouldBeTrue();
                    }
                    else
                    {
                        model.GuidedPage.ShouldBe(GuidedSessionPage.Save);
                    }
                }).Task.Unwrap().WaitAsync(TimeSpan.FromSeconds(20));
                DispatcherFrame frame = new();
                _ = completed.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
                Dispatcher.PushFrame(frame);
                completed.GetAwaiter().GetResult();
                return 0;
            });
    }

    [Theory]
    [InlineData(1000, 700)]
    [InlineData(1280, 720)]
    public async Task Crop_uses_the_whole_current_workspace_without_unrelated_forms(int width, int height)
    {
        using OperatorCultureScope culture = new("zh-CN");
        using SessionServiceHarness h = new();
        var model = (await ReviewGuidanceSetup.OpenAtAsync(h, "trim")).Screen;
        model.BeginTrimAdjustCommand.Execute(null);
        var result = WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = model }, new Size(width, height), tree =>
        {
            var workspace = GuidedSessionShellTests.Find<Grid>(tree, "Session.CurrentWorkspace");
            var surface = GuidedSessionShellTests.Find<Canvas>(tree, "Session.ManualCropSurface");
            var imageColumn = GuidedSessionShellTests.Ancestors(surface).OfType<Grid>().First(g => ReferenceEquals(g.Parent, workspace));
            return new
            {
                ImageShare = imageColumn.ActualWidth / workspace.ActualWidth,
                Technical = GuidedSessionProjectionTests.Shown(GuidedSessionShellTests.Find<FrameworkElement>(tree, "Session.TechnicalDetails")),
                Save = tree.OfType<TextBox>().Where(t => AutomationProperties.GetAutomationId(t) == "Session.FinalSave.FileName").Any(GuidedSessionProjectionTests.Shown),
                DuplicateApply = GuidedSessionProjectionTests.Shown(GuidedSessionShellTests.Find<Button>(tree, "Session.ApplyManualCrop")),
                Handles = tree.OfType<Thumb>().Count(t => AutomationProperties.GetAutomationId(t).StartsWith("Session.TrimAdjust.Handle.") && GuidedSessionProjectionTests.Shown(t)),
                Actions = new[] { "Session.TrimAdjust.Restore", "Session.TrimAdjust.Cancel", "Session.TrimAdjust.CompareToggle", "Session.TrimAdjust.Use" }
                    .Select(id => GuidedSessionShellTests.Find<Button>(tree, id)).Select(b => GuidedSessionProjectionTests.Shown(b) && b.ActualHeight > 0).ToArray(),
            };
        });
        result.Facts.ImageShare.ShouldBeGreaterThan(0.95, "crop owns the workspace instead of leaving a column of unrelated forms");
        result.Facts.Technical.ShouldBeFalse();
        result.Facts.Save.ShouldBeFalse();
        result.Facts.DuplicateApply.ShouldBeFalse("adjusting trim has one apply action");
        result.Facts.Handles.ShouldBe(8);
        result.Facts.Actions.ShouldAllBe(a => a);
    }

    [Fact]
    public async Task Cancel_preserves_exact_review_and_using_changed_crop_requires_a_new_review()
    {
        using SessionServiceHarness h = new();
        var opened = await ReviewGuidanceSetup.OpenAtAsync(h, "trim");
        var model = opened.Screen;
        var original = model.ReviewTargetIdentity;
        var before = (await h.Repository.LoadAsync(model.Id, default)).Value!;
        model.BeginTrimAdjustCommand.Execute(null);
        var suggestion = model.CropSelection;
        model.TryNudgeTrimHandle(CropHandle.Left, 1, 0, false, false).ShouldBeTrue();
        model.RestoreAutomaticSuggestionCommand.Execute(null);
        model.CropSelection.ShouldBe(suggestion);
        model.TryNudgeTrimHandle(CropHandle.Left, 1, 0, false, false).ShouldBeTrue();
        model.CancelTrimAdjustCommand.Execute(null);
        model.ReviewTargetIdentity.ShouldBe(original);
        var cancelled = (await h.Repository.LoadAsync(model.Id, default)).Value!;
        cancelled.Reviews.ShouldBe(before.Reviews);
        cancelled.Revisions.ShouldBe(before.Revisions);
        model.BeginTrimAdjustCommand.Execute(null);
        model.TryNudgeTrimHandle(CropHandle.Left, 1, 0, false, false).ShouldBeTrue();
        await model.UseThisTrimCommand.ExecuteAsync(null);
        model.GuidedPage.ShouldBe(GuidedSessionPage.Review);
        model.ReviewTargetIdentity.ShouldNotBe(original);
        model.CanApprove.ShouldBeTrue();
        var applied = (await h.Repository.LoadAsync(model.Id, default)).Value!;
        applied.Reviews.ShouldBe(before.Reviews);
        applied.Revisions.Count.ShouldBe(before.Revisions.Count + 1);
    }
}
