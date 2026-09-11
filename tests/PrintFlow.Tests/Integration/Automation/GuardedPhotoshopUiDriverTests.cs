using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Adapters.Photoshop;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Integration.Automation;

using Unit = PrintFlow.Domain.Results.Unit;

/// <summary>
/// What the Photoshop driver does and — more importantly — refuses to do
/// (Epic 11400 Part A §6, §9, §10, §11, §16, §17).
/// </summary>
/// <remarks>
/// Only the operating system is faked. The driver, the classifier and the identity rule are the
/// real implementations, so the guards being asserted here are the guards that will run against
/// the workstation.
///
/// Several tests assert that a recorder is <i>empty</i>. That is deliberate and is the only way
/// to state "no input was produced" as a checkable fact: a test that merely observed a failure
/// result would pass just as happily against a driver that had typed a path into Explorer first.
/// </remarks>
public sealed class GuardedPhotoshopUiDriverTests
{
    [Fact]
    public async Task Open_control_seam_refusal_does_not_claim_an_open_request()
    {
        Harness h = Build();
        StageOpenDialog(h);
        h.Controls.PressFailures.Add(1);
        List<ReadinessProbeStage> progress = [];
        OperationResult<PhotoshopTarget> result = await h.Driver.OpenManagedDocumentAsync(
            h.Target, PhotoshopFakes.ExpectedPath, progress.Add, CancellationToken.None);
        result.IsFailure.ShouldBeTrue();
        h.Controls.Presses.ShouldNotContain(press => press.ControlId == 1);
        progress.ShouldNotContain(ReadinessProbeStage.OpenRequested);
        progress.ShouldNotContain(ReadinessProbeStage.OpenConfirmed);
    }

    private static readonly PhotoshopAutomationOptions FastOptions = new()
    {
        PollInterval = TimeSpan.FromMilliseconds(5),
        ActivationTimeout = TimeSpan.FromMilliseconds(60),
        DialogTimeout = TimeSpan.FromMilliseconds(60),
        IdentityDialogTimeout = TimeSpan.FromMilliseconds(60),
        DialogCloseTimeout = TimeSpan.FromMilliseconds(60),
        OpenConfirmationTimeout = TimeSpan.FromMilliseconds(60),
        AttachTimeout = TimeSpan.FromMilliseconds(60),
        LaunchTimeout = TimeSpan.FromMilliseconds(60),
    };

    private sealed record Harness(
        GuardedPhotoshopUiDriver Driver,
        FakeWindowLocator Locator,
        FakeVerifiedControlSink Controls,
        RecordingInputSink Input,
        RecordingEvidenceSink Evidence,
        PhotoshopTarget Target);

    private static Harness Build(
        bool photoshopInForeground = true,
        string? windowTitle = null,
        PhotoshopBaseline? baseline = null)
    {
        FakeWindowLocator locator = new();
        FakeVerifiedControlSink controls = new();
        RecordingInputSink input = new();
        RecordingEvidenceSink evidence = new();

        PhotoshopTarget target = new(
            PhotoshopFakes.Process(),
            PhotoshopFakes.Window(title: windowTitle ?? PhotoshopFakes.NoDocumentTitle));
        locator.Register(target.Process, target.Window);

        if (photoshopInForeground)
        {
            locator.PutInForeground(target.Window);
        }
        else
        {
            locator.RefuseActivation = true;
        }

        GuardedPhotoshopUiDriver driver = new(
            locator, controls, input, evidence,
            new StubPhotoshopBaselineProvider(baseline ?? PhotoshopFakes.Baseline()),
            FastOptions,
            TimeProvider.System);

        return new Harness(driver, locator, controls, input, evidence, target);
    }

    /// <summary>
    /// Wires the signed Open dialog to appear when Ctrl+O is sent, and to close when pressed.
    /// </summary>
    /// <remarks>
    /// The dialog is raised <i>by the keystroke</i> rather than staged up front, because a
    /// dialog that is already there when PrintFlow first looks is a different situation
    /// entirely — a blocking modal it must refuse. Modelling the real ordering is what keeps
    /// these tests about the open path instead of accidentally about modal detection.
    /// </remarks>
    private static ExternalWindowRef StageOpenDialog(Harness h, string prefilled = "")
    {
        ExternalWindowRef dialog = PhotoshopFakes.Dialog(title: "打开");

        h.Controls.AddControl(dialog.Handle, 1148, "ComboBoxEx32", prefilled);
        h.Controls.AddControl(dialog.Handle, 1, "Button", "打开(&O)");
        h.Controls.AddControl(dialog.Handle, 2, "Button", "取消");

        OnShortcut(h, KnownShortcut.OpenFile, () => h.Locator.OwnedDialogs.Add(dialog));
        ClosesOnPress(h, dialog, 1, 2);

        return dialog;
    }

    /// <summary>
    /// Wires the signed identity surface to appear on Ctrl+Shift+S, pre-filled as Photoshop
    /// pre-fills it.
    /// </summary>
    private static ExternalWindowRef StageIdentityDialog(Harness h, string fileName, string folder)
    {
        ExternalWindowRef dialog = PhotoshopFakes.Dialog(handle: 0xD2B20, title: "另存为");

        h.Controls.AddControl(dialog.Handle, 1001, "Edit", fileName);
        h.Controls.AddControl(dialog.Handle, 1001, "ToolbarWindow32", $"地址: {folder}");
        h.Controls.AddControl(dialog.Handle, 2, "Button", "取消");
        h.Controls.AddControl(dialog.Handle, 1, "Button", "保存(&S)");

        OnShortcut(h, KnownShortcut.SaveAsProbe, () => h.Locator.OwnedDialogs.Add(dialog));
        ClosesOnPress(h, dialog, 2);

        return dialog;
    }

    /// <summary>Stages the exact signed prompt only after PrintFlow sends its guarded close.</summary>
    private static ExternalWindowRef StageDiscardPrompt(
        Harness h,
        string? questionFileName = null,
        string discardText = "否(&N)",
        bool includeDiscard = true,
        string? titleAfterDiscard = null,
        bool removePromptAfterDiscard = true,
        bool reEnableMainFrame = true)
    {
        ExternalWindowRef prompt = PhotoshopFakes.Dialog(
            handle: 0xD15CA,
            title: "Adobe Photoshop",
            className: "PSDialogBox");
        string documentName = questionFileName ?? PhotoshopFakes.ExpectedFileName;
        h.Controls.AddControl(
            prompt.Handle,
            203,
            "Static",
            $"要在关闭之前存储对 Adobe Photoshop 文档 “{documentName}”的更改吗？");
        h.Controls.AddControl(prompt.Handle, 10, "Button", "是(&Y)");
        if (includeDiscard)
        {
            h.Controls.AddControl(prompt.Handle, 11, "Button", discardText);
        }
        h.Controls.AddControl(prompt.Handle, 12, "Button", "取消");

        OnShortcut(h, KnownShortcut.CloseActiveDocument, () =>
        {
            h.Locator.OwnedDialogs.Add(prompt);
            h.Locator.Replace(
                h.Target.Process,
                h.Target.Window with { IsEnabled = false });
            h.Locator.PutInForeground(prompt);
        });

        Action<nint, int>? previous = h.Controls.OnPress;
        h.Controls.OnPress = (host, controlId) =>
        {
            previous?.Invoke(host, controlId);
            if (host != prompt.Handle.Value || controlId != 11)
            {
                return;
            }

            if (removePromptAfterDiscard)
            {
                h.Locator.OwnedDialogs.RemoveAll(dialog => dialog.Handle == prompt.Handle);
            }
            ExternalWindowRef after = PhotoshopFakes.Window(
                title: titleAfterDiscard ?? PhotoshopFakes.NoDocumentTitle,
                enabled: reEnableMainFrame);
            h.Locator.Replace(h.Target.Process, after);
            h.Locator.PutInForeground(after);
        };

        return prompt;
    }

    /// <summary>Chains a reaction onto the fake keyboard without discarding earlier ones.</summary>
    private static void OnShortcut(Harness h, KnownShortcut shortcut, Action reaction)
    {
        Action<KnownShortcut>? previous = h.Input.OnSend;
        h.Input.OnSend = sent =>
        {
            previous?.Invoke(sent);
            if (sent == shortcut)
            {
                reaction();
            }
        };
    }

    /// <summary>Chains "this dialog closes when one of these controls is pressed".</summary>
    private static void ClosesOnPress(Harness h, ExternalWindowRef dialog, params int[] controlIds)
    {
        Action<nint, int>? previous = h.Controls.OnPress;
        h.Controls.OnPress = (host, controlId) =>
        {
            previous?.Invoke(host, controlId);
            if (host == dialog.Handle.Value && controlIds.Contains(controlId))
            {
                h.Locator.OwnedDialogs.RemoveAll(d => d.Handle == dialog.Handle);
            }
        };
    }

    // -----------------------------------------------------------------------------------
    // Foreground safety (§16)
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// An unrelated application holding the foreground means nothing is sent at all.
    /// </summary>
    /// <remarks>
    /// The fake's default foreground is Explorer, and activation is refused, so this models the
    /// live case exactly: PrintFlow asks for the foreground, does not get it, and stops. The
    /// assertion that matters is the empty recorder — a refusal that had already typed Ctrl+O
    /// into Explorer would still have produced a failure result.
    /// </remarks>
    [Fact]
    public async Task An_unrelated_foreground_owner_causes_no_input_at_all()
    {
        Harness h = Build(photoshopInForeground: false);
        StageOpenDialog(h);

        OperationResult<PhotoshopTarget> opened = await h.Driver.OpenManagedDocumentAsync(
            h.Target, PhotoshopFakes.ExpectedPath, CancellationToken.None);

        opened.IsFailure.ShouldBeTrue();
        opened.Failure.Code.ShouldBe(FailureCode.PhotoshopTargetLost);
        opened.Failure.Context["inputSent"].ShouldBe("false");

        h.Input.Sends.ShouldBeEmpty();
        h.Controls.Writes.ShouldBeEmpty();
        h.Controls.Presses.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unrelated_foreground_owner_blocks_the_identity_probe_too()
    {
        Harness h = Build(
            photoshopInForeground: false,
            windowTitle: PhotoshopFakes.TitleFor(PhotoshopFakes.ExpectedFileName));

        OperationResult<PhotoshopDocumentIdentity> identity =
            await h.Driver.ProbeDocumentIdentityAsync(h.Target, CancellationToken.None);

        identity.IsFailure.ShouldBeTrue();
        identity.Failure.Code.ShouldBe(FailureCode.PhotoshopTargetLost);
        h.Input.Sends.ShouldBeEmpty();
    }

    // -----------------------------------------------------------------------------------
    // The open path (§9, §10)
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// The full guarded open: raise, write the exact path, read it back, press Open once.
    /// </summary>
    [Fact]
    public async Task The_exact_managed_path_is_written_read_back_and_confirmed_once()
    {
        Harness h = Build();
        ExternalWindowRef dialog = StageOpenDialog(h);

        OperationResult<PhotoshopTarget> opened = await h.Driver.OpenManagedDocumentAsync(
            h.Target, PhotoshopFakes.ExpectedPath, CancellationToken.None);

        opened.IsSuccess.ShouldBeTrue();

        // Exactly one keystroke, and it is the Open convention — not a typed path.
        h.Input.Sends.ShouldHaveSingleItem();
        h.Input.Sends[0].Shortcut.ShouldBe(KnownShortcut.OpenFile);
        h.Input.Sends[0].Target.ShouldBe(h.Target.Window.Handle);

        // The path went to the signed filename control and nowhere else.
        h.Controls.Writes.ShouldHaveSingleItem();
        h.Controls.Writes[0].ControlId.ShouldBe(1148);
        h.Controls.Writes[0].Value.ShouldBe(PhotoshopFakes.ExpectedPath);

        // Exactly one press, on the signed Open control — never Cancel as well.
        h.Controls.Presses.ShouldHaveSingleItem();
        h.Controls.Presses[0].ShouldBe((dialog.Handle.Value, 1, "Button"));
    }

    /// <summary>
    /// A field that does not read back what was written is never confirmed.
    /// </summary>
    /// <remarks>
    /// This is the case the read-back exists for. Pressing Open on an unverified field would act
    /// on whatever the dialog already had selected — a file PrintFlow did not choose, opened as
    /// though it had. The dialog is cancelled instead, and Cancel is the only thing pressed.
    /// </remarks>
    [Fact]
    public async Task A_read_back_mismatch_cancels_instead_of_confirming()
    {
        Harness h = Build();
        ExternalWindowRef dialog = StageOpenDialog(h);
        h.Controls.ReadBackOverride = @"C:\Users\admin\Downloads\SOMETHING-ELSE.png";

        OperationResult<PhotoshopTarget> opened = await h.Driver.OpenManagedDocumentAsync(
            h.Target, PhotoshopFakes.ExpectedPath, CancellationToken.None);

        opened.IsFailure.ShouldBeTrue();
        opened.Failure.Code.ShouldBe(FailureCode.PhotoshopOpenInputFailed);
        opened.Failure.Context["confirmPressed"].ShouldBe("false");

        h.Controls.Presses.ShouldHaveSingleItem();
        h.Controls.Presses[0].ControlId.ShouldBe(2);
    }

    /// <summary>A dialog that never appears produces no write and no press.</summary>
    [Fact]
    public async Task No_open_dialog_means_nothing_is_written_or_pressed()
    {
        Harness h = Build();

        OperationResult<PhotoshopTarget> opened = await h.Driver.OpenManagedDocumentAsync(
            h.Target, PhotoshopFakes.ExpectedPath, CancellationToken.None);

        opened.IsFailure.ShouldBeTrue();
        opened.Failure.Code.ShouldBe(FailureCode.PhotoshopUnknownState);
        h.Controls.Writes.ShouldBeEmpty();
        h.Controls.Presses.ShouldBeEmpty();
    }

    /// <summary>
    /// Without signed Open-dialog evidence, not even the keystroke is sent.
    /// </summary>
    [Fact]
    public async Task Without_signed_open_dialog_evidence_no_keystroke_is_sent()
    {
        Harness h = Build(baseline: PhotoshopFakes.Baseline(includeOpenDialog: false));
        StageOpenDialog(h);

        OperationResult<PhotoshopTarget> opened = await h.Driver.OpenManagedDocumentAsync(
            h.Target, PhotoshopFakes.ExpectedPath, CancellationToken.None);

        opened.IsFailure.ShouldBeTrue();
        opened.Failure.Code.ShouldBe(FailureCode.PhotoshopUnknownState);
        h.Input.Sends.ShouldBeEmpty();
        h.Controls.Writes.ShouldBeEmpty();
    }

    // -----------------------------------------------------------------------------------
    // The identity probe (§11, §13)
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// The probe reads both halves of the identity and cancels — it never saves.
    /// </summary>
    [Fact]
    public async Task The_identity_probe_reads_name_and_folder_then_cancels()
    {
        Harness h = Build(windowTitle: PhotoshopFakes.TitleFor(PhotoshopFakes.ExpectedFileName));
        ExternalWindowRef dialog = StageIdentityDialog(
            h, PhotoshopFakes.ExpectedFileName, PhotoshopFakes.WorkingDirectory);

        OperationResult<PhotoshopDocumentIdentity> identity =
            await h.Driver.ProbeDocumentIdentityAsync(h.Target, CancellationToken.None);

        identity.IsSuccess.ShouldBeTrue();
        identity.Value.ObservedFileName.ShouldBe(PhotoshopFakes.ExpectedFileName);
        identity.Value.ObservedDirectory.ShouldBe(PhotoshopFakes.WorkingDirectory);
        identity.Value.ObservedFullPath.ShouldBe(PhotoshopFakes.ExpectedPath);

        // Read-only: nothing was written to the Save As surface at all.
        h.Controls.Writes.ShouldBeEmpty();

        // And exactly one press, on Cancel — control 1 is Save and must never be pressed.
        h.Controls.Presses.ShouldHaveSingleItem();
        h.Controls.Presses[0].ShouldBe((dialog.Handle.Value, 2, "Button"));
    }

    /// <summary>
    /// The probe reports what Photoshop is holding, whatever that turns out to be.
    /// </summary>
    /// <remarks>
    /// Returning the observed path rather than a yes/no answer is what keeps the comparison at
    /// the caller: a probe that returned a boolean could quietly start saying <c>true</c> for a
    /// weaker reason, whereas a path cannot express "close enough".
    /// </remarks>
    [Fact]
    public async Task The_probe_reports_a_foreign_document_faithfully()
    {
        Harness h = Build(windowTitle: PhotoshopFakes.TitleFor("SOMEONE-ELSES-WORK.psd"));
        StageIdentityDialog(h, "SOMEONE-ELSES-WORK.psd", @"C:\Users\admin\Desktop");

        OperationResult<PhotoshopDocumentIdentity> identity =
            await h.Driver.ProbeDocumentIdentityAsync(h.Target, CancellationToken.None);

        identity.IsSuccess.ShouldBeTrue();
        identity.Value.ObservedFullPath.ShouldBe(@"C:\Users\admin\Desktop\SOMEONE-ELSES-WORK.psd");

        PhotoshopDocumentIdentityRule
            .MatchesExpectedDocument(PhotoshopFakes.ExpectedPath, identity.Value.ObservedFullPath)
            .ShouldBeFalse();
    }

    /// <summary>
    /// With no document loaded, the probe raises nothing at all.
    /// </summary>
    /// <remarks>
    /// The title check happens before the keystroke, so an empty Photoshop is never asked a
    /// question about a document that does not exist — and, since Ctrl+Shift+S over a start
    /// screen does nothing anyway, this turns a silent timeout into an immediate honest answer.
    /// </remarks>
    [Fact]
    public async Task With_no_document_open_the_probe_sends_nothing()
    {
        Harness h = Build();

        OperationResult<PhotoshopDocumentIdentity> identity =
            await h.Driver.ProbeDocumentIdentityAsync(h.Target, CancellationToken.None);

        identity.IsFailure.ShouldBeTrue();
        identity.Failure.Code.ShouldBe(FailureCode.PhotoshopDocumentIdentityUnconfirmed);
        identity.Failure.Context["inputSent"].ShouldBe("false");
        h.Input.Sends.ShouldBeEmpty();
    }

    /// <summary>Address text that lost its signed prefix refuses rather than guessing.</summary>
    [Fact]
    public async Task An_unrecognised_address_bar_refuses_identity()
    {
        Harness h = Build(windowTitle: PhotoshopFakes.TitleFor(PhotoshopFakes.ExpectedFileName));
        ExternalWindowRef dialog = PhotoshopFakes.Dialog(handle: 0xD2B20, title: "另存为");
        h.Controls.AddControl(dialog.Handle, 1001, "Edit", PhotoshopFakes.ExpectedFileName);

        // The address bar has lost its signed prefix — the surface is no longer the signed one.
        h.Controls.AddControl(dialog.Handle, 1001, "ToolbarWindow32", PhotoshopFakes.WorkingDirectory);
        h.Controls.AddControl(dialog.Handle, 2, "Button", "取消");
        OnShortcut(h, KnownShortcut.SaveAsProbe, () => h.Locator.OwnedDialogs.Add(dialog));
        ClosesOnPress(h, dialog, 2);

        OperationResult<PhotoshopDocumentIdentity> identity =
            await h.Driver.ProbeDocumentIdentityAsync(h.Target, CancellationToken.None);

        identity.IsFailure.ShouldBeTrue();
        identity.Failure.Code.ShouldBe(FailureCode.PhotoshopDocumentIdentityUnconfirmed);

        // The surface is still cancelled, so a refusal never leaves Photoshop modal.
        h.Controls.Presses.ShouldContain(p => p.ControlId == 2);
    }

    /// <summary>Without signed identity evidence, the probe sends nothing and claims nothing.</summary>
    [Fact]
    public async Task Without_signed_identity_evidence_nothing_is_probed()
    {
        Harness h = Build(
            windowTitle: PhotoshopFakes.TitleFor(PhotoshopFakes.ExpectedFileName),
            baseline: PhotoshopFakes.Baseline(includeIdentity: false));

        OperationResult<PhotoshopDocumentIdentity> identity =
            await h.Driver.ProbeDocumentIdentityAsync(h.Target, CancellationToken.None);

        identity.IsFailure.ShouldBeTrue();
        identity.Failure.Code.ShouldBe(FailureCode.PhotoshopDocumentIdentityUnconfirmed);
        h.Input.Sends.ShouldBeEmpty();
    }

    // -----------------------------------------------------------------------------------
    // Target loss (§17)
    // -----------------------------------------------------------------------------------

    /// <summary>A process that has exited produces no further input.</summary>
    [Fact]
    public async Task A_process_that_has_exited_produces_no_further_input()
    {
        Harness h = Build();
        StageOpenDialog(h);
        h.Locator.DeadProcessIds.Add(h.Target.Process.ProcessId);

        OperationResult<PhotoshopTarget> opened = await h.Driver.OpenManagedDocumentAsync(
            h.Target, PhotoshopFakes.ExpectedPath, CancellationToken.None);

        opened.IsFailure.ShouldBeTrue();
        opened.Failure.Code.ShouldBe(FailureCode.PhotoshopTargetLost);
        opened.Failure.Context["inputSent"].ShouldBe("false");
        h.Input.Sends.ShouldBeEmpty();
        h.Controls.Writes.ShouldBeEmpty();
    }

    /// <summary>
    /// A handle that now belongs to a different process is refused, not driven.
    /// </summary>
    /// <remarks>
    /// The handle-reuse case. A window handle stays valid-looking after the window it named is
    /// destroyed and the number reissued, so ownership has to be re-read rather than remembered
    /// — otherwise PrintFlow would type a file path into whatever inherited the number.
    /// </remarks>
    [Fact]
    public async Task A_reused_handle_owned_by_another_process_is_refused()
    {
        Harness h = Build();
        StageOpenDialog(h);

        h.Locator.Replace(
            h.Target.Process,
            PhotoshopFakes.Window(owningProcessId: 999_999));

        OperationResult<PhotoshopTarget> opened = await h.Driver.OpenManagedDocumentAsync(
            h.Target, PhotoshopFakes.ExpectedPath, CancellationToken.None);

        opened.IsFailure.ShouldBeTrue();
        opened.Failure.Code.ShouldBe(FailureCode.PhotoshopTargetLost);
        opened.Failure.Context["actualProcessId"].ShouldBe("999999");
        h.Input.Sends.ShouldBeEmpty();
    }

    /// <summary>A window that has disappeared produces no input.</summary>
    [Fact]
    public async Task A_window_that_has_disappeared_produces_no_input()
    {
        Harness h = Build();
        StageOpenDialog(h);
        h.Locator.Replace(h.Target.Process);

        OperationResult<PhotoshopTarget> opened = await h.Driver.OpenManagedDocumentAsync(
            h.Target, PhotoshopFakes.ExpectedPath, CancellationToken.None);

        opened.IsFailure.ShouldBeTrue();
        opened.Failure.Code.ShouldBe(FailureCode.PhotoshopWindowNotFound);
        h.Input.Sends.ShouldBeEmpty();
    }

    /// <summary>Ownership lost mid-sequence stops before the confirm.</summary>
    [Fact]
    public async Task Ownership_lost_before_the_write_stops_before_the_confirm()
    {
        Harness h = Build();
        StageOpenDialog(h);
        h.Controls.LostProcessIds.Add(h.Target.Process.ProcessId);

        OperationResult<PhotoshopTarget> opened = await h.Driver.OpenManagedDocumentAsync(
            h.Target, PhotoshopFakes.ExpectedPath, CancellationToken.None);

        opened.IsFailure.ShouldBeTrue();
        opened.Failure.Code.ShouldBe(FailureCode.PhotoshopTargetLost);
        h.Controls.Writes.ShouldBeEmpty();
        h.Controls.Presses.ShouldBeEmpty();
    }

    // -----------------------------------------------------------------------------------
    // Close (§21)
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// Closing re-proves identity first, and closes only the exact expected document.
    /// </summary>
    [Fact]
    public async Task Only_the_positively_identified_document_is_closed()
    {
        Harness h = Build(windowTitle: PhotoshopFakes.TitleFor(PhotoshopFakes.ExpectedFileName));
        StageIdentityDialog(h, PhotoshopFakes.ExpectedFileName, PhotoshopFakes.WorkingDirectory);

        // The window title stops naming the document as it closes, which is how the wait ends.
        OnShortcut(
            h,
            KnownShortcut.CloseActiveDocument,
            () => h.Locator.Replace(h.Target.Process, PhotoshopFakes.Window()));

        List<ReadinessProbeStage> progress = [];
        OperationResult<PhotoshopTarget> closed = await h.Driver.CloseExactDocumentAsync(
            h.Target, PhotoshopFakes.ExpectedPath, progress.Add, CancellationToken.None);

        closed.IsSuccess.ShouldBeTrue();
        h.Input.Sends.ShouldContain(s => s.Shortcut == KnownShortcut.CloseActiveDocument);
        closed.Value.Window.Title.ShouldBe(PhotoshopFakes.NoDocumentTitle);
        progress.ShouldBe(new[] { ReadinessProbeStage.CloseRequested, ReadinessProbeStage.CloseConfirmed });
    }

    /// <summary>
    /// An operator's own document is never closed, even when PrintFlow was asked to close.
    /// </summary>
    /// <remarks>
    /// The shared-process rule in its sharpest form. Photoshop may be holding work PrintFlow
    /// knows nothing about; closing the active document without re-proving which one it is would
    /// discard someone else's unsaved work on the strength of an identity established earlier.
    /// </remarks>
    [Fact]
    public async Task A_document_that_is_not_the_expected_one_is_never_closed()
    {
        Harness h = Build(windowTitle: PhotoshopFakes.TitleFor("SOMEONE-ELSES-WORK.psd"));
        StageIdentityDialog(h, "SOMEONE-ELSES-WORK.psd", @"C:\Users\admin\Desktop");

        List<ReadinessProbeStage> progress = [];
        OperationResult<PhotoshopTarget> closed = await h.Driver.CloseExactDocumentAsync(
            h.Target, PhotoshopFakes.ExpectedPath, progress.Add, CancellationToken.None);

        closed.IsFailure.ShouldBeTrue();
        closed.Failure.Code.ShouldBe(FailureCode.PhotoshopDocumentIdentityUnconfirmed);
        closed.Failure.Context["inputSent"].ShouldBe("false");
        progress.ShouldBeEmpty("entering the close helper is not a close request");

        h.Input.Sends.ShouldNotContain(s => s.Shortcut == KnownShortcut.CloseActiveDocument);
    }

    [Fact]
    public async Task Same_filename_in_the_wrong_directory_is_never_closed()
    {
        Harness h = Build(windowTitle: PhotoshopFakes.TitleFor(PhotoshopFakes.ExpectedFileName));
        StageIdentityDialog(h, PhotoshopFakes.ExpectedFileName, @"C:\SomeoneElse\Working");

        OperationResult<PhotoshopTarget> closed = await h.Driver.CloseExactDocumentAsync(
            h.Target, PhotoshopFakes.ExpectedPath, CancellationToken.None);

        closed.IsFailure.ShouldBeTrue();
        closed.Failure.Code.ShouldBe(FailureCode.PhotoshopDocumentIdentityUnconfirmed);
        h.Input.Sends.ShouldNotContain(s => s.Shortcut == KnownShortcut.CloseActiveDocument);
        h.Controls.Presses.ShouldNotContain(press => press.ControlId == 11);
    }

    [Fact]
    public async Task Dirty_owned_document_uses_the_signed_discard_control_exactly_once()
    {
        Harness h = Build(windowTitle: PhotoshopFakes.TitleFor(PhotoshopFakes.ExpectedFileName) + " *");
        StageIdentityDialog(h, "PFTEST-A-0001_WORKING.tif", PhotoshopFakes.WorkingDirectory);
        StageDiscardPrompt(h);

        OperationResult<PhotoshopTarget> closed = await h.Driver.CloseExactDocumentAsync(
            h.Target, PhotoshopFakes.ExpectedPath, CancellationToken.None);

        closed.IsSuccess.ShouldBeTrue(closed.IsFailure ? closed.Failure.ToString() : string.Empty);
        h.Input.Sends.Count(send => send.Shortcut == KnownShortcut.CloseActiveDocument).ShouldBe(1);
        h.Controls.Presses.Count(press => press.ControlId == 11).ShouldBe(1);
        h.Controls.Presses.ShouldNotContain(press => press.ControlId == 10 || press.ControlId == 12);
    }

    [Fact]
    public async Task A_pre_existing_exact_prompt_is_never_claimed_or_dismissed()
    {
        Harness h = Build(windowTitle: PhotoshopFakes.TitleFor(PhotoshopFakes.ExpectedFileName) + " *");
        ExternalWindowRef prompt = PhotoshopFakes.Dialog(
            handle: 0xD15CA, title: "Adobe Photoshop", className: "PSDialogBox");
        h.Locator.OwnedDialogs.Add(prompt);
        h.Locator.Replace(h.Target.Process, h.Target.Window with { IsEnabled = false });

        OperationResult<PhotoshopTarget> closed = await h.Driver.CloseExactDocumentAsync(
            h.Target, PhotoshopFakes.ExpectedPath, CancellationToken.None);

        closed.IsFailure.ShouldBeTrue();
        closed.Failure.Code.ShouldBe(FailureCode.PhotoshopBlockingDialog);
        closed.Failure.Context["preExistingDialog"].ShouldBe("true");
        h.Input.Sends.ShouldBeEmpty();
        h.Controls.Presses.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("PSExport_WindowClass", "存储为 Web 所用格式 (100%)")]
    [InlineData("#32770", "另存为")]
    [InlineData("PSDialogBox", "任意 Photoshop 错误")]
    public async Task A_non_discard_surface_after_close_is_left_untouched(
        string className, string title)
    {
        Harness h = Build(windowTitle: PhotoshopFakes.TitleFor(PhotoshopFakes.ExpectedFileName) + " *");
        StageIdentityDialog(h, PhotoshopFakes.ExpectedFileName, PhotoshopFakes.WorkingDirectory);
        ExternalWindowRef unknown = PhotoshopFakes.Dialog(
            handle: 0xBAD10, title: title, className: className);
        OnShortcut(h, KnownShortcut.CloseActiveDocument, () =>
        {
            h.Locator.OwnedDialogs.Add(unknown);
            h.Locator.PutInForeground(unknown);
        });

        OperationResult<PhotoshopTarget> closed = await h.Driver.CloseExactDocumentAsync(
            h.Target, PhotoshopFakes.ExpectedPath, CancellationToken.None);

        closed.IsFailure.ShouldBeTrue();
        closed.Failure.Code.ShouldBe(FailureCode.PhotoshopBlockingDialog);
        h.Controls.Presses.ShouldNotContain(press => press.ControlId == 11);
    }

    [Theory]
    [InlineData(false, "否(&N)")]
    [InlineData(true, "不要保存")]
    public async Task Missing_or_altered_discard_control_is_left_untouched(
        bool includeDiscard, string discardText)
    {
        Harness h = Build(windowTitle: PhotoshopFakes.TitleFor(PhotoshopFakes.ExpectedFileName) + " *");
        StageIdentityDialog(h, PhotoshopFakes.ExpectedFileName, PhotoshopFakes.WorkingDirectory);
        StageDiscardPrompt(h, includeDiscard: includeDiscard, discardText: discardText);

        OperationResult<PhotoshopTarget> closed = await h.Driver.CloseExactDocumentAsync(
            h.Target, PhotoshopFakes.ExpectedPath, CancellationToken.None);

        closed.IsFailure.ShouldBeTrue();
        h.Controls.Presses.ShouldNotContain(press => press.ControlId == 11);
    }

    [Fact]
    public async Task Foreground_loss_before_discard_invocation_leaves_the_prompt_untouched()
    {
        Harness h = Build(windowTitle: PhotoshopFakes.TitleFor(PhotoshopFakes.ExpectedFileName) + " *");
        StageIdentityDialog(h, PhotoshopFakes.ExpectedFileName, PhotoshopFakes.WorkingDirectory);
        StageDiscardPrompt(h);
        OnShortcut(h, KnownShortcut.CloseActiveDocument, () =>
            h.Locator.Foreground = new ForegroundIdentity(new WindowHandle(0xDEAD), 999, "explorer"));

        OperationResult<PhotoshopTarget> closed = await h.Driver.CloseExactDocumentAsync(
            h.Target, PhotoshopFakes.ExpectedPath, CancellationToken.None);

        closed.IsFailure.ShouldBeTrue();
        closed.Failure.Code.ShouldBe(FailureCode.PhotoshopTargetLost);
        h.Controls.Presses.ShouldNotContain(press => press.ControlId == 11);
    }

    [Fact]
    public async Task A_dirty_close_with_no_prompt_and_no_closed_document_times_out_without_discard()
    {
        Harness h = Build(windowTitle: PhotoshopFakes.TitleFor(PhotoshopFakes.ExpectedFileName) + " *");
        StageIdentityDialog(h, PhotoshopFakes.ExpectedFileName, PhotoshopFakes.WorkingDirectory);

        OperationResult<PhotoshopTarget> closed = await h.Driver.CloseExactDocumentAsync(
            h.Target, PhotoshopFakes.ExpectedPath, CancellationToken.None);

        closed.IsFailure.ShouldBeTrue();
        closed.Failure.Code.ShouldBe(FailureCode.Timeout);
        h.Input.Sends.Count(send => send.Shortcut == KnownShortcut.CloseActiveDocument).ShouldBe(1);
        h.Controls.Presses.ShouldNotContain(press => press.ControlId == 11);
    }

    [Fact]
    public async Task A_prompt_question_for_another_document_is_never_discarded()
    {
        Harness h = Build(windowTitle: PhotoshopFakes.TitleFor(PhotoshopFakes.ExpectedFileName) + " *");
        StageIdentityDialog(h, PhotoshopFakes.ExpectedFileName, PhotoshopFakes.WorkingDirectory);
        StageDiscardPrompt(h, questionFileName: "SOMEONE-ELSES-WORK.psd");

        OperationResult<PhotoshopTarget> closed = await h.Driver.CloseExactDocumentAsync(
            h.Target, PhotoshopFakes.ExpectedPath, CancellationToken.None);

        closed.IsFailure.ShouldBeTrue();
        closed.Failure.Code.ShouldBe(FailureCode.PhotoshopBlockingDialog);
        h.Controls.Presses.ShouldNotContain(press => press.ControlId == 11);
    }

    [Fact]
    public async Task A_recognised_discard_control_that_refuses_invocation_is_reported_without_success()
    {
        Harness h = Build(windowTitle: PhotoshopFakes.TitleFor(PhotoshopFakes.ExpectedFileName) + " *");
        StageIdentityDialog(h, PhotoshopFakes.ExpectedFileName, PhotoshopFakes.WorkingDirectory);
        StageDiscardPrompt(h);
        h.Controls.PressFailures.Add(11);

        OperationResult<PhotoshopTarget> closed = await h.Driver.CloseExactDocumentAsync(
            h.Target, PhotoshopFakes.ExpectedPath, CancellationToken.None);

        closed.IsFailure.ShouldBeTrue();
        closed.Failure.Code.ShouldBe(FailureCode.PhotoshopUnknownState);
        h.Controls.Presses.ShouldNotContain(press => press.ControlId == 11,
            "the recorder contains completed invocations only, and this one was refused");
    }

    [Fact]
    public async Task A_prompt_remaining_after_discard_is_a_bounded_failure()
    {
        Harness h = Build(windowTitle: PhotoshopFakes.TitleFor(PhotoshopFakes.ExpectedFileName) + " *");
        StageIdentityDialog(h, PhotoshopFakes.ExpectedFileName, PhotoshopFakes.WorkingDirectory);
        StageDiscardPrompt(h, removePromptAfterDiscard: false);

        OperationResult<PhotoshopTarget> closed = await h.Driver.CloseExactDocumentAsync(
            h.Target, PhotoshopFakes.ExpectedPath, CancellationToken.None);

        closed.IsFailure.ShouldBeTrue();
        closed.Failure.Code.ShouldBe(FailureCode.PhotoshopBlockingDialog);
        h.Controls.Presses.Count(press => press.ControlId == 11).ShouldBe(1);
    }

    [Fact]
    public async Task A_main_frame_that_remains_disabled_after_discard_is_not_cleanup_success()
    {
        Harness h = Build(windowTitle: PhotoshopFakes.TitleFor(PhotoshopFakes.ExpectedFileName) + " *");
        StageIdentityDialog(h, PhotoshopFakes.ExpectedFileName, PhotoshopFakes.WorkingDirectory);
        StageDiscardPrompt(h, reEnableMainFrame: false);

        OperationResult<PhotoshopTarget> closed = await h.Driver.CloseExactDocumentAsync(
            h.Target, PhotoshopFakes.ExpectedPath, CancellationToken.None);

        closed.IsFailure.ShouldBeTrue("cleanup is incomplete while Photoshop's main frame is disabled");
        h.Controls.Presses.Count(press => press.ControlId == 11).ShouldBe(1);
    }

    [Fact]
    public async Task A_discard_that_leaves_the_expected_document_active_is_not_cleanup_success()
    {
        Harness h = Build(windowTitle: PhotoshopFakes.TitleFor(PhotoshopFakes.ExpectedFileName) + " *");
        StageIdentityDialog(h, PhotoshopFakes.ExpectedFileName, PhotoshopFakes.WorkingDirectory);
        StageDiscardPrompt(
            h, titleAfterDiscard: PhotoshopFakes.TitleFor(PhotoshopFakes.ExpectedFileName));

        OperationResult<PhotoshopTarget> closed = await h.Driver.CloseExactDocumentAsync(
            h.Target, PhotoshopFakes.ExpectedPath, CancellationToken.None);

        closed.IsFailure.ShouldBeTrue();
        closed.Failure.Code.ShouldBe(FailureCode.Timeout);
        h.Controls.Presses.Count(press => press.ControlId == 11).ShouldBe(1);
    }

    [Fact]
    public async Task Same_name_previous_document_is_probed_by_path_and_never_closed_a_second_time()
    {
        string previousFolder = @"C:\Fake\PrintFlowStudio\Sessions\Earlier\Working";
        string sameTitle = PhotoshopFakes.TitleFor(PhotoshopFakes.ExpectedFileName) + " *";
        Harness h = Build(windowTitle: sameTitle);
        ExternalWindowRef identityDialog = StageIdentityDialog(
            h, PhotoshopFakes.ExpectedFileName, PhotoshopFakes.WorkingDirectory);
        StageDiscardPrompt(h, titleAfterDiscard: sameTitle);

        Action<nint, int>? previous = h.Controls.OnPress;
        h.Controls.OnPress = (host, controlId) =>
        {
            previous?.Invoke(host, controlId);
            if (controlId == 11)
            {
                h.Controls.ClearControls(identityDialog.Handle);
                h.Controls.AddControl(identityDialog.Handle, 1001, "Edit", PhotoshopFakes.ExpectedFileName);
                h.Controls.AddControl(identityDialog.Handle, 1001, "ToolbarWindow32", $"地址: {previousFolder}");
                h.Controls.AddControl(identityDialog.Handle, 2, "Button", "取消");
            }
        };

        OperationResult<PhotoshopTarget> closed = await h.Driver.CloseExactDocumentAsync(
            h.Target, PhotoshopFakes.ExpectedPath, CancellationToken.None);

        closed.IsSuccess.ShouldBeTrue(closed.IsFailure ? closed.Failure.ToString() : string.Empty);
        h.Input.Sends.Count(send => send.Shortcut == KnownShortcut.CloseActiveDocument).ShouldBe(1);
        h.Controls.Presses.Count(press => press.ControlId == 11).ShouldBe(1);
        closed.Value.Window.Title.ShouldContain(PhotoshopFakes.ExpectedFileName);
    }

    /// <summary>
    /// A title that changed without the document going away is not a close
    /// (Epic 11600 Part B §10).
    /// </summary>
    /// <remarks>
    /// The regression this exists for was found on the workstation, not here. The wait used to
    /// end when the window title stopped being <i>equal</i> to the title recorded during the
    /// identity probe, and Photoshop's title carries more than the document's name — it also
    /// carries the unsaved-changes marker. So a document that merely stopped being dirty produced
    /// a different title and was reported as closed while it was still loaded: nine consecutive
    /// calls returned success with the open-document count unmoved and the same file still named
    /// in the title.
    /// <para>
    /// Here the document stays exactly where it is and only the dirty marker moves, which is the
    /// smallest possible version of that change. Success would be a lie; a timeout saying the
    /// document may still be loaded is the truth, and it is the direction that fails safe.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_document_that_only_stopped_being_dirty_is_not_reported_as_closed()
    {
        string dirty = PhotoshopFakes.TitleFor(PhotoshopFakes.ExpectedFileName) + " *";
        Harness h = Build(windowTitle: dirty);
        StageIdentityDialog(h, PhotoshopFakes.ExpectedFileName, PhotoshopFakes.WorkingDirectory);

        // Ctrl+W arrives, and all that changes is the marker: the same document, still loaded.
        OnShortcut(
            h,
            KnownShortcut.CloseActiveDocument,
            () => h.Locator.Replace(
                h.Target.Process,
                PhotoshopFakes.Window(title: PhotoshopFakes.TitleFor(PhotoshopFakes.ExpectedFileName))));

        List<ReadinessProbeStage> progress = [];
        OperationResult<PhotoshopTarget> closed = await h.Driver.CloseExactDocumentAsync(
            h.Target, PhotoshopFakes.ExpectedPath, progress.Add, CancellationToken.None);

        closed.IsFailure.ShouldBeTrue("the document is still loaded, so the close did not finish.");
        closed.Failure.Code.ShouldBe(FailureCode.Timeout);
        closed.Failure.TechnicalDetail.ShouldContain("still shows the document");
        progress.ShouldBe(new[] { ReadinessProbeStage.CloseRequested });
    }

    /// <summary>
    /// A different document coming to the front <i>is</i> a close (Epic 11600 Part B §10).
    /// </summary>
    /// <remarks>
    /// The other half of the same rule, and the reason the fix is a name comparison rather than
    /// "wait for the no-document title": in sustained use the next thing in front is usually the
    /// previous job's document, not an empty editor.
    /// </remarks>
    [Fact]
    public async Task The_previous_document_coming_to_the_front_completes_the_close()
    {
        Harness h = Build(windowTitle: PhotoshopFakes.TitleFor(PhotoshopFakes.ExpectedFileName) + " *");
        StageIdentityDialog(h, PhotoshopFakes.ExpectedFileName, PhotoshopFakes.WorkingDirectory);

        OnShortcut(
            h,
            KnownShortcut.CloseActiveDocument,
            () => h.Locator.Replace(
                h.Target.Process,
                PhotoshopFakes.Window(title: PhotoshopFakes.TitleFor("PFTEST-EARLIER_WORKING.png") + " *")));

        OperationResult<PhotoshopTarget> closed = await h.Driver.CloseExactDocumentAsync(
            h.Target, PhotoshopFakes.ExpectedPath, CancellationToken.None);

        closed.IsSuccess.ShouldBeTrue(closed.IsFailure ? closed.Failure.ToString() : "");
        closed.Value.Window.Title.ShouldContain("PFTEST-EARLIER_WORKING.png");
    }

    // -----------------------------------------------------------------------------------
    // State observation (§7)
    // -----------------------------------------------------------------------------------

    [Fact]
    public async Task The_start_screen_is_observed_and_classified_without_any_input()
    {
        Harness h = Build();
        h.Controls.SetVisibleClasses(
            h.Target.Window.Handle,
            [.. PhotoshopFakes.EditorChromeClasses, PhotoshopFakes.StartScreenClass]);

        OperationResult<PhotoshopStateSnapshot> state =
            await h.Driver.InspectStateAsync(h.Target, null, CancellationToken.None);

        state.IsSuccess.ShouldBeTrue();
        state.Value.State.ShouldBe(PhotoshopStartingState.KnownStartScreen);

        h.Input.Sends.ShouldBeEmpty();
        h.Controls.Writes.ShouldBeEmpty();
        h.Controls.Presses.ShouldBeEmpty();
    }

    /// <summary>An owned dialog already up when PrintFlow looks is a modal it will not touch.</summary>
    [Fact]
    public async Task A_dialog_already_open_classifies_as_a_blocking_modal()
    {
        Harness h = Build();
        h.Controls.SetVisibleClasses(
            h.Target.Window.Handle,
            [.. PhotoshopFakes.EditorChromeClasses, PhotoshopFakes.StartScreenClass]);
        h.Locator.OwnedDialogs.Add(PhotoshopFakes.Dialog(title: "颜色设置"));

        OperationResult<PhotoshopStateSnapshot> state =
            await h.Driver.InspectStateAsync(h.Target, null, CancellationToken.None);

        state.Value.State.ShouldBe(PhotoshopStartingState.KnownModal);
        state.Value.IsSafeStartingState.ShouldBeFalse();
        h.Input.Sends.ShouldBeEmpty();
    }
}
