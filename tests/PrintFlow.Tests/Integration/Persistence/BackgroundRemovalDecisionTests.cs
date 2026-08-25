using System.IO;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Reviews;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Persistence;

/// <summary>
/// The reviewed-content authority for Background Removal, from the command to the cutout and
/// back out as audit (Epic 11300 Part C2B1 §27).
/// </summary>
/// <remarks>
/// One sentence is on trial in every test below: the decision means "Meitu's automatic selection
/// is authorised for <i>this</i> reviewed content", never "automatic selection is enabled for
/// this session" (§4). Everything else — the persistence, the attempt snapshot, the retry rules,
/// the read model — is machinery in service of keeping that sentence true across a restart, a
/// retry, a rewind and a file that changed underneath.
/// <para>
/// Fake mode throughout (§13, §30). The fake background-removal path writes a real, separate
/// transparent PNG, so everything downstream of the adapter call — the file existing, the
/// inspector reading it, the hash, the Revision, the review binding — is genuinely exercised.
/// Nothing here fabricates a Revision or bypasses <see cref="ISessionService"/>.
/// </para>
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class BackgroundRemovalDecisionTests
{
    // -----------------------------------------------------------------------------
    // §7: a missing product decision is not a failed processing attempt
    // -----------------------------------------------------------------------------

    /// <summary>
    /// Without an explicit decision the step refuses before anything exists to undo (§7, §26).
    /// </summary>
    /// <remarks>
    /// The four "no" assertions are the substance. A missing product decision must not be
    /// recorded as a Meitu failure, so there is no attempt row claiming the adapter ran and
    /// failed; and because the refusal happens before the automation lock, the working copy and
    /// the adapter call, there is nothing on disk either. An implementation that refused only
    /// inside the adapter would pass a "no Revision" assertion and fail every other one here.
    /// </remarks>
    [Fact]
    public async Task An_unspecified_decision_creates_no_attempt_and_never_reaches_the_adapter()
    {
        using SessionServiceHarness harness = new();
        CountingMeituProcessor meitu = new(harness.FakeMeitu);
        ISessionService service = harness.CreateServiceWithMeitu(meitu);

        SessionId id = await StartAtBackgroundRemovalAsync(harness, service);

        OperationResult<SessionView> refused = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.BackgroundRemoval), "tester", CancellationToken.None);

        refused.IsFailure.ShouldBeTrue();
        refused.Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);

        meitu.BackgroundRemovalCalls.ShouldBe(0);

        SessionAggregate after = await LoadAsync(harness, id);
        after.Attempts.ShouldNotContain(a => a.Step == StepKind.BackgroundRemoval);
        after.Revisions.ShouldNotContain(r => r.Operation == OperationKind.RemoveBackground);
        after.Steps.Single(s => s.Step == StepKind.BackgroundRemoval).State.ShouldBe(StepState.Waiting);
    }

    /// <summary>The refusal value cannot be recorded as though it were a decision (§7).</summary>
    /// <remarks>
    /// Storing <c>Unspecified</c> against a Revision would leave a row that reads like a decision
    /// and authorises nothing — the exact shape a later reader would misread. The absence of a
    /// decision is the absence of a row.
    /// </remarks>
    [Fact]
    public async Task Unspecified_is_refused_as_a_decision_rather_than_recorded()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await StartAtBackgroundRemovalAsync(harness, service);

        ArtefactView reviewed = await ReviewedArtefactAsync(service, id);

        OperationResult<SessionView> refused = await service.ExecuteAsync(
            id,
            new WorkflowCommand.SetBackgroundRemovalDecision(
                BackgroundRemovalDecision.Unspecified, reviewed.RevisionId, reviewed.Sha256),
            "tester", CancellationToken.None);

        refused.IsFailure.ShouldBeTrue();
        (await LoadAsync(harness, id)).Session.BackgroundRemovalAuthority.ShouldBeNull();
    }

    // -----------------------------------------------------------------------------
    // §14, §15, §25: the normal success chain
    // -----------------------------------------------------------------------------

    /// <summary>
    /// An authorised run produces exactly one CUTOUT Revision through the ordinary service
    /// (§14, §15, §25).
    /// </summary>
    /// <remarks>
    /// Every assertion is on what ended up in the workspace and the database, never on what the
    /// request object said. The output is a genuinely separate file from the working copy it was
    /// made from, named by the preset naming authority, and carrying alpha — a background removal
    /// that produced an opaque file would be a background removal that did not happen.
    /// </remarks>
    [Fact]
    public async Task An_authorised_run_produces_one_CUTOUT_Revision_awaiting_review()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await StartAtBackgroundRemovalAsync(harness, service, outputName: "PF-CUT-001");

        RevisionId reviewedUpstream = (await ReviewedArtefactAsync(service, id)).RevisionId;
        await SessionServiceHarness.AuthoriseBackgroundRemovalAsync(service, id);

        SessionView ran = await RunBackgroundRemovalAsync(service, id);

        ran.Steps.Single(s => s.Step == StepKind.BackgroundRemoval).State.ShouldBe(StepState.ReviewRequired);

        SessionAggregate after = await LoadAsync(harness, id);
        Revision cutout = after.Revisions.Single(r => r.Operation == OperationKind.RemoveBackground);

        cutout.File.FileName.ShouldBe("PF-CUT-001_CUTOUT.png");
        cutout.IsValid.ShouldBeTrue();
        cutout.Facts.HasAlpha.ShouldBe(true);
        File.Exists(harness.FileWorkspace.ResolveAbsolute(cutout.File)).ShouldBeTrue();

        // §15: lineage comes off the derivation edge, not from a filename or a step ordinal.
        cutout.SourceRevisionId.ShouldBe(reviewedUpstream);

        ProcessingAttempt producing = after.Attempts.Single(a => a.Step == StepKind.BackgroundRemoval);
        producing.Status.ShouldBe(AttemptStatus.Succeeded);
        producing.OutputRevisionId.ShouldBe(cutout.Id);
        producing.InputRevisionId.ShouldBe(reviewedUpstream);
    }

    /// <summary>
    /// The cutout is a new file beside its working copy, never the working copy itself (§25).
    /// </summary>
    [Fact]
    public async Task The_cutout_and_the_working_input_remain_separate_files()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await StartAtBackgroundRemovalAsync(harness, service);

        await SessionServiceHarness.AuthoriseBackgroundRemovalAsync(service, id);
        await RunBackgroundRemovalAsync(service, id);

        SessionAggregate after = await LoadAsync(harness, id);
        Revision cutout = after.Revisions.Single(r => r.Operation == OperationKind.RemoveBackground);
        AttemptId attempt = after.Attempts.Single(a => a.Step == StepKind.BackgroundRemoval).Id;

        string attemptDirectory = Path.Combine(
            harness.FileWorkspace.ResolveAbsoluteDirectory(after.Session.Workspace),
            "Working",
            attempt.Value.ToString("D"));

        string[] produced = Directory.GetFiles(attemptDirectory).Select(Path.GetFileName).ToArray()!;
        produced.ShouldContain("PF-DECISION_CUTOUT.png");

        // The input the adapter read is still there afterwards, and it is a different file.
        produced.Length.ShouldBeGreaterThan(1);
        Path.GetFileName(harness.FileWorkspace.ResolveAbsolute(cutout.File)).ShouldBe("PF-DECISION_CUTOUT.png");
    }

    /// <summary>
    /// Before/After keeps working off the generic derivation edge (§16).
    /// </summary>
    /// <remarks>
    /// There is deliberately nothing background-removal-specific being asserted: the pair is
    /// <c>Revision.SourceRevisionId</c> and the step's own result, exactly as it is for a trim or
    /// an enhancement. A special case for cutouts would be a second definition of "before".
    /// </remarks>
    [Fact]
    public async Task Before_and_after_come_from_the_same_generic_lineage_every_step_uses()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await StartAtBackgroundRemovalAsync(harness, service);

        RevisionId reviewed = (await ReviewedArtefactAsync(service, id)).RevisionId;
        await SessionServiceHarness.AuthoriseBackgroundRemovalAsync(service, id);

        SessionView ran = await RunBackgroundRemovalAsync(service, id);

        ran.HasBeforeAfterComparison.ShouldBeTrue();
        ran.CurrentArtefact!.IsCurrentStepResult.ShouldBeTrue();
        ran.CurrentArtefact.SourceRevisionId.ShouldBe(reviewed);
        ran.UpstreamArtefact!.RevisionId.ShouldBe(reviewed);
    }

    // -----------------------------------------------------------------------------
    // §8, §19, §24: authority is bound to content, not to the session
    // -----------------------------------------------------------------------------

    /// <summary>
    /// Authority granted over Revision A cannot be recorded against a session whose upstream has
    /// become B (§6, §8).
    /// </summary>
    [Fact]
    public async Task A_decision_naming_a_revision_that_is_not_the_upstream_is_refused()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await StartAtBackgroundRemovalAsync(harness, service);

        ArtefactView reviewed = await ReviewedArtefactAsync(service, id);
        RevisionId unrelated = RevisionId.From(Guid.CreateVersion7());

        OperationResult<SessionView> refused = await service.ExecuteAsync(
            id,
            new WorkflowCommand.SetBackgroundRemovalDecision(
                BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent,
                unrelated,
                reviewed.Sha256),
            "tester", CancellationToken.None);

        refused.IsFailure.ShouldBeTrue();
        refused.Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);
        (await LoadAsync(harness, id)).Session.BackgroundRemovalAuthority.ShouldBeNull();
    }

    /// <summary>
    /// A decision whose displayed hash no longer matches the artefact on offer is refused
    /// (§6, §24).
    /// </summary>
    /// <remarks>
    /// The right id with the wrong bytes is the interesting case, and the reason the authority
    /// binds to both halves: an operator who decided about a file that has since been replaced in
    /// place decided about content that is gone.
    /// </remarks>
    [Fact]
    public async Task A_decision_carrying_a_stale_displayed_hash_is_refused()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await StartAtBackgroundRemovalAsync(harness, service);

        ArtefactView reviewed = await ReviewedArtefactAsync(service, id);

        OperationResult<SessionView> refused = await service.ExecuteAsync(
            id,
            new WorkflowCommand.SetBackgroundRemovalDecision(
                BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent,
                reviewed.RevisionId,
                Sha256.Parse(new string('B', 64))),
            "tester", CancellationToken.None);

        refused.IsFailure.ShouldBeTrue();
        refused.Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);
        (await LoadAsync(harness, id)).Session.BackgroundRemovalAuthority.ShouldBeNull();
    }

    /// <summary>
    /// Bytes mutated after the decision was recorded stop it authorising anything (§24).
    /// </summary>
    /// <remarks>
    /// The authority is left exactly as it was — nothing re-binds it, nothing deletes it — and the
    /// run is refused anyway, by the integrity re-check that runs before the command reaches the
    /// engine. No attempt, no adapter call, and the reviewed Revision is marked
    /// <c>FileMutated</c>, so the refusal is recorded as what it is rather than as a Meitu
    /// failure.
    /// </remarks>
    [Fact]
    public async Task Mutating_the_reviewed_bytes_stops_an_already_granted_authority_executing()
    {
        using SessionServiceHarness harness = new();
        CountingMeituProcessor meitu = new(harness.FakeMeitu);
        ISessionService service = harness.CreateServiceWithMeitu(meitu);

        SessionId id = await StartAtBackgroundRemovalAsync(harness, service);
        await SessionServiceHarness.AuthoriseBackgroundRemovalAsync(service, id);

        SessionAggregate authorised = await LoadAsync(harness, id);
        BackgroundRemovalAuthority granted = authorised.Session.BackgroundRemovalAuthority.ShouldNotBeNull();

        Revision reviewed = authorised.Revisions.Single(r => r.Id == granted.ReviewedRevisionId);
        string path = harness.FileWorkspace.ResolveAbsolute(reviewed.File);
        byte[] bytes = File.ReadAllBytes(path);
        bytes[^1] ^= 0xFF;
        File.WriteAllBytes(path, bytes);

        OperationResult<SessionView> refused = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.BackgroundRemoval), "tester", CancellationToken.None);

        refused.IsFailure.ShouldBeTrue();
        refused.Failure.Code.ShouldBe(FailureCode.RevisionIntegrityMismatch);
        meitu.BackgroundRemovalCalls.ShouldBe(0);

        SessionAggregate after = await LoadAsync(harness, id);
        after.Attempts.ShouldNotContain(a => a.Step == StepKind.BackgroundRemoval);
        after.Revisions.Single(r => r.Id == granted.ReviewedRevisionId).InvalidationReason
            .ShouldBe(InvalidationReason.FileMutated);

        // Not silently re-bound to the mutated bytes: the record is exactly what was granted.
        after.Session.BackgroundRemovalAuthority.ShouldBe(granted);
    }

    /// <summary>
    /// Rewinding upstream and producing different content makes the old authority unusable
    /// (§8, §19).
    /// </summary>
    /// <remarks>
    /// The old record is deliberately still in the database afterwards. That is the invalidation
    /// strategy (§9): a stale authority is never usable because the artefact it names is no
    /// longer the one Background Removal would consume, so nothing has to hunt it down and delete
    /// it. What matters is that it cannot execute, and that the read model does not offer it as
    /// readiness.
    /// </remarks>
    [Fact]
    public async Task Returning_upstream_and_re_running_it_leaves_the_old_authority_unusable()
    {
        using SessionServiceHarness harness = new();
        CountingMeituProcessor meitu = new(harness.FakeMeitu);
        ISessionService service = harness.CreateServiceWithMeitu(meitu);

        SessionId id = await StartAtBackgroundRemovalAsync(harness, service);
        await SessionServiceHarness.AuthoriseBackgroundRemovalAsync(service, id);
        await RunBackgroundRemovalAsync(service, id);
        await ApproveAsync(service, id, StepKind.BackgroundRemoval);

        BackgroundRemovalAuthority granted =
            (await LoadAsync(harness, id)).Session.BackgroundRemovalAuthority.ShouldNotBeNull();

        // Rewind to Enhancement and produce a different Revision for it.
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.ReturnToStep(StepKind.Enhancement), "tester", CancellationToken.None));
        await RunAndApproveAsync(service, id, StepKind.Enhancement);

        int callsBefore = meitu.BackgroundRemovalCalls;

        OperationResult<SessionView> refused = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.BackgroundRemoval), "tester", CancellationToken.None);

        refused.IsFailure.ShouldBeTrue();
        refused.Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);
        meitu.BackgroundRemovalCalls.ShouldBe(callsBefore);

        SessionAggregate after = await LoadAsync(harness, id);

        // Retained as history, and powerless: the read model reports no usable decision.
        after.Session.BackgroundRemovalAuthority.ShouldBe(granted);
        after.ToSnapshot().UsableBackgroundRemovalAuthority.ShouldBeNull();

        SessionView view = (await service.LoadAsync(id, CancellationToken.None)).Value;
        view.BackgroundRemovalDecision.ShouldBe(BackgroundRemovalDecision.Unspecified);
        view.BackgroundRemovalDecisionRevisionId.ShouldBeNull();
        view.CanRunBackgroundRemoval.ShouldBeFalse();

        // A fresh decision over the new content is all that is needed, and it works.
        await SessionServiceHarness.AuthoriseBackgroundRemovalAsync(service, id);
        await RunBackgroundRemovalAsync(service, id);
        meitu.BackgroundRemovalCalls.ShouldBe(callsBefore + 1);
    }

    // -----------------------------------------------------------------------------
    // §17: retry semantics turn on whether the reviewed content changed
    // -----------------------------------------------------------------------------

    /// <summary>
    /// Rejecting a cutout and retrying over the same unchanged upstream stays authorised (§17).
    /// </summary>
    /// <remarks>
    /// The operator authorised automatic selection over specific reviewed content, and rejecting
    /// the <i>result</i> did not change that content. Demanding re-authorisation here would be
    /// asking the same question about the same bytes, which the existing semantics do not
    /// require. The distinction that matters is the upstream, not the number of attempts — and
    /// the test that changes the upstream is directly below.
    /// </remarks>
    [Fact]
    public async Task Reject_and_retry_over_byte_identical_reviewed_content_stays_authorised()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await StartAtBackgroundRemovalAsync(harness, service);

        await SessionServiceHarness.AuthoriseBackgroundRemovalAsync(service, id);
        SessionView first = await RunBackgroundRemovalAsync(service, id);

        Sha256 rejected = first.Steps.Single(s => s.Step == StepKind.BackgroundRemoval).CurrentRevisionSha256!.Value;
        await Must(service.ExecuteAsync(
            id,
            new WorkflowCommand.Reject(StepKind.BackgroundRemoval, rejected, RejectionReason.Other),
            "tester", CancellationToken.None));
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.Retry(StepKind.BackgroundRemoval), "tester", CancellationToken.None));

        // No second decision is issued: the reviewed content never changed.
        SessionView reRun = await RunBackgroundRemovalAsync(service, id);
        reRun.Steps.Single(s => s.Step == StepKind.BackgroundRemoval).State.ShouldBe(StepState.ReviewRequired);

        (await LoadAsync(harness, id)).Attempts.Count(a => a.Step == StepKind.BackgroundRemoval).ShouldBe(2);
    }

    /// <summary>
    /// A retry whose upstream changed in the meantime needs a new decision (§8, §17).
    /// </summary>
    [Fact]
    public async Task A_retry_after_the_upstream_changed_requires_a_new_decision()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await StartAtBackgroundRemovalAsync(harness, service);

        await SessionServiceHarness.AuthoriseBackgroundRemovalAsync(service, id);
        SessionView first = await RunBackgroundRemovalAsync(service, id);

        Sha256 rejected = first.Steps.Single(s => s.Step == StepKind.BackgroundRemoval).CurrentRevisionSha256!.Value;
        await Must(service.ExecuteAsync(
            id,
            new WorkflowCommand.Reject(StepKind.BackgroundRemoval, rejected, RejectionReason.Other),
            "tester", CancellationToken.None));

        // The upstream itself is replaced, which is what makes this different from a plain retry.
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.ReturnToStep(StepKind.Enhancement), "tester", CancellationToken.None));
        await RunAndApproveAsync(service, id, StepKind.Enhancement);

        OperationResult<SessionView> refused = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.BackgroundRemoval), "tester", CancellationToken.None);

        refused.IsFailure.ShouldBeTrue();
        refused.Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);

        await SessionServiceHarness.AuthoriseBackgroundRemovalAsync(service, id);
        await RunBackgroundRemovalAsync(service, id);
    }

    // -----------------------------------------------------------------------------
    // §11, §18: the attempt records what it ran under, once
    // -----------------------------------------------------------------------------

    /// <summary>
    /// Each attempt keeps its own decision, and a later one never rewrites an earlier one
    /// (§11, §18).
    /// </summary>
    /// <remarks>
    /// Attempt A runs under an authority for Enhancement result E1; the session is then rewound
    /// so Enhancement produces E2, a new decision is granted over E2, and attempt B runs. If the
    /// attempt copy were a read of session state rather than a snapshot, A would now claim to
    /// have been authorised over content it never saw.
    /// </remarks>
    [Fact]
    public async Task Each_attempt_keeps_the_authority_it_actually_ran_under()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await StartAtBackgroundRemovalAsync(harness, service);

        await SessionServiceHarness.AuthoriseBackgroundRemovalAsync(service, id);
        await RunBackgroundRemovalAsync(service, id);
        await ApproveAsync(service, id, StepKind.BackgroundRemoval);

        SessionAggregate afterFirst = await LoadAsync(harness, id);
        ProcessingAttempt attemptA = afterFirst.Attempts.Single(a => a.Step == StepKind.BackgroundRemoval);
        BackgroundRemovalAuthority authorityA = attemptA.BackgroundRemovalAuthority.ShouldNotBeNull();
        authorityA.Decision.ShouldBe(BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent);
        authorityA.ReviewedRevisionId.ShouldBe(attemptA.InputRevisionId!.Value);

        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.ReturnToStep(StepKind.Enhancement), "tester", CancellationToken.None));
        await RunAndApproveAsync(service, id, StepKind.Enhancement);
        await SessionServiceHarness.AuthoriseBackgroundRemovalAsync(service, id);
        await RunBackgroundRemovalAsync(service, id);

        SessionAggregate afterSecond = await LoadAsync(harness, id);
        ProcessingAttempt reloadedA = afterSecond.Attempts.Single(a => a.Id == attemptA.Id);
        ProcessingAttempt attemptB = afterSecond.Attempts
            .Single(a => a.Step == StepKind.BackgroundRemoval && a.Id != attemptA.Id);

        // A is untouched by anything that happened afterwards.
        reloadedA.BackgroundRemovalAuthority.ShouldBe(authorityA);

        // B has its own, over the content it was actually given.
        BackgroundRemovalAuthority authorityB = attemptB.BackgroundRemovalAuthority.ShouldNotBeNull();
        authorityB.ReviewedRevisionId.ShouldNotBe(authorityA.ReviewedRevisionId);
        authorityB.ReviewedRevisionId.ShouldBe(attemptB.InputRevisionId!.Value);
    }

    /// <summary>
    /// Nothing that is not an authorised background removal carries an authority (§11).
    /// </summary>
    /// <remarks>
    /// Null on an Enhancement or a Trim attempt reads as "this attempt had no reviewed-content
    /// authority", which is the truth. It must never come to read as "the default was used";
    /// there is no default.
    /// </remarks>
    [Fact]
    public async Task Only_the_background_removal_attempt_carries_an_authority()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await StartAtBackgroundRemovalAsync(harness, service);

        await SessionServiceHarness.AuthoriseBackgroundRemovalAsync(service, id);
        await RunBackgroundRemovalAsync(service, id);
        await ApproveAsync(service, id, StepKind.BackgroundRemoval);
        await RunAndApproveAsync(service, id, StepKind.Trim);

        SessionAggregate after = await LoadAsync(harness, id);

        after.Attempts.Single(a => a.Step == StepKind.BackgroundRemoval)
            .BackgroundRemovalAuthority.ShouldNotBeNull();

        foreach (ProcessingAttempt other in after.Attempts.Where(a => a.Step != StepKind.BackgroundRemoval))
        {
            other.BackgroundRemovalAuthority.ShouldBeNull(
                $"a {other.Step} attempt has no reviewed-content authority.");
        }
    }

    // -----------------------------------------------------------------------------
    // §20: restart and resume
    // -----------------------------------------------------------------------------

    /// <summary>
    /// A valid decision survives a restart, and stops being usable when its content is replaced
    /// (§20).
    /// </summary>
    /// <remarks>
    /// The second service instance shares nothing with the first except the database and the
    /// files on disk, which is what makes this a restart rather than a reload. Both halves matter:
    /// a decision the operator made before closing the app is still there, and it is still a
    /// decision about specific content rather than a permission that outlived it.
    /// </remarks>
    [Fact]
    public async Task A_valid_decision_survives_a_restart_and_dies_with_its_content()
    {
        using SessionServiceHarness harness = new();

        ISessionService first = harness.CreateService();
        SessionId id = await StartAtBackgroundRemovalAsync(harness, first);
        await SessionServiceHarness.AuthoriseBackgroundRemovalAsync(first, id);

        BackgroundRemovalAuthority granted =
            (await LoadAsync(harness, id)).Session.BackgroundRemovalAuthority.ShouldNotBeNull();

        // A different service instance, as a restarted process would build.
        ISessionService restarted = harness.CreateService();

        SessionView resumed = (await restarted.LoadAsync(id, CancellationToken.None)).Value;
        resumed.BackgroundRemovalDecision.ShouldBe(
            BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent);
        resumed.BackgroundRemovalDecisionRevisionId.ShouldBe(granted.ReviewedRevisionId);
        resumed.CanRunBackgroundRemoval.ShouldBeTrue();

        await RunBackgroundRemovalAsync(restarted, id);
        await ApproveAsync(restarted, id, StepKind.BackgroundRemoval);

        // Now replace the content the decision named, and it becomes unusable.
        await Must(restarted.ExecuteAsync(
            id, new WorkflowCommand.ReturnToStep(StepKind.Enhancement), "tester", CancellationToken.None));
        await RunAndApproveAsync(restarted, id, StepKind.Enhancement);

        SessionView stale = (await restarted.LoadAsync(id, CancellationToken.None)).Value;
        stale.BackgroundRemovalDecision.ShouldBe(BackgroundRemovalDecision.Unspecified);
        stale.CanRunBackgroundRemoval.ShouldBeFalse();
    }

    // -----------------------------------------------------------------------------
    // §22, §23: the read model and the command cannot disagree
    // -----------------------------------------------------------------------------

    /// <summary>
    /// Whatever the read model says about running Background Removal, the real command agrees
    /// (§23).
    /// </summary>
    /// <remarks>
    /// Walked across the states this slice actually creates — no decision, decided, and decided
    /// but stale — asserting the same predicate from both sides each time. A read model that
    /// re-derived the rules in its own words would drift from the engine here rather than in
    /// production.
    /// </remarks>
    [Fact]
    public async Task The_read_model_and_the_run_command_agree_at_every_stage()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await StartAtBackgroundRemovalAsync(harness, service);

        // Undecided: offered as decidable, not as runnable.
        SessionView undecided = (await service.LoadAsync(id, CancellationToken.None)).Value;
        undecided.CanSetBackgroundRemovalDecision.ShouldBeTrue();
        undecided.CanRunBackgroundRemoval.ShouldBeFalse();
        undecided.BackgroundRemovalDecision.ShouldBe(BackgroundRemovalDecision.Unspecified);
        (await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.BackgroundRemoval), "tester", CancellationToken.None))
            .IsFailure.ShouldBeTrue();

        // Decided: offered as runnable, and it runs.
        await SessionServiceHarness.AuthoriseBackgroundRemovalAsync(service, id);
        SessionView decided = (await service.LoadAsync(id, CancellationToken.None)).Value;
        decided.CanRunBackgroundRemoval.ShouldBeTrue();
        decided.BackgroundRemovalDecision.ShouldBe(
            BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent);

        await RunBackgroundRemovalAsync(service, id);

        // Awaiting review: neither decidable nor runnable, and both commands are refused.
        SessionView reviewing = (await service.LoadAsync(id, CancellationToken.None)).Value;
        reviewing.CanSetBackgroundRemovalDecision.ShouldBeFalse();
        reviewing.CanRunBackgroundRemoval.ShouldBeFalse();

        ArtefactView onScreen = reviewing.CurrentArtefact.ShouldNotBeNull();
        (await service.ExecuteAsync(
            id,
            new WorkflowCommand.SetBackgroundRemovalDecision(
                BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent,
                onScreen.RevisionId, onScreen.Sha256),
            "tester", CancellationToken.None))
            .IsFailure.ShouldBeTrue();
    }

    /// <summary>A workflow with no Background Removal step offers no decision (§6, §22).</summary>
    [Fact]
    public async Task A_workflow_without_the_step_refuses_the_command_and_offers_nothing()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId id = (await service.ImportAsync(
            WorkflowType.GeneratePrintTiff, harness.WriteSourcePng("no-cutout.png"), "no-cutout", "tester",
            CancellationToken.None)).Value.Id;
        await Must(service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None));

        SessionView view = (await service.LoadAsync(id, CancellationToken.None)).Value;
        view.CanSetBackgroundRemovalDecision.ShouldBeFalse();
        view.CanRunBackgroundRemoval.ShouldBeFalse();

        ArtefactView reviewed = view.CurrentArtefact.ShouldNotBeNull();
        OperationResult<SessionView> refused = await service.ExecuteAsync(
            id,
            new WorkflowCommand.SetBackgroundRemovalDecision(
                BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent,
                reviewed.RevisionId, reviewed.Sha256),
            "tester", CancellationToken.None);

        refused.IsFailure.ShouldBeTrue();
    }

    // -----------------------------------------------------------------------------
    // §26: once authorised, an adapter failure is an ordinary adapter failure
    // -----------------------------------------------------------------------------

    /// <summary>
    /// An authorised attempt that fails in Meitu behaves like any other failed attempt (§26).
    /// </summary>
    /// <remarks>
    /// The contrast with the first test in this file is the whole point: a missing product
    /// decision produces no attempt at all, while a genuine processing failure produces a Failed
    /// attempt with a retryable outcome and no usable Revision. The two must never be reported as
    /// the same thing. The authority is still recorded on the failed attempt, because it is still
    /// the truthful answer to "what was this run authorised by".
    /// </remarks>
    [Fact]
    public async Task An_authorised_attempt_that_fails_in_the_adapter_fails_normally()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await StartAtBackgroundRemovalAsync(harness, service);

        await SessionServiceHarness.AuthoriseBackgroundRemovalAsync(service, id);
        harness.FakeMeitu.SetScenario(
            PrintFlow.Infrastructure.Adapters.Fake.FakeAdapterScenario.FailWith(FailureCode.UnknownDialog));

        OperationResult<SessionView> failed = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.BackgroundRemoval), "tester", CancellationToken.None);

        failed.IsFailure.ShouldBeTrue();
        failed.Failure.Code.ShouldNotBe(FailureCode.PreconditionNotMet);

        SessionAggregate after = await LoadAsync(harness, id);
        ProcessingAttempt attempt = after.Attempts.Single(a => a.Step == StepKind.BackgroundRemoval);
        attempt.Status.ShouldBe(AttemptStatus.Failed);
        attempt.OutputRevisionId.ShouldBeNull();
        attempt.BackgroundRemovalAuthority.ShouldNotBeNull();

        after.Revisions.ShouldNotContain(r => r.Operation == OperationKind.RemoveBackground);
        after.Steps.Single(s => s.Step == StepKind.BackgroundRemoval).State.ShouldBe(StepState.Failed);
    }

    // -----------------------------------------------------------------------------
    // §12: Enhancement is untouched
    // -----------------------------------------------------------------------------

    /// <summary>
    /// Enhancement runs with no decision at all, exactly as before this slice (§12).
    /// </summary>
    /// <remarks>
    /// Asserted on the request the adapter actually received, not on the step succeeding: an
    /// enhancement is not a background removal, so it carries <c>Unspecified</c> and always has.
    /// </remarks>
    [Fact]
    public async Task Enhancement_still_runs_undecided_and_carries_no_background_removal_decision()
    {
        using SessionServiceHarness harness = new();
        CountingMeituProcessor meitu = new(harness.FakeMeitu);
        ISessionService service = harness.CreateServiceWithMeitu(meitu);

        SessionId id = (await service.ImportAsync(
            WorkflowType.PrepareAsset, harness.WriteSourcePng("enhance-only.png"), "PF-ENH", "tester",
            CancellationToken.None)).Value.Id;
        await Must(service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None));

        await RunAndApproveAsync(service, id, StepKind.Enhancement);

        meitu.LastEnhanceDecision.ShouldBe(BackgroundRemovalDecision.Unspecified);

        (await LoadAsync(harness, id)).Attempts.Single(a => a.Step == StepKind.Enhancement)
            .BackgroundRemovalAuthority.ShouldBeNull();
    }

    /// <summary>
    /// The decision the adapter receives is the one the attempt row recorded (§12).
    /// </summary>
    [Fact]
    public async Task The_adapter_receives_the_decision_the_attempt_recorded()
    {
        using SessionServiceHarness harness = new();
        CountingMeituProcessor meitu = new(harness.FakeMeitu);
        ISessionService service = harness.CreateServiceWithMeitu(meitu);

        SessionId id = await StartAtBackgroundRemovalAsync(harness, service);
        await SessionServiceHarness.AuthoriseBackgroundRemovalAsync(service, id);
        await RunBackgroundRemovalAsync(service, id);

        meitu.LastBackgroundRemovalDecision.ShouldBe(
            BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent);

        (await LoadAsync(harness, id)).Attempts.Single(a => a.Step == StepKind.BackgroundRemoval)
            .BackgroundRemovalAuthority!.Decision.ShouldBe(meitu.LastBackgroundRemovalDecision!.Value);
    }

    // -----------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------

    /// <summary>
    /// A pass-through Meitu adapter that records what it was asked for.
    /// </summary>
    /// <remarks>
    /// Wraps the real fake rather than replacing it, so every call it does forward still produces
    /// a genuine file and everything downstream stays real. It exists only to make "the adapter
    /// was never called" and "the adapter received this decision" directly assertable, rather
    /// than inferred from the absence of a file.
    /// </remarks>
    private sealed class CountingMeituProcessor : IMeituProcessor
    {
        private readonly IMeituProcessor _inner;

        public CountingMeituProcessor(IMeituProcessor inner) => _inner = inner;

        public string AdapterId => _inner.AdapterId;

        public AdapterExecutionMode Mode => _inner.Mode;

        public int BackgroundRemovalCalls { get; private set; }

        public BackgroundRemovalDecision? LastBackgroundRemovalDecision { get; private set; }

        public BackgroundRemovalDecision? LastEnhanceDecision { get; private set; }

        public Task<OperationResult<AdapterOutput>> ProcessAsync(
            MeituRequest request, CancellationToken cancellationToken)
        {
            if (request.Operation == MeituOperation.RemoveBackground)
            {
                BackgroundRemovalCalls++;
                LastBackgroundRemovalDecision = request.BackgroundRemovalDecision;
            }
            else
            {
                LastEnhanceDecision = request.BackgroundRemovalDecision;
            }

            return _inner.ProcessAsync(request, cancellationToken);
        }
    }

    /// <summary>Imports and runs PREPARE_ASSET as far as BackgroundRemoval, undecided.</summary>
    /// <remarks>
    /// Enhancement is run and approved rather than skipped, so the artefact Background Removal
    /// consumes is a produced Revision that a later <c>ReturnToStep</c> can genuinely replace —
    /// which is what the stale-authority tests need.
    /// </remarks>
    private static async Task<SessionId> StartAtBackgroundRemovalAsync(
        SessionServiceHarness harness, ISessionService service, string outputName = "PF-DECISION")
    {
        OperationResult<SessionView> imported = await service.ImportAsync(
            WorkflowType.PrepareAsset,
            harness.WriteBorderedSourcePng($"{outputName}-source.png"),
            outputName,
            "tester",
            CancellationToken.None);
        imported.IsSuccess.ShouldBeTrue(imported.IsFailure ? imported.Failure.ToString() : "");

        SessionId id = imported.Value.Id;
        await Must(service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None));
        await RunAndApproveAsync(service, id, StepKind.Enhancement);

        SessionView atStep = (await service.LoadAsync(id, CancellationToken.None)).Value;
        atStep.CurrentStep!.Step.ShouldBe(StepKind.BackgroundRemoval);
        return id;
    }

    /// <summary>The artefact the read model says is on screen — what an operator would decide about.</summary>
    private static async Task<ArtefactView> ReviewedArtefactAsync(ISessionService service, SessionId id)
    {
        SessionView view = (await service.LoadAsync(id, CancellationToken.None)).Value;
        return view.CurrentArtefact.ShouldNotBeNull();
    }

    private static async Task<SessionView> RunBackgroundRemovalAsync(ISessionService service, SessionId id)
    {
        OperationResult<SessionView> ran = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.BackgroundRemoval), "tester", CancellationToken.None);
        ran.IsSuccess.ShouldBeTrue(ran.IsFailure ? ran.Failure.ToString() : "");
        return ran.Value;
    }

    private static async Task RunAndApproveAsync(ISessionService service, SessionId id, StepKind step)
    {
        if (step == StepKind.BackgroundRemoval)
        {
            await SessionServiceHarness.AuthoriseBackgroundRemovalAsync(service, id);
        }

        OperationResult<SessionView> started = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(step), "tester", CancellationToken.None);
        started.IsSuccess.ShouldBeTrue(started.IsFailure ? started.Failure.ToString() : "");

        SessionStep produced = started.Value.Steps.Single(s => s.Step == step);
        if (produced.State == StepState.ReviewRequired)
        {
            await Must(service.ExecuteAsync(
                id, new WorkflowCommand.Approve(step, produced.CurrentRevisionSha256!.Value), "tester",
                CancellationToken.None));
        }
    }

    private static async Task ApproveAsync(ISessionService service, SessionId id, StepKind step)
    {
        SessionView view = (await service.LoadAsync(id, CancellationToken.None)).Value;
        Sha256 hash = view.Steps.Single(s => s.Step == step).CurrentRevisionSha256!.Value;
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.Approve(step, hash), "tester", CancellationToken.None));
    }

    private static async Task<SessionAggregate> LoadAsync(SessionServiceHarness harness, SessionId id) =>
        (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;

    private static async Task Must(Task<OperationResult<SessionView>> resultTask)
    {
        OperationResult<SessionView> result = await resultTask;
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : "");
    }
}
