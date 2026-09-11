using System.Collections.Immutable;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using PrintFlow.App.Composition;
using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Adapters.Meitu;
using PrintFlow.Infrastructure.Adapters.Photoshop;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Infrastructure.Configuration;
using PrintFlow.Infrastructure.Gate;
using PrintFlow.Infrastructure.Verification;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Tests.Integration.Ui;
using PrintFlow.Tests.Regression;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Verification;

public sealed class ProductionLiveWorkstationVerifierTests
{
    [Theory]
    [InlineData("before-open")]
    [InlineData("identity")]
    [InlineData("close-unconfirmed")]
    [InlineData("cleanup")]
    [InlineData("restore-and-cleanup")]
    [InlineData("primary-and-release")]
    [InlineData("close-exception")]
    public async Task Failed_real_probe_reaches_readiness_json_with_partial_diagnostics(string scenario)
    {
        using WorkstationVerificationFixture fixture = new();
        FileWorkspace workspace = new(fixture.WorkspaceRoot);
        bool openFailure = scenario is "before-open" or "identity" or "primary-and-release";
        bool corruptProbe = scenario is "cleanup" or "restore-and-cleanup";
        ScriptedPhotoshop photoshop = new(fixture.PhotoshopPath, workspace)
        {
            OpenFailure = openFailure ? OperationFailure.Create(
                scenario == "before-open" ? FailureCode.PreconditionNotMet : FailureCode.PhotoshopDocumentIdentityUnconfirmed,
                "synthetic open/identity refusal", context: new Dictionary<string, string>
                {
                    ["inputSent"] = scenario == "before-open" ? "false" : "true",
                    ["unrelatedInventory"] = "must not be projected",
                }) : null,
            FailureOpenProgress = scenario == "before-open"
                ? ReadinessProbeStage.OpenGuard : ReadinessProbeStage.IdentityCheck,
            CloseFailure = scenario == "close-unconfirmed"
                ? OperationFailure.Create(FailureCode.Timeout, "synthetic close unconfirmed") : null,
            RestorationFailure = scenario == "restore-and-cleanup"
                ? OperationFailure.Create(FailureCode.PhotoshopUnknownState, "synthetic restoration refusal") : null,
            CorruptProbeOnClose = corruptProbe,
            ThrowOnClose = scenario == "close-exception",
        };
        RecordingLock authority = new()
        {
            ReleaseFailure = scenario == "primary-and-release"
                ? OperationFailure.Create(FailureCode.AdapterUnavailable, "synthetic release refusal") : null,
        };
        ProductionLiveWorkstationVerifier live = new(
            new ScriptedMeitu(fixture.MeituPath), photoshop,
            new ScriptedRuntimeFacts(MatchingSettings(), []), authority, workspace, fixture.Clock);
        VerifiedEnvironmentGate gate = new(FullVerifier(fixture, live, omitRevalidation: false));

        EnvironmentReadinessReport report = await gate.RunLiveChecksAsync(CancellationToken.None);
        report.Verified.ShouldBeFalse();
        System.Text.Json.JsonSerializerOptions options = new()
        {
            WriteIndented = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        };
        string reportPath = Path.Combine(fixture.WorkspaceRoot, "readiness.json");
        File.WriteAllText(reportPath, System.Text.Json.JsonSerializer.Serialize(report, options));
        using System.Text.Json.JsonDocument json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(reportPath));
        json.RootElement.TryGetProperty("Lifecycle", out _).ShouldBeTrue(
            "the real probe currently loses all stage/cleanup facts at its normal report boundary");
        ReadinessProbeDiagnostics probe = report.Lifecycle!.LatestProbe.ShouldNotBeNull();
        EnvironmentReadinessReport restored = System.Text.Json.JsonSerializer.Deserialize<EnvironmentReadinessReport>(
            json.RootElement.GetRawText(), options)!;
        ReadinessProbeDiagnostics projected = restored.Lifecycle!.LatestProbe.ShouldNotBeNull();
        projected.OperationId.ShouldBe(probe.OperationId);
        projected.ManagedPath.ShouldBe(probe.ManagedPath);
        projected.Process.ShouldBe(probe.Process);
        projected.PrimaryFailure.ShouldBe(probe.PrimaryFailure);
        projected.Stages.ShouldBe(probe.Stages);
        projected.SecondaryFailures.ShouldBe(probe.SecondaryFailures);
        projected.Process.ProcessId.ShouldBe(707);
        projected.Process.ExecutablePath.ShouldBe(fixture.PhotoshopPath);
        projected.ManagedPath!.ShouldContain(probe.OperationId);
        projected.Stages.ShouldContain(ReadinessProbeStage.ProbeCreated);
        projected.PrimaryFailure.ShouldNotBeNull();
        report.Lifecycle.LastSuccessfulLiveAt.ShouldBeNull();
        report.Lifecycle.EvidenceAvailable.ShouldBeFalse();
        report.Lifecycle.LatestAttemptAt.ShouldBe(fixture.Clock.GetUtcNow());
        json.RootElement.GetRawText().ShouldNotContain("unrelatedInventory");
        json.RootElement.GetRawText().ShouldNotContain("OwnerToken");

        if (openFailure)
        {
            probe.PrimaryFailure!.Code.ShouldBe(photoshop.OpenFailure!.Code);
            probe.PrimaryFailure.InputSent.ShouldBe(scenario == "before-open" ? "false" : "true");
            probe.Stages.ShouldNotContain(ReadinessProbeStage.CloseRequested);
            probe.Stages.ShouldNotContain(ReadinessProbeStage.IdentityConfirmed);
            photoshop.CloseCalls.ShouldBe(0);
            if (scenario == "before-open") probe.Stages.ShouldNotContain(ReadinessProbeStage.OpenRequested);
            else probe.Stages.ShouldContain(ReadinessProbeStage.OpenConfirmed);
        }
        if (corruptProbe)
        {
            probe.CleanupOutcome.ShouldBe(ProbeCleanupOutcome.Failed);
            probe.Stages.ShouldContain(ReadinessProbeStage.CloseConfirmed);
            probe.Stages.ShouldContain(ReadinessProbeStage.CleanupAttempted);
            probe.Stages.ShouldNotContain(ReadinessProbeStage.CleanupCompleted);
            if (scenario == "cleanup")
            {
                probe.PrimaryFailure!.Code.ShouldBe(FailureCode.WorkspaceError);
                probe.Stages.ShouldContain(ReadinessProbeStage.PriorStateRestored);
            }
            else
            {
                probe.PrimaryFailure!.Code.ShouldBe(FailureCode.PhotoshopUnknownState);
                probe.SecondaryFailures.ShouldHaveSingleItem().Code.ShouldBe(FailureCode.WorkspaceError);
                probe.Stages.ShouldNotContain(ReadinessProbeStage.PriorStateRestored);
            }
        }
        else
        {
            probe.CleanupOutcome.ShouldBe(ProbeCleanupOutcome.NotRun);
            probe.Stages.ShouldNotContain(ReadinessProbeStage.CleanupAttempted);
            probe.Stages.ShouldNotContain(ReadinessProbeStage.PriorStateRestored);
            probe.Stages.ShouldNotContain(ReadinessProbeStage.CloseConfirmed);
        }
        if (scenario is "close-unconfirmed" or "close-exception")
        {
            probe.Stages.ShouldContain(ReadinessProbeStage.CloseRequested);
            probe.PrimaryFailure!.Code.ShouldBe(scenario == "close-unconfirmed"
                ? FailureCode.Timeout : FailureCode.EnvironmentNotVerified);
        }
        if (scenario == "primary-and-release")
            probe.SecondaryFailures.ShouldHaveSingleItem().Phase.ShouldBe("LeaseRelease");
        File.Exists(probe.ManagedPath).ShouldBeTrue("uncertain or changed backing files remain retained");
        authority.ReleaseCalls.ShouldBe(1);

        EnvironmentCheckReport check = report.Checks.Single(check => check.CheckKey == "PhotoshopTestImageRoundTrip");
        check.Lifecycle.ShouldBe(report.Lifecycle);
        restored.Checks.Single(item => item.CheckKey == check.CheckKey).Lifecycle!.LatestProbe!
            .OperationId.ShouldBe(probe.OperationId);
        report.Checks.Where(item => item.CheckKey != check.CheckKey)
            .ShouldAllBe(item => item.Lifecycle == null);
        EnvironmentCheckRow row = new(check with { CheckKey = "FutureDiagnosticCheck" });
        row.Detail.ShouldContain(probe.OperationId);
        row.Detail.ShouldContain(probe.PrimaryFailure!.Code.ToString());
        row.IsFailure.ShouldBeTrue();
        gate.Read().Lifecycle!.LatestProbe!.OperationId.ShouldBe(probe.OperationId,
            "passive refresh must retain the latest diagnostic without certifying it");
    }

    [Fact]
    public void Historical_readiness_json_has_no_invented_lifecycle()
    {
        const string historical = """{"Verified":false,"PresetIdentity":null,"ObservedAt":"2026-09-01T00:00:00Z","Checks":[]}""";
        System.Text.Json.JsonSerializer.Deserialize<EnvironmentReadinessReport>(historical)!
            .Lifecycle.ShouldBeNull();
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("drift")]
    [InlineData("failed-probe")]
    public async Task Evidence_history_is_separate_from_current_admission_and_latest_attempt(string invalidation)
    {
        using WorkstationVerificationFixture fixture = new();
        FileWorkspace workspace = new(fixture.WorkspaceRoot);
        ScriptedPhotoshop photoshop = new(fixture.PhotoshopPath, workspace);
        ScriptedMeitu meitu = new(fixture.MeituPath);
        ScriptedRuntimeFacts facts = new(MatchingSettings(), []);
        RecordingLock authority = new();
        ProductionLiveWorkstationVerifier live = new(meitu, photoshop, facts, authority, workspace, fixture.Clock);
        VerifiedEnvironmentGate gate = new(FullVerifier(fixture, live, omitRevalidation: false));

        gate.Read().Lifecycle!.LastSuccessfulLiveAt.ShouldBeNull();
        authority.AcquireCalls.ShouldBe(0);
        EnvironmentReadinessReport complete = await gate.RunLiveChecksAsync(CancellationToken.None);
        complete.Verified.ShouldBeTrue();
        DateTimeOffset completedAt = complete.Lifecycle!.LastSuccessfulLiveAt!.Value;
        string originalProbe = complete.Lifecycle.LatestProbe!.OperationId;
        fixture.Clock.Advance(TimeSpan.FromMinutes(5));
        IWorkstationAutomationLease owner = (await authority.TryAcquireAsync(null, CancellationToken.None)).Value;
        int factsBeforeBusy = facts.ReadCalls;
        int inspectionsBeforeBusy = photoshop.ReinspectCalls;
        for (int index = 0; index < 2; index++)
        {
            EnvironmentReadinessReport busy = gate.Read();
            busy.Verified.ShouldBeFalse();
            busy.Lifecycle!.CurrentObservationDeferred.ShouldBeTrue();
            busy.Lifecycle.EvidenceAvailable.ShouldBeTrue();
            busy.Lifecycle.LastSuccessfulLiveAt.ShouldBe(completedAt);
            busy.Lifecycle.LatestProbe!.OperationId.ShouldBe(originalProbe);
            busy.Checks.Single(check => check.CheckKey == "PhotoshopTestImageRoundTrip")
                .Status.ShouldBe(EnvironmentCheckStatus.Blocked);
        }
        facts.ReadCalls.ShouldBe(factsBeforeBusy);
        photoshop.ReinspectCalls.ShouldBe(inspectionsBeforeBusy);
        authority.AcquireCalls.ShouldBe(2);
        photoshop.OpenCalls.ShouldBe(1);
        (await owner.ReleaseAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();
        EnvironmentReadinessReport fresh = gate.Read();
        fresh.Verified.ShouldBeTrue();
        fresh.Lifecycle!.CurrentObservationDeferred.ShouldBeFalse();
        fresh.Lifecycle.LastSuccessfulLiveAt.ShouldBe(completedAt);
        photoshop.ReinspectCalls.ShouldBeGreaterThan(inspectionsBeforeBusy);
        facts.ReadCalls.ShouldBeGreaterThan(factsBeforeBusy);
        photoshop.OpenCalls.ShouldBe(1);

        EnvironmentReadinessReport invalid;
        if (invalidation == "unknown")
        {
            authority.ObservationOverride = WorkstationAutomationLeaseStatus.Unknown;
            invalid = gate.Read();
            authority.ObservationOverride = null;
        }
        else if (invalidation == "drift")
        {
            photoshop.RestorationFailure = OperationFailure.Create(FailureCode.PhotoshopTargetLost,
                "synthetic certified process was replaced");
            invalid = gate.Read();
            photoshop.RestorationFailure = null;
        }
        else
        {
            photoshop.OpenFailure = OperationFailure.Create(FailureCode.PhotoshopDocumentIdentityUnconfirmed,
                "synthetic later probe failed");
            invalid = await gate.RunLiveChecksAsync(CancellationToken.None);
            invalid.Lifecycle!.LatestProbe!.OperationId.ShouldNotBe(originalProbe);
            invalid.Lifecycle.LatestAttemptAt.ShouldBe(fixture.Clock.GetUtcNow());
            photoshop.OpenFailure = null;
        }
        invalid.Verified.ShouldBeFalse();
        invalid.Lifecycle!.EvidenceAvailable.ShouldBeFalse();
        invalid.Lifecycle.LastSuccessfulLiveAt.ShouldBe(completedAt);
        invalid.Lifecycle.Photoshop!.ProcessId.ShouldBe(707);
        gate.Read().Verified.ShouldBeFalse("restored facts alone cannot revive invalidated live evidence");

        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        EnvironmentReadinessReport rerun = await gate.RunLiveChecksAsync(CancellationToken.None);
        rerun.Verified.ShouldBeTrue();
        rerun.Lifecycle!.LastSuccessfulLiveAt.ShouldBe(fixture.Clock.GetUtcNow());
        rerun.Lifecycle.LatestProbe!.OperationId.ShouldNotBe(originalProbe);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Passive_observation_cannot_cross_a_new_live_evidence_revision(bool oldObservationPassed)
    {
        using WorkstationVerificationFixture fixture = new();
        FileWorkspace workspace = new(fixture.WorkspaceRoot);
        ScriptedPhotoshop photoshop = new(fixture.PhotoshopPath, workspace);
        ProductionLiveWorkstationVerifier live = new(new ScriptedMeitu(fixture.MeituPath),
            photoshop, new ScriptedRuntimeFacts(MatchingSettings(), []),
            new RecordingLock(), workspace, fixture.Clock);
        PausingReobserver interleaving = new(live);
        VerifiedEnvironmentGate gate = new(FullVerifier(fixture, interleaving, omitRevalidation: false));
        if (oldObservationPassed)
        {
            (await gate.RunLiveChecksAsync(CancellationToken.None)).Verified.ShouldBeTrue();
            photoshop.OpenFailure = OperationFailure.Create(FailureCode.PhotoshopDocumentIdentityUnconfirmed,
                "synthetic replacement attempt failed");
        }
        Task<EnvironmentReadinessReport> old = Task.Run(gate.Read);
        try
        {
            await interleaving.Observed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            (await gate.RunLiveChecksAsync(CancellationToken.None)).Verified.ShouldBe(!oldObservationPassed);
        }
        finally
        {
            interleaving.ReturnObservation.TrySetResult(true);
        }
        (await old.WaitAsync(TimeSpan.FromSeconds(10))).Verified.ShouldBeFalse();
        gate.Read().Verified.ShouldBe(!oldObservationPassed,
            "an old observation cannot clear newer evidence or authorize from evidence invalidated by a later attempt");
    }

    [Fact]
    public async Task Release_exception_cannot_erase_completed_probe_progress_or_certify_evidence()
    {
        using WorkstationVerificationFixture fixture = new();
        FileWorkspace workspace = new(fixture.WorkspaceRoot);
        RecordingLock authority = new() { ThrowOnRelease = true };
        ProductionLiveWorkstationVerifier live = new(new ScriptedMeitu(fixture.MeituPath),
            new ScriptedPhotoshop(fixture.PhotoshopPath, workspace), new ScriptedRuntimeFacts(MatchingSettings(), []),
            authority, workspace, fixture.Clock);
        VerifiedEnvironmentGate gate = new(FullVerifier(fixture, live, omitRevalidation: false));
        EnvironmentReadinessReport report = await gate.RunLiveChecksAsync(CancellationToken.None);
        report.Verified.ShouldBeFalse();
        report.Lifecycle!.EvidenceAvailable.ShouldBeFalse();
        report.Lifecycle.LastSuccessfulLiveAt.ShouldBeNull();
        report.Lifecycle.LatestProbe!.PrimaryFailure!.Phase.ShouldBe("LeaseRelease");
        report.Lifecycle.LatestProbe.CleanupOutcome.ShouldBe(ProbeCleanupOutcome.Succeeded);
        report.Lifecycle.LatestProbe.Stages.ShouldContain(ReadinessProbeStage.PriorStateRestored);
        authority.IsHeld.ShouldBeTrue();
    }

    [Fact]
    public async Task App_live_verifier_bootstrap_and_direct_scope_compete_on_one_real_isolated_authority()
    {
        using TempWorkspace leaseFiles = new();
        string store = Path.Combine(leaseFiles.Root, "shared-authority", "lease.db");
        string resource = "test.compositions." + Guid.NewGuid().ToString("N");
        SqliteWorkstationAutomationLeaseManager authority = new(store, resource);

        using WorkstationVerificationFixture workstation = new();
        workstation.RemoveRevalidationRecord();
        FileWorkspace liveWorkspace = new(workstation.WorkspaceRoot);
        ScriptedMeitu meitu = new(workstation.MeituPath);
        ScriptedPhotoshop photoshop = new(workstation.PhotoshopPath, liveWorkspace);
        ProductionLiveWorkstationVerifier live = new(
            meitu, photoshop, new ScriptedRuntimeFacts(MatchingSettings(), []),
            authority, liveWorkspace, workstation.Clock);
        ProductionWorkstationVerifier real = FullVerifier(workstation, live, omitRevalidation: false);
        ProductionWorkstationVerifier withoutRevalidation =
            FullVerifier(workstation, live, omitRevalidation: true);
        RegressionBootstrapWorkstationVerifier bootstrap = new(real, withoutRevalidation);

        using TempApplication application = new("Production");
        PrintFlowConfiguration configuration =
            PrintFlowConfiguration.LoadFromFile(application.ConfigurationFilePath);
        SqliteConnectionFactory business = new(application.DatabasePath);
        using (SqliteConnection connection = business.Open())
            MigrationRunner.Migrate(connection).IsSuccess.ShouldBeTrue();
        RecordingPsdProcessor appPhotoshop = new(new FileWorkspace(application.WorkspaceRoot));
        using ServiceProvider provider = ServiceRegistration.BuildServiceProvider(
            configuration, application.WorkspaceRoot, business, services =>
            {
                services.AddSingleton<IWorkstationAutomationLeaseManager>(authority);
                services.AddSingleton<IEnvironmentGate>(new VerifiedEnvironmentGate(bootstrap));
                services.AddSingleton<IPhotoshopOutputProcessor>(appPhotoshop);
            });
        ISessionService app = provider.GetRequiredService<ISessionService>();
        string source = Path.Combine(application.WorkspaceRoot, "composition.psd");
        File.WriteAllBytes(source, PsdInputPreparationTests.RgbCompositePsd());
        SessionId session = (await app.ImportAsync(
            WorkflowType.GeneratePrintTiff, source, null, "qa", CancellationToken.None)).Value.Id;

        await using (WorkstationAutomationLeaseScope direct =
                     await WorkstationAutomationLeaseScope.AcquireAsync(authority))
        {
            OperationResult<SessionView> refused = await app.ExecuteAsync(
                session, new WorkflowCommand.StartStep(StepKind.OriginalConfirmation),
                "qa", CancellationToken.None);
            refused.IsFailure.ShouldBeTrue();
            refused.Failure.Code.ShouldBe(FailureCode.AdapterUnavailable);
            appPhotoshop.Calls.ShouldBe(0);

            WorkstationLiveVerification liveRefused = await live.RunAsync(
                Requirements(workstation), CancellationToken.None);
            liveRefused.Checks[0].Outcome.ShouldBe(WorkstationCheckOutcome.Failed);
            meitu.EnsureCalls.ShouldBe(0);
        }

        WorkstationVerificationResult bootstrappedLive = await bootstrap
            .RunLiveChecksAsync(CancellationToken.None);
        bootstrappedLive.Verified.ShouldBeTrue();
        bootstrap.BootstrapWasUsed.ShouldBeTrue();
        VerifiedEnvironmentGate appGate = provider.GetRequiredService<IEnvironmentGate>()
            .ShouldBeOfType<VerifiedEnvironmentGate>();
        int probeOpens = photoshop.OpenCalls;
        int probeCloses = photoshop.CloseCalls;

        await using (WorkstationAutomationLeaseScope direct =
                     await WorkstationAutomationLeaseScope.AcquireAsync(authority))
        {
            appGate.Verify(AdapterExecutionMode.Production).IsFailure.ShouldBeTrue(
                "ordinary external admission must still fail while another scope owns automation");
            photoshop.OpenCalls.ShouldBe(probeOpens);
            photoshop.CloseCalls.ShouldBe(probeCloses);

            WorkstationVerificationResult internalVerification =
                ((IInternalProductionWorkstationVerifier)bootstrap).VerifyForInternalWork();
            internalVerification.Verified.ShouldBeTrue(
                string.Join(" | ", internalVerification.Checks.Select(check =>
                    $"{check.Check}={check.Outcome}/{check.Observed}/{check.Explanation}")));
            internalVerification.Checks.Single(check =>
                    check.Check == WorkstationVerificationCheck.ExternalApplicationAutomationLock)
                .Outcome.ShouldBe(WorkstationCheckOutcome.Advisory);
            internalVerification.Checks
                .Where(check => check.Check != WorkstationVerificationCheck.ExternalApplicationAutomationLock)
                .ShouldAllBe(check => check.Outcome == WorkstationCheckOutcome.Passed ||
                                      check.Outcome == WorkstationCheckOutcome.Advisory);
            ((IInternalProductionEnvironmentGate)appGate)
                .VerifyForInternalWork(AdapterExecutionMode.Production).IsSuccess.ShouldBeTrue();

            string pdfSource = Path.Combine(leaseFiles.Root, "in-process.pdf");
            File.WriteAllBytes(pdfSource, PdfFixtures.Read("single"));
            SessionView pdf = (await app.ImportAsync(
                WorkflowType.GeneratePrintTiff, pdfSource, null, "qa", CancellationToken.None)).Value;
            OperationResult<SessionView> preparedPdf = await app.ExecuteAsync(
                pdf.Id, new WorkflowCommand.StartStep(StepKind.OriginalConfirmation),
                "qa", CancellationToken.None);
            preparedPdf.IsSuccess.ShouldBeTrue(
                preparedPdf.IsFailure ? preparedPdf.Failure.ToString() : "");
            SessionAggregate persistedPdf = (await provider.GetRequiredService<ISessionRepository>()
                .LoadAsync(pdf.Id, CancellationToken.None)).Value!;
            persistedPdf.Attempts.Single(attempt => attempt.Operation == OperationKind.PreparePdf)
                .Status.ShouldBe(AttemptStatus.Succeeded);
            (await provider.GetRequiredService<ISessionRepository>()
                .GetAutomationLockAsync(CancellationToken.None)).Value.IsHeld.ShouldBeFalse();
            (await authority.ObserveAsync(null, CancellationToken.None)).Status
                .ShouldBe(WorkstationAutomationLeaseStatus.Busy);
            photoshop.OpenCalls.ShouldBe(probeOpens);
            photoshop.CloseCalls.ShouldBe(probeCloses);

            WorkstationVerificationResult scoped = bootstrap.Verify(direct.Lease);
            scoped.Verified.ShouldBeTrue(
                "the bootstrap must forward the exact owner to the real scoped live reobservation: " +
                string.Join(" | ", scoped.Checks.Select(check =>
                    $"{check.Check}={check.Outcome}/{check.Observed}/{check.Explanation}")));
        }

        OperationResult<SessionView> succeeded = await app.ExecuteAsync(
            session, new WorkflowCommand.StartStep(StepKind.OriginalConfirmation),
            "qa", CancellationToken.None);
        succeeded.IsSuccess.ShouldBeTrue(succeeded.IsFailure ? succeeded.Failure.ToString() : "");
        appPhotoshop.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Three_successful_runs_restore_state_clean_the_probe_and_release_the_lock()
    {
        using WorkstationVerificationFixture fixture = new();
        FileWorkspace workspace = new(fixture.WorkspaceRoot);
        ScriptedMeitu meitu = new(fixture.MeituPath);
        ScriptedPhotoshop photoshop = new(fixture.PhotoshopPath, workspace);
        ScriptedRuntimeFacts facts = new(MatchingSettings(), []);
        RecordingLock automationLock = new();
        ProductionLiveWorkstationVerifier verifier = new(
            meitu, photoshop, facts, automationLock, workspace, fixture.Clock);

        for (int index = 0; index < 3; index++)
        {
            WorkstationLiveVerification result = await verifier.RunAsync(
                Requirements(fixture), CancellationToken.None);
            result.Checks.ShouldAllBe(check => check.Outcome == WorkstationCheckOutcome.Passed);
            result.Evidence.ShouldNotBeNull();
            ReadinessProbeDiagnostics probe = result.Probe.ShouldNotBeNull();
            probe.Stages.ShouldBe(new[]
            {
                ReadinessProbeStage.ProbeCreation, ReadinessProbeStage.ProbeCreated,
                ReadinessProbeStage.OpenGuard, ReadinessProbeStage.OpenRequested,
                ReadinessProbeStage.OpenConfirmed, ReadinessProbeStage.IdentityCheck,
                ReadinessProbeStage.IdentityConfirmed, ReadinessProbeStage.CloseGuard,
                ReadinessProbeStage.CloseRequested, ReadinessProbeStage.CloseConfirmed,
                ReadinessProbeStage.PriorStateCheck, ReadinessProbeStage.PriorStateRestored,
                ReadinessProbeStage.CleanupAttempted, ReadinessProbeStage.CleanupCompleted,
            });
            probe.CleanupOutcome.ShouldBe(ProbeCleanupOutcome.Succeeded);
            probe.LastAttemptedStage.ShouldBe(ReadinessProbeStage.CleanupAttempted);
            probe.LastConfirmedStage.ShouldBe(ReadinessProbeStage.CleanupCompleted);
            probe.PrimaryFailure.ShouldBeNull();
            probe.SecondaryFailures.ShouldBeEmpty();
        }

        meitu.EnsureCalls.ShouldBe(3);
        photoshop.EnsureCalls.ShouldBe(3);
        photoshop.OpenCalls.ShouldBe(3);
        photoshop.CloseCalls.ShouldBe(3);
        automationLock.AcquireCalls.ShouldBe(3);
        automationLock.ReleaseCalls.ShouldBe(3);
        automationLock.IsHeld.ShouldBeFalse();
        Directory.EnumerateFiles(fixture.WorkspaceRoot, "PF_ENV_PROBE_*", SearchOption.AllDirectories)
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task Unsaved_operator_work_blocks_the_probe_without_closing_any_document()
    {
        using WorkstationVerificationFixture fixture = new();
        FileWorkspace workspace = new(fixture.WorkspaceRoot);
        ScriptedPhotoshop photoshop = new(fixture.PhotoshopPath, workspace);
        ImmutableArray<PhotoshopRuntimeDocument> documents =
            [new("customer.psd", @"D:\Customer\customer.psd", IsSaved: false, IsActive: true)];
        RecordingLock automationLock = new();
        ProductionLiveWorkstationVerifier verifier = new(
            new ScriptedMeitu(fixture.MeituPath), photoshop,
            new ScriptedRuntimeFacts(MatchingSettings(), documents), automationLock,
            workspace, fixture.Clock);

        WorkstationLiveVerification result = await verifier.RunAsync(
            Requirements(fixture), CancellationToken.None);

        result.Checks.Single(check => check.Check == WorkstationVerificationCheck.PhotoshopSafeStartingState)
            .Outcome.ShouldBe(WorkstationCheckOutcome.Failed);
        result.Checks.Single(check => check.Check == WorkstationVerificationCheck.PhotoshopTestImageRoundTrip)
            .Outcome.ShouldBe(WorkstationCheckOutcome.Blocked);
        photoshop.OpenCalls.ShouldBe(0);
        photoshop.CloseCalls.ShouldBe(0);
        automationLock.ReleaseCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Colour_mismatch_is_reported_with_expected_and_current_and_never_repaired()
    {
        using WorkstationVerificationFixture fixture = new();
        FileWorkspace workspace = new(fixture.WorkspaceRoot);
        ScriptedPhotoshop photoshop = new(fixture.PhotoshopPath, workspace);
        PhotoshopColourSettingsContract wrong = MatchingSettings() with { CmykWorkingSpace = "Wrong CMYK" };
        ScriptedRuntimeFacts facts = new(wrong, []);
        RecordingLock automationLock = new();
        ProductionLiveWorkstationVerifier verifier = new(
            new ScriptedMeitu(fixture.MeituPath), photoshop, facts, automationLock,
            workspace, fixture.Clock);

        WorkstationLiveVerification result = await verifier.RunAsync(
            Requirements(fixture), CancellationToken.None);

        WorkstationCheckResult mismatch = result.Checks.Single(
            check => check.Check == WorkstationVerificationCheck.PhotoshopColourSettings);
        mismatch.Outcome.ShouldBe(WorkstationCheckOutcome.Failed);
        mismatch.Expected!.ShouldContain("Synthetic CMYK");
        mismatch.Observed!.ShouldContain("Wrong CMYK");
        facts.Writes.ShouldBe(0);
        photoshop.OpenCalls.ShouldBe(0);
        automationLock.ReleaseCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Close_identity_failure_fails_closed_retains_the_still_open_probe_and_releases_the_lock()
    {
        using WorkstationVerificationFixture fixture = new();
        FileWorkspace workspace = new(fixture.WorkspaceRoot);
        ScriptedPhotoshop photoshop = new(fixture.PhotoshopPath, workspace) { FailClose = true };
        RecordingLock automationLock = new();
        ProductionLiveWorkstationVerifier verifier = new(
            new ScriptedMeitu(fixture.MeituPath), photoshop,
            new ScriptedRuntimeFacts(MatchingSettings(), []), automationLock,
            workspace, fixture.Clock);

        WorkstationLiveVerification result = await verifier.RunAsync(
            Requirements(fixture), CancellationToken.None);

        result.Checks.Single(check => check.Check == WorkstationVerificationCheck.PhotoshopTestImageRoundTrip)
            .Outcome.ShouldBe(WorkstationCheckOutcome.Failed);
        Directory.EnumerateFiles(fixture.WorkspaceRoot, "PF_ENV_PROBE_*", SearchOption.AllDirectories)
            .ShouldHaveSingleItem("an unconfirmed-open document's backing file must not be deleted");
        automationLock.ReleaseCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Lock_contention_runs_no_application_step_and_does_not_release_another_owner()
    {
        using WorkstationVerificationFixture fixture = new();
        FileWorkspace workspace = new(fixture.WorkspaceRoot);
        ScriptedMeitu meitu = new(fixture.MeituPath);
        ScriptedPhotoshop photoshop = new(fixture.PhotoshopPath, workspace);
        RecordingLock automationLock = new() { Contended = true };
        ProductionLiveWorkstationVerifier verifier = new(
            meitu, photoshop, new ScriptedRuntimeFacts(MatchingSettings(), []), automationLock,
            workspace, fixture.Clock);

        WorkstationLiveVerification result = await verifier.RunAsync(
            Requirements(fixture), CancellationToken.None);

        result.Checks[0].Check.ShouldBe(WorkstationVerificationCheck.ExternalApplicationAutomationLock);
        result.Checks[0].Outcome.ShouldBe(WorkstationCheckOutcome.Failed);
        result.Checks.Skip(1).ShouldAllBe(check => check.Outcome == WorkstationCheckOutcome.Blocked);
        meitu.EnsureCalls.ShouldBe(0);
        photoshop.EnsureCalls.ShouldBe(0);
        automationLock.ReleaseCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Meitu_launch_failure_blocks_every_later_step_and_releases_the_lock()
    {
        using WorkstationVerificationFixture fixture = new();
        FileWorkspace workspace = new(fixture.WorkspaceRoot);
        ScriptedMeitu meitu = new(fixture.MeituPath)
        {
            Failure = OperationFailure.Create(FailureCode.MeituLaunchFailed, "timed out", true),
        };
        ScriptedPhotoshop photoshop = new(fixture.PhotoshopPath, workspace);
        RecordingLock automationLock = new();
        ProductionLiveWorkstationVerifier verifier = new(
            meitu, photoshop, new ScriptedRuntimeFacts(MatchingSettings(), []), automationLock,
            workspace, fixture.Clock);

        WorkstationLiveVerification result = await verifier.RunAsync(
            Requirements(fixture), CancellationToken.None);

        result.Checks.Single(check => check.Check == WorkstationVerificationCheck.MeituLaunchability)
            .FailureCode.ShouldBe(FailureCode.MeituLaunchFailed);
        result.Checks.Skip(2).ShouldAllBe(check => check.Outcome == WorkstationCheckOutcome.Blocked);
        photoshop.EnsureCalls.ShouldBe(0);
        automationLock.ReleaseCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Photoshop_launch_failure_and_probe_open_failure_are_distinct_terminal_results()
    {
        using WorkstationVerificationFixture fixture = new();
        FileWorkspace workspace = new(fixture.WorkspaceRoot);
        RecordingLock firstLock = new();
        ScriptedPhotoshop unavailable = new(fixture.PhotoshopPath, workspace)
        {
            EnsureFailure = OperationFailure.Create(FailureCode.PhotoshopLaunchFailed, "timed out", true),
        };
        ProductionLiveWorkstationVerifier first = new(
            new ScriptedMeitu(fixture.MeituPath), unavailable,
            new ScriptedRuntimeFacts(MatchingSettings(), []), firstLock, workspace, fixture.Clock);

        WorkstationLiveVerification launch = await first.RunAsync(
            Requirements(fixture), CancellationToken.None);
        launch.Checks.Single(check => check.Check == WorkstationVerificationCheck.PhotoshopLaunchability)
            .FailureCode.ShouldBe(FailureCode.PhotoshopLaunchFailed);
        launch.Checks.Single(check => check.Check == WorkstationVerificationCheck.PhotoshopColourSettings)
            .Outcome.ShouldBe(WorkstationCheckOutcome.Blocked);
        firstLock.ReleaseCalls.ShouldBe(1);

        RecordingLock secondLock = new();
        ScriptedPhotoshop cannotOpen = new(fixture.PhotoshopPath, workspace)
        {
            OpenFailure = OperationFailure.Create(FailureCode.PhotoshopOpenInputFailed, "open failed", true),
        };
        ProductionLiveWorkstationVerifier second = new(
            new ScriptedMeitu(fixture.MeituPath), cannotOpen,
            new ScriptedRuntimeFacts(MatchingSettings(), []), secondLock, workspace, fixture.Clock);
        WorkstationLiveVerification open = await second.RunAsync(
            Requirements(fixture), CancellationToken.None);
        open.Checks.Single(check => check.Check == WorkstationVerificationCheck.PhotoshopTestImageRoundTrip)
            .FailureCode.ShouldBe(FailureCode.PhotoshopOpenInputFailed);
        cannotOpen.CloseCalls.ShouldBe(0);
        secondLock.ReleaseCalls.ShouldBe(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_during_close_keeps_ownership_through_bounded_unwind(bool unwindFails)
    {
        using WorkstationVerificationFixture fixture = new();
        FileWorkspace workspace = new(fixture.WorkspaceRoot);
        using CancellationTokenSource cancellation = new();
        TaskCompletionSource<bool> unwinding = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> finishUnwind = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ScriptedPhotoshop photoshop = new(fixture.PhotoshopPath, workspace)
        {
            CancelFirstClose = cancellation,
            CloseFailure = unwindFails ? OperationFailure.Create(FailureCode.Timeout, "synthetic unwind timeout") : null,
            CloseUnwind = async () =>
            {
                unwinding.TrySetResult(true);
                await finishUnwind.Task;
            },
        };
        RecordingLock automationLock = new();
        ProductionLiveWorkstationVerifier verifier = new(
            new ScriptedMeitu(fixture.MeituPath), photoshop,
            new ScriptedRuntimeFacts(MatchingSettings(), []), automationLock,
            workspace, fixture.Clock);

        Task<WorkstationLiveVerification> running = verifier.RunAsync(Requirements(fixture), cancellation.Token);
        try
        {
            await unwinding.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancellation.IsCancellationRequested.ShouldBeTrue();
            automationLock.IsHeld.ShouldBeTrue();
            automationLock.ReleaseCalls.ShouldBe(0);
            (await automationLock.TryAcquireAsync(null, CancellationToken.None)).IsFailure.ShouldBeTrue();
        }
        finally
        {
            finishUnwind.TrySetResult(true);
        }
        WorkstationLiveVerification result = await running.WaitAsync(TimeSpan.FromSeconds(10));

        result.Checks.ShouldContain(check => check.FailureCode == FailureCode.Cancelled);
        photoshop.CloseCalls.ShouldBe(2);
        result.Evidence.ShouldBeNull();
        result.Probe!.PrimaryFailure!.Code.ShouldBe(FailureCode.Cancelled);
        result.Probe.Stages.ShouldNotContain(ReadinessProbeStage.PriorStateRestored,
            "the existing cancellation unwind closes the exact probe but does not reobserve prior state");
        if (unwindFails)
        {
            result.Probe.SecondaryFailures.ShouldHaveSingleItem().Code.ShouldBe(FailureCode.Timeout);
            result.Probe.CleanupOutcome.ShouldBe(ProbeCleanupOutcome.NotRun);
            result.Probe.Stages.ShouldNotContain(ReadinessProbeStage.CloseConfirmed);
            File.Exists(result.Probe.ManagedPath).ShouldBeTrue();
        }
        else
        {
            result.Probe.SecondaryFailures.ShouldBeEmpty();
            result.Probe.CleanupOutcome.ShouldBe(ProbeCleanupOutcome.Succeeded);
            File.Exists(result.Probe.ManagedPath).ShouldBeFalse();
        }
        automationLock.ReleaseCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Static_identity_failure_marks_live_steps_blocked_and_never_calls_live_automation()
    {
        using WorkstationVerificationFixture fixture = new();
        WorkstationVerificationFixture.Remove(fixture.PhotoshopPath);
        RecordingLiveVerifier live = new();
        ProductionWorkstationVerifier verifier = new(
            fixture.ManifestPath, WorkstationVerificationFixture.PresetId,
            WorkstationVerificationFixture.PresetVersion, fixture.ManifestSha256,
            fixture.WorkspaceRoot, fixture.Facts, fixture.Artifacts, fixture.Clock, live, revalidation: null);

        WorkstationVerificationResult result = await verifier.RunLiveChecksAsync(CancellationToken.None);

        result.Checks.Single(check => check.Check == WorkstationVerificationCheck.PhotoshopExecutable)
            .Outcome.ShouldBe(WorkstationCheckOutcome.Failed);
        result.Checks.Where(check => check.Kind is WorkstationCheckKind.Live or WorkstationCheckKind.Smoke)
            .ShouldAllBe(check => check.Outcome == WorkstationCheckOutcome.Blocked);
        live.RunCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Sqlite_live_lock_uses_owned_compare_and_swap_and_releases_only_its_token()
    {
        string directory = Path.Combine(Path.GetTempPath(), "printflow-env-lock-" + Guid.NewGuid().ToString("N"));
        string database = Path.Combine(directory, "lock.db");
        Directory.CreateDirectory(directory);
        try
        {
            SqliteConnectionFactory connections = new(database);
            using (SqliteConnection connection = connections.Open())
                MigrationRunner.Migrate(connection).IsSuccess.ShouldBeTrue();
            SqliteEnvironmentAutomationLock first = new(connections, 101, "TEST");
            SqliteEnvironmentAutomationLock second = new(connections, 202, "TEST");

            OperationResult<EnvironmentAutomationLease> acquired = await first.TryAcquireAsync(
                DateTimeOffset.UnixEpoch, CancellationToken.None);
            acquired.IsSuccess.ShouldBeTrue();
            (await second.TryAcquireAsync(DateTimeOffset.UnixEpoch, CancellationToken.None))
                .IsFailure.ShouldBeTrue();
            (await second.ReleaseAsync(new EnvironmentAutomationLease("not-the-owner"), CancellationToken.None))
                .IsFailure.ShouldBeTrue();
            (await first.ReadAsync(CancellationToken.None)).Value.Purpose
                .ShouldBe(AutomationLockPurpose.EnvironmentVerification);

            (await first.ReleaseAsync(acquired.Value, CancellationToken.None)).IsSuccess.ShouldBeTrue();
            (await first.ReadAsync(CancellationToken.None)).Value.IsHeld.ShouldBeFalse();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static WorkstationRequirements Requirements(WorkstationVerificationFixture fixture) =>
        PresetWorkstationRequirements.Read(
            fixture.ManifestPath, WorkstationVerificationFixture.PresetId,
            WorkstationVerificationFixture.PresetVersion, fixture.ManifestSha256).Value;

    private static ProductionWorkstationVerifier FullVerifier(
        WorkstationVerificationFixture fixture,
        IProductionLiveWorkstationVerifier live,
        bool omitRevalidation) => new(
        fixture.ManifestPath,
        WorkstationVerificationFixture.PresetId,
        WorkstationVerificationFixture.PresetVersion,
        fixture.ManifestSha256,
        fixture.WorkspaceRoot,
        fixture.Facts,
        fixture.Artifacts,
        fixture.Clock,
        live,
        revalidation: null,
        omitProductionRevalidation: omitRevalidation);

    private static PhotoshopColourSettingsContract MatchingSettings() =>
        new("Synthetic RGB", "Synthetic CMYK", "Synthetic Gray", "Synthetic Spot");

    private sealed class RecordingPsdProcessor(IWorkspace workspace) : IPhotoshopOutputProcessor
    {
        public string AdapterId => "recording-app-photoshop";
        public AdapterExecutionMode Mode => AdapterExecutionMode.Production;
        public int Calls { get; private set; }

        public Task<OperationResult<AdapterOutput>> GenerateAsync(
            PhotoshopRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<OperationResult<AdapterOutput>> PreparePsdAsync(
            PsdPreparationRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            File.WriteAllBytes(workspace.ResolveAbsolute(request.ExpectedOutput), SyntheticImages.Png(4, 3));
            return Task.FromResult(OperationResult.Ok(new AdapterOutput(
                request.ExpectedOutput, TimeSpan.Zero, "recorded app boundary")
            {
                PsdInspection = new PsdInspection(
                    4, 3, "RGB", 8, true, true,
                    [new("Red", "COMPONENT"), new("Green", "COMPONENT"), new("Blue", "COMPONENT")],
                    "recorded-composition"),
            }));
        }
    }

    private sealed class RecordingLiveVerifier : IProductionLiveWorkstationVerifier
    {
        public int RunCalls { get; private set; }

        public Task<WorkstationLiveVerification> RunAsync(
            WorkstationRequirements requirements, CancellationToken cancellationToken)
        {
            RunCalls++;
            throw new InvalidOperationException("Static failure must prevent live automation.");
        }

        public WorkstationLiveVerification Reobserve(
            WorkstationRequirements requirements, WorkstationLiveEvidence? evidence) =>
            new(ProductionLiveWorkstationVerifier.BlockedChecks("not run"), null);
    }

    private sealed class PausingReobserver(IProductionLiveWorkstationVerifier inner) : IProductionLiveWorkstationVerifier
    {
        private int _pause = 1;
        public TaskCompletionSource<bool> Observed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> ReturnObservation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<WorkstationLiveVerification> RunAsync(WorkstationRequirements requirements, CancellationToken cancellationToken) =>
            inner.RunAsync(requirements, cancellationToken);

        public WorkstationLiveVerification Reobserve(WorkstationRequirements requirements, WorkstationLiveEvidence? evidence)
        {
            WorkstationLiveVerification observed = inner.Reobserve(requirements, evidence);
            if (Interlocked.Exchange(ref _pause, 0) == 1)
            {
                Observed.TrySetResult(true);
                ReturnObservation.Task.GetAwaiter().GetResult();
            }
            return observed;
        }
    }

    private sealed class RecordingLock : IWorkstationAutomationLeaseManager
    {
        public string ResourceId => "test.live-verifier";
        public bool Contended { get; init; }
        public bool IsHeld { get; private set; }
        public int AcquireCalls { get; private set; }
        public int ReleaseCalls { get; private set; }
        public OperationFailure? ReleaseFailure { get; init; }
        public bool ThrowOnRelease { get; init; }
        public WorkstationAutomationLeaseStatus? ObservationOverride { get; set; }

        public Task<OperationResult<IWorkstationAutomationLease>> TryAcquireAsync(
            IWorkstationAutomationLease? enclosingLease, CancellationToken cancellationToken)
        {
            AcquireCalls++;
            if (Contended || IsHeld)
                return Task.FromResult(OperationResult.Fail<IWorkstationAutomationLease>(
                    FailureCode.AdapterUnavailable, "held"));
            IsHeld = true;
            return Task.FromResult(OperationResult.Ok<IWorkstationAutomationLease>(new RecordingLease(this)));
        }

        public Task<WorkstationAutomationLeaseObservation> ObserveAsync(
            IWorkstationAutomationLease? ownLease, CancellationToken cancellationToken) =>
            Task.FromResult(new WorkstationAutomationLeaseObservation(
                ObservationOverride ?? (ownLease is RecordingLease { IsActive: true }
                    ? WorkstationAutomationLeaseStatus.Owned
                    : IsHeld ? WorkstationAutomationLeaseStatus.Busy : WorkstationAutomationLeaseStatus.Free),
                ResourceId,
                IsHeld ? "held" : "free"));

        private sealed class RecordingLease(RecordingLock owner) : IWorkstationAutomationLease
        {
            public string ResourceId => owner.ResourceId;
            public string OwnerToken => "owned";
            public bool IsActive { get; private set; } = true;

            public Task<OperationResult<PrintFlow.Domain.Results.Unit>> ReleaseAsync(
                CancellationToken cancellationToken)
            {
                if (IsActive)
                {
                    owner.ReleaseCalls++;
                    if (owner.ThrowOnRelease) throw new IOException("synthetic release exception");
                    if (owner.ReleaseFailure is { } failure)
                        return Task.FromResult(OperationResult.Fail<PrintFlow.Domain.Results.Unit>(failure));
                    owner.IsHeld = false;
                    IsActive = false;
                }

                return Task.FromResult(OperationResult.Ok());
            }
        }
    }

    private sealed class ScriptedRuntimeFacts(
        PhotoshopColourSettingsContract settings,
        ImmutableArray<PhotoshopRuntimeDocument> documents) : IPhotoshopRuntimeFactReader
    {
        public int Writes => 0;
        public int ReadCalls { get; private set; }

        public OperationResult<PhotoshopRuntimeFacts> Read(string acceptedExecutablePath)
        {
            ReadCalls++;
            return OperationResult.Ok(new PhotoshopRuntimeFacts(settings, documents));
        }
    }

    private sealed class ScriptedPhotoshop(string executable, IWorkspace workspace)
        : IPhotoshopAutomationFoundation
    {
        private readonly PhotoshopTarget _target = new(
            new ExternalProcessRef(707, executable, DateTimeOffset.UnixEpoch),
            PhotoshopFakes.Window(owningProcessId: 707));

        public bool FailClose { get; set; }
        public OperationFailure? EnsureFailure { get; init; }
        public OperationFailure? OpenFailure { get; set; }
        public ReadinessProbeStage FailureOpenProgress { get; init; } = ReadinessProbeStage.OpenRequested;
        public OperationFailure? CloseFailure { get; init; }
        public OperationFailure? RestorationFailure { get; set; }
        public bool CorruptProbeOnClose { get; init; }
        public bool ThrowOnClose { get; init; }
        public Func<Task>? CloseUnwind { get; init; }
        public CancellationTokenSource? CancelFirstClose { get; init; }
        public int EnsureCalls { get; private set; }
        public int OpenCalls { get; private set; }
        public int CloseCalls { get; private set; }
        public int ReinspectCalls { get; private set; }

        public Task<OperationResult<PhotoshopReadiness>> EnsureReadyAsync(CancellationToken cancellationToken)
        {
            EnsureCalls++;
            return Task.FromResult(EnsureFailure is { } failure
                ? OperationResult.Fail<PhotoshopReadiness>(failure)
                : OperationResult.Ok(Readiness()));
        }

        public Task<OperationResult<PhotoshopReadiness>> ReinspectAsync(
            PhotoshopReadiness previous, CancellationToken cancellationToken)
        {
            ReinspectCalls++;
            return Task.FromResult(RestorationFailure is { } failure
                ? OperationResult.Fail<PhotoshopReadiness>(failure) : OperationResult.Ok(Readiness()));
        }

        public Task<OperationResult<PhotoshopOpenedDocument>> OpenManagedWorkingFileAsync(
            WorkspaceFileRef workingFile, Action<ReadinessProbeStage>? observe, CancellationToken cancellationToken)
        {
            ReadinessProbeStage until = OpenFailure is null ? ReadinessProbeStage.IdentityConfirmed : FailureOpenProgress;
            foreach (ReadinessProbeStage stage in new[] { ReadinessProbeStage.OpenGuard,
                         ReadinessProbeStage.OpenRequested, ReadinessProbeStage.OpenConfirmed,
                         ReadinessProbeStage.IdentityCheck, ReadinessProbeStage.IdentityConfirmed })
                if ((int)stage <= (int)until) observe?.Invoke(stage);
            return OpenManagedWorkingFileAsync(workingFile, cancellationToken);
        }

        public Task<OperationResult<PhotoshopOpenedDocument>> OpenManagedWorkingFileAsync(
            WorkspaceFileRef workingFile, CancellationToken cancellationToken)
        {
            OpenCalls++;
            if (OpenFailure is { } failure)
                return Task.FromResult(OperationResult.Fail<PhotoshopOpenedDocument>(failure));
            string path = workspace.ResolveAbsolute(workingFile);
            PhotoshopDocumentIdentity identity = new(
                Path.GetFileName(path), Path.GetDirectoryName(path)!, path, Path.GetFileName(path));
            return Task.FromResult(OperationResult.Ok(new PhotoshopOpenedDocument(
                _target, PhotoshopState(PhotoshopStartingState.KnownEditorWithExpectedDocument),
                identity, OtherDocumentsMayBeOpen: false)));
        }

        public Task<OperationResult<PhotoshopTarget>> CloseExactDocumentAsync(
            PhotoshopOpenedDocument opened, WorkspaceFileRef workingFile, CancellationToken cancellationToken)
        {
            CloseCalls++;
            if (ThrowOnClose) throw new IOException("synthetic close exception");
            if (CancelFirstClose is { } cancellation && CloseCalls == 1)
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            }
            return Task.FromResult(CloseFailure is { } failure
                ? OperationResult.Fail<PhotoshopTarget>(failure) : FailClose
                ? OperationResult.Fail<PhotoshopTarget>(
                    FailureCode.PhotoshopDocumentIdentityUnconfirmed, "identity moved")
                : OperationResult.Ok(_target));
        }

        public async Task<OperationResult<PhotoshopTarget>> CloseExactDocumentAsync(
            PhotoshopOpenedDocument opened, WorkspaceFileRef workingFile,
            Action<ReadinessProbeStage>? observe, CancellationToken cancellationToken)
        {
            observe?.Invoke(ReadinessProbeStage.CloseGuard);
            if (!FailClose) observe?.Invoke(ReadinessProbeStage.CloseRequested);
            OperationResult<PhotoshopTarget> result = await CloseExactDocumentAsync(opened, workingFile, cancellationToken);
            if (CloseCalls > 1 && CloseUnwind is { } unwind) await unwind();
            if (result.IsSuccess)
            {
                observe?.Invoke(ReadinessProbeStage.CloseConfirmed);
                if (CorruptProbeOnClose) File.WriteAllText(workspace.ResolveAbsolute(workingFile), "synthetic changed bytes");
            }
            return result;
        }

        private PhotoshopReadiness Readiness() =>
            new(_target, PhotoshopState(PhotoshopStartingState.KnownEditorNoDocument), WasLaunched: false);

        private static PhotoshopStateSnapshot PhotoshopState(PhotoshopStartingState state) =>
            new(state, [], new PhotoshopObservation(
                "Adobe Photoshop", [], [], true, null));
    }

    private sealed class ScriptedMeitu(string executable) : IMeituAutomationFoundation
    {
        private readonly MeituReadiness _readiness = new(
            new MeituTarget(
                new ExternalProcessRef(606, executable, DateTimeOffset.UnixEpoch),
                MeituFakes.Window(owningProcessId: 606)),
            new MeituStateSnapshot(MeituStartingState.KnownWelcome, [],
                new MeituObservation("Meitu", [], [], true, null)),
            WasLaunched: false);

        public int EnsureCalls { get; private set; }
        public OperationFailure? Failure { get; init; }

        public Task<OperationResult<MeituReadiness>> EnsureReadyAsync(CancellationToken cancellationToken)
        {
            EnsureCalls++;
            return Task.FromResult(Failure is { } failure
                ? OperationResult.Fail<MeituReadiness>(failure)
                : OperationResult.Ok(_readiness));
        }

        public Task<OperationResult<MeituReadiness>> ReinspectAsync(
            MeituReadiness previous, CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult.Ok(_readiness));

        public Task<OperationResult<MeituOpenedWorkingCopy>> OpenWorkingCopyAsync(
            WorkspaceFileRef workingCopy, IAutomationStopSignal stop, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<OperationResult<MeituEnhancementOutcome>> EnhanceAsync(
            MeituOpenedWorkingCopy opened, WorkspaceFileRef workingCopy, IAutomationStopSignal stop,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OperationResult<MeituBackgroundRemovalOutcome>> RemoveBackgroundAsync(
            MeituOpenedWorkingCopy opened, WorkspaceFileRef workingCopy, BackgroundRemovalDecision modeDecision,
            IAutomationStopSignal stop, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OperationResult<MeituExportedOutput>> ExportEnhancedResultAsync(
            MeituEnhancementOutcome enhancement, WorkspaceFileRef workingCopy, FileFacts workingCopyFactsBefore,
            WorkspaceFileRef output, IAutomationStopSignal stop, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<OperationResult<MeituExportedOutput>> ExportBackgroundRemovalResultAsync(
            MeituBackgroundRemovalOutcome backgroundRemoval, WorkspaceFileRef workingCopy,
            FileFacts workingCopyFactsBefore, WorkspaceFileRef output, IAutomationStopSignal stop,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OperationResult<FileFacts>> InspectManagedFileAsync(
            WorkspaceFileRef file, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OperationResult<bool>> DismissExportResultSurfaceAsync(
            MeituTarget target, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OperationResult<MeituTarget>> CloseDocumentAsync(
            MeituTarget target, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
