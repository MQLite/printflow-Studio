using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;

namespace PrintFlow.Domain.Outputs;

/// <summary>
/// What a persisted <see cref="PrintDimensions"/> pair actually means (Epic 11400 Part B1A.2A §9).
/// </summary>
/// <remarks>
/// The millimetre columns did not change; what they <i>mean</i> did, and a stored pair that
/// cannot say which reading it was written under is a pair no code may act on. Two readings
/// exist and neither is a default:
/// <list type="bullet">
///   <item><see cref="LegacyExactPair"/> — written before B1A.1, when width and height were two
///         independently exact output dimensions. Readable for audit and display, and never
///         executable as a maximum-bound Photoshop plan.</item>
///   <item><see cref="MaxBoundsV1"/> — the accepted contract: a fit box, from which
///         <see cref="FitWithinBounds"/> selects the one edge Photoshop is allowed to receive.</item>
/// </list>
/// <para>
/// There is deliberately no member meaning "unknown". A session either recorded a reading or
/// holds no dimensions at all, and the absence of dimensions is <c>null</c>.
/// </para>
/// </remarks>
public enum PrintDimensionSemantics
{
    /// <summary>
    /// Two independently exact dimensions, recorded before the maximum-bound contract existed.
    /// </summary>
    /// <remarks>
    /// Never silently reinterpreted as a fit box. Which of the old pair was intended as the
    /// limiting edge is a question the record does not answer, and inferring one from the source
    /// ratio would be the software making the operator's decision for them (§10).
    /// </remarks>
    LegacyExactPair,

    /// <summary>Maximum bounds under the accepted B1A.1 fit-within-bounds contract.</summary>
    MaxBoundsV1,
}

/// <summary>What a preparation plan asks the future Photoshop operation to do.</summary>
public enum PrintPreparationMode
{
    /// <summary>
    /// The source already fits both bounds, so only the resolution is set. Nothing is resampled.
    /// </summary>
    ResolutionOnly,

    /// <summary>
    /// One edge is written and Photoshop derives the other with proportions constrained. Always
    /// a shrink; enlargement is never produced.
    /// </summary>
    ProportionalShrink,
}

/// <summary>
/// One <see cref="FitWithinBounds"/> result bound to the exact upstream artefact it was
/// calculated from (Epic 11400 Part B1A.2A §5, §7).
/// </summary>
/// <remarks>
/// The binding is the point of the record, exactly as it is for
/// <see cref="Sessions.BackgroundRemovalAuthority"/>. A limiting edge is not a property of a
/// size the operator typed; it is a property of <i>that size against these source pixels</i>.
/// Rebinding an existing plan to different content would therefore silently change which edge
/// Photoshop receives, which is why both halves of the artefact identity are kept:
/// <see cref="SourceRevisionId"/> answers "which artefact" and <see cref="SourceSha256"/>
/// answers "which bytes" (MVP design invariants 2 and 3).
/// <para>
/// It is <i>pending</i> state on a session. What a Photoshop attempt actually ran with is
/// snapshotted on that attempt and never rewritten, exactly as trim parameters and the
/// background-removal authority are (§12).
/// </para>
/// <para>
/// Nothing here is a Photoshop result. <see cref="ProjectedPixelWidth"/> and
/// <see cref="ProjectedPixelHeight"/> are planning evidence that the selected edge fits the
/// other bound — never <c>ActualWidth</c>, never a target pair, and never a read-back. The real
/// Photoshop-returned geometry is B1A.3's to record (§20).
/// </para>
/// <para>
/// <see cref="ResizePolicy"/> is the neutral Domain vocabulary. Mapping it to Photoshop's
/// <c>ResampleMethod.NONE</c> or <c>ResampleMethod.BICUBICSHARPER</c> is Infrastructure's job;
/// no COM or DOM type reaches this assembly (§5).
/// </para>
/// </remarks>
/// <param name="SourceRevisionId">The upstream Revision the plan was calculated from.</param>
/// <param name="SourceSha256">The bytes of that Revision as they were when it was calculated.</param>
/// <param name="SourcePixelWidth">The source's own pixel width, read from its validated facts.</param>
/// <param name="SourcePixelHeight">The source's own pixel height, read from its validated facts.</param>
/// <param name="MaxWidthMm">The fit box's maximum width. Not a width target.</param>
/// <param name="MaxHeightMm">The fit box's maximum height. Not a height target.</param>
/// <param name="LimitKind">Which named preset or custom entry the bounds came from.</param>
/// <param name="Mode">Whether the future operation resamples at all.</param>
/// <param name="LimitingEdge">The single edge Photoshop may be given, or None.</param>
/// <param name="LimitingValueMm">The millimetres for that edge, or null when nothing is written.</param>
/// <param name="ProjectedPixelWidth">Planning evidence only. Never a Photoshop target.</param>
/// <param name="ProjectedPixelHeight">Planning evidence only. Never a Photoshop target.</param>
/// <param name="ResizePolicy">The neutral resampling policy, fixed by the accepted contract.</param>
public sealed record PrintPreparationPlan(
    RevisionId SourceRevisionId,
    Sha256 SourceSha256,
    int SourcePixelWidth,
    int SourcePixelHeight,
    double MaxWidthMm,
    double MaxHeightMm,
    SizePreset LimitKind,
    PrintPreparationMode Mode,
    LimitingEdge LimitingEdge,
    double? LimitingValueMm,
    int ProjectedPixelWidth,
    int ProjectedPixelHeight,
    PhotoshopResizeMode ResizePolicy)
{
    /// <summary>The semantics version every plan of this shape was written under (§4).</summary>
    public const PrintDimensionSemantics Semantics = PrintDimensionSemantics.MaxBoundsV1;

    /// <summary>The fixed production resolution. Never operator-selected (MVP design §8.3).</summary>
    public int ProductionDpi => PrintDimensions.ProductionDpi;

    /// <summary>Whether the future operation resamples pixels at all.</summary>
    public bool RequiresShrink => Mode == PrintPreparationMode.ProportionalShrink;

    /// <summary>
    /// Calculates a plan for <paramref name="limits"/> against one exact upstream artefact.
    /// </summary>
    /// <remarks>
    /// The only way a plan is created, and it goes through <see cref="FitWithinBounds"/> — so
    /// limiting-edge selection, the already-within-bounds case, the no-enlargement rule and the
    /// projected pixels have exactly one implementation, wherever a plan comes from (§6).
    /// <para>
    /// Takes the source's own pixel dimensions rather than deriving them from anything: a
    /// filename, a stale screen value, or the independently converted
    /// <see cref="PrintDimensions.PixelWidth"/> would each be a different image's plan wearing
    /// this one's binding (§8).
    /// </para>
    /// </remarks>
    public static PrintPreparationPlan For(
        RevisionId sourceRevisionId,
        Sha256 sourceSha256,
        int sourcePixelWidth,
        int sourcePixelHeight,
        PrintDimensions limits)
    {
        FitWithinBoundsResult fit =
            FitWithinBounds.Calculate(sourcePixelWidth, sourcePixelHeight, limits);

        // The already-within-bounds case keeps the source's own pixels verbatim rather than
        // round-tripping them through millimetres. The two agree to well under half a pixel, but
        // "nothing is resampled" deserves to be exactly true rather than arithmetically close.
        bool resizes = fit.ResizeMode == PhotoshopResizeMode.BicubicSharper;

        return new PrintPreparationPlan(
            sourceRevisionId,
            sourceSha256,
            sourcePixelWidth,
            sourcePixelHeight,
            limits.MaxWidthMm,
            limits.MaxHeightMm,
            limits.Preset,
            resizes ? PrintPreparationMode.ProportionalShrink : PrintPreparationMode.ResolutionOnly,
            fit.LimitingEdge,
            fit.LimitingValueMm,
            resizes ? fit.ProjectedPixelWidth : sourcePixelWidth,
            resizes ? fit.ProjectedPixelHeight : sourcePixelHeight,
            fit.ResizeMode).Validated();
    }

    /// <summary>
    /// Rebuilds a stored plan, refusing one whose parts do not describe a single decision.
    /// </summary>
    /// <remarks>
    /// The mapper's entry point, and the last place a half-written or self-contradictory row can
    /// still be refused (§13). It deliberately does <b>not</b> re-run
    /// <see cref="FitWithinBounds"/> over the stored inputs: recalculating on read would quietly
    /// repair a row rather than reject it, and a plan that had to be recomputed to be readable
    /// was never really persisted.
    /// </remarks>
    public static PrintPreparationPlan Rehydrate(
        RevisionId sourceRevisionId,
        Sha256 sourceSha256,
        int sourcePixelWidth,
        int sourcePixelHeight,
        double maxWidthMm,
        double maxHeightMm,
        SizePreset limitKind,
        PrintPreparationMode mode,
        LimitingEdge limitingEdge,
        double? limitingValueMm,
        int projectedPixelWidth,
        int projectedPixelHeight,
        PhotoshopResizeMode resizePolicy) =>
        new PrintPreparationPlan(
            sourceRevisionId,
            sourceSha256,
            sourcePixelWidth,
            sourcePixelHeight,
            maxWidthMm,
            maxHeightMm,
            limitKind,
            mode,
            limitingEdge,
            limitingValueMm,
            projectedPixelWidth,
            projectedPixelHeight,
            resizePolicy).Validated();

    /// <summary>
    /// Whether this plan describes the artefact identified by <paramref name="revisionId"/> and
    /// <paramref name="sha256"/> (§7, §16).
    /// </summary>
    /// <remarks>
    /// The single predicate behind every staleness rule in the slice: the engine's Photoshop
    /// precondition, the attempt snapshot, and the read model's readiness all ask this one
    /// question, so an offered control and an accepted command cannot disagree about what "still
    /// usable" means — the same arrangement
    /// <see cref="Sessions.BackgroundRemovalAuthority.Authorises"/> established.
    /// <para>
    /// Both halves must match. Requiring the hash as well as the id is what makes a Revision
    /// whose bytes were replaced in place fail here rather than pass on identity alone.
    /// </para>
    /// </remarks>
    public bool Covers(RevisionId revisionId, Sha256 sha256) =>
        SourceRevisionId == revisionId && SourceSha256.Equals(sha256);

    /// <summary>
    /// Returns this plan, or throws when its parts contradict each other.
    /// </summary>
    /// <remarks>
    /// Every rule here is one the accepted contract already states, restated where a value can
    /// still be refused. The mode, edge, limiting value and resize policy are four expressions of
    /// <b>one</b> decision, so any combination in which they disagree is a record no reader could
    /// honestly interpret — and "projected never exceeds source" is the no-enlargement rule made
    /// impossible to hold rather than merely unlikely to occur.
    /// </remarks>
    private PrintPreparationPlan Validated()
    {
        Positive(SourcePixelWidth, nameof(SourcePixelWidth));
        Positive(SourcePixelHeight, nameof(SourcePixelHeight));
        Positive(ProjectedPixelWidth, nameof(ProjectedPixelWidth));
        Positive(ProjectedPixelHeight, nameof(ProjectedPixelHeight));
        PositiveMillimetres(MaxWidthMm, nameof(MaxWidthMm));
        PositiveMillimetres(MaxHeightMm, nameof(MaxHeightMm));

        bool shrinks = Mode == PrintPreparationMode.ProportionalShrink;

        if (shrinks != (ResizePolicy == PhotoshopResizeMode.BicubicSharper))
        {
            throw new ArgumentException(
                $"Mode {Mode} and resize policy {ResizePolicy} describe different decisions; " +
                "a shrink resamples with BicubicSharper and nothing else ever resamples.");
        }

        if (shrinks != (LimitingEdge is LimitingEdge.Width or LimitingEdge.Height))
        {
            throw new ArgumentException(
                $"Mode {Mode} and limiting edge {LimitingEdge} describe different decisions; " +
                "exactly a shrink writes exactly one edge.");
        }

        if (shrinks != LimitingValueMm.HasValue)
        {
            throw new ArgumentException(
                $"Mode {Mode} carries {(LimitingValueMm.HasValue ? "a" : "no")} limiting value; " +
                "a shrink writes one millimetre value and a resolution-only plan writes none.");
        }

        if (LimitingValueMm is { } limiting && !(double.IsFinite(limiting) && limiting > 0))
        {
            throw new ArgumentException(
                $"A limiting value of {limiting} mm is not a size Photoshop could be given.");
        }

        // The no-enlargement rule, held structurally rather than promised. A plan that projected
        // more pixels than it started with would be an upscale, whichever edge produced it.
        if (ProjectedPixelWidth > SourcePixelWidth || ProjectedPixelHeight > SourcePixelHeight)
        {
            throw new ArgumentException(
                $"Projected {ProjectedPixelWidth}×{ProjectedPixelHeight} px exceeds the source " +
                $"{SourcePixelWidth}×{SourcePixelHeight} px; enlargement is prohibited.");
        }

        return this;
    }

    private static void Positive(int pixels, string name)
    {
        if (pixels <= 0)
        {
            throw new ArgumentOutOfRangeException(name, pixels, "A preparation plan needs positive pixels.");
        }
    }

    private static void PositiveMillimetres(double millimetres, string name)
    {
        if (!double.IsFinite(millimetres) || millimetres <= 0)
        {
            throw new ArgumentOutOfRangeException(
                name, millimetres, "A maximum bound must be positive finite millimetres.");
        }
    }

    public override string ToString() =>
        $"{Mode} within {MaxWidthMm:0.##}×{MaxHeightMm:0.##} mm ({LimitKind}), edge {LimitingEdge}" +
        $"{(LimitingValueMm is { } v ? $" at {v:0.##} mm" : string.Empty)}, projected " +
        $"{ProjectedPixelWidth}×{ProjectedPixelHeight} px @ {ProductionDpi} dpi, {ResizePolicy}";
}
