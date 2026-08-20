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
}
