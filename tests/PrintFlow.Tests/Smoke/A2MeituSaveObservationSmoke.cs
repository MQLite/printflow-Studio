using System.IO;
using System.Text.Json;
using System.Windows.Automation;
using PrintFlow.Domain.Files;
using PrintFlow.Infrastructure.Adapters.Meitu;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Infrastructure.Configuration;
using PrintFlow.Tests.Fixtures;
using Xunit.Abstractions;

namespace PrintFlow.Tests.Smoke;

/// <summary>Opt-in, getter-only native/UIA evidence. Never activates or dismisses a window.</summary>
public sealed class A2MeituSaveObservationSmoke(ITestOutputHelper output)
{
    [Fact]
    public async Task Read_current_Save_relationship_without_input()
    {
        if (Environment.GetEnvironmentVariable("PRINTFLOW_A2_MEITU_OBSERVATION") != "1") return;

        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "PrintFlowStudio.sln")))
            root = root.Parent;
        root.ShouldNotBeNull();
        PrintFlowConfiguration configuration = PrintFlowConfiguration.LoadFromFile(
            Path.Combine(root.FullName, "appsettings.json"));
        var baseline = new PresetMeituBaselineProvider(
            Path.Combine(configuration.Workspace.Root, configuration.Preset.Path),
            Sha256.Parse(configuration.Preset.ExpectedSha256)).GetVerifiedBaseline();
        baseline.IsSuccess.ShouldBeTrue();

        await using WorkstationAutomationLeaseScope lease =
            await WorkstationAutomationLeaseScope.AcquireDefaultAsync();
        Win32ExternalAppWindowLocator locator = new();
        var processes = locator.FindProcessesByExecutable(baseline.Value.ExecutablePath);
        processes.IsSuccess.ShouldBeTrue();
        processes.Value.Count.ShouldBe(1);
        var process = processes.Value[0];
        output.WriteLine("ObservedAtUtc: " + DateTimeOffset.UtcNow.ToString("O"));
        output.WriteLine("Process: " + JsonSerializer.Serialize(process));
        output.WriteLine("ForegroundBefore: " + locator.ReadForeground().Value);
        var windows = locator.FindTopLevelWindows(process);
        windows.IsSuccess.ShouldBeTrue();
        foreach (var window in windows.Value)
        {
            output.WriteLine($"Native: handle={window.Handle}; owner=0x{NativeMethods.GetWindow(window.Handle.Value, NativeMethods.GW_OWNER):X}; " +
                $"style=0x{NativeMethods.GetWindowLongPtr(window.Handle.Value, NativeMethods.GWL_STYLE):X}; " +
                $"title={window.Title}; class={window.ClassName}; visible={window.IsVisible}; enabled={window.IsEnabled}");
            AutomationElement element = AutomationElement.FromHandle(window.Handle.Value);
            for (int depth = 0; depth < 5 && element is not null; depth++)
            {
                var current = element.Current;
                output.WriteLine($"UIA[{depth}]: hwnd=0x{current.NativeWindowHandle:X}; pid={current.ProcessId}; " +
                    $"id={current.AutomationId}; name={current.Name}; class={current.ClassName}; type={current.ControlType.ProgrammaticName}");
                element = TreeWalker.RawViewWalker.GetParent(element);
            }
            UiaElementProvider elements = new();
            var fields = elements.FindAll(window.Handle, new UiElementQuery(UiControlKind.Edit));
            if (fields.IsFailure) continue;
            foreach (var field in fields.Value)
            {
                var identity = elements.Describe(field);
                if (identity.IsFailure || !identity.Value.AutomationId.Contains("SaveMaskWidget", StringComparison.Ordinal)) continue;
                var value = elements.GetValue(field);
                output.WriteLine("SaveField: " + identity.Value + "; value=" +
                    (value.IsSuccess ? value.Value : value.Failure.TechnicalDetail));
            }
        }
        locator.IsAlive(process).ShouldBeTrue();
        output.WriteLine("ForegroundAfter: " + locator.ReadForeground().Value);
        output.WriteLine("ReadOnly: no activation, input, close, cleanup or readiness result");
    }
}
