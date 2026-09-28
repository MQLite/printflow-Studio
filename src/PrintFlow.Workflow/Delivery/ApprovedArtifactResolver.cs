using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Reviews;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Workflow.Delivery;

public enum ArtifactKind { ApprovedAssetPng, ApprovedPrintTiff }

public readonly record struct ArtifactKey(SessionId SessionId, ArtifactKind Kind, Guid ArtifactId);

public enum ArtifactRefusal
{
    None,
    Missing,
    ApprovalEvidenceMissing,
    PendingReview,
    Rejected,
    Invalidated,
    Recycled,
    Obsolete,
    InconsistentLineage,
}

public sealed record ApprovedArtifact(
    ArtifactKey Key,
    WorkspaceFileRef File,
    Sha256 ApprovedSha256,
    long ApprovedLength,
    ReviewId ReviewId,
    ReviewSubjectKind ApprovalSubjectKind,
    Guid ApprovalSubjectId,
    RevisionId? SourceRevisionId,
    string SuggestedFileName,
    double? PhysicalWidthMm,
    double? PhysicalHeightMm);

public sealed record ArtifactResolution(ApprovedArtifact? Artifact, ArtifactRefusal Refusal)
{
    public bool IsEligible => Artifact is not null && Refusal == ArtifactRefusal.None;
    public static ArtifactResolution Eligible(ApprovedArtifact artifact) => new(artifact, ArtifactRefusal.None);
    public static ArtifactResolution Refused(ArtifactRefusal refusal) => new(null, refusal);
}

/// <summary>Pure, exact-ID approval and lineage predicate; no file read grants authority.</summary>
public static class ApprovedArtifactResolver
{
    public static ArtifactResolution Resolve(SessionAggregate aggregate, ArtifactKey key)
    {
        if (key.SessionId != aggregate.Session.Id || key.ArtifactId == Guid.Empty)
            return ArtifactResolution.Refused(ArtifactRefusal.Missing);
        return key.Kind switch
        {
            ArtifactKind.ApprovedAssetPng => ResolvePng(aggregate, key),
            ArtifactKind.ApprovedPrintTiff => ResolveTiff(aggregate, key),
            _ => ArtifactResolution.Refused(ArtifactRefusal.Missing),
        };
    }

    private static ArtifactResolution ResolvePng(SessionAggregate aggregate, ArtifactKey key)
    {
        if (aggregate.Session.WorkflowType != WorkflowType.PrepareAsset)
            return ArtifactResolution.Refused(ArtifactRefusal.Missing);
        Revision? promoted = aggregate.Revisions.SingleOrDefault(r => r.Id.Value == key.ArtifactId);
        if (promoted is null) return ArtifactResolution.Refused(ArtifactRefusal.Missing);
        if (!promoted.IsValid || promoted.RetentionReleasedAtUtc is not null)
            return ArtifactResolution.Refused(ArtifactRefusal.Invalidated);
        if (promoted.Operation != OperationKind.PromoteApproved || promoted.File.Area != WorkspaceArea.Approved ||
            promoted.Facts.Format != ImageFormat.Png || promoted.Facts.ByteLength <= 0)
            return ArtifactResolution.Refused(ArtifactRefusal.InconsistentLineage);
        SessionStep? terminal = aggregate.Steps.SingleOrDefault(s => s.Step == StepKind.ApprovedPngExport);
        if (terminal is null || terminal.State != StepState.Approved || terminal.CurrentRevisionId != promoted.Id ||
            terminal.CurrentRevisionSha256 != promoted.Sha256)
            return ArtifactResolution.Refused(ArtifactRefusal.Obsolete);
        Revision? source = aggregate.Revisions.SingleOrDefault(r => r.Id == promoted.SourceRevisionId);
        ProcessingAttempt? promotion = aggregate.Attempts.SingleOrDefault(a => a.OutputRevisionId == promoted.Id);
        if (source is { IsValid: false } || source?.RetentionReleasedAtUtc is not null)
            return ArtifactResolution.Refused(ArtifactRefusal.Invalidated);
        if (source is null || promotion is null || promotion.Status != AttemptStatus.Succeeded ||
            promotion.Operation != OperationKind.PromoteApproved || promotion.InputRevisionId != source.Id ||
            source.SessionId != key.SessionId || promoted.SessionId != key.SessionId ||
            source.Sha256 != promoted.Sha256 || source.Facts.ByteLength != promoted.Facts.ByteLength ||
            !ValidAncestors(aggregate, source.Id))
            return ArtifactResolution.Refused(ArtifactRefusal.InconsistentLineage);
        if (source.ReviewState == ReviewState.Rejected)
            return ArtifactResolution.Refused(ArtifactRefusal.Rejected);
        ReviewDecision? latest = LatestReview(aggregate, ReviewSubjectKind.Revision, source.Id.Value);
        if (latest is null || source.ReviewState != ReviewState.Approved)
            return ArtifactResolution.Refused(ArtifactRefusal.ApprovalEvidenceMissing);
        if (!latest.IsApproved) return ArtifactResolution.Refused(ArtifactRefusal.Rejected);
        if (latest.ReviewedSha256 != source.Sha256)
            return ArtifactResolution.Refused(ArtifactRefusal.InconsistentLineage);
        return ArtifactResolution.Eligible(new ApprovedArtifact(
            key, promoted.File, promoted.Sha256, promoted.Facts.ByteLength, latest.Id,
            ReviewSubjectKind.Revision, source.Id.Value, source.Id, promoted.File.FileName, null, null));
    }

    private static ArtifactResolution ResolveTiff(SessionAggregate aggregate, ArtifactKey key)
    {
        PrintOutput? output = aggregate.Outputs.SingleOrDefault(o => o.Id.Value == key.ArtifactId);
        if (output is null || output.SessionId != key.SessionId)
            return ArtifactResolution.Refused(ArtifactRefusal.Missing);
        if (output.RecycledAtUtc is not null) return ArtifactResolution.Refused(ArtifactRefusal.Recycled);
        if (!output.IsValid) return ArtifactResolution.Refused(ArtifactRefusal.Invalidated);
        if (output.ReviewState == ReviewState.Rejected)
            return ArtifactResolution.Refused(ArtifactRefusal.Rejected);
        if (output.ReviewState != ReviewState.Approved)
            return ArtifactResolution.Refused(ArtifactRefusal.PendingReview);
        if (output.PromotionReservation is not null || output.File.Area != WorkspaceArea.Approved ||
            output.ByteLength <= 0)
            return ArtifactResolution.Refused(ArtifactRefusal.InconsistentLineage);
        RevisionId twinId = RevisionId.From(output.Id.Value);
        Revision? twin = aggregate.Revisions.SingleOrDefault(r => r.Id == twinId);
        ProcessingAttempt? producing = aggregate.Attempts.SingleOrDefault(a => a.OutputRevisionId == twinId);
        if (twin is { IsValid: false } || twin?.RetentionReleasedAtUtc is not null ||
            aggregate.Revisions.Any(r => r.Id == output.SourceRevisionId &&
                (!r.IsValid || r.RetentionReleasedAtUtc is not null)))
            return ArtifactResolution.Refused(ArtifactRefusal.Invalidated);
        if (twin is null || !twin.IsValid || twin.SessionId != key.SessionId ||
            twin.Operation != OperationKind.PhotoshopOutput || twin.SourceRevisionId != output.SourceRevisionId ||
            twin.Sha256 != output.Sha256 || twin.Facts.ByteLength != output.ByteLength ||
            twin.Facts.Format != ImageFormat.Tiff || producing is null ||
            producing.Status != AttemptStatus.Succeeded || producing.InputRevisionId != output.SourceRevisionId ||
            !ValidAncestors(aggregate, output.SourceRevisionId))
            return ArtifactResolution.Refused(ArtifactRefusal.InconsistentLineage);
        SessionStep? step = aggregate.Steps.SingleOrDefault(s => s.Step == StepKind.PhotoshopOutput);
        if (step?.CurrentRevisionId == twinId &&
            (step.State != StepState.Approved || step.CurrentRevisionSha256 != output.Sha256))
            return ArtifactResolution.Refused(ArtifactRefusal.Obsolete);
        ReviewDecision? latest = LatestReview(aggregate, ReviewSubjectKind.PrintOutput, output.Id.Value);
        if (latest is null) return ArtifactResolution.Refused(ArtifactRefusal.ApprovalEvidenceMissing);
        if (!latest.IsApproved) return ArtifactResolution.Refused(ArtifactRefusal.Rejected);
        if (latest.ReviewedSha256 != output.Sha256)
            return ArtifactResolution.Refused(ArtifactRefusal.InconsistentLineage);

        // Output-bound preparation is the physical-size authority. A legacy attempt without
        // preparation requires the separate read-only TIFF metadata fallback at the service edge.
        double? width = producing.Preparation is { } p
            ? p.ProjectedPixelWidth * 25.4 / p.ProductionDpi : null;
        double? height = producing.Preparation is { } q
            ? q.ProjectedPixelHeight * 25.4 / q.ProductionDpi : null;
        return ArtifactResolution.Eligible(new ApprovedArtifact(
            key, output.File, output.Sha256, output.ByteLength, latest.Id,
            ReviewSubjectKind.PrintOutput, output.Id.Value, null, output.File.FileName, width, height));
    }

    private static ReviewDecision? LatestReview(SessionAggregate aggregate, ReviewSubjectKind kind, Guid id) =>
        aggregate.Reviews.Where(r => r.SessionId == aggregate.Session.Id && r.SubjectKind == kind && r.SubjectId == id)
            .OrderByDescending(r => r.DecidedAtUtc).ThenByDescending(r => r.Id.Value).FirstOrDefault();

    private static bool ValidAncestors(SessionAggregate aggregate, RevisionId start)
    {
        HashSet<RevisionId> seen = [];
        RevisionId? current = start;
        while (current is { } id)
        {
            if (!seen.Add(id)) return false;
            Revision? revision = aggregate.Revisions.SingleOrDefault(r => r.Id == id);
            if (revision is null || revision.SessionId != aggregate.Session.Id || !revision.IsValid ||
                revision.RetentionReleasedAtUtc is not null || revision.Facts.ByteLength <= 0)
                return false;
            current = revision.SourceRevisionId;
        }
        return true;
    }
}
