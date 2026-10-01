using Microsoft.Data.Sqlite;
using PrintFlow.App.Resources;
using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Delivery;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

/// <summary>
/// SCRUM-11154 F-V8, observed on the workstation in zh-CN: after "Save approved result" the Session
/// screen offered Complete as the next action while the Recent row for the same job said "needs
/// your action · export reviewed PNG". The engine's own available commands decide what remains;
/// the historical save line stays separate and qualified.
/// </summary>
[Collection(SqliteCollection.Name)]
public sealed class RecentPendingCompletionTests
{
    private static string AwaitingComplete => Strings.Session_StatusInput + " · " + Strings.Resolve("Home_RecentStepComplete");

    [Fact]
    public async Task Saved_png_awaiting_Complete_says_so_on_Recent_then_Completed_keeps_history_separate()
    {
        using OperatorCultureScope culture = new("zh-CN");
        using HomeScreenHarness harness = TiffFinalReviewFixture.Harness(out _);
        SessionView review = await FinalSaveFixtures.PngAtFinalReviewAsync(harness.Inner, harness.Sessions);
        string folder = FinalSaveFixtures.NewFolder("recent-pending-complete");
        try
        {
            await SaveAsync(harness, review, folder, "pending.png");
            SessionView session = (await harness.Sessions.LoadAsync(review.Id, CancellationToken.None)).Value;
            session.State.ShouldBe(SessionState.Active, "saving never completes the job");
            session.AvailableCommands.ShouldContain(CommandKind.Complete, "the engine's next legal action");

            RecentSessionRow row = await RowAsync(harness, review);
            row.State.ShouldBe(AwaitingComplete);
            row.State.ShouldNotContain(DisplayNames.Step(StepKind.ApprovedPngExport));
            row.SaveHistoryText.ShouldBe(Strings.Home_RecentPngSavedPreviously);

            // Refresh and reopen read the same facts again; nothing was changed by listing.
            await harness.Home.RefreshCommand.ExecuteAsync(null);
            harness.Home.RecentSessions.Single(r => r.Id == review.Id).State.ShouldBe(AwaitingComplete);
            (await harness.Sessions.LoadAsync(review.Id, CancellationToken.None)).Value.State.ShouldBe(SessionState.Active);

            (await harness.Sessions.ExecuteAsync(review.Id, new WorkflowCommand.Complete(), "tester", CancellationToken.None)).IsSuccess.ShouldBeTrue();
            RecentSessionRow completed = await RowAsync(harness, review);
            completed.State.ShouldBe(Strings.Session_StatusCompleted);
            completed.SaveHistoryText.ShouldBe(Strings.Home_RecentPngSavedPreviously);
        }
        finally { FinalSaveFixtures.Remove(folder); }
    }

    [Fact]
    public async Task Failed_history_read_leaves_the_workflow_status_correct_and_the_history_unknown()
    {
        using OperatorCultureScope culture = new("zh-CN");
        using HomeScreenHarness harness = TiffFinalReviewFixture.Harness(out _);
        SessionView review = await FinalSaveFixtures.PngAtFinalReviewAsync(harness.Inner, harness.Sessions);
        string folder = FinalSaveFixtures.NewFolder("recent-pending-history-failure");
        try
        {
            await SaveAsync(harness, review, folder, "history.png");
            using (SqliteConnection connection = harness.Inner.Database.Factory.Open())
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA foreign_keys=OFF; DROP TABLE ArtifactDelivery;";
                command.ExecuteNonQuery();
            }

            RecentSessionRow row = await RowAsync(harness, review);
            row.State.ShouldBe(AwaitingComplete);
            row.SaveHistoryText.ShouldBe(Strings.Home_RecentSaveHistoryUnavailable);
        }
        finally { FinalSaveFixtures.Remove(folder); }
    }

    [Fact]
    public async Task Approved_but_unsaved_tiff_awaits_Complete_without_claiming_a_save()
    {
        using OperatorCultureScope culture = new("zh-CN");
        using HomeScreenHarness harness = TiffFinalReviewFixture.Harness(out _);
        SessionView sizeA = await FinalSaveFixtures.TiffAtFinalReviewAsync(harness.Inner, harness.Sessions, 200, "approved-unsaved");
        (await harness.Sessions.ExecuteAsync(sizeA.Id, new WorkflowCommand.Approve(StepKind.PhotoshopOutput, sizeA.CurrentArtefact!.Sha256),
            "tester", CancellationToken.None)).IsSuccess.ShouldBeTrue();

        RecentSessionRow row = await RowAsync(harness, sizeA);
        row.State.ShouldBe(AwaitingComplete);
        row.HasSaveHistoryText.ShouldBeFalse("approved is not saved");
    }

    [Fact]
    public async Task A_later_size_awaiting_review_is_current_while_the_earlier_saved_size_stays_historical()
    {
        using OperatorCultureScope culture = new("zh-CN");
        using HomeScreenHarness harness = TiffFinalReviewFixture.Harness(out _);
        SessionView sizeA = await FinalSaveFixtures.TiffAtFinalReviewAsync(harness.Inner, harness.Sessions, 200, "two-sizes");
        string folder = FinalSaveFixtures.NewFolder("recent-pending-second-size");
        try
        {
            await SaveAsync(harness, sizeA, folder, "size-a.tif");
            (await RowAsync(harness, sizeA)).State.ShouldBe(AwaitingComplete);
            (await harness.Sessions.ExecuteAsync(sizeA.Id, new WorkflowCommand.Complete(), "tester", CancellationToken.None)).IsSuccess.ShouldBeTrue();
            (await harness.Sessions.ExecuteAsync(sizeA.Id, new WorkflowCommand.AddAnotherSize(), "tester", CancellationToken.None)).IsSuccess.ShouldBeTrue();
            await FinalSaveFixtures.NextTiffSizeAsync(harness.Sessions, sizeA.Id, 250);

            RecentSessionRow row = await RowAsync(harness, sizeA);
            row.State.ShouldBe(Strings.Session_StatusReview + " · " + DisplayNames.Step(StepKind.PhotoshopOutput));
            row.SaveHistoryText.ShouldBe(string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Home_RecentTiffSavedPreviously, 1));
        }
        finally { FinalSaveFixtures.Remove(folder); }
    }

    [Fact]
    public void A_finished_step_whose_completion_was_not_established_is_not_guessed()
    {
        using OperatorCultureScope culture = new("zh-CN");
        SessionListItem item = new(new PrintFlow.Domain.Ids.SessionId(Guid.NewGuid()), WorkflowType.PrepareAsset,
            PrintFlow.Domain.Files.OutputName.Parse("unknown-completion"), StepKind.ApprovedPngExport, SessionState.Active, DateTimeOffset.UtcNow)
        { CurrentStepState = StepState.Approved };
        new RecentSessionRow(item).State.ShouldBe(Strings.Home_RecentStatusUnknown);
        new RecentSessionRow(item with { AwaitsCompletion = false }).State
            .ShouldBe(Strings.Session_StatusInput + " · " + DisplayNames.Step(StepKind.ApprovedPngExport));
        new RecentSessionRow(item with { AwaitsCompletion = true }).State.ShouldBe(AwaitingComplete);
    }

    private static async Task SaveAsync(HomeScreenHarness harness, SessionView review, string folder, string fileName)
    {
        var coordinator = new FinalSaveCoordinator(harness.Sessions, FinalSaveFixtures.Delivery(harness.Inner), "tester");
        (await coordinator.ConfirmAndSaveAsync(FinalSaveFixtures.Confirm(review, folder, fileName), null, CancellationToken.None))
            .Delivery!.Code.ShouldBe(DeliveryCode.Delivered);
    }

    private static async Task<RecentSessionRow> RowAsync(HomeScreenHarness harness, SessionView session) =>
        new((await harness.Sessions.ListRecentAsync(CancellationToken.None)).Value.Single(item => item.Id == session.Id));
}
