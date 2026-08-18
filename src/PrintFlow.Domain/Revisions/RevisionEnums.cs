namespace PrintFlow.Domain.Revisions;

/// <summary>The operation that produced a managed file.</summary>
public enum OperationKind
{
    /// <summary>The imported original, snapshotted by PrintFlow.</summary>
    Import,

    /// <summary>Meitu enhancement.</summary>
    Enhance,

    /// <summary>Meitu background removal.</summary>
    RemoveBackground,

    /// <summary>Deterministic canvas trimming (algorithm: Epic 11200).</summary>
    Trim,

    /// <summary>Promotion of an already-approved file into the Approved area; bytes unchanged.</summary>
    PromoteApproved,

    /// <summary>A file the operator produced manually and dragged back in.</summary>
    ManualImport,

    /// <summary>Photoshop production output (implementation: Epic 11400).</summary>
    PhotoshopOutput,
}

/// <summary>Why a Revision or PrintOutput stopped being valid.</summary>
public enum InvalidationReason
{
    /// <summary>A newer result replaced it at the same step.</summary>
    Superseded,

    /// <summary>An ancestor changed, so this derived result no longer reflects it.</summary>
    UpstreamChanged,

    /// <summary>The bytes on disk no longer match the recorded SHA-256.</summary>
    FileMutated,

    /// <summary>A reviewer rejected it.</summary>
    Rejected,

    /// <summary>The session was returned to an earlier step or reset.</summary>
    SessionReset,
}

/// <summary>
/// Cached review projection for list queries.
/// </summary>
/// <remarks>
/// Never the authority. Approval is the derived predicate in Epic 11100 plan §10.3:
/// the Revision is valid, its latest decision is Approved, and the reviewed hash still
/// matches the bytes on disk. This field only avoids a join when rendering a list.
/// </remarks>
public enum ReviewState
{
    NotReviewed,
    Approved,
    Rejected,
}
