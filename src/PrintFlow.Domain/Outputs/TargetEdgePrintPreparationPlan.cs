using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;

namespace PrintFlow.Domain.Outputs;

/// <summary>
/// A TargetEdgeV1 projection bound to one exact source. This is acceptance-state only: it opens
/// no document, persists nothing, and does not implement production Photoshop preparation.
/// </summary>
public sealed record TargetEdgePrintPreparationPlan
{
    private TargetEdgePrintPreparationPlan(
        RevisionId sourceRevisionId,
        Sha256 sourceSha256,
        int sourcePixelWidth,
        int sourcePixelHeight,
        FlexibleSizeSelection selection,
        ScaleToTargetEdgeResult projection)
    {
        SourceRevisionId = sourceRevisionId;
        SourceSha256 = sourceSha256;
        SourcePixelWidth = sourcePixelWidth;
        SourcePixelHeight = sourcePixelHeight;
        Selection = selection;
        Projection = projection;
    }

    public PrintDimensionSemantics Semantics => PrintDimensionSemantics.TargetEdgeV1;

    public RevisionId SourceRevisionId { get; }

    public Sha256 SourceSha256 { get; }

    public int SourcePixelWidth { get; }

    public int SourcePixelHeight { get; }

    public FlexibleSizeSelection Selection { get; }

    public ScaleToTargetEdgeResult Projection { get; }

    public bool PresetLimitExceeded => Selection.PresetLimitExceeded;

    public bool SourceCapacityExceeded => Projection.SourceCapacityExceeded;

    public bool RequiresEnlargementAuthority => Projection.Direction == ResizeDirection.Enlarge;

    public static TargetEdgePrintPreparationPlan For(
        RevisionId sourceRevisionId,
        Sha256 sourceSha256,
        int sourcePixelWidth,
        int sourcePixelHeight,
        FlexibleSizeSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (selection.Mode != OperatorSizingMode.CustomTargetEdge ||
            selection.SelectedTargetEdge is not { } edge ||
            selection.RequestedMillimetres is not { } millimetres)
        {
            throw new ArgumentException(
                "TargetEdgeV1 requires one explicit custom target edge and millimetre value.",
                nameof(selection));
        }

        ScaleToTargetEdgeResult projection = ScaleToTargetEdge.Calculate(
            sourcePixelWidth, sourcePixelHeight, edge, millimetres);
        return new TargetEdgePrintPreparationPlan(
            sourceRevisionId,
            sourceSha256,
            sourcePixelWidth,
            sourcePixelHeight,
            selection,
            projection);
    }

    public bool Covers(RevisionId revisionId, Sha256 sha256) =>
        SourceRevisionId == revisionId && SourceSha256.Equals(sha256);

    public bool IsExecutableWith(EnlargementAuthority? authority) =>
        !RequiresEnlargementAuthority || authority?.Authorises(this) == true;
}

/// <summary>
/// Explicit permission to enlarge one exact reviewed source at one exact requested target.
/// It is not a Session setting and cannot transfer to another Revision, hash, edge, or size.
/// </summary>
public sealed record EnlargementAuthority
{
    private EnlargementAuthority(TargetEdgePrintPreparationPlan plan)
    {
        SourceRevisionId = plan.SourceRevisionId;
        SourceSha256 = plan.SourceSha256;
        SizingMode = plan.Selection.Mode;
        SelectedTargetEdge = plan.Projection.SelectedTargetEdge;
        RequestedMillimetres = plan.Projection.RequestedMillimetres;
        ProjectedScale = plan.Projection.ProjectedScale;
        ProjectedTargetPixelWidth = plan.Projection.ProjectedPixelWidth;
        ProjectedTargetPixelHeight = plan.Projection.ProjectedPixelHeight;
    }

    public RevisionId SourceRevisionId { get; }

    public Sha256 SourceSha256 { get; }

    public OperatorSizingMode SizingMode { get; }

    public TargetEdge SelectedTargetEdge { get; }

    public decimal RequestedMillimetres { get; }

    public ResizeScale ProjectedScale { get; }

    public int ProjectedTargetPixelWidth { get; }

    public int ProjectedTargetPixelHeight { get; }

    public static EnlargementAuthority For(TargetEdgePrintPreparationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.RequiresEnlargementAuthority)
        {
            throw new ArgumentException(
                "Enlargement authority can cover only a plan that requires enlargement.",
                nameof(plan));
        }

        return new EnlargementAuthority(plan);
    }

    public bool Authorises(TargetEdgePrintPreparationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.RequiresEnlargementAuthority &&
            SourceRevisionId == plan.SourceRevisionId &&
            SourceSha256.Equals(plan.SourceSha256) &&
            SizingMode == plan.Selection.Mode &&
            SelectedTargetEdge == plan.Projection.SelectedTargetEdge &&
            RequestedMillimetres == plan.Projection.RequestedMillimetres &&
            ProjectedScale == plan.Projection.ProjectedScale &&
            ProjectedTargetPixelWidth == plan.Projection.ProjectedPixelWidth &&
            ProjectedTargetPixelHeight == plan.Projection.ProjectedPixelHeight;
    }
}
