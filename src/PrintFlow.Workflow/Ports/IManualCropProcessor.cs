using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Trimming;

namespace PrintFlow.Workflow.Ports;

/// <summary>What the manual-crop processor is asked to do (Epic 11200 Part C2 §9).</summary>
/// <remarks>
/// The base rectangle and explicit manual adjustment are independent of automatic trim settings.
/// Only the requested outward expansion is clamped; an invalid base rectangle is refused.
/// </remarks>
/// <param name="Input">The per-attempt working copy to read. Never modified.</param>
/// <param name="ExpectedOutput">Where the cropped PNG must be written.</param>
/// <param name="Crop">
/// The operator's rectangle, in the <b>source image's own pixel coordinates</b> and in the
/// half-open convention <see cref="TrimBounds"/> defines. Viewport pixels never reach this
/// seam: the display-to-source mapping is the review surface's problem and is resolved before
/// a command is ever issued (Part C2 §6, §7).
/// </param>
/// <param name="Margin">The operator's manual expansion decision, defaulting to Tight.</param>
public sealed record ManualCropRequest(
    WorkspaceFileRef Input,
    WorkspaceFileRef ExpectedOutput,
    TrimBounds Crop, ManualCropMargin Margin = default);

/// <summary>The deterministic facts one manual crop established.</summary>
/// <remarks>
/// <see cref="AppliedBounds"/> is reported back rather than assumed to equal
/// <see cref="ManualCropRequest.Crop"/>, so a caller can assert that the pixels kept are the
/// pixels asked for instead of trusting that they were. There is no "manual crop required"
/// outcome here: unlike the alpha trim, this operation either crops
/// the rectangle it was given or fails.
/// </remarks>
public sealed record ManualCropResult(
    WorkspaceFileRef ProducedFile,
    TrimBounds AppliedBounds,
    int SourceWidth,
    int SourceHeight, ManualCropGeometry Geometry)
{
    /// <summary>Output width in pixels; by construction the applied rectangle's width.</summary>
    public int ResultWidth => AppliedBounds.Width;

    /// <summary>Output height in pixels; by construction the applied rectangle's height.</summary>
    public int ResultHeight => AppliedBounds.Height;
}

/// <summary>
/// Crops an image to a rectangle a human explicitly chose (Epic 11200 Part C2 §10).
/// </summary>
/// <remarks>
/// The counterpart to <see cref="ITrimProcessor"/>, and deliberately a separate seam rather
/// than a mode of it. The two differ in the one respect that matters: the alpha trim
/// <i>decides</i> the rectangle and must refuse when it cannot do so honestly, while this one
/// is <i>given</i> the rectangle and must not second-guess it. In particular an implementation
/// must <b>not</b> refuse a source that carries no alpha — that is precisely the case the
/// operator is here to solve (Part C2 §27).
/// <para>
/// What an implementation may not do is anything other than cut: no alpha analysis, no
/// segmentation, no colour correction, no resampling, no enhancement, and no call to Meitu or
/// Photoshop. It drives no external application, so it takes no automation lock and passes
/// through no <see cref="IEnvironmentGate"/>.
/// </para>
/// </remarks>
public interface IManualCropProcessor
{
    /// <summary>
    /// Identifies the implementation; written to every attempt, exactly as an adapter id is,
    /// so a Revision always records which code produced it.
    /// </summary>
    string ProcessorId { get; }

    /// <summary>
    /// Reads <see cref="ManualCropRequest.Input"/> and writes the cropped PNG to
    /// <see cref="ManualCropRequest.ExpectedOutput"/>.
    /// </summary>
    /// <remarks>
    /// Fails rather than clamps when the rectangle does not fit inside the decoded canvas. The
    /// caller is the one place that knows the source dimensions the operator was actually
    /// looking at, so silently shrinking a crop here would hide a mapping defect instead of
    /// surfacing it (Part C2 §23).
    /// </remarks>
    Task<OperationResult<ManualCropResult>> CropAsync(
        ManualCropRequest request, CancellationToken cancellationToken);
}
