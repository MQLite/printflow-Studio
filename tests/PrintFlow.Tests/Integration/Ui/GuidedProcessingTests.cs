using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Adapters.Fake;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class GuidedProcessingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Running_has_truthful_indeterminate_focus_and_keeps_stop_or_takeover_reachable(bool takeOver)
    {
        using OperatorCultureScope culture = new("zh-CN");
        using HomeScreenHarness h = new();
        var model = await GuidedSessionProjectionTests.Open(h, WorkflowType.PrepareAsset);
        await model.ConfirmOriginalCommand.ExecuteAsync(null);
        h.Inner.FakeMeitu.SetScenario(FakeAdapterScenario.WaitForStopAt(ExternalOperationPhase.Busy));
        Task run = model.RunStepCommand.ExecuteAsync(null);
        await h.Inner.FakeMeitu.HangStarted;
        try
        {
            // Do not bind the driving model's observable collections to a second STA.
            var snapshot = h.Session(new RecordingNavigation());
            snapshot.Open((await h.Sessions.LoadAsync(model.Id, default)).Value);
            await snapshot.PreviewsLoaded;
            var rendered = WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = snapshot }, new Size(1000, 700), tree =>
            {
                var progress = tree.OfType<ProgressBar>().FirstOrDefault(p => AutomationProperties.GetAutomationId(p) == "Session.Processing.Progress");
                var app = tree.OfType<TextBlock>().FirstOrDefault(p => AutomationProperties.GetAutomationId(p) == "Session.Processing.Application");
                return new
                {
                    ProgressShown = progress is not null && GuidedSessionProjectionTests.Shown(progress),
                    Indeterminate = progress?.IsIndeterminate == true,
                    Application = app?.Text,
                    Actions = new[] { "Session.Stop", "Session.TakeOver" }.Select(id =>
                    {
                        var button = GuidedSessionShellTests.Find<Button>(tree, id);
                        return GuidedSessionProjectionTests.Shown(button) && button.IsEnabled;
                    }).ToArray(),
                    Technical = GuidedSessionProjectionTests.Shown(GuidedSessionShellTests.Find<FrameworkElement>(tree, "Session.TechnicalDetails")),
                    Preview = GuidedSessionProjectionTests.Shown(tree.OfType<SharedReviewSurface>().Single()),
                    ChangeRoute = snapshot.CanChangeRoute,
                };
            });
            rendered.Facts.ProgressShown.ShouldBeTrue("processing needs a focused progress surface");
            rendered.Facts.Indeterminate.ShouldBeTrue();
            rendered.Facts.Application!.ShouldContain("美图");
            rendered.Facts.Actions.ShouldAllBe(a => a);
            rendered.Facts.Technical.ShouldBeFalse();
            rendered.Facts.Preview.ShouldBeFalse();
            rendered.Facts.ChangeRoute.ShouldBeFalse("a reloaded running session must not offer route changes");
        }
        finally
        {
            if (takeOver) { model.BeginTakeOverCommand.Execute(null); model.ConfirmTakeOverCommand.Execute(null); }
            else model.StopAutomationCommand.Execute(null);
            await run;
        }
        model.GuidedPage.ShouldBe(takeOver ? GuidedSessionPage.Recovery : GuidedSessionPage.Failure);
        model.CanApprove.ShouldBeFalse();
        model.CanStopAutomation.ShouldBeFalse();
        if (takeOver) model.HasRetainedExternalState.ShouldBeTrue();
        var persisted = (await h.Inner.Repository.LoadAsync(model.Id, default)).Value!;
        persisted.Reviews.ShouldBeEmpty();
    }

    [Fact]
    public async Task Successful_processing_enters_required_review_without_automatic_next_step()
    {
        using HomeScreenHarness h = new();
        var model = await GuidedSessionProjectionTests.Open(h, WorkflowType.PrepareAsset);
        await model.ConfirmOriginalCommand.ExecuteAsync(null);
        await model.RunStepCommand.ExecuteAsync(null);
        model.GuidedPage.ShouldBe(GuidedSessionPage.Review);
        var state = (await h.Inner.Repository.LoadAsync(model.Id, default)).Value!;
        state.Steps.Single(s => s.Step == StepKind.Enhancement).State.ShouldBe(StepState.ReviewRequired);
        state.Steps.Single(s => s.Step == StepKind.BackgroundRemoval).State.ShouldNotBe(StepState.Processing);
        state.Reviews.ShouldBeEmpty();
        var rendered = WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = model }, new Size(1000, 700), tree =>
            tree.OfType<ProgressBar>().Where(p => AutomationProperties.GetAutomationId(p) == "Session.Processing.Progress")
                .Any(GuidedSessionProjectionTests.Shown));
        rendered.Facts.ShouldBeFalse();
    }
}
