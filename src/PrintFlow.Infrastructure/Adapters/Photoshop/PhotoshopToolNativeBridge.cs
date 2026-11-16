using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Automation;

namespace PrintFlow.Infrastructure.Adapters.Photoshop;

internal interface IPhotoshopToolNativeBridge
{
    OperationResult<Unit> SelectMoveTool(string expectedDocumentFullPath, PhotoshopBaseline baseline);
}

/// <summary>Selects one fixed tool on the exact managed document in the accepted running instance.</summary>
internal sealed class RotPhotoshopToolNativeBridge : IPhotoshopToolNativeBridge
{
    public OperationResult<Unit> SelectMoveTool(string expectedDocumentFullPath, PhotoshopBaseline baseline)
    {
        object? application = null;
        try
        {
            Marshal.ThrowExceptionForHR(NativeMethods.CLSIDFromProgID("Photoshop.Application.130", out Guid id));
            Marshal.ThrowExceptionForHR(NativeMethods.GetActiveObject(ref id, 0, out application));
            if (application is null) throw new InvalidOperationException("No running Photoshop object.");
            Type type = application.GetType();
            string path = Convert.ToString(type.InvokeMember("Path", BindingFlags.GetProperty, null,
                application, null), CultureInfo.InvariantCulture) ?? "";
            if (!string.Equals(Path.GetFullPath(path.TrimEnd('\\', '/')),
                Path.GetDirectoryName(Path.GetFullPath(baseline.ExecutablePath)), StringComparison.OrdinalIgnoreCase))
                return Refused("The running automation object does not belong to the accepted installation.");

            object? raw = type.InvokeMember("DoJavaScript", BindingFlags.InvokeMethod, null,
                application, [PhotoshopMoveToolProgram.Create(expectedDocumentFullPath)]);
            if (raw is string response && response == PhotoshopMoveToolProgram.SuccessResponse)
                return OperationResult.Ok();

            const string errorPrefix = "PF-MOVE-1\nERROR\n";
            string detail = raw is string failure && failure.StartsWith(errorPrefix, StringComparison.Ordinal)
                ? failure[errorPrefix.Length..]
                : "Photoshop did not return an accepted tool read-back.";
            return Refused(detail);
        }
        catch (Exception ex) when (ex is COMException or TargetInvocationException or ArgumentException or InvalidOperationException)
        {
            string detail = ex is TargetInvocationException { InnerException: { } inner } ? inner.Message : ex.Message;
            return Refused(detail);
        }
        finally
        {
            // Release only this acquisition; other automation readers may share the same RCW.
            if (application is not null && Marshal.IsComObject(application)) Marshal.ReleaseComObject(application);
        }
    }

    private static OperationResult<Unit> Refused(string detail) => OperationResult.Fail<Unit>(OperationFailure.Create(
        FailureCode.PhotoshopUnknownState,
        "Photoshop's Move tool could not be confirmed after opening the managed document. " +
        "Further processing was stopped: " + detail,
        isRetryable: false,
        context: new Dictionary<string, string>
        {
            ["toolSelectionConfirmed"] = "false",
            ["documentMutationInvoked"] = "false",
            ["automaticRetry"] = "false",
        }));
}

internal static class PhotoshopMoveToolProgram
{
    internal const string SuccessResponse = "PF-MOVE-1\nmoveTool";

    internal static string Create(string expectedDocumentFullPath)
    {
        string expectedPath = JsonSerializer.Serialize(Path.GetFullPath(expectedDocumentFullPath));
        return $$"""
            (function () {
                try {
                    if (app.documents.length < 1) { throw new Error('No active document.'); }
                    if (String(app.activeDocument.fullName.fsName).toLowerCase() !== {{expectedPath}}.toLowerCase()) {
                        throw new Error('The active document is not the exact managed Working document.');
                    }
                    // Tool state survives opening files. Do not depend on keyboard focus or shortcuts.
                    if (String(app.currentTool) !== 'moveTool') { app.currentTool = 'moveTool'; }
                    if (String(app.currentTool) !== 'moveTool') {
                        throw new Error('The Move tool was not selected; observed ' + String(app.currentTool) + '.');
                    }
                    return 'PF-MOVE-1\nmoveTool';
                } catch (error) {
                    return 'PF-MOVE-1\nERROR\n' + String(error);
                }
            }())
            """;
    }
}
