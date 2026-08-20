using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Trimming;

namespace PrintFlow.Workflow.Ports;

/// <summary>What the trim processor is asked to do.</summary>
/// <remarks>
/// Deliberately four plain values. No <c>ProcessingSession</c>, no <c>WorkflowSnapshot</c>,
/// no repository handle crosses this seam: everything the pixel work needs is the file to
/// read, the file to write and the shape of the crop. That is what keeps the implementation
/// substitutable and lets the whole algorithm be tested without a database.
/// </remarks>
/// <param name="Input">The per-attempt working copy to read. Never modified.</param>
/// <param name="ExpectedOutput">Where the trimmed PNG must be written when one is produced.</param>
/// <param name="Margin">How much canvas to keep outside the alpha content.</param>
public sealed record TrimRequest(
    WorkspaceFileRef Input,
    WorkspaceFileRef ExpectedOutput,
    TrimMargin Margin)
{
    /// <summary>The mode the operator asked for, carried by <see cref="Margin"/> itself.</summary>
    public TrimMode Mode => Margin.Mode;
}

/// <summary>
/// The deterministic facts one trim attempt established.
/// </summary>
/// <remarks>
/// Everything here is measurable and reproducible from the input bytes, which is what makes
/// it worth persisting and asserting on. No image bytes, no preview, no elapsed time, no UI
/// state: those belong to the image-review surface (Epic 11200 Part C), not to the seam.
///
/// The nullable properties are honest rather than lazy. A <see cref="TrimOutcome.ManualCropRequired"/>
/// result has no bounds and no file because none could be established; a file whose container
/// could not be decoded at all does not even have dimensions to report.
/// </remarks>
public sealed record TrimResult
{
    private TrimResult(
        TrimOutcome outcome,
        TrimBounds? contentBounds,
        TrimBounds? appliedBounds,
        int? originalWidth,
        int? originalHeight,
        WorkspaceFileRef? producedFile,
        string? manualCropReason)
    {
        Outcome = outcome;
        ContentBounds = contentBounds;
        AppliedBounds = appliedBounds;
        OriginalWidth = originalWidth;
        OriginalHeight = originalHeight;
        ProducedFile = producedFile;
        ManualCropReason = manualCropReason;
    }

    /// <summary>What the attempt established.</summary>
    public TrimOutcome Outcome { get; }

    /// <summary>The smallest rectangle containing every <c>alpha &gt; 0</c> pixel, before margins.</summary>
    public TrimBounds? ContentBounds { get; }

    /// <summary>The rectangle actually cropped to: <see cref="ContentBounds"/> grown by the margin and clamped to the canvas.</summary>
    public TrimBounds? AppliedBounds { get; }

    /// <summary>Source canvas width in pixels.</summary>
    public int? OriginalWidth { get; }

    /// <summary>Source canvas height in pixels.</summary>
    public int? OriginalHeight { get; }

    /// <summary>Output width in pixels; by construction the applied rectangle's width.</summary>
    public int? ResultWidth => AppliedBounds?.Width;

    /// <summary>Output height in pixels; by construction the applied rectangle's height.</summary>
    public int? ResultHeight => AppliedBounds?.Height;

    /// <summary>The written PNG, or <c>null</c> when no automatic trim was possible.</summary>
    public WorkspaceFileRef? ProducedFile { get; }

    /// <summary>English detail for the log explaining why a human must crop. Never shown raw to the operator.</summary>
    public string? ManualCropReason { get; }

    /// <summary>
    /// Records a trim that produced a file, deriving the outcome from the geometry.
    /// </summary>
    /// <remarks>
    /// The <see cref="TrimOutcome.NoChangeRequired"/> rule — "the applied rectangle is the
    /// whole source canvas" — is decided here and only here, so no caller can produce a
    /// result claiming a crop that did not happen (Epic 11200 Part B §15).
    /// </remarks>
    public static TrimResult Produced(
        TrimBounds contentBounds,
        TrimBounds appliedBounds,
        int originalWidth,
        int originalHeight,
        WorkspaceFileRef producedFile)
    {
        TrimOutcome outcome = appliedBounds.CoversCanvas(originalWidth, originalHeight)
            ? TrimOutcome.NoChangeRequired
            : TrimOutcome.Trimmed;

        return new TrimResult(
            outcome, contentBounds, appliedBounds, originalWidth, originalHeight, producedFile, null);
    }

    /// <summary>
    /// Records that no automatic trim is honest for this file, and why.
    /// </summary>
    /// <remarks>
    /// Not a failure of the processor: the processor did its job and the truthful answer is
    /// "a human has to decide". Turning that into a step outcome is the orchestrator's call.
    /// </remarks>
    public static TrimResult ManualCropRequired(string reason, int? originalWidth = null, int? originalHeight = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new TrimResult(
            TrimOutcome.ManualCropRequired, null, null, originalWidth, originalHeight, null, reason);
    }
}

/// <summary>
/// Deterministic in-process canvas trimming (MVP design §6.2; Epic 11200 Part B).
/// </summary>
/// <remarks>
/// The seam exposes no decoder, pixel format, stride or codec: those are the implementation's
/// business and would move if the imaging stack ever did. What it does guarantee is the part
/// that is safety-critical and must not vary by implementation — an image whose alpha cannot
/// be established is reported as <see cref="TrimOutcome.ManualCropRequired"/> and never
/// guessed at from colour. No implementation of this port may call Meitu, call Photoshop, run
/// a segmentation model, or treat white or black as "background".
///
/// This is <b>not</b> an adapter in the <c>AdapterKind.Meitu</c>/<c>AdapterKind.Photoshop</c>
/// sense. It drives no external application, so it takes no automation lock and passes through
/// no <see cref="IEnvironmentGate"/>; <c>StepDefinition.IsAdapterBacked</c> stays false for Trim.
/// </remarks>
public interface ITrimProcessor
{
    /// <summary>
    /// Identifies the implementation; written to every attempt, exactly as an adapter id is,
    /// so a Revision always records which algorithm produced it.
    /// </summary>
    string ProcessorId { get; }

    /// <summary>
    /// Reads <see cref="TrimRequest.Input"/>, and writes the cropped PNG to
    /// <see cref="TrimRequest.ExpectedOutput"/> unless the outcome is
    /// <see cref="TrimOutcome.ManualCropRequired"/>.
    /// </summary>
    /// <remarks>
    /// A failed <see cref="OperationResult{T}"/> means the attempt could not be carried out at
    /// all (the input is missing, the output could not be written). "This image has no alpha"
    /// is not that: it is a successful result carrying
    /// <see cref="TrimOutcome.ManualCropRequired"/>.
    /// </remarks>
    Task<OperationResult<TrimResult>> TrimAsync(TrimRequest request, CancellationToken cancellationToken);
}
