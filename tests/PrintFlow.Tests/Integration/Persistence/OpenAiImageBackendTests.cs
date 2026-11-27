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
using static PrintFlow.Tests.Fixtures.ResponsesImageFixture;
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
            request.Headers.Authorization!.Scheme.ShouldBe("Bearer");
            request.Headers.Authorization.Parameter.ShouldBe(TestKey);
            var submitted = await ReadAsync(request, token);
            submitted.Format.ShouldBe("png");
            submitted.Background.ShouldBe("transparent");
            submitted.Size.ShouldBe("auto");
            string prompt = submitted.Prompt;
            prompt.ShouldContain("Improve only the clarity and sharpness");
            prompt.ShouldContain("Preserve the entire original background");
            prompt.ShouldContain("Keep all existing transparent areas");
            prompt.ShouldContain("cut out or isolate the subject");
            prompt.ShouldContain("Do not add objects, retouch faces");
            prompt.ShouldContain("Keep the source canvas at 24 × 24 pixels");
            submitted.Image.ShouldBe(png);
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
        attempt.AdapterNotes.ShouldContain("resp_test42");
        attempt.AdapterNotes.ShouldContain("ig_test0");
        attempt.AdapterNotes.ShouldContain("total_tokens=12");
        attempt.AdapterNotes.ShouldNotContain(TestKey);
        attempt.AdapterNotes.ShouldNotContain("https://");
        attempt.AdapterNotes.ShouldNotContain("Private text recovered from an image");
        consent.TryReserve(ImageProcessingMethod.OpenAiRestore, source.RevisionId, source.Sha256).ShouldBeFalse();
        http.Calls.ShouldBe(1);
    }

    [Theory]
    [InlineData(1122, 1402, 2240, 2800)]
    [InlineData(2000, 2000, 2880, 2880)]
    [InlineData(2200, 800, 3840, 1392)]
    public async Task Restore_requests_a_proportional_size_up_to_twice_the_input(
        int width, int height, int expectedWidth, int expectedHeight)
    {
        using var h = new SessionServiceHarness();
        using var http = new ImageHttp(async (request, token) =>
        {
            var submitted = await ReadAsync(request, token);
            submitted.Size.ShouldBe($"{expectedWidth}x{expectedHeight}");
            submitted.Background.ShouldBe("opaque");
            return Reply(Image("opaque", (uint)expectedWidth, (uint)expectedHeight));
        });
        var (service, session) = await Ready(h, http, Image("opaque", (uint)width, (uint)height));
        var source = session.CurrentArtefact.ShouldNotBeNull();
        var confirmation = new ImageUploadConfirmation(ImageProcessingMethod.OpenAiRestore, source.RevisionId, source.Sha256);
        var result = await service.ExecuteAsync(session.Id, new WorkflowCommand.StartStep(StepKind.Enhancement, confirmation), "qa", default);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.TechnicalDetail : null);
        result.Value.CurrentStep!.State.ShouldBe(StepState.ReviewRequired);
        http.Calls.ShouldBe(1);
    }

    [Theory]
    [InlineData(448, 512, "opaque", 24, 24)]
    [InlineData(448, 512, "alpha", 896, 1024)]
    [InlineData(24, 24, "opaque", 48, 48)]
    public async Task Restore_rejects_responses_that_ignore_requested_size_or_add_transparency(
        int inputWidth, int inputHeight, string shape, int width, int height)
    {
        using var h = new SessionServiceHarness();
        using var http = new ImageHttp((_, _) => Task.FromResult(Reply(Image(shape, (uint)width, (uint)height))));
        var (service, session) = await Ready(h, http, Image("opaque", (uint)inputWidth, (uint)inputHeight));
        var source = session.CurrentArtefact.ShouldNotBeNull();
        var confirmation = new ImageUploadConfirmation(ImageProcessingMethod.OpenAiRestore, source.RevisionId, source.Sha256);
        var result = await service.ExecuteAsync(session.Id, new WorkflowCommand.StartStep(StepKind.Enhancement, confirmation), "qa", default);

        result.IsFailure.ShouldBeTrue();
        result.Failure.MessageKey.ShouldBe("ImageProcessing_OpenAiInvalidOutput");
        http.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Restore_preserves_the_scaled_original_alpha_where_the_model_moved_it()
    {
        using var h = new SessionServiceHarness();
        byte[] input = Image("alpha", 448, 512);
        using var http = new ImageHttp((_, _) => Task.FromResult(Reply(Image("alpha", 896, 1024))));
        var (service, session) = await Ready(h, http, input);
        var source = session.CurrentArtefact.ShouldNotBeNull();
        var confirmation = new ImageUploadConfirmation(ImageProcessingMethod.OpenAiRestore, source.RevisionId, source.Sha256);
        var result = await service.ExecuteAsync(session.Id, new WorkflowCommand.StartStep(StepKind.Enhancement, confirmation), "qa", default);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.TechnicalDetail : null);
        var aggregate = (await h.Repository.LoadAsync(session.Id, default)).Value!;
        var attempt = aggregate.Attempts.Single(a => a.Step == StepKind.Enhancement);
        var candidate = aggregate.Revisions.Single(revision => revision.Id == attempt.OutputRevisionId);
        using var rendered = new MagickImage(new FileWorkspace(h.Workspace.Root).ResolveAbsolute(candidate.File));
        using var pixels = rendered.GetPixels();
        pixels.ToByteArray(2, 2, 1, 1, "A").ShouldBe([0]);
        pixels.ToByteArray(35, 35, 1, 1, "RGBA").ShouldBe([255, 0, 0, 255]);
    }

    [Theory]
    [InlineData(4000, 2016, "auto", 4000, 2016, true)]
    [InlineData(3200, 3000, "auto", 3200, 3000, true)]
    [InlineData(3840, 2160, "3840x2160", 3840, 2160, true)]
    [InlineData(2000, 2000, "2880x2880", 2000, 2000, true)]
    [InlineData(4000, 2016, "auto", 3808, 1920, false)]
    public async Task Restore_accepts_the_exact_original_dimensions_when_the_source_is_too_large_for_two_times_enlargement(
        uint inputWidth, uint inputHeight, string requestedSize, uint outputWidth, uint outputHeight, bool accepted)
    {
        using var h = new SessionServiceHarness();
        using var http = new ImageHttp(async (request, token) =>
        {
            var submitted = await ReadAsync(request, token);
            submitted.Size.ShouldBe(requestedSize);
            submitted.Prompt.ShouldContain(requestedSize is "auto" or "3840x2160" ?
                "Keep the source canvas" : "Scale the full frame");
            return Reply(Image("opaque", outputWidth, outputHeight));
        });
        var (service, session) = await Ready(h, http, Image("opaque", inputWidth, inputHeight));
        var source = session.CurrentArtefact.ShouldNotBeNull();
        var confirmation = new ImageUploadConfirmation(ImageProcessingMethod.OpenAiRestore, source.RevisionId, source.Sha256);

        var result = await service.ExecuteAsync(session.Id, new WorkflowCommand.StartStep(StepKind.Enhancement, confirmation), "qa", default);

        http.Calls.ShouldBe(1);
        result.IsSuccess.ShouldBe(accepted, result.IsFailure ? result.Failure.TechnicalDetail : null);
        if (accepted)
            result.Value.CurrentStep!.State.ShouldBe(StepState.ReviewRequired);
        else
            result.Failure.MessageKey.ShouldBe("ImageProcessing_OpenAiInvalidOutput");
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
    [InlineData("incomplete_response", "ImageProcessing_OpenAiInvalidOutput")]
    [InlineData("incomplete_image", "ImageProcessing_OpenAiInvalidOutput")]
    [InlineData("multiple_images", "ImageProcessing_OpenAiInvalidOutput")]
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
            "incomplete_response" => Task.FromResult(Reply(Image("alpha"), status: "incomplete")),
            "incomplete_image" => Task.FromResult(Reply(Image("alpha"), imageStatus: "in_progress")),
            "multiple_images" => Task.FromResult(Reply(Image("alpha"), imageCount: 2)),
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
    [InlineData(2496)]
    [InlineData(1254)]
    public async Task Separate_cutout_preserves_the_approved_enhancement_canvas(int returnedEdge)
    {
        using var h = new SessionServiceHarness();
        byte[] enhanced = Image("opaque", 2496, 2496);
        string? cutoutRequestSize = null;
        using var http = new ImageHttp(async (request, token) =>
        {
            var submitted = await ReadAsync(request, token);
            if (submitted.Background == "opaque")
                return Reply(enhanced);
            cutoutRequestSize = submitted.Size;
            submitted.Image.ShouldBe(enhanced);
            return Reply(Image("alpha", (uint)returnedEdge, (uint)returnedEdge));
        });
        var (service, ready) = await Ready(h, http, Image("opaque", 1254, 1254));
        var original = ready.CurrentArtefact.ShouldNotBeNull();
        var enhancement = await service.ExecuteAsync(ready.Id, new WorkflowCommand.StartStep(StepKind.Enhancement,
            new ImageUploadConfirmation(ImageProcessingMethod.OpenAiRestore, original.RevisionId, original.Sha256)), "qa", default);
        var approvedInput = enhancement.Value.CurrentArtefact.ShouldNotBeNull();
        (approvedInput.Facts.PixelWidth, approvedInput.Facts.PixelHeight).ShouldBe((2496, 2496));
        var approved = await service.ExecuteAsync(ready.Id,
            new WorkflowCommand.Approve(StepKind.Enhancement, approvedInput.Sha256), "qa", default);
        var selected = await service.ExecuteAsync(ready.Id, new WorkflowCommand.SetImageProcessingSelection(
            StepKind.BackgroundRemoval, approved.Value.ImageProcessingSelection! with
            { BackgroundRemoval = ImageProcessingMethod.OpenAiCutout }), "qa", default);
        var input = selected.Value.CurrentArtefact.ShouldNotBeNull();
        var authorised = await service.ExecuteAsync(ready.Id, new WorkflowCommand.SetBackgroundRemovalDecision(
            BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent, input.RevisionId, input.Sha256,
            Method: ImageProcessingMethod.OpenAiCutout), "qa", default);
        authorised.IsSuccess.ShouldBeTrue();
        var cutout = await service.ExecuteAsync(ready.Id, new WorkflowCommand.StartStep(StepKind.BackgroundRemoval,
            new ImageUploadConfirmation(ImageProcessingMethod.OpenAiCutout, input.RevisionId, input.Sha256)), "qa", default);

        var current = (await service.LoadAsync(ready.Id, default)).Value;
        if (returnedEdge == 2496)
        {
            cutout.IsSuccess.ShouldBeTrue();
            current.CurrentStep!.State.ShouldBe(StepState.ReviewRequired);
            var result = current.CurrentArtefact.ShouldNotBeNull();
            (result.Facts.PixelWidth, result.Facts.PixelHeight).ShouldBe((2496, 2496));
        }
        else
        {
            cutout.IsFailure.ShouldBeTrue("the provider returned a smaller canvas than the approved enhancement");
            cutout.Failure.MessageKey.ShouldBe("ImageProcessing_OpenAiCutoutCanvasChanged");
            current.CurrentStep!.State.ShouldBe(StepState.Failed);
            current.CurrentArtefact!.RevisionId.ShouldBe(approvedInput.RevisionId);
        }
        cutoutRequestSize.ShouldBe("2496x2496");
        http.Calls.ShouldBe(2);
    }

    [Theory]
    [InlineData(ImageProcessingMethod.OpenAiCutout, 1122, 1402, "auto", 1122, 1402, true, 1)]
    [InlineData(ImageProcessingMethod.OpenAiCutout, 4000, 2016, "auto", 4000, 2016, false, 0)]
    [InlineData(ImageProcessingMethod.OpenAiCombined, 1122, 1402, "auto", 2240, 2800, true, 1)]
    [InlineData(ImageProcessingMethod.OpenAiCombined, 1122, 1402, "auto", 1122, 1402, true, 1)]
    [InlineData(ImageProcessingMethod.OpenAiCombined, 4000, 2016, "auto", 4000, 2016, true, 1)]
    [InlineData(ImageProcessingMethod.OpenAiCombined, 2000, 2000, "auto", 4096, 4096, true, 1)]
    public async Task Cutout_and_combined_request_transparency_with_their_respective_size_policy(
        ImageProcessingMethod method, uint inputWidth, uint inputHeight, string requestedSize,
        uint responseWidth, uint responseHeight, bool accepted, int expectedCalls)
    {
        using var h = new SessionServiceHarness();
        var workspace = new FileWorkspace(h.Workspace.Root);
        var prepared = WorkspaceFileRef.Create("test/openai/source.png", WorkspaceArea.Working);
        var expected = WorkspaceFileRef.Create("test/openai/result.png", WorkspaceArea.Working);
        string sourcePath = workspace.ResolveAbsolute(prepared);
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        byte[] source = Image("opaque", inputWidth, inputHeight);
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
            var submitted = await ReadAsync(message, token);
            submitted.Background.ShouldBe("transparent");
            submitted.Size.ShouldBe(requestedSize);
            string prompt = submitted.Prompt;
            prompt.ShouldContain("remove only the background");
            prompt.ShouldContain("T-shirt mockup");
            prompt.ShouldContain("make the entire shirt transparent");
            prompt.ShouldContain("Keep the printed artwork and lettering");
            prompt.ShouldContain("Do not add objects, retouch faces");
            return Reply(Image("alpha", responseWidth, responseHeight));
        });
        using var backend = new OpenAiImageBackend(workspace, new Credentials(), Model, http);
        var result = await backend.ProcessAsync(request, default);
        result.IsSuccess.ShouldBe(accepted, result.IsFailure ? result.Failure.TechnicalDetail : null);
        if (!accepted)
            result.Failure.MessageKey.ShouldBe(expectedCalls == 0
                ? "ImageProcessing_OpenAiCutoutCanvasUnsupported" : "ImageProcessing_OpenAiInvalidOutput");
        File.Exists(workspace.ResolveAbsolute(expected)).ShouldBe(accepted);
        (await backend.ProcessAsync(request, default)).IsFailure.ShouldBeTrue();
        http.Calls.ShouldBe(expectedCalls);
    }

    [Fact]
    public async Task Cutout_screen_only_sends_after_the_operator_accepts_the_upload()
    {
        using var h = new SessionServiceHarness();
        using var http = new ImageHttp((_, _) => Task.FromResult(Reply(Image("alpha"))));
        using var backend = new OpenAiImageBackend(new FileWorkspace(h.Workspace.Root), new Credentials(), Model, http);
        var service = h.CreateService(imageProcessingBackends: [backend]);
        string inputPath = h.Workspace.CreateSourceFile("cutout.png", Image("opaque"));
        var imported = (await service.ImportAsync(WorkflowType.PrepareAsset, inputPath, null, "qa", default)).Value;
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
    public async Task Separate_cutout_preserves_the_complete_returned_png()
    {
        using var h = new SessionServiceHarness();
        byte[] source = SyntheticImages.PngWithAlpha(6, 5, (x, _) => x switch
        {
            0 => 0, 2 => 128, _ => 255,
        }, colour: (128, 64, 32));
        byte[] generated = SyntheticImages.PngWithAlpha(6, 5, (x, _) => x switch
        {
            0 => 0, 2 => 128, 3 => 0, _ => 255,
        }, colour: (250, 5, 220));
        using var http = new ImageHttp(async (request, token) =>
        {
            string wording = (await ReadAsync(request, token)).Prompt;
            wording.ShouldContain("Do not add objects");
            wording.ShouldContain("retouch faces");
            wording.ShouldContain("exact pixel alignment");
            return Reply(generated);
        });
        var workspace = new FileWorkspace(h.Workspace.Root);
        using var backend = new OpenAiImageBackend(workspace, new Credentials(), Model, http);
        var service = h.CreateService(imageProcessingBackends: [backend]);
        var waiting = await CorrectionFixtures.AtBackgroundRemovalWaitingAsync(
            service, h.Workspace.CreateSourceFile("cutout-source.png", source));
        var selected = await service.ExecuteAsync(waiting.Id, new WorkflowCommand.SetImageProcessingSelection(
            StepKind.BackgroundRemoval, SessionImageProcessingSelection.Compatible with
            { BackgroundRemoval = ImageProcessingMethod.OpenAiCutout }), "qa", default);
        var input = selected.Value.CurrentArtefact.ShouldNotBeNull();
        var authorised = await service.ExecuteAsync(waiting.Id, new WorkflowCommand.SetBackgroundRemovalDecision(
            BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent, input.RevisionId, input.Sha256,
            Method: ImageProcessingMethod.OpenAiCutout), "qa", default);
        authorised.IsSuccess.ShouldBeTrue();
        var result = await service.ExecuteAsync(waiting.Id, new WorkflowCommand.StartStep(StepKind.BackgroundRemoval,
            new ImageUploadConfirmation(ImageProcessingMethod.OpenAiCutout, input.RevisionId, input.Sha256)), "qa", default);

        var review = result.Value.CurrentArtefact.ShouldNotBeNull();
        var aggregate = (await h.Repository.LoadAsync(waiting.Id, default)).Value!;
        var saved = aggregate.Revisions.Single(r => r.Id == review.RevisionId).File;
        File.ReadAllBytes(workspace.ResolveAbsolute(saved)).ShouldBe(generated);
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

    private static byte[] Image(string shape, uint width = 24, uint height = 24)
    {
        if (shape == "oversize")
            width = 5000;
        using var image = new MagickImage(shape is "opaque" or "jpeg" ? MagickColors.White : MagickColors.Transparent, width, height);
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
