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
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Settings;
using PrintFlow.Infrastructure.Adapters.OpenAI;
using PrintFlow.Infrastructure.Configuration;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;
using UnitResult = PrintFlow.Domain.Results.Unit;

namespace PrintFlow.Tests.Integration.Persistence;

[Collection(SqliteCollection.Name)]
public sealed class OpenAiImageBackendTests
{
    private const string TestKey = "sk-test-image-backend-secret";
    private const string Model = "gpt-image-2.6-sunburst";

    [Fact]
    public async Task Restore_requires_current_single_use_confirmation_and_records_review_evidence()
    {
        using var h = new SessionServiceHarness();
        byte[] png = Image("alpha");
        using var http = new ImageHttp(async (request, token) =>
        {
            request.RequestUri.ShouldBe(new Uri("https://api.openai.com/v1/images/edits"));
            request.Method.ShouldBe(HttpMethod.Post);
            request.Headers.Authorization!.Scheme.ShouldBe("Bearer");
            request.Headers.Authorization.Parameter.ShouldBe(TestKey);
            var parts = ((MultipartFormDataContent)request.Content!).ToDictionary(
                part => part.Headers.ContentDisposition!.Name!.Trim('"'));
            (await parts["model"].ReadAsStringAsync(token)).ShouldBe(Model);
            (await parts["output_format"].ReadAsStringAsync(token)).ShouldBe("png");
            (await parts["background"].ReadAsStringAsync(token)).ShouldBe("transparent");
            (await parts["n"].ReadAsStringAsync(token)).ShouldBe("1");
            (await parts["prompt"].ReadAsStringAsync(token)).ShouldContain("Restore");
            parts["image"].Headers.ContentDisposition!.FileName!.Trim('"').ShouldBe("input.png");
            (await parts["image"].ReadAsByteArrayAsync(token)).ShouldBe(png);
            return Reply(png);
        });
        var (service, session) = await Ready(h, http, png);
        var source = session.CurrentArtefact.ShouldNotBeNull();

        (await service.ExecuteAsync(session.Id, new WorkflowCommand.StartStep(StepKind.Enhancement), "qa", default))
            .Failure.MessageKey.ShouldBe("ImageProcessing_OpenAiConfirmationRequired");
        var wrong = new ImageUploadConfirmation(ImageProcessingMethod.OpenAiCutout, source.RevisionId, source.Sha256);
        (await service.ExecuteAsync(session.Id, new WorkflowCommand.StartStep(StepKind.Enhancement, wrong), "qa", default))
            .IsFailure.ShouldBeTrue();
        var wrongHash = new ImageUploadConfirmation(ImageProcessingMethod.OpenAiRestore, source.RevisionId,
            Sha256.FromBytes(new byte[32]));
        (await service.ExecuteAsync(session.Id, new WorkflowCommand.StartStep(StepKind.Enhancement, wrongHash), "qa", default))
            .IsFailure.ShouldBeTrue();
        http.Calls.ShouldBe(0);

        var consent = new ImageUploadConfirmation(ImageProcessingMethod.OpenAiRestore, source.RevisionId, source.Sha256);
        var command = new WorkflowCommand.StartStep(StepKind.Enhancement, consent);
        var result = await service.ExecuteAsync(session.Id, command, "qa", default);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.TechnicalDetail : null);
        result.Value.CurrentStep!.State.ShouldBe(StepState.ReviewRequired);
        var attempt = (await h.Repository.LoadAsync(session.Id, default)).Value!.Attempts
            .Single(a => a.Step == StepKind.Enhancement);
        attempt.ImageProcessingDetails!.AlgorithmVersion.ShouldBe(Model);
        attempt.ImageProcessingDetails.UploadConfirmedBy.ShouldBe("qa");
        attempt.ImageProcessingDetails.UploadConfirmedAtUtc.ShouldBe(h.Clock.GetUtcNow());
        attempt.AdapterNotes.ShouldNotBeNull();
        attempt.AdapterNotes.ShouldContain("req_test42");
        attempt.AdapterNotes.ShouldContain("total_tokens=12");
        attempt.AdapterNotes.ShouldNotContain(TestKey);
        attempt.AdapterNotes.ShouldNotContain("https://");
        consent.TryReserve(ImageProcessingMethod.OpenAiRestore, source.RevisionId, source.Sha256).ShouldBeFalse();
        http.Calls.ShouldBe(1);
    }

    [Theory]
    [InlineData("401", "ImageProcessing_OpenAiUnauthorised")]
    [InlineData("429", "ImageProcessing_OpenAiRateOrQuota")]
    [InlineData("offline", "ImageProcessing_OpenAiNetwork")]
    [InlineData("timeout", "ImageProcessing_OpenAiTimeout")]
    [InlineData("opaque", "ImageProcessing_OpenAiInvalidOutput")]
    [InlineData("transparent", "ImageProcessing_OpenAiInvalidOutput")]
    [InlineData("jpeg", "ImageProcessing_OpenAiInvalidOutput")]
    [InlineData("oversize", "ImageProcessing_OpenAiInvalidOutput")]
    [InlineData("url", "ImageProcessing_OpenAiInvalidOutput")]
    public async Task Failed_or_invalid_response_cannot_create_a_revision_or_be_replayed(string scenario, string key)
    {
        using var h = new SessionServiceHarness();
        using var http = new ImageHttp((_, _) => scenario switch
        {
            "401" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent(TestKey) }),
            "429" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent(TestKey) }),
            "offline" => throw new HttpRequestException(TestKey),
            "timeout" => throw new TaskCanceledException(TestKey),
            "url" => Task.FromResult(Reply(null)),
            _ => Task.FromResult(Reply(Image(scenario))),
        });
        var (service, session) = await Ready(h, http, Image("alpha"));
        var source = session.CurrentArtefact.ShouldNotBeNull();
        var consent = new ImageUploadConfirmation(ImageProcessingMethod.OpenAiRestore, source.RevisionId, source.Sha256);
        var command = new WorkflowCommand.StartStep(StepKind.Enhancement, consent);
        var result = await service.ExecuteAsync(session.Id, command, "qa", default);
        result.Failure.MessageKey.ShouldBe(key);
        JsonSerializer.Serialize(result.Failure).ShouldNotContain(TestKey);
        var persisted = (await h.Repository.LoadAsync(session.Id, default)).Value!;
        var attempt = persisted.Attempts.Single(a => a.Step == StepKind.Enhancement);
        attempt.OutputRevisionId.ShouldBeNull();
        if (key == "ImageProcessing_OpenAiInvalidOutput")
        {
            attempt.Failure!.Context["requestId"].ShouldBe("req_test42");
            attempt.Failure.Context["total_tokens"].ShouldBe("12");
        }
        (await service.ExecuteAsync(session.Id, new WorkflowCommand.Retry(StepKind.Enhancement), "qa", default))
            .IsSuccess.ShouldBeTrue();
        (await service.ExecuteAsync(session.Id, command, "qa", default))
            .Failure.MessageKey.ShouldBe("ImageProcessing_OpenAiConfirmationRequired");
        http.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Cloud_failure_can_hand_off_while_another_session_owns_the_desktop_lock()
    {
        using var h = new SessionServiceHarness();
        using var http = new ImageHttp((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)));
        var (service, session) = await Ready(h, http, Image("alpha"));
        var other = (await service.ImportAsync(WorkflowType.PrepareAsset, h.WriteSourcePng("other.png"), null, "qa", default)).Value;
        var holder = (await h.Repository.LoadAsync(other.Id, default)).Value!;
        (await h.Repository.CommitAsync(SessionMutation.Empty(holder.Session) with
        {
            LockChange = new AutomationLockChange(AutomationLockAction.Acquire, other.Id, h.Clock.GetUtcNow(),
                Environment.ProcessId, Environment.MachineName),
        }, default)).IsSuccess.ShouldBeTrue();

        var source = session.CurrentArtefact.ShouldNotBeNull();
        var consent = new ImageUploadConfirmation(ImageProcessingMethod.OpenAiRestore, source.RevisionId, source.Sha256);
        var attempt = await service.ExecuteAsync(session.Id, new WorkflowCommand.StartStep(StepKind.Enhancement, consent), "qa", default);
        attempt.Failure.MessageKey.ShouldBe("ImageProcessing_OpenAiRateOrQuota");
        var handoff = await service.ExecuteAsync(session.Id, new WorkflowCommand.HandOff(StepKind.Enhancement, "External correction"),
            "qa", default);
        handoff.IsSuccess.ShouldBeTrue(handoff.IsFailure ? handoff.Failure.TechnicalDetail : null);
        (await h.Repository.GetAutomationLockAsync(default)).Value.SessionId.ShouldBe(other.Id);
    }

    [Fact]
    public async Task Concurrent_starts_and_a_late_response_after_stop_do_not_publish_or_resend()
    {
        using var h = new SessionServiceHarness();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        byte[] png = Image("alpha");
        using var http = new ImageHttp(async (_, _) =>
        {
            entered.TrySetResult();
            await release.Task;
            return Reply(png);
        });
        var (service, session) = await Ready(h, http, png);
        var source = session.CurrentArtefact.ShouldNotBeNull();
        var consent = new ImageUploadConfirmation(ImageProcessingMethod.OpenAiRestore, source.RevisionId, source.Sha256);
        var command = new WorkflowCommand.StartStep(StepKind.Enhancement, consent);
        var first = service.ExecuteAsync(session.Id, command, "qa", default);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            (await service.ExecuteAsync(session.Id, command, "qa", default).WaitAsync(TimeSpan.FromSeconds(3)))
                .IsFailure.ShouldBeTrue();
            service.RequestStop(session.Id, AutomationStopMode.StopOperation).IsSuccess.ShouldBeTrue();
        }
        finally
        {
            release.TrySetResult();
            await first.WaitAsync(TimeSpan.FromSeconds(10));
        }

        (await first).Failure.Code.ShouldBe(FailureCode.Cancelled);
        var persisted = (await h.Repository.LoadAsync(session.Id, default)).Value!;
        persisted.Attempts.Single(a => a.Step == StepKind.Enhancement).OutputRevisionId.ShouldBeNull();
        http.Calls.ShouldBe(1);
    }

    [Theory]
    [InlineData(ImageProcessingMethod.OpenAiCutout, "Remove")]
    [InlineData(ImageProcessingMethod.OpenAiCombined, "Restore")]
    public async Task Cutout_and_combined_share_the_transparent_png_request(ImageProcessingMethod method, string prompt)
    {
        using var h = new SessionServiceHarness();
        var workspace = new FileWorkspace(h.Workspace.Root);
        var prepared = WorkspaceFileRef.Create("test/openai/source.png", WorkspaceArea.Working);
        var expected = WorkspaceFileRef.Create("test/openai/result.png", WorkspaceArea.Working);
        string sourcePath = workspace.ResolveAbsolute(prepared);
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        byte[] source = Image("opaque");
        File.WriteAllBytes(sourcePath, source);
        var revision = RevisionId.From(Guid.NewGuid());
        var sha = Sha256.FromBytes(System.Security.Cryptography.SHA256.HashData(source));
        Guid? position = method == ImageProcessingMethod.OpenAiCombined ? Guid.NewGuid() : null;
        var consent = new ImageUploadConfirmation(method, revision, sha, position);
        consent.TryReserve(method, revision, sha, position).ShouldBeTrue();
        var request = new ImageProcessingRequest(method, OperationKind.RemoveBackground, Model, revision, sha,
            prepared, expected, BackgroundRemovalAuthority.For(
                BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent, revision, sha))
            { UploadConfirmation = consent };

        using var http = new ImageHttp(async (message, token) =>
        {
            var parts = ((MultipartFormDataContent)message.Content!).ToDictionary(
                part => part.Headers.ContentDisposition!.Name!.Trim('"'));
            (await parts["background"].ReadAsStringAsync(token)).ShouldBe("transparent");
            (await parts["prompt"].ReadAsStringAsync(token)).ShouldContain(prompt);
            return Reply(Image("alpha"));
        });
        using var backend = new OpenAiImageBackend(workspace, new Credentials(), Model, http);
        var result = await backend.ProcessAsync(request, default);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.TechnicalDetail : null);
        File.Exists(workspace.ResolveAbsolute(expected)).ShouldBeTrue();
        (await backend.ProcessAsync(request, default)).IsFailure.ShouldBeTrue();
        http.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Cutout_screen_only_sends_after_the_operator_accepts_the_upload()
    {
        using var h = new SessionServiceHarness();
        using var http = new ImageHttp((_, _) => Task.FromResult(Reply(Image("alpha"))));
        using var backend = new OpenAiImageBackend(new FileWorkspace(h.Workspace.Root), new Credentials(), Model, http);
        var service = h.CreateService(imageProcessingBackends: [backend]);
        var imported = (await service.ImportAsync(WorkflowType.PrepareAsset, h.WriteSourcePng("cutout.png"), null, "qa", default)).Value;
        (await service.ExecuteAsync(imported.Id, new WorkflowCommand.ConfirmOriginal(), "qa", default)).IsSuccess.ShouldBeTrue();
        (await service.ExecuteAsync(imported.Id, new WorkflowCommand.Skip(StepKind.Enhancement), "qa", default)).IsSuccess.ShouldBeTrue();
        var selected = (await service.ExecuteAsync(imported.Id, new WorkflowCommand.SetImageProcessingSelection(
            StepKind.BackgroundRemoval, SessionImageProcessingSelection.Compatible with { BackgroundRemoval = ImageProcessingMethod.OpenAiCutout }),
            "qa", default)).Value;
        var source = selected.CurrentArtefact.ShouldNotBeNull();
        var ready = (await service.ExecuteAsync(imported.Id, new WorkflowCommand.SetBackgroundRemovalDecision(
            BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent, source.RevisionId, source.Sha256,
            Method: ImageProcessingMethod.OpenAiCutout), "qa", default)).Value;

        var prompt = new UploadPrompt();
        var screen = new SessionViewModel(service, h.Previews, h.TiffReviews, new RecordingNavigation(), openAiUploadPrompt: prompt);
        screen.Open(ready);
        screen.CanRunStep.ShouldBeTrue();
        await screen.RunStepCommand.ExecuteAsync(null);
        prompt.Calls.ShouldBe(1);
        http.Calls.ShouldBe(0);

        prompt.Accepted = true;
        await screen.RunStepCommand.ExecuteAsync(null);
        http.Calls.ShouldBe(1);
        (await service.LoadAsync(imported.Id, default)).Value.CurrentStep!.State.ShouldBe(StepState.ReviewRequired);
    }

    [Fact]
    public async Task Credential_controls_update_method_availability_without_persisting_the_key_in_settings()
    {
        using var h = new SessionServiceHarness();
        var credential = new WindowsOpenAiImageCredentials($"PrintFlow Studio/tests/{Guid.NewGuid():N}");
        var config = PrintFlowConfiguration.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"))
            with { Adapters = new AdaptersConfiguration("Fake") };
        using var provider = ServiceRegistration.BuildServiceProvider(config, h.Workspace.Root, h.Database.Factory, services =>
        {
            services.AddSingleton<IOpenAiImageCredentials>(credential);
            services.AddSingleton<INavigationService, RecordingNavigation>();
        });
        try
        {
            var screen = provider.GetRequiredService<SettingsViewModel>();
            await screen.OpenAsync(default);
            await screen.OpenImageProcessingCommand.ExecuteAsync(null);
            screen.ShowOpenAiCredentials.ShouldBeTrue();
            screen.EnhancementMethods.Single(m => m.Method == ImageProcessingMethod.OpenAiRestore).IsAvailable.ShouldBeFalse();
            string missing = screen.OpenAiCredentialStatus;

            screen.SelectedEnhancementMethod = ImageProcessingMethod.OpenAiRestore;
            screen.SaveImageProcessingCommand.CanExecute(null).ShouldBeFalse();
            screen.SaveOpenAiCredential(TestKey);
            screen.OpenAiCredentialStatus.ShouldNotBe(missing);
            screen.OpenAiCredentialStatus.ShouldNotContain(TestKey);
            screen.SelectedEnhancementMethod.ShouldBe(ImageProcessingMethod.OpenAiRestore);
            screen.SaveImageProcessingCommand.CanExecute(null).ShouldBeTrue();
            await screen.SaveImageProcessingCommand.ExecuteAsync(null);
            JsonSerializer.Serialize((await h.Settings.ReadAllAsync(default)).Value).ShouldNotContain(TestKey);

            screen.RemoveOpenAiCredentialCommand.Execute(null);
            screen.OpenAiCredentialStatus.ShouldBe(missing);
            screen.SelectedEnhancementMethod.ShouldBe(ImageProcessingMethod.OpenAiRestore);
            screen.SaveImageProcessingCommand.CanExecute(null).ShouldBeFalse();
        }
        finally
        {
            credential.Delete();
        }
    }

    [Fact]
    public void Windows_credentials_round_trip_with_an_isolated_target()
    {
        var credentials = new WindowsOpenAiImageCredentials($"PrintFlow Studio/tests/{Guid.NewGuid():N}");
        try
        {
            credentials.Read().Value.ShouldBeNull();
            credentials.Save(TestKey).IsSuccess.ShouldBeTrue();
            credentials.Read().Value.ShouldBe(TestKey);
            credentials.Delete().IsSuccess.ShouldBeTrue();
            credentials.Read().Value.ShouldBeNull();
        }
        finally
        {
            credentials.Delete();
        }
    }

    private static async Task<(ISessionService Service, SessionView Session)> Ready(
        SessionServiceHarness harness, ImageHttp http, byte[] png)
    {
        var backend = new OpenAiImageBackend(new FileWorkspace(harness.Workspace.Root), new Credentials(), Model, http);
        var service = harness.CreateService(imageProcessingBackends: [backend]);
        var path = harness.WriteSourcePng("customer-name.png");
        File.WriteAllBytes(path, png);
        var imported = (await service.ImportAsync(WorkflowType.PrepareAsset, path, null, "qa", default)).Value;
        (await service.ExecuteAsync(imported.Id, new WorkflowCommand.ConfirmOriginal(), "qa", default)).IsSuccess.ShouldBeTrue();
        var selected = await service.ExecuteAsync(imported.Id, new WorkflowCommand.SetImageProcessingSelection(
            StepKind.Enhancement, SessionImageProcessingSelection.Compatible with { Enhancement = ImageProcessingMethod.OpenAiRestore }),
            "qa", default);
        return (service, selected.Value);
    }

    private static HttpResponseMessage Reply(byte[]? png)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                data = new[] { new { b64_json = png is null ? null : Convert.ToBase64String(png), url = "https://example.test/private" } },
                usage = new { total_tokens = 12, input_tokens = 7, output_tokens = 5 },
            })),
        };
        response.Headers.Add("x-request-id", "req_test42");
        return response;
    }

    private static byte[] Image(string shape)
    {
        uint width = shape == "oversize" ? 5000u : 24u;
        using var image = new MagickImage(shape is "opaque" or "jpeg" ? MagickColors.White : MagickColors.Transparent, width, 24);
        if (shape is "alpha" or "oversize")
            new Drawables().FillColor(MagickColors.Red).Rectangle(2, 2, 20, 20).Draw(image);
        return image.ToByteArray(shape == "jpeg" ? MagickFormat.Jpeg : MagickFormat.Png);
    }

    private sealed class UploadPrompt : IOpenAiUploadPrompt
    {
        public bool Accepted { get; set; }
        public int Calls { get; private set; }

        public bool Confirm(string operation)
        {
            Calls++;
            return Accepted;
        }
    }

    private sealed class Credentials : IOpenAiImageCredentials
    {
        public OperationResult<string?> Read() => OperationResult.Ok<string?>(TestKey);
        public OperationResult<UnitResult> Save(string key) => OperationResult.Ok();
        public OperationResult<UnitResult> Delete() => OperationResult.Ok();
    }

    private sealed class ImageHttp(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return respond(request, cancellationToken);
        }
    }
}
