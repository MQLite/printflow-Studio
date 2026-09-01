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
    /// <param name="configuredRecommendations">
    /// The named-size recommendations this installation's verified preset configures right now, so
    /// a restored preset fit can be checked against the authority currently in force. Null means
    /// this caller could not ask, and a pending preset fit then fails closed
    /// (post-final A5 correction §13, §14).
    /// </param>
    public WorkflowSnapshot ToSnapshot(
        PresetPrintRecommendationSet? configuredRecommendations = null)
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
            approvedOutputCount)
        {
            // Carried across like the print size and the W1 branch, and for the same reason:
            // it is a decision the operator made that no Revision records, so a reload that
            // dropped it would silently re-trim at zero margin (Epic 11200 Part C3 §13).
            TrimMargin = Session.TrimMargin,

            // Carried across for the same reason, and with the same caveat: what is restored is
            // the *pending* authority, not permission to run. Whether it still authorises
            // anything is decided by WorkflowSnapshot.UsableBackgroundRemovalAuthority against
            // the upstream result these very rows describe, so a decision that survived a
            // restart is usable only if the content it named survived with it
            // (Epic 11300 Part C2B1 §9, §20).
            BackgroundRemovalAuthority = Session.BackgroundRemovalAuthority,

            // Carried across for the same reason and with the same caveat: what is restored is
            // the *pending* plan and what the recorded millimetres mean, not permission to run.
            // Whether the plan still applies is decided by
            // WorkflowSnapshot.UsablePrintPreparationPlan against the upstream result these very
            // rows describe, so a plan that survived a restart is usable only if the content it
            // was calculated from survived with it. A resumed legacy session restores its
            // historical pair and its LegacyExactPair reading, and is refused at StartStep
            // (Epic 11400 Part B1A.2A §7, §10).
            DimensionSemantics = Session.DimensionSemantics,
            PrintPreparationPlan = Session.PrintPreparationPlan,

            // The flexible-size decision restores on exactly the same terms, and its enlargement
            // authority most of all. A confirmation that survived a restart is usable only if the
            // exact source, edge, request and projection it names survived with it, which
            // WorkflowSnapshot.UsablePhotoshopPreparation decides against these very rows — so
            // reopening the app is not a way to acquire permission (Part B1A.2D §13, §30).
            SizeSelection = Session.SizeSelection,
            TargetEdgePlan = Session.TargetEdgePlan,
            EnlargementAuthority = Session.EnlargementAuthority,

            // Not restored from the session — it is not the session's to state. What the shop
            // currently recommends for a named size is the verified preset's answer, and carrying
            // it here is what lets WorkflowSnapshot.UsablePrintPreparationPlan notice that a
            // pending A5 plan was made under a recommendation this installation has since replaced
            // (post-final A5 correction §13, §14).
            ConfiguredRecommendations = configuredRecommendations,
        };
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
