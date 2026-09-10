using System.Collections.Immutable;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Adapters.Meitu;
using PrintFlow.Infrastructure.Automation;

namespace PrintFlow.Tests.Fixtures;

// The `Unit` result type, aliased inside the namespace: `PrintFlow.Tests.Unit` is a namespace
// in this assembly, and an enclosing namespace wins over a compilation-unit alias.
using Unit = PrintFlow.Domain.Results.Unit;

/// <summary>
/// Scriptable stand-ins for the OS seams the Meitu foundation sits on
/// (Epic 11300 Part A §25).
/// </summary>
/// <remarks>
/// Only the operating system is faked here. The classifier, the guarded driver and the
/// production adapter are all the real implementations, because every rule worth testing —
/// verify before input, unknown states stop, working copies only — lives in those and would be
/// proved by nothing if they were doubled.
///
/// The recorders matter as much as the scripts: several tests assert that
/// <see cref="RecordingInputSink.Sends"/> or <see cref="RecordingUiElementProvider.Invocations"/>
/// is <i>empty</i>, which is the only way to state "no input was sent" as a checkable fact
/// rather than an intention.
/// </remarks>
internal static class MeituFakes
{
    internal const string ExecutablePath = @"C:\Fake\MeituApp\XiuXiu\9.9.9.9\XiuXiu.exe";

    internal static readonly Sha256 ExecutableSha256 = Sha256.Parse(
        "1111111111111111111111111111111111111111111111111111111111111111");

    internal static readonly ImmutableArray<string> WelcomeMarkers =
        ["图片编辑", "海报设计", "批处理", "抠图", "AI消除", "AI变清晰", "证件照"];

    /// <summary>The automation id every start-page card on the fake page shares, as on the real one.</summary>
    internal const string CardAutomationId = "StartupWidget.wStartup.funcArea.functionWidget.CardButton";

    internal const string EditorTitle = "美图秀秀-图片编辑";

    internal static readonly ImmutableArray<string> EditorMarkers = ["保存", "撤销", "画布", "图层"];

    /// <summary>The structural card shape, with the real one's shape and none of its real ids.</summary>
    internal static MeituCardShape CardShape() => new(
        LabelControlType: "Text",
        LabelClassName: "QLabel",
        LabelAutomationIdSuffix: ".titleLabel",
        CardControlType: "CheckBox",
        CardClassName: "CardButton",
        CardAutomationIdSuffix: ".CardButton",
        RequiredCardPattern: UiPatternKind.Invoke);

    internal static MeituFileDialogSignature FileDialog() => new(
        Kind: "windows-common-dialog",
        WindowClassName: "#32770",
        AcceptedTitles: ["打开"],
        FileNameAutomationId: "1148",
        FileNameControlType: "Edit",
        ConfirmAutomationId: "1",
        ConfirmControlType: "Button");

    internal static MeituEditorSignature EditorWithDocument() =>
        new(EditorTitle, EditorMarkers, MinimumRequiredMarkers: 3, MeituFileNameLocation.TitleOrVisibleText);

    internal static MeituDocumentIdentitySignature DocumentIdentity() => new(
        EditorWithDocument(),
        SaveMarkerName: "保存",
        SaveControl: new MeituCardShape(
            "Text", "QLabel", ".textLabel", "Button", "OptionButton", ".saveButton", UiPatternKind.Invoke),
        DialogTitle: "Form",
        DialogClassName: "QtSaveDialog",
        FileNameControl: new MeituControlSignature(
            "", ".wName.fileNameEdit", "Edit", "QLineEdit", UiPatternKind.Value),
        CancelControl: new MeituControlSignature(
            "\uE0E6", ".titleFrame.closeButton", "Button", "IconFontButton", UiPatternKind.Invoke),
        OutputBaseNameSuffix: "_副本");

    /// <summary>Markers of the empty editor's open overlay, disjoint from the with-document set.</summary>
    internal static readonly ImmutableArray<string> EmptyEditorMarkers =
        ["打开图片", "新建画布", "手机导入图片", "最近打开"];

    internal static MeituControlSignature EditorOpenControl() => new(
        Name: "打开图片",
        AutomationIdContains: "OpenMaskWidget",
        ControlTypeName: "Button",
        ClassName: "QPushButton",
        RequiredPattern: UiPatternKind.Invoke);

    internal static MeituEditorSignature EditorEmptySignature() => new(
        EditorTitle,
        EmptyEditorMarkers,
        MinimumRequiredMarkers: 3,
        MeituFileNameLocation.TitleOrVisibleText,
        EditorOpenControl());


    /// <summary>The automation id every editor tool button on the fake editor shares, as on the real one.</summary>
    internal const string ModuleAutomationId = "MainWindow.contentsWidget.ModuleButton";

    /// <summary>The fake Enhancement marker. A name, not one of the welcome markers.</summary>
    internal const string EnhancementMarker = "ENH-ACTION";

    /// <summary>
    /// Positive Busy markers, in the shape the real evidence records: two mutually exclusive
    /// progress texts and one abort affordance, of which any two must be visible.
    /// </summary>
    internal static readonly ImmutableArray<string> BusyMarkers =
        ["ENH-RUNNING", "ENH-ELAPSED", "ENH-ABORT"];

    /// <summary>
    /// Positive completion markers: the five controls of the panel the action creates, of which
    /// four must be visible.
    /// </summary>
    internal static readonly ImmutableArray<string> CompletionMarkers =
        ["ENH-PANEL-A", "ENH-PANEL-B", "ENH-PANEL-C", "ENH-PANEL-D", "ENH-PANEL-E"];

    /// <summary>Visible names that positively read as Busy.</summary>
    internal static string[] BusyTexts() =>
        [.. EditorMarkers, .. CompletionMarkers, "ENH-RUNNING", "ENH-ABORT"];

    /// <summary>Visible names that positively read as completed.</summary>
    internal static string[] CompletedTexts() => [.. EditorMarkers, .. CompletionMarkers];

    internal static MeituCloseDocumentSignature CloseDocument() => new(
        MarkerName: "关闭图片",
        Control: new MeituCardShape(
            "Text", "QLabel", ".textLabel",
            "Button", "IconTextButton", ".closeButton", UiPatternKind.Invoke));

    /// <summary>
    /// The signed Enhancement route, with the real one's structure and none of its real values.
    /// </summary>
    /// <remarks>
    /// The owner sits <i>two</i> levels above the marker and the relative id suffix spans both,
    /// because that is what the workstation evidence records and because a fake that collapsed it
    /// to one level would let a one-level walk pass every test and still fail on the real editor.
    /// </remarks>
    internal static MeituEnhancementSignature Enhancement() => new(
        ActionMarkerName: EnhancementMarker,
        ActionControl: new MeituOwnedControlShape(
            MarkerControlType: "Text",
            MarkerClassName: "QLabel",
            MarkerAutomationIdSuffix: ".titleLabel",
            OwnerControlType: "CheckBox",
            OwnerClassName: "ModuleButton",
            OwnerAutomationIdSuffix: ".ModuleButton",
            MarkerRelativeAutomationIdSuffix: ".buttonWidget.titleLabel",
            OwnerAncestorDepth: 2,
            RequiredOwnerPattern: UiPatternKind.Invoke),
        Busy: new MeituBusySignature(BusyMarkers, MinimumRequiredMarkers: 2),
        Completion: new MeituCompletionSignature(
            CompletionMarkers, MinimumRequiredMarkers: 4, RequiresBusyAbsent: true));

    internal const string BackgroundActionMarker = "CUTOUT-ACTION";
    internal const string BackgroundReturnMarker = "CUTOUT-RETURN";
    internal static readonly ImmutableArray<string> BackgroundBusyMarkers =
        ["CUTOUT-RECOGNISING", "CUTOUT-RETURNING", "CUTOUT-COMPOSITING", "CUTOUT-ABORT"];
    internal static readonly ImmutableArray<string> BackgroundCompletionMarkers =
        ["CUTOUT-AUTO", "CUTOUT-LOCAL", "CUTOUT-MANUAL", "CUTOUT-INVERT", "CUTOUT-REMOVE-BG"];

    internal static MeituOwnedControlShape BackgroundPageShape() => new(
        "CheckBox", "PageButton", string.Empty,
        "CheckBox", "PageButton", string.Empty,
        string.Empty, 0, UiPatternKind.Invoke);

    internal static MeituBackgroundRemovalSignature BackgroundRemoval() => new(
        BackgroundActionMarker,
        BackgroundPageShape(),
        BackgroundReturnMarker,
        BackgroundPageShape(),
        "自动选择",
        MeituBackgroundRemovalModePolicy.OperatorOrReviewedContentDecision,
        AutoStartsOnEntry: true,
        new MeituBusySignature(BackgroundBusyMarkers, 2),
        new MeituCompletionSignature(BackgroundCompletionMarkers, 5, true));

    internal static string[] BackgroundBusyTexts() =>
        [.. BackgroundCompletionMarkers, "CUTOUT-RECOGNISING", "CUTOUT-ABORT"];

    internal static string[] BackgroundCompletedTexts() => [.. BackgroundCompletionMarkers];

    /// <summary>Positive markers of Meitu's post-save confirmation surface.</summary>
    internal static readonly ImmutableArray<string> ExportResultMarkers = ["EXP-SAVED", "EXP-OPEN-FOLDER"];

    /// <summary>The exact format value the fake export evidence requires.</summary>
    internal const string ExportFormatValue = "png";

    /// <summary>
    /// The signed export route, with the real one's structure and none of its real values.
    /// </summary>
    /// <remarks>
    /// The Save surface and the result surface deliberately share <see cref="ExportSurfaceClass"/>
    /// and the title <c>Form</c>, because they share both on the real workstation and that
    /// collision is the reason the driver identifies each by its contents. A fake that gave them
    /// different classes would let a class-only match pass every test here and pick the wrong
    /// surface on the real one.
    /// </remarks>
    internal static MeituExportSignature Export() => new(
        SurfaceTitle: "Form",
        SurfaceClassName: ExportSurfaceClass,
        FileNameControl: new MeituControlSignature(
            "", ".wName.fileNameEdit", "Edit", "QLineEdit", UiPatternKind.Value),
        FormatControl: new MeituControlSignature(
            "", ".SaveMaskWidget.wName.formatCombo", "ComboBox", "NoAnimationComboBox", UiPatternKind.Value),
        RequiredFormatValue: ExportFormatValue,
        SaveAsControl: new MeituControlSignature(
            "EXP-SAVE-AS", ".SaveMaskWidget.saveAsButton", "Button", "QPushButton", UiPatternKind.Invoke),
        Destination: new MeituExportDestinationSignature(
            WindowClassName: "#32770",
            AcceptedTitles: ["另存为"],
            FileNameAutomationId: "1001",
            FileNameControlType: "Edit",
            ConfirmAutomationId: "1",
            ConfirmControlType: "Button",
            CancelAutomationId: "2",
            CancelControlType: "Button"),
        Result: new MeituExportResultSignature(
            ExportResultMarkers,
            MinimumRequiredMarkers: 2,
            CloseControl: new MeituControlSignature(
                ExportCloseGlyph, ".SaveResultMaskWidget.titleFrame.closeButton", "Button",
                "IconFontButton", UiPatternKind.Invoke)),
        FormatSelection: ExportFormatSelection());

    internal static MeituExportFormatSelectionSignature ExportFormatSelection() => new(
        InitialFormatValue: "jpg",
        RequiredFormatValue: ExportFormatValue,
        FormatControl: new MeituControlSignature(
            "", ".SaveMaskWidget.wName.formatCombo", "ComboBox", "NoAnimationComboBox",
            UiPatternKind.Invoke),
        FormatControlRequiredPatterns: [UiPatternKind.Invoke, UiPatternKind.Value],
        PopupTitle: "XiuXiu",
        PopupWindowClassName: "Qt51517QWindowPopupSaveBits",
        PopupUiaClassName: "QComboBoxPrivateContainer",
        PopupControlType: "Window",
        PopupRequiredPatterns: [UiPatternKind.Invoke, UiPatternKind.Value, UiPatternKind.Window],
        ItemName: ExportFormatValue,
        ItemControlType: "ListItem",
        ItemClassName: string.Empty,
        ItemAutomationId: string.Empty,
        RequiredParentControlType: "List",
        RequiredParentClassName: "QListView",
        RequiredComboAncestorControlType: "ComboBox",
        RequiredComboAncestorClassName: "NoAnimationComboBox",
        ComboAncestorDepth: 2,
        RequiredActivation: MeituExportFormatActivation.RuntimeDerivedClickablePoint,
        SaveSurfaceMustRemainForeground: true,
        PopupMustDisappear: true);

    /// <summary>The icon-font glyph both Meitu title-bar close controls carry as their name.</summary>
    /// <remarks>
    /// A private-use codepoint, not an empty string, and the fake carries it because the real
    /// control does. Evidence transcribed from a UI dump recorded it as empty — the glyph renders
    /// as nothing — and the signature then matched no element at all. A fake with a blank name
    /// would have let that mistake pass every test here and fail on the real surface.
    /// </remarks>
    internal const string ExportCloseGlyph = "\uE0E6";

    /// <summary>The window class both Meitu save surfaces share.</summary>
    internal const string ExportSurfaceClass = "QtSaveDialog";

    /// <summary>A load on which Meitu did nothing of its own accord — the ordinary case.</summary>
    internal static MeituLoadObservation QuietLoad() => new(
        MeituEnhancementPhase.Unobserved, AutoStartedEnhancement: false, Busy: null, Completion: null);
    /// <summary>
    /// A baseline with the shape of the real one, and none of its real values.
    /// </summary>
    /// <param name="card">The signed card shape, or <c>null</c> to model a chain that vouches for none.</param>
    /// <param name="fileDialog">The signed picker signature, or <c>null</c>.</param>
    /// <param name="documentIdentity">The signed Save-dialog document identity, or <c>null</c>.</param>
    /// <param name="editorEmpty">The signed empty-editor signature, with its open control.</param>
    /// <summary>The automation id of the fake Busy-cancel button, matching the signed shape.</summary>
    internal const string BusyCancelAutomationId =
        "MainWindow.MaskDialog.MaskCenterWidget.LoadingMaskWidget.cancel";

    /// <summary>The exact automation name the cancel carries — the same one the file picker uses.</summary>
    /// <remarks>
    /// Identical to <see cref="DecoyCancelAutomationId"/>'s name on purpose. The workstation
    /// evidence records that Meitu's open picker has a Cancel button named exactly 取消, so a
    /// fake in which the two had different names would let a name-only rule pass every test here
    /// and dismiss a file dialog on the real machine.
    /// </remarks>
    internal const string BusyCancelName = "取消";

    /// <summary>The file picker's own Cancel, which is the live decoy this rule must refuse.</summary>
    internal const string DecoyCancelAutomationId = "2";

    /// <summary>
    /// The signed Busy-cancel route, with the real one's structure and none of its real values
    /// (Epic 11300 Part D2A §6).
    /// </summary>
    /// <remarks>
    /// The named element <i>is</i> the button here, unlike the Enhancement and close-document
    /// shapes where the name belongs to a label above the control that acts. That asymmetry is
    /// what the workstation showed, and reproducing it is the point: a fake that used a
    /// marker-to-owner walk would let a walk-based implementation pass and then climb past the
    /// real button to the progress mask.
    /// </remarks>
    internal static MeituBusyCancelSignature BusyCancel() => new(
        new MeituControlSignature(
            Name: BusyCancelName,
            AutomationIdContains: "MaskDialog.MaskCenterWidget.LoadingMaskWidget.cancel",
            ControlTypeName: "Button",
            ClassName: "QPushButton",
            RequiredPattern: UiPatternKind.Invoke),
        ["LoadingMaskWidget", "SpecialMaskWidget", "MaskDialog"],
        ["Enhance", "RemoveBackground"]);

    internal static MeituBaseline Baseline(
        MeituCardShape? card = null,
        MeituFileDialogSignature? fileDialog = null,
        MeituDocumentIdentitySignature? documentIdentity = null,
        MeituEditorSignature? editorEmpty = null,
        MeituCloseDocumentSignature? closeDocument = null,
        MeituEnhancementSignature? enhancement = null,
        MeituExportSignature? export = null,
        MeituBackgroundRemovalSignature? backgroundRemoval = null,
        MeituBusyCancelSignature? busyCancel = null) => new(
        ExecutablePath,
        ExecutableSha256,
        "9.9.9.9",
        "zh-CN",
        ["美图秀秀", "美图秀秀-图片编辑"],
        "美图秀秀",
        WelcomeMarkers,
        card ?? CardShape(),
        fileDialog ?? FileDialog(),
        documentIdentity ?? DocumentIdentity(),
        editorEmpty ?? EditorEmptySignature(),
        closeDocument ?? CloseDocument(),
        enhancement ?? Enhancement(),
        export ?? Export(),
        backgroundRemoval ?? BackgroundRemoval(),
        busyCancel ?? BusyCancel());

    /// <summary>A baseline whose optional evidence is exactly as supplied, including absent.</summary>
    /// <remarks>
    /// Separate from <see cref="Baseline"/> because that one substitutes defaults for anything
    /// omitted, which is what most tests want and precisely what a fail-closed test must not
    /// get: "the chain vouches for no card shape" has to be expressible.
    /// </remarks>
    internal static MeituBaseline BaselineWithout(
        bool card = false,
        bool fileDialog = false,
        bool documentIdentity = false,
        bool editorEmpty = false,
        bool closeDocument = false,
        bool enhancement = false,
        bool export = false,
        bool backgroundRemoval = false,
        bool busyCancel = false) => new(
        ExecutablePath,
        ExecutableSha256,
        "9.9.9.9",
        "zh-CN",
        ["美图秀秀", "美图秀秀-图片编辑"],
        "美图秀秀",
        WelcomeMarkers,
        card ? null : CardShape(),
        fileDialog ? null : FileDialog(),
        documentIdentity ? null : DocumentIdentity(),
        editorEmpty ? null : EditorEmptySignature(),
        closeDocument ? null : CloseDocument(),
        enhancement ? null : Enhancement(),
        export ? null : Export(),
        backgroundRemoval ? null : BackgroundRemoval(),
        busyCancel ? null : BusyCancel());

    internal static ExternalProcessRef Process(int id = 4242) =>
        new(id, ExecutablePath, new DateTimeOffset(2026, 8, 24, 9, 0, 0, TimeSpan.Zero));

    internal static ExternalWindowRef Window(
        nint handle = 0x1000,
        int owningProcessId = 4242,
        string title = "美图秀秀",
        string className = "MeituMainWindow",
        bool enabled = true) =>
        new(new WindowHandle(handle), owningProcessId, title, className,
            new WindowBounds(0, 0, 1920, 1040), IsVisible: true, IsMinimised: false, IsEnabled: enabled);

    internal static MeituTarget Target() => new(Process(), Window());
}

/// <summary>A baseline provider that returns whatever the test scripted.</summary>
internal sealed class StubMeituBaselineProvider : IMeituBaselineProvider
{
    private readonly OperationResult<MeituBaseline> _result;

    public StubMeituBaselineProvider(MeituBaseline baseline) =>
        _result = OperationResult.Ok(baseline);

    public StubMeituBaselineProvider(OperationFailure failure) =>
        _result = OperationResult.Fail<MeituBaseline>(failure);

    public OperationResult<MeituBaseline> GetVerifiedBaseline() => _result;
}

/// <summary>A scriptable operating system: which processes exist, which windows they own, and what is in front.</summary>
internal sealed class FakeWindowLocator : IExternalAppWindowLocator
{
    private readonly Dictionary<int, List<ExternalWindowRef>> _windows = [];

    public List<ExternalProcessRef> RunningProcesses { get; } = [];

    public List<ExternalWindowRef> OwnedDialogs { get; } = [];

    /// <summary>What the OS reports as foreground. Defaults to "something else entirely".</summary>
    public ForegroundIdentity Foreground { get; set; } =
        new(new WindowHandle(0xDEAD), 999, "explorer");

    /// <summary>How many times a launch was requested — asserted to be zero when reusing an instance.</summary>
    public int LaunchCount { get; private set; }

    /// <summary>When set, a launch registers this process and its windows.</summary>
    public ExternalProcessRef? LaunchResult { get; set; }

    /// <summary>When true, a launched process never presents a window — the launch-timeout case.</summary>
    public bool LaunchedProcessNeverShowsWindow { get; set; }

    public HashSet<int> DeadProcessIds { get; } = [];

    /// <summary>Points the foreground at <paramref name="window"/>, as a successful activation would.</summary>
    public void PutInForeground(ExternalWindowRef window) =>
        Foreground = new ForegroundIdentity(window.Handle, window.OwningProcessId, "XiuXiu");

    public void Register(ExternalProcessRef process, params ExternalWindowRef[] windows)
    {
        RunningProcesses.Add(process);
        _windows[process.ProcessId] = [.. windows];
    }

    public void Replace(ExternalProcessRef process, params ExternalWindowRef[] windows) =>
        _windows[process.ProcessId] = [.. windows];

    public OperationResult<IReadOnlyList<ExternalProcessRef>> FindProcessesByExecutable(
        string executableAbsolutePath) =>
        OperationResult.Ok<IReadOnlyList<ExternalProcessRef>>(
        [
            .. RunningProcesses.Where(p =>
                string.Equals(p.ExecutablePath, executableAbsolutePath, StringComparison.OrdinalIgnoreCase)),
        ]);

    public OperationResult<ExternalProcessRef> Launch(string executableAbsolutePath)
    {
        LaunchCount++;

        if (LaunchResult is not { } launched)
        {
            return OperationResult.Fail<ExternalProcessRef>(
                FailureCode.MeituLaunchFailed, "The test scripted no launch result.");
        }

        RunningProcesses.Add(launched);
        if (!LaunchedProcessNeverShowsWindow && !_windows.ContainsKey(launched.ProcessId))
        {
            _windows[launched.ProcessId] = [MeituFakes.Window(owningProcessId: launched.ProcessId)];
        }

        return OperationResult.Ok(launched);
    }

    public bool IsAlive(ExternalProcessRef process) => !DeadProcessIds.Contains(process.ProcessId);

    public OperationResult<IReadOnlyList<ExternalWindowRef>> FindTopLevelWindows(ExternalProcessRef process) =>
        OperationResult.Ok<IReadOnlyList<ExternalWindowRef>>(
            _windows.TryGetValue(process.ProcessId, out List<ExternalWindowRef>? windows) ? windows : []);

    public OperationResult<IReadOnlyList<ExternalWindowRef>> FindOwnedDialogs(
        ExternalProcessRef process, ExternalWindowRef mainWindow) =>
        OperationResult.Ok<IReadOnlyList<ExternalWindowRef>>(OwnedDialogs);

    /// <summary>
    /// Runs before each refresh, so a test can model a window that closes after a few looks.
    /// </summary>
    /// <remarks>
    /// The counterpart of <see cref="RecordingUiElementProvider.OnInvoke"/> for the locator, and
    /// needed for the same reason: "the surface went away" is something that happens <i>over</i>
    /// a few observations, and a fake whose windows vanish instantly can only prove that a wait
    /// terminates, never that it waits.
    /// </remarks>
    public Action<WindowHandle>? OnRefresh { get; set; }

    public OperationResult<ExternalWindowRef> Refresh(WindowHandle handle)
    {
        OnRefresh?.Invoke(handle);

        foreach (List<ExternalWindowRef> windows in _windows.Values)
        {
            ExternalWindowRef? match = windows.FirstOrDefault(w => w.Handle == handle);
            if (match is not null)
            {
                return OperationResult.Ok(match);
            }
        }

        ExternalWindowRef? dialog = OwnedDialogs.FirstOrDefault(w => w.Handle == handle);
        return dialog is not null
            ? OperationResult.Ok(dialog)
            : OperationResult.Fail<ExternalWindowRef>(
                FailureCode.MeituWindowNotFound, $"Window {handle} does not exist in this fake OS.");
    }

    public int ActivationRequests { get; private set; }

    /// <summary>When true, activation is requested but the foreground never changes.</summary>
    public bool RefuseActivation { get; set; }

    public OperationResult<Unit> Activate(ExternalWindowRef window)
    {
        ActivationRequests++;
        if (!RefuseActivation)
        {
            PutInForeground(window);
        }

        return OperationResult.Ok();
    }

    public OperationResult<ForegroundIdentity> ReadForeground() => OperationResult.Ok(Foreground);
}

/// <summary>One element in the fake automation tree.</summary>
/// <remarks>
/// Carries a parent link because Part B1's targeting rule is an <i>ancestry</i> rule: the fake
/// has to be able to answer "what is above this marker?" wrongly as well as rightly, or the
/// tests that prove PrintFlow refuses a wrong ancestor would have nothing to refuse.
/// </remarks>
internal sealed class FakeUiElement
{
    public required UiElementIdentity Identity { get; init; }

    public FakeUiElement? Parent { get; init; }

    public override string ToString() => Identity.ToString();
}

/// <summary>Records every invoke and value write, and answers lookups from a scripted tree.</summary>
internal sealed class RecordingUiElementProvider : IUiElementProvider
{
    private readonly Dictionary<nint, List<FakeUiElement>> _tree = [];
    private readonly Dictionary<nint, UiElementIdentity> _roots = [];

    /// <summary>Automation names each window reports, keyed by handle.</summary>
    public Dictionary<nint, List<string>> Texts { get; } = [];

    /// <summary>What was invoked, by automation id where there is one. Asserted <i>empty</i> more often than not.</summary>
    public List<string> Invocations { get; } = [];

    /// <summary>Elements activated through the live-clickable-point fallback.</summary>
    public List<string> Clicks { get; } = [];

    public List<ExternalProcessRef> ClickAcceptedProcesses { get; } = [];

    public List<WindowHandle> ClickExpectedForegroundWindows { get; } = [];

    public List<(string Element, string Value)> ValueWrites { get; } = [];

    public List<string> ValueReads { get; } = [];

    /// <summary>Adds one element beneath a window.</summary>
    public FakeUiElement Add(WindowHandle window, UiElementIdentity identity, FakeUiElement? parent = null)
    {
        FakeUiElement element = new() { Identity = identity, Parent = parent };
        if (!_tree.TryGetValue(window.Value, out List<FakeUiElement>? elements))
        {
            elements = [];
            _tree[window.Value] = elements;
        }

        elements.Add(element);
        return element;
    }

    public void SetRootIdentity(WindowHandle window, UiElementIdentity identity) =>
        _roots[window.Value] = identity;

    public void Remove(WindowHandle window, string automationId)
    {
        if (_tree.TryGetValue(window.Value, out List<FakeUiElement>? elements))
        {
            elements.RemoveAll(element =>
                string.Equals(element.Identity.AutomationId, automationId, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// Adds a start-page card shaped exactly like the one the workstation evidence records: a
    /// <c>CardButton</c> owning a <c>titleLabel</c> that carries the marker text.
    /// </summary>
    /// <param name="window">The window the card lives beneath.</param>
    /// <param name="marker">The signed marker the label is named with.</param>
    /// <param name="cardId">The card's automation id; the label's is this plus <c>.titleLabel</c>.</param>
    /// <param name="processId">The process both elements report.</param>
    public FakeUiElement AddStartPageCard(
        WindowHandle window,
        string marker,
        string cardId = MeituFakes.CardAutomationId,
        int processId = 4242)
    {
        FakeUiElement card = Add(window, new UiElementIdentity(
            ControlTypeName: "CheckBox",
            AutomationId: cardId,
            Name: string.Empty,
            ClassName: "CardButton",
            ProcessId: processId,
            SupportedPatterns: [UiPatternKind.Invoke, UiPatternKind.Value, UiPatternKind.Toggle],
            Bounds: new UiBounds(812, 520, 196, 70),
            IsEnabled: true,
            IsOffscreen: false));

        Add(window, new UiElementIdentity(
            ControlTypeName: "Text",
            AutomationId: cardId + ".titleLabel",
            Name: marker,
            ClassName: "QLabel",
            ProcessId: processId,
            SupportedPatterns: [UiPatternKind.Invoke],
            Bounds: new UiBounds(880, 535, 112, 22),
            IsEnabled: true,
            IsOffscreen: false),
            card);

        return card;
    }

    /// <summary>Adds the empty editor's open control, shaped like the observed one.</summary>
    public FakeUiElement AddEditorOpenControl(WindowHandle window, int processId = 4242) =>
        Add(window, new UiElementIdentity(
            ControlTypeName: "Button",
            AutomationId: "MainWindow.OpenMaskWidget.backgroundWidget.openWidget.widget_2.openButton",
            Name: "打开图片",
            ClassName: "QPushButton",
            ProcessId: processId,
            SupportedPatterns: [UiPatternKind.Invoke, UiPatternKind.Value],
            Bounds: new UiBounds(1014, 435, 250, 44),
            IsEnabled: true,
            IsOffscreen: false));

    public FakeUiElement AddEditorSaveControl(WindowHandle window, int processId = 4242)
    {
        const string saveId = "MainWindow.editorPage.saveButton";
        FakeUiElement owner = Add(window, new UiElementIdentity(
            "Button", saveId, "", "OptionButton", processId,
            [UiPatternKind.Invoke, UiPatternKind.Value], new UiBounds(1700, 50, 70, 36), true, false));
        Add(window, new UiElementIdentity(
            "Text", saveId + ".textLabel", "保存", "QLabel", processId,
            [UiPatternKind.Invoke], new UiBounds(1710, 55, 24, 24), true, false), owner);
        return owner;
    }


    /// <summary>
    /// Adds an editor tool button shaped exactly like the observed one: a <c>ModuleButton</c>
    /// whose <c>titleLabel</c> carries the marker text, with a layout <c>QWidget</c> in between.
    /// </summary>
    /// <param name="window">The window the control lives beneath.</param>
    /// <param name="marker">The signed marker the label is named with.</param>
    /// <param name="moduleId">The owner's automation id; every real sibling shares one.</param>
    /// <param name="processId">The process all three elements report.</param>
    /// <param name="enabled">Whether the owner is enabled.</param>
    /// <param name="offscreen">Whether the owner is scrolled out of view.</param>
    /// <remarks>
    /// The intermediate <c>QWidget</c> is given <c>InvokePattern</c> on purpose. It is the decoy
    /// the real editor contains, and a rule that walked up to the first invokable ancestor would
    /// select it, report success and do nothing.
    /// </remarks>
    public FakeUiElement AddEnhancementAction(
        WindowHandle window,
        string marker = MeituFakes.EnhancementMarker,
        string moduleId = MeituFakes.ModuleAutomationId,
        int processId = 4242,
        bool enabled = true,
        bool offscreen = false)
    {
        FakeUiElement owner = Add(window, new UiElementIdentity(
            ControlTypeName: "CheckBox",
            AutomationId: moduleId,
            Name: string.Empty,
            ClassName: "ModuleButton",
            ProcessId: processId,
            SupportedPatterns: [UiPatternKind.Invoke, UiPatternKind.Value, UiPatternKind.Toggle],
            Bounds: new UiBounds(452, 618, 260, 56),
            IsEnabled: enabled,
            IsOffscreen: offscreen));

        FakeUiElement layout = Add(window, new UiElementIdentity(
            ControlTypeName: "Group",
            AutomationId: moduleId + ".buttonWidget",
            Name: string.Empty,
            ClassName: "QWidget",
            ProcessId: processId,
            SupportedPatterns: [UiPatternKind.Invoke, UiPatternKind.Value],
            Bounds: new UiBounds(452, 618, 260, 56),
            IsEnabled: true,
            IsOffscreen: false),
            owner);

        Add(window, new UiElementIdentity(
            ControlTypeName: "Text",
            AutomationId: moduleId + ".buttonWidget.titleLabel",
            Name: marker,
            ClassName: "QLabel",
            ProcessId: processId,
            SupportedPatterns: [UiPatternKind.Invoke],
            Bounds: new UiBounds(494, 634, 67, 24),
            IsEnabled: true,
            IsOffscreen: false),
            layout);

        return owner;
    }

    public FakeUiElement AddBackgroundPageAction(
        WindowHandle window,
        string marker,
        int processId = 4242,
        bool enabled = true,
        bool offscreen = false)
    {
        return Add(window, new UiElementIdentity(
            "CheckBox", string.Empty, marker, "PageButton", processId,
            [UiPatternKind.Invoke, UiPatternKind.Value, UiPatternKind.Toggle],
            new UiBounds(372, marker == MeituFakes.BackgroundActionMarker ? 625 : 337, 56, 56),
            enabled, offscreen));
    }

    /// <summary>
    /// Adds the progress mask's cancel button and its three signed ancestors, shaped exactly
    /// like the observed one (Epic 11300 Part D2A §6).
    /// </summary>
    /// <param name="window">The window the mask lives beneath.</param>
    /// <param name="automationId">The button's automation id; the default is the signed one.</param>
    /// <param name="className">The button's class; a wrong one must be refused.</param>
    /// <param name="ancestorClasses">
    /// The classes of the three ancestors, innermost first. Supplying different ones is how a
    /// test states "the button is somewhere else in the tree".
    /// </param>
    /// <remarks>
    /// The ancestors are real elements rather than implied, because the rule walks to them and a
    /// fake without them would let an implementation that skipped the ancestry check pass.
    /// </remarks>
    public FakeUiElement AddBusyCancel(
        WindowHandle window,
        string automationId = MeituFakes.BusyCancelAutomationId,
        string className = "QPushButton",
        string[]? ancestorClasses = null,
        int processId = 4242,
        bool enabled = true,
        bool offscreen = false)
    {
        string[] classes = ancestorClasses ?? ["LoadingMaskWidget", "SpecialMaskWidget", "MaskDialog"];

        FakeUiElement? parent = null;
        for (int depth = classes.Length - 1; depth >= 0; depth--)
        {
            parent = Add(window, new UiElementIdentity(
                depth == classes.Length - 1 ? "Window" : "Group",
                $"MainWindow.ancestor{depth}",
                string.Empty,
                classes[depth],
                processId,
                [UiPatternKind.Invoke, UiPatternKind.Value],
                new UiBounds(-3, -3, 1920, 1040),
                true,
                false),
                parent);
        }

        return Add(window, new UiElementIdentity(
            ControlTypeName: "Button",
            AutomationId: automationId,
            Name: MeituFakes.BusyCancelName,
            ClassName: className,
            ProcessId: processId,
            SupportedPatterns: [UiPatternKind.Invoke, UiPatternKind.Value],
            Bounds: new UiBounds(904, 537, 104, 28),
            IsEnabled: enabled,
            IsOffscreen: offscreen),
            parent);
    }

    /// <summary>
    /// Adds the open picker's own Cancel button — the live decoy, named exactly 取消
    /// (Epic 11300 Part D2A §8).
    /// </summary>
    /// <remarks>
    /// Its parent is a <c>#32770</c> dialog rather than the progress mask, and its id is the
    /// bare <c>2</c> the workstation reported. Both are what must refuse it; neither its name
    /// nor its control type does.
    /// </remarks>
    public FakeUiElement AddDecoyPickerCancel(WindowHandle window, int processId = 4242)
    {
        FakeUiElement dialog = Add(window, new UiElementIdentity(
            "Window", string.Empty, "打开图片", "#32770", processId,
            [UiPatternKind.Window], new UiBounds(0, 0, 1920, 1040), true, false));

        return Add(window, new UiElementIdentity(
            "Button", MeituFakes.DecoyCancelAutomationId, MeituFakes.BusyCancelName, "Button",
            processId, [UiPatternKind.Invoke], new UiBounds(1807, 994, 88, 26), true, false),
            dialog);
    }

    /// <summary>Adds the loaded editor's close-document control, shaped like the observed one.</summary>
    public FakeUiElement AddEditorCloseControl(WindowHandle window, int processId = 4242)
    {
        const string closeId = "MainWindow.editorPage.closeButton";
        FakeUiElement owner = Add(window, new UiElementIdentity(
            "Button", closeId, "", "IconTextButton", processId,
            [UiPatternKind.Invoke, UiPatternKind.Value], new UiBounds(1307, 215, 90, 36), true, false));
        Add(window, new UiElementIdentity(
            "Text", closeId + ".textLabel", "关闭图片", "QLabel", processId,
            [UiPatternKind.Invoke], new UiBounds(1341, 221, 48, 24), true, false), owner);
        return owner;
    }
    public FakeUiElement AddIdentityDialogControl(
        WindowHandle dialog,
        string automationId,
        string name,
        string controlType,
        string className,
        UiPatternKind pattern,
        int processId = 4242) =>
        Add(dialog, new UiElementIdentity(
            controlType, automationId, name, className, processId,
            [pattern], new UiBounds(100, 100, 240, 30), true, false));

    /// <summary>
    /// Adds the three controls PrintFlow uses on Meitu's Save surface, plus the cancel control
    /// the identity probe signs (Epic 11300 Part B2B §8).
    /// </summary>
    /// <remarks>
    /// The format selector starts at the signed value, as the real one does, so the ordinary
    /// path exercises "write, then read back and find it already right" rather than a change
    /// the real application was never observed accepting.
    /// </remarks>
    public void AddExportSurfaceControls(WindowHandle surface, int processId = 4242)
    {
        Add(surface, new UiElementIdentity(
            "Edit", "MainWindow.MaskDialog.SaveMaskWidget.wName.fileNameEdit", string.Empty,
            "QLineEdit", processId, [UiPatternKind.Value], new UiBounds(10, 10, 180, 28), true, false));

        FakeUiElement format = Add(surface, new UiElementIdentity(
            "ComboBox", "MainWindow.MaskDialog.SaveMaskWidget.wName.formatCombo", string.Empty,
            "NoAnimationComboBox", processId, [UiPatternKind.Invoke, UiPatternKind.Value],
            new UiBounds(200, 10, 64, 28), true, false));

        Add(surface, new UiElementIdentity(
            "Button", "MainWindow.MaskDialog.SaveMaskWidget.saveAsButton", "EXP-SAVE-AS",
            "QPushButton", processId, [UiPatternKind.Invoke], new UiBounds(10, 60, 84, 36), true, false));

        SetReadValue(format.Identity.AutomationId, MeituFakes.ExportFormatValue);
    }

    /// <summary>Adds the transient format popup and its signed png-item ancestry.</summary>
    public FakeUiElement? AddExportFormatPopupControls(
        WindowHandle popup,
        int processId = 4242,
        bool includePngItem = true,
        bool pngItemEnabled = true,
        int? pngItemProcessId = null)
    {
        FakeUiElement combo = Add(popup, new UiElementIdentity(
            "ComboBox", string.Empty, string.Empty, "NoAnimationComboBox", processId,
            [UiPatternKind.Invoke, UiPatternKind.Value], new UiBounds(200, 38, 72, 134), true, false));
        FakeUiElement list = Add(popup, new UiElementIdentity(
            "List", string.Empty, string.Empty, "QListView", processId,
            [UiPatternKind.Invoke, UiPatternKind.Value], new UiBounds(207, 45, 58, 120), true, false), combo);
        return includePngItem ? Add(popup, new UiElementIdentity(
            "ListItem", string.Empty, MeituFakes.ExportFormatValue, string.Empty, pngItemProcessId ?? processId,
            [UiPatternKind.Invoke, UiPatternKind.Value, UiPatternKind.SelectionItem],
            new UiBounds(207, 69, 58, 24), pngItemEnabled, false), list) : null;
    }

    /// <summary>
    /// Adds Meitu's post-save confirmation surface: its markers and its own close control.
    /// </summary>
    /// <remarks>
    /// Given the same window class and title as the Save surface, because the real ones share
    /// both. Anything that tells them apart here has to be something that tells them apart there.
    /// </remarks>
    public void AddExportResultControls(WindowHandle surface, int processId = 4242)
    {
        SetTexts(surface, [.. MeituFakes.ExportResultMarkers]);

        Add(surface, new UiElementIdentity(
            "Button", "MainWindow.MaskDialog.SaveResultMaskWidget.titleFrame.closeButton",
            MeituFakes.ExportCloseGlyph,
            "IconFontButton", processId, [UiPatternKind.Invoke], new UiBounds(260, 4, 16, 16), true, false));
    }

    /// <summary>Adds the destination dialog's file-name, confirm and cancel controls.</summary>
    /// <remarks>
    /// The file-name id is deliberately carried by a <c>ComboBox</c> as well as the <c>Edit</c>,
    /// because it is on the real dialog: the field is an Edit nested in a ComboBox and both
    /// report id 1001. A resolver that took the first match would get the ComboBox.
    /// </remarks>
    public void AddDestinationDialogControls(WindowHandle dialog, int processId = 4242)
    {
        Add(dialog, new UiElementIdentity(
            "ComboBox", "1001", "文件名:", "AppControlHost", processId,
            [UiPatternKind.Value], new UiBounds(0, 0, 200, 24), true, false));

        Add(dialog, new UiElementIdentity(
            "Edit", "1001", "文件名:", "Edit", processId,
            [UiPatternKind.Value], new UiBounds(0, 0, 200, 24), true, false));

        Add(dialog, new UiElementIdentity(
            "Button", "1", "保存(S)", "Button", processId,
            [UiPatternKind.Invoke], new UiBounds(0, 40, 80, 24), true, false));

        Add(dialog, new UiElementIdentity(
            "Button", "2", "取消", "Button", processId,
            [UiPatternKind.Invoke], new UiBounds(90, 40, 80, 24), true, false));
    }

    /// <summary>Adds a picker control with the identity the signed dialog evidence records.</summary>
    public FakeUiElement AddDialogControl(
        WindowHandle dialog, string automationId, string controlType, int processId = 4242) =>
        Add(dialog, new UiElementIdentity(
            ControlTypeName: controlType,
            AutomationId: automationId,
            Name: automationId,
            ClassName: controlType,
            ProcessId: processId,
            SupportedPatterns: [UiPatternKind.Invoke, UiPatternKind.Value],
            Bounds: new UiBounds(0, 0, 200, 24),
            IsEnabled: true,
            IsOffscreen: false));

    public void SetTexts(WindowHandle window, params string[] texts) =>
        Texts[window.Value] = [.. texts];

    public OperationResult<UiElementRef> Find(WindowHandle root, UiElementQuery query)
    {
        List<FakeUiElement> matches = Matching(root, query);
        return matches.Count > 0
            ? OperationResult.Ok(Reference(matches[0], root))
            : OperationResult.Fail<UiElementRef>(
                FailureCode.MeituOpenInputFailed, $"Nothing matching {query} was scripted.");
    }

    public OperationResult<IReadOnlyList<UiElementRef>> FindAll(WindowHandle root, UiElementQuery query) =>
        OperationResult.Ok<IReadOnlyList<UiElementRef>>(
            [.. Matching(root, query).Select(e => Reference(e, root))]);

    public OperationResult<UiElementIdentity> DescribeWindow(WindowHandle root) =>
        _roots.TryGetValue(root.Value, out UiElementIdentity? identity)
            ? OperationResult.Ok(identity)
            : OperationResult.Fail<UiElementIdentity>(
                FailureCode.MeituOpenInputFailed, $"No root identity was scripted for {root}.");

    public OperationResult<UiElementIdentity> Describe(UiElementRef element) =>
        element.Native is FakeUiElement fake
            ? OperationResult.Ok(fake.Identity)
            : OperationResult.Fail<UiElementIdentity>(
                FailureCode.MeituOpenInputFailed, $"'{element.Name}' is not a scripted element.");

    public OperationResult<UiElementRef> GetParent(UiElementRef element)
    {
        if (element.Native is not FakeUiElement fake)
        {
            return OperationResult.Fail<UiElementRef>(
                FailureCode.MeituOpenInputFailed, $"'{element.Name}' is not a scripted element.");
        }

        return fake.Parent is { } parent
            ? OperationResult.Ok(Reference(parent, element.RootWindow))
            : OperationResult.Fail<UiElementRef>(
                FailureCode.MeituOpenInputFailed, $"'{element.Name}' was scripted with no parent.");
    }

    /// <summary>
    /// Runs after an invoke is recorded, so a test can model what invoking actually causes.
    /// </summary>
    /// <remarks>
    /// Needed because the interesting confirmation case is a <i>sequence</i>: PrintFlow invokes
    /// the picker's Open button, and only then does Meitu become an editor showing the file. A
    /// fake whose window state was fixed up front could only ever assert against a screen that
    /// already looked right, which would prove nothing about confirmation happening after the
    /// act rather than before it.
    /// </remarks>
    public Action<string>? OnInvoke { get; set; }

    public OperationResult<Unit> Invoke(UiElementRef element)
    {
        string description = Describes(element);
        Invocations.Add(description);
        OnInvoke?.Invoke(description);
        return OperationResult.Ok();
    }

    public Action<string>? OnClickAtLiveClickablePoint { get; set; }

    public OperationResult<Unit> ClickAtLiveClickablePoint(
        UiElementRef element,
        ExternalProcessRef acceptedProcess,
        WindowHandle expectedForegroundWindow)
    {
        string description = Describes(element);
        Clicks.Add(description);
        ClickAcceptedProcesses.Add(acceptedProcess);
        ClickExpectedForegroundWindows.Add(expectedForegroundWindow);
        OnClickAtLiveClickablePoint?.Invoke(description);
        return OperationResult.Ok();
    }

    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    public void SetReadValue(string elementDescription, string value) =>
        _values[elementDescription] = value;

    /// <summary>When set, a write is silently dropped — the lost-write case the read-back catches.</summary>
    public bool SilentlyDropValueWrites { get; set; }

    public OperationResult<string> GetValue(UiElementRef element)
    {
        string description = Describes(element);
        ValueReads.Add(description);
        OnGetValue?.Invoke(description);
        return OperationResult.Ok(_values.TryGetValue(description, out string? value) ? value : string.Empty);
    }

    public Action<string>? OnGetValue { get; set; }

    public OperationResult<Unit> SetValue(UiElementRef element, string value)
    {
        ValueWrites.Add((Describes(element), value));
        if (!SilentlyDropValueWrites)
        {
            _values[Describes(element)] = value;
        }

        return OperationResult.Ok();
    }

    public OperationResult<IReadOnlyList<string>> ReadMatchingTextSnapshot(
        WindowHandle root, IReadOnlyCollection<string> exactNames)
    {
        OnReadTextSnapshot?.Invoke(root);
        HashSet<string> signed = new(exactNames, StringComparer.Ordinal);
        return OperationResult.Ok<IReadOnlyList<string>>(
            Texts.TryGetValue(root.Value, out List<string>? texts)
                ? [.. texts.Where(signed.Contains)]
                : []);
    }

    public Action<WindowHandle>? OnReadTextSnapshot { get; set; }

    public OperationResult<IReadOnlyList<string>> ReadTextSnapshot(WindowHandle root, int maxItems)
    {
        OnReadTextSnapshot?.Invoke(root);
        return OperationResult.Ok<IReadOnlyList<string>>(
            Texts.TryGetValue(root.Value, out List<string>? texts) ? texts : []);
    }

    private List<FakeUiElement> Matching(WindowHandle root, UiElementQuery query)
    {
        if (!_tree.TryGetValue(root.Value, out List<FakeUiElement>? elements))
        {
            return [];
        }

        return
        [
            .. elements.Where(e =>
                (query.Kind == UiControlKind.Any ||
                 string.Equals(e.Identity.ControlTypeName, query.Kind.ToString(), StringComparison.Ordinal)) &&
                (string.IsNullOrEmpty(query.Name) ||
                 string.Equals(e.Identity.Name, query.Name, StringComparison.Ordinal)) &&
                (string.IsNullOrEmpty(query.AutomationId) ||
                 string.Equals(e.Identity.AutomationId, query.AutomationId, StringComparison.Ordinal))),
        ];
    }

    private static UiElementRef Reference(FakeUiElement element, WindowHandle root) =>
        new(element, element.Identity.Name, root);

    private static string Describes(UiElementRef element) =>
        element.Native is FakeUiElement fake && fake.Identity.AutomationId.Length > 0
            ? fake.Identity.AutomationId
            : element.Name;
}

/// <summary>
/// An input sink that always succeeds and records what it was asked to send.
/// </summary>
/// <remarks>
/// Deliberately unguarded. Its whole purpose is to fail loudly if the driver's own guard is
/// ever removed: a test asserting <see cref="Sends"/> is empty would still pass against a
/// guarded sink even if the driver stopped checking, so the sink used to prove the driver must
/// not do the checking itself.
/// </remarks>
internal sealed class RecordingInputSink : IScopedInputSink
{
    public List<(WindowHandle Target, KnownShortcut Shortcut)> Sends { get; } = [];

    /// <summary>
    /// Runs after each send, so a test can model what the keystroke did to the fake desktop.
    /// </summary>
    /// <remarks>
    /// Needed for shortcuts whose whole effect is a change of state rather than a new surface —
    /// closing a document, for instance, is observed as the window title ceasing to name it, and
    /// a fake that never changed the title could only prove that a wait times out.
    /// </remarks>
    public Action<KnownShortcut>? OnSend { get; set; }

    public OperationResult<Unit> SendShortcut(WindowHandle verifiedTarget, KnownShortcut shortcut)
    {
        Sends.Add((verifiedTarget, shortcut));
        OnSend?.Invoke(shortcut);
        return OperationResult.Ok();
    }
}

/// <summary>Records evidence requests without touching a screen or a disk.</summary>
internal sealed class RecordingEvidenceSink : IAutomationEvidenceSink
{
    public List<(string WindowTitle, string Reason)> Captures { get; } = [];

    /// <summary>When true, capture fails — proving a failed screenshot never replaces the real failure.</summary>
    public bool Fails { get; set; }

    public OperationResult<EvidenceRef> CaptureWindow(ExternalWindowRef window, string reason)
    {
        Captures.Add((window.Title, reason));

        return Fails
            ? OperationResult.Fail<EvidenceRef>(FailureCode.WorkspaceError, "scripted capture failure")
            : OperationResult.Ok(new EvidenceRef(
                $@"C:\Fake\Evidence\{reason}.png", reason, DateTimeOffset.UnixEpoch, window.Title));
    }
}
