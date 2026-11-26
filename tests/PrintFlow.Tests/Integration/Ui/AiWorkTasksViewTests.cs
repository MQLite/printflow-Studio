using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using PrintFlow.App.Navigation;
using PrintFlow.App.ViewModels.AiDesign;
using PrintFlow.App.Views.AiDesign;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Tests.Integration.AiDesign.Authoring;
using PrintFlow.Workflow.AiDesign;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class AiWorkTasksViewTests
{
    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en")]
    public async Task Ai_tasks_filters_and_row_actions_match_actual_task_state(string language)
    {
        using var culture = new OperatorCultureScope(language);
        using var app = new TempApplication();
        var query = new WorkResultCardTests.TestQuery();
        using var services = AuthoringGraphTests.Compose(app, new(), configure: s => s.AddSingleton<IWorkTaskQuery>(query));
        var nav = services.GetRequiredService<INavigationService>();
        var ai = services.GetRequiredService<IAiDesignNavigation>();
        var tracker = services.GetRequiredService<WorkTaskTracker>();
        var imported = WorkResultCardTests.TaskFor(null, WorkTaskStage.Imported);
        var saved = await WorkResultNavigationTests.ImportAsync(app, services, imported.RequestId);
        await nav.GoHomeAsync(default);
        await ai.GoToPromptWorkbenchAsync(null, default);
        var draft = nav.Current.ShouldBeOfType<AiPromptWorkbenchViewModel>().Draft!.Id;
        var now = DateTimeOffset.UtcNow;
        var pending = WorkResultCardTests.TaskFor(draft, WorkTaskStage.AwaitingVerification) with
            { UpdatedAtUtc = now, WorkConversationId = Guid.NewGuid() };
        var failed = WorkResultCardTests.TaskFor(draft, WorkTaskStage.NeedsAttention) with
            { UpdatedAtUtc = now.AddMinutes(-2), Failure = WorkTaskFailure.UploadFailed };
        imported = imported with { UpdatedAtUtc = now.AddMinutes(-1), Title = null, ImportedSessionId = saved.Id };
        query.Items = [failed, imported, pending];
        await ai.GoToAiWorkTasksAsync(draft, default);
        var page = nav.Current.ShouldBeOfType<AiWorkTasksViewModel>();
        await page.ThumbnailsLoaded;
        page.Rows.Select(x => x.Snapshot.RequestId).ShouldBe(new[] { pending.RequestId, imported.RequestId, failed.RequestId });
        page.Rows[0].CanContinue.ShouldBeFalse();
        page.Rows[0].CanViewWork.ShouldBeTrue();
        page.Rows[1].CanContinue.ShouldBeTrue();
        page.Rows[1].Title.ShouldBe(language == "en" ? "AI task" : "AI 任务");
        page.Rows[2].Problem.ShouldBe(language == "en"
            ? "Work did not confirm all required attachments. The prompt and attachments are saved in Studio."
            : PrintFlow.App.Resources.Strings.Resolve("AiPrompt_WorkUploadFailed"));

        var rendered = WpfRendering.Render(() => new AiWorkTasksView { DataContext = page }, new Size(1000, 700), visual =>
        {
            var open = visual.OfType<Button>().Single(x => AutomationProperties.GetAutomationId(x) == "AiWorkTasks.Continue" && x.Visibility == Visibility.Visible);
            var bounds = open.TransformToAncestor(visual.Root).TransformBounds(new Rect(open.RenderSize));
            return bounds.Right <= 1000 && bounds.Bottom <= 700 && open.IsTabStop && open.Focusable;
        });
        rendered.BindingErrors.ShouldBeEmpty();
        rendered.Facts.ShouldBeTrue();
        if (Environment.GetEnvironmentVariable("PRINTFLOW_WORK_UI_CAPTURE") is { Length: > 0 } directory)
            WpfRendering.CapturePng(() => new AiWorkTasksView { DataContext = page }, new Size(1000, 700),
                Path.Combine(directory, $"{language}-work-tasks.png"));

        page.Filter = WorkTaskFilter.Active;
        page.Rows.ShouldHaveSingleItem().Snapshot.ShouldBe(pending);
        page.Filter = WorkTaskFilter.NeedsAttention;
        page.Rows.ShouldHaveSingleItem().Snapshot.ShouldBe(failed);
        var portrait = imported with { Stage = WorkTaskStage.PortraitReview, PortraitReviewReason = "Compare the smile." };
        query.Items = [portrait, failed];
        tracker.RequestChanged(portrait.RequestId);
        await page.Refresh;
        page.Rows.Select(row => row.Snapshot).ShouldBe(new[] { portrait, failed });
        page.Rows[0].Problem.ShouldBe(portrait.PortraitReviewReason);
        query.Items = [];
        tracker.RequestChanged(pending.RequestId);
        await page.Refresh;
        page.Rows.ShouldBeEmpty();
        page.EmptyMessage.ShouldNotBeNullOrEmpty();
        query.Fail = true;
        tracker.RequestChanged(failed.RequestId);
        await page.Refresh;
        page.Error.ShouldNotBeNullOrEmpty();

        query.Fail = false;
        await page.ReturnWorkbenchCommand.ExecuteAsync(null);
        nav.Current.ShouldBeOfType<AiPromptWorkbenchViewModel>().Draft!.Id.ShouldBe(draft);
        query.Items = [portrait];
        await ai.GoToAiWorkTasksAsync(draft, default);
        page = nav.Current.ShouldBeOfType<AiWorkTasksViewModel>();
        await page.ContinueCommand.ExecuteAsync(page.Rows.ShouldHaveSingleItem());
        nav.Current.ShouldBeOfType<PrintFlow.App.ViewModels.SessionViewModel>().Id.ShouldBe(saved.Id);
    }
}
