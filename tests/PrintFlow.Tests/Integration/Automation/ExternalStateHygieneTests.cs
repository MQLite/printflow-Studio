using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Adapters.Meitu;
using PrintFlow.Infrastructure.Adapters.Photoshop;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Ports;
using FileWorkspace = PrintFlow.Infrastructure.Workspace.FileWorkspace;

namespace PrintFlow.Tests.Integration.Automation;

/// <summary>
/// What Photoshop and Meitu are allowed to be holding when the next production operation
/// starts, and what happens when they are holding something else (Epic 11600 Part A §3, §6,
/// §7, §8, §13, §14).
/// </summary>
/// <remarks>
/// These are the questions the workflow-level hygiene tests cannot ask. <c>SessionService</c>
/// sees an <c>OperationResult</c>; whether the second job of the morning can safely begin
/// depends on what the first one left on screen, which only the real adapters can be asked
/// about. So the adapters here are the production ones and only the operating system is faked.
/// <para>
/// The two applications reach a deliberately different boundary after a successful operation,
/// and these tests state which (§3):
/// </para>
/// <list type="bullet">
///   <item><b>Photoshop</b> — the owned working document may remain open. The production
///   composition never closes it, and the next operation opens its own file from
///   <c>KnownEditorWithOtherDocument</c>, proving identity by absolute path. Policy A.</item>
///   <item><b>Meitu</b> — the adapter returns the editor to its signed empty state after a
///   successful export, and a loaded document that is not the expected one is not a safe
///   starting state at all. Policy B.</item>
/// </list>
/// <para>
/// Neither policy is invented here. Both are read off the accepted adapters, and the tests are
/// what stop either from drifting: a Photoshop composition that started closing documents, or a
/// Meitu adapter that stopped tidying, would fail here rather than on a workstation.
/// </para>
/// </remarks>
public sealed class ExternalStateHygieneTests : IDisposable
{
    private static readonly PhotoshopAutomationOptions FastPhotoshop = new()
    {
        PollInterval = TimeSpan.FromMilliseconds(5),
        ActivationTimeout = TimeSpan.FromMilliseconds(60),
        DialogTimeout = TimeSpan.FromMilliseconds(60),
        IdentityDialogTimeout = TimeSpan.FromMilliseconds(60),
        DialogCloseTimeout = TimeSpan.FromMilliseconds(60),
        OpenConfirmationTimeout = TimeSpan.FromMilliseconds(120),
        AttachTimeout = TimeSpan.FromMilliseconds(120),
        LaunchTimeout = TimeSpan.FromMilliseconds(120),
    };

    private static readonly MeituAutomationOptions FastMeitu = new()
    {
        PollInterval = TimeSpan.FromMilliseconds(5),
        ActivationTimeout = TimeSpan.FromMilliseconds(60),
        DialogTimeout = TimeSpan.FromMilliseconds(60),
        AttachTimeout = TimeSpan.FromMilliseconds(60),
        LaunchTimeout = TimeSpan.FromMilliseconds(60),
        OpenConfirmationTimeout = TimeSpan.FromMilliseconds(60),
    };

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "PrintFlowHygieneTests", Guid.NewGuid().ToString("N"));

    private readonly string _photoshopExecutable;
    private readonly string _meituExecutable;

    public ExternalStateHygieneTests()
    {
        Directory.CreateDirectory(_root);

        // Real files with real digests: the identity rule reads whatever is at the configured
        // path, and a relaunch that skipped that check is exactly what §13 forbids.
        _photoshopExecutable = Path.Combine(_root, "Photoshop.exe");
        File.WriteAllBytes(_photoshopExecutable, [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00]);

        _meituExecutable = Path.Combine(_root, "XiuXiu.exe");
        File.WriteAllBytes(_meituExecutable, [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x02, 0x01]);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    // -----------------------------------------------------------------------------------
    // §3, §6 — Photoshop's residual-document policy, and what the next job does with it
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// The next job opens its own file while the previous job's document is still loaded, and
    /// proves by absolute path which document it got (§5, §6).
    /// </summary>
    /// <remarks>
    /// Policy A stated as behaviour. Photoshop begins this test where the accepted composition
    /// actually leaves it — holding job A's working document, because nothing in
    /// <c>GenerateAsync</c> closes one — and job B proceeds from there through
    /// <c>KnownEditorWithOtherDocument</c>, which the safe-starting-state allow-list admits.
    /// <para>
    /// The identity assertion is the part that makes the policy safe rather than merely
    /// convenient. A residual document is only harmless while "which document is loaded" is
    /// decided by the absolute path read from the signed probe; the moment it could be decided
    /// by a title, the leftover would be a candidate for the answer.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_second_job_opens_its_own_file_while_the_first_jobs_document_is_still_loaded()
    {
        PhotoshopHarness h = BuildPhotoshop();

        // Photoshop as job A left it: the editor holding A's working document.
        const string previous = "JOB-A_WORKING.png";
        ShowDocument(h, previous);

        WorkspaceFileRef jobB = ManagedFile(h, "JOB-B_WORKING.png");
        StageOpenThenDocument(h, jobB.FileName);

        OperationResult<PhotoshopOpenedDocument> opened =
            await h.Adapter.OpenManagedWorkingFileAsync(jobB, CancellationToken.None);

        opened.IsSuccess.ShouldBeTrue(opened.IsFailure ? opened.Failure.ToString() : "");
        opened.Value.Identity.ObservedFullPath.ShouldBe(Path.Combine(h.ManagedDirectory, jobB.FileName));
        opened.Value.Identity.ObservedFullPath.ShouldNotContain(previous);

        // The adapter recorded that it inherited a document rather than an empty editor, which
        // is what a later cleanup decision would have to consult.
        opened.Value.OtherDocumentsMayBeOpen.ShouldBeTrue();

        // Reused, not relaunched: the residual document did not cost a second Photoshop.
        h.Locator.LaunchCount.ShouldBe(0);
    }

    /// <summary>
    /// The production composition contains exactly one forward-only owned-document close after
    /// the independently validated TIFF (§3, §6, and Epic 11600 Part B1).
    /// </summary>
    /// <remarks>
    /// Structural because the ordering is the safety property: cleanup must not move before TIFF
    /// validation and must not multiply into close-all or retrying closes.
    /// </remarks>
    [Fact]
    public void The_composed_production_operation_closes_once_after_TIFF_validation()
    {
        string source = File.ReadAllText(Path.Combine(
            InfrastructureProjectDirectory(),
            "Adapters", "Photoshop", "ProductionPhotoshopOutputProcessor.cs"));

        int generateAt = source.IndexOf(
            "public async Task<OperationResult<AdapterOutput>> GenerateAsync", StringComparison.Ordinal);
        generateAt.ShouldBeGreaterThan(0);

        int endsAt = source.IndexOf("private static OperationResult<AdapterOutput> Refused", StringComparison.Ordinal);
        endsAt.ShouldBeGreaterThan(generateAt);

        string body = string.Join(
            '\n',
            source[generateAt..endsAt]
                .Split('\n')
                .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        (body.Split("CloseExactDocumentAsync(", StringSplitOptions.None).Length - 1).ShouldBe(1);
        body.IndexOf("SaveProductionTiffAsync", StringComparison.Ordinal)
            .ShouldBeLessThan(body.IndexOf("CloseExactDocumentAsync", StringComparison.Ordinal));
    }

    // -----------------------------------------------------------------------------------
    // §7 — a document PrintFlow cannot prove it owns
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// Photoshop holding an unprovable document refuses the operation without touching it, and
    /// the next operation succeeds once an accepted state is restored (§7).
    /// </summary>
    /// <remarks>
    /// The whole §7 scenario in one test, because the two halves only mean something together:
    /// a refusal that could not be recovered from would be a workstation an operator has to
    /// restart, and a recovery that did not first refuse would be the modification §7 exists to
    /// prevent.
    /// <para>
    /// "Cannot prove it owns" is modelled as the realistic case rather than an exotic one: the
    /// document reports the file name PrintFlow asked for, from a folder PrintFlow never named.
    /// A rule that compared names would call this a match, and it is the operator's own file.
    /// </para>
    /// <para>
    /// The failure code is the existing <c>PhotoshopDocumentIdentityUnconfirmed</c>. §7 asks for
    /// no new vocabulary where the current vocabulary already says what happened, and it does:
    /// the screen was recognised perfectly well, and the document on it could not be shown to be
    /// the one that was asked for.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task An_unprovable_document_is_refused_untouched_and_the_next_operation_still_succeeds()
    {
        PhotoshopHarness h = BuildPhotoshop();
        WorkspaceFileRef managed = ManagedFile(h, "OWNED_WORKING.png");

        // Photoshop answers the identity probe from somewhere PrintFlow never named.
        StageOpenThenDocument(h, managed.FileName, identityFolder: @"C:\Users\admin\Desktop");

        OperationResult<PhotoshopOpenedDocument> refused =
            await h.Adapter.OpenManagedWorkingFileAsync(managed, CancellationToken.None);

        refused.IsFailure.ShouldBeTrue();
        refused.Failure.Code.ShouldBe(FailureCode.PhotoshopDocumentIdentityUnconfirmed);

        // Nothing irreversible was attempted against the unknown document.
        refused.Failure.Context["w1ActionInvoked"].ShouldBe("false");
        refused.Failure.Context["tiffWritten"].ShouldBe("false");

        // Stated as an allow-list over everything that was sent, not as a list of the two
        // shortcuts it would have been most alarming to find. A deny-list would keep passing
        // when a new keystroke was added, which is precisely when this needs to fail.
        h.Input.Sends.Select(sent => sent.Shortcut).Distinct()
            .ShouldBeSubsetOf([KnownShortcut.OpenFile, KnownShortcut.SaveAsProbe]);

        // And no prompt was left standing: the identity surface PrintFlow raised was cancelled
        // on its way out rather than abandoned in front of the operator's document.
        h.Locator.OwnedDialogs.ShouldBeEmpty();

        // The operator restores an accepted state — the unknown document is gone and Photoshop
        // is back on its editor — and the very next operation goes through.
        h.Locator.Replace(h.Target.Process, PhotoshopFakes.Window());
        h.Controls.ClearControls(h.Target.Window.Handle);
        h.Controls.SetVisibleClasses(
            PhotoshopFakes.Window().Handle,
            [.. PhotoshopFakes.EditorChromeClasses, PhotoshopFakes.StartScreenClass]);
        StageOpenThenDocument(h, managed.FileName);

        OperationResult<PhotoshopOpenedDocument> recovered =
            await h.Adapter.OpenManagedWorkingFileAsync(managed, CancellationToken.None);

        recovered.IsSuccess.ShouldBeTrue(recovered.IsFailure ? recovered.Failure.ToString() : "");
        recovered.Value.Identity.ObservedFullPath.ShouldBe(Path.Combine(h.ManagedDirectory, managed.FileName));
    }

    /// <summary>
    /// A dialog Photoshop is already showing stops the operation before anything is sent, and is
    /// never answered (§7).
    /// </summary>
    /// <remarks>
    /// The other shape of "unknown state", and the one where a guess would be worst: an
    /// unsaved-changes prompt over somebody's own work. PrintFlow has no way to tell one modal
    /// from another, so it answers none of them.
    /// </remarks>
    [Fact]
    public async Task A_dialog_already_on_screen_stops_the_operation_and_is_never_answered()
    {
        PhotoshopHarness h = BuildPhotoshop();
        WorkspaceFileRef managed = ManagedFile(h, "BLOCKED_WORKING.png");

        h.Locator.OwnedDialogs.Add(PhotoshopFakes.Dialog(title: "Adobe Photoshop"));

        OperationResult<PhotoshopOpenedDocument> refused =
            await h.Adapter.OpenManagedWorkingFileAsync(managed, CancellationToken.None);

        refused.IsFailure.ShouldBeTrue();
        refused.Failure.Code.ShouldBe(FailureCode.PhotoshopBlockingDialog);

        h.Input.Sends.ShouldBeEmpty();
        h.Controls.Presses.ShouldBeEmpty();
        h.Controls.Writes.ShouldBeEmpty();

        // The dialog is still there. PrintFlow did not dismiss it on its way out.
        h.Locator.OwnedDialogs.Count.ShouldBe(1);
    }

    // -----------------------------------------------------------------------------------
    // §13, §14 — the application disappears, or outlives PrintFlow
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// Photoshop closed between jobs is relaunched, and the relaunched process is identity
    /// checked before anything is sent to it (§13).
    /// </summary>
    /// <remarks>
    /// The identity assertion is the point rather than the relaunch. Recovery must not be a
    /// route around the executable check, so the launch is scripted to produce a process running
    /// from a <i>different</i> binary and the run has to refuse it — the same refusal a first
    /// launch would make, on a path that only recovery reaches.
    /// </remarks>
    [Fact]
    public async Task Photoshop_closed_between_jobs_is_relaunched_and_still_identity_checked()
    {
        PhotoshopHarness gone = BuildPhotoshop(registerProcess: false);

        // Job B finds nothing running and launches. The launched process reports the accepted
        // path, so the run proceeds to a recognised state.
        gone.Locator.LaunchResult = new ExternalProcessRef(
            8888, _photoshopExecutable, new DateTimeOffset(2026, 9, 2, 9, 0, 0, TimeSpan.Zero));
        ExternalWindowRef relaunched = PhotoshopFakes.Window(handle: 0xB0BDC, owningProcessId: 8888);
        gone.Locator.Replace(gone.Locator.LaunchResult, relaunched);
        gone.Locator.PutInForeground(relaunched);
        gone.Controls.SetVisibleClasses(
            relaunched.Handle,
            [.. PhotoshopFakes.EditorChromeClasses, PhotoshopFakes.StartScreenClass]);

        OperationResult<PhotoshopReadiness> ready = await gone.Adapter.EnsureReadyAsync(CancellationToken.None);

        ready.IsSuccess.ShouldBeTrue(ready.IsFailure ? ready.Failure.ToString() : "");
        ready.Value.WasLaunched.ShouldBeTrue();
        gone.Locator.LaunchCount.ShouldBe(1);

        // The same recovery path, with a process that is not the accepted binary: refused.
        PhotoshopHarness impostor = BuildPhotoshop(registerProcess: false);
        string other = Path.Combine(_root, "NotPhotoshop.exe");
        File.WriteAllBytes(other, [0x4D, 0x5A, 0x00]);
        impostor.Locator.LaunchResult = new ExternalProcessRef(
            9999, other, new DateTimeOffset(2026, 9, 2, 9, 0, 0, TimeSpan.Zero));

        OperationResult<PhotoshopReadiness> refused =
            await impostor.Adapter.EnsureReadyAsync(CancellationToken.None);

        refused.IsFailure.ShouldBeTrue();
        refused.Failure.Code.ShouldBe(FailureCode.PhotoshopNotInstalled);
        refused.Failure.Context["inputSent"].ShouldBe("false");
        impostor.Input.Sends.ShouldBeEmpty();
    }

    /// <summary>
    /// A restarted PrintFlow attaches to the Photoshop that outlived it rather than launching a
    /// second one, and re-establishes the state itself (§14).
    /// </summary>
    /// <remarks>
    /// The shop scenario: PrintFlow is closed and reopened while Photoshop stays up. The second
    /// adapter instance shares nothing with the first — it is a different object over the same
    /// scripted operating system, which is exactly what a new process is — and what it must not
    /// do is either assume the old instance's ownership or start a rival Photoshop.
    /// <para>
    /// A second instance would be the more dangerous of the two mistakes, and the adapter refuses
    /// to choose between instances at all, so a launch here would not merely be untidy: it would
    /// wedge every later operation.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_restarted_PrintFlow_attaches_to_the_Photoshop_that_outlived_it()
    {
        PhotoshopHarness first = BuildPhotoshop();
        (await first.Adapter.EnsureReadyAsync(CancellationToken.None))
            .IsSuccess.ShouldBeTrue();

        // A new PrintFlow process: a new adapter over the same running Photoshop.
        ProductionPhotoshopOutputProcessor restarted = new(
            new StubPhotoshopBaselineProvider(PhotoshopBaseline()),
            first.Locator,
            first.Driver,
            new StubPhotoshopWorkspace(first.ManagedDirectory),
            FastPhotoshop,
            TimeProvider.System);

        OperationResult<PhotoshopReadiness> ready = await restarted.EnsureReadyAsync(CancellationToken.None);

        ready.IsSuccess.ShouldBeTrue(ready.IsFailure ? ready.Failure.ToString() : "");
        ready.Value.WasLaunched.ShouldBeFalse();
        ready.Value.Target.Process.ProcessId.ShouldBe(first.Target.Process.ProcessId);
        first.Locator.LaunchCount.ShouldBe(0);

        // Attaching re-inspected the screen rather than trusting an inherited belief about it.
        ready.Value.State.IsSafeStartingState.ShouldBeTrue();
    }

    // -----------------------------------------------------------------------------------
    // §3, §8 — Meitu's post-operation state, and a dirty editor
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// Meitu on its signed empty editor is ready; Meitu holding something PrintFlow did not put
    /// there is not, and refuses (§8).
    /// </summary>
    /// <remarks>
    /// Policy B stated as behaviour, and stated as a pair because the interesting claim is the
    /// contrast. Meitu's safe-starting-state allow-list has no "editor with some other document"
    /// member — unlike Photoshop's, deliberately, because Meitu is not a application the operator
    /// shares with PrintFlow in the same way — so an unexpected loaded asset is not a recognised
    /// screen at all and stops before any input.
    /// <para>
    /// The recovery half matters as much: once the editor is back in its signed empty state, the
    /// very next call is ready, so a dirty Meitu costs an operator a tidy-up rather than a
    /// restart.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_dirty_Meitu_editor_refuses_and_becomes_ready_again_once_it_is_empty()
    {
        MeituHarness h = BuildMeitu();

        // A document PrintFlow did not open: the markers of the signed empty editor are gone.
        h.Elements.SetTexts(h.Window.Handle, ["某个文档.png", "图层"]);

        OperationResult<MeituReadiness> refused = await h.Adapter.EnsureReadyAsync(CancellationToken.None);

        refused.IsFailure.ShouldBeTrue();
        refused.Failure.Code.ShouldBe(FailureCode.MeituUnknownState);

        // Nothing was pressed, typed or dismissed on the strength of a guess.
        h.Input.Sends.ShouldBeEmpty();
        h.Elements.Invocations.ShouldBeEmpty();
        h.Locator.LaunchCount.ShouldBe(0);

        // The editor is returned to the state a successful operation leaves it in, and the next
        // call is ready — no restart, no second instance.
        h.Elements.SetTexts(h.Window.Handle, [.. MeituFakes.EmptyEditorMarkers]);

        OperationResult<MeituReadiness> ready = await h.Adapter.EnsureReadyAsync(CancellationToken.None);

        ready.IsSuccess.ShouldBeTrue(ready.IsFailure ? ready.Failure.ToString() : "");
        ready.Value.State.IsSafeStartingState.ShouldBeTrue();
        h.Locator.LaunchCount.ShouldBe(0);
    }

    /// <summary>
    /// A dialog Meitu is already showing stops the operation and is never answered (§8).
    /// </summary>
    [Fact]
    public async Task A_Meitu_dialog_already_on_screen_stops_the_operation_and_is_never_answered()
    {
        MeituHarness h = BuildMeitu();
        h.Locator.OwnedDialogs.Add(MeituFakes.Window(handle: 0x9000, title: "提示", className: "#32770"));

        OperationResult<MeituReadiness> refused = await h.Adapter.EnsureReadyAsync(CancellationToken.None);

        refused.IsFailure.ShouldBeTrue();
        refused.Failure.Code.ShouldBeOneOf(FailureCode.MeituBlockingDialog, FailureCode.MeituUnknownState);

        h.Input.Sends.ShouldBeEmpty();
        h.Elements.Invocations.ShouldBeEmpty();
        h.Locator.OwnedDialogs.Count.ShouldBe(1);
    }

    /// <summary>
    /// Meitu closed between jobs is relaunched, and the relaunched process is identity checked
    /// (§13).
    /// </summary>
    [Fact]
    public async Task Meitu_closed_between_jobs_is_relaunched_and_still_identity_checked()
    {
        MeituHarness h = BuildMeitu(registerProcess: false);

        ExternalProcessRef launched = MeituFakes.Process(5150) with { ExecutablePath = _meituExecutable };
        ExternalWindowRef window = MeituFakes.Window(
            handle: 0xA100, owningProcessId: launched.ProcessId, title: MeituFakes.EditorTitle);
        h.Locator.LaunchResult = launched;
        h.Locator.Replace(launched, window);
        h.Locator.PutInForeground(window);
        h.Elements.SetTexts(window.Handle, [.. MeituFakes.EmptyEditorMarkers]);

        OperationResult<MeituReadiness> ready = await h.Adapter.EnsureReadyAsync(CancellationToken.None);

        ready.IsSuccess.ShouldBeTrue(ready.IsFailure ? ready.Failure.ToString() : "");
        h.Locator.LaunchCount.ShouldBe(1);

        // The same path with a binary whose bytes are not the accepted ones: refused, whatever
        // the path says. Relaunch is not a way around identity.
        File.WriteAllBytes(_meituExecutable, [0x4D, 0x5A, 0xFF, 0xFF]);

        OperationResult<MeituReadiness> refused = await h.Adapter.EnsureReadyAsync(CancellationToken.None);
        refused.IsFailure.ShouldBeTrue();
        refused.Failure.Code.ShouldBe(FailureCode.MeituNotInstalled);
    }

    /// <summary>
    /// A restarted PrintFlow attaches to the Meitu that outlived it rather than launching a
    /// second one (§14).
    /// </summary>
    [Fact]
    public async Task A_restarted_PrintFlow_attaches_to_the_Meitu_that_outlived_it()
    {
        MeituHarness h = BuildMeitu();
        (await h.Adapter.EnsureReadyAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();

        ProductionMeituProcessor restarted = new(
            new StubMeituBaselineProvider(MeituBaseline()),
            h.Locator,
            h.Driver,
            h.Workspace,
            new WicFileInspector(),
            new WicMeituTransparencyInspector(),
            new FileSystemMeituOutputProbe(),
            FastMeitu,
            TimeProvider.System);

        OperationResult<MeituReadiness> ready = await restarted.EnsureReadyAsync(CancellationToken.None);

        ready.IsSuccess.ShouldBeTrue(ready.IsFailure ? ready.Failure.ToString() : "");
        ready.Value.Target.Process.ProcessId.ShouldBe(h.Process.ProcessId);
        h.Locator.LaunchCount.ShouldBe(0);
    }

    // -----------------------------------------------------------------------------------
    // Epic 11600 Part B §19 — the invariants sustained repetition put under load
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// A newer installed version of Meitu is never selected, however it presents itself
    /// (Epic 11600 Part B Phase 0, §19).
    /// </summary>
    /// <remarks>
    /// This is the Part A workstation finding written down as a rule. Meitu updated itself in
    /// place beside the accepted version, so the machine now holds two installed binaries, and
    /// the tempting repair — "attach to the Meitu that is running", or "use the newest one
    /// installed" — would silently move production onto an executable no evidence chain covers.
    /// <para>
    /// The adapter resolves Meitu by the accepted absolute path and nothing else, so a process
    /// running from the newer directory is not a candidate at all: it is not attached to, not
    /// counted as an instance, and not treated as a reason to skip the launch. The assertion is
    /// on the executable the run ended up on, because a process-id check alone would keep
    /// passing if the selection rule were widened to a directory or a file name.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Meitu_never_selects_a_newer_installed_version()
    {
        MeituHarness h = BuildMeitu(registerProcess: false);

        // A second installed version, beside the accepted one exactly as the workstation has it.
        string newerDirectory = Path.Combine(_root, "7.9.9.9");
        Directory.CreateDirectory(newerDirectory);
        string newerExecutable = Path.Combine(newerDirectory, "XiuXiu.exe");
        File.WriteAllBytes(newerExecutable, [0x4D, 0x5A, 0x90, 0x00, 0x09, 0x09, 0x09]);

        // …and it is the one that is running.
        ExternalProcessRef newer = MeituFakes.Process(9990) with { ExecutablePath = newerExecutable };
        h.Locator.Register(newer, MeituFakes.Window(
            handle: 0xB900, owningProcessId: newer.ProcessId, title: MeituFakes.EditorTitle));

        // The accepted binary is launchable and presents the signed empty editor.
        ExternalProcessRef accepted = MeituFakes.Process(5151) with { ExecutablePath = _meituExecutable };
        ExternalWindowRef acceptedWindow = MeituFakes.Window(
            handle: 0xA200, owningProcessId: accepted.ProcessId, title: MeituFakes.EditorTitle);
        h.Locator.LaunchResult = accepted;
        h.Locator.Replace(accepted, acceptedWindow);
        h.Locator.PutInForeground(acceptedWindow);
        h.Elements.SetTexts(acceptedWindow.Handle, [.. MeituFakes.EmptyEditorMarkers]);

        OperationResult<MeituReadiness> ready = await h.Adapter.EnsureReadyAsync(CancellationToken.None);

        ready.IsSuccess.ShouldBeTrue(ready.IsFailure ? ready.Failure.ToString() : "");
        ready.Value.Target.Process.ExecutablePath.ShouldBe(_meituExecutable,
            "the run must be on the accepted binary, not on whichever Meitu happened to be running.");
        ready.Value.WasLaunched.ShouldBeTrue(
            "a process from an unaccepted path is not an instance to attach to.");
        h.Locator.LaunchCount.ShouldBe(1);
    }

    /// <summary>
    /// When a newer instance holds the single-instance slot, PrintFlow fails closed rather than
    /// falling back to it (Epic 11600 Part B Phase 0, §3, §19).
    /// </summary>
    /// <remarks>
    /// The live shape of the drift, reproduced synthetically. Meitu enforces a single instance
    /// itself: with a newer one already up, launching the accepted binary hands off to it and the
    /// launched process exits without ever presenting a window. Part A's smoke hit exactly this
    /// and reported <c>MeituLaunchFailed</c>.
    /// <para>
    /// What is asserted is that this stays a refusal. Availability is the cost, and it is the
    /// right cost: the alternative is enhancing a customer's asset through an executable whose
    /// UI nothing has verified. Nothing is sent to the newer instance on the way out.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_newer_meitu_holding_the_single_instance_slot_is_refused_not_adopted()
    {
        MeituHarness h = BuildMeitu(registerProcess: false);

        string newerDirectory = Path.Combine(_root, "7.9.9.9");
        Directory.CreateDirectory(newerDirectory);
        string newerExecutable = Path.Combine(newerDirectory, "XiuXiu.exe");
        File.WriteAllBytes(newerExecutable, [0x4D, 0x5A, 0x90, 0x00, 0x09, 0x09, 0x09]);

        ExternalProcessRef newer = MeituFakes.Process(9991) with { ExecutablePath = newerExecutable };
        h.Locator.Register(newer, MeituFakes.Window(
            handle: 0xB901, owningProcessId: newer.ProcessId, title: MeituFakes.EditorTitle));

        // The accepted binary launches and immediately exits, which is exactly what the live
        // hand-off looks like from outside — Part A observed "Meitu process 21068 exited before
        // presenting a window". Modelled as both facts, because they are separate: no window
        // ever appears, and the process PrintFlow started is gone.
        ExternalProcessRef handedOff = MeituFakes.Process(5152) with { ExecutablePath = _meituExecutable };
        h.Locator.LaunchResult = handedOff;
        h.Locator.LaunchedProcessNeverShowsWindow = true;
        h.Locator.DeadProcessIds.Add(handedOff.ProcessId);

        OperationResult<MeituReadiness> refused = await h.Adapter.EnsureReadyAsync(CancellationToken.None);

        refused.IsFailure.ShouldBeTrue();
        refused.Failure.Code.ShouldBe(FailureCode.MeituLaunchFailed);

        // The newer instance was never touched: no keystroke, no click, no window activation.
        h.Input.Sends.ShouldBeEmpty();
    }

    /// <summary>
    /// Meitu re-enters through its accepted neutral state on every repetition, and refuses the
    /// moment it is holding something else (Epic 11600 Part B §11, §19).
    /// </summary>
    /// <remarks>
    /// Stage C's claim, made twelve times because once is the claim Part A already proved. The
    /// point of the repetition is that readiness is re-observed rather than remembered: an
    /// adapter that cached "Meitu was fine last time" would pass a single-shot test and fail here
    /// on the iteration where the editor is holding an unexpected asset.
    /// </remarks>
    [Fact]
    public async Task Meitu_re_enters_through_the_accepted_neutral_state_on_every_repetition()
    {
        MeituHarness h = BuildMeitu();

        for (int repetition = 1; repetition <= 12; repetition++)
        {
            h.Elements.SetTexts(h.Window.Handle, [.. MeituFakes.EmptyEditorMarkers]);

            OperationResult<MeituReadiness> ready = await h.Adapter.EnsureReadyAsync(CancellationToken.None);

            ready.IsSuccess.ShouldBeTrue(
                $"repetition {repetition}: " + (ready.IsFailure ? ready.Failure.ToString() : ""));
            ready.Value.WasLaunched.ShouldBeFalse($"repetition {repetition} must reuse the instance.");
        }

        h.Locator.LaunchCount.ShouldBe(0, "twelve consecutive operations must launch nothing.");

        // The thirteenth finds the editor holding an asset nobody signed for. Meitu's allow-list
        // has no "editor with some other document" member, so this is Unknown and stops.
        h.Elements.SetTexts(h.Window.Handle, ["某个客户的图", "保存", "撤销"]);

        OperationResult<MeituReadiness> refused = await h.Adapter.EnsureReadyAsync(CancellationToken.None);

        refused.IsFailure.ShouldBeTrue();
        refused.Failure.Code.ShouldBe(FailureCode.MeituUnknownState);

        // And it is ready again as soon as the editor is empty — the refusal is about the screen,
        // not a latch the adapter set on itself.
        h.Elements.SetTexts(h.Window.Handle, [.. MeituFakes.EmptyEditorMarkers]);
        (await h.Adapter.EnsureReadyAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();
    }

    /// <summary>
    /// Photoshop ownership stays absolute-path based after a stack of prior PrintFlow documents
    /// (Epic 11600 Part B §9, §19).
    /// </summary>
    /// <remarks>
    /// Policy A's safety argument, put under Stage B's load. Twelve consecutive Photoshop jobs
    /// leave twelve PrintFlow-owned documents open, all with names from the same generated
    /// family — so the population of things that could be mistaken for the requested document
    /// grows with every job, and grows in exactly the direction that would defeat a name check.
    /// <para>
    /// The thirteenth open is answered with the right file <i>name</i> from the wrong folder.
    /// That is refused, which is the whole of what makes accumulation harmless: identity is the
    /// absolute path, and a leftover can never satisfy it however similar its name.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Photoshop_ownership_stays_absolute_path_based_after_many_prior_documents()
    {
        PhotoshopHarness h = BuildPhotoshop();

        for (int job = 1; job <= 12; job++)
        {
            WorkspaceFileRef managed = ManagedFile(h, $"PF_SOAK_{job:D2}_WORKING.png");
            StageOpenThenDocument(h, managed.FileName);

            OperationResult<PhotoshopOpenedDocument> opened =
                await h.Adapter.OpenManagedWorkingFileAsync(managed, CancellationToken.None);

            opened.IsSuccess.ShouldBeTrue(
                $"job {job}: " + (opened.IsFailure ? opened.Failure.ToString() : ""));
            opened.Value.Identity.ObservedFullPath.ShouldBe(
                Path.Combine(h.ManagedDirectory, managed.FileName));
        }

        // The thirteenth: the right name, somewhere PrintFlow never named.
        WorkspaceFileRef thirteenth = ManagedFile(h, "PF_SOAK_13_WORKING.png");
        StageOpenThenDocument(h, thirteenth.FileName, identityFolder: @"C:\Users\admin\Desktop");

        OperationResult<PhotoshopOpenedDocument> refused =
            await h.Adapter.OpenManagedWorkingFileAsync(thirteenth, CancellationToken.None);

        refused.IsFailure.ShouldBeTrue();
        refused.Failure.Code.ShouldBe(FailureCode.PhotoshopDocumentIdentityUnconfirmed);
        refused.Failure.Context["w1ActionInvoked"].ShouldBe("false");
        refused.Failure.Context["tiffWritten"].ShouldBe("false");

        // Twelve accumulated documents did not turn into twelve extra ways to be sent input.
        h.Input.Sends.Select(sent => sent.Shortcut).Distinct()
            .ShouldBeSubsetOf([KnownShortcut.OpenFile, KnownShortcut.SaveAsProbe]);
        h.Locator.OwnedDialogs.ShouldBeEmpty();
    }

    // -----------------------------------------------------------------------------------
    // Photoshop harness
    // -----------------------------------------------------------------------------------

    private sealed record PhotoshopHarness(
        ProductionPhotoshopOutputProcessor Adapter,
        GuardedPhotoshopUiDriver Driver,
        FakeWindowLocator Locator,
        FakeVerifiedControlSink Controls,
        RecordingInputSink Input,
        PhotoshopTarget Target,
        string ManagedDirectory);

    private PhotoshopHarness BuildPhotoshop(bool registerProcess = true)
    {
        FakeWindowLocator locator = new();
        FakeVerifiedControlSink controls = new();
        RecordingInputSink input = new();
        RecordingEvidenceSink evidence = new();

        ExternalProcessRef process = new(
            7777, _photoshopExecutable, new DateTimeOffset(2026, 8, 28, 9, 0, 0, TimeSpan.Zero));
        ExternalWindowRef window = PhotoshopFakes.Window();
        PhotoshopTarget target = new(process, window);

        if (registerProcess)
        {
            locator.Register(process, window);
            locator.PutInForeground(window);
        }

        controls.SetVisibleClasses(
            window.Handle, [.. PhotoshopFakes.EditorChromeClasses, PhotoshopFakes.StartScreenClass]);

        StubPhotoshopBaselineProvider baselines = new(PhotoshopBaseline());
        GuardedPhotoshopUiDriver driver = new(
            locator, controls, input, evidence, baselines, FastPhotoshop, TimeProvider.System);

        string managed = Path.Combine(_root, "Working");
        Directory.CreateDirectory(managed);

        ProductionPhotoshopOutputProcessor adapter = new(
            baselines, locator, driver, new StubPhotoshopWorkspace(managed), FastPhotoshop,
            TimeProvider.System);

        return new PhotoshopHarness(adapter, driver, locator, controls, input, target, managed);
    }

    private PhotoshopBaseline PhotoshopBaseline()
    {
        FileVersionInfo info = FileVersionInfo.GetVersionInfo(_photoshopExecutable);
        using FileStream stream = File.OpenRead(_photoshopExecutable);

        return PhotoshopFakes.Baseline() with
        {
            ExecutablePath = _photoshopExecutable,
            ExecutableSha256 = Sha256.FromBytes(SHA256.HashData(stream)),
            AcceptedProductVersion = info.ProductVersion ?? "(unreadable)",
            AcceptedFileVersion = info.FileVersion ?? "(unreadable)",
        };
    }

    /// <summary>A managed Working reference whose file genuinely exists on disk.</summary>
    private static WorkspaceFileRef ManagedFile(PhotoshopHarness h, string fileName)
    {
        File.WriteAllBytes(Path.Combine(h.ManagedDirectory, fileName), [1, 2, 3]);
        return WorkspaceFileRef.Create($"Sessions/S1/Working/{fileName}", WorkspaceArea.Working);
    }

    /// <summary>Puts Photoshop on the editor holding a named document, as a finished job leaves it.</summary>
    private static void ShowDocument(PhotoshopHarness h, string fileName)
    {
        ExternalWindowRef holding = PhotoshopFakes.Window(title: PhotoshopFakes.TitleFor(fileName));
        h.Locator.Replace(h.Target.Process, holding);
        h.Controls.SetVisibleClasses(
            holding.Handle, [.. PhotoshopFakes.EditorChromeClasses, PhotoshopFakes.DocumentClass]);
    }

    /// <summary>
    /// Scripts a Photoshop that raises the Open dialog, then shows a document and answers the
    /// identity probe from <paramref name="identityFolder"/>.
    /// </summary>
    private static void StageOpenThenDocument(
        PhotoshopHarness h, string loadedFileName, string? identityFolder = null)
    {
        ExternalWindowRef openDialog = PhotoshopFakes.Dialog(handle: 0xD1A10, title: "打开");
        ExternalWindowRef saveDialog = PhotoshopFakes.Dialog(handle: 0xD2B20, title: "另存为");

        // Cleared first so this can be called twice in one test. Windows common dialogs are
        // created fresh each time they are raised, and a fake that accumulated their controls
        // would fail the second staging with an ambiguity the real machine never produces.
        h.Controls.ClearControls(openDialog.Handle);
        h.Controls.ClearControls(saveDialog.Handle);

        h.Controls.AddControl(openDialog.Handle, 1148, "ComboBoxEx32");
        h.Controls.AddControl(openDialog.Handle, 1, "Button");
        h.Controls.AddControl(openDialog.Handle, 2, "Button");

        string folder = identityFolder ?? h.ManagedDirectory;
        h.Controls.AddControl(saveDialog.Handle, 1001, "Edit", loadedFileName);
        h.Controls.AddControl(saveDialog.Handle, 1001, "ToolbarWindow32", $"地址: {folder}");
        h.Controls.AddControl(saveDialog.Handle, 2, "Button");

        ExternalWindowRef loaded = PhotoshopFakes.Window(title: PhotoshopFakes.TitleFor(loadedFileName));

        h.Controls.OnPress = (host, controlId) =>
        {
            if (host == openDialog.Handle.Value)
            {
                h.Locator.OwnedDialogs.RemoveAll(d => d.Handle == openDialog.Handle);

                if (controlId == 1)
                {
                    h.Locator.Replace(h.Target.Process, loaded);
                    h.Controls.SetVisibleClasses(
                        loaded.Handle,
                        [.. PhotoshopFakes.EditorChromeClasses, PhotoshopFakes.DocumentClass]);
                }
            }
            else if (host == saveDialog.Handle.Value && controlId == 2)
            {
                h.Locator.OwnedDialogs.RemoveAll(d => d.Handle == saveDialog.Handle);
            }
        };

        h.Input.OnSend = shortcut =>
        {
            if (shortcut == KnownShortcut.OpenFile)
            {
                h.Locator.OwnedDialogs.Add(openDialog);
            }
            else if (shortcut == KnownShortcut.SaveAsProbe)
            {
                h.Locator.OwnedDialogs.Add(saveDialog);
            }
        };
    }

    // -----------------------------------------------------------------------------------
    // Meitu harness
    // -----------------------------------------------------------------------------------

    private sealed record MeituHarness(
        ProductionMeituProcessor Adapter,
        GuardedMeituUiDriver Driver,
        FakeWindowLocator Locator,
        RecordingUiElementProvider Elements,
        RecordingInputSink Input,
        IWorkspace Workspace,
        ExternalProcessRef Process,
        ExternalWindowRef Window);

    private MeituHarness BuildMeitu(bool registerProcess = true)
    {
        FakeWindowLocator locator = new();
        RecordingUiElementProvider elements = new();
        RecordingInputSink input = new();
        RecordingEvidenceSink evidence = new();

        ExternalProcessRef process = MeituFakes.Process() with { ExecutablePath = _meituExecutable };
        ExternalWindowRef window = MeituFakes.Window(
            owningProcessId: process.ProcessId, title: MeituFakes.EditorTitle);

        if (registerProcess)
        {
            locator.Register(process, window);
            locator.PutInForeground(window);
            elements.SetTexts(window.Handle, [.. MeituFakes.EmptyEditorMarkers]);
        }

        StubMeituBaselineProvider baselines = new(MeituBaseline());
        GuardedMeituUiDriver driver = new(
            locator, elements, input, evidence, baselines, FastMeitu, TimeProvider.System);

        IWorkspace workspace = new FileWorkspace(_root);
        ProductionMeituProcessor adapter = new(
            baselines, locator, driver, workspace, new WicFileInspector(),
            new WicMeituTransparencyInspector(), new FileSystemMeituOutputProbe(),
            FastMeitu, TimeProvider.System);

        return new MeituHarness(
            adapter, driver, locator, elements, input, workspace, process, window);
    }

    private MeituBaseline MeituBaseline()
    {
        using FileStream stream = File.OpenRead(_meituExecutable);
        return MeituFakes.Baseline() with
        {
            ExecutablePath = _meituExecutable,
            ExecutableSha256 = Sha256.FromBytes(SHA256.HashData(stream)),
        };
    }

    private static string InfrastructureProjectDirectory()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "PrintFlowStudio.sln")))
        {
            current = current.Parent;
        }

        current.ShouldNotBeNull();
        return Path.Combine(current.FullName, "src", "PrintFlow.Infrastructure");
    }
}
