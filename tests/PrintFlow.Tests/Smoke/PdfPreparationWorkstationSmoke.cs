using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PrintFlow.App.Composition;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Configuration;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Infrastructure.Sqlite;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;
using Xunit.Abstractions;

namespace PrintFlow.Tests.Smoke;

/// <summary>Opt-in, synthetic-only accepted-workstation proof. Never invokes Photoshop or TIFF generation.</summary>
public sealed class PdfPreparationWorkstationSmoke(ITestOutputHelper output)
{
    [Fact]
    public async Task Accepted_production_prepares_one_page_and_refuses_multiple_pages_without_selecting_one()
    {
        if (Environment.GetEnvironmentVariable("PRINTFLOW_PDF_PREPARATION_SMOKE") != "1") return;
        var configuration = Configuration();
        configuration.Adapters.Mode.ShouldBe("Production");
        var qa = Path.Combine(configuration.Workspace.Root, "Evidence", "SCRUM-11100-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(qa);
        string database = Path.Combine(qa, "pdf-smoke.db");
        var factory = new SqliteConnectionFactory(database);
        using (var connection = factory.Open()) MigrationRunner.Migrate(connection).IsSuccess.ShouldBeTrue();
        using var provider = ServiceRegistration.BuildServiceProvider(configuration, configuration.Workspace.Root, factory);
        var readiness = provider.GetRequiredService<IEnvironmentDiagnostics>().Read();
        File.WriteAllText(Path.Combine(qa, "readiness.json"), JsonSerializer.Serialize(readiness));
        readiness.Verified.ShouldBeTrue(string.Join("; ", readiness.BlockingFailures.Select(f => f.Detail)));
        var gate = provider.GetRequiredService<IEnvironmentGate>().Verify(AdapterExecutionMode.Production);
        gate.IsSuccess.ShouldBeTrue(gate.IsFailure ? gate.Failure.ToString() : "");
        var processor = provider.GetRequiredService<IPdfPreparationProcessor>().ShouldBeOfType<WindowsPdfPreparationProcessor>();
        var authority = processor.Authority.ShouldBeOfType<WindowsPdfDocumentAuthority>();
        var service = provider.GetRequiredService<ISessionService>();
        var repository = provider.GetRequiredService<ISessionRepository>();
        var workspace = provider.GetRequiredService<IWorkspace>();
        foreach (string fixture in new[] { "single", "two" })
        {
            int selections = authority.PageSelections, renders = authority.RenderCalls;
            byte[] bytes = PdfFixtures.Read(fixture);
            string source = Path.Combine(qa, "PF_SCRUM11100_" + fixture + ".pdf");
            File.WriteAllBytes(source, bytes);
            string beforeHash = Convert.ToHexString(SHA256.HashData(bytes));
            File.WriteAllText(Path.Combine(qa, fixture + "-before.sha256"), beforeHash);
            var imported = await service.ImportAsync(WorkflowType.GeneratePrintTiff, source, null, "synthetic-qa", default);
            imported.IsSuccess.ShouldBeTrue(imported.IsFailure ? imported.Failure.ToString() : "");
            authority.RenderCalls.ShouldBe(renders);
            var prepared = await service.ExecuteAsync(imported.Value.Id, new WorkflowCommand.StartStep(StepKind.OriginalConfirmation), "synthetic-qa", default);
            var stored = (await repository.LoadAsync(imported.Value.Id, default)).Value!;
            var root = stored.Revisions.Single(r => r.IsRoot);
            var attempt = stored.Attempts.Single(a => a.Operation == OperationKind.PreparePdf);
            File.ReadAllBytes(source).ShouldBe(bytes);
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(workspace.ResolveAbsolute(root.File)))).ShouldBe(beforeHash);
            (await repository.GetAutomationLockAsync(default)).Value.IsHeld.ShouldBeFalse();
            if (fixture == "single")
            {
                prepared.IsSuccess.ShouldBeTrue(prepared.IsFailure ? prepared.Failure.ToString() : "");
                stored.ToSnapshot().CurrentStep!.State.ShouldBe(StepState.ReviewRequired);
                var raster = stored.Revisions.Single(r => r.Operation == OperationKind.PreparePdf);
                raster.Facts.PixelWidth.ShouldBe(600); raster.Facts.PixelHeight.ShouldBe(300);
                attempt.PdfInspection!.IsPreparedSinglePage.ShouldBeTrue();
                authority.PageSelections.ShouldBe(selections + 1); authority.RenderCalls.ShouldBe(renders + 1);
                var expectation = new Expectation(database, stored.Session.Id.ToString(), root.Id.ToString(), root.Sha256.ToString(),
                    raster.Id.ToString(), raster.Sha256.ToString(), attempt.PdfInspection, Environment.ProcessId);
                string proof = Path.Combine(qa, "resume.json");
                File.WriteAllText(proof, JsonSerializer.Serialize(expectation));
                output.WriteLine("Resume proof: " + proof);
            }
            else
            {
                prepared.Failure.Code.ShouldBe(FailureCode.PdfMultiplePages);
                attempt.PdfInspection!.PageCount.ShouldBe(2);
                attempt.OutputRevisionId.ShouldBeNull(); stored.Revisions.Count.ShouldBe(1);
                authority.PageSelections.ShouldBe(selections); authority.RenderCalls.ShouldBe(renders);
                string sessionDirectory = Path.GetDirectoryName(Path.GetDirectoryName(workspace.ResolveAbsolute(root.File)))!;
                Directory.GetFiles(sessionDirectory, "*.png", SearchOption.AllDirectories).ShouldBeEmpty();
            }
            var evidence = new
            {
                Fixture = fixture, Session = imported.Value.Id.ToString(), Source = source, SourceHash = beforeHash,
                Attempt = attempt.Id.ToString(), Inspection = attempt.PdfInspection,
                PageSelections = authority.PageSelections - selections, RenderCalls = authority.RenderCalls - renders,
                RevisionCount = stored.Revisions.Count, LockFree = true, SourceUnchanged = true,
                Result = prepared.IsSuccess ? "ReviewRequired" : prepared.Failure.Code.ToString(),
            };
            File.WriteAllText(Path.Combine(qa, fixture + "-result.json"), JsonSerializer.Serialize(evidence));
            output.WriteLine(JsonSerializer.Serialize(evidence));
        }
        output.WriteLine("Production Readiness Ready; gate ALLOWED; evidence: " + qa);
    }

    [Fact]
    public async Task Separate_process_resume_loads_identical_pdf_and_raster_without_rendering()
    {
        string? proof = Environment.GetEnvironmentVariable("PRINTFLOW_PDF_RESUME_PROOF");
        if (proof is null) return;
        var expected = JsonSerializer.Deserialize<Expectation>(File.ReadAllText(proof))!;
        Environment.ProcessId.ShouldNotBe(expected.ProcessId);
        var configuration = Configuration();
        using var provider = ServiceRegistration.BuildServiceProvider(configuration, configuration.Workspace.Root,
            new SqliteConnectionFactory(expected.Database));
        var authority = provider.GetRequiredService<IPdfPreparationProcessor>().ShouldBeOfType<WindowsPdfPreparationProcessor>()
            .Authority.ShouldBeOfType<WindowsPdfDocumentAuthority>();
        var repository = provider.GetRequiredService<ISessionRepository>();
        var id = SessionId.From(Guid.Parse(expected.Session));
        var before = (await repository.LoadAsync(id, default)).Value!;
        var resumed = await provider.GetRequiredService<ISessionService>().LoadAsync(id, default);
        resumed.IsSuccess.ShouldBeTrue();
        resumed.Value.CurrentStep!.State.ShouldBe(StepState.ReviewRequired);
        resumed.Value.PdfInspection.ShouldBe(expected.Inspection);
        var after = (await repository.LoadAsync(id, default)).Value!;
        after.Attempts.Count.ShouldBe(before.Attempts.Count);
        after.Attempts.Count.ShouldBe(2);
        var source = after.Revisions.Single(r => r.IsRoot);
        var raster = after.Revisions.Single(r => r.Operation == OperationKind.PreparePdf);
        source.Id.ToString().ShouldBe(expected.SourceRevision); source.Sha256.ToString().ShouldBe(expected.SourceHash);
        raster.Id.ToString().ShouldBe(expected.PreparedRevision); raster.Sha256.ToString().ShouldBe(expected.PreparedHash);
        raster.Facts.PixelWidth.ShouldBe(600); raster.Facts.PixelHeight.ShouldBe(300);
        authority.RenderCalls.ShouldBe(0); authority.PageSelections.ShouldBe(0);
        (await repository.GetAutomationLockAsync(default)).Value.IsHeld.ShouldBeFalse();
        output.WriteLine($"Process {Environment.ProcessId}: same source/raster IDs, hashes, dimensions and inspection; two attempts; zero page selections/renders; lock free.");
    }

    private static PrintFlowConfiguration Configuration()
    {
        DirectoryInfo? repo = new(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "PrintFlowStudio.sln"))) repo = repo.Parent;
        return PrintFlowConfiguration.LoadFromFile(Path.Combine(repo!.FullName, "appsettings.json"));
    }

    public sealed record Expectation(string Database, string Session, string SourceRevision, string SourceHash,
        string PreparedRevision, string PreparedHash, PdfInspection Inspection, int ProcessId);
}
