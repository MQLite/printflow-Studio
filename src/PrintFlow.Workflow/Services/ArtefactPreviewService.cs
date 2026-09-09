using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Workflow.Services;

/// <summary>
/// Resolves a Revision inside its own session, then hands the file to the decoder
/// (Epic 11200 Part C1 §4).
/// </summary>
/// <remarks>
/// Two steps, and the first is the whole point: the Revision is looked up in the aggregate the
/// repository returns for <i>this</i> session, so a Revision belonging to another session is
/// simply not in the list and is refused for the same reason a made-up id would be. There is no
/// separate "does it belong here" check to forget, and no code path that could be given a path
/// instead.
/// <para>
/// The lookup is a fresh read rather than a cached one, so resuming a session after a restart
/// previews the Revision that is actually persisted rather than an image object left over from
/// an earlier visit (Part C1 §19). No preview bytes are retained here: each call decodes and
/// returns, and the payload is reclaimable as soon as the screen lets go of it (§6).
/// </para>
/// </remarks>
public sealed class ArtefactPreviewService : IArtefactPreviewService
{
    private readonly ISessionRepository _repository;
    private readonly IImagePreviewDecoder _decoder;

    public ArtefactPreviewService(ISessionRepository repository, IImagePreviewDecoder decoder)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(decoder);

        _repository = repository;
        _decoder = decoder;
    }

    /// <inheritdoc />
    public async Task<OperationResult<ImagePreview>> GetPreviewAsync(
        SessionId sessionId, RevisionId revisionId, CancellationToken cancellationToken)
    {
        OperationResult<SessionAggregate?> loaded = await _repository.LoadAsync(sessionId, cancellationToken);
        if (loaded.IsFailure)
        {
            return OperationResult.Fail<ImagePreview>(loaded.Failure);
        }

        if (loaded.Value is not { } aggregate)
        {
            return OperationResult.Fail<ImagePreview>(
                FailureCode.PreconditionNotMet, $"No session {sessionId} exists.");
        }

        // Membership is the lookup, not a check performed alongside it: the aggregate holds
        // this session's Revisions and no others, so "belongs to another session" and "does
        // not exist" are indistinguishable from here — which is exactly what the caller should
        // be told (§4).
        Revision? revision = null;
        foreach (Revision candidate in aggregate.Revisions)
        {
            if (candidate.Id == revisionId)
            {
                revision = candidate;
                break;
            }
        }

        if (revision is null)
        {
            return OperationResult.Fail<ImagePreview>(
                FailureCode.PreconditionNotMet,
                $"Revision {revisionId} is not a Revision of session {sessionId}.");
        }

        if (revision.RetentionReleasedAtUtc is not null)
            return OperationResult.Fail<ImagePreview>(FailureCode.PreconditionNotMet,
                "This rejected Meitu result's comparison retention ended when the session completed.");

        if (revision.Facts.Format is PrintFlow.Domain.Files.ImageFormat.Psd or PrintFlow.Domain.Files.ImageFormat.Pdf)
        {
            return OperationResult.Fail<ImagePreview>(revision.Facts.Format == PrintFlow.Domain.Files.ImageFormat.Pdf ? FailureCode.PdfPreparationFailed : FailureCode.PsdPreparationFailed,
                "Review the prepared managed raster; the original source document is not a UI preview.");
        }
        OperationResult<DecodedPreview> decoded = await _decoder.DecodeAsync(revision.File, cancellationToken);
        return Present(revision, decoded);
    }

    /// <inheritdoc />
    public async Task<OperationResult<ImagePreview>> GetRecentThumbnailAsync(
        SessionId sessionId, CancellationToken cancellationToken)
    {
        OperationResult<SessionAggregate?> loaded = await _repository.LoadAsync(sessionId, cancellationToken);
        if (loaded.IsFailure)
        {
            return OperationResult.Fail<ImagePreview>(loaded.Failure);
        }

        if (loaded.Value is not { } aggregate)
        {
            return OperationResult.Fail<ImagePreview>(
                FailureCode.PreconditionNotMet, $"No session {sessionId} exists.");
        }

        if (ThumbnailArtefactOf(aggregate) is not { } revision)
        {
            return OperationResult.Fail<ImagePreview>(
                FailureCode.PreconditionNotMet,
                $"Session {sessionId} holds no artefact this workstation can draw as a thumbnail.");
        }

        OperationResult<DecodedPreview> decoded =
            await _decoder.DecodeThumbnailAsync(revision.File, cancellationToken);
        return Present(revision, decoded);
    }

    /// <summary>
    /// Which of a session's Revisions stands for it in a list (Jira 11602).
    /// </summary>
    /// <remarks>
    /// The imported original, as this product can draw it. The root Revision is the operator's
    /// own file and the one fact about a session that never changes, which is exactly what a
    /// recognisable list row needs; when the root is a PSD or a single-page PDF — containers the
    /// review surface deliberately refuses and whose pixels only exist once a preparation step
    /// has produced a managed raster — the raster derived directly from that root stands in for
    /// it. Nothing else is considered: a later enhanced or trimmed result would make the same
    /// job look like a different one from one visit to the next.
    /// <para>
    /// This chooses; it does not decode, does not create anything, and cannot fall back to a
    /// file outside the aggregate it was handed. A session with no root Revision yet — an import
    /// that failed — and one whose derived raster has not been produced both answer null, and
    /// the row shows no picture rather than a wrong one.
    /// </para>
    /// </remarks>
    private static Revision? ThumbnailArtefactOf(SessionAggregate aggregate)
    {
        Revision? root = aggregate.Revisions.FirstOrDefault(candidate => candidate.IsRoot);
        if (root is null)
        {
            return null;
        }

        if (IsDrawable(root))
        {
            return root;
        }

        return aggregate.Revisions
            .Where(candidate => candidate.SourceRevisionId == root.Id && IsDrawable(candidate))
            .OrderBy(candidate => candidate.CreatedAtUtc)
            .FirstOrDefault();

        static bool IsDrawable(Revision revision) =>
            revision.RetentionReleasedAtUtc is null &&
            revision.Facts.Format is not (PrintFlow.Domain.Files.ImageFormat.Psd
                or PrintFlow.Domain.Files.ImageFormat.Pdf);
    }

    /// <summary>Wraps a decoded payload in the Revision identity it came from, or reports why not.</summary>
    private static OperationResult<ImagePreview> Present(
        Revision revision, OperationResult<DecodedPreview> decoded) =>
        decoded.IsFailure
            ? OperationResult.Fail<ImagePreview>(decoded.Failure)
            : OperationResult.Ok(new ImagePreview(
                revision.Id,
                decoded.Value.PixelWidth,
                decoded.Value.PixelHeight,
                decoded.Value.SourcePixelWidth,
                decoded.Value.SourcePixelHeight,
                decoded.Value.HasTransparency,
                decoded.Value.Payload));
}
