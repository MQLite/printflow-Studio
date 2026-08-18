using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Reviews;
using PrintFlow.Domain.Sessions;
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

    /// <summary>Release the global automation lock (MVP design invariant 7).</summary>
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
}
