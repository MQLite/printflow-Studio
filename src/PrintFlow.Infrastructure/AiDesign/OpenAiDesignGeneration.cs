using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using PrintFlow.Domain.AiDesign;
using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Settings;
using PrintFlow.Infrastructure.AiDesign.Authoring;
using PrintFlow.Workflow.AiDesign;
using PrintFlow.Workflow.AiDesign.Authoring;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Infrastructure.AiDesign;

public sealed class OpenAiDesignGeneration : IAiDesignApiGeneration, IDisposable
{
    private const int MaxPromptLength = 32_000;
    private const int MaxPngBytes = 40 * 1024 * 1024;
    private const int MaxPixels = 64_000_000;
    private const string Endpoint = "https://api.openai.com/v1/responses";
    private const string ResponsesModel = "gpt-6.2-sol";
    private readonly FileAiDesignRequestStore _store;
    private readonly VerifiedAiAttachmentReader _attachments;
    private readonly IOpenAiImageCredentials _credentials;
    private readonly string _model;
    private readonly IFileInspector _inspector;
    private readonly ISessionService _sessions;
    private readonly HttpClient _http;
    private readonly Uri? _cloudEndpoint;
    private readonly Func<string?> _cloudToken;
    public bool UsesCloudReview { get; }

    public OpenAiDesignGeneration(string workspaceRoot, IOpenAiImageCredentials credentials, string model,
        IFileInspector inspector, ISessionService sessions, string? cloudEndpoint = null)
        : this(workspaceRoot, credentials, model, inspector, sessions, new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(15)
        }, cloudEndpoint)
    {
    }

    internal OpenAiDesignGeneration(string workspaceRoot, IOpenAiImageCredentials credentials, string model,
        IFileInspector inspector, ISessionService sessions, HttpMessageHandler handler,
        string? cloudEndpoint = null, Func<string?>? cloudToken = null)
    {
        _store = new(workspaceRoot);
        _attachments = new(workspaceRoot);
        _credentials = credentials;
        _model = model;
        _inspector = inspector;
        _sessions = sessions;
        UsesCloudReview = cloudEndpoint is not null;
        _cloudEndpoint = OpenAiDesignCloudClient.ParseEndpoint(cloudEndpoint);
        _cloudToken = cloudToken ?? (() => Environment.GetEnvironmentVariable(OpenAiDesignCloudClient.TokenVariable));
        _http = new(handler)
        {
            Timeout = TimeSpan.FromMinutes(5),
            MaxResponseContentBufferSize = (UsesCloudReview ? 120 : 56) * 1024 * 1024
        };
    }

    public void Dispose() => _http.Dispose();

    public OperationFailure? GetUnavailable()
    {
        var key = ReadKey();
        return key.IsFailure ? key.Failure : null;
    }

    private OperationResult<string> ReadKey()
    {
        if (UsesCloudReview)
        {
            string? token = _cloudToken();
            return _cloudEndpoint is not null && CloudDesignLimits.IsValidAccessToken(token)
                ? OperationResult.Ok(token!)
                : Fail<string>(FailureCode.AdapterUnavailable, "AiPrompt_CloudSetup");
        }
        var key = _credentials.Read();
        if (key.IsFailure) return OperationResult.Fail<string>(key.Failure);
        if (string.IsNullOrWhiteSpace(key.Value))
            return Fail<string>(FailureCode.AdapterUnavailable, "ImageProcessing_OpenAiKeyMissing");
        return OperationResult.Ok(key.Value);
    }

    public async Task<OperationResult<Guid>> GenerateAsync(AiAuthoringDraft draft, CancellationToken cancellationToken)
    {
        try
        {
            var key = ReadKey();
            if (key.IsFailure) return OperationResult.Fail<Guid>(key.Failure);

            using var draftLease = _store.TryLockWorkDraft(draft.Id);
            if (draftLease is null)
                return Fail<Guid>(FailureCode.PreconditionNotMet, "AiPrompt_GenerationAlreadyActive");
            var requests = await _store.ListSnapshotsAsync(cancellationToken).ConfigureAwait(false);
            if (requests.IsFailure)
                return Fail<Guid>(FailureCode.PersistenceError, "AiPrompt_OpenAiStorageFailed");
            if (requests.Value.Any(request => request.WorkDraft?.DraftId == draft.Id && _store.BlocksNewSubmission(request)))
                return Fail<Guid>(FailureCode.PreconditionNotMet, "AiPrompt_GenerationAlreadyActive");

            var prompt = AiAuthoringModule.Export(draft, asset => asset.UploadName);
            if (prompt.Issues.Count != 0)
                return Fail<Guid>(FailureCode.PreconditionNotMet, "AiPrompt_WorkAttachmentRequired");
            if (string.IsNullOrWhiteSpace(prompt.Text) || prompt.Text.Length > MaxPromptLength)
                return Fail<Guid>(FailureCode.PreconditionNotMet, "AiPrompt_OpenAiPromptLength");

            var image = await RequestImageAsync(draft.Id, prompt, key.Value, cancellationToken).ConfigureAwait(false);
            if (image.IsFailure) return OperationResult.Fail<Guid>(image.Failure);

            return await ImportAsync(draft, prompt.Text, image.Value, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Fail<Guid>(FailureCode.Cancelled, "AiPrompt_OpenAiCancelled");
        }
        catch (OperationCanceledException)
        {
            return Fail<Guid>(FailureCode.Timeout, "ImageProcessing_OpenAiTimeout");
        }
        catch (HttpRequestException)
        {
            return Fail<Guid>(FailureCode.AdapterUnavailable, "ImageProcessing_OpenAiNetwork");
        }
        catch (JsonException)
        {
            return Fail<Guid>(FailureCode.OutputValidationFailed, "AiPrompt_OpenAiInvalidResult");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception)
        {
            return Fail<Guid>(FailureCode.WorkspaceError, "AiPrompt_OpenAiStorageFailed");
        }
    }

    private async Task<OperationResult<DesignResponse>> RequestImageAsync(Guid draftId, AiPromptExport prompt, string key,
        CancellationToken ct)
    {
        List<CloudDesignReference> references = [];
        foreach (var attachment in prompt.RequiredAttachments)
        {
            var opened = await _attachments.OpenAsync(draftId, attachment, ct).ConfigureAwait(false);
            if (opened.IsFailure)
            {
                ct.ThrowIfCancellationRequested();
                return Fail<DesignResponse>(FailureCode.RevisionIntegrityMismatch, "AiPrompt_OpenAiReferenceUnavailable");
            }
            await using var verified = opened.Value;
            byte[] bytes = new byte[checked((int)attachment.ByteLength)];
            await verified.Stream.ReadExactlyAsync(bytes, ct).ConfigureAwait(false);
            references.Add(new(attachment.UploadName, attachment.MediaType, bytes));
        }

        if (_cloudEndpoint is not null)
        {
            var reviewed = await OpenAiDesignCloudClient.RunAsync(_http, _cloudEndpoint,
                new(prompt.Text, references.ToArray()), key, ct).ConfigureAwait(false);
            if (reviewed.IsFailure) return OperationResult.Fail<DesignResponse>(reviewed.Failure);
            var approved = reviewed.Value;
            return OperationResult.Ok(new DesignResponse(approved.Png!, approved.Attempts[^1].Generation, approved.Pdf, approved.Attempts));
        }

        List<object> inputs = [new { type = "input_text", text = prompt.Text }];
        foreach (var reference in references)
        {
            // Data URLs have no filename, so identify the image next to its content.
            inputs.Add(new { type = "input_text", text = $"Reference image: {reference.Name}" });
            inputs.Add(new
            {
                type = "input_image",
                image_url = $"data:{reference.MediaType};base64,{Convert.ToBase64String(reference.Data)}",
                detail = "auto"
            });
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(new
            {
                model = ResponsesModel,
                store = true,
                reasoning = new { effort = "low" },
                max_tool_calls = 1,
                tool_choice = new { type = "image_generation" },
                tools = new[]
                {
                    new
                    {
                        type = "image_generation",
                        model = _model,
                        action = prompt.RequiredAttachments.Count == 0 ? "generate" : "auto",
                        output_format = "png"
                    }
                },
                input = new[] { new { role = "user", content = inputs } }
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            string message = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "AiPrompt_OpenAiAccessDenied",
                HttpStatusCode.TooManyRequests or HttpStatusCode.PaymentRequired => "ImageProcessing_OpenAiRateOrQuota",
                HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => "ImageProcessing_OpenAiTimeout",
                _ => "AiPrompt_OpenAiRejected"
            };
            return Fail<DesignResponse>(FailureCode.AdapterUnavailable, message);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var result = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        var root = result.RootElement;
        string? responseId = SafeIdentifier(StringProperty(root, "id"), "resp_", key);
        if (responseId is null || StringProperty(root, "status") != "completed" ||
            !root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
            return Fail<DesignResponse>(FailureCode.OutputValidationFailed, "AiPrompt_OpenAiInvalidResult");

        var images = output.EnumerateArray().Where(item => StringProperty(item, "type") == "image_generation_call").ToArray();
        if (images.Length != 1 || StringProperty(images[0], "status") != "completed" ||
            !images[0].TryGetProperty("result", out var encoded) || encoded.ValueKind != JsonValueKind.String ||
            !encoded.TryGetBytesFromBase64(out var png) || png.Length is <= 0 or > MaxPngBytes)
            return Fail<DesignResponse>(FailureCode.OutputValidationFailed, "AiPrompt_OpenAiInvalidResult");

        string? requestId = response.Headers.TryGetValues("x-request-id", out var requestIds)
            ? SafeIdentifier(requestIds.FirstOrDefault(), "req_", key) : null;
        string? imageCallId = SafeIdentifier(StringProperty(images[0], "id"), "ig_", key);
        root.TryGetProperty("usage", out var usage);
        return OperationResult.Ok(new DesignResponse(png, new(responseId, requestId, imageCallId,
            ReadTokens(usage, "input_tokens"), ReadTokens(usage, "output_tokens"), ReadTokens(usage, "total_tokens"))));
    }

    private static string? StringProperty(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? SafeIdentifier(string? id, string prefix, string key) =>
        id is { Length: <= 128 } && id.Length > prefix.Length && id.StartsWith(prefix, StringComparison.Ordinal) &&
        id.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-') && !id.Contains(key, StringComparison.Ordinal)
            ? id : null;

    private static long? ReadTokens(JsonElement usage, string name) =>
        usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long count) && count >= 0 ? count : null;

    private async Task<OperationResult<Guid>> ImportAsync(AiAuthoringDraft draft, string prompt, DesignResponse response,
        CancellationToken ct)
    {
        byte[] png = response.Png;
        Guid requestId = Guid.NewGuid();
        using var staged = AiDesignOwnedStagingFiles.CreateOpenAiResult(_store.WorkspaceRoot, requestId);
        await staged.Output.WriteAsync(png, ct).ConfigureAwait(false);
        await staged.MoveWithinBatchAsync(ct).ConfigureAwait(false);
        await using var pinned = VerifiedAiAttachmentReader.OpenRegular(staged.Path);
        var facts = await _inspector.InspectAsync(staged.Path, ct).ConfigureAwait(false);
        var hash = Sha256.FromBytes(SHA256.HashData(png));
        if (facts.IsFailure || facts.Value is not { Format: ImageFormat.Png, HasPixelDimensions: true } image ||
            image.ByteLength != png.Length || image.Sha256 != hash ||
            (long)image.PixelWidth!.Value * image.PixelHeight!.Value > MaxPixels)
            return Fail<Guid>(FailureCode.OutputValidationFailed, "AiPrompt_OpenAiInvalidResult");

        using var stagedPdf = response.Pdf is not null
            ? AiDesignOwnedStagingFiles.CreateOpenAiResult(_store.WorkspaceRoot, requestId, pdf: true) : null;
        CloudDesignReceipt? cloudReceipt = null;
        if (stagedPdf is not null && response.Pdf is { } pdf && response.Attempts is { } attempts)
        {
            await stagedPdf.Output.WriteAsync(pdf, ct).ConfigureAwait(false);
            await stagedPdf.MoveWithinBatchAsync(ct).ConfigureAwait(false);
            cloudReceipt = new(attempts, Sha256.FromBytes(SHA256.HashData(pdf)));
        }

        string title = draft.TemplateSnapshot.Name;
        var session = await _sessions.ImportVerifiedWorkResultAsync(requestId, staged.Path, title, hash, ct).ConfigureAwait(false);
        if (session.IsFailure)
            return Fail<Guid>(session.Failure.Code, ImageProcessingPolicy.IsDefaultsFailure(session.Failure)
                ? "WorkTask_RepairImageProcessingSettings" : "AiPrompt_OpenAiStorageFailed");

        // The imported session owns a reference to this source path.
        staged.Release();
        var candidate = new AiDesignCandidate(new(requestId, Path.GetFileName(staged.Path)), image);
        var recorded = await _store.RecordOpenAiResultAsync(new(draft.Id, title), prompt, candidate,
            session.Value.Id, response.Receipt, CancellationToken.None, cloudReceipt).ConfigureAwait(false);
        if (recorded.IsSuccess) stagedPdf?.Release();
        return recorded.IsSuccess ? OperationResult.Ok(requestId)
            : Fail<Guid>(FailureCode.PersistenceError, "AiPrompt_OpenAiHistoryFailed");
    }

    private sealed record DesignResponse(byte[] Png, OpenAiDesignReceipt Receipt,
        byte[]? Pdf = null, CloudDesignAttempt[]? Attempts = null);

    private static OperationResult<T> Fail<T>(FailureCode code, string messageKey) =>
        OperationResult.Fail<T>(OperationFailure.Create(code, messageKey, messageKey: messageKey));
}
