using System.IO;
using System.Security.Cryptography;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using PrintFlow.Domain.Results;

namespace PrintFlow.Infrastructure.Imaging;

internal sealed class BiRefNetMatteModel(string modelPath)
{
    internal const string AlgorithmVersion = "birefnet-lite-a97176ab-ort-1.32.0-cpu-v1";
    private const string ModelSha256 = "A97176ABB6FE55525B11113BF5BEAD5A9B5866B2C0D4AC058186CABB86CADFFF";
    private const int ModelBytes = 180_666_557;
    private const ulong MinimumAvailableMemory = 4 * 1024 * 1024 * 1024UL;

    private readonly object _modelCheckLock = new();
    private (DateTime WriteTimeUtc, DateTime CreationTimeUtc, bool Verified)? _modelCheck;

    public OperationFailure? GetUnavailable()
    {
        try
        {
            var info = new FileInfo(modelPath);
            if (info.Length != ModelBytes)
                return InvalidModel();

            lock (_modelCheckLock)
            {
                if (_modelCheck is not { } cached || cached.WriteTimeUtc != info.LastWriteTimeUtc ||
                    cached.CreationTimeUtc != info.CreationTimeUtc)
                {
                    using var stream = new FileStream(modelPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    _modelCheck = (info.LastWriteTimeUtc, info.CreationTimeUtc, MatchesModel(SHA256.HashData(stream)));
                }

                if (!_modelCheck.Value.Verified)
                    return InvalidModel();
            }

            return LocalInferenceRuntime.CpuRuntimeFailure is { } failure
                ? failure with { MessageKey = "ImageProcessing_BiRefNetRuntimeUnavailable" }
                : null;
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return ModelMissing();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return InvalidModel(error.Message);
        }
    }

    public OperationResult<float[]> Infer(float[] input, CancellationToken token)
    {
        try
        {
            if (LocalInferenceRuntime.AvailablePhysicalMemory() < MinimumAvailableMemory)
                return OperationResult.Fail<float[]>(LowMemory());

            // Hash the exact bytes passed to ONNX Runtime, independently of the
            // cached availability check used by settings and task views.
            using var stream = new FileStream(modelPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length != ModelBytes)
                return OperationResult.Fail<float[]>(InvalidModel());
            byte[] model = new byte[ModelBytes];
            stream.ReadExactly(model);
            if (!MatchesModel(SHA256.HashData(model)))
                return OperationResult.Fail<float[]>(InvalidModel());

            token.ThrowIfCancellationRequested();
            using var options = new SessionOptions
            {
                ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
                IntraOpNumThreads = Math.Clamp(Environment.ProcessorCount, 1, 4),
                EnableCpuMemArena = false,
                EnableMemoryPattern = false,
            };
            using var session = new InferenceSession(model, options);
            token.ThrowIfCancellationRequested();
            int edge = BiRefNetMatte.Edge;
            using var tensor = OrtValue.CreateTensorValueFromMemory(input, [1, 3, edge, edge]);
            using var run = new RunOptions();
            using var registration = token.Register(static state => ((RunOptions)state!).Terminate = true, run);
            using var outputs = session.Run(run, ["input_image"], [tensor], ["output_image"]);
            token.ThrowIfCancellationRequested();
            OrtValue output = outputs.Single();
            var type = output.GetTensorTypeAndShape();
            if (type.ElementDataType != TensorElementType.Float ||
                !type.Shape.AsSpan().SequenceEqual([1L, 1L, (long)edge, edge]))
                throw new InvalidDataException("BiRefNet did not return the expected FP32 [1,1,1024,1024] matte.");
            return OperationResult.Ok(output.GetTensorDataAsSpan<float>().ToArray());
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return OperationResult.Fail<float[]>(ModelMissing());
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            token.ThrowIfCancellationRequested();
            return OperationResult.Fail<float[]>(InvalidModel(error.Message));
        }
        catch (OnnxRuntimeException error)
        {
            token.ThrowIfCancellationRequested();
            return OperationResult.Fail<float[]>(OperationFailure.Create(FailureCode.AdapterUnavailable, error.Message,
                isRetryable: true, messageKey: "ImageProcessing_BiRefNetRunFailed"));
        }
        catch (OutOfMemoryException error)
        {
            token.ThrowIfCancellationRequested();
            return OperationResult.Fail<float[]>(LowMemory(error.Message));
        }
    }

    private static bool MatchesModel(byte[] digest) => Convert.ToHexString(digest) == ModelSha256;

    private static OperationFailure ModelMissing() =>
        OperationFailure.Create(FailureCode.AdapterUnavailable, "The local BiRefNet Lite ONNX model is missing.",
            messageKey: "ImageProcessing_BiRefNetModelMissing");

    private static OperationFailure InvalidModel(string? detail = null) =>
        OperationFailure.Create(FailureCode.AdapterUnavailable, detail ?? "The BiRefNet model failed its pinned identity check.",
            messageKey: "ImageProcessing_BiRefNetModelInvalid");

    private static OperationFailure LowMemory(string? detail = null) =>
        OperationFailure.Create(FailureCode.AdapterUnavailable, detail ?? "BiRefNet CPU inference needs 4 GiB of available system memory.",
            isRetryable: true, messageKey: "ImageProcessing_BiRefNetLowMemory");
}
