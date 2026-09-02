using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using PrintFlow.App.Composition;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Adapters.Meitu;
using PrintFlow.Infrastructure.Adapters.Photoshop;
using PrintFlow.Infrastructure.Configuration;
using PrintFlow.Infrastructure.Sqlite;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;
using Xunit.Abstractions;

using static PrintFlow.Tests.Fixtures.WorkstationObservation;

namespace PrintFlow.Tests.Smoke;

/// <summary>
/// The Epic 11600 Part B multi-job Production soak: forty synthetic jobs in four stages, with a
/// PrintFlow restart in the middle, and the bounded resource observations around them.
/// </summary>
/// <remarks>
/// Opt-in and inert by default, like every workstation smoke: the signed preset and the accepted
/// binaries exist on exactly one machine. <c>PRINTFLOW_SOAK_PHASE0=1</c> runs the Phase 0 Meitu
/// baseline gate; <c>PRINTFLOW_SOAK_SMOKE=1</c> runs the soak itself.
/// <para>
/// <b>What this adds to the recovery smoke.</b> Part A proved that a job, and then another job,
/// and then a job after a restart, all succeed. What it could not answer is whether the
/// workstation is still the same workstation after forty of them — whether Photoshop's accumulated
/// PrintFlow documents start to cost something, whether Meitu keeps returning to its signed empty
/// state, whether handles climb, whether the fortieth job takes longer than the first.
/// </para>
/// <para>
/// <b>What it will not do.</b> It injects no failures, kills no application, corrupts nothing,
/// closes no document and answers no dialog — deliberate failure injection is Part C. It stops on
/// the §5 conditions rather than automating through them, and a stop is reported as a stop.
/// </para>
/// </remarks>
public sealed class ProductionMultiJobSoakSmoke(ITestOutputHelper output)
{
    private const string SoakVariable = "PRINTFLOW_SOAK_SMOKE";
    private const string Phase0Variable = "PRINTFLOW_SOAK_PHASE0";

    /// <summary>The name prefix every artefact this smoke generates carries.</summary>
    private const string SmokeArtefactPrefix = "PF_11600B_";

    /// <summary>
    /// The prefix family PrintFlow's own synthetic soak documents carry, Part A's included.
    /// </summary>
    /// <remarks>
    /// Used only by this harness's preflight courtesy check, which asks whether an operator has
    /// unsaved work open before a long unattended run starts. It is emphatically not the product's
    /// ownership rule: that is <see cref="IPhotoshopUiDriver.ProbeDocumentIdentityAsync"/>'s
    /// absolute-path comparison, and nothing here relaxes it.
    /// </remarks>
    private const string SyntheticDocumentPrefix = "PF_11600";

    private const string UnsavedDocumentMarker = "*";

    /// <summary>
    /// Jobs that failed on their first attempt, kept whatever the retry then did (§15).
    /// </summary>
    /// <remarks>
    /// A naturally occurring failure is soak evidence, so it is recorded separately rather than
    /// replaced by the retry that follows it. xunit constructs one instance per test, so this
    /// belongs to the run rather than to the class.
    /// </remarks>
    private readonly List<JobRecord> _naturalFailures = [];

    // -----------------------------------------------------------------------------------
    // Phase 0 — the Meitu baseline gate
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// Establishes that the accepted Meitu baseline is stable before any soak begins
    /// (Epic 11600 Part B §2).
    /// </summary>
    /// <remarks>
    /// Part A's live smoke found the workstation holding two installed Meitu versions, with the
    /// accepted one still digest-exact but a newer one able to take the single-instance slot. This
    /// gate reports the whole version picture, refuses to proceed if anything but the accepted
    /// binary is running, and then proves the accepted binary still reaches a working enhancement
    /// from a cold start.
    /// </remarks>
    [Fact]
    public async Task Phase_0_the_accepted_meitu_baseline_is_stable_on_this_workstation()
    {
        if (Environment.GetEnvironmentVariable(Phase0Variable) != "1")
        {
            // Inert by design; see the class remarks.
            return;
        }

        PrintFlowConfiguration configuration = LoadCommittedConfiguration();
        string workspaceRoot = Path.GetFullPath(configuration.Workspace.Root);
        string qaRoot = CreateQaDirectory(workspaceRoot, out string token);

        // The accepted path is read from the verified preset, never from a constant here.
        MeituBaseline baseline = VerifiedMeituBaseline(configuration, workspaceRoot);
        MeituVersionTopology before = MeituVersionTopology.Read(baseline.ExecutablePath);

        output.WriteLine("=== Phase 0: Meitu version topology ===");
        ReportTopology(baseline, before);

        before.AcceptedExecutableExists.ShouldBeTrue("the accepted Meitu binary must be present.");
        before.OnlyAcceptedIsRunning.ShouldBeTrue(
            "Phase 0 refuses to proceed while an unaccepted Meitu instance holds the single-instance slot.");

        // One synthetic enhancement, from whatever cold or warm state the workstation is in.
        SqliteConnectionFactory factory = CreateQaDatabase(qaRoot, "printflow-soak-phase0.db");
        using ServiceProvider services =
            ServiceRegistration.BuildServiceProvider(configuration, workspaceRoot, factory);

        output.WriteLine(string.Empty);
        if (!await ReportReadinessAsync(services, "Phase 0"))
        {
            return;
        }

        output.WriteLine(string.Empty);
        output.WriteLine("=== Phase 0: controlled synthetic Meitu enhancement ===");
        JobRecord job = await RunJobAsync(services, workspaceRoot, qaRoot, 1, JobKind.Meitu, $"{token}_P0");
        ReportJob(job);

        MeituVersionTopology after = MeituVersionTopology.Read(baseline.ExecutablePath);
        output.WriteLine(string.Empty);
        output.WriteLine("=== Phase 0: Meitu version topology after ===");
        ReportTopology(baseline, after);

        job.Succeeded.ShouldBeTrue("the Phase 0 enhancement must succeed against the accepted binary.");
        job.LockHeldAfter.ShouldBeFalse();
        after.Fingerprint.ShouldBe(before.Fingerprint,
            "the enhancement must not have changed which Meitu versions are installed or running.");
        after.OnlyAcceptedIsRunning.ShouldBeTrue();

        output.WriteLine(string.Empty);
        output.WriteLine("MEITU BASELINE STABLE — retain accepted " + baseline.AcceptedVersion);
    }

    // -----------------------------------------------------------------------------------
    // The soak
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// Forty synthetic Production jobs across four stages, with a PrintFlow restart between
    /// Stage&#160;C and Stage&#160;D (Epic 11600 Part B §4, §6, §7, §12, §13, §14).
    /// </summary>
    [Fact]
    public async Task Forty_synthetic_production_jobs_leave_the_workstation_as_they_found_it()
    {
        if (Environment.GetEnvironmentVariable(SoakVariable) != "1")
        {
            // Inert by design; see the class remarks.
            return;
        }

        PrintFlowConfiguration configuration = LoadCommittedConfiguration();
        string workspaceRoot = Path.GetFullPath(configuration.Workspace.Root);
        string qaRoot = CreateQaDirectory(workspaceRoot, out string token);
        MeituBaseline baseline = VerifiedMeituBaseline(configuration, workspaceRoot);

        output.WriteLine("=== configuration ===");
        output.WriteLine($"committed mode          : {configuration.Adapters.Mode}");
        output.WriteLine("override                : none");
        output.WriteLine($"configured preset       : {configuration.Preset.Id} {configuration.Preset.Version}");
        output.WriteLine($"workspace root          : {workspaceRoot}");
        output.WriteLine($"QA directory            : {qaRoot}");
        output.WriteLine($"accepted Meitu version  : {baseline.AcceptedVersion}");
        output.WriteLine(string.Empty);

        // ---- preflight ----------------------------------------------------------------------

        if (UnsavedForeignPhotoshopDocument() is { } unsaved)
        {
            output.WriteLine("=== SOAK BLOCKED ===");
            output.WriteLine($"Photoshop is holding unsaved work that is not PrintFlow's: {unsaved}");
            output.WriteLine("Nothing was run. PrintFlow does not answer a prompt it did not raise.");
            return;
        }

        MeituVersionTopology topologyBefore = MeituVersionTopology.Read(baseline.ExecutablePath);
        ReportTopology(baseline, topologyBefore);
        topologyBefore.OnlyAcceptedIsRunning.ShouldBeTrue(
            "the soak refuses to start while an unaccepted Meitu instance is running (§5).");

        WorkspaceCensus censusBefore = WorkspaceCensus.Take(workspaceRoot);
        output.WriteLine(string.Empty);
        output.WriteLine("=== workstation before ===");
        output.WriteLine($"sessions                : {censusBefore.SessionDirectories.Length}");
        output.WriteLine($"Comparison files        : {censusBefore.ComparisonFiles}");
        output.WriteLine($"Quarantine files        : {censusBefore.QuarantineFiles}");
        output.WriteLine($"QA directories          : {censusBefore.QaDirectories}");
        output.WriteLine($"Photoshop title         : {PhotoshopTitle()}");

        // A metadata fingerprint of a bounded sample of pre-existing sessions, so §13's
        // "no historical session changed" claim rests on something read rather than on the
        // directory count alone. Metadata only: sizes, names and timestamps, never content.
        PreExistingSessionSample sample = PreExistingSessionSample.Take(workspaceRoot, censusBefore, count: 5);
        output.WriteLine($"sampled prior sessions  : {sample.Describe()}");

        SqliteConnectionFactory factory = CreateQaDatabase(qaRoot, "printflow-soak.db");

        List<JobRecord> jobs = [];
        List<Checkpoint> checkpoints = [];

        // ---- process 1: Stages A, B and C -----------------------------------------------------

        using (ServiceProvider services =
               ServiceRegistration.BuildServiceProvider(configuration, workspaceRoot, factory))
        {
            output.WriteLine(string.Empty);
            output.WriteLine("=== process 1 ===");
            if (!await ReportReadinessAsync(services, "process 1"))
            {
                return;
            }

            checkpoints.Add(Checkpoint.Take("before job 1"));
            ReportCheckpoint(checkpoints[^1]);

            // Stage A — mixed warm-up: P M P M P M P M. Rapid adapter switching, and the
            // baseline timings every later stage is compared against.
            output.WriteLine(string.Empty);
            output.WriteLine("=== Stage A — mixed warm-up (8 jobs) ===");
            for (int i = 1; i <= 8; i++)
            {
                jobs.Add(await RunAndReportAsync(
                    services, workspaceRoot, qaRoot, token, baseline, jobs,
                    number: i, kind: i % 2 == 1 ? JobKind.Photoshop : JobKind.Meitu));
            }

            checkpoints.Add(Checkpoint.Take("after job 8"));
            ReportCheckpoint(checkpoints[^1]);

            // Stage B — Photoshop accumulation: twelve consecutive P jobs, exercising Policy A
            // on purpose. Nothing here closes a document to improve the result (§4, §9).
            output.WriteLine(string.Empty);
            output.WriteLine("=== Stage B — Photoshop accumulation (12 jobs) ===");
            for (int i = 9; i <= 20; i++)
            {
                jobs.Add(await RunAndReportAsync(
                    services, workspaceRoot, qaRoot, token, baseline, jobs,
                    number: i, kind: JobKind.Photoshop));
            }

            checkpoints.Add(Checkpoint.Take("after job 20"));
            ReportCheckpoint(checkpoints[^1]);

            // Stage C — Meitu sustained reuse: twelve consecutive M jobs, each of which must
            // re-enter through the accepted neutral state without manual intervention (§11).
            output.WriteLine(string.Empty);
            output.WriteLine("=== Stage C — Meitu sustained reuse (12 jobs) ===");
            for (int i = 21; i <= 32; i++)
            {
                jobs.Add(await RunAndReportAsync(
                    services, workspaceRoot, qaRoot, token, baseline, jobs,
                    number: i, kind: JobKind.Meitu));
            }

            checkpoints.Add(Checkpoint.Take("after job 32"));
            ReportCheckpoint(checkpoints[^1]);
        }

        // ---- the restart checkpoint (§14) ------------------------------------------------------
        //
        // PrintFlow only. Photoshop and Meitu are deliberately left running across the boundary:
        // the claim is that a long-running external application does not require an equally
        // long-running PrintFlow process.

        output.WriteLine(string.Empty);
        output.WriteLine("=== restart checkpoint ===");
        output.WriteLine($"external apps still up  : {ExternalApplicationProbe.RunningCount()}");

        using (ServiceProvider restarted =
               ServiceRegistration.BuildServiceProvider(configuration, workspaceRoot, factory))
        {
            ISessionRepository repository = restarted.GetRequiredService<ISessionRepository>();

            bool lockBeforeRestart =
                (await repository.GetAutomationLockAsync(CancellationToken.None)).Value.IsHeld;

            OperationResult<StartupRecoveryReport> recovered = await restarted
                .GetRequiredService<IStartupRecoveryService>()
                .RecoverAsync(CancellationToken.None);
            recovered.IsSuccess.ShouldBeTrue(
                recovered.IsFailure ? recovered.Failure.ToString() : string.Empty);

            int quarantined = recovered.Value.Entries.Count(e =>
                e.Action == StartupRecoveryAction.WorkingFileQuarantined);

            output.WriteLine($"lock before restart     : {(lockBeforeRestart ? "HELD" : "free")}");
            output.WriteLine($"startup interrupted     : {recovered.Value.InterruptedAttemptCount}");
            output.WriteLine($"unexpected quarantine   : {quarantined}");

            lockBeforeRestart.ShouldBeFalse("thirty-two completed jobs release the lock.");
            recovered.Value.InterruptedAttemptCount.ShouldBe(0);
            quarantined.ShouldBe(0);
            recovered.Value.Entries.ShouldNotContain(e =>
                e.Action == StartupRecoveryAction.RecoveryFailed);

            // Every output produced before the restart, re-hashed from disk through a graph that
            // shares nothing with the one that wrote it.
            int rehashed = 0;
            foreach (JobRecord earlier in jobs.Where(j => j.Succeeded))
            {
                SessionAggregate reloaded =
                    (await repository.LoadAsync(earlier.Session, CancellationToken.None)).Value!;
                reloaded.Steps.Single(s => s.Step == earlier.Step).State.ShouldBe(StepState.ReviewRequired);

                string absolute = Path.Combine(workspaceRoot, earlier.RelativePath.Replace('/', '\\'));
                FileDigest(absolute).ShouldBe(earlier.Sha256.ToString(),
                    $"job {earlier.Number}'s output must be hash-identical across the restart.");
                rehashed++;
            }

            output.WriteLine($"prior outputs re-hashed : {rehashed} (all identical)");

            output.WriteLine(string.Empty);
            output.WriteLine("=== process 2 ===");
            if (!await ReportReadinessAsync(restarted, "process 2"))
            {
                return;
            }

            checkpoints.Add(Checkpoint.Take("after PrintFlow restart"));
            ReportCheckpoint(checkpoints[^1]);

            // Stage D — restart boundary: P M P M P M P M through the new process.
            output.WriteLine(string.Empty);
            output.WriteLine("=== Stage D — restart boundary (8 jobs) ===");
            for (int i = 33; i <= 40; i++)
            {
                jobs.Add(await RunAndReportAsync(
                    restarted, workspaceRoot, qaRoot, token, baseline, jobs,
                    number: i, kind: i % 2 == 1 ? JobKind.Photoshop : JobKind.Meitu));
            }

            checkpoints.Add(Checkpoint.Take("after job 40"));
            ReportCheckpoint(checkpoints[^1]);
        }

        // ---- what the run looked like ----------------------------------------------------------

        ReportPerJobTable(jobs);
        ReportTiming(jobs);
        ReportCheckpointTable(checkpoints);

        MeituVersionTopology topologyAfter = MeituVersionTopology.Read(baseline.ExecutablePath);
        output.WriteLine(string.Empty);
        output.WriteLine("=== Meitu topology after ===");
        ReportTopology(baseline, topologyAfter);

        output.WriteLine(string.Empty);
        output.WriteLine("=== workstation after ===");
        output.WriteLine($"Photoshop title         : {PhotoshopTitle()}");
        censusBefore.ReportAndAssert(
            workspaceRoot, [.. jobs.Select(j => j.SessionDirectory)], output);
        sample.ReportAndAssert(workspaceRoot, output);

        // ---- the claims the soak exists to make --------------------------------------------------

        int succeeded = jobs.Count(j => j.Succeeded);
        output.WriteLine(string.Empty);
        output.WriteLine($"jobs run                : {jobs.Count}");
        output.WriteLine($"jobs succeeded          : {succeeded}");
        output.WriteLine($"jobs failed             : {jobs.Count - succeeded}");

        jobs.ShouldAllBe(j => !j.LockHeldAfter, "no job may leave the automation lock held.");
        jobs.Where(j => j.Succeeded).Select(j => j.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase)
            .Count().ShouldBe(succeeded, "no job may reuse another session's output path.");
        topologyAfter.OnlyAcceptedIsRunning.ShouldBeTrue();
        succeeded.ShouldBe(40, "the soak requires forty successful synthetic Production jobs.");
    }

    // -----------------------------------------------------------------------------------
    // One job
    // -----------------------------------------------------------------------------------

    private enum JobKind
    {
        Photoshop,
        Meitu,
    }

    /// <summary>Everything §6 asks to be recorded for one job.</summary>
    private sealed record JobRecord(
        int Number,
        JobKind Kind,
        string Label,
        SessionId Session,
        string SessionDirectory,
        StepKind Step,
        string AdapterId,
        DateTimeOffset StartedAt,
        DateTimeOffset EndedAt,
        TimeSpan Duration,
        int AttemptCount,
        AttemptStatus AttemptStatus,
        StepState StepState,
        bool LockHeldBefore,
        bool LockHeldAfter,
        int ExternalBefore,
        int ExternalAfter,
        bool Succeeded,
        string RelativePath,
        Sha256 Sha256,
        long ByteLength,
        int? PixelWidth,
        int? PixelHeight,
        string Format,
        bool RevisionCreated,
        string? AdapterNotes,
        string? FailureCode,
        string? FailureDetail,
        string PhotoshopTitleAfter,
        WindowClassCensus PhotoshopWindowsAfter,
        bool WasRetry)
    {
        public bool Launched => ExternalAfter > ExternalBefore;

        /// <summary>
        /// Whether Meitu reported that it could not get back to its signed empty state. A
        /// successful output with a cleanup warning is called out separately (§11).
        /// </summary>
        public bool CleanupWarning =>
            AdapterNotes?.Contains("WARNING", StringComparison.Ordinal) ?? false;
    }

    /// <summary>
    /// Runs one job, reports it, and enforces the §5 conditions that must stop the soak.
    /// </summary>
    /// <remarks>
    /// A failure is recorded rather than erased (§15) and one retry is attempted, because a
    /// naturally occurring failure is soak evidence. What is never done is automating through a
    /// §5 condition to keep the count moving: an unaccepted Meitu, a held lock or a lost
    /// pre-existing session stops the run where it stands.
    /// </remarks>
    private async Task<JobRecord> RunAndReportAsync(
        ServiceProvider services,
        string workspaceRoot,
        string qaRoot,
        string token,
        MeituBaseline baseline,
        IReadOnlyList<JobRecord> soFar,
        int number,
        JobKind kind)
    {
        JobRecord job = await RunJobAsync(services, workspaceRoot, qaRoot, number, kind, $"{token}_{number:D2}");
        ReportJob(job);

        if (!job.Succeeded)
        {
            // §15: restore nothing, hide nothing, and try exactly once more. The retry is a new
            // attempt against the same session; the failure stays in history either way.
            _naturalFailures.Add(job);
            output.WriteLine($"  job {number} failed — running one retry (§15)");
            JobRecord retry = await RetryJobAsync(services, workspaceRoot, number, kind, job);
            ReportJob(retry);
            job = retry;
        }

        // §5 early-stop conditions, asserted rather than logged.
        job.LockHeldAfter.ShouldBeFalse(
            $"job {number} left the automation lock held.");
        MeituVersionTopology.Read(baseline.ExecutablePath).OnlyAcceptedIsRunning.ShouldBeTrue(
            $"an unaccepted Meitu instance appeared during job {number}.");
        soFar.ShouldNotContain(
            earlier => earlier.Succeeded && job.Succeeded &&
                       string.Equals(earlier.RelativePath, job.RelativePath, StringComparison.OrdinalIgnoreCase),
            $"job {number} selected an output path another session already owns.");
        job.Succeeded.ShouldBeTrue(
            $"job {number} failed and its retry failed too: {job.FailureCode} {job.FailureDetail}");

        return job;
    }

    private async Task<JobRecord> RunJobAsync(
        ServiceProvider services,
        string workspaceRoot,
        string qaRoot,
        int number,
        JobKind kind,
        string label)
    {
        ISessionService service = services.GetRequiredService<ISessionService>();

        string sourcePath = Path.Combine(qaRoot, $"{SmokeArtefactPrefix}{label}.png");
        File.WriteAllBytes(sourcePath, kind == JobKind.Photoshop
            ? SyntheticImages.PngWithAlpha(1200, 800, static (_, _) => byte.MaxValue, dpi: 240)
            : SyntheticImages.Png(320, 240, dpi: 300, alpha: true));

        WorkflowType workflow = kind == JobKind.Photoshop
            ? WorkflowType.GeneratePrintTiff
            : WorkflowType.PrepareAsset;

        SessionId id = Accept(await service.ImportAsync(
            workflow, sourcePath, $"{SmokeArtefactPrefix}{label}", "qa", CancellationToken.None)).Id;

        Accept(await service.ExecuteAsync(
            id, new WorkflowCommand.ConfirmOriginal("synthetic soak fixture"), "qa", CancellationToken.None));

        if (kind == JobKind.Photoshop)
        {
            Accept(await service.ExecuteAsync(
                id,
                new WorkflowCommand.SetPrintDimensions(
                    PrintDimensions.FromMillimetres(50.8, 100, SizePreset.Custom)),
                "qa",
                CancellationToken.None));
            Accept(await service.ExecuteAsync(
                id,
                new WorkflowCommand.SelectWhiteUnderbaseBranch(
                    WhiteUnderbaseBranch.W1_1px, "synthetic solid design"),
                "qa",
                CancellationToken.None));
        }

        return await RunStepAsync(services, workspaceRoot, number, kind, label, id, retry: false);
    }

    /// <summary>
    /// Re-runs a naturally failed job once, as a new attempt against the same session (§15).
    /// </summary>
    /// <remarks>
    /// <c>Retry</c> and <c>StartStep</c> are two commands and both are needed. <c>Retry</c> only
    /// returns the failed step to <c>Waiting</c> — it deliberately does not re-run anything, so
    /// that reopening a failed step and choosing to run it again stay separate operator
    /// decisions. A harness that sent only the first would record a step sitting at
    /// <c>Waiting</c> as a failed retry, which is what the first soak run did.
    /// <para>
    /// Nothing is restored, deleted or tidied in between. The failed attempt stays in history,
    /// its leftovers stay in its own attempt folder, and the retry writes to a new one.
    /// </para>
    /// </remarks>
    private async Task<JobRecord> RetryJobAsync(
        ServiceProvider services, string workspaceRoot, int number, JobKind kind, JobRecord failed)
    {
        ISessionService service = services.GetRequiredService<ISessionService>();
        StepKind step = kind == JobKind.Photoshop ? StepKind.PhotoshopOutput : StepKind.Enhancement;

        Accept(await service.ExecuteAsync(
            failed.Session, new WorkflowCommand.Retry(step), "qa", CancellationToken.None));

        return await RunStepAsync(
            services, workspaceRoot, number, kind, failed.Label, failed.Session, retry: true);
    }

    /// <summary>
    /// Runs one adapter-backed step and records the bounded evidence §6 asks for around it.
    /// </summary>
    /// <remarks>
    /// The lock, the external process count and the Photoshop window census are all read on both
    /// sides of the operation rather than only afterwards: "the lock is free now" says nothing on
    /// its own, and "an application was launched" is a difference rather than a state.
    /// </remarks>
    private async Task<JobRecord> RunStepAsync(
        ServiceProvider services,
        string workspaceRoot,
        int number,
        JobKind kind,
        string label,
        SessionId id,
        bool retry)
    {
        ISessionService service = services.GetRequiredService<ISessionService>();
        ISessionRepository repository = services.GetRequiredService<ISessionRepository>();

        StepKind step = kind == JobKind.Photoshop ? StepKind.PhotoshopOutput : StepKind.Enhancement;
        OperationKind operation = kind == JobKind.Photoshop
            ? OperationKind.PhotoshopOutput
            : OperationKind.Enhance;

        bool lockBefore = (await repository.GetAutomationLockAsync(CancellationToken.None)).Value.IsHeld;
        int externalBefore = ExternalApplicationProbe.RunningCount();

        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        long ticks = Stopwatch.GetTimestamp();

        OperationResult<SessionView> produced = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(step), "qa", CancellationToken.None);

        TimeSpan duration = Stopwatch.GetElapsedTime(ticks);
        DateTimeOffset endedAt = DateTimeOffset.UtcNow;

        int externalAfter = ExternalApplicationProbe.RunningCount();
        bool lockAfter = (await repository.GetAutomationLockAsync(CancellationToken.None)).Value.IsHeld;

        SessionAggregate aggregate = (await repository.LoadAsync(id, CancellationToken.None)).Value!;
        ProcessingAttempt attempt = aggregate.Attempts.Last(a => a.Step == step);
        SessionStep sessionStep = aggregate.Steps.Single(s => s.Step == step);
        Revision? revision = aggregate.Revisions.LastOrDefault(r => r.Operation == operation);

        string adapterId = kind == JobKind.Photoshop
            ? services.GetRequiredService<IPhotoshopOutputProcessor>().AdapterId
            : services.GetRequiredService<IMeituProcessor>().AdapterId;

        bool succeeded = produced.IsSuccess && revision is not null;

        if (succeeded)
        {
            // Validation, not the adapter's word (§6). The file must exist, and the step must
            // have landed on ReviewRequired rather than anywhere else.
            File.Exists(Path.Combine(workspaceRoot, revision!.File.RelativePath.Replace('/', '\\')))
                .ShouldBeTrue($"job {number}'s validated output must exist on disk.");
            sessionStep.State.ShouldBe(StepState.ReviewRequired);
        }

        return new JobRecord(
            number,
            kind,
            label,
            id,
            aggregate.Session.Workspace.RelativePath.Split('/')[^1],
            step,
            adapterId,
            startedAt,
            endedAt,
            duration,
            aggregate.Attempts.Count(a => a.Step == step),
            attempt.Status,
            sessionStep.State,
            lockBefore,
            lockAfter,
            externalBefore,
            externalAfter,
            succeeded,
            revision?.File.RelativePath ?? string.Empty,
            revision?.Facts.Sha256 ?? default,
            revision?.Facts.ByteLength ?? 0,
            revision?.Facts.PixelWidth,
            revision?.Facts.PixelHeight,
            revision?.Facts.Format.ToString() ?? "-",
            revision is not null,
            attempt.AdapterNotes,
            produced.IsFailure ? produced.Failure.Code.ToString() : null,
            produced.IsFailure ? produced.Failure.TechnicalDetail : null,
            PhotoshopTitle(),
            kind == JobKind.Photoshop ? WindowClassCensus.Read("Photoshop") : WindowClassCensus.Empty,
            retry);
    }

    // -----------------------------------------------------------------------------------
    // Checkpoints (§7)
    // -----------------------------------------------------------------------------------

    /// <summary>The bounded resource reading taken at each of the §7 checkpoints.</summary>
    private sealed record Checkpoint(
        string Name,
        DateTimeOffset At,
        ProcessVitals PrintFlow,
        ProcessVitals Photoshop,
        ProcessVitals Meitu,
        WindowClassCensus PhotoshopWindows)
    {
        public static Checkpoint Take(string name) => new(
            name,
            DateTimeOffset.Now,
            ProcessVitals.ReadSelf(),
            ProcessVitals.Read("Photoshop"),
            ProcessVitals.Read("XiuXiu"),
            WindowClassCensus.Read("Photoshop"));
    }

    // -----------------------------------------------------------------------------------
    // Reporting
    // -----------------------------------------------------------------------------------

    private void ReportJob(JobRecord job)
    {
        string verdict = job.Succeeded ? "OK " : "FAIL";
        output.WriteLine(
            $"[{verdict}] job {job.Number,2}{(job.WasRetry ? "r" : " ")}{(job.Kind == JobKind.Photoshop ? "P" : "M")} " +
            $"{job.Duration.TotalSeconds,6:F1}s  " +
            $"apps {job.ExternalBefore}->{job.ExternalAfter} {(job.Launched ? "LAUNCHED" : "reused")}  " +
            $"attempts {job.AttemptCount}  {job.AttemptStatus}/{job.StepState}  " +
            $"lock {(job.LockHeldBefore ? "held" : "free")}->{(job.LockHeldAfter ? "held" : "free")}");

        if (job.Succeeded)
        {
            output.WriteLine($"         out  {job.RelativePath}");
            output.WriteLine($"         sha  {job.Sha256.ToString()[..16]}…  " +
                             $"{job.PixelWidth}x{job.PixelHeight} {job.Format} {job.ByteLength} bytes");
        }
        else
        {
            output.WriteLine($"         code {job.FailureCode}");
            output.WriteLine($"         why  {job.FailureDetail}");
        }

        if (job.Kind == JobKind.Meitu)
        {
            output.WriteLine($"         {CleanupNote(job)}");
        }
        else
        {
            output.WriteLine($"         ps   {job.PhotoshopTitleAfter}");
            output.WriteLine($"         win  {job.PhotoshopWindowsAfter.Describe()}");
        }
    }

    /// <summary>
    /// The tail of an attempt's adapter notes from its cleanup clause onwards, which is where
    /// Meitu records whether it got back to its signed empty state (§11).
    /// </summary>
    private static string CleanupNote(JobRecord job)
    {
        if (job.AdapterNotes is null)
        {
            return "(no adapter notes)";
        }

        int at = job.AdapterNotes.LastIndexOf("cleanup ", StringComparison.Ordinal);
        return at < 0 ? job.AdapterNotes : job.AdapterNotes[at..];
    }

    private void ReportCheckpoint(Checkpoint checkpoint)
    {
        output.WriteLine($"--- checkpoint: {checkpoint.Name} ({checkpoint.At:HH:mm:ss}) ---");
        output.WriteLine($"    PrintFlow  {checkpoint.PrintFlow.Describe()}");
        output.WriteLine($"    Photoshop  {checkpoint.Photoshop.Describe()}");
        output.WriteLine($"    Meitu      {checkpoint.Meitu.Describe()}");
        output.WriteLine($"    PS windows {checkpoint.PhotoshopWindows.Describe()}");
    }

    private void ReportCheckpointTable(IReadOnlyList<Checkpoint> checkpoints)
    {
        output.WriteLine(string.Empty);
        output.WriteLine("=== resource checkpoints (absolute, and delta from the first) ===");

        Checkpoint first = checkpoints[0];
        foreach (Checkpoint point in checkpoints)
        {
            output.WriteLine($"{point.Name}");
            Row("PrintFlow", point.PrintFlow, first.PrintFlow);
            Row("Photoshop", point.Photoshop, first.Photoshop);
            Row("Meitu    ", point.Meitu, first.Meitu);
            output.WriteLine($"  PS windows  {point.PhotoshopWindows.Describe()}");
        }

        void Row(string name, ProcessVitals now, ProcessVitals start)
        {
            if (!now.Present)
            {
                output.WriteLine($"  {name}   (not running)");
                return;
            }

            output.WriteLine(
                $"  {name}   {now.Describe()}   " +
                $"Δws {Delta(now.WorkingSet - start.WorkingSet)} MB  " +
                $"Δpriv {Delta(now.PrivateMemory - start.PrivateMemory)} MB  " +
                $"Δhandles {(now.Handles - start.Handles):+#;-#;0}  " +
                $"Δthreads {(now.Threads - start.Threads):+#;-#;0}");
        }

        static string Delta(long bytes) => (bytes / (1024 * 1024)).ToString("+#;-#;0");
    }

    private void ReportPerJobTable(IReadOnlyList<JobRecord> jobs)
    {
        output.WriteLine(string.Empty);
        output.WriteLine("=== per-job census ===");
        output.WriteLine("  #  k  dur(s)  apps        attempts  status          lock         output");
        foreach (JobRecord job in jobs)
        {
            output.WriteLine(
                $" {job.Number,2}  {(job.Kind == JobKind.Photoshop ? "P" : "M")}  " +
                $"{job.Duration.TotalSeconds,6:F1}  " +
                $"{job.ExternalBefore}->{job.ExternalAfter} {(job.Launched ? "launch" : "reuse "),-6}  " +
                $"{job.AttemptCount,8}  {job.AttemptStatus,-9}/{job.StepState,-14}  " +
                $"{(job.LockHeldAfter ? "HELD" : "free"),-5}  {job.RelativePath}");
        }

        JobRecord[] warned = [.. jobs.Where(j => j.Kind == JobKind.Meitu && j.CleanupWarning)];
        output.WriteLine(string.Empty);
        output.WriteLine($"Meitu cleanup warnings  : {warned.Length}");
        foreach (JobRecord job in warned)
        {
            output.WriteLine($"  job {job.Number}: {job.AdapterNotes}");
        }

        output.WriteLine(string.Empty);
        output.WriteLine($"natural failures        : {_naturalFailures.Count} (§15)");
        foreach (JobRecord failure in _naturalFailures)
        {
            output.WriteLine(
                $"  job {failure.Number} ({failure.Kind}) {failure.FailureCode} — " +
                $"attempt {failure.AttemptStatus}, step {failure.StepState}, " +
                $"lock {(failure.LockHeldAfter ? "HELD" : "free")}, " +
                $"revision {(failure.RevisionCreated ? "CREATED" : "none")}");
            output.WriteLine($"    {failure.FailureDetail}");
        }

        output.WriteLine($"applications launched   : {jobs.Count(j => j.Launched)} of {jobs.Count}");
        output.WriteLine($"revisions created       : {jobs.Count(j => j.RevisionCreated)}");
        output.WriteLine($"ReviewRequired          : {jobs.Count(j => j.StepState == StepState.ReviewRequired)}");
    }

    /// <summary>
    /// Photoshop and Meitu timing distributions, kept apart because §12 requires it: they are
    /// different operations against different applications and a combined figure would describe
    /// neither.
    /// </summary>
    private void ReportTiming(IReadOnlyList<JobRecord> jobs)
    {
        output.WriteLine(string.Empty);
        output.WriteLine("=== timing (seconds, per adapter) ===");

        foreach (JobKind kind in (JobKind[])[JobKind.Photoshop, JobKind.Meitu])
        {
            double[] all = [.. jobs.Where(j => j.Kind == kind && j.Succeeded)
                                   .Select(j => j.Duration.TotalSeconds)];
            if (all.Length == 0)
            {
                continue;
            }

            double[] sorted = [.. all.Order()];
            double[] firstFive = [.. all.Take(5)];
            double[] lastFive = [.. all.TakeLast(5)];

            output.WriteLine($"{kind} (n={all.Length})");
            output.WriteLine($"  min {sorted[0],6:F1}   median {Percentile(sorted, 0.50),6:F1}   " +
                             $"mean {all.Average(),6:F1}   p95 {Percentile(sorted, 0.95),6:F1}   " +
                             $"max {sorted[^1],6:F1}");
            output.WriteLine($"  first 5 mean {firstFive.Average(),6:F1}   " +
                             $"last 5 mean {lastFive.Average(),6:F1}   " +
                             $"drift {lastFive.Average() - firstFive.Average(),+6:F1}");

            JobRecord[] launched = [.. jobs.Where(j => j.Kind == kind && j.Succeeded && j.Launched)];
            if (launched.Length > 0)
            {
                output.WriteLine($"  launch jobs  : " +
                                 string.Join(", ", launched.Select(j => $"#{j.Number} {j.Duration.TotalSeconds:F1}s")));
            }
        }

        static double Percentile(double[] sorted, double fraction)
        {
            if (sorted.Length == 1)
            {
                return sorted[0];
            }

            double position = fraction * (sorted.Length - 1);
            int low = (int)Math.Floor(position);
            int high = (int)Math.Ceiling(position);
            return sorted[low] + ((sorted[high] - sorted[low]) * (position - low));
        }
    }

    private void ReportTopology(MeituBaseline baseline, MeituVersionTopology topology)
    {
        output.WriteLine($"accepted executable     : {topology.AcceptedExecutable}");
        output.WriteLine($"accepted version/digest : {baseline.AcceptedVersion} / " +
                         $"{baseline.ExecutableSha256.ToString()[..12]}");
        output.WriteLine($"accepted binary present : {topology.AcceptedExecutableExists}");
        output.WriteLine($"installed versions      : " +
                         string.Join(", ", topology.InstalledVersionDirectories));
        output.WriteLine($"launcher stub resolves  : {topology.LauncherConfiguredVersion ?? "(no launcher config)"}");
        output.WriteLine($"running instances       : {topology.RunningExecutables.Count}");
        foreach (string path in topology.RunningExecutables)
        {
            output.WriteLine($"  {path}");
        }

        output.WriteLine($"only accepted running   : {topology.OnlyAcceptedIsRunning}");
    }

    /// <summary>Reports readiness and the composed adapters, and says whether to proceed.</summary>
    private async Task<bool> ReportReadinessAsync(ServiceProvider services, string which)
    {
        IEnvironmentDiagnostics diagnostics = services.GetRequiredService<IEnvironmentDiagnostics>();
        IEnvironmentGate gate = services.GetRequiredService<IEnvironmentGate>();
        IMeituProcessor meitu = services.GetRequiredService<IMeituProcessor>();
        IPhotoshopOutputProcessor photoshop = services.GetRequiredService<IPhotoshopOutputProcessor>();

        meitu.ShouldBeOfType<ProductionMeituProcessor>();
        photoshop.ShouldBeOfType<ProductionPhotoshopOutputProcessor>();

        EnvironmentReadinessReport readiness = diagnostics.Read();
        OperationResult<PrintFlow.Domain.Results.Unit> production =
            gate.Verify(AdapterExecutionMode.Production);

        output.WriteLine($"gate                    : {gate.GetType().Name}");
        output.WriteLine($"Meitu adapter           : {meitu.AdapterId} / {meitu.Mode}");
        output.WriteLine($"Photoshop adapter       : {photoshop.AdapterId} / {photoshop.Mode}");
        output.WriteLine($"preset identity         : {readiness.PresetIdentity ?? "(unverified)"}");
        output.WriteLine($"verified                : {readiness.Verified}");
        output.WriteLine($"blocking failures       : {readiness.BlockingFailures.Count()}");
        output.WriteLine($"Production Readiness    : {(production.IsSuccess ? "Ready" : "NOT READY")}");

        if (production.IsFailure)
        {
            output.WriteLine("=== SOAK BLOCKED ===");
            output.WriteLine($"{which} was refused Production: {production.Failure.Code}");
            output.WriteLine($"detail                  : {production.Failure.TechnicalDetail}");
            output.WriteLine("Nothing external was run. Repair the workstation; do not bypass the gate.");
        }

        await Task.CompletedTask;
        return production.IsSuccess;
    }

    // -----------------------------------------------------------------------------------
    // Workstation and workspace observation
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// The title of a Photoshop document with unsaved changes that PrintFlow did not create.
    /// </summary>
    /// <remarks>
    /// A courtesy preflight, not an ownership rule: it exists so a long unattended run does not
    /// start while an operator has work open, and it is deliberately conservative — anything it
    /// cannot recognise as PrintFlow's own synthetic naming counts as the operator's.
    /// </remarks>
    private static string? UnsavedForeignPhotoshopDocument()
    {
        foreach (Process process in Process.GetProcessesByName("Photoshop"))
        {
            using (process)
            {
                string title = process.MainWindowTitle;
                if (title.Contains(UnsavedDocumentMarker, StringComparison.Ordinal) &&
                    !title.Contains(SyntheticDocumentPrefix, StringComparison.Ordinal))
                {
                    return title;
                }
            }
        }

        return null;
    }

    private static string PhotoshopTitle()
    {
        foreach (Process process in Process.GetProcessesByName("Photoshop"))
        {
            using (process)
            {
                return process.MainWindowTitle;
            }
        }

        return "(not running)";
    }

    /// <summary>
    /// A metadata fingerprint of a bounded sample of sessions that existed before the soak
    /// (Epic 11600 Part B §13).
    /// </summary>
    /// <remarks>
    /// Names, sizes and write times only. §13 is explicit that customer content must not be read
    /// merely to produce evidence, and a metadata fingerprint answers the question that is
    /// actually being asked — did the soak change a session it does not own — without opening a
    /// single customer file.
    /// </remarks>
    private sealed record PreExistingSessionSample(IReadOnlyDictionary<string, string> Fingerprints)
    {
        public static PreExistingSessionSample Take(string workspaceRoot, WorkspaceCensus census, int count)
        {
            Dictionary<string, string> fingerprints = new(StringComparer.Ordinal);
            foreach (string name in census.SessionDirectories.OrderBy(n => n, StringComparer.Ordinal).Take(count))
            {
                fingerprints[name] = Fingerprint(Path.Combine(workspaceRoot, "Sessions", name));
            }

            return new PreExistingSessionSample(fingerprints);
        }

        public string Describe() => $"{Fingerprints.Count} ({string.Join(", ", Fingerprints.Keys)})";

        public void ReportAndAssert(string workspaceRoot, ITestOutputHelper output)
        {
            int unchanged = 0;
            foreach ((string name, string before) in Fingerprints)
            {
                string after = Fingerprint(Path.Combine(workspaceRoot, "Sessions", name));
                after.ShouldBe(before, $"pre-existing session {name} must not be mutated by the soak.");
                unchanged++;
            }

            output.WriteLine($"prior sessions unchanged: {unchanged}/{Fingerprints.Count}");
        }

        private static string Fingerprint(string directory)
        {
            if (!Directory.Exists(directory))
            {
                return "(missing)";
            }

            IEnumerable<string> lines = Directory
                .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .Select(p =>
                {
                    FileInfo info = new(p);
                    return $"{Path.GetRelativePath(directory, p)}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
                });

            return string.Join("\n", lines);
        }
    }

    /// <summary>
    /// What the managed workspace held before the run, so afterwards can be compared with it
    /// (Epic 11600 Part B §13).
    /// </summary>
    /// <remarks>
    /// The Part A census plus the QA-directory count this slice needs. A copy rather than a
    /// shared helper, for the reason Part A gave: the two smokes are independent evidence, and a
    /// change made for one silently changing what the other asserted would erode both.
    /// </remarks>
    private sealed record WorkspaceCensus(
        string[] SessionDirectories, int ComparisonFiles, int QuarantineFiles, int QaDirectories)
    {
        public static WorkspaceCensus Take(string workspaceRoot) => new(
            Names(Path.Combine(workspaceRoot, "Sessions")),
            FileCount(Path.Combine(workspaceRoot, "Comparison")),
            FileCount(Path.Combine(workspaceRoot, "Quarantine")),
            Names(Path.Combine(workspaceRoot, "QA")).Length);

        public void ReportAndAssert(
            string workspaceRoot, string[] expectedSessionDirectories, ITestOutputHelper output)
        {
            WorkspaceCensus after = Take(workspaceRoot);

            string[] added = [.. after.SessionDirectories.Except(SessionDirectories, StringComparer.Ordinal)];
            string[] removed = [.. SessionDirectories.Except(after.SessionDirectories, StringComparer.Ordinal)];

            output.WriteLine($"sessions before/after   : {SessionDirectories.Length} / {after.SessionDirectories.Length}");
            output.WriteLine($"session directories new : {added.Length}");
            output.WriteLine($"expected from this run  : {expectedSessionDirectories.Distinct(StringComparer.Ordinal).Count()}");
            output.WriteLine($"session directories lost: {removed.Length}");
            output.WriteLine($"Comparison files        : {after.ComparisonFiles} (was {ComparisonFiles})");
            output.WriteLine($"Quarantine files        : {after.QuarantineFiles} (was {QuarantineFiles})");
            output.WriteLine($"QA directories          : {after.QaDirectories} (was {QaDirectories})");

            removed.ShouldBeEmpty("no existing session may be removed.");
            after.ComparisonFiles.ShouldBe(ComparisonFiles, "no comparison artefact may be touched.");
            after.QuarantineFiles.ShouldBe(QuarantineFiles, "nothing may be quarantined.");
            added.ShouldBe(
                [.. expectedSessionDirectories.Distinct(StringComparer.Ordinal)],
                ignoreOrder: true,
                "the only new session directories may be this run's own.");
        }

        private static string[] Names(string directory) =>
            Directory.Exists(directory)
                ? [.. Directory.EnumerateDirectories(directory).Select(Path.GetFileName).OfType<string>()]
                : [];

        private static int FileCount(string directory) =>
            Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Count()
                : 0;
    }

    // -----------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// Loads the committed configuration and refuses anything but the activated Production mode.
    /// </summary>
    /// <remarks>
    /// Never overridden (§16, §18). A soak about the workstation that composed its own adapter
    /// mode would be a soak about a configuration nobody runs.
    /// </remarks>
    private static PrintFlowConfiguration LoadCommittedConfiguration()
    {
        PrintFlowConfiguration configuration =
            PrintFlowConfiguration.LoadFromFile(RepositoryFile("appsettings.json"));
        configuration.Adapters.Mode.ShouldBe("Production");
        return configuration;
    }

    /// <summary>
    /// Reads the accepted Meitu baseline from the configured preset, so the accepted executable
    /// path is learned at run time rather than written down in this repository.
    /// </summary>
    private static MeituBaseline VerifiedMeituBaseline(
        PrintFlowConfiguration configuration, string workspaceRoot)
    {
        PresetMeituBaselineProvider provider = new(
            Path.Combine(workspaceRoot, configuration.Preset.Path),
            Sha256.Parse(configuration.Preset.ExpectedSha256));

        OperationResult<MeituBaseline> baseline = provider.GetVerifiedBaseline();
        baseline.IsSuccess.ShouldBeTrue(
            baseline.IsFailure ? baseline.Failure.ToString() : string.Empty);
        return baseline.Value;
    }

    private static string CreateQaDirectory(string workspaceRoot, out string token)
    {
        token = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss") + "-" +
                Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        string qaRoot = Path.Combine(workspaceRoot, "QA", "Epic11600B", token);
        Directory.CreateDirectory(qaRoot);
        return qaRoot;
    }

    private static SqliteConnectionFactory CreateQaDatabase(string qaRoot, string fileName)
    {
        SqliteConnectionFactory factory = new(Path.Combine(qaRoot, fileName));
        using SqliteConnection connection = factory.Open();
        MigrationRunner.Migrate(connection).IsSuccess.ShouldBeTrue();
        return factory;
    }

    private static string FileDigest(string absolutePath)
    {
        using FileStream stream = File.OpenRead(absolutePath);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static SessionView Accept(OperationResult<SessionView> result)
    {
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : string.Empty);
        return result.Value;
    }

    private static string RepositoryFile(string relativePath)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "PrintFlowStudio.sln")))
        {
            current = current.Parent;
        }

        current.ShouldNotBeNull("the repository root must be locatable from the test output.");
        return Path.Combine(current.FullName, relativePath);
    }
}
