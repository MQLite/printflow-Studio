using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;

namespace PrintFlow.Domain.Attempts;

/// <summary>The outcome of one attempt.</summary>
public enum AttemptStatus
{
    /// <summary>The attempt has started. A row in this state at startup means the app crashed.</summary>
    Running,

    Succeeded,
    Failed,
    Interrupted,
    Cancelled,
}

/// <summary>
/// One automated processing or manual-handoff attempt (MVP design §5.4).
/// </summary>
/// <remarks>
/// Attempts and Revisions are separate concepts, and that separation is what makes a crash
/// detectable: the row is written before the work starts. <see cref="OutputRevisionId"/> is
/// non-null only when the attempt succeeded — design invariant 4 made structural, and
/// enforced again by a database CHECK in Task 11108.
///
/// Retries chain through <see cref="RetryOfAttemptId"/> so a step's failure history stays
/// queryable rather than being overwritten.
/// </remarks>
public sealed record ProcessingAttempt(
    AttemptId Id,
    SessionId SessionId,
    StepKind Step,
    RevisionId? InputRevisionId,
    OperationKind Operation,
    string AdapterId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? EndedAtUtc,
    AttemptStatus Status,
    RevisionId? OutputRevisionId,
    OperationFailure? Failure,
    AttemptId? RetryOfAttemptId,
    int RetrySequence)
{
    /// <summary>Starts an attempt. The output Revision, if any, is attached on success.</summary>
    public static ProcessingAttempt Start(
        AttemptId id,
        SessionId sessionId,
        StepKind step,
        RevisionId? inputRevisionId,
        OperationKind operation,
        string adapterId,
        DateTimeOffset startedAtUtc,
        AttemptId? retryOfAttemptId = null,
        int retrySequence = 0) =>
        new(id,
            sessionId,
            step,
            inputRevisionId,
            operation,
            adapterId,
            startedAtUtc,
            EndedAtUtc: null,
            AttemptStatus.Running,
            OutputRevisionId: null,
            Failure: null,
            retryOfAttemptId,
            retrySequence);

    public ProcessingAttempt Succeed(RevisionId outputRevisionId, DateTimeOffset endedAtUtc) =>
        this with
        {
            Status = AttemptStatus.Succeeded,
            OutputRevisionId = outputRevisionId,
            EndedAtUtc = endedAtUtc,
        };

    public ProcessingAttempt Fail(OperationFailure failure, DateTimeOffset endedAtUtc) =>
        this with { Status = AttemptStatus.Failed, Failure = failure, EndedAtUtc = endedAtUtc };

    public ProcessingAttempt Interrupt(DateTimeOffset endedAtUtc) =>
        this with { Status = AttemptStatus.Interrupted, EndedAtUtc = endedAtUtc };

    /// <summary>True when this attempt legitimately carries an output Revision.</summary>
    public bool ProducedRevision => Status == AttemptStatus.Succeeded && OutputRevisionId is not null;
}
