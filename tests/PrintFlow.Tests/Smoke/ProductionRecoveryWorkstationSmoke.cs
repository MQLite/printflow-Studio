using System.Diagnostics;
using System.IO;
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

namespace PrintFlow.Tests.Smoke;

/// <summary>
/// The controlled sequential-and-restart recovery smoke: four synthetic Production jobs back to
/// back in one process, a restart, and a fifth job afterwards (Epic 11600 Part A §16, §17).
/// </summary>
/// <remarks>
/// Opt-in and inert by default, like every workstation smoke: the signed preset and the accepted
/// binaries exist on exactly one machine. Set <c>PRINTFLOW_RECOVERY_SMOKE=1</c> to run it.
/// <para>
/// <b>What this adds to the activation smoke.</b> Epic 11500 Part D proved that <i>one</i>
/// Production job runs on this workstation. What it could not prove is the thing a shop
/// actually does, which is the next job, and the one after that, and the first one after
/// lunch. So the shape here is the sequence rather than the job: Photoshop twice, Meitu twice,
/// all four through one composed graph and one process, then a restart, then a fifth operation
/// through a graph that shares nothing with the first but the database and the disk.
/// </para>
/// <para>
/// <b>What is isolated, and what deliberately is not.</b> The database is a throwaway QA file,
/// so no row reaches the operator's installation. The workspace root is the real one and cannot
/// be redirected — the accepted preset names the root this workstation is verified for — so the
/// sessions live under the real <c>Sessions\</c>, in directories created fresh, and the census
/// at the end is what establishes that nothing already there moved.
/// </para>
/// <para>
/// <b>What it will not do.</b> It corrupts nothing, closes nothing, answers no dialog, and does
/// not manufacture the unknown-document scenario live: §16 requires that one to be proved
/// synthetically, and <c>ExternalStateHygieneTests</c> does. If Photoshop is holding an unsaved
/// document when the smoke starts, the smoke reports itself blocked and stops.
/// </para>
/// </remarks>
public sealed class ProductionRecoveryWorkstationSmoke(ITestOutputHelper output)
{
    private const string EnableVariable = "PRINTFLOW_RECOVERY_SMOKE";

    /// <summary>The window-title marker Photoshop appends to a document with unsaved changes.</summary>
    private const string UnsavedDocumentMarker = "*";

    /// <summary>The name prefix every artefact this smoke generates carries.</summary>
    private const string SmokeArtefactPrefix = "PF_11600A_";

    [Fact]
    public async Task Four_sequential_jobs_a_restart_and_a_fifth_job_all_succeed_on_this_workstation()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
        {
            // Inert by design; see the class remarks.
            return;
        }

        PrintFlowConfiguration configuration =
            PrintFlowConfiguration.LoadFromFile(RepositoryFile("appsettings.json"));

        // Never overridden. 11500-D activated Production in the committed file, and a smoke about
        // recovery that composed its own mode would be a smoke about a configuration nobody runs.
        configuration.Adapters.Mode.ShouldBe("Production");

        string workspaceRoot = Path.GetFullPath(configuration.Workspace.Root);
        string token = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss") + "-" +
                       Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        string qaRoot = Path.Combine(workspaceRoot, "QA", "Epic11600A", token);
        Directory.CreateDirectory(qaRoot);

        output.WriteLine("=== configuration ===");
        output.WriteLine($"committed Adapters.Mode : {configuration.Adapters.Mode}");
        output.WriteLine($"configured preset       : {configuration.Preset.Id} {configuration.Preset.Version}");
        output.WriteLine($"workspace root          : {workspaceRoot}");
        output.WriteLine($"QA directory            : {qaRoot}");
        output.WriteLine(string.Empty);

        // ---- preflight ---------------------------------------------------------------------

        if (UnsavedPhotoshopDocument() is { } unsaved)
        {
            output.WriteLine("=== SMOKE BLOCKED ===");
            output.WriteLine($"Photoshop is holding a document with unsaved changes: {unsaved}");
            output.WriteLine("Nothing was run. PrintFlow does not answer a prompt it did not raise.");
            return;
        }

        WorkspaceCensus before = WorkspaceCensus.Take(workspaceRoot);
        int externalBefore = ExternalApplicationProbe.RunningCount();

        output.WriteLine("=== workstation before ===");
        output.WriteLine($"external apps running   : {externalBefore}");
        output.WriteLine($"sessions                : {before.SessionDirectories.Length}");
        output.WriteLine($"Comparison files        : {before.ComparisonFiles}");
        output.WriteLine($"Quarantine files        : {before.QuarantineFiles}");
        output.WriteLine(string.Empty);

        string databasePath = Path.Combine(qaRoot, "printflow-recovery.db");
        SqliteConnectionFactory factory = new(databasePath);
        using (SqliteConnection connection = factory.Open())
        {
            MigrationRunner.Migrate(connection).IsSuccess.ShouldBeTrue();
        }

        List<string> newSessionDirectories = [];
        List<JobOutcome> outcomes = [];

        // ---- process 1: four jobs, one graph ------------------------------------------------

        using (ServiceProvider services =
               ServiceRegistration.BuildServiceProvider(configuration, workspaceRoot, factory))
        {
            output.WriteLine("=== process 1 ===");
            if (!await ReportReadinessAsync(services, "process 1"))
            {
                return;
            }

            outcomes.Add(await RunPhotoshopJobAsync(services, workspaceRoot, qaRoot, $"{token}_A"));
            outcomes.Add(await RunPhotoshopJobAsync(services, workspaceRoot, qaRoot, $"{token}_B"));
            outcomes.Add(await RunMeituJobAsync(services, workspaceRoot, qaRoot, $"{token}_C"));
            outcomes.Add(await RunMeituJobAsync(services, workspaceRoot, qaRoot, $"{token}_D"));
        }

        // ---- process 2: the restart ---------------------------------------------------------
        //
        // A genuinely separate graph over the same database and the same disk, with recovery run
        // first exactly as ApplicationStartup orders it. Photoshop and Meitu are left running
        // across the boundary on purpose: §14's claim is that their continued existence neither
        // resurrects the previous process's automation lock nor makes the next job unsafe.

        int externalAtRestart = ExternalApplicationProbe.RunningCount();
        output.WriteLine(string.Empty);
        output.WriteLine("=== restart ===");
        output.WriteLine($"external apps still up  : {externalAtRestart}");

        using (ServiceProvider restarted =
               ServiceRegistration.BuildServiceProvider(configuration, workspaceRoot, factory))
        {
            ISessionRepository repository = restarted.GetRequiredService<ISessionRepository>();

            AutomationLockState lockBeforeRecovery =
                (await repository.GetAutomationLockAsync(CancellationToken.None)).Value;

            OperationResult<StartupRecoveryReport> recovered = await restarted
                .GetRequiredService<IStartupRecoveryService>()
                .RecoverAsync(CancellationToken.None);
            recovered.IsSuccess.ShouldBeTrue(
                recovered.IsFailure ? recovered.Failure.ToString() : string.Empty);

            AutomationLockState lockAfterRecovery =
                (await repository.GetAutomationLockAsync(CancellationToken.None)).Value;

            output.WriteLine($"lock before recovery    : {(lockBeforeRecovery.IsHeld ? "HELD" : "free")}");
            output.WriteLine($"lock after recovery     : {(lockAfterRecovery.IsHeld ? "HELD" : "free")}");
            output.WriteLine($"attempts interrupted    : {recovered.Value.InterruptedAttemptCount}");
            output.WriteLine($"files quarantined       : " +
                             recovered.Value.Entries.Count(e =>
                                 e.Action == StartupRecoveryAction.WorkingFileQuarantined));
            output.WriteLine($"recovery failures       : " +
                             recovered.Value.Entries.Count(e => e.Action == StartupRecoveryAction.RecoveryFailed));

            // Four jobs that all ended cleanly leave nothing for recovery to correct, and a
            // restart that "corrected" something here would be corrupting completed work.
            lockBeforeRecovery.IsHeld.ShouldBeFalse("four completed jobs release the lock.");
            lockAfterRecovery.IsHeld.ShouldBeFalse();
            recovered.Value.InterruptedAttemptCount.ShouldBe(0);
            recovered.Value.Entries.ShouldNotContain(e =>
                e.Action == StartupRecoveryAction.WorkingFileQuarantined);
            recovered.Value.Entries.ShouldNotContain(e =>
                e.Action == StartupRecoveryAction.RecoveryFailed);

            // Every earlier job is still exactly what it was, read through the new process.
            output.WriteLine(string.Empty);
            output.WriteLine("=== state after restart ===");
            foreach (JobOutcome earlier in outcomes)
            {
                SessionAggregate reloaded =
                    (await repository.LoadAsync(earlier.Session, CancellationToken.None)).Value!;
                SessionStep step = reloaded.Steps.Single(s => s.Step == earlier.Step);
                Revision revision = reloaded.Revisions.Single(r => r.Id == earlier.Revision);

                output.WriteLine($"{earlier.Label,-6} {earlier.Step,-16} {step.State,-14} " +
                                 $"sha {revision.Facts.Sha256.ToString()[..16]}… " +
                                 $"attempts {reloaded.Attempts.Count(a => a.Step == earlier.Step)}");

                step.State.ShouldBe(StepState.ReviewRequired);
                revision.Facts.Sha256.ShouldBe(earlier.Sha256);
                File.Exists(Path.Combine(workspaceRoot, revision.File.RelativePath.Replace('/', '\\')))
                    .ShouldBeTrue("the validated output must still be on disk after a restart.");
            }

            output.WriteLine(string.Empty);
            output.WriteLine("=== process 2 ===");
            if (!await ReportReadinessAsync(restarted, "process 2"))
            {
                return;
            }

            outcomes.Add(await RunPhotoshopJobAsync(restarted, workspaceRoot, qaRoot, $"{token}_E"));
        }

        // ---- what the workstation looks like afterwards --------------------------------------

        int externalAfter = ExternalApplicationProbe.RunningCount();

        output.WriteLine(string.Empty);
        output.WriteLine("=== job census ===");
        foreach (JobOutcome job in outcomes)
        {
            output.WriteLine($"{job.Label,-6} {job.Step,-16} {job.AdapterId,-28} " +
                             $"apps {job.ExternalBefore}->{job.ExternalAfter} " +
                             $"({(job.ExternalAfter > job.ExternalBefore ? "launched" : "reused")})");
            output.WriteLine($"       output           : {job.RelativePath}");
            output.WriteLine($"       sha-256          : {job.Sha256}");
            output.WriteLine($"       pixels / bytes   : {job.PixelWidth}x{job.PixelHeight} / {job.ByteLength}");
            output.WriteLine($"       attempts / lock  : {job.AttemptCount} / " +
                             $"{(job.LockHeldBefore ? "held" : "free")} before, " +
                             $"{(job.LockHeldAfter ? "held" : "free")} after");
            newSessionDirectories.Add(job.SessionDirectory);
        }

        output.WriteLine(string.Empty);
        output.WriteLine("=== workstation after ===");
        output.WriteLine($"external apps running   : {externalAfter} (was {externalBefore})");
        before.ReportAndAssert(workspaceRoot, [.. newSessionDirectories], output);

        // The claim the whole smoke exists to make: the fifth job, in a process that started
        // after four others had finished and after a restart, succeeded like the first.
        outcomes.Count.ShouldBe(5);
        outcomes.ShouldAllBe(job => job.AttemptCount == 1);
        outcomes.ShouldAllBe(job => !job.LockHeldAfter);
    }

    // -----------------------------------------------------------------------------------
    // One job
    // -----------------------------------------------------------------------------------

    /// <summary>What one synthetic job did, in the bounded terms §17 permits recording.</summary>
    private sealed record JobOutcome(
        string Label,
        SessionId Session,
        string SessionDirectory,
        StepKind Step,
        string AdapterId,
        RevisionId Revision,
        string RelativePath,
        Sha256 Sha256,
        long ByteLength,
        int? PixelWidth,
        int? PixelHeight,
        int AttemptCount,
        bool LockHeldBefore,
        bool LockHeldAfter,
        int ExternalBefore,
        int ExternalAfter);

    private async Task<JobOutcome> RunPhotoshopJobAsync(
        ServiceProvider services, string workspaceRoot, string qaRoot, string label)
    {
        ISessionService service = services.GetRequiredService<ISessionService>();
        ISessionRepository repository = services.GetRequiredService<ISessionRepository>();

        string sourcePath = Path.Combine(qaRoot, $"{SmokeArtefactPrefix}{label}.png");
        File.WriteAllBytes(sourcePath, SyntheticImages.PngWithAlpha(
            1200, 800, static (_, _) => byte.MaxValue, dpi: 240));

        SessionId id = Accept(await service.ImportAsync(
            WorkflowType.GeneratePrintTiff, sourcePath, $"{SmokeArtefactPrefix}{label}", "qa",
            CancellationToken.None)).Id;

        Accept(await service.ExecuteAsync(
            id, new WorkflowCommand.ConfirmOriginal("synthetic recovery fixture"), "qa",
            CancellationToken.None));
        Accept(await service.ExecuteAsync(
            id,
            new WorkflowCommand.SetPrintDimensions(
                PrintDimensions.FromMillimetres(50.8, 100, SizePreset.Custom)),
            "qa",
            CancellationToken.None));
        Accept(await service.ExecuteAsync(
            id,
            new WorkflowCommand.SelectWhiteUnderbaseBranch(WhiteUnderbaseBranch.W1_1px, "synthetic solid design"),
            "qa",
            CancellationToken.None));

        return await RunStepAsync(
            services, repository, service, workspaceRoot, label, id,
            StepKind.PhotoshopOutput, OperationKind.PhotoshopOutput);
    }

    private async Task<JobOutcome> RunMeituJobAsync(
        ServiceProvider services, string workspaceRoot, string qaRoot, string label)
    {
        ISessionService service = services.GetRequiredService<ISessionService>();
        ISessionRepository repository = services.GetRequiredService<ISessionRepository>();

        string sourcePath = Path.Combine(qaRoot, $"{SmokeArtefactPrefix}{label}.png");
        File.WriteAllBytes(sourcePath, SyntheticImages.Png(320, 240, dpi: 300, alpha: true));

        SessionId id = Accept(await service.ImportAsync(
            WorkflowType.PrepareAsset, sourcePath, $"{SmokeArtefactPrefix}{label}", "qa",
            CancellationToken.None)).Id;

        Accept(await service.ExecuteAsync(
            id, new WorkflowCommand.ConfirmOriginal("synthetic recovery fixture"), "qa",
            CancellationToken.None));

        return await RunStepAsync(
            services, repository, service, workspaceRoot, label, id,
            StepKind.Enhancement, OperationKind.Enhance);
    }

    /// <summary>
    /// Runs one adapter-backed step and records the bounded evidence around it.
    /// </summary>
    /// <remarks>
    /// The lock and the external process count are read on both sides of the operation rather
    /// than only afterwards: "the lock is free now" says nothing on its own, and "an application
    /// was launched" is a difference rather than a state.
    /// </remarks>
    private async Task<JobOutcome> RunStepAsync(
        ServiceProvider services,
        ISessionRepository repository,
        ISessionService service,
        string workspaceRoot,
        string label,
        SessionId id,
        StepKind step,
        OperationKind operation)
    {
        bool lockBefore = (await repository.GetAutomationLockAsync(CancellationToken.None)).Value.IsHeld;
        int externalBefore = ExternalApplicationProbe.RunningCount();

        output.WriteLine($"running {label} ({step}) ...");
        OperationResult<SessionView> produced = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(step), "qa", CancellationToken.None);

        int externalAfter = ExternalApplicationProbe.RunningCount();
        bool lockAfter = (await repository.GetAutomationLockAsync(CancellationToken.None)).Value.IsHeld;

        if (produced.IsFailure)
        {
            output.WriteLine($"  REFUSED               : {produced.Failure.Code}");
            output.WriteLine($"  detail                : {produced.Failure.TechnicalDetail}");
            foreach (KeyValuePair<string, string> entry in produced.Failure.Context)
            {
                output.WriteLine($"    {entry.Key,-20}: {entry.Value}");
            }

            // A refusal must still have released the lock, whatever else it did. That is the
            // §9 invariant, and it is the one thing a failed live job must not also fail.
            lockAfter.ShouldBeFalse("a refused operation must not leave the automation lock held.");
        }

        produced.IsSuccess.ShouldBeTrue(
            produced.IsFailure ? produced.Failure.ToString() : string.Empty);

        SessionAggregate aggregate = (await repository.LoadAsync(id, CancellationToken.None)).Value!;
        Revision revision = aggregate.Revisions.Single(r => r.Operation == operation);
        ProcessingAttempt attempt = aggregate.Attempts.Last(a => a.Step == step);

        aggregate.Steps.Single(s => s.Step == step).State.ShouldBe(StepState.ReviewRequired);
        File.Exists(Path.Combine(workspaceRoot, revision.File.RelativePath.Replace('/', '\\')))
            .ShouldBeTrue();

        string adapterId = step == StepKind.PhotoshopOutput
            ? services.GetRequiredService<IPhotoshopOutputProcessor>().AdapterId
            : services.GetRequiredService<IMeituProcessor>().AdapterId;

        return new JobOutcome(
            label[^1..],
            id,
            aggregate.Session.Workspace.RelativePath.Split('/')[^1],
            step,
            adapterId,
            revision.Id,
            revision.File.RelativePath,
            revision.Facts.Sha256,
            revision.Facts.ByteLength,
            revision.Facts.PixelWidth,
            revision.Facts.PixelHeight,
            aggregate.Attempts.Count(a => a.Step == step),
            lockBefore,
            lockAfter,
            externalBefore,
            externalAfter);
    }

    // -----------------------------------------------------------------------------------
    // Readiness
    // -----------------------------------------------------------------------------------

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
        output.WriteLine($"gate(Production)        : {(production.IsSuccess ? "ALLOWED" : "REFUSED")}");

        if (production.IsFailure)
        {
            output.WriteLine("=== SMOKE BLOCKED ===");
            output.WriteLine($"{which} was refused Production: {production.Failure.Code}");
            output.WriteLine($"detail                  : {production.Failure.TechnicalDetail}");
            output.WriteLine("Nothing external was run. Repair the workstation; do not bypass the gate.");
        }

        await Task.CompletedTask;
        return production.IsSuccess;
    }

    // -----------------------------------------------------------------------------------
    // Workstation observation
    // -----------------------------------------------------------------------------------

    private static string? UnsavedPhotoshopDocument()
    {
        foreach (Process process in Process.GetProcessesByName("Photoshop"))
        {
            using (process)
            {
                string title = process.MainWindowTitle;
                if (title.Contains(UnsavedDocumentMarker, StringComparison.Ordinal) &&
                    !title.Contains(SmokeArtefactPrefix, StringComparison.Ordinal))
                {
                    return title;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// What the managed workspace held before the run, so afterwards can be compared with it.
    /// </summary>
    /// <remarks>
    /// Counts and directory names, never contents (§17). A copy of the activation smoke's census
    /// rather than a shared one: the two smokes are independent evidence, and a shared helper
    /// would mean a change made for one silently changing what the other asserted.
    /// </remarks>
    private sealed record WorkspaceCensus(
        string[] SessionDirectories, int ComparisonFiles, int QuarantineFiles)
    {
        public static WorkspaceCensus Take(string workspaceRoot) => new(
            Names(Path.Combine(workspaceRoot, "Sessions")),
            FileCount(Path.Combine(workspaceRoot, "Comparison")),
            FileCount(Path.Combine(workspaceRoot, "Quarantine")));

        public void ReportAndAssert(
            string workspaceRoot, string[] expectedSessionDirectories, ITestOutputHelper output)
        {
            WorkspaceCensus after = Take(workspaceRoot);

            string[] added = [.. after.SessionDirectories.Except(SessionDirectories, StringComparer.Ordinal)];
            string[] removed = [.. SessionDirectories.Except(after.SessionDirectories, StringComparer.Ordinal)];

            output.WriteLine($"sessions before/after   : {SessionDirectories.Length} / {after.SessionDirectories.Length}");
            output.WriteLine($"session directories new : {added.Length}");
            foreach (string name in added.OrderBy(n => n, StringComparer.Ordinal))
            {
                output.WriteLine($"  {name}");
            }

            output.WriteLine($"session directories lost: {removed.Length}");
            output.WriteLine($"Comparison files        : {after.ComparisonFiles} (was {ComparisonFiles})");
            output.WriteLine($"Quarantine files        : {after.QuarantineFiles} (was {QuarantineFiles})");

            removed.ShouldBeEmpty("no existing session may be removed.");
            after.ComparisonFiles.ShouldBe(ComparisonFiles, "no comparison artefact may be touched.");
            after.QuarantineFiles.ShouldBe(QuarantineFiles, "nothing may be quarantined.");
            added.ShouldBe(expectedSessionDirectories, ignoreOrder: true,
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
