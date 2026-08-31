using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using PrintFlow.Infrastructure.Automation;

namespace PrintFlow.Tests.Smoke;

/// <summary>Opt-in, read-only UTF-8 observation of one retained B1B synthetic document.</summary>
public sealed class PhotoshopW1RetainedObservationSmoke
{
    [Fact]
    public void Observe_exact_active_synthetic_channel_names_without_mutation()
    {
        if (Environment.GetEnvironmentVariable("PRINTFLOW_PHOTOSHOP_W1_OBSERVE") != "1") return;
        string output = Environment.GetEnvironmentVariable("PRINTFLOW_PHOTOSHOP_W1_OBSERVATION_PATH")
            ?? throw new InvalidOperationException("An explicit observation output path is required.");

        object? application = null;
        object? document = null;
        object? fullName = null;
        object? components = null;
        object? channels = null;
        try
        {
            int clsidResult = NativeMethods.CLSIDFromProgID(
                "Photoshop.Application.130", out Guid classId);
            Marshal.ThrowExceptionForHR(clsidResult);
            int activeResult = NativeMethods.GetActiveObject(ref classId, 0, out application);
            Marshal.ThrowExceptionForHR(activeResult);
            object app = application ?? throw new InvalidOperationException(
                "The accepted running Photoshop object was not available.");

            Type appType = app.GetType();
            string appPath = Convert.ToString(appType.InvokeMember(
                "Path", BindingFlags.GetProperty, null, app, null),
                CultureInfo.InvariantCulture) ?? string.Empty;
            Path.GetFullPath(appPath.TrimEnd(Path.DirectorySeparatorChar))
                .ShouldBe(@"D:\Adobe Photoshop CC 2019", StringCompareShould.IgnoreCase);

            document = appType.InvokeMember("ActiveDocument", BindingFlags.GetProperty,
                null, app, null);
            object activeDocument = document ?? throw new InvalidOperationException(
                "Photoshop has no active synthetic document.");
            Type documentType = activeDocument.GetType();
            fullName = documentType.InvokeMember("FullName", BindingFlags.GetProperty,
                null, activeDocument, null);
            object documentFullName = fullName ?? throw new InvalidOperationException(
                "The active synthetic document has no fullName.");
            string documentPath = Convert.ToString(documentFullName,
                CultureInfo.InvariantCulture) ?? string.Empty;
            documentPath.ShouldContain("PrintFlowPhotoshopW1Smoke", Case.Sensitive);
            Path.GetFileName(documentPath).ShouldStartWith("PF_B1B_W1_2px_", Case.Sensitive);

            components = documentType.InvokeMember("ComponentChannels", BindingFlags.GetProperty,
                null, activeDocument, null);
            channels = documentType.InvokeMember("Channels", BindingFlags.GetProperty,
                null, activeDocument, null);
            List<object> componentFacts = ReadChannels(components ?? throw new InvalidOperationException(
                "Photoshop returned no component-channel collection."));
            List<object> allFacts = ReadChannels(channels ?? throw new InvalidOperationException(
                "Photoshop returned no channel collection."));

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            File.WriteAllText(output, JsonSerializer.Serialize(new
            {
                documentPath,
                componentChannels = componentFacts,
                allChannels = allFacts,
                observationOnly = true,
                actionInvoked = false,
                saved = false,
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            foreach (object? value in new[] { channels, components, fullName, document, application })
            {
                if (value is not null && Marshal.IsComObject(value))
                {
                    _ = Marshal.FinalReleaseComObject(value);
                }
            }
        }
    }

    private static List<object> ReadChannels(object collection)
    {
        if (collection is Array array)
        {
            List<object> arrayResult = [];
            for (int offset = 0; offset < array.Length; offset++)
            {
                object channel = array.GetValue(offset) ?? throw new InvalidOperationException(
                    $"Photoshop returned no channel at offset {offset}.");
                try
                {
                    arrayResult.Add(ReadChannel(channel, offset + 1));
                }
                finally
                {
                    if (Marshal.IsComObject(channel)) _ = Marshal.FinalReleaseComObject(channel);
                }
            }
            return arrayResult;
        }

        Type type = collection.GetType();
        int count = Convert.ToInt32(type.InvokeMember(
            "Count", BindingFlags.GetProperty, null, collection, null),
            CultureInfo.InvariantCulture);
        List<object> result = [];
        for (int index = 1; index <= count; index++)
        {
            object channel = type.InvokeMember("Item", BindingFlags.GetProperty,
                null, collection, [index]) ?? throw new InvalidOperationException(
                    $"Photoshop returned no channel at index {index}.");
            try
            {
                result.Add(ReadChannel(channel, index));
            }
            finally
            {
                if (Marshal.IsComObject(channel)) _ = Marshal.FinalReleaseComObject(channel);
            }
        }
        return result;
    }

    private static object ReadChannel(object channel, int index)
    {
        Type channelType = channel.GetType();
        return new
        {
            index,
            name = Convert.ToString(channelType.InvokeMember(
                "Name", BindingFlags.GetProperty, null, channel, null),
                CultureInfo.InvariantCulture),
            type = Convert.ToString(channelType.InvokeMember(
                "Kind", BindingFlags.GetProperty, null, channel, null),
                CultureInfo.InvariantCulture),
        };
    }
}
