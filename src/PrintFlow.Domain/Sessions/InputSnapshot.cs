using PrintFlow.Domain.Ids;

namespace PrintFlow.Domain.Sessions;

/// <summary>
/// Provenance for the operator's imported file (MVP design §5.2).
/// </summary>
/// <remarks>
/// This is not a second file model. The snapshot's bytes live in the root
/// <c>Revision</c> — the one whose operation is <c>Import</c> and whose source is null — so
/// that every managed file in the system, without exception, has a hash, a place in the
/// derivation tree, and a validity state. This record adds only the facts about where the
/// file came from.
///
/// <see cref="OriginalSourcePath"/> records the external path. An operator can explicitly
/// replace the original during final delivery without changing the managed import bytes.
/// </remarks>
public sealed record InputSnapshot(
    SnapshotId Id,
    SessionId SessionId,
    RevisionId RootRevisionId,
    string OriginalSourcePath,
    string OriginalFileName,
    DateTimeOffset ImportedAtUtc);
