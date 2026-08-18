using System.IO;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Adapters.Fake;

/// <summary>
/// A deterministic local stand-in for Photoshop TIFF production (Epic 11100 §41; plan §15.2).
/// </summary>
/// <remarks>
/// By default it writes a real file at the reserved output path so the validation pipeline
/// downstream is genuinely exercised. <see cref="SetScenario"/> scripts the full deterministic
/// scenario set (Epic 11100 Part 3A §3) for tests that need to drive the failure and retry
/// paths. It makes no claim about CMYK conversion, the white-underbase channel, or any other
/// production-specific TIFF fact — those checks, and the real Photoshop automation that would
/// produce a file worth checking them on, are Epic 11400. Nothing here launches, focuses,
/// reads, or scripts Photoshop.
/// </remarks>
public sealed class FakePhotoshopOutputProcessor : IPhotoshopOutputProcessor
{
    private readonly IWorkspace _workspace;
    private FakeAdapterScenario _scenario = FakeAdapterScenario.Succeed;
    private TaskCompletionSource? _hangStarted;

    public FakePhotoshopOutputProcessor(IWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        _workspace = workspace;
    }

    /// <inheritdoc />
    public string AdapterId => "fake-photoshop-v1";

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
    public Task<OperationResult<AdapterOutput>> GenerateAsync(PhotoshopRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return FakeAdapterExecution.RunAsync(
            _scenario,
            request.ExpectedOutput,
            _workspace,
            _hangStarted,
            () => Succeed(request),
            cancellationToken);
    }

    private OperationResult<AdapterOutput> Succeed(PhotoshopRequest request)
    {
        string inputAbsolute = _workspace.ResolveAbsolute(request.ApprovedInput);
        string outputAbsolute = _workspace.ResolveAbsolute(request.ExpectedOutput);

        try
        {
            File.Copy(inputAbsolute, outputAbsolute, overwrite: true);
        }
        catch (IOException ex)
        {
            return OperationResult.Fail<AdapterOutput>(
                FailureCode.OutputMissing, $"Fake Photoshop adapter could not write '{outputAbsolute}': {ex.Message}");
        }

        return OperationResult.Ok(new AdapterOutput(request.ExpectedOutput, TimeSpan.Zero, "fake"));
    }
}
