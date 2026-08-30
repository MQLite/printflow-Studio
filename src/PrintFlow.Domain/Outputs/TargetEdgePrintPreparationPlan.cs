using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;

namespace PrintFlow.Domain.Outputs;

/// <summary>
/// A TargetEdgeV1 projection bound to one exact source.
/// </summary>
/// <remarks>
/// The binding is the point of the record, exactly as it is for
/// <see cref="PrintPreparationPlan"/>: a projected pixel pair is not a property of a size the
/// operator typed, it is a property of <i>that size against these source pixels</i>. Rebinding it
/// to different content would silently change what Photoshop is asked to produce.
/// <para>
/// Nothing here opens a document or names a Photoshop type.
/// <see cref="ScaleToTargetEdgeResult.ResizePolicy"/> is the neutral Domain vocabulary; mapping it
/// to <c>ResampleMethod</c> is Infrastructure's job, and no production resize exists yet
/// (Epic 11400 Part B1A.2D §8, §26).
/// </para>
/// </remarks>
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

    /// <summary>The fixed production resolution. Never operator-selected (MVP design §8.3).</summary>
    public int ProductionDpi => PrintDimensions.ProductionDpi;

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
            projection).Validated();
    }

    /// <summary>
    /// Rebuilds a stored plan, refusing one whose parts do not describe a single projection
    /// (Epic 11400 Part B1A.2D §23).
    /// </summary>
    /// <remarks>
    /// It deliberately does <b>not</b> re-run <see cref="ScaleToTargetEdge"/> over the stored
    /// millimetres. Recalculating on read would quietly repair a row rather than reject it, and a
    /// plan that had to be recomputed to be readable was never really persisted — the same rule
    /// <see cref="PrintPreparationPlan.Rehydrate"/> follows. What it does check is that the stored
    /// parts agree with each other, which is what makes a half-written or self-contradictory row
    /// fail here instead of downstream.
    /// </remarks>
    public static TargetEdgePrintPreparationPlan Rehydrate(
        RevisionId sourceRevisionId,
        Sha256 sourceSha256,
        int sourcePixelWidth,
        int sourcePixelHeight,
        FlexibleSizeSelection selection,
        LimitingEdge photoshopTargetEdge,
        int projectedPixelWidth,
        int projectedPixelHeight,
        ResizeScale projectedScale,
        ResizeDirection direction,
        PhotoshopResizeMode resizePolicy)
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

        return new TargetEdgePrintPreparationPlan(
            sourceRevisionId,
            sourceSha256,
            sourcePixelWidth,
            sourcePixelHeight,
            selection,
            new ScaleToTargetEdgeResult(
                edge,
                photoshopTargetEdge,
                millimetres,
                projectedPixelWidth,
                projectedPixelHeight,
                projectedScale,
                direction,
                resizePolicy)).Validated();
    }

    public bool Covers(RevisionId revisionId, Sha256 sha256) =>
        SourceRevisionId == revisionId && SourceSha256.Equals(sha256);

    /// <summary>
    /// This projection as the operator-facing <see cref="PrintDimensions"/> the session records
    /// for display, naming and the <c>PrintOutput</c> audit (Epic 11400 Part B1A.2D §6).
    /// </summary>
    /// <remarks>
    /// A description of the plan, never a second target. The authoritative edge is the operator's
    /// exact request and the other edge is read back from the projected pixels — so the pair says
    /// what this output will physically be, while the thing Photoshop is actually given remains
    /// the single edge in <see cref="Projection"/>. Sending both would be the non-proportional
    /// stretch the accepted contract prohibits.
    /// <para>
    /// The preset is always <see cref="SizePreset.Custom"/>, including for an override: once the
    /// operator has replaced a recommendation with their own edge, the result is no longer that
    /// named size. Which recommendation was overridden is recorded on <see cref="Selection"/>,
    /// where it stays legible as an override rather than as the preset itself (§6).
    /// </para>
    /// </remarks>
    public PrintDimensions AsRecordedDimensions()
    {
        const double MillimetresPerInch = 25.4;
        double requested = (double)Projection.RequestedMillimetres;
        double derivedWidth = Projection.ProjectedPixelWidth * MillimetresPerInch / ProductionDpi;
        double derivedHeight = Projection.ProjectedPixelHeight * MillimetresPerInch / ProductionDpi;

        return Projection.PhotoshopTargetEdge == LimitingEdge.Width
            ? PrintDimensions.FromMillimetres(requested, derivedHeight, SizePreset.Custom)
            : PrintDimensions.FromMillimetres(derivedWidth, requested, SizePreset.Custom);
    }

    public bool IsExecutableWith(EnlargementAuthority? authority) =>
        !RequiresEnlargementAuthority || authority?.Authorises(this) == true;

    /// <summary>
    /// Returns this plan, or throws when its parts contradict each other.
    /// </summary>
    /// <remarks>
    /// Every rule here is one the accepted contract already states, restated where a value can
    /// still be refused. The selected edge, the resolved Photoshop edge, the direction, the policy
    /// and the scale are five expressions of <b>one</b> decision, so any combination in which they
    /// disagree is a record no reader could honestly interpret.
    /// </remarks>
    private TargetEdgePrintPreparationPlan Validated()
    {
        Positive(SourcePixelWidth, nameof(SourcePixelWidth));
        Positive(SourcePixelHeight, nameof(SourcePixelHeight));
        Positive(Projection.ProjectedPixelWidth, nameof(Projection.ProjectedPixelWidth));
        Positive(Projection.ProjectedPixelHeight, nameof(Projection.ProjectedPixelHeight));

        if (Selection.SelectedTargetEdge != Projection.SelectedTargetEdge ||
            Selection.RequestedMillimetres != Projection.RequestedMillimetres)
        {
            throw new ArgumentException(
                "The stored selection and its projection describe different requests.");
        }

        // A LongEdge request is not executable until it has been resolved against real source
        // pixels: Photoshop is given Width or Height, never "the long one" (§8, §23).
        LimitingEdge expectedEdge = Projection.SelectedTargetEdge switch
        {
            TargetEdge.Width => LimitingEdge.Width,
            TargetEdge.Height => LimitingEdge.Height,
            TargetEdge.LongEdge when SourcePixelWidth >= SourcePixelHeight => LimitingEdge.Width,
            _ => LimitingEdge.Height,
        };
        if (Projection.PhotoshopTargetEdge != expectedEdge)
        {
            throw new ArgumentException(
                $"A {Projection.SelectedTargetEdge} request against {SourcePixelWidth}×" +
                $"{SourcePixelHeight} px resolves to {expectedEdge}, not " +
                $"{Projection.PhotoshopTargetEdge}.");
        }

        PhotoshopResizeMode expectedPolicy = Projection.Direction switch
        {
            ResizeDirection.ResolutionOnly => PhotoshopResizeMode.None,
            ResizeDirection.Shrink => PhotoshopResizeMode.BicubicSharper,
            ResizeDirection.Enlarge => PhotoshopResizeMode.PreserveDetails,
            _ => throw new ArgumentException($"Unknown resize direction {Projection.Direction}."),
        };
        if (Projection.ResizePolicy != expectedPolicy)
        {
            throw new ArgumentException(
                $"Direction {Projection.Direction} and resize policy {Projection.ResizePolicy} " +
                "describe different decisions; the accepted contract fixes one policy per direction.");
        }

        int targetAuthoritativePixels = Projection.PhotoshopTargetEdge == LimitingEdge.Width
            ? Projection.ProjectedPixelWidth
            : Projection.ProjectedPixelHeight;
        int sourceAuthoritativePixels = Projection.PhotoshopTargetEdge == LimitingEdge.Width
            ? SourcePixelWidth
            : SourcePixelHeight;

        if (Projection.ProjectedScale != ResizeScale.FromPixels(
                targetAuthoritativePixels, sourceAuthoritativePixels))
        {
            throw new ArgumentException(
                $"The stored scale {Projection.ProjectedScale.Numerator}/" +
                $"{Projection.ProjectedScale.Denominator} is not the ratio between " +
                $"{targetAuthoritativePixels} px and {sourceAuthoritativePixels} px.");
        }

        ResizeDirection expectedDirection =
            targetAuthoritativePixels.CompareTo(sourceAuthoritativePixels) switch
            {
                0 => ResizeDirection.ResolutionOnly,
                < 0 => ResizeDirection.Shrink,
                _ => ResizeDirection.Enlarge,
            };
        if (Projection.Direction != expectedDirection)
        {
            throw new ArgumentException(
                $"A projection from {sourceAuthoritativePixels} px to {targetAuthoritativePixels} px " +
                $"is a {expectedDirection}, not a {Projection.Direction}.");
        }

        return this;
    }

    private static void Positive(int pixels, string name)
    {
        if (pixels <= 0)
        {
            throw new ArgumentOutOfRangeException(
                name, pixels, "A target-edge plan needs positive pixels.");
        }
    }

    public override string ToString() =>
        $"{Semantics} {Projection.Direction} ({Projection.ResizePolicy}), " +
        $"{Projection.SelectedTargetEdge} at {Projection.RequestedMillimetres:0.####} mm on " +
        $"{Projection.PhotoshopTargetEdge}, projected {Projection.ProjectedPixelWidth}×" +
        $"{Projection.ProjectedPixelHeight} px @ {ProductionDpi} dpi";
}

/// <summary>
/// Explicit permission to enlarge one exact reviewed source at one exact requested target.
/// It is not a Session setting and cannot transfer to another Revision, hash, edge, or size.
/// </summary>
/// <remarks>
/// Deliberately not a boolean. "Enlargement was authorised" answers nothing on its own, because
/// what matters is <i>which</i> enlargement: a flag survives a changed source, a changed request
/// and a changed projection, and would keep authorising a run nobody agreed to
/// (Epic 11400 Part B1A.2D §9, §10).
/// </remarks>
public sealed record EnlargementAuthority
{
    private EnlargementAuthority(
        RevisionId sourceRevisionId,
        Sha256 sourceSha256,
        OperatorSizingMode sizingMode,
        TargetEdge selectedTargetEdge,
        decimal requestedMillimetres,
        ResizeScale projectedScale,
        int projectedTargetPixelWidth,
        int projectedTargetPixelHeight)
    {
        SourceRevisionId = sourceRevisionId;
        SourceSha256 = sourceSha256;
        SizingMode = sizingMode;
        SelectedTargetEdge = selectedTargetEdge;
        RequestedMillimetres = requestedMillimetres;
        ProjectedScale = projectedScale;
        ProjectedTargetPixelWidth = projectedTargetPixelWidth;
        ProjectedTargetPixelHeight = projectedTargetPixelHeight;
    }

    public RevisionId SourceRevisionId { get; }

    public Sha256 SourceSha256 { get; }

    public OperatorSizingMode SizingMode { get; }

    public TargetEdge SelectedTargetEdge { get; }

    public decimal RequestedMillimetres { get; }

    public ResizeScale ProjectedScale { get; }

    public int ProjectedTargetPixelWidth { get; }

    public int ProjectedTargetPixelHeight { get; }

    /// <summary>The semantics an authority can only ever cover.</summary>
    public PrintDimensionSemantics Semantics => PrintDimensionSemantics.TargetEdgeV1;

    public static EnlargementAuthority For(TargetEdgePrintPreparationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.RequiresEnlargementAuthority)
        {
            throw new ArgumentException(
                "Enlargement authority can cover only a plan that requires enlargement.",
                nameof(plan));
        }

        return new EnlargementAuthority(
            plan.SourceRevisionId,
            plan.SourceSha256,
            plan.Selection.Mode,
            plan.Projection.SelectedTargetEdge,
            plan.Projection.RequestedMillimetres,
            plan.Projection.ProjectedScale,
            plan.Projection.ProjectedPixelWidth,
            plan.Projection.ProjectedPixelHeight);
    }

    /// <summary>
    /// Rebuilds a stored authority, refusing one that is not a complete permission
    /// (Epic 11400 Part B1A.2D §9, §23).
    /// </summary>
    /// <remarks>
    /// The completeness rule is the whole of it. An authority missing any one of the facts it
    /// binds is not a weaker permission, it is a permission for something unspecified — so a
    /// partial row is refused rather than read as covering whatever happens to be beside it.
    /// </remarks>
    public static EnlargementAuthority Rehydrate(
        RevisionId sourceRevisionId,
        Sha256 sourceSha256,
        OperatorSizingMode sizingMode,
        TargetEdge selectedTargetEdge,
        decimal requestedMillimetres,
        ResizeScale projectedScale,
        int projectedTargetPixelWidth,
        int projectedTargetPixelHeight)
    {
        if (sizingMode != OperatorSizingMode.CustomTargetEdge)
        {
            throw new ArgumentException(
                "Only a custom target-edge decision can require enlargement authority; ordinary " +
                "preset use never enlarges.",
                nameof(sizingMode));
        }

        if (requestedMillimetres <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedMillimetres), requestedMillimetres,
                "An enlargement authority names positive millimetres.");
        }

        if (projectedTargetPixelWidth <= 0 || projectedTargetPixelHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(projectedTargetPixelWidth),
                "An enlargement authority names a positive projected pixel pair.");
        }

        if (projectedScale.Numerator <= projectedScale.Denominator)
        {
            throw new ArgumentException(
                $"A scale of {projectedScale.Numerator}/{projectedScale.Denominator} is not an " +
                "enlargement; authority is granted only where pixels are added.",
                nameof(projectedScale));
        }

        return new EnlargementAuthority(
            sourceRevisionId,
            sourceSha256,
            sizingMode,
            selectedTargetEdge,
            requestedMillimetres,
            projectedScale,
            projectedTargetPixelWidth,
            projectedTargetPixelHeight);
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
