using System.Runtime.InteropServices;
using PrintFlow.Domain.Results;
using static PrintFlow.Infrastructure.Automation.NativeMethods;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Adapters.OpenAI;

public sealed class WindowsOpenAiImageCredentials : IOpenAiImageCredentials
{
    private const uint GenericCredential = 1;
    private const uint PersistForWindowsUserOnThisMachine = 2;
    private const int NotFound = 1168;
    private const int MaximumKeyLength = 1024;
    private readonly string _target;

    public WindowsOpenAiImageCredentials() : this("PrintFlow Studio/OpenAI Images") { }

    internal WindowsOpenAiImageCredentials(string target) => _target = target;

    public OperationResult<string?> Read()
    {
        if (!CredReadW(_target, GenericCredential, 0, out nint handle))
            return Marshal.GetLastPInvokeError() == NotFound
                ? OperationResult.Ok<string?>(null)
                : OperationResult.Fail<string?>(StoreFailure());

        try
        {
            var credential = Marshal.PtrToStructure<WindowsCredential>(handle);
            if (credential.Blob == 0 || credential.BlobSize % 2 != 0 || credential.BlobSize > MaximumKeyLength * 2)
                return OperationResult.Fail<string?>(StoreFailure());
            string key = Marshal.PtrToStringUni(credential.Blob, checked((int)credential.BlobSize / 2));
            return IsValidKey(key) ? OperationResult.Ok<string?>(key) : OperationResult.Fail<string?>(StoreFailure());
        }
        finally
        {
            CredFree(handle);
        }
    }

    public OperationResult<Unit> Save(string key)
    {
        key = key.Trim();
        if (!IsValidKey(key))
            return OperationResult.Fail<Unit>(OperationFailure.Create(FailureCode.PreconditionNotMet,
                "Enter a valid OpenAI API key.", messageKey: "ImageProcessing_OpenAiKeyInvalid"));

        nint blob = Marshal.StringToCoTaskMemUni(key);
        try
        {
            var credential = new WindowsCredential
            {
                Type = GenericCredential,
                TargetName = _target,
                BlobSize = checked((uint)key.Length * 2),
                Blob = blob,
                Persist = PersistForWindowsUserOnThisMachine,
            };
            return CredWriteW(ref credential, 0) ? OperationResult.Ok() : OperationResult.Fail<Unit>(StoreFailure());
        }
        finally
        {
            Marshal.ZeroFreeCoTaskMemUnicode(blob);
        }
    }

    public OperationResult<Unit> Delete() =>
        CredDeleteW(_target, GenericCredential, 0) || Marshal.GetLastPInvokeError() == NotFound
            ? OperationResult.Ok() : OperationResult.Fail<Unit>(StoreFailure());

    internal static bool IsValidKey(string key) => key.Length is > 0 and <= MaximumKeyLength &&
        key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static OperationFailure StoreFailure() => OperationFailure.Create(FailureCode.AdapterUnavailable,
        "The Windows credential could not be accessed.", messageKey: "ImageProcessing_OpenAiCredentialStore");
}
