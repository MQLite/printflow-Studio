using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using PrintFlow.App.Navigation;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Settings;
using PrintFlow.Infrastructure.Adapters.Fake;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class GuidedProcessingTests
{
    [Theory]
    [InlineData(1024, 1024, true, false)]
    [InlineData(1025, 1024, true, true)]
    [InlineData(4096, 1024, true, true)]
    [InlineData(4097, 1, false, false)]
    [InlineData(2049, 2048, false, false)]
    public async Task Upscale_advises_using_source_dimensions_before_running(int width, int height, bool canRun, bool advises)
    {
        using OperatorCultureScope culture = new("zh-CN");
        using HomeScreenHarness h = new();
        var screen = await GuidedSessionProjectionTests.Open(h, WorkflowType.PrepareAsset);
        await screen.ConfirmOriginalCommand.ExecuteAsync(null);
        var session = (await h.Sessions.LoadAsync(screen.Id, default)).Value;
        var input = session.CurrentArtefact!;
        screen.Open(session with
        {
            ImageProcessingExecution = session.ImageProcessingExecution! with { Method = ImageProcessingMethod.AiUpscale },
            CurrentArtefact = input with { Facts = input.Facts with { PixelWidth = width, PixelHeight = height } },
        });
        await screen.PreviewsLoaded;
        screen.CanRunStep.ShouldBe(canRun);
        screen.NextStepText.Contains("建议跳过清晰化", StringComparison.Ordinal).ShouldBe(advises);
        if (advises)
            screen.NextStepText.ShouldContain($"{width} × {height}");
        if (!canRun)
            screen.NextStepText.ShouldContain("4,096");
    }

    [Theory]
    [InlineData("en-US", "Current method: Meitu", "Change method for this task")]
    [InlineData("zh-CN", "本次方式：美图", "更改本任务的方式")]
    public async Task Current_method_and_keyboard_entry_follow_workflow_permissions(
        string language, string methodLabel, string changeLabel)
    {
        using OperatorCultureScope culture = new(language);
        using HomeScreenHarness h = new();
        var model = await GuidedSessionProjectionTests.Open(h, WorkflowType.PrepareAsset);
        await model.ConfirmOriginalCommand.ExecuteAsync(null);

        async Task<(string Text, bool CanEdit)> RenderAsync()
        {
            var snapshot = h.Session(new RecordingNavigation());
            snapshot.Open((await h.Sessions.LoadAsync(model.Id, default)).Value);
            await snapshot.PreviewsLoaded;
            return WpfRendering.RenderExpectingNoBindingErrors(
                () => new SessionScreenView { DataContext = snapshot }, new Size(1000, 700), tree =>
                {
                    var method = GuidedSessionShellTests.Find<TextBlock>(tree, "Session.ImageProcessing.Method");
                    var change = GuidedSessionShellTests.Find<Expander>(tree, "Session.ImageProcessing.Change");
                    change.Header.ShouldBe(changeLabel);
                    return (method.Text, GuidedSessionProjectionTests.Shown(change) && change.IsEnabled);
                }).Facts;
        }

        (await RenderAsync()).ShouldBe((methodLabel, true));
        await model.RunStepCommand.ExecuteAsync(null);
        (await RenderAsync()).ShouldBe((methodLabel, false));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Task_method_change_persists_only_this_task_and_can_explicitly_skip_enhancement(bool combined)
    {
        using HomeScreenHarness h = new();
        if (combined)
        {
            var policy = ImageProcessingPolicy.CompatibleDefault with
            {
                Selection = SessionImageProcessingSelection.Compatible with { UseOpenAiCombined = true },
            };
            await h.Inner.Settings.UpsertAsync([policy.ToSetting()], default);
        }
        var model = await GuidedSessionProjectionTests.Open(h, WorkflowType.PrepareAsset);
        await model.ConfirmOriginalCommand.ExecuteAsync(null);
        var defaultsBefore = (await h.Inner.Settings.ReadAllAsync(default)).Value;

        model.DraftImageProcessingMethod = ImageProcessingMethod.SkipEnhancement;
        model.ApplyImageProcessingMethodCommand.CanExecute(null).ShouldBeTrue();
        await model.ApplyImageProcessingMethodCommand.ExecuteAsync(null);
        var persisted = (await h.Sessions.LoadAsync(model.Id, default)).Value;
        persisted.ImageProcessingSelection!.Enhancement.ShouldBe(ImageProcessingMethod.SkipEnhancement);
        persisted.ImageProcessingSelection.UseOpenAiCombined.ShouldBeFalse();
        (await h.Inner.Settings.ReadAllAsync(default)).Value.ShouldBe(defaultsBefore);

        model.CanRunStep.ShouldBeFalse();
        model.CanSkip.ShouldBeTrue();
        await model.SkipCommand.ExecuteAsync(null);
        (await h.Sessions.LoadAsync(model.Id, default)).Value.CurrentStep!.Step.ShouldBe(StepKind.BackgroundRemoval);
    }

    [Fact]
    public async Task Enhancement_offers_combined_processing_directly_and_still_requests_upload_consent()
    {
        using OperatorCultureScope culture = new("en-US");
        using SessionServiceHarness harness = new();
        var policy = ImageProcessingPolicy.CompatibleDefault with
        {
            Selection = SessionImageProcessingSelection.Compatible with
            {
                Enhancement = ImageProcessingMethod.SkipEnhancement,
                BackgroundRemoval = ImageProcessingMethod.KeepBackground,
            },
        };
        await harness.Settings.UpsertAsync([policy.ToSetting()], default);
        var service = harness.CreateService(imageProcessingBackends: [new AvailableCombinedBackend()]);
        string path = harness.WriteBorderedSourcePng("combined-choice.png");
        var imported = (await service.ImportAsync(WorkflowType.PrepareAsset, path, null, "tester", default)).Value;
        var enhancement = (await service.ExecuteAsync(imported.Id, new WorkflowCommand.ConfirmOriginal(), "tester", default)).Value;
        DecliningUploadPrompt prompt = new();
        SessionViewModel model = new(service, harness.Previews, harness.TiffReviews, new RecordingNavigation(), openAiUploadPrompt: prompt);
        model.Open(enhancement);
        await model.PreviewsLoaded;

        async Task ChooseAsync(string label)
        {
            SessionViewModel snapshot = new(service, harness.Previews, harness.TiffReviews, new RecordingNavigation());
            snapshot.Open((await service.LoadAsync(imported.Id, default)).Value);
            await snapshot.PreviewsLoaded;
            WpfRendering.RenderExpectingNoBindingErrors(
                () => new SessionScreenView { DataContext = snapshot }, new Size(1000, 700), tree =>
                {
                    var button = GuidedSessionShellTests.Find<Button>(tree, "Session.ImageProcessing.CombinedChoice");
                    GuidedSessionProjectionTests.Shown(button).ShouldBeTrue();
                    button.IsEnabled.ShouldBeTrue();
                    button.Content.ShouldBe(label);
                    GuidedSessionShellTests.Ancestors(button).ShouldNotContain(
                        GuidedSessionShellTests.Find<Expander>(tree, "Session.ImageProcessing.Change"));
                    button.Command.ShouldBeSameAs(snapshot.SwitchCombinedImageProcessingCommand);
                    return true;
                });
            await model.SwitchCombinedImageProcessingCommand.ExecuteAsync(null);
        }

        await ChooseAsync("Use OpenAI for clarity and background removal");
        var selected = (await service.LoadAsync(imported.Id, default)).Value;
        selected.ImageProcessingSelection!.UseOpenAiCombined.ShouldBeTrue();
        await ChooseAsync("Use separate steps");
        var separate = (await service.LoadAsync(imported.Id, default)).Value;
        separate.ImageProcessingSelection!.UseOpenAiCombined.ShouldBeFalse();
        separate.ImageProcessingSelection.Enhancement.ShouldBe(ImageProcessingMethod.SkipEnhancement);
        separate.ImageProcessingSelection.BackgroundRemoval.ShouldBe(ImageProcessingMethod.KeepBackground);
        await ChooseAsync("Use OpenAI for clarity and background removal");
        prompt.Calls.ShouldBe(0);
        await model.SkipCommand.ExecuteAsync(null);
        await model.RunStepCommand.ExecuteAsync(null);
        prompt.Calls.ShouldBe(1);
        (await service.LoadAsync(imported.Id, default)).Value.CurrentStep!.State.ShouldBe(StepState.Waiting);
        (await harness.Repository.LoadAsync(imported.Id, default)).Value!.Attempts
            .Where(attempt => attempt.Step is StepKind.Enhancement or StepKind.BackgroundRemoval).ShouldBeEmpty();
    }

    private sealed class AvailableCombinedBackend : IImageProcessingBackend
    {
        public ImageProcessingCapability? GetCapability(ImageProcessingMethod method) =>
            method == ImageProcessingMethod.OpenAiCombined ? new("test-combined", "v1") : null;

        public Task<OperationResult<AdapterOutput>> ProcessAsync(ImageProcessingRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Declining upload consent should prevent backend execution.");
    }

    private sealed class DecliningUploadPrompt : IOpenAiUploadPrompt
    {
        public int Calls { get; private set; }

        public bool Confirm(string operation)
        {
            Calls++;
            return false;
        }
    }

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
            snapshot.CanChangeImageProcessingMethod.ShouldBeFalse();
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
        if (!takeOver) model.CanChangeImageProcessingMethod.ShouldBeTrue();
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
