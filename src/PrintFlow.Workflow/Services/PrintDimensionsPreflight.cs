using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Trimming;

namespace PrintFlow.Workflow.Services;

/// <summary>How the exact Revision being sized acquired its current canvas.</summary>
public enum GraphicBoundsKind
{
    /// <summary>A deterministic alpha trim recorded both detected content and its applied crop.</summary>
    AutomaticTrim,

    /// <summary>An operator crop recorded both the selected rectangle and its applied crop.</summary>
    ManualCrop,

    /// <summary>The operator explicitly retained the full approved canvas.</summary>
    FullOriginalCanvas,

    /// <summary>The Revision was cropped by an older attempt which recorded no geometry.</summary>
    GeometryUnavailable,

    /// <summary>No crop or trim produced the Revision being sized.</summary>
    NoRelevantGeometry,
}

/// <summary>
/// What decided the physical size of one preflight, copied from the plan it projects (SCRUM-11150).
/// </summary>
/// <remarks>
/// A read-out of the existing plan's own decision, never a second calculation: a maximum-bound
/// plan's <see cref="PrintPreparationMode"/> and <see cref="LimitingEdge"/>, or a target-edge
/// plan's selected edge. Nothing here compares dimensions.
/// </remarks>
public enum PrintSizeGovernor
{
    /// <summary>
    /// Maximum bounds the source already fits (<see cref="PrintPreparationMode.ResolutionOnly"/>):
    /// its pixels are kept, it is not enlarged to fill the bounds, and no edge decides.
    /// </summary>
    WithinLimits,

    /// <summary>
    /// Maximum bounds the source exceeds: <see cref="PrintDimensionsPreflight.GoverningEdge"/> is
    /// the edge the plan limits to its bound, and the other edge follows proportionally.
    /// </summary>
    LimitReached,

    /// <summary>
    /// A custom target edge: <see cref="PrintDimensionsPreflight.SelectedTargetEdge"/> was set to
    /// the requested millimetres, resolved to <see cref="PrintDimensionsPreflight.GoverningEdge"/>.
    /// </summary>
    SelectedEdge,
}

/// <summary>
/// Read-only sizing facts for the exact approved visual Revision and one physical preparation.
/// </summary>
/// <remarks>
/// This is a projection of an existing preparation plan and persisted attempt geometry. It is
/// never persisted itself. In particular, <see cref="EffectiveSourcePpiX"/> and
/// <see cref="EffectiveSourcePpiY"/> describe the source pixels available at the proposed
/// physical size; <see cref="ProductionOutputPpi"/> remains the separate fixed TIFF contract.
/// </remarks>
public sealed record PrintDimensionsPreflight(
    RevisionId SourceRevisionId,
    int SourcePixelWidth,
    int SourcePixelHeight,
    GraphicBoundsKind GraphicBoundsKind,
    TrimBounds? ArtworkBounds,
    TrimBounds? FinalCanvasBounds,
    double PhysicalWidthMm,
    double PhysicalHeightMm,
    int OutputPixelWidth,
    int OutputPixelHeight,
    int ProductionOutputPpi,
    double EffectiveSourcePpiX,
    double EffectiveSourcePpiY,
    bool RequiresEnlargement,
    bool EnlargementAuthorised)
{
    /// <summary>
    /// Opaque handle for the current committed enlargement offer. Draft projections never have
    /// one, because viewing or editing a draft must not create authorization state.
    /// </summary>
    public Guid? EnlargementOfferId { get; init; }

    /// <summary>
    /// What decided the physical size, from the same plan; null when a projection was built
    /// without it.
    /// </summary>
    public PrintSizeGovernor? Governor { get; init; }

    /// <summary>
    /// The edge the plan writes: the limited edge of a shrinking maximum-bound plan, or the
    /// resolved target edge. <see cref="LimitingEdge.None"/> when nothing is limited.
    /// </summary>
    public LimitingEdge GoverningEdge { get; init; }

    /// <summary>The operator-selected target edge of a custom size; null for maximum bounds.</summary>
    public TargetEdge? SelectedTargetEdge { get; init; }
}
