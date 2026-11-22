using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Ports;
using static PrintFlow.Tests.Fixtures.CorrectionFixtures;

namespace PrintFlow.Tests.Integration.Persistence;

[Collection(SqliteCollection.Name)]
public sealed class PhotoshopBackgroundEditTests
{
    [Fact]
    public async Task Valid_save_replaces_only_the_exact_pending_review()
    {
        using SessionServiceHarness h = new();
        var service = Service(h);
        var review = await AtBackgroundRemovalReviewAsync(h, service, opaqueSource: true);
        var shown = review.CurrentArtefact!;
        var target = review.PhotoshopBackgroundEdit!;
        var save = new PhotoshopBackgroundSave(h.Workspace.CreateSourceFile("cutout.psd", [1]),
            shown.Sha256, CorrectedPng(h));

        var source = await service.GetPhotoshopBackgroundEditSourceAsync(review.Id, target, default);
        source.IsSuccess.ShouldBeTrue();
        var edited = await MustAsync(service.ImportPhotoshopBackgroundEditAsync(
            review.Id, target, save, "qa", default));

        edited.CurrentStep!.State.ShouldBe(StepState.ReviewRequired);
        edited.CurrentArtefact!.RevisionId.ShouldNotBe(shown.RevisionId);
        edited.CurrentArtefact.IsManualProcessingResult.ShouldBeTrue();
        var saved = await LoadAsync(h, review.Id);
        saved.Revisions.Single(r => r.Id == shown.RevisionId).ReviewState.ShouldBe(ReviewState.NotReviewed);
        var attempt = saved.Attempts.Single(a => a.OutputRevisionId == edited.CurrentArtefact.RevisionId);
        attempt.AdapterNotes!.ShouldContain(shown.RevisionId.ToString());
        attempt.ManualResultSourcePath.ShouldBe(save.PsdPath);
        (await service.ImportPhotoshopBackgroundEditAsync(review.Id, target, save, "qa", default)).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task Wrong_canvas_leaves_the_existing_review_and_attempts_intact()
    {
        using SessionServiceHarness h = new();
        var service = Service(h);
        var review = await AtBackgroundRemovalReviewAsync(h, service);
        var shown = review.CurrentArtefact!;
        var before = await LoadAsync(h, review.Id);
        string wrongSize = h.Workspace.CreateSourceFile("wrong-size.png",
            SyntheticImages.PngWithAlpha(7, 5, (x, _) => x == 0 ? (byte)0 : (byte)255));

        var refused = await service.ImportPhotoshopBackgroundEditAsync(review.Id, review.PhotoshopBackgroundEdit!,
            new PhotoshopBackgroundSave(wrongSize, shown.Sha256, wrongSize), "qa", default);

        refused.IsFailure.ShouldBeTrue();
        refused.Failure.MessageKey.ShouldBe("Failure_ManualResultCanvas");
        var after = await LoadAsync(h, review.Id);
        after.Attempts.Count.ShouldBe(before.Attempts.Count);
        after.Revisions.Count.ShouldBe(before.Revisions.Count);
        after.Steps.Single(s => s.Step == StepKind.BackgroundRemoval).CurrentRevisionId.ShouldBe(shown.RevisionId);
    }
}
