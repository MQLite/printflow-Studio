using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using PrintFlow.App.Resources;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Delivery;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class GuidedCompletionRecentTests
{
    [Theory]
    [InlineData("zh-CN", "已停止", "未识别的流程", "未识别的步骤")]
    [InlineData("en", "Stopped", "Unrecognized workflow", "Unrecognized step")]
    public async Task Recent_interrupted_and_unknown_states_use_operator_language(string language, string stopped, string workflow, string step)
    {
        using OperatorCultureScope culture = new(language);
        using HomeScreenHarness h = new();
        var model = await GuidedSessionProjectionTests.Open(h, WorkflowType.PrepareAsset);
        var item = (await h.Sessions.ListRecentAsync(default)).Value.Single();
        new RecentSessionRow(item with { CurrentStepState = StepState.Interrupted }).State.ShouldContain(stopped);
        var unknown = new RecentSessionRow(item with { WorkflowType = (WorkflowType)999, CurrentStep = (StepKind)999, State = (SessionState)999 });
        unknown.Workflow.ShouldBe(workflow); unknown.CurrentStep.ShouldBe(step);
        unknown.State.ShouldBe(Strings.Home_RecentStatusUnknown);
        unknown.MetadataText.ShouldNotContain("999");
        model.CanApprove.ShouldBeFalse();
    }

    [Fact]
    public async Task Recent_saved_history_does_not_claim_present_files()
    {
        using OperatorCultureScope culture = new("zh-CN");
        using HomeScreenHarness h = TiffFinalReviewFixture.Harness(out _);
        var review = await FinalSaveFixtures.PngAtFinalReviewAsync(h.Inner, h.Sessions);
        string folder = FinalSaveFixtures.NewFolder("e5-recent-history");
        try
        {
            var coordinator = new FinalSaveCoordinator(h.Sessions, FinalSaveFixtures.Delivery(h.Inner), "tester");
            var saved = await coordinator.ConfirmAndSaveAsync(FinalSaveFixtures.Confirm(review, folder, "history.png"), null, default);
            saved.Delivery!.Code.ShouldBe(DeliveryCode.Delivered);
            File.Delete(Path.Combine(folder, "history.png"));
            await h.Home.RefreshCommand.ExecuteAsync(null); await h.Home.ThumbnailsLoaded;
            var row = h.Home.RecentSessions.Single();
            row.SaveHistoryText.ShouldBe(Strings.Home_RecentPngSavedPreviously);
            var rendered = WpfRendering.RenderExpectingNoBindingErrors(() => new HomeView { DataContext = h.Home },
                new Size(1000, 700), tree => tree.OfType<TextBlock>()
                    .Where(t => AutomationProperties.GetAutomationId(t) == "Home.RecentNext")
                    .Select(t => t.Text).ToArray());
            rendered.Facts.ShouldContain(t => t.Contains("当前文件"));
            row.CanContinueProcessing.ShouldBeTrue("saving did not complete the task");
        }
        finally { FinalSaveFixtures.Remove(folder); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Recent_resume_projects_real_guided_page_and_history_is_read_only(bool completed)
    {
        using OperatorCultureScope culture = new("zh-CN");
        using HomeScreenHarness h = TiffFinalReviewFixture.Harness(out _);
        var review = await TiffFinalReviewFixture.ReviewRequiredAsync(h, "e5-continue.png");
        if (completed)
        {
            await review.Screen.ApproveCommand.ExecuteAsync(null);
            await review.Screen.CompleteCommand.ExecuteAsync(null);
        }
        string before = JsonSerializer.Serialize(await review.ReloadAsync());
        await h.Home.RefreshCommand.ExecuteAsync(null);
        var row = h.Home.RecentSessions.Single();
        row.CanContinueProcessing.ShouldBe(!completed);
        row.OpenActionLabel.ShouldBe(completed ? Strings.Home_Details : Strings.Home_Resume);
        await h.Home.ResumeCommand.ExecuteAsync(row);
        var opened = h.Session(new RecordingNavigation()); opened.Open(h.Navigation.SessionFor!); await opened.PreviewsLoaded;
        opened.GuidedPage.ShouldBe(completed ? GuidedSessionPage.Completed : GuidedSessionPage.Review);
        opened.IsReadOnly.ShouldBe(completed);
        if (completed) { opened.CanApprove.ShouldBeFalse(); opened.CanRunStep.ShouldBeFalse(); }
        JsonSerializer.Serialize(await review.ReloadAsync()).ShouldBe(before);
    }
}
