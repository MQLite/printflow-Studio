using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using PrintFlow.App.ViewModels.AiDesign;
using PrintFlow.Domain.AiDesign;
using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.AiDesign;
using PrintFlow.Infrastructure.AiDesign.Authoring;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.AiDesign;
using PrintFlow.Workflow.AiDesign.Authoring;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Ports;
using ResultUnit = PrintFlow.Domain.Results.Unit;

namespace PrintFlow.Tests.Integration.AiDesign;

[Collection(SqliteCollection.Name)]
public sealed class OpenAiDesignGenerationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Main_app_generates_and_reviews_via_openai_then_imports_png_and_pdf(bool withReference)
    {
        using var harness = new SessionServiceHarness();
        var (draft, reference) = await CreateDraftAsync(harness, withReference);
        byte[] png = SyntheticImages.Png(20, 12);
        using var transport = new ImageTransport(HttpStatusCode.OK, JsonSerializer.Serialize(new
        {
            id = "resp_test",
            status = "completed",
            usage = new { input_tokens = 123, output_tokens = 456, total_tokens = 579 },
            output = new[]
            {
                new { id = "ig_test", type = "image_generation_call", status = "completed", result = Convert.ToBase64String(png) }
            }
        }));
        using var generation = new OpenAiDesignGeneration(harness.Workspace.Root, new Credentials("test-key"),
            "test-image-model", harness.FileInspector, harness.CreateService(), transport);

        var generated = await generation.GenerateAsync(draft, default);

        generated.IsSuccess.ShouldBeTrue(generated.IsFailure ? generated.Failure.TechnicalDetail : null);
        transport.Calls.ShouldBe(2);
        transport.Path.ShouldBe("/v1/responses");
        transport.Key.ShouldBe("test-key");
        var body = transport.GenerationJson;
        body.GetProperty("model").GetString().ShouldBe("gpt-6.2-sol");
        body.GetProperty("store").GetBoolean().ShouldBeTrue();
        body.GetProperty("max_tool_calls").GetInt32().ShouldBe(1);
        body.GetProperty("reasoning").GetProperty("effort").GetString().ShouldBe("low");
        body.GetProperty("tool_choice").GetProperty("type").GetString().ShouldBe("image_generation");
        var tool = body.GetProperty("tools").EnumerateArray().ShouldHaveSingleItem();
        tool.GetProperty("model").GetString().ShouldBe("test-image-model");
        tool.GetProperty("action").GetString().ShouldBe(withReference ? "auto" : "generate");
        var input = body.GetProperty("input")[0].GetProperty("content");
        input[0].GetProperty("text").GetString()!.ShouldContain("Create a blue floral border");
        input.GetArrayLength().ShouldBe(withReference ? 3 : 1);
        if (reference is not null)
        {
            input[1].GetProperty("text").GetString()!.ShouldContain("attachment-01.png");
            input[2].GetProperty("image_url").GetString().ShouldBe("data:image/png;base64," + Convert.ToBase64String(reference));
        }

        var store = new FileAiDesignRequestStore(harness.Workspace.Root);
        var request = (await store.ReadAsync(generated.Value, default)).Value;
        request.State.ShouldBe(AiDesignState.Imported);
        request.Provider.ShouldBe(AiDesignProvider.OpenAiApi);
        request.WorkDraft!.DraftId.ShouldBe(draft.Id);
        request.WorkConversationId.ShouldBeNull();
        request.OpenAiReceipt.ShouldBe(new OpenAiDesignReceipt("resp_test", "req_test", "ig_test", 123, 456, 579));
        request.CloudReceipt!.Attempts.ShouldHaveSingleItem().Review.ShouldNotBeNull().ResponseId.ShouldBe("resp_review_test");
        transport.ReviewJson.GetProperty("text").GetProperty("format").GetProperty("strict").GetBoolean().ShouldBeTrue();
        string pdf = System.IO.Path.Combine(harness.Workspace.Root, "reviewed-design.pdf");
        (await new CloudDesignPdfExport(store).SaveAsync(generated.Value, pdf, default)).IsSuccess.ShouldBeTrue();
        var stored = (await harness.Repository.LoadAsync(request.ImportedSessionId!.Value, default)).Value!;
        stored.Revisions.ShouldHaveSingleItem().Sha256.ShouldBe(request.Candidate!.Facts.Sha256);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.OK, "ImageProcessing_OpenAiKeyMissing", 0)]
    [InlineData("test-key", HttpStatusCode.TooManyRequests, "ImageProcessing_OpenAiRateOrQuota", 1)]
    [InlineData("test-key", HttpStatusCode.OK, "AiPrompt_OpenAiInvalidResult", 1)]
    public async Task Missing_key_api_failure_and_invalid_image_do_not_publish_a_result(
        string? key, HttpStatusCode status, string message, int expectedCalls)
    {
        using var harness = new SessionServiceHarness();
        var (draft, _) = await CreateDraftAsync(harness, false);
        using var transport = new ImageTransport(status,
            """{"id":"resp_test","status":"completed","output":[{"type":"image_generation_call","id":"ig_test","status":"completed","result":"bm90IGEgcG5n"}]}""");
        using var generation = new OpenAiDesignGeneration(harness.Workspace.Root, new Credentials(key),
            "test-image-model", harness.FileInspector, harness.CreateService(), transport);

        var result = await generation.GenerateAsync(draft, default);

        result.IsFailure.ShouldBeTrue();
        result.Failure.MessageKey.ShouldBe(message);
        transport.Calls.ShouldBe(expectedCalls);
        (await new FileAiDesignRequestStore(harness.Workspace.Root).ListAsync(default)).Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task Direct_portrait_generation_retains_a_candidate_when_the_face_cannot_be_located()
    {
        using var harness = new SessionServiceHarness();
        var (draft, _) = await CreateDraftAsync(harness, withReference: true, portrait: true);
        byte[] png = SyntheticImages.Png(20, 12);
        using var transport = new ImageTransport(HttpStatusCode.OK, JsonSerializer.Serialize(new
        {
            id = "resp_test",
            status = "completed",
            output = new[] { new { id = "ig_test", type = "image_generation_call", status = "completed",
                result = Convert.ToBase64String(png) } }
        }));
        using var generator = new OpenAiDesignGeneration(harness.Workspace.Root, new Credentials("test-key"),
            "test-image-model", harness.FileInspector, harness.CreateService(), transport);

        var result = await generator.GenerateAsync(draft, default);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.TechnicalDetail : null);
        var request = (await new FileAiDesignRequestStore(harness.Workspace.Root).ReadAsync(result.Value, default)).Value;
        request.ManualReview.ShouldNotBeNull();
        request.ManualReview.Reason.ShouldNotBeNullOrWhiteSpace();
        request.CloudReceipt.ShouldBeNull();
    }

    [Fact]
    public async Task Main_app_retries_once_on_review_failure_and_does_not_import_an_unapproved_design()
    {
        using var harness = new SessionServiceHarness();
        var (draft, _) = await CreateDraftAsync(harness, false);
        const string reason = "The border is red, but the brief requires blue.";
        using var transport = new ImageTransport(HttpStatusCode.OK, JsonSerializer.Serialize(new
        {
            id = "resp_test",
            status = "completed",
            output = new[]
            {
                new { id = "ig_test", type = "image_generation_call", status = "completed",
                    result = Convert.ToBase64String(SyntheticImages.Png(20, 12)) }
            }
        }), reason);
        using var generation = new OpenAiDesignGeneration(harness.Workspace.Root, new Credentials("test-key"),
            "test-image-model", harness.FileInspector, harness.CreateService(), transport);

        var outcome = await generation.GenerateAsync(draft, default);

        transport.Calls.ShouldBe(4);
        transport.GenerationJson.GetProperty("previous_response_id").GetString().ShouldBe("resp_test");
        transport.GenerationJson.GetProperty("input")[0].GetProperty("content")[0].GetProperty("text").GetString()!
            .ShouldContain(reason);
        outcome.Failure.Context[CloudDesignLimits.ReviewReasonKey].ShouldBe(reason);
        (await new FileAiDesignRequestStore(harness.Workspace.Root).ListAsync(default)).Value.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Cloud_generation_imports_only_approved_results_and_saves_the_exact_returned_pdf(bool approved)
    {
        using var harness = new SessionServiceHarness();
        var (draft, _) = await CreateDraftAsync(harness, true);
        byte[] png = SyntheticImages.Png(20, 12);
        byte[] pdf = "%PDF-1.4\ncloud payload"u8.ToArray();
        var attempt = new CloudDesignAttempt(new("resp_generated", null, "ig_design", 1, 2, 3, 20),
            new("resp_reviewed", null, null, 3, 4, 7));
        var job = new CloudDesignJob(approved ? CloudDesignStage.Approved : CloudDesignStage.Rejected, [attempt],
            approved ? png : null, approved ? pdf : null, approved ? null : "Missing the requested blue border.");
        using var transport = new CloudTransport(job);
        using var generator = new OpenAiDesignGeneration(harness.Workspace.Root, new Credentials(null),
            "test-image-model", harness.FileInspector, harness.CreateService(), transport, "https://cloud.example",
            () => "test_cloud_token_for_authorized_clients");

        var result = await generator.GenerateAsync(draft, default);
        transport.Methods.ShouldBe([HttpMethod.Post, HttpMethod.Get, HttpMethod.Delete]);
        transport.SentReference!.PreserveFace.ShouldBeFalse();
        var store = new FileAiDesignRequestStore(harness.Workspace.Root);
        if (!approved)
        {
            result.Failure.MessageKey.ShouldBe("AiPrompt_CloudRejected");
            result.Failure.Context[CloudDesignLimits.ReviewReasonKey].ShouldBe(job.Reason);
            (await store.ListAsync(default)).Value.ShouldBeEmpty();
            return;
        }

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.TechnicalDetail : null);
        var request = (await store.ReadAsync(result.Value, default)).Value;
        request.State.ShouldBe(AiDesignState.Imported);
        request.CloudReceipt!.Attempts.ShouldHaveSingleItem().ShouldBe(attempt);
        string output = System.IO.Path.Combine(harness.Workspace.Root, "saved-cloud-design.pdf");
        var export = new CloudDesignPdfExport(store);
        (await export.SaveAsync(result.Value, output, default)).IsSuccess.ShouldBeTrue();
        (await System.IO.File.ReadAllBytesAsync(output)).ShouldBe(pdf);
        (await export.SaveAsync(result.Value, output, default)).IsFailure.ShouldBeTrue();
        (await System.IO.File.ReadAllBytesAsync(output)).ShouldBe(pdf);
    }

    [Theory]
    [InlineData(CloudDesignStage.NeedsReview, "uncertain", null, false)]
    [InlineData(CloudDesignStage.NeedsReview, "changed", "resp_composite", false)]
    [InlineData(CloudDesignStage.Approved, null, null, false)]
    [InlineData(CloudDesignStage.Approved, "uncertain", null, false)]
    [InlineData(CloudDesignStage.Approved, "changed", null, false)]
    [InlineData(CloudDesignStage.Approved, "changed", "invalid_id", false)]
    [InlineData(CloudDesignStage.Approved, "changed", "resp_reviewed", false)]
    [InlineData(CloudDesignStage.Approved, "changed", "resp_first_review", false)]
    [InlineData(CloudDesignStage.Approved, "preserved", null, true)]
    [InlineData(CloudDesignStage.Approved, "changed", "resp_composite", true)]
    public async Task Portrait_reference_requires_evidence_on_final_attempt_before_pdf_export(
        CloudDesignStage stage, string? faceStatus, string? compositeId, bool exportable)
    {
        using var harness = new SessionServiceHarness();
        var sessions = harness.CreateService();
        var (draft, _) = await CreateDraftAsync(harness, withReference: true, portrait: true);
        var last = new CloudDesignAttempt(new("resp_generated", null, "ig_design", 1, 2, 3),
            new("resp_reviewed", null, null, 3, 4, 7), FaceStatus: faceStatus,
            CompositeReview: compositeId is null ? null : new(compositeId, null, null, 1, 1, 2));
        // An earlier candidate preserved the face but failed the design brief.
        var first = new CloudDesignAttempt(new("resp_first", null, "ig_first", 1, 2, 3),
            new("resp_first_review", null, null, 3, 4, 7), "Border is missing.", FaceStatus: "preserved");
        byte[] png = SyntheticImages.Png(20, 12);
        byte[]? pdf = stage == CloudDesignStage.Approved ? "%PDF-1.4\ncloud payload"u8.ToArray() : null;
        string? reason = stage == CloudDesignStage.NeedsReview ? "The eyes do not match the reference." : null;
        using var transport = new CloudTransport(new(stage, [first, last], png, pdf, reason));
        using var generator = new OpenAiDesignGeneration(harness.Workspace.Root, new Credentials(null),
            "test-image-model", harness.FileInspector, sessions, transport, "https://cloud.example",
            () => "test_cloud_token_for_authorized_clients");

        var result = await generator.GenerateAsync(draft, default);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.TechnicalDetail : null);
        transport.SentReference!.PreserveFace.ShouldBeTrue();
        transport.Methods.ShouldBe([HttpMethod.Post, HttpMethod.Get, HttpMethod.Delete]);
        var store = new FileAiDesignRequestStore(harness.Workspace.Root);
        var request = (await store.ReadAsync(result.Value, default)).Value;
        var query = new FileWorkTaskQuery(store, sessions, new WorkTaskTracker());
        var row = new WorkTaskRow((await query.FindAsync(result.Value, default)).Value.ShouldNotBeNull());
        row.CanContinue.ShouldBeTrue();
        row.HasCloudPdf.ShouldBe(exportable);
        var output = System.IO.Path.Combine(harness.Workspace.Root, "portrait.pdf");
        (await new CloudDesignPdfExport(store).SaveAsync(result.Value, output, default)).IsSuccess.ShouldBe(exportable);
        if (exportable)
        {
            request.ManualReview.ShouldBeNull();
            request.CloudReceipt!.Attempts.Last().ShouldBe(last);
            (await System.IO.File.ReadAllBytesAsync(output)).ShouldBe(pdf!);
            return;
        }

        request.CloudReceipt.ShouldBeNull();
        request.ManualReview!.Attempts.Last().ShouldBe(last);
        request.ManualReview.Reason.ShouldBe(reason);
        row.Snapshot.Stage.ShouldBe(WorkTaskStage.PortraitReview);
        row.Problem.ShouldNotBeNullOrWhiteSpace();
        if (reason is not null) row.Problem.ShouldBe(reason);
        row.CloudReviewDetails.ShouldBeNull();
        System.IO.File.Exists(output).ShouldBeFalse();
        string requestFolder = System.IO.Path.Combine(store.RequestsRoot, result.Value.ToString("D"));
        System.IO.Directory.GetFiles(requestFolder, "*.pdf").ShouldBeEmpty();
        (await System.IO.File.ReadAllBytesAsync(System.IO.Path.Combine(requestFolder, request.Candidate!.File.RelativePath)))
            .ShouldBe(png);
        var session = (await sessions.LoadAsync(request.ImportedSessionId!.Value, default)).Value;
        session.Steps.Single(s => s.Step == StepKind.OriginalConfirmation).State.ShouldBe(StepState.Waiting);
        (await sessions.ExecuteAsync(session.Id, new WorkflowCommand.ConfirmOriginal(), "operator", default)).IsSuccess.ShouldBeTrue();
        var confirmed = new WorkTaskRow((await query.FindAsync(result.Value, default)).Value.ShouldNotBeNull());
        confirmed.Snapshot.Stage.ShouldBe(WorkTaskStage.Imported);
        confirmed.CanContinue.ShouldBeTrue();
        confirmed.HasCloudPdf.ShouldBeFalse();
    }

    [Theory]
    [InlineData(CloudDesignStage.NeedsReview, true, true, true)]
    [InlineData(CloudDesignStage.NeedsReview, true, false, false)]
    [InlineData(CloudDesignStage.NeedsReview, false, true, false)]
    [InlineData(CloudDesignStage.Approved, true, true, false)]
    public async Task Missing_review_receipt_is_allowed_only_on_final_manual_portrait_attempt(
        CloudDesignStage stage, bool portrait, bool missingFinal, bool retain)
    {
        using var harness = new SessionServiceHarness();
        var (draft, _) = await CreateDraftAsync(harness, withReference: true, portrait);
        var generation = new OpenAiDesignReceipt("resp_generated", null, "ig_design", 1, 2, 3);
        var review = new OpenAiDesignReceipt("resp_reviewed", null, null, 3, 4, 7);
        var first = new CloudDesignAttempt(generation, missingFinal ? review : null, "Border is missing.");
        var last = new CloudDesignAttempt(generation, missingFinal ? null : review, FaceStatus: "uncertain");
        const string reason = "Portrait review did not complete.";
        using var transport = new CloudTransport(new(stage, [first, last], SyntheticImages.Png(20, 12),
            stage == CloudDesignStage.Approved ? "%PDF-1.4 test"u8.ToArray() : null, reason));
        using var generator = new OpenAiDesignGeneration(harness.Workspace.Root, new Credentials(null),
            "test-image-model", harness.FileInspector, harness.CreateService(), transport, "https://cloud.example",
            () => "test_cloud_token_for_authorized_clients");

        var result = await generator.GenerateAsync(draft, default);
        var store = new FileAiDesignRequestStore(harness.Workspace.Root);
        if (!retain)
        {
            result.Failure.MessageKey.ShouldBe("AiPrompt_OpenAiInvalidResult");
            (await store.ListAsync(default)).Value.ShouldBeEmpty();
            return;
        }
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.TechnicalDetail : null);
        var request = (await store.ReadAsync(result.Value, default)).Value;
        request.Candidate.ShouldNotBeNull();
        request.CloudReceipt.ShouldBeNull();
        request.ManualReview.ShouldNotBeNull().Reason.ShouldBe(reason);
        request.ManualReview.Attempts.Last().Review.ShouldBeNull();
    }

    private sealed class CloudTransport(CloudDesignJob job) : HttpMessageHandler
    {
        public List<HttpMethod> Methods { get; } = [];
        public CloudDesignReference? SentReference { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            request.RequestUri!.Host.ShouldBe("cloud.example");
            request.RequestUri.AbsolutePath.ShouldStartWith(CloudDesignLimits.Route + "/");
            request.Headers.Authorization!.Parameter.ShouldBe("test_cloud_token_for_authorized_clients");
            Methods.Add(request.Method);
            if (request.Method == HttpMethod.Post)
            {
                var input = (await request.Content!.ReadFromJsonAsync<CloudDesignInput>(ct)).ShouldNotBeNull();
                SentReference = input.References.ShouldHaveSingleItem();
                SentReference.MediaType.ShouldBe("image/png");
                return new HttpResponseMessage(HttpStatusCode.Accepted);
            }
            return request.Method == HttpMethod.Get
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(job,
                    options: new JsonSerializerOptions(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull }) }
                : new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }

    private static async Task<(AiAuthoringDraft Draft, byte[]? Reference)> CreateDraftAsync(
        SessionServiceHarness harness, bool withReference, bool portrait = false)
    {
        string root = harness.Workspace.Root;
        var catalog = new AiTemplateCatalog(new FileAiTemplateLibraryStore(root));
        var attachments = new FileAiDesignAttachments(root);
        var module = new AiAuthoringModule(new FileAiAuthoringDraftStore(root), catalog, attachments);
        var draft = (await module.CreateAsync(BuiltInAiTemplates.All[0].Reference, default)).Value;
        var prompt = new AiTextPart(Guid.NewGuid(), "Create a blue floral border.");
        if (!withReference)
            return (draft with { FinalDocument = new([prompt]) }, null);

        var slot = draft.Slots.Single(s => s.SlotId == (portrait ? "person-photo" : "layout-reference"));
        string path = harness.WriteSourcePng();
        var bound = (await module.ApplyAsync(draft.Id, draft.Revision,
            new AiAuthoringEdit.BindImage(slot.SlotId, new(path, slot.Role)), default)).Value;
        return (bound with { FinalDocument = new([prompt, new AiImagePart(Guid.NewGuid(), slot.SlotId)]) },
            await System.IO.File.ReadAllBytesAsync(path));
    }

    private sealed class Credentials(string? key) : IOpenAiImageCredentials
    {
        public OperationResult<string?> Read() => OperationResult.Ok(key);
        public OperationResult<ResultUnit> Save(string key) => throw new NotSupportedException();
        public OperationResult<ResultUnit> Delete() => throw new NotSupportedException();
    }

    private sealed class ImageTransport(HttpStatusCode status, string body, string? rejection = null) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? Path { get; private set; }
        public string? Key { get; private set; }
        public JsonElement GenerationJson { get; private set; }
        public JsonElement ReviewJson { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Path = request.RequestUri!.AbsolutePath;
            Key = request.Headers.Authorization!.Parameter;
            request.RequestUri.Host.ShouldBe("api.openai.com");
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            bool generation = json.RootElement.TryGetProperty("tools", out _);
            if (generation) GenerationJson = json.RootElement.Clone();
            else ReviewJson = json.RootElement.Clone();
            bool portrait = !generation && ReviewJson.GetProperty("text").GetProperty("format").GetProperty("schema")
                .GetProperty("properties").TryGetProperty("faceStatus", out _);
            string review = portrait
                ? JsonSerializer.Serialize(new { otherDesignApproved = rejection is null, faceStatus = "preserved", reason = rejection ?? "" })
                : JsonSerializer.Serialize(new { approved = rejection is null, reason = rejection ?? "" });
            var response = new HttpResponseMessage(status)
            {
                Content = generation ? new StringContent(body) : JsonContent.Create(new
                {
                    id = "resp_review_test",
                    status = "completed",
                    output = new[]
                    {
                        new { type = "message", content = new[]
                        {
                            new { type = "output_text", text = review }
                        } }
                    }
                })
            };
            response.Headers.Add("x-request-id", "req_test");
            return response;
        }
    }
}
