using System.IO;
using System.Reflection;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Architecture;

/// <summary>SCRUM-11122's default-deny package/file/network architecture.</summary>
public sealed class DiagnosticPackageBoundaryTests
{
    [Fact]
    public void Writer_accepts_one_typed_plan_and_no_arbitrary_file_collection()
    {
        MethodInfo write = typeof(IDiagnosticPackageWriter).GetMethod(nameof(IDiagnosticPackageWriter.WriteAsync))!;
        write.GetParameters().Select(parameter => parameter.ParameterType).ShouldBe(
            [typeof(DiagnosticPackagePlan), typeof(string), typeof(CancellationToken)]);

        typeof(DiagnosticPackagePlan).GetProperties()
            .ShouldNotContain(property => property.PropertyType == typeof(IEnumerable<string>) &&
                                          property.Name.Contains("File", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Directory.Enumerate")]
    [InlineData("SearchOption.AllDirectories")]
    [InlineData("HttpClient")]
    [InlineData("WebClient")]
    [InlineData("SmtpClient")]
    [InlineData("Upload")]
    public void Package_implementation_has_no_recursive_or_network_collection_path(string forbidden)
    {
        string root = ProjectDirectory("PrintFlow.Infrastructure", "Diagnostics");
        string source = string.Join('\n', Directory.EnumerateFiles(root, "*DiagnosticPackage*.cs")
            .Select(File.ReadAllText));

        source.ShouldNotContain(forbidden, Case.Sensitive);
    }

    [Fact]
    public void Only_failure_screenshot_can_hold_an_evidence_file()
    {
        DiagnosticPackageFileSnapshot snapshot = new("C:\\Evidence\\capture.png", 1, DateTimeOffset.UnixEpoch);
        DiagnosticPackageItem included = DiagnosticPackageItem.IncludedEvidence(
            DiagnosticPackageItemRole.FailureScreenshot, "failure-screenshot.png", snapshot);

        included.Role.ShouldBe(DiagnosticPackageItemRole.FailureScreenshot);
        included.Content.ShouldBe(DiagnosticPackageItemContent.EvidenceFile);
        Should.Throw<ArgumentException>(() => DiagnosticPackagePlan.Create(
            DateTimeOffset.UnixEpoch,
            new DiagnosticPackageSubject(
                default, default, "test", WorkflowType.PrepareAsset, StepKind.Enhancement,
                PrintFlow.Domain.Attempts.AttemptStatus.Failed,
                PrintFlow.Domain.Revisions.OperationKind.Enhance,
                "test", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 1, 0, true),
            new DiagnosticPackageFailureFacts(
                null, null, null, DiagnosticPackageLogStatus.NotRecorded, null, null,
                null, ErrorPathStatus.NotRecorded, null, ErrorPathStatus.NotRecorded,
                null, DiagnosticImageStatus.NotCaptured),
            new DiagnosticPackageApplicationInfo("PrintFlow", "1"),
            new DiagnosticPackageStorageLocations("log", "evidence"),
            new DiagnosticPackageEnvironment(false, null, DateTimeOffset.UnixEpoch, []),
            [
                DiagnosticPackageItem.GeneratedManifest(),
                DiagnosticPackageItem.IncludedEvidence(
                    DiagnosticPackageItemRole.CustomerSource, "customer.png", snapshot),
            ]));
    }

    private static string ProjectDirectory(string project, string? child = null)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "PrintFlowStudio.sln")))
            current = current.Parent;
        string root = current is null
            ? throw new InvalidOperationException("Could not locate PrintFlowStudio.sln.")
            : Path.Combine(current.FullName, "src", project);
        return child is null ? root : Path.Combine(root, child);
    }
}
