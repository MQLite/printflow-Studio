namespace PrintFlow.Domain.Sessions;

/// <summary>The three fixed workflows (MVP design §6.1). There is no fourth, and none is configurable.</summary>
public enum WorkflowType
{
    /// <summary>Prepare Design Asset — produces an approved transparent PNG.</summary>
    PrepareAsset,

    /// <summary>Prepare Customer Design — processes a customer design into a production TIFF.</summary>
    PrepareCustomerDesign,

    /// <summary>Generate Print TIFF — produces a TIFF from a shop-created finished design.</summary>
    GeneratePrintTiff,
}

/// <summary>
/// The kinds of step a workflow can contain (Epic 11100 plan §7.2).
/// </summary>
/// <remarks>
/// Reviews are <b>not</b> step kinds. A review is the <c>ReviewRequired</c> phase of the step
/// that produced the result, and validation is the gate that decides whether an attempt
/// produces a valid artefact at all — never a node (plan §7.1). A later UI may still show a
/// dedicated review screen per reviewed step.
/// </remarks>
public enum StepKind
{
    Import,
    OriginalConfirmation,
    Enhancement,
    BackgroundRemoval,

    /// <summary>
    /// Deterministic canvas trimming. Defined here so the workflow shape is complete; the
    /// alpha-bound algorithm, manual crop and trim review belong to Epic 11200.
    /// </summary>
    Trim,

    ApprovedPngExport,
    PrintDimensions,
    PhotoshopOutput,
}

/// <summary>
/// Session-level lifecycle. Deliberately separate from step state: conflating the two is
/// the classic modelling error here (Epic 11100 plan §8.2).
/// </summary>
public enum SessionState
{
    /// <summary>The operator is working through the workflow.</summary>
    Active,

    /// <summary>Automation ended and the work was transferred to the operator (MVP design §6.5).</summary>
    HandedOff,

    /// <summary>All required outputs passed final review.</summary>
    Completed,

    /// <summary>The operator abandoned the session. Files are retained.</summary>
    Abandoned,
}

/// <summary>Per-step lifecycle (Epic 11100 plan §8.2–§8.3).</summary>
public enum StepState
{
    /// <summary>The step has not started.</summary>
    Waiting,

    /// <summary>An attempt is running.</summary>
    Processing,

    /// <summary>A validated result is waiting for a human decision.</summary>
    ReviewRequired,

    /// <summary>The current result passed review.</summary>
    Approved,

    /// <summary>The current result was rejected; a new attempt is required.</summary>
    RetryRequired,

    /// <summary>The step was unnecessary because the file already satisfied it. Creates no Revision.</summary>
    Skipped,

    /// <summary>Automation did not produce a valid result.</summary>
    Failed,

    /// <summary>The application or computer stopped unexpectedly during an attempt.</summary>
    Interrupted,
}
