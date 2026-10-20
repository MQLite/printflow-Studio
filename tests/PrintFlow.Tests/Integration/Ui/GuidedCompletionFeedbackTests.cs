using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Delivery;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class GuidedCompletionFeedbackTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failure_keeps_preservation_qualified_by_successful_or_failed_reload(bool stale)
    {
        using OperatorCultureScope culture = new("zh-CN");
        using SessionServiceHarness h = new();
        var real = h.CreateService();
        var imported = (await real.ImportAsync(WorkflowType.PrepareAsset, h.WriteSourcePng("feedback.png"), null, "tester", default)).Value;
        var before = (await h.Repository.LoadAsync(imported.Id, default)).Value!;
        var service = new ScriptedFailureSessionService(real)
        {
            NextExecuteFailure = OperationFailure.Create(FailureCode.WorkspaceError, "Synthetic local failure"),
            FailLoadsAfterScriptedFailure = stale,
        };
        var model = new SessionViewModel(service, h.Previews, h.TiffReviews, new RecordingNavigation());
        model.Open(imported);
        await model.ConfirmOriginalCommand.ExecuteAsync(null);
        model.NoticeErrorCode.ShouldBe(nameof(FailureCode.WorkspaceError));
        var facts = ReadSession(model);
        facts.GetValueOrDefault("Session.ActionPreserved", string.Empty)
            .ShouldContain(stale ? "尚未重新核实" : "保留规则");
        model.Notice!.ShouldNotContain("Synthetic local failure");
        if (stale) model.Notice!.ShouldNotContain("重试");
        var after = (await h.Repository.LoadAsync(imported.Id, default)).Value!;
        after.Steps.ShouldBe(before.Steps); after.Attempts.ShouldBe(before.Attempts);
        after.Reviews.ShouldBe(before.Reviews); after.Revisions.ShouldBe(before.Revisions);
    }

    [Fact]
    public async Task Refreshed_draft_confirmation_is_inert_without_discarding_supported_draft()
    {
        using HomeScreenHarness h = new();
        var navigation = new RecordingNavigation();
        var imported = (await h.Sessions.ImportAsync(WorkflowType.GeneratePrintTiff, h.WriteSourceFile("draft-refresh.png"), null, "tester", default)).Value;
        var model = h.Session(navigation); model.Open(imported);
        await model.ConfirmOriginalCommand.ExecuteAsync(null);
        model.WidthMmText = "317"; model.HeightMmText = "211";
        await model.BackToHomeCommand.ExecuteAsync(null);
        model.IsConfirmingDraftLeave.ShouldBeTrue();
        var before = (await h.Inner.Repository.LoadAsync(model.Id, default)).Value!;
        model.Open((await h.Sessions.LoadAsync(model.Id, default)).Value);
        await model.DiscardDraftAndLeaveCommand.ExecuteAsync(null);
        navigation.GoHomeCount.ShouldBe(0, "refresh retires the old confirmation callback");
        model.IsConfirmingDraftLeave.ShouldBeFalse();
        model.WidthMmText.ShouldBe("317"); model.HeightMmText.ShouldBe("211");
        var after = (await h.Inner.Repository.LoadAsync(model.Id, default)).Value!;
        after.Steps.ShouldBe(before.Steps); after.Reviews.ShouldBe(before.Reviews); after.Revisions.ShouldBe(before.Revisions);
    }

    [Fact]
    public async Task Refreshed_return_confirmation_cannot_invalidate_results()
    {
        using HomeScreenHarness h = new();
        var model = await GuidedSessionProjectionTests.Open(h, WorkflowType.PrepareAsset);
        await model.ConfirmOriginalCommand.ExecuteAsync(null);
        await model.SkipCommand.ExecuteAsync(null); await model.SkipCommand.ExecuteAsync(null);
        model.SelectedReturnTarget = model.ReturnTargets.First();
        model.BeginReturnCommand.Execute(null);
        model.IsConfirmingReturn.ShouldBeTrue();
        var before = (await h.Inner.Repository.LoadAsync(model.Id, default)).Value!;
        model.Open((await h.Sessions.LoadAsync(model.Id, default)).Value);
        await model.ConfirmReturnCommand.ExecuteAsync(null);
        model.IsConfirmingReturn.ShouldBeFalse();
        var after = (await h.Inner.Repository.LoadAsync(model.Id, default)).Value!;
        after.Steps.ShouldBe(before.Steps); after.Reviews.ShouldBe(before.Reviews); after.Revisions.ShouldBe(before.Revisions);
    }

    [Fact]
    public async Task Uncertain_save_and_unavailable_history_remain_separate_visible_facts()
    {
        using OperatorCultureScope culture = new("zh-CN");
        using SessionServiceHarness h = new();
        var workflow = FinalSaveFixtures.TiffService(h);
        var review = await FinalSaveFixtures.TiffAtFinalReviewAsync(h, workflow);
        string folder = FinalSaveFixtures.NewFolder("e5-multiple-facts");
        try
        {
            var delivery = new ObservedDeliveryService(FinalSaveFixtures.Delivery(h,
                new LostPublicationAckFileSystem(new WindowsDeliveryFileSystem())));
            var model = await GuidedCompletionSaveTests.Open(h, workflow, delivery, new GuidedCompletionSaveTests.Picker(folder), review);
            model.ChangeLocationCommand.Execute(null);
            await model.ConfirmAndSaveCommand.ExecuteAsync(null);
            await model.FinalSaveFactsLoaded;
            delivery.FailHistory = true;
            model.Open((await workflow.LoadAsync(model.Id, default)).Value);
            await model.FinalSaveFactsLoaded;
            var facts = ReadSession(model);
            facts.GetValueOrDefault("Session.Completion.Delivery", string.Empty).ShouldBe("保存结果待核实");
            facts.GetValueOrDefault("Session.FinalSave.Secondary", string.Empty).ShouldContain("保存记录");
            model.CanCheckSaveAgain.ShouldBeTrue(); model.CanOpenContainingFolder.ShouldBeFalse();
            delivery.DeliverCalls.ShouldBe(1); delivery.ReconcileCalls.ShouldBe(0);
        }
        finally { FinalSaveFixtures.Remove(folder); }
    }

    [Fact]
    public async Task Diagnostic_unavailability_does_not_hide_the_failure_and_technical_detail_is_on_demand()
    {
        using OperatorCultureScope culture = new("zh-CN");
        using SessionServiceHarness h = new();
        var id = await FailureGuidanceUiTests.FailedEnhancementAsync(h);
        var sessions = h.CreateService();
        var failed = (await sessions.LoadAsync(id, default)).Value;
        var model = new ErrorDetailsViewModel(sessions, new RecordingNavigation(), new PrintFlow.App.Localisation.LocalisationService(h.Settings));
        await model.OpenAsync(id, failed.CurrentFailureAttemptId!.Value, default);
        var before = (await h.Repository.LoadAsync(id, default)).Value!;
        var facts = WpfRendering.RenderExpectingNoBindingErrors(() => new PrintFlow.App.Views.ErrorDetailsView { DataContext = model },
            new Size(1000, 700), tree =>
            {
                var technical = tree.OfType<Expander>().FirstOrDefault(e => AutomationProperties.GetAutomationId(e) == "ErrorDetails.TechnicalDisclosure");
                var code = tree.OfType<TextBox>().Single(e => AutomationProperties.GetAutomationId(e) == "ErrorDetails.Code").Text;
                var unavailable = tree.OfType<TextBlock>().Single(e => AutomationProperties.GetAutomationId(e) == "ErrorDetails.ScreenshotStatus").Text;
                bool collapsed = technical is { IsExpanded: false };
                if (technical is not null) { technical.IsExpanded = true; tree.Root.UpdateLayout(); }
                var detail = tree.OfType<TextBox>().Single(e => AutomationProperties.GetAutomationId(e) == "ErrorDetails.TechnicalDetail");
                return new { Collapsed = collapsed, Code = code, Unavailable = unavailable, Reachable = GuidedSessionProjectionTests.Shown(detail) && detail.IsReadOnly };
            }).Facts;
        facts.Collapsed.ShouldBeTrue(); facts.Code.ShouldBe(nameof(FailureCode.Timeout));
        facts.Unavailable.ShouldNotBeNullOrWhiteSpace(); facts.Reachable.ShouldBeTrue();
        var after = (await h.Repository.LoadAsync(id, default)).Value!;
        after.Attempts.ShouldBe(before.Attempts); after.Reviews.ShouldBe(before.Reviews); after.Revisions.ShouldBe(before.Revisions);
    }

    internal static Dictionary<string, string> ReadSession(SessionViewModel model) =>
        WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = model }, new Size(1000, 700), tree =>
        {
            try
            {
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.SystemIdle);
                tree.Root.UpdateLayout();
                return GuidedCompletionSaveTests.Descendants(tree.Root).OfType<TextBlock>()
                    .Where(t => GuidedSessionProjectionTests.Shown(t) && !string.IsNullOrEmpty(AutomationProperties.GetAutomationId(t)))
                    .GroupBy(AutomationProperties.GetAutomationId).ToDictionary(g => g.Key, g => g.First().Text);
            }
            finally { tree.Root.DataContext = null; tree.Root.UpdateLayout(); }
        }).Facts;
}
