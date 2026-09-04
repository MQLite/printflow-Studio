using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Automation;

namespace PrintFlow.Infrastructure.Adapters.Photoshop;

internal sealed record PsdNativeCommand(string InputPath, string OutputPath);
internal sealed record PsdNativeOutcome(bool Succeeded, PsdInspection? Inspection, string Detail);
internal interface IPhotoshopPsdNativeBridge
{
    OperationResult<PsdNativeOutcome> ExportOnce(PsdNativeCommand command, PhotoshopBaseline baseline);
}

/// <summary>Attaches to the accepted running CC 2019 instance; accepts no caller-provided script.</summary>
internal sealed class RotPhotoshopPsdNativeBridge : IPhotoshopPsdNativeBridge
{
    public OperationResult<PsdNativeOutcome> ExportOnce(PsdNativeCommand command, PhotoshopBaseline baseline)
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
                throw new InvalidOperationException("The running object does not belong to the accepted installation.");
            object? raw = type.InvokeMember("DoJavaScript", BindingFlags.InvokeMethod, null,
                application, [PhotoshopPsdProgram.Create(command)]);
            return Decode(raw as string ?? "");
        }
        catch (Exception ex) when (ex is COMException or TargetInvocationException or ArgumentException or InvalidOperationException)
        {
            return OperationResult.Fail<PsdNativeOutcome>(FailureCode.PhotoshopTargetLost,
                "PSD preparation lost its exact Photoshop automation target: " + ex.Message);
        }
        finally
        {
            if (application is not null && Marshal.IsComObject(application)) Marshal.FinalReleaseComObject(application);
        }
    }

    internal static OperationResult<PsdNativeOutcome> Decode(string response)
    {
        try
        {
            string[] fields = response.Split('\n');
            if (fields.Length != 11 || fields[0] != "PF-PSD-1" || fields[1] is not ("0" or "1"))
                throw new FormatException("Invalid PSD observation protocol.");
            PsdInspection? facts = null;
            if (fields[3] != "")
            {
                facts = new PsdInspection(int.Parse(fields[3], CultureInfo.InvariantCulture),
                    int.Parse(fields[4], CultureInfo.InvariantCulture), Uri.UnescapeDataString(fields[5]),
                    int.Parse(fields[6], CultureInfo.InvariantCulture), true,
                    fields[7] switch { "1" => true, "0" => false, "" => null, _ => throw new FormatException() },
                    [.. fields[8].Split(';', StringSplitOptions.RemoveEmptyEntries).Select(p =>
                    {
                        string[] channel = p.Split(',');
                        if (channel.Length != 2) throw new FormatException();
                        return new PsdChannel(Uri.UnescapeDataString(channel[0]), Uri.UnescapeDataString(channel[1]));
                    })], Uri.UnescapeDataString(fields[9]));
                if (facts.PixelWidth <= 0 || facts.PixelHeight <= 0 || facts.BitDepth <= 0)
                    throw new FormatException("Invalid PSD observations.");
            }
            if (fields[10] != "source-active") throw new FormatException("The source document was not restored.");
            return OperationResult.Ok(new PsdNativeOutcome(fields[1] == "1", facts, Uri.UnescapeDataString(fields[2])));
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            return OperationResult.Fail<PsdNativeOutcome>(FailureCode.PhotoshopUnknownState,
                "PSD inspection/export returned invalid evidence: " + ex.Message);
        }
    }
}

internal static class PhotoshopPsdProgram
{
    internal static string Create(PsdNativeCommand command)
    {
        string input = JsonSerializer.Serialize(Path.GetFullPath(command.InputPath));
        string output = JsonSerializer.Serialize(Path.GetFullPath(command.OutputPath));
        return $$"""
            (function () {
                var source = null, copy = null, width = '', height = '', mode = '', depth = '';
                var alpha = '', channels = [], version = '', sourceId = null;
                function enc(s) { return encodeURIComponent(String(s)); }
                function result(ok, detail) {
                    var active = '';
                    try {
                        if (source && app.activeDocument.id === sourceId &&
                            String(source.fullName.fsName).toLowerCase() === {{input}}.toLowerCase()) active = 'source-active';
                    } catch (ignored) { }
                    return ['PF-PSD-1', ok ? '1' : '0', enc(detail), width, height, enc(mode), depth,
                        alpha, channels.join(';'), enc(version), active].join('\n');
                }
                try {
                    if (app.documents.length < 1) return result(false, 'No active document.');
                    source = app.activeDocument; sourceId = source.id;
                    if (String(source.fullName.fsName).toLowerCase() !== {{input}}.toLowerCase())
                        return result(false, 'The active document is not the exact managed PSD.');
                    width = Math.round(source.width.as('px')); height = Math.round(source.height.as('px'));
                    mode = String(source.mode).replace('DocumentMode.', '');
                    var bits = String(source.bitsPerChannel);
                    depth = bits === 'BitsPerChannelType.EIGHT' ? 8 :
                        (bits === 'BitsPerChannelType.SIXTEEN' ? 16 :
                        (bits === 'BitsPerChannelType.THIRTYTWO' ? 32 :
                        (bits === 'BitsPerChannelType.ONE' ? 1 : '')));
                    if (depth === '') return result(false, 'Unknown PSD bit depth.');
                    version = String(app.version);
                    var spot = false, w1 = false, components = 0;
                    for (var i = 0; i < source.channels.length; i++) {
                        var ch = source.channels[i], kind = String(ch.kind).replace('ChannelType.', '');
                        channels.push(enc(ch.name) + ',' + enc(kind));
                        if (kind === 'SPOTCOLOR') spot = true;
                        if (String(ch.name).toUpperCase() === 'W1') w1 = true;
                        if (kind === 'COMPONENT') components++;
                    }
                    if (w1 || spot) return result(false, 'Existing W1/spot channels require an operator decision.');
                    if (mode !== 'RGB' || depth !== 8 || components !== 3)
                        return result(false, 'Only RGB 8-bit PSD input is supported.');
                    var destination = new File({{output}});
                    if (destination.exists) return result(false, 'Attempt output already exists.');
                    // Merge a disposable duplicate. flatten() is intentionally absent because it
                    // introduces a background; the full document canvas and transparency survive.
                    copy = source.duplicate('PrintFlow PSD raster', true);
                    alpha = '0';
                    if (!copy.activeLayer.isBackgroundLayer) {
                        var set = new ActionDescriptor();
                        var selection = new ActionReference();
                        selection.putProperty(charIDToTypeID('Chnl'), charIDToTypeID('fsel'));
                        set.putReference(charIDToTypeID('null'), selection);
                        var transparency = new ActionReference();
                        transparency.putEnumerated(charIDToTypeID('Chnl'), charIDToTypeID('Chnl'), charIDToTypeID('Trsp'));
                        set.putReference(charIDToTypeID('T   '), transparency);
                        executeAction(charIDToTypeID('setd'), set, DialogModes.NO);
                        var mask = copy.channels.add();
                        copy.selection.store(mask); var histogram = mask.histogram;
                        alpha = histogram[255] < width * height ? '1' : '0';
                        mask.remove(); copy.selection.deselect();
                    }
                    // Extra alpha masks are not spot ink and do not alter the visible composite.
                    // Their names/types remain in the inspection; PNG carries visual transparency.
                    for (var c = copy.channels.length - 1; c >= 0; c--)
                        if (copy.channels[c].kind !== ChannelType.COMPONENT) copy.channels[c].remove();
                    copy.activeChannels = copy.componentChannels;
                    var options = new PNGSaveOptions(); options.interlaced = false;
                    copy.saveAs(destination, options, true, Extension.LOWERCASE);
                    copy.close(SaveOptions.DONOTSAVECHANGES); copy = null;
                    app.activeDocument = source;
                    return result(true, 'RGB/8 full-canvas PNG saved as copy.');
                } catch (error) {
                    // No modal dismissal and no generic cleanup. The outer guard reports any
                    // retained duplicate or dialog instead of inventing a successful preparation.
                    return result(false, String(error));
                }
            }())
            """;
    }
}
