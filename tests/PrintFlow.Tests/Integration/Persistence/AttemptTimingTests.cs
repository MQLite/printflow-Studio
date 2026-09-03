using PrintFlow.Domain.Attempts;
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
using Shouldly;
using Xunit.Abstractions;

namespace PrintFlow.Tests.Integration.Persistence;

/// <summary>
/// What a persisted <see cref="ProcessingAttempt"/> is allowed to claim about when it ran
/// (SCRUM-11137 prerequisite §20, §21).
/// </summary>
/// <remarks>
/// Every test here reads the attempt back out of SQLite rather than asserting on the in-memory
/// record the service returned. The defect these tests exist for was invisible in memory — the
/// domain type had always been able to express two different instants — and only the persisted
/// row proves an attempt's duration can be trusted afterwards, which is the whole point of the
/// measurement SCRUM-11137 will build on it.
/// <para>
/// The clock is advanced <i>inside</i> the adapter call, so the closing transaction observes a
/// different instant from the opening one. Under the pre-fix code every one of these tests
/// reports <c>EndedAtUtc == StartedAtUtc</c>; the property being asserted is not "the two values
/// differ" but "the terminal value is the one the closing transition observed" (§15).
/// </para>
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class AttemptTimingTests(ITestOutputHelper output)
{
    private static readonly TimeSpan Work = TimeSpan.FromMinutes(3);

    // -----------------------------------------------------------------------------------
    // §20 — Running
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// A running attempt records when it started and claims nothing about finishing, however
    /// much time has passed since (§3, §20).
    /// </summary>
    /// <remarks>
    /// The one case where a fabricated terminal timestamp would be actively dangerous: a row
    /// carrying both timestamps while the adapter is still working would report a completed
    /// duration for work that has not completed, and no later reader could tell it apart from an
    /// attempt that genuinely finished.
    /// </remarks>
    [Fact]
    public async Task A_running_attempt_records_its_start_and_claims_no_end()
    {
        using SessionServiceHarness harness = new();
        ClockAdvancingPhotoshopProcessor photoshop = new(harness.FakePhotoshop, harness.Clock, Work);
        ISessionService service = harness.CreateServiceWithPhotoshop(photoshop);

        SessionId id = await ReadyForPhotoshopAsync(harness, service);
        DateTimeOffset t0 = harness.Clock.GetUtcNow();

        harness.FakePhotoshop.SetScenario(FakeAdapterScenario.HangUntilCancelled);
        using CancellationTokenSource cancellation = new();
        Task<OperationResult<SessionView>> running = service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", cancellation.Token);
        await harness.FakePhotoshop.HangStarted;

        ProcessingAttempt attempt = await PhotoshopAttemptAsync(harness, id);
        attempt.Status.ShouldBe(AttemptStatus.Running);
        attempt.StartedAtUtc.ShouldBe(t0);
        attempt.EndedAtUtc.ShouldBeNull();

        await cancellation.CancelAsync();
        (await running).IsFailure.ShouldBeTrue();
    }

    // -----------------------------------------------------------------------------------
    // §20 — Success
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// A successful attempt ends at the instant its terminal transaction observed, not at the
    /// instant it started (§6, §20).
    /// </summary>
    /// <remarks>
    /// The direct reproduction of the reported defect: three minutes of adapter work, and before
    /// the fix the persisted row said the attempt ended at 09:00:00 — the moment it began.
    /// <para>
    /// This is also the controlled readback §22 asks for, which is why it reports the three
    /// values rather than only asserting them. The run is synthetic throughout — a generated
    /// PNG through the Fake adapter — because re-running a real customer order to observe a
    /// timestamp is exactly what §22 rules out.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_successful_attempt_ends_at_the_closing_observation_not_the_opening_one()
    {
        using SessionServiceHarness harness = new();
        ClockAdvancingPhotoshopProcessor photoshop = new(harness.FakePhotoshop, harness.Clock, Work);
        ISessionService service = harness.CreateServiceWithPhotoshop(photoshop);

        DateTimeOffset t0 = harness.Clock.GetUtcNow();
        SessionId id = await ReachReviewRequiredAsync(harness, service);

        ProcessingAttempt attempt = await PhotoshopAttemptAsync(harness, id);

        output.WriteLine($"AttemptId      = {attempt.Id.Value:D}");
        output.WriteLine($"Status         = {attempt.Status}");
        output.WriteLine($"StartedAtUtc   = {attempt.StartedAtUtc:O}");
        output.WriteLine($"EndedAtUtc     = {attempt.EndedAtUtc:O}");
        output.WriteLine($"Duration       = {Duration(attempt)}");
        output.WriteLine($"Adapter worked = {Work} (clock advanced inside the adapter call)");

        attempt.Status.ShouldBe(AttemptStatus.Succeeded);
        attempt.StartedAtUtc.ShouldBe(t0);
        attempt.EndedAtUtc.ShouldBe(t0 + Work);
        Duration(attempt).ShouldBe(Work);
    }

    // -----------------------------------------------------------------------------------
    // §20 — Adapter failure
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// An attempt the adapter failed ends at the instant the failure was persisted, and leaves
    /// the automation lock free (§7, §20).
    /// </summary>
    [Fact]
    public async Task A_failed_attempt_ends_at_the_instant_the_failure_closed_it()
    {
        using SessionServiceHarness harness = new();
        ClockAdvancingPhotoshopProcessor photoshop = new(harness.FakePhotoshop, harness.Clock, Work);
        ISessionService service = harness.CreateServiceWithPhotoshop(photoshop);

        SessionId id = await ReadyForPhotoshopAsync(harness, service);
        DateTimeOffset t0 = harness.Clock.GetUtcNow();

        harness.FakePhotoshop.SetScenario(FakeAdapterScenario.FailWith(FailureCode.AdapterUnavailable));
        OperationResult<SessionView> failed = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", CancellationToken.None);
        failed.IsFailure.ShouldBeTrue();

        ProcessingAttempt attempt = await PhotoshopAttemptAsync(harness, id);
        attempt.Status.ShouldBe(AttemptStatus.Failed);
        attempt.StartedAtUtc.ShouldBe(t0);
        attempt.EndedAtUtc.ShouldBe(t0 + Work);
        await LockShouldBeFreeAsync(harness);
    }

    // -----------------------------------------------------------------------------------
    // §20 — Unexpected exception
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// An attempt closed by 11600-A's exception containment ends at the instant the containment
    /// persisted it, and leaves the automation lock free (§8, §20).
    /// </summary>
    /// <remarks>
    /// The failure vocabulary is asserted alongside the timing on purpose: this task fixes when
    /// an attempt is recorded as ending, and must not have quietly changed what it is recorded
    /// as ending <i>with</i> (§24).
    /// </remarks>
    [Fact]
    public async Task A_contained_exception_ends_the_attempt_at_the_containment_instant()
    {
        using SessionServiceHarness harness = new();
        ClockAdvancingPhotoshopProcessor photoshop = new(harness.FakePhotoshop, harness.Clock, Work)
        {
            Behaviour = (_, _) => throw new InvalidOperationException("the adapter fell over"),
        };
        ISessionService service = harness.CreateServiceWithPhotoshop(photoshop);

        SessionId id = await ReadyForPhotoshopAsync(harness, service);
        DateTimeOffset t0 = harness.Clock.GetUtcNow();

        OperationResult<SessionView> faulted = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", CancellationToken.None);
        faulted.IsFailure.ShouldBeTrue();

        ProcessingAttempt attempt = await PhotoshopAttemptAsync(harness, id);
        attempt.Status.ShouldBe(AttemptStatus.Failed);
        attempt.StartedAtUtc.ShouldBe(t0);
        attempt.EndedAtUtc.ShouldBe(t0 + Work);
        attempt.Failure.ShouldNotBeNull().Code.ShouldBe(FailureCode.AdapterUnavailable);
        attempt.Failure!.Context["faultType"].ShouldBe(typeof(InvalidOperationException).FullName);
        await LockShouldBeFreeAsync(harness);
    }

    // -----------------------------------------------------------------------------------
    // §20 — Caller cancellation
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// An attempt closed after the caller cancelled ends at the closing transaction's own
    /// instant, which is later than any instant the cancelled caller supplied (§9, §20).
    /// </summary>
    /// <remarks>
    /// The clock is advanced a second time, after the adapter is already waiting and before the
    /// token is cancelled, so the expected value is one no other reading in the operation could
    /// have produced: not the start, and not the moment the adapter began waiting.
    /// </remarks>
    [Fact]
    public async Task A_cancelled_caller_still_gets_a_truthful_terminal_timestamp()
    {
        TimeSpan waiting = TimeSpan.FromMinutes(7);

        using SessionServiceHarness harness = new();
        ClockAdvancingPhotoshopProcessor photoshop = new(harness.FakePhotoshop, harness.Clock, Work);
        ISessionService service = harness.CreateServiceWithPhotoshop(photoshop);

        SessionId id = await ReadyForPhotoshopAsync(harness, service);
        DateTimeOffset t0 = harness.Clock.GetUtcNow();

        harness.FakePhotoshop.SetScenario(FakeAdapterScenario.HangUntilCancelled);
        using CancellationTokenSource cancellation = new();
        Task<OperationResult<SessionView>> running = service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", cancellation.Token);

        await harness.FakePhotoshop.HangStarted;
        harness.Clock.Advance(waiting);
        await cancellation.CancelAsync();
        (await running).IsFailure.ShouldBeTrue();

        ProcessingAttempt attempt = await PhotoshopAttemptAsync(harness, id);
        attempt.Status.ShouldBe(AttemptStatus.Failed);
        attempt.StartedAtUtc.ShouldBe(t0);
        attempt.EndedAtUtc.ShouldBe(t0 + Work + waiting);
        attempt.Failure.ShouldNotBeNull().Code.ShouldBe(FailureCode.Cancelled);
        await LockShouldBeFreeAsync(harness);
    }

    /// <summary>
    /// A step that throws cancellation nobody asked for is a fault, and it too closes at the
    /// containment instant (§9, §20).
    /// </summary>
    /// <remarks>
    /// The distinction 11600-A drew must survive this task: the caller's token was never
    /// cancelled, so this is recorded as a fault rather than as an operator cancellation — and
    /// the timing has to be truthful either way.
    /// </remarks>
    [Fact]
    public async Task An_unrequested_OperationCanceledException_is_a_fault_with_a_truthful_end()
    {
        using SessionServiceHarness harness = new();
        ClockAdvancingPhotoshopProcessor photoshop = new(harness.FakePhotoshop, harness.Clock, Work)
        {
            Behaviour = (_, _) => throw new OperationCanceledException("nobody asked for this"),
        };
        ISessionService service = harness.CreateServiceWithPhotoshop(photoshop);

        SessionId id = await ReadyForPhotoshopAsync(harness, service);
        DateTimeOffset t0 = harness.Clock.GetUtcNow();

        OperationResult<SessionView> faulted = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", CancellationToken.None);
        faulted.IsFailure.ShouldBeTrue();

        ProcessingAttempt attempt = await PhotoshopAttemptAsync(harness, id);
        attempt.Status.ShouldBe(AttemptStatus.Failed);
        attempt.StartedAtUtc.ShouldBe(t0);
        attempt.EndedAtUtc.ShouldBe(t0 + Work);

        // A fault, not a cancellation: the operator never stopped anything.
        attempt.Failure.ShouldNotBeNull().Code.ShouldBe(FailureCode.AdapterUnavailable);
        attempt.Failure!.Context.ShouldContainKey("faultType");
        await LockShouldBeFreeAsync(harness);
    }

    // -----------------------------------------------------------------------------------
    // §20 — Interrupted recovery, and the owner that is still alive
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// An attempt whose process died is closed at the instant recovery established the
    /// interruption, and the stale lock is released (§10, §20).
    /// </summary>
    /// <remarks>
    /// The recovery timestamp is the honest answer and deliberately not a guess at when the
    /// process actually died: nothing recovery can read — a file time, a session row, a TIFF
    /// header — is an authoritative record of that, and §10 forbids manufacturing one.
    /// </remarks>
    [Fact]
    public async Task An_interrupted_attempt_ends_at_the_recovery_instant()
    {
        TimeSpan untilRestart = TimeSpan.FromHours(2);

        using SessionServiceHarness harness = new();
        ClockAdvancingPhotoshopProcessor photoshop = new(harness.FakePhotoshop, harness.Clock, Work);
        ISessionService service = harness.CreateServiceWithPhotoshop(photoshop);

        SessionId id = await ReadyForPhotoshopAsync(harness, service);
        DateTimeOffset t0 = harness.Clock.GetUtcNow();

        // Started, never finished, never cancelled: the database's view of a killed process.
        harness.FakePhotoshop.SetScenario(FakeAdapterScenario.HangUntilCancelled);
        _ = service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", CancellationToken.None);
        await harness.FakePhotoshop.HangStarted;

        harness.Clock.Advance(untilRestart);
        DateTimeOffset t2 = harness.Clock.GetUtcNow();

        OperationResult<StartupRecoveryReport> recovered = await harness
            .CreateRecoveryService(new FakeProcessLiveness(ProcessLiveness.Dead))
            .RecoverAsync(CancellationToken.None);
        recovered.IsSuccess.ShouldBeTrue(recovered.IsFailure ? recovered.Failure.ToString() : "");

        ProcessingAttempt attempt = await PhotoshopAttemptAsync(harness, id);
        attempt.Status.ShouldBe(AttemptStatus.Interrupted);
        attempt.StartedAtUtc.ShouldBe(t0);
        attempt.EndedAtUtc.ShouldBe(t2);
        await LockShouldBeFreeAsync(harness);
    }

    /// <summary>
    /// An attempt whose owner is still alive keeps running, and is given no terminal timestamp
    /// at all (§11).
    /// </summary>
    /// <remarks>
    /// The fail-closed half of recovery. An attempt that recovery declines to interrupt must not
    /// come back with a partially applied outcome — a Running row wearing an <c>EndedAtUtc</c>
    /// would be exactly the fabricated completion §3 exists to forbid.
    /// </remarks>
    [Fact]
    public async Task A_live_owner_leaves_the_attempt_running_with_no_terminal_timestamp()
    {
        using SessionServiceHarness harness = new();
        ClockAdvancingPhotoshopProcessor photoshop = new(harness.FakePhotoshop, harness.Clock, Work);
        ISessionService service = harness.CreateServiceWithPhotoshop(photoshop);

        SessionId id = await ReadyForPhotoshopAsync(harness, service);
        DateTimeOffset t0 = harness.Clock.GetUtcNow();

        harness.FakePhotoshop.SetScenario(FakeAdapterScenario.HangUntilCancelled);
        using CancellationTokenSource cancellation = new();
        Task<OperationResult<SessionView>> running = service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", cancellation.Token);
        await harness.FakePhotoshop.HangStarted;

        harness.Clock.Advance(TimeSpan.FromHours(1));

        OperationResult<StartupRecoveryReport> recovered = await harness
            .CreateRecoveryService(new FakeProcessLiveness(ProcessLiveness.Alive))
            .RecoverAsync(CancellationToken.None);
        recovered.IsSuccess.ShouldBeTrue(recovered.IsFailure ? recovered.Failure.ToString() : "");

        ProcessingAttempt attempt = await PhotoshopAttemptAsync(harness, id);
        attempt.Status.ShouldBe(AttemptStatus.Running);
        attempt.StartedAtUtc.ShouldBe(t0);
        attempt.EndedAtUtc.ShouldBeNull();

        AutomationLockState held =
            (await harness.Repository.GetAutomationLockAsync(CancellationToken.None)).Value;
        held.IsHeld.ShouldBeTrue();

        await cancellation.CancelAsync();
        (await running).IsFailure.ShouldBeTrue();
    }

    // -----------------------------------------------------------------------------------
    // §12 — the terminal transaction that never commits
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// A terminal transaction that fails to commit leaves the attempt persisted as Running with
    /// no terminal timestamp, and a later recovery assigns its own (§12).
    /// </summary>
    /// <remarks>
    /// The rule this proves is that the closing clock observation is not a fact until the
    /// transaction carrying it commits. The service observed a terminal instant and built the
    /// row around it; because the commit never landed, none of it is true, and nothing "repairs"
    /// the row in memory afterwards.
    /// <para>
    /// The failing commit is chosen relative to the commits already made rather than by a fixed
    /// count, so the test keeps failing the transaction it means to fail even if the workflow
    /// ever commits a different number of times on the way to this step.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_terminal_commit_that_fails_leaves_no_terminal_timestamp_behind()
    {
        TimeSpan untilRestart = TimeSpan.FromHours(4);

        using SessionServiceHarness harness = new();
        FaultingRepository repository = new(harness.Repository);
        ClockAdvancingPhotoshopProcessor photoshop = new(harness.FakePhotoshop, harness.Clock, Work);
        ISessionService service = harness.CreateServiceWithPhotoshop(photoshop, repository: repository);

        SessionId id = await ReadyForPhotoshopAsync(harness, service);
        DateTimeOffset t0 = harness.Clock.GetUtcNow();

        // The step's opening transaction is the next commit; its closing one is the one after,
        // and that is the one that must never land.
        repository.FailFromCommit = repository.AttemptedCommits + 2;

        OperationResult<SessionView> crashed = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", CancellationToken.None);
        crashed.IsFailure.ShouldBeTrue();
        crashed.Failure.Code.ShouldBe(FailureCode.PersistenceError);

        ProcessingAttempt uncommitted = await PhotoshopAttemptAsync(harness, id);
        uncommitted.Status.ShouldBe(AttemptStatus.Running);
        uncommitted.StartedAtUtc.ShouldBe(t0);
        uncommitted.EndedAtUtc.ShouldBeNull();
        uncommitted.OutputRevisionId.ShouldBeNull();

        // What the next process makes of it: its own recovery instant, not the terminal instant
        // the crashed run had observed and failed to persist.
        harness.Clock.Advance(untilRestart);
        DateTimeOffset recoveryInstant = harness.Clock.GetUtcNow();

        OperationResult<StartupRecoveryReport> recovered = await harness
            .CreateRecoveryService(new FakeProcessLiveness(ProcessLiveness.Dead))
            .RecoverAsync(CancellationToken.None);
        recovered.IsSuccess.ShouldBeTrue(recovered.IsFailure ? recovered.Failure.ToString() : "");

        ProcessingAttempt interrupted = await PhotoshopAttemptAsync(harness, id);
        interrupted.Status.ShouldBe(AttemptStatus.Interrupted);
        interrupted.StartedAtUtc.ShouldBe(t0);
        interrupted.EndedAtUtc.ShouldBe(recoveryInstant);
    }

    // -----------------------------------------------------------------------------------
    // §13 — retry
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// Two attempts at the same step keep their own start and end instants, and the first one's
    /// are not rewritten by the second (§13, §20).
    /// </summary>
    /// <remarks>
    /// The durations are deliberately different. Equal ones would pass even if the second
    /// attempt's timing had been derived from the first, which is one of the things §13 forbids.
    /// </remarks>
    [Fact]
    public async Task A_retry_and_the_attempt_it_retries_keep_independent_timestamps()
    {
        TimeSpan firstRun = TimeSpan.FromMinutes(2);
        TimeSpan secondRun = TimeSpan.FromMinutes(11);

        using SessionServiceHarness harness = new();
        ClockAdvancingPhotoshopProcessor photoshop = new(harness.FakePhotoshop, harness.Clock, firstRun);
        ISessionService service = harness.CreateServiceWithPhotoshop(photoshop);

        SessionId id = await ReadyForPhotoshopAsync(harness, service);
        DateTimeOffset t0 = harness.Clock.GetUtcNow();

        harness.FakePhotoshop.SetScenario(FakeAdapterScenario.FailWith(FailureCode.AdapterUnavailable));
        (await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", CancellationToken.None))
            .IsFailure.ShouldBeTrue();

        DateTimeOffset t1 = harness.Clock.GetUtcNow();
        t1.ShouldBe(t0 + firstRun);

        harness.FakePhotoshop.SetScenario(FakeAdapterScenario.Succeed);
        photoshop.Advance = secondRun;
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.Retry(StepKind.PhotoshopOutput), "tester", CancellationToken.None));
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", CancellationToken.None));

        ProcessingAttempt[] attempts = await PhotoshopAttemptsAsync(harness, id);
        attempts.Length.ShouldBe(2);

        ProcessingAttempt first = attempts.Single(a => a.RetrySequence == 0);
        ProcessingAttempt second = attempts.Single(a => a.RetrySequence == 1);

        first.Status.ShouldBe(AttemptStatus.Failed);
        first.StartedAtUtc.ShouldBe(t0);
        first.EndedAtUtc.ShouldBe(t1);
        Duration(first).ShouldBe(firstRun);

        second.Status.ShouldBe(AttemptStatus.Succeeded);
        second.StartedAtUtc.ShouldBe(t1);
        second.EndedAtUtc.ShouldBe(t1 + secondRun);
        Duration(second).ShouldBe(secondRun);

        // Neither row carries the other's duration, and the retry chain is intact.
        second.RetryOfAttemptId.ShouldBe(first.Id);
    }

    // -----------------------------------------------------------------------------------
    // §21 — the shape of the first real production order
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// Import → PhotoshopOutput → ReviewRequired → Approval, with the clock advancing during the
    /// Photoshop work: the attempt's timestamps are truthful, and approving it afterwards does
    /// not touch them (§21).
    /// </summary>
    /// <remarks>
    /// Shaped like the controlled real order that exposed the defect, and run entirely on
    /// synthetic artwork. Approval is included because it is the step that follows the one being
    /// measured: an approval that rewrote the attempt's terminal timestamp would turn every
    /// automation duration into "time until a human got round to it", which is a different
    /// measurement and the wrong one for SCRUM-11137.
    /// </remarks>
    [Fact]
    public async Task The_real_order_shape_records_truthful_timing_that_approval_does_not_disturb()
    {
        using SessionServiceHarness harness = new();
        ClockAdvancingPhotoshopProcessor photoshop = new(harness.FakePhotoshop, harness.Clock, Work);
        ISessionService service = harness.CreateServiceWithPhotoshop(photoshop);

        DateTimeOffset t0 = harness.Clock.GetUtcNow();
        SessionId id = await ReachReviewRequiredAsync(harness, service);

        ProcessingAttempt attempt = await PhotoshopAttemptAsync(harness, id);
        attempt.Status.ShouldBe(AttemptStatus.Succeeded);
        attempt.RetrySequence.ShouldBe(0);
        attempt.StartedAtUtc.ShouldBe(t0);
        attempt.EndedAtUtc.ShouldBe(t0 + Work);

        SessionAggregate reviewed = await LoadAsync(harness, id);
        reviewed.Steps.Single(s => s.Step == StepKind.PhotoshopOutput).State
            .ShouldBe(StepState.ReviewRequired);

        Revision tiff = reviewed.Revisions.Single(r => r.Operation == OperationKind.PhotoshopOutput);

        // An operator who reviews it an hour later approves the same attempt, not a re-timed one.
        harness.Clock.Advance(TimeSpan.FromHours(1));
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.Approve(StepKind.PhotoshopOutput, tiff.Facts.Sha256), "tester",
            CancellationToken.None));

        ProcessingAttempt afterApproval = await PhotoshopAttemptAsync(harness, id);
        afterApproval.Id.ShouldBe(attempt.Id);
        afterApproval.StartedAtUtc.ShouldBe(t0);
        afterApproval.EndedAtUtc.ShouldBe(t0 + Work);
        Duration(afterApproval).ShouldBe(Work);
    }

    // -----------------------------------------------------------------------------------
    // §18 — the durations SCRUM-11137 will measure
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// Every terminal attempt a session produced can be reduced to a duration, and no attempt
    /// reports a negative one (§14, §18).
    /// </summary>
    /// <remarks>
    /// The prerequisite stated as the property the measurement task actually needs: a duration
    /// derived from two persisted timestamps, for a successful attempt, a failed one and a retry
    /// alike. It stops short of thresholds, dashboards and alarms, which belong to SCRUM-11137.
    /// </remarks>
    [Fact]
    public async Task Durations_are_derivable_for_every_terminal_attempt_of_a_session()
    {
        TimeSpan retryRun = TimeSpan.FromMinutes(5);

        using SessionServiceHarness harness = new();
        ClockAdvancingPhotoshopProcessor photoshop = new(harness.FakePhotoshop, harness.Clock, Work);
        ISessionService service = harness.CreateServiceWithPhotoshop(photoshop);

        SessionId id = await ReadyForPhotoshopAsync(harness, service);

        harness.FakePhotoshop.SetScenario(FakeAdapterScenario.FailWith(FailureCode.AdapterUnavailable));
        (await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", CancellationToken.None))
            .IsFailure.ShouldBeTrue();

        harness.FakePhotoshop.SetScenario(FakeAdapterScenario.Succeed);
        photoshop.Advance = retryRun;
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.Retry(StepKind.PhotoshopOutput), "tester", CancellationToken.None));
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", CancellationToken.None));

        SessionAggregate aggregate = await LoadAsync(harness, id);
        ProcessingAttempt[] terminal =
            [.. aggregate.Attempts.Where(a => a.Status != AttemptStatus.Running)];

        terminal.ShouldNotBeEmpty();
        foreach (ProcessingAttempt terminated in terminal)
        {
            terminated.EndedAtUtc.ShouldNotBeNull();
            Duration(terminated).ShouldBeGreaterThanOrEqualTo(TimeSpan.Zero);
        }

        // The three durations SCRUM-11137 names, each derived rather than persisted.
        Duration(terminal.Single(a => a.Step == StepKind.PhotoshopOutput && a.RetrySequence == 0))
            .ShouldBe(Work);
        Duration(terminal.Single(a => a.Step == StepKind.PhotoshopOutput && a.RetrySequence == 1))
            .ShouldBe(retryRun);
        Duration(terminal.Single(a => a.Step == StepKind.Import))
            .ShouldBeGreaterThanOrEqualTo(TimeSpan.Zero);
    }

    // -----------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------

    /// <summary>The duration §14 asks readers to derive rather than persist.</summary>
    private static TimeSpan Duration(ProcessingAttempt attempt) =>
        attempt.EndedAtUtc.ShouldNotBeNull() - attempt.StartedAtUtc;

    /// <summary>Imports a GeneratePrintTiff session and leaves PhotoshopOutput ready to start.</summary>
    private static async Task<SessionId> ReadyForPhotoshopAsync(
        SessionServiceHarness harness, ISessionService service)
    {
        SessionId id = (await service.ImportAsync(
            WorkflowType.GeneratePrintTiff,
            harness.WriteBorderedSourcePng(),
            "timing-out",
            "tester",
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

        return id;
    }

    private static async Task<SessionId> ReachReviewRequiredAsync(
        SessionServiceHarness harness, ISessionService service)
    {
        SessionId id = await ReadyForPhotoshopAsync(harness, service);
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", CancellationToken.None));
        return id;
    }

    private static async Task LockShouldBeFreeAsync(SessionServiceHarness harness)
    {
        AutomationLockState state =
            (await harness.Repository.GetAutomationLockAsync(CancellationToken.None)).Value;
        state.IsHeld.ShouldBeFalse();
    }

    private static async Task<SessionAggregate> LoadAsync(SessionServiceHarness harness, SessionId id)
    {
        OperationResult<SessionAggregate?> loaded =
            await harness.Repository.LoadAsync(id, CancellationToken.None);
        loaded.IsSuccess.ShouldBeTrue(loaded.IsFailure ? loaded.Failure.ToString() : "");
        return loaded.Value.ShouldNotBeNull();
    }

    private static async Task<ProcessingAttempt> PhotoshopAttemptAsync(
        SessionServiceHarness harness, SessionId id) =>
        (await LoadAsync(harness, id)).Attempts.Last(a => a.Step == StepKind.PhotoshopOutput);

    private static async Task<ProcessingAttempt[]> PhotoshopAttemptsAsync(
        SessionServiceHarness harness, SessionId id) =>
        [.. (await LoadAsync(harness, id)).Attempts.Where(a => a.Step == StepKind.PhotoshopOutput)];

    private static async Task Must(Task<OperationResult<SessionView>> pending)
    {
        OperationResult<SessionView> result = await pending;
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : "");
    }
}
