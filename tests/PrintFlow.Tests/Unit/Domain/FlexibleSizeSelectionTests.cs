using System.Reflection;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;

namespace PrintFlow.Tests.Unit.Domain;

public sealed class FlexibleSizeSelectionTests
{
    private static readonly RevisionId Revision =
        RevisionId.From(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

    private static readonly Sha256 Bytes = Sha256.Parse(new string('A', 64));

    /// <summary>
    /// The configured A4 recommendation, as v1.11.0 states it: a 280 mm maximum long edge, not
    /// the 297 mm ISO page the preset is named after (Epic 11400 Part B1A.2D §3, §4).
    /// </summary>
    private static readonly PresetPrintRecommendation A4Recommendation =
        PresetPrintRecommendation.MaximumLongEdge(SizePreset.A4, 280m);

    private static readonly PresetPrintRecommendation A5Recommendation =
        PresetPrintRecommendation.MaximumShortEdge(SizePreset.A5, 135m);

    [Fact]
    public void Named_preset_without_override_uses_preset_authority_and_exposes_no_axis()
    {
        FlexibleSizeSelection selection = FlexibleSizeSelection.PresetFit(A4Recommendation);

        selection.Mode.ShouldBe(OperatorSizingMode.PresetFit);
        selection.BasedOnPreset.ShouldBe(SizePreset.A4);
        selection.PresetOverridden.ShouldBeFalse();
        selection.SelectedTargetEdge.ShouldBeNull();
        selection.RequestedMillimetres.ShouldBeNull();

        // The recommendation the operator was shown is retained even when nothing overrode it,
        // so a persisted preset decision says which configured limit it was made against and is
        // never re-derived from a paper standard afterwards (Part B1A.2D §3, §6).
        selection.Recommendation.ShouldBe(A4Recommendation);
        selection.Recommendation!.MaxLongEdgeMm.ShouldBe(280m);
    }

    [Fact]
    public void Preset_override_retains_both_recommendation_and_exact_request()
    {
        FlexibleSizeSelection selection =
            FlexibleSizeSelection.OverridePreset(A4Recommendation, TargetEdge.LongEdge, 320m);

        selection.Mode.ShouldBe(OperatorSizingMode.CustomTargetEdge);
        selection.BasedOnPreset.ShouldBe(SizePreset.A4);
        selection.PresetOverridden.ShouldBeTrue();
        selection.Recommendation!.MaxLongEdgeMm.ShouldBe(280m);
        selection.SelectedTargetEdge.ShouldBe(TargetEdge.LongEdge);
        selection.RequestedMillimetres.ShouldBe(320m);
    }

    [Fact]
    public void Preset_limit_can_be_exceeded_without_exceeding_source_capacity()
    {
        TargetEdgePrintPreparationPlan plan = TargetEdgePrintPreparationPlan.For(
            Revision,
            Bytes,
            6000,
            4000,
            FlexibleSizeSelection.OverridePreset(A4Recommendation, TargetEdge.LongEdge, 320m));

        plan.Semantics.ShouldBe(PrintDimensionSemantics.TargetEdgeV1);
        plan.PresetLimitExceeded.ShouldBeTrue();
        plan.SourceCapacityExceeded.ShouldBeFalse();
        plan.RequiresEnlargementAuthority.ShouldBeFalse();
        plan.IsExecutableWith(authority: null).ShouldBeTrue();
    }

    [Fact]
    public void Preset_limit_and_source_capacity_are_independent_classifications()
    {
        TargetEdgePrintPreparationPlan plan = TargetEdgePrintPreparationPlan.For(
            Revision,
            Bytes,
            3000,
            2000,
            FlexibleSizeSelection.OverridePreset(A4Recommendation, TargetEdge.LongEdge, 320m));

        plan.PresetLimitExceeded.ShouldBeTrue();
        plan.SourceCapacityExceeded.ShouldBeTrue();
        plan.RequiresEnlargementAuthority.ShouldBeTrue();
    }

    [Fact]
    public void An_A5_scalar_over_135_can_project_inside_the_short_edge_recommendation()
    {
        TargetEdgePrintPreparationPlan plan = TargetEdgePrintPreparationPlan.For(
            Revision,
            Bytes,
            6000,
            1500,
            FlexibleSizeSelection.OverridePreset(A5Recommendation, TargetEdge.Width, 160m));

        plan.Projection.ProjectedPixelWidth.ShouldBe(1890);
        plan.Projection.ProjectedPixelHeight.ShouldBe(473);
        plan.PresetLimitExceeded.ShouldBeFalse();
        plan.SourceCapacityExceeded.ShouldBeFalse();
    }

    [Fact]
    public void An_A5_projection_over_135_on_its_short_edge_exceeds_the_recommendation()
    {
        TargetEdgePrintPreparationPlan plan = TargetEdgePrintPreparationPlan.For(
            Revision,
            Bytes,
            4000,
            8000,
            FlexibleSizeSelection.OverridePreset(A5Recommendation, TargetEdge.Width, 160m));

        plan.Projection.ProjectedPixelWidth.ShouldBe(1890);
        plan.Projection.ProjectedPixelHeight.ShouldBe(3780);
        plan.PresetLimitExceeded.ShouldBeTrue();
        plan.SourceCapacityExceeded.ShouldBeFalse();
    }

    [Fact]
    public void An_ordinary_preset_cannot_be_reinterpreted_as_TargetEdgeV1()
    {
        FlexibleSizeSelection preset = FlexibleSizeSelection.PresetFit(A4Recommendation);

        Should.Throw<ArgumentException>(() =>
            TargetEdgePrintPreparationPlan.For(Revision, Bytes, 6000, 4000, preset));
    }

    [Fact]
    public void Operator_selection_has_no_resampling_or_interpolation_choice()
    {
        string[] propertyNames = typeof(FlexibleSizeSelection)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToArray();
        Type[] factoryParameterTypes = typeof(FlexibleSizeSelection)
            .GetMethods(BindingFlags.Static | BindingFlags.Public)
            .SelectMany(method => method.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        propertyNames.ShouldNotContain(name =>
            name.Contains("Resampl", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Interpol", StringComparison.OrdinalIgnoreCase));
        factoryParameterTypes.ShouldNotContain(typeof(PhotoshopResizeMode));
    }
}
