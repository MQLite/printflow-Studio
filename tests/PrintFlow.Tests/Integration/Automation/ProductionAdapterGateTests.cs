using System.IO;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Adapters.Meitu;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;
using FileWorkspace = PrintFlow.Infrastructure.Workspace.FileWorkspace;

namespace PrintFlow.Tests.Integration.Automation;

/// <summary>
/// The real production Meitu adapter, behind the real environment gate, in the real session
/// flow (Epic 11300 Part A §22, §26).
/// </summary>
/// <remarks>
/// Epic 11100 already proved this with a hand-written stand-in. Repeating it with the actual
/// adapter is the point of doing it again: the stand-in could not have launched anything even if
/// the gate had let it through, whereas this one can. What is being asserted is that it is never
/// asked to.
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class ProductionAdapterGateTests
{
    [Fact]
    public async Task The_real_production_Meitu_adapter_is_refused_before_it_is_ever_invoked()
    {
        using SessionServiceHarness harness = new();
        using TempWorkspace adapterWorkspace = new();

        CountingMeituProcessor counted = new(BuildProductionAdapter(adapterWorkspace));
        ISessionService service = harness.CreateServiceWithMeitu(counted);
        string source = harness.WriteSourcePng();

        SessionId id = (await service.ImportAsync(
            WorkflowType.PrepareAsset, source, "art", "tester", CancellationToken.None)).Value.Id;
        await service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None);

        OperationResult<SessionView> started = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None);

        started.IsFailure.ShouldBeTrue();
        started.Failure.Code.ShouldBe(FailureCode.EnvironmentNotVerified);
        counted.Calls.ShouldBe(0);

        // Nothing was launched, nothing was locked, and no attempt row exists to explain away.
        AutomationLockState lockState =
            (await harness.Repository.GetAutomationLockAsync(CancellationToken.None)).Value;
        lockState.IsHeld.ShouldBeFalse();

        SessionAggregate reloaded = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        reloaded.Attempts.ShouldNotContain(a => a.Step == StepKind.Enhancement);
    }

    /// <summary>
    /// Even reached directly, the adapter cannot produce a Revision-worthy success.
    /// </summary>
    /// <remarks>
    /// The gate is the outer control; this is the inner one. If a future composition mistake put
    /// the Part A adapter into a live graph, the worst outcome is a structured failure — never a
    /// Revision recorded for an image nothing processed (§24).
    /// </remarks>
    [Fact]
    public async Task Calling_the_adapter_directly_still_cannot_report_success()
    {
        using TempWorkspace adapterWorkspace = new();
        IMeituProcessor adapter = BuildProductionAdapter(adapterWorkspace);

        WorkspaceFileRef file = WorkspaceFileRef.Create("Sessions/S/Working/a.png", WorkspaceArea.Working);
        OperationResult<AdapterOutput> result = await adapter.ProcessAsync(
            new MeituRequest(file, MeituOperation.Enhance, WorkspaceDirRef.Create("Sessions/S"), file),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
    }

    /// <summary>
    /// Builds the real adapter over a fake OS, so nothing is launched by a unit test run.
    /// </summary>
    private static ProductionMeituProcessor BuildProductionAdapter(TempWorkspace workspace)
    {
        StubMeituBaselineProvider baselines = new(MeituFakes.Baseline());
        FakeWindowLocator locator = new();
        MeituAutomationOptions options = new() { PollInterval = TimeSpan.FromMilliseconds(5) };

        GuardedMeituUiDriver driver = new(
            locator, new RecordingUiElementProvider(), new RecordingInputSink(),
            new RecordingEvidenceSink(), baselines, options, TimeProvider.System);

        return new ProductionMeituProcessor(
            baselines, locator, driver, new FileWorkspace(workspace.Root), options, TimeProvider.System);
    }

    /// <summary>Counts calls that reach the adapter, so "never invoked" is a checkable fact.</summary>
    private sealed class CountingMeituProcessor : IMeituProcessor
    {
        private readonly IMeituProcessor _inner;

        public CountingMeituProcessor(IMeituProcessor inner) => _inner = inner;

        public int Calls { get; private set; }

        public string AdapterId => _inner.AdapterId;

        public AdapterExecutionMode Mode => _inner.Mode;

        public Task<OperationResult<AdapterOutput>> ProcessAsync(
            MeituRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            return _inner.ProcessAsync(request, cancellationToken);
        }
    }
}
