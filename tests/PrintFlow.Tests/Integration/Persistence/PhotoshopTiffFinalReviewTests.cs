using System.IO;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Reviews;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;
using Shouldly;

namespace PrintFlow.Tests.Integration.Persistence;

/// <summary>
/// The final review of a production TIFF: what approval and rejection do to the file, to the
/// records, and to a session that is interrupted in the middle of either
/// (Epic 11400 Part C2B §5–§18, §27–§34).
/// </summary>
/// <remarks>
/// Driven through the Fake adapter, deliberately. §36 requires the file lifecycle to be
/// adapter-agnostic: promotion and disposal are decisions about a validated artefact, and a
/// version of them that only worked when Photoshop had produced the bytes would be a second review
/// path. The controlled workstation smoke proves the real adapter arrives at the same seam.
/// <para>
/// Nothing here re-implements the exact-hash guard. Approve and Reject reach
/// <see cref="RevisionIntegrityGuard"/> through <c>SessionService.EnsureIntegrityAsync</c>, the
/// same route every other review takes, and §27's regressions prove the guard is what stops a
/// mutated TIFF rather than a new check C2B added beside it.
/// </para>
/// </remarks>
public sealed class PhotoshopTiffFinalReviewTests
{
    // -----------------------------------------------------------------------------------
    // §6, §8, §28 — approval promotes the exact reviewed bytes
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// Approval copies the reviewed TIFF into <c>Approved\</c> unchanged, and the record of the
    /// deliverable follows it there (§6, §8, §28).
    /// </summary>
    /// <remarks>
    /// Asserted as one property rather than four tests, because the property <i>is</i> their
    /// agreement: an Approved file whose bytes differ, or a PrintOutput still pointing at Working
    /// after approval, each passes most of the separate assertions while being exactly the state
    /// this slice exists to prevent.
    /// </remarks>
    [Fact]
    public async Task Approval_promotes_the_exact_reviewed_TIFF_into_Approved_with_the_same_hash()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReachReviewRequiredAsync(harness, service);

        SessionAggregate before = await LoadAsync(harness, id);
        Revision tiff = TiffRevision(before);
        string workingPath = harness.FileWorkspace.ResolveAbsolute(tiff.File);

        await Must(ApproveAsync(service, id, tiff));

        SessionAggregate after = await LoadAsync(harness, id);
        PrintOutput output = after.Outputs.Single();

        output.File.Area.ShouldBe(WorkspaceArea.Approved);
        output.File.FileName.ShouldBe("tiff-out_200mm_CMYK_W.tif");
        output.ReviewState.ShouldBe(ReviewState.Approved);
        output.PromotionReservation.ShouldBeNull();

        // The promoted bytes are the reviewed bytes, byte for byte, and both records still agree
        // on the one hash. Approval is a lifecycle operation, never a second export (§8).
        string approvedPath = harness.FileWorkspace.ResolveAbsolute(output.File);
        File.Exists(approvedPath).ShouldBeTrue();
        File.ReadAllBytes(approvedPath).ShouldBe(File.ReadAllBytes(workingPath));
        output.Sha256.ShouldBe(tiff.Sha256);
        output.ByteLength.ShouldBe(tiff.Facts.ByteLength);

        // The producing Revision is immutable and did not move with the deliverable: it records
        // what was produced and where it was produced (§4).
        Revision reloaded = TiffRevision(after);
        reloaded.File.ShouldBe(tiff.File);
        reloaded.File.Area.ShouldBe(WorkspaceArea.Working);
        reloaded.Sha256.ShouldBe(tiff.Sha256);
        File.Exists(workingPath).ShouldBeTrue();
    }

    /// <summary>
    /// Promotion creates no second content Revision, and leaves exactly one review decision
    /// (§28).
    /// </summary>
    /// <remarks>
    /// The bytes moved folders; nothing new was produced. A Revision minted for the copy would
    /// claim a derivation step that never happened, and would give the session two artefacts with
    /// one hash where the audit expects one.
    /// </remarks>
    [Fact]
    public async Task Approval_creates_no_second_Revision_and_exactly_one_review_decision()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReachReviewRequiredAsync(harness, service);

        SessionAggregate before = await LoadAsync(harness, id);
        await Must(ApproveAsync(service, id, TiffRevision(before)));

        SessionAggregate after = await LoadAsync(harness, id);

        after.Revisions.Count(r => r.Operation == OperationKind.PhotoshopOutput).ShouldBe(1);
        after.Revisions.Count.ShouldBe(before.Revisions.Count);
        after.Outputs.Count.ShouldBe(1);
        after.Attempts.Count(a => a.Step == StepKind.PhotoshopOutput).ShouldBe(1);

        ReviewDecision decision = after.Reviews.Single(r => r.Step == StepKind.PhotoshopOutput);
        decision.IsApproved.ShouldBeTrue();
        decision.SubjectKind.ShouldBe(ReviewSubjectKind.PrintOutput);
        decision.ReviewedSha256.ShouldBe(TiffRevision(after).Sha256);
    }

    /// <summary>
    /// The approved TIFF is the only file in <c>Approved\</c>, and the workspace holds exactly two
    /// copies of those bytes: the immutable producing artefact, and the deliverable (§9).
    /// </summary>
    /// <remarks>
    /// The Working original is retained rather than deleted, and that is the established promotion
    /// contract rather than an oversight — <c>ApprovedPngExport</c> has always promoted by copying
    /// and left its source in place. Deleting it would break the producing Revision, whose
    /// integrity guard re-reads that exact file, for the sake of tidiness that belongs to session
    /// cleanup (§20).
    /// </remarks>
    [Fact]
    public async Task Approval_leaves_one_Approved_file_and_retains_the_producing_Working_artefact()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReachReviewRequiredAsync(harness, service);

        SessionAggregate before = await LoadAsync(harness, id);
        await Must(ApproveAsync(service, id, TiffRevision(before)));

        SessionAggregate after = await LoadAsync(harness, id);
        ApprovedFiles(harness, after).Length.ShouldBe(1);

        // And the retained Working artefact is no longer presented as anything pending: the step
        // is Approved, so no review command is offered against it.
        after.Steps.Single(s => s.Step == StepKind.PhotoshopOutput).State.ShouldBe(StepState.Approved);
    }

    // -----------------------------------------------------------------------------------
    // §7, §29 — the Approved collision contract
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// An existing approved file of the same name is never overwritten: the established
    /// <c>_02</c> numbering claims the next name, and the record names the file that was actually
    /// written (§7, §29).
    /// </summary>
    [Fact]
    public async Task Approval_never_overwrites_an_existing_Approved_file_and_records_the_name_it_took()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReachReviewRequiredAsync(harness, service);

        SessionAggregate before = await LoadAsync(harness, id);
        Revision tiff = TiffRevision(before);

        // Someone else's approved TIFF already holds the name this one would take.
        string approvedDirectory = Path.Combine(
            harness.FileWorkspace.ResolveAbsoluteDirectory(before.Session.Workspace), "Approved");
        Directory.CreateDirectory(approvedDirectory);
        string seeded = Path.Combine(approvedDirectory, "tiff-out_200mm_CMYK_W.tif");
        byte[] seededBytes = "an earlier approved output"u8.ToArray();
        File.WriteAllBytes(seeded, seededBytes);

        await Must(ApproveAsync(service, id, tiff));

        PrintOutput output = (await LoadAsync(harness, id)).Outputs.Single();
        output.File.FileName.ShouldBe("tiff-out_200mm_CMYK_W_02.tif");

        // The seeded file is untouched, and the record points at the file that really holds the
        // approved bytes rather than at the name that was asked for.
        File.ReadAllBytes(seeded).ShouldBe(seededBytes);
        File.ReadAllBytes(harness.FileWorkspace.ResolveAbsolute(output.File))
            .ShouldBe(File.ReadAllBytes(harness.FileWorkspace.ResolveAbsolute(tiff.File)));
    }

    // -----------------------------------------------------------------------------------
    // §11 — approval idempotency
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// A second approval of an approved TIFF is refused, and copies nothing (§11).
    /// </summary>
    /// <remarks>
    /// Refused by the existing review contract rather than by a new rule: <c>Approve</c> is not a
    /// legal command against a step in <c>Approved</c>, so a replayed command cannot reach the
    /// file work at all. That is the check that matters — a second Approved copy would be created
    /// by a second promotion, and there is no second promotion to reach.
    /// </remarks>
    [Fact]
    public async Task A_replayed_approval_is_refused_and_creates_no_second_Approved_copy()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReachReviewRequiredAsync(harness, service);

        SessionAggregate before = await LoadAsync(harness, id);
        Revision tiff = TiffRevision(before);
        await Must(ApproveAsync(service, id, tiff));

        OperationResult<SessionView> replay = await ApproveAsync(service, id, tiff);
        replay.IsFailure.ShouldBeTrue();

        SessionAggregate after = await LoadAsync(harness, id);
        ApprovedFiles(harness, after).Length.ShouldBe(1);
        after.Reviews.Count(r => r.Step == StepKind.PhotoshopOutput).ShouldBe(1);
        after.Revisions.Count(r => r.Operation == OperationKind.PhotoshopOutput).ShouldBe(1);
    }

    // -----------------------------------------------------------------------------------
    // §18 — approval completes the workflow
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// Completion is available only after the TIFF has been approved, and completing then marks
    /// the session Completed with the approved output intact (§18).
    /// </summary>
    /// <remarks>
    /// Approval does not silently complete the session, and this test asserts both halves of why
    /// that is right: before approval <c>Complete</c> is refused because the terminal step is not
    /// Approved, and after it the existing session-scoped transition does the rest. Adding an
    /// automatic completion here would have changed a terminal transition three workflows share,
    /// to replace a rule that already produces the required outcome.
    /// </remarks>
    [Fact]
    public async Task The_session_completes_only_after_the_TIFF_is_approved()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReachReviewRequiredAsync(harness, service);

        OperationResult<SessionView> early = await service.ExecuteAsync(
            id, new WorkflowCommand.Complete(), "tester", CancellationToken.None);
        early.IsFailure.ShouldBeTrue();
        (await LoadAsync(harness, id)).Session.State.ShouldBe(SessionState.Active);

        SessionAggregate reviewed = await LoadAsync(harness, id);
        await Must(ApproveAsync(service, id, TiffRevision(reviewed)));

        OperationResult<SessionView> completed = await service.ExecuteAsync(
            id, new WorkflowCommand.Complete(), "tester", CancellationToken.None);
        await Must(Task.FromResult(completed));

        SessionAggregate after = await LoadAsync(harness, id);
        after.Session.State.ShouldBe(SessionState.Completed);
        after.Session.CompletedAtUtc.ShouldNotBeNull();

        PrintOutput output = after.Outputs.Single();
        output.ReviewState.ShouldBe(ReviewState.Approved);
        output.File.Area.ShouldBe(WorkspaceArea.Approved);
        File.Exists(harness.FileWorkspace.ResolveAbsolute(output.File)).ShouldBeTrue();
    }

    /// <summary>
    /// Completion retention preserves every authoritative Revision at its persisted location.
    /// </summary>
    /// <remarks>
    /// SCRUM-11114 replaces the former unwired boundary with verified promotion followed by
    /// classified cleanup. The stronger assertion is against reloaded authority, not a folder
    /// name: a current Revision must still resolve after its storage location changes.
    /// </remarks>
    [Fact]
    public async Task Completing_the_session_retains_all_authoritative_Revision_files()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReachReviewRequiredAsync(harness, service);

        SessionAggregate reviewed = await LoadAsync(harness, id);
        await Must(ApproveAsync(service, id, TiffRevision(reviewed)));
        await Must(service.ExecuteAsync(id, new WorkflowCommand.Complete(), "tester", CancellationToken.None));

        SessionAggregate after = await LoadAsync(harness, id);
        foreach (Revision revision in after.Revisions)
        {
            File.Exists(harness.FileWorkspace.ResolveAbsolute(revision.File))
                .ShouldBeTrue($"{revision.Operation} revision {revision.Id} lost its file.");
        }
    }

    // -----------------------------------------------------------------------------------
    // §12, §13, §14, §16, §30 — rejection
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// Rejection sends the exact generated TIFF to the Recycle Bin, records the typed reason, and
    /// returns the step to RetryRequired without approving anything (§12, §13, §14, §30).
    /// </summary>
    [Fact]
    public async Task Rejection_recycles_the_exact_TIFF_records_the_reason_and_returns_to_RetryRequired()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReachReviewRequiredAsync(harness, service);

        SessionAggregate before = await LoadAsync(harness, id);
        Revision tiff = TiffRevision(before);
        string tiffPath = harness.FileWorkspace.ResolveAbsolute(tiff.File);

        await Must(service.ExecuteAsync(
            id,
            new WorkflowCommand.Reject(
                StepKind.PhotoshopOutput, tiff.Sha256, RejectionReason.WhiteInkIssue, "underbase too tight"),
            "tester",
            CancellationToken.None));

        // Exactly one Recycle Bin call, for exactly the file under review.
        harness.RecycleBin.Recycled.ShouldBe([tiffPath]);
        File.Exists(tiffPath).ShouldBeFalse();

        SessionAggregate after = await LoadAsync(harness, id);

        ReviewDecision decision = after.Reviews.Single(r => r.Step == StepKind.PhotoshopOutput);
        decision.IsApproved.ShouldBeFalse();
        decision.QuickReason.ShouldBe(RejectionReason.WhiteInkIssue);
        decision.Notes.ShouldBe("underbase too tight");
        decision.ReviewedSha256.ShouldBe(tiff.Sha256);
        decision.SubjectKind.ShouldBe(ReviewSubjectKind.PrintOutput);

        PrintOutput output = after.Outputs.Single();
        output.ReviewState.ShouldBe(ReviewState.Rejected);
        output.RecycledAtUtc.ShouldNotBeNull();

        after.Steps.Single(s => s.Step == StepKind.PhotoshopOutput).State.ShouldBe(StepState.RetryRequired);
        after.Session.State.ShouldBe(SessionState.Active);
        ApprovedFiles(harness, after).ShouldBeEmpty();
    }

    /// <summary>
    /// The rejected TIFF's history survives its disposal: the Revision, its hash, its lineage, the
    /// producing attempt and the decision all remain (§16).
    /// </summary>
    /// <remarks>
    /// The record and the bytes are different things, and only one of them was disposed of. What
    /// must not survive is the claim that the file is still available — that is what
    /// <c>RecycledAtUtc</c> says, and what the read model reports.
    /// </remarks>
    [Fact]
    public async Task A_rejected_TIFF_stays_auditable_after_its_bytes_are_recycled()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReachReviewRequiredAsync(harness, service);

        SessionAggregate before = await LoadAsync(harness, id);
        Revision tiff = TiffRevision(before);
        ProcessingAttempt producing = PhotoshopAttempt(before);

        await Must(RejectAsync(service, id, tiff));

        SessionAggregate after = await LoadAsync(harness, id);

        Revision historical = TiffRevision(after);
        historical.Id.ShouldBe(tiff.Id);
        historical.Sha256.ShouldBe(tiff.Sha256);
        historical.File.FileName.ShouldBe(tiff.File.FileName);
        historical.SourceRevisionId.ShouldBe(tiff.SourceRevisionId);

        PhotoshopAttempt(after).Id.ShouldBe(producing.Id);
        PhotoshopAttempt(after).OutputRevisionId.ShouldBe(tiff.Id);
        after.Reviews.Count(r => r.Step == StepKind.PhotoshopOutput).ShouldBe(1);
    }

    /// <summary>
    /// After rejection, Retry starts a fresh attempt writing to a new path; the recycled TIFF is
    /// never restored and no old path is overwritten (§17).
    /// </summary>
    [Fact]
    public async Task Retry_after_rejection_uses_a_new_attempt_and_a_new_output_path()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReachReviewRequiredAsync(harness, service);

        SessionAggregate before = await LoadAsync(harness, id);
        Revision rejected = TiffRevision(before);
        AttemptId firstAttempt = PhotoshopAttempt(before).Id;

        await Must(RejectAsync(service, id, rejected));

        // The size decision and the W1 branch are not asked for again: workflow policy already
        // keeps them across a retry, and reconfirming them would be an invention (§17).
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.Retry(StepKind.PhotoshopOutput), "tester", CancellationToken.None));
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", CancellationToken.None));

        SessionAggregate after = await LoadAsync(harness, id);
        ProcessingAttempt retry = PhotoshopAttempt(after);

        retry.Id.ShouldNotBe(firstAttempt);
        retry.RetryOfAttemptId.ShouldBe(firstAttempt);

        Revision replacement = after.Revisions
            .Single(r => r.Operation == OperationKind.PhotoshopOutput && r.Id != rejected.Id);
        replacement.File.RelativePath.ShouldNotBe(rejected.File.RelativePath);
        replacement.File.RelativePath.ShouldContain($"/Working/{retry.Id.Value:D}/");
        File.Exists(harness.FileWorkspace.ResolveAbsolute(replacement.File)).ShouldBeTrue();

        // The recycled bytes stay recycled. Nothing restored them, and nothing wrote over the old
        // attempt's path to pretend they were never there.
        File.Exists(harness.FileWorkspace.ResolveAbsolute(rejected.File)).ShouldBeFalse();
        after.Steps.Single(s => s.Step == StepKind.PhotoshopOutput).State.ShouldBe(StepState.ReviewRequired);
    }

    // -----------------------------------------------------------------------------------
    // §27 — a TIFF that changed underneath the review
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// A TIFF mutated in <c>Working\</c> cannot be approved: the decision is refused, the Revision
    /// is invalidated, and nothing is promoted (§27).
    /// </summary>
    [Fact]
    public async Task A_mutated_TIFF_cannot_be_approved_and_nothing_is_promoted()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReachReviewRequiredAsync(harness, service);

        SessionAggregate before = await LoadAsync(harness, id);
        Revision tiff = TiffRevision(before);
        MutateInPlace(harness, tiff);

        OperationResult<SessionView> refused = await ApproveAsync(service, id, tiff);

        refused.IsFailure.ShouldBeTrue();
        refused.Failure.Code.ShouldBe(FailureCode.RevisionIntegrityMismatch);

        SessionAggregate after = await LoadAsync(harness, id);
        ApprovedFiles(harness, after).ShouldBeEmpty();
        after.Reviews.ShouldNotContain(r => r.Step == StepKind.PhotoshopOutput);
        after.Outputs.Single().ReviewState.ShouldBe(ReviewState.NotReviewed);
        after.Outputs.Single().File.Area.ShouldBe(WorkspaceArea.Working);
        after.Outputs.Single().PromotionReservation.ShouldBeNull();
        after.Steps.Single(s => s.Step == StepKind.PhotoshopOutput).State.ShouldBe(StepState.ReviewRequired);
        TiffRevision(after).IsValid.ShouldBeFalse();
    }

    /// <summary>
    /// A mutated TIFF cannot be rejected either, and the Recycle Bin is never called (§27).
    /// </summary>
    /// <remarks>
    /// The symmetry matters more than it looks. Rejection disposes of a file, so a rejection that
    /// skipped the hash check would be a route to recycling bytes nobody reviewed — the same
    /// authority failure as approving them, with the file gone at the end of it.
    /// </remarks>
    [Fact]
    public async Task A_mutated_TIFF_cannot_be_rejected_and_the_Recycle_Bin_is_never_called()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReachReviewRequiredAsync(harness, service);

        SessionAggregate before = await LoadAsync(harness, id);
        Revision tiff = TiffRevision(before);
        MutateInPlace(harness, tiff);

        OperationResult<SessionView> refused = await RejectAsync(service, id, tiff);

        refused.IsFailure.ShouldBeTrue();
        refused.Failure.Code.ShouldBe(FailureCode.RevisionIntegrityMismatch);
        harness.RecycleBin.Recycled.ShouldBeEmpty();
        File.Exists(harness.FileWorkspace.ResolveAbsolute(tiff.File)).ShouldBeTrue();

        SessionAggregate after = await LoadAsync(harness, id);
        after.Reviews.ShouldNotContain(r => r.Step == StepKind.PhotoshopOutput);
        after.Outputs.Single().RecycledAtUtc.ShouldBeNull();
        after.Steps.Single(s => s.Step == StepKind.PhotoshopOutput).State.ShouldBe(StepState.ReviewRequired);
    }

    // -----------------------------------------------------------------------------------
    // §31, §32 — failures during the file work
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// A Recycle Bin failure records no rejection at all: the TIFF stays where it is, the step
    /// stays in review, and nothing was hard-deleted (§14, §31).
    /// </summary>
    [Fact]
    public async Task A_failed_recycle_records_no_rejection_and_keeps_the_TIFF()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReachReviewRequiredAsync(harness, service);

        SessionAggregate before = await LoadAsync(harness, id);
        Revision tiff = TiffRevision(before);
        harness.RecycleBin.FailsWith = FailureCode.WorkspaceError;

        OperationResult<SessionView> refused = await RejectAsync(service, id, tiff);

        refused.IsFailure.ShouldBeTrue();
        refused.Failure.Code.ShouldBe(FailureCode.WorkspaceError);
        refused.Failure.Context["recycled"].ShouldBe("false");
        refused.Failure.Context["reviewRecorded"].ShouldBe("false");
        refused.Failure.Context["hardDeleted"].ShouldBe("false");

        File.Exists(harness.FileWorkspace.ResolveAbsolute(tiff.File)).ShouldBeTrue();

        SessionAggregate after = await LoadAsync(harness, id);
        after.Reviews.ShouldNotContain(r => r.Step == StepKind.PhotoshopOutput);
        after.Outputs.Single().ReviewState.ShouldBe(ReviewState.NotReviewed);
        after.Outputs.Single().RecycledAtUtc.ShouldBeNull();
        after.Steps.Single(s => s.Step == StepKind.PhotoshopOutput).State.ShouldBe(StepState.ReviewRequired);
        after.Session.State.ShouldBe(SessionState.Active);

        // And the same rejection succeeds once the cause is cleared: the refusal left nothing
        // behind that has to be unwound first (§31).
        harness.RecycleBin.FailsWith = null;
        await Must(RejectAsync(service, id, tiff));
        (await LoadAsync(harness, id)).Outputs.Single().ReviewState.ShouldBe(ReviewState.Rejected);
    }

    /// <summary>
    /// An I/O failure while copying the approved bytes records no approval, and does not complete
    /// the session (§32).
    /// </summary>
    [Fact]
    public async Task A_failed_promotion_copy_records_no_approval()
    {
        using SessionServiceHarness harness = new();
        FaultingWorkspace faulting = new(harness.FileWorkspace);
        ISessionService service = harness.CreateService(workspace: faulting);
        SessionId id = await ReachReviewRequiredAsync(harness, service);

        SessionAggregate before = await LoadAsync(harness, id);
        Revision tiff = TiffRevision(before);
        faulting.WriteReservedFailsWith = FailureCode.WorkspaceError;

        OperationResult<SessionView> refused = await ApproveAsync(service, id, tiff);
        refused.IsFailure.ShouldBeTrue();

        SessionAggregate after = await LoadAsync(harness, id);
        after.Reviews.ShouldNotContain(r => r.Step == StepKind.PhotoshopOutput);
        after.Outputs.Single().ReviewState.ShouldBe(ReviewState.NotReviewed);
        after.Outputs.Single().File.Area.ShouldBe(WorkspaceArea.Working);
        after.Steps.Single(s => s.Step == StepKind.PhotoshopOutput).State.ShouldBe(StepState.ReviewRequired);
        after.Session.State.ShouldBe(SessionState.Active);

        // The reviewed TIFF is untouched and still recoverable.
        File.Exists(harness.FileWorkspace.ResolveAbsolute(tiff.File)).ShouldBeTrue();

        // And retrying writes into the destination already claimed rather than a second one.
        faulting.WriteReservedFailsWith = null;
        await Must(ApproveAsync(service, id, tiff));
        ApprovedFiles(harness, await LoadAsync(harness, id)).Length.ShouldBe(1);
    }

    /// <summary>
    /// A promoted file whose bytes do not match the reviewed TIFF is never presented as the
    /// approved deliverable: the review is refused and the bad copy is quarantined out of
    /// <c>Approved\</c> (§32).
    /// </summary>
    /// <remarks>
    /// The one failure a copy cannot report about itself. <c>WriteReservedAsync</c> returned
    /// success; only the independent re-hash of what actually landed catches it, which is why that
    /// re-hash exists rather than trusting the copy's own answer (§6).
    /// </remarks>
    [Fact]
    public async Task A_promoted_file_that_does_not_match_the_reviewed_bytes_is_refused_and_quarantined()
    {
        using SessionServiceHarness harness = new();
        FaultingWorkspace faulting = new(harness.FileWorkspace);
        ISessionService service = harness.CreateService(workspace: faulting);
        SessionId id = await ReachReviewRequiredAsync(harness, service);

        SessionAggregate before = await LoadAsync(harness, id);
        Revision tiff = TiffRevision(before);
        faulting.CorruptWrittenBytes = true;

        OperationResult<SessionView> refused = await ApproveAsync(service, id, tiff);

        refused.IsFailure.ShouldBeTrue();
        refused.Failure.Code.ShouldBe(FailureCode.OutputValidationFailed);
        refused.Failure.Context["promoted"].ShouldBe("false");
        refused.Failure.Context["reviewRecorded"].ShouldBe("false");

        SessionAggregate after = await LoadAsync(harness, id);
        after.Reviews.ShouldNotContain(r => r.Step == StepKind.PhotoshopOutput);
        after.Outputs.Single().ReviewState.ShouldBe(ReviewState.NotReviewed);
        after.Outputs.Single().PromotionReservation.ShouldBeNull();

        // Nothing corrupt is left in Approved to be mistaken for the deliverable.
        ApprovedFiles(harness, after).ShouldBeEmpty();
        faulting.Quarantined.Count.ShouldBe(1);

        // And the next approval claims a fresh name and succeeds.
        faulting.CorruptWrittenBytes = false;
        await Must(ApproveAsync(service, id, tiff));
        SessionAggregate approved = await LoadAsync(harness, id);
        ApprovedFiles(harness, approved).Length.ShouldBe(1);
        File.ReadAllBytes(harness.FileWorkspace.ResolveAbsolute(approved.Outputs.Single().File))
            .ShouldBe(File.ReadAllBytes(harness.FileWorkspace.ResolveAbsolute(tiff.File)));
    }

    // -----------------------------------------------------------------------------------
    // §33 — crash and restart around approval
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// A crash before any file work leaves the session exactly as it was, and the approval simply
    /// happens after the restart (§33 A).
    /// </summary>
    [Fact]
    public async Task A_crash_before_the_promotion_leaves_the_TIFF_awaiting_review()
    {
        using SessionServiceHarness harness = new();
        FaultingRepository faulting = new(harness.Repository);
        ISessionService crashing = harness.CreateService(repository: faulting);
        SessionId id = await ReachReviewRequiredAsync(harness, crashing);

        SessionAggregate before = await LoadAsync(harness, id);
        Revision tiff = TiffRevision(before);

        // The reservation transaction is the first thing an approval commits, so failing from here
        // is a crash before any byte was copied.
        faulting.FailFromCommit = faulting.AttemptedCommits + 1;
        (await ApproveAsync(crashing, id, tiff)).IsFailure.ShouldBeTrue();

        SessionAggregate afterCrash = await LoadAsync(harness, id);
        ApprovedFiles(harness, afterCrash).ShouldBeEmpty();
        afterCrash.Outputs.Single().PromotionReservation.ShouldBeNull();
        afterCrash.Outputs.Single().ReviewState.ShouldBe(ReviewState.NotReviewed);
        afterCrash.Steps.Single(s => s.Step == StepKind.PhotoshopOutput).State.ShouldBe(StepState.ReviewRequired);

        ISessionService restarted = harness.CreateService();
        await Must(ApproveAsync(restarted, id, tiff));

        SessionAggregate after = await LoadAsync(harness, id);
        ApprovedFiles(harness, after).Length.ShouldBe(1);
        after.Reviews.Count(r => r.Step == StepKind.PhotoshopOutput).ShouldBe(1);
    }

    /// <summary>
    /// A crash after the promoted copy but before the review commit resumes into the same Approved
    /// file: one copy, one decision, no <c>_02</c> duplicate (§33 B).
    /// </summary>
    /// <remarks>
    /// This is the case the persisted reservation exists for, and the one that would otherwise be
    /// silently wrong. Without it the restarted process would find the name taken by the copy its
    /// predecessor had already written and claim the next one, leaving two identical approved
    /// TIFFs of which only one is recorded.
    /// </remarks>
    [Fact]
    public async Task A_crash_after_the_promotion_resumes_into_the_same_Approved_file()
    {
        using SessionServiceHarness harness = new();
        FaultingRepository faulting = new(harness.Repository);
        ISessionService crashing = harness.CreateService(repository: faulting);
        SessionId id = await ReachReviewRequiredAsync(harness, crashing);

        SessionAggregate before = await LoadAsync(harness, id);
        Revision tiff = TiffRevision(before);

        // Let the reservation commit land, then lose the review commit — the file is on disk and
        // the database still says the TIFF is awaiting review.
        faulting.FailFromCommit = faulting.AttemptedCommits + 2;
        (await ApproveAsync(crashing, id, tiff)).IsFailure.ShouldBeTrue();

        SessionAggregate afterCrash = await LoadAsync(harness, id);
        afterCrash.Outputs.Single().PromotionReservation.ShouldNotBeNull();
        afterCrash.Outputs.Single().ReviewState.ShouldBe(ReviewState.NotReviewed);
        afterCrash.Outputs.Single().File.Area.ShouldBe(WorkspaceArea.Working);
        afterCrash.Steps.Single(s => s.Step == StepKind.PhotoshopOutput).State.ShouldBe(StepState.ReviewRequired);
        ApprovedFiles(harness, afterCrash).Length.ShouldBe(1);

        ISessionService restarted = harness.CreateService();
        await Must(ApproveAsync(restarted, id, tiff));

        SessionAggregate after = await LoadAsync(harness, id);
        string[] approved = ApprovedFiles(harness, after);
        approved.Length.ShouldBe(1);
        Path.GetFileName(approved[0]).ShouldBe("tiff-out_200mm_CMYK_W.tif");

        PrintOutput output = after.Outputs.Single();
        output.File.Area.ShouldBe(WorkspaceArea.Approved);
        output.PromotionReservation.ShouldBeNull();
        output.Sha256.ShouldBe(tiff.Sha256);
        File.ReadAllBytes(approved[0]).ShouldBe(File.ReadAllBytes(harness.FileWorkspace.ResolveAbsolute(tiff.File)));
        after.Reviews.Count(r => r.Step == StepKind.PhotoshopOutput).ShouldBe(1);
    }

    /// <summary>
    /// A restart after a completed approval changes nothing, and the approval cannot be repeated
    /// (§33 C).
    /// </summary>
    [Fact]
    public async Task A_restart_after_a_completed_approval_changes_nothing()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReachReviewRequiredAsync(harness, service);

        SessionAggregate before = await LoadAsync(harness, id);
        Revision tiff = TiffRevision(before);
        await Must(ApproveAsync(service, id, tiff));
        await Must(service.ExecuteAsync(id, new WorkflowCommand.Complete(), "tester", CancellationToken.None));

        ISessionService restarted = harness.CreateService();
        OperationResult<SessionView> reloaded = await restarted.LoadAsync(id, CancellationToken.None);
        reloaded.IsSuccess.ShouldBeTrue();
        reloaded.Value.State.ShouldBe(SessionState.Completed);

        (await ApproveAsync(restarted, id, tiff)).IsFailure.ShouldBeTrue();

        SessionAggregate after = await LoadAsync(harness, id);
        ApprovedFiles(harness, after).Length.ShouldBe(1);
        after.Reviews.Count(r => r.Step == StepKind.PhotoshopOutput).ShouldBe(1);
        after.Outputs.Single().File.Area.ShouldBe(WorkspaceArea.Approved);
    }

    // -----------------------------------------------------------------------------------
    // §34 — crash and restart around rejection
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// A crash before the disposal leaves the TIFF and the review exactly as they were (§34 A).
    /// </summary>
    [Fact]
    public async Task A_crash_before_the_recycle_leaves_the_TIFF_awaiting_review()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReachReviewRequiredAsync(harness, service);

        SessionAggregate before = await LoadAsync(harness, id);
        Revision tiff = TiffRevision(before);

        // Nothing reached the Recycle Bin, because the decision was refused before it could.
        harness.RecycleBin.FailsWith = FailureCode.Cancelled;
        (await RejectAsync(service, id, tiff)).IsFailure.ShouldBeTrue();
        harness.RecycleBin.FailsWith = null;

        SessionAggregate afterCrash = await LoadAsync(harness, id);
        File.Exists(harness.FileWorkspace.ResolveAbsolute(tiff.File)).ShouldBeTrue();
        afterCrash.Reviews.ShouldNotContain(r => r.Step == StepKind.PhotoshopOutput);
        afterCrash.Steps.Single(s => s.Step == StepKind.PhotoshopOutput).State.ShouldBe(StepState.ReviewRequired);

        ISessionService restarted = harness.CreateService();
        await Must(RejectAsync(restarted, id, tiff));
        (await LoadAsync(harness, id)).Outputs.Single().ReviewState.ShouldBe(ReviewState.Rejected);
    }

    /// <summary>
    /// A crash after the disposal but before the review commit is deterministic: no decision is
    /// recorded, no false state exists, the bytes are in the Recycle Bin rather than deleted, and
    /// the next decision on that step is refused because the artefact can no longer be re-read
    /// (§34 B).
    /// </summary>
    /// <remarks>
    /// The honest description of a window this architecture cannot close, stated as a test rather
    /// than left to be discovered. The alternative ordering — record the rejection first, dispose
    /// afterwards — closes this window and opens a worse one: a Recycle Bin failure would then
    /// leave a completed rejection over a file still sitting on disk, which §14 forbids outright.
    /// This is the direction that never claims something untrue.
    /// <para>
    /// The operator's route out is the same one any externally removed artefact leaves: the
    /// Revision is invalidated, the refusal says so, and the file is restorable from the Windows
    /// Recycle Bin. Nothing was hard-deleted, so nothing is unrecoverable.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_crash_after_the_recycle_records_no_decision_and_refuses_the_next_one()
    {
        using SessionServiceHarness harness = new();
        FaultingRepository faulting = new(harness.Repository);
        ISessionService crashing = harness.CreateService(repository: faulting);
        SessionId id = await ReachReviewRequiredAsync(harness, crashing);

        SessionAggregate before = await LoadAsync(harness, id);
        Revision tiff = TiffRevision(before);
        string tiffPath = harness.FileWorkspace.ResolveAbsolute(tiff.File);

        faulting.FailFromCommit = faulting.AttemptedCommits + 1;
        (await RejectAsync(crashing, id, tiff)).IsFailure.ShouldBeTrue();

        // The file went to the Recycle Bin; the decision did not land.
        harness.RecycleBin.Recycled.ShouldBe([tiffPath]);
        harness.RecycleBin.Holding.ShouldContainKey(tiffPath);
        File.Exists(harness.RecycleBin.Holding[tiffPath]).ShouldBeTrue();
        File.Exists(tiffPath).ShouldBeFalse();

        SessionAggregate afterCrash = await LoadAsync(harness, id);
        afterCrash.Reviews.ShouldNotContain(r => r.Step == StepKind.PhotoshopOutput);
        afterCrash.Outputs.Single().ReviewState.ShouldBe(ReviewState.NotReviewed);
        afterCrash.Outputs.Single().RecycledAtUtc.ShouldBeNull();
        afterCrash.Steps.Single(s => s.Step == StepKind.PhotoshopOutput).State.ShouldBe(StepState.ReviewRequired);

        // The restarted process cannot approve what is no longer there, and says why.
        ISessionService restarted = harness.CreateService();
        OperationResult<SessionView> refused = await ApproveAsync(restarted, id, tiff);
        refused.IsFailure.ShouldBeTrue();
        refused.Failure.Code.ShouldBe(FailureCode.RevisionIntegrityMismatch);

        SessionAggregate after = await LoadAsync(harness, id);
        TiffRevision(after).IsValid.ShouldBeFalse();
        ApprovedFiles(harness, after).ShouldBeEmpty();
        after.Reviews.ShouldNotContain(r => r.Step == StepKind.PhotoshopOutput);
        harness.RecycleBin.Recycled.Count.ShouldBe(1);
    }

    /// <summary>
    /// A restart after a completed rejection reads back the decision, the retryable step and the
    /// disposed file, and disposes of nothing a second time (§34 C).
    /// </summary>
    [Fact]
    public async Task A_restart_after_a_completed_rejection_changes_nothing()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReachReviewRequiredAsync(harness, service);

        SessionAggregate before = await LoadAsync(harness, id);
        await Must(RejectAsync(service, id, TiffRevision(before)));

        ISessionService restarted = harness.CreateService();
        OperationResult<SessionView> reloaded = await restarted.LoadAsync(id, CancellationToken.None);
        reloaded.IsSuccess.ShouldBeTrue();
        reloaded.Value.State.ShouldBe(SessionState.Active);

        SessionAggregate after = await LoadAsync(harness, id);
        after.Reviews.Count(r => r.Step == StepKind.PhotoshopOutput).ShouldBe(1);
        after.Outputs.Single().ReviewState.ShouldBe(ReviewState.Rejected);
        after.Outputs.Single().RecycledAtUtc.ShouldNotBeNull();
        after.Steps.Single(s => s.Step == StepKind.PhotoshopOutput).State.ShouldBe(StepState.RetryRequired);
        harness.RecycleBin.Recycled.Count.ShouldBe(1);
    }

    // -----------------------------------------------------------------------------------
    // §19 — the workflow that has no TIFF
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// PREPARE_ASSET is unaffected: its intermediate reviews recycle nothing, and its approved PNG
    /// still arrives through the promotion step it always used (§19).
    /// </summary>
    /// <remarks>
    /// The regression that matters most, because C2B's file work hangs off Approve and Reject —
    /// the two commands every workflow uses. A version of it that keyed off "the step requires
    /// review" rather than "this artefact is a production output" would have started recycling
    /// rejected Meitu results, which MVP design §10 keeps for comparison until the session ends.
    /// </remarks>
    [Fact]
    public async Task PREPARE_ASSET_review_and_completion_are_unchanged()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId id = (await service.ImportAsync(
            WorkflowType.PrepareAsset, harness.WriteBorderedSourcePng(), "asset", "tester",
            CancellationToken.None)).Value.Id;

        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.ConfirmOriginal("looks right"), "tester", CancellationToken.None));
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.Skip(StepKind.Enhancement, "not needed"), "tester", CancellationToken.None));
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.Skip(StepKind.BackgroundRemoval, "already transparent"), "tester",
            CancellationToken.None));

        // A rejected intermediate result is retained, not recycled: this workflow's rejections are
        // not disposals, and nothing about C2B changed that.
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Trim), "tester", CancellationToken.None));
        SessionAggregate afterTrim = await LoadAsync(harness, id);
        Revision firstTrim = afterTrim.Revisions.Last(r => r.Operation == OperationKind.Trim);

        await Must(service.ExecuteAsync(
            id,
            new WorkflowCommand.Reject(StepKind.Trim, firstTrim.Sha256, RejectionReason.EdgeError),
            "tester",
            CancellationToken.None));

        harness.RecycleBin.Recycled.ShouldBeEmpty();
        File.Exists(harness.FileWorkspace.ResolveAbsolute(firstTrim.File)).ShouldBeTrue();

        // And the ordinary path still ends in an approved PNG and a Completed session.
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.Retry(StepKind.Trim), "tester", CancellationToken.None));
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Trim), "tester", CancellationToken.None));

        SessionAggregate retried = await LoadAsync(harness, id);
        Revision secondTrim = retried.Revisions.Last(r => r.Operation == OperationKind.Trim);
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.Approve(StepKind.Trim, secondTrim.Sha256), "tester", CancellationToken.None));
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.ApprovedPngExport), "tester", CancellationToken.None));
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.Complete(), "tester", CancellationToken.None));

        SessionAggregate after = await LoadAsync(harness, id);
        after.Session.State.ShouldBe(SessionState.Completed);
        after.Outputs.ShouldBeEmpty();
        harness.RecycleBin.Recycled.ShouldBeEmpty();

        Revision promoted = after.Revisions.Single(r => r.Operation == OperationKind.PromoteApproved);
        promoted.File.Area.ShouldBe(WorkspaceArea.Approved);
        promoted.Sha256.ShouldBe(secondTrim.Sha256);
        File.Exists(harness.FileWorkspace.ResolveAbsolute(promoted.File)).ShouldBeTrue();
    }

    // -----------------------------------------------------------------------------------
    // §36, §39 — adapter mode
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// The whole approve-and-reject lifecycle runs under the Fake adapter, and the environment
    /// gate still refuses every Production adapter while it does (§36, §39).
    /// </summary>
    /// <remarks>
    /// Both halves in one test on purpose. "File lifecycle works" and "Production is still shut"
    /// are the two claims that would be contradictory if either were achieved by loosening the
    /// other, and asserting them together is what makes it visible that neither was.
    /// </remarks>
    [Fact]
    public async Task The_file_lifecycle_is_not_conditional_on_a_production_adapter()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        harness.EnvironmentGate.Verify(AdapterExecutionMode.Production).IsFailure.ShouldBeTrue();
        harness.EnvironmentGate.Verify(AdapterExecutionMode.Production).Failure.Code
            .ShouldBe(FailureCode.EnvironmentNotVerified);
        harness.FakePhotoshop.Mode.ShouldBe(AdapterExecutionMode.Fake);

        // Reject, retry, then approve — the same seam either way.
        SessionId id = await ReachReviewRequiredAsync(harness, service);
        SessionAggregate first = await LoadAsync(harness, id);
        await Must(RejectAsync(service, id, TiffRevision(first)));

        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.Retry(StepKind.PhotoshopOutput), "tester", CancellationToken.None));
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", CancellationToken.None));

        SessionAggregate second = await LoadAsync(harness, id);
        Revision replacement = second.Revisions
            .Single(r => r.Operation == OperationKind.PhotoshopOutput && r.Id != TiffRevision(first).Id);
        await Must(ApproveAsync(service, id, replacement));
        await Must(service.ExecuteAsync(id, new WorkflowCommand.Complete(), "tester", CancellationToken.None));

        SessionAggregate after = await LoadAsync(harness, id);
        after.Session.State.ShouldBe(SessionState.Completed);
        after.Outputs.Count.ShouldBe(2);
        after.Outputs.Count(o => o.ReviewState == ReviewState.Approved).ShouldBe(1);
        after.Outputs.Count(o => o.ReviewState == ReviewState.Rejected).ShouldBe(1);
        ApprovedFiles(harness, after).Length.ShouldBe(1);

        harness.EnvironmentGate.Verify(AdapterExecutionMode.Production).IsFailure.ShouldBeTrue();
    }

    // -----------------------------------------------------------------------------------
    // §24, §25 — the read model
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// The read model says where the deliverable is, and stops offering bytes that were disposed
    /// of (§24, §25).
    /// </summary>
    [Fact]
    public async Task The_read_model_reports_the_output_location_truthfully()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReachReviewRequiredAsync(harness, service);

        SessionView awaiting = (await service.LoadAsync(id, CancellationToken.None)).Value;
        PrintOutputView pending = awaiting.Outputs.Single();
        pending.Area.ShouldBe(WorkspaceArea.Working);
        pending.IsRecycled.ShouldBeFalse();
        pending.ReviewState.ShouldBe(ReviewState.NotReviewed);

        SessionAggregate before = await LoadAsync(harness, id);
        await Must(RejectAsync(service, id, TiffRevision(before)));

        SessionView rejected = (await service.LoadAsync(id, CancellationToken.None)).Value;
        PrintOutputView disposed = rejected.Outputs.Single();
        disposed.IsRecycled.ShouldBeTrue();
        disposed.ReviewState.ShouldBe(ReviewState.Rejected);
        rejected.CurrentArtefact?.RevisionId.ShouldNotBe(TiffRevision(before).Id);

        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.Retry(StepKind.PhotoshopOutput), "tester", CancellationToken.None));
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", CancellationToken.None));

        SessionAggregate second = await LoadAsync(harness, id);
        Revision replacement = second.Revisions
            .Single(r => r.Operation == OperationKind.PhotoshopOutput && r.Id != TiffRevision(before).Id);
        await Must(ApproveAsync(service, id, replacement));

        SessionView approved = (await service.LoadAsync(id, CancellationToken.None)).Value;
        PrintOutputView deliverable = approved.Outputs.Single(o => o.ReviewState == ReviewState.Approved);
        deliverable.Area.ShouldBe(WorkspaceArea.Approved);
        deliverable.IsRecycled.ShouldBeFalse();
    }

    // -----------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------

    private static Task<OperationResult<SessionView>> ApproveAsync(
        ISessionService service, SessionId id, Revision tiff) =>
        service.ExecuteAsync(
            id, new WorkflowCommand.Approve(StepKind.PhotoshopOutput, tiff.Sha256), "tester",
            CancellationToken.None);

    private static Task<OperationResult<SessionView>> RejectAsync(
        ISessionService service, SessionId id, Revision tiff) =>
        service.ExecuteAsync(
            id,
            new WorkflowCommand.Reject(StepKind.PhotoshopOutput, tiff.Sha256, RejectionReason.InsufficientResult),
            "tester",
            CancellationToken.None);

    /// <summary>Changes the bytes on disk without telling PrintFlow, as an outside editor would.</summary>
    private static void MutateInPlace(SessionServiceHarness harness, Revision revision)
    {
        string path = harness.FileWorkspace.ResolveAbsolute(revision.File);
        byte[] bytes = File.ReadAllBytes(path);
        bytes[^1] ^= 0xFF;
        File.WriteAllBytes(path, bytes);
    }

    private static string[] ApprovedFiles(SessionServiceHarness harness, SessionAggregate aggregate)
    {
        string approved = Path.Combine(
            harness.FileWorkspace.ResolveAbsoluteDirectory(aggregate.Session.Workspace), "Approved");
        return Directory.Exists(approved)
            ? Directory.GetFiles(approved, "*", SearchOption.AllDirectories)
            : [];
    }

    /// <summary>Imports a GeneratePrintTiff session and runs Photoshop output to ReviewRequired.</summary>
    private static async Task<SessionId> ReachReviewRequiredAsync(
        SessionServiceHarness harness, ISessionService service)
    {
        SessionId id = (await service.ImportAsync(
            WorkflowType.GeneratePrintTiff, harness.WriteBorderedSourcePng(), "tiff-out", "tester",
            CancellationToken.None)).Value.Id;

        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.ConfirmOriginal("finished design"), "tester", CancellationToken.None));
        await Must(service.ExecuteAsync(
            id,
            new WorkflowCommand.SetPrintDimensions(PrintDimensions.FromMillimetres(200, 150, SizePreset.Custom)),
            "tester",
            CancellationToken.None));
        await Must(service.ExecuteAsync(
            id,
            new WorkflowCommand.SelectWhiteUnderbaseBranch(WhiteUnderbaseBranch.W1_1px, "ordinary design"),
            "tester",
            CancellationToken.None));
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", CancellationToken.None));

        return id;
    }

    private static Revision TiffRevision(SessionAggregate aggregate) =>
        aggregate.Revisions.Last(r => r.Operation == OperationKind.PhotoshopOutput);

    private static ProcessingAttempt PhotoshopAttempt(SessionAggregate aggregate) =>
        aggregate.Attempts.Last(a => a.Step == StepKind.PhotoshopOutput);

    private static async Task<SessionAggregate> LoadAsync(SessionServiceHarness harness, SessionId id) =>
        (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;

    private static async Task Must(Task<OperationResult<SessionView>> resultTask)
    {
        OperationResult<SessionView> result = await resultTask;
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : "");
    }
}
