using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;

namespace PrintFlow.Workflow.Services;

/// <summary>
/// One Revision, decoded for display (Epic 11200 Part C1 §5).
/// </summary>
/// <remarks>
/// Carries the Revision's identity and pixels and nothing else — no workspace path, no source
/// path, no hash. The hash is deliberately absent: a preview is visual evidence, and pairing it
/// with a hash would invite a screen to treat "I displayed this" as "I verified this". The
/// artefact metadata a review is actually bound to already reaches the UI through
/// <see cref="ArtefactView"/> (Part C1 §7, §18).
/// </remarks>
public sealed record ImagePreview(
    RevisionId RevisionId,
    int PixelWidth,
    int PixelHeight,
    int SourcePixelWidth,
    int SourcePixelHeight,
    bool HasTransparency,
    ReadOnlyMemory<byte> Payload)
{
    /// <summary>True when the payload is reduced for display rather than every pixel.</summary>
    public bool IsDownsampledForDisplay =>
        PixelWidth < SourcePixelWidth || PixelHeight < SourcePixelHeight;
}

/// <summary>
/// The only route by which image bytes reach the UI (Epic 11200 Part C1 §3, §4).
/// </summary>
/// <remarks>
/// The shape of this interface is the security boundary. It names a <see cref="SessionId"/> and
/// a <see cref="RevisionId"/> — never a path — so there is no <c>ReadAnyFile(string)</c> to
/// misuse, no way to escape the workspace, and no way to reach a file that is not a Revision of
/// the session the caller named. A view model holding both identifiers can still only see what
/// the operator is already working on.
/// <para>
/// Strictly read-only. No implementation may write a file, commit a mutation, or advance a
/// session: a preview that could change workflow state would make "the operator looked at it"
/// an action, which it is not (Part C1 §29).
/// </para>
/// </remarks>
public interface IArtefactPreviewService
{
    /// <summary>
    /// Decodes the Revision <paramref name="revisionId"/> of session <paramref name="sessionId"/>.
    /// </summary>
    /// <remarks>
    /// Fails with <see cref="FailureCode.PreconditionNotMet"/> when the session does not exist
    /// or the Revision does not belong to it, and with the decoder's own failure when the file
    /// is missing or cannot be displayed. Neither outcome changes anything about the session.
    /// </remarks>
    Task<OperationResult<ImagePreview>> GetPreviewAsync(
        SessionId sessionId, RevisionId revisionId, CancellationToken cancellationToken);

    /// <summary>
    /// A small picture of what this session is about, for a Recent Processing row (Jira 11602).
    /// </summary>
    /// <remarks>
    /// The caller names a session and nothing else. Which artefact stands for that session is
    /// decided here, from the Revisions the session actually persisted, so a list cannot invent
    /// an image authority of its own or reach for a file that is not this session's — the same
    /// containment <see cref="GetPreviewAsync"/> gives, with the identity the caller can
    /// reasonably be expected to hold.
    /// <para>
    /// The artefact chosen is the imported original as PrintFlow can draw it: the root Revision
    /// when its container is one this product decodes, and otherwise the managed raster the
    /// PSD/PDF preparation step derived from that root. That is what an operator recognises —
    /// it is the picture they dropped in — and it does not change as the job progresses, so a
    /// row does not silently become a different image between two visits to Home.
    /// </para>
    /// <para>
    /// Strictly read-only, exactly like <see cref="GetPreviewAsync"/>: no Revision, no review, no
    /// workflow state and no file is created or changed by looking at a list. A session with
    /// nothing displayable yet, or whose artefact has gone, fails — and a failure here means
    /// "there is no picture", never "something is wrong with this job".
    /// </para>
    /// </remarks>
    Task<OperationResult<ImagePreview>> GetRecentThumbnailAsync(
        SessionId sessionId, CancellationToken cancellationToken);
}
