using System.Globalization;
using System.IO;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Adapters.Photoshop;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Adapters.Fake;

/// <summary>
/// A deterministic local stand-in for Photoshop TIFF production (Epic 11100 §41; plan §15.2).
/// </summary>
/// <remarks>
/// By default it writes a real file at the reserved output path so the validation pipeline
/// downstream is genuinely exercised. <see cref="SetScenario"/> scripts the full deterministic
/// scenario set (Epic 11100 Part 3A §3) for tests that need to drive the failure and retry
/// paths. Nothing here launches, focuses, reads, or scripts Photoshop.
/// <para>
/// <see cref="SetTiffOutput"/> is the second, independent half of the vocabulary (SCRUM-11097):
/// it decides what the file <i>is</i> when a call writes one. Under any value but the default the
/// adapter writes a genuine production TIFF with one named fact deliberately wrong and then
/// submits it for validation, so a structural refusal is the Product detecting a real fault rather
/// than the fake announcing one. The default output class remains a copy of the approved input,
/// which makes no claim about CMYK conversion or the white-underbase channel at all.
/// </para>
/// <para>
/// The absolute half of that validation is <see cref="ProductionTiffInspector"/> — the same type
/// the production TIFF save path uses, unchanged. The relative half,
/// <see cref="ProductionTiffPreparationMatch"/>, is reached only from here: production compares
/// its saved bytes to the Photoshop document it prepared, and that document has already been
/// compared to the preparation, so it needs no direct comparison. See that type's remarks.
/// </para>
/// </remarks>
public sealed class FakePhotoshopOutputProcessor : IPhotoshopOutputProcessor
{
    private readonly IWorkspace _workspace;
    private readonly ProductionTiffInspector _inspector = new();
    private FakeAdapterScenario _scenario = FakeAdapterScenario.Succeed;
    private FakePhotoshopTiffOutput _output = FakePhotoshopTiffOutput.CopyApprovedInput;
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
    /// Scripts which class of TIFF a successful call writes (SCRUM-11097). Stays in effect until
    /// changed again.
    /// </summary>
    /// <remarks>
    /// Independent of <see cref="SetScenario"/>: that one decides whether a call succeeds, fails,
    /// times out, hangs or leaves nothing behind, and this one decides what the file <i>is</i>
    /// when a call does write one. A scenario that produces no file at all ignores this
    /// completely, which is correct — an export failure has no output class.
    /// </remarks>
    public void SetTiffOutput(FakePhotoshopTiffOutput output) => _output = output;

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
        string outputAbsolute = _workspace.ResolveAbsolute(request.ExpectedOutput);

        if (_output != FakePhotoshopTiffOutput.CopyApprovedInput)
        {
            return WriteProductionTiff(request, outputAbsolute);
        }

        string inputAbsolute = _workspace.ResolveAbsolute(request.ApprovedInput);

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
    /// Writes the scripted class of production TIFF and then submits it to the Product's own
    /// validation, exactly as the production adapter submits Photoshop's (SCRUM-11097).
    /// </summary>
    /// <remarks>
    /// The order is the whole point. The bytes are written first, with one named fact deliberately
    /// wrong, and only then does anything look at them — so a refusal here is
    /// <see cref="ProductionTiffInspector"/> and <see cref="ProductionTiffPreparationMatch"/>
    /// rejecting a real file, not the fake reporting a verdict it was handed. A scenario that
    /// claimed "wrong dimensions" while writing a correct file would prove nothing about whether
    /// PrintFlow can detect wrong dimensions, which is the only thing SCRUM-11129 is asking.
    /// <para>
    /// <see cref="ProductionTiffInspector"/> is the type the production TIFF save path uses, and
    /// the result leaves through the application-facing port Production also implements: nothing
    /// here is a second validation engine, a test-only inspector, or a bypass of the ordinary
    /// adapter result. The workflow layer sees an ordinary <see cref="OperationResult{T}"/>
    /// failure and cannot tell which adapter produced it.
    /// </para>
    /// <para>
    /// It stays deterministic and offline: bytes on a local filesystem, no process, no COM, no
    /// UI automation, and no dependency on the verified workstation.
    /// </para>
    /// </remarks>
    private OperationResult<AdapterOutput> WriteProductionTiff(PhotoshopRequest request, string outputAbsolute)
    {
        PhotoshopPreparation preparation = request.Preparation;

        try
        {
            FakeProductionTiff.WriteAt(outputAbsolute, OptionsFor(_output, preparation));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Fail<AdapterOutput>(
                FailureCode.OutputMissing,
                $"Fake Photoshop adapter could not write '{outputAbsolute}': {ex.Message}");
        }

        OperationResult<ProductionTiffFacts> inspected = _inspector.Inspect(outputAbsolute);
        if (inspected.IsFailure)
        {
            return OperationResult.Fail<AdapterOutput>(inspected.Failure);
        }

        if (ProductionTiffPreparationMatch.Check(inspected.Value, preparation) is { } mismatch)
        {
            return OperationResult.Fail<AdapterOutput>(mismatch);
        }

        ProductionTiffFacts facts = inspected.Value;
        return OperationResult.Ok(new AdapterOutput(
            request.ExpectedOutput,
            TimeSpan.Zero,
            $"{Notes(request)} Fake production TIFF ({_output}); validated " +
            $"{facts.PixelWidth}x{facts.PixelHeight} px @ " +
            $"{facts.XResolutionDpi.ToString("0.##", CultureInfo.InvariantCulture)}x" +
            $"{facts.YResolutionDpi.ToString("0.##", CultureInfo.InvariantCulture)} dpi, " +
            $"{facts.SamplesPerPixel} samples, compression {facts.Compression}, spot " +
            $"{(facts.ExtraChannelNames.IsDefaultOrEmpty ? "(none)" : string.Join("+", facts.ExtraChannelNames))}."));
    }

    /// <summary>
    /// The accepted layout at this preparation's geometry, with exactly one fact changed.
    /// </summary>
    /// <remarks>
    /// Every case starts from the same projected width and height, so a structural fault is never
    /// accidentally also a geometry fault and a geometry fault is never accidentally also a
    /// structural one. A test that asserted a specific refusal would otherwise be able to pass on
    /// the strength of a second, unintended deviation.
    /// <para>
    /// <see cref="FakePhotoshopTiffOutput.WrongPixelWidth"/> and
    /// <see cref="FakePhotoshopTiffOutput.WrongPixelHeight"/> deviate by a single pixel on
    /// purpose. A file that is half the expected size would be caught by almost any check; one
    /// pixel is the smallest disagreement that still has to be caught, and it is the shape a real
    /// rounding or resample defect takes.
    /// </para>
    /// <para>
    /// They deviate <i>upward</i>, and that matters. Subtracting a pixel needs a clamp at 1, and a
    /// clamp means a preparation projecting a one-pixel edge would silently be handed the accepted
    /// geometry — a scenario named "wrong dimensions" that produced a correct file and returned
    /// success. Adding a pixel is always representable, so the scenario can never invert.
    /// </para>
    /// </remarks>
    private static FakeProductionTiffOptions OptionsFor(
        FakePhotoshopTiffOutput output, PhotoshopPreparation preparation)
    {
        FakeProductionTiffOptions accepted = new(
            PixelWidth: preparation.ProjectedPixelWidth,
            PixelHeight: preparation.ProjectedPixelHeight,
            CmykSamples: [0x10, 0x40, 0x80, 0x20],
            FifthSampleEverywhere: 0);

        return output switch
        {
            FakePhotoshopTiffOutput.ValidTiff => accepted,
            FakePhotoshopTiffOutput.MissingWhiteChannel => accepted with { SamplesPerPixel = 4 },
            FakePhotoshopTiffOutput.EmptyWhiteChannel => accepted with { FifthSampleEverywhere = byte.MaxValue },
            FakePhotoshopTiffOutput.WrongColourMode => accepted with { PhotometricInterpretation = 2 },
            FakePhotoshopTiffOutput.WrongPixelWidth => accepted with
            {
                PixelWidth = preparation.ProjectedPixelWidth + 1,
            },
            FakePhotoshopTiffOutput.WrongPixelHeight => accepted with
            {
                PixelHeight = preparation.ProjectedPixelHeight + 1,
            },
            FakePhotoshopTiffOutput.IncorrectDpi => accepted with { Dpi = 150 },
            FakePhotoshopTiffOutput.IncorrectMetadata => accepted with { Compression = 5 },
            FakePhotoshopTiffOutput.IncorrectChannelMetadata => accepted with { ChannelName = "White" },
            _ => throw new InvalidOperationException($"Unhandled fake Photoshop TIFF output '{output}'."),
        };
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
