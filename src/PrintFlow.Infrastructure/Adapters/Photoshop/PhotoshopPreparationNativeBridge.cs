using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Automation;

namespace PrintFlow.Infrastructure.Adapters.Photoshop;

/// <summary>The only three Photoshop-native resampling identifiers accepted by B1A.3.</summary>
internal enum PhotoshopNativeResampleMethod
{
    NONE,
    BICUBICSHARPER,
    PRESERVEDETAILS,
}

/// <summary>Closed Infrastructure-only mapping from neutral policy to Photoshop CC 2019.</summary>
internal static class PhotoshopResizePolicyMapping
{
    internal static OperationResult<PhotoshopNativeResampleMethod> Map(PhotoshopResizeMode policy) =>
        policy switch
        {
            PhotoshopResizeMode.None => OperationResult.Ok(PhotoshopNativeResampleMethod.NONE),
            PhotoshopResizeMode.BicubicSharper =>
                OperationResult.Ok(PhotoshopNativeResampleMethod.BICUBICSHARPER),
            PhotoshopResizeMode.PreserveDetails =>
                OperationResult.Ok(PhotoshopNativeResampleMethod.PRESERVEDETAILS),
            _ => OperationResult.Fail<PhotoshopNativeResampleMethod>(OperationFailure.Create(
                FailureCode.PreconditionNotMet,
                $"Photoshop resize policy value {(int)policy} is unsupported. Nothing was changed.",
                context: new Dictionary<string, string>
                {
                    ["mutationInvoked"] = "false",
                    ["fallbackUsed"] = "false",
                })),
        };
}

/// <summary>
/// One fixed native call. Expected source pixels are preconditions, never resize targets; no
/// projected pixel pair crosses this boundary.
/// </summary>
internal sealed record PhotoshopNativePreparationCommand(
    string ExpectedDocumentFullPath,
    int ExpectedSourcePixelWidth,
    int ExpectedSourcePixelHeight,
    LimitingEdge Edge,
    double? EdgeMillimetres,
    int ResolutionPpi,
    PhotoshopNativeResampleMethod ResampleMethod);

internal sealed record PhotoshopNativePreparationOutcome(
    bool Succeeded,
    bool MutationInvoked,
    string? FailureDetail,
    PhotoshopDocumentFacts? Before,
    PhotoshopDocumentFacts? After);

/// <summary>
/// The replaceable operating-system boundary beneath the typed preparation operation. It is
/// internal so neither App nor Workflow can acquire a generic native-command capability.
/// </summary>
internal interface IPhotoshopPreparationNativeBridge
{
    OperationResult<PhotoshopNativePreparationOutcome> ApplyOnce(
        PhotoshopNativePreparationCommand command,
        PhotoshopBaseline baseline);
}

/// <summary>
/// Attaches to the already-running Photoshop CC 2019 object and executes one fixed Image Size
/// program. It never activates the registered LocalServer and never accepts caller script.
/// </summary>
internal sealed class RotPhotoshopPreparationNativeBridge : IPhotoshopPreparationNativeBridge
{
    private const string PhotoshopCc2019ProgId = "Photoshop.Application.130";

    public OperationResult<PhotoshopNativePreparationOutcome> ApplyOnce(
        PhotoshopNativePreparationCommand command,
        PhotoshopBaseline baseline)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(baseline);

        object? application = null;
        try
        {
            int classResult = NativeMethods.CLSIDFromProgID(PhotoshopCc2019ProgId, out Guid classId);
            if (classResult < 0)
            {
                Marshal.ThrowExceptionForHR(classResult);
            }

            int activeResult = NativeMethods.GetActiveObject(ref classId, 0, out application);
            if (activeResult < 0 || application is null)
            {
                if (activeResult < 0)
                {
                    Marshal.ThrowExceptionForHR(activeResult);
                }

                return NotAttached("The running Photoshop CC 2019 automation object was not available.");
            }

            Type automationType = application.GetType();
            string reportedPath = Convert.ToString(automationType.InvokeMember(
                "Path",
                BindingFlags.GetProperty,
                binder: null,
                target: application,
                args: null), CultureInfo.InvariantCulture) ?? string.Empty;

            string expectedDirectory = Path.GetDirectoryName(Path.GetFullPath(baseline.ExecutablePath))!;
            string actualDirectory = Path.GetFullPath(reportedPath.TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!string.Equals(expectedDirectory, actualDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return NotAttached(
                    $"The running automation object reports '{actualDirectory}', not the accepted " +
                    $"Photoshop directory '{expectedDirectory}'. Nothing was changed.");
            }

            string program = PhotoshopPreparationProgram.Create(command);
            object? raw = automationType.InvokeMember(
                "DoJavaScript",
                BindingFlags.InvokeMethod,
                binder: null,
                target: application,
                args: [program]);

            if (raw is not string response || string.IsNullOrWhiteSpace(response))
            {
                return OperationResult.Fail<PhotoshopNativePreparationOutcome>(OperationFailure.Create(
                    FailureCode.PhotoshopUnknownState,
                    "Photoshop returned no factual preparation observation. Success is unknown and " +
                    "no retry was attempted.",
                    context: new Dictionary<string, string>
                    {
                        ["mutationMayBeRetained"] = "true",
                        ["automaticRetry"] = "false",
                    }));
            }

            OperationResult<PhotoshopNativePreparationOutcome> decoded = Decode(response);
            if (decoded.IsFailure)
            {
                return decoded;
            }

            return decoded;
        }
        catch (Exception ex) when (ex is COMException or TargetInvocationException or
                                   InvalidOperationException or ArgumentException or JsonException)
        {
            string detail = ex is TargetInvocationException { InnerException: { } inner }
                ? inner.Message
                : ex.Message;
            return OperationResult.Fail<PhotoshopNativePreparationOutcome>(OperationFailure.Create(
                FailureCode.PhotoshopTargetLost,
                $"The exact running Photoshop automation object could not complete the fixed " +
                $"preparation call: {detail}. No automatic retry was attempted.",
                isRetryable: false,
                context: new Dictionary<string, string>
                {
                    ["mutationStateKnown"] = "false",
                    ["mutationMayBeRetained"] = "true",
                    ["automaticRetry"] = "false",
                }));
        }
        finally
        {
            if (application is not null && Marshal.IsComObject(application))
            {
                _ = Marshal.FinalReleaseComObject(application);
            }
        }
    }

    private static OperationResult<PhotoshopNativePreparationOutcome> NotAttached(string detail) =>
        OperationResult.Fail<PhotoshopNativePreparationOutcome>(OperationFailure.Create(
            FailureCode.PhotoshopTargetLost,
            detail,
            isRetryable: true,
            context: new Dictionary<string, string>
            {
                ["mutationInvoked"] = "false",
                ["automaticRetry"] = "false",
            }));

    private static OperationResult<PhotoshopNativePreparationOutcome> Decode(string response)
    {
        try
        {
            string[] lines = response.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            if (lines.Length != 6 || !string.Equals(lines[0], "PF-B1A3-1", StringComparison.Ordinal))
            {
                return DecodeFailure();
            }

            bool succeeded = lines[1] switch
            {
                "1" => true,
                "0" => false,
                _ => throw new FormatException("Invalid success flag."),
            };
            bool mutationInvoked = lines[2] switch
            {
                "1" => true,
                "0" => false,
                _ => throw new FormatException("Invalid mutation flag."),
            };
            string? error = string.IsNullOrEmpty(lines[3])
                ? null
                : Uri.UnescapeDataString(lines[3]);
            PhotoshopDocumentFacts? before = DecodeFacts(lines[4]);
            PhotoshopDocumentFacts? after = DecodeFacts(lines[5]);

            return OperationResult.Ok(new PhotoshopNativePreparationOutcome(
                succeeded, mutationInvoked, error, before, after));
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or UriFormatException)
        {
            return DecodeFailure(ex.Message);
        }
    }

    private static PhotoshopDocumentFacts? DecodeFacts(string packed)
    {
        if (string.IsNullOrEmpty(packed))
        {
            return null;
        }

        string[] fields = packed.Split('|');
        if (fields.Length != 11)
        {
            throw new FormatException($"Expected 11 fact fields; observed {fields.Length}.");
        }

        ImmutableArray<PhotoshopChannelFact> channels = string.IsNullOrEmpty(fields[10])
            ? []
            : [.. fields[10].Split(';').Select(entry =>
            {
                string[] pair = entry.Split(',');
                if (pair.Length != 2)
                {
                    throw new FormatException("A channel fact did not contain exactly two fields.");
                }

                return new PhotoshopChannelFact(
                    Uri.UnescapeDataString(pair[0]), Uri.UnescapeDataString(pair[1]));
            })];

        return new PhotoshopDocumentFacts(
            Uri.UnescapeDataString(fields[0]),
            int.Parse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture),
            int.Parse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture),
            double.Parse(fields[3], NumberStyles.Float, CultureInfo.InvariantCulture),
            double.Parse(fields[4], NumberStyles.Float, CultureInfo.InvariantCulture),
            double.Parse(fields[5], NumberStyles.Float, CultureInfo.InvariantCulture),
            Uri.UnescapeDataString(fields[6]),
            Uri.UnescapeDataString(fields[7]),
            channels,
            fields[8] switch
            {
                "1" => true,
                "0" => false,
                _ => throw new FormatException("Invalid W1 flag."),
            },
            int.Parse(fields[9], NumberStyles.Integer, CultureInfo.InvariantCulture));
    }

    private static OperationResult<PhotoshopNativePreparationOutcome> DecodeFailure(string? detail = null) =>
        OperationResult.Fail<PhotoshopNativePreparationOutcome>(OperationFailure.Create(
            FailureCode.PhotoshopUnknownState,
            "Photoshop's fixed preparation observation did not match the accepted PF-B1A3-1 " +
            $"protocol{(string.IsNullOrEmpty(detail) ? "." : $": {detail}")} Success is unknown and " +
            "no retry was attempted.",
            context: new Dictionary<string, string>
            {
                ["mutationMayBeRetained"] = "true",
                ["automaticRetry"] = "false",
            }));
}

/// <summary>Builds the fixed operation-specific program; there is no caller-supplied source.</summary>
internal static class PhotoshopPreparationProgram
{
    internal static string Create(PhotoshopNativePreparationCommand command)
    {
        string expectedPath = JsonSerializer.Serialize(Path.GetFullPath(command.ExpectedDocumentFullPath));
        string edgeValue = command.EdgeMillimetres?.ToString("R", CultureInfo.InvariantCulture) ?? "null";
        string nativeCall = command.Edge switch
        {
            LimitingEdge.None =>
                $"doc.resizeImage(undefined, undefined, {command.ResolutionPpi}, " +
                $"ResampleMethod.{command.ResampleMethod});",
            LimitingEdge.Width =>
                $"doc.resizeImage(UnitValue({edgeValue}, 'mm'), undefined, {command.ResolutionPpi}, " +
                $"ResampleMethod.{command.ResampleMethod});",
            LimitingEdge.Height =>
                $"doc.resizeImage(undefined, UnitValue({edgeValue}, 'mm'), {command.ResolutionPpi}, " +
                $"ResampleMethod.{command.ResampleMethod});",
            _ => throw new InvalidOperationException(
                $"Unresolved Photoshop edge value {(int)command.Edge} reached the native boundary."),
        };

        return $$"""
            (function () {
                var mutationInvoked = false;
                var before = null;
                var after = null;
                function facts(doc) {
                    var channels = [];
                    var w1 = false;
                    for (var i = 0; i < doc.channels.length; i++) {
                        var channel = doc.channels[i];
                        var name = String(channel.name);
                        var type = String(channel.kind);
                        channels.push({ name: name, type: type });
                        if (name === 'W1') { w1 = true; }
                    }
                    return {
                        path: String(doc.fullName.fsName),
                        pixelWidth: Math.round(doc.width.as('px')),
                        pixelHeight: Math.round(doc.height.as('px')),
                        resolution: Number(doc.resolution),
                        widthMm: Number(doc.width.as('mm')),
                        heightMm: Number(doc.height.as('mm')),
                        mode: String(doc.mode),
                        bits: String(doc.bitsPerChannel),
                        channels: channels,
                        w1Exists: w1,
                        documentCount: app.documents.length
                    };
                }
                function refusal(message) {
                    return result(false, message);
                }
                function encoded(value) {
                    return encodeURIComponent(String(value));
                }
                function packedFacts(value) {
                    if (value === null) { return ''; }
                    var packedChannels = [];
                    for (var p = 0; p < value.channels.length; p++) {
                        packedChannels.push(encoded(value.channels[p].name) + ',' + encoded(value.channels[p].type));
                    }
                    return [
                        encoded(value.path),
                        value.pixelWidth,
                        value.pixelHeight,
                        value.resolution,
                        value.widthMm,
                        value.heightMm,
                        encoded(value.mode),
                        encoded(value.bits),
                        value.w1Exists ? '1' : '0',
                        value.documentCount,
                        packedChannels.join(';')
                    ].join('|');
                }
                function result(ok, message) {
                    return [
                        'PF-B1A3-1',
                        ok ? '1' : '0',
                        mutationInvoked ? '1' : '0',
                        message ? encoded(message) : '',
                        packedFacts(before),
                        packedFacts(after)
                    ].join('\n');
                }
                try {
                    if (app.documents.length < 1) { return refusal('No active document.'); }
                    var doc = app.activeDocument;
                    if (String(doc.fullName.fsName).toLowerCase() !== {{expectedPath}}.toLowerCase()) {
                        return refusal('The active document path is not the exact managed Working path.');
                    }
                    before = facts(doc);
                    if (before.pixelWidth !== {{command.ExpectedSourcePixelWidth}} ||
                        before.pixelHeight !== {{command.ExpectedSourcePixelHeight}}) {
                        return refusal('The active document pixels do not match the immutable source.');
                    }
                    if (before.w1Exists) { return refusal('W1 already exists.'); }
                    if (before.mode !== 'DocumentMode.RGB') {
                        return refusal('Only the accepted RGB preparation input is supported.');
                    }
                    if (before.bits !== 'BitsPerChannelType.EIGHT') {
                        return refusal('Only the accepted 8-bit preparation input is supported.');
                    }
                    var componentCount = 0;
                    for (var c = 0; c < before.channels.length; c++) {
                        if (before.channels[c].type === 'ChannelType.COMPONENT') { componentCount++; }
                        if (before.channels[c].type === 'ChannelType.SPOTCOLOR') {
                            return refusal('A pre-existing spot channel is unsupported.');
                        }
                    }
                    if (componentCount !== 3) {
                        return refusal('The RGB document does not have exactly three component channels.');
                    }
                    mutationInvoked = true;
                    {{nativeCall}}
                    after = facts(doc);
                    return result(true, null);
                } catch (error) {
                    try {
                        if (app.documents.length > 0 &&
                            String(app.activeDocument.fullName.fsName).toLowerCase() === {{expectedPath}}.toLowerCase()) {
                            after = facts(app.activeDocument);
                        }
                    } catch (observationError) { }
                    return refusal(String(error));
                }
            }())
            """;
    }
}
