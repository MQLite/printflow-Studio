using System.IO;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Adapters.Meitu;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Tests.Fixtures;

using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Integration.Automation;

/// <summary>
/// The guarded export route: set the signed fields, read them back, name the destination in the
/// signed dialog, confirm once (Epic 11300 Part B2B §5–§13, §33).
/// </summary>
/// <remarks>
/// The fake Meitu here is a state machine for the same reason the Enhancement one is: everything
/// worth proving is a sequence. The Save surface has to appear before its fields are written, the
/// fields have to read back before Save As is invoked, and the destination dialog has to appear
/// before a path is written into it — and a route that did any of those in the wrong order would
/// still pass a test that only looked at the end state.
///
/// Almost every refusal asserts that <c>Invocations</c> contains no confirm control. A failure
/// code proves PrintFlow reported a problem; a zero confirm count proves it did not write a file
/// somewhere and report the problem afterwards.
/// </remarks>
public sealed class GuardedMeituExportTests
{
    private const string ExpectedFile = "PF_B2B_A.png";
    private const string ExpectedIdentity = "PF_B2B_A_副本";
    private const string SaveId = "MainWindow.editorPage.saveButton";
    private const string CancelId = "MainWindow.MaskDialog.SaveMaskWidget.titleFrame.closeButton";
    private const string SurfaceFileNameId = "MainWindow.MaskDialog.SaveMaskWidget.wName.fileNameEdit";
    private const string SurfaceFormatId = "MainWindow.MaskDialog.SaveMaskWidget.wName.formatCombo";
    private const string SaveAsId = "MainWindow.MaskDialog.SaveMaskWidget.saveAsButton";
    private const string ResultCloseId = "MainWindow.MaskDialog.SaveResultMaskWidget.titleFrame.closeButton";
    private const string DestinationConfirmId = "1";
    private const string DestinationCancelId = "2";
    private const string DestinationFileNameId = "1001";

    private static readonly MeituAutomationOptions FastOptions = new()
    {
        PollInterval = TimeSpan.FromMilliseconds(5),
        ActivationTimeout = TimeSpan.FromMilliseconds(40),
        DialogTimeout = TimeSpan.FromMilliseconds(40),
    };

    private static string Destination(string name = "PF_B2B_A_HD.png") =>
        Path.Combine(Path.GetTempPath(), "PrintFlowExportRoute", "A_1", name);

    private sealed class Scenario
    {
        public required FakeWindowLocator Locator { get; init; }

        public required RecordingUiElementProvider Elements { get; init; }

        public required RecordingInputSink Input { get; init; }

        public required GuardedMeituUiDriver Driver { get; init; }

        public required MeituTarget Editor { get; init; }

        public required ExternalWindowRef Surface { get; init; }

        public required ExternalWindowRef DestinationDialog { get; init; }

        public required ExternalProcessRef Process { get; init; }

        public int Invocations(string automationId) =>
            Elements.Invocations.Count(i => i == automationId);

        public string? WroteTo(string automationId) => Elements.ValueWrites
            .Where(w => w.Element == automationId)
            .Select(w => w.Value)
            .LastOrDefault();
    }

    /// <summary>
    /// Builds the fake Meitu: an editor that raises a Save surface, a Save surface that raises a
    /// destination dialog, and a destination dialog that closes when confirmed.
    /// </summary>
    /// <param name="raiseDestinationDialog">False models a Save As that presents nothing.</param>
    /// <param name="destinationDialogCloses">False models Windows asking a question PrintFlow has no answer for.</param>
    /// <param name="formatValue">What the format selector reads back after being written.</param>
    /// <param name="dropWrites">True models a value pattern that reports success without landing.</param>
    private static Scenario Build(
        bool raiseSurface = true,
        bool raiseDestinationDialog = true,
        bool destinationDialogCloses = true,
        string? formatValue = null,
        bool dropWrites = false,
        bool showResultSurfaceAfterConfirm = true,
        MeituBaseline? baseline = null)
    {
        ExternalProcessRef process = MeituFakes.Process();
        ExternalWindowRef editorWindow = MeituFakes.Window(title: MeituFakes.EditorTitle);
        ExternalWindowRef surface = MeituFakes.Window(
            handle: 0x6000, owningProcessId: process.ProcessId, title: "Form",
            className: MeituFakes.ExportSurfaceClass);
        ExternalWindowRef resultSurface = MeituFakes.Window(
            handle: 0x6100, owningProcessId: process.ProcessId, title: "Form",
            className: MeituFakes.ExportSurfaceClass);
        ExternalWindowRef destination = MeituFakes.Window(
            handle: 0x7000, owningProcessId: process.ProcessId, title: "另存为", className: "#32770");

        FakeWindowLocator locator = new();
        RecordingUiElementProvider elements = new();
        RecordingInputSink input = new();

        locator.Register(process, editorWindow);
        locator.PutInForeground(editorWindow);

        elements.SetTexts(editorWindow.Handle, MeituFakes.CompletedTexts());
        elements.AddEditorSaveControl(editorWindow.Handle, process.ProcessId);
        elements.AddEditorCloseControl(editorWindow.Handle, process.ProcessId);

        elements.AddExportSurfaceControls(surface.Handle, process.ProcessId);
        elements.AddIdentityDialogControl(
            surface.Handle, CancelId, "\uE0E6", "Button", "IconFontButton",
            UiPatternKind.Invoke, process.ProcessId);
        elements.SetReadValue(SurfaceFileNameId, ExpectedIdentity);
        elements.SetReadValue(SurfaceFormatId, formatValue ?? MeituFakes.ExportFormatValue);

        elements.AddExportResultControls(resultSurface.Handle, process.ProcessId);
        elements.AddDestinationDialogControls(destination.Handle, process.ProcessId);

        elements.SilentlyDropValueWrites = dropWrites;

        Scenario scenario = new()
        {
            Locator = locator,
            Elements = elements,
            Input = input,
            Driver = new GuardedMeituUiDriver(
                locator, elements, input, new RecordingEvidenceSink(),
                new StubMeituBaselineProvider(baseline ?? MeituFakes.Baseline()),
                FastOptions,
                TimeProvider.System),
            Editor = new MeituTarget(process, editorWindow),
            Surface = surface,
            DestinationDialog = destination,
            Process = process,
        };

        elements.OnInvoke = invoked =>
        {
            if (invoked == SaveId && raiseSurface)
            {
                locator.OwnedDialogs.Add(surface);
                locator.PutInForeground(surface);
            }
            else if (invoked == CancelId)
            {
                locator.OwnedDialogs.Remove(surface);
                locator.PutInForeground(editorWindow);
            }
            else if (invoked == SaveAsId && raiseDestinationDialog)
            {
                locator.Replace(process, editorWindow, destination);
                locator.PutInForeground(destination);
            }
            else if (invoked == DestinationConfirmId && destinationDialogCloses)
            {
                locator.Replace(process, editorWindow);
                locator.OwnedDialogs.Remove(surface);
                if (showResultSurfaceAfterConfirm)
                {
                    locator.OwnedDialogs.Add(resultSurface);
                }

                locator.PutInForeground(editorWindow);
            }
            else if (invoked == DestinationCancelId)
            {
                locator.Replace(process, editorWindow);
                locator.PutInForeground(surface);
            }
            else if (invoked == ResultCloseId)
            {
                locator.OwnedDialogs.Remove(resultSurface);
            }
        };

        return scenario;
    }

    private Task<OperationResult<MeituExportEvidence>> ExportAsync(Scenario s, string? destination = null) =>
        s.Driver.ExportResultAsync(
            s.Editor, ExpectedFile, ExpectedIdentity, destination ?? Destination(), InertAutomationStopSignal.Instance, CancellationToken.None);

    // -----------------------------------------------------------------------------
    // The whole route, once
    // -----------------------------------------------------------------------------

    [Fact]
    public async Task The_export_sets_the_signed_fields_names_the_destination_and_confirms_once()
    {
        Scenario s = Build();

        OperationResult<MeituExportEvidence> export = await ExportAsync(s);

        export.IsSuccess.ShouldBeTrue(export.IsFailure ? export.Failure.TechnicalDetail : string.Empty);
        export.Value.RequestedBaseName.ShouldBe("PF_B2B_A_HD");
        export.Value.ConfirmedFormatValue.ShouldBe("png");
        export.Value.RequestedAbsolutePath.ShouldBe(Destination());
        export.Value.DestinationDialogTitle.ShouldBe("另存为");

        s.Invocations(DestinationConfirmId).ShouldBe(1);
        s.Invocations(DestinationCancelId).ShouldBe(0);
        s.Input.Sends.ShouldBeEmpty();
    }

    /// <summary>
    /// The base name goes to the Save surface and the full path goes to the destination dialog.
    /// </summary>
    /// <remarks>
    /// The two halves of §9 and §10 in one assertion, because getting them the wrong way round is
    /// silent: a full path in Meitu's file-name field produces a file whose name contains
    /// separators, and a base name in the dialog produces one in whatever directory the dialog
    /// happened to open in.
    /// </remarks>
    [Fact]
    public async Task The_surface_receives_the_base_name_and_the_dialog_receives_the_full_path()
    {
        Scenario s = Build();

        await ExportAsync(s);

        s.WroteTo(SurfaceFileNameId).ShouldBe("PF_B2B_A_HD");
        s.WroteTo(SurfaceFileNameId)!.ShouldNotContain(Path.DirectorySeparatorChar);
        s.WroteTo(DestinationFileNameId).ShouldBe(Destination());
    }

    /// <summary>
    /// The order is: fields, then Save As, then the destination, then confirm.
    /// </summary>
    /// <remarks>
    /// Checkable rather than a matter of reading the code. If Save As were invoked before the
    /// fields were set, Meitu would carry its own default name into the dialog and PrintFlow
    /// would be overwriting it there — which happens to work, and would leave the surface's
    /// read-back check proving nothing.
    /// </remarks>
    [Fact]
    public async Task The_fields_are_set_and_read_back_before_Save_As_is_invoked()
    {
        Scenario s = Build();

        await ExportAsync(s);

        int nameWrite = s.Elements.ValueWrites.FindIndex(w => w.Element == SurfaceFileNameId);
        int formatWrite = s.Elements.ValueWrites.FindIndex(w => w.Element == SurfaceFormatId);
        int nameRead = s.Elements.ValueReads.IndexOf(SurfaceFileNameId);
        int saveAs = s.Elements.Invocations.IndexOf(SaveAsId);
        int confirm = s.Elements.Invocations.IndexOf(DestinationConfirmId);

        nameWrite.ShouldBeGreaterThanOrEqualTo(0);
        formatWrite.ShouldBeGreaterThanOrEqualTo(0);
        nameRead.ShouldBeGreaterThanOrEqualTo(0);
        saveAs.ShouldBeGreaterThanOrEqualTo(0);
        confirm.ShouldBeGreaterThan(saveAs);
    }

    /// <summary>
    /// The Save surface's folder field is never written, whatever else the route does.
    /// </summary>
    /// <remarks>
    /// The behavioural half of the architecture test that forbids the literal. Writing that field
    /// succeeds, reads back exactly, and sends the export to Meitu's remembered folder — observed
    /// live — so the only safe rule is that it is never touched at all.
    /// </remarks>
    [Fact]
    public async Task No_value_is_ever_written_to_a_folder_field()
    {
        Scenario s = Build();

        await ExportAsync(s);

        s.Elements.ValueWrites.ShouldAllBe(w => !w.Element.Contains("folder", StringComparison.OrdinalIgnoreCase));
        s.Elements.ValueWrites.Count.ShouldBe(3);
    }

    // -----------------------------------------------------------------------------
    // Destination shape (§4, §11)
    // -----------------------------------------------------------------------------

    /// <summary>
    /// A destination PrintFlow would refuse costs no Save surface at all.
    /// </summary>
    [Theory]
    [InlineData("a_HD.jpg")]
    [InlineData("a_HD")]
    public async Task A_destination_disagreeing_with_the_signed_format_never_raises_the_Save_surface(
        string name)
    {
        Scenario s = Build();

        OperationResult<MeituExportEvidence> export = await ExportAsync(s, Destination(name));

        export.IsFailure.ShouldBeTrue();
        s.Elements.Invocations.ShouldBeEmpty();
        s.Elements.ValueWrites.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_relative_destination_never_raises_the_Save_surface()
    {
        Scenario s = Build();

        OperationResult<MeituExportEvidence> export = await s.Driver.ExportResultAsync(
            s.Editor, ExpectedFile, ExpectedIdentity, "Working/A_1/a_HD.png", InertAutomationStopSignal.Instance, CancellationToken.None);

        export.IsFailure.ShouldBeTrue();
        s.Elements.Invocations.ShouldBeEmpty();
    }

    // -----------------------------------------------------------------------------
    // Read-back (§8, §9, §11)
    // -----------------------------------------------------------------------------

    /// <summary>
    /// A format that will not read back as the signed value stops the export and cancels.
    /// </summary>
    /// <remarks>
    /// §11 says fail closed if the format cannot be positively confirmed, and this is what that
    /// means in practice: no Save As, no destination dialog, no file — and the surface backed out
    /// through its signed cancel control rather than left in front of the operator.
    /// </remarks>
    [Fact]
    public async Task A_format_that_does_not_read_back_as_the_signed_value_stops_before_Save_As()
    {
        Scenario s = Build(formatValue: "jpg", dropWrites: true);

        OperationResult<MeituExportEvidence> export = await ExportAsync(s);

        export.IsFailure.ShouldBeTrue();
        export.Failure.Context["control"].ShouldBe("format");
        export.Failure.Context["exportInvoked"].ShouldBe("false");
        s.Invocations(SaveAsId).ShouldBe(0);
        s.Invocations(DestinationConfirmId).ShouldBe(0);
        s.Invocations(CancelId).ShouldBe(1);
    }

    /// <summary>
    /// A file-name write that does not land stops the export before anything is invoked.
    /// </summary>
    /// <remarks>
    /// The lost-write case, which is not hypothetical: a value pattern can report success without
    /// the text arriving. Without the read-back, Save As would carry Meitu's own default
    /// <c>_副本</c> name into the dialog and the export would land under a name PrintFlow never
    /// chose.
    /// </remarks>
    [Fact]
    public async Task A_file_name_that_does_not_read_back_stops_before_Save_As()
    {
        Scenario s = Build(dropWrites: true);
        s.Elements.SetReadValue(SurfaceFormatId, MeituFakes.ExportFormatValue);

        OperationResult<MeituExportEvidence> export = await ExportAsync(s);

        export.IsFailure.ShouldBeTrue();
        export.Failure.Context["control"].ShouldBe("output base name");
        export.Failure.Context["observed"].ShouldBe(ExpectedIdentity);
        s.Invocations(SaveAsId).ShouldBe(0);
        s.Invocations(CancelId).ShouldBe(1);
    }

    /// <summary>
    /// A destination path that does not read back is cancelled, not confirmed.
    /// </summary>
    /// <remarks>
    /// The most consequential read-back of the three. Confirming a dialog whose path did not land
    /// writes a real file to a real place — the place Windows had selected, which is wherever the
    /// operator was last working.
    /// </remarks>
    [Fact]
    public async Task A_destination_path_that_does_not_read_back_cancels_instead_of_confirming()
    {
        Scenario s = Build();
        s.Elements.OnInvoke += invoked =>
        {
            if (invoked == SaveAsId)
            {
                s.Elements.SilentlyDropValueWrites = true;
                s.Elements.SetReadValue(DestinationFileNameId, @"C:\Users\somebody\Downloads\a_HD.png");
            }
        };

        OperationResult<MeituExportEvidence> export = await ExportAsync(s);

        export.IsFailure.ShouldBeTrue();
        export.Failure.Context["confirmInvoked"].ShouldBe("false");
        export.Failure.Context["intendedPath"].ShouldBe(Destination());
        s.Invocations(DestinationConfirmId).ShouldBe(0);
        s.Invocations(DestinationCancelId).ShouldBe(1);
    }

    /// <summary>Windows paths are case-insensitive, and an echoed casing change is accepted.</summary>
    [Fact]
    public async Task A_destination_echoed_back_in_a_different_casing_is_accepted()
    {
        Scenario s = Build();
        s.Elements.OnInvoke += invoked =>
        {
            if (invoked == SaveAsId)
            {
                s.Elements.SilentlyDropValueWrites = true;
                s.Elements.SetReadValue(DestinationFileNameId, Destination().ToUpperInvariant());
            }
        };

        OperationResult<MeituExportEvidence> export = await ExportAsync(s);

        export.IsSuccess.ShouldBeTrue(export.IsFailure ? export.Failure.TechnicalDetail : string.Empty);
        s.Invocations(DestinationConfirmId).ShouldBe(1);
    }

    // -----------------------------------------------------------------------------
    // Missing surfaces (§12, §32)
    // -----------------------------------------------------------------------------

    [Fact]
    public async Task A_Save_control_that_presents_no_surface_produces_no_export()
    {
        Scenario s = Build(raiseSurface: false);

        OperationResult<MeituExportEvidence> export = await ExportAsync(s);

        export.IsFailure.ShouldBeTrue();
        export.Failure.TechnicalDetail.ShouldContain("no output was produced");
        s.Elements.ValueWrites.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_Save_As_control_that_presents_no_destination_dialog_produces_no_export()
    {
        Scenario s = Build(raiseDestinationDialog: false);

        OperationResult<MeituExportEvidence> export = await ExportAsync(s);

        export.IsFailure.ShouldBeTrue();
        export.Failure.TechnicalDetail.ShouldContain("never named a destination");
        s.Invocations(DestinationConfirmId).ShouldBe(0);
    }

    /// <summary>
    /// A destination dialog that stays open after being confirmed is backed out, never confirmed
    /// again (§12, §33).
    /// </summary>
    /// <remarks>
    /// The likeliest cause is a confirm-overwrite prompt for a file that was not there when the
    /// destination was checked. PrintFlow has no signed answer for it, and pressing the confirm
    /// control a second time would be guessing — a confirm that was accepted and a confirm that
    /// was ignored look identical from here.
    /// </remarks>
    [Fact]
    public async Task A_destination_dialog_that_will_not_close_is_cancelled_and_never_reconfirmed()
    {
        Scenario s = Build(destinationDialogCloses: false);

        OperationResult<MeituExportEvidence> export = await ExportAsync(s);

        export.IsFailure.ShouldBeTrue();
        export.Failure.Context["confirmInvoked"].ShouldBe("once");
        s.Invocations(DestinationConfirmId).ShouldBe(1);
        s.Invocations(DestinationCancelId).ShouldBe(1);
    }

    // -----------------------------------------------------------------------------
    // Guards (§12)
    // -----------------------------------------------------------------------------

    [Fact]
    public async Task An_editor_that_is_not_showing_the_expected_document_produces_no_export()
    {
        Scenario s = Build();
        s.Elements.SetTexts(s.Editor.Window.Handle, "something", "else");

        OperationResult<MeituExportEvidence> export = await ExportAsync(s);

        export.IsFailure.ShouldBeTrue();
        s.Elements.Invocations.ShouldBeEmpty();
        s.Elements.ValueWrites.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_process_that_exits_mid_export_produces_no_confirm()
    {
        Scenario s = Build();
        s.Elements.OnInvoke += invoked =>
        {
            if (invoked == SaveId)
            {
                s.Locator.DeadProcessIds.Add(s.Process.ProcessId);
            }
        };

        OperationResult<MeituExportEvidence> export = await ExportAsync(s);

        export.IsFailure.ShouldBeTrue();
        export.Failure.Code.ShouldBe(FailureCode.MeituTargetLost);
        s.Invocations(DestinationConfirmId).ShouldBe(0);
    }

    /// <summary>
    /// A foreground change while the destination dialog is up stops before the confirm.
    /// </summary>
    /// <remarks>
    /// The confirm control is the single irreversible action of the slice, so the guard closest
    /// to it is the strongest one in the adapter: the dialog itself must be the exact foreground
    /// window, not merely a window of the right process.
    /// </remarks>
    [Fact]
    public async Task A_foreground_change_before_the_confirm_produces_no_file()
    {
        Scenario s = Build();
        s.Elements.OnInvoke += invoked =>
        {
            if (invoked == SaveAsId)
            {
                s.Locator.Foreground = new ForegroundIdentity(new WindowHandle(0xDEAD), 999, "explorer");
            }
        };

        OperationResult<MeituExportEvidence> export = await ExportAsync(s);

        export.IsFailure.ShouldBeTrue();
        export.Failure.Code.ShouldBe(FailureCode.MeituTargetLost);
        s.Invocations(DestinationConfirmId).ShouldBe(0);
        s.Elements.ValueWrites.ShouldAllBe(w => w.Element != DestinationFileNameId);
    }

    /// <summary>
    /// Without signed export evidence there is no export route at all, and no Save probe either.
    /// </summary>
    [Fact]
    public async Task A_chain_that_vouches_for_no_export_evidence_invokes_nothing()
    {
        Scenario s = Build(baseline: MeituFakes.BaselineWithout(export: true));

        OperationResult<MeituExportEvidence> export = await ExportAsync(s);

        export.IsFailure.ShouldBeTrue();
        export.Failure.TechnicalDetail.ShouldContain("no signed description");
        s.Elements.Invocations.ShouldBeEmpty();
    }

    [Fact]
    public async Task Cancellation_before_the_export_produces_no_confirm()
    {
        Scenario s = Build();
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => s.Driver.ExportResultAsync(
            s.Editor, ExpectedFile, ExpectedIdentity, Destination(), InertAutomationStopSignal.Instance, cancelled.Token));

        s.Invocations(DestinationConfirmId).ShouldBe(0);
        s.Elements.ValueWrites.ShouldBeEmpty();
    }

    // -----------------------------------------------------------------------------
    // The post-save surface (§13, §24)
    // -----------------------------------------------------------------------------

    [Fact]
    public async Task The_result_surface_is_dismissed_through_its_own_signed_close_control()
    {
        Scenario s = Build();
        await ExportAsync(s);

        OperationResult<bool> dismissed =
            await s.Driver.DismissExportResultSurfaceAsync(s.Editor, CancellationToken.None);

        dismissed.IsSuccess.ShouldBeTrue();
        dismissed.Value.ShouldBeTrue();
        s.Invocations(ResultCloseId).ShouldBe(1);
        s.Locator.OwnedDialogs.ShouldBeEmpty();
    }

    /// <summary>
    /// The dismissal waits for the surface to actually go away.
    /// </summary>
    /// <remarks>
    /// Returning as soon as the close control was invoked is not good enough, and a live run
    /// proved it: the surface disables the editor while it is up, so the close that follows finds
    /// a blocking modal and reports the document as still loaded — for a close that then succeeds
    /// anyway. The cost of getting this wrong is a cleanup warning on a run that had nothing
    /// wrong with it.
    /// </remarks>
    [Fact]
    public async Task Dismissing_the_result_surface_waits_for_it_to_disappear()
    {
        Scenario s = Build();
        await ExportAsync(s);

        // The surface lingers for two polls after its close control is invoked.
        int reads = 0;
        ExternalWindowRef lingering = s.Locator.OwnedDialogs.Single();
        s.Elements.OnInvoke += invoked =>
        {
            if (invoked == ResultCloseId)
            {
                s.Locator.OwnedDialogs.Add(lingering);
            }
        };

        s.Locator.OnRefresh = _ =>
        {
            if (++reads >= 2)
            {
                s.Locator.OwnedDialogs.Remove(lingering);
            }
        };

        OperationResult<bool> dismissed =
            await s.Driver.DismissExportResultSurfaceAsync(s.Editor, CancellationToken.None);

        dismissed.IsSuccess.ShouldBeTrue(dismissed.IsFailure ? dismissed.Failure.TechnicalDetail : string.Empty);
        dismissed.Value.ShouldBeTrue();
        reads.ShouldBeGreaterThan(1);
    }

    /// <summary>
    /// A surface that will not go away is a cleanup failure, not a silent success.
    /// </summary>
    [Fact]
    public async Task A_result_surface_that_never_disappears_is_reported_as_a_cleanup_failure()
    {
        Scenario s = Build();
        await ExportAsync(s);

        ExternalWindowRef stuck = s.Locator.OwnedDialogs.Single();
        s.Elements.OnInvoke += invoked =>
        {
            if (invoked == ResultCloseId)
            {
                s.Locator.OwnedDialogs.Add(stuck);
            }
        };

        OperationResult<bool> dismissed =
            await s.Driver.DismissExportResultSurfaceAsync(s.Editor, CancellationToken.None);

        dismissed.IsFailure.ShouldBeTrue();
        dismissed.Failure.TechnicalDetail.ShouldContain("neutral state");
    }

    /// <summary>
    /// No result surface is a success, not a failure.
    /// </summary>
    /// <remarks>
    /// Meitu's save-confirmation panel is a setting the operator can switch off. Requiring it
    /// would make cleanup depend on a preference, and would turn a perfectly clean run into a
    /// warning on any machine where someone had unticked it.
    /// </remarks>
    [Fact]
    public async Task No_result_surface_present_is_reported_as_nothing_to_dismiss()
    {
        Scenario s = Build(showResultSurfaceAfterConfirm: false);
        await ExportAsync(s);

        OperationResult<bool> dismissed =
            await s.Driver.DismissExportResultSurfaceAsync(s.Editor, CancellationToken.None);

        dismissed.IsSuccess.ShouldBeTrue();
        dismissed.Value.ShouldBeFalse();
        s.Invocations(ResultCloseId).ShouldBe(0);
    }

    /// <summary>
    /// The Save surface is not mistaken for the result surface, though they share class and title.
    /// </summary>
    /// <remarks>
    /// The disambiguation that a class-only match would get wrong. Here the Save surface is still
    /// up — an export that stopped before confirming — and dismissing it would mean invoking a
    /// close control on a surface PrintFlow had not identified.
    /// </remarks>
    [Fact]
    public async Task The_Save_surface_is_not_dismissed_as_a_result_surface()
    {
        Scenario s = Build();
        s.Locator.OwnedDialogs.Add(s.Surface);

        OperationResult<bool> dismissed =
            await s.Driver.DismissExportResultSurfaceAsync(s.Editor, CancellationToken.None);

        dismissed.IsSuccess.ShouldBeTrue();
        dismissed.Value.ShouldBeFalse();
        s.Elements.Invocations.ShouldBeEmpty();
    }

    /// <summary>
    /// The modified-document prompt is never dismissed: it carries none of the signed markers.
    /// </summary>
    /// <remarks>
    /// §25's boundary, asserted rather than described. The prompt is owned by Meitu and shares
    /// the surface class, so the only thing standing between it and a click is that PrintFlow
    /// requires the positive markers it has actually been shown — and this prompt has none.
    /// </remarks>
    [Fact]
    public async Task The_modified_document_prompt_is_not_dismissed()
    {
        Scenario s = Build();
        ExternalWindowRef prompt = MeituFakes.Window(
            handle: 0x8000, owningProcessId: s.Process.ProcessId, title: "Form",
            className: MeituFakes.ExportSurfaceClass);

        s.Elements.SetTexts(prompt.Handle, "温馨提示", "当前图片已修改，是否保存？", "保存图片", "不用了，谢谢");
        s.Locator.OwnedDialogs.Add(prompt);

        OperationResult<bool> dismissed =
            await s.Driver.DismissExportResultSurfaceAsync(s.Editor, CancellationToken.None);

        dismissed.IsSuccess.ShouldBeTrue();
        dismissed.Value.ShouldBeFalse();
        s.Elements.Invocations.ShouldBeEmpty();
    }
}
