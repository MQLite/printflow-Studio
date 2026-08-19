using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using PrintFlow.App.Startup;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Configuration;
using PrintFlow.Infrastructure.Sqlite;
using PrintFlow.Infrastructure.Startup;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.App.Composition;

/// <summary>
/// What one application launch produced: the outcome, and the service graph when there is one.
/// </summary>
/// <remarks>
/// <see cref="Services"/> is null whenever <see cref="StartupStatus.CanShowShell"/> is false —
/// a refused instance never receives a container, so it cannot reach an adapter, a repository
/// or a session even by accident.
/// </remarks>
public sealed class StartupResult : IDisposable
{
    internal StartupResult(StartupStatus status, ServiceProvider? services)
    {
        Status = status;
        Services = services;
    }

    public StartupStatus Status { get; }

    public ServiceProvider? Services { get; }

    public void Dispose() => Services?.Dispose();
}

/// <summary>
/// The fixed application startup sequence (Epic 11100 Part 3C1 §4).
/// </summary>
/// <remarks>
/// Order, and why each edge matters:
/// <list type="number">
///   <item>acquire the single-instance guard — everything below may mutate application-owned
///         state, and only one process may;</item>
///   <item>load configuration;</item>
///   <item>resolve and create the application-owned root directories;</item>
///   <item>open SQLite and apply migrations;</item>
///   <item>compose the service graph;</item>
///   <item>verify the signed preset's integrity, recorded and non-blocking;</item>
///   <item>run <see cref="IStartupRecoveryService.RecoverAsync"/> exactly once;</item>
///   <item>record the result in <see cref="StartupStatusAccessor"/>;</item>
///   <item>hand the caller a graph it may show the shell from.</item>
/// </list>
/// Step 5 sits ahead of the plan's nominal position because recovery is resolved <i>from</i> the
/// graph; composing it is registration only and touches nothing. The semantics the plan actually
/// requires are preserved and are what the tests assert: migrations precede recovery, recovery
/// precedes the shell, and therefore recovery precedes any session interaction or adapter-backed
/// processing this process could start.
/// <para>
/// Every stage fails closed. A refused startup returns no container at all rather than a
/// half-usable one, because "recovery did not finish" and "the operator may start work" must
/// never be true at the same time.
/// </para>
/// <para>
/// This object owns the single-instance guard: the caller keeps it alive for the whole
/// application lifetime and disposes it during ordinary shutdown, which is the only thing that
/// releases the guard (Part 3C1 §3).
/// </para>
/// </remarks>
public sealed class ApplicationStartup : IDisposable
{
    private readonly ISingleInstanceGuard _guard;
    private readonly string _configurationFilePath;
    private readonly Action<IServiceCollection>? _serviceOverrides;

    /// <param name="guard">Claimed by <see cref="RunAsync"/>, released by <see cref="Dispose"/>.</param>
    /// <param name="configurationFilePath">Absolute path to <c>appsettings.json</c>.</param>
    /// <param name="serviceOverrides">Test seam; null in the application.</param>
    public ApplicationStartup(
        ISingleInstanceGuard guard,
        string configurationFilePath,
        Action<IServiceCollection>? serviceOverrides = null)
    {
        ArgumentNullException.ThrowIfNull(guard);
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationFilePath);

        _guard = guard;
        _configurationFilePath = configurationFilePath;
        _serviceOverrides = serviceOverrides;
    }

    /// <summary>
    /// The installed layout: the real Windows guard, and <c>appsettings.json</c> beside the
    /// executable. The only construction the application itself uses.
    /// </summary>
    public static ApplicationStartup ForInstalledLayout() =>
        new(new SingleInstanceGuard(), DefaultConfigurationFilePath);

    /// <summary>The installed layout: <c>appsettings.json</c> beside the executable.</summary>
    public static string DefaultConfigurationFilePath =>
        System.IO.Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    /// <summary>Releases the single-instance guard. Ordinary application shutdown only.</summary>
    public void Dispose() => _guard.Dispose();

    /// <summary>Runs the sequence once.</summary>
    public async Task<StartupResult> RunAsync(CancellationToken cancellationToken)
    {
        // 1 — single-instance guard.
        SingleInstanceOutcome outcome = _guard.TryAcquire();
        switch (outcome)
        {
            case SingleInstanceOutcome.AlreadyRunning:
                return new StartupResult(StartupStatus.SecondInstance(), null);

            case SingleInstanceOutcome.Unavailable:
                return new StartupResult(
                    StartupStatus.Refused(
                        StartupStage.SingleInstanceGuard,
                        null,
                        "The single-instance guard could not be evaluated, so this process cannot prove " +
                        "it is the only PrintFlow instance. Refusing to start.",
                        isPrimaryInstance: false),
                    null);
        }

        // 2 — configuration.
        PrintFlowConfiguration configuration;
        try
        {
            configuration = PrintFlowConfiguration.LoadFromFile(_configurationFilePath);
        }
        catch (Exception ex) when (ex is System.IO.IOException
                                       or UnauthorizedAccessException
                                       or System.Text.Json.JsonException
                                       or InvalidOperationException
                                       or ArgumentException)
        {
            return Refuse(
                StartupStage.Configuration,
                null,
                $"Configuration could not be loaded from '{_configurationFilePath}': {ex.Message}");
        }

        // 3 — application-owned root directories.
        string workspaceRoot;
        try
        {
            workspaceRoot = System.IO.Path.GetFullPath(configuration.Workspace.Root);
            System.IO.Directory.CreateDirectory(workspaceRoot);
        }
        catch (Exception ex) when (ex is System.IO.IOException
                                       or UnauthorizedAccessException
                                       or ArgumentException
                                       or NotSupportedException)
        {
            return Refuse(
                StartupStage.WorkspaceRoot,
                FailureCode.WorkspaceError,
                $"The workspace root '{configuration.Workspace.Root}' could not be prepared: {ex.Message}");
        }

        // 4 — database and migrations, before anything reads persistence.
        string databasePath = System.IO.Path.Combine(workspaceRoot, configuration.Database.RelativePath);
        SqliteConnectionFactory connectionFactory;
        try
        {
            connectionFactory = new SqliteConnectionFactory(databasePath);
            using SqliteConnection migrationConnection = connectionFactory.Open();
            OperationResult<Unit> migrated = MigrationRunner.Migrate(migrationConnection);
            if (migrated.IsFailure)
            {
                return Refuse(StartupStage.Database, migrated.Failure.Code, migrated.Failure.TechnicalDetail);
            }
        }
        catch (Exception ex) when (ex is SqliteException
                                       or System.IO.IOException
                                       or UnauthorizedAccessException)
        {
            return Refuse(
                StartupStage.Database,
                FailureCode.PersistenceError,
                $"The application database at '{databasePath}' could not be opened: {ex.Message}");
        }

        // 5 — the service graph. Registration only; fails closed on an unsupported adapter mode.
        ServiceProvider services;
        try
        {
            services = ServiceRegistration.BuildServiceProvider(
                configuration, workspaceRoot, connectionFactory, _serviceOverrides);
        }
        catch (Exception ex) when (ex is NotSupportedException or FormatException or ArgumentException)
        {
            return Refuse(StartupStage.Composition, null, ex.Message);
        }

        try
        {
            // 6 — signed preset integrity. Recorded, never blocking: the Epic 11100 semantics are
            // that an unverified preset stops production work at the gate, not at the shell.
            OperationResult<Domain.Outputs.ProductionPresetRef> preset =
                services.GetRequiredService<IWorkstationPresetProvider>().GetVerifiedPreset();

            // 7 — recovery, exactly once, from the process that owns the guard.
            OperationResult<StartupRecoveryReport> recovered = await services
                .GetRequiredService<IStartupRecoveryService>()
                .RecoverAsync(cancellationToken)
                .ConfigureAwait(true);

            if (recovered.IsFailure)
            {
                // Recovery infrastructure failed — distinct from "recovery completed with
                // entries". Persisted state has not been reconciled with reality, so proceeding
                // would let the operator act on records recovery was meant to correct.
                services.Dispose();
                return new StartupResult(
                    StartupStatus.Refused(
                        StartupStage.Recovery, recovered.Failure.Code, recovered.Failure.TechnicalDetail),
                    null);
            }

            // 8 — record the result where the shell can read it.
            StartupStatus status = StartupStatus.Started(preset.IsSuccess, recovered.Value);
            services.GetRequiredService<StartupStatusAccessor>().Publish(status);

            // 9 — the caller composes and shows the shell.
            return new StartupResult(status, services);
        }
        catch
        {
            services.Dispose();
            throw;
        }
    }

    private static StartupResult Refuse(StartupStage stage, FailureCode? code, string detail) =>
        new(StartupStatus.Refused(stage, code, detail), null);
}
