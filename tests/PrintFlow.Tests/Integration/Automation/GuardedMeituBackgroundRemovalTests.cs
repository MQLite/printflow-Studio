using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Adapters.Meitu;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Integration.Automation;

public sealed class GuardedMeituBackgroundRemovalTests
{
    private const string ExpectedFile = "PF_C1_A.png";
    private const string ExpectedIdentity = "PF_C1_A_副本";
    private const string SaveId = "MainWindow.editorPage.saveButton";
    private const string CancelId = "MainWindow.titleFrame.closeButton";
    private const string FileNameId = "MainWindow.wName.fileNameEdit";

    private static readonly MeituAutomationOptions FastOptions = new()
    {
        PollInterval = TimeSpan.FromMilliseconds(5),
        ActivationTimeout = TimeSpan.FromMilliseconds(40),
        DialogTimeout = TimeSpan.FromMilliseconds(40),
        BackgroundRemovalBusyTimeout = TimeSpan.FromMilliseconds(100),
        BackgroundRemovalCompletionTimeout = TimeSpan.FromMilliseconds(150),
    };

    private sealed class Scenario
    {
        public required FakeWindowLocator Locator { get; init; }
        public required RecordingUiElementProvider Elements { get; init; }
        public required GuardedMeituUiDriver Driver { get; init; }
        public required MeituTarget Editor { get; init; }
        public Queue<string[]> AfterAction { get; } = new();
        public bool ActionInvoked { get; set; }
        public int ActionCount => Elements.Invocations.Count(x =>
            x == MeituFakes.BackgroundActionMarker);
        public int ReturnCount => Elements.Invocations.Count(x =>
            x == MeituFakes.BackgroundReturnMarker);
    }

    private static Scenario Build(
        string identityBefore = ExpectedIdentity,
        string identityAfter = ExpectedIdentity,
        string[][]? afterAction = null,
        string[]? initialScreen = null,
        bool actionPresent = true,
        bool actionEnabled = true,
        MeituBaseline? baseline = null)
    {
        ExternalProcessRef process = MeituFakes.Process();
        ExternalWindowRef editorWindow = MeituFakes.Window(title: MeituFakes.EditorTitle);
        ExternalWindowRef saveSurface = MeituFakes.Window(
            handle: 0x6000, owningProcessId: process.ProcessId,
            title: "Form", className: "QtSaveDialog") with { OwnerHandle = editorWindow.Handle };

        FakeWindowLocator locator = new();
        RecordingUiElementProvider elements = new();
        locator.Register(process, editorWindow);
        locator.PutInForeground(editorWindow);

        elements.SetTexts(editorWindow.Handle, initialScreen ?? IdleScreen());
        elements.AddEditorSaveControl(editorWindow.Handle, process.ProcessId);
        if (actionPresent)
        {
            elements.AddBackgroundPageAction(
                editorWindow.Handle, MeituFakes.BackgroundActionMarker,
                process.ProcessId, enabled: actionEnabled);
        }
        elements.AddBackgroundPageAction(
            editorWindow.Handle, MeituFakes.BackgroundReturnMarker, process.ProcessId);
        elements.AddIdentityDialogControl(
            saveSurface.Handle, FileNameId, "", "Edit", "QLineEdit", UiPatternKind.Value, process.ProcessId);
        elements.AddIdentityDialogControl(
            saveSurface.Handle, CancelId, "\uE0E6", "Button", "IconFontButton",
            UiPatternKind.Invoke, process.ProcessId);
        elements.SetReadValue(FileNameId, identityBefore);

        Scenario scenario = new()
        {
            Locator = locator,
            Elements = elements,
            Editor = new MeituTarget(process, editorWindow),
            Driver = new GuardedMeituUiDriver(
                locator,
                elements,
                new RecordingInputSink(),
                new RecordingEvidenceSink(),
                new StubMeituBaselineProvider(baseline ?? MeituFakes.Baseline()),
                FastOptions,
                TimeProvider.System),
        };
        foreach (string[] screen in afterAction ??
            [BackgroundBusyScreen(), BackgroundBusyScreen(), BackgroundCompleteScreen()])
        {
            scenario.AfterAction.Enqueue(screen);
        }

        elements.OnInvoke = invoked =>
        {
            if (invoked == SaveId)
            {
                locator.OwnedDialogs.Add(saveSurface);
                locator.PutInForeground(saveSurface);
            }
            else if (invoked == CancelId)
            {
                locator.OwnedDialogs.Remove(saveSurface);
                locator.Foreground = new ForegroundIdentity(
                    new WindowHandle(0x9999), process.ProcessId, "XiuXiu");
            }
            else if (invoked == MeituFakes.BackgroundActionMarker)
            {
                scenario.ActionInvoked = true;
            }
            else if (invoked == MeituFakes.BackgroundReturnMarker)
            {
                scenario.ActionInvoked = false;
                elements.SetTexts(editorWindow.Handle, IdleScreen());
                elements.SetReadValue(FileNameId, identityAfter);
            }
        };

        elements.OnReadTextSnapshot = _ =>
        {
            if (scenario.ActionInvoked && scenario.AfterAction.Count > 0)
            {
                elements.SetTexts(editorWindow.Handle, scenario.AfterAction.Dequeue());
            }
        };

        return scenario;
    }

    [Fact]
    public async Task Expected_A_action_Busy_completion_and_expected_A_again_succeeds()
    {
        Scenario s = Build();

        OperationResult<MeituBackgroundRemovalOutcome> result = await s.Driver.RunBackgroundRemovalAsync(
            s.Editor,
            ExpectedFile,
            BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent,
            InertAutomationStopSignal.Instance,
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.TechnicalDetail : "");
        result.Value.ObservedDocumentIdentity.ShouldBe(ExpectedIdentity);
        result.Value.Busy.State.ShouldBe(MeituStartingState.Busy);
        result.Value.ObservedAutomaticModeName.ShouldBe("自动选择");
        result.Value.IdentityAfterCompletion.State
            .ShouldBe(MeituStartingState.KnownEditorWithExpectedWorkingCopy);
        s.ActionCount.ShouldBe(1);
        s.ReturnCount.ShouldBe(1);
        s.Elements.ValueWrites.ShouldBeEmpty();
    }

    [Fact]
    public async Task Cancellation_before_the_action_produces_no_operation_input()
    {
        Scenario s = Build();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            s.Driver.RunBackgroundRemovalAsync(
                s.Editor,
                ExpectedFile,
                BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent,
                InertAutomationStopSignal.Instance,
                cancellation.Token));

        s.ActionCount.ShouldBe(0);
        s.Elements.Invocations.ShouldBeEmpty();
    }

    [Fact]
    public async Task Unspecified_mode_requires_a_product_decision_before_even_identity_input()
    {
        Scenario s = Build();
        OperationResult<MeituBackgroundRemovalOutcome> result = await s.Driver.RunBackgroundRemovalAsync(
            s.Editor, ExpectedFile, BackgroundRemovalDecision.Unspecified, InertAutomationStopSignal.Instance, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Failure.TechnicalDetail.ShouldContain("PRODUCT DECISION REQUIRED");
        result.Failure.Context["inputSent"].ShouldBe("false");
        s.Elements.Invocations.ShouldBeEmpty();
    }

    [Fact]
    public async Task Missing_signed_route_refuses_before_the_identity_probe()
    {
        Scenario s = Build(baseline: MeituFakes.BaselineWithout(backgroundRemoval: true));
        OperationResult<MeituBackgroundRemovalOutcome> result = await Run(s);
        result.IsFailure.ShouldBeTrue();
        s.Elements.Invocations.ShouldBeEmpty();
    }

    [Fact]
    public async Task Wrong_pre_action_identity_produces_no_Background_Removal_action()
    {
        Scenario s = Build(identityBefore: "PF_C1_B_副本");
        OperationResult<MeituBackgroundRemovalOutcome> result = await Run(s);
        result.IsFailure.ShouldBeTrue();
        s.ActionCount.ShouldBe(0);
    }

    [Fact]
    public async Task Identity_B_after_completion_fails_the_C1_claim()
    {
        Scenario s = Build(identityAfter: "PF_C1_B_副本");
        OperationResult<MeituBackgroundRemovalOutcome> result = await Run(s);
        result.IsFailure.ShouldBeTrue();
        s.ActionCount.ShouldBe(1);
        s.ReturnCount.ShouldBe(1);
    }

    [Fact]
    public async Task Missing_action_target_produces_no_unintended_action()
    {
        Scenario s = Build(actionPresent: false);
        OperationResult<MeituBackgroundRemovalOutcome> result = await Run(s);
        result.IsFailure.ShouldBeTrue();
        s.ActionCount.ShouldBe(0);
    }

    [Fact]
    public async Task Disabled_action_target_produces_no_unintended_action()
    {
        Scenario s = Build(actionEnabled: false);
        OperationResult<MeituBackgroundRemovalOutcome> result = await Run(s);
        result.IsFailure.ShouldBeTrue();
        s.ActionCount.ShouldBe(0);
    }

    [Fact]
    public async Task Stale_completion_panel_is_not_accepted_or_restarted()
    {
        Scenario s = Build(initialScreen: BackgroundCompleteScreen(), afterAction: []);
        OperationResult<MeituBackgroundRemovalOutcome> result = await Run(s);
        result.IsFailure.ShouldBeTrue();
        s.ActionCount.ShouldBe(0);
    }

    [Fact]
    public async Task Already_processing_is_not_invoked_a_second_time()
    {
        Scenario s = Build(initialScreen: BackgroundBusyScreen(), afterAction: []);
        OperationResult<MeituBackgroundRemovalOutcome> result = await Run(s);
        result.IsFailure.ShouldBeTrue();
        s.ActionCount.ShouldBe(0);
    }

    [Fact]
    public async Task Busy_never_appearing_is_a_bounded_failure()
    {
        Scenario s = Build(afterAction: []);
        OperationResult<MeituBackgroundRemovalOutcome> result = await Run(s);
        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.Timeout);
        result.Failure.Context["wantedPhase"].ShouldBe("Busy");
        s.ActionCount.ShouldBe(1);
        s.ReturnCount.ShouldBe(0);
    }

    [Fact]
    public async Task Process_exit_while_Busy_is_structured_and_sends_no_return_input()
    {
        Scenario s = Build(afterAction: [BackgroundBusyScreen()]);
        Action<WindowHandle>? scriptedRead = s.Elements.OnReadTextSnapshot;
        int busyReads = 0;
        s.Elements.OnReadTextSnapshot = handle =>
        {
            scriptedRead?.Invoke(handle);
            if (s.ActionInvoked && ++busyReads == 1)
            {
                s.Locator.DeadProcessIds.Add(s.Editor.Process.ProcessId);
            }
        };

        OperationResult<MeituBackgroundRemovalOutcome> result = await Run(s);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.MeituTargetLost);
        result.Failure.MessageKey.ShouldBe("Failure_MeituClosed");
        s.ActionCount.ShouldBe(1);
        s.ReturnCount.ShouldBe(0);
    }

    [Fact]
    public async Task Busy_never_finishing_times_out_without_input_while_Busy()
    {
        Scenario s = Build(afterAction: [BackgroundBusyScreen()]);
        OperationResult<MeituBackgroundRemovalOutcome> result = await Run(s);
        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.Timeout);
        result.Failure.Context["wantedPhase"].ShouldBe("Complete");
        result.Failure.Context["lastPhase"].ShouldBe("Busy");
        s.ActionCount.ShouldBe(1);
        s.ReturnCount.ShouldBe(0);
    }

    [Fact]
    public async Task Busy_disappearing_without_positive_completion_times_out()
    {
        Scenario s = Build(afterAction: [BackgroundBusyScreen(), IdleScreen()]);
        OperationResult<MeituBackgroundRemovalOutcome> result = await Run(s);
        result.IsFailure.ShouldBeTrue();
        result.Failure.Context["lastPhase"].ShouldBe("Unobserved");
        s.ReturnCount.ShouldBe(0);
    }

    [Fact]
    public async Task Unknown_editor_state_during_processing_stops_without_navigation()
    {
        Scenario s = Build(afterAction: [BackgroundBusyScreen(), ["unrecognised screen"]]);

        OperationResult<MeituBackgroundRemovalOutcome> result = await Run(s);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.MeituUnknownState);
        result.Failure.Context["operatorActionRequired"].ShouldBe("true");
        s.ActionCount.ShouldBe(1);
        s.ReturnCount.ShouldBe(0);
    }

    [Fact]
    public async Task Modal_during_processing_stops_without_dismissal_or_return_input()
    {
        Scenario s = Build(afterAction: [BackgroundBusyScreen()]);
        ExternalWindowRef modal = MeituFakes.Window(
            handle: 0x7000, owningProcessId: s.Editor.Process.ProcessId, title: "温馨提示");
        int reads = 0;
        s.Elements.OnReadTextSnapshot = _ =>
        {
            if (s.ActionInvoked && ++reads == 2)
            {
                s.Locator.OwnedDialogs.Add(modal);
            }
        };

        OperationResult<MeituBackgroundRemovalOutcome> result = await Run(s);
        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.MeituBlockingDialog);
        s.ActionCount.ShouldBe(1);
        s.ReturnCount.ShouldBe(0);
    }

    [Fact]
    public async Task Cancellation_during_Busy_produces_no_further_input()
    {
        Scenario s = Build(afterAction: [BackgroundBusyScreen()]);
        using CancellationTokenSource cancellation = new();
        int reads = 0;
        s.Elements.OnReadTextSnapshot = _ =>
        {
            if (s.ActionInvoked && ++reads >= 2)
            {
                cancellation.Cancel();
            }
        };

        await Should.ThrowAsync<OperationCanceledException>(() => s.Driver.RunBackgroundRemovalAsync(
            s.Editor,
            ExpectedFile,
            BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent,
            InertAutomationStopSignal.Instance,
            cancellation.Token));
        s.ActionCount.ShouldBe(1);
        s.ReturnCount.ShouldBe(0);
    }

    [Fact]
    public async Task Wrong_foreground_process_produces_no_input()
    {
        Scenario s = Build();
        s.Locator.RefuseActivation = true;
        s.Locator.Foreground = new ForegroundIdentity(new WindowHandle(0xDEAD), 999, "explorer");
        OperationResult<MeituBackgroundRemovalOutcome> result = await Run(s);
        result.IsFailure.ShouldBeTrue();
        s.Elements.Invocations.ShouldBeEmpty();
    }

    // Run a2-v3-20260917-131609-d2a0a55e: the signed-marker walk threw ElementNotAvailable with
    // an empty message while Meitu replaced its processing overlay with the completion controls.
    // The same editor handle was freshly read two seconds later.

    [Fact]
    public async Task Marker_read_interrupted_by_a_tree_change_is_reobserved_within_the_budget()
    {
        Scenario s = Build();
        InterruptReads(s, readable: true, 2);

        OperationResult<MeituBackgroundRemovalOutcome> result = await Run(s);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.TechnicalDetail : "");
        result.Value.Completion.Observation.VisibleTexts
            .ShouldContain(MeituFakes.BackgroundCompletionMarkers[0]);
        s.ActionCount.ShouldBe(1);
        s.ReturnCount.ShouldBe(1);
    }

    [Fact]
    public async Task Full_completion_read_interrupted_by_a_tree_change_is_reobserved_not_reused()
    {
        Scenario s = Build();
        int[] count = [0];
        string? interruptedKind = null;
        s.Elements.ReadFailure = handle =>
        {
            if (!s.ActionInvoked || ++count[0] != 4)
            {
                return null;
            }

            interruptedKind = s.Elements.ReadKinds[^1];
            return Interruption(handle, readable: true);
        };

        OperationResult<MeituBackgroundRemovalOutcome> result = await Run(s);

        interruptedKind.ShouldBe("full");
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.TechnicalDetail : "");
        s.ActionCount.ShouldBe(1);
        s.ReturnCount.ShouldBe(1);
    }

    [Fact]
    public async Task Tree_changes_until_the_deadline_time_out_with_the_original_exception_and_no_return()
    {
        Scenario s = Build();
        InterruptReads(s, readable: true, [.. Enumerable.Range(2, 10_000)]);

        OperationResult<MeituBackgroundRemovalOutcome> result = await Run(s);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.Timeout);
        result.Failure.Context["wantedPhase"].ShouldBe("Complete");
        // Nothing from a discarded read may stand in for an observed phase.
        result.Failure.Context["lastPhase"].ShouldBe("Unobserved");
        result.Failure.Context["lastReadInterruption"].ShouldContain("ElementNotAvailableException");
        result.Failure.Context["lastReadInterruption"].ShouldContain("Message=(empty)");
        s.ActionCount.ShouldBe(1);
        s.ReturnCount.ShouldBe(0);
    }

    [Fact]
    public async Task Unreadable_root_window_during_the_marker_read_still_stops_immediately()
    {
        Scenario s = Build();
        int[] count = [0];
        s.Elements.ReadFailure = handle =>
            s.ActionInvoked && ++count[0] == 2 ? Interruption(handle, readable: false) : null;

        OperationResult<MeituBackgroundRemovalOutcome> result = await Run(s);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.MeituTargetLost);
        result.Failure.Context[UiReadInterruption.RootWindowReadableKey].ShouldBe("false");
        count[0].ShouldBe(2);
        s.ActionCount.ShouldBe(1);
        s.ReturnCount.ShouldBe(0);
    }

    [Fact]
    public async Task Reobservation_after_a_tree_change_refuses_a_window_that_is_then_gone()
    {
        Scenario s = Build();
        int[] count = [0];
        s.Elements.ReadFailure = handle =>
        {
            if (!s.ActionInvoked || ++count[0] != 2)
            {
                return null;
            }

            s.Locator.Replace(s.Editor.Process);
            return Interruption(handle, readable: true);
        };

        OperationResult<MeituBackgroundRemovalOutcome> result = await Run(s);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.MeituTargetLost);
        result.Failure.Context["targetLoss"].ShouldBe("window-disappeared");
        s.ActionCount.ShouldBe(1);
        s.ReturnCount.ShouldBe(0);
    }

    [Fact]
    public void Interruption_preserves_an_empty_exception_message_and_is_recognised_only_when_root_readable()
    {
        OperationFailure readable = Interruption(new WindowHandle(0x1D0376), readable: true);
        OperationFailure lost = Interruption(new WindowHandle(0x1D0376), readable: false);

        UiReadInterruption.IsDescendantChange(readable).ShouldBeTrue();
        UiReadInterruption.IsDescendantChange(lost).ShouldBeFalse();
        UiReadInterruption.IsDescendantChange(OperationFailure.Create(
            FailureCode.MeituTargetLost, "Window disappeared while reading signed markers: ")).ShouldBeFalse();
        readable.Context["exceptionMessage"].ShouldBe("(empty)");
        readable.Context["exceptionType"].ShouldBe(typeof(System.Windows.Automation.ElementNotAvailableException).FullName);
        readable.TechnicalDetail.ShouldNotContain("disappeared");
        lost.TechnicalDetail.ShouldContain("disappeared");
    }

    [Fact]
    public async Task Interrupted_full_read_of_a_generally_unrecognised_completion_page_waits_for_the_next_poll()
    {
        // The live completion page carries the cutout markers but not the general editor markers.
        string[] completionPageOnly = [.. MeituFakes.BackgroundCompletedTexts()];
        Scenario s = Build(afterAction:
            [BackgroundBusyScreen(), BackgroundBusyScreen(), completionPageOnly]);
        List<string> afterAction = [];
        int[] count = [0];
        s.Elements.ReadFailure = handle =>
        {
            if (!s.ActionInvoked)
            {
                return null;
            }

            afterAction.Add(s.Elements.ReadKinds[^1]);
            return ++count[0] == 4 ? Interruption(handle, readable: true) : null;
        };

        OperationResult<MeituBackgroundRemovalOutcome> result = await Run(s);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.TechnicalDetail : "");
        afterAction.Take(6).ShouldBe(["matching", "matching", "matching", "full", "matching", "full"]);
        s.ActionCount.ShouldBe(1);
        s.ReturnCount.ShouldBe(1);
    }

    [Fact]
    public async Task Interruption_while_waiting_for_Busy_is_reobserved_and_Busy_is_still_required()
    {
        Scenario s = Build();
        InterruptReads(s, readable: true, 1);

        OperationResult<MeituBackgroundRemovalOutcome> result = await Run(s);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.TechnicalDetail : "");
        result.Value.Busy.State.ShouldBe(MeituStartingState.Busy);
        s.ActionCount.ShouldBe(1);
        s.ReturnCount.ShouldBe(1);
    }

    [Fact]
    public async Task Unknown_screen_after_a_tree_change_still_stops_without_navigation()
    {
        Scenario s = Build(afterAction:
            [BackgroundBusyScreen(), BackgroundBusyScreen(), ["unrecognised screen"]]);
        InterruptReads(s, readable: true, 2);

        OperationResult<MeituBackgroundRemovalOutcome> result = await Run(s);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.MeituUnknownState);
        s.ActionCount.ShouldBe(1);
        s.ReturnCount.ShouldBe(0);
    }

    [Theory]
    [InlineData(AutomationStopMode.StopOperation)]
    [InlineData(AutomationStopMode.TakeOver)]
    public async Task Stop_during_tree_changes_ends_orchestration_with_no_cancel_or_return(
        AutomationStopMode mode)
    {
        Scenario s = Build();
        RequestedStop stop = new();
        int[] count = [0];
        s.Elements.ReadFailure = handle =>
        {
            if (!s.ActionInvoked || ++count[0] < 2)
            {
                return null;
            }

            stop.RequestedMode = mode;
            return Interruption(handle, readable: true);
        };

        OperationResult<MeituBackgroundRemovalOutcome> result = await s.Driver.RunBackgroundRemovalAsync(
            s.Editor,
            ExpectedFile,
            BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent,
            stop,
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.Cancelled);
        result.Failure.Context["inputSent"].ShouldBe("false");
        result.Failure.Context["meituCancelInvoked"].ShouldBe("false");
        count[0].ShouldBe(2);
        s.ActionCount.ShouldBe(1);
        s.ReturnCount.ShouldBe(0);
        stop.OperationCancelWasInvoked.ShouldBeFalse();
    }

    private sealed class RequestedStop : IAutomationStopSignal
    {
        public AutomationStopMode? RequestedMode { get; set; }

        public ExternalOperationPhase Phase { get; private set; } = ExternalOperationPhase.NotStarted;

        public bool OperationCancelWasInvoked { get; private set; }

        public bool OperationLeftBusyAfterCancel { get; private set; }

        public void ReportPhase(ExternalOperationPhase phase) => Phase = phase;

        public void ReportOperationCancelOutcome(bool leftBusy)
        {
            OperationCancelWasInvoked = true;
            OperationLeftBusyAfterCancel = leftBusy;
        }
    }

    private static void InterruptReads(Scenario s, bool readable, params int[] readNumbers)
    {
        HashSet<int> interrupted = [.. readNumbers];
        int count = 0;
        s.Elements.ReadFailure = handle =>
            s.ActionInvoked && interrupted.Contains(++count) ? Interruption(handle, readable) : null;
    }

    private static OperationFailure Interruption(WindowHandle root, bool readable) =>
        UiReadInterruption.Create(
            root,
            "reading signed markers",
            new System.Windows.Automation.ElementNotAvailableException(string.Empty),
            readable);

    private static Task<OperationResult<MeituBackgroundRemovalOutcome>> Run(Scenario scenario) =>
        scenario.Driver.RunBackgroundRemovalAsync(
            scenario.Editor,
            ExpectedFile,
            BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent,
            InertAutomationStopSignal.Instance,
            CancellationToken.None);

    private static string[] IdleScreen() => [.. MeituFakes.EditorMarkers, "普通面板"];
    private static string[] BackgroundBusyScreen() =>
        [.. MeituFakes.EditorMarkers, .. MeituFakes.BackgroundBusyTexts()];
    private static string[] BackgroundCompleteScreen() =>
        [.. MeituFakes.EditorMarkers, .. MeituFakes.BackgroundCompletedTexts()];
}
