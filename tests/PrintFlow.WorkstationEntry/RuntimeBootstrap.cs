using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using PrintFlow.App;
using PrintFlow.App.Localisation;
using PrintFlow.App.Navigation;
using PrintFlow.App.Startup;
using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Adapters.Fake;
using PrintFlow.Infrastructure.Preset;
using PrintFlow.Infrastructure.Sqlite;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Delivery;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;
using PrintFlow.WorkstationEntry.Scenarios;

namespace PrintFlow.WorkstationEntry;

public static class RuntimeBootstrap
{
    public static int Execute(ValidatedInput input, EntryOptions options, TextWriter output)
    {
        if (System.Reflection.Assembly.GetEntryAssembly() != typeof(Program).Assembly)
            throw new InvalidOperationException("Runtime bootstrap is only valid in its dedicated test-owned executable.");
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA) throw new InvalidOperationException("Runtime entry requires STA.");
        if (options.Mode == "Interactive")
        {
            if (!options.SafeDesktopConfirmed) throw new ArgumentException("Fresh desktop acknowledgment is required.");
            OwnedRun.VerifyResume(input.Plan.Root, input.CandidateHash, input.ScenarioHash, true);
        }
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        DispatcherFrame frame = new();
        Task<int> task = RunAsync(input, options, output);
        _ = task.ContinueWith(_ => Dispatcher.CurrentDispatcher.BeginInvoke(() => frame.Continue = false),
            CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.FromCurrentSynchronizationContext());
        Dispatcher.PushFrame(frame);
        return task.GetAwaiter().GetResult();
    }

    private static async Task<int> RunAsync(ValidatedInput input, EntryOptions options, TextWriter output)
    {
        using OwnedRun run = OwnedRun.Claim(input, options.Resume || options.Mode == "Interactive");
        OwnedPaths paths = run.Paths;
        using CancellationTokenSource cancellation = new(TimeSpan.FromMinutes(5));
        CancellationToken ct = cancellation.Token;
        string databasePath = paths.Require(paths.At("state", "app.db"), "state");
        SqliteConnectionFactory database = new(databasePath);
        using (SqliteConnection migration = database.Open())
        {
            OperationResult<Unit> result = MigrationRunner.Migrate(migration);
            if (result.IsFailure) throw new IOException("Isolated migration refused: " + result.Failure.TechnicalDetail);
        }
        string presetPath = paths.At("preset", "synthetic-preset.json");
        paths.Require(presetPath, "preset");
        if (!File.Exists(presetPath))
        {
            using FileStream presetFile = new(presetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using StreamWriter writer = new(presetFile);
            writer.Write(PresetFixture.Json);
        }
        using NativePathLease presetIdentity = NativePathLease.ReadFile(presetPath);
        PrintFlow.Domain.Files.Sha256 presetHash = PrintFlow.Domain.Files.Sha256.FromBytes(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(presetPath)));
        WorkstationPresetProvider preset = new(presetPath, PresetFixture.PresetId, PresetFixture.PresetVersion, presetHash);
        ContainedNativePorts native = options.Mode == "Interactive"
            ? new(paths, new OpenFileDialogPicker(), new OpenFolderDialogPicker(), new SaveDiagnosticPackageDialog(),
                new PrintFlow.Infrastructure.Delivery.WindowsDeliveredFileShell(), new PrintFlow.Infrastructure.Workspace.WindowsCorrectionFolderShell())
            : new(paths); // Every authorized run in this task uses recording/refusing delegates.
        using ServiceProvider services = EntryComposition.Build(paths, database, preset, native);
        OwnedWork ownedWork = new();
        INavigationService trackedNavigation = services.GetRequiredService<INavigationService>();
        trackedNavigation.CurrentChanged += (_, _) => ownedWork.ObserveScreen(trackedNavigation.Current);
        OperationResult<PrintFlow.Domain.Outputs.ProductionPresetRef> presetResult = preset.GetVerifiedPreset();
        if (presetResult.IsFailure) throw new IOException("Synthetic preset integrity failed.");
        OperationResult<StartupRecoveryReport> recovered = await services.GetRequiredService<IStartupRecoveryService>().RecoverAsync(ct);
        if (recovered.IsFailure) throw new IOException("Isolated recovery failed: " + recovered.Failure.TechnicalDetail);
        DiagnosticRetentionReport retention = await services.GetRequiredService<IDiagnosticRetentionService>().MaintainAsync(ct);
        await services.GetRequiredService<ILocalisationService>().RestoreAsync(ct);
        StartupStatus startup = StartupStatus.Started(true, recovered.Value, retention);
        services.GetRequiredService<StartupStatusAccessor>().Publish(startup);
        Write(paths, "startup-summary-" + run.Ownership.OwnerToken + ".json", new { Evidence = "SYNTHETIC_ONLY", startup, RunToken = run.Ownership.RunToken, Database = databasePath, SchemaVersion = MigrationRunner.NewestKnownVersion });

        Application application = new() { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        MainWindow window = new() { DataContext = services.GetRequiredService<ShellViewModel>() };
        UIElement productContent = (UIElement)window.Content;
        window.Content = null;
        DockPanel host = new();
        TextBlock banner = new() { Text = "SYNTHETIC WORKSTATION ENTRY — FAKE PROCESSORS — PRODUCTION DENIED", Background = Brushes.Gold, Foreground = Brushes.Black, Padding = new Thickness(8) };
        DockPanel.SetDock(banner, Dock.Top);
        host.Children.Add(banner); host.Children.Add(productContent); window.Content = host;
        window.Title = "SYNTHETIC — PrintFlow Studio";
        bool graphSmokeCompleted = false;
        try
        {
            if (options.Mode == "Interactive")
            {
                // Future path only: matching prepared ownership and fresh acknowledgment checked
                // above, no acknowledgment is written to disk. Never invoked by current verification.
                TaskCompletionSource closed = new();
                window.Closed += (_, _) => closed.TrySetResult();
                await services.GetRequiredService<INavigationService>().GoHomeAsync(ct);
                window.Show();
                await closed.Task;
                return 0;
            }
            if (options.Resume)
            {
                if (options.RestartChildToken is { } resumeToken)
                {
                    Task recoveryCheck = OwnedRestart.VerifyRecoveryAsync(services, run, input, resumeToken, recovered.Value, ownedWork, ct);
                    ownedWork.Track(recoveryCheck); await recoveryCheck.WaitAsync(ct);
                }
                else
                {
                    Task home = services.GetRequiredService<INavigationService>().GoHomeAsync(ct);
                    ownedWork.Track(home); await home.WaitAsync(ct);
                    Task screen = AwaitScreen(services.GetRequiredService<INavigationService>().Current);
                    ownedWork.Track(screen); await screen.WaitAsync(ct);
                }
                Write(paths, "resume-" + Guid.NewGuid().ToString("N") + ".json", new { Recovered = recovered.Value, WindowShown = false, NativeDispatches = native.NativeDispatches });
                return 0;
            }
            if (options.RestartChildToken is { } childToken)
            {
                Task child = OwnedRestart.RunChildAsync(services, run, input, childToken, ownedWork, ct);
                ownedWork.Track(child); await child.WaitAsync(ct);
                return 3;
            }
            await LeaseChecks.RunAsync(paths, "test." + input.Plan.Scenario.RunId, ct);
            Task<ScenarioLedger> scenarios = ScenarioRunner.RunAsync(new(paths.Root, services.GetRequiredService<ISessionService>(),
                services.GetRequiredService<IWorkspace>(), services.GetRequiredService<ISessionRepository>(),
                services.GetRequiredService<IApprovedArtifactDeliveryService>(), services.GetRequiredService<IProductionTiffReviewService>(),
                services.GetRequiredService<FakeMeituProcessor>(), services.GetRequiredService<FakePhotoshopOutputProcessor>(), paths.Admit,
                path => { paths.Require(path, PathRules.Within(path, paths.At("evidence")) ? "evidence" : "fixtures"); if (File.Exists(path) || Directory.Exists(path)) throw new IOException("Owned fixture/evidence write requires CreateNew."); }, ownedWork.Track), ct);
            ownedWork.Track(scenarios);
            ScenarioLedger ledger = await scenarios;
            List<string> screens = await SmokeNavigation(services, native, paths, host, ct);
            bool failed = ledger.Scenarios.Any(entry => entry.Status is "FAILED" or "BLOCKED_REQUIRED_SCENARIO" or "INCOMPLETE_CANCELLED");
            Write(paths, "graph-smoke.json", new { Screens = screens, WindowShown = false, NativeDispatches = native.NativeDispatches, ScenariosFailed = failed,
                UnverifiedScenarioRows = ledger.Scenarios.Where(entry => entry.Status != "VERIFIED_SYNTHETIC_SERVICE_FACTS").Select(entry => new { entry.Id, entry.Status, entry.Limitation }),
                PreservedLimitations = new[] { "KeepOriginalExtent downstream approval/promotion gate remains qualified by recorded facts.",
                    "Legacy customer-size binding is not manufactured; only lawful current review service calls are evidence.",
                    "Interrupted recovery requires the separate owned-child run; no power-loss claim.",
                    "Failure-notice direct Details route remains an approved gap; smoke uses existing Recent reload and Session command.",
                    "F6 is only a negative persisted-size/decoded-size mismatch; maximum-side/both-dimension acceptance remains open.",
                    "Physical/native/editor/picker actions not actually performed remain NOT_RUN; see scenario fact limits." },
                RegistrationAllowlist = EntryComposition.Allowlist });
            if (native.NativeDispatches != 0 || window.IsVisible) throw new InvalidOperationException("Noninteractive boundary violated.");
            graphSmokeCompleted = !failed;
            if (failed) output.WriteLine("PARTIAL: inspect runtime ledger; entry is not ready for a visible run.");
            // The return completes only after the finally block has settled and retained
            // this run's typed prepared record. Any teardown/identity/write error refuses it.
            return failed ? 3 : 0;
        }
        finally
        {
            Exception? cancellationFailure = null;
            try { cancellation.Cancel(); window.IsEnabled = false; }
            catch (Exception ex) { cancellationFailure = ex; } // Still settle before any ownership unwind.
            ownedWork.ObserveScreen(trackedNavigation.Current);
            ownedWork.CloseRegistration();
            bool settled = await ownedWork.WaitForQuiescenceAsync(TimeSpan.FromSeconds(20));
            void RecordQuiescence() => Write(paths, "quiescence-" + Guid.NewGuid().ToString("N") + ".json", new { OwnedTasksSettled = settled, PendingTasks = ownedWork.PendingCount,
                LateRegistration = ownedWork.LateRegistration, WindowShown = options.Mode == "Interactive", EvidenceRetained = true, AtUtc = DateTimeOffset.UtcNow });
            if (!settled)
            {
                // Never release a guard/provider around a live owned operation. Terminate this
                // isolated child host only; OS teardown stops its work and then releases handles.
                // This is a failed/incomplete run, not graceful-shutdown evidence.
                try
                {
                    RecordQuiescence();
                    output.WriteLine("INCOMPLETE_NONQUIESCENT: retained evidence; terminating only this owned host process.");
                }
                finally { Environment.Exit(4); } // Even disk-full/reporting failure cannot unwind live ownership.
            }
            else RecordQuiescence();
            window.Close();
            application.Shutdown();
            services.Dispose();
            using (SqliteConnection pool = new(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString())) SqliteConnection.ClearPool(pool);
            // The real lease manager has its own pool. Close it while WAL/SHM identity holds
            // still exist, so process-exit cleanup cannot delete sidecars after ownership ends.
            using (SqliteConnection leasePool = new(new SqliteConnectionStringBuilder { DataSource = paths.At("state", "automation-lease.db") }.ToString())) SqliteConnection.ClearPool(leasePool);
            if (cancellationFailure is not null) throw new IOException("Shutdown cancellation failed after owned work settled.", cancellationFailure);
            if (graphSmokeCompleted)
            {
                paths.ProtectDatabase(databasePath, createNew: false);
                paths.ProtectDatabase(paths.At("state", "automation-lease.db"), createNew: false);
                string preparedPath = paths.Require(paths.At("state", "prepared.json"), "state");
                using FileStream preparedFile = new(preparedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                JsonSerializer.Serialize(preparedFile, new PreparedRun(input.CandidateHash, input.ScenarioHash,
                    run.Ownership.RunToken, run.Ownership.OwnerToken, DateTimeOffset.UtcNow, "NONINTERACTIVE_ONLY"), ManifestReader.Json);
                preparedFile.Flush(true);
                output.WriteLine("PREPARED_NONINTERACTIVE: synthetic graph verified and owned work settled; matching prepared record retained.");
            }
        }
    }

    private static async Task<List<string>> SmokeNavigation(ServiceProvider services, ContainedNativePorts native, OwnedPaths paths, FrameworkElement host, CancellationToken ct)
    {
        INavigationService navigation = services.GetRequiredService<INavigationService>();
        List<string> visited = [];
        async Task Record()
        {
            await AwaitScreen(navigation.Current); await Dispatcher.Yield(DispatcherPriority.DataBind);
            host.Measure(new Size(1000, 700)); host.Arrange(new Rect(0, 0, 1000, 700)); host.UpdateLayout();
            visited.Add(navigation.Current!.GetType().Name);
        }
        await navigation.GoHomeAsync(ct); await Record();
        native.NextFixture = Directory.EnumerateFiles(paths.At("fixtures", "inputs"), "*.png").First();
        await ((HomeViewModel)navigation.Current!).ChooseFileCommand.ExecuteAsync(null); await Record();
        if (navigation.Current is not WorkflowSelectionViewModel selection) throw new InvalidOperationException("Home import did not reach real route selection.");
        await selection.SelectCommand.ExecuteAsync(selection.Workflows.First(choice => choice.Type == WorkflowType.PrepareAsset)); await Record();
        if (navigation.Current is not SessionViewModel session) throw new InvalidOperationException("Route did not reach real Session.");
        await session.ConfirmOriginalCommand.ExecuteAsync(null); await Record();
        FakeMeituProcessor fake = services.GetRequiredService<FakeMeituProcessor>();
        fake.SetScenario(FakeAdapterScenario.FailWith(FailureCode.MeituLaunchFailed));
        try { await session.RunStepCommand.ExecuteAsync(null); await Record(); }
        finally { fake.SetScenario(FakeAdapterScenario.Succeed); }
        PrintFlow.Domain.Ids.SessionId failedId = session.Id;
        await session.BackToHomeCommand.ExecuteAsync(null); await Record();
        HomeViewModel recent = (HomeViewModel)navigation.Current!;
        RecentSessionRow failedRow = recent.RecentSessions.Single(row => row.Id == failedId);
        await recent.ResumeCommand.ExecuteAsync(failedRow); await Record();
        SessionViewModel failedSession = (SessionViewModel)navigation.Current!;
        if (!failedSession.CanOpenErrorDetails) throw new InvalidOperationException("Reloaded failed Session does not offer its existing Error Details command.");
        await failedSession.OpenErrorDetailsCommand.ExecuteAsync(null); await Record();
        if (navigation.Current is not ErrorDetailsViewModel details) throw new InvalidOperationException("Existing Error Details command did not navigate.");
        await details.BackCommand.ExecuteAsync(null); await Record();
        await ((SessionViewModel)navigation.Current!).BackToHomeCommand.ExecuteAsync(null); await Record();
        HomeViewModel recovery = (HomeViewModel)navigation.Current!;
        if (recovery.RecoverySessions.Count > 0)
        {
            await recovery.OpenRecoveryCommand.ExecuteAsync(recovery.RecoverySessions[0]); await Record();
            await ((SessionViewModel)navigation.Current!).BackToHomeCommand.ExecuteAsync(null); await Record();
        }
        else visited.Add("RecoveryList: no interrupted candidate; owned-child restart evidence remains required");
        await ((HomeViewModel)navigation.Current!).ShowSettingsCommand.ExecuteAsync(null); await Record();
        await ((SettingsViewModel)navigation.Current!).BackToHomeCommand.ExecuteAsync(null); await Record();
        await ((HomeViewModel)navigation.Current!).ShowEnvironmentCommand.ExecuteAsync(null); await Record();
        await ((EnvironmentReadinessViewModel)navigation.Current!).BackToHomeCommand.ExecuteAsync(null); await Record();
        return visited;
    }

    private static Task AwaitScreen(object? screen) => screen switch
    {
        HomeViewModel home => home.ThumbnailsLoaded,
        WorkflowSelectionViewModel selection => selection.PreviewLoaded,
        SessionViewModel session => Task.WhenAll(session.PreviewsLoaded, session.PreflightLoaded, session.FinalSaveFactsLoaded),
        _ => Task.CompletedTask
    };
    private static void Write(OwnedPaths paths, string leaf, object value)
    {
        string path = paths.Require(paths.At("evidence", leaf), "evidence");
        using FileStream file = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(file, value, ManifestReader.Json);
        file.Flush(true);
    }
}
