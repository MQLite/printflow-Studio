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
        ActivationTimeout = TimeSpan.FromMilliseconds(250),
        DialogTimeout = TimeSpan.FromMilliseconds(250),
        BackgroundRemovalBusyTimeout = TimeSpan.FromMilliseconds(500),
        BackgroundRemovalCompletionTimeout = TimeSpan.FromSeconds(1),
    };

    private sealed class Scenario
    {
        public required FakeWindowLocator Locator { get; init; }
        public required RecordingUiElementProvider Elements { get; init; }
        public required GuardedMeituUiDriver Driver { get; init; }
        public required MeituTarget Editor { get; init; }
        public Queue<string[]> AfterAction { get; } = new();
        public bool ActionInvoked { get; set; }
        public Action<string>? BaseOnInvoke { get; set; }
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
        MeituBaseline? baseline = null,
        bool recognizersPresent = true)
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

        scenario.BaseOnInvoke = elements.OnInvoke;
        if (recognizersPresent) ScriptRecognizers(scenario, initial: 3, final: 3);
        return scenario;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Take_over_while_the_returned_editor_is_being_read_prevents_the_final_identity_probe(int stopOnReading)
    {
        Scenario s = Build();
        RequestedStop stop = new();
        bool returned = false;
        int returnedReadings = 0;
        Action<string>? originalInvoke = s.Elements.OnInvoke;
        s.Elements.OnInvoke = id => { originalInvoke?.Invoke(id); if (id == MeituFakes.BackgroundReturnMarker) returned = true; };
        Action<WindowHandle>? originalRead = s.Elements.OnReadTextSnapshot;
        s.Elements.OnReadTextSnapshot = handle => { originalRead?.Invoke(handle); if (returned && ++returnedReadings == stopOnReading) stop.RequestedMode = AutomationStopMode.TakeOver; };

        var result = await s.Driver.RunBackgroundRemovalAsync(s.Editor, ExpectedFile,
            BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent, stop, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.Cancelled);
        s.Elements.Invocations.Count(id => id == SaveId).ShouldBe(1);
    }

    [Fact]
    public async Task Returning_to_adjust_waits_for_the_old_cutout_panel_to_retire_before_the_identity_probe()
    {
        Scenario s = Build();
        DelayReturnedPanel(s, readingsBeforeRetirement: 2);

        var result = await Run(s);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.TechnicalDetail : "");
        s.Elements.Invocations.Count(id => id == SaveId).ShouldBe(2);
        s.ReturnCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_return_panel_that_never_retires_times_out_without_probing_or_exporting_the_stale_surface()
    {
        Scenario s = Build();
        DelayReturnedPanel(s, readingsBeforeRetirement: int.MaxValue);

        var result = await Run(s);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.Timeout);
        s.Elements.Invocations.Count(id => id == SaveId).ShouldBe(1);
        s.ReturnCount.ShouldBe(1);
    }

    private static void DelayReturnedPanel(Scenario s, int readingsBeforeRetirement)
    {
        bool returned = false;
        int readings = 0;
        Action<string>? originalInvoke = s.Elements.OnInvoke;
        s.Elements.OnInvoke = id =>
        {
            originalInvoke?.Invoke(id);
            if (id == MeituFakes.BackgroundReturnMarker) returned = true;
        };
        Action<WindowHandle>? originalRead = s.Elements.OnReadTextSnapshot;
        s.Elements.OnReadTextSnapshot = handle =>
        {
            originalRead?.Invoke(handle);
            if (returned) s.Elements.SetTexts(handle, ++readings <= readingsBeforeRetirement ? BackgroundCompleteScreen() : IdleScreen());
        };
    }

    [Fact]
    public async Task Instant_cached_reapplication_uses_verified_history_evidence_without_inventing_Busy()
    {
        Scenario s = Build(baseline: PreviewBaseline(), afterAction: [ExpandedPreviewScreen()]);
        ScriptRecognizers(s, initial: 3, final: 2, reprocessBusy: false, expandedPreview: true, neutralAfterUndo: true);
        s.Elements.Add(s.Editor.Window.Handle, new UiElementIdentity("Button", ApplyId, "应用到背景", "QPushButton",
            s.Editor.Process.ProcessId, [UiPatternKind.Invoke], new UiBounds(100, 500, 260, 32), true, false));
        var result = await s.Driver.RunBackgroundRemovalAsync(s.Editor, ExpectedFile,
            BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent,
            InertAutomationStopSignal.Instance, CancellationToken.None, BackgroundRemovalMode.Products);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.TechnicalDetail : "");
        result.Value.Busy.ShouldBeNull();
        result.Value.ReappliedRecognition.ShouldNotBeNull();
        result.Value.Mode.ShouldBe(BackgroundRemovalMode.Products);
    }

    [Fact]
    public async Task Expanded_preview_completion_requires_the_actual_owned_apply_button()
    {
        Scenario s = Build(baseline: PreviewBaseline(), afterAction: [BackgroundBusyScreen(), BackgroundBusyScreen(), ExpandedPreviewScreen()]);
        ScriptRecognizers(s, initial: 3, final: 2, expandedPreview: true, neutralAfterUndo: true);
        s.Elements.Add(s.Editor.Window.Handle, new UiElementIdentity("Button", ApplyId, "应用到背景", "QPushButton",
            s.Editor.Process.ProcessId, [UiPatternKind.Invoke], new UiBounds(100, 500, 260, 32), true, false));
        var result = await s.Driver.RunBackgroundRemovalAsync(s.Editor, ExpectedFile,
            BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent,
            InertAutomationStopSignal.Instance, CancellationToken.None, BackgroundRemovalMode.Products);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.TechnicalDetail : "");
        result.Value.Completion.Observation.BackgroundRemovalApplyControlObserved.ShouldBeTrue();
        result.Value.Completion.Observation.VisibleTexts.ShouldNotContain("移除背景");
        s.Elements.Invocations.ShouldNotContain(ApplyId);
    }

    [Theory]
    [InlineData("wrong-process")]
    [InlineData("wrong-id")]
    [InlineData("duplicate")]
    [InlineData("zero-bounds")]
    public async Task Apply_background_text_with_an_unverified_button_is_not_completion(string problem)
    {
        Scenario s = Build(baseline: PreviewBaseline(), afterAction: [BackgroundBusyScreen(), BackgroundBusyScreen(), ExpandedPreviewScreen()]);
        var button = new UiElementIdentity("Button", problem == "wrong-id" ? "Other.applyIntelligentButton" : ApplyId,
            "应用到背景", "QPushButton", s.Editor.Process.ProcessId + (problem == "wrong-process" ? 1 : 0),
            [UiPatternKind.Invoke], problem == "zero-bounds" ? default : new UiBounds(100, 500, 260, 32), true, false);
        s.Elements.Add(s.Editor.Window.Handle, button);
        if (problem == "duplicate") s.Elements.Add(s.Editor.Window.Handle, button);
        var result = await s.Driver.RunBackgroundRemovalAsync(s.Editor, ExpectedFile,
            BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent,
            InertAutomationStopSignal.Instance, CancellationToken.None, BackgroundRemovalMode.Products);

        result.IsFailure.ShouldBeTrue();
        s.Elements.Invocations.ShouldNotContain(UndoId);
        s.Elements.Invocations.ShouldNotContain(ApplyId);
        s.ReturnCount.ShouldBe(0);
    }

    private const string ApplyId = "MainWindow.MattingNewParameterWidget.widget.stackedMattingWidget.page1.scrollArea.qt_scrollarea_viewport.scrollAreaWidgetContents.resetWidget.applyIntelligentButton";
    private static string[] ExpandedPreviewScreen() => BackgroundCompleteScreen().Select(text => text == "CUTOUT-REMOVE-BG" ? "应用到背景" : text).ToArray();
    private static MeituBaseline PreviewBaseline() => MeituFakes.Baseline() with
    {
        BackgroundRemoval = MeituFakes.BackgroundRemoval() with
        {
            Completion = new MeituCompletionSignature([.. MeituFakes.BackgroundCompletionMarkers.Take(4), "移除背景"], 5, true),
        },
    };

    [Fact]
    public async Task An_instant_entry_result_is_undone_and_reprocessed_with_fresh_Busy_evidence()
    {
        Scenario s = Build(afterAction: [BackgroundCompleteScreen()]);
        var result = await Run(s);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.TechnicalDetail : "");
        result.Value.Busy.ShouldNotBeNull().State.ShouldBe(MeituStartingState.Busy);
        s.Elements.Invocations.Count(id => id == UndoId).ShouldBe(1);
        s.Elements.Invocations.Count(id => id == RecognizerId(3)).ShouldBe(1);
    }

    [Fact]
    public async Task Stop_arriving_during_the_last_state_read_prevents_undo_input()
    {
        Scenario s = Build();
        ScriptRecognizers(s, initial: 3, final: 2);
        RequestedStop stop = new();
        bool undoLocated = false;
        s.Elements.OnDescribe = element =>
        {
            if (element.Native is FakeUiElement fake && fake.Identity.AutomationId == UndoId) undoLocated = true;
        };
        Action<WindowHandle>? original = s.Elements.OnReadTextSnapshot;
        s.Elements.OnReadTextSnapshot = handle =>
        {
            original?.Invoke(handle);
            if (undoLocated) stop.RequestedMode = AutomationStopMode.TakeOver;
        };
        var result = await s.Driver.RunBackgroundRemovalAsync(s.Editor, ExpectedFile,
            BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent,
            stop, CancellationToken.None, BackgroundRemovalMode.Products);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.Cancelled);
        s.Elements.Invocations.ShouldNotContain(UndoId);
        s.Elements.Invocations.ShouldNotContain(RecognizerId(2));
        s.ReturnCount.ShouldBe(0);
    }

    [Fact]
    public async Task Undo_that_does_not_restore_closed_recognition_never_reapplies_the_mode()
    {
        Scenario s = Build();
        ScriptRecognizers(s, initial: 3, final: 2, undoRestores: false);
        var result = await s.Driver.RunBackgroundRemovalAsync(s.Editor, ExpectedFile,
            BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent,
            InertAutomationStopSignal.Instance, CancellationToken.None, BackgroundRemovalMode.Products);

        result.IsFailure.ShouldBeTrue();
        s.Elements.Invocations.ShouldNotContain(RecognizerId(2));
        s.ReturnCount.ShouldBe(0);
    }

    [Fact]
    public async Task Persistent_entry_controls_do_not_prove_the_reprocessed_result_without_new_Busy()
    {
        Scenario s = Build();
        ScriptRecognizers(s, initial: 3, final: 2, reprocessBusy: false, historyChanges: false);
        var result = await s.Driver.RunBackgroundRemovalAsync(s.Editor, ExpectedFile,
            BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent,
            InertAutomationStopSignal.Instance, CancellationToken.None, BackgroundRemovalMode.Products);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.MeituUnknownState);
        s.ReturnCount.ShouldBe(0);
    }

    [Fact]
    public async Task Stop_after_undo_does_not_reapply_a_recognizer()
    {
        Scenario s = Build();
        ScriptRecognizers(s, initial: 3, final: 2);
        RequestedStop stop = new();
        Action<string>? original = s.Elements.OnInvoke;
        s.Elements.OnInvoke = id => { original?.Invoke(id); if (id == UndoId) stop.RequestedMode = AutomationStopMode.StopOperation; };
        var result = await s.Driver.RunBackgroundRemovalAsync(s.Editor, ExpectedFile,
            BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent,
            stop, CancellationToken.None, BackgroundRemovalMode.Products);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.Cancelled);
        s.Elements.Invocations.ShouldNotContain(RecognizerId(2));
        s.ReturnCount.ShouldBe(0);
    }

    [Theory]
    [InlineData("wrong-process")]
    [InlineData("duplicate")]
    [InlineData("unknown-selected-state")]
    public async Task Unverifiable_recognition_controls_prevent_undo_and_reprocessing(string problem)
    {
        Scenario s = Build();
        ScriptRecognizers(s, initial: 3, final: 2);
        string id = RecognizerId(2);
        if (problem != "duplicate")
            foreach (string old in new[] { id, id + ".widget", id + ".widget.label" }) s.Elements.Remove(s.Editor.Window.Handle, old);
        int pid = s.Editor.Process.ProcessId + (problem == "wrong-process" ? 1 : 0);
        var owner = s.Elements.Add(s.Editor.Window.Handle, new UiElementIdentity("CheckBox", id, "", "CMattingIconButton", pid,
            [UiPatternKind.Invoke, UiPatternKind.Toggle], new UiBounds(220, 220, 51, 80), true, false)
            { IsSelected = problem == "unknown-selected-state" ? null : false });
        var group = s.Elements.Add(s.Editor.Window.Handle, new UiElementIdentity("Group", id + ".widget", "", "QWidget", pid,
            [UiPatternKind.Invoke], new UiBounds(222, 222, 47, 76), true, false), owner);
        s.Elements.Add(s.Editor.Window.Handle, new UiElementIdentity("Text", id + ".widget.label", "商品\n货物", "QLabel", pid,
            [UiPatternKind.Invoke], new UiBounds(222, 260, 47, 32), true, false), group);
        var result = await s.Driver.RunBackgroundRemovalAsync(s.Editor, ExpectedFile,
            BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent,
            InertAutomationStopSignal.Instance, CancellationToken.None, BackgroundRemovalMode.Products);

        result.IsFailure.ShouldBeTrue();
        s.Elements.Invocations.ShouldNotContain(UndoId);
        s.Elements.Invocations.ShouldNotContain(RecognitionRoot);
        s.Elements.Invocations.ShouldNotContain(RecognizerId(2));
        s.ReturnCount.ShouldBe(0);
    }

    [Fact]
    public async Task Auto_entry_is_completed_then_undone_even_when_it_already_chose_the_requested_mode()
    {
        Scenario s = Build();
        ScriptRecognizers(s, initial: 2, final: 2);
        var result = await s.Driver.RunBackgroundRemovalAsync(s.Editor, ExpectedFile,
            BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent,
            InertAutomationStopSignal.Instance, CancellationToken.None, BackgroundRemovalMode.Products);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.TechnicalDetail : "");
        result.Value.Mode.ShouldBe(BackgroundRemovalMode.Products);
        s.Elements.Invocations.Count(id => id == UndoId).ShouldBe(1);
        s.Elements.Invocations.IndexOf(UndoId).ShouldBeLessThan(s.Elements.Invocations.IndexOf(RecognizerId(2)));
        s.Elements.Invocations.Count(id => id == RecognizerId(2)).ShouldBe(1);
    }

    [Theory]
    [InlineData(BackgroundRemovalMode.PortraitPets, 1, "人像宠物")]
    [InlineData(BackgroundRemovalMode.Products, 2, "商品货物")]
    [InlineData(BackgroundRemovalMode.LogosStamps, 3, "图标印章")]
    public async Task Explicit_recognizer_is_reprocessed_and_read_back(BackgroundRemovalMode mode, int index, string name)
    {
        Scenario s = Build();
        ScriptRecognizers(s, initial: index == 3 ? 1 : 3, final: index);
        var result = await s.Driver.RunBackgroundRemovalAsync(s.Editor, ExpectedFile,
            BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent,
            InertAutomationStopSignal.Instance, CancellationToken.None, mode);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.TechnicalDetail : "");
        result.Value.Mode.ShouldBe(mode);
        result.Value.ObservedAutomaticModeName.ShouldBe(name);
        s.Elements.Invocations.Count(id => id == UndoId).ShouldBe(1);
        s.Elements.Invocations.Count(id => id == RecognizerId(index)).ShouldBe(1);
    }

    [Fact]
    public async Task A_recognizer_that_does_not_become_selected_cannot_return_a_result()
    {
        Scenario s = Build();
        ScriptRecognizers(s, initial: 3, final: 1);
        var result = await s.Driver.RunBackgroundRemovalAsync(s.Editor, ExpectedFile,
            BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent,
            InertAutomationStopSignal.Instance, CancellationToken.None, BackgroundRemovalMode.Products);

        result.IsFailure.ShouldBeTrue();
        s.ReturnCount.ShouldBe(0);
    }

    private const string RecognitionRoot = "MainWindow.MattingNewParameterWidget.widget.stackedMattingWidget.page1.scrollArea.qt_scrollarea_viewport.scrollAreaWidgetContents.mattingnew";
    private const string UndoId = "MainWindow.centralwidget.mainStackedWidget.editPage.subBackgroundWidget.titleWidget.stackedWidget.editorPage.undoButton";
    private const string RedoId = "MainWindow.centralwidget.mainStackedWidget.editPage.subBackgroundWidget.titleWidget.stackedWidget.editorPage.redoButton";
    private static string RecognizerId(int index) => RecognitionRoot + ".centerWidget.item1.container1.btnSod" + index;

    private static void ScriptRecognizers(Scenario s, int initial, int final, bool undoRestores = true, bool reprocessBusy = true, bool expandedPreview = false, bool neutralAfterUndo = false, bool historyChanges = true)
    {
        int pid = s.Editor.Process.ProcessId;
        var window = s.Editor.Window.Handle;
        foreach (string old in new[] { RecognitionRoot, RecognitionRoot + ".buttonWidget", RecognitionRoot + ".buttonWidget.titleLabel", UndoId, RedoId }) s.Elements.Remove(window, old);
        var module = s.Elements.Add(window, new UiElementIdentity("CheckBox", RecognitionRoot, "", "ModuleButton", pid,
            [UiPatternKind.Invoke, UiPatternKind.Toggle], new UiBounds(100, 160, 260, 146), true, false) { IsSelected = false });
        var heading = s.Elements.Add(window, new UiElementIdentity("Group", RecognitionRoot + ".buttonWidget", "", "QWidget", pid,
            [UiPatternKind.Invoke], new UiBounds(100, 160, 260, 56), true, false), module);
        s.Elements.Add(window, new UiElementIdentity("Text", RecognitionRoot + ".buttonWidget.titleLabel", "自动选择", "QLabel", pid,
            [UiPatternKind.Invoke], new UiBounds(130, 180, 71, 24), true, false), heading);
        void Choices(int selected)
        {
            for (int i = 0; i < 4; i++)
            {
                string id = RecognizerId(i);
                foreach (string old in new[] { id, id + ".widget", id + ".widget.label" }) s.Elements.Remove(window, old);
                var owner = s.Elements.Add(window, new UiElementIdentity("CheckBox", id, "", "CMattingIconButton", pid,
                    [UiPatternKind.Invoke, UiPatternKind.Toggle], new UiBounds(100 + i * 60, 220, 51, 80), true, false) { IsSelected = selected == i });
                var group = s.Elements.Add(window, new UiElementIdentity("Group", id + ".widget", "", "QWidget", pid,
                    [UiPatternKind.Invoke], new UiBounds(102 + i * 60, 222, 47, 76), true, false), owner);
                string label = i switch { 0 => "关闭\n", 1 => "人像\n宠物", 2 => "商品\n货物", _ => "图标\n印章" };
                s.Elements.Add(window, new UiElementIdentity("Text", id + ".widget.label", label, "QLabel", pid,
                    [UiPatternKind.Invoke], new UiBounds(102 + i * 60, 260, 47, 32), true, false), group);
            }
        }
        void History(bool undoEnabled, bool redoEnabled)
        {
            s.Elements.Remove(window, UndoId);
            s.Elements.Remove(window, RedoId);
            s.Elements.Add(window, new UiElementIdentity("Button", UndoId, "\uE2A3", "proui::IconFontButton", pid,
                [UiPatternKind.Invoke], new UiBounds(1000, 50, 32, 32), undoEnabled, false));
            s.Elements.Add(window, new UiElementIdentity("Button", RedoId, "\uE2A4", "proui::IconFontButton", pid,
                [UiPatternKind.Invoke], new UiBounds(1036, 50, 32, 32), redoEnabled, false));
        }
        Choices(initial);
        History(true, false);
        Action<string>? original = s.BaseOnInvoke;
        s.Elements.OnInvoke = id =>
        {
            original?.Invoke(id);
            if (id == UndoId && undoRestores)
            {
                Choices(0);
                if (neutralAfterUndo) s.Elements.SetTexts(window, ExpandedPreviewScreen().Where(text => text != "应用到背景").ToArray());
                History(false, true);
            }
            if (Enumerable.Range(1, 3).Any(i => id == RecognizerId(i)))
            {
                Choices(final);
                if (historyChanges) History(true, false);
                if (reprocessBusy)
                {
                    s.AfterAction.Enqueue(BackgroundBusyScreen());
                    s.AfterAction.Enqueue(BackgroundBusyScreen());
                }
                s.AfterAction.Enqueue(expandedPreview ? ExpandedPreviewScreen() : BackgroundCompleteScreen());
            }
        };
    }

    [Fact]
    public async Task A_requested_recognizer_with_no_matching_controls_never_returns_an_automatic_result()
    {
        Scenario s = Build(recognizersPresent: false);
        var result = await s.Driver.RunBackgroundRemovalAsync(
            s.Editor, ExpectedFile, BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent,
            InertAutomationStopSignal.Instance, CancellationToken.None, BackgroundRemovalMode.Products);

        result.IsFailure.ShouldBeTrue();
        s.ReturnCount.ShouldBe(0);
    }

    [Fact]
    public async Task Undefined_recognizer_is_rejected_before_any_identity_or_operation_input()
    {
        Scenario s = Build();
        var result = await s.Driver.RunBackgroundRemovalAsync(
            s.Editor, ExpectedFile, BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent,
            InertAutomationStopSignal.Instance, CancellationToken.None, (BackgroundRemovalMode)999);

        result.IsFailure.ShouldBeTrue();
        s.Elements.Invocations.ShouldBeEmpty();
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
        result.Value.Busy.ShouldNotBeNull().State.ShouldBe(MeituStartingState.Busy);
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
    public async Task Signed_cutout_controls_can_appear_before_the_processing_overlay()
    {
        string[] entry = [.. MeituFakes.BackgroundCompletionMarkers.Take(3)];
        Scenario s = Build(afterAction:
            [entry, entry, BackgroundBusyScreen(), BackgroundBusyScreen(), BackgroundCompleteScreen()]);

        var result = await Run(s);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.TechnicalDetail : "");
        result.Value.Busy.ShouldNotBeNull().State.ShouldBe(MeituStartingState.Busy);
        s.ActionCount.ShouldBe(1);
        s.ReturnCount.ShouldBe(1);
    }

    [Fact]
    public async Task Signed_cutout_entry_without_processing_is_still_a_bounded_failure()
    {
        string[] entry = [.. MeituFakes.BackgroundCompletionMarkers.Take(3)];
        Scenario s = Build(afterAction: [entry]);

        var result = await Run(s);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.Timeout);
        result.Failure.Context["wantedPhase"].ShouldBe("Busy");
        s.ActionCount.ShouldBe(1);
        s.ReturnCount.ShouldBe(0);
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public async Task Partial_cutout_controls_are_refused_without_entry_evidence_or_after_Busy(
        int markers, bool afterBusy)
    {
        string[] partial = [.. MeituFakes.BackgroundCompletionMarkers.Take(markers)];
        Scenario s = Build(afterAction: afterBusy
            ? [BackgroundBusyScreen(), BackgroundBusyScreen(), partial, partial] : [partial]);

        var result = await Run(s);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.MeituUnknownState);
        s.ActionCount.ShouldBe(1);
        s.ReturnCount.ShouldBe(0);
    }

    [Fact]
    public async Task Partial_cutout_controls_under_a_changed_title_are_refused()
    {
        string[] partial = [.. MeituFakes.BackgroundCompletionMarkers.Take(3)];
        Scenario s = Build(afterAction: [partial]);
        Action<WindowHandle>? read = s.Elements.OnReadTextSnapshot;
        s.Elements.OnReadTextSnapshot = handle =>
        {
            read?.Invoke(handle);
            if (s.ActionInvoked)
                s.Locator.Replace(s.Editor.Process, s.Editor.Window with { Title = "unexpected editor" });
        };

        var result = await Run(s);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.MeituUnknownState);
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
        result.Value.Busy.ShouldNotBeNull().State.ShouldBe(MeituStartingState.Busy);
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
