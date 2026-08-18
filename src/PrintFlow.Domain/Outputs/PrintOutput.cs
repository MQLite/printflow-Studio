using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Revisions;

namespace PrintFlow.Domain.Outputs;

/// <summary>
/// A production TIFF produced from an approved Revision, target dimensions, and the signed
/// production preset (MVP design §5.5).
/// </summary>
/// <remarks>
/// Modelled now, exercised in Epic 11400. Epic 11100 creates and reasons about the record;
/// it performs no TIFF processing, inspects no CMYK or spot channel, and runs no Photoshop
/// Action. Only the already-confirmed production concepts the architecture needs are
/// present — the type is deliberately thin so Epic 11400 can deepen it without unwinding
/// speculative structure.
///
/// One session may hold several PrintOutputs at different sizes. They are siblings derived
/// from the same approved Revision, not descendants of one another, which is why approving
/// or invalidating one leaves the others alone.
/// </remarks>
public sealed record PrintOutput(
    PrintOutputId Id,
    SessionId SessionId,
    RevisionId SourceRevisionId,
    PrintDimensions Dimensions,
    WhiteUnderbaseBranch Branch,
    ProductionPresetRef Preset,
    WorkspaceFileRef File,
    long ByteLength,
    Sha256 Sha256,
    DateTimeOffset CreatedAtUtc,
    ReviewState ReviewState,
    bool IsValid,
    InvalidationReason? InvalidationReason,
    DateTimeOffset? RecycledAtUtc)
{
    public static PrintOutput Create(
        PrintOutputId id,
        SessionId sessionId,
        RevisionId sourceRevisionId,
        PrintDimensions dimensions,
        WhiteUnderbaseBranch branch,
        ProductionPresetRef preset,
        WorkspaceFileRef file,
        long byteLength,
        Sha256 sha256,
        DateTimeOffset createdAtUtc) =>
        new(id,
            sessionId,
            sourceRevisionId,
            dimensions,
            branch,
            preset,
            file,
            byteLength,
            sha256,
            createdAtUtc,
            ReviewState.NotReviewed,
            IsValid: true,
            InvalidationReason: null,
            RecycledAtUtc: null);

    public PrintOutput Invalidate(InvalidationReason reason) =>
        this with { IsValid = false, InvalidationReason = reason };

    /// <summary>Records that a rejected output was moved to the Recycle Bin (MVP design §10).</summary>
    public PrintOutput Recycled(DateTimeOffset atUtc) =>
        this with { RecycledAtUtc = atUtc };
}
