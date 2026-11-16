using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using PrintFlow.Domain.Files;
using PrintFlow.Infrastructure.Adapters.Photoshop;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Infrastructure.Configuration;
using PrintFlow.Tests.Fixtures;

namespace PrintFlow.Tests.Smoke;

/// <summary>Opt-in tool-state regression using an unchanged synthetic image in the accepted instance.</summary>
public sealed class PhotoshopMoveToolWorkstationSmoke
{
    [Fact]
    public async Task Select_and_read_back_Move_from_Crop_Paint_Bucket_and_Move_on_a_loaded_image()
    {
        if (Environment.GetEnvironmentVariable("PRINTFLOW_PHOTOSHOP_MOVE_TOOL_SMOKE") != "1") return;
        await using WorkstationAutomationLeaseScope lease = await WorkstationAutomationLeaseScope.AcquireDefaultAsync();
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "PrintFlowStudio.sln")))
            repository = repository.Parent;
        PrintFlowConfiguration configuration = PrintFlowConfiguration.LoadFromFile(
            Path.Combine(repository!.FullName, "appsettings.json"));
        var verified = new PresetPhotoshopBaselineProvider(
            Path.Combine(configuration.Workspace.Root, configuration.Preset.Path),
            Sha256.Parse(configuration.Preset.ExpectedSha256)).GetVerifiedBaseline();
        verified.IsSuccess.ShouldBeTrue(verified.IsFailure ? verified.Failure.ToString() : "");
        PhotoshopBaseline baseline = verified.Value;

        string root = Path.Combine(Path.GetTempPath(), "PrintFlowMoveToolSmoke", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "Working", "move-tool.png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, SyntheticImages.Png(32, 24, dpi: 240, alpha: true));
        byte[] hash = SHA256.HashData(File.ReadAllBytes(path));
        string encodedPath = JsonSerializer.Serialize(path);
        object? application = null;
        string? originalTool = null;
        try
        {
            Marshal.ThrowExceptionForHR(NativeMethods.CLSIDFromProgID("Photoshop.Application.130", out Guid id));
            Marshal.ThrowExceptionForHR(NativeMethods.GetActiveObject(ref id, 0, out application));
            application.ShouldNotBeNull();
            Type type = application.GetType();
            string installed = Convert.ToString(type.InvokeMember("Path", BindingFlags.GetProperty,
                null, application, null), CultureInfo.InvariantCulture)!;
            Path.GetFullPath(installed.TrimEnd('\\', '/')).ShouldBe(
                Path.GetDirectoryName(baseline.ExecutablePath), StringCompareShould.IgnoreCase);

            string Run(string program) => Execute(application, program);
            Run("String(app.documents.length)").ShouldBe("0", "the controlled smoke requires no pre-existing documents");
            originalTool = Run("String(app.currentTool)");
            RotPhotoshopToolNativeBridge bridge = new();
            foreach (string tool in new[] { "cropTool", "bucketTool", "moveTool" })
            {
                Run($"app.open(new File({encodedPath})); app.currentTool = {JsonSerializer.Serialize(tool)}; app.refresh(); String(app.currentTool);")
                    .ShouldBe(tool);
                var selected = bridge.SelectMoveTool(path, baseline);
                selected.IsSuccess.ShouldBeTrue(selected.IsFailure ? selected.Failure.ToString() : "");
                Run("String(app.currentTool)").ShouldBe("moveTool");
                Run("[app.activeDocument.width.as('px'), app.activeDocument.height.as('px')].join('x')").ShouldBe("32x24");
                Run("String(app.activeDocument.saved)").ShouldBe("true");
                SHA256.HashData(File.ReadAllBytes(path)).ShouldBe(hash);
                Console.WriteLine($"loaded image: {tool} -> moveTool; document and backing bytes unchanged");
                Run($"if (String(app.activeDocument.fullName.fsName).toLowerCase() !== {encodedPath}.toLowerCase()) throw new Error('Unexpected document'); app.activeDocument.close(SaveOptions.DONOTSAVECHANGES); 'closed';")
                    .ShouldBe("closed");
            }
            Run("String(app.documents.length)").ShouldBe("0");
        }
        finally
        {
            if (application is not null && Marshal.IsComObject(application))
            {
                try
                {
                    // Close only this smoke's exact document, including on an assertion failure.
                    string restore = originalTool is null ? "" : $"app.currentTool = {JsonSerializer.Serialize(originalTool)};";
                    Execute(application,
                        $"if (app.documents.length > 0 && String(app.activeDocument.fullName.fsName).toLowerCase() === {encodedPath}.toLowerCase()) app.activeDocument.close(SaveOptions.DONOTSAVECHANGES); {restore} 'clean';");
                }
                finally { Marshal.FinalReleaseComObject(application); }
            }
        }
    }

    private static string Execute(object application, string program)
    {
        Stopwatch elapsed = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return Convert.ToString(application.GetType().InvokeMember("DoJavaScript",
                    BindingFlags.InvokeMethod, null, application, [program]), CultureInfo.InvariantCulture)!;
            }
            catch (Exception ex) when (Busy(ex) && elapsed.Elapsed < TimeSpan.FromSeconds(5))
            {
                // Photoshop rejected dispatch while its test open/close UI settles. Only rejected
                // fixture calls are repeated; the production bridge itself remains single-shot.
                Thread.Sleep(50);
            }
        }
    }

    private static bool Busy(Exception exception) =>
        (exception is TargetInvocationException { InnerException: { } inner } ? inner : exception)
        is COMException { HResult: unchecked((int)0x8001010A) };
}
