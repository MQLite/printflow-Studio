using Microsoft.Extensions.DependencyInjection;
using PrintFlow.App.Navigation;
using PrintFlow.App.Startup;
using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Infrastructure.Adapters.Fake;
using PrintFlow.Infrastructure.Adapters.Meitu;
using PrintFlow.Infrastructure.Adapters.Photoshop;
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
        services.AddSingleton<IManualResultImporter, WicManualResultImporter>();
        services.AddSingleton<IPdfPreparationProcessor, PrintFlow.Infrastructure.Imaging.WindowsPdfPreparationProcessor>();
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
        RegisterEnvironmentGate(services, configuration, workspaceRootAbsolute, presetManifestPath,
            expectedPresetHash, connectionFactory);
        services.AddSingleton<ISessionRepository>(new SqliteSessionRepository(connectionFactory));

        // The settings store the same migrated database already carries (Jira 11108; MVP design
        // §17.6). Registered beside the session repository and against the same connection
        // factory, so a persisted setting and a persisted session are the same database's facts.
        // Which values the operator may edit, and what wins when a persisted value disagrees
        // with appsettings.json or the signed preset, is SCRUM-11118's decision; nothing in this
        // graph reads a setting yet, and no current default changed.
        services.AddSingleton<ISettingsRepository>(new SqliteSettingsRepository(connectionFactory));

        RegisterAdapters(
            services,
            configuration.Adapters is { } adapters ? adapters.Mode : null,
            workspaceRootAbsolute,
            presetManifestPath,
            expectedPresetHash);

        services.AddSingleton<ISessionService, SessionService>();

        // The read-only image seam (Epic 11200 Part C1 §3). Registered beside the session
        // service rather than inside it: previews change nothing, and a screen that could only
        // reach them through the command service would blur that.
        services.AddSingleton<IImagePreviewDecoder, WicImagePreviewDecoder>();
        services.AddSingleton<IArtefactPreviewService, ArtefactPreviewService>();

        // The specialist production-TIFF review seam (SCRUM-11104 §14, §43). A second read-only
        // seam rather than a wider first one: the general review surface compares artefacts, and
        // teaching it to read separated CMYK and a Photoshop spot channel would make every
        // ordinary preview carry a TIFF parser it never uses.
        services.AddSingleton<ITiffReviewDecoder, ProductionTiffReviewDecoder>();
        services.AddSingleton<IProductionTiffReviewService, ProductionTiffReviewService>();

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

        // The operator surface onto workstation readiness (Epic 11500 Part C §3). It resolves
        // IEnvironmentDiagnostics — the same object the gate is — and nothing else that could
        // reach the workstation. Passive refresh and the explicitly labelled bounded live check
        // share that authority; the shell still has no route to authorisation.
        services.AddTransient<EnvironmentReadinessViewModel>();

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
        Sha256 expectedPresetHash,
        SqliteConnectionFactory connectionFactory)
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
                provider.GetRequiredService<IWorkspace>(),
                connectionFactory,
                System.IO.Path.Combine(workspaceRootAbsolute, EvidenceFolderName),
                provider.GetRequiredService<TimeProvider>()));

        services.AddSingleton<VerifiedEnvironmentGate>();
        services.AddSingleton<IEnvironmentGate>(p => p.GetRequiredService<VerifiedEnvironmentGate>());
        services.AddSingleton<IEnvironmentDiagnostics>(p => p.GetRequiredService<VerifiedEnvironmentGate>());
    }

    /// <summary>
    /// Chooses, from the one configured mode, which pair of adapters the workflow will drive
    /// (Epic 11500 Part D §2).
    /// </summary>
    /// <remarks>
    /// <b>One mode decides both processors.</b> There is no per-adapter selection and no hybrid:
    /// the accepted preset describes one production workstation, the gate asks one question about
    /// it (Part B §13), and a composition that could run a real Photoshop against a fake Meitu
    /// would make that single answer a lie. Both branches therefore register both ports, and
    /// there is no arrangement of configuration that registers one of each.
    /// <para>
    /// <b>Composition is not authorisation.</b> Constructing the Production adapters means only
    /// that the objects exist; whether they may run is decided later and elsewhere, by
    /// <see cref="IEnvironmentGate"/>, on every adapter-backed step. Nothing here consults the
    /// verifier, and nothing here pre-authorises a workflow — which is why
    /// <c>Adapters.Mode = Production</c> on a workstation that fails verification produces an
    /// application that starts normally and refuses Production work, rather than one that cannot
    /// start (§5).
    /// </para>
    /// <para>
    /// <b>Nothing is launched, read or hashed here.</b> Both compositions assemble Win32 locators,
    /// input sinks and preset-backed baseline providers, and every one of them is inert until an
    /// operation runs: the baseline providers read the signed manifest lazily, the evidence sink
    /// creates its directory only when it captures, and no constructor starts a process or sends
    /// an input. The accepted executables and the canonical Action are re-verified by the adapters
    /// at operation time, immediately before they are used, and not by this method (§8).
    /// </para>
    /// <para>
    /// <b>No fallback, in either direction.</b> An unknown, absent or empty mode throws rather
    /// than defaulting, and the Production branch has no path that substitutes a fake — including
    /// when Production initialisation fails. A workstation configured for Production must never
    /// quietly run against doubles, and one configured for Fake must never reach a real
    /// application.
    /// </para>
    /// </remarks>
    private static void RegisterAdapters(
        ServiceCollection services,
        string? adapterMode,
        string workspaceRootAbsolute,
        string presetManifestPath,
        Sha256 expectedPresetHash)
    {
        switch (adapterMode)
        {
            case "Fake":
                services.AddSingleton<IMeituProcessor, FakeMeituProcessor>();
                services.AddSingleton<IPhotoshopOutputProcessor, FakePhotoshopOutputProcessor>();
                break;

            case "Production":
                // Failure captures live beside the workspace rather than inside a session: they
                // are diagnostic material about this workstation, and keeping them out of
                // Sessions\ is what stops one being promoted, hashed into a Revision, or deleted
                // with the session that happened to be running. The directory is created on first
                // capture, so a Production installation that never fails never grows one.
                string evidenceDirectory =
                    System.IO.Path.Combine(workspaceRootAbsolute, EvidenceFolderName);

                // The accepted adapters from Epics 11300 and 11400, composed through the same
                // factories the controlled workstation smokes have used since those epics — so
                // what the application now registers is the object graph that was accepted there,
                // not a second implementation assembled here to look like it.
                services.AddSingleton<IMeituProcessor>(provider =>
                    MeituAutomationComposition.CreateProductionProcessor(
                        presetManifestPath,
                        expectedPresetHash,
                        provider.GetRequiredService<IWorkspace>(),
                        evidenceDirectory,
                        provider.GetRequiredService<TimeProvider>()));

                services.AddSingleton<IPhotoshopOutputProcessor>(provider =>
                    PhotoshopAutomationComposition.CreateProductionProcessor(
                        presetManifestPath,
                        expectedPresetHash,
                        provider.GetRequiredService<IWorkspace>(),
                        evidenceDirectory,
                        provider.GetRequiredService<TimeProvider>()));
                break;

            default:
                throw new NotSupportedException(
                    $"Unknown Adapters:Mode '{adapterMode ?? "(absent)"}'. Expected 'Fake' or 'Production'.");
        }
    }

    /// <summary>Where a Production adapter writes a failure capture, beside the workspace areas.</summary>
    private const string EvidenceFolderName = "Evidence";
}
