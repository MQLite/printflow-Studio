using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Automation;

namespace PrintFlow.Infrastructure.Imaging;

internal static class LocalInferenceRuntime
{
    private static readonly Lazy<OperationFailure?> Runtime = new(CheckCpuRuntime);

    public static OperationFailure? CpuRuntimeFailure => Runtime.Value;

    public static ulong AvailablePhysicalMemory()
    {
        var status = new NativeMethods.MemoryStatus { Length = (uint)Marshal.SizeOf<NativeMethods.MemoryStatus>() };
        return NativeMethods.GlobalMemoryStatusEx(ref status) != 0 ? status.AvailablePhysical : 0;
    }

    private static OperationFailure? CheckCpuRuntime()
    {
        try
        {
            if (OrtEnv.Instance().GetAvailableProviders().Contains("CPUExecutionProvider", StringComparer.Ordinal))
                return null;
        }
        catch (Exception error) when (error is TypeInitializationException or DllNotFoundException or
            BadImageFormatException or EntryPointNotFoundException or OnnxRuntimeException)
        {
            return OperationFailure.Create(FailureCode.AdapterUnavailable, error.Message,
                messageKey: "ImageProcessing_LocalRuntimeUnavailable");
        }

        return OperationFailure.Create(FailureCode.AdapterUnavailable, "ONNX Runtime's CPU execution provider is unavailable.",
            messageKey: "ImageProcessing_LocalRuntimeUnavailable");
    }
}
