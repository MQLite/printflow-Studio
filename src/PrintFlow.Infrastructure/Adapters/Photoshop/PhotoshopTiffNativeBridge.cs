using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Automation;

namespace PrintFlow.Infrastructure.Adapters.Photoshop;

internal sealed record PhotoshopNativeTiffCommand(
    string ExpectedDocumentFullPath,
    string ExpectedOutputFullPath,
    int ExpectedPixelWidth,
    int ExpectedPixelHeight,
    double ExpectedResolutionPpi);

internal sealed record PhotoshopNativeTiffOutcome(
    bool Succeeded,
    int SaveInvocationCount,
    string? FailureDetail,
    PhotoshopDocumentFacts? Before,
    PhotoshopDocumentFacts? After,
    PhotoshopW1ChannelFacts? W1Before,
    bool OutputExists,
    bool AcceptedSettingsObserved);

internal interface IPhotoshopTiffNativeBridge
{
    OperationResult<PhotoshopNativeTiffOutcome> SaveOnce(
        PhotoshopNativeTiffCommand command,
        PhotoshopBaseline baseline);
}

/// <summary>
/// Attaches to the accepted CC 2019 COM object and executes one fixed save-as-copy program.
/// No format, script, path option, compression value, or retry policy crosses this boundary.
/// </summary>
internal sealed class RotPhotoshopTiffNativeBridge : IPhotoshopTiffNativeBridge
{
    private const string PhotoshopCc2019ProgId = "Photoshop.Application.130";

    public OperationResult<PhotoshopNativeTiffOutcome> SaveOnce(
        PhotoshopNativeTiffCommand command,
        PhotoshopBaseline baseline)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(baseline);

        object? application = null;
        try
        {
            int classResult = NativeMethods.CLSIDFromProgID(PhotoshopCc2019ProgId, out Guid classId);
            if (classResult < 0) Marshal.ThrowExceptionForHR(classResult);
            int activeResult = NativeMethods.GetActiveObject(ref classId, 0, out application);
            if (activeResult < 0 || application is null)
            {
                if (activeResult < 0) Marshal.ThrowExceptionForHR(activeResult);
                return NotAttached("The running Photoshop CC 2019 automation object was not available.");
            }

            Type automationType = application.GetType();
            string reportedPath = Convert.ToString(automationType.InvokeMember(
                "Path", BindingFlags.GetProperty, null, application, null),
                CultureInfo.InvariantCulture) ?? string.Empty;
            string expectedDirectory = Path.GetDirectoryName(Path.GetFullPath(baseline.ExecutablePath))!;
            string actualDirectory = Path.GetFullPath(reportedPath.TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!string.Equals(expectedDirectory, actualDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return NotAttached(
                    $"The running automation object reports '{actualDirectory}', not the accepted " +
                    $"Photoshop directory '{expectedDirectory}'. No save was invoked.");
            }

            object? raw = automationType.InvokeMember(
                "DoJavaScript", BindingFlags.InvokeMethod, null, application,
                [PhotoshopProductionTiffProgram.Create(command)]);
            return raw is string response && !string.IsNullOrWhiteSpace(response)
                ? Decode(response)
                : Unknown("Photoshop returned no factual TIFF-save observation.");
        }
        catch (Exception ex) when (ex is COMException or TargetInvocationException or
                                   InvalidOperationException or ArgumentException or JsonException)
        {
            string detail = ex is TargetInvocationException { InnerException: { } inner }
                ? inner.Message
                : ex.Message;
            return OperationResult.Fail<PhotoshopNativeTiffOutcome>(OperationFailure.Create(
                FailureCode.PhotoshopTargetLost,
                $"The exact Photoshop automation object could not complete the fixed production-TIFF " +
                $"call: {detail}. Save state is ambiguous and no retry was attempted.",
                isRetryable: false,
                context: UnknownContext()));
        }
        finally
        {
            if (application is not null && Marshal.IsComObject(application))
            {
                _ = Marshal.FinalReleaseComObject(application);
            }
        }
    }

    private static OperationResult<PhotoshopNativeTiffOutcome> Decode(string response)
    {
        try
        {
            string[] lines = response.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            if (lines.Length != 9 || !string.Equals(lines[0], "PF-C1-TIFF-1", StringComparison.Ordinal))
            {
                return Unknown("Photoshop returned an unexpected production-TIFF protocol.");
            }
            bool succeeded = ParseBoolean(lines[1]);
            int saves = int.Parse(lines[2], NumberStyles.Integer, CultureInfo.InvariantCulture);
            string? failure = lines[3].Length == 0 ? null : Uri.UnescapeDataString(lines[3]);
            PhotoshopDocumentFacts? before = DecodeFacts(lines[4]);
            PhotoshopDocumentFacts? after = DecodeFacts(lines[5]);
            PhotoshopW1ChannelFacts? w1 = DecodeW1(lines[6]);
            bool outputExists = ParseBoolean(lines[7]);
            bool settings = ParseBoolean(lines[8]);
            return OperationResult.Ok(new PhotoshopNativeTiffOutcome(
                succeeded, saves, failure, before, after, w1, outputExists, settings));
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or UriFormatException)
        {
            return Unknown($"Photoshop's TIFF-save observation could not be decoded: {ex.Message}");
        }
    }

    private static PhotoshopDocumentFacts? DecodeFacts(string packed)
    {
        if (packed.Length == 0) return null;
        string[] fields = packed.Split('|');
        if (fields.Length != 11)
        {
            throw new FormatException($"Expected 11 document fields; observed {fields.Length}.");
        }
        ImmutableArray<PhotoshopChannelFact> channels = fields[10].Length == 0
            ? []
            : [.. fields[10].Split(';').Select(entry =>
            {
                string[] pair = entry.Split(',');
                if (pair.Length != 2) throw new FormatException("A channel fact is malformed.");
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
            ParseBoolean(fields[8]),
            int.Parse(fields[9], NumberStyles.Integer, CultureInfo.InvariantCulture));
    }

    private static PhotoshopW1ChannelFacts? DecodeW1(string packed)
    {
        if (packed.Length == 0) return null;
        string[] fields = packed.Split('|');
        if (fields.Length != 6) throw new FormatException("A W1 observation is malformed.");
        ImmutableArray<double> colour = fields[5].Length == 0
            ? []
            : [.. fields[5].Split(',').Select(value =>
                double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture))];
        return new PhotoshopW1ChannelFacts(
            Uri.UnescapeDataString(fields[0]),
            Uri.UnescapeDataString(fields[1]),
            ParseBoolean(fields[2]),
            long.Parse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture),
            fields[4].Length == 0
                ? null
                : double.Parse(fields[4], NumberStyles.Float, CultureInfo.InvariantCulture),
            colour);
    }

    private static bool ParseBoolean(string value) => value switch
    {
        "1" => true,
        "0" => false,
        _ => throw new FormatException("A Boolean protocol field is invalid."),
    };

    private static OperationResult<PhotoshopNativeTiffOutcome> NotAttached(string detail) =>
        OperationResult.Fail<PhotoshopNativeTiffOutcome>(OperationFailure.Create(
            FailureCode.PhotoshopTargetLost, detail, isRetryable: true,
            context: new Dictionary<string, string>
            {
                ["saveInvocationCount"] = "0",
                ["automaticRetry"] = "false",
                ["adapterOutputCreated"] = "false",
            }));

    private static OperationResult<PhotoshopNativeTiffOutcome> Unknown(string detail) =>
        OperationResult.Fail<PhotoshopNativeTiffOutcome>(OperationFailure.Create(
            FailureCode.PhotoshopUnknownState,
            detail + " Save state is ambiguous and no retry was attempted.",
            isRetryable: false,
            context: UnknownContext()));

    private static Dictionary<string, string> UnknownContext() => new()
    {
        ["saveInvocationCountKnown"] = "false",
        ["automaticRetry"] = "false",
        ["workingArtefactMayBeRetained"] = "true",
        ["validatedCandidateCreated"] = "false",
        ["adapterOutputCreated"] = "false",
        ["revisionCreated"] = "false",
    };
}

/// <summary>Builds the fixed CC 2019 ExtendScript save program.</summary>
internal static class PhotoshopProductionTiffProgram
{
    internal static string Create(PhotoshopNativeTiffCommand command)
    {
        string expectedPath = JsonSerializer.Serialize(Path.GetFullPath(command.ExpectedDocumentFullPath));
        string outputPath = JsonSerializer.Serialize(Path.GetFullPath(command.ExpectedOutputFullPath));
        string expectedPpi = command.ExpectedResolutionPpi.ToString("R", CultureInfo.InvariantCulture);
        return $$"""
            (function () {
                var saveInvocations = 0;
                var before = null;
                var after = null;
                var w1Before = null;
                var outputExists = false;
                var settingsAccepted = false;
                function encoded(value) { return encodeURIComponent(String(value)); }
                function facts(doc) {
                    var channels = [];
                    var w1Exists = false;
                    for (var i = 0; i < doc.channels.length; i++) {
                        var channel = doc.channels[i];
                        var name = String(channel.name);
                        channels.push({ name: name, type: String(channel.kind) });
                        if (name === 'W1') { w1Exists = true; }
                    }
                    return {
                        path: String(doc.fullName.fsName),
                        pixelWidth: Math.round(doc.width.as('px')),
                        pixelHeight: Math.round(doc.height.as('px')),
                        resolution: Number(doc.resolution),
                        widthMm: Number(doc.width.as('mm')),
                        heightMm: Number(doc.height.as('mm')),
                        mode: String(doc.mode), bits: String(doc.bitsPerChannel),
                        channels: channels, w1Exists: w1Exists,
                        documentCount: app.documents.length
                    };
                }
                function readW1(doc) {
                    var matches = [];
                    for (var i = 0; i < doc.channels.length; i++) {
                        if (String(doc.channels[i].name) === 'W1') { matches.push(doc.channels[i]); }
                    }
                    if (matches.length !== 1) { return null; }
                    var channel = matches[0];
                    var histogram = channel.histogram;
                    var nonWhite = 0;
                    for (var h = 0; h < 255; h++) { nonWhite += Number(histogram[h]); }
                    var solidity = null;
                    try { solidity = Number(channel.opacity); } catch (noOpacity) { }
                    var colour = [];
                    try { colour = [Number(channel.color.rgb.red), Number(channel.color.rgb.green),
                        Number(channel.color.rgb.blue)]; } catch (noColour) { }
                    return { name: String(channel.name), type: String(channel.kind),
                        nonEmpty: nonWhite > 0, nonWhitePixels: nonWhite,
                        solidity: solidity, colour: colour };
                }
                function packedFacts(value) {
                    if (value === null) { return ''; }
                    var packedChannels = [];
                    for (var p = 0; p < value.channels.length; p++) {
                        packedChannels.push(encoded(value.channels[p].name) + ',' + encoded(value.channels[p].type));
                    }
                    return [encoded(value.path), value.pixelWidth, value.pixelHeight, value.resolution,
                        value.widthMm, value.heightMm, encoded(value.mode), encoded(value.bits),
                        value.w1Exists ? '1' : '0', value.documentCount, packedChannels.join(';')].join('|');
                }
                function packedW1(value) {
                    if (value === null) { return ''; }
                    return [encoded(value.name), encoded(value.type), value.nonEmpty ? '1' : '0',
                        value.nonWhitePixels, value.solidity === null ? '' : value.solidity,
                        value.colour.join(',')].join('|');
                }
                function result(ok, message) {
                    return ['PF-C1-TIFF-1', ok ? '1' : '0', saveInvocations,
                        message ? encoded(message) : '', packedFacts(before), packedFacts(after),
                        packedW1(w1Before), outputExists ? '1' : '0',
                        settingsAccepted ? '1' : '0'].join('\n');
                }
                function refusal(message) { return result(false, message); }
                try {
                    if (app.documents.length < 1) { return refusal('No active document.'); }
                    var doc = app.activeDocument;
                    if (String(doc.fullName.fsName).toLowerCase() !== {{expectedPath}}.toLowerCase()) {
                        return refusal('The active document is not the exact managed Working document.');
                    }
                    var output = new File({{outputPath}});
                    if (output.exists) { return refusal('The exact Working TIFF destination already exists.'); }
                    before = facts(doc);
                    w1Before = readW1(doc);
                    if (before.pixelWidth !== {{command.ExpectedPixelWidth}} ||
                        before.pixelHeight !== {{command.ExpectedPixelHeight}} ||
                        Math.abs(before.resolution - {{expectedPpi}}) > 0.0000001 ||
                        before.mode !== 'DocumentMode.CMYK' ||
                        before.bits !== 'BitsPerChannelType.EIGHT' ||
                        before.channels.length !== 5 || w1Before === null ||
                        w1Before.type !== 'ChannelType.SPOTCOLOR' || !w1Before.nonEmpty) {
                        return refusal('The immediate pre-save CMYK/8/geometry/W1 state is not accepted.');
                    }
                    var components = 0;
                    var exactW1 = 0;
                    for (var c = 0; c < before.channels.length; c++) {
                        if (before.channels[c].type === 'ChannelType.COMPONENT') { components++; }
                        if (before.channels[c].name === 'W1' &&
                            before.channels[c].type === 'ChannelType.SPOTCOLOR') { exactW1++; }
                    }
                    if (components !== 4 || exactW1 !== 1) {
                        return refusal('The immediate pre-save channel set is not four components plus W1 spot.');
                    }

                    var options = new TiffSaveOptions();
                    options.alphaChannels = false;
                    options.annotations = false;
                    options.byteOrder = ByteOrder.IBM;
                    options.embedColorProfile = true;
                    options.imageCompression = TIFFEncoding.NONE;
                    options.interleaveChannels = true;
                    options.layerCompression = LayerCompression.RLE;
                    options.layers = true;
                    options.saveImagePyramid = false;
                    options.spotColors = true;
                    options.transparency = false;
                    settingsAccepted =
                        options.alphaChannels === false && options.annotations === false &&
                        String(options.byteOrder) === 'ByteOrder.IBM' &&
                        options.embedColorProfile === true &&
                        String(options.imageCompression) === 'TIFFEncoding.NONE' &&
                        options.interleaveChannels === true &&
                        String(options.layerCompression) === 'LayerCompression.RLE' &&
                        options.layers === true && options.saveImagePyramid === false &&
                        options.spotColors === true && options.transparency === false;
                    if (!settingsAccepted) {
                        return refusal('Photoshop CC 2019 did not retain the exact TIFF option values.');
                    }
                    saveInvocations++;
                    doc.saveAs(output, options, true, Extension.LOWERCASE);
                    outputExists = output.exists;
                    after = facts(doc);
                    if (String(after.path).toLowerCase() !== String(before.path).toLowerCase()) {
                        return refusal('Save-as-copy changed the active document identity.');
                    }
                    return result(outputExists, outputExists ? null : 'The save returned without the TIFF appearing.');
                } catch (error) {
                    try {
                        outputExists = new File({{outputPath}}).exists;
                        if (app.documents.length > 0) { after = facts(app.activeDocument); }
                    } catch (observationError) { }
                    return refusal(String(error));
                }
            }())
            """;
    }
}
