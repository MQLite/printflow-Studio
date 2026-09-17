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
