using System.IO;
using System.Text.Json;
using PrintFlow.Domain.Files;
using PrintFlow.Infrastructure.Adapters.Photoshop;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Infrastructure.Configuration;
using PrintFlow.Tests.Fixtures;
using Xunit.Abstractions;

namespace PrintFlow.Tests.Smoke;

/// <summary>Opt-in getter-only census; never an authorizing readiness result or recovery action.</summary>
public sealed class A2PhotoshopReadOnlyObservationSmoke(ITestOutputHelper output)
{
    [Fact]
    public async Task Read_current_documents_without_input()
    {
        if (Environment.GetEnvironmentVariable("PRINTFLOW_A2_READONLY_OBSERVATION") != "1") return;

        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "PrintFlowStudio.sln")))
            root = root.Parent;
        root.ShouldNotBeNull();
        PrintFlowConfiguration configuration = PrintFlowConfiguration.LoadFromFile(
            Path.Combine(root.FullName, "appsettings.json"));
        PresetPhotoshopBaselineProvider provider = new(
            Path.Combine(configuration.Workspace.Root, configuration.Preset.Path),
            Sha256.Parse(configuration.Preset.ExpectedSha256));
        var baseline = provider.GetVerifiedBaseline();
        baseline.IsSuccess.ShouldBeTrue();

        await using WorkstationAutomationLeaseScope lease =
            await WorkstationAutomationLeaseScope.AcquireDefaultAsync();
        Win32ExternalAppWindowLocator locator = new();
        var processes = locator.FindProcessesByExecutable(baseline.Value.ExecutablePath);
        processes.IsSuccess.ShouldBeTrue();
        processes.Value.Count.ShouldBe(1);
        var process = processes.Value[0];
        var facts = new RotPhotoshopRuntimeFactReader().Read(baseline.Value.ExecutablePath);
        output.WriteLine("ObservedAtUtc: " + DateTimeOffset.UtcNow.ToString("O"));
        output.WriteLine("AcceptedProcess: " + JsonSerializer.Serialize(process));
        output.WriteLine("CompleteRead: " + facts.IsSuccess);
        if (facts.IsFailure)
            output.WriteLine("ReadFailure: " + facts.Failure.TechnicalDetail);
        facts.IsSuccess.ShouldBeTrue("an unreadable census must never become an empty document list");
        locator.IsAlive(process).ShouldBeTrue();
        output.WriteLine("DocumentCount: " + facts.Value.Documents.Length);
        foreach (var document in facts.Value.Documents)
            output.WriteLine("Document: " + JsonSerializer.Serialize(document));
        output.WriteLine("ReadOnly: no input, document close, file cleanup or readiness result produced");
    }
}
