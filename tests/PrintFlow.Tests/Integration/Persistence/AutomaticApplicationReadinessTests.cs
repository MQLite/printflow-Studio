using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Gate;
using PrintFlow.Infrastructure.Verification;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Tests.Integration.Ui;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Persistence;

[Collection(SqliteCollection.Name)]
public sealed class AutomaticApplicationReadinessTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Starting_external_processing_prepares_closed_applications_before_admission(bool photoshop)
    {
        using SessionServiceHarness harness = new();
        ColdWorkstation verifier = new();
        VerifiedEnvironmentGate gate = new(verifier);
        ProductionProcessor processor = new();
        ISessionService service = photoshop
            ? harness.CreateServiceWithPhotoshop(processor, gate)
            : harness.CreateServiceWithMeitu(processor, environmentGate: gate);
        SessionId id = await ImportAsync(harness, service, photoshop);

        OperationResult<SessionView> result = await service.ExecuteAsync(id,
            new WorkflowCommand.StartStep(photoshop ? StepKind.OriginalConfirmation : StepKind.Enhancement),
            "tester", CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.AdapterUnavailable,
            "the prepared workstation must reach the processor, which returns this sentinel refusal");
        verifier.LiveRuns.ShouldBe(1);
        processor.Calls.ShouldBe(1);
        (await harness.Repository.GetAutomationLockAsync(CancellationToken.None)).Value.IsHeld.ShouldBeFalse();
    }

    [Fact]
    public async Task Failed_automatic_preparation_keeps_the_attempt_unstarted()
    {
        using SessionServiceHarness harness = new();
        ColdWorkstation verifier = new() { CanPrepare = false };
        ProductionProcessor processor = new();
        ISessionService service = harness.CreateServiceWithMeitu(processor,
            environmentGate: new VerifiedEnvironmentGate(verifier));
        SessionId id = await ImportAsync(harness, service, photoshop: false);

        OperationResult<SessionView> result = await service.ExecuteAsync(id,
            new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        verifier.LiveRuns.ShouldBe(1);
        processor.Calls.ShouldBe(0);
        SessionAggregate stored = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        stored.Attempts.ShouldNotContain(attempt => attempt.Step == StepKind.Enhancement);
        stored.Steps.Single(step => step.Step == StepKind.Enhancement).State.ShouldBe(StepState.Waiting);
    }

    [Fact]
    public async Task An_already_ready_workstation_does_not_repeat_the_live_probe()
    {
        using SessionServiceHarness harness = new();
        ColdWorkstation verifier = new() { Ready = true };
        ProductionProcessor processor = new();
        ISessionService service = harness.CreateServiceWithMeitu(processor,
            environmentGate: new VerifiedEnvironmentGate(verifier));
        SessionId id = await ImportAsync(harness, service, photoshop: false);

        await service.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.Enhancement),
            "tester", CancellationToken.None);

        processor.Calls.ShouldBe(1);
        verifier.LiveRuns.ShouldBe(0);
    }

    [Theory]
    [InlineData(WorkstationCheckKind.Immutable)]
    [InlineData(WorkstationCheckKind.Dynamic)]
    public async Task Failed_workstation_prerequisites_never_launch_applications(WorkstationCheckKind kind)
    {
        ColdWorkstation verifier = new() { PrerequisiteFailure = kind };
        VerifiedEnvironmentGate gate = new(verifier);

        (await gate.PrepareAsync(AdapterExecutionMode.Production, CancellationToken.None))
            .IsFailure.ShouldBeTrue();

        verifier.LiveRuns.ShouldBe(0);
        verifier.Ready.ShouldBeFalse();
    }

    [Fact]
    public async Task Losing_readiness_after_preparation_is_refused_before_the_attempt_starts()
    {
        using SessionServiceHarness harness = new();
        ColdWorkstation verifier = new() { LoseReadinessBeforeAdmission = true };
        ProductionProcessor processor = new();
        ISessionService service = harness.CreateServiceWithMeitu(processor,
            environmentGate: new VerifiedEnvironmentGate(verifier));
        SessionId id = await ImportAsync(harness, service, photoshop: false);

        OperationResult<SessionView> result = await service.ExecuteAsync(id,
            new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.EnvironmentNotVerified);
        verifier.LiveRuns.ShouldBe(1);
        processor.Calls.ShouldBe(0);
        SessionAggregate stored = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        stored.Attempts.ShouldNotContain(attempt => attempt.Step == StepKind.Enhancement);
        (await harness.Repository.GetAutomationLockAsync(CancellationToken.None)).Value.IsHeld.ShouldBeFalse();
    }

    [Fact]
    public void Passive_readings_leave_applications_closed()
    {
        ColdWorkstation verifier = new();
        VerifiedEnvironmentGate gate = new(verifier);

        gate.Read().Verified.ShouldBeFalse();
        gate.Verify(AdapterExecutionMode.Production).IsFailure.ShouldBeTrue();

        verifier.LiveRuns.ShouldBe(0);
        verifier.Ready.ShouldBeFalse();
    }

    [Fact]
    public async Task Cancelled_preparation_does_not_launch_applications()
    {
        ColdWorkstation verifier = new();
        VerifiedEnvironmentGate gate = new(verifier);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            gate.PrepareAsync(AdapterExecutionMode.Production, cancellation.Token));

        verifier.LiveRuns.ShouldBe(0);
    }

    private static async Task<SessionId> ImportAsync(
        SessionServiceHarness harness, ISessionService service, bool photoshop)
    {
        string source = photoshop
            ? harness.Workspace.CreateSourceFile("source.psd", PsdInputPreparationTests.RgbCompositePsd())
            : harness.WriteSourcePng();
        OperationResult<SessionView> imported = await service.ImportAsync(
            photoshop ? WorkflowType.GeneratePrintTiff : WorkflowType.PrepareAsset,
            source, "art", "tester", CancellationToken.None);
        imported.IsSuccess.ShouldBeTrue(imported.IsFailure ? imported.Failure.ToString() : "");
        if (!photoshop)
            (await service.ExecuteAsync(imported.Value.Id, new WorkflowCommand.ConfirmOriginal(),
                "tester", CancellationToken.None)).IsSuccess.ShouldBeTrue();
        return imported.Value.Id;
    }

    private sealed class ColdWorkstation : IProductionWorkstationVerifier
    {
        public bool Ready { get; set; }
        public bool CanPrepare { get; init; } = true;
        public WorkstationCheckKind? PrerequisiteFailure { get; init; }
        public bool LoseReadinessBeforeAdmission { get; init; }
        public int LiveRuns { get; private set; }

        public WorkstationVerificationResult Verify() => WorkstationVerificationResult.From(
            new ProductionPresetRef("test", "1", Sha256.Parse(new string('A', 64))),
            [
                WorkstationCheckResult.Passed(WorkstationVerificationCheck.PresetIntegrity,
                    WorkstationCheckKind.Immutable, "accepted", "accepted"),
                PrerequisiteFailure is { } kind
                    ? WorkstationCheckResult.Failed(
                        kind == WorkstationCheckKind.Immutable ? WorkstationVerificationCheck.MeituExecutable
                            : WorkstationVerificationCheck.DisplayConfiguration,
                        kind, FailureCode.EnvironmentNotVerified, "accepted", "mismatch", "Prerequisite failed")
                    : WorkstationCheckResult.Passed(WorkstationVerificationCheck.DisplayConfiguration,
                        WorkstationCheckKind.Dynamic, "accepted", "accepted"),
                Ready
                    ? WorkstationCheckResult.Passed(WorkstationVerificationCheck.MeituLaunchability,
                        WorkstationCheckKind.Live, "ready", "Applications are ready")
                    : WorkstationCheckResult.Blocked(WorkstationVerificationCheck.MeituLaunchability,
                        WorkstationCheckKind.Live, "ready", "Applications have not been started"),
            ], DateTimeOffset.UnixEpoch);

        public Task<WorkstationVerificationResult> RunLiveChecksAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LiveRuns++;
            Ready = CanPrepare;
            WorkstationVerificationResult result = Verify();
            if (LoseReadinessBeforeAdmission) Ready = false;
            return Task.FromResult(result);
        }
    }

    private sealed class ProductionProcessor : IMeituProcessor, IPhotoshopOutputProcessor
    {
        public string AdapterId => "production-readiness-test";
        public AdapterExecutionMode Mode => AdapterExecutionMode.Production;
        public int Calls { get; private set; }
        public Task<OperationResult<AdapterOutput>> ProcessAsync(MeituRequest request, CancellationToken cancellationToken) => Refuse();
        public Task<OperationResult<AdapterOutput>> GenerateAsync(PhotoshopRequest request, CancellationToken cancellationToken) => Refuse();
        public Task<OperationResult<AdapterOutput>> PreparePsdAsync(PsdPreparationRequest request, CancellationToken cancellationToken) => Refuse();

        private Task<OperationResult<AdapterOutput>> Refuse()
        {
            Calls++;
            return Task.FromResult(OperationResult.Fail<AdapterOutput>(
                FailureCode.AdapterUnavailable, "Processor reached after workstation admission"));
        }
    }
}
