using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Tests.Integration.Ui;
using PrintFlow.Workflow.Services;
using Xunit.Abstractions;

namespace PrintFlow.Tests.Smoke;

/// <summary>
/// Opt-in real-window proof of Recent Processing record management (SCRUM-11117; Jira 11602).
/// </summary>
/// <remarks>
/// Synthetic and deterministic throughout: real SQLite, real FileWorkspace, the real session
/// service and the real Home view, with a Photoshop adapter that writes an accepted production
/// TIFF rather than starting Photoshop. Nothing here launches Meitu or Photoshop, and no
/// coordinate is used — the row is found through the Windows UI Automation client by the name the
/// operator gave the job, and the action is invoked through <c>InvokePattern</c>.
/// <para>
/// It is opt-in for the reason every live smoke here is: it needs a real desktop with a visible
/// window. The same claims are proved deterministically and unconditionally by
/// <c>HomeRecentAccessibilityTests</c> using in-process UI Automation peers; this adds the
/// end-to-end version an operator would actually see.
/// </para>
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class RecentRecordManagementLiveSmoke(ITestOutputHelper output)
{
    [Fact]
    public async Task Live_removing_one_finished_record_leaves_every_file_and_every_other_record()
    {
        string? evidence = Environment.GetEnvironmentVariable("PRINTFLOW_RECENT_RECORD_LIVE");
        if (string.IsNullOrWhiteSpace(evidence)) return;
        Directory.CreateDirectory(evidence);

        string culture = Environment.GetEnvironmentVariable("PRINTFLOW_RECENT_RECORD_CULTURE") ?? "en-US";
        List<string> log =
        [
            "Real WPF synthetic test window + Windows UIA client; real SQLite/FileWorkspace/SessionService. " +
            "No external application automation, no coordinates. Operator language: " + culture,
        ];

        try
        {
            await RunAsync(evidence, culture, log);
        }
        finally
        {
            await File.WriteAllLinesAsync(Path.Combine(evidence, $"recent-record-transcript-{culture}.txt"), log);
            foreach (string line in log) output.WriteLine(line);
        }
    }

    private static async Task RunAsync(string evidence, string culture, List<string> log)
    {
        using HomeScreenHarness harness = TiffFinalReviewFixture.Harness(out _);
        harness.Inner.Database.RetainForInspection = true;
        harness.Inner.Workspace.RetainForInspection = true;

        // One finished job carrying a real approved production TIFF.
        TiffFinalReviewFixture.Review finished =
            await TiffFinalReviewFixture.ReviewRequiredAsync(harness, "PF_RECENT_LIVE_FINISHED.png");
        await finished.Screen.ApproveCommand.ExecuteAsync(null);
        await finished.Screen.CompleteCommand.ExecuteAsync(null);
        finished.Screen.Notice.ShouldBeNull();

        // One job still in progress, and one unresolved interruption.
        harness.FilePicker.Path = harness.WriteSourceFile("PF_RECENT_LIVE_INPROGRESS.png");
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);
        SessionId inProgress = harness.Navigation.WorkflowSelectionFor!.Id;
        SessionId interrupted = await RecoverySurfaceTests.Seed(harness.Inner, "PF_RECENT_LIVE_INTERRUPTED");

        SessionAggregate before = await finished.ReloadAsync();
        Dictionary<string, byte[]> files = [];
        foreach (Revision revision in before.Revisions) Capture(revision.File);
        foreach (PrintOutput output in before.Outputs) Capture(output.File);
        string customerSource = before.Snapshot!.OriginalSourcePath;
        byte[] customerBytes = File.ReadAllBytes(customerSource);

        log.Add($"finished={finished.Id}; inProgress={inProgress}; interrupted={interrupted}");
        log.Add($"SQLite={harness.Inner.Database.Path}; workspace={harness.Inner.Workspace.Root}");
        log.Add($"protected files captured before the action: {files.Count} (+ the customer source {customerSource})");

        WpfRendering.OnStaThread(() =>
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);

            HomeViewModel model = harness.Home;
            model.RefreshCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            model.ThumbnailsLoaded.GetAwaiter().GetResult();

            HomeView view = new() { DataContext = model };
            Window window = new()
            {
                Title = "PrintFlow synthetic Recent Processing proof",
                Content = view,
                Width = 1150,
                Height = 840,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
            };

            window.Show();
            window.Activate();
            SharedReviewSurfaceTests.Settle(view);

            nint handle = new WindowInteropHelper(window).Handle;
            Dispatcher dispatcher = window.Dispatcher;
            Save(window, Path.Combine(evidence, $"recent-before-{culture}.png"));

            Task drive = Task.Run(() =>
            {
                DesktopAutomation.RegisterClientSideProviders();
                using Process process = Process.GetCurrentProcess();
                AutomationElement root = AutomationElement.FromHandle(handle);

                AutomationElement list = DesktopAutomation.Element(root, "Home.RecentSessionList");
                AutomationElement row = DesktopAutomation.Row(list, "Home.RecentSession", "PF_RECENT_LIVE_FINISHED");
                DesktopAutomation.Element(row, "Home.RecentSessionThumbnail");
                log.Add("Row located by the operator's own output name; its thumbnail element is present.");

                AutomationElement remove = DesktopAutomation.Element(row, "Home.RemoveSessionRecord");
                remove.Current.IsEnabled.ShouldBeTrue();
                remove.Current.Name.ShouldBe(culture == "zh-CN" ? "从列表移除" : "Remove from list");
                log.Add("Remove action accessible name in " + culture + ": " + remove.Current.Name);

                // Keyboard traversal is proved deterministically in-process by
                // HomeRecentAccessibilityTests rather than by synthesising key presses here: a
                // Tab route driven at a shared desktop reports whatever window happens to hold
                // the keyboard, which says nothing about this screen's tab order.
                DesktopAutomation.Invoke(remove);
                log.Add("InvokePattern.Invoke on Home.RemoveSessionRecord — no coordinate used.");

                DesktopAutomation.Wait("the record leaves the list", () => dispatcher.Invoke(
                    () => !model.IsBusy && model.RecentSessions.All(r => r.Id != finished.Id)));

                dispatcher.Invoke(() => Save(window, Path.Combine(evidence, $"recent-after-{culture}.png")));
            });

            DispatcherFrame frame = new();
            _ = drive.ContinueWith(
                _ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)), TaskScheduler.Default);
            try
            {
                Dispatcher.PushFrame(frame);
                drive.GetAwaiter().GetResult();
            }
            finally
            {
                window.Close();
            }
        });

        // Independent of the screen: fresh services and a fresh read of the database and disk.
        IReadOnlyList<SessionListItem> listed =
            (await harness.Sessions.ListRecentAsync(CancellationToken.None)).Value;
        listed.ShouldNotContain(item => item.Id == finished.Id);
        listed.Select(item => item.Id).ShouldContain(inProgress);

        HomeViewModel restarted = harness.RestartHome(new RecordingNavigation());
        await restarted.RefreshCommand.ExecuteAsync(null);
        restarted.RecentSessions.ShouldNotContain(row => row.Id == finished.Id);
        restarted.RecentSessions.Select(row => row.Id).ShouldContain(inProgress);
        restarted.RecoverySessions.Select(entry => entry.Id).ShouldContain(interrupted);
        log.Add("After a restart over the same database: the record is still gone, the other two are still there.");

        SessionAggregate after = await finished.ReloadAsync();
        after.Session.State.ShouldBe(SessionState.Completed);
        after.Revisions.ShouldBe(before.Revisions);
        after.Reviews.ShouldBe(before.Reviews);
        after.Outputs.ShouldBe(before.Outputs);
        after.Attempts.ShouldBe(before.Attempts);
        after.Snapshot.ShouldBe(before.Snapshot);

        foreach ((string path, byte[] bytes) in files)
        {
            File.Exists(path).ShouldBeTrue(path);
            File.ReadAllBytes(path).ShouldBe(bytes, path);
        }

        File.ReadAllBytes(customerSource).ShouldBe(customerBytes);
        log.Add($"Every protected file is byte-identical; the persisted record is unchanged and still Completed.");

        void Capture(WorkspaceFileRef file)
        {
            string path = harness.Inner.FileWorkspace.ResolveAbsolute(file);
            if (File.Exists(path)) files[path] = File.ReadAllBytes(path);
        }
    }

    private static void Save(Window window, string path)
    {
        window.UpdateLayout();
        RenderTargetBitmap bitmap = new(
            (int)window.ActualWidth, (int)window.ActualHeight, 96, 96,
            System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(window);
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream file = File.Create(path);
        encoder.Save(file);
    }
}
