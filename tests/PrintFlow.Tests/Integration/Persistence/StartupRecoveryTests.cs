using System.IO;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
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
/// Startup crash recovery driven against a real temp database, a real temp workspace and the
/// deterministic fake adapters (Epic 11100 Part 3B §11–§14).
/// </summary>
/// <remarks>
/// Every test here simulates process death the same way: the fake adapter is scripted to hang,
/// the command that drives it is started and then abandoned, and its cancellation token is
/// never signalled. The <c>Running</c> attempt row has already been committed by then, and the
/// abandoned call never writes another row — which is exactly what the database sees when the
/// application is killed mid-attempt, with none of the flakiness of racing a real process.
/// Recovery then runs on a service instance that shares nothing with it but the disk.
/// </remarks>
public sealed class StartupRecoveryTests
{
    // -------------------------------------------------------------------------------------
    // §11: a persisted Running attempt recovers to Interrupted, and Retry starts clean
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task A_crashed_Running_attempt_recovers_to_Interrupted_and_fabricates_no_Revision()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId id = await StartSessionAsync(harness, service);
        AttemptId crashedAttemptId = await CrashDuringAsync(harness, service, id, StepKind.Enhancement);

        // Precondition: the crash left exactly the state recovery is meant to find.
        SessionAggregate beforeRecovery = await LoadAsync(harness, id);
        beforeRecovery.Attempts.Single(a => a.Id == crashedAttemptId).Status.ShouldBe(AttemptStatus.Running);
        beforeRecovery.Steps.Single(s => s.Step == StepKind.Enhancement).State.ShouldBe(StepState.Processing);
        (await harness.Repository.GetAutomationLockAsync(CancellationToken.None)).Value.IsHeld.ShouldBeTrue();

        FakeProcessLiveness liveness = new(ProcessLiveness.Dead);
        StartupRecoveryReport report = await RecoverAsync(harness, liveness);

        report.InterruptedAttemptCount.ShouldBe(1);
        report.ReleasedAutomationLock.ShouldBeTrue();

        SessionAggregate recovered = await LoadAsync(harness, id);
        ProcessingAttempt interrupted = recovered.Attempts.Single(a => a.Id == crashedAttemptId);
        interrupted.Status.ShouldBe(AttemptStatus.Interrupted);
        interrupted.OutputRevisionId.ShouldBeNull();
        interrupted.EndedAtUtc.ShouldNotBeNull();

        // Start time is history and is preserved; the interruption time is recorded separately.
        interrupted.StartedAtUtc.ShouldBe(beforeRecovery.Attempts.Single(a => a.Id == crashedAttemptId).StartedAtUtc);

        recovered.Steps.Single(s => s.Step == StepKind.Enhancement).State.ShouldBe(StepState.Interrupted);
        recovered.Steps.Single(s => s.Step == StepKind.Enhancement).CurrentRevisionId.ShouldBeNull();

        // The crash invariant: no Revision, no PrintOutput, whatever the working directory holds.
        recovered.Revisions.ShouldNotContain(r => r.Operation == OperationKind.Enhance);
        recovered.Outputs.ShouldBeEmpty();

        (await harness.Repository.GetAutomationLockAsync(CancellationToken.None)).Value.IsHeld.ShouldBeFalse();
    }

    [Fact]
    public async Task Retry_after_recovery_gets_a_new_attempt_and_a_new_working_directory()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId id = await StartSessionAsync(harness, service);
        AttemptId crashedAttemptId = await CrashDuringAsync(harness, service, id, StepKind.Enhancement);

        await RecoverAsync(harness, new FakeProcessLiveness(ProcessLiveness.Dead));

        // The next process reopens the session and retries the interrupted step.
        ISessionService afterRestart = harness.CreateService();
        await Must(afterRestart.ExecuteAsync(
            id, new WorkflowCommand.Retry(StepKind.Enhancement), "tester", CancellationToken.None));

        harness.FakeMeitu.SetScenario(FakeAdapterScenario.Succeed);
        OperationResult<SessionView> restarted = await afterRestart.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None);
        restarted.IsSuccess.ShouldBeTrue(restarted.IsFailure ? restarted.Failure.ToString() : "");

        SessionAggregate aggregate = await LoadAsync(harness, id);
        List<ProcessingAttempt> attempts = [.. aggregate.Attempts.Where(a => a.Step == StepKind.Enhancement)];
        attempts.Count.ShouldBe(2);

        // The interrupted attempt stays audit-visible rather than being overwritten.
        ProcessingAttempt old = attempts.Single(a => a.Id == crashedAttemptId);
        old.Status.ShouldBe(AttemptStatus.Interrupted);

        ProcessingAttempt fresh = attempts.Single(a => a.Id != crashedAttemptId);
        fresh.Status.ShouldBe(AttemptStatus.Succeeded);

        // A brand new Working\<attemptId>\ — the interrupted attempt's directory is never reused.
        Revision produced = aggregate.Revisions.Single(r => r.Operation == OperationKind.Enhance);
        produced.File.RelativePath.ShouldContain(fresh.Id.Value.ToString("D"));
        produced.File.RelativePath.ShouldNotContain(crashedAttemptId.Value.ToString("D"));
    }

    // -------------------------------------------------------------------------------------
    // §12: a leftover file in the old Working directory never becomes a result
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task A_partial_file_left_by_a_crash_is_quarantined_and_protected_areas_are_untouched()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId id = await StartSessionAsync(harness, service);

        // A genuinely successful step first: for an adapter-backed step the Revision's file
        // *is* its working copy, so this is the case quarantine must never touch.
        harness.FakeMeitu.SetScenario(FakeAdapterScenario.Succeed);
        OperationResult<SessionView> enhanced = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None);
        enhanced.IsSuccess.ShouldBeTrue(enhanced.IsFailure ? enhanced.Failure.ToString() : "");
        Sha256 enhancedHash = enhanced.Value.Steps.Single(s => s.Step == StepKind.Enhancement).CurrentRevisionSha256!.Value;
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.Approve(StepKind.Enhancement, enhancedHash), "tester", CancellationToken.None));

        AttemptId crashedAttemptId = await CrashDuringAsync(harness, service, id, StepKind.BackgroundRemoval);

        SessionAggregate beforeRecovery = await LoadAsync(harness, id);
        Revision enhancementRevision = beforeRecovery.Revisions.Single(r => r.Operation == OperationKind.Enhance);
        string enhancementFile = harness.FileWorkspace.ResolveAbsolute(enhancementRevision.File);
        string sourceFile = harness.FileWorkspace.ResolveAbsolute(
            beforeRecovery.Revisions.Single(r => r.Operation == OperationKind.Import).File);

        // A half-written adapter output, and an approved artefact recovery must never reach.
        string sessionRoot = harness.FileWorkspace.ResolveAbsoluteDirectory(beforeRecovery.Session.Workspace);
        string partial = Path.Combine(
            sessionRoot, "Working", crashedAttemptId.Value.ToString("D"), "half-written-output.png");
        File.WriteAllBytes(partial, [0x89, 0x50, 0x4E]);
        string approved = Path.Combine(sessionRoot, "Approved", "already-signed-off.png");
        File.WriteAllBytes(approved, SyntheticImages.Png(4, 4, alpha: true));

        StartupRecoveryReport report = await RecoverAsync(harness, new FakeProcessLiveness(ProcessLiveness.Dead));

        SessionAggregate recovered = await LoadAsync(harness, id);
        recovered.Attempts.Single(a => a.Id == crashedAttemptId).Status.ShouldBe(AttemptStatus.Interrupted);
        recovered.Steps.Single(s => s.Step == StepKind.BackgroundRemoval).State.ShouldBe(StepState.Interrupted);
        recovered.Revisions.ShouldNotContain(r => r.Operation == OperationKind.RemoveBackground);

        // Only the crashed attempt's own leftovers moved: its abandoned working copy and the
        // half-written output beside it.
        string[] quarantined = QuarantinedFileNames(harness);
        report.QuarantinedFileCount.ShouldBe(2);
        quarantined.ShouldContain(n => n.EndsWith("half-written-output.png", StringComparison.Ordinal));
        File.Exists(partial).ShouldBeFalse();

        // Everything protected is exactly where it was.
        File.Exists(enhancementFile).ShouldBeTrue("a succeeded attempt's Revision file is not an orphan");
        File.Exists(sourceFile).ShouldBeTrue("the InputSnapshot is never touched");
        File.Exists(approved).ShouldBeTrue("Approved is never touched");
        Directory.Exists(Path.Combine(harness.Workspace.Root, "Baseline")).ShouldBeTrue();
        Directory.Exists(Path.Combine(harness.Workspace.Root, "TestData")).ShouldBeTrue();
    }

    // -------------------------------------------------------------------------------------
    // §13: stale versus live automation lock
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task A_lock_whose_owner_is_dead_is_released()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId id = await StartSessionAsync(harness, service);
        await CrashDuringAsync(harness, service, id, StepKind.Enhancement);

        AutomationLockState held = (await harness.Repository.GetAutomationLockAsync(CancellationToken.None)).Value;
        FakeProcessLiveness liveness = new(ProcessLiveness.Dead);

        StartupRecoveryReport report = await RecoverAsync(harness, liveness);

        // Recovery asked about the owner the lock actually names, not some other process.
        liveness.LastProcessId.ShouldBe(held.ProcessId);
        liveness.LastMachineName.ShouldBe(held.MachineName);

        report.ReleasedAutomationLock.ShouldBeTrue();
        (await harness.Repository.GetAutomationLockAsync(CancellationToken.None)).Value.IsHeld.ShouldBeFalse();
    }

    [Theory]
    [InlineData(ProcessLiveness.Alive)]
    [InlineData(ProcessLiveness.Unknown)]
    public async Task A_lock_whose_owner_is_alive_or_unverifiable_is_never_stolen(ProcessLiveness answer)
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId id = await StartSessionAsync(harness, service);
        AttemptId crashedAttemptId = await CrashDuringAsync(harness, service, id, StepKind.Enhancement);

        StartupRecoveryReport report = await RecoverAsync(harness, new FakeProcessLiveness(answer));

        report.ReleasedAutomationLock.ShouldBeFalse();
        report.Entries.ShouldContain(e => e.Action == StartupRecoveryAction.AutomationLockRetained);

        AutomationLockState after = (await harness.Repository.GetAutomationLockAsync(CancellationToken.None)).Value;
        after.IsHeld.ShouldBeTrue();
        after.SessionId.ShouldBe(id);

        // Failing closed covers the work too: an attempt the possibly-live owner may still be
        // driving must not be declared interrupted behind its back.
        report.InterruptedAttemptCount.ShouldBe(0);
        SessionAggregate untouched = await LoadAsync(harness, id);
        untouched.Attempts.Single(a => a.Id == crashedAttemptId).Status.ShouldBe(AttemptStatus.Running);
        untouched.Steps.Single(s => s.Step == StepKind.Enhancement).State.ShouldBe(StepState.Processing);
    }

    // -------------------------------------------------------------------------------------
    // §14: recovery may itself be interrupted, so running it twice must be safe
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task Running_recovery_twice_changes_nothing_the_second_time()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId id = await StartSessionAsync(harness, service);
        AttemptId crashedAttemptId = await CrashDuringAsync(harness, service, id, StepKind.Enhancement);

        StartupRecoveryReport first = await RecoverAsync(harness, new FakeProcessLiveness(ProcessLiveness.Dead));
        first.InterruptedAttemptCount.ShouldBe(1);
        first.QuarantinedFileCount.ShouldBe(1);

        SessionAggregate afterFirst = await LoadAsync(harness, id);
        string[] quarantinedAfterFirst = QuarantinedFileNames(harness);

        // Move the clock so a second write would be visible rather than coincidentally equal.
        harness.Clock.Advance(TimeSpan.FromMinutes(17));

        StartupRecoveryReport second = await RecoverAsync(harness, new FakeProcessLiveness(ProcessLiveness.Dead));

        second.IsNoOp.ShouldBeTrue();
        second.InterruptedAttemptCount.ShouldBe(0);
        second.QuarantinedFileCount.ShouldBe(0);

        SessionAggregate afterSecond = await LoadAsync(harness, id);
        afterSecond.Attempts.Count.ShouldBe(afterFirst.Attempts.Count);

        ProcessingAttempt before = afterFirst.Attempts.Single(a => a.Id == crashedAttemptId);
        ProcessingAttempt after = afterSecond.Attempts.Single(a => a.Id == crashedAttemptId);
        after.Status.ShouldBe(AttemptStatus.Interrupted);
        after.EndedAtUtc.ShouldBe(before.EndedAtUtc, "the interruption is recorded once, not re-stamped");

        afterSecond.Steps.ShouldBe(afterFirst.Steps);
        QuarantinedFileNames(harness).ShouldBe(quarantinedAfterFirst);
        (await harness.Repository.GetAutomationLockAsync(CancellationToken.None)).Value.IsHeld.ShouldBeFalse();
    }

    [Fact]
    public async Task Recovery_against_a_cleanly_shut_down_workspace_does_nothing()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId id = await StartSessionAsync(harness, service);
        harness.FakeMeitu.SetScenario(FakeAdapterScenario.Succeed);
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None));

        FakeProcessLiveness liveness = new(ProcessLiveness.Dead);
        StartupRecoveryReport report = await RecoverAsync(harness, liveness);

        report.IsNoOp.ShouldBeTrue();

        // Nothing was held, so nothing needed a liveness question in the first place.
        liveness.CallCount.ShouldBe(0);
        Directory.Exists(Path.Combine(harness.Workspace.Root, "Quarantine")).ShouldBeFalse();
    }

    // -------------------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------------------

    private static async Task<SessionId> StartSessionAsync(SessionServiceHarness harness, ISessionService service)
    {
        string source = harness.WriteSourcePng();
        OperationResult<SessionView> imported = await service.ImportAsync(
            WorkflowType.PrepareAsset, source, "art", "tester", CancellationToken.None);
        imported.IsSuccess.ShouldBeTrue(imported.IsFailure ? imported.Failure.ToString() : "");

        SessionId id = imported.Value.Id;
        await Must(service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None));
        return id;
    }

    /// <summary>
    /// Starts an adapter-backed step and abandons it mid-call, leaving a committed
    /// <c>Running</c> attempt and no outcome — the database's view of a killed process.
    /// </summary>
    private static async Task<AttemptId> CrashDuringAsync(
        SessionServiceHarness harness, ISessionService service, SessionId id, StepKind step)
    {
        harness.FakeMeitu.SetScenario(FakeAdapterScenario.HangUntilCancelled);

        // Deliberately never awaited and never cancelled: the hung call writes nothing more,
        // which is precisely what a process that stopped existing does.
        _ = service.ExecuteAsync(id, new WorkflowCommand.StartStep(step), "tester", CancellationToken.None);
        await harness.FakeMeitu.HangStarted;

        SessionAggregate aggregate = await LoadAsync(harness, id);
        return aggregate.Attempts.Single(a => a.Step == step && a.Status == AttemptStatus.Running).Id;
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

    private static async Task<SessionAggregate> LoadAsync(SessionServiceHarness harness, SessionId id) =>
        (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;

    /// <summary>Quarantined artefacts, excluding the reason note written beside each one.</summary>
    private static string[] QuarantinedFileNames(SessionServiceHarness harness)
    {
        string quarantine = Path.Combine(harness.Workspace.Root, "Quarantine");
        if (!Directory.Exists(quarantine))
        {
            return [];
        }

        return [.. Directory.EnumerateFiles(quarantine)
            .Select(Path.GetFileName)
            .Where(name => !name!.EndsWith(".reason.txt", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)!];
    }

    private static async Task Must(Task<OperationResult<SessionView>> call)
    {
        OperationResult<SessionView> result = await call;
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : "");
    }
}
