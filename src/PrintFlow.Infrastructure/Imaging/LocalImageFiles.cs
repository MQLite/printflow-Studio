using System.Runtime.InteropServices;
using ImageMagick;
using ImageMagick.Formats;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Workspace;

namespace PrintFlow.Infrastructure.Imaging;

internal static class LocalImageFiles
{
    private static readonly Lazy<OperationFailure?> Runtime = new(CheckRuntime);

    public static OperationFailure? MagickRuntimeFailure => Runtime.Value;

    public static bool Fits(uint width, uint height, int maximumEdge, long maximumPixels) =>
        width > 0 && height > 0 && width <= maximumEdge && height <= maximumEdge && (long)width * height <= maximumPixels;

    public static void WriteCandidate(MagickImage image, string outputPath, PngWriteDefines defines, CancellationToken token)
    {
        image.Progress += (_, progress) => progress.Cancel = token.IsCancellationRequested;
        FileWorkspace.WriteNewWorkingCandidate(outputPath, output => image.Write(output, defines), token);
    }

    private static OperationFailure? CheckRuntime()
    {
        if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            try
            {
                _ = MagickNET.Features;
                return null;
            }
            catch (Exception error) when (error is DllNotFoundException or BadImageFormatException or
                EntryPointNotFoundException or TypeInitializationException)
            {
                return OperationFailure.Create(FailureCode.AdapterUnavailable, error.Message,
                    messageKey: "ImageProcessing_LocalRuntimeUnavailable");
            }
        }

        return OperationFailure.Create(FailureCode.AdapterUnavailable, "Local image processing requires the Windows x64 runtime.",
            messageKey: "ImageProcessing_LocalRuntimeUnavailable");
    }
}
