using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;

namespace PrintFlow.Tests.Unit.Domain;

public sealed class EnlargementAuthorityTests
{
    private static readonly RevisionId Revision =
        RevisionId.From(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

    private static readonly RevisionId OtherRevision =
        RevisionId.From(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));

    private static readonly Sha256 Bytes = Sha256.Parse(new string('A', 64));

    private static readonly Sha256 OtherBytes = Sha256.Parse(new string('B', 64));

    [Fact]
    public void Enlargement_is_refused_until_the_exact_plan_is_authorised()
    {
        TargetEdgePrintPreparationPlan plan = Plan(Revision, Bytes, TargetEdge.Width, 320m);

        plan.RequiresEnlargementAuthority.ShouldBeTrue();
        plan.IsExecutableWith(authority: null).ShouldBeFalse();

        EnlargementAuthority authority = EnlargementAuthority.For(plan);
        authority.Authorises(plan).ShouldBeTrue();
        plan.IsExecutableWith(authority).ShouldBeTrue();
    }

    [Fact]
    public void Authority_records_every_exact_image_and_target_binding()
    {
        TargetEdgePrintPreparationPlan plan = Plan(Revision, Bytes, TargetEdge.Width, 320m);
        EnlargementAuthority authority = EnlargementAuthority.For(plan);

        authority.SourceRevisionId.ShouldBe(Revision);
        authority.SourceSha256.ShouldBe(Bytes);
        authority.SizingMode.ShouldBe(OperatorSizingMode.CustomTargetEdge);
        authority.SelectedTargetEdge.ShouldBe(TargetEdge.Width);
        authority.RequestedMillimetres.ShouldBe(320m);
        authority.ProjectedScale.ShouldBe(plan.Projection.ProjectedScale);
        authority.ProjectedTargetPixelWidth.ShouldBe(plan.Projection.ProjectedPixelWidth);
        authority.ProjectedTargetPixelHeight.ShouldBe(plan.Projection.ProjectedPixelHeight);
    }

    [Fact]
    public void Stale_revision_invalidates_authority()
    {
        EnlargementAuthority authority = EnlargementAuthority.For(Plan(Revision, Bytes, TargetEdge.Width, 320m));

        authority.Authorises(Plan(OtherRevision, Bytes, TargetEdge.Width, 320m)).ShouldBeFalse();
    }

    [Fact]
    public void Changed_hash_under_the_same_revision_invalidates_authority()
    {
        EnlargementAuthority authority = EnlargementAuthority.For(Plan(Revision, Bytes, TargetEdge.Width, 320m));

        authority.Authorises(Plan(Revision, OtherBytes, TargetEdge.Width, 320m)).ShouldBeFalse();
    }

    [Fact]
    public void Changed_requested_millimetres_invalidates_authority()
    {
        EnlargementAuthority authority = EnlargementAuthority.For(Plan(Revision, Bytes, TargetEdge.Width, 320m));

        authority.Authorises(Plan(Revision, Bytes, TargetEdge.Width, 321m)).ShouldBeFalse();
    }

    [Fact]
    public void Changed_selected_edge_invalidates_authority()
    {
        EnlargementAuthority authority = EnlargementAuthority.For(Plan(Revision, Bytes, TargetEdge.Width, 320m));

        authority.Authorises(Plan(Revision, Bytes, TargetEdge.Height, 320m)).ShouldBeFalse();
    }

    [Fact]
    public void Non_enlargement_cannot_acquire_enlargement_authority()
    {
        TargetEdgePrintPreparationPlan shrink = TargetEdgePrintPreparationPlan.For(
            Revision,
            Bytes,
            6000,
            4000,
            FlexibleSizeSelection.CustomTarget(TargetEdge.Width, 254m));

        shrink.IsExecutableWith(authority: null).ShouldBeTrue();
        Should.Throw<ArgumentException>(() => EnlargementAuthority.For(shrink));
    }

    private static TargetEdgePrintPreparationPlan Plan(
        RevisionId revision,
        Sha256 hash,
        TargetEdge edge,
        decimal millimetres) =>
        TargetEdgePrintPreparationPlan.For(
            revision,
            hash,
            3000,
            2000,
            FlexibleSizeSelection.CustomTarget(edge, millimetres));
}
