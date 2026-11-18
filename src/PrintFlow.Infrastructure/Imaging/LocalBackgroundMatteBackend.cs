using System.Diagnostics;
using System.Globalization;
using System.IO;
using ImageMagick;
using ImageMagick.Formats;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Settings;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Imaging;

public sealed class LocalBackgroundMatteBackend : IImageProcessingBackend
{
    public const string ModelFileName = "birefnet-lite-1024.onnx";
    private const int MaximumEdge = 8192;
    private const long MaximumPixels = 24_000_000;
    private const long MaximumFileBytes = 128L * 1024 * 1024;

    private readonly IWorkspace _workspace;
    private readonly Func<OperationFailure?> _modelUnavailable;
    private readonly Func<float[], CancellationToken, OperationResult<float[]>> _infer;
    private readonly SemaphoreSlim _slot = new(1);

    public LocalBackgroundMatteBackend(IWorkspace workspace, string modelPath)
        : this(workspace, new BiRefNetMatteModel(modelPath))
    {
    }

    private LocalBackgroundMatteBackend(IWorkspace workspace, BiRefNetMatteModel model)
        : this(workspace, model.GetUnavailable, model.Infer)
    {
    }

    internal LocalBackgroundMatteBackend(IWorkspace workspace, Func<OperationFailure?> modelUnavailable,
        Func<float[], CancellationToken, OperationResult<float[]>> infer)
    {
        _workspace = workspace;
        _modelUnavailable = modelUnavailable;
        _infer = infer;
    }

    public ImageProcessingCapability? GetCapability(ImageProcessingMethod method) =>
        method == ImageProcessingMethod.BiRefNet
            ? new("local-birefnet-cpu", BiRefNetMatteModel.AlgorithmVersion, GetUnavailable())
            : null;

    public async Task<OperationResult<AdapterOutput>> ProcessAsync(ImageProcessingRequest request, CancellationToken cancellationToken)
    {
        await _slot.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => Process(request, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _slot.Release();
        }
    }

    private OperationResult<AdapterOutput> Process(ImageProcessingRequest request, CancellationToken token)
    {
        if (request.Operation != OperationKind.RemoveBackground || request.Method != ImageProcessingMethod.BiRefNet ||
            request.AlgorithmVersion != BiRefNetMatteModel.AlgorithmVersion ||
            request.Input.Area != WorkspaceArea.Working || request.ExpectedOutput.Area != WorkspaceArea.Working ||
            request.BackgroundRemovalAuthority is not { Decision: BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent,
                Mode: BackgroundRemovalMode.Automatic } authority ||
            !authority.Authorises(request.InputRevision, request.InputSha256))
        {
            return OperationResult.Fail<AdapterOutput>(FailureCode.PreconditionNotMet,
                "Local BiRefNet requires the locked algorithm, assigned working files and matching reviewed-content authority.");
        }

        if (GetUnavailable() is { } unavailable)
            return OperationResult.Fail<AdapterOutput>(unavailable);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            string inputPath = _workspace.ResolveAbsolute(request.Input);
            string outputPath = _workspace.ResolveAbsolute(request.ExpectedOutput);
            if (string.Equals(inputPath, outputPath, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetDirectoryName(inputPath), Path.GetDirectoryName(outputPath), StringComparison.OrdinalIgnoreCase))
            {
                return OperationResult.Fail<AdapterOutput>(FailureCode.PreconditionNotMet,
                    "The BiRefNet output must be a distinct sibling of the working input.");
            }

            using var input = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length > MaximumFileBytes)
                return TooLarge();

            Span<byte> header = stackalloc byte[FormatSniffer.RequiredHeaderLength];
            if (input.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) != header.Length)
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
            input.Position = 0;
            var info = new MagickImageInfo(input, settings);
            if (!LocalImageFiles.Fits(info.Width, info.Height, MaximumEdge, MaximumPixels))
                return TooLarge();

            token.ThrowIfCancellationRequested();
            input.Position = 0;
            using var source = new MagickImage(input, settings);
            if (source.ColorSpace is not (ColorSpace.sRGB or ColorSpace.Gray) || source.Depth > 16)
                return Unsupported();

            uint depth = source.Depth > 8 ? 16u : 8u;
            float[] modelInput = BiRefNetMatte.PrepareInput(source, token);
            var inferred = _infer(modelInput, token);
            token.ThrowIfCancellationRequested();
            if (inferred.IsFailure)
                return OperationResult.Fail<AdapterOutput>(inferred.Failure);

            BiRefNetMatte.Apply(source, inferred.Value, depth, token);
            var png = new PngWriteDefines
            {
                BitDepth = depth,
                PreserveiCCP = true,
                ColorType = source.ColorSpace == ColorSpace.Gray ? ColorType.GrayscaleAlpha : ColorType.TrueColorAlpha,
            };
            LocalImageFiles.WriteCandidate(source, outputPath, png, token);
            return OperationResult.Ok(new AdapterOutput(request.ExpectedOutput, stopwatch.Elapsed,
                string.Create(CultureInfo.InvariantCulture, $"Input and output {source.Width}x{source.Height} px, {depth}-bit Alpha.")));
        }
        catch (InvalidDataException error)
        {
            token.ThrowIfCancellationRequested();
            return OperationResult.Fail<AdapterOutput>(OperationFailure.Create(FailureCode.OutputValidationFailed,
                error.Message, messageKey: "ImageProcessing_BiRefNetInvalidMatte"));
        }
        catch (MagickException error)
        {
            token.ThrowIfCancellationRequested();
            return OperationResult.Fail<AdapterOutput>(OperationFailure.Create(FailureCode.OutputValidationFailed,
                error.Message, messageKey: "ImageProcessing_BiRefNetImageError"));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            token.ThrowIfCancellationRequested();
            return OperationResult.Fail<AdapterOutput>(FailureCode.WorkspaceError, error.Message, isRetryable: true);
        }
    }

    private OperationFailure? GetUnavailable() =>
        LocalImageFiles.MagickRuntimeFailure is { } failure
            ? failure with { MessageKey = "ImageProcessing_BiRefNetRuntimeUnavailable" }
            : _modelUnavailable();

    private static OperationResult<AdapterOutput> TooLarge() =>
        OperationResult.Fail<AdapterOutput>(OperationFailure.Create(FailureCode.PreconditionNotMet,
            "BiRefNet supports images up to 8192 px per edge, 24 megapixels and 128 MiB.",
            messageKey: "ImageProcessing_BiRefNetTooLarge"));

    private static OperationResult<AdapterOutput> Unsupported() =>
        OperationResult.Fail<AdapterOutput>(OperationFailure.Create(FailureCode.SourceFormatUnsupported,
            "BiRefNet accepts RGB or grayscale PNG and JPEG images up to 16 bits per channel.",
            messageKey: "ImageProcessing_BiRefNetUnsupported"));
}
