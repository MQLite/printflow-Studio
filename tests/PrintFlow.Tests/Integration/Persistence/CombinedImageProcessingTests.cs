using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using ImageMagick;
using ImageMagick.Drawing;
using Microsoft.Extensions.DependencyInjection;
using PrintFlow.App.Composition;
using PrintFlow.App.Navigation;
using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Reviews;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Settings;
using PrintFlow.Infrastructure.Adapters.OpenAI;
using PrintFlow.Infrastructure.Configuration;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Persistence;

[Collection(SqliteCollection.Name)]
public sealed class CombinedImageProcessingTests
{
    [Theory]
    [InlineData(WorkflowType.PrepareAsset)]
    [InlineData(WorkflowType.PrepareCustomerDesign)]
    public async Task Paired_processing_confirms_one_upload_and_reviews_one_candidate_before_trim(WorkflowType workflow)
    {
        using var scenario = new Scenario();
        var enhancement = await scenario.SelectCombinedAsync(workflow);
        var screen = new SessionViewModel(scenario.Service, scenario.Harness.Previews, scenario.Harness.TiffReviews,
            new RecordingNavigation(), openAiUploadPrompt: scenario.Prompt);
        screen.Open(enhancement);

        screen.CanRunStep.ShouldBeFalse();
        screen.CanSkip.ShouldBeTrue();
        (await scenario.Execute(new WorkflowCommand.StartStep(StepKind.Enhancement))).IsFailure.ShouldBeTrue();
        (await scenario.Execute(new WorkflowCommand.Skip(StepKind.Enhancement))).IsFailure.ShouldBeTrue();
        await screen.SkipCommand.ExecuteAsync(null);
        var ready = await scenario.Load();
        ready.CurrentStep!.Step.ShouldBe(StepKind.BackgroundRemoval);
        ready.Steps.Single(x => x.Step == StepKind.Enhancement).SkipReason.ShouldBe("CombinedWithBackgroundRemoval");
        ready.CurrentArtefact!.RevisionId.ShouldBe(enhancement.CurrentArtefact!.RevisionId);
        ready.CanSetBackgroundRemovalDecision.ShouldBeFalse();
        ready.AvailableCommands.ShouldNotContain(CommandKind.Skip);
        scenario.Prompt.Calls.ShouldBe(0);
        scenario.Http.Calls.ShouldBe(0);
        (await scenario.ReadAttempts()).ShouldBeEmpty();

        (await scenario.Execute(new WorkflowCommand.StartStep(StepKind.BackgroundRemoval)))
            .Failure.MessageKey.ShouldBe("ImageProcessing_OpenAiConfirmationRequired");
        screen.CanRunStep.ShouldBeTrue();
        await screen.RunStepCommand.ExecuteAsync(null);
        scenario.Prompt.Calls.ShouldBe(1);
        scenario.Http.Calls.ShouldBe(0);
        (await scenario.ReadAttempts()).ShouldBeEmpty();

        scenario.Prompt.Accepted = true;
        await screen.RunStepCommand.ExecuteAsync(null);
        var review = await scenario.Load();
        scenario.Prompt.Calls.ShouldBe(2);
        scenario.Http.Calls.ShouldBe(1);
        scenario.Http.Background.ShouldBe("transparent");
        scenario.Http.Format.ShouldBe("png");
        scenario.Http.Image!.ShouldBe(scenario.InputBytes);
        review.CurrentStep!.State.ShouldBe(StepState.ReviewRequired);
        review.CurrentStep.Step.ShouldBe(StepKind.BackgroundRemoval);
        review.UpstreamArtefact!.RevisionId.ShouldBe(ready.CurrentArtefact.RevisionId);
        review.CurrentArtefact!.RevisionId.ShouldNotBe(review.UpstreamArtefact.RevisionId);
        screen.ReviewGuidance.ShouldBe(ReviewGuidanceStep.CombinedImageProcessing);
        (await scenario.Execute(new WorkflowCommand.StartStep(StepKind.Trim))).IsFailure.ShouldBeTrue();

        var attempt = (await scenario.ReadAttempts()).ShouldHaveSingleItem();
        attempt.Step.ShouldBe(StepKind.BackgroundRemoval);
        attempt.Status.ShouldBe(AttemptStatus.Succeeded);
        attempt.OutputRevisionId.ShouldBe(review.CurrentArtefact.RevisionId);
        attempt.ImageProcessingDetails!.Method.ShouldBe(ImageProcessingMethod.OpenAiCombined);
        attempt.ImageProcessingDetails.UploadConfirmedAtUtc.ShouldNotBeNull();
        var trim = (await scenario.Execute(new WorkflowCommand.Approve(StepKind.BackgroundRemoval, review.CurrentArtefact.Sha256))).Value;
        trim.CurrentStep!.Step.ShouldBe(StepKind.Trim);
        trim.CurrentArtefact!.RevisionId.ShouldBe(review.CurrentArtefact.RevisionId);
    }

    [Fact]
    public async Task Failure_can_keep_the_reviewed_input_only_by_an_explicit_choice()
    {
        using var scenario = new Scenario();
        var ready = await scenario.ReachBackgroundAsync();
        scenario.Http.Respond = () => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var consent = Confirm(ready);
        var run = new WorkflowCommand.StartStep(StepKind.BackgroundRemoval, consent);
        (await scenario.Execute(run)).Failure.MessageKey.ShouldBe("ImageProcessing_OpenAiRateOrQuota");
        var failed = await scenario.Load();
        failed.CanRunBackgroundRemoval.ShouldBeFalse();
        failed.AvailableCommands.ShouldNotContain(CommandKind.HandOff);
        (await scenario.Execute(new WorkflowCommand.Skip(StepKind.BackgroundRemoval))).IsFailure.ShouldBeTrue();

        scenario.Http.Calls.ShouldBe(1);
        var kept = await scenario.Execute(new WorkflowCommand.Skip(StepKind.BackgroundRemoval, "KeepReviewedInputAfterCombined"));
        kept.Value.CurrentStep!.Step.ShouldBe(StepKind.Trim);
        kept.Value.CurrentArtefact!.RevisionId.ShouldBe(ready.CurrentArtefact!.RevisionId);
        (await scenario.ReadAttempts()).ShouldHaveSingleItem().OutputRevisionId.ShouldBeNull();
    }

    [Fact]
    public async Task Rejection_can_rewind_and_resume_the_saved_independent_methods()
    {
        using var scenario = new Scenario();
        var ready = await scenario.ReachBackgroundAsync();
        var review = (await scenario.Execute(new WorkflowCommand.StartStep(StepKind.BackgroundRemoval, Confirm(ready)))).Value;
        (await scenario.Execute(new WorkflowCommand.Skip(StepKind.BackgroundRemoval, "KeepReviewedInputAfterCombined")))
            .IsFailure.ShouldBeTrue();
        var rejected = (await scenario.Execute(new WorkflowCommand.Reject(StepKind.BackgroundRemoval,
            review.CurrentArtefact!.Sha256, RejectionReason.InsufficientResult))).Value;
        var screen = new SessionViewModel(scenario.Service, scenario.Harness.Previews, scenario.Harness.TiffReviews,
            new RecordingNavigation(), openAiUploadPrompt: scenario.Prompt);
        screen.Open(rejected);
        screen.CanReturnToSeparateProcessing.ShouldBeTrue();
        screen.ReturnToSeparateProcessingCommand.Execute(null);
        await screen.ConfirmReturnCommand.ExecuteAsync(null);
        var enhancement = await scenario.Load();
        enhancement.CurrentStep!.Step.ShouldBe(StepKind.Enhancement);
        var split = enhancement;
        split.ImageProcessingSelection!.Enhancement.ShouldBe(ImageProcessingMethod.MeituEnhancement);
        split.ImageProcessingSelection!.UseOpenAiCombined.ShouldBeFalse();
        split.ImageProcessingSelection.BackgroundRemoval.ShouldBe(ImageProcessingMethod.MeituBackgroundRemoval);
        (await scenario.ReadAttempts()).ShouldHaveSingleItem().OutputRevisionId.ShouldBe(review.CurrentArtefact.RevisionId);
        scenario.Http.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Rejected_result_can_be_replaced_by_a_new_confirmed_attempt()
    {
        using var scenario = new Scenario();
        var ready = await scenario.ReachBackgroundAsync();
        var firstRun = new WorkflowCommand.StartStep(StepKind.BackgroundRemoval, Confirm(ready));
        var firstReview = (await scenario.Execute(firstRun)).Value;
        var rejected = (await scenario.Execute(new WorkflowCommand.Reject(StepKind.BackgroundRemoval,
            firstReview.CurrentArtefact!.Sha256, RejectionReason.InsufficientResult))).Value;
        rejected.CanRunBackgroundRemoval.ShouldBeFalse();
        var readyAgain = (await scenario.Execute(new WorkflowCommand.Retry(StepKind.BackgroundRemoval))).Value;
        (await scenario.Execute(firstRun)).Failure.MessageKey.ShouldBe("ImageProcessing_OpenAiConfirmationRequired");
        var secondReview = (await scenario.Execute(
            new WorkflowCommand.StartStep(StepKind.BackgroundRemoval, Confirm(readyAgain)))).Value;
        secondReview.CurrentStep!.State.ShouldBe(StepState.ReviewRequired);
        secondReview.CurrentArtefact!.RevisionId.ShouldNotBe(firstReview.CurrentArtefact.RevisionId);
        var attempts = (await scenario.ReadAttempts()).OrderBy(attempt => attempt.RetrySequence).ToArray();
        attempts.Length.ShouldBe(2);
        attempts[1].RetryOfAttemptId.ShouldBe(attempts[0].Id);
        scenario.Http.Calls.ShouldBe(2);
    }

    [Fact]
    public async Task Returning_to_enhancement_or_importing_again_invalidates_unused_confirmation()
    {
        using var scenario = new Scenario();
        var ready = await scenario.ReachBackgroundAsync();
        var oldCommand = new WorkflowCommand.StartStep(StepKind.BackgroundRemoval, Confirm(ready));

        (await scenario.Execute(new WorkflowCommand.ReturnToStep(StepKind.Enhancement))).IsSuccess.ShouldBeTrue();
        var revisited = (await scenario.Execute(
            new WorkflowCommand.Skip(StepKind.Enhancement, WorkflowCommand.Skip.CombinedWithBackgroundRemoval))).Value;
        revisited.CurrentArtefact!.RevisionId.ShouldBe(ready.CurrentArtefact!.RevisionId);
        revisited.CurrentStep!.EnteredStateAtUtc.ShouldBe(ready.CurrentStep!.EnteredStateAtUtc);
        revisited.CurrentStep.CombinedUploadPositionId.ShouldNotBe(ready.CurrentStep.CombinedUploadPositionId);
        (await scenario.Execute(oldCommand)).Failure.MessageKey.ShouldBe("ImageProcessing_OpenAiConfirmationRequired");

        var beforeNewImport = new WorkflowCommand.StartStep(StepKind.BackgroundRemoval, Confirm(revisited));
        var newInput = await scenario.ReachBackgroundAsync();
        newInput.CurrentArtefact!.RevisionId.ShouldNotBe(revisited.CurrentArtefact.RevisionId);
        newInput.CurrentArtefact.Sha256.ShouldBe(revisited.CurrentArtefact.Sha256);
        (await scenario.Execute(beforeNewImport)).Failure.MessageKey.ShouldBe("ImageProcessing_OpenAiConfirmationRequired");
        scenario.Http.Calls.ShouldBe(0);
        (await scenario.Execute(new WorkflowCommand.StartStep(StepKind.BackgroundRemoval, Confirm(newInput)))).IsSuccess.ShouldBeTrue();
        scenario.Http.Calls.ShouldBe(1);
    }

    [Theory]
    [InlineData(ImageProcessingMethod.LightSharpen, 0)]
    [InlineData(ImageProcessingMethod.OpenAiRestore, 1)]
    public async Task Independent_enhancement_and_cloud_cutout_each_require_review(
        ImageProcessingMethod enhancementMethod, int enhancementRequests)
    {
        using var scenario = new Scenario();
        var enhancement = await scenario.SelectCombinedAsync(WorkflowType.PrepareAsset);
        var selection = enhancement.ImageProcessingSelection! with { Enhancement = enhancementMethod, UseOpenAiCombined = false };
        var separate = (await scenario.Execute(new WorkflowCommand.SetImageProcessingSelection(StepKind.Enhancement, selection))).Value;
        var input = separate.CurrentArtefact!;
        var consent = enhancementRequests == 0 ? null :
            new ImageUploadConfirmation(enhancementMethod, input.RevisionId, input.Sha256);
        var review = (await scenario.Execute(new WorkflowCommand.StartStep(StepKind.Enhancement, consent))).Value;
        review.CurrentStep!.State.ShouldBe(StepState.ReviewRequired);
        scenario.Http.Calls.ShouldBe(enhancementRequests);
        (await scenario.Execute(new WorkflowCommand.StartStep(StepKind.BackgroundRemoval))).IsFailure.ShouldBeTrue();
        var enhanced = review.CurrentArtefact!;
        (await scenario.Execute(new WorkflowCommand.Approve(StepKind.Enhancement, enhanced.Sha256))).IsSuccess.ShouldBeTrue();

        (await scenario.Execute(new WorkflowCommand.SetImageProcessingSelection(StepKind.BackgroundRemoval,
            selection with { BackgroundRemoval = ImageProcessingMethod.OpenAiCutout }))).IsSuccess.ShouldBeTrue();
        (await scenario.Execute(new WorkflowCommand.SetBackgroundRemovalDecision(
            BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent, enhanced.RevisionId, enhanced.Sha256,
            Method: ImageProcessingMethod.OpenAiCutout))).IsSuccess.ShouldBeTrue();
        var cutout = new ImageUploadConfirmation(ImageProcessingMethod.OpenAiCutout, enhanced.RevisionId, enhanced.Sha256);
        var result = (await scenario.Execute(new WorkflowCommand.StartStep(StepKind.BackgroundRemoval, cutout))).Value;
        result.CurrentStep!.State.ShouldBe(StepState.ReviewRequired);
        result.UpstreamArtefact!.RevisionId.ShouldBe(enhanced.RevisionId);
        scenario.Http.Calls.ShouldBe(enhancementRequests + 1);
        (await scenario.ReadAttempts()).Select(attempt => attempt.ImageProcessingDetails!.Method)
            .ShouldBe([enhancementMethod, ImageProcessingMethod.OpenAiCutout], ignoreOrder: true);
    }

    [Fact]
    public async Task Settings_save_combined_for_new_tasks_and_restore_the_separate_choices_when_disabled()
    {
        using var scenario = new Scenario();
        var config = PrintFlowConfiguration.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"))
            with { Adapters = new AdaptersConfiguration("Fake") };
        using var provider = ServiceRegistration.BuildServiceProvider(config, scenario.Harness.Workspace.Root,
            scenario.Harness.Database.Factory, services =>
            {
                services.AddSingleton<IOpenAiImageCredentials>(new Credentials());
                services.AddSingleton<INavigationService, RecordingNavigation>();
            });
        var screen = provider.GetRequiredService<SettingsViewModel>();
        await screen.OpenAsync(default);
        await screen.OpenImageProcessingCommand.ExecuteAsync(null);
        screen.SelectedEnhancementMethod = ImageProcessingMethod.OpenAiRestore;
        screen.SelectedBackgroundRemovalMethod = ImageProcessingMethod.KeepBackground;
        screen.UseOpenAiCombined = true;
        screen.SaveImageProcessingCommand.CanExecute(null).ShouldBeTrue();
        await screen.SaveImageProcessingCommand.ExecuteAsync(null);
        var saved = ImageProcessingPolicy.FromSettings((await scenario.Harness.Settings.ReadAllAsync(default)).Value).Value;
        saved.Selection.UseOpenAiCombined.ShouldBeTrue();
        await screen.BackFromImageProcessingCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));
        await screen.OpenImageProcessingCommand.ExecuteAsync(null);
        screen.UseOpenAiCombined.ShouldBeTrue();

        string path = scenario.Harness.WriteSourcePng("settings.png");
        var paired = (await scenario.Service.ImportAsync(WorkflowType.PrepareAsset, path, null, "qa", default)).Value;
        paired.ImageProcessingSelection!.UseOpenAiCombined.ShouldBeTrue();
        screen.UseOpenAiCombined = false;
        screen.SelectedEnhancementMethod.ShouldBe(ImageProcessingMethod.OpenAiRestore);
        screen.SelectedBackgroundRemovalMethod.ShouldBe(ImageProcessingMethod.KeepBackground);
        await screen.SaveImageProcessingCommand.ExecuteAsync(null);
        var split = (await scenario.Service.ImportAsync(WorkflowType.PrepareAsset, path, null, "qa", default)).Value;
        split.ImageProcessingSelection!.UseOpenAiCombined.ShouldBeFalse();
        (await scenario.Service.LoadAsync(paired.Id, default)).Value.ImageProcessingSelection!.UseOpenAiCombined.ShouldBeTrue();
        scenario.Http.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task An_opaque_combined_response_creates_no_candidate_or_review()
    {
        using var scenario = new Scenario();
        var ready = await scenario.ReachBackgroundAsync();
        scenario.Http.Respond = () => Task.FromResult(scenario.Http.Reply(transparent: false));
        var result = await scenario.Execute(new WorkflowCommand.StartStep(StepKind.BackgroundRemoval, Confirm(ready)));
        result.Failure.MessageKey.ShouldBe("ImageProcessing_OpenAiInvalidOutput");
        var failed = await scenario.Load();
        failed.CurrentStep!.State.ShouldBe(StepState.Failed);
        failed.AvailableCommands.ShouldNotContain(CommandKind.Approve);
        (await scenario.ReadAttempts()).ShouldHaveSingleItem().OutputRevisionId.ShouldBeNull();
        scenario.Http.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task A_duplicate_start_and_a_cancelled_late_response_leave_no_candidate()
    {
        using var scenario = new Scenario();
        var ready = await scenario.ReachBackgroundAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scenario.Http.Respond = async () =>
        {
            entered.SetResult();
            await release.Task;
            return scenario.Http.Reply();
        };

        var pending = scenario.Execute(new WorkflowCommand.StartStep(StepKind.BackgroundRemoval, Confirm(ready)));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var duplicate = scenario.Execute(new WorkflowCommand.StartStep(StepKind.BackgroundRemoval, Confirm(ready)));
            (await duplicate.WaitAsync(TimeSpan.FromSeconds(3))).IsFailure.ShouldBeTrue();
            scenario.Service.RequestStop(ready.Id, AutomationStopMode.StopOperation).IsSuccess.ShouldBeTrue();
        }
        finally
        {
            release.TrySetResult();
            await pending.WaitAsync(TimeSpan.FromSeconds(10));
        }

        (await pending).Failure.Code.ShouldBe(FailureCode.Cancelled);
        var attempt = (await scenario.ReadAttempts()).ShouldHaveSingleItem();
        attempt.Status.ShouldBe(AttemptStatus.Cancelled);
        attempt.OutputRevisionId.ShouldBeNull();
        scenario.Http.Calls.ShouldBe(1);
    }

    private static ImageUploadConfirmation Confirm(SessionView view) =>
        new(ImageProcessingMethod.OpenAiCombined, view.CurrentArtefact!.RevisionId, view.CurrentArtefact.Sha256,
            view.CurrentStep!.CombinedUploadPositionId);

    private sealed class Scenario : IDisposable
    {
        private SessionId _id;
        private readonly OpenAiImageBackend _backend;
        public SessionServiceHarness Harness { get; } = new();
        public ISessionService Service { get; }
        public ImageHttp Http { get; } = new();
        public UploadPrompt Prompt { get; } = new();
        public byte[] InputBytes { get; private set; } = [];

        public Scenario()
        {
            var workspace = new FileWorkspace(Harness.Workspace.Root);
            _backend = new OpenAiImageBackend(workspace, new Credentials(), OpenAiImageBackend.DefaultModel, Http);
            var local = new LocalEnhancementBackend(workspace, Path.Combine(Harness.Workspace.Root, LocalEnhancementBackend.ModelFileName));
            Service = Harness.CreateService(imageProcessingBackends: [_backend, local]);
        }

        public Task<OperationResult<SessionView>> Execute(WorkflowCommand command) =>
            Service.ExecuteAsync(_id, command, "qa", default);

        public async Task<SessionView> Load() => (await Service.LoadAsync(_id, default)).Value;

        public async Task<IReadOnlyList<ProcessingAttempt>> ReadAttempts() =>
            (await Harness.Repository.LoadAsync(_id, default)).Value!.Attempts
                .Where(attempt => attempt.Step is StepKind.Enhancement or StepKind.BackgroundRemoval).ToArray();

        public async Task<SessionView> SelectCombinedAsync(WorkflowType workflow)
        {
            string path = Harness.WriteSourcePng("render.png");
            InputBytes = await File.ReadAllBytesAsync(path);
            _id = (await Service.ImportAsync(workflow, path, null, "qa", default)).Value.Id;
            (await Execute(new WorkflowCommand.ConfirmOriginal())).IsSuccess.ShouldBeTrue();
            var selection = SessionImageProcessingSelection.Compatible;

            return (await Execute(new WorkflowCommand.SetImageProcessingSelection(StepKind.Enhancement,
                selection with { UseOpenAiCombined = true }))).Value;
        }

        public async Task<SessionView> ReachBackgroundAsync()
        {
            await SelectCombinedAsync(WorkflowType.PrepareAsset);
            return (await Execute(new WorkflowCommand.Skip(StepKind.Enhancement, "CombinedWithBackgroundRemoval"))).Value;
        }

        public void Dispose()
        {
            _backend.Dispose();
            Harness.Dispose();
        }
    }

    private sealed class UploadPrompt : IOpenAiUploadPrompt
    {
        public int Calls { get; private set; }
        public bool Accepted { get; set; }

        public bool Confirm(string operation)
        {
            Calls++;
            return Accepted;
        }
    }

    private sealed class Credentials : IOpenAiImageCredentials
    {
        public OperationResult<string?> Read() => OperationResult.Ok<string?>("sk-test-combined-image-processing");
        public OperationResult<PrintFlow.Domain.Results.Unit> Save(string key) => OperationResult.Ok();
        public OperationResult<PrintFlow.Domain.Results.Unit> Delete() => OperationResult.Ok();
    }

    private sealed class ImageHttp : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? Background { get; private set; }
        public string? Format { get; private set; }
        public byte[]? Image { get; private set; }
        public Func<Task<HttpResponseMessage>>? Respond { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            var parts = ((MultipartFormDataContent)request.Content!).ToDictionary(
                part => part.Headers.ContentDisposition!.Name!.Trim('"'));
            Background = await parts["background"].ReadAsStringAsync(cancellationToken);
            Format = await parts["output_format"].ReadAsStringAsync(cancellationToken);
            Image = await parts["image"].ReadAsByteArrayAsync(cancellationToken);
            return Respond is { } respond ? await respond() : Reply();
        }

        public HttpResponseMessage Reply(bool transparent = true)
        {
            using var image = new MagickImage(transparent ? MagickColors.Transparent : MagickColors.White, 24, 24);
            new Drawables().FillColor(MagickColors.Red).Rectangle(2, 2, 20, 20).Draw(image);
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    data = new[] { new { b64_json = Convert.ToBase64String(image.ToByteArray(MagickFormat.Png)) } },
                })),
            };
        }
    }
}
