using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Adapters.Fake;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class GuidedCompletionRecoveryTests
{
    [Theory]
    [InlineData("prepare")]
    [InlineData("waiting")]
    [InlineData("reopened")]
    [InlineData("returned")]
    public async Task Correction_prepare_wait_import_and_review_remain_explicit(string stage)
    {
        using OperatorCultureScope culture = new("zh-CN");
        using SessionServiceHarness h = new();
        var service = CorrectionFixtures.Service(h);
        var review = await CorrectionFixtures.AtBackgroundRemovalReviewAsync(h, service);
        var picker = new ColleagueCorrectionUiTests.RecordingPicker();
        var model = new SessionViewModel(service, h.Previews, h.TiffReviews, new RecordingNavigation(), filePicker: picker);
        model.Open(review); await model.PreviewsLoaded;
        string before = JsonSerializer.Serialize((await h.Repository.LoadAsync(review.Id, default)).Value);
        model.BeginAskColleagueCommand.Execute(null);
        if (stage != "prepare") await model.PrepareCorrectionFilesCommand.ExecuteAsync(null);
        if (stage == "returned")
        {
            picker.Next = CorrectionFixtures.CorrectedPng(h);
            await model.ImportCorrectedImageCommand.ExecuteAsync(null);
        }
        if (stage == "reopened")
        {
            before = JsonSerializer.Serialize((await h.Repository.LoadAsync(review.Id, default)).Value);
            model = new SessionViewModel(service, h.Previews, h.TiffReviews, new RecordingNavigation(), filePicker: picker);
            model.Open((await service.LoadAsync(review.Id, default)).Value);
        }
        await model.PreviewsLoaded;
        var state = (await h.Repository.LoadAsync(review.Id, default)).Value!;
        if (stage is "prepare" or "reopened") JsonSerializer.Serialize(state).ShouldBe(before);
        var guidance = ReadGuidance(model);
        if (stage == "returned")
        {
            model.GuidedPage.ShouldBe(GuidedSessionPage.Review);
            model.ReviewTargetIdentity.ShouldNotBeNull();
            state.Reviews.ShouldNotContain(r => r.Step == StepKind.BackgroundRemoval && r.IsApproved);
            guidance.GetValueOrDefault("Session.Correction.ReturnReview", string.Empty).ShouldContain("重新审核");
        }
        else
        {
            model.GuidedPage.ShouldBe(GuidedSessionPage.Correction);
            guidance.GetValueOrDefault("Session.RecoveryGuidance.Heading", string.Empty).ShouldBe(stage == "prepare" ? "准备同事修正" : "等待修正后的图片");
            guidance.GetValueOrDefault("Session.Correction.Flow", string.Empty).ShouldContain("显式导入");
            guidance.GetValueOrDefault("Session.Correction.Flow", string.Empty).ShouldContain("重新审核");
            if (stage != "prepare")
            {
                model.IsHandedOff.ShouldBeTrue();
                model.CanImportCorrectedImage.ShouldBeTrue();
                guidance.GetValueOrDefault("Session.RecoveryGuidance.Next", string.Empty).ShouldContain(model.CorrectionImportLabel);
                guidance.GetValueOrDefault("Session.RecoveryGuidance.Preserved", string.Empty).ShouldContain("自行交给同事");
            }
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Handoff_reopens_with_only_supported_manual_paths_and_never_restarts(bool photoshop, bool hasResult)
    {
        using OperatorCultureScope culture = new("zh-CN");
        using HomeScreenHarness h = new();
        var driver = await GuidedSessionProjectionTests.Open(h, photoshop ? WorkflowType.GeneratePrintTiff : WorkflowType.PrepareAsset);
        await driver.ConfirmOriginalCommand.ExecuteAsync(null);
        if (photoshop)
        {
            driver.WidthMmText = "200"; driver.HeightMmText = "150";
            await driver.SetMaximumBoundsCommand.ExecuteAsync(null);
            driver.SelectedWhiteUnderbaseChoice = driver.WhiteUnderbaseChoices[1];
            await driver.SelectWhiteUnderbaseCommand.ExecuteAsync(null);
            if (!hasResult) h.Inner.FakePhotoshop.SetScenario(FakeAdapterScenario.FailWith(PrintFlow.Domain.Results.FailureCode.AdapterUnavailable));
            await driver.RunStepCommand.ExecuteAsync(null);
            driver.CanHandOff.ShouldBeTrue();
            await driver.HandOffCommand.ExecuteAsync(null);
        }
        else
        {
            h.Inner.FakeMeitu.SetScenario(FakeAdapterScenario.WaitForStopAt(ExternalOperationPhase.Busy));
            Task run = driver.RunStepCommand.ExecuteAsync(null);
            await h.Inner.FakeMeitu.HangStarted.WaitAsync(TimeSpan.FromSeconds(10));
            try { driver.BeginTakeOverCommand.Execute(null); driver.ConfirmTakeOverCommand.Execute(null); }
            finally { if (driver.CanStopAutomation) driver.StopAutomationCommand.Execute(null); await run.WaitAsync(TimeSpan.FromSeconds(10)); }
        }
        var before = (await h.Inner.Repository.LoadAsync(driver.Id, default)).Value!;
        var model = h.Session(new RecordingNavigation());
        model.Open((await h.Sessions.LoadAsync(driver.Id, default)).Value); await model.PreviewsLoaded;
        var facts = ReadGuidance(model);
        model.GuidedPage.ShouldBe(GuidedSessionPage.Recovery);
        if (!photoshop) model.HasRetainedExternalState.ShouldBeTrue();
        model.CanSubmitManualResult.ShouldBe(!photoshop);
        model.CanApprove.ShouldBeFalse();
        facts.GetValueOrDefault("Session.RecoveryGuidance.Heading", string.Empty).ShouldBe("已移交，选择如何继续");
        model.CanReenterAutomation.ShouldBe(!hasResult);
        if (hasResult) facts.GetValueOrDefault("Session.RecoveryGuidance.Next", string.Empty).ShouldNotContain(model.ReenterAutomationLabel);
        else facts.GetValueOrDefault("Session.RecoveryGuidance.Next", string.Empty).ShouldContain(model.ReenterAutomationLabel);
        if (photoshop) facts.GetValueOrDefault("Session.RecoveryGuidance.Preserved", string.Empty).ShouldContain("不支持手动 TIFF 回导");
        else facts.GetValueOrDefault("Session.RecoveryGuidance.Next", string.Empty).ShouldContain(model.SubmitManualResultLabel);
        JsonSerializer.Serialize((await h.Inner.Repository.LoadAsync(driver.Id, default)).Value).ShouldBe(JsonSerializer.Serialize(before));
        await driver.ReenterAutomationCommand.ExecuteAsync(null);
        var after = (await h.Inner.Repository.LoadAsync(driver.Id, default)).Value!;
        after.Attempts.ShouldBe(before.Attempts);
        after.Reviews.ShouldBe(before.Reviews);
        after.ToSnapshot().CurrentStep!.State.ShouldBe(hasResult ? StepState.ReviewRequired : StepState.Waiting);
        if (hasResult) JsonSerializer.Serialize(after).ShouldBe(JsonSerializer.Serialize(before));
    }

    [Fact]
    public async Task Restored_interruption_names_only_executable_actions_and_keeps_the_original_identity()
    {
        using OperatorCultureScope culture = new("zh-CN");
        using HomeScreenHarness h = new();
        var id = await RecoverySurfaceTests.Seed(h.Inner, "e5-restored");
        var before = (await h.Inner.Repository.LoadAsync(id, default)).Value!;
        var model = h.Session(new RecordingNavigation());
        model.Open((await h.Sessions.LoadAsync(id, default)).Value); await model.PreviewsLoaded;
        var facts = ReadGuidance(model);
        model.GuidedPage.ShouldBe(GuidedSessionPage.Failure);
        facts.GetValueOrDefault("Session.RecoveryGuidance.Heading", string.Empty).ShouldBe("处理已停止");
        facts.GetValueOrDefault("Session.RecoveryGuidance.Preserved", string.Empty).ShouldContain("原件");
        var next = facts.GetValueOrDefault("Session.RecoveryGuidance.Next", string.Empty);
        next.ShouldNotBeNullOrWhiteSpace();
        if (!model.CanRetry) next.ShouldNotContain(model.RetryLabel);
        if (!model.CanSubmitManualResult) next.ShouldNotContain(model.SubmitManualResultLabel);
        model.CanApprove.ShouldBeFalse();
        JsonSerializer.Serialize((await h.Inner.Repository.LoadAsync(id, default)).Value).ShouldBe(JsonSerializer.Serialize(before));
    }

    internal static Dictionary<string, string> ReadGuidance(SessionViewModel model) => WpfRendering.RenderExpectingNoBindingErrors(
        () => new SessionScreenView { DataContext = model }, new Size(1000, 700), tree =>
        {
            try
            {
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.SystemIdle);
                tree.Root.UpdateLayout();
                return GuidedCompletionSaveTests.Descendants(tree.Root).OfType<TextBlock>()
                    .Where(t => GuidedSessionProjectionTests.Shown(t) &&
                        (AutomationProperties.GetAutomationId(t).StartsWith("Session.RecoveryGuidance.") ||
                         AutomationProperties.GetAutomationId(t) is "Session.Correction.Flow" or "Session.Correction.ReturnReview"))
                    .ToDictionary(AutomationProperties.GetAutomationId, t => t.Text);
            }
            finally { tree.Root.DataContext = null; tree.Root.UpdateLayout(); }
        }).Facts;
}
