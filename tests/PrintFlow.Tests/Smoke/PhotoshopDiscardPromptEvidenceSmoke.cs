using System.IO;
using PrintFlow.Domain.Files;
using PrintFlow.Infrastructure.Adapters.Photoshop;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Infrastructure.Configuration;

namespace PrintFlow.Tests.Smoke;

/// <summary>
/// Read-only census of the Photoshop prompt raised from a synthetic PrintFlow-owned document.
/// </summary>
/// <remarks>
/// Opt in with <c>PRINTFLOW_PS_DISCARD_EVIDENCE=1</c> only after the controlled close request has
/// raised the prompt. This test presses nothing; it records process/window ownership and the
/// native ids, classes and text of the controls exposed by the candidate surface.
/// </remarks>
public sealed class PhotoshopDiscardPromptEvidenceSmoke
{
    [Fact]
    public void Read_the_owned_prompt_and_its_native_controls_without_acting()
    {
        if (Environment.GetEnvironmentVariable("PRINTFLOW_PS_DISCARD_EVIDENCE") != "1") return;

        PrintFlowConfiguration configuration = PrintFlowConfiguration.LoadFromFile(
            RepositoryFile("appsettings.json"));
        string manifestPath = Path.Combine(configuration.Workspace.Root, configuration.Preset.Path);
        PresetPhotoshopBaselineProvider provider = new(
            manifestPath, Sha256.Parse(configuration.Preset.ExpectedSha256));
        PhotoshopBaseline baseline = provider.GetVerifiedBaseline().Value;
        PhotoshopOwnedDocumentCleanupSignature cleanup = baseline.OwnedDocumentCleanup.ShouldNotBeNull(
            "the selected signed preset must contain the owned-document cleanup contract.");

        Win32ExternalAppWindowLocator locator = new();
        Win32VerifiedControlSink controls = new();
        ExternalProcessRef process = locator.FindProcessesByExecutable(baseline.ExecutablePath).Value.Single();
        ExternalWindowRef main = locator.FindTopLevelWindows(process).Value
            .Single(window => string.Equals(window.ClassName, baseline.MainWindowClassName, StringComparison.Ordinal));
        ExternalWindowRef[] owned = [.. locator.FindOwnedDialogs(process, main).Value];
        foreach (ExternalWindowRef window in owned)
        {
            Console.WriteLine(
                $"owned   : {window.Handle} class '{window.ClassName}' title '{window.Title}' enabled {window.IsEnabled}");
        }

        ExternalWindowRef dialog = owned.Single(window =>
            string.Equals(window.ClassName, cleanup.PromptWindowClassName, StringComparison.Ordinal) &&
            string.Equals(window.Title, cleanup.PromptTitle, StringComparison.Ordinal));

        Console.WriteLine($"process : {process.ProcessId} {process.ExecutablePath}");
        Console.WriteLine($"main    : {main.Handle} class '{main.ClassName}' title '{main.Title}' enabled {main.IsEnabled}");
        Console.WriteLine($"dialog  : {dialog.Handle} class '{dialog.ClassName}' title '{dialog.Title}' enabled {dialog.IsEnabled}");

        foreach (string className in new[]
                 {
                     cleanup.Message.ControlClass,
                     cleanup.SaveControl.ControlClass,
                     cleanup.DiscardControl.ControlClass,
                     cleanup.CancelControl.ControlClass
                 }
                     .Distinct(StringComparer.Ordinal))
        {
            foreach (VerifiedControlRef control in controls.LocateByClass(process, dialog.Handle, className).Value)
            {
                Console.WriteLine(
                    $"control : id {control.ControlId} class '{control.ClassName}' text '{controls.ReadText(process, control).Value}'");
            }
        }
    }

    private static string RepositoryFile(string relativePath)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "PrintFlowStudio.sln")))
        {
            current = current.Parent;
        }

        current.ShouldNotBeNull("the repository root must be locatable from the test output.");
        return Path.Combine(current.FullName, relativePath);
    }
}
