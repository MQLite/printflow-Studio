using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Definitions;

namespace PrintFlow.Tests.Unit.Workflow;

/// <summary>
/// The three fixed workflows must have exactly the steps, order and flags the confirmed
/// design specifies (MVP design §6.2–§6.4). These assertions are the product decision itself,
/// so a change here should require a deliberate edit and a code review.
/// </summary>
public sealed class WorkflowShapeTests
{
    [Fact]
    public void PrepareAsset_has_the_confirmed_steps_in_order()
    {
        WorkflowCatalog.PrepareAsset.Steps.Select(s => s.Kind).ShouldBe(
        [
            StepKind.Import,
            StepKind.OriginalConfirmation,
            StepKind.Enhancement,
            StepKind.BackgroundRemoval,
            StepKind.Trim,
            StepKind.ApprovedPngExport,
        ]);
    }

    [Fact]
    public void PrepareCustomerDesign_has_the_confirmed_steps_in_order()
    {
        WorkflowCatalog.PrepareCustomerDesign.Steps.Select(s => s.Kind).ShouldBe(
        [
            StepKind.Import,
            StepKind.OriginalConfirmation,
            StepKind.Enhancement,
            StepKind.BackgroundRemoval,
            StepKind.Trim,
            StepKind.PrintDimensions,
            StepKind.PhotoshopOutput,
        ]);
    }

    [Fact]
    public void GeneratePrintTiff_has_the_confirmed_steps_in_order()
    {
        WorkflowCatalog.GeneratePrintTiff.Steps.Select(s => s.Kind).ShouldBe(
        [
            StepKind.Import,
            StepKind.OriginalConfirmation,
            StepKind.PrintDimensions,
            StepKind.PhotoshopOutput,
        ]);
    }

    [Fact]
    public void GeneratePrintTiff_uses_no_Meitu_and_no_automatic_trim()
    {
        WorkflowDefinition definition = WorkflowCatalog.GeneratePrintTiff;

        definition.Contains(StepKind.Enhancement).ShouldBeFalse();
        definition.Contains(StepKind.BackgroundRemoval).ShouldBeFalse();
        definition.Contains(StepKind.Trim).ShouldBeFalse();
        definition.Steps.ShouldAllBe(s => s.Adapter != AdapterKind.Meitu);
    }

    [Fact]
    public void PrepareAsset_requests_no_print_dimensions_and_no_TIFF()
    {
        WorkflowDefinition definition = WorkflowCatalog.PrepareAsset;

        definition.Contains(StepKind.PrintDimensions).ShouldBeFalse();
        definition.Contains(StepKind.PhotoshopOutput).ShouldBeFalse();
        definition.Steps.ShouldAllBe(s => s.Adapter != AdapterKind.Photoshop);
        definition.Terminal.Kind.ShouldBe(StepKind.ApprovedPngExport);
    }

    [Theory]
    [InlineData(WorkflowType.PrepareAsset)]
    [InlineData(WorkflowType.PrepareCustomerDesign)]
    public void Enhancement_and_background_removal_are_the_only_skippable_steps(WorkflowType type)
    {
        WorkflowDefinition definition = WorkflowCatalog.For(type);

        definition.Steps.Where(s => s.IsSkippable).Select(s => s.Kind).ShouldBe(
            [StepKind.Enhancement, StepKind.BackgroundRemoval]);
    }

    [Fact]
    public void GeneratePrintTiff_has_no_skippable_step()
    {
        WorkflowCatalog.GeneratePrintTiff.Steps.ShouldAllBe(s => !s.IsSkippable);
    }

    [Theory]
    [InlineData(WorkflowType.PrepareAsset)]
    [InlineData(WorkflowType.PrepareCustomerDesign)]
    public void Enhancement_always_precedes_background_removal(WorkflowType type)
    {
        WorkflowDefinition definition = WorkflowCatalog.For(type);

        definition.IndexOf(StepKind.Enhancement)
            .ShouldBeLessThan(definition.IndexOf(StepKind.BackgroundRemoval));
    }

    [Theory]
    [InlineData(WorkflowType.PrepareAsset)]
    [InlineData(WorkflowType.PrepareCustomerDesign)]
    public void Trim_follows_background_removal_and_is_not_skippable(WorkflowType type)
    {
        WorkflowDefinition definition = WorkflowCatalog.For(type);

        definition.IndexOf(StepKind.BackgroundRemoval)
            .ShouldBeLessThan(definition.IndexOf(StepKind.Trim));
        definition.Find(StepKind.Trim)!.IsSkippable.ShouldBeFalse();
    }

    [Fact]
    public void PrepareCustomerDesign_orders_trim_before_dimensions_before_output()
    {
        WorkflowDefinition definition = WorkflowCatalog.PrepareCustomerDesign;

        definition.IndexOf(StepKind.Trim)
            .ShouldBeLessThan(definition.IndexOf(StepKind.PrintDimensions));
        definition.IndexOf(StepKind.PrintDimensions)
            .ShouldBeLessThan(definition.IndexOf(StepKind.PhotoshopOutput));
    }

    [Theory]
    [InlineData(WorkflowType.PrepareAsset)]
    [InlineData(WorkflowType.PrepareCustomerDesign)]
    [InlineData(WorkflowType.GeneratePrintTiff)]
    public void Every_step_has_a_contiguous_ordinal(WorkflowType type)
    {
        WorkflowDefinition definition = WorkflowCatalog.For(type);

        definition.Steps.Select(s => s.Ordinal).ShouldBe(Enumerable.Range(0, definition.Steps.Count));
    }

    [Theory]
    [InlineData(WorkflowType.PrepareAsset)]
    [InlineData(WorkflowType.PrepareCustomerDesign)]
    [InlineData(WorkflowType.GeneratePrintTiff)]
    public void Every_producing_step_declares_an_operation(WorkflowType type)
    {
        WorkflowCatalog.For(type).Steps
            .Where(s => s.ProducesRevision)
            .ShouldAllBe(s => s.Operation != null);
    }

    [Theory]
    [InlineData(WorkflowType.PrepareAsset)]
    [InlineData(WorkflowType.PrepareCustomerDesign)]
    [InlineData(WorkflowType.GeneratePrintTiff)]
    public void No_step_is_both_skippable_and_terminal(WorkflowType type)
    {
        WorkflowCatalog.For(type).Terminal.IsSkippable.ShouldBeFalse();
    }

    /// <summary>
    /// Reviews are phases of the step that produced the result, never separate nodes
    /// (Epic 11100 plan §7.1). A step kind mirroring a review screen would reintroduce the
    /// illegal state "output exists, unvalidated, but the workflow advanced".
    /// </summary>
    [Fact]
    public void No_step_kind_exists_merely_to_mirror_a_review_screen()
    {
        string[] forbidden =
        [
            "EnhancementReview",
            "BackgroundRemovalReview",
            "TrimReview",
            "TiffValidation",
            "FinalReview",
        ];

        IEnumerable<string> declared = Enum.GetNames<StepKind>();
        declared.Intersect(forbidden).ShouldBeEmpty();
    }

    [Fact]
    public void Trim_is_defined_but_carries_no_external_adapter()
    {
        // Epic 11100 defines the Trim step; the alpha-bound algorithm and the crop/review UI
        // are Epic 11200. Nothing here routes trimming through Meitu or Photoshop.
        foreach (WorkflowDefinition definition in WorkflowCatalog.All)
        {
            StepDefinition? trim = definition.Find(StepKind.Trim);
            if (trim is not null)
            {
                trim.Adapter.ShouldBe(AdapterKind.Internal);
                trim.Operation.ShouldBe(OperationKind.Trim);
                trim.RequiresReview.ShouldBeTrue();
            }
        }
    }

    [Fact]
    public void ApprovedPngExport_needs_no_second_review_because_the_bytes_do_not_change()
    {
        StepDefinition export = WorkflowCatalog.PrepareAsset.Find(StepKind.ApprovedPngExport)!;

        export.Operation.ShouldBe(OperationKind.PromoteApproved);
        export.RequiresReview.ShouldBeFalse();
        export.ProducesRevision.ShouldBeTrue();
    }

    [Fact]
    public void Catalog_exposes_exactly_the_three_fixed_workflows()
    {
        WorkflowCatalog.All.Count.ShouldBe(3);
        WorkflowCatalog.All.Select(w => w.Type).ShouldBe(Enum.GetValues<WorkflowType>());
    }
}
