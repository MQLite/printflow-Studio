using System.Globalization;
using System.IO;
using PrintFlow.Domain.Outputs;
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
            () => Task.FromResult(Succeed(request)),
            InertAutomationStopSignal.Instance,
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

        return OperationResult.Ok(new AdapterOutput(request.ExpectedOutput, TimeSpan.Zero, Notes(request)));
    }

    /// <summary>
    /// States the preparation this synthetic output was produced under
    /// (Epic 11400 Part B1A.2A §18; Part B1A.2D §27).
    /// </summary>
    /// <remarks>
    /// Derived entirely from <see cref="PhotoshopRequest.Preparation"/> — the source-bound
    /// geometry the workflow validated and the attempt row already recorded — so the note is
    /// deterministic for a given preparation and a Fake run demonstrably received one. Nothing
    /// here recalculates a limiting edge, resolves a preset, decides whether an override was
    /// allowed, reinterprets a legacy pair, or reads
    /// <c>PhotoshopRequest.Dimensions.PixelWidth</c>.
    /// <para>
    /// Both accepted forms are reported in their own vocabulary rather than flattened into one,
    /// because an audit line that called a target-edge enlargement a maximum-bound shrink would be
    /// worse than no line at all.
    /// </para>
    /// <para>
    /// It stays prefixed "fake" and says <c>projected</c> rather than naming a result: no
    /// Photoshop ran, nothing was resampled, and the file beside this note is a copy of the input
    /// rather than a resized image. In particular a <c>PreserveDetails</c> policy is reported as
    /// the policy the run <i>would</i> have used — Preserve Details did not execute, and claiming
    /// it had would put a fabricated production fact in the one place a Revision is created from
    /// (§20, §27).
    /// </para>
    /// </remarks>
    private static string Notes(PhotoshopRequest request)
    {
        PhotoshopPreparation preparation = request.Preparation;
        string edge = preparation.PhotoshopEdgeValueMm is { } value
            ? $"{preparation.PhotoshopEdge} at {value.ToString("0.##", CultureInfo.InvariantCulture)} mm"
            : preparation.PhotoshopEdge.ToString();

        string detail = preparation switch
        {
            FitWithinBoundsPreparation bounds =>
                $"{bounds.Plan.Mode} ({bounds.ResizePolicy}), edge {edge}",

            TargetEdgePreparation target =>
                $"{target.Plan.Projection.Direction} ({target.ResizePolicy}), requested " +
                $"{target.Plan.Projection.SelectedTargetEdge} at " +
                $"{target.Plan.Projection.RequestedMillimetres.ToString("0.####", CultureInfo.InvariantCulture)} mm " +
                $"resolved to {edge}, scale {target.Plan.Projection.ProjectedScale.Numerator}/" +
                $"{target.Plan.Projection.ProjectedScale.Denominator}, preset limit exceeded " +
                $"{target.Plan.PresetLimitExceeded}, source capacity exceeded " +
                $"{target.Plan.SourceCapacityExceeded}, enlargement authority " +
                $"{(target.Plan.RequiresEnlargementAuthority ? "required" : "not required")} and " +
                $"{(target.IsAuthorisedEnlargement ? "present" : "absent")}",

            _ => edge,
        };

        return $"fake; {preparation.Semantics} {detail}, projected " +
            $"{preparation.ProjectedPixelWidth}x{preparation.ProjectedPixelHeight} px @ " +
            $"{preparation.ProductionDpi} ppi; no Photoshop ran and nothing was resampled.";
    }
}
