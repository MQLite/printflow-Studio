using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;

namespace PrintFlow.Workflow.Definitions;

/// <summary>Which external application, if any, performs a step's work.</summary>
public enum AdapterKind
{
    /// <summary>No adapter: the step is an operator decision or an in-process file promotion.</summary>
    None,

    /// <summary>Meitu screen automation (Epic 11300). Only fake adapters exist today.</summary>
    Meitu,

    /// <summary>Photoshop screen automation (Epic 11400). Only fake adapters exist today.</summary>
    Photoshop,

    /// <summary>Deterministic in-process image work (Epic 11200 implements trimming).</summary>
    Internal,
}

/// <summary>
/// One step of a fixed workflow, with the flags that drive every legality rule.
/// </summary>
/// <param name="Kind">Which step this is.</param>
/// <param name="Ordinal">Zero-based position in the workflow.</param>
/// <param name="IsSkippable">
/// Whether the operator may skip the step because the file already satisfies it. Only
/// Enhancement and BackgroundRemoval are skippable (MVP design §6.2–§6.3).
/// </param>
/// <param name="RequiresReview">
/// Whether a validated result must pass a human decision before the workflow advances.
/// This is what makes the step's lifecycle include <c>ReviewRequired</c>; it does not add
/// a separate review step (Epic 11100 plan §7.1).
/// </param>
/// <param name="ProducesRevision">Whether a successful attempt creates a new Revision.</param>
/// <param name="Operation">The operation recorded on the Revision this step produces.</param>
/// <param name="Adapter">Which adapter performs the work.</param>
public sealed record StepDefinition(
    StepKind Kind,
    int Ordinal,
    bool IsSkippable,
    bool RequiresReview,
    bool ProducesRevision,
    OperationKind? Operation,
    AdapterKind Adapter)
{
    /// <summary>True when starting this step invokes an external application adapter.</summary>
    public bool IsAdapterBacked => Adapter is AdapterKind.Meitu or AdapterKind.Photoshop;
}
