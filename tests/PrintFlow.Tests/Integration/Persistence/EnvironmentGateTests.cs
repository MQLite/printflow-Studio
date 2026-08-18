using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Gate;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Persistence;

/// <summary>
/// Epic 11100 Part 3A §7–§8: the <see cref="FoundationEnvironmentGate"/> foundation seam.
/// </summary>
public sealed class EnvironmentGateTests
{
    [Fact]
    public void Foundation_gate_allows_Fake_and_rejects_Production_with_EnvironmentNotVerified()
    {
        FoundationEnvironmentGate gate = new();

        gate.Verify(AdapterExecutionMode.Fake).IsSuccess.ShouldBeTrue();

        OperationResult<PrintFlow.Domain.Results.Unit> productionResult = gate.Verify(AdapterExecutionMode.Production);
        productionResult.IsFailure.ShouldBeTrue();
        productionResult.Failure.Code.ShouldBe(FailureCode.EnvironmentNotVerified);
    }

    [Fact]
    public async Task SessionService_refuses_a_Production_mode_adapter_before_it_is_ever_invoked()
    {
        using SessionServiceHarness harness = new();
        NeverCallMeMeituProcessor productionMeitu = new();
        ISessionService service = harness.CreateServiceWithMeitu(productionMeitu);
        string source = harness.WriteSourcePng();

        SessionId id = (await service.ImportAsync(
            WorkflowType.PrepareAsset, source, "art", "tester", CancellationToken.None)).Value.Id;
        await service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None);

        OperationResult<SessionView> started = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None);

        started.IsFailure.ShouldBeTrue();
        started.Failure.Code.ShouldBe(FailureCode.EnvironmentNotVerified);
        productionMeitu.WasCalled.ShouldBeFalse();

        // Refused before the "Running" attempt was ever committed and before the automation
        // lock was ever acquired — the gate sits ahead of both (plan §8).
        SessionAggregate reloaded = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        reloaded.Attempts.ShouldNotContain(a => a.Step == StepKind.Enhancement);
        reloaded.Steps.Single(s => s.Step == StepKind.Enhancement).State.ShouldBe(StepState.Waiting);

        AutomationLockState lockState = (await harness.Repository.GetAutomationLockAsync(CancellationToken.None)).Value;
        lockState.IsHeld.ShouldBeFalse();
    }

    /// <summary>
    /// A minimal stand-in for a future production Meitu adapter (Epic 11300): it declares
    /// <see cref="AdapterExecutionMode.Production"/> and would fail the test outright if the
    /// gate ever let a call through to it.
    /// </summary>
    private sealed class NeverCallMeMeituProcessor : IMeituProcessor
    {
        public bool WasCalled { get; private set; }

        public string AdapterId => "would-be-production-meitu";

        public AdapterExecutionMode Mode => AdapterExecutionMode.Production;

        public Task<OperationResult<AdapterOutput>> ProcessAsync(MeituRequest request, CancellationToken cancellationToken)
        {
            WasCalled = true;
            throw new InvalidOperationException(
                "The environment gate should have refused this step before the adapter was ever called.");
        }
    }
}
