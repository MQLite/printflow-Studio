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

    [Fact]
    public void Named_preset_without_override_uses_preset_authority_and_exposes_no_axis()
    {
        FlexibleSizeSelection selection = FlexibleSizeSelection.PresetFit(SizePreset.A4);

        selection.Mode.ShouldBe(OperatorSizingMode.PresetFit);
        selection.BasedOnPreset.ShouldBe(SizePreset.A4);
        selection.PresetOverridden.ShouldBeFalse();
        selection.SelectedTargetEdge.ShouldBeNull();
        selection.RequestedMillimetres.ShouldBeNull();
        selection.ConfiguredPresetLimitMm.ShouldBeNull();
    }

    [Fact]
    public void Preset_override_retains_both_recommendation_and_exact_request()
    {
        FlexibleSizeSelection selection =
            FlexibleSizeSelection.OverridePreset(SizePreset.A4, 280m, TargetEdge.LongEdge, 320m);

        selection.Mode.ShouldBe(OperatorSizingMode.CustomTargetEdge);
        selection.BasedOnPreset.ShouldBe(SizePreset.A4);
        selection.PresetOverridden.ShouldBeTrue();
        selection.ConfiguredPresetLimitMm.ShouldBe(280m);
        selection.SelectedTargetEdge.ShouldBe(TargetEdge.LongEdge);
        selection.RequestedMillimetres.ShouldBe(320m);
        selection.PresetLimitExceeded.ShouldBeTrue();
    }

    [Fact]
    public void Preset_limit_can_be_exceeded_without_exceeding_source_capacity()
    {
        TargetEdgePrintPreparationPlan plan = TargetEdgePrintPreparationPlan.For(
            Revision,
            Bytes,
            6000,
            4000,
            FlexibleSizeSelection.OverridePreset(SizePreset.A4, 280m, TargetEdge.LongEdge, 320m));

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
            FlexibleSizeSelection.OverridePreset(SizePreset.A4, 280m, TargetEdge.LongEdge, 320m));

        plan.PresetLimitExceeded.ShouldBeTrue();
        plan.SourceCapacityExceeded.ShouldBeTrue();
        plan.RequiresEnlargementAuthority.ShouldBeTrue();
    }

    [Fact]
    public void An_ordinary_preset_cannot_be_reinterpreted_as_TargetEdgeV1()
    {
        FlexibleSizeSelection preset = FlexibleSizeSelection.PresetFit(SizePreset.A4);

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
