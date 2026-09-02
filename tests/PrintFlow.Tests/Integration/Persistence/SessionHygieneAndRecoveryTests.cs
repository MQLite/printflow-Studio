using System.IO;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Adapters.Fake;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Persistence;

/// <summary>
/// What repeated production use leaves behind, and whether the next job can safely begin
/// (Epic 11600 Part A §5, §9, §10, §11, §12).
/// </summary>
/// <remarks>
/// The invariant this file exists to state is a sequence, not a snapshot:
/// <code>
/// one operation ends → the system reaches a known safe boundary → the next may begin
/// </code>
/// So every test here runs <b>two</b> pieces of work through <b>one</b> service instance
/// against <b>one</b> workspace and database, which is what a shop's second job of the morning
/// actually is. A test that built a fresh harness per job would prove nothing about hygiene,
/// because the state under suspicion is exactly the state a fresh harness discards.
/// <para>
/// Driven through the deterministic fake adapters, deliberately. The rules below are about what
/// <c>SessionService</c> persists and which session may consume which file — they are the same
/// rules whichever adapter returned the bytes, and stating them against the seam both adapters
/// share is what keeps them true of production rather than of one implementation. What the real
/// applications retain between jobs is a different question, proved against the real adapters in
/// <c>ExternalStateHygieneTests</c>, and on the workstation by the controlled recovery smoke.
/// </para>
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class SessionHygieneAndRecoveryTests
{
    // -------------------------------------------------------------------------------------
    // §5 — successful-operation hygiene: one job then the next, in one process
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Two Photoshop jobs run back to back both reach ReviewRequired, each holding only its own
    /// output (§5).
    /// </summary>
    /// <remarks>
    /// The assertion that matters is the one about paths. Both sessions ask for a TIFF, both
    /// render a name from the same naming contract, and B's output name is a deliberate near-miss
    /// of A's — so a pipeline that selected a result by resemblance rather than by the reference
    /// its own attempt reserved would produce two Revisions pointing into one directory, and only
    /// a path comparison catches it.
    /// </remarks>
    [Fact]
    public async Task Two_sequential_Photoshop_jobs_reach_ReviewRequired_holding_only_their_own_output()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId a = await PhotoshopJobAsync(harness, service, "seq-job", "a.png");
        SessionId b = await PhotoshopJobAsync(harness, service, "seq-job-2", "b.png");

        a.ShouldNotBe(b);

        SessionAggregate first = await LoadAsync(harness, a);
        SessionAggregate second = await LoadAsync(harness, b);

        StateOf(first, StepKind.PhotoshopOutput).ShouldBe(StepState.ReviewRequired);
        StateOf(second, StepKind.PhotoshopOutput).ShouldBe(StepState.ReviewRequired);

        Revision firstTiff = OutputRevision(first);
        Revision secondTiff = OutputRevision(second);

        // Different files, on disk, under their own session directories. Compared against each
        // session's own workspace reference rather than against its id: the directory name is a
        // rendered timestamp plus a short suffix, so an id substring would be asserting against
        // a naming detail instead of against the boundary that actually separates the two.
        firstTiff.File.RelativePath.ShouldNotBe(secondTiff.File.RelativePath);
        firstTiff.File.RelativePath.ShouldStartWith(first.Session.Workspace.RelativePath);
        secondTiff.File.RelativePath.ShouldStartWith(second.Session.Workspace.RelativePath);
        secondTiff.File.RelativePath.ShouldNotStartWith(first.Session.Workspace.RelativePath);
        File.Exists(harness.FileWorkspace.ResolveAbsolute(firstTiff.File)).ShouldBeTrue();
        File.Exists(harness.FileWorkspace.ResolveAbsolute(secondTiff.File)).ShouldBeTrue();

        // Neither session's records mention the other's work.
        second.Attempts.ShouldAllBe(attempt => attempt.SessionId == b);
        second.Revisions.ShouldAllBe(revision => revision.SessionId == b);
        second.Revisions.ShouldNotContain(revision => revision.File.RelativePath == firstTiff.File.RelativePath);
        second.Outputs.Count.ShouldBe(1);
        first.Outputs.Count.ShouldBe(1);
    }

    /// <summary>Two Meitu jobs run back to back succeed independently (§5).</summary>
    [Fact]
    public async Task Two_sequential_Meitu_jobs_succeed_independently()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId a = await MeituJobAsync(harness, service, "meitu-a", "ma.png");
        SessionId b = await MeituJobAsync(harness, service, "meitu-b", "mb.png");

        SessionAggregate first = await LoadAsync(harness, a);
        SessionAggregate second = await LoadAsync(harness, b);

        Revision firstEnhanced = first.Revisions.Single(r => r.Operation == OperationKind.Enhance);
        Revision secondEnhanced = second.Revisions.Single(r => r.Operation == OperationKind.Enhance);

        firstEnhanced.File.RelativePath.ShouldNotBe(secondEnhanced.File.RelativePath);
        secondEnhanced.File.RelativePath.ShouldStartWith(second.Session.Workspace.RelativePath);
        secondEnhanced.File.RelativePath.ShouldNotStartWith(first.Session.Workspace.RelativePath);

        // Each enhancement descends from its own session's confirmed original, never from the
        // previous session's result.
        secondEnhanced.SourceRevisionId.ShouldBe(second.Revisions.First(r => r.Operation == OperationKind.Import).Id);
        second.Attempts.ShouldAllBe(attempt => attempt.SessionId == b);
    }

    /// <summary>
    /// A Photoshop job and a Meitu job run back to back, in both orders, without either
    /// adapter's residue reaching the other (§5).
    /// </summary>
    /// <remarks>
    /// A theory rather than two tests because the property is order-independence itself: the
    /// claim "state left by one adapter does not interfere with the other" is only interesting
    /// when the same code proves it both ways round.
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_Photoshop_job_and_a_Meitu_job_do_not_interfere_in_either_order(bool photoshopFirst)
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId photoshop;
        SessionId meitu;

        if (photoshopFirst)
        {
            photoshop = await PhotoshopJobAsync(harness, service, "cross-tiff", "c1.png");
            meitu = await MeituJobAsync(harness, service, "cross-png", "c2.png");
        }
        else
        {
            meitu = await MeituJobAsync(harness, service, "cross-png", "c2.png");
            photoshop = await PhotoshopJobAsync(harness, service, "cross-tiff", "c1.png");
        }

        SessionAggregate tiffSession = await LoadAsync(harness, photoshop);
        SessionAggregate pngSession = await LoadAsync(harness, meitu);

        StateOf(tiffSession, StepKind.PhotoshopOutput).ShouldBe(StepState.ReviewRequired);
        StateOf(pngSession, StepKind.Enhancement).ShouldBe(StepState.Approved);

        // The Photoshop session produced a PrintOutput; the Meitu session did not, whichever
        // ran first. A PrintOutput leaking across would be the clearest possible cross-session
        // contamination, because it is the artefact a shop actually prints.
        tiffSession.Outputs.Count.ShouldBe(1);
        pngSession.Outputs.ShouldBeEmpty();

        tiffSession.Attempts.ShouldAllBe(a => a.SessionId == photoshop);
        pngSession.Attempts.ShouldAllBe(a => a.SessionId == meitu);

        // And the lock is free after the pair, so a third job could start.
        (await harness.Repository.GetAutomationLockAsync(CancellationToken.None)).Value.IsHeld.ShouldBeFalse();
    }

    // -------------------------------------------------------------------------------------
    // §4 — session isolation, by path identity and persisted state
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Every mutable artefact one session owns lives under that session's own directory, and no
    /// other session's records refer to it (§4).
    /// </summary>
    /// <remarks>
    /// Stated over the whole artefact set rather than over the output alone. §4 lists working
    /// files, revisions, attempt artefacts and evidence paths separately because a leak through
    /// any one of them is a leak; asserting only on the interesting file would leave the other
    /// routes untested.
    /// <para>
    /// Both sessions are given the <i>same</i> operator-facing output name on purpose. That is
    /// the realistic collision — two customers' jobs called the same thing on the same morning —
    /// and it makes filename similarity useless as an isolation mechanism, which is exactly §4's
    /// requirement.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Identically_named_sessions_share_no_artefact_path_and_no_record()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId a = await MeituJobAsync(harness, service, "same-name", "one.png");
        SessionId b = await MeituJobAsync(harness, service, "same-name", "two.png");

        SessionAggregate first = await LoadAsync(harness, a);
        SessionAggregate second = await LoadAsync(harness, b);

        HashSet<string> firstPaths = ArtefactPathsOf(first);
        HashSet<string> secondPaths = ArtefactPathsOf(second);

        firstPaths.ShouldNotBeEmpty();
        secondPaths.ShouldNotBeEmpty();
        firstPaths.Intersect(secondPaths, StringComparer.OrdinalIgnoreCase).ShouldBeEmpty();

        // The session directory is the boundary, and it is a directory identity rather than a
        // name prefix: every path each session owns sits under its own workspace reference.
        foreach (string path in firstPaths)
        {
            path.ShouldStartWith(first.Session.Workspace.RelativePath);
        }

        foreach (string path in secondPaths)
        {
            path.ShouldStartWith(second.Session.Workspace.RelativePath);
        }

        first.Session.Workspace.RelativePath.ShouldNotBe(second.Session.Workspace.RelativePath);
    }

    /// <summary>
    /// Session B's work leaves every byte and every record of session A exactly as it was (§4).
    /// </summary>
    /// <remarks>
    /// Digests of A's files before and after, rather than a "the file still exists" check: a
    /// second session overwriting the first session's result in place would pass an existence
    /// test and is precisely the contamination that would send the wrong artwork to print.
    /// </remarks>
    [Fact]
    public async Task Running_a_second_session_mutates_nothing_the_first_session_owns()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId a = await PhotoshopJobAsync(harness, service, "untouched", "u1.png");
        SessionAggregate before = await LoadAsync(harness, a);

        Dictionary<string, Sha256> digestsBefore = DigestsOf(harness, before);
        digestsBefore.ShouldNotBeEmpty();

        await PhotoshopJobAsync(harness, service, "untouched-too", "u2.png");

        SessionAggregate after = await LoadAsync(harness, a);
        DigestsOf(harness, after).ShouldBe(digestsBefore);

        // The persisted record is untouched too: same revisions, same attempts, same step state.
        after.Revisions.Select(r => r.Id).ShouldBe(before.Revisions.Select(r => r.Id));
        after.Attempts.Select(r => r.Id).ShouldBe(before.Attempts.Select(r => r.Id));
        StateOf(after, StepKind.PhotoshopOutput).ShouldBe(StateOf(before, StepKind.PhotoshopOutput));
    }

    // -------------------------------------------------------------------------------------
    // §9, §10 — a producing step that ends in an unhandled fault
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// A step whose work throws closes its attempt, releases the automation lock and reports a
    /// stable failure instead of escaping (§9, §10).
    /// </summary>
    /// <remarks>
    /// Before Epic 11600 Part A the exception unwound out of <c>ExecuteAsync</c> altogether,
    /// leaving the attempt frozen at <c>Running</c>, the step frozen at <c>Processing</c> and the
    /// automation lock held — by a process still alive, so startup recovery's liveness check
    /// would refuse to release it. Every later adapter-backed step, in every session, was then
    /// blocked by a run that had ended.
    /// <para>
    /// The failure is checked for its <i>message key</i> as well as its code. It shares
    /// <c>AdapterUnavailable</c> with a genuinely missing application, and the operator's next
    /// move differs: "the required application is unavailable" sends them to an installation
    /// that is fine, when what they need to do is look at what Photoshop or Meitu is showing.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task An_unhandled_fault_closes_the_attempt_and_releases_the_automation_lock()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateServiceWithMeitu(new FaultingMeituProcessor(harness));

        SessionId id = await ReadyForEnhancementAsync(harness, service, "faulted", "f.png");

        OperationResult<SessionView> started = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None);

        // Reported, not thrown.
        started.IsFailure.ShouldBeTrue();
        started.Failure.Code.ShouldBe(FailureCode.AdapterUnavailable);
        started.Failure.MessageKey.ShouldBe("Failure_OperationFaulted");
        started.Failure.Context["faultType"].ShouldBe(typeof(InvalidOperationException).FullName);
        started.Failure.Context["revisionCreated"].ShouldBe("false");
        started.Failure.Context["retainedExternalState"].ShouldBe("unknown");

        // The exception's own message survives into the immutable attempt record rather than
        // being swallowed by the containment.
        SessionAggregate aggregate = await LoadAsync(harness, id);
        ProcessingAttempt attempt = aggregate.Attempts.Single(a => a.Step == StepKind.Enhancement);
        attempt.Status.ShouldBe(AttemptStatus.Failed);
        attempt.OutputRevisionId.ShouldBeNull();
        attempt.Failure!.TechnicalDetail.ShouldContain(FaultingMeituProcessor.Message);

        StateOf(aggregate, StepKind.Enhancement).ShouldBe(StepState.Failed);
        aggregate.Revisions.ShouldNotContain(r => r.Operation == OperationKind.Enhance);

        (await harness.Repository.GetAutomationLockAsync(CancellationToken.None)).Value.IsHeld.ShouldBeFalse();
    }

    /// <summary>
    /// A fault in one session does not stop the next session from taking the automation lock
    /// (§9).
    /// </summary>
    /// <remarks>
    /// The consequence that makes the containment worth having, and it is deliberately proved
    /// with a <i>different</i> session: a lock left held by session A blocks B with
    /// "Meitu/Photoshop is already controlled by session A", which no retry inside A would ever
    /// reveal.
    /// </remarks>
    [Fact]
    public async Task A_faulted_job_does_not_block_the_next_session_from_running()
    {
        using SessionServiceHarness harness = new();

        FaultingMeituProcessor faulting = new(harness);
        ISessionService faultingService = harness.CreateServiceWithMeitu(faulting);
        SessionId faulted = await ReadyForEnhancementAsync(harness, faultingService, "fault-first", "g1.png");

        (await faultingService.ExecuteAsync(
            faulted, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None))
            .IsFailure.ShouldBeTrue();

        // A second session, through the ordinary fake adapter, runs to a normal success.
        ISessionService service = harness.CreateService();
        SessionId next = await MeituJobAsync(harness, service, "fault-next", "g2.png");

        SessionAggregate aggregate = await LoadAsync(harness, next);
        StateOf(aggregate, StepKind.Enhancement).ShouldBe(StepState.Approved);
        aggregate.Attempts.Single(a => a.Step == StepKind.Enhancement).Status.ShouldBe(AttemptStatus.Succeeded);
    }

    /// <summary>
    /// A retry after a fault is a new, distinguishable attempt that can succeed, and the failed
    /// one stays in history (§10).
    /// </summary>
    [Fact]
    public async Task A_retry_after_a_fault_is_a_new_attempt_and_the_fault_stays_in_history()
    {
        using SessionServiceHarness harness = new();
        FaultingMeituProcessor meitu = new(harness);
        ISessionService service = harness.CreateServiceWithMeitu(meitu);

        SessionId id = await ReadyForEnhancementAsync(harness, service, "retry-after-fault", "r.png");

        (await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None))
            .IsFailure.ShouldBeTrue();

        AttemptId faultedAttempt = (await LoadAsync(harness, id))
            .Attempts.Single(a => a.Step == StepKind.Enhancement).Id;

        // The condition clears — an operator restored whatever was wrong — and the same session
        // retries through the same service instance.
        meitu.StopFaulting();

        await RetryStepAsync(service, id, StepKind.Enhancement);

        SessionAggregate aggregate = await LoadAsync(harness, id);
        List<ProcessingAttempt> attempts = [.. aggregate.Attempts.Where(a => a.Step == StepKind.Enhancement)];

        attempts.Count.ShouldBe(2);
        attempts.Single(a => a.Id == faultedAttempt).Status.ShouldBe(AttemptStatus.Failed);

        ProcessingAttempt retry = attempts.Single(a => a.Id != faultedAttempt);
        retry.Status.ShouldBe(AttemptStatus.Succeeded);
        retry.RetrySequence.ShouldBe(1);
        retry.RetryOfAttemptId.ShouldBe(faultedAttempt);
        retry.OutputRevisionId.ShouldNotBeNull();

        // The retry's output is written under its own attempt folder, so it cannot be the
        // half-written leftover of the attempt that faulted.
        Revision produced = aggregate.Revisions.Single(r => r.Id == retry.OutputRevisionId!.Value);
        produced.File.RelativePath.ShouldContain(retry.Id.Value.ToString("D"));
        produced.File.RelativePath.ShouldNotContain(faultedAttempt.Value.ToString("D"));
    }

    /// <summary>
    /// A fault after an earlier approved result leaves that result exactly as it was (§10).
    /// </summary>
    /// <remarks>
    /// The rule that stops a broken second run from costing an operator a good first one. The
    /// approved artefact is compared by digest, not by existence, for the same reason as the
    /// cross-session test above.
    /// </remarks>
    [Fact]
    public async Task A_fault_never_overwrites_an_already_approved_result()
    {
        using SessionServiceHarness harness = new();
        FaultingMeituProcessor meitu = new(harness) { Only = MeituOperation.RemoveBackground };
        ISessionService service = harness.CreateServiceWithMeitu(meitu);

        SessionId id = await ReadyForEnhancementAsync(harness, service, "keep-approved", "k.png");
        await RunAndApproveAsync(service, id, StepKind.Enhancement);

        SessionAggregate approved = await LoadAsync(harness, id);
        Revision enhancement = approved.Revisions.Single(r => r.Operation == OperationKind.Enhance);
        Sha256 before = DigestOf(harness, enhancement.File);

        await SessionServiceHarness.AuthoriseBackgroundRemovalAsync(service, id);
        (await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.BackgroundRemoval), "tester", CancellationToken.None))
            .IsFailure.ShouldBeTrue();

        SessionAggregate after = await LoadAsync(harness, id);

        // The approved upstream artefact and its Revision are untouched, and the step it belongs
        // to is still Approved rather than having been dragged down by the failure below it.
        DigestOf(harness, enhancement.File).ShouldBe(before);
        after.Revisions.Single(r => r.Id == enhancement.Id).Facts.Sha256.ShouldBe(enhancement.Facts.Sha256);
        StateOf(after, StepKind.Enhancement).ShouldBe(StepState.Approved);
        StateOf(after, StepKind.BackgroundRemoval).ShouldBe(StepState.Failed);
    }

    // -------------------------------------------------------------------------------------
    // §11 — partial output never becomes a result
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// No shape of incomplete output reaches ReviewRequired or becomes a Revision (§11).
    /// </summary>
    /// <remarks>
    /// One theory over the whole family rather than a test per shape, because the property is
    /// the family: validation is what decides, so "zero bytes", "nothing at all" and "the adapter
    /// gave up" must all end the same way. A shape that only failed because a different guard
    /// happened to catch it would still be a shape validation had not covered.
    /// </remarks>
    [Theory]
    [InlineData(FakeAdapterScenarioKind.ProduceUnreadableFile)]
    [InlineData(FakeAdapterScenarioKind.ProduceMissingFile)]
    [InlineData(FakeAdapterScenarioKind.Timeout)]
    public async Task No_incomplete_output_reaches_ReviewRequired(FakeAdapterScenarioKind kind)
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReadyForPhotoshopAsync(harness, service, "partial", "p.png");

        harness.FakePhotoshop.SetScenario(new FakeAdapterScenario(kind));

        OperationResult<SessionView> started = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", CancellationToken.None);
        started.IsFailure.ShouldBeTrue();

        SessionAggregate aggregate = await LoadAsync(harness, id);

        StateOf(aggregate, StepKind.PhotoshopOutput).ShouldNotBe(StepState.ReviewRequired);
        StateOf(aggregate, StepKind.PhotoshopOutput).ShouldBe(StepState.Failed);
        aggregate.Revisions.ShouldNotContain(r => r.Operation == OperationKind.PhotoshopOutput);
        aggregate.Outputs.ShouldBeEmpty();
        aggregate.Attempts.Single(a => a.Step == StepKind.PhotoshopOutput)
            .OutputRevisionId.ShouldBeNull();

        (await harness.Repository.GetAutomationLockAsync(CancellationToken.None)).Value.IsHeld.ShouldBeFalse();
    }

    /// <summary>
    /// A retry after a partial write produces its own file and never adopts the leftover (§11).
    /// </summary>
    /// <remarks>
    /// The zero-byte scenario leaves a genuine file on disk at a genuine reserved path, which is
    /// what makes this worth asserting: the leftover is still there when the retry runs, and the
    /// only thing keeping the retry from finding it is that the new attempt reserves a
    /// destination of its own.
    /// </remarks>
    [Fact]
    public async Task A_retry_after_a_partial_write_never_adopts_the_leftover_file()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReadyForPhotoshopAsync(harness, service, "leftover", "l.png");

        harness.FakePhotoshop.SetScenario(FakeAdapterScenario.ProduceUnreadableFile);
        (await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", CancellationToken.None))
            .IsFailure.ShouldBeTrue();

        AttemptId partialAttempt = (await LoadAsync(harness, id))
            .Attempts.Single(a => a.Step == StepKind.PhotoshopOutput).Id;

        // The half-written artefact is genuinely on disk under the failed attempt's folder.
        string partialFolder = Path.Combine(
            harness.FileWorkspace.ResolveAbsoluteDirectory(
                (await LoadAsync(harness, id)).Session.Workspace),
            "Working",
            partialAttempt.Value.ToString("D"));
        Directory.Exists(partialFolder).ShouldBeTrue();
        string[] leftovers = Directory.GetFiles(partialFolder);
        leftovers.ShouldNotBeEmpty();

        harness.FakePhotoshop.SetScenario(FakeAdapterScenario.Succeed);
        await RetryStepAsync(service, id, StepKind.PhotoshopOutput);

        SessionAggregate aggregate = await LoadAsync(harness, id);
        Revision produced = OutputRevision(aggregate);

        produced.File.RelativePath.ShouldNotContain(partialAttempt.Value.ToString("D"));
        harness.FileWorkspace.ResolveAbsolute(produced.File)
            .ShouldNotBeOneOf([.. leftovers]);
        new FileInfo(harness.FileWorkspace.ResolveAbsolute(produced.File)).Length.ShouldBeGreaterThan(0);

        // The leftover is still exactly where it was: nothing about the retry deleted, moved or
        // promoted it, which is the existing retention contract rather than a new one.
        Directory.GetFiles(partialFolder).ShouldBe(leftovers);
    }

    // -------------------------------------------------------------------------------------
    // §12 — restart preserves truthful state
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// A completed ReviewRequired session survives a restart intact, and can still be approved
    /// by the process that came after (§12).
    /// </summary>
    /// <remarks>
    /// The second service instance shares nothing with the first but the database and the files,
    /// which is what reopening the application actually is. Approving through it is the part that
    /// matters: a restart that restored a session which could be read but not acted on would have
    /// preserved a picture of the work rather than the work.
    /// </remarks>
    [Fact]
    public async Task A_restart_preserves_a_ReviewRequired_session_and_it_can_still_be_approved()
    {
        using SessionServiceHarness harness = new();
        SessionId id = await PhotoshopJobAsync(harness, harness.CreateService(), "restart-ok", "ro.png");

        SessionAggregate before = await LoadAsync(harness, id);
        Revision tiff = OutputRevision(before);
        Sha256 digestBefore = DigestOf(harness, tiff.File);

        // A brand-new process: recovery runs first, exactly as ApplicationStartup orders it.
        StartupRecoveryReport report = await RecoverAsync(harness, new FakeProcessLiveness(ProcessLiveness.Dead));
        report.InterruptedAttemptCount.ShouldBe(0);

        ISessionService restarted = harness.CreateService();
        SessionAggregate after = await LoadAsync(harness, id);

        StateOf(after, StepKind.PhotoshopOutput).ShouldBe(StepState.ReviewRequired);
        after.Revisions.Select(r => r.Id).ShouldBe(before.Revisions.Select(r => r.Id));
        after.Attempts.Select(a => (a.Id, a.Status)).ShouldBe(before.Attempts.Select(a => (a.Id, a.Status)));
        after.Outputs.Single().Sha256.ShouldBe(before.Outputs.Single().Sha256);

        // The validated output is still the validated output, byte for byte, and recovery did
        // not quarantine it as a leftover.
        DigestOf(harness, tiff.File).ShouldBe(digestBefore);

        OperationResult<SessionView> approved = await restarted.ExecuteAsync(
            id, new WorkflowCommand.Approve(StepKind.PhotoshopOutput, tiff.Facts.Sha256),
            "tester", CancellationToken.None);
        approved.IsSuccess.ShouldBeTrue(approved.IsFailure ? approved.Failure.ToString() : "");
    }

    /// <summary>
    /// A restart after a fault does not turn the failure into anything else, and the retry that
    /// follows it is a clean new attempt (§12).
    /// </summary>
    [Fact]
    public async Task A_restart_after_a_fault_keeps_the_failure_and_the_next_attempt_is_clean()
    {
        using SessionServiceHarness harness = new();
        FaultingMeituProcessor meitu = new(harness);

        SessionId id = await ReadyForEnhancementAsync(
            harness, harness.CreateServiceWithMeitu(meitu), "restart-fault", "rf.png");

        (await harness.CreateServiceWithMeitu(meitu).ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None))
            .IsFailure.ShouldBeTrue();

        AttemptId faulted = (await LoadAsync(harness, id))
            .Attempts.Single(a => a.Step == StepKind.Enhancement).Id;

        // Restart. Nothing was left Running, so recovery has nothing to correct — and must not
        // invent anything either.
        StartupRecoveryReport report = await RecoverAsync(harness, new FakeProcessLiveness(ProcessLiveness.Dead));
        report.InterruptedAttemptCount.ShouldBe(0);
        report.ReleasedAutomationLock.ShouldBeFalse();

        SessionAggregate after = await LoadAsync(harness, id);
        after.Attempts.Single(a => a.Id == faulted).Status.ShouldBe(AttemptStatus.Failed);
        StateOf(after, StepKind.Enhancement).ShouldBe(StepState.Failed);
        after.Revisions.ShouldNotContain(r => r.Operation == OperationKind.Enhance);

        // The next process retries successfully, and the retry is distinguishable from the
        // attempt the previous process failed.
        ISessionService restarted = harness.CreateService();
        await RetryStepAsync(restarted, id, StepKind.Enhancement);

        SessionAggregate final = await LoadAsync(harness, id);
        final.Attempts.Count(a => a.Step == StepKind.Enhancement).ShouldBe(2);
        final.Attempts.Single(a => a.Id == faulted).Status.ShouldBe(AttemptStatus.Failed);
        final.Attempts.Single(a => a.Id != faulted && a.Step == StepKind.Enhancement)
            .Status.ShouldBe(AttemptStatus.Succeeded);
    }

    // -------------------------------------------------------------------------------------
    // §14, §15 — what a restart may and may not do
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Startup recovery deletes no session, no working file and no failed attempt when there was
    /// no crash to recover from (§15).
    /// </summary>
    /// <remarks>
    /// The negative half of §15, and it needs stating precisely because recovery <i>is</i>
    /// allowed to move files: the licence is confined to leftovers of attempts this pass actually
    /// interrupted. A startup that found nothing to recover has no business touching anything, and
    /// the two sessions here — one finished, one failed with a real leftover on disk — are the two
    /// things a tidying startup would reach for first.
    /// </remarks>
    [Fact]
    public async Task A_startup_with_nothing_to_recover_deletes_and_quarantines_nothing()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId finished = await PhotoshopJobAsync(harness, service, "keep-me", "km.png");

        SessionId failed = await ReadyForPhotoshopAsync(harness, service, "keep-me-too", "kmt.png");
        harness.FakePhotoshop.SetScenario(FakeAdapterScenario.ProduceUnreadableFile);
        (await service.ExecuteAsync(
            failed, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", CancellationToken.None))
            .IsFailure.ShouldBeTrue();

        string[] filesBefore = WorkspaceFileList(harness);
        int sessionsBefore = Directory.GetDirectories(Path.Combine(harness.Workspace.Root, "Sessions")).Length;

        StartupRecoveryReport report = await RecoverAsync(harness, new FakeProcessLiveness(ProcessLiveness.Dead));

        report.Entries.ShouldNotContain(e => e.Action == StartupRecoveryAction.WorkingFileQuarantined);
        report.InterruptedAttemptCount.ShouldBe(0);

        WorkspaceFileList(harness).ShouldBe(filesBefore);
        Directory.GetDirectories(Path.Combine(harness.Workspace.Root, "Sessions")).Length
            .ShouldBe(sessionsBefore);

        SessionAggregate failedAfter = await LoadAsync(harness, failed);
        failedAfter.Attempts.Single(a => a.Step == StepKind.PhotoshopOutput)
            .Status.ShouldBe(AttemptStatus.Failed);
        StateOf(await LoadAsync(harness, finished), StepKind.PhotoshopOutput)
            .ShouldBe(StepState.ReviewRequired);
    }

    /// <summary>
    /// A restart releases a lock whose PrintFlow owner is gone, whatever the external
    /// applications are doing (§14).
    /// </summary>
    /// <remarks>
    /// The ownership question §14 turns on. The lock records the PrintFlow process that took it,
    /// and that is the only thing liveness is asked about — so a Photoshop or Meitu that happens
    /// to still be running across the restart neither keeps the lock alive nor resurrects the
    /// session that held it. Nothing in the recovery path can even name an external application,
    /// which is why this is asserted about the released lock rather than about a process count.
    /// </remarks>
    [Fact]
    public async Task A_lock_left_by_a_dead_PrintFlow_process_is_released_on_the_next_start()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId id = await ReadyForEnhancementAsync(harness, service, "stale-lock", "sl.png");

        harness.FakeMeitu.SetScenario(FakeAdapterScenario.HangUntilCancelled);
        _ = service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None);
        await harness.FakeMeitu.HangStarted;

        AutomationLockState held = (await harness.Repository.GetAutomationLockAsync(CancellationToken.None)).Value;
        held.IsHeld.ShouldBeTrue();
        held.SessionId.ShouldBe(id);

        FakeProcessLiveness liveness = new(ProcessLiveness.Dead);
        StartupRecoveryReport report = await RecoverAsync(harness, liveness);

        // Liveness was asked about the PrintFlow process the lock names, not about anything else.
        liveness.LastProcessId.ShouldBe(held.ProcessId);
        report.ReleasedAutomationLock.ShouldBeTrue();

        (await harness.Repository.GetAutomationLockAsync(CancellationToken.None)).Value.IsHeld.ShouldBeFalse();

        // And the new process can take the lock and finish the work.
        harness.FakeMeitu.SetScenario(FakeAdapterScenario.Succeed);
        ISessionService restarted = harness.CreateService();
        await RetryStepAsync(restarted, id, StepKind.Enhancement);
    }

    // -------------------------------------------------------------------------------------
    // Fakes
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// A Meitu adapter that throws rather than returning a failure (Epic 11600 Part A §9, §10).
    /// </summary>
    /// <remarks>
    /// Not a <see cref="FakeAdapterScenario"/>, on purpose. Every scenario the fake adapters
    /// script is a <i>reported</i> outcome, which is the contract adapters are written to; what
    /// is under test here is the containment for an adapter that breaks that contract, and a
    /// scenario kind meaning "throw" would invite the fakes to be used as though throwing were
    /// an ordinary result.
    /// <para>
    /// It declares <see cref="AdapterExecutionMode.Fake"/> so the harness's real
    /// <c>UnverifiedEnvironmentGate</c> admits it: the gate is not what is being tested, and a
    /// Production-mode double would be refused before it could throw.
    /// </para>
    /// </remarks>
    private sealed class FaultingMeituProcessor(SessionServiceHarness harness) : IMeituProcessor
    {
        internal const string Message = "the adapter broke in a way it has no result for";

        private bool _faulting = true;

        /// <summary>Restricts the fault to one operation, leaving the other one working.</summary>
        /// <remarks>
        /// Needed by the "an approved result survives a later fault" test, which has to reach a
        /// successful Enhancement before the fault it is about can happen at all.
        /// </remarks>
        public MeituOperation? Only { get; init; }

        public string AdapterId => "faulting-meitu-v1";

        public AdapterExecutionMode Mode => AdapterExecutionMode.Fake;

        /// <summary>Models the operator having restored whatever the fault was about.</summary>
        public void StopFaulting() => _faulting = false;

        public Task<OperationResult<AdapterOutput>> ProcessAsync(
            MeituRequest request, CancellationToken cancellationToken)
        {
            bool applies = Only is null || Only == request.Operation;
            return _faulting && applies
                ? throw new InvalidOperationException(Message)

                // Delegating to the harness's own fake keeps the non-faulting path a real run
                // against the real workspace, so a retry after a fault produces an actual file
                // rather than a scripted claim about one.
                : harness.FakeMeitu.ProcessAsync(request, cancellationToken);
        }
    }

    // -------------------------------------------------------------------------------------
    // Epic 11600 Part B §19 — what sustained repetition has to keep being true
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Twelve sequential Photoshop jobs each hold only their own output, and none of the earlier
    /// eleven changes while the later ones run (Epic 11600 Part B §4 Stage&#160;B, §19).
    /// </summary>
    /// <remarks>
    /// The two-job version of this claim is above; this is the one Stage&#160;B actually makes.
    /// Two jobs can stay separate by accident — there is only one other session to collide with.
    /// Twelve, all asking for a TIFF, all rendering names from the same contract, and all leaving
    /// their working documents open in the same Photoshop, is where a pipeline that resolved
    /// anything by name rather than by the reference its own attempt reserved would finally
    /// produce two Revisions pointing at one file.
    /// <para>
    /// The digest sweep at the end is the part that could not be inferred from the path
    /// assertions: identical paths would prove nothing was <i>overwritten</i> only if the bytes
    /// were also checked, and a later job writing through an earlier job's reference is exactly
    /// the failure that leaves the path list intact.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Twelve_sequential_Photoshop_jobs_each_hold_only_their_own_output()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        List<SessionId> sessions = [];
        Dictionary<string, Sha256> digests = new(StringComparer.Ordinal);

        for (int job = 1; job <= 12; job++)
        {
            // Deliberately near-identical operator-facing names: filename similarity must be
            // useless as a separator.
            SessionId id = await PhotoshopJobAsync(harness, service, $"soak-job-{job}", $"soak{job}.png");
            sessions.Add(id);

            SessionAggregate aggregate = await LoadAsync(harness, id);
            StateOf(aggregate, StepKind.PhotoshopOutput).ShouldBe(StepState.ReviewRequired);

            Revision tiff = OutputRevision(aggregate);
            tiff.File.RelativePath.ShouldStartWith(aggregate.Session.Workspace.RelativePath);
            digests.ShouldNotContainKey(tiff.File.RelativePath,
                $"job {job} selected an output path an earlier session already owns.");
            digests[tiff.File.RelativePath] = DigestOf(harness, tiff.File);
        }

        sessions.Distinct().Count().ShouldBe(12);

        // Every earlier output is still byte-for-byte what it was when it was written, after
        // eleven further jobs went through the same Photoshop and the same workspace.
        foreach (SessionId id in sessions)
        {
            SessionAggregate aggregate = await LoadAsync(harness, id);
            Revision tiff = OutputRevision(aggregate);

            DigestOf(harness, tiff.File).ShouldBe(digests[tiff.File.RelativePath],
                $"session {id} output changed while later jobs ran.");
            aggregate.Attempts.ShouldAllBe(attempt => attempt.SessionId == id);
            aggregate.Revisions.ShouldAllBe(revision => revision.SessionId == id);
            aggregate.Outputs.Count.ShouldBe(1);
        }
    }

    /// <summary>
    /// A restart with many historical <c>ReviewRequired</c> jobs mutates none of them
    /// (Epic 11600 Part B §14, §19).
    /// </summary>
    /// <remarks>
    /// Part A proved this for one finished session. The soak's restart happens on top of
    /// thirty-two, which is a different question: recovery walks what it finds, and a pass that
    /// treated a completed attempt as a leftover would do it to all of them at once. So the
    /// assertion is over the whole population — every state, every revision id, every attempt
    /// status and every output digest — plus a whole-workspace file census, because "nothing
    /// changed" has to include "nothing was quarantined or moved either".
    /// </remarks>
    [Fact]
    public async Task A_restart_with_many_historical_ReviewRequired_jobs_mutates_none_of_them()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        List<SessionId> sessions = [];
        for (int job = 1; job <= 8; job++)
        {
            sessions.Add(await PhotoshopJobAsync(harness, service, $"restart-many-{job}", $"rm{job}.png"));
        }

        Dictionary<SessionId, SessionAggregate> before = [];
        Dictionary<SessionId, Dictionary<string, Sha256>> digestsBefore = [];
        foreach (SessionId id in sessions)
        {
            SessionAggregate aggregate = await LoadAsync(harness, id);
            before[id] = aggregate;
            digestsBefore[id] = DigestsOf(harness, aggregate);
        }

        string[] filesBefore = WorkspaceFileList(harness);

        // A brand-new process, recovery first, exactly as ApplicationStartup orders it.
        StartupRecoveryReport report = await RecoverAsync(harness, new FakeProcessLiveness(ProcessLiveness.Dead));

        report.InterruptedAttemptCount.ShouldBe(0, "eight completed jobs leave nothing to interrupt.");
        report.Entries.ShouldNotContain(e => e.Action == StartupRecoveryAction.WorkingFileQuarantined);
        report.Entries.ShouldNotContain(e => e.Action == StartupRecoveryAction.RecoveryFailed);

        foreach (SessionId id in sessions)
        {
            SessionAggregate after = await LoadAsync(harness, id);

            StateOf(after, StepKind.PhotoshopOutput).ShouldBe(StepState.ReviewRequired);
            after.Revisions.Select(r => r.Id).ShouldBe(before[id].Revisions.Select(r => r.Id));
            after.Attempts.Select(a => (a.Id, a.Status))
                .ShouldBe(before[id].Attempts.Select(a => (a.Id, a.Status)));
            DigestsOf(harness, after).ShouldBe(digestsBefore[id]);
        }

        // And startup was not destructive anywhere else either.
        WorkspaceFileList(harness).ShouldBe(filesBefore);
    }

    // -------------------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------------------

    /// <summary>Runs a whole GeneratePrintTiff job to ReviewRequired and returns its session.</summary>
    private static async Task<SessionId> PhotoshopJobAsync(
        SessionServiceHarness harness, ISessionService service, string outputName, string sourceFileName)
    {
        SessionId id = await ReadyForPhotoshopAsync(harness, service, outputName, sourceFileName);
        await MustAsync(service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", CancellationToken.None));
        return id;
    }

    private static async Task<SessionId> ReadyForPhotoshopAsync(
        SessionServiceHarness harness, ISessionService service, string outputName, string sourceFileName)
    {
        OperationResult<SessionView> imported = await service.ImportAsync(
            WorkflowType.GeneratePrintTiff,
            harness.WriteBorderedSourcePng(sourceFileName),
            outputName,
            "tester",
            CancellationToken.None);
        imported.IsSuccess.ShouldBeTrue(imported.IsFailure ? imported.Failure.ToString() : "");

        SessionId id = imported.Value.Id;
        await MustAsync(service.ExecuteAsync(
            id, new WorkflowCommand.ConfirmOriginal("finished design"), "tester", CancellationToken.None));
        await MustAsync(service.ExecuteAsync(
            id,
            new WorkflowCommand.SetPrintDimensions(PrintDimensions.FromMillimetres(200, 150, SizePreset.Custom)),
            "tester",
            CancellationToken.None));
        await MustAsync(service.ExecuteAsync(
            id,
            new WorkflowCommand.SelectWhiteUnderbaseBranch(WhiteUnderbaseBranch.W1_1px, "ordinary design"),
            "tester",
            CancellationToken.None));
        return id;
    }

    /// <summary>Runs a PrepareAsset enhancement to Approved and returns its session.</summary>
    private static async Task<SessionId> MeituJobAsync(
        SessionServiceHarness harness, ISessionService service, string outputName, string sourceFileName)
    {
        SessionId id = await ReadyForEnhancementAsync(harness, service, outputName, sourceFileName);
        await RunAndApproveAsync(service, id, StepKind.Enhancement);
        return id;
    }

    private static async Task<SessionId> ReadyForEnhancementAsync(
        SessionServiceHarness harness, ISessionService service, string outputName, string sourceFileName)
    {
        OperationResult<SessionView> imported = await service.ImportAsync(
            WorkflowType.PrepareAsset,
            harness.WriteSourcePng(sourceFileName),
            outputName,
            "tester",
            CancellationToken.None);
        imported.IsSuccess.ShouldBeTrue(imported.IsFailure ? imported.Failure.ToString() : "");

        SessionId id = imported.Value.Id;
        await MustAsync(service.ExecuteAsync(
            id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None));
        return id;
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

        Sha256 hash = started.Value.Steps.Single(s => s.Step == step).CurrentRevisionSha256!.Value;
        await MustAsync(service.ExecuteAsync(
            id, new WorkflowCommand.Approve(step, hash), "tester", CancellationToken.None));
    }

    /// <summary>Retries a step the way an operator does: back to Waiting, then started again.</summary>
    /// <remarks>
    /// Two commands rather than one because <c>Retry</c> is not a run. It clears the step's
    /// result and returns it to <c>Waiting</c>; the attempt that follows is an ordinary
    /// <c>StartStep</c>, which is what gives the retry its own attempt row, its own retry
    /// sequence and its own <c>Working\&lt;attemptId&gt;\</c> destination.
    /// </remarks>
    private static async Task RetryStepAsync(ISessionService service, SessionId id, StepKind step)
    {
        await MustAsync(service.ExecuteAsync(
            id, new WorkflowCommand.Retry(step), "tester", CancellationToken.None));
        await MustAsync(service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(step), "tester", CancellationToken.None));
    }

    private static async Task<StartupRecoveryReport> RecoverAsync(
        SessionServiceHarness harness, FakeProcessLiveness liveness)
    {
        OperationResult<StartupRecoveryReport> recovered =
            await harness.CreateRecoveryService(liveness).RecoverAsync(CancellationToken.None);
        recovered.IsSuccess.ShouldBeTrue(recovered.IsFailure ? recovered.Failure.ToString() : "");
        recovered.Value.Entries.ShouldNotContain(e => e.Action == StartupRecoveryAction.RecoveryFailed);
        return recovered.Value;
    }

    /// <summary>Every managed path a session's records refer to, as one set.</summary>
    private static HashSet<string> ArtefactPathsOf(SessionAggregate aggregate)
    {
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);

        foreach (Revision revision in aggregate.Revisions)
        {
            paths.Add(revision.File.RelativePath);
        }

        foreach (PrintOutput output in aggregate.Outputs)
        {
            paths.Add(output.File.RelativePath);
        }

        return paths;
    }

    private static Dictionary<string, Sha256> DigestsOf(SessionServiceHarness harness, SessionAggregate aggregate) =>
        aggregate.Revisions.ToDictionary(
            revision => revision.File.RelativePath,
            revision => DigestOf(harness, revision.File));

    private static Sha256 DigestOf(SessionServiceHarness harness, WorkspaceFileRef file) =>
        Sha256.FromBytes(System.Security.Cryptography.SHA256.HashData(
            File.ReadAllBytes(harness.FileWorkspace.ResolveAbsolute(file))));

    /// <summary>Every file under the workspace, ordered, for a before/after census.</summary>
    private static string[] WorkspaceFileList(SessionServiceHarness harness) =>
        [.. Directory.GetFiles(harness.Workspace.Root, "*", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(".db-wal", StringComparison.OrdinalIgnoreCase)
                        && !path.EndsWith(".db-shm", StringComparison.OrdinalIgnoreCase)
                        && !path.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal)];

    private static StepState StateOf(SessionAggregate aggregate, StepKind step) =>
        aggregate.Steps.Single(s => s.Step == step).State;

    private static Revision OutputRevision(SessionAggregate aggregate) =>
        aggregate.Revisions.Single(r => r.Operation == OperationKind.PhotoshopOutput);

    private static async Task<SessionAggregate> LoadAsync(SessionServiceHarness harness, SessionId id)
    {
        OperationResult<SessionAggregate?> loaded = await harness.Repository.LoadAsync(id, CancellationToken.None);
        loaded.IsSuccess.ShouldBeTrue(loaded.IsFailure ? loaded.Failure.ToString() : "");
        loaded.Value.ShouldNotBeNull();
        return loaded.Value;
    }

    private static async Task MustAsync(Task<OperationResult<SessionView>> pending)
    {
        OperationResult<SessionView> result = await pending;
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : "");
    }
}
