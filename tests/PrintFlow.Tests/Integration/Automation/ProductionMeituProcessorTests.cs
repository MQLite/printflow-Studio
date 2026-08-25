using System.IO;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Adapters.Meitu;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Ports;
using FileWorkspace = PrintFlow.Infrastructure.Workspace.FileWorkspace;

namespace PrintFlow.Tests.Integration.Automation;

/// <summary>
/// The production adapter's starting-state, launch, reuse and working-copy rules
/// (Epic 11300 Part A §13–§16, §24, §25).
/// </summary>
/// <remarks>
/// The adapter under test is the real one; only the operating system is faked. Its executable
/// identity check reads a real file from a temp directory and hashes it, so "the accepted binary
/// is what is actually there" is exercised rather than stubbed out.
/// </remarks>
public sealed class ProductionMeituProcessorTests : IDisposable
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

    private readonly TempWorkspace _workspace = new();
    private readonly string _executablePath;
    private readonly Sha256 _executableSha256;

    public ProductionMeituProcessorTests()
    {
        // A real file on disk standing in for the accepted binary: the adapter hashes whatever
        // is at the configured path, so an identity check that only compared strings would fail
        // the mismatch test below.
        _executablePath = Path.Combine(_workspace.Root, "XiuXiu.exe");
        byte[] bytes = [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x02, 0x01];
        File.WriteAllBytes(_executablePath, bytes);
        _executableSha256 = Sha256.FromBytes(System.Security.Cryptography.SHA256.HashData(bytes));
    }

    public void Dispose() => _workspace.Dispose();

    private MeituBaseline Baseline() => MeituFakes.Baseline() with
    {
        ExecutablePath = _executablePath,
        ExecutableSha256 = _executableSha256,
    };

    /// <summary>
    /// A fake running process launched from the same path the baseline accepts.
    /// </summary>
    /// <remarks>
    /// The adapter matches candidate processes by executable path, so a fake whose path did not
    /// match the baseline would silently look like "Meitu is not running" — which would make the
    /// reuse tests pass for the wrong reason.
    /// </remarks>
    private ExternalProcessRef Process(int id = 4242) =>
        MeituFakes.Process(id) with { ExecutablePath = _executablePath };

    private sealed record Harness(
        ProductionMeituProcessor Adapter,
        FakeWindowLocator Locator,
        RecordingUiElementProvider Elements,
        RecordingInputSink Input,
        RecordingEvidenceSink Evidence,
        IWorkspace Workspace);

    private Harness Build(MeituBaseline? baseline = null)
    {
        FakeWindowLocator locator = new();
        RecordingUiElementProvider elements = new();
        RecordingInputSink input = new();
        RecordingEvidenceSink evidence = new();
        StubMeituBaselineProvider baselines = new(baseline ?? Baseline());

        GuardedMeituUiDriver driver = new(
            locator, elements, input, evidence, baselines, FastOptions, TimeProvider.System);

        IWorkspace workspace = new FileWorkspace(_workspace.Root);
        ProductionMeituProcessor adapter = new(
            baselines, locator, driver, workspace, new WicFileInspector(),
            new WicMeituTransparencyInspector(),
            new FileSystemMeituOutputProbe(), FastOptions, TimeProvider.System);

        return new Harness(adapter, locator, elements, input, evidence, workspace);
    }

    private static void ShowWelcomePage(RecordingUiElementProvider elements, ExternalWindowRef window) =>
        elements.SetTexts(window.Handle, [.. MeituFakes.WelcomeMarkers]);

    // -----------------------------------------------------------------------------
    // The workflow seam never succeeds in this slice (§24)
    // -----------------------------------------------------------------------------

    /// <summary>
    /// Background removal is still refused before Meitu is touched at all
    /// (Epic 11300 Part B2B §27).
    /// </summary>
    /// <remarks>
    /// Enhancement can now succeed, which makes this the assertion that keeps the slice a slice.
    /// The refusal is unconditional and comes first, so an operation this adapter has no signed
    /// route for cannot reach a window, a control or a file — and the failure names the adapter,
    /// so a Revision that should never exist has an operator-readable reason for not existing.
    /// </remarks>
    [Fact]
    public async Task Unspecified_background_removal_is_refused_before_Meitu_interaction()
    {
        Harness h = Build();
        WorkspaceDirRef sessionDir = WorkspaceDirRef.Create("Sessions/S_1");
        WorkspaceFileRef input = WorkspaceFileRef.Create("Sessions/S_1/Working/A_1/a.png", WorkspaceArea.Working);
        WorkspaceFileRef output = WorkspaceFileRef.Create(
            "Sessions/S_1/Working/A_1/a_CUTOUT.png", WorkspaceArea.Working);

        OperationResult<AdapterOutput> result = await h.Adapter.ProcessAsync(
            new MeituRequest(
                input, MeituOperation.RemoveBackground, BackgroundRemovalDecision.Unspecified,
                sessionDir, output),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);
        result.Failure.IsRetryable.ShouldBeFalse();
        result.Failure.Context["adapterId"].ShouldBe("meitu-xiuxiu-production-v1");
        result.Failure.Context["decision"].ShouldBe("Unspecified");
        result.Failure.Context["inputSent"].ShouldBe("false");

        // Refused before anything at all: no window was looked at, nothing was invoked.
        h.Elements.Invocations.ShouldBeEmpty();
        h.Input.Sends.ShouldBeEmpty();
    }

    /// <summary>
    /// An Enhancement whose expected output is its own input is refused before Meitu is touched
    /// (Epic 11300 Part B2B §19).
    /// </summary>
    /// <remarks>
    /// This is the exact request shape the workflow used to send, and the reason it had to
    /// change. Exporting over the working copy would destroy the bytes the attempt validates its
    /// result against — and it would do it silently, because the file would still exist, still
    /// be a readable PNG, and still hash to something.
    /// </remarks>
    [Fact]
    public async Task Enhancement_refuses_to_export_over_its_own_working_copy()
    {
        Harness h = Build();
        WorkspaceDirRef sessionDir = WorkspaceDirRef.Create("Sessions/S_1");
        WorkspaceFileRef file = WorkspaceFileRef.Create("Sessions/S_1/Working/A_1/a.png", WorkspaceArea.Working);

        OperationResult<AdapterOutput> result = await h.Adapter.ProcessAsync(
            new MeituRequest(
                file, MeituOperation.Enhance, BackgroundRemovalDecision.Unspecified, sessionDir, file),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);
        h.Elements.Invocations.ShouldBeEmpty();
        h.Input.Sends.ShouldBeEmpty();
    }

    /// <summary>
    /// A result may only be written into the Working area (Epic 11300 Part B2B §7).
    /// </summary>
    [Theory]
    [InlineData(WorkspaceArea.Source)]
    [InlineData(WorkspaceArea.Approved)]
    [InlineData(WorkspaceArea.Rejected)]
    public async Task Enhancement_refuses_to_export_outside_the_Working_area(WorkspaceArea area)
    {
        Harness h = Build();
        WorkspaceDirRef sessionDir = WorkspaceDirRef.Create("Sessions/S_1");
        WorkspaceFileRef input = WorkspaceFileRef.Create("Sessions/S_1/Working/A_1/a.png", WorkspaceArea.Working);
        WorkspaceFileRef output = WorkspaceFileRef.Create($"Sessions/S_1/{area}/a_HD.png", area);

        OperationResult<AdapterOutput> result = await h.Adapter.ProcessAsync(
            new MeituRequest(
                input, MeituOperation.Enhance, BackgroundRemovalDecision.Unspecified, sessionDir, output),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);
        h.Elements.Invocations.ShouldBeEmpty();
    }

    [Fact]
    public void The_production_adapter_declares_Production_mode()
    {
        Build().Adapter.Mode.ShouldBe(AdapterExecutionMode.Production);
    }

    // -----------------------------------------------------------------------------
    // Executable identity (§7)
    // -----------------------------------------------------------------------------

    [Fact]
    public async Task A_missing_accepted_executable_fails_closed()
    {
        Harness h = Build(Baseline() with { ExecutablePath = Path.Combine(_workspace.Root, "absent.exe") });

        OperationResult<MeituReadiness> ready = await h.Adapter.EnsureReadyAsync(CancellationToken.None);

        ready.IsFailure.ShouldBeTrue();
        ready.Failure.Code.ShouldBe(FailureCode.MeituNotInstalled);
        h.Locator.LaunchCount.ShouldBe(0);
    }

    /// <summary>
    /// A binary at the accepted path whose hash has moved is not the accepted binary.
    /// </summary>
    /// <remarks>
    /// This is what makes a silent Meitu auto-update stop automation rather than run it against
    /// an unvalidated UI. Epic 11000 allows upgrades — it requires revalidation first, and this
    /// is where that requirement becomes enforceable.
    /// </remarks>
    [Fact]
    public async Task An_executable_whose_hash_has_changed_fails_closed()
    {
        Harness h = Build(Baseline() with
        {
            ExecutableSha256 = Sha256.Parse(
                "0000000000000000000000000000000000000000000000000000000000000000"),
        });

        OperationResult<MeituReadiness> ready = await h.Adapter.EnsureReadyAsync(CancellationToken.None);

        ready.IsFailure.ShouldBeTrue();
        ready.Failure.Code.ShouldBe(FailureCode.MeituNotInstalled);
        ready.Failure.Context.ShouldContainKey("actualSha256");
        h.Locator.LaunchCount.ShouldBe(0);
    }

    // -----------------------------------------------------------------------------
    // Existing instance (§15)
    // -----------------------------------------------------------------------------

    [Fact]
    public async Task An_existing_instance_on_a_safe_state_is_reused_without_launching_another()
    {
        Harness h = Build();
        ExternalProcessRef process = Process();
        ExternalWindowRef window = MeituFakes.Window(owningProcessId: process.ProcessId);
        h.Locator.Register(process, window);
        ShowWelcomePage(h.Elements, window);

        OperationResult<MeituReadiness> ready = await h.Adapter.EnsureReadyAsync(CancellationToken.None);

        ready.IsSuccess.ShouldBeTrue();
        ready.Value.State.State.ShouldBe(MeituStartingState.KnownWelcome);
        ready.Value.WasLaunched.ShouldBeFalse();
        h.Locator.LaunchCount.ShouldBe(0);
    }

    [Fact]
    public async Task Two_candidate_processes_are_never_chosen_between()
    {
        Harness h = Build();
        h.Locator.Register(Process(id: 1), MeituFakes.Window(handle: 0x11, owningProcessId: 1));
        h.Locator.Register(Process(id: 2), MeituFakes.Window(handle: 0x22, owningProcessId: 2));

        OperationResult<MeituReadiness> ready = await h.Adapter.EnsureReadyAsync(CancellationToken.None);

        ready.IsFailure.ShouldBeTrue();
        ready.Failure.Code.ShouldBe(FailureCode.MeituUnknownState);
        h.Locator.LaunchCount.ShouldBe(0);
    }

    // -----------------------------------------------------------------------------
    // Unsafe starting states (§13)
    // -----------------------------------------------------------------------------

    [Fact]
    public async Task An_unrecognised_screen_stops_without_any_interaction()
    {
        Harness h = Build();
        ExternalProcessRef process = Process();
        ExternalWindowRef window = MeituFakes.Window(owningProcessId: process.ProcessId, title: "美图秀秀-图片编辑");
        h.Locator.Register(process, window);
        h.Elements.SetTexts(window.Handle, "某个未知界面");

        OperationResult<MeituReadiness> ready = await h.Adapter.EnsureReadyAsync(CancellationToken.None);

        ready.IsFailure.ShouldBeTrue();
        ready.Failure.Code.ShouldBe(FailureCode.MeituUnknownState);
        h.Input.Sends.ShouldBeEmpty();
        h.Elements.Invocations.ShouldBeEmpty();
        h.Evidence.Captures.ShouldHaveSingleItem();
        ready.Failure.Context["evidencePath"].ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// An unknown dialog is reported, never dismissed.
    /// </summary>
    /// <remarks>
    /// MVP design §11.4 is unambiguous: PrintFlow does not close unknown documents or dialogs.
    /// The assertion that carries that is <c>Invocations</c> being empty — a "helpful" adapter
    /// that pressed 取消 to get on with the job would fail here.
    /// </remarks>
    [Fact]
    public async Task A_blocking_dialog_is_reported_and_never_dismissed()
    {
        Harness h = Build();
        ExternalProcessRef process = Process();
        ExternalWindowRef window = MeituFakes.Window(owningProcessId: process.ProcessId, enabled: false);
        h.Locator.Register(process, window);
        h.Locator.OwnedDialogs.Add(MeituFakes.Window(
            handle: 0x9000, owningProcessId: process.ProcessId, title: "更新提示", className: "#32770"));
        ShowWelcomePage(h.Elements, window);

        OperationResult<MeituReadiness> ready = await h.Adapter.EnsureReadyAsync(CancellationToken.None);

        ready.IsFailure.ShouldBeTrue();
        ready.Failure.Code.ShouldBe(FailureCode.MeituBlockingDialog);
        ready.Failure.Context["dialogTitles"].ShouldContain("更新提示");
        h.Elements.Invocations.ShouldBeEmpty();
        h.Input.Sends.ShouldBeEmpty();
    }

    /// <summary>A failed screenshot must not become the reported problem.</summary>
    [Fact]
    public async Task A_failed_evidence_capture_does_not_replace_the_real_failure()
    {
        Harness h = Build();
        h.Evidence.Fails = true;
        ExternalProcessRef process = Process();
        ExternalWindowRef window = MeituFakes.Window(owningProcessId: process.ProcessId);
        h.Locator.Register(process, window);
        h.Elements.SetTexts(window.Handle, "unrecognisable");

        OperationResult<MeituReadiness> ready = await h.Adapter.EnsureReadyAsync(CancellationToken.None);

        ready.Failure.Code.ShouldBe(FailureCode.MeituUnknownState);
        ready.Failure.Context.ShouldNotContainKey("evidencePath");
    }

    // -----------------------------------------------------------------------------
    // Launch (§14)
    // -----------------------------------------------------------------------------

    [Fact]
    public async Task A_launch_that_never_presents_a_window_fails_on_a_bounded_timeout()
    {
        Harness h = Build();
        h.Locator.LaunchResult = Process(id: 777);
        h.Locator.LaunchedProcessNeverShowsWindow = true;

        OperationResult<MeituReadiness> ready = await h.Adapter.EnsureReadyAsync(CancellationToken.None);

        ready.IsFailure.ShouldBeTrue();
        ready.Failure.Code.ShouldBe(FailureCode.MeituWindowNotFound);
        h.Locator.LaunchCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_launch_that_reaches_the_welcome_page_succeeds_and_reports_that_PrintFlow_started_it()
    {
        Harness h = Build();
        ExternalProcessRef launched = Process(id: 777);
        h.Locator.LaunchResult = launched;

        // The fake OS gives a launched process the default window handle; script that window to
        // show the signed welcome markers.
        ShowWelcomePage(h.Elements, MeituFakes.Window(owningProcessId: launched.ProcessId));

        OperationResult<MeituReadiness> ready = await h.Adapter.EnsureReadyAsync(CancellationToken.None);

        ready.IsSuccess.ShouldBeTrue();
        ready.Value.WasLaunched.ShouldBeTrue();
        ready.Value.State.State.ShouldBe(MeituStartingState.KnownWelcome);
    }

    [Fact]
    public async Task Cancellation_during_a_launch_wait_exits_without_interaction()
    {
        Harness h = Build();
        h.Locator.LaunchResult = Process(id: 777);
        h.Locator.LaunchedProcessNeverShowsWindow = true;

        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            h.Adapter.EnsureReadyAsync(cancelled.Token));

        h.Input.Sends.ShouldBeEmpty();
        h.Elements.Invocations.ShouldBeEmpty();
    }

    // -----------------------------------------------------------------------------
    // Working-copy boundary (§16)
    // -----------------------------------------------------------------------------

    /// <summary>
    /// Only a Working reference may be handed to Meitu.
    /// </summary>
    /// <remarks>
    /// Refused before the reference is even resolved to a path, so the customer's original is
    /// not merely "not opened" — its absolute path is never computed inside the adapter at all.
    /// </remarks>
    [Theory]
    [InlineData(WorkspaceArea.Source)]
    [InlineData(WorkspaceArea.Approved)]
    [InlineData(WorkspaceArea.Rejected)]
    [InlineData(WorkspaceArea.Logs)]
    public async Task Only_a_Working_copy_may_be_handed_to_Meitu(WorkspaceArea area)
    {
        Harness h = Build();
        ExternalProcessRef process = Process();
        ExternalWindowRef window = MeituFakes.Window(owningProcessId: process.ProcessId);
        h.Locator.Register(process, window);
        ShowWelcomePage(h.Elements, window);

        WorkspaceFileRef notWorking = WorkspaceFileRef.Create("Sessions/S_1/Any/customer.png", area);

        OperationResult<MeituOpenedWorkingCopy> opened =
            await h.Adapter.OpenWorkingCopyAsync(notWorking, CancellationToken.None);

        opened.IsFailure.ShouldBeTrue();
        opened.Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);
        opened.Failure.TechnicalDetail.ShouldContain("Working copy");

        // Refused ahead of everything: no process was looked for, no window activated, nothing sent.
        h.Locator.ActivationRequests.ShouldBe(0);
        h.Input.Sends.ShouldBeEmpty();
        h.Elements.Invocations.ShouldBeEmpty();
    }

    /// <summary>
    /// Opening is confirmed by seeing PrintFlow's own file name in Meitu, not by a dialog closing.
    /// </summary>
    [Fact]
    public async Task An_opened_working_copy_is_confirmed_by_its_name_appearing_in_Meitu()
    {
        Harness h = Build();
        ExternalProcessRef process = Process();
        ExternalWindowRef window = MeituFakes.Window(owningProcessId: process.ProcessId);
        ExternalWindowRef dialog = MeituFakes.Window(
            handle: 0x2000, owningProcessId: process.ProcessId, title: "打开", className: "#32770");
        ExternalWindowRef saveDialog = MeituFakes.Window(
            handle: 0x6000, owningProcessId: process.ProcessId, title: "Form", className: "QtSaveDialog");
        // Only the start page exists to begin with; the editor and the picker appear as they are
        // asked for, below.
        h.Locator.Register(process, window);
        ShowWelcomePage(h.Elements, window);

        // The editor is a second top-level window that only exists once the start-page card has
        // been invoked, exactly as on the workstation (Part B1 §7).
        ExternalWindowRef editor = MeituFakes.Window(
            handle: 0x5000, owningProcessId: process.ProcessId, title: MeituFakes.EditorTitle);

        h.Elements.AddStartPageCard(window.Handle, "图片编辑", processId: process.ProcessId);
        h.Elements.AddEditorOpenControl(editor.Handle, process.ProcessId);
        h.Elements.AddDialogControl(dialog.Handle, "1148", "Edit", processId: process.ProcessId);
        h.Elements.AddDialogControl(dialog.Handle, "1", "Button", processId: process.ProcessId);
        h.Elements.AddIdentityDialogControl(
            saveDialog.Handle, "MainWindow.wName.fileNameEdit", "", "Edit", "QLineEdit",
            UiPatternKind.Value, process.ProcessId);
        h.Elements.AddIdentityDialogControl(
            saveDialog.Handle, "MainWindow.titleFrame.closeButton", "\uE0E6", "Button", "IconFontButton",
            UiPatternKind.Invoke, process.ProcessId);
        h.Elements.SetReadValue("MainWindow.wName.fileNameEdit", "working_副本");

        WorkspaceFileRef working = WorkspaceFileRef.Create(
            "Sessions/S_1/Working/A_1/working.png", WorkspaceArea.Working);
        string absolute = h.Workspace.ResolveAbsolute(working);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        File.WriteAllBytes(absolute, [0x89, 0x50, 0x4E, 0x47]);

        // Each invoke moves Meitu one step, and the order is the point: the start page is what
        // EnsureReadyAsync sees, the empty editor is where the picker comes from, and the editor
        // showing the handed-over name is what confirmation sees. A fake that showed the file
        // name from the outset would let a confirmation step that never ran pass.
        h.Elements.OnInvoke = invoked =>
        {
            if (invoked.EndsWith(".CardButton", StringComparison.Ordinal))
            {
                h.Locator.Replace(process, window, editor);
                h.Elements.SetTexts(editor.Handle, [.. MeituFakes.EmptyEditorMarkers]);
            }
            else if (invoked.EndsWith(".openButton", StringComparison.Ordinal))
            {
                h.Locator.Replace(process, window, editor, dialog);
            }
            else if (invoked == "1")
            {
                h.Elements.SetTexts(editor.Handle, [.. MeituFakes.EditorMarkers]);
                h.Elements.AddEditorSaveControl(editor.Handle, process.ProcessId);
            }
            else if (invoked.EndsWith(".saveButton", StringComparison.Ordinal))
            {
                h.Locator.OwnedDialogs.Add(saveDialog);
                h.Locator.PutInForeground(saveDialog);
            }
            else if (invoked.EndsWith(".titleFrame.closeButton", StringComparison.Ordinal))
            {
                h.Locator.OwnedDialogs.Remove(saveDialog);
                h.Locator.PutInForeground(editor);
            }
        };

        OperationResult<MeituOpenedWorkingCopy> opened =
            await h.Adapter.OpenWorkingCopyAsync(working, CancellationToken.None);

        opened.IsSuccess.ShouldBeTrue();
        opened.Value.State.State.ShouldBe(MeituStartingState.KnownEditorWithExpectedWorkingCopy);
        h.Elements.ValueWrites.ShouldHaveSingleItem();
        h.Elements.ValueWrites[0].Value.ShouldBe(absolute);
        Directory.EnumerateFiles(Path.GetDirectoryName(absolute)!).ShouldHaveSingleItem().ShouldBe(absolute);
    }

    [Fact]
    public async Task A_working_reference_with_no_file_behind_it_is_refused_before_Meitu_is_touched()
    {
        Harness h = Build();
        ExternalProcessRef process = Process();
        ExternalWindowRef window = MeituFakes.Window(owningProcessId: process.ProcessId);
        h.Locator.Register(process, window);
        ShowWelcomePage(h.Elements, window);

        WorkspaceFileRef missing = WorkspaceFileRef.Create(
            "Sessions/S_1/Working/A_1/gone.png", WorkspaceArea.Working);

        OperationResult<MeituOpenedWorkingCopy> opened =
            await h.Adapter.OpenWorkingCopyAsync(missing, CancellationToken.None);

        opened.IsFailure.ShouldBeTrue();
        opened.Failure.Code.ShouldBe(FailureCode.OutputMissing);
        h.Elements.ValueWrites.ShouldBeEmpty();
    }

    // -----------------------------------------------------------------------------
    // Confirmation (§13, §14)
    // -----------------------------------------------------------------------------

    /// <summary>
    /// An open that cannot be confirmed by identity is never reported as a success.
    /// </summary>
    /// <remarks>
    /// This pins the Part B1 finding as behaviour. Meitu 7.8.7.5 exposes the open document's name
    /// nowhere a UI Automation client can read it — 141 elements at depth 30, zero occurrences of
    /// the handed-over file name — so the verified chain carries no editor-with-working-copy
    /// signature and the state stays unreachable.
    ///
    /// The file is still handed over, and the picker still closes. What must not happen is the
    /// adapter concluding from that that the right file is loaded. The assertion to read here is
    /// the failure: PrintFlow gets all the way to a loaded editor and still declines to claim it
    /// is looking at PrintFlow's file, because it cannot tell.
    /// </remarks>
    [Fact]
    public async Task An_open_that_cannot_be_confirmed_by_identity_is_refused_not_claimed()
    {
        Harness h = Build(MeituFakes.BaselineWithout(documentIdentity: true) with
        {
            ExecutablePath = _executablePath,
            ExecutableSha256 = _executableSha256,
        });
        ExternalProcessRef process = Process();
        ExternalWindowRef window = MeituFakes.Window(owningProcessId: process.ProcessId);
        ExternalWindowRef dialog = MeituFakes.Window(
            handle: 0x2000, owningProcessId: process.ProcessId, title: "打开", className: "#32770");
        ExternalWindowRef editor = MeituFakes.Window(
            handle: 0x5000, owningProcessId: process.ProcessId, title: MeituFakes.EditorTitle);

        h.Locator.Register(process, window);
        ShowWelcomePage(h.Elements, window);
        h.Elements.AddStartPageCard(window.Handle, "图片编辑", processId: process.ProcessId);
        h.Elements.AddEditorOpenControl(editor.Handle, process.ProcessId);
        h.Elements.AddDialogControl(dialog.Handle, "1148", "Edit", processId: process.ProcessId);
        h.Elements.AddDialogControl(dialog.Handle, "1", "Button", processId: process.ProcessId);

        h.Elements.OnInvoke = invoked =>
        {
            if (invoked.EndsWith(".CardButton", StringComparison.Ordinal))
            {
                h.Locator.Replace(process, window, editor);
                h.Elements.SetTexts(editor.Handle, [.. MeituFakes.EmptyEditorMarkers]);
            }
            else if (invoked.EndsWith(".openButton", StringComparison.Ordinal))
            {
                h.Locator.Replace(process, window, editor, dialog);
            }
            else if (invoked == "1")
            {
                // Meitu really does load the file — it just never says which file it is.
                h.Elements.SetTexts(editor.Handle, [.. MeituFakes.EditorMarkers]);
            }
        };

        WorkspaceFileRef working = WorkspaceFileRef.Create(
            "Sessions/S_1/Working/A_1/working.png", WorkspaceArea.Working);
        string absolute = h.Workspace.ResolveAbsolute(working);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        File.WriteAllBytes(absolute, [0x89, 0x50, 0x4E, 0x47]);

        OperationResult<MeituOpenedWorkingCopy> opened =
            await h.Adapter.OpenWorkingCopyAsync(working, CancellationToken.None);

        opened.IsFailure.ShouldBeTrue();
        opened.Failure.Code.ShouldBe(FailureCode.MeituUnknownState);
        opened.Failure.Context["missingEvidence"].ShouldBe("editor-with-working-copy");

        // The path was still delivered — the refusal is about what may be concluded, not about
        // whether the work was attempted.
        h.Elements.ValueWrites.ShouldHaveSingleItem();
    }

    /// <summary>
    /// A confirmation that times out is a failure, not a success carrying <c>Unknown</c>.
    /// </summary>
    /// <remarks>
    /// Regression test for a defect this slice found in the Part A confirmation path: the poll
    /// returned its last observation as a success when the deadline passed, so an open that never
    /// produced the expected screen was reported as having succeeded with a state of
    /// <c>Unknown</c>. That is the "opened successfully means processing succeeded" conflation
    /// stated in code.
    /// </remarks>
    [Fact]
    public async Task A_loaded_editor_signature_that_never_arrives_is_a_failure_before_Save()
    {
        Harness h = Build();
        ExternalProcessRef process = Process();
        ExternalWindowRef window = MeituFakes.Window(owningProcessId: process.ProcessId);
        ExternalWindowRef dialog = MeituFakes.Window(
            handle: 0x2000, owningProcessId: process.ProcessId, title: "打开", className: "#32770");
        ExternalWindowRef editor = MeituFakes.Window(
            handle: 0x5000, owningProcessId: process.ProcessId, title: MeituFakes.EditorTitle);

        h.Locator.Register(process, window);
        ShowWelcomePage(h.Elements, window);
        h.Elements.AddStartPageCard(window.Handle, "图片编辑", processId: process.ProcessId);
        h.Elements.AddEditorOpenControl(editor.Handle, process.ProcessId);
        h.Elements.AddDialogControl(dialog.Handle, "1148", "Edit", processId: process.ProcessId);
        h.Elements.AddDialogControl(dialog.Handle, "1", "Button", processId: process.ProcessId);

        h.Elements.OnInvoke = invoked =>
        {
            if (invoked.EndsWith(".CardButton", StringComparison.Ordinal))
            {
                h.Locator.Replace(process, window, editor);
                h.Elements.SetTexts(editor.Handle, [.. MeituFakes.EmptyEditorMarkers]);
            }
            else if (invoked.EndsWith(".openButton", StringComparison.Ordinal))
            {
                h.Locator.Replace(process, window, editor, dialog);
            }

            // Nothing ever shows the expected file: the editor stays as it was.
        };

        WorkspaceFileRef working = WorkspaceFileRef.Create(
            "Sessions/S_1/Working/A_1/working.png", WorkspaceArea.Working);
        string absolute = h.Workspace.ResolveAbsolute(working);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        File.WriteAllBytes(absolute, [0x89, 0x50, 0x4E, 0x47]);

        OperationResult<MeituOpenedWorkingCopy> opened =
            await h.Adapter.OpenWorkingCopyAsync(working, CancellationToken.None);

        opened.IsFailure.ShouldBeTrue();
        opened.Failure.Code.ShouldBe(FailureCode.MeituUnknownState);
        opened.Failure.TechnicalDetail.ShouldContain("Save was not invoked");
        h.Elements.Invocations.ShouldNotContain("MainWindow.editorPage.saveButton");
    }

    // -----------------------------------------------------------------------------
    // The Enhancement seam (Part B2A §16, §20)
    // -----------------------------------------------------------------------------

    /// <summary>
    /// Enhancement refuses anything that is not a Working copy, before it touches Meitu.
    /// </summary>
    /// <remarks>
    /// The same boundary as the open path, restated at the point that reaches an irreversible
    /// action. "The caller already checked" is exactly the reasoning that lets a Source or
    /// Approved reference through once a second way in exists.
    /// </remarks>
    [Theory]
    [InlineData(WorkspaceArea.Source)]
    [InlineData(WorkspaceArea.Approved)]
    [InlineData(WorkspaceArea.Rejected)]
    public async Task Enhancement_refuses_anything_that_is_not_a_Working_copy(WorkspaceArea area)
    {
        Harness h = Build();
        ExternalProcessRef process = Process();
        ExternalWindowRef window = MeituFakes.Window(owningProcessId: process.ProcessId);
        h.Locator.Register(process, window);

        WorkspaceFileRef reference = WorkspaceFileRef.Create($"Sessions/S_1/{area}/a.png", area);
        MeituOpenedWorkingCopy opened = new(
            new MeituTarget(process, window),
            new MeituStateSnapshot(
                MeituStartingState.KnownEditorWithExpectedWorkingCopy,
                [],
                new MeituObservation(MeituFakes.EditorTitle, [], [], true, "a.png", "a_副本")),
            MeituFakes.QuietLoad());

        OperationResult<MeituEnhancementOutcome> run =
            await h.Adapter.EnhanceAsync(opened, reference, CancellationToken.None);

        run.IsFailure.ShouldBeTrue();
        run.Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);
        h.Elements.Invocations.ShouldBeEmpty();
        h.Input.Sends.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(WorkspaceArea.Source)]
    [InlineData(WorkspaceArea.Approved)]
    [InlineData(WorkspaceArea.Rejected)]
    public async Task Background_Removal_C1_refuses_anything_that_is_not_a_Working_copy(
        WorkspaceArea area)
    {
        Harness h = Build();
        ExternalProcessRef process = Process();
        ExternalWindowRef window = MeituFakes.Window(owningProcessId: process.ProcessId);
        h.Locator.Register(process, window);

        WorkspaceFileRef reference = WorkspaceFileRef.Create($"Sessions/S_1/{area}/a.png", area);
        MeituOpenedWorkingCopy opened = new(
            new MeituTarget(process, window),
            new MeituStateSnapshot(
                MeituStartingState.KnownEditorWithExpectedWorkingCopy,
                [],
                new MeituObservation(MeituFakes.EditorTitle, [], [], true, "a.png", "a_副本")),
            MeituFakes.QuietLoad());

        OperationResult<MeituBackgroundRemovalOutcome> run =
            await h.Adapter.RemoveBackgroundAsync(
                opened,
                reference,
                BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent,
                CancellationToken.None);

        run.IsFailure.ShouldBeTrue();
        run.Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);
        h.Elements.Invocations.ShouldBeEmpty();
        h.Input.Sends.ShouldBeEmpty();
    }

    /// <summary>
    /// A confirmed open does not license an Enhancement without re-establishing identity.
    /// </summary>
    /// <remarks>
    /// The state <c>OpenWorkingCopyAsync</c> returned is a fact about the past. Here the fake
    /// Meitu has no Save control at all, so the mandatory pre-Enhancement probe cannot run — and
    /// the adapter must refuse rather than act on the earlier confirmation.
    /// </remarks>
    [Fact]
    public async Task Enhancement_re_probes_identity_rather_than_trusting_the_confirmed_open()
    {
        Harness h = Build();
        ExternalProcessRef process = Process();
        ExternalWindowRef editor = MeituFakes.Window(
            owningProcessId: process.ProcessId, title: MeituFakes.EditorTitle);

        h.Locator.Register(process, editor);
        h.Locator.PutInForeground(editor);
        h.Elements.SetTexts(editor.Handle, [.. MeituFakes.EditorMarkers]);
        h.Elements.AddEnhancementAction(editor.Handle, processId: process.ProcessId);

        WorkspaceFileRef working = WorkspaceFileRef.Create(
            "Sessions/S_1/Working/A_1/a.png", WorkspaceArea.Working);
        MeituOpenedWorkingCopy opened = new(
            new MeituTarget(process, editor),
            new MeituStateSnapshot(
                MeituStartingState.KnownEditorWithExpectedWorkingCopy,
                [],
                new MeituObservation(MeituFakes.EditorTitle, [], [], true, "a.png", "a_副本")),
            MeituFakes.QuietLoad());

        OperationResult<MeituEnhancementOutcome> run =
            await h.Adapter.EnhanceAsync(opened, working, CancellationToken.None);

        run.IsFailure.ShouldBeTrue();
        h.Elements.Invocations.Count(i => i == MeituFakes.ModuleAutomationId).ShouldBe(0);
    }

    /// <summary>
    /// A failed Enhancement attaches local evidence without letting it replace the failure.
    /// </summary>
    [Fact]
    public async Task A_failed_Enhancement_is_reported_with_its_own_failure_not_the_capture_result()
    {
        Harness h = Build();
        h.Evidence.Fails = true;

        ExternalProcessRef process = Process();
        ExternalWindowRef editor = MeituFakes.Window(
            owningProcessId: process.ProcessId, title: MeituFakes.EditorTitle);
        h.Locator.Register(process, editor);

        WorkspaceFileRef working = WorkspaceFileRef.Create(
            "Sessions/S_1/Working/A_1/a.png", WorkspaceArea.Working);
        MeituOpenedWorkingCopy opened = new(
            new MeituTarget(process, editor),
            new MeituStateSnapshot(
                MeituStartingState.KnownEditorWithExpectedWorkingCopy,
                [],
                new MeituObservation(MeituFakes.EditorTitle, [], [], true, "a.png", "a_副本")),
            MeituFakes.QuietLoad());

        OperationResult<MeituEnhancementOutcome> run =
            await h.Adapter.EnhanceAsync(opened, working, CancellationToken.None);

        run.IsFailure.ShouldBeTrue();
        run.Failure.Code.ShouldBe(FailureCode.MeituUnknownState);
        run.Failure.Context.ShouldNotContainKey("evidencePath");
        h.Evidence.Captures.ShouldContain(c => c.Reason == "enhancement-failed");
    }
}
