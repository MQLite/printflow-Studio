using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Reviews;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Persistence;

/// <summary>
/// What the specialist review seam will and will not produce a payload for
/// (SCRUM-11104 §16, §38, §43, §45).
/// </summary>
/// <remarks>
/// The refusals are the content. A payload exists only for a Revision that is a <b>validated
/// production output</b> of the session the caller named, whose bytes still hash to what the
/// output records — so "decode first and see what it looks like" is not a route this seam offers,
/// and validation stays the gate rather than the preview (§45).
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class ProductionTiffReviewServiceTests
{
    [Fact]
    public async Task A_validated_production_output_yields_a_payload_bound_to_its_own_identity()
    {
        using HomeScreenHarness harness = TiffFinalReviewFixture.Harness(out _);
        TiffFinalReviewFixture.Review review =
            await TiffFinalReviewFixture.ReviewRequiredAsync(harness, "service-ok.png");

        SessionAggregate persisted = await review.ReloadAsync();
        PrintOutput output = persisted.Outputs.ShouldHaveSingleItem();
        Revision tiff = persisted.Revisions.Last(r => r.Operation == OperationKind.PhotoshopOutput);

        OperationResult<TiffReviewPayload> payload = await harness.Inner.TiffReviews
            .GetReviewAsync(review.Id, tiff.Id, CancellationToken.None);

        payload.IsSuccess.ShouldBeTrue(payload.IsFailure ? payload.Failure.ToString() : string.Empty);
        payload.Value.PrintOutputId.ShouldBe(output.Id);
        payload.Value.RevisionId.ShouldBe(tiff.Id);
        payload.Value.Sha256.ShouldBe(output.Sha256);
        payload.Value.Covers(output.Id, output.Sha256).ShouldBeTrue();
        payload.Value.OutputPath.ShouldBe(harness.Inner.FileWorkspace.ResolveAbsolute(output.File));
        payload.Value.Branch.ShouldBe(output.Branch);
        payload.Value.ByteLength.ShouldBe(output.ByteLength);
    }

    /// <summary>
    /// A Revision that is not a production output gets no payload, however decodable it is (§45).
    /// </summary>
    /// <remarks>
    /// The imported PNG is a perfectly good image that the general preview seam shows every day.
    /// It has no <c>PrintOutput</c> row, so it never passed production validation — and that,
    /// rather than anything about its bytes, is why the specialist surface refuses it.
    /// </remarks>
    [Fact]
    public async Task A_Revision_that_is_not_a_production_output_gets_no_payload()
    {
        using HomeScreenHarness harness = TiffFinalReviewFixture.Harness(out _);
        TiffFinalReviewFixture.Review review =
            await TiffFinalReviewFixture.ReviewRequiredAsync(harness, "service-not-output.png");

        SessionAggregate persisted = await review.ReloadAsync();
        Revision imported = persisted.Revisions.First(r => r.Operation == OperationKind.Import);

        OperationResult<TiffReviewPayload> payload = await harness.Inner.TiffReviews
            .GetReviewAsync(review.Id, imported.Id, CancellationToken.None);

        payload.IsFailure.ShouldBeTrue();
        payload.Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);
        payload.Failure.TechnicalDetail.ShouldContain("production output");
    }

    [Fact]
    public async Task A_Revision_of_another_session_is_indistinguishable_from_one_that_does_not_exist()
    {
        using HomeScreenHarness harness = TiffFinalReviewFixture.Harness(out _);
        TiffFinalReviewFixture.Review mine =
            await TiffFinalReviewFixture.ReviewRequiredAsync(harness, "service-mine.png");
        TiffFinalReviewFixture.Review theirs =
            await TiffFinalReviewFixture.ReviewRequiredAsync(harness, "service-theirs.png");

        Revision other = (await theirs.ReloadAsync())
            .Revisions.Last(r => r.Operation == OperationKind.PhotoshopOutput);

        OperationResult<TiffReviewPayload> borrowed = await harness.Inner.TiffReviews
            .GetReviewAsync(mine.Id, other.Id, CancellationToken.None);

        borrowed.IsFailure.ShouldBeTrue();
        borrowed.Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);

        OperationResult<TiffReviewPayload> invented = await harness.Inner.TiffReviews
            .GetReviewAsync(mine.Id, RevisionId.From(Guid.NewGuid()), CancellationToken.None);

        invented.IsFailure.ShouldBeTrue();
        invented.Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);
    }

    /// <summary>
    /// A rejected output's bytes are gone, and the seam says so rather than failing obscurely
    /// (§37).
    /// </summary>
    [Fact]
    public async Task A_recycled_output_is_reported_as_missing_rather_than_decoded()
    {
        using HomeScreenHarness harness = TiffFinalReviewFixture.Harness(out _);
        TiffFinalReviewFixture.Review review =
            await TiffFinalReviewFixture.ReviewRequiredAsync(harness, "service-recycled.png");

        Revision tiff = (await review.ReloadAsync())
            .Revisions.Last(r => r.Operation == OperationKind.PhotoshopOutput);

        review.Screen.SelectedRejectionReason = review.Screen.RejectionReasons
            .Single(choice => choice.Reason == RejectionReason.ColourIssue);
        await review.Screen.RejectCommand.ExecuteAsync(null);
        review.Screen.Notice.ShouldBeNull();

        (await review.ReloadAsync()).Outputs.ShouldHaveSingleItem().RecycledAtUtc.ShouldNotBeNull();

        OperationResult<TiffReviewPayload> payload = await harness.Inner.TiffReviews
            .GetReviewAsync(review.Id, tiff.Id, CancellationToken.None);

        payload.IsFailure.ShouldBeTrue();
        payload.Failure.Code.ShouldBe(FailureCode.OutputMissing);
    }

    /// <summary>
    /// The seam changes nothing it looks at (§36).
    /// </summary>
    [Fact]
    public async Task Asking_for_a_payload_advances_nothing()
    {
        using HomeScreenHarness harness = TiffFinalReviewFixture.Harness(out _);
        TiffFinalReviewFixture.Review review =
            await TiffFinalReviewFixture.ReviewRequiredAsync(harness, "service-inert.png");

        SessionAggregate before = await review.ReloadAsync();
        Revision tiff = before.Revisions.Last(r => r.Operation == OperationKind.PhotoshopOutput);

        for (int call = 0; call < 3; call++)
        {
            (await harness.Inner.TiffReviews.GetReviewAsync(review.Id, tiff.Id, CancellationToken.None))
                .IsSuccess.ShouldBeTrue();
        }

        SessionAggregate after = await review.ReloadAsync();
        after.Session.ShouldBe(before.Session);
        after.Outputs.ShouldBe(before.Outputs);
        after.Reviews.Count.ShouldBe(before.Reviews.Count);
        after.Revisions.Count.ShouldBe(before.Revisions.Count);
        after.Attempts.Count.ShouldBe(before.Attempts.Count);
    }
}
