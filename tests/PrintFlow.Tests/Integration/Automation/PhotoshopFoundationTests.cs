using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Adapters.Photoshop;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Integration.Automation;

/// <summary>
/// The Part A foundation end to end, against a faked operating system
/// (Epic 11400 Part A §5, §8, §12, §18, §19).
/// </summary>
/// <remarks>
/// The executable-identity tests use a real file on disk with its real digest and its real
/// (absent) version information, because the rule under test is precisely "what is on disk
/// agrees with what was signed" — a stubbed hash would assert nothing.
/// </remarks>
public sealed class PhotoshopFoundationTests : IDisposable
{
    private static readonly PhotoshopAutomationOptions FastOptions = new()
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

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "PrintFlowPhotoshopTests", Guid.NewGuid().ToString("N"));

    private readonly string _executable;

    public PhotoshopFoundationTests()
    {
        Directory.CreateDirectory(_root);
        _executable = Path.Combine(_root, "Photoshop.exe");
        File.WriteAllBytes(_executable, [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00]);
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

    private Sha256 ActualDigest()
    {
        using FileStream stream = File.OpenRead(_executable);
        return Sha256.FromBytes(SHA256.HashData(stream));
    }

    /// <summary>The versions the OS actually reports for the temp file, whatever they are.</summary>
    private (string Product, string File) ActualVersions()
    {
        FileVersionInfo info = FileVersionInfo.GetVersionInfo(_executable);
        return (info.ProductVersion ?? "(unreadable)", info.FileVersion ?? "(unreadable)");
    }

    private PhotoshopBaseline BaselineForRealFile(
        Sha256? digestOverride = null,
        string? productVersionOverride = null,
        string? executablePathOverride = null)
    {
        (string product, string file) = ActualVersions();

        return PhotoshopFakes.Baseline() with
        {
            ExecutablePath = executablePathOverride ?? _executable,
            ExecutableSha256 = digestOverride ?? ActualDigest(),
            AcceptedProductVersion = productVersionOverride ?? product,
            AcceptedFileVersion = file,
        };
    }

    private sealed record Harness(
        ProductionPhotoshopOutputProcessor Adapter,
        FakeWindowLocator Locator,
        FakeVerifiedControlSink Controls,
        RecordingInputSink Input,
        RecordingEvidenceSink Evidence,
        PhotoshopTarget Target,
        string ManagedDirectory);

    private Harness Build(
        PhotoshopBaseline baseline,
        string windowTitle = PhotoshopFakes.NoDocumentTitle,
        bool registerProcess = true,
        bool inForeground = true)
    {
        FakeWindowLocator locator = new();
        FakeVerifiedControlSink controls = new();
        RecordingInputSink input = new();
        RecordingEvidenceSink evidence = new();

        ExternalProcessRef process = new(
            7777, baseline.ExecutablePath, new DateTimeOffset(2026, 8, 28, 9, 0, 0, TimeSpan.Zero));
        ExternalWindowRef window = PhotoshopFakes.Window(title: windowTitle);
        PhotoshopTarget target = new(process, window);

        if (registerProcess)
        {
            locator.Register(process, window);
        }

        if (inForeground)
        {
            locator.PutInForeground(window);
        }
        else
        {
            locator.RefuseActivation = true;
        }

        controls.SetVisibleClasses(
            window.Handle,
            [.. PhotoshopFakes.EditorChromeClasses, PhotoshopFakes.StartScreenClass]);

        StubPhotoshopBaselineProvider baselines = new(baseline);
        GuardedPhotoshopUiDriver driver = new(
            locator, controls, input, evidence, baselines, FastOptions, TimeProvider.System);

        // A managed directory that really exists, because the open path checks the file is on
        // disk before it touches Photoshop.
        string managed = Path.Combine(_root, "Working");
        Directory.CreateDirectory(managed);

        ProductionPhotoshopOutputProcessor adapter = new(
            baselines, locator, driver, new StubPhotoshopWorkspace(managed), FastOptions, TimeProvider.System);

        return new Harness(adapter, locator, controls, input, evidence, target, managed);
    }

    // -----------------------------------------------------------------------------------
    // §22.1, §22.2 — executable identity
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// The accepted path, version and digest together let the sequence proceed past identity.
    /// </summary>
    /// <remarks>
    /// Asserted by what happens <i>next</i> rather than by a success: with no process registered,
    /// the run reaches the launch path, which is only reachable once identity has passed.
    /// </remarks>
    [Fact]
    public async Task The_accepted_path_version_and_digest_pass_identity()
    {
        Harness h = Build(BaselineForRealFile(), registerProcess: false);

        OperationResult<PhotoshopReadiness> ready =
            await h.Adapter.EnsureReadyAsync(CancellationToken.None);

        // It got past identity and tried to launch — the failure is about the window, not the binary.
        ready.IsFailure.ShouldBeTrue();
        ready.Failure.Code.ShouldNotBe(FailureCode.PhotoshopNotInstalled);
        h.Locator.LaunchCount.ShouldBe(1);
    }

    /// <summary>A binary whose digest has moved is refused before any window is touched.</summary>
    [Fact]
    public async Task A_wrong_executable_hash_fails_before_any_input()
    {
        Sha256 wrong = Sha256.Parse(
            "abababababababababababababababababababababababababababababababab");
        Harness h = Build(BaselineForRealFile(digestOverride: wrong));

        OperationResult<PhotoshopReadiness> ready =
            await h.Adapter.EnsureReadyAsync(CancellationToken.None);

        ready.IsFailure.ShouldBeTrue();
        ready.Failure.Code.ShouldBe(FailureCode.PhotoshopNotInstalled);
        ready.Failure.Context["inputSent"].ShouldBe("false");
        ready.Failure.Context["expectedSha256"].ShouldBe(wrong.ToString());

        h.Locator.LaunchCount.ShouldBe(0);
        h.Input.Sends.ShouldBeEmpty();
        h.Controls.Writes.ShouldBeEmpty();
    }

    /// <summary>A Photoshop upgrade is a refusal, never an accepted new baseline.</summary>
    [Fact]
    public async Task A_different_version_at_the_accepted_path_fails_before_any_input()
    {
        Harness h = Build(BaselineForRealFile(productVersionOverride: "26.0"));

        OperationResult<PhotoshopReadiness> ready =
            await h.Adapter.EnsureReadyAsync(CancellationToken.None);

        ready.IsFailure.ShouldBeTrue();
        ready.Failure.Code.ShouldBe(FailureCode.PhotoshopNotInstalled);
        ready.Failure.Context["expectedProductVersion"].ShouldBe("26.0");
        h.Input.Sends.ShouldBeEmpty();
    }

    /// <summary>A different installation path is refused, and says which ones were excluded.</summary>
    [Fact]
    public async Task An_absent_accepted_executable_fails_before_any_input()
    {
        Harness h = Build(BaselineForRealFile(
            executablePathOverride: Path.Combine(_root, "NotThere", "Photoshop.exe")));

        OperationResult<PhotoshopReadiness> ready =
            await h.Adapter.EnsureReadyAsync(CancellationToken.None);

        ready.IsFailure.ShouldBeTrue();
        ready.Failure.Code.ShouldBe(FailureCode.PhotoshopNotInstalled);
        ready.Failure.Context["excludedInstallations"].ShouldContain("Adobe Photoshop 2026");
        h.Input.Sends.ShouldBeEmpty();
    }

    /// <summary>
    /// A binary replaced <i>after</i> a passing run is refused on the next one, in the same
    /// process (Epic 11500 Part C §7).
    /// </summary>
    /// <remarks>
    /// The operation-time half of Part C's immutable-baseline decision, and the reason the
    /// restart-bound environment gate is safe enough to keep. The gate reads the signed baseline
    /// once per process; this rule does not. Whatever the gate concluded when PrintFlow started,
    /// the bytes at the accepted path are hashed again before every run, so an executable
    /// replaced mid-session cannot be launched or driven by the adapter that would use it.
    /// <para>
    /// Same adapter, same baseline provider, same process — only the file on disk changed.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_binary_replaced_after_a_passing_run_is_refused_on_the_next_run()
    {
        Harness h = Build(BaselineForRealFile(), registerProcess: false);

        // It got past identity: the failure is about the window, not the binary.
        OperationResult<PhotoshopReadiness> first =
            await h.Adapter.EnsureReadyAsync(CancellationToken.None);
        first.Failure.Code.ShouldNotBe(FailureCode.PhotoshopNotInstalled);
        h.Locator.LaunchCount.ShouldBe(1);

        File.WriteAllBytes(_executable, [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0xFF]);

        OperationResult<PhotoshopReadiness> second =
            await h.Adapter.EnsureReadyAsync(CancellationToken.None);

        second.IsFailure.ShouldBeTrue();
        second.Failure.Code.ShouldBe(FailureCode.PhotoshopNotInstalled);
        second.Failure.Context["inputSent"].ShouldBe("false");
        h.Locator.LaunchCount.ShouldBe(1, "the replaced binary was never launched.");
        h.Input.Sends.ShouldBeEmpty();
    }

    // -----------------------------------------------------------------------------------
    // §22.3 — process and window ownership
    // -----------------------------------------------------------------------------------

    /// <summary>Two candidate instances is a refusal, never a choice.</summary>
    [Fact]
    public async Task Two_running_instances_are_refused_rather_than_chosen_between()
    {
        PhotoshopBaseline baseline = BaselineForRealFile();
        Harness h = Build(baseline);
        h.Locator.Register(
            new ExternalProcessRef(8888, baseline.ExecutablePath, DateTimeOffset.UnixEpoch),
            PhotoshopFakes.Window(handle: 0xB0000, owningProcessId: 8888));

        OperationResult<PhotoshopReadiness> ready =
            await h.Adapter.EnsureReadyAsync(CancellationToken.None);

        ready.IsFailure.ShouldBeTrue();
        ready.Failure.Code.ShouldBe(FailureCode.PhotoshopUnknownState);
        ready.Failure.Context["candidates"].ShouldBe("2");
        h.Input.Sends.ShouldBeEmpty();
    }

    /// <summary>
    /// A window of the wrong class in the right process is not the application frame.
    /// </summary>
    /// <remarks>
    /// Photoshop's process owns dozens of windows — palettes, CEF hosts, an OLE DDE server — and
    /// exactly one is the frame. Matching on ownership alone would let PrintFlow address a
    /// keystroke to a palette.
    /// </remarks>
    [Fact]
    public async Task A_window_of_the_wrong_class_is_not_the_accepted_window()
    {
        PhotoshopBaseline baseline = BaselineForRealFile();
        Harness h = Build(baseline, registerProcess: false);

        ExternalProcessRef process = new(
            7777, baseline.ExecutablePath, DateTimeOffset.UnixEpoch);
        h.Locator.Register(process, PhotoshopFakes.Window(className: "OWL.Palette"));

        OperationResult<PhotoshopReadiness> ready =
            await h.Adapter.EnsureReadyAsync(CancellationToken.None);

        ready.IsFailure.ShouldBeTrue();
        ready.Failure.Code.ShouldBe(FailureCode.PhotoshopWindowNotFound);
        h.Input.Sends.ShouldBeEmpty();
    }

    // -----------------------------------------------------------------------------------
    // §22.5, §22.6 — open-path authority
    // -----------------------------------------------------------------------------------

    /// <summary>The whole Part A sequence: verified, opened, and proved by absolute path.</summary>
    [Fact]
    public async Task A_managed_Working_file_is_opened_and_positively_identified()
    {
        PhotoshopBaseline baseline = BaselineForRealFile();
        Harness h = Build(baseline);

        WorkspaceFileRef managed = WorkspaceFileRef.Create(
            $"Sessions/S1/Working/{PhotoshopFakes.ExpectedFileName}", WorkspaceArea.Working);
        File.WriteAllBytes(Path.Combine(h.ManagedDirectory, managed.FileName), [1, 2, 3]);

        StageOpenThenDocument(h, managed.FileName);

        OperationResult<PhotoshopOpenedDocument> opened =
            await h.Adapter.OpenManagedWorkingFileAsync(managed, CancellationToken.None);

        opened.IsSuccess.ShouldBeTrue();
        opened.Value.State.State.ShouldBe(PhotoshopStartingState.KnownEditorWithExpectedDocument);
        opened.Value.Identity.ObservedFullPath
            .ShouldBe(Path.Combine(h.ManagedDirectory, managed.FileName));
    }

    /// <summary>
    /// The one refusal that protects the customer's original: non-managed areas never reach
    /// Photoshop.
    /// </summary>
    /// <remarks>
    /// Checked before the path is even resolved, so there is no window in which a Source or
    /// Approved reference could be turned into an absolute path and handed over.
    /// </remarks>
    [Theory]
    [InlineData(WorkspaceArea.Source)]
    [InlineData(WorkspaceArea.Approved)]
    [InlineData(WorkspaceArea.Rejected)]
    [InlineData(WorkspaceArea.Logs)]
    public async Task Only_a_managed_Working_reference_may_be_opened(WorkspaceArea area)
    {
        Harness h = Build(BaselineForRealFile());
        WorkspaceFileRef reference = WorkspaceFileRef.Create($"Sessions/S1/x/CUSTOMER.png", area);

        OperationResult<PhotoshopOpenedDocument> opened =
            await h.Adapter.OpenManagedWorkingFileAsync(reference, CancellationToken.None);

        opened.IsFailure.ShouldBeTrue();
        opened.Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);
        opened.Failure.Context["inputSent"].ShouldBe("false");

        h.Input.Sends.ShouldBeEmpty();
        h.Controls.Writes.ShouldBeEmpty();
        h.Locator.LaunchCount.ShouldBe(0);
    }

    /// <summary>
    /// There is no overload taking a path, so no caller can name an arbitrary file.
    /// </summary>
    /// <remarks>
    /// A type-level assertion rather than a behavioural one, and the stronger of the two: a
    /// behavioural test proves a particular arbitrary path is refused, whereas this proves none
    /// can be expressed (§8, §23).
    /// </remarks>
    [Fact]
    public void The_foundation_exposes_no_path_taking_open_overload()
    {
        System.Reflection.MethodInfo[] methods =
            [.. typeof(IPhotoshopAutomationFoundation).GetMethods()];

        methods.ShouldNotContain(method => method.GetParameters()
            .Any(parameter => parameter.ParameterType == typeof(string)));

        methods.Single(m => m.Name == nameof(IPhotoshopAutomationFoundation.OpenManagedWorkingFileAsync))
            .GetParameters()[0].ParameterType.ShouldBe(typeof(WorkspaceFileRef));
    }

    // -----------------------------------------------------------------------------------
    // §22.7 – §22.10 — document identity
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// Expected A, loaded B: refused, with nothing further done to the document.
    /// </summary>
    /// <remarks>
    /// The failure this whole slice exists to prevent. Photoshop is holding a perfectly real
    /// document; it is simply not the one that was handed over, and everything downstream would
    /// have been an irreversible operation against a customer file nobody chose (§12).
    /// </remarks>
    [Fact]
    public async Task A_wrong_document_is_refused_and_nothing_further_happens()
    {
        Harness h = Build(BaselineForRealFile());

        WorkspaceFileRef managed = WorkspaceFileRef.Create(
            $"Sessions/S1/Working/{PhotoshopFakes.ExpectedFileName}", WorkspaceArea.Working);
        File.WriteAllBytes(Path.Combine(h.ManagedDirectory, managed.FileName), [1, 2, 3]);

        // Photoshop reports the right name but from a folder PrintFlow never named.
        StageOpenThenDocument(
            h, managed.FileName, identityFolder: @"C:\Users\admin\Downloads");

        OperationResult<PhotoshopOpenedDocument> opened =
            await h.Adapter.OpenManagedWorkingFileAsync(managed, CancellationToken.None);

        opened.IsFailure.ShouldBeTrue();
        opened.Failure.Code.ShouldBe(FailureCode.PhotoshopDocumentIdentityUnconfirmed);
        opened.Failure.Context["w1ActionInvoked"].ShouldBe("false");
        opened.Failure.Context["tiffWritten"].ShouldBe("false");
        opened.Failure.Context["observedDocument"].ShouldBe(
            @"C:\Users\admin\Downloads\" + managed.FileName);
    }

    /// <summary>A stale document left over from a previous run never satisfies identity.</summary>
    [Fact]
    public async Task A_stale_previous_document_is_refused()
    {
        Harness h = Build(BaselineForRealFile());

        WorkspaceFileRef managed = WorkspaceFileRef.Create(
            $"Sessions/S1/Working/{PhotoshopFakes.ExpectedFileName}", WorkspaceArea.Working);
        File.WriteAllBytes(Path.Combine(h.ManagedDirectory, managed.FileName), [1, 2, 3]);

        // The open is driven, but Photoshop goes on showing the previous document.
        StageOpenThenDocument(h, "PFTEST-PREVIOUS_WORKING.png", identityFolder: h.ManagedDirectory);

        OperationResult<PhotoshopOpenedDocument> opened =
            await h.Adapter.OpenManagedWorkingFileAsync(managed, CancellationToken.None);

        opened.IsFailure.ShouldBeTrue();
        opened.Failure.Code.ShouldBe(FailureCode.PhotoshopDocumentIdentityUnconfirmed);
    }

    /// <summary>No document at all is a refusal, never a pass by absence.</summary>
    [Fact]
    public async Task No_document_after_the_open_is_refused()
    {
        Harness h = Build(BaselineForRealFile());

        WorkspaceFileRef managed = WorkspaceFileRef.Create(
            $"Sessions/S1/Working/{PhotoshopFakes.ExpectedFileName}", WorkspaceArea.Working);
        File.WriteAllBytes(Path.Combine(h.ManagedDirectory, managed.FileName), [1, 2, 3]);

        // The dialog is raised and driven and closes, but no document ever appears.
        ExternalWindowRef dialog = PhotoshopFakes.Dialog(title: "打开");
        h.Controls.AddControl(dialog.Handle, 1148, "ComboBoxEx32");
        h.Controls.AddControl(dialog.Handle, 1, "Button");
        h.Controls.AddControl(dialog.Handle, 2, "Button");
        h.Input.OnSend = shortcut =>
        {
            if (shortcut == KnownShortcut.OpenFile)
            {
                h.Locator.OwnedDialogs.Add(dialog);
            }
        };
        h.Controls.OnPress = (host, _) =>
        {
            if (host == dialog.Handle.Value)
            {
                h.Locator.OwnedDialogs.RemoveAll(d => d.Handle == dialog.Handle);
            }
        };

        OperationResult<PhotoshopOpenedDocument> opened =
            await h.Adapter.OpenManagedWorkingFileAsync(managed, CancellationToken.None);

        opened.IsFailure.ShouldBeTrue();
        opened.Failure.Code.ShouldBe(FailureCode.PhotoshopDocumentIdentityUnconfirmed);
    }

    // -----------------------------------------------------------------------------------
    // §22.12, §22.13 — Part A produces nothing
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// The workflow seam stops at its first stage and produces no output when the exact managed
    /// document cannot be established (Epic 11400 Part C2A §5, §17).
    /// </summary>
    /// <remarks>
    /// Part A asserted this seam refused unconditionally, before examining the request. C2A opens
    /// it, so what is worth asserting changes: the seam now runs, and the property that has to
    /// hold is that stage A's failure ends the run rather than being stepped over. The input file
    /// named below does not exist, so identity cannot be established — and the observable
    /// consequence is that no Action is invoked, no TIFF is written anywhere under the managed
    /// root, and the failure says in as many words that no workflow output was constructed.
    /// <para>
    /// A success here would still be fabricated, and it would still be fabricated in the one
    /// place a Revision is created from. What has changed is only that the refusal is now earned
    /// by a real run rather than granted by an unconditional return.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_workflow_seam_stops_at_identity_and_produces_no_output()
    {
        Harness h = Build(BaselineForRealFile());

        WorkspaceFileRef input = WorkspaceFileRef.Create(
            "Sessions/S1/Working/A_WORKING.png", WorkspaceArea.Working);
        WorkspaceFileRef output = WorkspaceFileRef.Create(
            "Sessions/S1/Working/A_1/A_W1-1PX.tif", WorkspaceArea.Working);

        OperationResult<AdapterOutput> generated = await h.Adapter.GenerateAsync(
            new PhotoshopRequest(
                input,
                PrintDimensions.FromMillimetres(200, 100, SizePreset.Custom),

                // A real plan, calculated by the one domain authority rather than assembled by
                // hand: the refusal below must be a fully-formed request stopping at stage A,
                // not the request failing to be constructible (Epic 11400 B1A.2A §17).
                new FitWithinBoundsPreparation(PrintPreparationPlan.For(
                    RevisionId.From(Guid.Parse("11111111-1111-1111-1111-111111111111")),
                    Sha256.Parse(new string('a', 64)),
                    sourcePixelWidth: 1200,
                    sourcePixelHeight: 600,
                    PrintDimensions.FromMillimetres(200, 100, SizePreset.Custom))),
                new ProductionPresetRef("p", "1", Sha256.Parse(new string('0', 64))),
                WhiteUnderbaseBranch.W1_1px,
                output.FileName,
                WorkspaceDirRef.Create("Sessions/S1/Working"),
                output),
            CancellationToken.None);

        generated.IsFailure.ShouldBeTrue();
        generated.Failure.Context["adapterOutputConstructed"].ShouldBe("false");
        generated.Failure.Context["revisionCreated"].ShouldBe("false");

        // The run never reached a document, so it never reached an Action or a save: no
        // keystroke was sent, and no file appeared anywhere under the managed root.
        h.Input.Sends.ShouldBeEmpty();
        h.Controls.Writes.ShouldBeEmpty();
        h.Controls.Presses.ShouldBeEmpty();
        Directory.GetFiles(h.ManagedDirectory, "*.tif", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    /// <summary>The adapter declares Production, so the environment gate stays authoritative.</summary>
    [Fact]
    public void The_adapter_declares_itself_production_so_the_gate_still_applies()
    {
        Harness h = Build(BaselineForRealFile());

        h.Adapter.Mode.ShouldBe(AdapterExecutionMode.Production);
        h.Adapter.AdapterId.ShouldBe("photoshop-cc2019-production-v1");
    }

    // -----------------------------------------------------------------------------------
    // Staging helper
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// Scripts a fake Photoshop that raises the Open dialog, then shows a document and answers
    /// the identity probe.
    /// </summary>
    private void StageOpenThenDocument(
        Harness h, string loadedFileName, string? identityFolder = null)
    {
        ExternalWindowRef openDialog = PhotoshopFakes.Dialog(handle: 0xD1A10, title: "打开");
        ExternalWindowRef saveDialog = PhotoshopFakes.Dialog(handle: 0xD2B20, title: "另存为");

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
                    // The document appears: the frame retitles and the document marker shows up.
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

        // Each surface is raised by the keystroke that raises it on the real workstation. Staging
        // a dialog up front would put a modal in front of Photoshop before PrintFlow first looks,
        // which is a different situation and one the adapter correctly refuses.
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
}
