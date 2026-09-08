using System.Collections.Immutable;
using System.IO;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Automation;

namespace PrintFlow.Infrastructure.Adapters.Photoshop;

internal sealed record PhotoshopRuntimeDocument(
    string Name, string? FullPath, bool IsSaved, bool IsActive);

internal sealed record PhotoshopRuntimeFacts(
    PhotoshopColourSettingsContract ColourSettings,
    ImmutableArray<PhotoshopRuntimeDocument> Documents)
{
    internal int UnsavedDocumentCount => Documents.Count(document => !document.IsSaved);

    internal string DocumentStateDescription => Documents.Length switch
    {
        0 => "No document is open.",
        _ when UnsavedDocumentCount == 0 => $"{Documents.Length} saved document(s) are open.",
        _ => $"{UnsavedDocumentCount} of {Documents.Length} open document(s) have unsaved changes.",
    };
}

internal interface IPhotoshopRuntimeFactReader
{
    OperationResult<PhotoshopRuntimeFacts> Read(string acceptedExecutablePath);
}

/// <summary>
/// Reads the already-running accepted Photoshop through its ROT automation object. The fixed
/// program contains getters only: it changes no preference, document, dialog, or file.
/// </summary>
internal sealed class RotPhotoshopRuntimeFactReader : IPhotoshopRuntimeFactReader
{
    private const string PhotoshopCc2019ProgId = "Photoshop.Application.130";

    public OperationResult<PhotoshopRuntimeFacts> Read(string acceptedExecutablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(acceptedExecutablePath);

        object? application = null;
        try
        {
            Marshal.ThrowExceptionForHR(NativeMethods.CLSIDFromProgID(PhotoshopCc2019ProgId, out Guid classId));
            Marshal.ThrowExceptionForHR(NativeMethods.GetActiveObject(ref classId, 0, out application));
            if (application is null)
            {
                return Unavailable("The running Photoshop CC 2019 automation object was not available.");
            }

            Type type = application.GetType();
            string reportedPath = Convert.ToString(type.InvokeMember(
                "Path", BindingFlags.GetProperty, null, application, null),
                CultureInfo.InvariantCulture) ?? string.Empty;
            string acceptedDirectory = Path.GetDirectoryName(Path.GetFullPath(acceptedExecutablePath))!;
            string observedDirectory = Path.GetFullPath(reportedPath.TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!string.Equals(acceptedDirectory, observedDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return Unavailable(
                    $"The running Photoshop automation object reports '{observedDirectory}', not the accepted " +
                    $"directory '{acceptedDirectory}'.");
            }

            object? raw = type.InvokeMember(
                "DoJavaScript", BindingFlags.InvokeMethod, null, application, [ReadOnlyProgram]);
            return raw is string response && !string.IsNullOrWhiteSpace(response)
                ? Decode(response)
                : Unavailable("Photoshop returned no live colour-settings observation.");
        }
        catch (Exception ex) when (ex is COMException or TargetInvocationException or
                                   InvalidOperationException or ArgumentException or FormatException)
        {
            string detail = ex is TargetInvocationException { InnerException: { } inner }
                ? inner.Message
                : ex.Message;
            return Unavailable($"Photoshop live settings could not be read: {detail}");
        }
        finally
        {
            if (application is not null && Marshal.IsComObject(application))
            {
                _ = Marshal.FinalReleaseComObject(application);
            }
        }
    }

    private static OperationResult<PhotoshopRuntimeFacts> Decode(string response)
    {
        try
        {
            string[] lines = response.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            if (lines.Length < 7 || lines[0] != "PF-ENV-COLOR-1" || lines[1] != "1")
            {
                string detail = lines.Length > 2 ? Uri.UnescapeDataString(lines[2]) : "unknown response";
                return Unavailable($"Photoshop could not report its active colour settings: {detail}");
            }

            PhotoshopColourSettingsContract settings = new(
                Uri.UnescapeDataString(lines[2]),
                Uri.UnescapeDataString(lines[3]),
                Uri.UnescapeDataString(lines[4]),
                Uri.UnescapeDataString(lines[5]));
            int count = int.Parse(lines[6], CultureInfo.InvariantCulture);
            if (count < 0 || lines.Length != 7 + count)
            {
                return Unavailable("Photoshop returned an incomplete document-state observation.");
            }

            ImmutableArray<PhotoshopRuntimeDocument>.Builder documents =
                ImmutableArray.CreateBuilder<PhotoshopRuntimeDocument>(count);
            for (int index = 0; index < count; index++)
            {
                string[] fields = lines[7 + index].Split('|');
                if (fields.Length != 4 || fields[2] is not ("0" or "1") || fields[3] is not ("0" or "1"))
                {
                    return Unavailable("Photoshop returned an invalid document-state observation.");
                }

                documents.Add(new PhotoshopRuntimeDocument(
                    Uri.UnescapeDataString(fields[0]),
                    fields[1].Length == 0 ? null : Uri.UnescapeDataString(fields[1]),
                    fields[2] == "1",
                    fields[3] == "1"));
            }

            return OperationResult.Ok(new PhotoshopRuntimeFacts(settings, documents.ToImmutable()));
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or UriFormatException)
        {
            return Unavailable($"Photoshop returned an unreadable live observation: {ex.Message}");
        }
    }

    private static OperationResult<PhotoshopRuntimeFacts> Unavailable(string detail) =>
        OperationResult.Fail<PhotoshopRuntimeFacts>(OperationFailure.Create(
            FailureCode.PhotoshopUnknownState,
            detail,
            isRetryable: true,
            context: new Dictionary<string, string> { ["settingsChanged"] = "false" }));

    private const string ReadOnlyProgram = """
        (function () {
            function enc(value) { return encodeURIComponent(String(value)); }
            try {
                var reference = new ActionReference();
                reference.putProperty(charIDToTypeID('Prpr'), stringIDToTypeID('colorSettings'));
                reference.putEnumerated(charIDToTypeID('capp'), charIDToTypeID('Ordn'), charIDToTypeID('Trgt'));
                var applicationDescriptor = executeActionGet(reference);
                var settings = applicationDescriptor.getObjectValue(stringIDToTypeID('colorSettings'));
                function text(key) {
                    var id = stringIDToTypeID(key);
                    return settings.hasKey(id) ? settings.getString(id) : '';
                }
                var lines = ['PF-ENV-COLOR-1', '1', enc(text('workingRGB')), enc(text('workingCMYK')),
                    enc(text('workingGray')), enc(text('workingSpot')), String(app.documents.length)];
                for (var index = 0; index < app.documents.length; index++) {
                    var document = app.documents[index];
                    var fullPath = '';
                    try { fullPath = document.fullName.fsName; } catch (_) { }
                    var active = app.documents.length > 0 && document === app.activeDocument;
                    lines.push(enc(document.name) + '|' + enc(fullPath) + '|' +
                        (document.saved ? '1' : '0') + '|' + (active ? '1' : '0'));
                }
                return lines.join('\n');
            } catch (error) {
                return ['PF-ENV-COLOR-1', '0', enc(error.message || error)].join('\n');
            }
        }());
        """;
}

internal static class PhotoshopColourSettingsRule
{
    internal static bool Matches(
        PhotoshopColourSettingsContract expected,
        PhotoshopColourSettingsContract observed) =>
        string.Equals(expected.RgbWorkingSpace, observed.RgbWorkingSpace, StringComparison.Ordinal) &&
        string.Equals(expected.CmykWorkingSpace, observed.CmykWorkingSpace, StringComparison.Ordinal) &&
        string.Equals(expected.GrayWorkingSpace, observed.GrayWorkingSpace, StringComparison.Ordinal) &&
        string.Equals(expected.SpotWorkingSpace, observed.SpotWorkingSpace, StringComparison.Ordinal);
}
