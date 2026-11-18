using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using ImageMagick;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Settings;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Adapters.OpenAI;

public sealed class OpenAiImageBackend : IImageProcessingBackend, IDisposable
{
    public const string DefaultModel = "gpt-image-2.6-sunburst";
    private const string AdapterId = "openai-images";
    private const int MaximumInputBytes = 20 * 1024 * 1024;
    private const int MaximumResponseBytes = 48 * 1024 * 1024;
    private static readonly Uri EditEndpoint = new("https://api.openai.com/v1/images/edits");

    private readonly IWorkspace _workspace;
    private readonly IOpenAiImageCredentials _credentials;
    private readonly string _model;
    private readonly HttpClient _http;

    public OpenAiImageBackend(IWorkspace workspace, IOpenAiImageCredentials credentials, string model)
        : this(workspace, credentials, model, new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            PooledConnectionLifetime = TimeSpan.Zero,
            ConnectTimeout = TimeSpan.FromSeconds(15),
        })
    {
    }

    internal OpenAiImageBackend(IWorkspace workspace, IOpenAiImageCredentials credentials, string model, HttpMessageHandler handler)
    {
        _workspace = workspace;
        _credentials = credentials;
        _model = model;
        _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public void Dispose() => _http.Dispose();

    public ImageProcessingCapability? GetCapability(ImageProcessingMethod method)
    {
        if (!ImageUploadConfirmation.IsOpenAiMethod(method))
            return null;

        var key = GetKey();
        return new(AdapterId, _model, key.IsFailure ? key.Failure : null);
    }

    public async Task<OperationResult<AdapterOutput>> ProcessAsync(
        ImageProcessingRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        bool restore = request.Method == ImageProcessingMethod.OpenAiRestore;
        if (!ImageUploadConfirmation.IsOpenAiMethod(request.Method) ||
            request.Operation != (restore ? OperationKind.Enhance : OperationKind.RemoveBackground) ||
            request.AlgorithmVersion != _model ||
            request.Input.Area != WorkspaceArea.Working || request.ExpectedOutput.Area != WorkspaceArea.Working ||
            (request.Method == ImageProcessingMethod.OpenAiCutout && (request.BackgroundRemovalAuthority is not
                { Decision: BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent,
                    Mode: BackgroundRemovalMode.Automatic } authority ||
                !authority.Authorises(request.InputRevision, request.InputSha256))))
            return Reject(FailureCode.PreconditionNotMet, "The OpenAI Images request is outside the assigned operation.",
                "ImageProcessing_OpenAiConfirmationRequired");

        if (request.UploadConfirmation is not { } confirmation)
            return MissingConfirmation();

        var key = GetKey();
        if (key.IsFailure)
            return OperationResult.Fail<AdapterOutput>(key.Failure);

        IReadOnlyDictionary<string, string>? evidence = null;
        try
        {
            string inputPath = _workspace.ResolveAbsolute(request.Input);
            string outputPath = _workspace.ResolveAbsolute(request.ExpectedOutput);
            if (string.Equals(inputPath, outputPath, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetDirectoryName(inputPath), Path.GetDirectoryName(outputPath), StringComparison.OrdinalIgnoreCase) ||
                File.Exists(outputPath))
                return Reject(FailureCode.WorkspaceError, "OpenAI Images requires a new output beside the assigned working input.",
                    "ImageProcessing_OpenAiWorkspace");

            byte[] input;
            using (var stream = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length is <= 0 or > MaximumInputBytes)
                    return UnsupportedInput();

                input = new byte[checked((int)stream.Length)];
                await stream.ReadExactlyAsync(input, cancellationToken).ConfigureAwait(false);
            }
            if (Sha256.FromBytes(SHA256.HashData(input)) != request.InputSha256)
                return Reject(FailureCode.RevisionIntegrityMismatch, "The working input no longer matches the approved image.",
                    "ImageProcessing_OpenAiConfirmationRequired");

            var format = FormatSniffer.Detect(input);
            if (format is not (ImageFormat.Png or ImageFormat.Jpeg))
                return UnsupportedInput();

            var source = Decode(input, format == ImageFormat.Png ? MagickFormat.Png : MagickFormat.Jpeg, 8192, 24_000_000);
            if (source is null || !source.Value.HasVisiblePixels)
                return UnsupportedInput();

            bool transparent = !restore || source.Value.HasTransparentPixels;
            using var message = CreateRequest(request.Method, key.Value, input, format, transparent);
            cancellationToken.ThrowIfCancellationRequested();
            if (!confirmation.TryConsume(request.Method, request.InputRevision, request.InputSha256))
                return MissingConfirmation();

            var stopwatch = Stopwatch.StartNew();
            var response = await EditAsync(message, key.Value, cancellationToken).ConfigureAwait(false);
            if (response.IsFailure)
                return OperationResult.Fail<AdapterOutput>(response.Failure);

            evidence = response.Value.Evidence;
            var png = response.Value.Png;
            cancellationToken.ThrowIfCancellationRequested();
            if (FormatSniffer.Detect(png) != ImageFormat.Png ||
                Decode(png, MagickFormat.Png, 3840, 8_294_400) is not { HasVisiblePixels: true } decoded ||
                (transparent && !decoded.HasTransparentPixels))
                return OperationResult.Fail<AdapterOutput>(InvalidOutputFailure(evidence));

            FileWorkspace.WriteNewWorkingCandidate(outputPath, stream => stream.Write(png), cancellationToken);
            string notes = string.Join(", ", evidence.Select(pair => $"{pair.Key}={pair.Value}"));
            return OperationResult.Ok(new AdapterOutput(request.ExpectedOutput, stopwatch.Elapsed, notes)
            {
                ValidatedImageSha256 = Sha256.FromBytes(SHA256.HashData(png)),
                ImageProcessingEvidence = evidence,
            });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return OperationResult.Fail<AdapterOutput>(Failure(FailureCode.WorkspaceError,
                "The assigned OpenAI Images working files could not be read or written.", "ImageProcessing_OpenAiWorkspace", evidence));
        }
    }

    private OperationResult<string> GetKey()
    {
        if (_model is not (DefaultModel or "gpt-image-2.6-flare"))
            return OperationResult.Fail<string>(OperationFailure.Create(FailureCode.AdapterUnavailable,
                "Select a supported OpenAI Images model in the deployment configuration.",
                messageKey: "ImageProcessing_OpenAiModelUnavailable"));

        if (LocalImageFiles.MagickRuntimeFailure is { } runtime)
            return OperationResult.Fail<string>(runtime);

        var result = _credentials.Read();
        if (result.IsFailure)
            return OperationResult.Fail<string>(result.Failure);
        return result.Value is { } key && WindowsOpenAiImageCredentials.IsValidKey(key)
            ? OperationResult.Ok(key)
            : OperationResult.Fail<string>(OperationFailure.Create(FailureCode.AdapterUnavailable,
                "OpenAI Images has no configured API key.", messageKey: "ImageProcessing_OpenAiKeyMissing"));
    }

    private HttpRequestMessage CreateRequest(ImageProcessingMethod method, string key, byte[] input, ImageFormat format, bool transparent)
    {
        string prompt = method switch
        {
            ImageProcessingMethod.OpenAiRestore =>
                "Restore this photo with subtle cleanup and improved clarity. Preserve identity, composition, text, and existing transparency.",
            ImageProcessingMethod.OpenAiCutout =>
                "Remove the background and leave it transparent. Preserve the subject, existing transparent areas, fine edges, text, and composition.",
            _ => "Restore this photo with subtle cleanup and remove the background. Keep the background transparent. " +
                "Preserve identity, fine edges, text, and composition.",
        };
        var content = new MultipartFormDataContent();
        content.Add(new StringContent(_model), "model");
        content.Add(new StringContent(prompt), "prompt");
        content.Add(new StringContent("png"), "output_format");
        content.Add(new StringContent(transparent ? "transparent" : "auto"), "background");
        content.Add(new StringContent("auto"), "size");
        content.Add(new StringContent("1"), "n");

        bool png = format == ImageFormat.Png;
        var file = new ByteArrayContent(input);
        file.Headers.ContentType = new MediaTypeHeaderValue(png ? "image/png" : "image/jpeg");
        content.Add(file, "image", png ? "input.png" : "input.jpg");

        var message = new HttpRequestMessage(HttpMethod.Post, EditEndpoint) { Content = content };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return message;
    }

    private async Task<OperationResult<EditResponse>> EditAsync(
        HttpRequestMessage message, string key, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        CancellationToken token = timeout.Token;
        var evidence = new Dictionary<string, string>();

        try
        {
            using var response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (RequestId(response, key) is { } requestId)
                evidence.Add("requestId", requestId);
            if (!response.IsSuccessStatusCode)
            {
                string messageKey = response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "ImageProcessing_OpenAiUnauthorised",
                    HttpStatusCode.TooManyRequests or HttpStatusCode.PaymentRequired => "ImageProcessing_OpenAiRateOrQuota",
                    _ => "ImageProcessing_OpenAiRejected",
                };
                return OperationResult.Fail<EditResponse>(Failure(FailureCode.AdapterUnavailable,
                    $"OpenAI Images returned HTTP {(int)response.StatusCode}.", messageKey, evidence));
            }

            await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var body = new MemoryStream();
            var buffer = new byte[81920];
            int count;
            while ((count = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
            {
                if (body.Length + count > MaximumResponseBytes)
                    return OperationResult.Fail<EditResponse>(InvalidOutputFailure(evidence));
                body.Write(buffer, 0, count);
            }
            body.Position = 0;
            using var json = await JsonDocument.ParseAsync(body, cancellationToken: token).ConfigureAwait(false);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return OperationResult.Fail<EditResponse>(InvalidOutputFailure(evidence));

            if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                foreach (string name in new[] { "input_tokens", "output_tokens", "total_tokens" })
                {
                    if (usage.TryGetProperty(name, out var number) && number.ValueKind == JsonValueKind.Number &&
                        number.TryGetInt64(out long value) && value >= 0)
                        evidence.Add(name, value.ToString(CultureInfo.InvariantCulture));
                }
            }

            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() != 1 ||
                data[0].ValueKind != JsonValueKind.Object ||
                !data[0].TryGetProperty("b64_json", out var encoded) || encoded.ValueKind != JsonValueKind.String ||
                !encoded.TryGetBytesFromBase64(out byte[]? png))
                return OperationResult.Fail<EditResponse>(InvalidOutputFailure(evidence));

            token.ThrowIfCancellationRequested();
            return OperationResult.Ok(new EditResponse(png, evidence));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return OperationResult.Fail<EditResponse>(Failure(FailureCode.Timeout,
                "OpenAI Images did not respond within the request deadline.", "ImageProcessing_OpenAiTimeout", evidence));
        }
        catch (Exception error) when (error is HttpRequestException or IOException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return OperationResult.Fail<EditResponse>(Failure(FailureCode.AdapterUnavailable,
                "The OpenAI Images network request did not complete.", "ImageProcessing_OpenAiNetwork", evidence));
        }
        catch (JsonException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return OperationResult.Fail<EditResponse>(InvalidOutputFailure(evidence));
        }
    }

    private static string? RequestId(HttpResponseMessage response, string key)
    {
        if (!response.Headers.TryGetValues("x-request-id", out var values))
            return null;
        string? id = values.FirstOrDefault();
        return id is { Length: > 4 and <= 128 } && id.StartsWith("req_", StringComparison.Ordinal) &&
            id.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-') && !id.Contains(key, StringComparison.Ordinal)
            ? id : null;
    }

    private static ImageAlpha? Decode(byte[] imageBytes, MagickFormat format, int maximumEdge, long maximumPixels)
    {
        try
        {
            var settings = new MagickReadSettings { Format = format };
            using var stream = new MemoryStream(imageBytes, writable: false);
            var info = new MagickImageInfo(stream, settings);
            if (!LocalImageFiles.Fits(info.Width, info.Height, maximumEdge, maximumPixels))
                return null;

            stream.Position = 0;
            using var image = new MagickImage(stream, settings);
            if (!LocalImageFiles.Fits(image.Width, image.Height, maximumEdge, maximumPixels))
                return null;
            if (!image.HasAlpha || image.IsOpaque)
                return new ImageAlpha(true, false);

            var alpha = image.Statistics().GetChannel(PixelChannel.Alpha);
            return alpha is null ? null : new ImageAlpha(alpha.Maximum > 0, true);
        }
        catch (MagickException)
        {
            return null;
        }
    }

    private static OperationResult<AdapterOutput> MissingConfirmation() =>
        Reject(FailureCode.PreconditionNotMet, "Confirm this exact image and operation before uploading to OpenAI Images.",
            "ImageProcessing_OpenAiConfirmationRequired");

    private static OperationResult<AdapterOutput> UnsupportedInput() =>
        Reject(FailureCode.SourceFormatUnsupported, "OpenAI Images requires a readable PNG or JPEG with visible pixels within the input limits.",
            "ImageProcessing_OpenAiUnsupportedInput");

    private static OperationFailure InvalidOutputFailure(IReadOnlyDictionary<string, string> evidence) =>
        Failure(FailureCode.OutputValidationFailed, "OpenAI Images did not return one decodable PNG with the required dimensions and Alpha.",
            "ImageProcessing_OpenAiInvalidOutput", evidence);

    private static OperationResult<AdapterOutput> Reject(FailureCode code, string detail, string messageKey) =>
        OperationResult.Fail<AdapterOutput>(Failure(code, detail, messageKey));

    private static OperationFailure Failure(
        FailureCode code, string detail, string messageKey, IReadOnlyDictionary<string, string>? evidence = null) =>
        OperationFailure.Create(code, detail, isRetryable: code is FailureCode.Timeout or FailureCode.AdapterUnavailable
            or FailureCode.OutputValidationFailed or FailureCode.WorkspaceError,
            messageKey: messageKey, context: evidence);

    private readonly record struct ImageAlpha(bool HasVisiblePixels, bool HasTransparentPixels);
    private sealed record EditResponse(byte[] Png, IReadOnlyDictionary<string, string> Evidence);
}
