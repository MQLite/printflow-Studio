using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Adapters.Meitu;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Tests.Fixtures;

namespace PrintFlow.Tests.Integration.Automation;

// The `Unit` result type, aliased inside the namespace: `PrintFlow.Tests.Unit` is a namespace
// in this assembly, and an enclosing namespace wins over a compilation-unit alias.
using Unit = PrintFlow.Domain.Results.Unit;

/// <summary>
/// The rule the whole epic exists for: verify the target, then act — never act, then verify
/// (Epic 11300 Part A §3, §10, §19, §25).
/// </summary>
/// <remarks>
/// Every negative case here asserts on a <i>recorder</i> being empty rather than only on the
/// failure code. A failure code proves PrintFlow reported a problem; an empty recorder proves it
/// did not first send a keystroke into someone else's window and report the problem afterwards.
/// That distinction is the entire difference between this slice and the incident that motivated
/// it.
/// </remarks>
public sealed class GuardedMeituUiDriverTests
{
    private static readonly MeituAutomationOptions FastOptions = new()
    {
        PollInterval = TimeSpan.FromMilliseconds(5),
        ActivationTimeout = TimeSpan.FromMilliseconds(60),
        DialogTimeout = TimeSpan.FromMilliseconds(60),
        AttachTimeout = TimeSpan.FromMilliseconds(60),
        LaunchTimeout = TimeSpan.FromMilliseconds(60),
        OpenConfirmationTimeout = TimeSpan.FromMilliseconds(60),
    };

    private sealed record Harness(
        GuardedMeituUiDriver Driver,
        FakeWindowLocator Locator,
        RecordingUiElementProvider Elements,
        RecordingInputSink Input,
        RecordingEvidenceSink Evidence,
        MeituTarget Target);

    private static Harness Build(bool meituInForeground)
    {
        FakeWindowLocator locator = new();
        RecordingUiElementProvider elements = new();
        RecordingInputSink input = new();
        RecordingEvidenceSink evidence = new();

        MeituTarget target = MeituFakes.Target();
        locator.Register(target.Process, target.Window);

        if (meituInForeground)
        {
            locator.PutInForeground(target.Window);
        }

        GuardedMeituUiDriver driver = new(
            locator, elements, input, evidence,
            new StubMeituBaselineProvider(MeituFakes.Baseline()),
            FastOptions,
            TimeProvider.System);

        return new Harness(driver, locator, elements, input, evidence, target);
    }

    // -----------------------------------------------------------------------------
    // Correct target
    // -----------------------------------------------------------------------------

    [Fact]
    public async Task A_verified_foreground_Meitu_window_may_be_interacted_with()
    {
        Harness h = Build(meituInForeground: true);
        h.Elements.AddStartPageCard(h.Target.Window.Handle, "图片编辑");

        OperationResult<Unit> invoked = await h.Driver.InvokeKnownElementAsync(
            h.Target, KnownMeituElement.WelcomeOpenEntry, CancellationToken.None);

        invoked.IsSuccess.ShouldBeTrue();
        h.Elements.Invocations.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_verified_foreground_Meitu_window_may_receive_a_named_shortcut()
    {
        Harness h = Build(meituInForeground: true);

        OperationResult<Unit> sent = await h.Driver.SendVerifiedShortcutAsync(
            h.Target, KnownShortcut.OpenFile, CancellationToken.None);

        sent.IsSuccess.ShouldBeTrue();
        h.Input.Sends.ShouldHaveSingleItem();
        h.Input.Sends[0].Target.ShouldBe(h.Target.Window.Handle);
    }

    // -----------------------------------------------------------------------------
    // Wrong foreground process — the Explorer regression (§19)
    // -----------------------------------------------------------------------------

    [Fact]
    public async Task No_keystroke_is_sent_when_Explorer_holds_the_foreground()
    {
        Harness h = Build(meituInForeground: false);
        h.Locator.Foreground = new ForegroundIdentity(new WindowHandle(0xE1E1), 777, "explorer");

        OperationResult<Unit> sent = await h.Driver.SendVerifiedShortcutAsync(
            h.Target, KnownShortcut.OpenFile, CancellationToken.None);

        sent.IsFailure.ShouldBeTrue();
        sent.Failure.Code.ShouldBe(FailureCode.MeituTargetLost);
        sent.Failure.TechnicalDetail.ShouldContain("explorer");
        h.Input.Sends.ShouldBeEmpty();
    }

    [Fact]
    public async Task No_element_is_invoked_when_Explorer_holds_the_foreground()
    {
        Harness h = Build(meituInForeground: false);
        h.Locator.Foreground = new ForegroundIdentity(new WindowHandle(0xE1E1), 777, "explorer");
        h.Elements.AddStartPageCard(h.Target.Window.Handle, "图片编辑");

        OperationResult<Unit> invoked = await h.Driver.InvokeKnownElementAsync(
            h.Target, KnownMeituElement.WelcomeOpenEntry, CancellationToken.None);

        invoked.IsFailure.ShouldBeTrue();
        invoked.Failure.Code.ShouldBe(FailureCode.MeituTargetLost);
        h.Elements.Invocations.ShouldBeEmpty();
    }

    /// <summary>
    /// The failure record says, in so many words, that nothing was sent.
    /// </summary>
    /// <remarks>
    /// Worth asserting because this is what an operator or an auditor reads after the fact. A
    /// <c>MeituTargetLost</c> that left it ambiguous whether a key had already gone somewhere
    /// would be a much less useful record than one that states it did not.
    /// </remarks>
    [Fact]
    public async Task The_target_lost_failure_records_that_no_input_was_sent()
    {
        Harness h = Build(meituInForeground: false);

        OperationResult<Unit> sent = await h.Driver.SendVerifiedShortcutAsync(
            h.Target, KnownShortcut.OpenFile, CancellationToken.None);

        sent.Failure.Context["inputSent"].ShouldBe("false");
        sent.Failure.Context["actualProcess"].ShouldBe("explorer");
        sent.Failure.IsRetryable.ShouldBeTrue();
    }

    /// <summary>
    /// A handle reused by another process is not the window PrintFlow verified.
    /// </summary>
    /// <remarks>
    /// The subtle version of the same hazard: the foreground handle matches, so a check that
    /// compared handles alone would wave this through — but the window now belongs to a
    /// different process, which means the application behind it is a different application.
    /// </remarks>
    [Fact]
    public async Task No_input_is_sent_when_the_window_handle_now_belongs_to_another_process()
    {
        Harness h = Build(meituInForeground: true);
        h.Locator.Replace(
            h.Target.Process,
            MeituFakes.Window(handle: h.Target.Window.Handle.Value, owningProcessId: 31337, title: "Something else"));

        OperationResult<Unit> sent = await h.Driver.SendVerifiedShortcutAsync(
            h.Target, KnownShortcut.OpenFile, CancellationToken.None);

        sent.IsFailure.ShouldBeTrue();
        sent.Failure.Code.ShouldBe(FailureCode.MeituTargetLost);
        h.Input.Sends.ShouldBeEmpty();
    }

    [Fact]
    public async Task No_input_is_sent_when_the_Meitu_process_has_exited()
    {
        Harness h = Build(meituInForeground: true);
        h.Locator.DeadProcessIds.Add(h.Target.Process.ProcessId);

        OperationResult<Unit> sent = await h.Driver.SendVerifiedShortcutAsync(
            h.Target, KnownShortcut.OpenFile, CancellationToken.None);

        sent.IsFailure.ShouldBeTrue();
        sent.Failure.Code.ShouldBe(FailureCode.MeituTargetLost);
        h.Input.Sends.ShouldBeEmpty();
    }

    // -----------------------------------------------------------------------------
    // Activation
    // -----------------------------------------------------------------------------

    [Fact]
    public async Task Activation_fails_closed_when_Windows_declines_to_change_the_foreground()
    {
        Harness h = Build(meituInForeground: false);
        h.Locator.RefuseActivation = true;

        OperationResult<MeituTarget> activated =
            await h.Driver.ActivateAsync(h.Target, CancellationToken.None);

        activated.IsFailure.ShouldBeTrue();
        activated.Failure.Code.ShouldBe(FailureCode.MeituTargetLost);
        h.Locator.ActivationRequests.ShouldBeGreaterThan(0);
        h.Input.Sends.ShouldBeEmpty();
    }

    // -----------------------------------------------------------------------------
    // Opening a file
    // -----------------------------------------------------------------------------

    /// <summary>
    /// Puts the target window on the signed empty editor, which is where the picker comes from.
    /// </summary>
    /// <remarks>
    /// The start page has no picker of its own on Meitu 7.8.7.5 — invoking its card opens the
    /// editor as a second top-level window, and the editor's own control raises the dialog
    /// (Part B1 §7). These tests start from the editor so that what they exercise is the
    /// dialog handling rather than the window transition, which has its own tests.
    /// </remarks>
    private static void ShowEmptyEditor(Harness h)
    {
        h.Locator.Replace(
            h.Target.Process, h.Target.Window with { Title = MeituFakes.EditorTitle });
        h.Elements.SetTexts(h.Target.Window.Handle, [.. MeituFakes.EmptyEditorMarkers]);
        h.Elements.AddEditorOpenControl(h.Target.Window.Handle, h.Target.Process.ProcessId);
    }

    [Fact]
    public async Task Opening_a_working_copy_writes_the_path_into_a_dialog_owned_by_the_verified_process()
    {
        Harness h = Build(meituInForeground: true);
        ShowEmptyEditor(h);

        ExternalWindowRef editor = h.Target.Window with { Title = MeituFakes.EditorTitle };
        ExternalWindowRef dialog = MeituFakes.Window(
            handle: 0x2000, owningProcessId: h.Target.Process.ProcessId, title: "打开", className: "#32770");
        h.Locator.Replace(h.Target.Process, editor, dialog);
        h.Elements.AddDialogControl(dialog.Handle, "1148", "Edit");
        h.Elements.AddDialogControl(dialog.Handle, "1", "Button");

        OperationResult<MeituTarget> opened = await h.Driver.OpenWorkingCopyAsync(
            h.Target, @"C:\Temp\printflow\working.png", CancellationToken.None);

        opened.IsSuccess.ShouldBeTrue();
        h.Elements.ValueWrites.ShouldHaveSingleItem();
        h.Elements.ValueWrites[0].Value.ShouldBe(@"C:\Temp\printflow\working.png");

        // The window handed back is the one the file went into, because that is the window a
        // caller must confirm against (§13).
        opened.Value.Window.Title.ShouldBe(MeituFakes.EditorTitle);
    }

    /// <summary>
    /// A dialog belonging to some other process is never written to.
    /// </summary>
    /// <remarks>
    /// This is the file-dialog form of the Explorer regression: a Save As box in another
    /// application looks exactly like the one PrintFlow is waiting for, and typing a path into it
    /// would be the same class of accident. Ownership by the verified Meitu process is what
    /// separates the two, so the test hands the fake a perfectly convincing common dialog owned
    /// by someone else.
    /// </remarks>
    [Fact]
    public async Task A_file_dialog_belonging_to_another_process_is_never_typed_into()
    {
        Harness h = Build(meituInForeground: true);
        ShowEmptyEditor(h);

        ExternalProcessRef impostor = MeituFakes.Process(id: 5150);
        ExternalWindowRef decoy = MeituFakes.Window(
            handle: 0x3000, owningProcessId: impostor.ProcessId, title: "打开", className: "#32770");
        h.Locator.Register(impostor, decoy);

        // The decoy is furnished with a perfectly usable file-name field, so the only thing
        // standing between PrintFlow and typing into it is the ownership check.
        h.Elements.AddDialogControl(decoy.Handle, "1148", "Edit", processId: impostor.ProcessId);

        OperationResult<MeituTarget> opened = await h.Driver.OpenWorkingCopyAsync(
            h.Target, @"C:\Temp\printflow\working.png", CancellationToken.None);

        opened.IsFailure.ShouldBeTrue();
        opened.Failure.Code.ShouldBe(FailureCode.MeituOpenInputFailed);
        h.Elements.ValueWrites.ShouldBeEmpty();
    }

    // -----------------------------------------------------------------------------
    // Cancellation
    // -----------------------------------------------------------------------------

    [Fact]
    public async Task Cancellation_exits_without_sending_anything()
    {
        Harness h = Build(meituInForeground: true);
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            h.Driver.SendVerifiedShortcutAsync(h.Target, KnownShortcut.OpenFile, cancelled.Token));

        h.Input.Sends.ShouldBeEmpty();
        h.Elements.Invocations.ShouldBeEmpty();
    }

    // -----------------------------------------------------------------------------
    // Element naming
    // -----------------------------------------------------------------------------

    /// <summary>
    /// The driver refuses to look for a control the signed evidence does not name.
    /// </summary>
    /// <remarks>
    /// <c>WelcomeOpenEntryName</c> is configurable, which without this check would be a back door
    /// for pointing the automation at any label at all. Requiring the configured value to appear
    /// in the verified baseline keeps configuration a <i>selection from</i> signed evidence
    /// rather than an addition to it (§2).
    /// </remarks>
    [Fact]
    public void An_element_name_absent_from_the_signed_markers_is_never_searched_for()
    {
        FakeWindowLocator locator = new();
        RecordingUiElementProvider elements = new();
        MeituTarget target = MeituFakes.Target();
        locator.Register(target.Process, target.Window);
        locator.PutInForeground(target.Window);

        GuardedMeituUiDriver driver = new(
            locator, elements, new RecordingInputSink(), new RecordingEvidenceSink(),
            new StubMeituBaselineProvider(MeituFakes.Baseline()),
            FastOptions with { WelcomeOpenEntryName = "删除全部" },
            TimeProvider.System);

        OperationResult<UiElementRef> found =
            driver.FindKnownElement(target, KnownMeituElement.WelcomeOpenEntry);

        found.IsFailure.ShouldBeTrue();
        found.Failure.Code.ShouldBe(FailureCode.MeituUnknownState);
    }

    // -----------------------------------------------------------------------------
    // Evidence the chain does not carry (§10, §23)
    // -----------------------------------------------------------------------------

    private static Harness BuildWith(MeituBaseline baseline, bool meituInForeground = true)
    {
        FakeWindowLocator locator = new();
        RecordingUiElementProvider elements = new();
        RecordingInputSink input = new();
        RecordingEvidenceSink evidence = new();

        MeituTarget target = MeituFakes.Target();
        locator.Register(target.Process, target.Window);
        if (meituInForeground)
        {
            locator.PutInForeground(target.Window);
        }

        GuardedMeituUiDriver driver = new(
            locator, elements, input, evidence,
            new StubMeituBaselineProvider(baseline), FastOptions, TimeProvider.System);

        return new Harness(driver, locator, elements, input, evidence, target);
    }

    /// <summary>
    /// With no signed card structure, nothing on the start page is invoked.
    /// </summary>
    /// <remarks>
    /// The fail-closed direction of §10 stated as behaviour rather than as a comment: an
    /// evidence file that is not in the verified chain does not make PrintFlow fall back to a
    /// weaker rule, it makes PrintFlow decline. The card is present and perfectly valid; the
    /// only thing missing is the evidence that says what a card looks like.
    /// </remarks>
    [Fact]
    public async Task Without_a_signed_card_structure_the_start_page_entry_is_never_invoked()
    {
        Harness h = BuildWith(MeituFakes.BaselineWithout(card: true));
        h.Elements.AddStartPageCard(h.Target.Window.Handle, "图片编辑");

        OperationResult<Unit> invoked = await h.Driver.InvokeKnownElementAsync(
            h.Target, KnownMeituElement.WelcomeOpenEntry, CancellationToken.None);

        invoked.IsFailure.ShouldBeTrue();
        invoked.Failure.Code.ShouldBe(FailureCode.MeituUnknownState);
        h.Elements.Invocations.ShouldBeEmpty();
        h.Input.Sends.ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_a_signed_picker_signature_nothing_is_written_or_invoked()
    {
        Harness h = BuildWith(MeituFakes.BaselineWithout(fileDialog: true));
        ShowEmptyEditor(h);

        OperationResult<MeituTarget> opened = await h.Driver.OpenWorkingCopyAsync(
            h.Target, @"C:\Temp\printflow\working.png", CancellationToken.None);

        opened.IsFailure.ShouldBeTrue();
        opened.Failure.Code.ShouldBe(FailureCode.MeituUnknownState);
        h.Elements.ValueWrites.ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_a_signed_empty_editor_the_open_sequence_never_starts()
    {
        Harness h = BuildWith(MeituFakes.BaselineWithout(editorEmpty: true));
        ShowEmptyEditor(h);

        OperationResult<MeituTarget> opened = await h.Driver.OpenWorkingCopyAsync(
            h.Target, @"C:\Temp\printflow\working.png", CancellationToken.None);

        opened.IsFailure.ShouldBeTrue();
        h.Elements.Invocations.ShouldBeEmpty();
        h.Elements.ValueWrites.ShouldBeEmpty();
    }

    // -----------------------------------------------------------------------------
    // The picker (§12, §21)
    // -----------------------------------------------------------------------------

    /// <summary>
    /// A picker that never appears leaves the field unwritten.
    /// </summary>
    [Fact]
    public async Task A_picker_that_never_appears_is_a_failure_with_nothing_typed()
    {
        Harness h = Build(meituInForeground: true);
        ShowEmptyEditor(h);

        OperationResult<MeituTarget> opened = await h.Driver.OpenWorkingCopyAsync(
            h.Target, @"C:\Temp\printflow\working.png", CancellationToken.None);

        opened.IsFailure.ShouldBeTrue();
        opened.Failure.Code.ShouldBe(FailureCode.MeituOpenInputFailed);
        opened.Failure.TechnicalDetail.ShouldContain("nothing was typed");
        h.Elements.ValueWrites.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_picker_with_no_file_name_field_is_never_confirmed()
    {
        Harness h = Build(meituInForeground: true);
        ShowEmptyEditor(h);

        ExternalWindowRef editor = h.Target.Window with { Title = MeituFakes.EditorTitle };
        ExternalWindowRef dialog = MeituFakes.Window(
            handle: 0x2000, owningProcessId: h.Target.Process.ProcessId, title: "打开", className: "#32770");
        h.Locator.Replace(h.Target.Process, editor, dialog);

        // Only the Open button exists. A path that cannot be delivered must not be followed by
        // pressing Open anyway: that would open whatever the picker already had selected.
        h.Elements.AddDialogControl(dialog.Handle, "1", "Button");

        OperationResult<MeituTarget> opened = await h.Driver.OpenWorkingCopyAsync(
            h.Target, @"C:\Temp\printflow\working.png", CancellationToken.None);

        opened.IsFailure.ShouldBeTrue();
        h.Elements.ValueWrites.ShouldBeEmpty();
        h.Elements.Invocations.ShouldNotContain("1");
    }

    [Fact]
    public async Task A_picker_with_no_Open_control_is_never_confirmed()
    {
        Harness h = Build(meituInForeground: true);
        ShowEmptyEditor(h);

        ExternalWindowRef editor = h.Target.Window with { Title = MeituFakes.EditorTitle };
        ExternalWindowRef dialog = MeituFakes.Window(
            handle: 0x2000, owningProcessId: h.Target.Process.ProcessId, title: "打开", className: "#32770");
        h.Locator.Replace(h.Target.Process, editor, dialog);
        h.Elements.AddDialogControl(dialog.Handle, "1148", "Edit");

        OperationResult<MeituTarget> opened = await h.Driver.OpenWorkingCopyAsync(
            h.Target, @"C:\Temp\printflow\working.png", CancellationToken.None);

        opened.IsFailure.ShouldBeTrue();
        h.Elements.Invocations.ShouldNotContain("1");
    }

    /// <summary>
    /// A file-name field that silently drops the write is never followed by Open.
    /// </summary>
    /// <remarks>
    /// The read-back exists for this case. If the write is lost and Open is pressed regardless,
    /// the picker acts on whatever it already had selected — which, in a dialog that opens in the
    /// operator's last-used folder, is a file PrintFlow did not choose and may not open at all
    /// (§12).
    /// </remarks>
    [Fact]
    public async Task A_file_name_field_that_does_not_take_the_write_stops_before_Open()
    {
        Harness h = Build(meituInForeground: true);
        ShowEmptyEditor(h);

        ExternalWindowRef editor = h.Target.Window with { Title = MeituFakes.EditorTitle };
        ExternalWindowRef dialog = MeituFakes.Window(
            handle: 0x2000, owningProcessId: h.Target.Process.ProcessId, title: "打开", className: "#32770");
        h.Locator.Replace(h.Target.Process, editor, dialog);
        h.Elements.AddDialogControl(dialog.Handle, "1148", "Edit");
        h.Elements.AddDialogControl(dialog.Handle, "1", "Button");
        h.Elements.SilentlyDropValueWrites = true;

        OperationResult<MeituTarget> opened = await h.Driver.OpenWorkingCopyAsync(
            h.Target, @"C:\Temp\printflow\working.png", CancellationToken.None);

        opened.IsFailure.ShouldBeTrue();
        opened.Failure.TechnicalDetail.ShouldContain("Open was not invoked");
        h.Elements.Invocations.ShouldNotContain("1");
    }

    /// <summary>
    /// A picker control belonging to another process is never used.
    /// </summary>
    /// <remarks>
    /// Distinct from the owned-window check: here the window is Meitu's but the control inside it
    /// reports a different process. That should be impossible, which is exactly why it is
    /// checked — if it happens, the assumption the lookup rested on has already broken.
    /// </remarks>
    [Fact]
    public async Task A_picker_control_reporting_another_process_is_never_written_to()
    {
        Harness h = Build(meituInForeground: true);
        ShowEmptyEditor(h);

        ExternalWindowRef editor = h.Target.Window with { Title = MeituFakes.EditorTitle };
        ExternalWindowRef dialog = MeituFakes.Window(
            handle: 0x2000, owningProcessId: h.Target.Process.ProcessId, title: "打开", className: "#32770");
        h.Locator.Replace(h.Target.Process, editor, dialog);
        h.Elements.AddDialogControl(dialog.Handle, "1148", "Edit", processId: 9999);
        h.Elements.AddDialogControl(dialog.Handle, "1", "Button");

        OperationResult<MeituTarget> opened = await h.Driver.OpenWorkingCopyAsync(
            h.Target, @"C:\Temp\printflow\working.png", CancellationToken.None);

        opened.IsFailure.ShouldBeTrue();
        opened.Failure.Code.ShouldBe(FailureCode.MeituTargetLost);
        h.Elements.ValueWrites.ShouldBeEmpty();
    }

    /// <summary>
    /// Two controls sharing the picker's file-name id are never chosen between.
    /// </summary>
    /// <remarks>
    /// Not hypothetical: in a Windows common dialog the file-name Edit is nested inside a
    /// ComboBox and <b>both</b> report automation id 1148. The control type separates them, and
    /// this test pins that a second control of the <i>same</i> type would stop the sequence
    /// rather than have one picked.
    /// </remarks>
    [Fact]
    public async Task Two_picker_controls_with_the_signed_identity_stop_the_sequence()
    {
        Harness h = Build(meituInForeground: true);
        ShowEmptyEditor(h);

        ExternalWindowRef editor = h.Target.Window with { Title = MeituFakes.EditorTitle };
        ExternalWindowRef dialog = MeituFakes.Window(
            handle: 0x2000, owningProcessId: h.Target.Process.ProcessId, title: "打开", className: "#32770");
        h.Locator.Replace(h.Target.Process, editor, dialog);
        h.Elements.AddDialogControl(dialog.Handle, "1148", "Edit");
        h.Elements.AddDialogControl(dialog.Handle, "1148", "Edit");
        h.Elements.AddDialogControl(dialog.Handle, "1", "Button");

        OperationResult<MeituTarget> opened = await h.Driver.OpenWorkingCopyAsync(
            h.Target, @"C:\Temp\printflow\working.png", CancellationToken.None);

        opened.IsFailure.ShouldBeTrue();
        opened.Failure.TechnicalDetail.ShouldContain("2 control(s)");
        h.Elements.ValueWrites.ShouldBeEmpty();
    }

    [Fact]
    public async Task Cancellation_during_the_open_sequence_writes_nothing()
    {
        Harness h = Build(meituInForeground: true);
        ShowEmptyEditor(h);

        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            h.Driver.OpenWorkingCopyAsync(h.Target, @"C:\Temp\printflow\working.png", cancelled.Token));

        h.Elements.ValueWrites.ShouldBeEmpty();
        h.Elements.Invocations.ShouldBeEmpty();
    }

    /// <summary>
    /// An unrecognised screen never starts the open sequence.
    /// </summary>
    [Fact]
    public async Task An_unrecognised_screen_never_starts_the_open_sequence()
    {
        Harness h = Build(meituInForeground: true);
        h.Elements.SetTexts(h.Target.Window.Handle, "某个未知界面");

        OperationResult<MeituTarget> opened = await h.Driver.OpenWorkingCopyAsync(
            h.Target, @"C:\Temp\printflow\working.png", CancellationToken.None);

        opened.IsFailure.ShouldBeTrue();
        opened.Failure.Code.ShouldBe(FailureCode.MeituUnknownState);
        h.Elements.Invocations.ShouldBeEmpty();
        h.Elements.ValueWrites.ShouldBeEmpty();
    }

    /// <summary>
    /// The foreground moving away mid-sequence stops it with nothing written.
    /// </summary>
    [Fact]
    public async Task The_foreground_moving_to_Explorer_mid_sequence_writes_nothing()
    {
        Harness h = Build(meituInForeground: true);
        ShowEmptyEditor(h);

        h.Elements.OnInvoke = _ =>
            h.Locator.Foreground = new ForegroundIdentity(new WindowHandle(0xE1E1), 777, "explorer");

        ExternalWindowRef editor = h.Target.Window with { Title = MeituFakes.EditorTitle };
        ExternalWindowRef dialog = MeituFakes.Window(
            handle: 0x2000, owningProcessId: h.Target.Process.ProcessId, title: "打开", className: "#32770");
        h.Locator.Replace(h.Target.Process, editor, dialog);
        h.Elements.AddDialogControl(dialog.Handle, "1148", "Edit");
        h.Elements.AddDialogControl(dialog.Handle, "1", "Button");

        OperationResult<MeituTarget> opened = await h.Driver.OpenWorkingCopyAsync(
            h.Target, @"C:\Temp\printflow\working.png", CancellationToken.None);

        opened.IsFailure.ShouldBeTrue();
        h.Elements.ValueWrites.ShouldBeEmpty();
        h.Input.Sends.ShouldBeEmpty();
    }
}
