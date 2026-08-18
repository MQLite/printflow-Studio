using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;

namespace PrintFlow.Workflow.Definitions;

/// <summary>
/// The three fixed workflows (MVP design §6).
/// </summary>
/// <remarks>
/// This class is the entire workflow "configuration" of PrintFlow Studio. There is no
/// designer, no database table, and no settings file behind it.
/// </remarks>
public static class WorkflowCatalog
{
    /// <summary>
    /// Prepare Design Asset: photo or logo in, approved transparent PNG out.
    /// No print dimensions and no TIFF (MVP design §6.2).
    /// </summary>
    public static readonly WorkflowDefinition PrepareAsset = new(
        WorkflowType.PrepareAsset,
        [
            new StepDefinition(
                StepKind.Import, 0,
                IsSkippable: false, RequiresReview: false, ProducesRevision: true,
                OperationKind.Import, AdapterKind.None),

            new StepDefinition(
                StepKind.OriginalConfirmation, 1,
                IsSkippable: false, RequiresReview: false, ProducesRevision: false,
                Operation: null, AdapterKind.None),

            new StepDefinition(
                StepKind.Enhancement, 2,
                IsSkippable: true, RequiresReview: true, ProducesRevision: true,
                OperationKind.Enhance, AdapterKind.Meitu),

            new StepDefinition(
                StepKind.BackgroundRemoval, 3,
                IsSkippable: true, RequiresReview: true, ProducesRevision: true,
                OperationKind.RemoveBackground, AdapterKind.Meitu),

            // Defined here so the workflow shape is complete and testable. The alpha-bound
            // algorithm, manual crop and trim review are Epic 11200.
            new StepDefinition(
                StepKind.Trim, 4,
                IsSkippable: false, RequiresReview: true, ProducesRevision: true,
                OperationKind.Trim, AdapterKind.Internal),

            // Promotion copies the approved trimmed bytes into the Approved area unchanged,
            // so the existing hash-bound approval already covers the promoted file. No
            // second review is fabricated; the Revision is still recorded so the chain stays
            // complete (Epic 11100 plan §7.3).
            new StepDefinition(
                StepKind.ApprovedPngExport, 5,
                IsSkippable: false, RequiresReview: false, ProducesRevision: true,
                OperationKind.PromoteApproved, AdapterKind.None),
        ]);

    /// <summary>
    /// Prepare Customer Design: a customer-composed design becomes a production TIFF
    /// (MVP design §6.3). Enhancement always precedes background removal.
    /// </summary>
    public static readonly WorkflowDefinition PrepareCustomerDesign = new(
        WorkflowType.PrepareCustomerDesign,
        [
            new StepDefinition(
                StepKind.Import, 0,
                IsSkippable: false, RequiresReview: false, ProducesRevision: true,
                OperationKind.Import, AdapterKind.None),

            new StepDefinition(
                StepKind.OriginalConfirmation, 1,
                IsSkippable: false, RequiresReview: false, ProducesRevision: false,
                Operation: null, AdapterKind.None),

            new StepDefinition(
                StepKind.Enhancement, 2,
                IsSkippable: true, RequiresReview: true, ProducesRevision: true,
                OperationKind.Enhance, AdapterKind.Meitu),

            // Skippable because a finished design may intentionally contain a background.
            new StepDefinition(
                StepKind.BackgroundRemoval, 3,
                IsSkippable: true, RequiresReview: true, ProducesRevision: true,
                OperationKind.RemoveBackground, AdapterKind.Meitu),

            new StepDefinition(
                StepKind.Trim, 4,
                IsSkippable: false, RequiresReview: true, ProducesRevision: true,
                OperationKind.Trim, AdapterKind.Internal),

            new StepDefinition(
                StepKind.PrintDimensions, 5,
                IsSkippable: false, RequiresReview: false, ProducesRevision: false,
                Operation: null, AdapterKind.None),

            // TIFF validation is the gate inside this step, and final review is this step's
            // ReviewRequired phase — neither is a separate node (plan §7.1).
            // Production implementation is Epic 11400.
            new StepDefinition(
                StepKind.PhotoshopOutput, 6,
                IsSkippable: false, RequiresReview: true, ProducesRevision: true,
                OperationKind.PhotoshopOutput, AdapterKind.Photoshop),
        ]);

    /// <summary>
    /// Generate Print TIFF: a shop-created finished design becomes a production TIFF
    /// (MVP design §6.4). No Meitu step and no automatic trim exist in this definition.
    /// </summary>
    public static readonly WorkflowDefinition GeneratePrintTiff = new(
        WorkflowType.GeneratePrintTiff,
        [
            new StepDefinition(
                StepKind.Import, 0,
                IsSkippable: false, RequiresReview: false, ProducesRevision: true,
                OperationKind.Import, AdapterKind.None),

            // The design-readiness review: this workflow assumes a finished design, so the
            // operator must actively confirm it rather than merely acknowledge it.
            new StepDefinition(
                StepKind.OriginalConfirmation, 1,
                IsSkippable: false, RequiresReview: true, ProducesRevision: false,
                Operation: null, AdapterKind.None),

            new StepDefinition(
                StepKind.PrintDimensions, 2,
                IsSkippable: false, RequiresReview: false, ProducesRevision: false,
                Operation: null, AdapterKind.None),

            new StepDefinition(
                StepKind.PhotoshopOutput, 3,
                IsSkippable: false, RequiresReview: true, ProducesRevision: true,
                OperationKind.PhotoshopOutput, AdapterKind.Photoshop),
        ]);

    /// <summary>Every fixed workflow, in menu order.</summary>
    public static readonly IReadOnlyList<WorkflowDefinition> All =
        [PrepareAsset, PrepareCustomerDesign, GeneratePrintTiff];

    /// <summary>Returns the definition for <paramref name="type"/>.</summary>
    public static WorkflowDefinition For(WorkflowType type) => type switch
    {
        WorkflowType.PrepareAsset => PrepareAsset,
        WorkflowType.PrepareCustomerDesign => PrepareCustomerDesign,
        WorkflowType.GeneratePrintTiff => GeneratePrintTiff,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown workflow type."),
    };
}
