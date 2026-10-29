using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PrintFlow.App.Localisation;
using PrintFlow.App.Startup;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Infrastructure.Gate;
using PrintFlow.Infrastructure.Diagnostics;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class RecoveryUtilityRolloutTests
{
    public static TheoryData<string, int, int> Viewports() => Cases("utility");
    public static TheoryData<string, int, int> RecoveryCases() => Cases("failed", "prepare", "waiting", "returned");

    [Theory]
    [InlineData("en-US", 700)]
    [InlineData("zh-CN", 700)]
    [InlineData("en-US", 1000)]
    [InlineData("zh-CN", 1000)]
    public async Task Live_check_actions_wrap_without_covering_back_or_losing_cancel(string language, int width)
    {
        using OperatorCultureScope culture = new(language);
        using WorkstationVerificationFixture fixture = new();
        PausedDiagnostics diagnostics = new(new VerifiedEnvironmentGate(fixture.CreateVerifier()));
        EnvironmentReadinessViewModel model = new(diagnostics, new RecordingNavigation(), new ReadinessObservationAccessor());
        await model.OpenAsync(default);
        Task run = model.RunLiveChecksCommand.ExecuteAsync(null);
        await diagnostics.Started.Task;
        try
        {
            model.IsRunningLiveChecks.ShouldBeTrue();
            Size viewport = new(width, 700);
            Check(() => new EnvironmentReadinessView { DataContext = model }, viewport, tree =>
            {
                var back = Find<Button>(tree, "Environment.BackToHome");
                var start = Find<Button>(tree, "Environment.RunLiveChecks");
                foreach (string id in new[] { "Environment.BackToHome", "Environment.RunLiveChecks", "Environment.CancelLiveChecks", "Environment.Refresh" })
                    ActionFits(Find<Button>(tree, id), tree.Root, viewport);
                Bounds(back, tree.Root).Right.ShouldBeLessThanOrEqualTo(Bounds(start, tree.Root).Left);
                Find<Button>(tree, "Environment.CancelLiveChecks").IsEnabled.ShouldBeTrue();
            });
            Capture(() => new EnvironmentReadinessView { DataContext = model }, viewport, language, "live-check");
        }
        finally
        {
            model.CancelLiveChecksCommand.Execute(null);
            await run;
        }
        model.IsRunningLiveChecks.ShouldBeFalse();
    }

    // Catches an unreachable footer, shrunken primary controls or an editable production fact.
    [Theory]
    [MemberData(nameof(Viewports))]
    public async Task Utility_actions_remain_readable_reachable_and_production_facts_readonly(string language, int width, int height)
    {
        using SettingsScreenHarness settings = new();
        var preferences = await settings.OpenAsync();
        using OperatorCultureScope culture = new(language);
        Size viewport = new(width, height);
        Check(() => new SettingsView { DataContext = preferences }, viewport, tree =>
        {
            var apply = Find<Button>(tree, "Settings.Apply");
            var back = Find<Button>(tree, "Settings.Back");
            ActionFits(apply, tree.Root, viewport);
            ActionFits(back, tree.Root, viewport);
            Bounds(apply, tree.Root).Left.ShouldBeGreaterThan(Bounds(back, tree.Root).Right);
            foreach (string id in new[] { "Settings.OutputRoot", "Settings.ProductionDpi", "Settings.Preset", "Settings.PhotoshopColourSettings" })
                Find<TextBox>(tree, id).IsReadOnly.ShouldBeTrue();
            Find<TextBox>(tree, "Settings.TrimMargin").ActualHeight.ShouldBeGreaterThanOrEqualTo(42);
            tree.Root.TryFindResource("DefaultButtonStyle").ShouldNotBeNull();
        });
        Capture(() => new SettingsView { DataContext = preferences }, viewport, language, "settings");
        Capture(() => new SettingsView { DataContext = preferences }, viewport, language, "settings-advanced", tree =>
        {
            Find<Expander>(tree, "Settings.Advanced").IsExpanded = true;
            tree.Root.Dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
            tree.Root.UpdateLayout();
            tree.OfType<ScrollViewer>().First().ScrollToBottom();
        });

        using WorkstationVerificationFixture fixture = new();
        fixture.Facts.Display = fixture.Facts.Display with { ActiveDisplayCount = 2 };
        EnvironmentReadinessViewModel environment = new(new VerifiedEnvironmentGate(fixture.CreateVerifier()),
            new RecordingNavigation(), new ReadinessObservationAccessor());
        await environment.OpenAsync(default);
        environment.IsReady.ShouldBeFalse();
        Check(() => new EnvironmentReadinessView { DataContext = environment }, viewport, tree =>
        {
            var run = Find<Button>(tree, "Environment.RunLiveChecks");
            var back = Find<Button>(tree, "Environment.BackToHome");
            ActionFits(run, tree.Root, viewport);
            ActionFits(back, tree.Root, viewport);
            Bounds(run, tree.Root).Left.ShouldBeGreaterThan(Bounds(back, tree.Root).Right);
            Bounds(run, tree.Root).Bottom.ShouldBeGreaterThan(viewport.Height - 90);
            Find<TextBlock>(tree, "Environment.Status").Text.ShouldBe(environment.StatusText);
            tree.Root.TryFindResource("DefaultButtonStyle").ShouldNotBeNull();
        });
        Capture(() => new EnvironmentReadinessView { DataContext = environment }, viewport, language, "environment-blocked");

        using SessionServiceHarness sessions = new();
        var id = await FailureGuidanceUiTests.FailedEnhancementAsync(sessions);
        var service = sessions.CreateService();
        var failed = (await service.LoadAsync(id, default)).Value;
        string evidenceRoot = Path.Combine(sessions.Workspace.Root, "Evidence");
        Directory.CreateDirectory(evidenceRoot);
        LocalDiagnosticPackageEvidence evidence = new(evidenceRoot);
        DiagnosticPackageService packages = new(service, new VerifiedEnvironmentGate(fixture.CreateVerifier()), evidence,
            new DiagnosticPackageArchiveWriter(evidence, Path.Combine(sessions.Workspace.Root, "package-stage")),
            new DiagnosticPackageApplicationInfo("PrintFlow Studio", "u08-layout"),
            new DiagnosticPackageStorageLocations(sessions.Database.Factory.DatabasePath, evidenceRoot), sessions.Clock);
        StubDiagnosticPackageDestinationPicker picker = new();
        ErrorDetailsViewModel details = new(service, new RecordingNavigation(), new LocalisationService(sessions.Settings), packages, picker);
        await details.OpenAsync(id, failed.CurrentFailureAttemptId!.Value, default);
        string before = JsonSerializer.Serialize((await sessions.Repository.LoadAsync(id, default)).Value);
        Check(() => new PrintFlow.App.Views.ErrorDetailsView { DataContext = details }, viewport, tree =>
        {
            foreach (var action in tree.OfType<Button>().Where(GuidedSessionProjectionTests.Shown))
                ActionFits(action, tree.Root, viewport);
            Find<TextBox>(tree, "ErrorDetails.Description").Text.ShouldNotBeNullOrWhiteSpace();
            Find<TextBox>(tree, "ErrorDetails.Code").IsReadOnly.ShouldBeTrue();
            var technical = Find<Expander>(tree, "ErrorDetails.TechnicalDisclosure");
            technical.IsExpanded.ShouldBeFalse();
            technical.IsExpanded = true;
            tree.Root.UpdateLayout();
            Find<TextBox>(tree, "ErrorDetails.TechnicalDetail").IsReadOnly.ShouldBeTrue();
            tree.Root.TryFindResource("DefaultButtonStyle").ShouldNotBeNull();
        });
        JsonSerializer.Serialize((await sessions.Repository.LoadAsync(id, default)).Value).ShouldBe(before);
        Capture(() => new PrintFlow.App.Views.ErrorDetailsView { DataContext = details }, viewport, language, "error-details");
        await details.OpenDiagnosticPackageCommand.ExecuteAsync(null);
        details.IsPackagePreview.ShouldBeTrue();
        Check(() => new PrintFlow.App.Views.ErrorDetailsView { DataContext = details }, viewport, tree =>
        {
            ActionFits(Find<Button>(tree, "DiagnosticPackage.Save"), tree.Root, viewport);
            ActionFits(Find<Button>(tree, "DiagnosticPackage.Back"), tree.Root, viewport);
            Find<TextBlock>(tree, "DiagnosticPackage.LocalOnlyNotice").Text.ShouldNotBeNullOrWhiteSpace();
        });
        Capture(() => new PrintFlow.App.Views.ErrorDetailsView { DataContext = details }, viewport, language, "diagnostic-package");
        details.BackFromPackageCommand.Execute(null);
        details.IsPackagePreview.ShouldBeFalse();
        picker.CallCount.ShouldBe(0);
        JsonSerializer.Serialize((await sessions.Repository.LoadAsync(id, default)).Value).ShouldBe(before);
    }

    // Catches small recovery inputs or a partial theme scope while preserving the real review flow.
    [Theory]
    [MemberData(nameof(RecoveryCases))]
    public async Task Recovery_and_colleague_panels_keep_real_state_and_usable_controls(string languageAndStage, int width, int height)
    {
        string[] parts = languageAndStage.Split('|');
        string language = parts[0], stage = parts[1];
        using OperatorCultureScope culture = new(language);
        using SessionServiceHarness h = new();
        SessionViewModel model;
        if (stage == "failed")
        {
            var id = await FailureGuidanceUiTests.FailedEnhancementAsync(h);
            model = new(h.CreateService(), h.Previews, h.TiffReviews, new RecordingNavigation());
            model.Open((await h.CreateService().LoadAsync(id, default)).Value);
            await model.PreviewsLoaded;
        }
        else
        {
            var opened = await ReviewGuidanceSetup.OpenAtAsync(h, "background");
            model = opened.Screen;
            model.BeginAskColleagueCommand.Execute(null);
            if (stage is "waiting" or "returned") await model.PrepareCorrectionFilesCommand.ExecuteAsync(null);
            if (stage == "returned")
            {
                opened.Picker.Next = CorrectionFixtures.CorrectedPng(h);
                await model.ImportCorrectedImageCommand.ExecuteAsync(null);
                model.IsReviewRequired.ShouldBeTrue();
                model.CanApprove.ShouldBeTrue();
            }
        }
        string before = JsonSerializer.Serialize((await h.Repository.LoadAsync(model.Id, default)).Value);
        Size viewport = new(width, height);
        Check(() => new SessionScreenView { DataContext = model }, viewport, tree =>
        {
            if (stage == "prepare")
            {
                var note = Find<TextBox>(tree, "Session.Correction.Note");
                note.MinHeight.ShouldBeGreaterThanOrEqualTo(42);
                note.FontSize.ShouldBeGreaterThanOrEqualTo(16);
                ActionFits(Find<Button>(tree, "Session.Correction.Prepare"), tree.Root, viewport);
            }
            if (stage is "failed" or "prepare" or "waiting")
            {
                string panelId = stage switch { "failed" => "Session.RecoveryGuidance", "prepare" => "Session.Correction.AskPanel", _ => "Session.Correction.Panel" };
                var card = tree.OfType<Wpf.Ui.Controls.Card>().Single(c => AutomationProperties.GetAutomationId(c) == panelId);
                GuidedSessionProjectionTests.Shown(card).ShouldBeTrue();
                card.FontSize.ShouldBeGreaterThanOrEqualTo(16);
            }
            if (stage == "returned") ActionFits(Find<Button>(tree, "Session.Approve"), tree.Root, viewport);
        });
        JsonSerializer.Serialize((await h.Repository.LoadAsync(model.Id, default)).Value).ShouldBe(before);
        Capture(() => new SessionScreenView { DataContext = model }, viewport, language, stage);
    }

    [Theory]
    [MemberData(nameof(Viewports))]
    public async Task Recent_recovery_and_abandon_confirmation_keep_exact_task_and_cancel_without_writes(string language, int width, int height)
    {
        using OperatorCultureScope culture = new(language);
        using SessionServiceHarness h = new();
        var empty = RecoverySurfaceTests.Home(h, h.CreateService());
        await empty.RefreshCommand.ExecuteAsync(null);
        Capture(() => new HomeView { DataContext = empty }, new(width, height), language, "home-empty");
        var id = await RecoverySurfaceTests.Seed(h, "u08-recovery");
        // A captured model's default CollectionView belongs to the completed render STA.
        // Use the independently loaded recovery model, as the application does on navigation.
        var home = RecoverySurfaceTests.Home(h, h.CreateService());
        await home.RefreshCommand.ExecuteAsync(null);
        string before = JsonSerializer.Serialize((await h.Repository.LoadAsync(id, default)).Value);
        Size viewport = new(width, height);
        Check(() => new HomeView { DataContext = home }, viewport, tree =>
        {
            var card = tree.OfType<Wpf.Ui.Controls.Card>().Single(c => AutomationProperties.GetAutomationId(c) == "Home.RecoveryCard");
            card.FontSize.ShouldBeGreaterThanOrEqualTo(16);
            Find<ItemsControl>(tree, "Home.RecoveryList").Items.Count.ShouldBe(1);
        });
        Capture(() => new HomeView { DataContext = home }, viewport, language, "home-recovery");
        await home.AbandonRecoveryCommand.ExecuteAsync(home.RecoverySessions.Single());
        home.PendingAbandon!.Id.ShouldBe(id);
        Check(() => new HomeView { DataContext = home }, viewport, tree =>
        {
            var confirm = Find<ReviewApprovalButton>(tree, "Home.AbandonConfirmation.Confirm");
            confirm.TargetIdentity.ShouldBe(home.PendingAbandon.GestureTarget);
            confirm.IsDefault.ShouldBeFalse();
            ActionFits(confirm, tree.Root, viewport);
            var keep = Find<Button>(tree, "Home.AbandonConfirmation.Keep");
            keep.IsCancel.ShouldBeTrue();
            ActionFits(keep, tree.Root, viewport);
        });
        Capture(() => new HomeView { DataContext = home }, viewport, language, "home-abandon-confirmation");
        home.KeepJobCommand.Execute(null);
        JsonSerializer.Serialize((await h.Repository.LoadAsync(id, default)).Value).ShouldBe(before);
    }

    [Theory]
    [MemberData(nameof(Viewports))]
    public async Task Return_confirmation_keeps_readable_neutral_heading_and_cancel_preserves_task(string language, int width, int height)
    {
        using OperatorCultureScope culture = new(language);
        using SessionServiceHarness h = new();
        var model = (await ReviewGuidanceSetup.OpenAtAsync(h, "background")).Screen;
        string before = JsonSerializer.Serialize((await h.Repository.LoadAsync(model.Id, default)).Value);
        model.SelectedReturnTarget = model.ReturnTargets.First();
        model.BeginReturnCommand.Execute(null);
        model.IsConfirmingReturn.ShouldBeTrue();
        Size viewport = new(width, height);
        Check(() => new SessionScreenView { DataContext = model }, viewport, tree =>
        {
            var panel = tree.OfType<Wpf.Ui.Controls.Card>().Single(c => AutomationProperties.GetAutomationId(c) == "Session.ReturnConfirmation");
            GuidedSessionProjectionTests.Shown(panel).ShouldBeTrue();
            var heading = tree.OfType<TextBlock>().Single(t => t.Name == "ReturnHeadingText");
            heading.Focusable.ShouldBeTrue();
            foreach (string action in new[] { "Session.ConfirmReturn", "Session.CancelReturn" })
            {
                var button = Find<Button>(tree, action);
                button.IsDefault.ShouldBeFalse();
                ActionFits(button, tree.Root, viewport);
            }
        });
        Capture(() => new SessionScreenView { DataContext = model }, viewport, language, "return-confirmation");
        model.CancelReturnCommand.Execute(null);
        JsonSerializer.Serialize((await h.Repository.LoadAsync(model.Id, default)).Value).ShouldBe(before);
    }

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public void Notice_details_are_expandable_copyable_and_have_a_full_sized_keyboard_target(string language)
    {
        using OperatorCultureScope culture = new(language);
        Check(() => new NoticeErrorDetails { Code = "OutputMissing", DataContext = new NoticeModel() }, new Size(420, 220), tree =>
        {
            var disclosure = Find<Expander>(tree, "NoticeErrorDetails");
            disclosure.MinHeight.ShouldBeGreaterThanOrEqualTo(42);
            disclosure.IsExpanded.ShouldBeFalse();
            disclosure.IsExpanded = true;
            tree.Root.Dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
            tree.Root.UpdateLayout();
            var code = Find<TextBox>(tree, "NoticeErrorDetails.Code");
            code.IsReadOnly.ShouldBeTrue();
            code.Text.ShouldBe("OutputMissing");
            Bounds(code, tree.Root).Right.ShouldBeLessThanOrEqualTo(420);
        });
    }

    private static TheoryData<string, int, int> Cases(params string[] stages)
    {
        TheoryData<string, int, int> data = new();
        foreach (string language in new[] { "zh-CN", "en-US" })
            foreach (var size in new[] { (1000, 700), (1280, 720), (1920, 1080) })
                foreach (string stage in stages)
                    data.Add(stage == "utility" ? language : language + "|" + stage, size.Item1, size.Item2);
        return data;
    }

    private static T Find<T>(RenderedTree tree, string id) where T : DependencyObject =>
        tree.OfType<T>().Single(e => AutomationProperties.GetAutomationId(e) == id);

    private static Rect Bounds(FrameworkElement element, Visual root) => element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));

    private static void ActionFits(Button button, FrameworkElement root, Size viewport)
    {
        button.ActualHeight.ShouldBeGreaterThanOrEqualTo(42, AutomationProperties.GetAutomationId(button));
        button.FontSize.ShouldBeGreaterThanOrEqualTo(16);
        var text = new FormattedText(button.Content?.ToString() ?? "", CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight, new Typeface(button.FontFamily, button.FontStyle, button.FontWeight, button.FontStretch), button.FontSize, Brushes.Black, 1);
        (text.WidthIncludingTrailingWhitespace + button.Padding.Left + button.Padding.Right).ShouldBeLessThanOrEqualTo(button.ActualWidth + 1);
        Rect bounds = Bounds(button, root);
        bounds.Left.ShouldBeGreaterThanOrEqualTo(-0.5);
        bounds.Top.ShouldBeGreaterThanOrEqualTo(-0.5);
        bounds.Right.ShouldBeLessThanOrEqualTo(viewport.Width + 0.5);
        bounds.Bottom.ShouldBeLessThanOrEqualTo(viewport.Height + 0.5);
    }

    private static void Check(Func<UserControl> create, Size size, Action<RenderedTree> inspect) =>
        WpfRendering.RenderExpectingNoBindingErrors(create, size, tree =>
        {
            try { inspect(tree); return 0; }
            finally { tree.Root.DataContext = null; tree.Root.UpdateLayout(); }
        });

    private static void Capture(Func<UserControl> create, Size size, string language, string stage, Action<RenderedTree>? prepare = null)
    {
        if (Environment.GetEnvironmentVariable("PF_U08_CAPTURE_DIR") is { Length: > 0 } directory)
            WpfRendering.CapturePng(create, size, Path.Combine(directory, $"{stage}-{language}-{size.Width}x{size.Height}.png"), prepare);
    }

    private sealed class NoticeModel
    {
        public string NoticeErrorDetailsLabel => "Error details / 错误详情";
        public string NoticeErrorCodeLabel => "Code / 错误代码";
        public string NoticeErrorCodeHint => "Share this code when asking for help. / 请提供此代码。";
    }

    // The real gate supplies the report; only the slow external check is paused until cancellation.
    private sealed class PausedDiagnostics(IEnvironmentDiagnostics inner) : IEnvironmentDiagnostics
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public EnvironmentReadinessReport Read() => inner.Read();
        public async Task<EnvironmentReadinessReport> RunLiveChecksAsync(CancellationToken cancellationToken)
        {
            Started.SetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return Read();
        }
    }
}
