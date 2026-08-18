using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Sessions;

namespace PrintFlow.Domain.Reviews;

/// <summary>What a review decision was made about.</summary>
public enum ReviewSubjectKind
{
    Revision,
    PrintOutput,
}

/// <summary>Quick rejection reasons offered by the review UI (MVP design §7.3).</summary>
public enum RejectionReason
{
    InsufficientResult,
    EdgeError,
    MissingContent,
    ColourIssue,
    DimensionIssue,
    WhiteInkIssue,
    Other,
}

/// <summary>
/// One human decision, bound to the exact bytes that were reviewed (MVP design §7.3).
/// </summary>
/// <remarks>
/// Append-only: decisions are never edited or deleted, so the audit trail survives even
/// when a later change invalidates the artefact. <see cref="ReviewedSha256"/> is what makes
/// design invariant 3 real — a changed file cannot inherit an earlier approval, because the
/// approval predicate re-compares the hash against the bytes on disk.
///
/// <see cref="Operator"/> is the Windows username, falling back to <c>Operator</c>
/// (design §4.1). There is no authentication in the MVP.
/// </remarks>
public sealed record ReviewDecision(
    ReviewId Id,
    SessionId SessionId,
    StepKind Step,
    ReviewSubjectKind SubjectKind,
    Guid SubjectId,
    Sha256 ReviewedSha256,
    string Operator,
    DateTimeOffset DecidedAtUtc,
    bool IsApproved,
    RejectionReason? QuickReason,
    string? Notes)
{
    public static ReviewDecision Approve(
        ReviewId id,
        SessionId sessionId,
        StepKind step,
        ReviewSubjectKind subjectKind,
        Guid subjectId,
        Sha256 reviewedSha256,
        string @operator,
        DateTimeOffset decidedAtUtc,
        string? notes = null) =>
        new(id, sessionId, step, subjectKind, subjectId, reviewedSha256, @operator, decidedAtUtc,
            IsApproved: true, QuickReason: null, notes);

    public static ReviewDecision Reject(
        ReviewId id,
        SessionId sessionId,
        StepKind step,
        ReviewSubjectKind subjectKind,
        Guid subjectId,
        Sha256 reviewedSha256,
        string @operator,
        DateTimeOffset decidedAtUtc,
        RejectionReason quickReason,
        string? notes = null) =>
        new(id, sessionId, step, subjectKind, subjectId, reviewedSha256, @operator, decidedAtUtc,
            IsApproved: false, quickReason, notes);
}
