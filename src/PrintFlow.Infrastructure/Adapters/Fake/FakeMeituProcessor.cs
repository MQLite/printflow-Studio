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

        // The expected output is a new file beside the working copy, so the fake produces one:
        // a byte-for-byte copy of its input at the path the workflow named. It claims no
        // enhancement or background-removal behaviour — the bytes are identical and the adapter
        // id says which adapter made them — but producing a real, separate file is what keeps
        // everything downstream genuine, including the two things the production adapter now
        // depends on: that the result is a distinct file, and that the working copy it came from
        // is still there afterwards (Epic 11300 Part B2B §19).
        return FakeAdapterExecution.RunAsync(
            _scenario,
            request.ExpectedOutput,
            _workspace,
            _hangStarted,
            () => SucceedAsync(request, cancellationToken),
            cancellationToken);
    }

    private async Task<OperationResult<AdapterOutput>> SucceedAsync(
        MeituRequest request, CancellationToken cancellationToken)
    {
        OperationResult<Unit> written = await _workspace
            .WriteReservedAsync(request.ExpectedOutput, request.Input, cancellationToken)
            .ConfigureAwait(false);

        return written.IsFailure
            ? OperationResult.Fail<AdapterOutput>(written.Failure)
            : OperationResult.Ok(new AdapterOutput(request.ExpectedOutput, TimeSpan.Zero, "fake"));
    }
}
