using PrintFlow.Domain.Ids;
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
}
