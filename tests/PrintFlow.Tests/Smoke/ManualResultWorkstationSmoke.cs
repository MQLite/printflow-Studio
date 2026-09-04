using System.IO;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PrintFlow.App.Composition;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Adapters.Fake;
using PrintFlow.Infrastructure.Configuration;
using PrintFlow.Infrastructure.Sqlite;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;
using Xunit.Abstractions;

namespace PrintFlow.Tests.Smoke;

/// <summary>SCRUM-11092 / SCRUM-11112: opt-in synthetic desktop re-entry proof using an isolated database.</summary>
public sealed class ManualResultWorkstationSmoke(ITestOutputHelper output)
{
    [Fact]
    public async Task Prepare_synthetic_handoffs_for_the_production_Session_screen()
    {
        string? evidence = Environment.GetEnvironmentVariable("PRINTFLOW_MANUAL_RESULT_SEED");
        if (evidence is null) return;
        Directory.CreateDirectory(evidence);
        var configuration = Configuration();
        configuration.Adapters.Mode.ShouldBe("Production");
        string database = Path.Combine(evidence, "manual-result.db");
        var factory = new SqliteConnectionFactory(database);
        using (var connection = factory.Open()) MigrationRunner.Migrate(connection).IsSuccess.ShouldBeTrue();
        // Only seed the failed automation boundary: real manual import, repository and production UI follow.
        // No customer artwork or existing Meitu/Photoshop document is touched by this fixture.
        using var provider = ServiceRegistration.BuildServiceProvider(configuration, configuration.Workspace.Root, factory,
            services => services.AddSingleton<IMeituProcessor>(sp =>
            {
                var fake = new FakeMeituProcessor(sp.GetRequiredService<IWorkspace>());
                fake.SetScenario(FakeAdapterScenario.Timeout);
                return fake;
            }));
        var service = provider.GetRequiredService<ISessionService>();
        var sessions = new List<string>();
        foreach (string name in new[] { "PF_MANUAL_LIVE_A", "PF_MANUAL_LIVE_B" })
        {
            string source = Path.Combine(evidence, name + "-source.png");
            File.WriteAllBytes(source, SyntheticImages.OpaqueRgbPng(640, 480,
                (x, y) => ((byte)(x % 256), (byte)(y % 256), (byte)100)));
            string manual = Path.Combine(evidence, name + "-manual.png");
            File.WriteAllBytes(manual, SyntheticImages.PngWithAlpha(640, 480,
                (x, y) => x is > 100 and < 540 && y is > 80 and < 400 ? (byte)255 : (byte)0));
            var imported = await service.ImportAsync(WorkflowType.PrepareAsset, source, name, "synthetic-live-qa", default);
            imported.IsSuccess.ShouldBeTrue();
            var id = imported.Value.Id;
            await Must(service, id, new WorkflowCommand.ConfirmOriginal());
            (await service.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.Enhancement), "synthetic-live-qa", default))
                .IsFailure.ShouldBeTrue();
            await Must(service, id, new WorkflowCommand.HandOff(StepKind.Enhancement, "SCRUM-11092 / SCRUM-11112 synthetic manual handoff"));
            sessions.Add(id.ToString());
        }
        string proof = Path.Combine(evidence, "expectation.json");
        File.WriteAllText(proof, JsonSerializer.Serialize(new Expectation(database, sessions.ToArray(), Environment.ProcessId)));
        output.WriteLine(proof);
    }

    [Fact]
    public async Task Verify_desktop_results_after_restart_and_authoritative_downstream_input()
    {
        string? proof = Environment.GetEnvironmentVariable("PRINTFLOW_MANUAL_RESULT_VERIFY");
        if (proof is null) return;
        var expected = JsonSerializer.Deserialize<Expectation>(File.ReadAllText(proof))!;
        Environment.ProcessId.ShouldNotBe(expected.ProcessId);
        var configuration = Configuration();
        using var provider = ServiceRegistration.BuildServiceProvider(configuration, configuration.Workspace.Root,
            new SqliteConnectionFactory(expected.Database));
        var service = provider.GetRequiredService<ISessionService>();
        var repository = provider.GetRequiredService<ISessionRepository>();
        var workspace = provider.GetRequiredService<IWorkspace>();
        foreach (string value in expected.Sessions)
        {
            var id = SessionId.From(Guid.Parse(value));
            var saved = (await repository.LoadAsync(id, default)).Value!;
            var resumed = (await service.LoadAsync(id, default)).Value;
            var manual = saved.Revisions.Single(r => r.Operation == OperationKind.ManualResultImport);
            var attempt = saved.Attempts.Single(a => a.OutputRevisionId == manual.Id);
            attempt.Operation.ShouldBe(OperationKind.ManualResultImport);
            attempt.EndedAtUtc.ShouldNotBeNull();
            saved.Attempts.Single(a => a.Operation == OperationKind.Enhance).Status.ShouldBe(Domain.Attempts.AttemptStatus.Failed);
            string name = saved.Session.OutputName.Value;
            byte[] external = File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(proof)!, name + "-manual.png"));
            File.ReadAllBytes(workspace.ResolveAbsolute(manual.File)).ShouldBe(external);
            var root = saved.Revisions.Single(r => r.IsRoot);
            File.ReadAllBytes(workspace.ResolveAbsolute(root.File)).ShouldBe(
                File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(proof)!, name + "-source.png")));
            if (name.EndsWith("_A", StringComparison.Ordinal))
            {
                saved.Reviews.ShouldContain(r => r.SubjectId == manual.Id.Value && r.IsApproved);
                saved.ToSnapshot().UpstreamRevisionOf(StepKind.BackgroundRemoval).ShouldBe(manual.Id);
                saved.Attempts.ShouldContain(a => a.Step == StepKind.Trim && a.InputRevisionId == manual.Id);
            }
            else
            {
                resumed.CurrentStep!.State.ShouldBe(StepState.ReviewRequired);
                resumed.CurrentArtefact!.RevisionId.ShouldBe(manual.Id);
                resumed.CurrentArtefact.Sha256.ShouldBe(manual.Sha256);
            }
            output.WriteLine(JsonSerializer.Serialize(new
            {
                name, SessionId = value, AttemptId = attempt.Id.ToString(), RevisionId = manual.Id.ToString(),
                Hash = manual.Sha256.ToString(), Path = manual.File.RelativePath, State = resumed.CurrentStep!.State.ToString(),
                SourcePreserved = true, ExternalPreserved = true, RestartProcess = Environment.ProcessId,
            }));
        }
        (await repository.GetAutomationLockAsync(default)).Value.IsHeld.ShouldBeFalse();
    }

    private static async Task Must(ISessionService service, SessionId id, WorkflowCommand command)
    {
        var result = await service.ExecuteAsync(id, command, "synthetic-live-qa", default);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : "");
    }

    private static PrintFlowConfiguration Configuration()
    {
        DirectoryInfo? repo = new(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "PrintFlowStudio.sln"))) repo = repo.Parent;
        return PrintFlowConfiguration.LoadFromFile(Path.Combine(repo!.FullName, "appsettings.json"));
    }

    public sealed record Expectation(string Database, string[] Sessions, int ProcessId);
}
