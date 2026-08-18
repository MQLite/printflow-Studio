namespace PrintFlow.Workflow.Commands;

/// <summary>Why the engine refused a command.</summary>
/// <remarks>
/// A rejection means the caller asked for something the rules forbid. Because the UI renders
/// only the commands the engine reports as available, a rejection in production indicates a
/// defect rather than a production event — which is exactly why it is a separate concept
/// from <c>OperationFailure</c> (Epic 11100 plan §9.3).
/// </remarks>
public enum RejectionCode
{
    /// <summary>The session is not Active, so ordinary progression is not legal.</summary>
    SessionNotActive,

    /// <summary>The requested step is not part of this session's workflow.</summary>
    StepNotInWorkflow,

    /// <summary>The command targeted a step other than the one the session is on.</summary>
    NotCurrentStep,

    /// <summary>The step's current state does not permit this command.</summary>
    IllegalStateTransition,

    /// <summary>The step is not marked skippable.</summary>
    StepNotSkippable,

    /// <summary>The workflow may no longer be changed because a derived Revision exists.</summary>
    WorkflowLocked,

    /// <summary>A required input was missing or invalid.</summary>
    InvalidPayload,

    /// <summary>A precondition of the command has not been satisfied.</summary>
    PreconditionNotMet,

    /// <summary>Completion was requested while required steps remain unfinished.</summary>
    WorkflowNotComplete,

    /// <summary>The command does not apply to this workflow at all.</summary>
    CommandNotApplicable,
}

/// <summary>
/// A refusal: no state changed and no effect was produced.
/// </summary>
/// <param name="Code">Stable machine-readable reason.</param>
/// <param name="DebugMessage">English detail for the log. Never shown raw to the operator.</param>
public sealed record CommandRejection(RejectionCode Code, string DebugMessage)
{
    public override string ToString() => $"{Code}: {DebugMessage}";
}
