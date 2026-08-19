using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using PrintFlow.App.Composition;
using PrintFlow.App.Startup;
using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Sqlite;
using PrintFlow.Infrastructure.Startup;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Startup;

/// <summary>
/// The application startup sequence: who may start, in what order, and how often recovery runs
/// (Epic 11100 Part 3C1 §4, §5, §7, §9, §10, §11).
/// </summary>
/// <remarks>
/// Every test drives the real <see cref="ApplicationStartup"/> against a throwaway installed
/// layout — its own configuration file, workspace root, database and synthetic preset — so the
/// ordering assertions are about the code the application actually runs, not a re-creation of
/// it. Only the single-instance guard and (where the assertion is about call counts) the
/// recovery service are scripted.
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class ApplicationStartupTests
{
    // -------------------------------------------------------------------------------------
    // §4, §11: the primary instance starts, and recovery runs exactly once
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task The_primary_instance_starts_and_composes_a_usable_shell()
    {
        using TempApplication application = new();
        using FakeSingleInstanceGuard guard = new(SingleInstanceOutcome.Acquired);

        using StartupResult result = await RunAsync(application, guard);

        result.Status.IsPrimaryInstance.ShouldBeTrue();
        result.Status.Failure.ShouldBeNull();
        result.Status.CanShowShell.ShouldBeTrue();
        result.Status.RecoveryExecuted.ShouldBeTrue();
        result.Status.PresetVerified.ShouldBeTrue();

        guard.AcquireCallCount.ShouldBe(1);
        guard.IsHeld.ShouldBeTrue();

        // The screens the App would show resolve from the graph startup returned.
        result.Services.ShouldNotBeNull();
        result.Services!.GetRequiredService<WorkflowSelectionViewModel>().Workflows.Count.ShouldBe(3);
    }

    [Fact]
    public async Task A_successful_startup_calls_startup_recovery_exactly_once()
    {
        using TempApplication application = new();
        using FakeSingleInstanceGuard guard = new(SingleInstanceOutcome.Acquired);
        RecordingStartupRecoveryService recovery = new();

        using StartupResult result = await RunAsync(application, guard, Substitute(recovery));

        result.Status.CanShowShell.ShouldBeTrue();
        recovery.CallCount.ShouldBe(1);

        // Resolving the shell must not trigger a second pass: recovery belongs to startup, not
        // to a view model, a navigation path or database initialisation.
        result.Services!.GetRequiredService<ShellViewModel>();
        result.Services!.GetRequiredService<ISessionService>();
        recovery.CallCount.ShouldBe(1);
    }

    /// <summary>
    /// The structural half of "exactly once": startup is the only place in the product that can
    /// call it at all (Part 3C1 §11).
    /// </summary>
    [Fact]
    public void Startup_recovery_is_invoked_from_exactly_one_place_in_the_product()
    {
        List<string> callSites = [];
        foreach (string file in Directory.EnumerateFiles(SourceRoot(), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            // The declaration in IStartupRecoveryService and the implementation in
            // StartupRecoveryService are the contract, not call sites.
            string name = Path.GetFileName(file);
            if (name is "IStartupRecoveryService.cs" or "StartupRecoveryService.cs")
            {
                continue;
            }

            if (File.ReadAllText(file).Contains(".RecoverAsync(", StringComparison.Ordinal))
            {
                callSites.Add(name);
            }
        }

        callSites.ShouldBe(["ApplicationStartup.cs"]);
    }

    // -------------------------------------------------------------------------------------
    // §5, §10: a second instance is refused and runs no recovery
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task A_second_instance_is_refused_and_never_runs_recovery()
    {
        using TempApplication application = new();
        using FakeSingleInstanceGuard guard = new(SingleInstanceOutcome.AlreadyRunning);
        RecordingStartupRecoveryService recovery = new();

        using StartupResult result = await RunAsync(application, guard, Substitute(recovery));

        result.Status.IsPrimaryInstance.ShouldBeFalse();
        result.Status.CanShowShell.ShouldBeFalse();
        result.Status.RecoveryExecuted.ShouldBeFalse();
        result.Status.RecoveryReport.ShouldBeNull();

        recovery.CallCount.ShouldBe(0);

        // No container at all: a refused instance cannot reach a repository, an adapter or a
        // session even by accident.
        result.Services.ShouldBeNull();

        // It got no further than the guard: no database was created under the workspace root.
        File.Exists(application.DatabasePath).ShouldBeFalse();
    }

    [Fact]
    public async Task An_unevaluable_guard_refuses_startup_rather_than_claiming_a_second_instance()
    {
        using TempApplication application = new();
        using FakeSingleInstanceGuard guard = new(SingleInstanceOutcome.Unavailable);
        RecordingStartupRecoveryService recovery = new();

        using StartupResult result = await RunAsync(application, guard, Substitute(recovery));

        result.Status.CanShowShell.ShouldBeFalse();
        result.Status.Failure.ShouldNotBeNull();
        result.Status.Failure!.Stage.ShouldBe(StartupStage.SingleInstanceGuard);
        recovery.CallCount.ShouldBe(0);
        result.Services.ShouldBeNull();
    }

    // -------------------------------------------------------------------------------------
    // §9: migrations precede recovery, and an unknown future schema fails closed
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task Migrations_are_applied_before_recovery_reads_persistence()
    {
        using TempApplication application = new();
        using FakeSingleInstanceGuard guard = new(SingleInstanceOutcome.Acquired);

        long userVersionWhenRecoveryRan = -1;
        long recoveryReadableTableCount = -1;

        RecordingStartupRecoveryService recovery = new(onCalled: () =>
        {
            using SqliteConnection connection = new SqliteConnectionFactory(application.DatabasePath).Open();

            using SqliteCommand version = connection.CreateCommand();
            version.CommandText = "PRAGMA user_version;";
            userVersionWhenRecoveryRan = Convert.ToInt64(version.ExecuteScalar());

            using SqliteCommand table = connection.CreateCommand();
            table.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' " +
                "AND name IN ('ProcessingSession', 'ProcessingAttempt', 'AutomationLock');";
            recoveryReadableTableCount = Convert.ToInt64(table.ExecuteScalar());
        });

        // A brand new, empty application database: nothing exists until startup migrates it.
        File.Exists(application.DatabasePath).ShouldBeFalse();

        using StartupResult result = await RunAsync(application, guard, Substitute(recovery));

        result.Status.CanShowShell.ShouldBeTrue();
        recovery.CallCount.ShouldBe(1);
        userVersionWhenRecoveryRan.ShouldBe(1L);
        recoveryReadableTableCount.ShouldBe(3L);
    }

    [Fact]
    public async Task A_database_from_a_newer_build_fails_closed_before_recovery()
    {
        using TempApplication application = new();
        using FakeSingleInstanceGuard guard = new(SingleInstanceOutcome.Acquired);
        RecordingStartupRecoveryService recovery = new();

        // A schema this build does not know. Never auto-downgraded, never auto-repaired.
        using (SqliteConnection connection = new SqliteConnectionFactory(application.DatabasePath).Open())
        using (SqliteCommand bump = connection.CreateCommand())
        {
            bump.CommandText = "PRAGMA user_version = 999;";
            bump.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();

        using StartupResult result = await RunAsync(application, guard, Substitute(recovery));

        result.Status.CanShowShell.ShouldBeFalse();
        result.Status.Failure.ShouldNotBeNull();
        result.Status.Failure!.Stage.ShouldBe(StartupStage.Database);
        result.Status.Failure.Code.ShouldBe(FailureCode.PersistenceError);
        recovery.CallCount.ShouldBe(0);
        result.Services.ShouldBeNull();
    }

    // -------------------------------------------------------------------------------------
    // §6, §7, §13: the startup status carries the recovery summary; a failed pass stops startup
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task The_startup_status_carries_the_recovery_summary_and_the_shell_shows_it()
    {
        using TempApplication application = new();
        using FakeSingleInstanceGuard guard = new(SingleInstanceOutcome.Acquired);

        SessionId session = SessionId.From(Guid.NewGuid());
        AttemptId attempt = AttemptId.From(Guid.NewGuid());
        DateTimeOffset at = new(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);

        RecordingStartupRecoveryService recovery = new(() => new StartupRecoveryReport(
        [
            new StartupRecoveryEntry(
                StartupRecoveryAction.AttemptInterrupted, at, session, attempt, null, "interrupted"),
            new StartupRecoveryEntry(
                StartupRecoveryAction.StepInterrupted, at, session, attempt, null, "step interrupted"),
            new StartupRecoveryEntry(
                StartupRecoveryAction.AutomationLockReleased, at, session, null, null, "lock released"),
            new StartupRecoveryEntry(
                StartupRecoveryAction.WorkingFileQuarantined, at, session, attempt, null, "quarantined"),
        ]));

        using StartupResult result = await RunAsync(application, guard, Substitute(recovery));

        StartupStatus status = result.Status;
        status.CanShowShell.ShouldBeTrue();
        status.RecoveryExecuted.ShouldBeTrue();
        status.RecoveredAttemptCount.ShouldBe(1);
        status.ReleasedStaleLockCount.ShouldBe(1);
        status.QuarantinedFileCount.ShouldBe(1);
        status.RecoveryFailureCount.ShouldBe(0);
        status.RecoveryReport!.Entries.Count.ShouldBe(4);

        // The same status is what the shell reads — the report is not discarded silently.
        result.Services!.GetRequiredService<StartupStatusAccessor>().Status.ShouldBeSameAs(status);
        result.Services!.GetRequiredService<HomeViewModel>().StartupSummary.ShouldContain("1");
    }

    [Fact]
    public async Task A_recovery_infrastructure_failure_stops_startup_rather_than_proceeding()
    {
        using TempApplication application = new();
        using FakeSingleInstanceGuard guard = new(SingleInstanceOutcome.Acquired);

        RecordingStartupRecoveryService recovery = new(
            failure: OperationFailure.Create(
                FailureCode.PersistenceError, "The automation lock could not be read."));

        using StartupResult result = await RunAsync(application, guard, Substitute(recovery));

        recovery.CallCount.ShouldBe(1);
        result.Status.CanShowShell.ShouldBeFalse();
        result.Status.RecoveryExecuted.ShouldBeFalse();
        result.Status.Failure.ShouldNotBeNull();
        result.Status.Failure!.Stage.ShouldBe(StartupStage.Recovery);
        result.Status.Failure.Code.ShouldBe(FailureCode.PersistenceError);
        result.Services.ShouldBeNull();
    }

    // -------------------------------------------------------------------------------------
    // §8: production remains fail-closed through the new sequence
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task A_workstation_configured_for_production_adapters_refuses_to_start()
    {
        using TempApplication application = new(adapterMode: "Production");
        using FakeSingleInstanceGuard guard = new(SingleInstanceOutcome.Acquired);
        RecordingStartupRecoveryService recovery = new();

        using StartupResult result = await RunAsync(application, guard, Substitute(recovery));

        result.Status.CanShowShell.ShouldBeFalse();
        result.Status.Failure!.Stage.ShouldBe(StartupStage.Composition);
        recovery.CallCount.ShouldBe(0);
        result.Services.ShouldBeNull();
    }

    // -------------------------------------------------------------------------------------

    private static Task<StartupResult> RunAsync(
        TempApplication application,
        ISingleInstanceGuard guard,
        Action<IServiceCollection>? overrides = null) =>
        new ApplicationStartup(guard, application.ConfigurationFilePath, overrides)
            .RunAsync(CancellationToken.None);

    private static Action<IServiceCollection> Substitute(IStartupRecoveryService recovery) =>
        services => services.AddSingleton(recovery);

    /// <summary>Walks up to the repository root, then into <c>src\</c>.</summary>
    private static string SourceRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "PrintFlowStudio.sln")))
        {
            current = current.Parent;
        }

        return current is null
            ? throw new InvalidOperationException(
                "Could not locate the repository root (PrintFlowStudio.sln) above " + AppContext.BaseDirectory)
            : Path.Combine(current.FullName, "src");
    }
}
