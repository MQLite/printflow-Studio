using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Workflow.Services;

/// <summary>
/// Resolves one production output inside its own session, then hands the file to the TIFF review
/// decoder (SCRUM-11104 §16, §43, §45).
/// </summary>
/// <remarks>
/// Three steps, and the order is the contract. The Revision is looked up in the aggregate the
/// repository returns for <i>this</i> session, so a Revision belonging to another session is
/// simply not in the list and is refused for the same reason a made-up id would be. Then the
/// <c>PrintOutput</c> that describes it is found — and its absence is a refusal, because a TIFF
/// with no output row is a TIFF that never passed production validation (§45). Only then are any
/// bytes read, and they are read against the output's own hash.
/// <para>
/// The lookup is a fresh read rather than a cached one, so resuming a session after a restart
/// reviews the output that is actually persisted rather than an object left over from an earlier
/// visit (§39). No payload is retained here: each call decodes and returns, and the bytes are
/// reclaimable as soon as the screen lets go of them.
/// </para>
/// </remarks>
public sealed class ProductionTiffReviewService : IProductionTiffReviewService
{
    private const double MillimetresPerInch = 25.4;

    private readonly ISessionRepository _repository;
    private readonly ITiffReviewDecoder _decoder;
    private readonly IWorkspace _workspace;

    public ProductionTiffReviewService(
        ISessionRepository repository, ITiffReviewDecoder decoder, IWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(decoder);
        ArgumentNullException.ThrowIfNull(workspace);

        _repository = repository;
        _decoder = decoder;
        _workspace = workspace;
    }

    /// <inheritdoc />
    public async Task<OperationResult<TiffReviewPayload>> GetReviewAsync(
        SessionId sessionId, RevisionId revisionId, CancellationToken cancellationToken)
    {
        OperationResult<SessionAggregate?> loaded = await _repository.LoadAsync(sessionId, cancellationToken);
        if (loaded.IsFailure)
        {
            return OperationResult.Fail<TiffReviewPayload>(loaded.Failure);
        }

        if (loaded.Value is not { } aggregate)
        {
            return OperationResult.Fail<TiffReviewPayload>(
                FailureCode.PreconditionNotMet, $"No session {sessionId} exists.");
        }

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
            return OperationResult.Fail<TiffReviewPayload>(
                FailureCode.PreconditionNotMet,
                $"Revision {revisionId} is not a Revision of session {sessionId}.");
        }

        // The accepted join: a PrintOutput carries the same underlying GUID as the Revision row
        // describing the same physical TIFF (SessionService.BuildPrintOutput). Asking for the
        // output rather than trusting the Revision's extension is what makes "validated
        // production TIFF" the precondition instead of "a file whose name ends in .tif" (§45).
        PrintOutputId outputId = PrintOutputId.From(revision.Id.Value);
        PrintOutput? output = null;
        foreach (PrintOutput candidate in aggregate.Outputs)
        {
            if (candidate.Id == outputId)
            {
                output = candidate;
                break;
            }
        }

        if (output is null)
        {
            return OperationResult.Fail<TiffReviewPayload>(
                FailureCode.PreconditionNotMet,
                $"Revision {revisionId} is not a validated production output of session {sessionId}.");
        }

        if (output.RecycledAtUtc is not null)
        {
            return OperationResult.Fail<TiffReviewPayload>(
                FailureCode.OutputMissing,
                "The rejected production TIFF was moved to the Recycle Bin and is no longer available.");
        }

        // The hash the decoder is told to expect is the output's own, never the Revision's and
        // never the file's. It is the value an approval binds to, so it is the value the pixels
        // an operator looks at have to come from (§16, §17).
        OperationResult<DecodedTiffReview> decoded = await _decoder
            .DecodeAsync(output.File, output.Sha256, cancellationToken);

        if (decoded.IsFailure)
        {
            return OperationResult.Fail<TiffReviewPayload>(decoded.Failure);
        }

        PhotoshopPreparation? preparation = PreparationFor(aggregate, revision.Id);
        (double widthMm, double heightMm) = PhysicalMillimetres(preparation, output, decoded.Value);

        return OperationResult.Ok(new TiffReviewPayload(
            output.Id,
            revision.Id,
            output.Sha256,
            output.File.FileName,
            _workspace.ResolveAbsolute(output.File),
            output.File.Area,
            output.ByteLength,
            decoded.Value.PixelWidth,
            decoded.Value.PixelHeight,
            decoded.Value.PreviewPixelWidth,
            decoded.Value.PreviewPixelHeight,
            decoded.Value.XResolutionDpi,
            decoded.Value.YResolutionDpi,
            decoded.Value.ColourMode,
            decoded.Value.BitsPerSample,
            decoded.Value.InkChannelCount,
            decoded.Value.WhiteInkChannelName,
            decoded.Value.WhiteInkSampleCount,
            output.Branch,
            output.Preset,
            decoded.Value.ColourConversion,
            decoded.Value.WhiteInkPolarity,
            widthMm,
            heightMm,
            EffectiveResolution(preparation, widthMm, heightMm),
            decoded.Value.ColourPayload,
            decoded.Value.WhiteInkPayload,
            decoded.Value.OverlayPayload));
    }

    /// <summary>
    /// The preparation the attempt that produced this exact TIFF actually ran under (§20).
    /// </summary>
    /// <remarks>
    /// Found through <see cref="ProcessingAttempt.OutputRevisionId"/> — the attempt that says it
    /// produced this Revision — exactly as <c>SessionView</c> finds trim parameters and the
    /// background-removal decision. The session's <i>pending</i> plan is deliberately not
    /// consulted: it answers "what would the next run be allowed to do", and after a reject, a
    /// retry at different limits or an Add Another Size it describes a different output from the
    /// one on screen.
    /// </remarks>
    private static PhotoshopPreparation? PreparationFor(SessionAggregate aggregate, RevisionId revisionId)
    {
        foreach (ProcessingAttempt attempt in aggregate.Attempts)
        {
            if (attempt.OutputRevisionId == revisionId && attempt.Preparation is { } preparation)
            {
                return preparation;
            }
        }

        return null;
    }

    /// <summary>
    /// The physical size this output was prepared to be (§26).
    /// </summary>
    /// <remarks>
    /// From the preparation's projected pixels at the fixed production resolution, because that
    /// is the authoritative record of what was asked for. <c>PrintOutput.Dimensions</c> is
    /// deliberately not used for a maximum-bound run: there the pair is a <i>fit box</i>, and
    /// 200 × 150 mm bounds routinely produce a 200 × 143 mm output — reporting the box as the
    /// size would overstate one edge on almost every job.
    /// <para>
    /// The fallback, for a row whose attempt recorded no preparation, is the TIFF's own pixels at
    /// its own resolution. That is a measurement of the file rather than a record of the request,
    /// which is why it is the fallback and not the rule.
    /// </para>
    /// </remarks>
    private static (double WidthMm, double HeightMm) PhysicalMillimetres(
        PhotoshopPreparation? preparation, PrintOutput output, DecodedTiffReview decoded)
    {
        if (preparation is { } prepared)
        {
            return (
                prepared.ProjectedPixelWidth * MillimetresPerInch / prepared.ProductionDpi,
                prepared.ProjectedPixelHeight * MillimetresPerInch / prepared.ProductionDpi);
        }

        double x = decoded.XResolutionDpi > 0 ? decoded.XResolutionDpi : output.Dimensions.Dpi;
        double y = decoded.YResolutionDpi > 0 ? decoded.YResolutionDpi : output.Dimensions.Dpi;
        return (decoded.PixelWidth * MillimetresPerInch / x, decoded.PixelHeight * MillimetresPerInch / y);
    }

    /// <summary>The effective source resolution, or null when nothing authoritative says (§25).</summary>
    private static TiffEffectiveResolution? EffectiveResolution(
        PhotoshopPreparation? preparation, double widthMm, double heightMm)
    {
        if (preparation is not { } prepared)
        {
            return null;
        }

        bool requiresAuthority = prepared is TargetEdgePreparation
        {
            Plan.RequiresEnlargementAuthority: true,
        };

        return new TiffEffectiveResolution(
            prepared.SourceRevisionId,
            prepared.SourcePixelWidth,
            prepared.SourcePixelHeight,
            widthMm,
            heightMm,
            TiffEffectiveResolution.EffectiveDpi(prepared.SourcePixelWidth, widthMm),
            TiffEffectiveResolution.EffectiveDpi(prepared.SourcePixelHeight, heightMm),
            prepared.ProductionDpi,
            requiresAuthority,
            prepared is TargetEdgePreparation { IsAuthorisedEnlargement: true });
    }
}
