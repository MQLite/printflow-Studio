using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PrintFlow.App.Startup;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Infrastructure.Delivery;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class GuidedCompletionVisualEvidenceTests
{
    [Theory]
    [InlineData(1000, 700)]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    public async Task Chinese_completion_recovery_and_utilities_keep_actions_reachable(int width, int height)
    {
        using OperatorCultureScope culture = new("zh-CN");
        Size viewport = new(width, height);
        using (SessionServiceHarness h = new())
        {
            var workflow = FinalSaveFixtures.TiffService(h);
            var review = await FinalSaveFixtures.TiffAtFinalReviewAsync(h, workflow);
            string folder = FinalSaveFixtures.NewFolder("e5-visual-save");
            try
            {
                var delivery = FinalSaveFixtures.Delivery(h);
                var model = await GuidedCompletionSaveTests.Open(h, workflow, delivery, new GuidedCompletionSaveTests.Picker(folder), review);
                await model.ApproveCommand.ExecuteAsync(null);
                await model.FinalSaveFactsLoaded;
                model.ChangeLocationCommand.Execute(null);
                var saveSnapshot = await GuidedCompletionSaveTests.Open(h, workflow, delivery, new GuidedCompletionSaveTests.Picker(folder),
                    (await workflow.LoadAsync(model.Id, default)).Value);
                saveSnapshot.ChangeLocationCommand.Execute(null);
                CaptureSession(saveSnapshot, viewport, "save", ["Session.FinalSave.SaveApproved", "Session.FinalSave.ChangeLocation"],
                    ["Session.FinalSave.FileName", "Session.FinalSave.Folder"]);
                await model.SaveApprovedCommand.ExecuteAsync(null);
                await model.FinalSaveFactsLoaded;
                await model.CompleteCommand.ExecuteAsync(null);
                await model.FinalSaveFactsLoaded;
                CaptureSession(model, viewport, "completed", []);
            }
            finally { FinalSaveFixtures.Remove(folder); }
        }
        using (SessionServiceHarness h = new())
        {
            var workflow = CorrectionFixtures.Service(h);
            var review = await CorrectionFixtures.AtBackgroundRemovalReviewAsync(h, workflow);
            var picker = new ColleagueCorrectionUiTests.RecordingPicker();
            var model = new SessionViewModel(workflow, h.Previews, h.TiffReviews, new RecordingNavigation(), filePicker: picker);
            model.Open(review); await model.PreviewsLoaded;
            model.BeginAskColleagueCommand.Execute(null);
            if (width == 1000)
            {
                var prepare = new SessionViewModel(workflow, h.Previews, h.TiffReviews, new RecordingNavigation(), filePicker: picker);
                prepare.Open(review); await prepare.PreviewsLoaded; prepare.BeginAskColleagueCommand.Execute(null);
                CaptureSession(prepare, viewport, "correction-prepare", ["Session.Correction.Prepare"]);
            }
            await model.PrepareCorrectionFilesCommand.ExecuteAsync(null);
            await model.PreviewsLoaded;
            var waiting = new SessionViewModel(workflow, h.Previews, h.TiffReviews, new RecordingNavigation(), filePicker: picker);
            waiting.Open((await workflow.LoadAsync(model.Id, default)).Value); await waiting.PreviewsLoaded;
            CaptureSession(waiting, viewport, "correction-waiting", ["Session.Correction.Import"]);
            if (width == 1000)
            {
                picker.Next = CorrectionFixtures.CorrectedPng(h);
                await model.ImportCorrectedImageCommand.ExecuteAsync(null);
                await model.PreviewsLoaded;
                CaptureSession(model, viewport, "correction-returned", ["Session.Approve", "Session.Reject"]);
            }
        }
        using (HomeScreenHarness h = new())
        {
            var id = await RecoverySurfaceTests.Seed(h.Inner, "visual-recovery");
            var model = h.Session(new RecordingNavigation());
            model.Open((await h.Sessions.LoadAsync(id, default)).Value); await model.PreviewsLoaded;
            CaptureSession(model, viewport, "recovery", []);
        }
        using (HomeScreenHarness h = new())
        {
            var failedId = await FailureGuidanceUiTests.FailedEnhancementAsync(h.Inner);
            var home = RecoverySurfaceTests.Home(h.Inner, h.Sessions);
            await home.RefreshCommand.ExecuteAsync(null);
            await home.ThumbnailsLoaded;
            Capture(() => new HomeView { DataContext = home }, viewport, "recent", []);
            if (width == 1000)
            {
                home = RecoverySurfaceTests.Home(h.Inner, h.Sessions);
                await home.RefreshCommand.ExecuteAsync(null);
                await home.ThumbnailsLoaded;
                await home.AbandonCommand.ExecuteAsync(home.RecentSessions.Single(r => r.Id == failedId));
                Capture(() => new HomeView { DataContext = home }, viewport, "confirmation",
                    ["Home.AbandonConfirmation.Keep", "Home.AbandonConfirmation.Confirm"]);
            }
        }
        using (SettingsScreenHarness h = new())
        {
            var settings = await h.OpenAsync();
            Capture(() => new SettingsView { DataContext = settings }, viewport, "settings", ["Settings.Apply", "Settings.Back"],
                ["Settings.Language", "Settings.TrimMargin", "Settings.LogRetention", "Settings.OutputRoot"]);
        }
        var diagnostics = new GuidedCompletionUtilityTests.FixedDiagnostics(GuidedCompletionUtilityTests.Report("checked"));
        var environment = new EnvironmentReadinessViewModel(diagnostics, new RecordingNavigation(), new ReadinessObservationAccessor());
        await environment.OpenAsync(default);
        Capture(() => new EnvironmentReadinessView { DataContext = environment }, viewport, "readiness",
            ["Environment.RunLiveChecks", "Environment.BackToHome"], ["Environment.RestartRequirement"]);
        diagnostics.LiveCalls.ShouldBe(0);
        if (width != 1000) return;
        foreach (string state in new[] { "blocked", "stale", "unchecked" })
        {
            var d = new GuidedCompletionUtilityTests.FixedDiagnostics(GuidedCompletionUtilityTests.Report(state));
            var e = new EnvironmentReadinessViewModel(d, new RecordingNavigation(), new ReadinessObservationAccessor());
            if (state != "unchecked") await e.OpenAsync(default);
            Capture(() => new EnvironmentReadinessView { DataContext = e }, viewport, "readiness-" + state,
                ["Environment.RunLiveChecks", "Environment.BackToHome"], ["Environment.Observation", "Environment.RestartRequirement"]);
            d.LiveCalls.ShouldBe(0);
        }
        foreach (bool uncertain in new[] { false, true })
        {
            using SessionServiceHarness h = new();
            var workflow = FinalSaveFixtures.TiffService(h);
            var review = await FinalSaveFixtures.TiffAtFinalReviewAsync(h, workflow);
            string folder = FinalSaveFixtures.NewFolder("e5-visual-adverse");
            try
            {
                var delivery = FinalSaveFixtures.Delivery(h, uncertain ? new LostPublicationAckFileSystem(new WindowsDeliveryFileSystem()) : null);
                var model = await GuidedCompletionSaveTests.Open(h, workflow, delivery, new GuidedCompletionSaveTests.Picker(folder), review);
                model.SaveFileNameText = "result.tif"; model.ChangeLocationCommand.Execute(null);
                if (!uncertain) File.WriteAllBytes(Path.Combine(folder, "result.tif"), [1, 2, 3]);
                await model.ConfirmAndSaveCommand.ExecuteAsync(null); await model.FinalSaveFactsLoaded;
                CaptureSession(model, viewport, uncertain ? "save-uncertain" : "save-collision",
                    [uncertain ? "Session.FinalSave.CheckAgain" : "Session.FinalSave.UseSuggestion"]);
            }
            finally { FinalSaveFixtures.Remove(folder); }
        }
        using (SessionServiceHarness h = new())
        {
            var id = await FailureGuidanceUiTests.FailedEnhancementAsync(h);
            var workflow = h.CreateService();
            var view = (await workflow.LoadAsync(id, default)).Value;
            var model = new SessionViewModel(workflow, h.Previews, h.TiffReviews, new RecordingNavigation());
            model.Open(view); await model.PreviewsLoaded;
            CaptureSession(model, viewport, "failure", ["Session.Retry"]);
            var details = new ErrorDetailsViewModel(workflow, new RecordingNavigation(), new PrintFlow.App.Localisation.LocalisationService(h.Settings));
            await details.OpenAsync(id, view.CurrentFailureAttemptId!.Value, default);
            Capture(() => new PrintFlow.App.Views.ErrorDetailsView { DataContext = details }, viewport, "error-details", []);
        }
    }

    private static void CaptureSession(SessionViewModel model, Size viewport, string state, string[] essential, string[]? fields = null) =>
        Capture(() => new SessionScreenView { DataContext = model }, viewport, state, essential, fields);

    private static void Capture(Func<UserControl> create, Size viewport, string state, string[] essential, string[]? fields = null)
    {
        var result = WpfRendering.RenderExpectingNoBindingErrors(create, viewport, tree =>
        {
            try
            {
                Settle(tree.Root);
                var all = GuidedCompletionSaveTests.Descendants(tree.Root).OfType<FrameworkElement>().ToArray();
                return all.Where(e => GuidedSessionProjectionTests.Shown(e) && !string.IsNullOrEmpty(AutomationProperties.GetAutomationId(e)))
                    .Select(e => new
                    {
                        Id = AutomationProperties.GetAutomationId(e), e.IsEnabled,
                        Bounds = Bounds(e, tree.Root),
                        WithinScroll = all.OfType<ScrollViewer>().Where(s => s.IsAncestorOf(e)).All(s =>
                            Bounds(e, s).Top >= -0.5 && Bounds(e, s).Bottom <= s.ViewportHeight + 0.5),
                        Text = e is TextBlock t ? t.Text : e is Button b ? b.Content?.ToString() : null,
                    }).ToArray();
            }
            finally { tree.Root.DataContext = null; tree.Root.UpdateLayout(); }
        });
        foreach (string id in essential.Concat(fields ?? []))
        {
            result.Facts.ShouldContain(e => e.Id == id, state + ": " + id + " must be visible");
            var item = result.Facts.Single(e => e.Id == id);
            item.IsEnabled.ShouldBeTrue(state + ": " + id);
            item.Bounds.Left.ShouldBeGreaterThanOrEqualTo(-0.5, state + ": " + id);
            item.Bounds.Top.ShouldBeGreaterThanOrEqualTo(-0.5, state + ": " + id);
            item.Bounds.Right.ShouldBeLessThanOrEqualTo(viewport.Width + 0.5, state + ": " + id);
            item.Bounds.Bottom.ShouldBeLessThanOrEqualTo(viewport.Height + 0.5, state + ": " + id);
            item.Bounds.Height.ShouldBeGreaterThan(0, state + ": " + id);
            item.WithinScroll.ShouldBeTrue(state + ": " + id);
        }
        string? directory = Environment.GetEnvironmentVariable("PF_11172_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        // Later acceptance passes can reuse this fixture without emitting unrelated screenshots.
        string? onlyStates = Environment.GetEnvironmentVariable("PF_11172_CAPTURE_STATES");
        if (!string.IsNullOrWhiteSpace(onlyStates) && !onlyStates.Split(',').Contains(state)) return;
        Directory.CreateDirectory(directory);
        string stem = $"{state}-{viewport.Width:0}x{viewport.Height:0}";
        string path = Path.Combine(directory, stem + ".png");
        WpfRendering.CapturePng(create, viewport, path, tree => Settle(tree.Root));
        File.WriteAllText(Path.Combine(directory, stem + ".json"), JsonSerializer.Serialize(new
        {
            State = state, Language = "zh-CN", WidthDip = viewport.Width, HeightDip = viewport.Height, Dpi = 96,
            CaptureKind = "Synthetic offscreen WPF RenderTargetBitmap; no App/Window.Show, desktop input or real external application",
            ScreenshotSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
            EssentialActions = essential, EssentialFields = fields ?? [], Measurements = result.Facts,
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static Rect Bounds(FrameworkElement element, Visual root) =>
        element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
    private static void Settle(UserControl root)
    {
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.SystemIdle);
        root.UpdateLayout();
    }
}
