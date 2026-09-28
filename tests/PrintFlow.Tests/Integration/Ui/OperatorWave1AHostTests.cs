using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using PrintFlow.App.Localisation;
using PrintFlow.App.Resources;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Settings;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

/// <summary>Operator-launched synthetic host; never part of unattended acceptance.</summary>
[Collection(SqliteCollection.Name)]
public sealed class OperatorWave1AHostTests
{
    [Fact]
    public async Task Nonvisible_seed_and_real_command_persistence_smoke()
    {
        using SyntheticHostData data = new();
        await data.NewReview();
        string first = data.Screen.ReviewTargetIdentity!;
        await data.NewRevision();
        data.Screen.ReviewTargetIdentity.ShouldNotBe(first);
        data.Screen.Id.ShouldBe(data.Jobs.Single());
        await data.Screen.ApproveCommand.ExecuteAsync(null);
        (await data.Approvals()).Length.ShouldBe(1);
        await data.NewReview();
        data.Jobs.Count.ShouldBe(2);
        await data.Crop();
        data.Screen.IsCropping.ShouldBeTrue();
        data.Screen.CropPane!.HasImage.ShouldBeTrue();
        data.Photoshop(false);
        data.Screen.TakeOverConfirmQuestion.ShouldContain("Photoshop");
        data.Photoshop(true);
        data.Screen.HandedOffNotice.ShouldContain("Photoshop");
        await data.NewReview();
        // Finish collection mutations before handing this model to the existing layout-only
        // STA harness. The interactive path creates and uses both model and view on one STA.
        foreach (OperatorLanguage language in new[] { OperatorLanguage.English, OperatorLanguage.SimplifiedChinese })
        {
            data.Language.Use(language);
            WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = data.Screen },
                WpfRendering.ReviewViewport, tree =>
                {
                    ReviewApprovalButton approve = tree.OfType<ReviewApprovalButton>().Single();
                    approve.Command.ShouldBeSameAs(data.Screen.ApproveCommand);
                    approve.TargetIdentity.ShouldBe(data.Screen.ReviewTargetIdentity);
                    approve.IsDefault.ShouldBeFalse();
                    return 0;
                });
        }
    }

    [OperatorDesktopFact]
    [Trait("Category", "OperatorInteractive")]
    public void Operator_launched_visible_synthetic_host()
    {
        // A second guard prevents an accidental direct invocation from showing a window.
        if (Environment.GetEnvironmentVariable("PF_OPUX_SAFE_DESKTOP") != "operator-confirmed")
            throw new InvalidOperationException("Use the explicit safe-time operator launcher.");
        string evidence = Environment.GetEnvironmentVariable("PF_OPUX_HOST_LOG")
            ?? throw new InvalidOperationException("The launcher must allocate a new evidence file.");
        Exception? failure = null;
        Thread thread = new(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            using StreamWriter log = new(new FileStream(evidence, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
            using SyntheticHostData data = new();
            Window? window = null;
            Dispatcher.CurrentDispatcher.BeginInvoke(async () =>
            {
                try
                {
                    await data.NewReview();
                    window = BuildWindow(data, log);
                    window.Closed += (_, _) => Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                    window.Show(); // Only this explicitly opted-in path creates a visible native window.
                }
                catch (Exception ex)
                {
                    failure = ex;
                    window?.Close();
                    Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                }
            });
            Dispatcher.CurrentDispatcher.UnhandledException += (_, e) =>
            {
                failure = e.Exception;
                e.Handled = true;
                window?.Close();
                Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            };
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("Synthetic host failed; retain its log.", failure);
    }

    private static Window BuildWindow(SyntheticHostData data, StreamWriter log)
    {
        SessionScreenView view = new() { DataContext = data.Screen };
        TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6) };
        WrapPanel controls = new();
        DockPanel root = new();
        StackPanel header = new();
        header.Children.Add(new TextBlock { Text = "SYNTHETIC TEST / 合成测试 — physical input only; no automatic PASS. F6 refresh / F7 language (keep focus).", TextWrapping = TextWrapping.Wrap });
        header.Children.Add(controls);
        header.Children.Add(status);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(view);
        Window window = new() { Title = "PF-OPUX Wave1A — SYNTHETIC TEST ONLY / 仅合成测试", Content = root,
            Width = 1200, Height = 850, Background = Brushes.White };
        int positive = 0;
        int clicks = 0;
        string? armed = null;
        bool busy = false;
        CheckBox nextAfterApproval = new() { Content = "TEST STIMULUS: new job after accepted approval / 批准后切换测试任务", Margin = new Thickness(6) };

        void Record(string kind, object? details = null)
        {
            DpiScale dpi = VisualTreeHelper.GetDpi(view);
            string? focus = Keyboard.FocusedElement is DependencyObject focused
                ? AutomationProperties.GetAutomationId(focused) + "/" + focused.GetType().Name : null;
            log.WriteLine(JsonSerializer.Serialize(new { at = DateTimeOffset.UtcNow, kind, details,
                target = data.Screen.ReviewTargetIdentity, language = data.Language.Current.ToString(), positive, clicks,
                nativeFocus = GetFocus().ToInt64(), foreground = GetForegroundWindow().ToInt64(),
                ownedWindow = new WindowInteropHelper(window).Handle.ToInt64(), focus, active = window.IsActive,
                viewport = new { width = view.ActualWidth, height = view.ActualHeight },
                dpi = new { x = dpi.PixelsPerInchX, y = dpi.PixelsPerInchY },
                primaryDisplayDip = new { width = SystemParameters.PrimaryScreenWidth, height = SystemParameters.PrimaryScreenHeight } }));
        }
        async Task Run(string name, Func<Task> action)
        {
            if (busy) { Record("stimulus-refused-busy", name); return; }
            busy = true;
            try
            {
                Record("stimulus-start", name);
                await action();
                await data.Screen.PreviewsLoaded;
                view.UpdateLayout();
                var approvals = await data.Approvals();
                status.Text = $"{name}; positive={positive}; approval clicks={clicks}; persisted approvals={approvals.Length}; target={data.Screen.ReviewTargetIdentity}";
                Record("state", new { name, approvals });
            }
            catch (Exception ex) { status.Text = "HOST ERROR: " + ex.Message; Record("host-error", ex.ToString()); }
            finally { busy = false; }
        }
        void Add(string label, Func<Task> action)
        {
            Button button = new() { Content = label, Margin = new Thickness(3), Padding = new Thickness(5) };
            button.Click += async (_, _) => await Run(label, action);
            controls.Children.Add(button);
        }
        // This is an ordinary WPF Button: test keyboard and physical mouse here first.
        Add("Positive control / 普通按钮", () => { positive++; Record("positive-control"); return Task.CompletedTask; });
        Add("New review / 新任务", data.NewReview);
        Add("New revision / 新版本", data.NewRevision);
        Add("Arm job change / 下次按下切任务", () => { armed = "job"; return Task.CompletedTask; });
        Add("Arm revision change / 下次按下切版本", () => { armed = "revision"; return Task.CompletedTask; });
        Add("Crop / 裁剪", data.Crop);
        Add("Photoshop confirmation", () => { data.Photoshop(false); return Task.CompletedTask; });
        Add("Photoshop handed off", () => { data.Photoshop(true); return Task.CompletedTask; });
        Add("Record state / 记录", () => Task.CompletedTask);
        Button close = new() { Content = "Close / 关闭", Margin = new Thickness(3) };
        close.Click += (_, _) => window.Close();
        controls.Children.Add(close);
        controls.Children.Add(nextAfterApproval);
        window.Closing += (_, e) =>
        {
            // Do not dispose a test workspace while an asynchronous stimulus still uses it.
            if (!busy && !data.Screen.IsBusy) return;
            e.Cancel = true;
            status.Text = "Synthetic operation still running; retry Close when it finishes. / 测试操作未结束，请稍后关闭。";
            Record("close-deferred-busy");
        };
        void TriggerStimulus()
        {
            if (armed is not { } stimulus) return;
            armed = null;
            // Queue after the original down event. No input is forged or injected.
            window.Dispatcher.BeginInvoke(async () => await Run("held-input TEST STIMULUS " + stimulus,
                stimulus == "job" ? data.NewReview : data.NewRevision), DispatcherPriority.Background);
        }
        window.PreviewKeyDown += async (_, e) =>
        {
            Record("key-down", new { key = e.Key.ToString(), e.IsRepeat, source = "WPF received; operator must classify physical/OS/UIA separately" });
            if (e.Key == Key.F6) { e.Handled = true; await Run("refresh", data.Refresh); }
            else if (e.Key == Key.F7)
            {
                e.Handled = true;
                await Run("language", () => { data.Language.Use(data.Language.Current == OperatorLanguage.English
                    ? OperatorLanguage.SimplifiedChinese : OperatorLanguage.English); return Task.CompletedTask; });
            }
            else if (e.Key is Key.Enter or Key.Space && e.OriginalSource is ReviewApprovalButton) TriggerStimulus();
        };
        window.AddHandler(Keyboard.KeyUpEvent, new KeyEventHandler((_, e) => Record("key-up", e.Key.ToString())), true);
        window.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler((_, e) =>
        {
            Record("mouse-down", new { button = e.ChangedButton.ToString(), e.ClickCount });
            if (IsApprovalSource(e.OriginalSource as DependencyObject)) TriggerStimulus();
        }), true);
        window.AddHandler(Mouse.MouseUpEvent, new MouseButtonEventHandler((_, e) => Record("mouse-up", e.ClickCount)), true);
        window.AddHandler(Button.ClickEvent, new RoutedEventHandler((_, e) =>
        {
            if (e.OriginalSource is not ReviewApprovalButton) return;
            clicks++;
            string? exactTarget = data.Screen.ReviewTargetIdentity;
            Record("approval-click-request", exactTarget);
            window.Dispatcher.BeginInvoke(async () => await Run("approval-result", async () =>
            {
                if (data.Screen.ApproveCommand.ExecutionTask is { } execution) await execution;
                var approvals = await data.Approvals();
                bool accepted = approvals.Any(a => a.target == exactTarget);
                Record("approval-result", new { exactTarget, accepted, approvals });
                if (accepted && nextAfterApproval.IsChecked == true) await data.NewReview();
            }), DispatcherPriority.Background);
        }), true);
        window.Activated += (_, _) => Record("activated");
        window.Deactivated += (_, _) => Record("deactivated-stop-input");
        window.ContentRendered += async (_, _) => await Run("visible-ready", () => Task.CompletedTask);
        window.SizeChanged += (_, _) => Record("size");
        window.Closed += (_, _) => Record("closed-no-acceptance-inferred");
        Record("composition", new { workspace = data.Harness.Inner.Workspace.Root, database = data.Harness.Inner.Database.Path,
            lease = Path.Combine(data.Harness.Inner.Workspace.Root, "workstation-lease.db"), evidence = ((FileStream)log.BaseStream).Name,
            startup = "No production App; no recovery/instance startup; navigation recorded; file picker absent; adapters fake" });
        return window;
    }

    private static bool IsApprovalSource(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ReviewApprovalButton) return true;
            source = source is Visual ? VisualTreeHelper.GetParent(source) : null;
        }
        return false;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetFocus();
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();

    private sealed class SyntheticHostData : IDisposable
    {
        private readonly CultureInfo _culture = OperatorCulture.Current;
        private readonly CultureInfo? _defaultCulture = CultureInfo.DefaultThreadCurrentUICulture;
        private readonly CultureInfo _threadCulture = CultureInfo.CurrentUICulture;
        private SessionView? _review;
        public HomeScreenHarness Harness { get; } = new();
        public SessionViewModel Screen { get; }
        public LocalisationService Language { get; }
        public List<SessionId> Jobs { get; } = [];
        public SyntheticHostData()
        {
            Language = new LocalisationService(Harness.Inner.Settings);
            Language.Use(OperatorLanguage.English);
            Screen = new SessionViewModel(Harness.Sessions, Harness.Previews, Harness.TiffReviews,
                new RecordingNavigation(), localisation: Language);
        }
        public async Task NewReview()
        {
            Harness.FilePicker.Path = Harness.WriteSourceFile($"synthetic-{Guid.NewGuid():N}.png");
            await Harness.Home.ChooseFileCommand.ExecuteAsync(null);
            Screen.Open(Harness.Navigation.WorkflowSelectionFor!);
            Jobs.Add(Screen.Id);
            await Screen.ConfirmOriginalCommand.ExecuteAsync(null);
            await Screen.RunStepCommand.ExecuteAsync(null);
            await SaveReview();
        }
        public async Task NewRevision()
        {
            if (!Screen.CanApprove) throw new InvalidOperationException("Start a new review before the revision stimulus.");
            await Screen.RejectCommand.ExecuteAsync(null);
            await Screen.RetryCommand.ExecuteAsync(null);
            await Screen.RunStepCommand.ExecuteAsync(null);
            await SaveReview();
        }
        private async Task SaveReview()
        {
            await Screen.PreviewsLoaded;
            Screen.CanApprove.ShouldBeTrue();
            _review = (await Harness.Sessions.LoadAsync(Screen.Id, CancellationToken.None)).Value;
        }
        public async Task Refresh()
        {
            Screen.Open((await Harness.Sessions.LoadAsync(Screen.Id, CancellationToken.None)).Value);
            await Screen.PreviewsLoaded;
        }
        public async Task Crop()
        {
            Harness.FilePicker.Path = Harness.Inner.WriteOpaqueSourcePng($"synthetic-crop-{Guid.NewGuid():N}.png");
            await Harness.Home.ChooseFileCommand.ExecuteAsync(null);
            Screen.Open(Harness.Navigation.WorkflowSelectionFor!);
            Jobs.Add(Screen.Id);
            await Screen.ConfirmOriginalCommand.ExecuteAsync(null);
            await Screen.SkipCommand.ExecuteAsync(null);
            await Screen.SkipCommand.ExecuteAsync(null);
            await Screen.RunStepCommand.ExecuteAsync(null);
            await Screen.PreviewsLoaded;
            Screen.BeginManualCropCommand.Execute(null);
        }
        public void Photoshop(bool handedOff)
        {
            // Presentation-only, matching the historical capture fixture, never a Photoshop operation.
            SessionView source = _review ?? throw new InvalidOperationException("Create a review first.");
            SessionStep step = source.CurrentStep! with { Step = StepKind.PhotoshopOutput,
                State = handedOff ? StepState.Failed : StepState.Processing };
            Screen.Open(source with { WorkflowType = WorkflowType.GeneratePrintTiff,
                State = handedOff ? SessionState.HandedOff : SessionState.Active,
                CurrentStep = step, Steps = [source.Steps[0], step], AvailableCommands = [] });
            Screen.IsConfirmingTakeOver = !handedOff;
        }
        public async Task<ApprovalEvidence[]> Approvals()
        {
            List<ApprovalEvidence> approvals = [];
            foreach (SessionId id in Jobs)
            {
                SessionAggregate aggregate = (await Harness.Inner.Repository.LoadAsync(id, CancellationToken.None)).Value!;
                approvals.AddRange(aggregate.Reviews.Where(r => r.IsApproved).Select(r =>
                    new ApprovalEvidence($"{r.SessionId}|{r.Step}|{new RevisionId(r.SubjectId)}|{r.ReviewedSha256.Value}", r.Id.ToString())));
            }
            return [.. approvals];
        }
        public void Dispose()
        {
            Harness.Dispose();
            OperatorCulture.Select(_culture);
            CultureInfo.DefaultThreadCurrentUICulture = _defaultCulture;
            CultureInfo.CurrentUICulture = _threadCulture;
        }
    }

    private sealed record ApprovalEvidence(string target, string reviewId);
}

public sealed class OperatorDesktopFactAttribute : FactAttribute
{
    public OperatorDesktopFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("PF_OPUX_SAFE_DESKTOP") != "operator-confirmed")
            Skip = "NOT RUN: operator must launch at a verified safe time using Start-Wave1AHost.ps1.";
    }
}
