using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using PrintFlow.App.Localisation;
using PrintFlow.App.Navigation;
using PrintFlow.App.Composition;
using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Automation;
using PrintFlow.Infrastructure.Startup;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Settings;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;
using ErrorPage = PrintFlow.App.Views.ErrorDetailsView;

namespace PrintFlow.Tests.Integration.Ui;

/// <summary>SCRUM-11120: rendered controls, exact-attempt navigation, and durable recovery through the real graph.</summary>
[Collection(SqliteCollection.Name)]
public sealed class ErrorDetailsRenderingTests
{
    [Fact]
    public async Task Rendered_failure_entry_and_UIA_retry_preserve_history_and_only_reset_the_step()
    {
        using FailureApplication app = await FailureApplication.CreateAsync(screenshotExists: true);
        SessionViewModel processing = app.Navigation.Current.ShouldBeOfType<SessionViewModel>();
        SessionAggregate failed = await app.ReloadAsync();
        ProcessingAttempt attempt = failed.Attempts.Single(a => a.Status == AttemptStatus.Failed);
        ErrorDetailsView expected = (await app.Sessions.LoadErrorDetailsAsync(
            failed.Session.Id, attempt.Id, CancellationToken.None)).Value;

        WpfRendering.RenderExpectingNoBindingErrors(
            () => new PrintFlow.App.Views.SessionScreenView { DataContext = processing },
            WpfRendering.ReviewViewport,
            tree =>
            {
                Invoke(ById<Button>(tree, "Session.ErrorDetails"));
                PumpUntil(() => app.Navigation.Current is ErrorDetailsViewModel { HasDetails: true, IsBusy: false });
                return true;
            });
        ErrorDetailsViewModel details = app.Navigation.Current.ShouldBeOfType<ErrorDetailsViewModel>();

        string? evidenceDirectory = Environment.GetEnvironmentVariable("PRINTFLOW_ERROR_DETAILS_EVIDENCE");
        if (!string.IsNullOrWhiteSpace(evidenceDirectory))
        {
            WpfRendering.CapturePng(() => new ErrorPage { DataContext = details },
                WpfRendering.ReviewViewport, Path.Combine(evidenceDirectory, "error-details-en-US.png"));
            WpfRendering.CapturePng(() => new ErrorPage { DataContext = details },
                WpfRendering.ReviewViewport, Path.Combine(evidenceDirectory, "error-details-evidence-en-US.png"),
                tree => tree.OfType<ScrollViewer>().First().ScrollToBottom());
        }

        WpfRendering.RenderExpectingNoBindingErrors(
            () => new ErrorPage { DataContext = details }, WpfRendering.ReviewViewport,
            tree =>
            {
                ById<TextBox>(tree, "ErrorDetails.Code").Text.ShouldBe("MeituTargetLost");
                ById<TextBox>(tree, "ErrorDetails.Description").Text.ShouldBe(Resource("Failure_MeituTargetLost", "en-US"));
                ById<TextBox>(tree, "ErrorDetails.Description").Text.ShouldNotContain(FailingMeitu.Detail);
                ById<TextBox>(tree, "ErrorDetails.TechnicalDetail").Text.ShouldBe(FailingMeitu.Detail);
                ById<TextBox>(tree, "ErrorDetails.InputPath").Text.ShouldBe(expected.ManagedInputPath);
                ById<TextBox>(tree, "ErrorDetails.InputPath").Text.ShouldNotBe(app.SourcePath);
                ById<TextBox>(tree, "ErrorDetails.ExpectedOutputPath").Text.ShouldBe(expected.ExpectedOutputPath);
                ById<TextBlock>(tree, "ErrorDetails.RetryInformation").Text.ShouldBe("Attempt: 1   Previous retries: 0");
                ById<Image>(tree, "ErrorDetails.Screenshot").Source.ShouldNotBeNull();
                ById<TextBox>(tree, "ErrorDetails.ScreenshotPath").Text.ShouldBe(app.ScreenshotPath);
                AssertCopyableAndActions(tree, retry: true, manual: true);
                AssertKeyboardRoute(tree);
                Invoke(ById<Button>(tree, "ErrorDetails.Retry"));
                PumpUntil(() => app.Navigation.Current is SessionViewModel && !details.IsBusy);
                return true;
            });

        SessionAggregate after = await app.ReloadAsync();
        after.ToSnapshot().CurrentStep!.State.ShouldBe(StepState.Waiting);
        after.Attempts.Count.ShouldBe(failed.Attempts.Count);
        after.Attempts.Single(a => a.Id == attempt.Id).Status.ShouldBe(AttemptStatus.Failed);
        after.Revisions.Count.ShouldBe(failed.Revisions.Count);
        app.Processor.Calls.ShouldBe(1, "Retry does not run the deterministic adapter again or launch an external application");
        app.Navigation.Current.ShouldBeOfType<SessionViewModel>().CanRunStep.ShouldBeTrue();

        // Reopening old evidence is read-only and Back returns to the fresh, runnable projection.
        await app.Navigation.GoToErrorDetailsAsync(failed.Session.Id, attempt.Id, CancellationToken.None);
        ErrorDetailsViewModel historical = app.Navigation.Current.ShouldBeOfType<ErrorDetailsViewModel>();
        WpfRendering.RenderExpectingNoBindingErrors(
            () => new ErrorPage { DataContext = historical }, WpfRendering.ReviewViewport,
            tree =>
            {
                AssertCopyableAndActions(tree, retry: false, manual: false);
                ById<TextBlock>(tree, "ErrorDetails.HistoricalNotice").Text.ShouldNotBeNullOrWhiteSpace();
                ById<Image>(tree, "ErrorDetails.Screenshot").Source.ShouldNotBeNull();
                Invoke(ById<Button>(tree, "ErrorDetails.Back"));
                PumpUntil(() => app.Navigation.Current is SessionViewModel && !historical.IsBusy);
                return true;
            });
        (await app.ReloadAsync()).Attempts.Count.ShouldBe(after.Attempts.Count);
        app.Navigation.Current.ShouldBeOfType<SessionViewModel>().CanRunStep.ShouldBeTrue();
    }

    [Fact]
    public async Task Missing_image_stays_open_switches_language_and_manual_action_reaches_existing_handoff()
    {
        using FailureApplication app = await FailureApplication.CreateAsync(screenshotExists: false);
        SessionAggregate before = await app.ReloadAsync();
        AttemptId attemptId = before.Attempts.Single(a => a.Status == AttemptStatus.Failed).Id;
        await app.Navigation.GoToErrorDetailsAsync(before.Session.Id, attemptId, CancellationToken.None);
        ErrorDetailsViewModel details = app.Navigation.Current.ShouldBeOfType<ErrorDetailsViewModel>();

        // Persist the selected language before applying it, matching Settings' authority order.
        (await app.Services.GetRequiredService<ISettingsRepository>().UpsertAsync(
            [new SettingEntry(SettingKey.UiLanguage, "zh-CN")], CancellationToken.None)).IsSuccess.ShouldBeTrue();

        WpfRendering.RenderExpectingNoBindingErrors(
            () => new ErrorPage { DataContext = details }, WpfRendering.ReviewViewport,
            tree =>
            {
                ById<TextBlock>(tree, "ErrorDetails.ScreenshotStatus").Text.ShouldBe("Evidence image unavailable");
                ById<Image>(tree, "ErrorDetails.Screenshot").Source.ShouldBeNull();
                ById<TextBox>(tree, "ErrorDetails.ScreenshotPath").Text.ShouldBe(app.ScreenshotPath);
                app.Localisation.Use(OperatorLanguage.SimplifiedChinese);
                tree.Root.UpdateLayout();
                ById<TextBox>(tree, "ErrorDetails.Code").Text.ShouldBe("MeituTargetLost");
                ById<TextBox>(tree, "ErrorDetails.Description").Text.ShouldBe(Resource("Failure_MeituTargetLost", "zh-CN"));
                ById<TextBlock>(tree, "ErrorDetails.ScreenshotStatus").Text.ShouldBe("诊断截图不可用");
                ById<TextBox>(tree, "ErrorDetails.InputPath").Text.ShouldContain(".png");
                AssertCopyableAndActions(tree, retry: true, manual: true);
                Invoke(ById<Button>(tree, "ErrorDetails.ManualProcessing"));
                PumpUntil(() => app.Navigation.Current is SessionViewModel && !details.IsBusy);
                return true;
            });
        SessionAggregate after = await app.ReloadAsync();
        after.Session.State.ShouldBe(SessionState.HandedOff);
        after.Attempts.Count.ShouldBe(before.Attempts.Count);
        after.Attempts.Single(a => a.Id == attemptId).Status.ShouldBe(AttemptStatus.Failed);
        app.Navigation.Current.ShouldBeOfType<SessionViewModel>().CanSubmitManualResult.ShouldBeTrue();
        app.Processor.Calls.ShouldBe(1);
    }

    private static void AssertCopyableAndActions(RenderedTree tree, bool retry, bool manual)
    {
        foreach (string id in new[] { "InputPath", "ExpectedOutputPath", "ScreenshotPath", "TechnicalDetail", "Code", "Description" })
        {
            TextBox box = ById<TextBox>(tree, "ErrorDetails." + id);
            box.IsReadOnly.ShouldBeTrue();
            box.Focusable.ShouldBeTrue();
            KeyboardNavigation.GetIsTabStop(box).ShouldBeTrue();
            IValueProvider value = (IValueProvider)UIElementAutomationPeer.CreatePeerForElement(box)
                .GetPattern(PatternInterface.Value)!;
            value.IsReadOnly.ShouldBeTrue();
            value.Value.ShouldBe(box.Text);
        }
        ById<Button>(tree, "ErrorDetails.Retry").Visibility.ShouldBe(retry ? Visibility.Visible : Visibility.Collapsed);
        ById<Button>(tree, "ErrorDetails.ManualProcessing").Visibility.ShouldBe(manual ? Visibility.Visible : Visibility.Collapsed);
        ById<Button>(tree, "ErrorDetails.ReenterAutomation").Visibility.ShouldBe(Visibility.Collapsed);
        foreach (Button button in tree.OfType<Button>().Where(b => b.Visibility == Visibility.Visible))
        {
            button.Focusable.ShouldBeTrue();
            KeyboardNavigation.GetIsTabStop(button).ShouldBeTrue();
            UIElementAutomationPeer.CreatePeerForElement(button).GetPattern(PatternInterface.Invoke).ShouldBeAssignableTo<IInvokeProvider>();
        }
        tree.OfType<Button>().ShouldNotContain(b => AutomationProperties.GetAutomationId(b).Contains("Continue", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Failure_before_input_or_output_is_established_displays_truthful_absence()
    {
        using SettingsScreenHarness culture = new();
        culture.Localisation.Use(OperatorLanguage.English);
        using SessionServiceHarness harness = new();
        string source = harness.Workspace.CreateSourceFile("unreadable.png", []);
        ISessionService sessions = harness.CreateService();
        (await sessions.ImportAsync(WorkflowType.PrepareAsset, source, "unreadable", "tester",
            CancellationToken.None)).IsFailure.ShouldBeTrue();
        SessionId id = (await harness.Repository.ListRecentAsync(10, harness.Clock.GetUtcNow().AddDays(-1),
            CancellationToken.None)).Value.Single().Id;
        SessionAggregate aggregate = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        ErrorDetailsViewModel details = new(sessions, new RecordingNavigation(), culture.Localisation);
        await details.OpenAsync(id, aggregate.Attempts.Single().Id, CancellationToken.None);
        WpfRendering.RenderExpectingNoBindingErrors(
            () => new ErrorPage { DataContext = details }, WpfRendering.ReviewViewport,
            tree =>
            {
                ById<TextBox>(tree, "ErrorDetails.InputPath").Text.ShouldBe("Not established");
                ById<TextBox>(tree, "ErrorDetails.ExpectedOutputPath").Text.ShouldBe("Not established");
                ById<TextBlock>(tree, "ErrorDetails.ScreenshotStatus").Text.ShouldBe("No screenshot captured");
                ById<Image>(tree, "ErrorDetails.Screenshot").Source.ShouldBeNull();
                return true;
            });
    }

    private static void AssertKeyboardRoute(RenderedTree tree)
    {
        Window window = new() { Content = tree.Root, Width = 1000, Height = 700, ShowInTaskbar = false };
        try
        {
            window.Show();
            window.UpdateLayout();
            TextBox input = ById<TextBox>(tree, "ErrorDetails.InputPath");
            input.Focus().ShouldBeTrue();
            input.IsKeyboardFocused.ShouldBeTrue();
            foreach (string next in new[] { "ExpectedOutputPath", "ScreenshotPath", "TechnicalDetail", "Retry", "ManualProcessing", "Back" })
            {
                ((UIElement)Keyboard.FocusedElement).MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)).ShouldBeTrue();
                AutomationProperties.GetAutomationId((DependencyObject)Keyboard.FocusedElement).ShouldBe("ErrorDetails." + next);
            }
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    private static T ById<T>(RenderedTree tree, string id) where T : FrameworkElement =>
        tree.OfType<T>().Single(e => AutomationProperties.GetAutomationId(e) == id);

    private static void Invoke(Button button) => ((IInvokeProvider)UIElementAutomationPeer
        .CreatePeerForElement(button).GetPattern(PatternInterface.Invoke)!).Invoke();

    private static void PumpUntil(Func<bool> done)
    {
        Stopwatch timeout = Stopwatch.StartNew();
        do
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.SystemIdle);
            Thread.Sleep(5);
        }
        while (!done() && timeout.Elapsed < TimeSpan.FromSeconds(15));
        done().ShouldBeTrue("the rendered UIA action must complete");
    }

    private static string Resource(string key, string culture) => new System.Resources.ResourceManager(
        "PrintFlow.App.Resources.Strings", typeof(ErrorDetailsViewModel).Assembly)
        .GetString(key, System.Globalization.CultureInfo.GetCultureInfo(culture))!;

    private sealed class FailureApplication : IDisposable
    {
        private readonly TempApplication _application = new();
        private readonly FakeSingleInstanceGuard _guard = new(SingleInstanceOutcome.Acquired);
        private StartupResult? _started;
        public IServiceProvider Services => _started!.Services!;
        public INavigationService Navigation => Services.GetRequiredService<INavigationService>();
        public ISessionService Sessions => Services.GetRequiredService<ISessionService>();
        public ILocalisationService Localisation => Services.GetRequiredService<ILocalisationService>();
        public FailingMeitu Processor { get; private set; } = null!;
        public string SourcePath { get; private set; } = null!;
        public string ScreenshotPath { get; private set; } = null!;
        public SessionId SessionId { get; private set; }

        public static async Task<FailureApplication> CreateAsync(bool screenshotExists)
        {
            FailureApplication app = new();
            app.SourcePath = Path.Combine(app._application.WorkspaceRoot, "synthetic-input.png");
            app.ScreenshotPath = Path.Combine(app._application.WorkspaceRoot, "deterministic-failure.png");
            await File.WriteAllBytesAsync(app.SourcePath, SyntheticImages.Png(16, 12));
            if (screenshotExists)
                await File.WriteAllBytesAsync(app.ScreenshotPath, SyntheticImages.Png(120, 70));
            app.Processor = new FailingMeitu(app.ScreenshotPath);
            app._started = await new ApplicationStartup(app._guard, app._application.ConfigurationFilePath,
                services =>
                {
                    services.AddSingleton<IMeituProcessor>(app.Processor);
                    services.AddSingleton<IFilePicker>(new StubFilePicker(app.SourcePath));
                }).RunAsync(CancellationToken.None);
            app._started.Status.CanShowShell.ShouldBeTrue();
            app.Localisation.Use(OperatorLanguage.English);
            await app.Navigation.GoHomeAsync(CancellationToken.None);
            await app.Navigation.Current.ShouldBeOfType<HomeViewModel>().ChooseFileCommand.ExecuteAsync(null);
            WorkflowSelectionViewModel selection = app.Navigation.Current.ShouldBeOfType<WorkflowSelectionViewModel>();
            await selection.SelectCommand.ExecuteAsync(selection.Workflows.Single(w => w.Type == WorkflowType.PrepareAsset));
            SessionViewModel processing = app.Navigation.Current.ShouldBeOfType<SessionViewModel>();
            await processing.ConfirmOriginalCommand.ExecuteAsync(null);
            await processing.RunStepCommand.ExecuteAsync(null);
            app.Processor.Calls.ShouldBe(1);
            app.SessionId = (await app.Services.GetRequiredService<ISessionRepository>().ListRecentAsync(
                10, DateTimeOffset.UtcNow.AddDays(-1), CancellationToken.None)).Value.Single().Id;
            return app;
        }

        public async Task<SessionAggregate> ReloadAsync() =>
            (await Services.GetRequiredService<ISessionRepository>().LoadAsync(SessionId, CancellationToken.None)).Value!;

        public void Dispose()
        {
            _started?.Dispose();
            _guard.Dispose();
            _application.Dispose();
        }
    }

    private sealed class FailingMeitu(string screenshot) : IMeituProcessor
    {
        public const string Detail = "Deterministic failure for the rendered Error Details proof.";
        public string AdapterId => "error-details-uia-fake";
        public AdapterExecutionMode Mode => AdapterExecutionMode.Fake;
        public int Calls { get; private set; }
        public Task<OperationResult<AdapterOutput>> ProcessAsync(MeituRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(OperationResult.Fail<AdapterOutput>(OperationFailure.Create(
                FailureCode.MeituTargetLost, Detail, isRetryable: true,
                context: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [AutomationLogEntry.ScreenshotContextKey] = screenshot,
                })));
        }
    }
}
