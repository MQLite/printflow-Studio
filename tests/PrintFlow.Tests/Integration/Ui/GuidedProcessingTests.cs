using System.Windows;
using PrintFlow.App.Localisation;
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
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class GuidedProcessingTests
{
    [Fact]
    public async Task Switching_language_relabels_methods_and_preserves_the_pending_selection()
    {
        using OperatorCultureScope culture = new("en-US");
        using HomeScreenHarness harness = new();
        var driver = await GuidedSessionProjectionTests.Open(harness, WorkflowType.PrepareAsset);
        await driver.ConfirmOriginalCommand.ExecuteAsync(null);
        var language = new LocalisationService(harness.Inner.Settings);
        language.Use(OperatorLanguage.English);
        var screen = new SessionViewModel(harness.Sessions, harness.Previews, harness.TiffReviews,
            new RecordingNavigation(), localisation: language);
        screen.Open((await harness.Sessions.LoadAsync(driver.Id, default)).Value);
        screen.DraftImageProcessingMethod = ImageProcessingMethod.SkipEnhancement;
        var before = screen.TaskImageProcessingMethods.Single(m => m.Method == ImageProcessingMethod.SkipEnhancement);

        language.Use(OperatorLanguage.SimplifiedChinese);

        var after = screen.TaskImageProcessingMethods.Single(m => m.Method == ImageProcessingMethod.SkipEnhancement);
        after.Label.ShouldBe("跳过清晰化");
        after.Description.ShouldNotBe(before.Description);
        screen.DraftImageProcessingMethod.ShouldBe(ImageProcessingMethod.SkipEnhancement);
    }

    [Theory]
    [InlineData(1024, 1024, true, false)]
    [InlineData(1025, 1024, true, true)]
    [InlineData(4096, 1024, true, true)]
    [InlineData(4097, 1, false, false)]
    [InlineData(2049, 2048, false, false)]
    public async Task Upscale_advises_using_source_dimensions_before_running(int width, int height, bool canRun, bool advises)
    {
        using OperatorCultureScope culture = new("zh-CN");
        using SessionServiceHarness h = new();
        var service = h.CreateService(imageProcessingBackends: [new AvailableImageBackend(ImageProcessingMethod.AiUpscale)]);
        var imported = (await service.ImportAsync(WorkflowType.PrepareAsset, h.WriteBorderedSourcePng("upscale.png"),
            null, "tester", default)).Value;
        await service.ExecuteAsync(imported.Id, new WorkflowCommand.ConfirmOriginal(), "tester", default);
        var session = (await service.ExecuteAsync(imported.Id,
            new WorkflowCommand.SetImageProcessingSelection(StepKind.Enhancement,
                SessionImageProcessingSelection.Compatible with { Enhancement = ImageProcessingMethod.AiUpscale }),
            "tester", default)).Value;
        SessionViewModel screen = new(service, h.Previews, h.TiffReviews, new RecordingNavigation());
        var input = session.CurrentArtefact!;
        screen.Open(session with
        {
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
    [InlineData(ImageProcessingMethod.LightSharpen)]
    [InlineData(ImageProcessingMethod.SkipEnhancement)]
    public async Task A_stale_processing_action_cannot_use_a_method_changed_by_another_view(ImageProcessingMethod previousMethod)
    {
        using SessionServiceHarness h = new();
        var service = h.CreateService(imageProcessingBackends: [new AvailableImageBackend(previousMethod)]);
        var imported = (await service.ImportAsync(WorkflowType.PrepareAsset, h.WriteBorderedSourcePng("stale-method.png"),
            null, "tester", default)).Value;
        await service.ExecuteAsync(imported.Id, new WorkflowCommand.ConfirmOriginal(), "tester", default);
        var previous = (await service.ExecuteAsync(imported.Id,
            new WorkflowCommand.SetImageProcessingSelection(StepKind.Enhancement,
                SessionImageProcessingSelection.Compatible with { Enhancement = previousMethod }),
            "tester", default)).Value;
        SessionViewModel screen = new(service, h.Previews, h.TiffReviews, new RecordingNavigation());
        screen.Open(previous);
        screen.AdvanceImageProcessingCommand.CanExecute(null).ShouldBeTrue();
        (await service.ExecuteAsync(imported.Id, new WorkflowCommand.SetImageProcessingSelection(
            StepKind.Enhancement, SessionImageProcessingSelection.Compatible), "tester", default)).IsSuccess.ShouldBeTrue();

        await screen.AdvanceImageProcessingCommand.ExecuteAsync(null);

        var stored = (await h.Repository.LoadAsync(imported.Id, default)).Value!;
        stored.Steps.Single(step => step.Step == StepKind.Enhancement).State.ShouldBe(StepState.Waiting);
        stored.Attempts.ShouldNotContain(attempt => attempt.Step == StepKind.Enhancement);
        screen.DraftImageProcessingMethod.ShouldBe(ImageProcessingMethod.MeituEnhancement);
    }

    [Fact]
    public async Task A_stale_processing_action_cannot_use_a_source_replaced_by_another_view()
    {
        using SessionServiceHarness h = new();
        var service = h.CreateService();
        var policy = ImageProcessingPolicy.CompatibleDefault with
        {
            Selection = SessionImageProcessingSelection.Compatible with { BackgroundRemoval = ImageProcessingMethod.KeepBackground },
        };
        await h.Settings.UpsertAsync([policy.ToSetting()], default);
        var imported = (await service.ImportAsync(WorkflowType.PrepareAsset, h.WriteBorderedSourcePng("stale-input.png"),
            null, "tester", default)).Value;
        await Execute(new WorkflowCommand.ConfirmOriginal());
        var previous = await ApproveEnhancedSource();
        SessionViewModel screen = new(service, h.Previews, h.TiffReviews, new RecordingNavigation());
        screen.Open(previous);
        screen.AdvanceImageProcessingCommand.CanExecute(null).ShouldBeTrue();
        await Execute(new WorkflowCommand.ReturnToStep(StepKind.Enhancement));
        var replaced = await ApproveEnhancedSource();
        replaced.CurrentArtefact!.RevisionId.ShouldNotBe(previous.CurrentArtefact!.RevisionId);

        await screen.AdvanceImageProcessingCommand.ExecuteAsync(null);

        var stored = (await service.LoadAsync(imported.Id, default)).Value;
        stored.CurrentStep!.Step.ShouldBe(StepKind.BackgroundRemoval);
        stored.CurrentArtefact!.RevisionId.ShouldBe(replaced.CurrentArtefact.RevisionId);

        async Task<SessionView> ApproveEnhancedSource()
        {
            var result = await Execute(new WorkflowCommand.StartStep(StepKind.Enhancement));
            return await Execute(new WorkflowCommand.Approve(StepKind.Enhancement, result.CurrentArtefact!.Sha256));
        }

        async Task<SessionView> Execute(WorkflowCommand command) =>
            (await service.ExecuteAsync(imported.Id, command, "tester", default)).Value;
    }

    [Theory]
    [InlineData("en-US", "Current method: Meitu", "Current method: Skip enhancement")]
    [InlineData("zh-CN", "本次方式：美图", "本次方式：跳过清晰化")]
    public async Task Current_method_is_preselected_in_the_visible_list(string language, string methodLabel, string changedLabel)
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
                    string initialMethod = method.Text;
                    var list = GuidedSessionShellTests.Find<ListBox>(tree, "Session.ImageProcessing.Select");
                    bool canEdit = GuidedSessionProjectionTests.Shown(list) && list.IsEnabled;
                    if (canEdit)
                    {
                        list.SelectedValue.ShouldBe(ImageProcessingMethod.MeituEnhancement);
                        var selected = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(list.SelectedItem);
                        Rect visible = GuidedSessionShellTests.Bounds(
                            GuidedSessionShellTests.Ancestors(list).OfType<ScrollViewer>().First(), tree.Root);
                        visible.Contains(GuidedSessionShellTests.Bounds(selected, tree.Root)).ShouldBeTrue();
                        var unavailable = list.Items.Cast<ImageProcessingMethodOption>().Single(
                            option => option.Method == ImageProcessingMethod.OpenAiRestore);
                        var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(unavailable);
                        row.IsEnabled.ShouldBeFalse();
                        AutomationProperties.GetHelpText(row).ShouldBe(unavailable.Description);
                        var primary = GuidedSessionShellTests.Find<Button>(tree, "Session.RunStep");
                        primary.IsEnabled.ShouldBeTrue();
                        GuidedSessionProjectionTests.Shown(primary).ShouldBeTrue();
                        GuidedSessionProjectionTests.Shown(GuidedSessionShellTests.Find<Button>(tree, "Session.Skip")).ShouldBeFalse();
                        list.SetCurrentValue(ListBox.SelectedValueProperty, ImageProcessingMethod.SkipEnhancement);
                        method.Text.ShouldBe(changedLabel);
                    }
                    return (initialMethod, canEdit);
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
        (await h.Sessions.LoadAsync(model.Id, default)).Value.ImageProcessingSelection!
            .Enhancement.ShouldBe(ImageProcessingMethod.MeituEnhancement);
        await model.AdvanceImageProcessingCommand.ExecuteAsync(null);
        var persisted = (await h.Sessions.LoadAsync(model.Id, default)).Value;
        persisted.ImageProcessingSelection!.Enhancement.ShouldBe(ImageProcessingMethod.SkipEnhancement);
        persisted.ImageProcessingSelection.UseOpenAiCombined.ShouldBeFalse();
        (await h.Inner.Settings.ReadAllAsync(default)).Value.ShouldBe(defaultsBefore);

        persisted.CurrentStep!.Step.ShouldBe(StepKind.BackgroundRemoval);
        h.Meitu.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_rejected_method_change_never_runs_the_previous_method()
    {
        using HomeScreenHarness h = new();
        var original = await GuidedSessionProjectionTests.Open(h, WorkflowType.PrepareAsset);
        await original.ConfirmOriginalCommand.ExecuteAsync(null);
        var service = new ScriptedFailureSessionService(h.Sessions)
        {
            NextExecuteFailure = OperationFailure.Create(FailureCode.PersistenceError, "Method selection failed."),
        };
        var model = new SessionViewModel(service, h.Previews, h.TiffReviews, new RecordingNavigation());
        model.Open((await h.Sessions.LoadAsync(original.Id, default)).Value);
        model.DraftImageProcessingMethod = ImageProcessingMethod.SkipEnhancement;

        await model.AdvanceImageProcessingCommand.ExecuteAsync(null);

        model.DraftImageProcessingMethod.ShouldBe(ImageProcessingMethod.SkipEnhancement);
        service.ExecuteCalls.ShouldBe(1);
        var persisted = (await h.Sessions.LoadAsync(model.Id, default)).Value;
        persisted.ImageProcessingSelection!.Enhancement.ShouldBe(ImageProcessingMethod.MeituEnhancement);
        persisted.CurrentStep!.Step.ShouldBe(StepKind.Enhancement);
        h.Meitu.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task Two_advances_during_processing_start_one_attempt()
    {
        using HomeScreenHarness h = new();
        var model = await GuidedSessionProjectionTests.Open(h, WorkflowType.PrepareAsset);
        await model.ConfirmOriginalCommand.ExecuteAsync(null);
        h.Inner.FakeMeitu.SetScenario(FakeAdapterScenario.WaitForStopAt(ExternalOperationPhase.Busy));
        Task advancing = model.AdvanceImageProcessingCommand.ExecuteAsync(null);
        try
        {
            await h.Inner.FakeMeitu.HangStarted.WaitAsync(TimeSpan.FromSeconds(20));
            model.AdvanceImageProcessingCommand.CanExecute(null).ShouldBeFalse();
            await model.AdvanceImageProcessingCommand.ExecuteAsync(null);
            h.Meitu.CallCount.ShouldBe(1);
        }
        finally
        {
            model.StopAutomationCommand.Execute(null);
            await advancing;
        }
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
        var service = harness.CreateService(imageProcessingBackends: [new AvailableImageBackend(ImageProcessingMethod.OpenAiCombined)]);
        string path = harness.WriteBorderedSourcePng("combined-choice.png");
        var imported = (await service.ImportAsync(WorkflowType.PrepareAsset, path, null, "tester", default)).Value;
        var enhancement = (await service.ExecuteAsync(imported.Id, new WorkflowCommand.ConfirmOriginal(), "tester", default)).Value;
        DecliningUploadPrompt prompt = new();
        SessionViewModel model = new(service, harness.Previews, harness.TiffReviews, new RecordingNavigation(), openAiUploadPrompt: prompt);
        model.Open(enhancement);
        await model.PreviewsLoaded;

        SessionViewModel snapshot = new(service, harness.Previews, harness.TiffReviews, new RecordingNavigation());
        snapshot.Open(enhancement);
        await snapshot.PreviewsLoaded;
        WpfRendering.RenderExpectingNoBindingErrors(
            () => new SessionScreenView { DataContext = snapshot }, new Size(1000, 700), tree =>
            {
                var list = GuidedSessionShellTests.Find<ListBox>(tree, "Session.ImageProcessing.Select");
                list.Items.Cast<ImageProcessingMethodOption>().Single(
                    option => option.Method == ImageProcessingMethod.OpenAiCombined).IsAvailable.ShouldBeTrue();
                return 0;
            });
        model.DraftImageProcessingMethod = ImageProcessingMethod.OpenAiCombined;
        (await service.LoadAsync(imported.Id, default)).Value.ImageProcessingSelection!.UseOpenAiCombined.ShouldBeFalse();
        prompt.Calls.ShouldBe(0);
        await model.AdvanceImageProcessingCommand.ExecuteAsync(null);
        var selected = (await service.LoadAsync(imported.Id, default)).Value;
        selected.ImageProcessingSelection!.UseOpenAiCombined.ShouldBeTrue();
        selected.CurrentStep!.Step.ShouldBe(StepKind.BackgroundRemoval);
        selected.ImageProcessingSelection.Enhancement.ShouldBe(ImageProcessingMethod.SkipEnhancement);
        selected.ImageProcessingSelection.BackgroundRemoval.ShouldBe(ImageProcessingMethod.KeepBackground);
        prompt.Calls.ShouldBe(0);
        await model.AdvanceImageProcessingCommand.ExecuteAsync(null);
        prompt.Calls.ShouldBe(1);
        (await service.LoadAsync(imported.Id, default)).Value.CurrentStep!.State.ShouldBe(StepState.Waiting);
        (await harness.Repository.LoadAsync(imported.Id, default)).Value!.Attempts
            .Where(attempt => attempt.Step is StepKind.Enhancement or StepKind.BackgroundRemoval).ShouldBeEmpty();
    }

    private sealed class AvailableImageBackend(ImageProcessingMethod availableMethod) : IImageProcessingBackend
    {
        public ImageProcessingCapability? GetCapability(ImageProcessingMethod method) =>
            method == availableMethod ? new("test-image-processing", "v1") : null;

        public Task<OperationResult<AdapterOutput>> ProcessAsync(ImageProcessingRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("This test does not expect the backend to execute.");
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
        if (!takeOver)
        {
            model.DraftImageProcessingMethod = ImageProcessingMethod.SkipEnhancement;
            await model.AdvanceImageProcessingCommand.ExecuteAsync(null);
            var resumed = (await h.Sessions.LoadAsync(model.Id, default)).Value;
            resumed.CurrentStep!.Step.ShouldBe(StepKind.BackgroundRemoval);
            resumed.ImageProcessingSelection!.Enhancement.ShouldBe(ImageProcessingMethod.SkipEnhancement);
            h.Meitu.CallCount.ShouldBe(1);
        }
    }

    [Fact]
    public async Task Local_upscaling_displays_the_reported_percentage_in_the_processing_view()
    {
        using OperatorCultureScope culture = new("zh-CN");
        using HomeScreenHarness harness = new();
        var driver = await GuidedSessionProjectionTests.Open(harness, WorkflowType.PrepareAsset);
        await driver.ConfirmOriginalCommand.ExecuteAsync(null);
        var service = new ScriptedFailureSessionService(harness.Sessions)
        {
            Runtime = new AutomationRuntimeView(driver.Id, null, StepKind.Enhancement,
                AutomationRuntimeState.Running, ExternalOperationPhase.NotStarted, false) { UpscalePercent = 37 },
        };
        var model = new SessionViewModel(service, harness.Previews, harness.TiffReviews, new RecordingNavigation());
        model.Open((await service.LoadAsync(driver.Id, default)).Value);
        await model.PreviewsLoaded;
        model.GuidedProcessingNotice.ShouldContain("37%");
        WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = model }, new Size(1000, 700), tree =>
        {
            var bar = GuidedSessionShellTests.Find<ProgressBar>(tree, "Session.Processing.Progress");
            GuidedSessionProjectionTests.Shown(bar).ShouldBeTrue();
            bar.IsIndeterminate.ShouldBeFalse();
            bar.Value.ShouldBe(37);
            return true;
        });
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
