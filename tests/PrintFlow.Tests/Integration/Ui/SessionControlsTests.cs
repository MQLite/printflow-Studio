using System.IO;
using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Reviews;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

/// <summary>
/// The session screen's processing and review controls, driven against the real session
/// service, workspace, fake adapters and SQLite database (Epic 11100 Part 3C3A §19).
/// </summary>
/// <remarks>
/// Every assertion about an outcome reads what was actually <b>persisted</b>, through the
/// repository rather than through the view model's own properties. A screen that says
/// "Approved" while the database says otherwise is exactly the defect these tests exist to
/// catch.
/// <para>
/// Deliberately a short list. The engine already has an exhaustive transition matrix; what is
/// new in this slice is that the buttons reach it correctly, so these cover the paths the slice
/// introduces and stop. No combinatorial sweep over view-model states is attempted.
/// </para>
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class SessionControlsTests
{
    // -------------------------------------------------------------------------------------
    // §6: Confirm Original
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task Confirming_the_original_persists_an_approved_step_and_advances_to_enhancement()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await OpenAsync(harness, "confirm.png");

        open.Screen.CanConfirmOriginal.ShouldBeTrue();

        await open.Screen.ConfirmOriginalCommand.ExecuteAsync(null);

        open.Screen.Notice.ShouldBeNull();

        SessionAggregate persisted = await open.ReloadAsync();
        StepOf(persisted, StepKind.OriginalConfirmation).State.ShouldBe(StepState.Approved);
        persisted.ToSnapshot().CurrentStep!.Step.ShouldBe(StepKind.Enhancement);

        // Confirmation produces no Revision: only the imported original exists.
        persisted.Revisions.Count.ShouldBe(1);
    }

    // -------------------------------------------------------------------------------------
    // §7: Run Step
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task Running_a_waiting_step_goes_through_the_real_fake_adapter_pipeline_to_review_required()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await ConfirmedAsync(harness, "run.png");

        open.Screen.CanRunStep.ShouldBeTrue();

        await open.Screen.RunStepCommand.ExecuteAsync(null);

        open.Screen.Notice.ShouldBeNull();

        SessionAggregate persisted = await open.ReloadAsync();
        StepOf(persisted, StepKind.Enhancement).State.ShouldBe(StepState.ReviewRequired);

        // A real attempt, a real Revision, a real file, and a hash that could only come from
        // hashing bytes that were actually written and read back.
        ProcessingAttempt attempt = persisted.Attempts.Single(a => a.Step == StepKind.Enhancement);
        attempt.Status.ShouldBe(AttemptStatus.Succeeded);

        Revision produced = persisted.Revisions.Single(r => r.Id == attempt.OutputRevisionId);
        produced.Sha256.Value.Length.ShouldBe(Sha256.HexLength);
        File.Exists(harness.ResolveInWorkspace(produced.File.RelativePath)).ShouldBeTrue();
    }

    [Fact]
    public async Task The_screen_shows_the_produced_file_metadata_without_any_source_path()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await InReviewAsync(harness, "metadata.png");
        SessionViewModel screen = open.Screen;

        screen.HasArtefact.ShouldBeTrue();
        screen.ArtefactIsInput.ShouldBeFalse();
        screen.ArtefactFormat.ShouldBe("PNG");
        screen.ArtefactPixels.ShouldNotBeNullOrWhiteSpace();
        screen.ArtefactHash.Length.ShouldBe(12);
        screen.ArtefactRevision.ShouldNotBeNullOrWhiteSpace();

        // A bare file name: no drive letter, no separator, nothing that could disclose where
        // the operator's original lives or how the workspace is laid out (§3).
        screen.ArtefactFileName.ShouldNotBeNullOrWhiteSpace();
        screen.ArtefactFileName.ShouldNotContain(Path.DirectorySeparatorChar.ToString());
        screen.ArtefactFileName.ShouldNotContain("/");
        screen.ArtefactFileName.ShouldNotContain(":");
    }

    // -------------------------------------------------------------------------------------
    // §8: fake mode
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task A_session_processed_by_fake_adapters_says_so_on_screen()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await OpenAsync(harness, "fake.png");

        open.Screen.IsFakeProcessing.ShouldBeTrue();
        open.Screen.FakeModeNotice.ShouldNotBeNullOrWhiteSpace();
    }

    // -------------------------------------------------------------------------------------
    // §10: Approve
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task Approving_sends_the_hash_of_the_revision_on_screen_and_persists_the_decision()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await InReviewAsync(harness, "approve.png");

        string displayedHashPrefix = open.Screen.ArtefactHash;
        open.Screen.CanApprove.ShouldBeTrue();

        await open.Screen.ApproveCommand.ExecuteAsync(null);

        open.Screen.Notice.ShouldBeNull();

        SessionAggregate persisted = await open.ReloadAsync();
        SessionStep enhancement = StepOf(persisted, StepKind.Enhancement);
        enhancement.State.ShouldBe(StepState.Approved);

        ReviewDecision decision = persisted.Reviews.Single(r => r.Step == StepKind.Enhancement);
        decision.IsApproved.ShouldBeTrue();

        // The decision binds to the exact bytes the screen described, not to some other
        // Revision of the same step (MVP design invariants 2 and 3).
        decision.ReviewedSha256.ShortForm.ShouldBe(displayedHashPrefix);
        decision.ReviewedSha256.ShouldBe(enhancement.CurrentRevisionSha256!.Value);
    }

    /// <summary>
    /// A file mutated after it was displayed cannot inherit the approval it was about
    /// (Part 3C3A §10).
    /// </summary>
    /// <remarks>
    /// This is the test that makes hash binding worth having. The screen holds a hash it read
    /// moments ago; the bytes on disk no longer match it; and the answer must be a refusal with
    /// no progression, not a silently re-computed approval of whatever is there now.
    /// </remarks>
    [Fact]
    public async Task Approving_a_file_that_changed_after_it_was_displayed_is_refused_and_does_not_advance()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await InReviewAsync(harness, "stale.png");

        SessionAggregate before = await open.ReloadAsync();
        Revision underReview =
            before.Revisions.Single(r => r.Id == StepOf(before, StepKind.Enhancement).CurrentRevisionId);

        // Someone edited the result behind PrintFlow's back, between display and decision.
        await File.WriteAllBytesAsync(
            harness.ResolveInWorkspace(underReview.File.RelativePath),
            SyntheticImages.Png(9, 7, alpha: true));

        await open.Screen.ApproveCommand.ExecuteAsync(null);

        open.Screen.Notice.ShouldNotBeNullOrWhiteSpace();
        open.Screen.Notice!.ShouldContain(nameof(FailureCode.RevisionIntegrityMismatch));

        SessionAggregate after = await open.ReloadAsync();
        StepOf(after, StepKind.Enhancement).State.ShouldBe(StepState.ReviewRequired);
        after.Reviews.ShouldBeEmpty();
        after.Revisions.Single(r => r.Id == underReview.Id).IsValid.ShouldBeFalse();
    }

    // -------------------------------------------------------------------------------------
    // §11: Reject
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task Rejecting_records_the_reason_and_notes_and_leaves_the_step_needing_a_retry()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await InReviewAsync(harness, "reject.png");
        SessionViewModel screen = open.Screen;

        // All seven MVP quick reasons are offered, as stable internal values behind labels.
        screen.RejectionReasons.Select(choice => choice.Reason)
            .ShouldBe(Enum.GetValues<RejectionReason>());
        screen.RejectionReasons.ShouldAllBe(choice => !string.IsNullOrWhiteSpace(choice.Label));

        screen.SelectedRejectionReason =
            screen.RejectionReasons.Single(choice => choice.Reason == RejectionReason.EdgeError);
        screen.RejectionNotes = "  halo around the sleeve  ";

        screen.CanReject.ShouldBeTrue();
        await screen.RejectCommand.ExecuteAsync(null);

        screen.Notice.ShouldBeNull();

        SessionAggregate persisted = await open.ReloadAsync();
        StepOf(persisted, StepKind.Enhancement).State.ShouldBe(StepState.RetryRequired);

        ReviewDecision decision = persisted.Reviews.Single(r => r.Step == StepKind.Enhancement);
        decision.IsApproved.ShouldBeFalse();
        decision.QuickReason.ShouldBe(RejectionReason.EdgeError);
        decision.Notes.ShouldBe("halo around the sleeve");

        // The audit trail survives the rejection. The judged Revision is still on record and
        // still valid in itself — only descendants of a rejected result are invalidated
        // (plan §10.4), and this one has none — but the step no longer offers it downstream.
        persisted.Revisions.Single(r => r.Id.Value == decision.SubjectId).IsValid.ShouldBeTrue();
        StepOf(persisted, StepKind.Enhancement).CurrentRevisionId.ShouldBeNull();
    }

    // -------------------------------------------------------------------------------------
    // §12: Retry
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task Retry_returns_the_step_to_waiting_and_a_new_run_produces_a_fresh_attempt()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await InReviewAsync(harness, "retry.png");

        await open.Screen.RejectCommand.ExecuteAsync(null);
        open.Screen.CanRetry.ShouldBeTrue();

        await open.Screen.RetryCommand.ExecuteAsync(null);

        // Retry stops at Waiting. The screen never folds Retry and Run into one hidden
        // operation, so the state progression stays visible to the operator (§12).
        SessionAggregate afterRetry = await open.ReloadAsync();
        StepOf(afterRetry, StepKind.Enhancement).State.ShouldBe(StepState.Waiting);
        open.Screen.CanRunStep.ShouldBeTrue();

        AttemptId first = afterRetry.Attempts.Single(a => a.Step == StepKind.Enhancement).Id;

        await open.Screen.RunStepCommand.ExecuteAsync(null);
        open.Screen.Notice.ShouldBeNull();

        SessionAggregate afterRerun = await open.ReloadAsync();
        List<ProcessingAttempt> attempts =
            [.. afterRerun.Attempts.Where(a => a.Step == StepKind.Enhancement)];

        attempts.Count.ShouldBe(2);
        ProcessingAttempt second = attempts.Single(a => a.Id != first);

        // A fresh attempt id, and therefore a fresh Working directory: the new run never
        // reuses the rejected attempt's working copy (MVP design invariant 8).
        Revision produced = afterRerun.Revisions.Single(r => r.Id == second.OutputRevisionId);
        produced.File.RelativePath.ShouldContain(second.Id.Value.ToString("D"));
        produced.File.RelativePath.ShouldNotContain(first.Value.ToString("D"));
    }

    // -------------------------------------------------------------------------------------
    // §13: Skip
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task Skipping_a_skippable_step_records_it_without_creating_a_revision()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await ConfirmedAsync(harness, "skip.png");

        open.Screen.CanSkip.ShouldBeTrue();
        int revisionsBefore = (await open.ReloadAsync()).Revisions.Count;

        await open.Screen.SkipCommand.ExecuteAsync(null);

        open.Screen.Notice.ShouldBeNull();

        SessionAggregate persisted = await open.ReloadAsync();
        SessionStep skipped = StepOf(persisted, StepKind.Enhancement);
        skipped.State.ShouldBe(StepState.Skipped);
        skipped.CurrentRevisionId.ShouldBeNull();
        skipped.SkipReason.ShouldNotBeNullOrWhiteSpace();

        persisted.Revisions.Count.ShouldBe(revisionsBefore);
    }

    /// <summary>
    /// Trim is not skippable, so the screen must not offer Skip once the session reaches it
    /// (Part 3C3A §13).
    /// </summary>
    [Fact]
    public async Task Skip_is_not_offered_for_trim()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await ConfirmedAsync(harness, "trim.png");

        await open.Screen.SkipCommand.ExecuteAsync(null);   // Enhancement
        await open.Screen.SkipCommand.ExecuteAsync(null);   // BackgroundRemoval
        open.Screen.Notice.ShouldBeNull();

        SessionAggregate persisted = await open.ReloadAsync();
        persisted.ToSnapshot().CurrentStep!.Step.ShouldBe(StepKind.Trim);

        open.Screen.CanSkip.ShouldBeFalse();
        open.Screen.CanRunStep.ShouldBeTrue();

        // Neither skipped step produced a Revision, so Trim's input falls through to the
        // imported original — the only Revision that exists.
        persisted.Revisions.Count.ShouldBe(1);
    }

    // -------------------------------------------------------------------------------------
    // §14: Manual hand-off
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task Handing_off_ends_automated_processing_and_releases_the_automation_lock()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await InReviewAsync(harness, "handoff.png");
        SessionViewModel screen = open.Screen;

        screen.CanHandOff.ShouldBeTrue();

        await screen.HandOffCommand.ExecuteAsync(null);

        screen.Notice.ShouldBeNull();
        screen.IsHandedOff.ShouldBeTrue();
        screen.IsReadOnly.ShouldBeTrue();
        screen.HandedOffNotice.ShouldNotBeNullOrWhiteSpace();

        SessionAggregate persisted = await open.ReloadAsync();
        persisted.Session.State.ShouldBe(SessionState.HandedOff);

        // No run action survives: automated progression is over for this session.
        screen.CanRunStep.ShouldBeFalse();
        screen.CanApprove.ShouldBeFalse();
        screen.CanReject.ShouldBeFalse();
        screen.CanRetry.ShouldBeFalse();
        screen.CanSkip.ShouldBeFalse();

        AutomationLockState automationLock = (await harness.Inner.Repository
            .GetAutomationLockAsync(CancellationToken.None)).Value;
        automationLock.IsHeld.ShouldBeFalse();
    }

    // -------------------------------------------------------------------------------------
    // §4: availability comes from the workflow layer
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Every action the screen offers agrees with <c>AvailableCommands</c>, in each state this
    /// slice actually puts the operator in (Part 3C3A §4, §19).
    /// </summary>
    /// <remarks>
    /// The comparison is against the engine's answer for the <i>persisted</i> snapshot,
    /// computed independently of the view model. If the screen ever grew its own copy of a
    /// legality rule, the two would disagree here.
    /// </remarks>
    [Fact]
    public async Task Button_availability_agrees_with_the_engine_in_every_state_this_slice_reaches()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await OpenAsync(harness, "availability.png");

        await AssertAgreesAsync(open);                                  // OriginalConfirmation

        await open.Screen.ConfirmOriginalCommand.ExecuteAsync(null);
        await AssertAgreesAsync(open);                                  // Enhancement / Waiting

        await open.Screen.RunStepCommand.ExecuteAsync(null);
        await AssertAgreesAsync(open);                                  // Enhancement / ReviewRequired

        await open.Screen.RejectCommand.ExecuteAsync(null);
        await AssertAgreesAsync(open);                                  // Enhancement / RetryRequired

        await open.Screen.RetryCommand.ExecuteAsync(null);
        await AssertAgreesAsync(open);                                  // Enhancement / Waiting again

        await open.Screen.HandOffCommand.ExecuteAsync(null);
        await AssertAgreesAsync(open);                                  // Session / HandedOff
    }

    // -------------------------------------------------------------------------------------
    // §16: navigation
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task Going_back_to_home_changes_nothing_and_home_shows_the_persisted_state()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await InReviewAsync(harness, "back.png");

        SessionAggregate before = await open.ReloadAsync();

        RecordingNavigation navigation = new();
        SessionViewModel leaving = harness.Session(navigation);
        leaving.Open((await harness.Sessions.LoadAsync(open.Id, CancellationToken.None)).Value);
        await leaving.BackToHomeCommand.ExecuteAsync(null);

        navigation.GoHomeCount.ShouldBe(1);

        SessionAggregate after = await open.ReloadAsync();
        after.ToSnapshot().ShouldBe(before.ToSnapshot());

        await harness.Home.RefreshCommand.ExecuteAsync(null);
        RecentSessionRow row = harness.Home.RecentSessions.Single(r => r.Id == open.Id);
        row.CanContinueProcessing.ShouldBeTrue();
        row.CurrentStep.ShouldNotBeNullOrWhiteSpace();

        // Resuming reopens what SQLite holds, mid-review, down to the hash the screen had been
        // showing — not anything the departing screen remembered.
        await harness.Home.ResumeCommand.ExecuteAsync(row);
        SessionView resumed = harness.Navigation.SessionFor!;
        resumed.CurrentStep!.Step.ShouldBe(StepKind.Enhancement);
        resumed.CurrentStep.State.ShouldBe(StepState.ReviewRequired);
        resumed.CurrentArtefact!.Sha256.ShortForm.ShouldBe(open.Screen.ArtefactHash);
    }

    // -------------------------------------------------------------------------------------

    /// <summary>A session screen, plus the identity needed to read the same session back.</summary>
    private sealed record OpenSession(HomeScreenHarness Harness, SessionViewModel Screen, SessionId Id)
    {
        /// <summary>The session exactly as persistence currently holds it.</summary>
        public async Task<SessionAggregate> ReloadAsync() =>
            (await Harness.Inner.Repository.LoadAsync(Id, CancellationToken.None)).Value!;
    }

    /// <summary>Imports a synthetic file and opens the session screen on it.</summary>
    private static async Task<OpenSession> OpenAsync(HomeScreenHarness harness, string fileName)
    {
        harness.FilePicker.Path = harness.WriteSourceFile(fileName);
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);

        SessionView imported = harness.Navigation.WorkflowSelectionFor!;
        SessionViewModel screen = harness.Session(new RecordingNavigation());
        screen.Open(imported);

        return new OpenSession(harness, screen, imported.Id);
    }

    /// <summary>...and confirms the original, leaving the session on Enhancement/Waiting.</summary>
    private static async Task<OpenSession> ConfirmedAsync(HomeScreenHarness harness, string fileName)
    {
        OpenSession open = await OpenAsync(harness, fileName);
        await open.Screen.ConfirmOriginalCommand.ExecuteAsync(null);
        open.Screen.Notice.ShouldBeNull();
        return open;
    }

    /// <summary>...and runs Enhancement, leaving the session on Enhancement/ReviewRequired.</summary>
    private static async Task<OpenSession> InReviewAsync(HomeScreenHarness harness, string fileName)
    {
        OpenSession open = await ConfirmedAsync(harness, fileName);
        await open.Screen.RunStepCommand.ExecuteAsync(null);
        open.Screen.Notice.ShouldBeNull();
        return open;
    }

    private static SessionStep StepOf(SessionAggregate aggregate, StepKind kind) =>
        aggregate.Steps.Single(step => step.Step == kind);

    private static async Task AssertAgreesAsync(OpenSession open)
    {
        SessionAggregate persisted = await open.ReloadAsync();
        IReadOnlyList<CommandKind> legal =
            WorkflowEngine.Instance.AvailableCommands(persisted.ToSnapshot());

        SessionViewModel screen = open.Screen;
        screen.CanConfirmOriginal.ShouldBe(legal.Contains(CommandKind.ConfirmOriginal));
        screen.CanRunStep.ShouldBe(legal.Contains(CommandKind.StartStep));
        screen.CanApprove.ShouldBe(legal.Contains(CommandKind.Approve));
        screen.CanReject.ShouldBe(legal.Contains(CommandKind.Reject));
        screen.CanRetry.ShouldBe(legal.Contains(CommandKind.Retry));
        screen.CanSkip.ShouldBe(legal.Contains(CommandKind.Skip));
        screen.CanHandOff.ShouldBe(legal.Contains(CommandKind.HandOff));
    }
}
