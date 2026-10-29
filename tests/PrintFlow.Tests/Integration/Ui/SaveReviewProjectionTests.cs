using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class SaveReviewProjectionTests
{
    [Fact]
    public async Task Png_promotion_reports_the_actual_review_input_and_survives_reload()
    {
        using SessionServiceHarness harness = new();
        var service = harness.CreateService();
        var review = await FinalSaveFixtures.PngAtFinalReviewAsync(harness, service);
        var expectedInput = review.UpstreamArtefact!;
        review.ApprovedPngReviewInput.ShouldBeNull("no PNG export exists before approval");
        (await service.ExecuteAsync(review.Id,
            new WorkflowCommand.Approve(StepKind.Trim, review.CurrentArtefact!.Sha256), "tester", default)).IsSuccess.ShouldBeTrue();
        var exported = (await service.ExecuteAsync(review.Id,
            new WorkflowCommand.StartStep(StepKind.ApprovedPngExport), "tester", default)).Value;

        foreach (var state in new[] { exported, (await service.LoadAsync(review.Id, default)).Value })
        {
            var input = state.ApprovedPngReviewInput.ShouldNotBeNull();
            input.RevisionId.ShouldBe(expectedInput.RevisionId);
            input.Sha256.ShouldBe(expectedInput.Sha256);
            input.FileName.ShouldBe(expectedInput.FileName);
            input.RevisionId.ShouldNotBe(state.UpstreamArtefact!.RevisionId,
                "the approved trim and its PNG export have identical bytes but are not the before/after pair");
        }

        var missingInput = exported with
        {
            History = exported.History.Select(step => step with
            {
                Revisions = step.Revisions.Where(revision => revision.Id != expectedInput.RevisionId).ToArray(),
            }).ToArray(),
        };
        missingInput.ApprovedPngReviewInput.ShouldBeNull("missing lineage must never fall back to another revision");

        var invalidTrim = exported with
        {
            History = exported.History.Select(step => step with
            {
                Revisions = step.Revisions.Select(revision => revision.Id == review.CurrentArtefact!.RevisionId
                    ? revision with { IsValid = false } : revision).ToArray(),
            }).ToArray(),
        };
        invalidTrim.ApprovedPngReviewInput.ShouldBeNull("an invalidated trim is never presented as an approved comparison");
    }

    [Fact]
    public async Task A_tiff_review_does_not_borrow_a_png_review_input()
    {
        using SessionServiceHarness harness = new();
        var service = FinalSaveFixtures.TiffService(harness);
        var review = await FinalSaveFixtures.TiffAtFinalReviewAsync(harness, service);
        review.ApprovedPngReviewInput.ShouldBeNull();
    }
}
