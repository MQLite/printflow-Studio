using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Adapters.Fake;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Persistence;

/// <summary>
/// The full deterministic fake-adapter scenario set driven through the real
/// <see cref="SessionService"/> pipeline (Epic 11100 Part 3A §3–§4): every scripted outcome
/// still goes through the real workspace and file inspector, never fabricating a Revision.
/// </summary>
[Collection(SqliteCollection.Name)]
public sealed class FakeAdapterScenarioTests
{
    [Fact]
    public async Task Explicit_adapter_failure_leaves_the_step_Failed_with_no_output_Revision_and_releases_the_lock()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        string source = harness.WriteSourcePng();

        SessionId id = (await service.ImportAsync(
            WorkflowType.PrepareAsset, source, "art", "tester", CancellationToken.None)).Value.Id;
        await Must(service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None));

        harness.FakeMeitu.SetScenario(FakeAdapterScenario.FailWith(FailureCode.OutputValidationFailed));

        OperationResult<SessionView> started = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None);

        started.IsFailure.ShouldBeTrue();
        started.Failure.Code.ShouldBe(FailureCode.OutputValidationFailed);

        SessionAggregate reloaded = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        reloaded.Steps.Single(s => s.Step == StepKind.Enhancement).State.ShouldBe(StepState.Failed);
        reloaded.Revisions.ShouldNotContain(r => r.Operation == OperationKind.Enhance);

        ProcessingAttempt attempt = reloaded.Attempts.Single(a => a.Step == StepKind.Enhancement);
        attempt.Status.ShouldBe(AttemptStatus.Failed);
        attempt.OutputRevisionId.ShouldBeNull();
        attempt.Failure!.Code.ShouldBe(FailureCode.OutputValidationFailed);

        AutomationLockState lockState = (await harness.Repository.GetAutomationLockAsync(CancellationToken.None)).Value;
        lockState.IsHeld.ShouldBeFalse();
    }

    [Fact]
    public async Task Missing_output_fails_with_OutputMissing_and_leaves_retry_available()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        string source = harness.WriteSourcePng();

        SessionId id = (await service.ImportAsync(
            WorkflowType.PrepareAsset, source, "art", "tester", CancellationToken.None)).Value.Id;
        await Must(service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None));

        harness.FakeMeitu.SetScenario(FakeAdapterScenario.ProduceMissingFile);

        OperationResult<SessionView> started = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None);

        started.IsFailure.ShouldBeTrue();
        started.Failure.Code.ShouldBe(FailureCode.OutputMissing);

        SessionAggregate reloaded = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        reloaded.Steps.Single(s => s.Step == StepKind.Enhancement).State.ShouldBe(StepState.Failed);
        reloaded.Revisions.ShouldNotContain(r => r.Operation == OperationKind.Enhance);

        // Retry remains legal: Failed accepts Retry per the step transition table.
        OperationResult<SessionView> retried = await service.ExecuteAsync(
            id, new WorkflowCommand.Retry(StepKind.Enhancement), "tester", CancellationToken.None);
        retried.IsSuccess.ShouldBeTrue(retried.IsFailure ? retried.Failure.ToString() : "");
        retried.Value.Steps.Single(s => s.Step == StepKind.Enhancement).State.ShouldBe(StepState.Waiting);
    }

    [Fact]
    public async Task Unreadable_output_fails_with_OutputUnreadable_and_creates_no_Revision()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        string source = harness.WriteSourcePng();

        SessionId id = (await service.ImportAsync(
            WorkflowType.PrepareAsset, source, "art", "tester", CancellationToken.None)).Value.Id;
        await Must(service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None));

        harness.FakeMeitu.SetScenario(FakeAdapterScenario.ProduceUnreadableFile);

        OperationResult<SessionView> started = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None);

        started.IsFailure.ShouldBeTrue();
        started.Failure.Code.ShouldBe(FailureCode.OutputUnreadable);

        SessionAggregate reloaded = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        reloaded.Revisions.ShouldNotContain(r => r.Operation == OperationKind.Enhance);
    }

    [Fact]
    public async Task Timeout_scenario_fails_with_FailureCode_Timeout_and_releases_the_lock()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        string source = harness.WriteSourcePng();

        SessionId id = (await service.ImportAsync(
            WorkflowType.PrepareAsset, source, "art", "tester", CancellationToken.None)).Value.Id;
        await Must(service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None));

        harness.FakeMeitu.SetScenario(FakeAdapterScenario.Timeout);

        OperationResult<SessionView> started = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None);

        started.IsFailure.ShouldBeTrue();
        started.Failure.Code.ShouldBe(FailureCode.Timeout);

        SessionAggregate reloaded = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        reloaded.Revisions.ShouldNotContain(r => r.Operation == OperationKind.Enhance);

        AutomationLockState lockState = (await harness.Repository.GetAutomationLockAsync(CancellationToken.None)).Value;
        lockState.IsHeld.ShouldBeFalse();
    }

    [Fact]
    public async Task HangUntilCancelled_is_safely_cancelled_with_no_Revision_and_the_lock_released()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        string source = harness.WriteSourcePng();

        SessionId id = (await service.ImportAsync(
            WorkflowType.PrepareAsset, source, "art", "tester", CancellationToken.None)).Value.Id;
        await Must(service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None));

        harness.FakeMeitu.SetScenario(FakeAdapterScenario.HangUntilCancelled);

        using CancellationTokenSource cts = new();
        Task<OperationResult<SessionView>> running = service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", cts.Token);

        // Deterministic synchronisation, not a sleep: only cancel once the fake adapter has
        // genuinely entered its wait, so "Running -> cancellation" is a real sequence, not a race.
        await harness.FakeMeitu.HangStarted;

        // The Attempt was committed as Running before the adapter was ever called (plan §38).
        SessionAggregate whileRunning = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        whileRunning.Attempts.Single(a => a.Step == StepKind.Enhancement).Status.ShouldBe(AttemptStatus.Running);

        cts.Cancel();
        OperationResult<SessionView> result = await running;

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.Cancelled);

        SessionAggregate reloaded = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        reloaded.Steps.Single(s => s.Step == StepKind.Enhancement).State.ShouldBe(StepState.Failed);
        reloaded.Revisions.ShouldNotContain(r => r.Operation == OperationKind.Enhance);
        reloaded.Attempts.Single(a => a.Step == StepKind.Enhancement).OutputRevisionId.ShouldBeNull();

        AutomationLockState lockState = (await harness.Repository.GetAutomationLockAsync(CancellationToken.None)).Value;
        lockState.IsHeld.ShouldBeFalse();
    }

    private static async Task Must(Task<OperationResult<SessionView>> resultTask)
    {
        OperationResult<SessionView> result = await resultTask;
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : "");
    }
}
