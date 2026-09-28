using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using PrintFlow.App.Resources;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Adapters.Fake;
using PrintFlow.Workflow.Ports;
using PrintFlow.App.Localisation;
using PrintFlow.Domain.Settings;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class OperatorWave1Tests
{
    [Theory]
    [InlineData("en")]
    [InlineData("zh-CN")]
    public async Task Active_crop_panel_asks_for_the_selection_not_to_begin_cropping_again(string language)
    {
        using OperatorCultureScope culture = new(language);
        using HomeScreenHarness harness = new();
        harness.FilePicker.Path = harness.Inner.Workspace.CreateSourceFile("crop-synthetic.png",
            SyntheticImages.OpaqueRgbPng(12, 10, (_, _) => ((byte)80, (byte)120, (byte)160)));
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);
        SessionViewModel screen = harness.Session(new RecordingNavigation());
        screen.Open(harness.Navigation.WorkflowSelectionFor!);
        await screen.ConfirmOriginalCommand.ExecuteAsync(null);
        await screen.SkipCommand.ExecuteAsync(null);
        await screen.SkipCommand.ExecuteAsync(null);
        await screen.RunStepCommand.ExecuteAsync(null);
        await screen.PreviewsLoaded;
        screen.BeginManualCropCommand.Execute(null);
        screen.IsCropping.ShouldBeTrue();
        screen.StatusHeading.ShouldBe(language == "en" ? "Needs your input" : "需要你操作");
        screen.NextStepText.ShouldContain(language == "en" ? "Draw or adjust the crop boundary" : "请绘制或调整裁边边界");
        screen.NextStepText.ShouldNotContain(screen.BeginManualCropLabel);
        screen.RecommendedCommand.ShouldNotBeSameAs(screen.BeginManualCropCommand);
        string? destination = Environment.GetEnvironmentVariable("PF_OPUX_CAPTURE_DIR");
        if (!string.IsNullOrWhiteSpace(destination))
            WpfRendering.CapturePng(() => new SessionScreenView { DataContext = screen },
                WpfRendering.ReviewViewport, Path.Combine(destination, $"manual-crop-{language}.png"));
    }

    [Fact]
    public async Task Complete_baseline_toolbar_bindings_and_eligibility_survive_existing_transition_cases()
    {
        using HomeScreenHarness harness = new();
        SessionView source = await Imported(harness);
        SessionViewModel driver = harness.Session(new RecordingNavigation());
        driver.Open(source);
        await AssertSurface();
        await driver.ConfirmOriginalCommand.ExecuteAsync(null);
        await AssertSurface();
        await driver.RunStepCommand.ExecuteAsync(null);
        await AssertSurface();
        await driver.RejectCommand.ExecuteAsync(null);
        await AssertSurface();
        await driver.RetryCommand.ExecuteAsync(null);
        await AssertSurface();
        await driver.HandOffCommand.ExecuteAsync(null);
        await AssertSurface();

        async Task AssertSurface()
        {
            SessionView state = (await harness.Sessions.LoadAsync(source.Id, CancellationToken.None)).Value;
            SessionViewModel screen = harness.Session(new RecordingNavigation());
            screen.Open(state);
            await screen.PreviewsLoaded;
            // Exact original toolbar inventory and predicates from baseline eea6019.
            // AvailableCommands is the service/engine answer, not the new recommendation.
            var original = new (string Id, ICommand Command, bool Visible)[]
            {
                ("ConfirmOriginal", screen.ConfirmOriginalCommand, Allows(CommandKind.ConfirmOriginal)),
                ("RunStep", screen.RunStepCommand, Allows(CommandKind.StartStep)),
                ("Approve", screen.ApproveCommand, Allows(CommandKind.Approve)),
                ("Reject", screen.RejectCommand, Allows(CommandKind.Reject)),
                ("Retry", screen.RetryCommand, Allows(CommandKind.Retry) && state.CurrentStepFailure is not
                    (FailureCode.PdfMultiplePages or FailureCode.PdfUnreadable or FailureCode.PdfEncrypted)),
                ("ErrorDetails", screen.OpenErrorDetailsCommand, state.CurrentFailureAttemptId is not null),
                ("Skip", screen.SkipCommand, Allows(CommandKind.Skip)),
                ("KeepOriginalExtent", screen.KeepOriginalExtentCommand, Allows(CommandKind.KeepOriginalExtent)),
                ("SubmitManualResult", screen.SubmitManualResultCommand, Allows(CommandKind.SubmitManualResult)),
                ("HandOff", screen.HandOffCommand, Allows(CommandKind.HandOff)),
                ("Complete", screen.CompleteCommand, Allows(CommandKind.Complete)),
                ("AddAnotherSize", screen.AddAnotherSizeCommand, Allows(CommandKind.AddAnotherSize)),
            };
            WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = screen },
                WpfRendering.ReviewViewport, tree =>
                {
                    foreach (var expected in original)
                    {
                        Button actual = tree.OfType<Button>().Single(b =>
                            AutomationProperties.GetAutomationId(b) == "Session." + expected.Id);
                        actual.Command.ShouldBeSameAs(expected.Command);
                        actual.Visibility.ShouldBe(expected.Visible ? Visibility.Visible : Visibility.Collapsed);
                        if (expected.Visible) actual.IsEnabled.ShouldBeTrue();
                    }
                    tree.OfType<TextBlock>().ShouldNotContain(t => t.Text.Contains("browsable processing history"));
                    return 0;
                });
            bool Allows(CommandKind command) => state.AvailableCommands.Contains(command);
        }
    }

    [Theory]
    [InlineData(Key.Enter, true)]
    [InlineData(Key.Space, true)]
    [InlineData(Key.Enter, false)]
    [InlineData(Key.Space, false)]
    public async Task Repeating_key_and_old_release_cannot_approve_another_job(Key key, bool anotherJob)
    {
        using HomeScreenHarness harness = new();
        SessionView source = await Imported(harness);
        SessionViewModel first = harness.Session(new RecordingNavigation());
        first.Open(source);
        await first.ConfirmOriginalCommand.ExecuteAsync(null);
        await first.RunStepCommand.ExecuteAsync(null);
        await first.PreviewsLoaded;
        SessionView review = (await harness.Sessions.LoadAsync(source.Id, CancellationToken.None)).Value;
        SessionViewModel second = harness.Session(new RecordingNavigation());
        second.Open(anotherJob
            ? review with { Id = new PrintFlow.Domain.Ids.SessionId(Guid.NewGuid()) }
            : review with { CurrentArtefact = review.CurrentArtefact! with
                { RevisionId = new PrintFlow.Domain.Ids.RevisionId(Guid.NewGuid()) } });
        await second.PreviewsLoaded;
        WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = first },
            WpfRendering.ReviewViewport, tree =>
            {
                using HwndSource inputSource = new(new HwndSourceParameters("Wave1 isolated input") { WindowStyle = 0 });
                Button button = tree.OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "Session.Approve");
                int approvals = 0;
                button.Command = new RelayCommand(() => approvals++);
                SendKey(button, inputSource, key, down: true);
                int atTransition = approvals;
                tree.Root.DataContext = second;
                tree.Root.UpdateLayout();
                SendKey(button, inputSource, key, down: true, repeat: true);
                SendKey(button, inputSource, key, down: false);
                approvals.ShouldBe(atTransition, "the old gesture must never approve the subsequent target");
                int beforeFresh = approvals;
                SendKey(button, inputSource, key, down: true);
                SendKey(button, inputSource, key, down: false);
                approvals.ShouldBe(beforeFresh + 1, "fresh activation on the current action remains supported");
                return 0;
            });
    }

    [Fact]
    public async Task New_review_focus_is_nonactivating_refresh_preserves_focus_and_mouse_remainders_are_consumed()
    {
        using OperatorCultureScope culture = new("en");
        using HomeScreenHarness harness = new();
        SessionView source = await Imported(harness);
        LocalisationService localisation = new(harness.Inner.Settings);
        localisation.Use(OperatorLanguage.English);
        SessionViewModel screen = new(harness.Sessions, harness.Previews, harness.TiffReviews,
            new RecordingNavigation(), localisation: localisation);
        screen.Open(source);
        await screen.ConfirmOriginalCommand.ExecuteAsync(null);
        await screen.RunStepCommand.ExecuteAsync(null);
        await screen.PreviewsLoaded;
        SessionView review = (await harness.Sessions.LoadAsync(source.Id, CancellationToken.None)).Value;
        WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = screen },
            WpfRendering.ReviewViewport, tree =>
            {
                Border panel = (Border)tree.Root.FindName("OperatorStatusPanel");
                Button approve = tree.OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "Session.Approve");
                Button reject = tree.OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "Session.Reject");
                DependencyObject scope = FocusManager.GetFocusScope(tree.Root);
                FocusManager.GetFocusedElement(scope).ShouldBe(panel);
                approve.IsDefault.ShouldBeFalse();
                FocusManager.SetFocusedElement(scope, reject);
                screen.Open(review);
                tree.Root.UpdateLayout();
                FocusManager.GetFocusedElement(scope).ShouldBe(reject);
                localisation.Use(OperatorLanguage.SimplifiedChinese);
                tree.Root.UpdateLayout();
                FocusManager.GetFocusedElement(scope).ShouldBe(reject);
                AutomationProperties.GetName(panel).ShouldBe("等待你检查");
                int invoked = 0;
                approve.Command = new RelayCommand(() => invoked++);
                MouseButtonEventArgs doubleDown = MouseEvent(UIElement.MouseLeftButtonDownEvent, 2);
                approve.RaiseEvent(doubleDown);
                doubleDown.Handled.ShouldBeTrue("a second click is not a new approval gesture");
                MouseButtonEventArgs oldUp = MouseEvent(UIElement.MouseLeftButtonUpEvent, 2);
                approve.RaiseEvent(oldUp);
                oldUp.Handled.ShouldBeTrue();
                invoked.ShouldBe(0);
                approve.RaiseEvent(MouseEvent(UIElement.MouseLeftButtonDownEvent, 1));
                screen.Open(review with { CurrentArtefact = review.CurrentArtefact! with
                    { RevisionId = new PrintFlow.Domain.Ids.RevisionId(Guid.NewGuid()) } });
                tree.Root.UpdateLayout();
                approve.RaiseEvent(MouseEvent(UIElement.MouseLeftButtonUpEvent, 1));
                invoked.ShouldBe(0, "a mouse release cannot cross an exact-target change");
                FocusManager.GetFocusedElement(scope).ShouldBe(panel);
                ((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(approve)!
                    .GetPattern(PatternInterface.Invoke)!).Invoke();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.SystemIdle);
                invoked.ShouldBe(1, "an explicit accessibility invocation remains supported");
                return 0;
            });
    }

    private static MouseButtonEventArgs MouseEvent(RoutedEvent routed, int clicks)
    {
        MouseButtonEventArgs input = new(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        { RoutedEvent = routed };
        typeof(MouseButtonEventArgs).GetProperty(nameof(MouseButtonEventArgs.ClickCount))!.SetValue(input, clicks);
        return input;
    }

    [Theory]
    [InlineData("en")]
    [InlineData("zh-CN")]
    public async Task Running_and_stopped_states_follow_runtime_and_localized_failure(string language)
    {
        using OperatorCultureScope culture = new(language);
        using HomeScreenHarness harness = new();
        SessionView source = await Imported(harness);
        SessionViewModel screen = harness.Session(new RecordingNavigation());
        screen.Open(source);
        await screen.ConfirmOriginalCommand.ExecuteAsync(null);
        harness.Inner.FakeMeitu.SetScenario(FakeAdapterScenario.WaitForStopAt(ExternalOperationPhase.Busy));
        Task run = screen.RunStepCommand.ExecuteAsync(null);
        await harness.Inner.FakeMeitu.HangStarted;
        screen.StatusHeading.ShouldBe(language == "en" ? "Processing automatically" : "正在自动处理");
        screen.RecommendedCommand.ShouldBeNull();
        screen.CanStopAutomation.ShouldBeTrue();
        screen.CanTakeOverAutomation.ShouldBeTrue();
        screen.StopAutomationCommand.Execute(null);
        screen.NextStepText.ShouldBe(screen.StoppingNotice);
        await run;
        await screen.PreviewsLoaded;
        screen.StatusHeading.ShouldBe(language == "en" ? "Stopped or failed" : "已停止或失败");
        screen.StatusReason.ShouldBe(Strings.Failure_AutomationStopped);
        screen.RecommendedCommand.ShouldBeSameAs(screen.RetryCommand);
        SessionView stopped = (await harness.Sessions.LoadAsync(source.Id, CancellationToken.None)).Value;
        screen.Open(stopped with { CurrentStepFailure = FailureCode.PdfEncrypted });
        screen.CanRetry.ShouldBeFalse();
        screen.RecommendedCommand.ShouldBeNull();
        screen.StatusReason.ShouldBe(DisplayNames.Failure(FailureCode.PdfEncrypted));
    }

    [Theory]
    [InlineData(StepKind.Enhancement)]
    [InlineData(StepKind.BackgroundRemoval)]
    public async Task Meitu_labels_are_unchanged_and_Photoshop_never_promises_unavailable_reentry(StepKind step)
    {
        using HomeScreenHarness harness = new();
        SessionView source = await Imported(harness);
        SessionViewModel screen = harness.Session(new RecordingNavigation());
        foreach (string language in new[] { "en", "zh-CN" })
        {
            using OperatorCultureScope culture = new(language);
            screen.Open(source with { CurrentStep = source.CurrentStep! with { Step = step } });
            screen.TakeOverLabel.ShouldBe(Strings.Session_TakeOver);
            screen.StopHint.ShouldBe($"{Strings.Session_StopHint} {Strings.Session_TakeOverHint}");
            screen.TakeOverConfirmQuestion.ShouldBe(Strings.Session_TakeOverConfirmQuestion);
            screen.Open(source with { State = SessionState.HandedOff,
                CurrentStep = source.CurrentStep! with { Step = StepKind.PhotoshopOutput }, AvailableCommands = [] });
            screen.HandedOffNotice.ShouldContain("Photoshop");
            screen.HandedOffNotice.ShouldNotContain(screen.ReenterAutomationLabel);
            screen.RecommendedCommand.ShouldBeNull();
        }
    }

    private static void SendKey(Button button, PresentationSource source, Key key, bool down, bool repeat = false)
    {
        KeyEventArgs input = new(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
        { RoutedEvent = down ? Keyboard.KeyDownEvent : Keyboard.KeyUpEvent };
        // WPF's input manager supplies this flag. Set the same event flag while keeping all
        // routing and Button class handlers real; no desktop-wide key injection is needed.
        if (repeat) typeof(KeyEventArgs).GetMethod("SetRepeat", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(input, [true]);
        button.RaiseEvent(input);
    }

    [Fact]
    public async Task Panel_distinguishes_review_handoff_and_completion_without_claiming_delivery()
    {
        using OperatorCultureScope culture = new("en");
        using HomeScreenHarness harness = new();
        SessionView source = await Imported(harness);
        SessionViewModel screen = harness.Session(new RecordingNavigation());
        screen.Open(source);
        Read(screen, "StatusHeading").ShouldBe("Needs your input");
        await screen.ConfirmOriginalCommand.ExecuteAsync(null);
        await screen.RunStepCommand.ExecuteAsync(null);
        Read(screen, "StatusHeading").ShouldBe("Waiting for your review");
        SessionView review = (await harness.Sessions.LoadAsync(source.Id, CancellationToken.None)).Value;
        screen.Open(review with { State = SessionState.HandedOff, AvailableCommands = [] });
        Read(screen, "StatusHeading").ShouldBe("Handed off");
        Read(screen, "NextStepText").ShouldNotContain("Approve");
        screen.Open(review with { State = SessionState.Completed, AvailableCommands = [], CurrentStep = null });
        Read(screen, "StatusHeading").ShouldBe("Completed");
        Read(screen, "NextStepText").ShouldNotContain("saved", Case.Insensitive);
    }

    private static string Read(SessionViewModel screen, string property)
    {
        var member = typeof(SessionViewModel).GetProperty(property);
        member.ShouldNotBeNull($"the operator panel must expose {property}");
        return (string)member.GetValue(screen)!;
    }

    [Theory]
    [InlineData("en", StepKind.PhotoshopOutput, ImageFormat.Png)]
    [InlineData("zh-CN", StepKind.PhotoshopOutput, ImageFormat.Png)]
    [InlineData("en", StepKind.OriginalConfirmation, ImageFormat.Psd)]
    [InlineData("zh-CN", StepKind.OriginalConfirmation, ImageFormat.Psd)]
    [InlineData("en", StepKind.OriginalConfirmation, ImageFormat.Pdf)]
    [InlineData("zh-CN", StepKind.OriginalConfirmation, ImageFormat.Pdf)]
    public async Task Photoshop_takeover_names_the_actual_application(string language, StepKind step, ImageFormat format)
    {
        using OperatorCultureScope culture = new(language);
        using HomeScreenHarness harness = new();
        SessionView source = await Imported(harness);
        SessionViewModel screen = harness.Session(new RecordingNavigation());
        screen.Open(source with { CurrentStep = source.CurrentStep! with { Step = step }, OriginalSourceFormat = format });
        foreach (string text in new[] { screen.TakeOverLabel, screen.StopHint, screen.TakeOverConfirmQuestion })
        {
            text.ShouldContain("Photoshop");
            text.ShouldNotContain("Meitu");
            text.ShouldNotContain("美图");
        }
        screen.Open(source with
        {
            State = SessionState.HandedOff,
            CurrentStep = source.CurrentStep! with { Step = step },
            OriginalSourceFormat = format,
            AvailableCommands = [CommandKind.ReenterAutomation],
        });
        screen.HandedOffNotice.ShouldContain("Photoshop");
        screen.HandedOffNotice.ShouldContain(screen.ReenterAutomationLabel);
        screen.CanSubmitManualResult.ShouldBeFalse();
        screen.CanStopAutomation.ShouldBeFalse();
        screen.CanTakeOverAutomation.ShouldBeFalse();
    }

    [Theory]
    [InlineData("en")]
    [InlineData("zh-CN")]
    public async Task Capture_synthetic_review_for_visual_evidence(string language)
    {
        string? destination = Environment.GetEnvironmentVariable("PF_OPUX_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(destination)) return;
        using OperatorCultureScope culture = new(language);
        using HomeScreenHarness harness = new();
        SessionView source = await Imported(harness);
        SessionViewModel screen = harness.Session(new RecordingNavigation());
        screen.Open(source);
        await screen.ConfirmOriginalCommand.ExecuteAsync(null);
        await screen.RunStepCommand.ExecuteAsync(null);
        await screen.PreviewsLoaded;
        WpfRendering.CapturePng(() => new SessionScreenView { DataContext = screen },
            WpfRendering.ReviewViewport, Path.Combine(destination, $"review-{language}.png"));

        // Presentation-only synthetic Photoshop fixtures. No Photoshop adapter or process runs.
        SessionStep photoshop = source.CurrentStep! with { Step = StepKind.PhotoshopOutput, State = StepState.Processing };
        SessionView prepared = source with { WorkflowType = WorkflowType.GeneratePrintTiff,
            CurrentStep = photoshop, Steps = [source.Steps[0], photoshop], AvailableCommands = [] };
        SessionViewModel confirmation = harness.Session(new RecordingNavigation());
        confirmation.Open(prepared);
        confirmation.IsConfirmingTakeOver = true;
        await confirmation.PreviewsLoaded;
        WpfRendering.CapturePng(() => new SessionScreenView { DataContext = confirmation },
            WpfRendering.ReviewViewport, Path.Combine(destination, $"photoshop-confirmation-{language}.png"));
        SessionViewModel handedOff = harness.Session(new RecordingNavigation());
        SessionStep halted = photoshop with { State = StepState.Failed };
        handedOff.Open(prepared with { State = SessionState.HandedOff,
            CurrentStep = halted, Steps = [source.Steps[0], halted], AvailableCommands = [],
            LastAutomationStop = new AutomationStopAudit(AutomationStopMode.TakeOver,
                ExternalOperationPhase.Busy, false, RetainedExternalState.Unknown) });
        await handedOff.PreviewsLoaded;
        WpfRendering.CapturePng(() => new SessionScreenView { DataContext = handedOff },
            WpfRendering.ReviewViewport, Path.Combine(destination, $"photoshop-handedoff-{language}.png"));
    }

    internal static async Task<SessionView> Imported(HomeScreenHarness harness)
    {
        harness.FilePicker.Path = harness.WriteSourceFile("wave1-synthetic.png");
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);
        return harness.Navigation.WorkflowSelectionFor!;
    }
}
