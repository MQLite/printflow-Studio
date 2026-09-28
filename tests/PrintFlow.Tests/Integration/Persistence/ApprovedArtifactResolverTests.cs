using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Reviews;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Delivery;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Persistence;

public sealed class ApprovedArtifactResolverTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Sha256 Hash = Sha256.Parse(new string('A', 64));

    [Fact]
    public void Promoted_png_inherits_immediate_source_human_review()
    {
        SessionAggregate aggregate = PngAggregate(withReview: true);
        Revision promoted = aggregate.Revisions[^1];

        ArtifactResolution resolved = ApprovedArtifactResolver.Resolve(
            aggregate, new ArtifactKey(aggregate.Session.Id, ArtifactKind.ApprovedAssetPng, promoted.Id.Value));

        resolved.IsEligible.ShouldBeTrue();
        resolved.Artifact!.ReviewId.ShouldBe(aggregate.Reviews[0].Id);
        resolved.Artifact.SourceRevisionId.ShouldBe(aggregate.Revisions[0].Id);
        resolved.Artifact.ApprovedSha256.ShouldBe(Hash);
    }

    [Fact]
    public void Promoted_png_without_human_review_fails_closed()
    {
        SessionAggregate aggregate = PngAggregate(withReview: false);

        ArtifactResolution resolved = ApprovedArtifactResolver.Resolve(
            aggregate, new ArtifactKey(aggregate.Session.Id, ArtifactKind.ApprovedAssetPng,
                aggregate.Revisions[^1].Id.Value));

        resolved.IsEligible.ShouldBeFalse();
        resolved.Refusal.ShouldBe(ArtifactRefusal.ApprovalEvidenceMissing);
    }

    [Fact]
    public void Rejected_invalidated_or_obsolete_promoted_png_is_refused_by_exact_id()
    {
        SessionAggregate baseline = PngAggregate(withReview: true);
        Revision promoted = baseline.Revisions[^1];
        ArtifactKey key = new(baseline.Session.Id, ArtifactKind.ApprovedAssetPng, promoted.Id.Value);
        SessionAggregate rejected = baseline with { Revisions =
            [baseline.Revisions[0] with { ReviewState = ReviewState.Rejected }, promoted] };
        ApprovedArtifactResolver.Resolve(rejected, key).Refusal.ShouldBe(ArtifactRefusal.Rejected);
        SessionAggregate invalidated = baseline with { Revisions =
            [baseline.Revisions[0], promoted with { IsValid = false }] };
        ApprovedArtifactResolver.Resolve(invalidated, key).Refusal.ShouldBe(ArtifactRefusal.Invalidated);
        SessionAggregate sourceInvalidated = baseline with { Revisions =
            [baseline.Revisions[0] with { IsValid = false }, promoted] };
        ApprovedArtifactResolver.Resolve(sourceInvalidated, key).Refusal.ShouldBe(ArtifactRefusal.Invalidated);
        SessionAggregate obsolete = baseline with { Steps = baseline.Steps.Select(s =>
            s.Step == StepKind.ApprovedPngExport
                ? s with { CurrentRevisionId = RevisionId.From(Guid.NewGuid()) } : s).ToArray() };
        ApprovedArtifactResolver.Resolve(obsolete, key).Refusal.ShouldBe(ArtifactRefusal.Obsolete);
    }

    private static SessionAggregate PngAggregate(bool withReview)
    {
        SessionId sessionId = SessionId.From(Guid.NewGuid());
        RevisionId sourceId = RevisionId.From(Guid.NewGuid());
        RevisionId promotedId = RevisionId.From(Guid.NewGuid());
        var session = new ProcessingSession(sessionId, WorkflowType.PrepareAsset,
            OutputName.Create("art").Name, StepKind.ApprovedPngExport, SessionState.Active,
            WorkspaceDirRef.Create("Sessions/test"), Now, Now, null, null, null, null, null, null, null);
        FileFacts facts = new(ImageFormat.Png, 12, Hash, 2, 2, 96, 96, ColourMode.Rgb, true);
        Revision source = Revision.Create(sourceId, sessionId, null, OperationKind.Import,
            WorkspaceFileRef.Create("Sessions/test/Source/a.png", WorkspaceArea.Source), facts, Now)
            with { ReviewState = withReview ? ReviewState.Approved : ReviewState.NotReviewed };
        Revision promoted = Revision.Create(promotedId, sessionId, sourceId, OperationKind.PromoteApproved,
            WorkspaceFileRef.Create("Sessions/test/Approved/a.png", WorkspaceArea.Approved), facts, Now);
        AttemptId attemptId = AttemptId.From(Guid.NewGuid());
        ProcessingAttempt attempt = ProcessingAttempt.Start(attemptId, sessionId, StepKind.ApprovedPngExport,
            sourceId, OperationKind.PromoteApproved, "internal-promote-v1", Now).Succeed(promotedId, Now);
        ReviewDecision[] reviews = withReview
            ? [ReviewDecision.Approve(ReviewId.From(Guid.NewGuid()), sessionId, StepKind.OriginalConfirmation,
                ReviewSubjectKind.Revision, sourceId.Value, Hash, "operator", Now)]
            : [];
        SessionStep[] steps =
        [
            new(StepKind.Import, 0, StepState.Approved, sourceId, Hash, null, 1, Now),
            new(StepKind.ApprovedPngExport, 1, StepState.Approved, promotedId, Hash, null, 1, Now),
        ];
        return new SessionAggregate(session, null, steps, [source, promoted], [attempt], reviews, []);
    }
}
