using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Reviews;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Trimming;
using PrintFlow.Workflow.Definitions;

namespace PrintFlow.Workflow.Effects;

/// <summary>
/// Work the engine has decided must happen, expressed as data.
/// </summary>
/// <remarks>
/// The engine returns effects; it never performs them. That is what keeps it pure, and what
/// keeps transaction ordering and file sequencing in one place — the application service
/// that interprets them (Epic 11100 plan §8.1, §10.5).
///
/// Several effects describe work whose implementation belongs to a later Epic. They are
/// modelled now so the reducer output is already complete and the later Epic adds an
/// interpreter rather than a new workflow rule.
/// </remarks>
public abstract record WorkflowEffect
{
    private protected WorkflowEffect()
    {
    }

    /// <summary>A short stable name used in logs and assertions.</summary>
    public string Kind => GetType().Name;

    /// <summary>Create a fresh working copy of a Revision for an external application to edit.</summary>
    /// <remarks>Every retry starts from a clean copy (MVP design invariant 8). Task 11106 implements it.</remarks>
    public sealed record CreateWorkingCopy(
        StepKind Step,
        RevisionId SourceRevision,
        WorkspaceArea TargetArea) : WorkflowEffect;

    /// <summary>Invoke an adapter for a step. Epic 11100 has fake adapters only.</summary>
    public sealed record RunAdapter(
        AttemptId AttemptId,
        StepKind Step,
        AdapterKind Adapter,
        OperationKind Operation,
        RevisionId? InputRevision) : WorkflowEffect;

    /// <summary>
    /// Crop a Revision to a rectangle the operator drew (Epic 11200 Part C2 §12).
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="RunAdapter"/> because it carries the one thing an adapter call
    /// never does: geometry a human chose. Modelling it as data on the effect keeps the crop
    /// rectangle on the same audited path as every other input to a producing attempt, rather
    /// than being smuggled to the processor around the reducer.
    /// </remarks>
    public sealed record ImportManualResult(
        AttemptId AttemptId, StepKind Step, RevisionId InputRevision, string SelectedPath) : WorkflowEffect;

    public sealed record RunManualCrop(
        AttemptId AttemptId,
        StepKind Step,
        RevisionId InputRevision,
        TrimBounds Crop, ManualCropMargin Margin = default) : WorkflowEffect;

    /// <summary>Record the start of an attempt before any work begins, so a crash is detectable.</summary>
    public sealed record RecordAttemptStarted(
        AttemptId AttemptId,
        StepKind Step,
        OperationKind Operation,
        RevisionId? InputRevision,
        int RetrySequence) : WorkflowEffect;

    /// <summary>Persist a validated Revision produced by a successful attempt.</summary>
    public sealed record PersistRevision(
        RevisionId RevisionId,
        StepKind Step,
        AttemptId AttemptId) : WorkflowEffect;

    /// <summary>Append a review decision, bound to the hash that was reviewed.</summary>
    public sealed record RecordReview(
        ReviewId ReviewId,
        StepKind Step,
        ReviewSubjectKind SubjectKind,
        Guid SubjectId,
        Sha256 ReviewedHash,
        bool IsApproved,
        RejectionReason? QuickReason,
        string? Notes) : WorkflowEffect;

    /// <summary>Record that a step was skipped, with its reason. Creates no Revision.</summary>
    public sealed record RecordSkip(StepKind Step, string Reason) : WorkflowEffect;

    /// <summary>
    /// Invalidate everything derived from a Revision.
    /// </summary>
    /// <remarks>
    /// The engine states which subtree must be invalidated; the recursive descendant walk
    /// and the file moves belong to Tasks 11105 and 11108. Siblings are never touched.
    /// </remarks>
    public sealed record InvalidateDescendants(
        RevisionId FromRevision,
        InvalidationReason Reason) : WorkflowEffect;

    /// <summary>Reset a step and everything after it to Waiting after returning upstream.</summary>
    public sealed record ResetStepsFrom(StepKind FromStep) : WorkflowEffect;

    /// <summary>Persist the confirmed print dimensions.</summary>
    public sealed record PersistPrintDimensions(PrintDimensions Dimensions) : WorkflowEffect;

    /// <summary>
    /// Persist the trim margin the next deterministic Trim attempt will run with
    /// (Epic 11200 Part C3 §13, §14).
    /// </summary>
    /// <remarks>
    /// The session-level half of the parameter record: it is what "Run Trim" reads, so the
    /// value that runs is one that survived a restart rather than one a screen was holding.
    /// The audit half is separate and lives on <c>ProcessingAttempt.TrimParameters</c>, written
    /// when the attempt starts — that is the row that must never be rewritten when the operator
    /// changes their mind and re-runs (§15).
    /// </remarks>
    public sealed record PersistTrimParameters(TrimMargin Margin) : WorkflowEffect;

    /// <summary>
    /// Persist the reviewed-content authority the next Background Removal attempt may run under
    /// (Epic 11300 Part C2B1 §5, §10).
    /// </summary>
    /// <remarks>
    /// The session-level half of the authority record: it is what "Run Background Removal"
    /// reads, so a decision an operator made before closing the app is still there when they
    /// reopen it (§20). The audit half is separate and lives on
    /// <c>ProcessingAttempt.BackgroundRemovalAuthority</c>, written when the attempt starts —
    /// that is the row that must never be rewritten when the operator later authorises
    /// different content (§11, §18).
    /// </remarks>
    public sealed record PersistBackgroundRemovalDecision(
        BackgroundRemovalAuthority Authority) : WorkflowEffect;

    /// <summary>
    /// Persist the operator's explicit permission to enlarge one exact target
    /// (Epic 11400 Part B1A.2D §9, §10).
    /// </summary>
    /// <remarks>
    /// The session-level half of the authority, so a confirmation given before the app closed is
    /// still there when it reopens. The audit half is separate and lives on the attempt's
    /// <c>Preparation</c>, written when the run starts — that is the row a later change of mind
    /// must never rewrite (§24).
    /// <para>
    /// There is deliberately no matching "clear the authority" effect. An authority stops
    /// applying because the exact source, edge, request or projection it names is no longer the
    /// one on offer, which is a comparison rather than a write (§30).
    /// </para>
    /// </remarks>
    public sealed record PersistEnlargementAuthority(
        EnlargementAuthority Authority) : WorkflowEffect;

    /// <summary>Persist the explicit white-underbase decision and its justification.</summary>
    public sealed record PersistWhiteUnderbaseBranch(
        WhiteUnderbaseBranch Branch,
        string Justification) : WorkflowEffect;

    /// <summary>Persist a changed output name.</summary>
    public sealed record PersistOutputName(OutputName Name) : WorkflowEffect;

    /// <summary>Persist a changed workflow selection.</summary>
    public sealed record PersistWorkflowSelection(WorkflowType Type) : WorkflowEffect;

    /// <summary>Open the working copy for the operator and end automated progression.</summary>
    public sealed record OpenForManualWork(StepKind Step, string Reason) : WorkflowEffect;

    /// <summary>Release this business database's automation correlation/recovery row.</summary>
    public sealed record ReleaseAutomationLock : WorkflowEffect;

    /// <summary>Remove safe-to-delete working copies once a session concludes (MVP design §10).</summary>
    public sealed record CleanupWorking : WorkflowEffect;

    /// <summary>Mark the session finished at the given instant.</summary>
    public sealed record MarkSessionCompleted(DateTimeOffset AtUtc) : WorkflowEffect;

    /// <summary>Mark the session handed off to the operator.</summary>
    public sealed record MarkSessionHandedOff(DateTimeOffset AtUtc, string Reason) : WorkflowEffect;

    /// <summary>Mark the session abandoned. Files are retained.</summary>
    public sealed record MarkSessionAbandoned(DateTimeOffset AtUtc, string Reason) : WorkflowEffect;

    /// <summary>Reopen a completed session at PrintDimensions to produce another size.</summary>
    public sealed record BeginAdditionalOutput(RevisionId SourceRevision) : WorkflowEffect;

    /// <summary>Record a failed attempt and its structured failure.</summary>
    public sealed record RecordAttemptFailure(
        AttemptId AttemptId,
        StepKind Step,
        OperationFailure Failure) : WorkflowEffect;

    /// <summary>Record that an attempt was interrupted by a crash or shutdown.</summary>
    public sealed record RecordAttemptInterrupted(AttemptId AttemptId, StepKind Step) : WorkflowEffect;

    /// <summary>
    /// Record that a human stopped a running attempt, and what it left behind
    /// (Epic 11300 Part D2A §12, §29).
    /// </summary>
    /// <remarks>
    /// Carries the <see cref="OperationFailure"/> rather than a bare "stopped" flag because the
    /// audit §29 asks for is entirely in its structured context: which mode was requested,
    /// whether a signed cancel was positively invoked, and whether the external application may
    /// still be running. A flag would record that a stop happened and lose every question an
    /// operator actually asks afterwards.
    /// </remarks>
    public sealed record RecordAttemptCancelled(
        AttemptId AttemptId,
        StepKind Step,
        OperationFailure Failure) : WorkflowEffect;

    /// <summary>
    /// Return a handed-off session to automation at the operator's explicit request
    /// (Epic 11300 Part D2A §22).
    /// </summary>
    /// <remarks>
    /// Clears the handoff record on the session rather than leaving it standing, because the
    /// session-level fields answer "is this session currently handed off" and the answer has
    /// changed. The <i>history</i> of the takeover is not stored there and is not lost: it is
    /// the closed, never-rewritten attempt row the takeover produced, whose status is
    /// <c>Cancelled</c> and whose structured context says a takeover was what stopped it
    /// (§15, §29).
    /// </remarks>
    public sealed record MarkSessionReenteredAutomation(DateTimeOffset AtUtc) : WorkflowEffect;
}
