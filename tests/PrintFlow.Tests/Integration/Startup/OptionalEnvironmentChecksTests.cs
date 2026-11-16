using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using PrintFlow.App.Composition;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Infrastructure.Configuration;
using PrintFlow.Infrastructure.Sqlite;
using PrintFlow.Infrastructure.Verification;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Tests.Integration.Ui;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Startup;

[Collection(SqliteCollection.Name)]
public sealed class OptionalEnvironmentChecksTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Processing_reaches_the_selected_adapter_without_running_environment_checks(bool photoshop)
    {
        using TempApplication application = new("Production");
        FailingDiagnosticVerifier verifier = new();
        RecordingProductionProcessor processor = new();
        using ServiceProvider services = Compose(application, verifier, processor);
        ISessionService sessions = services.GetRequiredService<ISessionService>();
        string source = Path.Combine(application.WorkspaceRoot, photoshop ? "source.psd" : "source.png");
        File.WriteAllBytes(source, photoshop ? PsdInputPreparationTests.RgbCompositePsd()
            : SyntheticImages.OpaqueRgbPng(12, 10, (_, _) => ((byte)20, (byte)40, (byte)60)));
        var imported = await sessions.ImportAsync(
            photoshop ? WorkflowType.GeneratePrintTiff : WorkflowType.PrepareAsset,
            source, "optional-checks", "tester", CancellationToken.None);
        imported.IsSuccess.ShouldBeTrue(imported.IsFailure ? imported.Failure.ToString() : "");
        if (!photoshop)
            (await sessions.ExecuteAsync(imported.Value.Id, new WorkflowCommand.ConfirmOriginal(),
                "tester", CancellationToken.None)).IsSuccess.ShouldBeTrue();

        OperationResult<SessionView> result = await sessions.ExecuteAsync(imported.Value.Id,
            new WorkflowCommand.StartStep(photoshop ? StepKind.OriginalConfirmation : StepKind.Enhancement),
            "tester", CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.AdapterUnavailable,
            "this refusal comes from the reached adapter, not the environment report");
        processor.Calls.ShouldBe(1);
        verifier.Reads.ShouldBe(0);
        verifier.LiveRuns.ShouldBe(0);
        (await services.GetRequiredService<ISessionRepository>().GetAutomationLockAsync(CancellationToken.None))
            .Value.IsHeld.ShouldBeFalse();
    }

    [Fact]
    public async Task Failed_diagnostics_remain_visible_without_preventing_any_execution_entry()
    {
        using TempApplication application = new("Production");
        FailingDiagnosticVerifier verifier = new();
        using ServiceProvider services = Compose(application, verifier, new RecordingProductionProcessor());
        IEnvironmentDiagnostics diagnostics = services.GetRequiredService<IEnvironmentDiagnostics>();
        diagnostics.Read().Verified.ShouldBeFalse();
        int reads = verifier.Reads;
        IEnvironmentGate gate = services.GetRequiredService<IEnvironmentGate>();
        IWorkstationAutomationLeaseManager leases = services.GetRequiredService<IWorkstationAutomationLeaseManager>();
        IWorkstationAutomationLease lease = (await leases.TryAcquireAsync(null, CancellationToken.None)).Value;
        try
        {
            gate.Verify(AdapterExecutionMode.Production).IsSuccess.ShouldBeTrue();
            ((IWorkstationScopedEnvironmentGate)gate).Verify(AdapterExecutionMode.Production, lease).IsSuccess.ShouldBeTrue();
            ((IInternalProductionEnvironmentGate)gate).VerifyForInternalWork(AdapterExecutionMode.Production).IsSuccess.ShouldBeTrue();
            (await ((IPreparingEnvironmentGate)gate).PrepareAsync(AdapterExecutionMode.Production, CancellationToken.None))
                .IsSuccess.ShouldBeTrue();
            verifier.Reads.ShouldBe(reads);
            verifier.LiveRuns.ShouldBe(0);
            gate.Verify((AdapterExecutionMode)42).IsFailure.ShouldBeTrue();
        }
        finally { (await lease.ReleaseAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue(); }
    }

    [Fact]
    public async Task Optional_environment_checks_still_respect_another_workstation_owner()
    {
        using TempApplication application = new("Production");
        FailingDiagnosticVerifier verifier = new();
        RecordingProductionProcessor processor = new();
        using ServiceProvider services = Compose(application, verifier, processor);
        ISessionService sessions = services.GetRequiredService<ISessionService>();
        string source = Path.Combine(application.WorkspaceRoot, "source.png");
        File.WriteAllBytes(source, SyntheticImages.OpaqueRgbPng(12, 10, (_, _) => ((byte)20, (byte)40, (byte)60)));
        var imported = await sessions.ImportAsync(WorkflowType.PrepareAsset, source, "busy", "tester", CancellationToken.None);
        imported.IsSuccess.ShouldBeTrue();
        (await sessions.ExecuteAsync(imported.Value.Id, new WorkflowCommand.ConfirmOriginal(),
            "tester", CancellationToken.None)).IsSuccess.ShouldBeTrue();
        IWorkstationAutomationLeaseManager authority = services.GetRequiredService<IWorkstationAutomationLeaseManager>();
        IWorkstationAutomationLease owner = (await authority.TryAcquireAsync(null, CancellationToken.None)).Value;
        try
        {
            OperationResult<SessionView> result = await sessions.ExecuteAsync(imported.Value.Id,
                new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None);

            result.IsFailure.ShouldBeTrue();
            result.Failure.Code.ShouldBe(FailureCode.AdapterUnavailable);
            processor.Calls.ShouldBe(0);
            verifier.Reads.ShouldBe(0);
            verifier.LiveRuns.ShouldBe(0);
            (await authority.ObserveAsync(owner, CancellationToken.None)).Status.ShouldBe(WorkstationAutomationLeaseStatus.Owned);
        }
        finally { (await owner.ReleaseAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue(); }
    }

    private static ServiceProvider Compose(TempApplication application,
        FailingDiagnosticVerifier verifier, RecordingProductionProcessor processor)
    {
        PrintFlowConfiguration configuration = PrintFlowConfiguration.LoadFromFile(application.ConfigurationFilePath);
        SqliteConnectionFactory factory = new(application.DatabasePath);
        using (SqliteConnection connection = factory.Open()) MigrationRunner.Migrate(connection).IsSuccess.ShouldBeTrue();
        return ServiceRegistration.BuildServiceProvider(configuration, application.WorkspaceRoot, factory, services =>
        {
            services.AddSingleton<IProductionWorkstationVerifier>(verifier);
            services.AddSingleton<IMeituProcessor>(processor);
            services.AddSingleton<IPhotoshopOutputProcessor>(processor);
            services.AddSingleton<IWorkstationAutomationLeaseManager>(new SqliteWorkstationAutomationLeaseManager(
                Path.Combine(application.WorkspaceRoot, "test-authority.db"), "test.optional." + Guid.NewGuid().ToString("N")));
        });
    }

    private sealed class FailingDiagnosticVerifier : IProductionWorkstationVerifier
    {
        public int Reads { get; private set; }
        public int LiveRuns { get; private set; }
        public WorkstationVerificationResult Verify()
        {
            Reads++;
            return WorkstationVerificationResult.From(null,
                [WorkstationCheckResult.Failed(WorkstationVerificationCheck.ProductionRevalidation,
                    WorkstationCheckKind.Dynamic, FailureCode.EnvironmentNotVerified,
                    "historical approval", "different build", "The saved report covers an older build.")],
                DateTimeOffset.UnixEpoch);
        }
        public Task<WorkstationVerificationResult> RunLiveChecksAsync(CancellationToken cancellationToken)
        {
            LiveRuns++;
            return Task.FromResult(Verify());
        }
    }

    private sealed class RecordingProductionProcessor : IMeituProcessor, IPhotoshopOutputProcessor
    {
        public string AdapterId => "optional-checks-production";
        public AdapterExecutionMode Mode => AdapterExecutionMode.Production;
        public int Calls { get; private set; }
        public Task<OperationResult<AdapterOutput>> ProcessAsync(MeituRequest request, CancellationToken cancellationToken) => Reached();
        public Task<OperationResult<AdapterOutput>> GenerateAsync(PhotoshopRequest request, CancellationToken cancellationToken) => Reached();
        public Task<OperationResult<AdapterOutput>> PreparePsdAsync(PsdPreparationRequest request, CancellationToken cancellationToken) => Reached();
        private Task<OperationResult<AdapterOutput>> Reached()
        {
            Calls++;
            return Task.FromResult(OperationResult.Fail<AdapterOutput>(FailureCode.AdapterUnavailable,
                "Reached the adapter without a mandatory environment check."));
        }
    }
}
