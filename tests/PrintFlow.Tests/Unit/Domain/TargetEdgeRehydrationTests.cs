using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;

namespace PrintFlow.Tests.Unit.Domain;

/// <summary>
/// What a stored flexible-size decision must be able to say for itself before anything acts on it
/// (Epic 11400 Part B1A.2D §22, §23).
/// </summary>
/// <remarks>
/// The database CHECK constraints refuse most of these rows outright; these tests are the second
/// half of the same rule, and both halves are deliberate. A schema can be migrated, copied, or
/// edited by hand, and the moment a row reaches code the question is no longer "could this have
/// been written" but "will this be read". Every case below is refused rather than repaired,
/// because each missing or contradictory value is one a reader would otherwise have to invent —
/// and an invented resolved edge is an edge nobody calculated being handed to Photoshop.
/// <para>
/// Rehydration deliberately never recalculates. A plan that had to be recomputed to be readable
/// was never really persisted, and recomputing would quietly repair a bad row rather than reject
/// it — the rule <c>PrintPreparationPlan.Rehydrate</c> already follows.
/// </para>
/// </remarks>
public sealed class TargetEdgeRehydrationTests
{
    private static readonly RevisionId Revision =
        RevisionId.From(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

    private static readonly Sha256 Bytes = Sha256.Parse(new string('A', 64));

    /// <summary>A complete stored plan comes back exactly as it was calculated (§8, §34).</summary>
    [Fact]
    public void A_complete_stored_plan_rehydrates_to_the_calculated_one()
    {
        TargetEdgePrintPreparationPlan calculated = TargetEdgePrintPreparationPlan.For(
            Revision, Bytes, 2000, 1000,
            FlexibleSizeSelection.CustomTarget(TargetEdge.Width, 84.709m));

        TargetEdgePrintPreparationPlan restored = TargetEdgePrintPreparationPlan.Rehydrate(
            Revision, Bytes, 2000, 1000,
            calculated.Selection,
            calculated.Projection.PhotoshopTargetEdge,
            calculated.Projection.ProjectedPixelWidth,
            calculated.Projection.ProjectedPixelHeight,
            calculated.Projection.ProjectedScale,
            calculated.Projection.Direction,
            calculated.Projection.ResizePolicy);

        restored.ShouldBe(calculated);
    }

    /// <summary>An ordinary preset fit is not a target-edge plan and never rehydrates as one (§5).</summary>
    [Fact]
    public void A_preset_fit_selection_cannot_rehydrate_a_target_edge_plan() =>
        Should.Throw<ArgumentException>(() => TargetEdgePrintPreparationPlan.Rehydrate(
            Revision, Bytes, 2000, 1000,
            FlexibleSizeSelection.PresetFit(
                PresetPrintRecommendation.MaximumLongEdge(SizePreset.A4, 280m)),
            LimitingEdge.Width, 1001, 501, ResizeScale.FromPixels(1001, 2000),
            ResizeDirection.Shrink, PhotoshopResizeMode.BicubicSharper));

    /// <summary>
    /// A long-edge request that was never resolved against real pixels is not executable (§8, §23).
    /// </summary>
    /// <remarks>
    /// Photoshop is given Width or Height. "The long one" is a question about the source, and a
    /// row that recorded the answer wrongly would hand Photoshop the edge the operator did not
    /// choose — on a landscape source, the short one.
    /// </remarks>
    [Fact]
    public void A_long_edge_resolved_to_the_wrong_concrete_edge_is_refused() =>
        Should.Throw<ArgumentException>(() => TargetEdgePrintPreparationPlan.Rehydrate(
            Revision, Bytes, 2000, 1000,
            FlexibleSizeSelection.CustomTarget(TargetEdge.LongEdge, 84.709m),

            // The source is landscape, so the long edge is Width.
            LimitingEdge.Height, 1001, 501, ResizeScale.FromPixels(1001, 2000),
            ResizeDirection.Shrink, PhotoshopResizeMode.BicubicSharper));

    /// <summary>
    /// The direction and the resampling policy are one decision (§23).
    /// </summary>
    /// <remarks>
    /// The accepted contract fixes exactly one policy per direction, so a stored pair that
    /// disagrees is a record no reader could honestly interpret — and the one that matters most is
    /// an enlargement claiming BicubicSharper, which would resample added pixels with the method
    /// chosen for removing them.
    /// </remarks>
    [Theory]
    [InlineData(ResizeDirection.Shrink, PhotoshopResizeMode.PreserveDetails)]
    [InlineData(ResizeDirection.Shrink, PhotoshopResizeMode.None)]
    [InlineData(ResizeDirection.ResolutionOnly, PhotoshopResizeMode.BicubicSharper)]
    public void A_direction_and_policy_that_disagree_are_refused(
        ResizeDirection direction, PhotoshopResizeMode policy) =>
        Should.Throw<ArgumentException>(() => TargetEdgePrintPreparationPlan.Rehydrate(
            Revision, Bytes, 2000, 1000,
            FlexibleSizeSelection.CustomTarget(TargetEdge.Width, 84.709m),
            LimitingEdge.Width, 1001, 501, ResizeScale.FromPixels(1001, 2000),
            direction, policy));

    /// <summary>
    /// A stored direction that the stored pixels contradict is refused (§23).
    /// </summary>
    /// <remarks>
    /// 1001 projected from 2000 source pixels removes pixels, whatever the row calls it. A plan
    /// labelled an enlargement here would demand an authority for a shrink — or, the other way
    /// round, let an enlargement run as a shrink with no confirmation at all.
    /// </remarks>
    [Fact]
    public void A_direction_the_stored_pixels_contradict_is_refused() =>
        Should.Throw<ArgumentException>(() => TargetEdgePrintPreparationPlan.Rehydrate(
            Revision, Bytes, 2000, 1000,
            FlexibleSizeSelection.CustomTarget(TargetEdge.Width, 84.709m),
            LimitingEdge.Width, 1001, 501, ResizeScale.FromPixels(1001, 2000),
            ResizeDirection.Enlarge, PhotoshopResizeMode.PreserveDetails));

    /// <summary>
    /// A scale that is not the ratio between the stored pixels is refused (§7, §23).
    /// </summary>
    /// <remarks>
    /// The ratio is what the enlargement authority is matched on, exactly. A row whose scale drifts
    /// from its own pixels would be a plan whose granted permission no longer covers it, in a way
    /// nothing downstream could diagnose.
    /// </remarks>
    [Fact]
    public void A_scale_that_is_not_the_stored_ratio_is_refused() =>
        Should.Throw<ArgumentException>(() => TargetEdgePrintPreparationPlan.Rehydrate(
            Revision, Bytes, 2000, 1000,
            FlexibleSizeSelection.CustomTarget(TargetEdge.Width, 84.709m),
            LimitingEdge.Width, 1001, 501, ResizeScale.FromPixels(1000, 2000),
            ResizeDirection.Shrink, PhotoshopResizeMode.BicubicSharper));

    /// <summary>An unreduced stored ratio is refused rather than reduced (§7).</summary>
    /// <remarks>
    /// 2002/4000 and 1001/2000 are the same number and not the same record, and only one of them
    /// is what the calculator writes. Reducing here would be repairing a row; the authority
    /// comparison is exact, so a stored unreduced pair would silently stop authorising the plan it
    /// was granted for.
    /// </remarks>
    [Fact]
    public void An_unreduced_stored_scale_is_refused() =>
        Should.Throw<ArgumentException>(() => ResizeScale.FromReduced(2002, 4000));

    /// <summary>An enlargement without its authority cannot be made executable (§9, §12).</summary>
    /// <remarks>
    /// Not a check a consumer performs — a value a consumer cannot build. By the time a
    /// preparation exists, its existence is already the proof that a human authorised this exact
    /// enlargement of this exact source, so no adapter, mapper or screen has to ask.
    /// </remarks>
    [Fact]
    public void An_enlargement_preparation_cannot_be_built_without_its_authority()
    {
        TargetEdgePrintPreparationPlan enlarging = TargetEdgePrintPreparationPlan.For(
            Revision, Bytes, 2000, 1000,
            FlexibleSizeSelection.CustomTarget(TargetEdge.Width, 300m));

        enlarging.RequiresEnlargementAuthority.ShouldBeTrue();

        Should.Throw<ArgumentException>(() => new TargetEdgePreparation(enlarging, authority: null));
        new TargetEdgePreparation(enlarging, EnlargementAuthority.For(enlarging))
            .IsAuthorisedEnlargement.ShouldBeTrue();
    }

    /// <summary>
    /// An authority granted for a different target cannot be attached to this one (§9).
    /// </summary>
    [Fact]
    public void An_authority_for_a_different_target_cannot_be_attached()
    {
        TargetEdgePrintPreparationPlan three_hundred = TargetEdgePrintPreparationPlan.For(
            Revision, Bytes, 2000, 1000,
            FlexibleSizeSelection.CustomTarget(TargetEdge.Width, 300m));
        TargetEdgePrintPreparationPlan three_twenty = TargetEdgePrintPreparationPlan.For(
            Revision, Bytes, 2000, 1000,
            FlexibleSizeSelection.CustomTarget(TargetEdge.Width, 320m));

        Should.Throw<ArgumentException>(() =>
            new TargetEdgePreparation(three_twenty, EnlargementAuthority.For(three_hundred)));
    }

    /// <summary>
    /// Permission to enlarge cannot be attached to a run that does not enlarge (§9, §11).
    /// </summary>
    /// <remarks>
    /// A leftover authority beside a shrink is not harmless: it reads as a human having agreed to
    /// add pixels to a job that removes them, and it is exactly the shape a stale row takes after
    /// a target changes.
    /// </remarks>
    [Fact]
    public void An_authority_cannot_be_attached_to_a_shrink()
    {
        TargetEdgePrintPreparationPlan enlarging = TargetEdgePrintPreparationPlan.For(
            Revision, Bytes, 2000, 1000,
            FlexibleSizeSelection.CustomTarget(TargetEdge.Width, 300m));
        TargetEdgePrintPreparationPlan shrinking = TargetEdgePrintPreparationPlan.For(
            Revision, Bytes, 2000, 1000,
            FlexibleSizeSelection.CustomTarget(TargetEdge.Width, 60m));

        Should.Throw<ArgumentException>(() =>
            new TargetEdgePreparation(shrinking, EnlargementAuthority.For(enlarging)));
    }

    /// <summary>
    /// A stored selection whose parts describe no single decision is refused (§6, §23).
    /// </summary>
    /// <remarks>
    /// Each case is a row that could only come from a partial write or a hand edit: a preset fit
    /// that also carries a custom edge, an override with no recommendation behind it, and a custom
    /// target that carries a recommendation without being marked an override. None of the three
    /// has a reading, and guessing which half to believe would be the software deciding what the
    /// operator meant.
    /// </remarks>
    [Fact]
    public void A_stored_selection_that_describes_no_single_decision_is_refused()
    {
        PresetPrintRecommendation a4 =
            PresetPrintRecommendation.MaximumLongEdge(SizePreset.A4, 280m);

        Should.Throw<ArgumentException>(() => FlexibleSizeSelection.Rehydrate(
            OperatorSizingMode.PresetFit, a4, presetOverridden: false,
            TargetEdge.LongEdge, 320m));

        Should.Throw<ArgumentException>(() => FlexibleSizeSelection.Rehydrate(
            OperatorSizingMode.PresetFit, recommendation: null, presetOverridden: false,
            selectedTargetEdge: null, requestedMillimetres: null));

        Should.Throw<ArgumentException>(() => FlexibleSizeSelection.Rehydrate(
            OperatorSizingMode.CustomTargetEdge, recommendation: null, presetOverridden: true,
            TargetEdge.LongEdge, 320m));

        Should.Throw<ArgumentException>(() => FlexibleSizeSelection.Rehydrate(
            OperatorSizingMode.CustomTargetEdge, a4, presetOverridden: false,
            TargetEdge.LongEdge, 320m));

        Should.Throw<ArgumentException>(() => FlexibleSizeSelection.Rehydrate(
            OperatorSizingMode.CustomTargetEdge, recommendation: null, presetOverridden: false,
            TargetEdge.LongEdge, requestedMillimetres: null));
    }

    /// <summary>
    /// A stored preset fit keeps the recommendation it was made against (§6, §19).
    /// </summary>
    /// <remarks>
    /// The configured limit is not re-derived on read. If it were, a decision recorded under
    /// v1.11.0 would silently become a decision under whatever is configured when the session is
    /// reopened — which is exactly the retroactive reinterpretation §19 draws a line under.
    /// </remarks>
    [Fact]
    public void A_stored_preset_fit_keeps_the_recommendation_it_was_made_against()
    {
        PresetPrintRecommendation asRecorded =
            PresetPrintRecommendation.MaximumLongEdge(SizePreset.A4, 280m);

        FlexibleSizeSelection restored = FlexibleSizeSelection.Rehydrate(
            OperatorSizingMode.PresetFit, asRecorded, presetOverridden: false,
            selectedTargetEdge: null, requestedMillimetres: null);

        restored.Recommendation.ShouldBe(asRecorded);
        restored.ConfiguredPresetLimitMm.ShouldBe(280m);
    }
}
