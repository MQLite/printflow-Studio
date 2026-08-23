using System.IO;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Adapters.Meitu;
using PrintFlow.Infrastructure.Automation;
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
            baselines, locator, driver, workspace, FastOptions, TimeProvider.System);

        return new Harness(adapter, locator, elements, input, evidence, workspace);
    }

    private static void ShowWelcomePage(RecordingUiElementProvider elements, ExternalWindowRef window) =>
        elements.SetTexts(window.Handle, [.. MeituFakes.WelcomeMarkers]);

    // -----------------------------------------------------------------------------
    // The workflow seam never succeeds in this slice (§24)
    // -----------------------------------------------------------------------------

    /// <summary>
    /// Opening a file is not processing, and the adapter says so.
    /// </summary>
    /// <remarks>
    /// The single most important assertion in this file. If <c>ProcessAsync</c> could return a
    /// success, a Revision would be created for an image nothing had enhanced — the "pretend
    /// opened means succeeded" failure §24 exists to rule out.
    /// </remarks>
    [Theory]
    [InlineData(MeituOperation.Enhance)]
    [InlineData(MeituOperation.RemoveBackground)]
    public async Task The_workflow_seam_always_fails_because_no_operation_is_automated_yet(
        MeituOperation operation)
    {
        Harness h = Build();
        WorkspaceDirRef sessionDir = WorkspaceDirRef.Create("Sessions/S_1");
        WorkspaceFileRef file = WorkspaceFileRef.Create("Sessions/S_1/Working/a.png", WorkspaceArea.Working);

        OperationResult<AdapterOutput> result = await h.Adapter.ProcessAsync(
            new MeituRequest(file, operation, sessionDir, file), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.AdapterUnavailable);
        result.Failure.IsRetryable.ShouldBeFalse();
        result.Failure.Context["adapterId"].ShouldBe("meitu-xiuxiu-production-v1");
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
        h.Locator.Register(process, window, dialog);
        ShowWelcomePage(h.Elements, window);

        h.Elements.MakeFindable(new UiElementQuery(UiControlKind.Any, Name: "图片编辑"));
        h.Elements.MakeFindable(new UiElementQuery(UiControlKind.Edit, AutomationId: "1148"));
        h.Elements.MakeFindable(new UiElementQuery(UiControlKind.Button, AutomationId: "1"));

        WorkspaceFileRef working = WorkspaceFileRef.Create(
            "Sessions/S_1/Working/A_1/working.png", WorkspaceArea.Working);
        string absolute = h.Workspace.ResolveAbsolute(working);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        File.WriteAllBytes(absolute, [0x89, 0x50, 0x4E, 0x47]);

        // The fake window reports one fixed set of names, so it carries both the welcome markers
        // (which EnsureReadyAsync needs) and the handed-over file name (which confirmation
        // needs). The classifier checks the expected file name before the welcome markers, so
        // this also pins that ordering: an open document outranks a start page that is still
        // partly visible.
        h.Elements.SetTexts(window.Handle, [.. MeituFakes.WelcomeMarkers, "working.png"]);

        OperationResult<MeituOpenedWorkingCopy> opened =
            await h.Adapter.OpenWorkingCopyAsync(working, CancellationToken.None);

        opened.IsSuccess.ShouldBeTrue();
        opened.Value.State.State.ShouldBe(MeituStartingState.KnownEditorWithExpectedWorkingCopy);
        h.Elements.ValueWrites.ShouldHaveSingleItem();
        h.Elements.ValueWrites[0].Value.ShouldBe(absolute);
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
}
