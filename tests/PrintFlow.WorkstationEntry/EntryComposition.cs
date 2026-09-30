using Microsoft.Extensions.DependencyInjection;
using PrintFlow.App.Localisation;
using PrintFlow.App.Navigation;
using PrintFlow.App.Settings;
using PrintFlow.App.Startup;
using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Ids;
using PrintFlow.Infrastructure.Adapters.Fake;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Infrastructure.Diagnostics;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Infrastructure.Preset;
using PrintFlow.Infrastructure.Sqlite;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Workflow.Delivery;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.WorkstationEntry;

public static class EntryComposition
{
    private static readonly HashSet<Type> RequiredServices =
    [
        typeof(OwnedPaths), typeof(TimeProvider), typeof(IIdGenerator), typeof(IWorkflowEngine), typeof(IWorkstationPresetProvider),
        typeof(IWorkspace), typeof(IFileInspector), typeof(IManualResultImporter), typeof(IPdfPreparationProcessor), typeof(ITrimProcessor),
        typeof(IManualCropProcessor), typeof(IRecycleBin), typeof(IWorkstationAutomationLeaseManager), typeof(SyntheticEnvironment),
        typeof(IEnvironmentGate), typeof(IEnvironmentDiagnostics), typeof(ISessionRepository), typeof(IDeliveryRepository), typeof(ISettingsRepository),
        typeof(ContainedDelivery), typeof(IDeliveryFileSystem), typeof(IApprovedArtifactDeliveryService), typeof(ICorrectionPackageStore),
        typeof(FinalSaveCoordinator), typeof(LocalDiagnosticLocations), typeof(IDiagnosticRetentionRepository), typeof(IDiagnosticFileStore),
        typeof(DiagnosticRetentionOptions), typeof(IDiagnosticRetentionService), typeof(DiagnosticPackageStorageLocations), typeof(DiagnosticPackageApplicationInfo),
        typeof(IDiagnosticPackageEvidenceInspector), typeof(IDiagnosticPackageWriter), typeof(SettingsDefaults), typeof(ILocalisationService),
        typeof(FakeMeituProcessor), typeof(FakePhotoshopOutputProcessor), typeof(IMeituProcessor), typeof(IPhotoshopOutputProcessor), typeof(IImagePreviewDecoder),
        typeof(IDiagnosticImagePreviewDecoder), typeof(IArtefactPreviewService), typeof(ISessionService), typeof(IDiagnosticPackageService), typeof(ITiffReviewDecoder),
        typeof(IProductionTiffReviewService), typeof(IProcessLiveness), typeof(IStartupRecoveryService), typeof(StartupStatusAccessor),
        typeof(ReadinessObservationAccessor), typeof(IEnvironmentReadinessObservations), typeof(INavigationService), typeof(ContainedNativePorts),
        typeof(IFilePicker), typeof(IDeliveryFolderPicker), typeof(IDiagnosticPackageDestinationPicker), typeof(IDeliveredFileShell), typeof(ICorrectionFolderShell),
        typeof(ShellViewModel), typeof(HomeViewModel), typeof(WorkflowSelectionViewModel), typeof(SessionViewModel), typeof(ErrorDetailsViewModel),
        typeof(EnvironmentReadinessViewModel), typeof(SettingsViewModel)
    ];
    private static readonly HashSet<Type> AllowedConcreteTypes =
    [
        typeof(OwnedPaths), TimeProvider.System.GetType(), typeof(SystemIdGenerator), typeof(WorkflowEngine), typeof(WorkstationPresetProvider),
        typeof(ContainedWorkspace), typeof(ContainedFileInspector), typeof(ContainedManualImporter), typeof(RefusingPdf), typeof(ContainedTrim),
        typeof(ContainedCrop), typeof(ContainedMeitu), typeof(ContainedPhotoshop), typeof(ContainedRecycle), typeof(SqliteWorkstationAutomationLeaseManager), typeof(SyntheticEnvironment),
        typeof(SqliteSessionRepository), typeof(SqliteDeliveryRepository), typeof(SqliteSettingsRepository), typeof(ContainedDelivery),
        typeof(ContainedCorrection), typeof(FinalSaveCoordinator), typeof(LocalDiagnosticLocations), typeof(SqliteDiagnosticRetentionRepository),
        typeof(ContainedDiagnosticStore), typeof(DiagnosticRetentionOptions), typeof(DiagnosticRetentionService), typeof(DiagnosticPackageStorageLocations),
        typeof(DiagnosticPackageApplicationInfo), typeof(ContainedDiagnosticEvidence), typeof(ContainedDiagnosticWriter), typeof(SettingsDefaults),
        typeof(LocalisationService), typeof(FakeMeituProcessor), typeof(FakePhotoshopOutputProcessor), typeof(ContainedPreviewDecoder),
        typeof(ArtefactPreviewService), typeof(SessionService), typeof(DiagnosticPackageService), typeof(ContainedTiffDecoder), typeof(ProductionTiffReviewService),
        typeof(SystemProcessLiveness), typeof(StartupRecoveryService), typeof(StartupStatusAccessor), typeof(ReadinessObservationAccessor),
        typeof(NavigationService), typeof(ContainedNativePorts), typeof(ShellViewModel), typeof(HomeViewModel), typeof(WorkflowSelectionViewModel),
        typeof(SessionViewModel), typeof(ErrorDetailsViewModel), typeof(EnvironmentReadinessViewModel), typeof(SettingsViewModel)
    ];
    public static string[] Allowlist => RequiredServices.Select(type => type.FullName!).Order().ToArray();
    // No caller-supplied factories/overrides. Every boundary is explicit; production registration
    // is never invoked. This method itself is a runtime phase, never called by Validate.
    public static ServiceProvider Build(OwnedPaths paths, SqliteConnectionFactory database, WorkstationPresetProvider preset,
        ContainedNativePorts native)
    {
        ServiceCollection services = new();
        services.AddSingleton(paths);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IIdGenerator>(SystemIdGenerator.Instance);
        services.AddSingleton<IWorkflowEngine>(WorkflowEngine.Instance);
        services.AddSingleton<IWorkstationPresetProvider>(preset);
        services.AddSingleton<IWorkspace, ContainedWorkspace>();
        services.AddSingleton<IFileInspector, ContainedFileInspector>();
        services.AddSingleton<IManualResultImporter, ContainedManualImporter>();
        services.AddSingleton<IPdfPreparationProcessor, RefusingPdf>();
        services.AddSingleton<ITrimProcessor, ContainedTrim>();
        services.AddSingleton<IManualCropProcessor, ContainedCrop>();
        services.AddSingleton<IRecycleBin, ContainedRecycle>();
        services.AddSingleton<IWorkstationAutomationLeaseManager>(new SqliteWorkstationAutomationLeaseManager(paths.At("state", "automation-lease.db"), "test." + Path.GetFileName(paths.Root)));
        services.AddSingleton<SyntheticEnvironment>();
        services.AddSingleton<IEnvironmentGate>(p => p.GetRequiredService<SyntheticEnvironment>());
        services.AddSingleton<IEnvironmentDiagnostics>(p => p.GetRequiredService<SyntheticEnvironment>());
        services.AddSingleton<ISessionRepository>(new SqliteSessionRepository(database));
        services.AddSingleton<IDeliveryRepository>(new SqliteDeliveryRepository(database));
        services.AddSingleton<ISettingsRepository>(new SqliteSettingsRepository(database));
        services.AddSingleton<ContainedDelivery>();
        services.AddSingleton<IDeliveryFileSystem>(p => p.GetRequiredService<ContainedDelivery>());
        services.AddSingleton<IApprovedArtifactDeliveryService>(p => new ApprovedArtifactDeliveryService(
            p.GetRequiredService<ISessionRepository>(), p.GetRequiredService<IDeliveryRepository>(), p.GetRequiredService<IWorkspace>(),
            p.GetRequiredService<IDeliveryFileSystem>(), p.GetRequiredService<ContainedDelivery>().ProtectedRoots,
            TimeProvider.System, p.GetRequiredService<ITiffReviewDecoder>()));
        services.AddSingleton<ICorrectionPackageStore, ContainedCorrection>();
        services.AddSingleton<FinalSaveCoordinator>();
        services.AddSingleton(new LocalDiagnosticLocations(database.DatabasePath, paths.At("evidence")));
        services.AddSingleton<IDiagnosticRetentionRepository>(new SqliteDiagnosticRetentionRepository(database, paths.At("workspace")));
        services.AddSingleton<IDiagnosticFileStore, ContainedDiagnosticStore>();
        services.AddSingleton(new DiagnosticRetentionOptions(paths.At("evidence"), 30, SettingsDefaults.FallbackLogRetentionDays, SettingsDefaults.MaximumLogRetentionDays));
        services.AddSingleton<IDiagnosticRetentionService, DiagnosticRetentionService>();
        services.AddSingleton(new DiagnosticPackageStorageLocations(database.DatabasePath, paths.At("evidence")));
        services.AddSingleton(new DiagnosticPackageApplicationInfo("PrintFlow Studio — SYNTHETIC ENTRY", typeof(EntryComposition).Assembly.GetName().Version!.ToString()));
        services.AddSingleton<IDiagnosticPackageEvidenceInspector, ContainedDiagnosticEvidence>();
        services.AddSingleton<IDiagnosticPackageWriter, ContainedDiagnosticWriter>();
        services.AddSingleton(new SettingsDefaults(30));
        services.AddSingleton<ILocalisationService, LocalisationService>();
        services.AddSingleton<FakeMeituProcessor>();
        services.AddSingleton<FakePhotoshopOutputProcessor>();
        services.AddSingleton<IMeituProcessor, ContainedMeitu>();
        services.AddSingleton<IPhotoshopOutputProcessor, ContainedPhotoshop>();
        services.AddSingleton<IImagePreviewDecoder, ContainedPreviewDecoder>();
        services.AddSingleton<IDiagnosticImagePreviewDecoder>(p => new ContainedDiagnosticDecoder(paths, new WicImagePreviewDecoder(p.GetRequiredService<IWorkspace>())));
        services.AddSingleton<IArtefactPreviewService, ArtefactPreviewService>();
        services.AddSingleton<ISessionService, SessionService>();
        services.AddSingleton<IDiagnosticPackageService, DiagnosticPackageService>();
        services.AddSingleton<ITiffReviewDecoder, ContainedTiffDecoder>();
        services.AddSingleton<IProductionTiffReviewService, ProductionTiffReviewService>();
        services.AddSingleton<IProcessLiveness, SystemProcessLiveness>();
        services.AddSingleton<IStartupRecoveryService, StartupRecoveryService>();
        services.AddSingleton<StartupStatusAccessor>();
        services.AddSingleton<ReadinessObservationAccessor>();
        services.AddSingleton<IEnvironmentReadinessObservations>(p => p.GetRequiredService<ReadinessObservationAccessor>());
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton(native);
        services.AddSingleton<IFilePicker>(native);
        services.AddSingleton<IDeliveryFolderPicker>(native);
        services.AddSingleton<IDiagnosticPackageDestinationPicker>(native);
        services.AddSingleton<IDeliveredFileShell>(native);
        services.AddSingleton<ICorrectionFolderShell>(native);
        services.AddSingleton<ShellViewModel>();
        services.AddTransient<HomeViewModel>();
        services.AddTransient<WorkflowSelectionViewModel>();
        services.AddTransient<SessionViewModel>();
        services.AddTransient<ErrorDetailsViewModel>();
        services.AddTransient<EnvironmentReadinessViewModel>();
        services.AddTransient<SettingsViewModel>();
        ValidateDescriptors(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    public static void ValidateDescriptors(IEnumerable<ServiceDescriptor> descriptors)
    {
        ServiceDescriptor[] bindings = descriptors.ToArray();
        if (bindings.GroupBy(b => b.ServiceType).Any(group => group.Count() != 1)) throw new ArgumentException("Duplicate service authority refused.");
        foreach (ServiceDescriptor descriptor in bindings)
        {
            Type? concrete = descriptor.ImplementationType ?? descriptor.ImplementationInstance?.GetType();
            if (descriptor.IsKeyedService || !RequiredServices.Contains(descriptor.ServiceType) ||
                (concrete is not null && !AllowedConcreteTypes.Contains(concrete)))
                throw new ArgumentException("Implementation is not in the closed isolated graph allowlist.");
            if (descriptor.ImplementationFactory is { } factory)
            {
                Type? declaration = factory.Method.DeclaringType;
                while (declaration?.DeclaringType is { } parent) declaration = parent;
                if (declaration != typeof(EntryComposition)) throw new ArgumentException("Unreviewed factory refused without invocation.");
            }
        }
        if (!RequiredServices.SetEquals(bindings.Select(d => d.ServiceType)))
            throw new ArgumentException("Required containment boundary is missing.");
    }
}
