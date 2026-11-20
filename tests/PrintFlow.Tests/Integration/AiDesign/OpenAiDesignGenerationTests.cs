using System.Net;
using System.Net.Http;
using System.Text.Json;
using PrintFlow.Domain.AiDesign;
using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.AiDesign;
using PrintFlow.Infrastructure.AiDesign.Authoring;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.AiDesign;
using PrintFlow.Workflow.AiDesign.Authoring;
using PrintFlow.Workflow.Ports;
using ResultUnit = PrintFlow.Domain.Results.Unit;

namespace PrintFlow.Tests.Integration.AiDesign;

[Collection(SqliteCollection.Name)]
public sealed class OpenAiDesignGenerationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Generates_from_the_saved_prompt_and_imports_one_reviewable_png(bool withReference)
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
        transport.Calls.ShouldBe(1);
        transport.Path.ShouldBe("/v1/responses");
        transport.Key.ShouldBe("test-key");
        var body = transport.Json;
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

    private static async Task<(AiAuthoringDraft Draft, byte[]? Reference)> CreateDraftAsync(
        SessionServiceHarness harness, bool withReference)
    {
        string root = harness.Workspace.Root;
        var catalog = new AiTemplateCatalog(new FileAiTemplateLibraryStore(root));
        var attachments = new FileAiDesignAttachments(root);
        var module = new AiAuthoringModule(new FileAiAuthoringDraftStore(root), catalog, attachments);
        var draft = (await module.CreateAsync(BuiltInAiTemplates.All[0].Reference, default)).Value;
        var prompt = new AiTextPart(Guid.NewGuid(), "Create a blue floral border.");
        if (!withReference)
            return (draft with { FinalDocument = new([prompt]) }, null);

        var slot = draft.Slots.First();
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

    private sealed class ImageTransport(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? Path { get; private set; }
        public string? Key { get; private set; }
        public JsonElement Json { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Path = request.RequestUri!.AbsolutePath;
            Key = request.Headers.Authorization!.Parameter;
            if (request.Content is not MultipartFormDataContent)
            {
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                Json = json.RootElement.Clone();
            }
            var response = new HttpResponseMessage(status) { Content = new StringContent(body) };
            response.Headers.Add("x-request-id", "req_test");
            return response;
        }
    }
}
