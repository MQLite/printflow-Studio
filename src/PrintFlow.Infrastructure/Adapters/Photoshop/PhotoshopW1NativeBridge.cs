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

internal sealed record PhotoshopNativeW1Command(
    string ExpectedDocumentFullPath,
    int ExpectedPixelWidth,
    int ExpectedPixelHeight,
    double ExpectedResolutionPpi,
    WhiteUnderbaseBranch Branch,
    PhotoshopW1ActionContract Contract);

internal sealed record PhotoshopNativeW1Outcome(
    bool Succeeded,
    int ActionInvocationCount,
    string? FailureDetail,
    PhotoshopDocumentFacts? Before,
    PhotoshopDocumentFacts? After,
    PhotoshopW1ChannelFacts? W1);

internal interface IPhotoshopW1NativeBridge
{
    OperationResult<PhotoshopNativeW1Outcome> ExecuteOnce(
        PhotoshopNativeW1Command command,
        PhotoshopBaseline baseline);
}

/// <summary>
/// Attaches to the already-running accepted Photoshop object and runs one fixed W1 program.
/// No caller-supplied action, set or script crosses this boundary.
/// </summary>
internal sealed class RotPhotoshopW1NativeBridge : IPhotoshopW1NativeBridge
{
    private const string PhotoshopCc2019ProgId = "Photoshop.Application.130";

    public OperationResult<PhotoshopNativeW1Outcome> ExecuteOnce(
        PhotoshopNativeW1Command command,
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
                "Path", BindingFlags.GetProperty, null, application, null),
                CultureInfo.InvariantCulture) ?? string.Empty;
            string expectedDirectory = Path.GetDirectoryName(Path.GetFullPath(baseline.ExecutablePath))!;
            string actualDirectory = Path.GetFullPath(reportedPath.TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!string.Equals(expectedDirectory, actualDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return NotAttached(
                    $"The running automation object reports '{actualDirectory}', not the accepted " +
                    $"Photoshop directory '{expectedDirectory}'. No Action was invoked.");
            }

            object? raw = automationType.InvokeMember(
                "DoJavaScript",
                BindingFlags.InvokeMethod,
                null,
                application,
                [PhotoshopW1Program.Create(command)]);
            if (raw is not string response || string.IsNullOrWhiteSpace(response))
            {
                return Unknown("Photoshop returned no factual CMYK + W1 observation.");
            }

            return Decode(response);
        }
        catch (Exception ex) when (ex is COMException or TargetInvocationException or
                                   InvalidOperationException or ArgumentException or JsonException)
        {
            string detail = ex is TargetInvocationException { InnerException: { } inner }
                ? inner.Message
                : ex.Message;
            return OperationResult.Fail<PhotoshopNativeW1Outcome>(OperationFailure.Create(
                FailureCode.PhotoshopTargetLost,
                $"The exact running Photoshop automation object could not complete the fixed CMYK + " +
                $"W1 call: {detail}. No retry was attempted.",
                isRetryable: false,
                context: UnknownRetainedContext()));
        }
        finally
        {
            if (application is not null && Marshal.IsComObject(application))
            {
                _ = Marshal.FinalReleaseComObject(application);
            }
        }
    }

    private static OperationResult<PhotoshopNativeW1Outcome> Decode(string response)
    {
        try
        {
            string[] lines = response.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            if (lines.Length != 7 || !string.Equals(lines[0], "PF-B1B-1", StringComparison.Ordinal))
            {
                return Unknown("Photoshop returned an unexpected CMYK + W1 protocol.");
            }

            bool succeeded = lines[1] switch
            {
                "1" => true,
                "0" => false,
                _ => throw new FormatException("Invalid success flag."),
            };
            int invocations = int.Parse(lines[2], NumberStyles.Integer, CultureInfo.InvariantCulture);
            string? failure = lines[3].Length == 0 ? null : Uri.UnescapeDataString(lines[3]);
            PhotoshopDocumentFacts? before = DecodeFacts(lines[4]);
            PhotoshopDocumentFacts? after = DecodeFacts(lines[5]);
            PhotoshopW1ChannelFacts? w1 = DecodeW1(lines[6]);
            return OperationResult.Ok(new PhotoshopNativeW1Outcome(
                succeeded, invocations, failure, before, after, w1));
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or UriFormatException)
        {
            return Unknown($"Photoshop's CMYK + W1 observation could not be decoded: {ex.Message}");
        }
    }

    private static PhotoshopDocumentFacts? DecodeFacts(string packed)
    {
        if (packed.Length == 0)
        {
            return null;
        }

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
            fields[8] == "1",
            int.Parse(fields[9], NumberStyles.Integer, CultureInfo.InvariantCulture));
    }

    private static PhotoshopW1ChannelFacts? DecodeW1(string packed)
    {
        if (packed.Length == 0)
        {
            return null;
        }

        string[] fields = packed.Split('|');
        if (fields.Length != 6)
        {
            throw new FormatException($"Expected 6 W1 fields; observed {fields.Length}.");
        }

        ImmutableArray<double> colour = fields[5].Length == 0
            ? []
            : [.. fields[5].Split(',').Select(value =>
                double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture))];
        return new PhotoshopW1ChannelFacts(
            Uri.UnescapeDataString(fields[0]),
            Uri.UnescapeDataString(fields[1]),
            fields[2] == "1",
            long.Parse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture),
            fields[4].Length == 0
                ? null
                : double.Parse(fields[4], NumberStyles.Float, CultureInfo.InvariantCulture),
            colour);
    }

    private static OperationResult<PhotoshopNativeW1Outcome> NotAttached(string detail) =>
        OperationResult.Fail<PhotoshopNativeW1Outcome>(OperationFailure.Create(
            FailureCode.PhotoshopTargetLost,
            detail,
            isRetryable: true,
            context: new Dictionary<string, string>
            {
                ["actionInvocationCount"] = "0",
                ["automaticRetry"] = "false",
            }));

    private static OperationResult<PhotoshopNativeW1Outcome> Unknown(string detail) =>
        OperationResult.Fail<PhotoshopNativeW1Outcome>(OperationFailure.Create(
            FailureCode.PhotoshopUnknownState,
            detail + " Success is unknown and no retry was attempted.",
            isRetryable: false,
            context: UnknownRetainedContext()));

    private static Dictionary<string, string> UnknownRetainedContext() => new()
    {
        ["inMemoryCmykW1MayBeRetained"] = "true",
        ["actionInvocationCountKnown"] = "false",
        ["automaticRetry"] = "false",
        ["saved"] = "false",
    };
}

/// <summary>Builds the fixed operation-specific program; there is no caller script surface.</summary>
internal static class PhotoshopW1Program
{
    internal static OperationResult<string> ActionName(
        WhiteUnderbaseBranch branch,
        PhotoshopW1ActionContract contract)
    {
        PhotoshopW1BranchContract[] matches = [.. contract.Branches.Where(b => b.Branch == branch)];
        return matches.Length == 1
            ? OperationResult.Ok(matches[0].ActionName)
            : OperationResult.Fail<string>(FailureCode.EnvironmentNotVerified,
                $"The verified W1 contract does not map branch '{branch}' exactly once.");
    }

    internal static string Create(PhotoshopNativeW1Command command)
    {
        OperationResult<string> selected = ActionName(command.Branch, command.Contract);
        if (selected.IsFailure)
        {
            throw new InvalidOperationException(selected.Failure.TechnicalDetail);
        }

        string expectedPath = JsonSerializer.Serialize(Path.GetFullPath(command.ExpectedDocumentFullPath));
        string setName = JsonSerializer.Serialize(command.Contract.SetName);
        string actionName = JsonSerializer.Serialize(selected.Value);
        string expectedActions = "[" + string.Join(",", command.Contract.Branches
            .OrderBy(b => b.ActionName, StringComparer.Ordinal)
            .Select(branch => "{" +
                $"name:{JsonSerializer.Serialize(branch.ActionName)}," +
                "commands:[" + string.Join(",", branch.RuntimeCommands.Select(
                    value => JsonSerializer.Serialize(value))) + "]}")) + "]";

        return $$"""
            (function () {
                var actionInvocations = 0;
                var before = null;
                var after = null;
                var w1 = null;
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
                        mode: String(doc.mode),
                        bits: String(doc.bitsPerChannel),
                        channels: channels,
                        w1Exists: w1Exists,
                        documentCount: app.documents.length
                    };
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
                    return ['PF-B1B-1', ok ? '1' : '0', actionInvocations,
                        message ? encoded(message) : '', packedFacts(before), packedFacts(after),
                        packedW1(w1)].join('\n');
                }
                function refusal(message) { return result(false, message); }
                function runtimeActions() {
                    var sets = [];
                    for (var si = 1; ; si++) {
                        var setRef = new ActionReference();
                        setRef.putIndex(charIDToTypeID('ASet'), si);
                        var setDesc;
                        try { setDesc = executeActionGet(setRef); } catch (endSets) { break; }
                        var set = { name: setDesc.getString(charIDToTypeID('Nm  ')), actions: [] };
                        var actionCount = setDesc.getInteger(charIDToTypeID('NmbC'));
                        for (var ai = 1; ai <= actionCount; ai++) {
                            var actionRef = new ActionReference();
                            actionRef.putIndex(charIDToTypeID('Actn'), ai);
                            actionRef.putIndex(charIDToTypeID('ASet'), si);
                            var actionDesc = executeActionGet(actionRef);
                            var action = { name: actionDesc.getString(charIDToTypeID('Nm  ')), commands: [] };
                            var commandCount = actionDesc.getInteger(charIDToTypeID('NmbC'));
                            for (var ci = 1; ci <= commandCount; ci++) {
                                var commandRef = new ActionReference();
                                commandRef.putIndex(charIDToTypeID('Cmnd'), ci);
                                commandRef.putIndex(charIDToTypeID('Actn'), ai);
                                commandRef.putIndex(charIDToTypeID('ASet'), si);
                                var commandDesc = executeActionGet(commandRef);
                                action.commands.push(commandDesc.getString(charIDToTypeID('Nm  ')));
                            }
                            set.actions.push(action);
                        }
                        sets.push(set);
                    }
                    return sets;
                }
                function sameTranscript(actual, expected) {
                    if (actual.length !== expected.length) { return false; }
                    for (var i = 0; i < actual.length; i++) {
                        if (actual[i] !== expected[i]) { return false; }
                    }
                    return true;
                }
                function verifyRuntime() {
                    var sets = runtimeActions();
                    var matches = [];
                    for (var s = 0; s < sets.length; s++) {
                        if (sets[s].name === {{setName}}) { matches.push(sets[s]); }
                    }
                    if (matches.length !== 1) { return 'The exact accepted Action set is missing or duplicated.'; }
                    var actual = matches[0].actions;
                    var expected = {{expectedActions}};
                    if (actual.length !== expected.length) { return 'The accepted Action set action count changed.'; }
                    for (var e = 0; e < expected.length; e++) {
                        var actionMatches = [];
                        for (var a = 0; a < actual.length; a++) {
                            if (actual[a].name === expected[e].name) { actionMatches.push(actual[a]); }
                        }
                        if (actionMatches.length !== 1) { return 'An exact accepted Action is missing or duplicated.'; }
                        if (!sameTranscript(actionMatches[0].commands, expected[e].commands)) {
                            return 'An accepted Action command transcript changed.';
                        }
                    }
                    return null;
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
                    try {
                        colour = [Number(channel.color.rgb.red), Number(channel.color.rgb.green),
                            Number(channel.color.rgb.blue)];
                    } catch (noColour) { }
                    return { name: String(channel.name), type: String(channel.kind),
                        nonEmpty: nonWhite > 0, nonWhitePixels: nonWhite,
                        solidity: solidity, colour: colour };
                }
                try {
                    if (app.documents.length < 1) { return refusal('No active document.'); }
                    var doc = app.activeDocument;
                    if (String(doc.fullName.fsName).toLowerCase() !== {{expectedPath}}.toLowerCase()) {
                        return refusal('The active document is not the exact managed Working path.');
                    }
                    before = facts(doc);
                    if (before.pixelWidth !== {{command.ExpectedPixelWidth}} ||
                        before.pixelHeight !== {{command.ExpectedPixelHeight}} ||
                        Math.abs(before.resolution - {{command.ExpectedResolutionPpi.ToString("R", CultureInfo.InvariantCulture)}}) > 0.0000001) {
                        return refusal('Prepared geometry or 300 PPI changed before Action invocation.');
                    }
                    if (before.mode !== 'DocumentMode.RGB' || before.bits !== 'BitsPerChannelType.EIGHT') {
                        return refusal('The prepared document is not RGB/8.');
                    }
                    var components = 0;
                    for (var c = 0; c < before.channels.length; c++) {
                        if (before.channels[c].type === 'ChannelType.COMPONENT') { components++; }
                        else { return refusal('A pre-existing non-component channel proves an unsupported prior state.'); }
                    }
                    if (components !== 3 || before.w1Exists) {
                        return refusal('The prepared RGB component/W1 state is not accepted.');
                    }
                    var runtimeFailure = verifyRuntime();
                    if (runtimeFailure !== null) { return refusal(runtimeFailure); }
                    actionInvocations++;
                    app.doAction({{actionName}}, {{setName}});
                    after = facts(doc);
                    w1 = readW1(doc);
                    return result(true, null);
                } catch (error) {
                    try {
                        if (app.documents.length > 0 &&
                            String(app.activeDocument.fullName.fsName).toLowerCase() === {{expectedPath}}.toLowerCase()) {
                            after = facts(app.activeDocument);
                            w1 = readW1(app.activeDocument);
                        }
                    } catch (observationError) { }
                    return refusal(String(error));
                }
            }())
            """;
    }
}
