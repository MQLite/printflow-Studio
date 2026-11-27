using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
    private const string ResponsesModel = "gpt-6.2-sol";
    private const string AdapterId = "openai-responses-images";
    private const int MaximumInputBytes = 20 * 1024 * 1024;
    private const int MaximumResponseBytes = 48 * 1024 * 1024;
    private const int MaximumOutputEdge = 3840;
    private const long MinimumCustomOutputPixels = 655_360;
    private const long MaximumOutputPixels = 8_294_400;
    private const int CustomDimensionMultiple = 16;
    private static readonly Uri ResponsesEndpoint = new("https://api.openai.com/v1/responses");

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

            MagickFormat inputFormat = format == ImageFormat.Png ? MagickFormat.Png : MagickFormat.Jpeg;
            if (Decode(input, inputFormat, (w, h) => LocalImageFiles.Fits(w, h, 8192, 24_000_000))
                is not { HasVisiblePixels: true } source)
                return UnsupportedInput();

            bool transparent = !restore || source.HasTransparentPixels;
            bool cutout = request.Method == ImageProcessingMethod.OpenAiCutout;
            bool combined = request.Method == ImageProcessingMethod.OpenAiCombined;
            var sourceSize = (source.Width, source.Height);
            if (cutout && !LocalImageFiles.Fits(source.Width, source.Height, MaximumOutputEdge, MaximumOutputPixels))
                return Reject(FailureCode.SourceFormatUnsupported,
                    $"The {source.Width}×{source.Height} input exceeds the supported OpenAI cutout canvas.",
                    "ImageProcessing_OpenAiCutoutCanvasUnsupported");

            var size = restore ? EnhancementSize(source) : null;
            if (!combined && size is null && SupportsCustomOutputSize(source.Width, source.Height))
                size = sourceSize;
            bool AllowsOutputSize(uint width, uint height)
            {
                if (combined)
                    return true;
                if (cutout)
                    return LocalImageFiles.Fits(width, height, MaximumOutputEdge, MaximumOutputPixels);

                return (width, height) == (size ?? sourceSize) || ((width, height) == sourceSize &&
                    !LocalImageFiles.Fits(source.Width * 2, source.Height * 2, MaximumOutputEdge, MaximumOutputPixels));
            }
            using var message = CreateRequest(request.Method, key.Value, input, format, transparent, sourceSize, size);
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
                Decode(png, MagickFormat.Png, AllowsOutputSize)
                    is not { HasVisiblePixels: true } decoded ||
                decoded.HasTransparentPixels != transparent)
                return OperationResult.Fail<AdapterOutput>(InvalidOutputFailure(evidence));

            if (cutout && (decoded.Width, decoded.Height) != sourceSize)
                return Reject(FailureCode.OutputValidationFailed,
                    $"OpenAI returned {decoded.Width}×{decoded.Height} for a {source.Width}×{source.Height} cutout.",
                    "ImageProcessing_OpenAiCutoutCanvasChanged", evidence);

            if (restore && transparent)
                png = RestoreSourceAlpha(input, png, decoded.Width, decoded.Height, cancellationToken);

            FileWorkspace.WriteNewWorkingCandidate(outputPath, stream => stream.Write(png), cancellationToken);
            string notes = string.Join(", ", evidence.Select(pair => $"{pair.Key}={pair.Value}"));
            return OperationResult.Ok(new AdapterOutput(request.ExpectedOutput, stopwatch.Elapsed, notes)
            {
                ValidatedImageSha256 = Sha256.FromBytes(SHA256.HashData(png)),
                ImageProcessingEvidence = evidence,
            });
        }
        catch (MagickException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return OperationResult.Fail<AdapterOutput>(InvalidOutputFailure(evidence));
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
        if (_model != DefaultModel)
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

    private static (uint Width, uint Height)? EnhancementSize(ImageAlpha source)
    {
        uint longEdge = Math.Max(source.Width, source.Height);
        uint shortEdge = Math.Min(source.Width, source.Height);
        if (longEdge > 3 * shortEdge)
            return null;

        // Compute one scale for both axes, then round down to the sizes the API accepts.
        double scale = Math.Min(2.0, Math.Min((double)MaximumOutputEdge / longEdge,
            Math.Sqrt((double)MaximumOutputPixels / ((long)source.Width * source.Height))));
        uint width = AlignedDimension(source.Width * scale);
        uint height = AlignedDimension(source.Height * scale);
        width = Math.Min(width, 3 * height);
        height = Math.Min(height, 3 * width);

        // Leave the canvas at the original dimensions if there is no supported enlargement.
        if (width < source.Width || height < source.Height || (width == source.Width && height == source.Height) ||
            (long)width * height < MinimumCustomOutputPixels)
            return null;

        return (width, height);
    }

    private static uint AlignedDimension(double dimension) =>
        (uint)Math.Floor((dimension + 1e-9) / CustomDimensionMultiple) * CustomDimensionMultiple;

    private static bool SupportsCustomOutputSize(uint width, uint height) =>
        LocalImageFiles.Fits(width, height, MaximumOutputEdge, MaximumOutputPixels) &&
        (long)width * height >= MinimumCustomOutputPixels &&
        width % CustomDimensionMultiple == 0 && height % CustomDimensionMultiple == 0 &&
        Math.Max(width, height) <= 3 * Math.Min(width, height);

    private static byte[] RestoreSourceAlpha(byte[] input, byte[] output, uint width, uint height, CancellationToken token)
    {
        using var original = new MagickImage(input, MagickFormat.Png);
        using var candidate = new MagickImage(output, MagickFormat.Png);
        original.Progress += (_, progress) => progress.Cancel = token.IsCancellationRequested;
        candidate.Progress += (_, progress) => progress.Cancel = token.IsCancellationRequested;
        if (width != original.Width || height != original.Height)
        {
            original.FilterType = FilterType.Triangle;
            original.Resize(new MagickGeometry(width, height) { IgnoreAspectRatio = true });
        }

        // Fill any area the generated image made transparent from the original, then set
        // opacity from the resized original so the model cannot replace the transparency mask.
        candidate.Composite(original, CompositeOperator.DstOver);
        candidate.Composite(original, CompositeOperator.CopyAlpha);
        token.ThrowIfCancellationRequested();
        return candidate.ToByteArray(MagickFormat.Png);
    }

    private HttpRequestMessage CreateRequest(ImageProcessingMethod method, string key, byte[] input, ImageFormat format,
        bool transparent, (uint Width, uint Height) sourceSize, (uint Width, uint Height)? size)
    {
        string geometry = size is { } target && target != sourceSize
            ? FormattableString.Invariant($"Scale the full frame to {target.Width} × {target.Height} pixels, preserving framing and relative proportions.")
            : FormattableString.Invariant($"Keep the source canvas at {sourceSize.Width} × {sourceSize.Height} pixels.");
        const string shirtMockupInstruction = """
            If the artwork is displayed on a T-shirt mockup, make the entire shirt transparent, including its outline, fabric, folds, and shadows.
            Keep the printed artwork and lettering, including any clothing depicted within the artwork. Make exposed shirt fabric between design elements transparent.
            """;
        string prompt = method switch
        {
            ImageProcessingMethod.OpenAiRestore => $"""
                Edit the supplied image. Improve only the clarity and sharpness of existing detail.
                Preserve the entire original background. Keep all existing transparent areas and their edge coverage.
                Do not remove or replace any background, add new transparency, or cut out or isolate the subject.
                {geometry} Do not crop, extend, rotate, or reposition anything.
                Preserve the subject's identity, facial geometry, expression, skin texture, hair, accessories, and text.
                Do not add objects, retouch faces, or reconstruct details that are missing or obscured.
                Return one complete PNG.
                """,
            ImageProcessingMethod.OpenAiCutout => $"""
                Edit the supplied image to remove only the background. Make only clear background areas transparent.
                {shirtMockupInstruction}
                {geometry} Keep the exact pixel alignment of the foreground. Do not crop, extend, rotate, or reposition anything.
                Retain the foreground subject's original facial features, expression, skin texture, hair wisps, eyewear, accessories, clothing, and text.
                Keep natural partial transparency at fine edges and preserve existing transparent areas.
                When a detail could belong to the foreground, retain it.
                Do not add objects, retouch faces, sharpen the image, or reconstruct missing or obscured content.
                Return one complete transparent PNG.
                """,
            _ => $"""
                Edit the supplied image once to remove only the background and gently improve the clarity and sharpness of existing foreground detail.
                Make only clear background areas transparent. Keep existing transparent areas and natural partial transparency at fine edges.
                {shirtMockupInstruction}
                Preserve the full frame, aspect ratio, and relative proportions. Do not crop, extend, rotate, or reposition anything.
                Preserve the foreground subject's identity, facial geometry, expression, skin texture, hair wisps, accessories, clothing, and text.
                When a detail could belong to the foreground, retain it.
                Do not add objects, retouch faces, or reconstruct missing or obscured content.
                Return one complete transparent PNG.
                """,
        };
        string mime = format == ImageFormat.Png ? "image/png" : "image/jpeg";
        var message = new HttpRequestMessage(HttpMethod.Post, ResponsesEndpoint)
        {
            Content = JsonContent.Create(new
            {
                model = ResponsesModel,
                instructions = "Edit the supplied image with the image generation tool once. " +
                    "Carry every geometry, preservation, and output constraint from the edit instructions into the tool prompt.",
                store = true,
                max_tool_calls = 1,
                tool_choice = new { type = "image_generation" },
                tools = new[]
                {
                    new
                    {
                        type = "image_generation",
                        model = _model,
                        action = "edit",
                        background = transparent ? "transparent" : "opaque",
                        output_format = "png",
                        size = size is { } requested
                            ? FormattableString.Invariant($"{requested.Width}x{requested.Height}") : "auto",
                    },
                },
                input = new[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "input_text", text = prompt },
                            new { type = "input_image", image_url = $"data:{mime};base64,{Convert.ToBase64String(input)}", detail = "high" },
                        },
                    },
                },
            }),
        };
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
            if (response.Headers.TryGetValues("x-request-id", out var requestIds))
                RecordIdentifier(evidence, "requestId", requestIds.FirstOrDefault(), "req_", key);
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

            RecordIdentifier(evidence, "responseId", StringProperty(root, "id"), "resp_", key);
            if (StringProperty(root, "status") != "completed" ||
                FindSingleImage(root) is not { } image ||
                StringProperty(image, "status") != "completed" ||
                !image.TryGetProperty("result", out var encoded) || encoded.ValueKind != JsonValueKind.String ||
                !encoded.TryGetBytesFromBase64(out byte[]? png))
                return OperationResult.Fail<EditResponse>(InvalidOutputFailure(evidence));

            RecordIdentifier(evidence, "imageCallId", StringProperty(image, "id"), "ig_", key);
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

    private static JsonElement? FindSingleImage(JsonElement response)
    {
        if (!response.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
            return null;

        JsonElement? image = null;
        foreach (JsonElement item in output.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                return null;
            if (StringProperty(item, "type") != "image_generation_call")
                continue;
            if (image is not null)
                return null;
            image = item;
        }
        return image;
    }

    private static string? StringProperty(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static void RecordIdentifier(Dictionary<string, string> evidence, string name, string? id,
        string prefix, string key)
    {
        if (id is { Length: <= 128 } && id.Length > prefix.Length && id.StartsWith(prefix, StringComparison.Ordinal) &&
            id.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-') && !id.Contains(key, StringComparison.Ordinal))
            evidence.Add(name, id);
    }

    private static ImageAlpha? Decode(byte[] imageBytes, MagickFormat format, Func<uint, uint, bool> allowsSize)
    {
        try
        {
            var settings = new MagickReadSettings { Format = format };
            using var stream = new MemoryStream(imageBytes, writable: false);
            var info = new MagickImageInfo(stream, settings);
            if (!allowsSize(info.Width, info.Height))
                return null;

            stream.Position = 0;
            using var image = new MagickImage(stream, settings);
            if (image.Width != info.Width || image.Height != info.Height)
                return null;
            if (!image.HasAlpha || image.IsOpaque)
                return new ImageAlpha(image.Width, image.Height, true, false);

            var alpha = image.Statistics().GetChannel(PixelChannel.Alpha);
            return alpha is null ? null : new ImageAlpha(image.Width, image.Height, alpha.Maximum > 0, true);
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

    private static OperationFailure InvalidOutputFailure(IReadOnlyDictionary<string, string>? evidence) =>
        Failure(FailureCode.OutputValidationFailed, "OpenAI Images did not return one decodable PNG with the required dimensions and Alpha.",
            "ImageProcessing_OpenAiInvalidOutput", evidence);

    private static OperationResult<AdapterOutput> Reject(FailureCode code, string detail, string messageKey,
        IReadOnlyDictionary<string, string>? evidence = null) =>
        OperationResult.Fail<AdapterOutput>(Failure(code, detail, messageKey, evidence));

    private static OperationFailure Failure(
        FailureCode code, string detail, string messageKey, IReadOnlyDictionary<string, string>? evidence = null) =>
        OperationFailure.Create(code, detail, isRetryable: code is FailureCode.Timeout or FailureCode.AdapterUnavailable
            or FailureCode.OutputValidationFailed or FailureCode.WorkspaceError,
            messageKey: messageKey, context: evidence);

    private readonly record struct ImageAlpha(uint Width, uint Height, bool HasVisiblePixels, bool HasTransparentPixels);
    private sealed record EditResponse(byte[] Png, IReadOnlyDictionary<string, string> Evidence);
}
