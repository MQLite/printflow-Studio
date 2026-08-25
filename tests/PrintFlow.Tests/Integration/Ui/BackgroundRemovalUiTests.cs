using System.Globalization;
using System.IO;
using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Adapters.Meitu;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

/// <summary>
/// The Background Removal operator surface, driven end to end against the real session
/// service, workspace, fake Meitu adapter and SQLite database (Epic 11300 Part C2B2 §27).
/// </summary>
/// <remarks>
/// The screen's job in this slice is small and exact: let an operator say "use Meitu's
/// automatic selection for <i>this</i> image", bound to the Revision and hash actually on
/// screen, and never let that sentence turn into "automatic background removal is on for this
/// session". Every test below is one way that could go wrong.
/// <para>
/// Outcomes are read from persistence wherever a claim is about what happened, not from the
/// view model's own properties: a screen that says "authorised" while the database holds no
/// authority — or holds one over different content — is the defect this suite exists to catch.
/// </para>
/// <para>
/// Fake adapters throughout. No production Meitu is registered, launched or driven anywhere in
/// this file (§25).
/// </para>
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class BackgroundRemovalUiTests
{
    // -------------------------------------------------------------------------------------
    // §7, §8, §10: nothing is authorised until the operator says so
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Arriving at Background Removal offers the authorisation and nothing else (§7, §8, §10).
    /// </summary>
    /// <remarks>
    /// The three halves of "no silent authorisation" this state can show: the session holds no
    /// authority, the read model reports the step as not runnable, and the screen's own Run
    /// control is absent — while the control that <i>would</i> authorise is offered, because the
    /// engine says the command would be accepted.
    /// </remarks>
    [Fact]
    public async Task Arriving_at_background_removal_authorises_nothing_and_offers_no_run()
    {
        using HomeScreenHarness harness = new();
        AtBackgroundRemoval open = await AtBackgroundRemovalAsync(harness, "br-arrive.png");
        SessionViewModel screen = open.Screen;

        screen.CanAuthoriseAutomaticSelection.ShouldBeTrue();
        screen.CanBeginAutomaticSelection.ShouldBeTrue();
        screen.IsAutomaticSelectionPending.ShouldBeTrue();
        screen.IsAutomaticSelectionAuthorised.ShouldBeFalse();
        screen.AutomaticSelectionAuthorisedNotice.ShouldBeEmpty();

        screen.CanRunBackgroundRemoval.ShouldBeFalse();
        screen.CanRunStep.ShouldBeFalse();

        // Merely opening the confirmation still records nothing.
        screen.BeginAutomaticSelectionCommand.Execute(null);
        screen.IsConfirmingAutomaticSelection.ShouldBeTrue();

        SessionAggregate persisted = await open.ReloadAsync();
        persisted.Session.BackgroundRemovalAuthority.ShouldBeNull();
        persisted.Attempts.ShouldNotContain(a => a.Step == StepKind.BackgroundRemoval);
        harness.Meitu.CallCount.ShouldBe(1, "only the Enhancement run reached the adapter.");
    }

    /// <summary>
    /// Cancelling the confirmation leaves nothing behind (§4, §10).
    /// </summary>
    /// <remarks>
    /// The structural half of "this is not a checkbox": Cancel reaches no service, so there is
    /// nothing for it to leave a trace with, and the screen returns to where it was.
    /// </remarks>
    [Fact]
    public async Task Cancelling_the_confirmation_records_no_authority()
    {
        using HomeScreenHarness harness = new();
        AtBackgroundRemoval open = await AtBackgroundRemovalAsync(harness, "br-cancel.png");
        SessionViewModel screen = open.Screen;

        screen.BeginAutomaticSelectionCommand.Execute(null);
        screen.CancelAutomaticSelectionCommand.Execute(null);

        screen.IsConfirmingAutomaticSelection.ShouldBeFalse();
        screen.IsAutomaticSelectionPending.ShouldBeTrue();
        screen.CanRunBackgroundRemoval.ShouldBeFalse();
        (await open.ReloadAsync()).Session.BackgroundRemovalAuthority.ShouldBeNull();
    }

    /// <summary>
    /// Run Step never authorises on the operator's behalf (§10).
    /// </summary>
    /// <remarks>
    /// The button is not offered in this state, but pressing the command anyway is the honest
    /// test: a screen that quietly issued the decision to make its own Run work would satisfy
    /// every visible assertion and defeat the entire point of the slice.
    /// </remarks>
    [Fact]
    public async Task Running_the_step_without_authority_authorises_nothing_and_starts_nothing()
    {
        using HomeScreenHarness harness = new();
        AtBackgroundRemoval open = await AtBackgroundRemovalAsync(harness, "br-run-first.png");
        SessionViewModel screen = open.Screen;

        int callsBefore = harness.Meitu.CallCount;
        await screen.RunStepCommand.ExecuteAsync(null);

        SessionAggregate persisted = await open.ReloadAsync();
        persisted.Session.BackgroundRemovalAuthority.ShouldBeNull();
        persisted.Attempts.ShouldNotContain(a => a.Step == StepKind.BackgroundRemoval);
        harness.Meitu.CallCount.ShouldBe(callsBefore);
        persisted.ToSnapshot().CurrentStep!.Step.ShouldBe(StepKind.BackgroundRemoval);
    }

    // -------------------------------------------------------------------------------------
    // §1, §5, §8: the authorisation binds to the displayed artefact and unlocks the run
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Authorising records an authority over the exact artefact on screen, and the run becomes
    /// available (§1, §5, §8).
    /// </summary>
    /// <remarks>
    /// Both halves of the binding are asserted against what the screen displayed rather than
    /// against whatever the session happens to hold: the Revision the metadata panel named, and
    /// the hash behind it. That is the difference between "the operator authorised this image"
    /// and "an authority exists".
    /// </remarks>
    [Fact]
    public async Task Authorising_binds_to_the_displayed_revision_and_hash_and_makes_the_run_available()
    {
        using HomeScreenHarness harness = new();
        AtBackgroundRemoval open = await AtBackgroundRemovalAsync(harness, "br-authorise.png");
        SessionViewModel screen = open.Screen;

        ArtefactView displayed = open.Displayed;
        screen.ArtefactRevision.ShouldBe(ShortRevision(displayed.RevisionId));
        screen.ArtefactHash.ShouldBe(displayed.Sha256.ShortForm);

        screen.BeginAutomaticSelectionCommand.Execute(null);
        await screen.ConfirmAutomaticSelectionCommand.ExecuteAsync(null);

        screen.Notice.ShouldBeNull();
        screen.IsAutomaticSelectionAuthorised.ShouldBeTrue();
        screen.IsAutomaticSelectionPending.ShouldBeFalse();
        screen.AutomaticSelectionAuthorisedNotice.ShouldContain(ShortRevision(displayed.RevisionId));

        // The confirmation is put away by the refresh, so it cannot be confirmed twice.
        screen.IsConfirmingAutomaticSelection.ShouldBeFalse();

        // Readiness comes from the read model, and the ordinary Run control agrees with it.
        screen.CanRunBackgroundRemoval.ShouldBeTrue();
        screen.CanRunStep.ShouldBeTrue();

        SessionAggregate persisted = await open.ReloadAsync();
        BackgroundRemovalAuthority stored = persisted.Session.BackgroundRemovalAuthority.ShouldNotBeNull();
        stored.Decision.ShouldBe(BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent);
        stored.ReviewedRevisionId.ShouldBe(displayed.RevisionId);
        stored.ReviewedSha256.ShouldBe(displayed.Sha256);

        // Still no attempt and no adapter call: a decision starts nothing.
        persisted.Attempts.ShouldNotContain(a => a.Step == StepKind.BackgroundRemoval);
    }

    // -------------------------------------------------------------------------------------
    // §6, §19: a stale screen cannot authorise what replaced what it shows
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// A screen still showing Revision A cannot authorise Revision B (§6, §19).
    /// </summary>
    /// <remarks>
    /// The race made concrete: the session moves on behind the screen's back — the upstream is
    /// returned to and re-run, so Background Removal will now consume a different file — and the
    /// operator, looking at the old one, clicks authorise. The command carries A, and A is
    /// refused. Nothing is retried against B, because the operator has not seen B.
    /// <para>
    /// The refusal is then <i>followed</i> by the screen refreshing to B, which is the other half
    /// of §19: the operator is shown what is really there rather than left looking at a Revision
    /// that no longer matters.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_stale_screen_cannot_authorise_the_artefact_that_replaced_the_one_it_shows()
    {
        using HomeScreenHarness harness = new();
        AtBackgroundRemoval open = await AtBackgroundRemovalAsync(harness, "br-stale.png");
        SessionViewModel screen = open.Screen;

        RevisionId displayedA = open.Displayed.RevisionId;
        screen.BeginAutomaticSelectionCommand.Execute(null);

        // Someone — another window, a resumed session — moves the workflow on underneath.
        await ReplaceUpstreamAsync(harness, open.Id);

        RevisionId currentB = (await CurrentArtefactAsync(harness, open.Id)).RevisionId;
        currentB.ShouldNotBe(displayedA);

        await screen.ConfirmAutomaticSelectionCommand.ExecuteAsync(null);

        screen.Notice.ShouldNotBeNullOrWhiteSpace();
        screen.Notice!.ShouldContain(nameof(FailureCode.PreconditionNotMet));

        SessionAggregate persisted = await open.ReloadAsync();
        persisted.Session.BackgroundRemovalAuthority.ShouldBeNull();
        persisted.Attempts.ShouldNotContain(a => a.Step == StepKind.BackgroundRemoval);

        // §19: the screen now shows B, unauthorised, and B was never authorised by A's click.
        screen.ArtefactRevision.ShouldBe(ShortRevision(currentB));
        screen.IsAutomaticSelectionAuthorised.ShouldBeFalse();
        screen.CanRunBackgroundRemoval.ShouldBeFalse();
    }

    // -------------------------------------------------------------------------------------
    // §20: content mutated after it was displayed
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Authorising a file that changed after it was displayed is refused (§20).
    /// </summary>
    /// <remarks>
    /// The same integrity machinery an Approve goes through, applied to the same kind of
    /// decision: the reviewed bytes are re-hashed before the command reaches the engine, the
    /// mismatch invalidates the Revision, and no authority is recorded. Nothing in the shell
    /// computes a hash — the identity it sent is the one the read model gave it.
    /// </remarks>
    [Fact]
    public async Task Authorising_content_that_changed_after_it_was_displayed_is_refused()
    {
        using HomeScreenHarness harness = new();
        AtBackgroundRemoval open = await AtBackgroundRemovalAsync(harness, "br-mutated.png");
        SessionViewModel screen = open.Screen;

        SessionAggregate before = await open.ReloadAsync();
        Revision reviewed = before.Revisions.Single(r => r.Id == open.Displayed.RevisionId);

        // Someone edited the file behind PrintFlow's back, between display and decision.
        await File.WriteAllBytesAsync(
            harness.ResolveInWorkspace(reviewed.File.RelativePath),
            SyntheticImages.Png(9, 7, alpha: true));

        screen.BeginAutomaticSelectionCommand.Execute(null);
        await screen.ConfirmAutomaticSelectionCommand.ExecuteAsync(null);

        screen.Notice.ShouldNotBeNullOrWhiteSpace();
        screen.Notice!.ShouldContain(nameof(FailureCode.RevisionIntegrityMismatch));

        SessionAggregate after = await open.ReloadAsync();
        after.Session.BackgroundRemovalAuthority.ShouldBeNull();
        after.Attempts.ShouldNotContain(a => a.Step == StepKind.BackgroundRemoval);
        after.Revisions.Single(r => r.Id == reviewed.Id).IsValid.ShouldBeFalse();

        screen.IsAutomaticSelectionAuthorised.ShouldBeFalse();
        screen.CanRunBackgroundRemoval.ShouldBeFalse();
        harness.Meitu.CallCount.ShouldBe(1, "only the Enhancement run reached the adapter.");
    }

    // -------------------------------------------------------------------------------------
    // §13: a retry over unchanged content is still authorised
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Rejecting a cutout and retrying does not ask the operator to authorise again (§13).
    /// </summary>
    /// <remarks>
    /// C2B1 decided this: the authority is about the reviewed <i>upstream</i>, and rejecting the
    /// result did not change the upstream. The screen respects that answer instead of adding a
    /// UI-only "always ask again on retry" policy, which would be a second staleness rule — and
    /// one that disagreed with the engine.
    /// </remarks>
    [Fact]
    public async Task Retrying_over_unchanged_reviewed_content_stays_authorised()
    {
        using HomeScreenHarness harness = new();
        AtBackgroundRemoval open = await AtBackgroundRemovalAsync(harness, "br-retry.png");
        SessionViewModel screen = open.Screen;

        await AuthoriseAsync(screen);
        await screen.RunStepCommand.ExecuteAsync(null);
        screen.IsReviewRequired.ShouldBeTrue();

        await screen.RejectCommand.ExecuteAsync(null);
        await screen.RetryCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();

        // No second decision is asked for, and none is issued.
        screen.IsAutomaticSelectionAuthorised.ShouldBeTrue();
        screen.CanRunBackgroundRemoval.ShouldBeTrue();

        await screen.RunStepCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();

        SessionAggregate persisted = await open.ReloadAsync();
        persisted.Attempts.Count(a => a.Step == StepKind.BackgroundRemoval).ShouldBe(2);
        StepOf(persisted, StepKind.BackgroundRemoval).State.ShouldBe(StepState.ReviewRequired);
    }

    // -------------------------------------------------------------------------------------
    // §12: a changed upstream returns the screen to the unauthorised state
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Replacing the reviewed content puts the screen back where it started (§12).
    /// </summary>
    /// <remarks>
    /// Authority over Revision A does not become authority over Revision B, and the screen does
    /// not carry A's visual state forward. The operator is asked again, about the image they can
    /// now see.
    /// </remarks>
    [Fact]
    public async Task Replacing_the_reviewed_content_returns_the_screen_to_the_unauthorised_state()
    {
        using HomeScreenHarness harness = new();
        AtBackgroundRemoval open = await AtBackgroundRemovalAsync(harness, "br-upstream.png");
        SessionViewModel screen = open.Screen;

        RevisionId revisionA = open.Displayed.RevisionId;
        await AuthoriseAsync(screen);
        screen.IsAutomaticSelectionAuthorised.ShouldBeTrue();

        // Back to Enhancement, re-run it, and return to Background Removal over new content.
        screen.SelectedReturnTarget = screen.ReturnTargets.Single(t => t.Step == StepKind.Enhancement);
        screen.BeginReturnCommand.Execute(null);
        await screen.ConfirmReturnCommand.ExecuteAsync(null);
        await screen.RunStepCommand.ExecuteAsync(null);
        await screen.ApproveCommand.ExecuteAsync(null);
        await screen.PreviewsLoaded;
        screen.Notice.ShouldBeNull();

        SessionAggregate moved = await open.ReloadAsync();
        moved.ToSnapshot().CurrentStep!.Step.ShouldBe(StepKind.BackgroundRemoval);
        screen.ArtefactRevision.ShouldNotBe(ShortRevision(revisionA));

        // The stale authority is still in the database, and the screen shows none of it.
        moved.Session.BackgroundRemovalAuthority.ShouldNotBeNull().ReviewedRevisionId.ShouldBe(revisionA);

        screen.IsAutomaticSelectionAuthorised.ShouldBeFalse();
        screen.IsAutomaticSelectionPending.ShouldBeTrue();
        screen.AutomaticSelectionAuthorisedNotice.ShouldBeEmpty();
        screen.CanRunBackgroundRemoval.ShouldBeFalse();
        screen.CanRunStep.ShouldBeFalse();

        // And the operator can authorise the new content explicitly.
        screen.CanAuthoriseAutomaticSelection.ShouldBeTrue();
        await AuthoriseAsync(screen);
        screen.CanRunBackgroundRemoval.ShouldBeTrue();
    }

    // -------------------------------------------------------------------------------------
    // §21: restart
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// A restart shows the authority still in force, and drops it once the content changes
    /// (§21).
    /// </summary>
    /// <remarks>
    /// A fresh service and a fresh view model over the same database: nothing the previous
    /// screen held in memory survives, so what is displayed afterwards can only have come from
    /// persistence. Both directions are asserted, because a restart that always showed the
    /// authority would be as wrong as one that never did.
    /// </remarks>
    [Fact]
    public async Task A_restart_shows_a_valid_authority_and_drops_one_whose_content_changed()
    {
        using HomeScreenHarness harness = new();
        AtBackgroundRemoval open = await AtBackgroundRemovalAsync(harness, "br-restart.png");

        await AuthoriseAsync(open.Screen);
        RevisionId authorised = open.Displayed.RevisionId;

        RestartedSession first = harness.RestartSession(new RecordingNavigation());
        SessionViewModel resumed = first.Screen;
        resumed.Open((await first.Sessions.LoadAsync(open.Id, CancellationToken.None)).Value);
        await resumed.PreviewsLoaded;

        resumed.IsAutomaticSelectionAuthorised.ShouldBeTrue();
        resumed.AutomaticSelectionAuthorisedNotice.ShouldContain(ShortRevision(authorised));
        resumed.CanRunBackgroundRemoval.ShouldBeTrue();

        // Now the reviewed content is replaced, and a second restart must show none of it.
        await ReplaceUpstreamAsync(harness, open.Id);

        RestartedSession second = harness.RestartSession(new RecordingNavigation());
        SessionViewModel afterChange = second.Screen;
        afterChange.Open((await second.Sessions.LoadAsync(open.Id, CancellationToken.None)).Value);
        await afterChange.PreviewsLoaded;

        afterChange.IsAutomaticSelectionAuthorised.ShouldBeFalse();
        afterChange.AutomaticSelectionAuthorisedNotice.ShouldBeEmpty();
        afterChange.CanRunBackgroundRemoval.ShouldBeFalse();
        afterChange.CanAuthoriseAutomaticSelection.ShouldBeTrue();
    }

    // -------------------------------------------------------------------------------------
    // §14, §15: the review shows the authority that produced the result
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// The review states the authority the producing attempt ran under (§14).
    /// </summary>
    /// <remarks>
    /// One concise line naming the reviewed Revision, and no internal detail: no attempt id, no
    /// timestamps, no adapter identifier, no file path.
    /// </remarks>
    [Fact]
    public async Task The_review_shows_the_authority_the_producing_attempt_ran_under()
    {
        using HomeScreenHarness harness = new();
        AtBackgroundRemoval open = await AtBackgroundRemovalAsync(harness, "br-audit.png");
        SessionViewModel screen = open.Screen;

        RevisionId reviewed = open.Displayed.RevisionId;
        await AuthoriseAsync(screen);
        await screen.RunStepCommand.ExecuteAsync(null);
        await screen.PreviewsLoaded;

        screen.IsReviewRequired.ShouldBeTrue();
        screen.HasBackgroundRemovalAttemptAudit.ShouldBeTrue();
        screen.BackgroundRemovalAttemptAudit.ShouldContain(ShortRevision(reviewed));

        SessionAggregate persisted = await open.ReloadAsync();
        ProcessingAttempt producing = persisted.Attempts.Single(a => a.Step == StepKind.BackgroundRemoval);
        producing.BackgroundRemovalAuthority.ShouldNotBeNull().ReviewedRevisionId.ShouldBe(reviewed);

        // Nothing internal leaks into the line the operator reads.
        screen.BackgroundRemovalAttemptAudit.ShouldNotContain(
            producing.Id.Value.ToString("N", CultureInfo.InvariantCulture));
        screen.BackgroundRemovalAttemptAudit.ShouldNotContain(
            reviewed.Value.ToString("D", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// A later session authority cannot rewrite what the review says about an earlier result
    /// (§15).
    /// </summary>
    /// <remarks>
    /// The distinction §15 exists for, made observable: the session ends up holding an authority
    /// over Revision B while the history holds a result produced under Revision A's authority.
    /// Each review describes the attempt that produced what is on screen — the immutable record —
    /// and never the session's current pending value.
    /// </remarks>
    [Fact]
    public async Task A_later_session_authority_does_not_rewrite_an_earlier_results_review()
    {
        using HomeScreenHarness harness = new();
        AtBackgroundRemoval open = await AtBackgroundRemovalAsync(harness, "br-history.png");
        SessionViewModel screen = open.Screen;

        RevisionId reviewedA = open.Displayed.RevisionId;
        await AuthoriseAsync(screen);
        await screen.RunStepCommand.ExecuteAsync(null);
        screen.IsReviewRequired.ShouldBeTrue();
        screen.BackgroundRemovalAttemptAudit.ShouldContain(ShortRevision(reviewedA));

        // Reject, replace the upstream, and authorise the replacement: the session's pending
        // authority now names B while the history still holds an attempt that ran under A.
        await screen.RejectCommand.ExecuteAsync(null);
        screen.SelectedReturnTarget = screen.ReturnTargets.Single(t => t.Step == StepKind.Enhancement);
        screen.BeginReturnCommand.Execute(null);
        await screen.ConfirmReturnCommand.ExecuteAsync(null);
        await screen.RunStepCommand.ExecuteAsync(null);
        await screen.ApproveCommand.ExecuteAsync(null);
        await screen.PreviewsLoaded;
        screen.Notice.ShouldBeNull();

        RevisionId reviewedB = (await CurrentArtefactAsync(harness, open.Id)).RevisionId;
        reviewedB.ShouldNotBe(reviewedA);
        await AuthoriseAsync(screen);

        (await open.ReloadAsync()).Session.BackgroundRemovalAuthority
            .ShouldNotBeNull().ReviewedRevisionId.ShouldBe(reviewedB);

        // Run again and read the review of the *new* result: it names B, not A.
        await screen.RunStepCommand.ExecuteAsync(null);
        await screen.PreviewsLoaded;
        screen.IsReviewRequired.ShouldBeTrue();
        screen.BackgroundRemovalAttemptAudit.ShouldContain(ShortRevision(reviewedB));
        screen.BackgroundRemovalAttemptAudit.ShouldNotContain(ShortRevision(reviewedA));

        // Both attempt rows survive, each holding what it actually ran under.
        SessionAggregate persisted = await open.ReloadAsync();
        List<RevisionId> recorded = [.. persisted.Attempts
            .Where(a => a.Step == StepKind.BackgroundRemoval)
            .Select(a => a.BackgroundRemovalAuthority.ShouldNotBeNull().ReviewedRevisionId)];

        recorded.ShouldContain(reviewedA);
        recorded.ShouldContain(reviewedB);
    }

    // -------------------------------------------------------------------------------------
    // §18: the whole operator journey, in Fake mode
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// The complete Fake-mode journey, from import to an approved transparent cutout (§18).
    /// </summary>
    /// <remarks>
    /// The one test that walks the operator's actual path with no service call of its own: the
    /// Home screen imports, the session screen confirms, runs Enhancement, approves it, arrives
    /// at Background Removal with the run unavailable, authorises the displayed artefact, runs,
    /// gets a real transparent PNG back, sees the before/after pair and the attempt audit, and
    /// approves. The file on disk is inspected because "a cutout was produced" must mean pixels,
    /// not a database row.
    /// </remarks>
    [Fact]
    public async Task The_full_fake_mode_journey_reaches_an_approved_transparent_cutout()
    {
        using HomeScreenHarness harness = new();
        harness.FilePicker.Path = harness.Inner.WriteBorderedSourcePng("br-journey.png");
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);

        SessionView imported = harness.Navigation.WorkflowSelectionFor!;
        SessionViewModel screen = harness.Session(new RecordingNavigation());
        screen.Open(imported);
        await screen.PreviewsLoaded;

        screen.IsFakeProcessing.ShouldBeTrue("this journey must never touch a real Meitu.");

        await screen.ConfirmOriginalCommand.ExecuteAsync(null);
        await screen.RunStepCommand.ExecuteAsync(null);
        await screen.ApproveCommand.ExecuteAsync(null);
        await screen.PreviewsLoaded;
        screen.Notice.ShouldBeNull();

        SessionAggregate atStep = await ReloadAsync(harness, imported.Id);
        atStep.ToSnapshot().CurrentStep!.Step.ShouldBe(StepKind.BackgroundRemoval);

        // The operator sees the artefact the step will consume, and cannot run yet.
        screen.HasArtefact.ShouldBeTrue();
        screen.ArtefactIsInput.ShouldBeTrue();
        screen.CanRunBackgroundRemoval.ShouldBeFalse();
        screen.CanRunStep.ShouldBeFalse();

        // ...and authorises automatic selection for it, explicitly.
        screen.BeginAutomaticSelectionCommand.Execute(null);
        screen.IsConfirmingAutomaticSelection.ShouldBeTrue();
        await screen.ConfirmAutomaticSelectionCommand.ExecuteAsync(null);
        await screen.PreviewsLoaded;

        screen.IsAutomaticSelectionAuthorised.ShouldBeTrue();
        screen.CanRunBackgroundRemoval.ShouldBeTrue();

        await screen.RunStepCommand.ExecuteAsync(null);
        await screen.PreviewsLoaded;
        screen.Notice.ShouldBeNull();

        // ReviewRequired, with a real before/after pair and the attempt's authority beside it.
        screen.IsReviewRequired.ShouldBeTrue();
        screen.PreviewPanes.Count.ShouldBe(2);
        screen.PreviewPanes.Select(p => p.Heading).ShouldBe([screen.BeforeLabel, screen.AfterLabel]);
        screen.PreviewPanes.ShouldAllBe(p => p.HasImage);
        screen.HasBackgroundRemovalAttemptAudit.ShouldBeTrue();

        // The cutout is a real transparent PNG on disk, not a row that says so.
        SessionAggregate persisted = await ReloadAsync(harness, imported.Id);
        Revision cutout = persisted.Revisions.Single(
            r => r.Id == StepOf(persisted, StepKind.BackgroundRemoval).CurrentRevisionId);

        MeituTransparencyFacts alpha = (await new WicMeituTransparencyInspector().InspectAsync(
            harness.ResolveInWorkspace(cutout.File.RelativePath), CancellationToken.None)).Value;
        alpha.HasTransparentPixels.ShouldBeTrue();
        alpha.HasVisiblePixels.ShouldBeTrue();

        await screen.ApproveCommand.ExecuteAsync(null);
        await screen.PreviewsLoaded;
        screen.Notice.ShouldBeNull();

        SessionAggregate approved = await ReloadAsync(harness, imported.Id);
        StepOf(approved, StepKind.BackgroundRemoval).State.ShouldBe(StepState.Approved);
        approved.Reviews.ShouldContain(r => r.Step == StepKind.BackgroundRemoval && r.IsApproved);

        // ...and the workflow carries on from there.
        approved.ToSnapshot().CurrentStep!.Step.ShouldBe(StepKind.Trim);
    }

    // -------------------------------------------------------------------------------------
    // Plumbing
    // -------------------------------------------------------------------------------------

    /// <summary>A session screen sitting on Background Removal, with nothing authorised.</summary>
    private sealed record AtBackgroundRemoval(
        HomeScreenHarness Harness, SessionViewModel Screen, SessionId Id, ArtefactView Displayed)
    {
        public Task<SessionAggregate> ReloadAsync() => BackgroundRemovalUiTests.ReloadAsync(Harness, Id);
    }

    /// <summary>
    /// Imports, confirms, runs and approves Enhancement, and stops on Background Removal.
    /// </summary>
    /// <remarks>
    /// Enhancement is run and approved rather than skipped, so the reviewed content is a produced
    /// Revision a later <c>ReturnToStep</c> can genuinely replace — which is what the
    /// changed-content tests need to be about anything at all.
    /// </remarks>
    private static async Task<AtBackgroundRemoval> AtBackgroundRemovalAsync(
        HomeScreenHarness harness, string fileName)
    {
        harness.FilePicker.Path = harness.Inner.WriteBorderedSourcePng(fileName);
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);

        SessionView imported = harness.Navigation.WorkflowSelectionFor!;
        SessionViewModel screen = harness.Session(new RecordingNavigation());
        screen.Open(imported);

        await screen.ConfirmOriginalCommand.ExecuteAsync(null);
        await screen.RunStepCommand.ExecuteAsync(null);
        await screen.ApproveCommand.ExecuteAsync(null);
        await screen.PreviewsLoaded;
        screen.Notice.ShouldBeNull();

        (await ReloadAsync(harness, imported.Id)).ToSnapshot()
            .CurrentStep!.Step.ShouldBe(StepKind.BackgroundRemoval);

        ArtefactView displayed = await CurrentArtefactAsync(harness, imported.Id);
        return new AtBackgroundRemoval(harness, screen, imported.Id, displayed);
    }

    /// <summary>Drives the screen's own authorisation path: open the confirmation, confirm it.</summary>
    private static async Task AuthoriseAsync(SessionViewModel screen)
    {
        screen.BeginAutomaticSelectionCommand.Execute(null);
        await screen.ConfirmAutomaticSelectionCommand.ExecuteAsync(null);
        await screen.PreviewsLoaded;
        screen.Notice.ShouldBeNull();
    }

    /// <summary>
    /// Moves the session's reviewed content on behind the screen's back, through the service.
    /// </summary>
    /// <remarks>
    /// Deliberately not through the view model under test: the point of a stale-screen test is
    /// that the change happened somewhere the screen could not see.
    /// </remarks>
    private static async Task ReplaceUpstreamAsync(HomeScreenHarness harness, SessionId id)
    {
        await Must(harness.Sessions.ExecuteAsync(
            id, new WorkflowCommand.ReturnToStep(StepKind.Enhancement), "other", CancellationToken.None));
        await Must(harness.Sessions.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "other", CancellationToken.None));

        SessionView produced = (await harness.Sessions.LoadAsync(id, CancellationToken.None)).Value;
        Sha256 hash = produced.Steps.Single(s => s.Step == StepKind.Enhancement).CurrentRevisionSha256!.Value;

        await Must(harness.Sessions.ExecuteAsync(
            id, new WorkflowCommand.Approve(StepKind.Enhancement, hash), "other", CancellationToken.None));
    }

    private static async Task<ArtefactView> CurrentArtefactAsync(HomeScreenHarness harness, SessionId id) =>
        (await harness.Sessions.LoadAsync(id, CancellationToken.None)).Value.CurrentArtefact.ShouldNotBeNull();

    private static async Task<SessionAggregate> ReloadAsync(HomeScreenHarness harness, SessionId id) =>
        (await harness.Inner.Repository.LoadAsync(id, CancellationToken.None)).Value!;

    private static async Task Must(Task<OperationResult<SessionView>> pending)
    {
        OperationResult<SessionView> result = await pending;
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : string.Empty);
    }

    private static SessionStep StepOf(SessionAggregate aggregate, StepKind step) =>
        aggregate.Steps.Single(s => s.Step == step);

    /// <summary>
    /// The eight-character form the screen displays, so tests compare like with like.
    /// </summary>
    /// <remarks>
    /// The trailing digits, for the reason the view model states: Revision ids are UUIDv7 and
    /// two produced moments apart share their leading digits, so a leading short form would make
    /// these tests pass while the screen showed the operator the same label for both.
    /// </remarks>
    private static string ShortRevision(RevisionId revision) =>
        revision.Value.ToString("N", CultureInfo.InvariantCulture)[^8..];
}
