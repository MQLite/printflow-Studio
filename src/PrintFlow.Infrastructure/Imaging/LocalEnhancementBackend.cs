using System.Diagnostics;
using System.Globalization;
using System.IO;
using ImageMagick;
using ImageMagick.Formats;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Settings;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Imaging;

public sealed class LocalEnhancementBackend : IImageProcessingBackend
{
    public const string ModelFileName = "real_esrgan_x2.onnx";
    private const int SharpenMaximumEdge = 8192;
    private const long SharpenMaximumPixels = 24_000_000;
    private const long MaximumFileBytes = 128L * 1024 * 1024;
    private readonly IWorkspace _workspace;
    private readonly RealEsrganUpscaler _upscaler;

    public LocalEnhancementBackend(IWorkspace workspace, string modelPath)
        : this(workspace, new RealEsrganUpscaler(modelPath))
    {
    }

    internal LocalEnhancementBackend(IWorkspace workspace, RealEsrganUpscaler upscaler)
    {
        _workspace = workspace;
        _upscaler = upscaler;
    }

    internal static string VersionFor(ImageProcessingMethod method) => method switch
    {
        ImageProcessingMethod.LightSharpen => "magick-net-q16-15.1.0-unsharp-v1",
        ImageProcessingMethod.AiUpscale => RealEsrganUpscaler.AlgorithmVersion,
        _ => throw new ArgumentOutOfRangeException(nameof(method)),
    };

    public ImageProcessingCapability? GetCapability(ImageProcessingMethod method) => method switch
    {
        ImageProcessingMethod.LightSharpen => new("local-magick-sharpen", VersionFor(method), LocalImageFiles.MagickRuntimeFailure),
        ImageProcessingMethod.AiUpscale => new("local-realesrgan-cpu", VersionFor(method),
            LocalImageFiles.MagickRuntimeFailure ?? _upscaler.GetUnavailable()),
        _ => null,
    };

    public Task<OperationResult<AdapterOutput>> ProcessAsync(ImageProcessingRequest request, CancellationToken cancellationToken) =>
        Task.Run(() => Process(request, cancellationToken), cancellationToken);

    private OperationResult<AdapterOutput> Process(ImageProcessingRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (request.Operation != OperationKind.Enhance ||
            request.Method is not (ImageProcessingMethod.LightSharpen or ImageProcessingMethod.AiUpscale) ||
            request.AlgorithmVersion != VersionFor(request.Method) ||
            request.Input.Area != WorkspaceArea.Working || request.ExpectedOutput.Area != WorkspaceArea.Working)
        {
            return OperationResult.Fail<AdapterOutput>(FailureCode.PreconditionNotMet,
                "Local enhancement requires its locked algorithm version and assigned working files.");
        }

        bool upscale = request.Method == ImageProcessingMethod.AiUpscale;
        if (LocalImageFiles.MagickRuntimeFailure is { } runtimeFailure)
            return OperationResult.Fail<AdapterOutput>(runtimeFailure);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            string inputPath = _workspace.ResolveAbsolute(request.Input);
            string outputPath = _workspace.ResolveAbsolute(request.ExpectedOutput);
            if (string.Equals(inputPath, outputPath, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetDirectoryName(inputPath), Path.GetDirectoryName(outputPath), StringComparison.OrdinalIgnoreCase))
            {
                return OperationResult.Fail<AdapterOutput>(FailureCode.PreconditionNotMet,
                    "Enhancement output must be a distinct sibling of the working input.");
            }

            using var stream = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaximumFileBytes)
                return TooLarge(upscale);

            Span<byte> header = stackalloc byte[FormatSniffer.RequiredHeaderLength];
            if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) != header.Length)
                return Unsupported();
            MagickFormat format = FormatSniffer.Detect(header) switch
            {
                ImageFormat.Png => MagickFormat.Png,
                ImageFormat.Jpeg => MagickFormat.Jpeg,
                _ => MagickFormat.Unknown,
            };
            if (format == MagickFormat.Unknown)
                return Unsupported();

            var settings = new MagickReadSettings { Format = format };
            stream.Position = 0;
            var info = new MagickImageInfo(stream, settings);
            if (upscale ? !LocalUpscaleLimits.Fits(info.Width, info.Height) :
                !LocalImageFiles.Fits(info.Width, info.Height, SharpenMaximumEdge, SharpenMaximumPixels))
            {
                return TooLarge(upscale);
            }
            token.ThrowIfCancellationRequested();
            stream.Position = 0;
            using var source = new MagickImage(stream, settings);
            if (source.ColorSpace is not (ColorSpace.sRGB or ColorSpace.Gray) || source.Depth > 16)
                return Unsupported();
            if (upscale && source.Depth > 8)
            {
                return OperationResult.Fail<AdapterOutput>(OperationFailure.Create(FailureCode.PreconditionNotMet,
                    "Real-ESRGAN v1 accepts source samples up to 8 bits per channel.",
                    messageKey: "ImageProcessing_UpscaleBitDepth"));
            }

            source.AutoOrient();
            uint width = source.Width;
            uint height = source.Height;
            if (upscale)
            {
                var enhanced = _upscaler.Run(source, token, request.UpscaleProgress);
                if (enhanced.IsFailure)
                    return OperationResult.Fail<AdapterOutput>(enhanced.Failure);
                using var candidate = enhanced.Value;
                WriteCandidate(candidate, outputPath, 8, token);
            }
            else
            {
                uint depth = source.Depth > 8 ? 16u : 8u;
                Sharpen(source, token);
                WriteCandidate(source, outputPath, depth, token);
            }

            int scale = upscale ? 2 : 1;
            return OperationResult.Ok(new AdapterOutput(request.ExpectedOutput, stopwatch.Elapsed,
                string.Create(CultureInfo.InvariantCulture, $"Input {width}x{height}, output {width * scale}x{height * scale} px.")));
        }
        catch (Exception error) when (error is MagickException or InvalidDataException)
        {
            token.ThrowIfCancellationRequested();
            return OperationResult.Fail<AdapterOutput>(OperationFailure.Create(FailureCode.OutputValidationFailed, error.Message,
                messageKey: "ImageProcessing_LocalImageError"));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            token.ThrowIfCancellationRequested();
            return OperationResult.Fail<AdapterOutput>(FailureCode.WorkspaceError, error.Message, isRetryable: true);
        }
    }

    private static void Sharpen(MagickImage image, CancellationToken token)
    {
        image.Progress += (_, progress) => progress.Cancel = token.IsCancellationRequested;
        if (!image.HasAlpha)
        {
            image.UnsharpMask(0, 0.7, 0.45, 0.015);
            return;
        }

        // The default blur respects alpha when accumulating colour. Restore the original
        // alpha plane after sharpening so opacity itself receives no unsharp mask.
        using var original = image.Clone();
        image.UnsharpMask(0, 0.7, 0.45, 0.015);
        token.ThrowIfCancellationRequested();
        image.Composite(original, CompositeOperator.CopyAlpha);
    }

    private static void WriteCandidate(MagickImage image, string outputPath, uint depth, CancellationToken token) =>
        LocalImageFiles.WriteCandidate(image, outputPath, new PngWriteDefines { BitDepth = depth, PreserveiCCP = true }, token);

    private static OperationResult<AdapterOutput> TooLarge(bool upscale) =>
        OperationResult.Fail<AdapterOutput>(OperationFailure.Create(FailureCode.PreconditionNotMet,
            "The working image exceeds the selected enhancement method's input or output limits.",
            messageKey: upscale ? "ImageProcessing_UpscaleTooLarge" : "ImageProcessing_SharpenTooLarge"));

    private static OperationResult<AdapterOutput> Unsupported() =>
        OperationResult.Fail<AdapterOutput>(OperationFailure.Create(FailureCode.SourceFormatUnsupported,
            "Local enhancement accepts supported RGB or grayscale PNG and JPEG images.",
            messageKey: "ImageProcessing_LocalImageUnsupported"));
}
