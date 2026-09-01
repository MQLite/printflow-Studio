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
using PrintFlow.Infrastructure.Verification;
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

        // Registered unconditionally, outside RegisterAdapters: deterministic trimming drives
        // no external application, so it has no Fake/Production duality to choose between and
        // nothing about it is gated on the workstation environment (Epic 11200 Part B §17).
        services.AddSingleton<ITrimProcessor, DeterministicAlphaTrimProcessor>();

        // The manual-crop counterpart, registered on exactly the same terms and for the same
        // reasons: in-process pixel work, no external application, no environment gate. It is
        // the fallback the operator reaches only after the automatic trim has refused
        // (Epic 11200 Part C2 §9).
        services.AddSingleton<IManualCropProcessor, WicManualCropProcessor>();

        services.AddSingleton<IRecycleBin, RecycleBin>();
        RegisterEnvironmentGate(services, configuration, workspaceRootAbsolute, presetManifestPath, expectedPresetHash);
        services.AddSingleton<ISessionRepository>(new SqliteSessionRepository(connectionFactory));

        RegisterAdapters(services, configuration.Adapters.Mode);

        services.AddSingleton<ISessionService, SessionService>();

        // The read-only image seam (Epic 11200 Part C1 §3). Registered beside the session
        // service rather than inside it: previews change nothing, and a screen that could only
        // reach them through the command service would blur that.
        services.AddSingleton<IImagePreviewDecoder, WicImagePreviewDecoder>();
        services.AddSingleton<IArtefactPreviewService, ArtefactPreviewService>();

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

    /// <summary>
    /// Wires the workstation verifier and the one gate that consults it (Epic 11500 Part B §14).
    /// </summary>
    /// <remarks>
    /// Every accepted value comes from the configuration this method was handed — the already
    /// resolved workspace root, and the preset id, version, path and expected digest that
    /// <see cref="BuildServiceProvider"/> parsed once for
    /// <see cref="IWorkstationPresetProvider"/>. Nothing here re-reads <c>appsettings.json</c> or
    /// re-parses a hash, so a configured installation cannot end up with a preset provider and a
    /// verifier that disagree about which preset it is running.
    /// <para>
    /// <b>Registration only, and nothing is verified here.</b> Constructing the verifier reads no
    /// file; the manifest, the evidence chain, the binaries and the session are read on the first
    /// <c>Verify</c>, which happens when something asks for Production authorisation. That is what
    /// keeps a workstation that fails verification from turning into "PrintFlow cannot start"
    /// (§15) — the shell opens, Fake work proceeds, and only Production is closed.
    /// </para>
    /// <para>
    /// <b>Exactly one gate, and no permissive fallback.</b> There is no branch here on
    /// <c>Adapters:Mode</c>, no development gate, and no path that registers an
    /// <see cref="IEnvironmentGate"/> which authorises Production without asking the verifier
    /// (§14, §24). <see cref="VerifiedEnvironmentGate"/> is registered as a concrete singleton and
    /// both interfaces resolve to that same instance, so the object that authorises and the object
    /// that reports are one — and so no second consumer of the verifier exists.
    /// </para>
    /// </remarks>
    private static void RegisterEnvironmentGate(
        ServiceCollection services,
        PrintFlowConfiguration configuration,
        string workspaceRootAbsolute,
        string presetManifestPath,
        Sha256 expectedPresetHash)
    {
        // The narrow fact readers are composed by ProductionWorkstationVerifier.ForWorkstation
        // rather than registered here, because Part A §18 keeps them internal to Infrastructure
        // on purpose: between them they expose exactly the machine facts the preset names, and
        // publishing them so the composition root could name them would widen that surface for
        // no gain the graph does not already have. What matters — that the verifier reads the
        // machine through those two readers and through no shell, script or caller-chosen
        // registry path — is asserted by the verification boundary tests.
        services.AddSingleton<IProductionWorkstationVerifier>(provider =>
            ProductionWorkstationVerifier.ForWorkstation(
                presetManifestPath,
                configuration.Preset.Id,
                configuration.Preset.Version,
                expectedPresetHash,
                workspaceRootAbsolute,
                provider.GetRequiredService<TimeProvider>()));

        services.AddSingleton<VerifiedEnvironmentGate>();
        services.AddSingleton<IEnvironmentGate>(p => p.GetRequiredService<VerifiedEnvironmentGate>());
        services.AddSingleton<IEnvironmentDiagnostics>(p => p.GetRequiredService<VerifiedEnvironmentGate>());
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
                // The controlled Epic 11300 seam can now produce validated Enhancement and
                // reviewed-content Background Removal outputs. Global Production composition
                // nevertheless remains closed.
                //
                // Failing closed rather than falling back to the fake remains the point: a
                // workstation configured for Production must never quietly run against fakes.
                // The Part A foundation is exercised through
                // MeituAutomationComposition.CreateFoundation, which is reachable from the
                // controlled smoke and from tests but not from the normal Production
                // composition. Epic 11300's Meitu workflow authority is complete, but global
                // Production still waits for the Epic 11400 Photoshop adapter and Epic 11500's
                // authoritative workstation EnvironmentGate.
                throw new NotSupportedException(
                    "Adapters:Mode is 'Production', but global production automation remains closed: " +
                    "the production Photoshop adapter is owned by Epic 11400 and workstation verification " +
                    "is owned by Epic 11500. Refusing to start rather than silently substituting a fake.");

            default:
                throw new NotSupportedException(
                    $"Unknown Adapters:Mode '{adapterMode}'. Expected 'Fake' or 'Production'.");
        }
    }
}
