using System.IO;
using System.Security.Cryptography;
using ImageMagick;
using Microsoft.ML.OnnxRuntime;
using PrintFlow.Domain.Results;

namespace PrintFlow.Infrastructure.Imaging;

internal sealed class RealEsrganUpscaler
{
    internal const string AlgorithmVersion = "realesrgan-x2plus-7115ba92-ort-1.32.0-cpu-v1";
    private const string ModelSha256 = "7115BA92E8A1BFA63D68558EF006EF3D91273A068D321B1439F8BB1C9179002C";
    private const int ModelBytes = 67_073_434;
    internal const ulong MinimumAvailableMemory = 1536 * 1024 * 1024UL;
    private const int MaximumInputEdge = 1024;
    private const int MaximumInputPixels = 512 * 512;

    private readonly string _modelPath;
    private readonly Func<ulong> _availableMemory;
    private readonly SemaphoreSlim _inferenceSlot = new(1);
    private readonly object _modelCheckLock = new();
    private (DateTime WriteTimeUtc, DateTime CreationTimeUtc, bool Verified)? _modelCheck;

    public RealEsrganUpscaler(string modelPath, Func<ulong>? availableMemory = null)
    {
        _modelPath = modelPath;
        _availableMemory = availableMemory ?? LocalInferenceRuntime.AvailablePhysicalMemory;
    }

    public static bool Fits(uint width, uint height) =>
        LocalImageFiles.Fits(width, height, MaximumInputEdge, MaximumInputPixels);

    public OperationFailure? GetUnavailable() => CheckModel() ?? LocalInferenceRuntime.CpuRuntimeFailure;

    public OperationResult<MagickImage> Run(MagickImage source, CancellationToken token)
    {
        _inferenceSlot.Wait(token);
        try
        {
            if (GetUnavailable() is { } unavailable)
                return OperationResult.Fail<MagickImage>(unavailable);
            // Available memory is transient. A cached session view must keep the same
            // method runnable after the operator frees memory.
            if (_availableMemory() < MinimumAvailableMemory)
                return OperationResult.Fail<MagickImage>(LowMemory());

            // Read and hash the same bytes ORT will load. The cached UI capability is only a hint.
            using var modelStream = new FileStream(_modelPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (modelStream.Length != ModelBytes)
                return OperationResult.Fail<MagickImage>(InvalidModel());
            byte[] model = new byte[ModelBytes];
            modelStream.ReadExactly(model);
            if (!MatchesModel(SHA256.HashData(model)))
                return OperationResult.Fail<MagickImage>(InvalidModel());

            token.ThrowIfCancellationRequested();
            using var options = new SessionOptions
            {
                ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
                IntraOpNumThreads = Math.Clamp(Environment.ProcessorCount, 1, 4),
                EnableCpuMemArena = false,
                EnableMemoryPattern = false,
            };
            using var session = new InferenceSession(model, options);
            return OperationResult.Ok(Infer(session, source, token));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            token.ThrowIfCancellationRequested();
            return OperationResult.Fail<MagickImage>(InvalidModel(error.Message));
        }
        catch (OnnxRuntimeException error)
        {
            token.ThrowIfCancellationRequested();
            return OperationResult.Fail<MagickImage>(OperationFailure.Create(FailureCode.AdapterUnavailable, error.Message,
                isRetryable: true, messageKey: "ImageProcessing_UpscaleRunFailed"));
        }
        catch (OutOfMemoryException error)
        {
            token.ThrowIfCancellationRequested();
            return OperationResult.Fail<MagickImage>(LowMemory(error.Message));
        }
        finally
        {
            _inferenceSlot.Release();
        }
    }

    private static MagickImage Infer(InferenceSession session, MagickImage source, CancellationToken token)
    {
        IColorProfile? profile = source.GetColorProfile();
        if (profile is not null)
        {
            if (!source.TransformColorSpace(ColorProfiles.SRGB))
                throw new InvalidDataException("The input ICC profile could not be transformed to the model colour space.");
        }
        else if (source.ColorSpace != ColorSpace.sRGB)
        {
            source.ColorSpace = ColorSpace.sRGB;
        }

        int width = (int)source.Width;
        int height = (int)source.Height;
        bool hasAlpha = source.HasAlpha;
        int channels = hasAlpha ? 4 : 3;
        using var pixels = source.GetPixels();
        byte[] colours = (hasAlpha ? pixels.ToByteArray(PixelMapping.RGBA) : pixels.ToByteArray(PixelMapping.RGB))
            ?? throw new InvalidDataException("Could not export the image pixels for inference.");
        byte[]? alpha = hasAlpha ? ExtendColourIntoTransparentPixels(colours, width, height) : null;

        // The x2plus network uses pixel-unshuffle. Reflect-pad odd edges once, then
        // discard the extra output rows/columns to obtain precisely twice the input.
        int modelWidth = width + width % 2;
        int modelHeight = height + height % 2;
        int planeSize = modelWidth * modelHeight;
        var input = new float[3 * planeSize];
        for (int y = 0; y < modelHeight; y++)
        {
            token.ThrowIfCancellationRequested();
            int sy = y < height ? y : Math.Max(0, height - 2);
            for (int x = 0; x < modelWidth; x++)
            {
                int sx = x < width ? x : Math.Max(0, width - 2);
                int sourceIndex = (sy * width + sx) * channels;
                int planeIndex = y * modelWidth + x;
                for (int channel = 0; channel < 3; channel++)
                    input[channel * planeSize + planeIndex] = colours[sourceIndex + channel] / 255f;
            }
        }

        using var tensor = OrtValue.CreateTensorValueFromMemory(input, [1, 3, modelHeight, modelWidth]);
        using var run = new RunOptions();
        using var registration = token.Register(static state => ((RunOptions)state!).Terminate = true, run);
        using var outputs = session.Run(run, ["input"], [tensor], ["output"]);
        token.ThrowIfCancellationRequested();
        OrtValue output = outputs.Single();
        int stride = modelWidth * 2;
        int outputPlaneSize = planeSize * 4;
        if (!output.GetTensorTypeAndShape().Shape.AsSpan().SequenceEqual([1L, 3L, modelHeight * 2L, stride]))
            throw new InvalidDataException("The Real-ESRGAN tensor does not match its established 2× RGB contract.");

        int outputWidth = width * 2;
        int outputHeight = height * 2;
        ReadOnlySpan<float> values = output.GetTensorDataAsSpan<float>();
        byte[] resultPixels = new byte[outputWidth * outputHeight * channels];
        for (int y = 0; y < outputHeight; y++)
        {
            token.ThrowIfCancellationRequested();
            for (int x = 0; x < outputWidth; x++)
            {
                int pixel = (y * outputWidth + x) * channels;
                for (int channel = 0; channel < 3; channel++)
                {
                    float value = values[channel * outputPlaneSize + y * stride + x];
                    if (!float.IsFinite(value))
                        throw new InvalidDataException("The Real-ESRGAN tensor contains invalid sample values.");
                    resultPixels[pixel + channel] = (byte)MathF.Round(Math.Clamp(value, 0f, 1f) * 255f);
                }
                if (alpha is not null)
                    resultPixels[pixel + 3] = InterpolateAlpha(alpha, width, height, x, y);
            }
        }

        return CreateResult(source, profile, resultPixels, outputWidth, outputHeight, hasAlpha);
    }

    private static MagickImage CreateResult(MagickImage source, IColorProfile? originalProfile,
        byte[] pixels, int width, int height, bool hasAlpha)
    {
        var output = new MagickImage();
        try
        {
            output.ReadPixels(pixels, new PixelReadSettings((uint)width, (uint)height, StorageType.Char,
                hasAlpha ? PixelMapping.RGBA : PixelMapping.RGB));
            output.Depth = 8;
            output.Density = source.Density;
            if (originalProfile is not null)
            {
                output.SetProfile(ColorProfiles.SRGB);
                if (!output.TransformColorSpace(originalProfile))
                    throw new InvalidDataException("The model output could not be converted back to its original ICC profile.");
            }
            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }

    // Propagate nearest visible RGB through fully transparent pixels. These hidden
    // colours enter the RGB model, but the independently resampled alpha masks them.
    private static byte[] ExtendColourIntoTransparentPixels(byte[] colours, int width, int height)
    {
        int count = width * height;
        byte[] alpha = new byte[count];
        int[] nearest = new int[count];
        int[] distance = new int[count];
        int unset = width + height;
        for (int index = 0; index < count; index++)
        {
            alpha[index] = colours[index * 4 + 3];
            nearest[index] = alpha[index] == 0 ? -1 : index;
            distance[index] = alpha[index] == 0 ? unset : 0;
        }

        for (int index = 0; index < count; index++)
        {
            if (index % width > 0)
                Consider(index, index - 1);
            if (index >= width)
                Consider(index, index - width);
        }
        for (int index = count - 1; index >= 0; index--)
        {
            if (index % width < width - 1)
                Consider(index, index + 1);
            if (index + width < count)
                Consider(index, index + width);
        }

        for (int index = 0; index < count; index++)
        {
            if (alpha[index] != 0)
                continue;
            Span<byte> target = colours.AsSpan(index * 4, 3);
            int visible = nearest[index];
            if (visible >= 0)
                colours.AsSpan(visible * 4, 3).CopyTo(target);
            else
                target.Clear();
        }
        return alpha;

        void Consider(int pixel, int neighbour)
        {
            int candidate = distance[neighbour] + 1;
            if (candidate < distance[pixel])
            {
                distance[pixel] = candidate;
                nearest[pixel] = nearest[neighbour];
            }
        }
    }

    private static byte InterpolateAlpha(byte[] alpha, int width, int height, int x, int y)
    {
        // Centre-aligned 2× bilinear sampling, with clamp-to-edge at the canvas.
        int x0 = (x - 1) >> 1;
        int y0 = (y - 1) >> 1;
        int left = Math.Clamp(x0, 0, width - 1);
        int right = Math.Clamp(x0 + 1, 0, width - 1);
        int top = Math.Clamp(y0, 0, height - 1) * width;
        int bottom = Math.Clamp(y0 + 1, 0, height - 1) * width;
        int wx = x % 2 == 0 ? 3 : 1;
        int wy = y % 2 == 0 ? 3 : 1;
        int upper = alpha[top + left] * (4 - wx) + alpha[top + right] * wx;
        int lower = alpha[bottom + left] * (4 - wx) + alpha[bottom + right] * wx;
        return (byte)((upper * (4 - wy) + lower * wy + 8) / 16);
    }

    private OperationFailure? CheckModel()
    {
        try
        {
            var info = new FileInfo(_modelPath);
            if (!info.Exists)
                return OperationFailure.Create(FailureCode.AdapterUnavailable, "The local 2× ONNX model is missing.",
                    messageKey: "ImageProcessing_UpscaleModelMissing");
            if (info.Length != ModelBytes)
                return InvalidModel();

            lock (_modelCheckLock)
            {
                if (_modelCheck is { } cached && cached.WriteTimeUtc == info.LastWriteTimeUtc &&
                    cached.CreationTimeUtc == info.CreationTimeUtc)
                    return cached.Verified ? null : InvalidModel();

                using var stream = new FileStream(_modelPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                bool verified = MatchesModel(SHA256.HashData(stream));
                _modelCheck = (info.LastWriteTimeUtc, info.CreationTimeUtc, verified);
                return verified ? null : InvalidModel();
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return InvalidModel(error.Message);
        }
    }

    private static OperationFailure LowMemory(string? detail = null) =>
        OperationFailure.Create(FailureCode.AdapterUnavailable, detail ?? "2× CPU inference needs 1.5 GiB of available system memory.",
            isRetryable: true, messageKey: "ImageProcessing_UpscaleLowMemory");

    private static bool MatchesModel(byte[] digest) => Convert.ToHexString(digest) == ModelSha256;

    private static OperationFailure InvalidModel(string? detail = null) =>
        OperationFailure.Create(FailureCode.AdapterUnavailable, detail ?? "The 2× ONNX model failed its pinned identity check.",
            messageKey: "ImageProcessing_UpscaleModelInvalid");
}
