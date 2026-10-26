using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Delivery;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Delivery;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class SaveWorkspaceRolloutTests
{
    public static TheoryData<string, int, int> Viewports()
    {
        TheoryData<string, int, int> cases = new();
        foreach (string language in new[] { "zh-CN", "en-US" })
            foreach ((int w, int h) in new[] { (1000, 700), (1280, 720), (1920, 1080) }) cases.Add(language, w, h);
        return cases;
    }

    // Completion without saving is legal: the summary must never imply that completion delivered bytes.
    [Theory]
    [MemberData(nameof(Viewports))]
    public async Task Completed_summary_keeps_unsaved_saved_and_history_facts_separate(string language, int width, int height)
    {
        using OperatorCultureScope culture = new(language);
        foreach (string state in new[] { "unsaved", "saved", "history" })
        {
            using SessionServiceHarness h = new();
            var workflow = FinalSaveFixtures.TiffService(h);
            var review = await FinalSaveFixtures.TiffAtFinalReviewAsync(h, workflow);
            string folder = FinalSaveFixtures.NewFolder("u07-completed");
            try
            {
                var delivery = new ObservedDeliveryService(FinalSaveFixtures.Delivery(h));
                var model = await GuidedCompletionSaveTests.Open(h, workflow, delivery, new GuidedCompletionSaveTests.Picker(folder), review);
                if (state != "unsaved")
                {
                    model.ChangeLocationCommand.Execute(null);
                    await model.ConfirmAndSaveCommand.ExecuteAsync(null);
                }
                else await model.ApproveCommand.ExecuteAsync(null);
                await model.FinalSaveFactsLoaded;
                await model.CompleteCommand.ExecuteAsync(null);
                await model.FinalSaveFactsLoaded; await model.PreviewsLoaded;
                if (state == "history")
                    model = await GuidedCompletionSaveTests.Open(h, workflow, delivery, new GuidedCompletionSaveTests.Picker(folder),
                        (await workflow.LoadAsync(review.Id, default)).Value);
                string before = JsonSerializer.Serialize((await h.Repository.LoadAsync(review.Id, default)).Value);
                WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = model }, new(width, height), tree =>
                {
                    var summary = Find<Border>(tree, "Session.Completion.Summary");
                    Inside(summary, tree.Root);
                    Find<TextBlock>(tree, "Session.Completion.Workflow").Text.ShouldBe(model.GuidedCompletionState);
                    Find<TextBlock>(tree, "Session.Completion.Delivery").Text.ShouldBe(model.GuidedDeliveryState);
                    var count = Find<TextBlock>(tree, "Session.Completion.SavedCount");
                    count.Text.ShouldContain(state == "unsaved" ? "0" : "1");
                    count.Text.ShouldContain(language == "zh-CN" ? "记录" : "record");
                    tree.OfType<TextBox>().Where(GuidedSessionProjectionTests.Shown)
                        .ShouldNotContain(t => AutomationProperties.GetAutomationId(t) == "Session.FinalSave.FileName",
                            "the completed page first presents its outcome, with optional saving in a disclosure");
                    var preview = tree.OfType<SharedReviewSurface>().Single();
                    preview.ResultOnly.ShouldBeTrue();
                    var image = GuidedCompletionSaveTests.Descendants(preview).OfType<Image>().Single();
                    image.Source.ShouldNotBeNull(); image.ActualHeight.ShouldBeGreaterThanOrEqualTo(180);
                    Inside(image, tree.Root);
                    Find<Button>(tree, "Session.AddAnotherSize").Command.ShouldBeSameAs(model.AddAnotherSizeCommand);
                    if (state != "unsaved")
                    {
                        var path = Find<TextBox>(tree, "Session.FinalSave.SavedPath");
                        path.Text.ShouldBe(model.FinalSaveSavedPath); path.IsReadOnly.ShouldBeTrue();
                        path.BringIntoView(); ProcessingWorkspaceRolloutTests.Settle(tree.Root); Inside(path, tree.Root);
                        path.SelectAll(); path.SelectedText.ShouldBe(model.FinalSaveSavedPath);
                    }
                    return true;
                });
                Capture(model, new(width, height), "completed-" + state);
                delivery.DeliverCalls.ShouldBe(state == "unsaved" ? 0 : 1);
                JsonSerializer.Serialize((await h.Repository.LoadAsync(review.Id, default)).Value).ShouldBe(before);
            }
            finally { FinalSaveFixtures.Remove(folder); }
        }
    }

    // A read-only comparison disclosure must not change approval identity, viewport or durable facts.
    [Theory]
    [MemberData(nameof(Viewports))]
    public async Task Png_save_defaults_to_result_and_comparison_is_explicit_and_reversible(string language, int width, int height)
    {
        using OperatorCultureScope culture = new(language);
        using SessionServiceHarness h = new();
        var workflow = h.CreateService();
        var review = await FinalSaveFixtures.PngAtFinalReviewAsync(h, workflow);
        var model = await GuidedCompletionSaveTests.Open(h, workflow, FinalSaveFixtures.Delivery(h), new GuidedCompletionSaveTests.Picker(null), review);
        await model.ApproveCommand.ExecuteAsync(null);
        await model.RunStepCommand.ExecuteAsync(null);
        await model.PreviewsLoaded; await model.FinalSaveFactsLoaded;
        model.GuidedPage.ShouldBe(GuidedSessionPage.Save);
        model.SaveReviewPanes.Select(pane => pane.RevisionId!.Value).ShouldBe(
            new[] { review.UpstreamArtefact!.RevisionId, review.CurrentArtefact!.RevisionId });
        model.SaveReviewPanes[^1].Heading.ShouldBe(model.AfterLabel);
        model.ReviewViewport.HorizontalPosition = .4;
        model.ReviewViewport.VerticalPosition = .6;
        var inspection = (model.ReviewViewport.Mode, model.ReviewViewport.Background, model.ReviewViewport.HorizontalPosition,
            model.ReviewViewport.VerticalPosition, model.ZoomScale, model.IsFitToViewport);
        var before = JsonSerializer.Serialize((await h.Repository.LoadAsync(review.Id, default)).Value);
        WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = model }, new(width, height), tree =>
        {
            var surface = tree.OfType<SharedReviewSurface>().Single();
            GuidedCompletionSaveTests.Descendants(surface).OfType<Image>().Count().ShouldBe(1);
            var toggle = Find<Button>(tree, "Session.Save.ReviewResult");
            Inside(toggle, tree.Root);
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ProcessingWorkspaceRolloutTests.Settle(tree.Root);
            GuidedCompletionSaveTests.Descendants(surface).OfType<Image>().Count().ShouldBe(model.PreviewPanes.Count);
            GuidedCompletionSaveTests.Descendants(surface).OfType<Image>().Max(image => image.ActualWidth * image.ActualHeight)
                .ShouldBeGreaterThanOrEqualTo(10_000, "opening comparison must leave a usable picture area while preserving the reviewed image's aspect ratio");
            GuidedCompletionSaveTests.Descendants(surface).OfType<TextBlock>()
                .ShouldContain(t => t.Text.StartsWith(review.UpstreamArtefact!.FileName + " · "),
                    "the PNG promotion must compare the actual pre-trim input, not two copies of the trimmed result");
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ProcessingWorkspaceRolloutTests.Settle(tree.Root);
            GuidedCompletionSaveTests.Descendants(surface).OfType<Image>().Count().ShouldBe(1);
            var field = Find<TextBox>(tree, "Session.FinalSave.FileName");
            field.ActualHeight.ShouldBeGreaterThanOrEqualTo(42); Inside(field, tree.Root);
            return true;
        });
        (model.ReviewViewport.Mode, model.ReviewViewport.Background, model.ReviewViewport.HorizontalPosition,
            model.ReviewViewport.VerticalPosition, model.ZoomScale, model.IsFitToViewport).ShouldBe(inspection);
        JsonSerializer.Serialize((await h.Repository.LoadAsync(review.Id, default)).Value).ShouldBe(before);
        Capture(model, new(width, height), "png-save");
        Capture(model, new(width, height), "png-reviewed-comparison", tree =>
        {
            Find<Button>(tree, "Session.Save.ReviewResult").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ProcessingWorkspaceRolloutTests.Settle(tree.Root);
        });
    }

    [Theory]
    [MemberData(nameof(Viewports))]
    public async Task Save_adverse_states_remain_readable_with_the_original_qualified_action(string language, int width, int height)
    {
        using OperatorCultureScope culture = new(language);
        foreach (string state in new[] { "failed", "collision", "uncertain", "saved" })
        {
            using SessionServiceHarness h = new();
            var workflow = FinalSaveFixtures.TiffService(h);
            var review = await FinalSaveFixtures.TiffAtFinalReviewAsync(h, workflow);
            string folder = FinalSaveFixtures.NewFolder("u07-adverse");
            try
            {
                var delivery = new ObservedDeliveryService(FinalSaveFixtures.Delivery(h,
                    state == "uncertain" ? new LostPublicationAckFileSystem(new WindowsDeliveryFileSystem()) : null));
                var model = await GuidedCompletionSaveTests.Open(h, workflow, delivery,
                    new GuidedCompletionSaveTests.Picker(state == "failed" ? Path.Combine(folder, "missing") : folder), review);
                model.SaveFileNameText = "result.tif";
                model.ChangeLocationCommand.Execute(null);
                if (state == "failed")
                {
                    await model.ApproveCommand.ExecuteAsync(null);
                    await model.FinalSaveFactsLoaded;
                    model = await GuidedCompletionSaveTests.Open(h, workflow, delivery,
                        new GuidedCompletionSaveTests.Picker(Path.Combine(folder, "missing")),
                        (await workflow.LoadAsync(review.Id, default)).Value);
                    model.SaveFileNameText = "result.tif";
                    model.ChangeLocationCommand.Execute(null);
                }
                if (state == "collision") File.WriteAllBytes(Path.Combine(folder, "result.tif"), [1, 2, 3]);
                if (state == "failed") await model.SaveApprovedCommand.ExecuteAsync(null);
                else await model.ConfirmAndSaveCommand.ExecuteAsync(null);
                await model.FinalSaveFactsLoaded; await model.PreviewsLoaded;
                int calls = delivery.DeliverCalls;
                WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = model }, new(width, height), tree =>
                {
                    var status = Find<TextBlock>(tree, "Session.FinalSave.Status");
                    status.FontSize.ShouldBeGreaterThanOrEqualTo(16);
                    status.BringIntoView(); ProcessingWorkspaceRolloutTests.Settle(tree.Root); Inside(status, tree.Root);
                    if (state == "uncertain")
                    {
                        var warning = Find<Border>(tree, "Session.Save.DeliveryHighlight");
                        var brush = warning.Background.ShouldBeOfType<SolidColorBrush>();
                        brush.Color.A.ShouldBe(byte.MaxValue, "uncertainty must have a persistent highlighted warning");
                        warning.BringIntoView(); ProcessingWorkspaceRolloutTests.Settle(tree.Root);
                        Inside(warning, tree.Root);
                        var check = Find<ReviewApprovalButton>(tree, "Session.FinalSave.CheckAgain");
                        check.Command.ShouldBeSameAs(model.CheckSaveAgainCommand);
                        check.TargetIdentity.ShouldBe(model.RecordedActionIdentity); Inside(check, tree.Root);
                        model.CanOpenContainingFolder.ShouldBeFalse();
                    }
                    var surface = tree.OfType<SharedReviewSurface>().Single();
                    surface.ResultOnly.ShouldBeTrue();
                    GuidedCompletionSaveTests.Descendants(surface).OfType<Image>().Single().ActualHeight.ShouldBeGreaterThanOrEqualTo(180);
                    return true;
                });
                Capture(model, new(width, height), "save-" + state);
                delivery.DeliverCalls.ShouldBe(calls);
                if (state == "collision") File.ReadAllBytes(Path.Combine(folder, "result.tif")).ShouldBe([1, 2, 3]);
            }
            finally { FinalSaveFixtures.Remove(folder); }
        }
    }

    [Theory]
    [MemberData(nameof(Viewports))]
    public async Task Three_size_history_wraps_in_details_without_squeezing_result_preview(string language, int width, int height)
    {
        using OperatorCultureScope culture = new(language);
        using SessionServiceHarness h = new();
        var workflow = FinalSaveFixtures.TiffService(h);
        var view = await FinalSaveFixtures.TiffAtFinalReviewAsync(h, workflow);
        string folder = FinalSaveFixtures.NewFolder("u07-sizes");
        try
        {
            var delivery = FinalSaveFixtures.Delivery(h);
            for (int i = 0; i < 3; i++)
            {
                if (i > 0)
                {
                    (await workflow.ExecuteAsync(view.Id, new WorkflowCommand.AddAnotherSize(), "tester", default)).IsSuccess.ShouldBeTrue();
                    view = await FinalSaveFixtures.NextTiffSizeAsync(workflow, view.Id, 200 + i * 50);
                }
                var saved = await new FinalSaveCoordinator(workflow, delivery, "tester").ConfirmAndSaveAsync(
                    FinalSaveFixtures.Confirm(view, folder, $"size-{i}.tif"), null, default);
                saved.Delivery!.Code.ShouldBe(DeliveryCode.Delivered);
                (await workflow.ExecuteAsync(view.Id, new WorkflowCommand.Complete(), "tester", default)).IsSuccess.ShouldBeTrue();
            }
            var model = await GuidedCompletionSaveTests.Open(h, workflow, delivery, new GuidedCompletionSaveTests.Picker(null),
                (await workflow.LoadAsync(view.Id, default)).Value);
            model.FinalSaveTargets.Count.ShouldBe(3);
            WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = model }, new(width, height), tree =>
            {
                var preview = tree.OfType<SharedReviewSurface>().Single();
                double heightBefore = preview.ActualHeight;
                var rows = tree.OfType<TextBlock>().Where(t => AutomationProperties.GetAutomationId(t) == "Session.OutputState").ToArray();
                rows.Length.ShouldBeGreaterThanOrEqualTo(3);
                foreach (var row in rows)
                {
                    row.TextWrapping.ShouldBe(TextWrapping.Wrap);
                    row.BringIntoView(); ProcessingWorkspaceRolloutTests.Settle(tree.Root); Inside(row, tree.Root);
                }
                preview.ActualHeight.ShouldBe(heightBefore);
                GuidedCompletionSaveTests.Descendants(preview).OfType<Image>().Single().ActualHeight.ShouldBeGreaterThanOrEqualTo(180);
                return true;
            });
            Capture(model, new(width, height), "three-sizes");
        }
        finally { FinalSaveFixtures.Remove(folder); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Completion_facts_do_not_require_a_save_coordinator_or_a_saveable_result(bool abandoned)
    {
        using OperatorCultureScope culture = new("zh-CN");
        using SessionServiceHarness h = new();
        var workflow = FinalSaveFixtures.TiffService(h);
        var view = abandoned
            ? (await workflow.ImportAsync(WorkflowType.GeneratePrintTiff, h.WriteSourcePng(), "abandoned", "tester", default)).Value
            : await FinalSaveFixtures.TiffAtFinalReviewAsync(h, workflow);
        if (!abandoned)
            (await workflow.ApproveExactReviewAsync(view.Id, view.CurrentStep!.Step, view.CurrentArtefact!.RevisionId,
                view.CurrentArtefact.Sha256, "tester", default)).IsSuccess.ShouldBeTrue();
        (await workflow.ExecuteAsync(view.Id, abandoned ? new WorkflowCommand.AbandonSession("operator stopped") : new WorkflowCommand.Complete(), "tester", default)).IsSuccess.ShouldBeTrue();
        var model = new SessionViewModel(workflow, h.Previews, h.TiffReviews, new RecordingNavigation());
        model.Open((await workflow.LoadAsync(view.Id, default)).Value);
        await model.PreviewsLoaded;
        model.HasFinalSave.ShouldBeFalse();
        model.GuidedCurrentTitle.ShouldBe(abandoned ? "任务已放弃" : "任务已完成");
        WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = model }, new(1000, 700), tree =>
        {
            Inside(Find<Border>(tree, "Session.Completion.Summary"), tree.Root);
            Find<TextBlock>(tree, "Session.Completion.Workflow").Text.ShouldBe(abandoned ? "任务已放弃" : "任务已完成");
            if (!abandoned) Find<TextBlock>(tree, "Session.Completion.Approval").Text.ShouldBe("结果已批准");
            tree.OfType<TextBox>().ShouldNotContain(t => AutomationProperties.GetAutomationId(t) == "Session.FinalSave.FileName");
            return true;
        });
        Capture(model, new(1000, 700), abandoned ? "abandoned-no-save-target" : "completed-no-coordinator");
    }

    private static T Find<T>(RenderedTree tree, string id) where T : FrameworkElement =>
        tree.OfType<T>().Single(e => AutomationProperties.GetAutomationId(e) == id);
    private static void Inside(FrameworkElement e, FrameworkElement root)
    {
        GuidedSessionProjectionTests.Shown(e).ShouldBeTrue();
        var b = e.TransformToAncestor(root).TransformBounds(new Rect(e.RenderSize));
        b.Left.ShouldBeGreaterThanOrEqualTo(-.5); b.Top.ShouldBeGreaterThanOrEqualTo(-.5);
        b.Right.ShouldBeLessThanOrEqualTo(root.ActualWidth + .5); b.Bottom.ShouldBeLessThanOrEqualTo(root.ActualHeight + .5);
    }
    private static void Capture(SessionViewModel model, Size size, string state, Action<RenderedTree>? prepare = null)
    {
        string? folder = Environment.GetEnvironmentVariable("PF_U07_CAPTURE_DIR");
        if (!string.IsNullOrWhiteSpace(folder)) WpfRendering.CapturePng(() => new SessionScreenView { DataContext = model }, size,
            Path.Combine(folder, $"{state}-{PrintFlow.App.Resources.OperatorCulture.Current.Name}-{size.Width}x{size.Height}.png"), prepare);
    }
}
