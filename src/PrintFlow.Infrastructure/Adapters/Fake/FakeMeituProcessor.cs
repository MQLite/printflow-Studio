using PrintFlow.Domain.Results;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Adapters.Fake;

/// <summary>
/// A deterministic local stand-in for Meitu screen automation (Epic 11100 §41; plan §15.2).
/// </summary>
/// <remarks>
/// By default it writes a <b>real</b> file — a copy of the input, re-saved to the expected
/// output path — so the full pipeline downstream of the adapter call (existence, stable size,
/// streaming read, hash, metadata extraction, Revision creation, review binding) is genuinely
/// exercised rather than stubbed. <see cref="SetScenario"/> scripts the full deterministic
/// scenario set (Epic 11100 Part 3A §3: fail, timeout, missing/unreadable output, cancellation)
/// for tests that need to drive the failure and retry paths; every scenario still runs through
/// the real infrastructure, never fabricating a Revision directly. It is identifiable:
/// <see cref="AdapterId"/> is written to every attempt so a fake result can never be mistaken
/// for a production one.
/// </remarks>
public sealed class FakeMeituProcessor : IMeituProcessor
{
    private readonly IWorkspace _workspace;
    private FakeAdapterScenario _scenario = FakeAdapterScenario.Succeed;
    private TaskCompletionSource? _hangStarted;

    public FakeMeituProcessor(IWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        _workspace = workspace;
    }

    /// <inheritdoc />
    public string AdapterId => "fake-meitu-v1";

    /// <inheritdoc />
    public AdapterExecutionMode Mode => AdapterExecutionMode.Fake;

    /// <summary>Scripts the behaviour of the next call. Stays in effect until changed again.</summary>
    public void SetScenario(FakeAdapterScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        _scenario = scenario;
        _hangStarted = scenario.Kind == FakeAdapterScenarioKind.HangUntilCancelled
            ? new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
            : null;
    }

    /// <summary>
    /// Completes once a <see cref="FakeAdapterScenarioKind.HangUntilCancelled"/> call has
    /// actually begun waiting, so a test can cancel deterministically without a sleep.
    /// </summary>
    public Task HangStarted => _hangStarted?.Task ?? Task.CompletedTask;

    /// <inheritdoc />
    public Task<OperationResult<AdapterOutput>> ProcessAsync(MeituRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The request's input and expected-output references already point at the same
        // working-copy file (SessionService creates one working copy per attempt); the fake
        // "processes" it in place, which is sufficient to exercise validation genuinely without
        // claiming any real enhancement or background-removal behaviour.
        return FakeAdapterExecution.RunAsync(
            _scenario,
            request.ExpectedOutput,
            _workspace,
            _hangStarted,
            () => OperationResult.Ok(new AdapterOutput(request.ExpectedOutput, TimeSpan.Zero, "fake")),
            cancellationToken);
    }
}
