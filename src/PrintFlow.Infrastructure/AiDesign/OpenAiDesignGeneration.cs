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
    private const string Endpoint = "https://api.openai.com/v1/images/";
    private readonly FileAiDesignRequestStore _store;
    private readonly VerifiedAiAttachmentReader _attachments;
    private readonly IOpenAiImageCredentials _credentials;
    private readonly string _model;
    private readonly IFileInspector _inspector;
    private readonly ISessionService _sessions;
    private readonly HttpClient _http;

    public OpenAiDesignGeneration(string workspaceRoot, IOpenAiImageCredentials credentials, string model,
        IFileInspector inspector, ISessionService sessions)
        : this(workspaceRoot, credentials, model, inspector, sessions, new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(15)
        })
    {
    }

    internal OpenAiDesignGeneration(string workspaceRoot, IOpenAiImageCredentials credentials, string model,
        IFileInspector inspector, ISessionService sessions, HttpMessageHandler handler)
    {
        _store = new(workspaceRoot);
        _attachments = new(workspaceRoot);
        _credentials = credentials;
        _model = model;
        _inspector = inspector;
        _sessions = sessions;
        _http = new(handler)
        {
            Timeout = TimeSpan.FromMinutes(5),
            MaxResponseContentBufferSize = 56 * 1024 * 1024
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

    private async Task<OperationResult<byte[]>> RequestImageAsync(Guid draftId, AiPromptExport prompt, string key,
        CancellationToken ct)
    {
        bool edit = prompt.RequiredAttachments.Count > 0;
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint + (edit ? "edits" : "generations"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        if (edit)
        {
            var content = new MultipartFormDataContent();
            request.Content = content;
            content.Add(new StringContent(_model), "model");
            content.Add(new StringContent(prompt.Text), "prompt");
            content.Add(new StringContent("png"), "output_format");
            foreach (var attachment in prompt.RequiredAttachments)
            {
                var opened = await _attachments.OpenAsync(draftId, attachment, ct).ConfigureAwait(false);
                if (opened.IsFailure)
                {
                    ct.ThrowIfCancellationRequested();
                    return Fail<byte[]>(FailureCode.RevisionIntegrityMismatch, "AiPrompt_OpenAiReferenceUnavailable");
                }
                var part = new StreamContent(opened.Value.Stream);
                part.Headers.ContentType = new MediaTypeHeaderValue(attachment.MediaType);
                content.Add(part, "image[]", attachment.UploadName);
            }
        }
        else
        {
            request.Content = JsonContent.Create(new { model = _model, prompt = prompt.Text, output_format = "png" });
        }

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            string message = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "AiPrompt_OpenAiAccessDenied",
                HttpStatusCode.TooManyRequests => "ImageProcessing_OpenAiRateOrQuota",
                HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => "ImageProcessing_OpenAiTimeout",
                _ => "AiPrompt_OpenAiRejected"
            };
            return Fail<byte[]>(FailureCode.AdapterUnavailable, message);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var result = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        var root = result.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array ||
            data.GetArrayLength() != 1 || data[0].ValueKind != JsonValueKind.Object ||
            !data[0].TryGetProperty("b64_json", out var encoded) || encoded.ValueKind != JsonValueKind.String ||
            !encoded.TryGetBytesFromBase64(out var png) || png.Length is <= 0 or > MaxPngBytes)
            return Fail<byte[]>(FailureCode.OutputValidationFailed, "AiPrompt_OpenAiInvalidResult");
        return OperationResult.Ok(png);
    }

    private async Task<OperationResult<Guid>> ImportAsync(AiAuthoringDraft draft, string prompt, byte[] png,
        CancellationToken ct)
    {
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

        string title = draft.TemplateSnapshot.Name;
        var session = await _sessions.ImportVerifiedWorkResultAsync(requestId, staged.Path, title, hash, ct).ConfigureAwait(false);
        if (session.IsFailure)
            return Fail<Guid>(session.Failure.Code, ImageProcessingPolicy.IsDefaultsFailure(session.Failure)
                ? "WorkTask_RepairImageProcessingSettings" : "AiPrompt_OpenAiStorageFailed");

        // The imported session owns a reference to this source path.
        staged.Release();
        var candidate = new AiDesignCandidate(new(requestId, Path.GetFileName(staged.Path)), image);
        var recorded = await _store.RecordOpenAiResultAsync(new(draft.Id, title), prompt, candidate,
            session.Value.Id, CancellationToken.None).ConfigureAwait(false);
        return recorded.IsSuccess ? OperationResult.Ok(requestId)
            : Fail<Guid>(FailureCode.PersistenceError, "AiPrompt_OpenAiHistoryFailed");
    }

    private static OperationResult<T> Fail<T>(FailureCode code, string messageKey) =>
        OperationResult.Fail<T>(OperationFailure.Create(code, messageKey, messageKey: messageKey));
}
