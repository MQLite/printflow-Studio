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
/// Identity, bytes and pixel facts are immutable. Completion retention may switch a Working
/// location to a verified durable copy, preserving the former location as provenance, or
/// explicitly release rejected Meitu comparison bytes. Both transitions are guarded in SQLite.
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
    /// <summary>
    /// Historical producing location after verified retention promotion. This is provenance,
    /// not a second claim that bytes still exist there; cleanup may remove the redundant copy.
    /// </summary>
    public WorkspaceFileRef? FormerWorkingFile { get; init; }

    /// <summary>
    /// End of the rejected Meitu comparison retention period. File then names the historical
    /// location, whose bytes may be absent. This state never authorises consumption or approval.
    /// Written before deletion so a crash leaves recoverable excess bytes, not lost authority.
    /// </summary>
    public DateTimeOffset? RetentionReleasedAtUtc { get; init; }

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
