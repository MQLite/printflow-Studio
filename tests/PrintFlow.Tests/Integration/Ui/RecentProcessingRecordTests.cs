using System.IO;
using PrintFlow.App.Resources;
using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

/// <summary>
/// Recent Processing's picture and its record management (SCRUM-11117; Jira 11602;
/// MVP design §10, §13.2).
/// </summary>
/// <remarks>
/// The AC names two things the list did not have: a thumbnail, and a delete-record action. Both
/// are easy to build wrongly in the same way — by letting a list reach past the seams that exist
/// to contain it. A thumbnail is a picture of an artefact the session already persisted, decoded
/// through the same read-only preview seam a review uses; taking a record off the list changes
/// one nullable column and no file at all.
/// <para>
/// The tests are grouped by which of those two claims they defend, and the file-level assertions
/// are the point of the second group: an operator pressing "Remove from list" must not be able to
/// lose a customer's source, an approved PNG or a production TIFF.
/// </para>
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class RecentProcessingRecordTests
{
    // -------------------------------------------------------------------------------------
    // Thumbnail
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// A listed job shows a picture of the file the operator imported, bounded to a list size.
    /// </summary>
    /// <remarks>
    /// Both halves matter. The identity assertion says the picture came from the session's own
    /// root Revision — the persisted authoritative artefact — rather than from some other file;
    /// the size assertion says a list row costs a list row, not a review preview. The source is
    /// deliberately larger than the thumbnail bound so "bounded" is a fact about this payload
    /// rather than a coincidence of a small fixture.
    /// </remarks>
    [Fact]
    public async Task A_listed_job_shows_a_bounded_thumbnail_of_its_own_root_revision()
    {
        using HomeScreenHarness harness = new();
        string source = harness.Inner.Workspace.CreateSourceFile(
            "large-design.png", SyntheticImages.Png(600, 400, alpha: true));
        harness.FilePicker.Path = source;
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);
        SessionId id = harness.Navigation.WorkflowSelectionFor!.Id;

        await harness.Home.RefreshCommand.ExecuteAsync(null);
        await harness.Home.ThumbnailsLoaded;

        RecentSessionRow row = harness.Home.RecentSessions.Single();
        row.HasThumbnail.ShouldBeTrue();
        row.ThumbnailState.ShouldBeEmpty();
        row.ThumbnailName.ShouldContain(row.DisplayName);

        SessionAggregate stored =
            (await harness.Inner.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        Revision root = stored.Revisions.Single(revision => revision.IsRoot);

        OperationResult<ImagePreview> thumbnail =
            await harness.Previews.GetRecentThumbnailAsync(id, CancellationToken.None);
        thumbnail.IsSuccess.ShouldBeTrue();
        thumbnail.Value.RevisionId.ShouldBe(root.Id, "the list draws the session's own imported original");

        int bound = harness.Inner.PreviewDecoder.ThumbnailEdge;
        bound.ShouldBeLessThan(600, "the fixture must actually exercise the reduction");
        Math.Max(thumbnail.Value.PixelWidth, thumbnail.Value.PixelHeight).ShouldBeLessThanOrEqualTo(bound);
        thumbnail.Value.SourcePixelWidth.ShouldBe(600);
        thumbnail.Value.IsDownsampledForDisplay.ShouldBeTrue();
    }

    /// <summary>
    /// A picture that cannot be produced leaves the row usable and says nothing alarming.
    /// </summary>
    /// <remarks>
    /// Two causes in one test because the operator's next action is identical for both and the
    /// row must be identical too: an artefact whose file has gone, and a PSD whose managed raster
    /// does not exist yet. Neither is a problem with the job, so Resume/Details must still open
    /// it from real persisted state.
    /// </remarks>
    [Fact]
    public async Task A_row_whose_picture_cannot_be_produced_stays_neutral_and_still_opens()
    {
        using HomeScreenHarness harness = new();

        harness.FilePicker.Path = harness.WriteSourceFile("vanished.png");
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);
        SessionId raster = harness.Navigation.WorkflowSelectionFor!.Id;

        // A PSD root: a real, supported, deliberately undrawable artefact until preparation runs.
        harness.FilePicker.Path = harness.Inner.Workspace.CreateSourceFile(
            "unprepared.psd", PsdInputPreparationTests.RgbCompositePsd());
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);

        // ...and the raster session's file removed underneath it, which is what a cleaned-up or
        // externally deleted workspace looks like from Home.
        SessionAggregate stored =
            (await harness.Inner.Repository.LoadAsync(raster, CancellationToken.None)).Value!;
        string vanishing = harness.Inner.FileWorkspace.ResolveAbsolute(stored.Revisions.Single().File);
        // Only a test may do this: the imported source is held read-only precisely so the product
        // cannot. Clearing the attribute first is what makes "the file is gone" reachable at all.
        File.SetAttributes(vanishing, FileAttributes.Normal);
        File.Delete(vanishing);

        await harness.Home.RefreshCommand.ExecuteAsync(null);
        await harness.Home.ThumbnailsLoaded;

        harness.Home.RecentSessions.Count.ShouldBe(2);
        foreach (RecentSessionRow row in harness.Home.RecentSessions)
        {
            row.HasThumbnail.ShouldBeFalse();
            row.ThumbnailState.ShouldBe(Strings.Home_RecentNoThumbnail);
        }

        harness.Home.Notice.ShouldBeNull("a missing picture is not an operator problem");

        RecentSessionRow missing = harness.Home.RecentSessions.Single(row => row.Id == raster);
        await harness.Home.ResumeCommand.ExecuteAsync(missing);
        harness.Navigation.SessionFor.ShouldNotBeNull().Id.ShouldBe(raster);
    }

    /// <summary>
    /// Looking at the list changes nothing: no metadata, no file, no workflow state.
    /// </summary>
    /// <remarks>
    /// The exact claim the AC's "presentation only" rests on. Compared against a full aggregate
    /// snapshot rather than a spot check, because the ways a preview could accidentally mutate
    /// something — a Revision row, a review, a step, an attempt — are not ones a targeted
    /// assertion would think to look for.
    /// </remarks>
    [Fact]
    public async Task Loading_the_lists_pictures_changes_no_metadata_and_no_file()
    {
        using HomeScreenHarness harness = new();
        string source = harness.WriteSourceFile("untouched.png");
        harness.FilePicker.Path = source;
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);
        SessionId id = harness.Navigation.WorkflowSelectionFor!.Id;

        SessionAggregate before =
            (await harness.Inner.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        string managed = harness.Inner.FileWorkspace.ResolveAbsolute(before.Revisions.Single().File);
        byte[] managedBytes = File.ReadAllBytes(managed);
        byte[] sourceBytes = File.ReadAllBytes(source);

        await harness.Home.RefreshCommand.ExecuteAsync(null);
        await harness.Home.ThumbnailsLoaded;
        harness.Home.RecentSessions.Single().HasThumbnail.ShouldBeTrue();

        SessionAggregate after =
            (await harness.Inner.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        after.Session.ShouldBe(before.Session);
        after.Steps.ShouldBe(before.Steps);
        after.Revisions.ShouldBe(before.Revisions);
        after.Attempts.ShouldBe(before.Attempts);
        after.Reviews.ShouldBe(before.Reviews);
        after.Outputs.ShouldBe(before.Outputs);
        after.Snapshot.ShouldBe(before.Snapshot);

        File.ReadAllBytes(managed).ShouldBe(managedBytes);
        File.ReadAllBytes(source).ShouldBe(sourceBytes);
    }

    /// <summary>
    /// Home publishes its rows before it decodes anything, and decodes one row at a time.
    /// </summary>
    /// <remarks>
    /// The structural answer to "opening Home must not trigger a decode storm", and the reason
    /// no performance timing test is needed: with the first decode held open, the list is already
    /// complete and exactly one decode has been asked for. A screen that decoded on the way to
    /// building the list would fail the first assertion; one that fanned out over every row would
    /// fail the second.
    /// </remarks>
    [Fact]
    public async Task The_list_is_published_before_decoding_and_decodes_one_row_at_a_time()
    {
        using HomeScreenHarness harness = new();
        foreach (string name in new[] { "one.png", "two.png", "three.png" })
        {
            harness.FilePicker.Path = harness.WriteSourceFile(name);
            await harness.Home.ChooseFileCommand.ExecuteAsync(null);
        }

        GatedThumbnailService gate = new(harness.Previews);
        HomeViewModel home = new(
            harness.Sessions, gate, new RecordingNavigation(), new StubFilePicker(),
            new PrintFlow.App.Startup.StartupStatusAccessor());

        await home.RefreshCommand.ExecuteAsync(null);
        await gate.FirstCallStarted;

        home.RecentSessions.Count.ShouldBe(3, "the list is usable before any picture exists");
        home.RecentSessions.ShouldAllBe(row => !row.HasThumbnail);
        gate.Calls.ShouldBe(1, "three rows must not become three concurrent decodes");

        gate.Release();
        await home.ThumbnailsLoaded;

        home.RecentSessions.ShouldAllBe(row => row.HasThumbnail);
        gate.Calls.ShouldBe(3);
    }

    // -------------------------------------------------------------------------------------
    // Record management
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// A finished job's record leaves the list for good, and takes nothing with it.
    /// </summary>
    /// <remarks>
    /// The exact AC reading: "delete-record" removes the record from Recent Processing, and
    /// "older records may disappear from the list without deleting approved production files" is
    /// the safety rule it is bound by. So the assertions come in two halves — the entry is gone
    /// and stays gone across a restart, and every persisted row and every file is exactly as it
    /// was, including the approved production TIFF sitting in the Approved area.
    /// </remarks>
    [Fact]
    public async Task A_completed_record_leaves_the_list_durably_while_every_file_survives()
    {
        using HomeScreenHarness harness = TiffFinalReviewFixture.Harness(out _);

        TiffFinalReviewFixture.Review review =
            await TiffFinalReviewFixture.ReviewRequiredAsync(harness, "finished.png");
        await review.Screen.ApproveCommand.ExecuteAsync(null);
        await review.Screen.CompleteCommand.ExecuteAsync(null);
        review.Screen.Notice.ShouldBeNull();

        // A second job, untouched throughout, so "removed exactly one record" is a real claim.
        harness.FilePicker.Path = harness.WriteSourceFile("bystander.png");
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);
        SessionId bystander = harness.Navigation.WorkflowSelectionFor!.Id;

        SessionAggregate before = await review.ReloadAsync();
        before.Session.State.ShouldBe(SessionState.Completed);
        before.Outputs.Single().ReviewState.ShouldBe(ReviewState.Approved);

        Dictionary<string, byte[]> files = SnapshotFiles(harness, before);
        files.Count.ShouldBeGreaterThan(1);
        string customerSource = before.Snapshot!.OriginalSourcePath;
        byte[] customerBytes = File.ReadAllBytes(customerSource);

        await harness.Home.RefreshCommand.ExecuteAsync(null);
        RecentSessionRow row = harness.Home.RecentSessions.Single(candidate => candidate.Id == review.Id);
        row.CanRemoveRecord.ShouldBeTrue();
        row.CanAbandon.ShouldBeFalse("a finished job is removed from the list, never abandoned");

        await harness.Home.RemoveRecordCommand.ExecuteAsync(row);

        harness.Home.Notice.ShouldNotBeNullOrWhiteSpace();
        harness.Home.Notice!.ShouldContain(row.DisplayName);
        harness.Home.RecentSessions.Select(r => r.Id).ShouldBe([bystander]);

        // It survives a restart, because it is persisted rather than remembered.
        RecordingNavigation navigation = new();
        HomeViewModel restarted = harness.RestartHome(navigation);
        await restarted.RefreshCommand.ExecuteAsync(null);
        restarted.RecentSessions.Select(r => r.Id).ShouldBe([bystander]);

        // Nothing was deleted. The record is still complete, and so is every file it names.
        SessionAggregate after = await review.ReloadAsync();
        after.Session.State.ShouldBe(SessionState.Completed);
        after.Steps.ShouldBe(before.Steps);
        after.Revisions.ShouldBe(before.Revisions);
        after.Attempts.ShouldBe(before.Attempts);
        after.Reviews.ShouldBe(before.Reviews);
        after.Outputs.ShouldBe(before.Outputs);
        after.Snapshot.ShouldBe(before.Snapshot);

        foreach ((string path, byte[] bytes) in files)
        {
            File.Exists(path).ShouldBeTrue($"{path} was removed with the record");
            File.ReadAllBytes(path).ShouldBe(bytes, $"{path} was rewritten");
        }

        File.ReadAllBytes(customerSource).ShouldBe(customerBytes, "the customer's own file is never touched");

        // And the untouched job is exactly as it was.
        (await harness.Inner.Repository.LoadAsync(bystander, CancellationToken.None)).Value.ShouldNotBeNull();
    }

    /// <summary>
    /// An abandoned job can be taken off the list too, and its record and files remain.
    /// </summary>
    /// <remarks>
    /// The abandoned case has to be tested separately from the completed one because the two
    /// arrive by opposite routes — one is finished work, the other is work given up on — and the
    /// AC's safety rule is about states, not about how they were reached. The interrupted attempt
    /// left behind by the synthetic crash is deliberately part of the fixture: an interruption
    /// the operator has already resolved by abandoning must not strand the card on Home forever.
    /// </remarks>
    [Fact]
    public async Task An_abandoned_record_with_a_resolved_interruption_can_be_taken_off_the_list()
    {
        using HomeScreenHarness harness = new();
        SessionId id = await RecoverySurfaceTests.Seed(harness.Inner, "crashed");

        await harness.Sessions.ExecuteAsync(
            id, new WorkflowCommand.AbandonSession("test"), "tester", CancellationToken.None);

        SessionAggregate before =
            (await harness.Inner.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        before.Session.State.ShouldBe(SessionState.Abandoned);
        before.Attempts.ShouldContain(attempt => attempt.Status == AttemptStatus.Interrupted);

        // It is no longer recoverable, which is what makes removing its card safe.
        (await harness.Sessions.ListRecoveryAsync(CancellationToken.None)).Value.ShouldBeEmpty();

        await harness.Home.RefreshCommand.ExecuteAsync(null);
        RecentSessionRow row = harness.Home.RecentSessions.Single();
        row.CanRemoveRecord.ShouldBeTrue();

        await harness.Home.RemoveRecordCommand.ExecuteAsync(row);
        harness.Home.RecentSessions.ShouldBeEmpty();

        SessionAggregate after = (await harness.Inner.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        after.Revisions.ShouldBe(before.Revisions);
        after.Attempts.ShouldBe(before.Attempts);
        after.Snapshot.ShouldBe(before.Snapshot);
        foreach (Revision revision in after.Revisions)
        {
            File.Exists(harness.Inner.FileWorkspace.ResolveAbsolute(revision.File)).ShouldBeTrue();
        }
    }

    /// <summary>
    /// Unfinished and recoverable work cannot be taken off the list, from either direction.
    /// </summary>
    /// <remarks>
    /// The screen does not offer the action, and the service refuses it when asked anyway — the
    /// same division Abandon already keeps, and the reason Recent Processing cannot become a way
    /// to hide a failure that startup recovery still has to resolve. A recoverable session is
    /// tested through the service directly because Home does not even list it as a Recent row:
    /// it appears as a recovery entry instead, and the two must never contradict each other.
    /// </remarks>
    [Fact]
    public async Task Active_handed_off_and_recoverable_records_are_refused()
    {
        using HomeScreenHarness harness = new();

        harness.FilePicker.Path = harness.WriteSourceFile("in-progress.png");
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);
        SessionId active = harness.Navigation.WorkflowSelectionFor!.Id;

        await harness.Home.RefreshCommand.ExecuteAsync(null);
        RecentSessionRow row = harness.Home.RecentSessions.Single();
        row.CanRemoveRecord.ShouldBeFalse();
        row.CanAbandon.ShouldBeTrue("unfinished work is abandoned, not removed");

        // Asked anyway — a stale row, or a driver — and refused by the one authority.
        OperationResult<PrintFlow.Domain.Results.Unit> refused =
            await harness.Sessions.RemoveFromRecentAsync(active, CancellationToken.None);
        refused.IsFailure.ShouldBeTrue();
        refused.Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);
        harness.Home.RecentSessions.Single().Id.ShouldBe(active);

        // A handed-off job is still the operator's to finish by hand.
        await harness.Sessions.ExecuteAsync(
            active, new WorkflowCommand.HandOff(StepKind.OriginalConfirmation, "manual"), "tester", CancellationToken.None);
        (await harness.Sessions.RemoveFromRecentAsync(active, CancellationToken.None))
            .IsFailure.ShouldBeTrue();

        // And an unresolved interruption is untouchable while it is still recoverable.
        SessionId interrupted = await RecoverySurfaceTests.Seed(harness.Inner, "unresolved");
        (await harness.Sessions.ListRecoveryAsync(CancellationToken.None))
            .Value.Select(entry => entry.Id).ShouldContain(interrupted);
        (await harness.Sessions.RemoveFromRecentAsync(interrupted, CancellationToken.None))
            .IsFailure.ShouldBeTrue();

        await harness.Home.RefreshCommand.ExecuteAsync(null);
        harness.Home.RecoverySessions.Select(entry => entry.Id).ShouldContain(interrupted);
        harness.Home.RecentSessions.ShouldNotContain(candidate => candidate.Id == interrupted);
    }

    /// <summary>
    /// Removing the same record twice is refused rather than silently reported as done.
    /// </summary>
    [Fact]
    public async Task A_record_that_has_already_left_the_list_is_refused_a_second_time()
    {
        using HomeScreenHarness harness = new();
        harness.FilePicker.Path = harness.WriteSourceFile("once.png");
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);
        SessionId id = harness.Navigation.WorkflowSelectionFor!.Id;

        await harness.Sessions.ExecuteAsync(
            id, new WorkflowCommand.AbandonSession("test"), "tester", CancellationToken.None);

        (await harness.Sessions.RemoveFromRecentAsync(id, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await harness.Sessions.RemoveFromRecentAsync(id, CancellationToken.None)).IsFailure.ShouldBeTrue();
    }

    // -------------------------------------------------------------------------------------

    private static Dictionary<string, byte[]> SnapshotFiles(
        HomeScreenHarness harness, SessionAggregate aggregate)
    {
        Dictionary<string, byte[]> files = [];

        foreach (Revision revision in aggregate.Revisions)
        {
            Record(revision.File);
        }

        foreach (PrintOutput output in aggregate.Outputs)
        {
            Record(output.File);
        }

        return files;

        void Record(WorkspaceFileRef file)
        {
            string path = harness.Inner.FileWorkspace.ResolveAbsolute(file);
            if (File.Exists(path)) files[path] = File.ReadAllBytes(path);
        }
    }

    /// <summary>
    /// Holds the first thumbnail request open so "what does Home look like meanwhile" is a
    /// question with a deterministic answer.
    /// </summary>
    /// <remarks>
    /// Delegates rather than stubs, so what eventually arrives is a real decoded picture and the
    /// test is about <i>when</i> decoding happens rather than about what a double returns — the
    /// same shape as the review surface's own gated double.
    /// </remarks>
    private sealed class GatedThumbnailService : IArtefactPreviewService
    {
        private readonly IArtefactPreviewService _inner;
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public GatedThumbnailService(IArtefactPreviewService inner) => _inner = inner;

        public Task FirstCallStarted => _started.Task;

        public int Calls => Volatile.Read(ref _calls);

        public void Release() => _gate.TrySetResult();

        public Task<OperationResult<ImagePreview>> GetPreviewAsync(
            SessionId sessionId, RevisionId revisionId, CancellationToken cancellationToken) =>
            _inner.GetPreviewAsync(sessionId, revisionId, cancellationToken);

        public async Task<OperationResult<ImagePreview>> GetRecentThumbnailAsync(
            SessionId sessionId, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                _started.TrySetResult();
                await _gate.Task;
            }

            return await _inner.GetRecentThumbnailAsync(sessionId, cancellationToken);
        }
    }
}
