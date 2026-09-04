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

        if (revision.Facts.Format is PrintFlow.Domain.Files.ImageFormat.Psd or PrintFlow.Domain.Files.ImageFormat.Pdf)
        {
            return OperationResult.Fail<ImagePreview>(revision.Facts.Format == PrintFlow.Domain.Files.ImageFormat.Pdf ? FailureCode.PdfPreparationFailed : FailureCode.PsdPreparationFailed,
                "Review the prepared managed raster; the original source document is not a UI preview.");
        }
        OperationResult<DecodedPreview> decoded = await _decoder.DecodeAsync(revision.File, cancellationToken);
        return decoded.IsFailure
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
}
