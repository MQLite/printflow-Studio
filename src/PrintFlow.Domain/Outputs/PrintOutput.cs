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
    DateTimeOffset? RecycledAtUtc,
    WorkspaceFileRef? PromotionReservation)
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
            RecycledAtUtc: null,
            PromotionReservation: null);

    public PrintOutput Invalidate(InvalidationReason reason) =>
        this with { IsValid = false, InvalidationReason = reason };

    /// <summary>Records that a rejected output was moved to the Recycle Bin (MVP design §10).</summary>
    public PrintOutput Recycled(DateTimeOffset atUtc) =>
        this with { RecycledAtUtc = atUtc };

    /// <summary>
    /// Records the <c>Approved</c> destination atomically reserved for this output, before its
    /// bytes have been written there (Epic 11400 Part C2B §10, §11).
    /// </summary>
    /// <remarks>
    /// This is the half of final approval that survives a crash. Reserving a name and copying the
    /// bytes is file-system work; recording the approval is a database transaction; the two cannot
    /// be one atomic act. Persisting the reservation <i>first</i> is what makes a second attempt at
    /// an interrupted approval resume the promotion already begun rather than reserve a second
    /// name — which is how a <c>_02</c> duplicate of one approved TIFF is prevented (§33).
    /// <para>
    /// Deliberately not <see cref="File"/>: until the bytes are copied and independently re-hashed,
    /// a reservation is an empty claim on a name, and a record that pointed <see cref="File"/> at
    /// it would say this output's <see cref="Sha256"/> bytes live somewhere they do not.
    /// </para>
    /// </remarks>
    public PrintOutput ReservingPromotion(WorkspaceFileRef reservation) =>
        this with { PromotionReservation = reservation };

    /// <summary>
    /// Moves this output's authoritative location to the promoted <c>Approved</c> file
    /// (Epic 11400 Part C2B §6, §9).
    /// </summary>
    /// <remarks>
    /// <see cref="Sha256"/> and <see cref="ByteLength"/> are deliberately untouched: promotion
    /// copies already validated bytes and is never a second export, so an approval that changed
    /// either would be describing a different file (§8). The producing Revision is untouched as
    /// well — it stays the immutable record of what was produced and where, which the database
    /// enforces in <c>Revision_Immutable_Update</c>.
    /// </remarks>
    public PrintOutput Promoted(WorkspaceFileRef approved) =>
        this with { File = approved, PromotionReservation = null };

    /// <summary>Abandons a reservation whose promotion could not be established (§32).</summary>
    public PrintOutput WithoutPromotionReservation() =>
        this with { PromotionReservation = null };
}
