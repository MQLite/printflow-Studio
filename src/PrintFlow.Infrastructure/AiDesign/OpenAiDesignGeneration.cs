using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using ImageMagick;
using PrintFlow.Design;
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
    private readonly FileAiDesignRequestStore _store;
    private readonly VerifiedAiAttachmentReader _attachments;
    private readonly IOpenAiImageCredentials _credentials;
    private readonly string _model;
    private readonly IFileInspector _inspector;
    private readonly ISessionService _sessions;
    private readonly HttpClient _http;
    private readonly Uri? _cloudEndpoint;
    private readonly Func<string?> _cloudToken;
    private readonly bool _usesRemoteDesignService;
    public bool UsesCloudReview => true;

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
        _usesRemoteDesignService = cloudEndpoint is not null;
        _cloudEndpoint = OpenAiDesignCloudClient.ParseEndpoint(cloudEndpoint);
        _cloudToken = cloudToken ?? (() => Environment.GetEnvironmentVariable(OpenAiDesignCloudClient.TokenVariable));
        _http = new(handler)
        {
            Timeout = TimeSpan.FromMinutes(5),
            MaxResponseContentBufferSize = (_usesRemoteDesignService ? 120 : 56) * 1024 * 1024
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
        if (_usesRemoteDesignService)
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
            if (string.IsNullOrWhiteSpace(prompt.Text) || prompt.Text.Length > CloudDesignLimits.MaxPromptLength)
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
        catch (HttpRequestException error)
        {
            return Fail<Guid>(FailureCode.AdapterUnavailable, error.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "AiPrompt_OpenAiAccessDenied",
                HttpStatusCode.TooManyRequests or HttpStatusCode.PaymentRequired => "ImageProcessing_OpenAiRateOrQuota",
                HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => "ImageProcessing_OpenAiTimeout",
                null => "ImageProcessing_OpenAiNetwork",
                _ => "AiPrompt_OpenAiRejected"
            });
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or MagickException)
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

        var input = new CloudDesignInput(prompt.Text, references.ToArray());
        if (_cloudEndpoint is not null)
        {
            var review = await OpenAiDesignCloudClient.RunAsync(_http, _cloudEndpoint, input, key, ct).ConfigureAwait(false);
            return review.IsSuccess ? ToDesignResponse(review.Value) : OperationResult.Fail<DesignResponse>(review.Failure);
        }

        if (!input.IsValid(AiDesignAttachmentLimits.TotalBytes))
            return Fail<DesignResponse>(FailureCode.PreconditionNotMet, "AiPrompt_OpenAiReferenceUnavailable");

        var design = new DesignReviewPipeline(new DesignResponses(_http, key, _model));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromMinutes(CloudDesignLimits.CloudTimeoutMinutes));
        return ToDesignResponse(await design.RunAsync(input, deadline.Token).ConfigureAwait(false));
    }

    private static OperationResult<DesignResponse> ToDesignResponse(CloudDesignJob job)
    {
        if (job.Stage == CloudDesignStage.Rejected && job.Reason is { Length: > 0 and <= CloudDesignLimits.MaxReasonLength })
            return OperationResult.Fail<DesignResponse>(OperationFailure.Create(FailureCode.OutputValidationFailed,
                "OpenAI design review did not pass.",
                context: new Dictionary<string, string> { [CloudDesignLimits.ReviewReasonKey] = job.Reason },
                messageKey: "AiPrompt_CloudRejected"));

        if (job.Stage != CloudDesignStage.Approved || job.Attempts is not { Length: 1 or 2 } ||
            !job.Attempts.All(a => a is { Generation: { }, Review: { } } &&
                IsResponseId(a.Generation.ResponseId) && IsResponseId(a.Review.ResponseId)) ||
            job.Png is not { Length: > 0 and <= CloudDesignLimits.MaxArtifactBytes } ||
            job.Pdf is not { Length: > 5 and <= CloudDesignLimits.MaxArtifactBytes } ||
            !job.Pdf.AsSpan().StartsWith("%PDF-"u8))
            return Fail<DesignResponse>(FailureCode.OutputValidationFailed, "AiPrompt_OpenAiInvalidResult");

        return OperationResult.Ok(new DesignResponse(job.Png, job.Attempts[^1].Generation, job.Pdf, job.Attempts));
    }

    private static bool IsResponseId(string? value) =>
        value is { Length: > 5 and <= 128 } && value.StartsWith("resp_", StringComparison.Ordinal) &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');

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
            (long)image.PixelWidth!.Value * image.PixelHeight!.Value > CloudDesignLimits.MaxArtifactPixels)
            return Fail<Guid>(FailureCode.OutputValidationFailed, "AiPrompt_OpenAiInvalidResult");

        using var stagedPdf = AiDesignOwnedStagingFiles.CreateOpenAiResult(_store.WorkspaceRoot, requestId, pdf: true);
        await stagedPdf.Output.WriteAsync(response.Pdf, ct).ConfigureAwait(false);
        await stagedPdf.MoveWithinBatchAsync(ct).ConfigureAwait(false);
        CloudDesignReceipt cloudReceipt = new(response.Attempts, Sha256.FromBytes(SHA256.HashData(response.Pdf)));

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
        if (recorded.IsSuccess) stagedPdf.Release();
        return recorded.IsSuccess ? OperationResult.Ok(requestId)
            : Fail<Guid>(FailureCode.PersistenceError, "AiPrompt_OpenAiHistoryFailed");
    }

    private sealed record DesignResponse(byte[] Png, OpenAiDesignReceipt Receipt,
        byte[] Pdf, CloudDesignAttempt[] Attempts);

    private static OperationResult<T> Fail<T>(FailureCode code, string messageKey) =>
        OperationResult.Fail<T>(OperationFailure.Create(code, messageKey, messageKey: messageKey));
}
