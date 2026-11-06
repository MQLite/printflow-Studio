using System.IO;
using System.Reflection;
using System.Security.Cryptography;
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
/// The production adapter end to end: open, enhance, export, and succeed only on a validated
/// file (Epic 11300 Part B2B §3, §16–§19, §27, §33).
/// </summary>
/// <remarks>
/// Only the operating system is faked. The workspace, the file inspector, the output probe, the
/// stability rule and the whole guarded driver are the real implementations, and the fake Meitu
/// writes a real PNG to a real path when its confirm control is invoked — so every claim
/// <c>ProcessAsync</c> makes about a file is made about a file that genuinely exists.
///
/// That matters most for the failure tests. A fake that returned "success" without producing
/// bytes would prove nothing about a validation pipeline; here a corrupt export is genuinely
/// corrupt on disk, an empty one is genuinely empty, and a missing one genuinely never appears.
/// </remarks>
public sealed class ProductionMeituExportTests : IDisposable
{
    private const string RecognitionRoot = "MainWindow.MattingNewParameterWidget.widget.stackedMattingWidget.page1.scrollArea.qt_scrollarea_viewport.scrollAreaWidgetContents.mattingnew";
    private const string RecognitionUndoId = "MainWindow.centralwidget.mainStackedWidget.editPage.subBackgroundWidget.titleWidget.stackedWidget.editorPage.undoButton";
    private const string RecognitionRedoId = "MainWindow.centralwidget.mainStackedWidget.editPage.subBackgroundWidget.titleWidget.stackedWidget.editorPage.redoButton";
    private static string RecognizerId(int index) => RecognitionRoot + ".centerWidget.item1.container1.btnSod" + index;
    private const string SaveId = "MainWindow.editorPage.saveButton";
    private const string CancelId = "MainWindow.MaskDialog.SaveMaskWidget.titleFrame.closeButton";
    private const string SurfaceFileNameId = "MainWindow.MaskDialog.SaveMaskWidget.wName.fileNameEdit";
    private const string SurfaceFormatId = "MainWindow.MaskDialog.SaveMaskWidget.wName.formatCombo";
    private const string SaveAsId = "MainWindow.MaskDialog.SaveMaskWidget.saveAsButton";
    private const string ResultCloseId = "MainWindow.MaskDialog.SaveResultMaskWidget.titleFrame.closeButton";
    private const string ConfirmId = "1";
    private const string OpenControlId =
        "MainWindow.OpenMaskWidget.backgroundWidget.openWidget.widget_2.openButton";
    private const string PickerFileNameId = "1148";
    private const string PickerOpenId = "1";

    private const string WorkingRelative = "Sessions/S_1/Working/A_1/PF_B2B_A.png";
    private const string OutputRelative = "Sessions/S_1/Working/A_1/PF_B2B_A_HD.png";
    private const string CutoutRelative = "Sessions/S_1/Working/A_1/PF_B2B_A_CUTOUT.png";
    private const string ExpectedIdentity = "PF_B2B_A_副本";

    private static readonly MeituAutomationOptions FastOptions = new()
    {
        PollInterval = TimeSpan.FromMilliseconds(5),
        ActivationTimeout = TimeSpan.FromMilliseconds(250),
        DialogTimeout = TimeSpan.FromMilliseconds(250),
        AttachTimeout = TimeSpan.FromMilliseconds(60),
        AutoEnhancementWatchTimeout = TimeSpan.FromMilliseconds(60),
        EnhancementBusyTimeout = TimeSpan.FromMilliseconds(500),
        EnhancementCompletionTimeout = TimeSpan.FromSeconds(1),
        BackgroundRemovalBusyTimeout = TimeSpan.FromMilliseconds(500),
        BackgroundRemovalCompletionTimeout = TimeSpan.FromSeconds(1),
        OutputStabilityTimeout = TimeSpan.FromMilliseconds(400),
        OutputPollInterval = TimeSpan.FromMilliseconds(5),
    };

    private readonly TempWorkspace _temp = new();
    private readonly string _executablePath;
    private readonly Sha256 _executableSha256;

    public ProductionMeituExportTests()
    {
        _executablePath = Path.Combine(_temp.Root, "XiuXiu.exe");
        byte[] bytes = [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x02, 0x01];
        File.WriteAllBytes(_executablePath, bytes);
        _executableSha256 = Sha256.FromBytes(SHA256.HashData(bytes));
    }

    public void Dispose() => _temp.Dispose();

    private static readonly WorkspaceFileRef Working =
        WorkspaceFileRef.Create(WorkingRelative, WorkspaceArea.Working);

    private static readonly WorkspaceFileRef Output =
        WorkspaceFileRef.Create(OutputRelative, WorkspaceArea.Working);

    /// <summary>What the fake Meitu writes when its confirm control is invoked.</summary>
    private enum ExportProduces
    {
        UpscaledPng,
        Nothing,
        EmptyFile,
        CorruptBytes,
        ShrunkPng,
        JpegBytes,
        CutoutPng,
        OpaquePng,
        FullyTransparentPng,
        WrongSizeCutoutPng,
    }

    private sealed class Scenario
    {
        public required ProductionMeituProcessor Adapter { get; init; }

        public required RecordingUiElementProvider Elements { get; init; }

        public required IWorkspace Workspace { get; init; }

        public required string WorkingAbsolute { get; init; }

        public required string OutputAbsolute { get; init; }

        public required WorkspaceFileRef Output { get; init; }

        public required MeituOperation Operation { get; init; }

        /// <summary>
        /// Confirms of the destination dialog specifically, counted as they happen.
        /// </summary>
        /// <remarks>
        /// Not derivable from the invocation list: the picker's Open button and the destination
        /// dialog's confirm control both report automation id "1", on the real machine as much as
        /// here, because both are Windows common dialogs. Counting the id would count both.
        /// </remarks>
        public required int[] DestinationConfirms { get; init; }

        public int Invocations(string automationId) =>
            Elements.Invocations.Count(i => i == automationId);
    }

    /// <summary>
    /// Builds the whole fake: a Meitu that opens a picker, enhances, exports, and produces a
    /// real file at whatever path is written into its destination dialog.
    /// </summary>
    /// <param name="produces">What the confirm control writes.</param>
    /// <param name="moduleRetainedAcrossLoad">
    /// True models the observed hazard: the module is still selected, and opening a document
    /// starts an enhancement with no PrintFlow input at all.
    /// </param>
    /// <param name="staleModuleWithoutWork">
    /// True models the panel from a previous document with nothing running behind it — the
    /// reading §21 forbids treating as an enhancement of this one.
    /// </param>
    private Scenario Build(
        ExportProduces produces = ExportProduces.UpscaledPng,
        bool moduleRetainedAcrossLoad = false,
        bool staleModuleWithoutWork = false,
        bool backgroundRemoval = false,
        bool mutateSourceOnExport = false,
        bool backgroundBusyObserved = true,
        string? cutoutFileName = null,
        IMeituOutputProbe? outputProbe = null,
        bool appTakesFocusAfterExport = false,
        bool refuseCleanupActivation = false,
        Action<BackgroundRemovalMode>? captureBackgroundMode = null,
        BackgroundRemovalMode? reportedBackgroundMode = null,
        bool? instantRecognitionEvidence = null,
        string? reapplyDefect = null)
    {
        ExternalProcessRef process = MeituFakes.Process() with { ExecutablePath = _executablePath };
        ExternalWindowRef editor = MeituFakes.Window(
            owningProcessId: process.ProcessId, title: MeituFakes.EditorTitle);
        ExternalWindowRef picker = MeituFakes.Window(
            handle: 0x5000, owningProcessId: process.ProcessId, title: "打开", className: "#32770");
        ExternalWindowRef surface = MeituFakes.Window(
            handle: 0x6000, owningProcessId: process.ProcessId, title: "Form",
            className: MeituFakes.ExportSurfaceClass) with { OwnerHandle = editor.Handle };
        ExternalWindowRef result = MeituFakes.Window(
            handle: 0x6100, owningProcessId: process.ProcessId, title: "Form",
            className: MeituFakes.ExportSurfaceClass);
        ExternalWindowRef destination = MeituFakes.Window(
            handle: 0x7000, owningProcessId: process.ProcessId, title: "另存为", className: "#32770");

        FakeWindowLocator locator = new();
        RecordingUiElementProvider elements = new();
        locator.Register(process, editor);
        locator.PutInForeground(editor);

        IWorkspace workspace = new FileWorkspace(_temp.Root);
        WorkspaceFileRef output = backgroundRemoval
            ? WorkspaceFileRef.Create(
                cutoutFileName is null
                    ? CutoutRelative
                    : $"Sessions/S_1/Working/A_1/{cutoutFileName}",
                WorkspaceArea.Working)
            : Output;
        string workingAbsolute = workspace.ResolveAbsolute(Working);
        string outputAbsolute = workspace.ResolveAbsolute(output);
        Directory.CreateDirectory(Path.GetDirectoryName(workingAbsolute)!);
        File.WriteAllBytes(
            workingAbsolute,
            backgroundRemoval
                ? SyntheticImages.OpaqueRgbPng(
                    320, 240, (x, y) => x is > 80 and < 240 && y is > 40 and < 220
                        ? ((byte)35, (byte)85, (byte)190)
                        : ((byte)238, (byte)238, (byte)238))
                : SyntheticImages.Png(320, 240, dpi: 300, alpha: true));

        // Meitu starts on its signed empty editor, so the run goes through the whole real
        // sequence: picker, open, load observation, identity, enhancement, export.
        elements.SetTexts(editor.Handle, [.. MeituFakes.EmptyEditorMarkers]);

        elements.AddEditorSaveControl(editor.Handle, process.ProcessId);
        elements.AddEditorCloseControl(editor.Handle, process.ProcessId);
        elements.AddEditorOpenControl(editor.Handle, process.ProcessId);
        elements.AddEnhancementAction(editor.Handle, processId: process.ProcessId);
        elements.AddBackgroundPageAction(
            editor.Handle, MeituFakes.BackgroundActionMarker, process.ProcessId);
        elements.AddBackgroundPageAction(
            editor.Handle, MeituFakes.BackgroundReturnMarker, process.ProcessId);
        elements.AddDialogControl(picker.Handle, PickerFileNameId, "Edit", process.ProcessId);
        elements.AddDialogControl(picker.Handle, PickerOpenId, "Button", process.ProcessId);

        elements.AddExportSurfaceControls(surface.Handle, process.ProcessId);
        elements.AddIdentityDialogControl(
            surface.Handle, CancelId, "", "Button", "IconFontButton",
            UiPatternKind.Invoke, process.ProcessId);
        elements.SetReadValue(SurfaceFileNameId, ExpectedIdentity);
        elements.SetReadValue(SurfaceFormatId, MeituFakes.ExportFormatValue);
        elements.AddExportResultControls(result.Handle, process.ProcessId);
        elements.AddDestinationDialogControls(destination.Handle, process.ProcessId);

        int[] confirms = [0];
        int enhancementReads = 0;
        bool enhancing = false;
        bool removingBackground = false;
        int backgroundReads = 0;
        bool documentLoaded = false;
        bool pickerUp = false;

        void Recognizers(int selected)
        {
            for (int index = 0; index < 4; index++)
            {
                string id = RecognizerId(index);
                foreach (string old in new[] { id, id + ".widget", id + ".widget.label" })
                    elements.Remove(editor.Handle, old);
                var owner = elements.Add(editor.Handle, new UiElementIdentity(
                    "CheckBox", id, "", "CMattingIconButton", process.ProcessId,
                    [UiPatternKind.Invoke, UiPatternKind.Toggle],
                    new UiBounds(100 + index * 60, 220, 51, 80), true, false)
                {
                    IsSelected = selected == index,
                });
                var group = elements.Add(editor.Handle, new UiElementIdentity(
                    "Group", id + ".widget", "", "QWidget", process.ProcessId,
                    [UiPatternKind.Invoke], new UiBounds(102 + index * 60, 222, 47, 76), true, false), owner);
                string label = index switch { 0 => "关闭\n", 1 => "人像\n宠物", 2 => "商品\n货物", _ => "图标\n印章" };
                elements.Add(editor.Handle, new UiElementIdentity(
                    "Text", id + ".widget.label", label, "QLabel", process.ProcessId,
                    [UiPatternKind.Invoke], new UiBounds(102 + index * 60, 260, 47, 32), true, false), group);
            }
        }

        elements.OnReadTextSnapshot = _ =>
        {
            if (!documentLoaded)
            {
                return;
            }

            if (enhancing)
            {
                // Busy for a few reads, then the settled panel — the observed shape of a run.
                elements.SetTexts(
                    editor.Handle,
                    ++enhancementReads <= 3 ? MeituFakes.BusyTexts() : MeituFakes.CompletedTexts());
            }
            else if (removingBackground)
            {
                elements.SetTexts(
                    editor.Handle,
                    backgroundBusyObserved && ++backgroundReads <= 3
                        ? MeituFakes.BackgroundBusyTexts()
                        : MeituFakes.BackgroundCompletedTexts());
            }
        };

        elements.OnInvoke = invoked =>
        {
            if (invoked == RecognitionUndoId)
            {
                Recognizers(0);
                elements.Add(editor.Handle, new UiElementIdentity(
                    "Button", RecognitionRedoId, "\uE2A4", "proui::IconFontButton", process.ProcessId,
                    [UiPatternKind.Invoke], new UiBounds(1036, 50, 32, 32), true, false));
                return;
            }
            int recognizer = Enumerable.Range(1, 3).FirstOrDefault(index => invoked == RecognizerId(index));
            if (recognizer != 0)
            {
                Recognizers(recognizer);
                backgroundReads = 0;
                removingBackground = true;
                return;
            }
            // The picker's Open button and the destination dialog's confirm control both report
            // automation id "1" — they are both Windows common dialogs, and they do on the real
            // machine too. Which one was pressed is decided by which dialog is up, exactly as the
            // driver decides it by which window it verified.
            if (invoked == PickerOpenId && pickerUp)
            {
                pickerUp = false;
                documentLoaded = true;
                locator.Replace(process, editor);
                locator.PutInForeground(editor);

                // What the retained module does to a freshly opened document.
                enhancing = moduleRetainedAcrossLoad;
                enhancementReads = 0;
                elements.SetTexts(
                    editor.Handle,
                    moduleRetainedAcrossLoad ? MeituFakes.BusyTexts()
                    : staleModuleWithoutWork ? MeituFakes.CompletedTexts()
                    : [.. MeituFakes.EditorMarkers]);
                return;
            }

            switch (invoked)
            {
                case OpenControlId:
                    pickerUp = true;
                    locator.Replace(process, editor, picker);
                    locator.PutInForeground(picker);
                    break;

                case MeituFakes.ModuleAutomationId:
                    enhancing = true;
                    enhancementReads = 0;
                    break;

                case MeituFakes.BackgroundActionMarker:
                    removingBackground = true;
                    backgroundReads = 0;
                    Recognizers(3);
                    elements.Remove(editor.Handle, RecognitionUndoId);
                    elements.Remove(editor.Handle, RecognitionRedoId);
                    elements.Add(editor.Handle, new UiElementIdentity(
                        "Button", RecognitionUndoId, "\uE2A3", "proui::IconFontButton", process.ProcessId,
                        [UiPatternKind.Invoke], new UiBounds(1000, 50, 32, 32), true, false));
                    break;

                case MeituFakes.BackgroundReturnMarker:
                    removingBackground = false;
                    elements.SetTexts(editor.Handle, [.. MeituFakes.EditorMarkers]);
                    break;

                case SaveId:
                    locator.OwnedDialogs.Add(surface);
                    locator.PutInForeground(surface);
                    break;

                case CancelId:
                    locator.OwnedDialogs.Remove(surface);
                    locator.PutInForeground(editor);
                    break;

                case SaveAsId:
                    locator.Replace(process, editor, destination);
                    locator.PutInForeground(destination);
                    break;

                case ConfirmId:
                    confirms[0]++;
                    Produce(produces, elements, outputAbsolute);
                    if (mutateSourceOnExport)
                    {
                        using FileStream mutation = new(
                            workingAbsolute, FileMode.Append, FileAccess.Write, FileShare.Read);
                        mutation.WriteByte(0x01);
                    }
                    locator.Replace(process, editor);
                    locator.OwnedDialogs.Remove(surface);
                    locator.OwnedDialogs.Add(result);
                    locator.PutInForeground(editor);
                    break;

                case ResultCloseId:
                    locator.OwnedDialogs.Remove(result);
                    if (appTakesFocusAfterExport)
                    {
                        locator.Foreground = new ForegroundIdentity(
                            new WindowHandle(0x9000), 9999, "PrintFlow.App");
                    }
                    locator.RefuseActivation = refuseCleanupActivation;
                    break;

                case "MainWindow.editorPage.closeButton":
                    documentLoaded = false;
                    enhancing = false;
                    removingBackground = false;
                    elements.SetTexts(editor.Handle, [.. MeituFakes.EmptyEditorMarkers]);
                    break;
            }
        };

        StubMeituBaselineProvider baselines = new(MeituFakes.Baseline() with
        {
            ExecutablePath = _executablePath,
            ExecutableSha256 = _executableSha256,
        });

        GuardedMeituUiDriver driver = new(
            locator, elements, new RecordingInputSink(), new RecordingEvidenceSink(),
            baselines, FastOptions, TimeProvider.System);
        IMeituUiDriver adapterDriver = driver;
        if (captureBackgroundMode is not null || reportedBackgroundMode is not null || instantRecognitionEvidence.HasValue)
        {
            adapterDriver = DispatchProxy.Create<IMeituUiDriver, RecognitionModeProbe>();
            var probe = (RecognitionModeProbe)adapterDriver;
            probe.Inner = driver;
            probe.Capture = captureBackgroundMode ?? (_ => { });
            probe.ReportedMode = reportedBackgroundMode;
            probe.InstantRecognitionEvidence = instantRecognitionEvidence;
            probe.ReapplyDefect = reapplyDefect;
        }

        return new Scenario
        {
            Adapter = new ProductionMeituProcessor(
                baselines, locator, adapterDriver, workspace, new WicFileInspector(),
                new WicMeituTransparencyInspector(),
                outputProbe ?? new FileSystemMeituOutputProbe(), FastOptions, TimeProvider.System),
            Elements = elements,
            Workspace = workspace,
            WorkingAbsolute = workingAbsolute,
            OutputAbsolute = outputAbsolute,
            Output = output,
            Operation = backgroundRemoval ? MeituOperation.RemoveBackground : MeituOperation.Enhance,
            DestinationConfirms = confirms,
        };
    }

    /// <summary>Writes whatever the scenario says Meitu produces, at the path it was given.</summary>
    private static void Produce(
        ExportProduces produces, RecordingUiElementProvider elements, string fallbackPath)
    {
        string path = elements.ValueWrites
            .Where(w => w.Element == "1001")
            .Select(w => w.Value)
            .LastOrDefault() ?? fallbackPath;

        switch (produces)
        {
            case ExportProduces.UpscaledPng:
                File.WriteAllBytes(path, SyntheticImages.Png(1280, 960, dpi: 300, alpha: true));
                break;

            case ExportProduces.ShrunkPng:
                File.WriteAllBytes(path, SyntheticImages.Png(160, 120, dpi: 300, alpha: true));
                break;

            case ExportProduces.EmptyFile:
                File.WriteAllBytes(path, []);
                break;

            case ExportProduces.CorruptBytes:
                File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0xFF, 0xFF]);
                break;

            case ExportProduces.JpegBytes:
                File.WriteAllBytes(path, SyntheticImages.Jpeg(1280, 960));
                break;

            case ExportProduces.CutoutPng:
                File.WriteAllBytes(path, SyntheticImages.PngWithAlpha(
                    320, 240, (x, y) => x < 32 || y < 24 ? (byte)0 : (byte)255));
                break;

            case ExportProduces.OpaquePng:
                File.WriteAllBytes(path, SyntheticImages.PngWithAlpha(320, 240, (_, _) => 255));
                break;

            case ExportProduces.FullyTransparentPng:
                File.WriteAllBytes(path, SyntheticImages.PngWithAlpha(320, 240, (_, _) => 0));
                break;

            case ExportProduces.WrongSizeCutoutPng:
                File.WriteAllBytes(path, SyntheticImages.PngWithAlpha(
                    160, 120, (x, _) => x == 0 ? (byte)0 : (byte)255));
                break;

            case ExportProduces.Nothing:
                break;
        }
    }

    private Task<OperationResult<AdapterOutput>> ProcessAsync(
        Scenario s, BackgroundRemovalMode mode = BackgroundRemovalMode.Automatic) =>
        s.Adapter.ProcessAsync(
            new MeituRequest(
                Working,
                s.Operation,
                s.Operation == MeituOperation.RemoveBackground
                    ? BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent
                    : BackgroundRemovalDecision.Unspecified,
                WorkspaceDirRef.Create("Sessions/S_1/Working/A_1"),
                s.Output)
            {
                BackgroundRemovalMode = mode,
            },
            CancellationToken.None);

    public class RecognitionModeProbe : DispatchProxy
    {
        public IMeituUiDriver Inner { get; set; } = null!;
        public Action<BackgroundRemovalMode> Capture { get; set; } = null!;
        public BackgroundRemovalMode? ReportedMode { get; set; }
        public bool? InstantRecognitionEvidence { get; set; }
        public string? ReapplyDefect { get; set; }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == nameof(IMeituUiDriver.RunBackgroundRemovalAsync))
            {
                Capture(args!.LastOrDefault() is BackgroundRemovalMode mode ? mode : BackgroundRemovalMode.Automatic);
                if (ReportedMode is not null || InstantRecognitionEvidence.HasValue)
                {
                    // Keep the existing synthetic UI/export lifecycle real while controlling
                    // the driver's reported recogniser at the processor's trust boundary.
                    object?[] automaticArgs = (object?[])args!.Clone();
                    automaticArgs[^1] = BackgroundRemovalMode.Automatic;
                    return ReportModeAsync((Task<OperationResult<MeituBackgroundRemovalOutcome>>)
                        method.Invoke(Inner, automaticArgs)!, ReportedMode, InstantRecognitionEvidence, ReapplyDefect);
                }
                return Task.FromResult(OperationResult.Fail<MeituBackgroundRemovalOutcome>(
                    FailureCode.UnknownDialog, "Synthetic recognition-mode boundary reached."));
            }

            return method.Invoke(Inner, args);
        }

        private static async Task<OperationResult<MeituBackgroundRemovalOutcome>> ReportModeAsync(
            Task<OperationResult<MeituBackgroundRemovalOutcome>> run, BackgroundRemovalMode? mode,
            bool? instantRecognitionEvidence, string? reapplyDefect)
        {
            OperationResult<MeituBackgroundRemovalOutcome> result = await run;
            if (result.IsFailure) return result;
            BackgroundRemovalMode reported = mode ?? result.Value.Mode;
            MeituRecognitionReapplyEvidence? evidence = instantRecognitionEvidence == true
                ? MeituRecognitionReapplyEvidenceFixture.Create(MeituFakes.BackgroundRemoval(),
                    result.Value.ObservedDocumentIdentity, result.Value.Target.Process.ProcessId,
                    reapplyDefect == "different-recognizer" ? 3 : reported == BackgroundRemovalMode.Automatic ? 3 : (int)reported)
                : null;
            if (reapplyDefect == "foreign-title" && evidence is not null)
                evidence = evidence with
                {
                    Before = evidence.Before with { Observation = evidence.Before.Observation with { WindowTitle = "foreign editor" } },
                    After = evidence.After with { Observation = evidence.After.Observation with { WindowTitle = "foreign editor" } },
                };
            MeituStateSnapshot completion = evidence?.After ?? result.Value.Completion;
            if (reapplyDefect == "different-completion")
                completion = completion with { Observation = completion.Observation with { WindowTitle = "different observation" } };
            return OperationResult.Ok(result.Value with
            {
                Mode = reported,
                Busy = instantRecognitionEvidence.HasValue ? null : result.Value.Busy,
                Completion = completion,
                ReappliedRecognition = evidence,
            });
        }
    }

    [Theory]
    [InlineData(false, BackgroundRemovalMode.Automatic)]
    [InlineData(true, BackgroundRemovalMode.Automatic)]
    [InlineData(true, BackgroundRemovalMode.PortraitPets)]
    [InlineData(true, BackgroundRemovalMode.Products)]
    [InlineData(true, BackgroundRemovalMode.LogosStamps)]
    public async Task An_instant_result_without_Busy_requires_strict_native_reapply_evidence(
        bool evidencePresent, BackgroundRemovalMode mode)
    {
        Scenario s = Build(backgroundRemoval: true, produces: ExportProduces.CutoutPng,
            instantRecognitionEvidence: evidencePresent, reportedBackgroundMode: mode);
        OperationResult<AdapterOutput> result = await ProcessAsync(s, mode);

        result.IsSuccess.ShouldBe(evidencePresent, result.IsFailure ? result.Failure.ToString() : "");
        s.DestinationConfirms[0].ShouldBe(evidencePresent ? 1 : 0);
        File.Exists(s.OutputAbsolute).ShouldBe(evidencePresent);
        if (!evidencePresent) result.Failure.Code.ShouldBe(FailureCode.MeituUnknownState);
    }

    [Theory]
    [InlineData("different-completion")]
    [InlineData("different-recognizer")]
    [InlineData("foreign-title")]
    public async Task Instant_reapply_evidence_must_describe_the_same_completion_and_requested_recognizer(string defect)
    {
        Scenario s = Build(backgroundRemoval: true, produces: ExportProduces.CutoutPng,
            reportedBackgroundMode: BackgroundRemovalMode.Products,
            instantRecognitionEvidence: true, reapplyDefect: defect);
        OperationResult<AdapterOutput> result = await ProcessAsync(s, BackgroundRemovalMode.Products);
        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.MeituUnknownState);
        s.DestinationConfirms[0].ShouldBe(0);
        File.Exists(s.OutputAbsolute).ShouldBeFalse();
    }

    [Theory]
    [InlineData(BackgroundRemovalMode.Automatic)]
    [InlineData(BackgroundRemovalMode.PortraitPets)]
    [InlineData(BackgroundRemovalMode.Products)]
    [InlineData(BackgroundRemovalMode.LogosStamps)]
    public async Task Explicit_request_recogniser_reaches_the_UI_driver_through_the_foundation(BackgroundRemovalMode mode)
    {
        BackgroundRemovalMode? received = null;
        Scenario s = Build(backgroundRemoval: true, captureBackgroundMode: value => received = value);
        OperationResult<AdapterOutput> result = await s.Adapter.ProcessAsync(new MeituRequest(
            Working, MeituOperation.RemoveBackground,
            BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent,
            WorkspaceDirRef.Create("Sessions/S_1/Working/A_1"), s.Output)
        {
            BackgroundRemovalMode = mode,
        }, CancellationToken.None);

        received.ShouldBe(mode);
        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.UnknownDialog);
        File.Exists(s.OutputAbsolute).ShouldBeFalse();
    }

    [Fact]
    public async Task A_different_reported_recogniser_is_refused_before_export()
    {
        Scenario s = Build(backgroundRemoval: true, produces: ExportProduces.CutoutPng,
            reportedBackgroundMode: BackgroundRemovalMode.Products);
        OperationResult<AdapterOutput> result = await ProcessAsync(s);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);
        s.DestinationConfirms[0].ShouldBe(0);
        File.Exists(s.OutputAbsolute).ShouldBeFalse();
    }

    [Theory]
    [InlineData(BackgroundRemovalMode.Automatic, "mode Automatic")]
    [InlineData(BackgroundRemovalMode.PortraitPets, "mode PortraitPets")]
    [InlineData(BackgroundRemovalMode.Products, "mode Products")]
    [InlineData(BackgroundRemovalMode.LogosStamps, "mode LogosStamps")]
    public async Task Successful_cutout_notes_include_the_verified_actual_recogniser(
        BackgroundRemovalMode mode, string expectedNote)
    {
        Scenario s = Build(backgroundRemoval: true, produces: ExportProduces.CutoutPng,
            reportedBackgroundMode: mode);
        OperationResult<AdapterOutput> result = await s.Adapter.ProcessAsync(new MeituRequest(
            Working, MeituOperation.RemoveBackground,
            BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent,
            WorkspaceDirRef.Create("Sessions/S_1/Working/A_1"), s.Output)
        {
            BackgroundRemovalMode = mode,
        }, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : "");
        result.Value.AdapterNotes.ShouldNotBeNull().ShouldContain(expectedNote);
        s.DestinationConfirms[0].ShouldBe(1);
        File.Exists(s.OutputAbsolute).ShouldBeTrue();
    }

    private sealed class NeverSettlesProbe : IMeituOutputProbe
    {
        private long _length;

        public MeituOutputObservation Probe(string absolutePath) =>
            new(true, ++_length, true);
    }

    // -----------------------------------------------------------------------------
    // Success (§3, §27)
    // -----------------------------------------------------------------------------

    [Fact]
    public async Task Enhancement_releases_its_document_when_PrintFlow_regains_focus_before_cutout()
    {
        Scenario s = Build(appTakesFocusAfterExport: true);

        OperationResult<AdapterOutput> enhanced = await ProcessAsync(s);

        enhanced.IsSuccess.ShouldBeTrue(enhanced.IsFailure ? enhanced.Failure.TechnicalDetail : "");
        enhanced.Value.AdapterNotes.ShouldNotBeNull().ShouldNotContain("WARNING");
        s.Invocations("MainWindow.editorPage.closeButton").ShouldBe(1);
        OperationResult<MeituReadiness> next = await s.Adapter.EnsureReadyAsync(CancellationToken.None);
        next.IsSuccess.ShouldBeTrue(next.IsFailure ? next.Failure.TechnicalDetail : "");
        next.Value.State.State.ShouldBe(MeituStartingState.KnownEditorEmpty);
    }

    [Fact]
    public async Task Refused_cleanup_activation_preserves_the_export_without_sending_document_close()
    {
        Scenario s = Build(appTakesFocusAfterExport: true, refuseCleanupActivation: true);

        OperationResult<AdapterOutput> enhanced = await ProcessAsync(s);

        enhanced.IsSuccess.ShouldBeTrue();
        File.Exists(s.OutputAbsolute).ShouldBeTrue();
        enhanced.Value.AdapterNotes.ShouldNotBeNull().ShouldContain("WARNING");
        s.Invocations("MainWindow.editorPage.closeButton").ShouldBe(0);
        (await s.Adapter.EnsureReadyAsync(CancellationToken.None)).IsFailure.ShouldBeTrue();
    }

    /// <summary>
    /// A complete run returns the managed output reference, and the file is really there.
    /// </summary>
    /// <remarks>
    /// The first success this adapter has ever been able to return. Everything asserted here is
    /// a property of the file system rather than of the screen: the output exists at the path
    /// the attempt named, it is bigger than the working copy in both directions, and the working
    /// copy is still exactly the bytes that were handed over.
    /// </remarks>
    [Fact]
    public async Task A_complete_run_succeeds_with_a_validated_file_on_the_controlled_path()
    {
        Scenario s = Build();
        byte[] inputBefore = await File.ReadAllBytesAsync(s.WorkingAbsolute);

        OperationResult<AdapterOutput> result = await ProcessAsync(s);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.TechnicalDetail : string.Empty);
        result.Value.ProducedFile.ShouldBe(Output);
        File.Exists(s.OutputAbsolute).ShouldBeTrue();

        // §19: the input survived its own attempt, byte for byte.
        (await File.ReadAllBytesAsync(s.WorkingAbsolute)).ShouldBe(inputBefore);

        s.DestinationConfirms[0].ShouldBe(1);
        s.Invocations(MeituFakes.ModuleAutomationId).ShouldBe(1);
    }

    /// <summary>The notes carry the evidence a report needs, without a screenshot.</summary>
    [Fact]
    public async Task A_successful_run_reports_the_source_and_output_facts_it_validated()
    {
        Scenario s = Build();

        OperationResult<AdapterOutput> result = await ProcessAsync(s);

        result.Value.AdapterNotes!.ShouldContain("320x240");
        result.Value.AdapterNotes!.ShouldContain("1280x960");
        result.Value.AdapterNotes!.ShouldContain("format png");
        result.Value.AdapterNotes!.ShouldContain("invoked by PrintFlow");
    }

    /// <summary>After a successful export the editor is returned to its empty state.</summary>
    [Fact]
    public async Task A_successful_run_dismisses_the_result_surface_and_closes_the_document()
    {
        Scenario s = Build();

        OperationResult<AdapterOutput> result = await ProcessAsync(s);

        result.IsSuccess.ShouldBeTrue();
        s.Invocations(ResultCloseId).ShouldBe(1);
        result.Value.AdapterNotes!.ShouldContain("signed empty state");
    }

    [Fact]
    public async Task Cleanup_failure_after_valid_output_is_a_warning_not_lost_success()
    {
        Scenario s = Build();
        Action<string>? scriptedInvoke = s.Elements.OnInvoke;
        s.Elements.OnInvoke = invoked =>
        {
            scriptedInvoke?.Invoke(invoked);
            if (invoked == ResultCloseId)
            {
                s.Elements.SetTexts(new WindowHandle(0x1000), ["unexpected retained editor state"]);
                s.Elements.Remove(new WindowHandle(0x1000), "MainWindow.editorPage.closeButton");
                s.Elements.Remove(new WindowHandle(0x1000), "MainWindow.editorPage.closeButton.textLabel");
            }
        };

        OperationResult<AdapterOutput> result = await ProcessAsync(s);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : "");
        File.Exists(s.OutputAbsolute).ShouldBeTrue();
        string notes = result.Value.AdapterNotes.ShouldNotBeNull();
        notes.ShouldContain("WARNING:");
        notes.ShouldContain("output is valid");
    }

    // -----------------------------------------------------------------------------
    // Output failures (§32)
    // -----------------------------------------------------------------------------

    /// <summary>
    /// The dialog closed and no file appeared: a failure, not a success.
    /// </summary>
    /// <remarks>
    /// §13's rule made concrete. Every screen-level signal in this run is exactly what a
    /// successful export produces — the surface closed, the result panel appeared — and the only
    /// thing that differs is the file system, which is the only thing that counts.
    /// </remarks>
    [Fact]
    public async Task A_dialog_that_closes_without_producing_a_file_fails()
    {
        Scenario s = Build(ExportProduces.Nothing);

        OperationResult<AdapterOutput> result = await ProcessAsync(s);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.OutputMissing);
        result.Failure.Context["exists"].ShouldBe("false");
        File.Exists(s.OutputAbsolute).ShouldBeFalse();
    }

    [Fact]
    public async Task Cancellation_before_export_confirm_sends_no_confirm_and_produces_no_file()
    {
        Scenario s = Build();
        using CancellationTokenSource cancellation = new();
        s.Elements.OnGetValue = element =>
        {
            if (element == "1001")
            {
                cancellation.Cancel();
            }
        };

        OperationResult<AdapterOutput> result = await s.Adapter.ProcessAsync(
            new MeituRequest(
                Working,
                MeituOperation.Enhance,
                BackgroundRemovalDecision.Unspecified,
                WorkspaceDirRef.Create("Sessions/S_1/Working/A_1"),
                s.Output),
            cancellation.Token);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.Cancelled);
        s.DestinationConfirms[0].ShouldBe(0);
        File.Exists(s.OutputAbsolute).ShouldBeFalse();
    }

    [Fact]
    public async Task A_zero_byte_export_fails()
    {
        Scenario s = Build(ExportProduces.EmptyFile);

        OperationResult<AdapterOutput> result = await ProcessAsync(s);

        result.IsFailure.ShouldBeTrue();
        result.Failure.TechnicalDetail.ShouldContain("stayed empty");
    }

    [Fact]
    public async Task A_corrupt_export_fails_and_creates_nothing_usable()
    {
        Scenario s = Build(ExportProduces.CorruptBytes);

        OperationResult<AdapterOutput> result = await ProcessAsync(s);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.OutputUnreadable);
    }

    /// <summary>
    /// A file that is a valid image of the wrong format is refused on its bytes, not its name.
    /// </summary>
    [Fact]
    public async Task An_export_whose_content_is_not_png_fails_despite_the_png_extension()
    {
        Scenario s = Build(ExportProduces.JpegBytes);

        OperationResult<AdapterOutput> result = await ProcessAsync(s);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Context["actualFormat"].ShouldBe("Jpeg");
    }

    [Fact]
    public async Task A_shrunk_enhancement_is_rejected_and_releases_its_document()
    {
        Scenario s = Build(ExportProduces.ShrunkPng);

        OperationResult<AdapterOutput> result = await ProcessAsync(s);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.OutputUnreadable);
        result.Failure.Context["outputPixels"].ShouldBe("160x120");
        s.Invocations(ResultCloseId).ShouldBe(1);
        s.Invocations("MainWindow.editorPage.closeButton").ShouldBe(1);
        OperationResult<MeituReadiness> next = await s.Adapter.EnsureReadyAsync(CancellationToken.None);
        next.IsSuccess.ShouldBeTrue(next.IsFailure ? next.Failure.TechnicalDetail : "");
        next.Value.State.State.ShouldBe(MeituStartingState.KnownEditorEmpty);
        result.Failure.MessageKey.ShouldBe("Failure_MeituEnhancementShrunkOutput");
    }

    // -----------------------------------------------------------------------------
    // Collision (§33)
    // -----------------------------------------------------------------------------

    /// <summary>
    /// A file already sitting on the exact output path stops the run before Meitu is touched.
    /// </summary>
    /// <remarks>
    /// Fail closed, and deliberately without a collision suffix. Uniqueness comes from the
    /// attempt's own directory, so anything already there is something PrintFlow cannot account
    /// for — and quietly writing beside it would hide the fact that an attempt's workspace was
    /// not what it expected.
    /// </remarks>
    [Fact]
    public async Task An_output_path_that_already_exists_fails_closed_without_overwriting_it()
    {
        Scenario s = Build();
        byte[] existing = [1, 2, 3, 4];
        await File.WriteAllBytesAsync(s.OutputAbsolute, existing);

        OperationResult<AdapterOutput> result = await ProcessAsync(s);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);
        result.Failure.Context["exportInvoked"].ShouldBe("false");
        (await File.ReadAllBytesAsync(s.OutputAbsolute)).ShouldBe(existing);
        s.DestinationConfirms[0].ShouldBe(0);
    }

    // -----------------------------------------------------------------------------
    // Auto-started and stale enhancements (§20, §21, §22)
    // -----------------------------------------------------------------------------

    /// <summary>
    /// An enhancement Meitu started by itself is used, not repeated.
    /// </summary>
    /// <remarks>
    /// The module is retained across loads, so this is what an ordinary second attempt looks
    /// like. Busy was watched from the open and the identity probe then confirmed the document,
    /// so the work is provably this attempt's work — and invoking the module now would toggle it
    /// off and discard the result rather than run it again.
    /// </remarks>
    [Fact]
    public async Task An_enhancement_Meitu_started_by_itself_is_exported_without_a_second_invocation()
    {
        Scenario s = Build(moduleRetainedAcrossLoad: true);

        OperationResult<AdapterOutput> result = await ProcessAsync(s);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.TechnicalDetail : string.Empty);
        s.Invocations(MeituFakes.ModuleAutomationId).ShouldBe(0);
        result.Value.AdapterNotes!.ShouldContain("auto-started by Meitu and waited out");
        File.Exists(s.OutputAbsolute).ShouldBeTrue();
    }

    /// <summary>
    /// A module panel with no work behind it produces no export and no Revision.
    /// </summary>
    /// <remarks>
    /// The §21 refusal at adapter level. The panel is the previous document's, so this document
    /// has not been enhanced — and the run stops naming the reason that actually applies rather
    /// than exporting an unenhanced image under an enhanced name.
    /// </remarks>
    [Fact]
    public async Task A_stale_module_panel_produces_no_export_and_no_output_file()
    {
        Scenario s = Build(staleModuleWithoutWork: true);

        OperationResult<AdapterOutput> result = await ProcessAsync(s);

        result.IsFailure.ShouldBeTrue();
        result.Failure.TechnicalDetail.ShouldContain("already selected");
        s.Invocations(MeituFakes.ModuleAutomationId).ShouldBe(0);
        s.DestinationConfirms[0].ShouldBe(0);
        File.Exists(s.OutputAbsolute).ShouldBeFalse();
    }

    // -----------------------------------------------------------------------------
    // Background Removal C2A
    // -----------------------------------------------------------------------------

    [Theory]
    [InlineData(BackgroundRemovalMode.Automatic, 3)]
    [InlineData(BackgroundRemovalMode.PortraitPets, 1)]
    [InlineData(BackgroundRemovalMode.Products, 2)]
    [InlineData(BackgroundRemovalMode.LogosStamps, 3)]
    public async Task A_reviewed_content_cutout_succeeds_through_the_production_seam(
        BackgroundRemovalMode mode, int selectedRecognizer)
    {
        Scenario s = Build(ExportProduces.CutoutPng, backgroundRemoval: true);
        byte[] sourceBefore = await File.ReadAllBytesAsync(s.WorkingAbsolute);

        OperationResult<AdapterOutput> result = await ProcessAsync(s, mode);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.TechnicalDetail : string.Empty);
        result.Value.ProducedFile.ShouldBe(s.Output);
        result.Value.ProducedFile.FileName.ShouldBe("PF_B2B_A_CUTOUT.png");
        (await File.ReadAllBytesAsync(s.WorkingAbsolute)).ShouldBe(sourceBefore);
        s.DestinationConfirms[0].ShouldBe(1);
        s.Invocations(MeituFakes.BackgroundActionMarker).ShouldBe(1);
        s.Invocations(MeituFakes.BackgroundReturnMarker).ShouldBe(1);
        s.Invocations(RecognitionUndoId).ShouldBe(1);
        s.Invocations(RecognizerId(selectedRecognizer)).ShouldBe(1);
        result.Value.AdapterNotes!.ShouldContain($"mode {mode}");
        result.Value.AdapterNotes!.ShouldContain("UseAutomaticSelectionForReviewedContent");
        result.Value.AdapterNotes!.ShouldContain("transparent pixels");
        result.Value.AdapterNotes!.ShouldContain("visible pixels");
    }

    [Fact]
    public async Task An_opaque_PNG_is_not_a_cutout()
    {
        Scenario s = Build(ExportProduces.OpaquePng, backgroundRemoval: true);

        OperationResult<AdapterOutput> result = await ProcessAsync(s);

        result.IsFailure.ShouldBeTrue();
        result.Failure.TechnicalDetail.ShouldContain("completely opaque");
    }

    [Fact]
    public async Task A_fully_transparent_PNG_has_lost_the_foreground()
    {
        Scenario s = Build(ExportProduces.FullyTransparentPng, backgroundRemoval: true);

        OperationResult<AdapterOutput> result = await ProcessAsync(s);

        result.IsFailure.ShouldBeTrue();
        result.Failure.TechnicalDetail.ShouldContain("completely transparent");
    }

    [Fact]
    public async Task A_cutout_that_changes_canvas_dimensions_is_refused()
    {
        Scenario s = Build(ExportProduces.WrongSizeCutoutPng, backgroundRemoval: true);

        OperationResult<AdapterOutput> result = await ProcessAsync(s);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Context["sourcePixels"].ShouldBe("320x240");
        result.Failure.Context["outputPixels"].ShouldBe("160x120");
    }

    [Fact]
    public async Task A_cutout_export_that_mutates_its_source_is_refused()
    {
        Scenario s = Build(
            ExportProduces.CutoutPng, backgroundRemoval: true, mutateSourceOnExport: true);

        OperationResult<AdapterOutput> result = await ProcessAsync(s);

        result.IsFailure.ShouldBeTrue();
        result.Failure.TechnicalDetail.ShouldContain("is not the file it was before the run");
    }

    [Fact]
    public async Task A_stale_completion_panel_without_current_load_Busy_is_not_exported()
    {
        Scenario s = Build(
            ExportProduces.CutoutPng, backgroundRemoval: true, backgroundBusyObserved: false);

        OperationResult<AdapterOutput> result = await ProcessAsync(s);

        result.IsFailure.ShouldBeTrue();
        s.DestinationConfirms[0].ShouldBe(0);
        File.Exists(s.OutputAbsolute).ShouldBeFalse();
    }

    [Theory]
    [InlineData("Name_CUTOUT.jpg")]
    [InlineData("Name_HD.png")]
    public async Task A_wrong_cutout_name_or_extension_is_refused_before_Meitu(
        string fileName)
    {
        Scenario s = Build(
            ExportProduces.CutoutPng, backgroundRemoval: true, cutoutFileName: fileName);

        OperationResult<AdapterOutput> result = await ProcessAsync(s);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Context["exportInvoked"].ShouldBe("false");
        s.Elements.Invocations.ShouldBeEmpty();
        s.DestinationConfirms[0].ShouldBe(0);
    }

    [Fact]
    public async Task An_existing_cutout_destination_is_never_overwritten()
    {
        Scenario s = Build(ExportProduces.CutoutPng, backgroundRemoval: true);
        byte[] existing = [7, 8, 9];
        await File.WriteAllBytesAsync(s.OutputAbsolute, existing);

        OperationResult<AdapterOutput> result = await ProcessAsync(s);

        result.IsFailure.ShouldBeTrue();
        (await File.ReadAllBytesAsync(s.OutputAbsolute)).ShouldBe(existing);
        s.DestinationConfirms[0].ShouldBe(0);
    }

    [Fact]
    public async Task A_cutout_that_never_stabilises_is_refused()
    {
        Scenario s = Build(
            ExportProduces.CutoutPng,
            backgroundRemoval: true,
            outputProbe: new NeverSettlesProbe());

        OperationResult<AdapterOutput> result = await ProcessAsync(s);

        result.IsFailure.ShouldBeTrue();
        result.Failure.TechnicalDetail.ShouldContain("did not settle");
    }
}
