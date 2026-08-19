using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Reviews;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Engine;

namespace PrintFlow.Workflow.Services;

/// <summary>
/// Everything persisted about one session, and the sole place that reconstructs a
/// <see cref="WorkflowSnapshot"/> from it (Epic 11100 Task 11108; plan §11.4).
/// </summary>
/// <remarks>
/// This is the load-time mirror of the effects the engine emits at run time: the same facts
/// the engine keeps in memory during a live session must be recoverable from these rows after
/// a restart, or "reload restores an identical <see cref="WorkflowSnapshot"/>" (plan §17.3)
/// could not hold.
/// </remarks>
public sealed record SessionAggregate(
    ProcessingSession Session,
    InputSnapshot? Snapshot,
    IReadOnlyList<SessionStep> Steps,
    IReadOnlyList<Revision> Revisions,
    IReadOnlyList<ProcessingAttempt> Attempts,
    IReadOnlyList<ReviewDecision> Reviews,
    IReadOnlyList<PrintOutput> Outputs)
{
    /// <summary>
    /// Rebuilds the engine's <see cref="WorkflowSnapshot"/> from persisted rows.
    /// </summary>
    /// <remarks>
    /// <see cref="WorkflowSnapshot.HasDerivedRevision"/>, <c>LatestApprovedRevisionId</c> and
    /// <c>ApprovedPrintOutputCount</c> are all re-derived from the current step/output rows
    /// rather than stored redundantly — the same rule the live engine itself follows
    /// (<c>WorkflowEngine.HasDerivedRevision</c>, and every approve/return-to-step handler
    /// recomputing <c>LatestApprovedRevisionId</c> from the step list). Recomputing here keeps
    /// there being exactly one definition of each rule instead of two that could drift apart.
    /// </remarks>
    public WorkflowSnapshot ToSnapshot()
    {
        bool hasDerivedRevision = Steps.Any(s => s.Step != StepKind.Import && s.CurrentRevisionId is not null);

        RevisionId? latestApproved = Steps
            .Where(s => s.State == StepState.Approved && s.CurrentRevisionId is not null)
            .OrderByDescending(s => s.Ordinal)
            .Select(s => s.CurrentRevisionId)
            .FirstOrDefault();

        int approvedOutputCount = Outputs.Count(o => o.ReviewState == ReviewState.Approved);

        return new WorkflowSnapshot(
            Session.Id,
            Session.WorkflowType,
            Session.OutputName,
            Session.State,
            Steps.OrderBy(s => s.Ordinal).ToList(),
            hasDerivedRevision,
            latestApproved,
            Session.Dimensions,
            Session.WhiteUnderbaseBranch,
            approvedOutputCount);
    }
}

/// <summary>
/// A flattened row for the "Recent Processing" list (MVP design §10, §13.2).
/// </summary>
/// <remarks>
/// Deliberately the smallest thing an operator needs in order to recognise a session and
/// decide what to do with it. No workspace path, no original source path, no revision and no
/// storage detail: Home lists work, not a database (Epic 11100 Part 3C2 §8).
/// <para>
/// <see cref="CanAbandon"/> and <see cref="CanContinueProcessing"/> are reported here rather
/// than re-derived by a view model, and both delegate to <see cref="SessionStateRules"/> — the
/// same predicates <see cref="Engine.WorkflowEngine"/> guards its own commands with. They
/// decide which entry action Home offers; the command path still decides whether it is
/// accepted.
/// </para>
/// </remarks>
public sealed record SessionListItem(
    SessionId Id,
    WorkflowType WorkflowType,
    OutputName OutputName,
    StepKind CurrentStep,
    SessionState State,
    DateTimeOffset UpdatedAtUtc)
{
    /// <summary>Whether Home may offer Abandon for this session.</summary>
    public bool CanAbandon => SessionStateRules.AllowsAbandon(State);

    /// <summary>
    /// Whether resuming this session means "carry on processing" rather than "look at a
    /// finished record".
    /// </summary>
    public bool CanContinueProcessing => SessionStateRules.AllowsProgress(State);
}

/// <summary>The global automation lock's current holder, if any (MVP design invariant 7).</summary>
public sealed record AutomationLockState(
    SessionId? SessionId, DateTimeOffset? AcquiredAtUtc, int? ProcessId, string? MachineName)
{
    public bool IsHeld => SessionId is not null;
}
