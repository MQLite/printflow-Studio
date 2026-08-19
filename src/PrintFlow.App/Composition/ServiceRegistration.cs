using Microsoft.Extensions.DependencyInjection;
using PrintFlow.App.Navigation;
using PrintFlow.App.Startup;
using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Infrastructure.Adapters.Fake;
using PrintFlow.Infrastructure.Configuration;
using PrintFlow.Infrastructure.Diagnostics;
using PrintFlow.Infrastructure.Gate;
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
/// Registration only. Configuration loading, directory creation, database migration, preset
/// verification and crash recovery are the ordered startup <i>sequence</i> and belong to
/// <see cref="ApplicationStartup"/> (Epic 11100 Part 3C1 §4) — keeping them out of here is what
/// makes "recovery runs exactly once, from the primary instance" checkable rather than hoped for.
/// An architecture test asserts that no type outside this namespace references an Infrastructure
/// type.
/// </remarks>
public static class ServiceRegistration
{
    /// <summary>
    /// Registers everything the shell needs, against an already-migrated database.
    /// </summary>
    /// <param name="configuration">The loaded <c>appsettings.json</c>.</param>
    /// <param name="workspaceRootAbsolute">The resolved, already-created workspace root.</param>
    /// <param name="connectionFactory">A factory for the already-migrated database.</param>
    /// <param name="overrides">
    /// Applied last, so a test can substitute one registration without rebuilding the graph by
    /// hand. Nothing in the application passes it.
    /// </param>
    public static ServiceProvider BuildServiceProvider(
        PrintFlowConfiguration configuration,
        string workspaceRootAbsolute,
        SqliteConnectionFactory connectionFactory,
        Action<IServiceCollection>? overrides = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRootAbsolute);
        ArgumentNullException.ThrowIfNull(connectionFactory);

        string presetManifestPath =
            System.IO.Path.Combine(workspaceRootAbsolute, configuration.Preset.Path);
        Sha256 expectedPresetHash = Sha256.Parse(configuration.Preset.ExpectedSha256);
        WorkstationPresetProvider presetProvider = new(
            presetManifestPath, configuration.Preset.Id, configuration.Preset.Version, expectedPresetHash);

        ServiceCollection services = new();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IIdGenerator>(SystemIdGenerator.Instance);
        services.AddSingleton<IWorkflowEngine>(WorkflowEngine.Instance);
        services.AddSingleton<IWorkstationPresetProvider>(presetProvider);
        services.AddSingleton<IFileInspector, WicFileInspector>();
        services.AddSingleton<IWorkspace>(new FileWorkspace(workspaceRootAbsolute));
        services.AddSingleton<IRecycleBin, RecycleBin>();
        services.AddSingleton<IEnvironmentGate, FoundationEnvironmentGate>();
        services.AddSingleton<ISessionRepository>(new SqliteSessionRepository(connectionFactory));

        RegisterAdapters(services, configuration.Adapters.Mode);

        services.AddSingleton<ISessionService, SessionService>();

        // Composed here, invoked only by ApplicationStartup — once, behind the single-instance
        // guard and after migrations. Nothing else in the graph may call RecoverAsync.
        services.AddSingleton<IProcessLiveness, SystemProcessLiveness>();
        services.AddSingleton<IStartupRecoveryService, StartupRecoveryService>();

        services.AddSingleton<StartupStatusAccessor>();

        // Navigation is a singleton because "which screen is current" is one fact per window;
        // the screens themselves are transient so each visit starts from a clean view model
        // and cannot carry the previous session's state forward.
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IFilePicker, OpenFileDialogPicker>();
        services.AddSingleton<ShellViewModel>();
        services.AddTransient<HomeViewModel>();
        services.AddTransient<WorkflowSelectionViewModel>();
        services.AddTransient<SessionViewModel>();

        overrides?.Invoke(services);

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
