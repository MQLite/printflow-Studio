using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;

namespace PrintFlow.Domain.Outputs;

/// <summary>
/// One fully resolved Photoshop geometry decision, in whichever of the two accepted forms it was
/// made (Epic 11400 Part B1A.2D §12, §25).
/// </summary>
/// <remarks>
/// The single executable preparation authority. Before it existed, "what is Photoshop being asked
/// to do" was a <see cref="PrintPreparationPlan"/> and nothing else; the flexible-size contract
/// adds a second, genuinely different answer, and the choice was either a closed set of two or a
/// mode string with everything else nullable beside it. A closed set is what lets the engine, the
/// attempt snapshot, the request and the adapter each handle both forms without any of them
/// inventing a third.
/// <para>
/// The hierarchy is closed by a <c>private protected</c> constructor: the two cases below are the
/// only ones that can ever exist, so a <c>switch</c> over them is exhaustive by construction
/// rather than by convention, and no assembly outside the Domain can add a variant the workflow
/// has never validated.
/// </para>
/// <para>
/// Every variant is <b>already authorised</b> by the time it exists.
/// <see cref="TargetEdgePreparation"/> refuses to be constructed for an enlargement without a
/// matching <see cref="EnlargementAuthority"/>, so "an unauthorised enlargement request" is not a
/// state Infrastructure has to check for — it is a value that cannot be built (§12, §25).
/// </para>
/// </remarks>
public abstract record PhotoshopPreparation
{
    private protected PhotoshopPreparation()
    {
    }

    /// <summary>Which accepted sizing contract this preparation was made under.</summary>
    public abstract PrintDimensionSemantics Semantics { get; }

    /// <summary>The upstream Revision the geometry was calculated from.</summary>
    public abstract RevisionId SourceRevisionId { get; }

    /// <summary>The bytes of that Revision as they were when it was calculated.</summary>
    public abstract Sha256 SourceSha256 { get; }

    /// <summary>The source's own pixel width, read from its validated facts.</summary>
    public abstract int SourcePixelWidth { get; }

    /// <summary>The source's own pixel height, read from its validated facts.</summary>
    public abstract int SourcePixelHeight { get; }

    /// <summary>The single edge Photoshop may be given, or <c>None</c> when nothing is written.</summary>
    public abstract LimitingEdge PhotoshopEdge { get; }

    /// <summary>The millimetres for that edge, or null when nothing is written.</summary>
    public abstract double? PhotoshopEdgeValueMm { get; }

    /// <summary>Planning evidence only. Never a Photoshop target pair and never a read-back.</summary>
    public abstract int ProjectedPixelWidth { get; }

    /// <inheritdoc cref="ProjectedPixelWidth" />
    public abstract int ProjectedPixelHeight { get; }

    /// <summary>
    /// The neutral resampling policy. Never a Photoshop COM value: mapping <c>None</c>,
    /// <c>BicubicSharper</c> and <c>PreserveDetails</c> onto <c>ResampleMethod</c> is
    /// Infrastructure's job. B1A.3 performs that mapping only inside its closed production
    /// preparation seam; no Photoshop-native value enters this assembly (§26).
    /// </summary>
    public abstract PhotoshopResizeMode ResizePolicy { get; }

    /// <summary>Whether the future operation would resample pixels at all.</summary>
    public bool Resamples => ResizePolicy != PhotoshopResizeMode.None;

    /// <summary>The fixed production resolution. Never operator-selected (MVP design §8.3).</summary>
    public int ProductionDpi => PrintDimensions.ProductionDpi;

    /// <summary>
    /// Whether this preparation describes the artefact identified by
    /// <paramref name="revisionId"/> and <paramref name="sha256"/>.
    /// </summary>
    /// <remarks>
    /// Both halves must match, for the reason every review decision in this codebase keeps both:
    /// the id answers "which artefact" and the hash answers "which bytes", and an id alone still
    /// matches after the file underneath it was replaced in place.
    /// </remarks>
    public bool Covers(RevisionId revisionId, Sha256 sha256) =>
        SourceRevisionId == revisionId && SourceSha256.Equals(sha256);
}

/// <summary>
/// The maximum-bound form: a fit box from which <see cref="FitWithinBounds"/> selected the one
/// edge Photoshop is allowed to receive (the accepted B1A.1 contract).
/// </summary>
public sealed record FitWithinBoundsPreparation : PhotoshopPreparation
{
    public FitWithinBoundsPreparation(PrintPreparationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        Plan = plan;
    }

    public PrintPreparationPlan Plan { get; }

    public override PrintDimensionSemantics Semantics => PrintPreparationPlan.Semantics;

    public override RevisionId SourceRevisionId => Plan.SourceRevisionId;

    public override Sha256 SourceSha256 => Plan.SourceSha256;

    public override int SourcePixelWidth => Plan.SourcePixelWidth;

    public override int SourcePixelHeight => Plan.SourcePixelHeight;

    public override LimitingEdge PhotoshopEdge => Plan.LimitingEdge;

    public override double? PhotoshopEdgeValueMm => Plan.LimitingValueMm;

    public override int ProjectedPixelWidth => Plan.ProjectedPixelWidth;

    public override int ProjectedPixelHeight => Plan.ProjectedPixelHeight;

    public override PhotoshopResizeMode ResizePolicy => Plan.ResizePolicy;

    public override string ToString() => Plan.ToString();
}

/// <summary>
/// The flexible-size form: one exact operator-selected physical edge, with the explicit
/// enlargement authority when the request adds pixels (Epic 11400 Part B1A.2D §8, §9).
/// </summary>
/// <remarks>
/// <see cref="Authority"/> is validated <b>here</b>, at construction, rather than checked again by
/// each consumer. An enlargement preparation without a matching authority throws, so every layer
/// downstream — the attempt snapshot, the request, the adapter — holds a value whose existence is
/// already the proof that a human authorised this exact enlargement of this exact source (§12).
/// <para>
/// The matching rule itself lives in
/// <see cref="TargetEdgePrintPreparationPlan.IsExecutableWith"/> and is not restated here, in SQL,
/// in the service or in the UI (§9).
/// </para>
/// </remarks>
public sealed record TargetEdgePreparation : PhotoshopPreparation
{
    public TargetEdgePreparation(
        TargetEdgePrintPreparationPlan plan, EnlargementAuthority? authority)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (!plan.IsExecutableWith(authority))
        {
            throw new ArgumentException(
                "An enlargement needs an exact matching authority for this source, edge, requested " +
                "millimetres and projected pixel pair; nothing here grants one.",
                nameof(authority));
        }

        if (!plan.RequiresEnlargementAuthority && authority is not null)
        {
            throw new ArgumentException(
                "A shrink or resolution-only plan carries no enlargement authority; keeping one " +
                "would read as permission for a run that was never asked for.",
                nameof(authority));
        }

        Plan = plan;
        Authority = authority;
    }

    public TargetEdgePrintPreparationPlan Plan { get; }

    /// <summary>The explicit permission this run holds, or null when none is needed.</summary>
    public EnlargementAuthority? Authority { get; }

    public override PrintDimensionSemantics Semantics => Plan.Semantics;

    public override RevisionId SourceRevisionId => Plan.SourceRevisionId;

    public override Sha256 SourceSha256 => Plan.SourceSha256;

    public override int SourcePixelWidth => Plan.SourcePixelWidth;

    public override int SourcePixelHeight => Plan.SourcePixelHeight;

    public override LimitingEdge PhotoshopEdge => Plan.Projection.PhotoshopTargetEdge;

    public override double? PhotoshopEdgeValueMm => (double)Plan.Projection.RequestedMillimetres;

    public override int ProjectedPixelWidth => Plan.Projection.ProjectedPixelWidth;

    public override int ProjectedPixelHeight => Plan.Projection.ProjectedPixelHeight;

    public override PhotoshopResizeMode ResizePolicy => Plan.Projection.ResizePolicy;

    /// <summary>Whether this run adds pixels and therefore ran under an explicit authority.</summary>
    public bool IsAuthorisedEnlargement => Authority is not null;

    public override string ToString() =>
        $"{Plan}{(Authority is null ? string.Empty : ", authorised enlargement")}";
}
