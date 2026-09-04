using System.IO;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PrintFlow.App.Composition;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Configuration;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Infrastructure.Sqlite;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;
using Xunit.Abstractions;

namespace PrintFlow.Tests.Smoke;

public sealed class PsdResumeWorkstationSmoke(ITestOutputHelper output)
{
    [Fact]
    public async Task No_composite_is_refused_in_production_without_opening_Photoshop()
    {
        if (Environment.GetEnvironmentVariable("PRINTFLOW_PSD_NO_COMPOSITE_SMOKE") != "1") return;
        DirectoryInfo? repo = new(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "PrintFlowStudio.sln"))) repo = repo.Parent;
        var configuration = PrintFlowConfiguration.LoadFromFile(Path.Combine(repo!.FullName, "appsettings.json"));
        string qa = Path.Combine(configuration.Workspace.Root, "Evidence", "SCRUM-11099-no-composite-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(qa);
        var factory = new SqliteConnectionFactory(Path.Combine(qa, "psd-smoke.db"));
        using (var connection = factory.Open()) MigrationRunner.Migrate(connection).IsSuccess.ShouldBeTrue();
        using var provider = ServiceRegistration.BuildServiceProvider(configuration, configuration.Workspace.Root, factory);
        var gate = provider.GetRequiredService<IEnvironmentGate>().Verify(AdapterExecutionMode.Production);
        gate.IsSuccess.ShouldBeTrue(gate.IsFailure ? gate.Failure.ToString() : "");
        byte[] bytes = Integration.Ui.PsdInputPreparationTests.RgbCompositePsd(); bytes[50] = 0;
        string source = Path.Combine(qa, "PF_NO_COMPOSITE.psd"); File.WriteAllBytes(source, bytes);
        var service = provider.GetRequiredService<ISessionService>();
        var imported = await service.ImportAsync(WorkflowType.GeneratePrintTiff, source, null, "synthetic-qa", CancellationToken.None);
        imported.IsSuccess.ShouldBeTrue();
        var refused = await service.ExecuteAsync(imported.Value.Id,
            new Workflow.Commands.WorkflowCommand.StartStep(StepKind.OriginalConfirmation), "synthetic-qa", CancellationToken.None);
        refused.IsFailure.ShouldBeTrue();
        refused.Failure.Code.ShouldBe(Domain.Results.FailureCode.PsdCompositeMissing);
        var repository = provider.GetRequiredService<ISessionRepository>();
        var state = (await repository.LoadAsync(imported.Value.Id, CancellationToken.None)).Value!;
        state.Revisions.Count.ShouldBe(1);
        (await repository.GetAutomationLockAsync(CancellationToken.None)).Value.IsHeld.ShouldBeFalse();
        File.ReadAllBytes(source).ShouldBe(bytes);
        output.WriteLine($"Production gate ALLOWED; {qa}; session {imported.Value.Id}; PsdCompositeMissing before any Photoshop call; " +
            "source unchanged; no prepared Revision; lock free.");
    }

    [Fact]
    public async Task A_separate_process_restores_the_same_prepared_psd_without_automation()
    {
        string? proof = Environment.GetEnvironmentVariable("PRINTFLOW_PSD_RESUME_PROOF");
        if (proof is null) return;
        var expected = JsonSerializer.Deserialize<Expectation>(File.ReadAllText(proof))!;
        DirectoryInfo? repo = new(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "PrintFlowStudio.sln"))) repo = repo.Parent;
        var configuration = PrintFlowConfiguration.LoadFromFile(Path.Combine(repo!.FullName, "appsettings.json"));
        var factory = new SqliteConnectionFactory(expected.Database);
        using var provider = ServiceRegistration.BuildServiceProvider(configuration, configuration.Workspace.Root, factory);
        var repository = provider.GetRequiredService<ISessionRepository>();
        var id = SessionId.From(Guid.Parse(expected.Session));
        var before = (await repository.LoadAsync(id, CancellationToken.None)).Value!;
        var resumed = await provider.GetRequiredService<ISessionService>().LoadAsync(id, CancellationToken.None);
        resumed.IsSuccess.ShouldBeTrue();
        resumed.Value.CurrentStep!.State.ShouldBe(StepState.ReviewRequired);
        var after = (await repository.LoadAsync(id, CancellationToken.None)).Value!;
        after.Attempts.Count.ShouldBe(before.Attempts.Count);
        var source = after.Revisions.Single(r => r.IsRoot);
        var prepared = after.Revisions.Single(r => r.Operation == OperationKind.PreparePsd);
        source.Id.ToString().ShouldBe(expected.SourceRevision);
        source.Sha256.ToString().ShouldBe(expected.SourceHash);
        prepared.Id.ToString().ShouldBe(expected.PreparedRevision);
        prepared.Sha256.ToString().ShouldBe(expected.PreparedHash);
        prepared.Facts.PixelWidth.ShouldBe(expected.Width);
        prepared.Facts.PixelHeight.ShouldBe(expected.Height);
        var workspace = provider.GetRequiredService<IWorkspace>();
        foreach (var revision in new[] { source, prepared })
        {
            var facts = await new WicFileInspector().InspectAsync(workspace.ResolveAbsolute(revision.File), CancellationToken.None);
            facts.Value.Sha256.ShouldBe(revision.Sha256);
        }
        (await repository.GetAutomationLockAsync(CancellationToken.None)).Value.IsHeld.ShouldBeFalse();
        output.WriteLine($"Process {Environment.ProcessId}: resumed session {id}; identical source {source.Id} and raster {prepared.Id}; " +
            $"identical hashes and {expected.Width}x{expected.Height} dimensions; {after.Attempts.Count} attempts unchanged; lock free.");
    }

    public sealed record Expectation(string Database, string Session, string SourceRevision, string SourceHash,
        string PreparedRevision, string PreparedHash, int Width, int Height);
}
