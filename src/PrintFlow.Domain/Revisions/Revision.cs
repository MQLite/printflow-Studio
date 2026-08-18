using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;

namespace PrintFlow.Domain.Revisions;

/// <summary>
/// A file that was successfully produced, confirmed readable, and hashed (MVP design §5.3).
/// </summary>
/// <remarks>
/// A Revision exists only after every validation stage passed; a failed attempt never
/// creates one (design invariants 4 and 5). Revisions form a tree through
/// <see cref="SourceRevisionId"/>, which is what makes downstream invalidation a
/// well-defined descendant walk rather than a heuristic.
///
/// Once written, only validity, invalidation and the cached review state may change.
/// Everything else — including the hash — is immutable. Epic 11100 Task 11108 enforces that
/// in the database as well as here; disk hashing itself belongs to Task 11106.
/// </remarks>
public sealed record Revision(
    RevisionId Id,
    SessionId SessionId,
    RevisionId? SourceRevisionId,
    OperationKind Operation,
    WorkspaceFileRef File,
    FileFacts Facts,
    DateTimeOffset CreatedAtUtc,
    bool IsValid,
    DateTimeOffset? InvalidatedAtUtc,
    InvalidationReason? InvalidationReason,
    ReviewState ReviewState)
{
    /// <summary>The root of a session's derivation tree has no source.</summary>
    public bool IsRoot => SourceRevisionId is null;

    /// <summary>Convenience accessor; the hash is the identity every approval binds to.</summary>
    public Sha256 Sha256 => Facts.Sha256;

    /// <summary>Creates a freshly validated Revision.</summary>
    public static Revision Create(
        RevisionId id,
        SessionId sessionId,
        RevisionId? sourceRevisionId,
        OperationKind operation,
        WorkspaceFileRef file,
        FileFacts facts,
        DateTimeOffset createdAtUtc) =>
        new(id,
            sessionId,
            sourceRevisionId,
            operation,
            file,
            facts,
            createdAtUtc,
            IsValid: true,
            InvalidatedAtUtc: null,
            InvalidationReason: null,
            ReviewState.NotReviewed);

    public Revision Invalidate(InvalidationReason reason, DateTimeOffset atUtc) =>
        this with { IsValid = false, InvalidatedAtUtc = atUtc, InvalidationReason = reason };
}
