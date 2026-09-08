using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PrintFlow.App.Navigation;
using PrintFlow.App.Startup;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Tests.Integration.Ui;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;
using Xunit.Abstractions;
using static PrintFlow.Tests.Integration.Ui.RecoverySurfaceTests;

namespace PrintFlow.Tests.Smoke;

/// <summary>Opt-in real WPF/UIA proof using synthetic files; never launches Meitu or Photoshop.</summary>
[Collection(SqliteCollection.Name)]
public sealed class RecoverySurfaceLiveSmoke(ITestOutputHelper output)
{
    [Fact]
    public async Task Live_A_restart_B_owned_dialog_manual_result_C_abandon()
    {
        string? evidence = Environment.GetEnvironmentVariable("PRINTFLOW_RECOVERY_SURFACE_LIVE");
        if (string.IsNullOrWhiteSpace(evidence)) return;
        Directory.CreateDirectory(evidence);
        List<string> log = ["Real WPF synthetic test window + UIA; real SQLite/FileWorkspace/StartupRecoveryService/SessionService. No external application automation."];
        string phase = Environment.GetEnvironmentVariable("PRINTFLOW_RECOVERY_SURFACE_PHASE")
            ?? throw new InvalidOperationException("Run A, B and C in separate test-host processes, setting PRINTFLOW_RECOVERY_SURFACE_PHASE.");
        new[] { "A", "B", "C" }.ShouldContain(phase);
        try
        {
            await Run(phase, evidence, log);
        }
        finally
        {
            await File.WriteAllLinesAsync(Path.Combine(evidence, "live-transcript-" + phase + ".txt"), log);
            foreach (string line in log) output.WriteLine(line);
        }
    }

    private static async Task Run(string phase, string evidence, List<string> log)
    {
        using SessionServiceHarness h = new();
        h.Database.RetainForInspection = true;
        h.Workspace.RetainForInspection = true;
        SessionId id = await Seed(h, "PF_RECOVERY_LIVE_" + phase);
        SessionAggregate before = await Load(h, id);
        string source = Path.Combine(h.Workspace.Root, "PF_RECOVERY_LIVE_" + phase + ".png");
        byte[] sourceBytes = File.ReadAllBytes(source);
        Revision upstream = before.Revisions.Single(r => r.SourceRevisionId is null);
        byte[] snapshot = File.ReadAllBytes(h.FileWorkspace.ResolveAbsolute(upstream.File));
        string manual = h.WriteSourcePng("saved-manual.png");
        byte[] manualBytes = File.ReadAllBytes(manual);
        log.Add($"{phase}: session={id}; SQLite={h.Database.Path}; workspace={h.Workspace.Root}; Interrupted attempt={before.Attempts.Single(a => a.Status == AttemptStatus.Interrupted).Id}");
        // A second startup before selecting anything must reconstruct the unresolved entry.
        var startup = await h.CreateRecoveryService(new FakeProcessLiveness(ProcessLiveness.Dead)).RecoverAsync(default);
        startup.IsSuccess.ShouldBeTrue();
        (await h.CreateService().ListRecoveryAsync(default)).Value.Single().Id.ShouldBe(id);

        WpfRendering.OnStaThread(() =>
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            var service = h.CreateService();
            RecordingNavigation nav = new();
            StartupStatusAccessor startupStatus = new();
            startupStatus.Publish(StartupStatus.Started(presetVerified: false, startup.Value));
            HomeViewModel model = new(service, nav, new OpenFileDialogPicker(), startupStatus);
            model.RefreshCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            HomeView view = new() { DataContext = model };
            Window window = new() { Title = "PrintFlow synthetic recovery WPF proof " + phase, Content = view,
                Width = 1100, Height = 820, WindowStartupLocation = WindowStartupLocation.CenterScreen };
            nav.CurrentChanged += (_, _) =>
            {
                if (nav.SessionFor is { } state)
                {
                    SessionViewModel session = new(service, h.Previews, h.TiffReviews, nav);
                    session.Open(state);
                    window.Content = new SessionScreenView { DataContext = session };
                }
            };
            window.Show();
            window.Activate();
            SharedReviewSurfaceTests.Settle(view);
            nint handle = new WindowInteropHelper(window).Handle;
            var dispatcher = window.Dispatcher;
            Save(window, Path.Combine(evidence, phase + "-home.png"));
            Task drive = Task.Run(() =>
            {
                DesktopAutomation.RegisterClientSideProviders();
                using Process process = Process.GetCurrentProcess();
                AutomationElement root = AutomationElement.FromHandle(handle);
                var list = DesktopAutomation.Element(root, "Home.RecoveryList");
                var row = DesktopAutomation.Row(list, "Home.Recovery", "PF_RECOVERY_LIVE_" + phase);
                string action = phase == "A" ? "Restart" : phase == "B" ? "ManualResult" : "Abandon";
                var button = DesktopAutomation.Element(row, "Home.Recovery." + action);
                button.Current.IsEnabled.ShouldBeTrue();
                if (phase == "A")
                {
                    var route = DesktopAutomation.TabTo(process, root, "Home.Recovery.Restart");
                    log.Add($"A: real Tab route: {DesktopAutomation.Describe(route)}; Space activates Restart.");
                    DesktopAutomation.PressSpace();
                }
                else if (phase == "B")
                {
                    var route = DesktopAutomation.TabTo(process, root, "Home.Recovery.ManualResult");
                    log.Add($"B: real Tab route: {DesktopAutomation.Describe(route)}; focused ManualResult also supports UIA Invoke.");
                    var existing = DesktopAutomation.OwnedDialogs(process, handle);
                    DesktopAutomation.Invoke(button);
                    var dialog = DesktopAutomation.FileDialogOpenedBy(process, existing, handle);
                    log.Add($"B: real common dialog '{dialog.Current.Name}', class={dialog.Current.ClassName}; process and exact WPF owner verified; ValuePattern + InvokePattern choose {manual}.");
                    DesktopAutomation.Choose(dialog, manual);
                    DesktopAutomation.Wait("owned dialog closes", () => DesktopAutomation.OwnedDialogs(process, handle).Count == 0);
                }
                else
                {
                    var route = DesktopAutomation.TabTo(process, root, "Home.Recovery.Abandon");
                    log.Add($"C: real Tab route: {DesktopAutomation.Describe(route)}; Space activates Abandon.");
                    DesktopAutomation.PressSpace();
                }

                DesktopAutomation.Wait("recovery action commits and card disappears", () => dispatcher.Invoke(() => !model.IsBusy && model.RecoverySessions.Count == 0));
                if (phase != "C")
                {
                    var session = DesktopAutomation.Element(root, "Screen.Session");
                    session.Current.Name.ShouldBe("PF_RECOVERY_LIVE_" + phase);
                    DesktopAutomation.Element(session, phase == "B" ? "Session.Approve" : "Session.RunStep");
                    log.Add($"{phase}: real Session screen reached, {(phase == "B" ? "Approve available for ReviewRequired" : "Run available but NOT invoked")}.");
                }
                dispatcher.Invoke(() =>
                {
                    var content = (DependencyObject)window.Content;
                    var first = SharedReviewSurfaceTests.Descendants<Button>(content).First(b => b.IsVisible && b.IsEnabled);
                    first.Focus().ShouldBeTrue();
                    first.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)).ShouldBeTrue();
                    Save(window, Path.Combine(evidence, phase + "-after.png"));
                });
                log.Add($"{phase}: recovery entry removed immediately; keyboard traversal remains available after navigation/action.");
            });
            DispatcherFrame frame = new();
            _ = drive.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)), TaskScheduler.Default);
            try { Dispatcher.PushFrame(frame); drive.GetAwaiter().GetResult(); }
            finally { window.Close(); }
        });

        // New repository connections and a fresh service; UI state is not the source of these assertions.
        SessionAggregate after = await Load(h, id);
        after.Attempts.Single(a => a.Status == AttemptStatus.Interrupted).ShouldBe(before.Attempts.Single(a => a.Status == AttemptStatus.Interrupted));
        File.ReadAllBytes(source).ShouldBe(sourceBytes);
        File.ReadAllBytes(h.FileWorkspace.ResolveAbsolute(upstream.File)).ShouldBe(snapshot);
        (await h.Repository.GetAutomationLockAsync(default)).Value.IsHeld.ShouldBeFalse();
        if (phase == "A")
        {
            after.ToSnapshot().CurrentStep!.State.ShouldBe(StepState.Waiting);
            after.Attempts.ShouldBe(before.Attempts);
        }
        else if (phase == "B")
        {
            Revision imported = after.Revisions.Single(r => r.Operation == OperationKind.ManualResultImport);
            after.Attempts.Single(a => a.OutputRevisionId == imported.Id).Operation.ShouldBe(OperationKind.ManualResultImport);
            after.Attempts.Single(a => a.OutputRevisionId == imported.Id).AdapterId.ShouldBe("manual-result-import-v1");
            after.ToSnapshot().CurrentStep!.State.ShouldBe(StepState.ReviewRequired);
            File.ReadAllBytes(manual).ShouldBe(manualBytes);
            File.ReadAllBytes(h.FileWorkspace.ResolveAbsolute(imported.File)).ShouldBe(manualBytes);
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(h.FileWorkspace.ResolveAbsolute(imported.File))))
                .ShouldBe(imported.Sha256.Value, StringCompareShould.IgnoreCase);
            var reread = (await h.CreateService().LoadAsync(id, default)).Value;
            reread.CurrentArtefact!.RevisionId.ShouldBe(imported.Id);
            reread.CurrentArtefact.Sha256.ShouldBe(imported.Sha256);
            log.Add($"B: persisted ReviewRequired; managed path={imported.File.RelativePath}; Revision={imported.Id}; SHA-256={imported.Sha256}; operation=ManualResultImport; source manual bytes unchanged.");
        }
        else
        {
            after.Session.State.ShouldBe(SessionState.Abandoned);
            after.Attempts.ShouldBe(before.Attempts);
        }
        (await h.CreateRecoveryService(new FakeProcessLiveness(ProcessLiveness.Dead)).RecoverAsync(default)).IsSuccess.ShouldBeTrue();
        (await h.CreateService().ListRecoveryAsync(default)).Value.ShouldBeEmpty();
        var restarted = await Load(h, id);
        restarted.Revisions.ShouldBe(after.Revisions);
        restarted.Attempts.ShouldBe(after.Attempts);
        log.Add($"{phase}: PASS independent SQLite/filesystem readback and startup rerun; original Interrupted immutable; source/InputSnapshot unchanged; no automation lock held; no unresolved card after restart.");
    }

    private static void Save(Window window, string path)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(window);
        PngBitmapEncoder encoder = new(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
}
