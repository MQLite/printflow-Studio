using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Persistence;

/// <summary>
/// Persistence-boundary cases for Epic 11600 Part C. The repository decorator fails one real
/// SQLite commit while the workflow, workspace, lock, attempts, recovery, and retry paths stay
/// production-shaped.
/// </summary>
[Collection(SqliteCollection.Name)]
public sealed class ProductionFailurePersistenceTests
{
    [Fact]
    public async Task Attempt_start_commit_failure_reaches_no_adapter_and_leaves_no_lock_or_attempt()
    {
        using SessionServiceHarness harness = new();
        SessionId id = await ReadyForEnhancementAsync(harness, harness.CreateService(), "start-commit.png");

        FaultingRepository faulting = new(harness.Repository) { FailFromCommit = 1 };
        CountingMeitu counted = new(harness.FakeMeitu);
        ISessionService failing = harness.CreateServiceWithMeitu(counted, repository: faulting);

        OperationResult<SessionView> result = await failing.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.PersistenceError);
        counted.Calls.ShouldBe(0, "external work starts only after the opening transaction commits");

        SessionAggregate unchanged = await LoadAsync(harness, id);
        unchanged.Attempts.ShouldNotContain(a => a.Step == StepKind.Enhancement);
        unchanged.Steps.Single(s => s.Step == StepKind.Enhancement).State.ShouldBe(StepState.Waiting);
        (await harness.Repository.GetAutomationLockAsync(CancellationToken.None)).Value.IsHeld.ShouldBeFalse();

        // Remove only the synthetic persistence fault. The same waiting step must run normally.
        await Must(harness.CreateService().ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None));
        SessionAggregate recovered = await LoadAsync(harness, id);
        recovered.Attempts.Single(a => a.Step == StepKind.Enhancement).Status.ShouldBe(AttemptStatus.Succeeded);
        recovered.Steps.Single(s => s.Step == StepKind.Enhancement).State.ShouldBe(StepState.ReviewRequired);
    }

    [Fact]
    public async Task Success_commit_failure_is_recovered_as_interrupted_then_retried_on_a_new_attempt()
    {
        using SessionServiceHarness harness = new();
        SessionId id = await ReadyForEnhancementAsync(harness, harness.CreateService(), "success-commit.png");

        // Commit 1 records Running + lock. Commit 2 would atomically record the Revision,
        // Succeeded attempt, ReviewRequired step, and lock release; it is the injected failure.
        FaultingRepository faulting = new(harness.Repository) { FailFromCommit = 2 };
        CountingMeitu counted = new(harness.FakeMeitu);
        ISessionService failing = harness.CreateServiceWithMeitu(counted, repository: faulting);

        OperationResult<SessionView> result = await failing.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.PersistenceError);
        counted.Calls.ShouldBe(1);

        SessionAggregate crashed = await LoadAsync(harness, id);
        ProcessingAttempt first = crashed.Attempts.Single(a => a.Step == StepKind.Enhancement);
        first.Status.ShouldBe(AttemptStatus.Running);
        first.OutputRevisionId.ShouldBeNull();
        crashed.Revisions.ShouldNotContain(r => r.Operation == OperationKind.Enhance);
        crashed.Steps.Single(s => s.Step == StepKind.Enhancement).State.ShouldBe(StepState.Processing);
        (await harness.Repository.GetAutomationLockAsync(CancellationToken.None)).Value.IsHeld.ShouldBeTrue();

        StartupRecoveryReport recovery = Accept(await harness
            .CreateRecoveryService(new FakeProcessLiveness(ProcessLiveness.Dead))
            .RecoverAsync(CancellationToken.None));
        recovery.InterruptedAttemptCount.ShouldBe(1);
        recovery.ReleasedAutomationLock.ShouldBeTrue();

        ISessionService restarted = harness.CreateService();
        await Must(restarted.ExecuteAsync(
            id, new WorkflowCommand.Retry(StepKind.Enhancement), "tester", CancellationToken.None));
        await Must(restarted.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None));

        SessionAggregate after = await LoadAsync(harness, id);
        after.Attempts.Single(a => a.Id == first.Id).Status.ShouldBe(AttemptStatus.Interrupted);
        ProcessingAttempt retry = after.Attempts.Single(a => a.Step == StepKind.Enhancement && a.Id != first.Id);
        retry.Status.ShouldBe(AttemptStatus.Succeeded);
        retry.RetryOfAttemptId.ShouldBe(first.Id);
        retry.OutputRevisionId.ShouldNotBeNull();
        (await harness.Repository.GetAutomationLockAsync(CancellationToken.None)).Value.IsHeld.ShouldBeFalse();
    }

    [Fact]
    public async Task Failure_close_commit_failure_requires_dead_owner_recovery_then_a_clean_retry_succeeds()
    {
        using SessionServiceHarness harness = new();
        SessionId id = await ReadyForEnhancementAsync(harness, harness.CreateService(), "failure-commit.png");

        FaultingRepository faulting = new(harness.Repository) { FailFromCommit = 2 };
        ISessionService failing = harness.CreateServiceWithMeitu(
            new FailingMeitu(FailureCode.MeituTargetLost), repository: faulting);

        OperationResult<SessionView> result = await failing.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.PersistenceError,
            "the adapter failure could not be truthfully closed because its closing commit failed");

        SessionAggregate crashed = await LoadAsync(harness, id);
        ProcessingAttempt first = crashed.Attempts.Single(a => a.Step == StepKind.Enhancement);
        first.Status.ShouldBe(AttemptStatus.Running);
        crashed.Revisions.ShouldNotContain(r => r.Operation == OperationKind.Enhance);
        (await harness.Repository.GetAutomationLockAsync(CancellationToken.None)).Value.IsHeld.ShouldBeTrue();

        StartupRecoveryReport recovery = Accept(await harness
            .CreateRecoveryService(new FakeProcessLiveness(ProcessLiveness.Dead))
            .RecoverAsync(CancellationToken.None));
        recovery.InterruptedAttemptCount.ShouldBe(1);
        recovery.ReleasedAutomationLock.ShouldBeTrue();

        ISessionService restarted = harness.CreateService();
        await Must(restarted.ExecuteAsync(
            id, new WorkflowCommand.Retry(StepKind.Enhancement), "tester", CancellationToken.None));
        await Must(restarted.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None));

        SessionAggregate after = await LoadAsync(harness, id);
        after.Attempts.Single(a => a.Id == first.Id).Status.ShouldBe(AttemptStatus.Interrupted);
        after.Attempts.Single(a => a.Step == StepKind.Enhancement && a.Id != first.Id)
            .Status.ShouldBe(AttemptStatus.Succeeded);
        (await harness.Repository.GetAutomationLockAsync(CancellationToken.None)).Value.IsHeld.ShouldBeFalse();
    }

    [Fact]
    public async Task Adapter_thrown_cancellation_without_caller_cancellation_is_a_fault_not_an_operator_stop()
    {
        using SessionServiceHarness harness = new();
        ISessionService faulting = harness.CreateServiceWithMeitu(new UnexpectedCancellationMeitu());
        SessionId id = await ReadyForEnhancementAsync(harness, faulting, "unexpected-cancel.png");

        OperationResult<SessionView> result = await faulting.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.AdapterUnavailable);
        result.Failure.MessageKey.ShouldBe("Failure_OperationFaulted");

        SessionAggregate failed = await LoadAsync(harness, id);
        ProcessingAttempt first = failed.Attempts.Single(a => a.Step == StepKind.Enhancement);
        first.Status.ShouldBe(AttemptStatus.Failed);
        first.Failure!.Code.ShouldBe(FailureCode.AdapterUnavailable);
        first.Failure.Context["faultType"].ShouldBe(typeof(OperationCanceledException).FullName);
        first.OutputRevisionId.ShouldBeNull();
        (await harness.Repository.GetAutomationLockAsync(CancellationToken.None)).Value.IsHeld.ShouldBeFalse();

        ISessionService restored = harness.CreateService();
        await Must(restored.ExecuteAsync(
            id, new WorkflowCommand.Retry(StepKind.Enhancement), "tester", CancellationToken.None));
        await Must(restored.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None));
        (await LoadAsync(harness, id)).Steps.Single(s => s.Step == StepKind.Enhancement)
            .State.ShouldBe(StepState.ReviewRequired);
    }

    [Fact]
    public async Task Photoshop_adapter_exception_is_persisted_releases_the_lock_and_a_retry_succeeds()
    {
        using SessionServiceHarness harness = new();
        ISessionService faulting = harness.CreateServiceWithPhotoshop(new ThrowingPhotoshop());
        SessionId id = await ReadyForPhotoshopAsync(harness, faulting, "photoshop-fault.png");

        OperationResult<SessionView> result = await faulting.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.AdapterUnavailable);
        SessionAggregate failed = await LoadAsync(harness, id);
        ProcessingAttempt first = failed.Attempts.Single(a => a.Step == StepKind.PhotoshopOutput);
        first.Status.ShouldBe(AttemptStatus.Failed);
        first.Failure!.Context["faultType"].ShouldBe(typeof(InvalidOperationException).FullName);
        failed.Revisions.ShouldNotContain(r => r.Operation == OperationKind.PhotoshopOutput);
        (await harness.Repository.GetAutomationLockAsync(CancellationToken.None)).Value.IsHeld.ShouldBeFalse();

        ISessionService restored = harness.CreateService();
        await Must(restored.ExecuteAsync(
            id, new WorkflowCommand.Retry(StepKind.PhotoshopOutput), "tester", CancellationToken.None));
        await Must(restored.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", CancellationToken.None));
        (await LoadAsync(harness, id)).Steps.Single(s => s.Step == StepKind.PhotoshopOutput)
            .State.ShouldBe(StepState.ReviewRequired);
    }

    private static async Task<SessionId> ReadyForEnhancementAsync(
        SessionServiceHarness harness, ISessionService service, string fileName)
    {
        SessionId id = Accept(await service.ImportAsync(
            WorkflowType.PrepareAsset, harness.WriteSourcePng(fileName), "recovery", "tester",
            CancellationToken.None)).Id;
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None));
        return id;
    }

    private static async Task<SessionId> ReadyForPhotoshopAsync(
        SessionServiceHarness harness, ISessionService service, string fileName)
    {
        SessionId id = Accept(await service.ImportAsync(
            WorkflowType.GeneratePrintTiff, harness.WriteBorderedSourcePng(fileName), "recovery", "tester",
            CancellationToken.None)).Id;
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.ConfirmOriginal("finished design"), "tester", CancellationToken.None));
        await Must(service.ExecuteAsync(
            id,
            new WorkflowCommand.SetPrintDimensions(
                PrintDimensions.FromMillimetres(200, 150, SizePreset.Custom)),
            "tester",
            CancellationToken.None));
        await Must(service.ExecuteAsync(
            id,
            new WorkflowCommand.SelectWhiteUnderbaseBranch(
                WhiteUnderbaseBranch.W1_1px, "ordinary design"),
            "tester",
            CancellationToken.None));
        return id;
    }

    private static async Task<SessionAggregate> LoadAsync(SessionServiceHarness harness, SessionId id) =>
        (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;

    private static SessionView Accept(OperationResult<SessionView> result)
    {
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : string.Empty);
        return result.Value;
    }

    private static StartupRecoveryReport Accept(OperationResult<StartupRecoveryReport> result)
    {
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : string.Empty);
        return result.Value;
    }

    private static async Task Must(Task<OperationResult<SessionView>> task) => Accept(await task);

    private sealed class CountingMeitu(IMeituProcessor inner) : IMeituProcessor
    {
        public int Calls { get; private set; }
        public string AdapterId => inner.AdapterId;
        public AdapterExecutionMode Mode => inner.Mode;

        public Task<OperationResult<AdapterOutput>> ProcessAsync(
            MeituRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            return inner.ProcessAsync(request, cancellationToken);
        }
    }

    private sealed class FailingMeitu(FailureCode code) : IMeituProcessor
    {
        public string AdapterId => "part-c-failing-meitu";
        public AdapterExecutionMode Mode => AdapterExecutionMode.Fake;

        public Task<OperationResult<AdapterOutput>> ProcessAsync(
            MeituRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult.Fail<AdapterOutput>(code, "Injected adapter failure."));
    }

    private sealed class UnexpectedCancellationMeitu : IMeituProcessor
    {
        public string AdapterId => "part-c-unexpected-cancellation-meitu";
        public AdapterExecutionMode Mode => AdapterExecutionMode.Fake;

        public Task<OperationResult<AdapterOutput>> ProcessAsync(
            MeituRequest request, CancellationToken cancellationToken) =>
            throw new OperationCanceledException("Injected cancellation without caller request.");
    }

    private sealed class ThrowingPhotoshop : IPhotoshopOutputProcessor
    {
        public string AdapterId => "part-c-throwing-photoshop";
        public AdapterExecutionMode Mode => AdapterExecutionMode.Fake;

        public Task<OperationResult<AdapterOutput>> GenerateAsync(
            PhotoshopRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Injected Photoshop adapter exception.");
    }
}
