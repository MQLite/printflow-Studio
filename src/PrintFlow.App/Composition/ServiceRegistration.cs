using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Infrastructure.Adapters.Fake;
using PrintFlow.Infrastructure.Configuration;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Infrastructure.Preset;
using PrintFlow.Infrastructure.Sqlite;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.App.Composition;

/// <summary>
/// The composition root: the only place in <c>PrintFlow.App</c> permitted to see
/// <c>PrintFlow.Infrastructure</c>.
/// </summary>
/// <remarks>
/// Startup sequence, fail-closed at every stage (Epic 11100 plan §18):
/// configuration → workspace root → database + migrations → preset verification (recorded,
/// never blocking) → adapters (fails closed on <c>Adapters.Mode = Production</c>, which has no
/// implementation yet) → the service graph. An architecture test asserts that no type outside
/// this namespace references an Infrastructure type.
/// </remarks>
public static class ServiceRegistration
{
    /// <summary>Registers everything the shell needs to start.</summary>
    public static ServiceProvider BuildServiceProvider()
    {
        string baseDirectory = AppContext.BaseDirectory;
        string configPath = System.IO.Path.Combine(baseDirectory, "appsettings.json");
        PrintFlowConfiguration configuration = PrintFlowConfiguration.LoadFromFile(configPath);

        string workspaceRoot = System.IO.Path.GetFullPath(configuration.Workspace.Root);
        string databasePath = System.IO.Path.Combine(workspaceRoot, configuration.Database.RelativePath);

        SqliteConnectionFactory connectionFactory = new(databasePath);
        using (SqliteConnection migrationConnection = connectionFactory.Open())
        {
            var migrated = MigrationRunner.Migrate(migrationConnection);
            if (migrated.IsFailure)
            {
                throw new InvalidOperationException(
                    $"Database migration failed: {migrated.Failure}. PrintFlow will not start against an unmigrated or newer-than-known database.");
            }
        }

        string presetManifestPath = System.IO.Path.Combine(workspaceRoot, configuration.Preset.Path);
        Sha256 expectedPresetHash = Sha256.Parse(configuration.Preset.ExpectedSha256);
        WorkstationPresetProvider presetProvider = new(
            presetManifestPath, configuration.Preset.Id, configuration.Preset.Version, expectedPresetHash);

        ServiceCollection services = new();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IIdGenerator>(SystemIdGenerator.Instance);
        services.AddSingleton<IWorkflowEngine>(WorkflowEngine.Instance);
        services.AddSingleton<IWorkstationPresetProvider>(presetProvider);
        services.AddSingleton<IFileInspector, WicFileInspector>();
        services.AddSingleton<IWorkspace>(new FileWorkspace(workspaceRoot));
        services.AddSingleton<IRecycleBin, RecycleBin>();
        services.AddSingleton<ISessionRepository>(new SqliteSessionRepository(connectionFactory));

        RegisterAdapters(services, configuration.Adapters.Mode);

        services.AddSingleton<ISessionService, SessionService>();

        services.AddTransient<ShellViewModel>();

        return services.BuildServiceProvider();
    }

    private static void RegisterAdapters(ServiceCollection services, string adapterMode)
    {
        switch (adapterMode)
        {
            case "Fake":
                services.AddSingleton<IMeituProcessor, FakeMeituProcessor>();
                services.AddSingleton<IPhotoshopOutputProcessor, FakePhotoshopOutputProcessor>();
                break;

            case "Production":
                // No production adapter exists yet (Epic 11300/11400). Failing closed here,
                // rather than falling back to the fake silently, is the point: a workstation
                // configured for Production must never quietly run against fakes.
                throw new NotSupportedException(
                    "Adapters:Mode is 'Production', but no production Meitu/Photoshop adapter exists yet " +
                    "(Epic 11300/11400). Refusing to start rather than silently substituting a fake.");

            default:
                throw new NotSupportedException(
                    $"Unknown Adapters:Mode '{adapterMode}'. Expected 'Fake' or 'Production'.");
        }
    }
}
