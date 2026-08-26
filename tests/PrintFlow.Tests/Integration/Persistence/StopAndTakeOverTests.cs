using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Adapters.Fake;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Persistence;

/// <summary>
/// Operator Stop and Take Over, driven end to end through <see cref="SessionService"/> against
/// real files and a real database (Epic 11300 Part D2A §36).
/// </summary>
/// <remarks>
/// These are the <b>workflow-level</b> halves of D2A: what PrintFlow records, what it refuses,
/// what survives a restart, and what the automation lock does. What Meitu is shown — the exact
/// signed cancel control, the single invocation, the refusals when it cannot be proven — is
/// tested against the driver with a fake automation tree in
/// <c>GuardedMeituBusyCancelTests</c>, where an invocation is a real thing that can be counted.
/// The split matters: a test that proved "the attempt is Cancelled" would prove nothing about
/// which control was clicked, and vice versa.
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class StopAndTakeOverTests
{
    // -----------------------------------------------------------------------------
    // §36.1 — Stop before invoke
    // -----------------------------------------------------------------------------

    /// <summary>
    /// A Stop that arrives before an operation is invoked produces zero operation input (§5).
    /// </summary>
    /// <remarks>
    /// Exercised through the adapter's <see cref="ExternalOperationPhase.NotStarted"/> phase,
    /// which is the honest stand-in for "PrintFlow has the working copy open and has asked
    /// Meitu for nothing". The assertion that carries the rule is the recorded phase: an audit
    /// saying <c>NotStarted</c> is a claim that no operation input existed to cancel.
    /// </remarks>
    [Fact]
    public async Task Stop_before_the_operation_is_invoked_records_no_operation_input()
    {
        using Fixture fixture = await Fixture.AtEnhancementAsync();

        SessionView view = await fixture.StopDuringAsync(
            ExternalOperationPhase.NotStarted, AutomationStopMode.StopOperation);

        AutomationStopAudit audit = view.LastAutomationStop.ShouldNotBeNull();
        audit.Phase.ShouldBe(ExternalOperationPhase.NotStarted);
        audit.Mode.ShouldBe(AutomationStopMode.StopOperation);
        audit.SignedCancelInvoked.ShouldBeFalse();
        audit.Retained.ShouldBe(RetainedExternalState.None);
        audit.OperatorActionMayBeRequired.ShouldBeFalse();
    }

    // -----------------------------------------------------------------------------
    // §36.6 — a stop never creates a Revision
    // -----------------------------------------------------------------------------

    /// <summary>
    /// A stopped attempt is Cancelled, carries no output Revision, and leaves the step
    /// Interrupted (§12).
    /// </summary>
    /// <remarks>
    /// The database enforces the middle claim independently of this code — the
    /// <c>ProcessingAttempt</c> CHECK admits an <c>OutputRevisionId</c> only for
    /// <c>SUCCEEDED</c> — so a defect that tried to attach one would fail the commit rather
    /// than this assertion. Both are worth having: one says the rule holds, the other says it
    /// cannot be broken.
    /// </remarks>
    [Fact]
    public async Task A_stopped_attempt_is_cancelled_and_creates_no_revision()
    {
        using Fixture fixture = await Fixture.AtEnhancementAsync();
        int revisionsBefore = (await fixture.AggregateAsync()).Revisions.Count;

        SessionView view = await fixture.StopDuringAsync(
            ExternalOperationPhase.Busy, AutomationStopMode.StopOperation);

        view.Steps.Single(s => s.Step == StepKind.Enhancement).State.ShouldBe(StepState.Interrupted);

        SessionAggregate aggregate = await fixture.AggregateAsync();
        aggregate.Revisions.Count.ShouldBe(revisionsBefore, "a stop must produce no Revision");

        ProcessingAttempt attempt = aggregate.Attempts.Single(a => a.Step == StepKind.Enhancement);
        attempt.Status.ShouldBe(AttemptStatus.Cancelled);
        attempt.OutputRevisionId.ShouldBeNull();
        attempt.ProducedRevision.ShouldBeFalse();
        attempt.EndedAtUtc.ShouldNotBeNull();
    }

    /// <summary>The same for Background Removal, which has its own authority and output rules.</summary>
    [Fact]
    public async Task A_stopped_background_removal_attempt_creates_no_revision()
    {
        using Fixture fixture = await Fixture.AtBackgroundRemovalAsync();

        SessionView view = await fixture.StopDuringAsync(
            ExternalOperationPhase.Busy, AutomationStopMode.StopOperation, StepKind.BackgroundRemoval);

        view.Steps.Single(s => s.Step == StepKind.BackgroundRemoval).State
            .ShouldBe(StepState.Interrupted);

        SessionAggregate aggregate = await fixture.AggregateAsync();
        ProcessingAttempt attempt = aggregate.Attempts
            .Single(a => a.Step == StepKind.BackgroundRemoval);
        attempt.Status.ShouldBe(AttemptStatus.Cancelled);
        attempt.OutputRevisionId.ShouldBeNull();
    }

    // -----------------------------------------------------------------------------
    // §36.4 — no cancel available means no guessed input
    // -----------------------------------------------------------------------------

    /// <summary>
    /// A stop whose cancel could not be proven records that Meitu may still be running, and
    /// never records a cancel it did not invoke (§10, §29).
    /// </summary>
    /// <remarks>
    /// The fake adapter drives no external application, so it reports no signed cancel — which
    /// is exactly the "Stop requested but Meitu Cancel unavailable" row §29 asks to be
    /// distinguishable. The assertion is that the audit says so rather than staying silent:
    /// silence would read like a clean cancellation.
    /// </remarks>
    [Fact]
    public async Task A_stop_with_no_provable_cancel_reports_that_the_operation_may_still_be_running()
    {
        using Fixture fixture = await Fixture.AtEnhancementAsync();

        SessionView view = await fixture.StopDuringAsync(
            ExternalOperationPhase.Busy, AutomationStopMode.StopOperation);

        AutomationStopAudit audit = view.LastAutomationStop.ShouldNotBeNull();
        audit.SignedCancelInvoked.ShouldBeFalse();
        audit.Retained.ShouldBe(RetainedExternalState.OperationMayStillBeRunning);
        audit.OperatorActionMayBeRequired.ShouldBeTrue();
        view.HasRetainedExternalState.ShouldBeTrue();
    }

    // -----------------------------------------------------------------------------
    // §36.7 — completed before export
    // -----------------------------------------------------------------------------

    /// <summary>
    /// A stop after processing finished and before the export records a retained result and
    /// exports nothing (§13).
    /// </summary>
    /// <remarks>
    /// The retained state is the point. PrintFlow did not discard Meitu's processed result and
    /// does not claim to have done: §13 permits exactly one action here — not exporting — and
    /// the operator has to be told the result is still sitting in Meitu.
    /// </remarks>
    [Fact]
    public async Task A_stop_after_completion_and_before_export_reports_a_retained_result()
    {
        using Fixture fixture = await Fixture.AtEnhancementAsync();

        SessionView view = await fixture.StopDuringAsync(
            ExternalOperationPhase.CompletedBeforeExport, AutomationStopMode.StopOperation);

        AutomationStopAudit audit = view.LastAutomationStop.ShouldNotBeNull();
        audit.Phase.ShouldBe(ExternalOperationPhase.CompletedBeforeExport);
        audit.Retained.ShouldBe(RetainedExternalState.ProcessedResultRetained);
        (await fixture.AggregateAsync()).Attempts
            .Single(a => a.Step == StepKind.Enhancement)
            .OutputRevisionId.ShouldBeNull();
    }

    // -----------------------------------------------------------------------------
    // §36.9 — a late stop cannot erase a validated success
    // -----------------------------------------------------------------------------

    /// <summary>
    /// A Stop that arrives after a validated output exists leaves the Attempt and Revision
    /// complete (§16).
    /// </summary>
    /// <remarks>
    /// Driven the only honest way: the adapter is allowed to <b>succeed</b>, and the Stop is
    /// requested against the run once it has finished. The registry refuses it — the run has
    /// ended — and the session keeps its Revision. That refusal <i>is</i> the boundary: there
    /// is no window in which a Stop can reach an attempt whose success transaction has
    /// committed, because the run is unregistered before the call returns.
    /// </remarks>
    [Fact]
    public async Task A_stop_requested_after_a_validated_success_cannot_erase_it()
    {
        using Fixture fixture = await Fixture.AtEnhancementAsync();

        fixture.Harness.FakeMeitu.SetScenario(FakeAdapterScenario.Succeed);
        OperationResult<SessionView> ran = await fixture.Service.ExecuteAsync(
            fixture.Id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None);
        ran.IsSuccess.ShouldBeTrue(ran.IsFailure ? ran.Failure.ToString() : string.Empty);

        OperationResult<PrintFlow.Domain.Results.Unit> late = fixture.Service.RequestStop(
            fixture.Id, AutomationStopMode.StopOperation);
        late.IsFailure.ShouldBeTrue("a run that has finished is no longer stoppable");

        SessionAggregate aggregate = await fixture.AggregateAsync();
        ProcessingAttempt attempt = aggregate.Attempts.Single(a => a.Step == StepKind.Enhancement);
        attempt.Status.ShouldBe(AttemptStatus.Succeeded);
        attempt.OutputRevisionId.ShouldNotBeNull();

        SessionView reloaded = (await fixture.Service.LoadAsync(fixture.Id, CancellationToken.None)).Value;
        reloaded.Steps.Single(s => s.Step == StepKind.Enhancement).State
            .ShouldBe(StepState.ReviewRequired);
        reloaded.LastAutomationStop.ShouldBeNull("nothing about this attempt was stopped");
    }

    // -----------------------------------------------------------------------------
    // §36.10, §36.11 — Take Over
    // -----------------------------------------------------------------------------

    /// <summary>
    /// A takeover ends the attempt, hands the session over, and records that Meitu was left
    /// untouched (§17, §18, §19).
    /// </summary>
    [Fact]
    public async Task Take_over_ends_the_attempt_and_hands_the_session_to_the_operator()
    {
        using Fixture fixture = await Fixture.AtEnhancementAsync();

        SessionView view = await fixture.StopDuringAsync(
            ExternalOperationPhase.Busy, AutomationStopMode.TakeOver);

        view.State.ShouldBe(SessionState.HandedOff);
        view.Steps.Single(s => s.Step == StepKind.Enhancement).State.ShouldBe(StepState.Interrupted);

        AutomationStopAudit audit = view.LastAutomationStop.ShouldNotBeNull();
        audit.Mode.ShouldBe(AutomationStopMode.TakeOver);
        audit.SignedCancelInvoked.ShouldBeFalse("a takeover invokes nothing at all");
        audit.Retained.ShouldBe(RetainedExternalState.OperationMayStillBeRunning);

        SessionAggregate aggregate = await fixture.AggregateAsync();
        aggregate.Session.HandedOffAtUtc.ShouldNotBeNull();
        aggregate.Attempts.Single(a => a.Step == StepKind.Enhancement)
            .Status.ShouldBe(AttemptStatus.Cancelled);
    }

    /// <summary>
    /// A takeover from a blocking modal or an unrecognised screen dismisses nothing (§20).
    /// </summary>
    /// <remarks>
    /// The phase is what carries this. <see cref="ExternalOperationPhase.UnknownOrBlocked"/>
    /// resolves — for either mode, and for a takeover unconditionally — to "no input may be
    /// produced", so the rule is a property of the policy table rather than of a branch someone
    /// has to remember to write. The audit records <c>Unknown</c> rather than pretending to
    /// know what Meitu is showing.
    /// </remarks>
    [Fact]
    public async Task Take_over_from_an_unknown_or_blocked_screen_records_unknown_state()
    {
        using Fixture fixture = await Fixture.AtEnhancementAsync();

        SessionView view = await fixture.StopDuringAsync(
            ExternalOperationPhase.UnknownOrBlocked, AutomationStopMode.TakeOver);

        AutomationStopAudit audit = view.LastAutomationStop.ShouldNotBeNull();
        audit.Phase.ShouldBe(ExternalOperationPhase.UnknownOrBlocked);
        audit.Retained.ShouldBe(RetainedExternalState.Unknown);
        audit.SignedCancelInvoked.ShouldBeFalse();
        view.State.ShouldBe(SessionState.HandedOff);
    }

    // -----------------------------------------------------------------------------
    // §36.13, §36.14, §36.15 — restart, re-entry, immutability
    // -----------------------------------------------------------------------------

    /// <summary>
    /// A restart after a takeover does not resume automation, and the ordinary progression
    /// commands stay refused until re-entry (§30).
    /// </summary>
    /// <remarks>
    /// The second service instance shares nothing with the first except the database and the
    /// files, which is what makes it a restart rather than a reload. Startup recovery runs too,
    /// because the honest question is whether recovery reopens what a takeover closed — and it
    /// must not: the attempt is already Cancelled, so there is no Running row to recover.
    /// </remarks>
    [Fact]
    public async Task A_restart_after_take_over_does_not_resume_automation()
    {
        using Fixture fixture = await Fixture.AtEnhancementAsync();
        await fixture.StopDuringAsync(ExternalOperationPhase.Busy, AutomationStopMode.TakeOver);

        StartupRecoveryReport report = (await fixture.Harness
            .CreateRecoveryService(new FakeProcessLiveness())
            .RecoverAsync(CancellationToken.None)).Value;
        report.Entries.ShouldNotContain(
            entry => entry.Action == StartupRecoveryAction.AttemptInterrupted,
            "a takeover already closed the attempt; recovery has nothing to reopen");

        ISessionService restarted = fixture.Harness.CreateService();
        SessionView reloaded = (await restarted.LoadAsync(fixture.Id, CancellationToken.None)).Value;

        reloaded.State.ShouldBe(SessionState.HandedOff);
        reloaded.CanContinueProcessing.ShouldBeFalse();
        restarted.GetAutomationRuntime(fixture.Id).State.ShouldBe(AutomationRuntimeState.Idle);

        OperationResult<SessionView> resumed = await restarted.ExecuteAsync(
            fixture.Id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None);
        resumed.IsFailure.ShouldBeTrue("a handed-off session must not be driven without explicit re-entry");
    }

    /// <summary>
    /// Explicit re-entry returns the session to automation and the next run is a <b>new</b>
    /// attempt; the handed-off one is untouched (§22, §15).
    /// </summary>
    [Fact]
    public async Task Re_entry_creates_a_new_attempt_and_leaves_the_handed_off_one_immutable()
    {
        using Fixture fixture = await Fixture.AtEnhancementAsync();
        await fixture.StopDuringAsync(ExternalOperationPhase.Busy, AutomationStopMode.TakeOver);

        ProcessingAttempt handedOff = (await fixture.AggregateAsync()).Attempts
            .Single(a => a.Step == StepKind.Enhancement);

        ISessionService restarted = fixture.Harness.CreateService();
        SessionView reentered = (await restarted.ExecuteAsync(
            fixture.Id, new WorkflowCommand.ReenterAutomation(), "tester", CancellationToken.None)).Value;

        reentered.State.ShouldBe(SessionState.Active);
        reentered.Steps.Single(s => s.Step == StepKind.Enhancement).State.ShouldBe(StepState.Waiting);

        fixture.Harness.FakeMeitu.SetScenario(FakeAdapterScenario.Succeed);
        OperationResult<SessionView> ran = await restarted.ExecuteAsync(
            fixture.Id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None);
        ran.IsSuccess.ShouldBeTrue(ran.IsFailure ? ran.Failure.ToString() : string.Empty);

        List<ProcessingAttempt> attempts =
            [.. (await fixture.AggregateAsync()).Attempts.Where(a => a.Step == StepKind.Enhancement)];
        attempts.Count.ShouldBe(2, "re-entry must produce a new attempt, never reopen the old one");

        // Field by field rather than record equality: OperationFailure carries an
        // IReadOnlyDictionary, which records compare by reference, so a reloaded row is never
        // == the one that was written even when every value in it is identical. Naming the
        // fields also says what "immutable" means here — the outcome, the timestamps, the
        // absence of a Revision and the audit are all exactly as they were written.
        ProcessingAttempt after = attempts.Single(a => a.Id == handedOff.Id);
        after.Status.ShouldBe(handedOff.Status);
        after.StartedAtUtc.ShouldBe(handedOff.StartedAtUtc);
        after.EndedAtUtc.ShouldBe(handedOff.EndedAtUtc);
        after.OutputRevisionId.ShouldBeNull();
        after.RetrySequence.ShouldBe(handedOff.RetrySequence);
        after.AdapterNotes.ShouldBe(handedOff.AdapterNotes);
        AutomationStopAudit.Read(after.Failure).ShouldNotBeNull()
            .Mode.ShouldBe(AutomationStopMode.TakeOver);

        attempts.Single(a => a.Id != handedOff.Id).Status.ShouldBe(AttemptStatus.Succeeded);
    }

    /// <summary>Re-entry is refused for a session that was never handed off (§22).</summary>
    [Fact]
    public async Task Re_entry_is_refused_for_a_session_that_is_still_active()
    {
        using Fixture fixture = await Fixture.AtEnhancementAsync();

        OperationResult<SessionView> refused = await fixture.Service.ExecuteAsync(
            fixture.Id, new WorkflowCommand.ReenterAutomation(), "tester", CancellationToken.None);

        refused.IsFailure.ShouldBeTrue();
    }

    // -----------------------------------------------------------------------------
    // §36.16 — the automation lock
    // -----------------------------------------------------------------------------

    /// <summary>
    /// Every way of stopping releases the global automation lock (§32).
    /// </summary>
    /// <remarks>
    /// Both modes and a plain failure share one assertion because the requirement is the same
    /// for all of them: a lock held on behalf of a run that has ended would block every future
    /// attempt on this workstation, and no amount of correct stop semantics would be worth
    /// that.
    /// </remarks>
    [Theory]
    [InlineData(AutomationStopMode.StopOperation)]
    [InlineData(AutomationStopMode.TakeOver)]
    public async Task Stopping_releases_the_global_automation_lock(AutomationStopMode mode)
    {
        using Fixture fixture = await Fixture.AtEnhancementAsync();

        await fixture.StopDuringAsync(ExternalOperationPhase.Busy, mode);

        AutomationLockState state = (await fixture.Harness.Repository
            .GetAutomationLockAsync(CancellationToken.None)).Value;
        state.IsHeld.ShouldBeFalse("a stopped run must not keep the machine-wide lock");
    }

    /// <summary>
    /// Surrendering the lock does not let a later attempt skip the ordinary safe-start checks
    /// (§32).
    /// </summary>
    /// <remarks>
    /// The rule §32 states is not "the lock is released" but "the next attempt still passes
    /// normal inspection". Here that is the engine refusing to start a step on a handed-off
    /// session at all: releasing the lock creates no shortcut, because the lock was never the
    /// only thing standing in the way.
    /// </remarks>
    [Fact]
    public async Task Releasing_the_lock_does_not_let_a_handed_off_session_start_another_run()
    {
        using Fixture fixture = await Fixture.AtEnhancementAsync();
        await fixture.StopDuringAsync(ExternalOperationPhase.Busy, AutomationStopMode.TakeOver);

        (await fixture.Harness.Repository.GetAutomationLockAsync(CancellationToken.None))
            .Value.IsHeld.ShouldBeFalse();

        OperationResult<SessionView> blocked = await fixture.Service.ExecuteAsync(
            fixture.Id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None);
        blocked.IsFailure.ShouldBeTrue();
    }

    // -----------------------------------------------------------------------------
    // The stop request itself
    // -----------------------------------------------------------------------------

    /// <summary>A Stop against a session with nothing running is refused, not silently ignored.</summary>
    /// <remarks>
    /// "Stop did nothing" is the one outcome an operator must not be left guessing about, so
    /// the service refuses rather than returning success (§24).
    /// </remarks>
    [Fact]
    public async Task A_stop_with_nothing_running_is_refused()
    {
        using Fixture fixture = await Fixture.AtEnhancementAsync();

        OperationResult<PrintFlow.Domain.Results.Unit> refused = fixture.Service.RequestStop(
            fixture.Id, AutomationStopMode.StopOperation);

        refused.IsFailure.ShouldBeTrue();
        fixture.Service.GetAutomationRuntime(fixture.Id).CanStopAutomation.ShouldBeFalse();
    }

    /// <summary>
    /// A second stop against a run that is already stopping is refused (§9).
    /// </summary>
    /// <remarks>
    /// §9 permits exactly one cancel invocation, and the realistic way a second one happens is
    /// an operator pressing Stop twice while the first is unwinding. Refusing the second
    /// request is what makes "once" true regardless of how fast the operator is.
    /// </remarks>
    [Fact]
    public async Task A_second_stop_request_against_the_same_run_is_refused()
    {
        using Fixture fixture = await Fixture.AtEnhancementAsync();

        fixture.Harness.FakeMeitu.SetScenario(
            FakeAdapterScenario.WaitForStopAt(ExternalOperationPhase.Busy));

        Task<OperationResult<SessionView>> run = fixture.Service.ExecuteAsync(
            fixture.Id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None);

        await fixture.Harness.FakeMeitu.HangStarted;

        fixture.Service.RequestStop(fixture.Id, AutomationStopMode.StopOperation).IsSuccess.ShouldBeTrue();
        fixture.Service.RequestStop(fixture.Id, AutomationStopMode.TakeOver).IsFailure.ShouldBeTrue();

        (await run).IsFailure.ShouldBeTrue();

        // The first request is the one that took effect; the second changed nothing.
        SessionView reloaded = (await fixture.Service.LoadAsync(fixture.Id, CancellationToken.None)).Value;
        reloaded.LastAutomationStop.ShouldNotBeNull().Mode.ShouldBe(AutomationStopMode.StopOperation);
        reloaded.State.ShouldBe(SessionState.Active, "a plain Stop does not hand the session over");
    }

    /// <summary>
    /// The runtime read model reports a stoppable run only while one is actually in flight
    /// (§24, §28).
    /// </summary>
    [Fact]
    public async Task The_runtime_read_model_offers_stop_only_while_a_run_is_in_flight()
    {
        using Fixture fixture = await Fixture.AtEnhancementAsync();

        fixture.Service.GetAutomationRuntime(fixture.Id).CanStopAutomation.ShouldBeFalse();

        fixture.Harness.FakeMeitu.SetScenario(
            FakeAdapterScenario.WaitForStopAt(ExternalOperationPhase.Busy));

        Task<OperationResult<SessionView>> run = fixture.Service.ExecuteAsync(
            fixture.Id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None);
        await fixture.Harness.FakeMeitu.HangStarted;

        AutomationRuntimeView running = fixture.Service.GetAutomationRuntime(fixture.Id);
        running.CanStopAutomation.ShouldBeTrue();
        running.CanTakeOverAutomation.ShouldBeTrue("Enhancement drives an external application");
        running.Step.ShouldBe(StepKind.Enhancement);
        running.Phase.ShouldBe(ExternalOperationPhase.Busy);

        fixture.Service.RequestStop(fixture.Id, AutomationStopMode.StopOperation);
        await run;

        fixture.Service.GetAutomationRuntime(fixture.Id).State.ShouldBe(AutomationRuntimeState.Idle);
        fixture.Service.GetAutomationRuntime(fixture.Id).CanStopAutomation.ShouldBeFalse();
    }

    /// <summary>
    /// Work that drives no external application offers Stop but not Take Over (§25).
    /// </summary>
    /// <remarks>
    /// Trim is PrintFlow's own pixel work. There is no Meitu to hand over, so offering the
    /// control would be the generic always-visible session button §25 rules out — while Stop
    /// still means something, because the orchestration is genuinely running.
    /// </remarks>
    [Fact]
    public async Task Internal_work_offers_no_take_over()
    {
        using Fixture fixture = await Fixture.AtEnhancementAsync();

        AutomationRuntimeView internalRun = new(
            fixture.Id, AttemptId.From(Guid.CreateVersion7()), StepKind.Trim,
            AutomationRuntimeState.Running, ExternalOperationPhase.NotStarted,
            DrivesExternalApplication: false);

        internalRun.CanStopAutomation.ShouldBeTrue();
        internalRun.CanTakeOverAutomation.ShouldBeFalse();
        internalRun.RetainedExternalState.ShouldBe(RetainedExternalState.None);
    }

    // -----------------------------------------------------------------------------
    // §36.17, §36.18 — the happy paths are unchanged
    // -----------------------------------------------------------------------------

    /// <summary>Enhancement still succeeds and produces a Revision when nothing stops it (§36.17).</summary>
    [Fact]
    public async Task The_enhancement_happy_path_is_unchanged()
    {
        using Fixture fixture = await Fixture.AtEnhancementAsync();

        fixture.Harness.FakeMeitu.SetScenario(FakeAdapterScenario.Succeed);
        SessionView ran = (await fixture.Service.ExecuteAsync(
            fixture.Id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None)).Value;

        ran.Steps.Single(s => s.Step == StepKind.Enhancement).State.ShouldBe(StepState.ReviewRequired);
        ran.LastAutomationStop.ShouldBeNull();
        ran.HasRetainedExternalState.ShouldBeFalse();
        (await fixture.AggregateAsync()).Attempts
            .Single(a => a.Step == StepKind.Enhancement)
            .OutputRevisionId.ShouldNotBeNull();
    }

    /// <summary>Background Removal still succeeds and produces a Revision (§36.18).</summary>
    [Fact]
    public async Task The_background_removal_happy_path_is_unchanged()
    {
        using Fixture fixture = await Fixture.AtBackgroundRemovalAsync();

        fixture.Harness.FakeMeitu.SetScenario(FakeAdapterScenario.Succeed);
        SessionView ran = (await fixture.Service.ExecuteAsync(
            fixture.Id, new WorkflowCommand.StartStep(StepKind.BackgroundRemoval), "tester", CancellationToken.None)).Value;

        ran.Steps.Single(s => s.Step == StepKind.BackgroundRemoval).State.ShouldBe(StepState.ReviewRequired);
        ran.LastAutomationStop.ShouldBeNull();
        (await fixture.AggregateAsync()).Attempts
            .Single(a => a.Step == StepKind.BackgroundRemoval)
            .OutputRevisionId.ShouldNotBeNull();
    }

    // -----------------------------------------------------------------------------
    // Fixture
    // -----------------------------------------------------------------------------

    /// <summary>
    /// A session parked immediately before a producing step, with a helper that runs it and
    /// stops it at a chosen phase.
    /// </summary>
    /// <remarks>
    /// The stop is issued from a different task while the run is in flight, because that is the
    /// only arrangement that resembles what actually happens: the run holds a thread inside
    /// <c>ExecuteAsync</c> and the operator's Stop arrives from the UI thread while that call
    /// has not returned. A helper that set a flag before starting the run would test a
    /// different, easier thing.
    /// </remarks>
    private sealed class Fixture : IDisposable
    {
        private Fixture(SessionServiceHarness harness, ISessionService service, SessionId id)
        {
            Harness = harness;
            Service = service;
            Id = id;
        }

        public SessionServiceHarness Harness { get; }

        public ISessionService Service { get; }

        public SessionId Id { get; }

        public static async Task<Fixture> AtEnhancementAsync()
        {
            SessionServiceHarness harness = new();
            ISessionService service = harness.CreateService();
            SessionId id = (await service.ImportAsync(
                WorkflowType.PrepareAsset, harness.WriteSourcePng(), "art", "tester",
                CancellationToken.None)).Value.Id;

            await Accept(service.ExecuteAsync(
                id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None));

            return new Fixture(harness, service, id);
        }

        public static async Task<Fixture> AtBackgroundRemovalAsync()
        {
            Fixture fixture = await AtEnhancementAsync();

            fixture.Harness.FakeMeitu.SetScenario(FakeAdapterScenario.Succeed);
            SessionView enhanced = await Accept(fixture.Service.ExecuteAsync(
                fixture.Id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester",
                CancellationToken.None));

            Sha256 hash = enhanced.Steps.Single(s => s.Step == StepKind.Enhancement)
                .CurrentRevisionSha256!.Value;
            SessionView approved = await Accept(fixture.Service.ExecuteAsync(
                fixture.Id, new WorkflowCommand.Approve(StepKind.Enhancement, hash), "tester",
                CancellationToken.None));

            // Background Removal cannot start without the reviewed-content authority, so
            // recording it is part of reaching the step rather than something this slice adds.
            ArtefactView reviewed = approved.CurrentArtefact.ShouldNotBeNull();
            await Accept(fixture.Service.ExecuteAsync(
                fixture.Id,
                new WorkflowCommand.SetBackgroundRemovalDecision(
                    BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent,
                    reviewed.RevisionId,
                    reviewed.Sha256),
                "tester",
                CancellationToken.None));

            return fixture;
        }

        /// <summary>Runs the step, requests a stop once the adapter reports <paramref name="phase"/>, and returns the reloaded view.</summary>
        public async Task<SessionView> StopDuringAsync(
            ExternalOperationPhase phase,
            AutomationStopMode mode,
            StepKind step = StepKind.Enhancement)
        {
            Harness.FakeMeitu.SetScenario(FakeAdapterScenario.WaitForStopAt(phase));

            Task<OperationResult<SessionView>> run = Service.ExecuteAsync(
                Id, new WorkflowCommand.StartStep(step), "tester", CancellationToken.None);

            // The adapter signals once it has reported its phase and begun waiting, so the stop
            // lands during the run rather than racing it.
            await Harness.FakeMeitu.HangStarted;

            OperationResult<PrintFlow.Domain.Results.Unit> requested = Service.RequestStop(Id, mode);
            requested.IsSuccess.ShouldBeTrue(
                requested.IsFailure ? requested.Failure.ToString() : string.Empty);

            OperationResult<SessionView> outcome = await run;
            outcome.IsFailure.ShouldBeTrue("a stopped run produces no result");
            outcome.Failure.Code.ShouldBe(FailureCode.Cancelled);

            return (await Service.LoadAsync(Id, CancellationToken.None)).Value;
        }

        public async Task<SessionAggregate> AggregateAsync() =>
            (await Harness.Repository.LoadAsync(Id, CancellationToken.None)).Value!;

        public void Dispose() => Harness.Dispose();

        private static async Task<SessionView> Accept(Task<OperationResult<SessionView>> pending)
        {
            OperationResult<SessionView> result = await pending;
            result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : string.Empty);
            return result.Value;
        }
    }
}
