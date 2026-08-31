using System.Globalization;
using System.IO;
using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Reviews;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

/// <summary>
/// The final TIFF review as an operator meets it: the same Approve and Reject controls every
/// other review uses, wearing wording that says what is being decided
/// (Epic 11400 Part C2B §22–§26).
/// </summary>
/// <remarks>
/// Every assertion about an outcome reads what was <b>persisted</b> as well as what the screen
/// says, for the reason Part 3C3B established: a screen claiming a TIFF is approved while the
/// database and the workspace say otherwise is exactly the defect these exist to catch — and in
/// this slice the workspace is part of the claim, because approving moves a file.
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class TiffFinalReviewUiTests
{
    // -----------------------------------------------------------------------------------
    // §22, §23 — the same controls, TIFF wording
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// The TIFF review reuses the generic review panel and only changes its words; an ordinary
    /// image review keeps the generic ones (§22, §23).
    /// </summary>
    /// <remarks>
    /// The second half is the point. If TIFF wording leaked into every review, the "Reject and
    /// make another TIFF" button would appear under a trimmed PNG — which is how a screen that
    /// special-cases by state instead of by artefact goes wrong.
    /// </remarks>
    [Fact]
    public async Task The_TIFF_review_uses_the_generic_controls_with_TIFF_wording()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await ReviewRequiredTiffAsync(harness, "tiff-wording.png");
        SessionViewModel screen = open.Screen;

        screen.IsReviewRequired.ShouldBeTrue();
        screen.CanApprove.ShouldBeTrue();
        screen.CanReject.ShouldBeTrue();

        screen.IsProductionTiffReview.ShouldBeTrue();
        screen.ReviewHeading.ShouldBe(Text.TiffReviewHeading);
        screen.ApproveLabel.ShouldBe(Text.ApproveTiff);
        screen.RejectLabel.ShouldBe(Text.RejectTiff);

        // What was established, and the limit of the claim — no parser vocabulary, and no
        // suggestion that print quality was judged (§23).
        screen.TiffReviewSummary.ShouldContain("W1");
        screen.TiffReviewCaveat.ShouldBe(Text.TiffReviewCaveat);

        // The facts §22 asks for are the ones the artefact pane already shows.
        screen.ArtefactFileName.ShouldContain(".tif");
        screen.ArtefactHash.ShouldNotBeNullOrWhiteSpace();
        screen.ArtefactPixels.ShouldNotBeNullOrWhiteSpace();

        // An image review on the other workflow keeps the generic wording.
        using HomeScreenHarness assetHarness = new();
        SessionViewModel asset = (await ReviewRequiredTrimAsync(assetHarness, "png-wording.png")).Screen;
        asset.IsReviewRequired.ShouldBeTrue();
        asset.IsProductionTiffReview.ShouldBeFalse();
        asset.ReviewHeading.ShouldBe(Text.ReviewHeading);
        asset.ApproveLabel.ShouldBe(Text.Approve);
        asset.RejectLabel.ShouldBe(Text.Reject);
    }

    // -----------------------------------------------------------------------------------
    // §24 — after approval
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// Approving through the screen clears the review panel, moves the deliverable, and reports
    /// where it went (§24).
    /// </summary>
    [Fact]
    public async Task Approving_clears_the_review_panel_and_reports_the_Approved_location()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await ReviewRequiredTiffAsync(harness, "approve-ui.png");
        SessionViewModel screen = open.Screen;

        screen.Outputs.ShouldHaveSingleItem().Location.ShouldBe(Text.LocationWorking);

        await screen.ApproveCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();

        screen.IsReviewRequired.ShouldBeFalse();
        screen.CanApprove.ShouldBeFalse();
        screen.CanReject.ShouldBeFalse();
        screen.CanComplete.ShouldBeTrue();

        PrintOutputRow row = screen.Outputs.ShouldHaveSingleItem();
        row.Review.ShouldBe(Text.ReviewApproved);
        row.Location.ShouldBe(Text.LocationApproved);
        row.IsAvailable.ShouldBeTrue();

        await screen.CompleteCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();
        screen.State.ShouldBe(Text.SessionCompleted);

        // And the screen is telling the truth about the workspace.
        SessionAggregate persisted = await open.ReloadAsync();
        persisted.Session.State.ShouldBe(SessionState.Completed);

        PrintOutput output = persisted.Outputs.ShouldHaveSingleItem();
        output.ReviewState.ShouldBe(ReviewState.Approved);
        output.File.Area.ShouldBe(WorkspaceArea.Approved);
        File.Exists(open.Harness.Inner.FileWorkspace.ResolveAbsolute(output.File)).ShouldBeTrue();
    }

    // -----------------------------------------------------------------------------------
    // §25 — after rejection
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// Rejecting through the screen leaves a retryable step, an active session, and a row that
    /// says the bytes are gone (§25).
    /// </summary>
    [Fact]
    public async Task Rejecting_leaves_a_retryable_step_and_stops_offering_the_recycled_TIFF()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await ReviewRequiredTiffAsync(harness, "reject-ui.png");
        SessionViewModel screen = open.Screen;

        SessionAggregate before = await open.ReloadAsync();
        Revision tiff = before.Revisions.Last(r => r.Operation == OperationKind.PhotoshopOutput);
        string tiffPath = open.Harness.Inner.FileWorkspace.ResolveAbsolute(tiff.File);

        screen.SelectedRejectionReason = screen.RejectionReasons
            .Single(choice => choice.Reason == RejectionReason.WhiteInkIssue);
        screen.RejectionNotes = "underbase too tight";

        await screen.RejectCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();

        screen.IsReviewRequired.ShouldBeFalse();
        screen.CanRetry.ShouldBeTrue();
        screen.CanComplete.ShouldBeFalse();

        PrintOutputRow row = screen.Outputs.ShouldHaveSingleItem();
        row.Review.ShouldBe(Text.ReviewRejected);
        row.Location.ShouldBe(Text.LocationRecycled);
        row.IsAvailable.ShouldBeFalse();

        SessionAggregate persisted = await open.ReloadAsync();
        persisted.Session.State.ShouldBe(SessionState.Active);
        persisted.Steps.Single(s => s.Step == StepKind.PhotoshopOutput).State.ShouldBe(StepState.RetryRequired);

        ReviewDecision decision = persisted.Reviews.Single(r => r.Step == StepKind.PhotoshopOutput);
        decision.IsApproved.ShouldBeFalse();
        decision.QuickReason.ShouldBe(RejectionReason.WhiteInkIssue);
        decision.Notes.ShouldBe("underbase too tight");

        open.Harness.Inner.RecycleBin.Recycled.ShouldBe([tiffPath]);
        File.Exists(tiffPath).ShouldBeFalse();

        // And the operator can start another attempt from the screen.
        await screen.RetryCommand.ExecuteAsync(null);
        await screen.RunStepCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();
        screen.IsReviewRequired.ShouldBeTrue();
    }

    // -----------------------------------------------------------------------------------
    // §26 — nothing happens until the command runs
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// Choosing a reason and typing notes changes nothing: no review, no file work, no state
    /// (§26).
    /// </summary>
    /// <remarks>
    /// There is no confirmation dialog to cancel, because the reason and the notes are part of the
    /// review panel itself — so "Cancel" is simply not pressing Reject, and this is what that has
    /// to mean. It matters more here than it did for an image review: pressing Reject now disposes
    /// of a file, so a screen that acted while the operator was still choosing a reason would
    /// recycle a TIFF nobody had decided about.
    /// </remarks>
    [Fact]
    public async Task Choosing_a_rejection_reason_performs_no_review_and_no_file_work()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await ReviewRequiredTiffAsync(harness, "reject-cancel.png");
        SessionViewModel screen = open.Screen;

        SessionAggregate before = await open.ReloadAsync();
        Revision tiff = before.Revisions.Last(r => r.Operation == OperationKind.PhotoshopOutput);
        string tiffPath = open.Harness.Inner.FileWorkspace.ResolveAbsolute(tiff.File);

        screen.SelectedRejectionReason = screen.RejectionReasons
            .Single(choice => choice.Reason == RejectionReason.ColourIssue);
        screen.RejectionNotes = "thinking about it";

        // The operator changes their mind and simply does not press Reject.
        screen.RejectionNotes = string.Empty;

        open.Harness.Inner.RecycleBin.Recycled.ShouldBeEmpty();
        File.Exists(tiffPath).ShouldBeTrue();

        SessionAggregate after = await open.ReloadAsync();
        after.Reviews.ShouldNotContain(r => r.Step == StepKind.PhotoshopOutput);
        after.Outputs.ShouldHaveSingleItem().ReviewState.ShouldBe(ReviewState.NotReviewed);
        after.Outputs.ShouldHaveSingleItem().File.Area.ShouldBe(WorkspaceArea.Working);
        after.Steps.Single(s => s.Step == StepKind.PhotoshopOutput).State.ShouldBe(StepState.ReviewRequired);
        screen.IsReviewRequired.ShouldBeTrue();

        string approved = Path.Combine(
            open.Harness.Inner.FileWorkspace.ResolveAbsoluteDirectory(after.Session.Workspace), "Approved");
        (Directory.Exists(approved)
            ? Directory.GetFiles(approved, "*", SearchOption.AllDirectories)
            : []).ShouldBeEmpty();
    }

    // -----------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------

    /// <summary>Localised text a test compares against, resolved the same way the screen does.</summary>
    private static class Text
    {
        public static string ReviewHeading => Resolve("Session_ReviewHeading");

        public static string TiffReviewHeading => Resolve("Session_TiffReviewHeading");

        public static string TiffReviewCaveat => Resolve("Session_TiffReviewCaveat");

        public static string Approve => Resolve("Session_Approve");

        public static string Reject => Resolve("Session_Reject");

        public static string ApproveTiff => Resolve("Session_ApproveTiff");

        public static string RejectTiff => Resolve("Session_RejectTiff");

        public static string ReviewApproved => Resolve("ReviewState_Approved");

        public static string ReviewRejected => Resolve("ReviewState_Rejected");

        public static string SessionCompleted => Resolve("SessionState_Completed");

        public static string LocationWorking => Resolve("OutputLocation_Working");

        public static string LocationApproved => Resolve("OutputLocation_Approved");

        public static string LocationRecycled => Resolve("OutputLocation_Recycled");

        private static string Resolve(string key) =>
            new System.Resources.ResourceManager(
                    "PrintFlow.App.Resources.Strings", typeof(SessionViewModel).Assembly)
                .GetString(key, CultureInfo.CurrentUICulture)
            ?? throw new InvalidOperationException($"No resource '{key}'.");
    }

    /// <summary>A session screen, plus the identity needed to read the same session back.</summary>
    private sealed record OpenSession(HomeScreenHarness Harness, SessionViewModel Screen, SessionId Id)
    {
        public async Task<SessionAggregate> ReloadAsync() =>
            (await Harness.Inner.Repository.LoadAsync(Id, CancellationToken.None)).Value!;
    }

    /// <summary>Drives a GeneratePrintTiff session through the screen to a TIFF awaiting review.</summary>
    private static async Task<OpenSession> ReviewRequiredTiffAsync(HomeScreenHarness harness, string fileName)
    {
        OpenSession open = await OpenAsync(harness, fileName, WorkflowType.GeneratePrintTiff);
        SessionViewModel screen = open.Screen;

        await screen.ConfirmOriginalCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();

        screen.WidthMmText = 200d.ToString(CultureInfo.CurrentCulture);
        screen.HeightMmText = 150d.ToString(CultureInfo.CurrentCulture);
        await screen.SetMaximumBoundsCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();

        screen.SelectedWhiteUnderbaseChoice = screen.WhiteUnderbaseChoices
            .Single(choice => choice.Branch == WhiteUnderbaseBranch.W1_1px);
        await screen.SelectWhiteUnderbaseCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();

        await screen.RunStepCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();
        screen.IsReviewRequired.ShouldBeTrue();

        return open;
    }

    /// <summary>Drives a PrepareAsset session to a trimmed PNG awaiting review.</summary>
    private static async Task<OpenSession> ReviewRequiredTrimAsync(HomeScreenHarness harness, string fileName)
    {
        OpenSession open = await OpenAsync(harness, fileName, WorkflowType.PrepareAsset);
        SessionViewModel screen = open.Screen;

        await screen.ConfirmOriginalCommand.ExecuteAsync(null);
        await screen.SkipCommand.ExecuteAsync(null);   // Enhancement
        await screen.SkipCommand.ExecuteAsync(null);   // BackgroundRemoval
        await screen.RunStepCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();

        return open;
    }

    /// <summary>Imports a synthetic file and opens the session screen the way an operator does.</summary>
    private static async Task<OpenSession> OpenAsync(
        HomeScreenHarness harness, string fileName, WorkflowType workflow)
    {
        harness.FilePicker.Path = harness.WriteSourceFile(fileName);
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);
        harness.Home.Notice.ShouldBeNull();

        RecordingNavigation navigation = new();
        WorkflowSelectionViewModel selection = harness.WorkflowSelection(navigation);
        selection.Open(harness.Navigation.WorkflowSelectionFor!);
        await selection.SelectCommand.ExecuteAsync(
            selection.Workflows.Single(choice => choice.Type == workflow));
        selection.Notice.ShouldBeNull();

        SessionView chosen = navigation.SessionFor.ShouldNotBeNull();
        SessionViewModel screen = harness.Session(navigation);
        screen.Open(chosen);
        await screen.PreviewsLoaded;

        return new OpenSession(harness, screen, chosen.Id);
    }
}
