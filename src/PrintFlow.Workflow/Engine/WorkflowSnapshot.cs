using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Definitions;

namespace PrintFlow.Workflow.Engine;

/// <summary>
/// Everything the workflow engine is allowed to see about one session.
/// </summary>
/// <remarks>
/// This is the reducer's entire input state. It contains no file handles, no repository, no
/// clock and no adapter — which is what makes the engine testable without a database or a
/// disk (Epic 11100 plan §8.1).
///
/// <see cref="HasDerivedRevision"/> is the fact that freezes the workflow choice: once any
/// non-root Revision exists, the session cannot switch workflows (MVP design §6.1).
/// </remarks>
/// <param name="SessionId">Which session this describes.</param>
/// <param name="WorkflowType">The selected workflow.</param>
/// <param name="OutputName">The current editable output name.</param>
/// <param name="SessionState">Session-level lifecycle state.</param>
/// <param name="Steps">One entry per step of the workflow definition, in ordinal order.</param>
/// <param name="HasDerivedRevision">Whether any Revision beyond the import root exists.</param>
/// <param name="LatestApprovedRevisionId">The most recent approved Revision, if any.</param>
/// <param name="Dimensions">Confirmed print dimensions, once the operator has set them.</param>
/// <param name="WhiteUnderbaseBranch">
/// The explicitly chosen W1 branch. Null means not chosen — there is deliberately no default.
/// </param>
/// <param name="ApprovedPrintOutputCount">How many approved PrintOutputs this session holds.</param>
public sealed record WorkflowSnapshot(
    SessionId SessionId,
    WorkflowType WorkflowType,
    OutputName OutputName,
    SessionState SessionState,
    IReadOnlyList<SessionStep> Steps,
    bool HasDerivedRevision,
    RevisionId? LatestApprovedRevisionId,
    PrintDimensions? Dimensions,
    WhiteUnderbaseBranch? WhiteUnderbaseBranch,
    int ApprovedPrintOutputCount)
{
    /// <summary>The definition this snapshot is being driven against.</summary>
    public WorkflowDefinition Definition => WorkflowCatalog.For(WorkflowType);

    /// <summary>
    /// Value equality, including the step list element by element.
    /// </summary>
    /// <remarks>
    /// The compiler-generated comparison would use reference equality for
    /// <see cref="Steps"/>, so two snapshots describing an identical session would compare
    /// unequal purely because the lists were built separately. That would quietly weaken
    /// every "the state did not change" and "reload restores the same state" assertion, so
    /// the comparison is written out.
    /// </remarks>
    public bool Equals(WorkflowSnapshot? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return SessionId.Equals(other.SessionId)
            && WorkflowType == other.WorkflowType
            && OutputName.Equals(other.OutputName)
            && SessionState == other.SessionState
            && HasDerivedRevision == other.HasDerivedRevision
            && Nullable.Equals(LatestApprovedRevisionId, other.LatestApprovedRevisionId)
            && Nullable.Equals(Dimensions, other.Dimensions)
            && Nullable.Equals(WhiteUnderbaseBranch, other.WhiteUnderbaseBranch)
            && ApprovedPrintOutputCount == other.ApprovedPrintOutputCount
            && Steps.SequenceEqual(other.Steps);
    }

    public override int GetHashCode()
    {
        HashCode hash = default;
        hash.Add(SessionId);
        hash.Add(WorkflowType);
        hash.Add(OutputName);
        hash.Add(SessionState);
        hash.Add(HasDerivedRevision);
        hash.Add(LatestApprovedRevisionId);
        hash.Add(Dimensions);
        hash.Add(WhiteUnderbaseBranch);
        hash.Add(ApprovedPrintOutputCount);

        foreach (SessionStep step in Steps)
        {
            hash.Add(step);
        }

        return hash.ToHashCode();
    }

    /// <summary>Returns the step entry for <paramref name="kind"/>, or null when absent from this workflow.</summary>
    public SessionStep? Step(StepKind kind)
    {
        foreach (SessionStep step in Steps)
        {
            if (step.Step == kind)
            {
                return step;
            }
        }

        return null;
    }

    /// <summary>
    /// The step the operator is expected to act on: the first that is neither Approved nor
    /// Skipped. Null when every step is finished and only <c>Complete</c> remains.
    /// </summary>
    public SessionStep? CurrentStep
    {
        get
        {
            foreach (SessionStep step in Steps)
            {
                if (!step.IsFinished)
                {
                    return step;
                }
            }

            return null;
        }
    }

    /// <summary>True when every step of the workflow is Approved or Skipped.</summary>
    public bool AllStepsFinished => CurrentStep is null;

    /// <summary>Creates the initial snapshot for a workflow, with every step Waiting.</summary>
    public static WorkflowSnapshot Create(
        SessionId sessionId,
        WorkflowType workflowType,
        OutputName outputName,
        DateTimeOffset nowUtc)
    {
        WorkflowDefinition definition = WorkflowCatalog.For(workflowType);
        List<SessionStep> steps = new(definition.Steps.Count);
        foreach (StepDefinition step in definition.Steps)
        {
            steps.Add(new SessionStep(
                step.Kind,
                step.Ordinal,
                StepState.Waiting,
                CurrentRevisionId: null,
                CurrentRevisionSha256: null,
                SkipReason: null,
                AttemptCount: 0,
                nowUtc));
        }

        return new WorkflowSnapshot(
            sessionId,
            workflowType,
            outputName,
            SessionState.Active,
            steps,
            HasDerivedRevision: false,
            LatestApprovedRevisionId: null,
            Dimensions: null,
            WhiteUnderbaseBranch: null,
            ApprovedPrintOutputCount: 0);
    }

    /// <summary>
    /// The most recent Revision offered by a step strictly before <paramref name="kind"/>.
    /// </summary>
    /// <remarks>
    /// This is what a step consumes as input. Skipped steps hold no Revision, so the search
    /// naturally falls through to the last step that actually produced one — which is exactly
    /// the rule "skipping a step makes the downstream input the last approved upstream
    /// Revision" (Epic 11100 plan §17.1), with no special case needed.
    /// </remarks>
    public RevisionId? UpstreamRevisionOf(StepKind kind)
    {
        RevisionId? found = null;
        foreach (SessionStep step in Steps)
        {
            if (step.Step == kind)
            {
                break;
            }

            if (step.CurrentRevisionId is not null)
            {
                found = step.CurrentRevisionId;
            }
        }

        return found;
    }

    /// <summary>Returns a copy with <paramref name="replacement"/> substituted for its step.</summary>
    public WorkflowSnapshot WithStep(SessionStep replacement)
    {
        List<SessionStep> steps = new(Steps.Count);
        foreach (SessionStep step in Steps)
        {
            steps.Add(step.Step == replacement.Step ? replacement : step);
        }

        return this with { Steps = steps };
    }
}
