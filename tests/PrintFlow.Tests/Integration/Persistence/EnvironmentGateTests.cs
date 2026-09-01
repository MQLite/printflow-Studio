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
/// The <see cref="IEnvironmentGate"/> seam as the workflow meets it (Epic 11100 Part 3A §7–§8).
/// </summary>
/// <remarks>
/// What this file asserted before Epic 11500 Part B was that the foundation gate refused
/// Production unconditionally. Part B replaced that gate with one that refuses on evidence, so
/// the workflow-facing behaviour under test is unchanged — an unverified workstation still never
/// reaches a production adapter — while the reason moved into
/// <c>VerifiedEnvironmentGateTests</c>.
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class EnvironmentGateTests
{
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
    /// The workflow reaches production authorisation through <see cref="IEnvironmentGate"/> and
    /// through nothing else (Epic 11500 Part B §3).
    /// </summary>
    /// <remarks>
    /// The dependency direction stated as behaviour rather than as a source scan: substituting
    /// the gate is sufficient to change what the workflow is allowed to do, which could not be
    /// true if the session service or the adapter consulted the workstation on its own.
    /// </remarks>
    [Fact]
    public async Task The_gate_is_the_only_thing_the_workflow_asks_about_production_permission()
    {
        using SessionServiceHarness harness = new();
        UnverifiedEnvironmentGate gate = new();
        NeverCallMeMeituProcessor productionMeitu = new();
        ISessionService service = harness.CreateServiceWithMeitu(productionMeitu, environmentGate: gate);
        string source = harness.WriteSourcePng();

        SessionId id = (await service.ImportAsync(
            WorkflowType.PrepareAsset, source, "art", "tester", CancellationToken.None)).Value.Id;
        await service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None);
        await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None);

        gate.Requests.ShouldContain(AdapterExecutionMode.Production);
        productionMeitu.WasCalled.ShouldBeFalse();
    }

    /// <summary>
    /// A minimal stand-in for a production Meitu adapter: it declares
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
