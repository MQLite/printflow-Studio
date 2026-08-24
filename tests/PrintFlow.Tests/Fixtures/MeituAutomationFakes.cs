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

    /// <summary>
    /// A baseline with the shape of the real one, and none of its real values.
    /// </summary>
    /// <param name="card">The signed card shape, or <c>null</c> to model a chain that vouches for none.</param>
    /// <param name="fileDialog">The signed picker signature, or <c>null</c>.</param>
    /// <param name="editorWithWorkingCopy">The signed editor-with-document signature, or <c>null</c>.</param>
    /// <param name="editorEmpty">The signed empty-editor signature, with its open control.</param>
    internal static MeituBaseline Baseline(
        MeituCardShape? card = null,
        MeituFileDialogSignature? fileDialog = null,
        MeituEditorSignature? editorWithWorkingCopy = null,
        MeituEditorSignature? editorEmpty = null) => new(
        ExecutablePath,
        ExecutableSha256,
        "9.9.9.9",
        "zh-CN",
        ["美图秀秀", "美图秀秀-图片编辑"],
        "美图秀秀",
        WelcomeMarkers,
        card ?? CardShape(),
        fileDialog ?? FileDialog(),
        editorWithWorkingCopy ?? EditorWithDocument(),
        editorEmpty ?? EditorEmptySignature());

    /// <summary>A baseline whose optional evidence is exactly as supplied, including absent.</summary>
    /// <remarks>
    /// Separate from <see cref="Baseline"/> because that one substitutes defaults for anything
    /// omitted, which is what most tests want and precisely what a fail-closed test must not
    /// get: "the chain vouches for no card shape" has to be expressible.
    /// </remarks>
    internal static MeituBaseline BaselineWithout(
        bool card = false,
        bool fileDialog = false,
        bool editorWithWorkingCopy = false,
        bool editorEmpty = false) => new(
        ExecutablePath,
        ExecutableSha256,
        "9.9.9.9",
        "zh-CN",
        ["美图秀秀", "美图秀秀-图片编辑"],
        "美图秀秀",
        WelcomeMarkers,
        card ? null : CardShape(),
        fileDialog ? null : FileDialog(),
        editorWithWorkingCopy ? null : EditorWithDocument(),
        editorEmpty ? null : EditorEmptySignature());

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

    public OperationResult<ExternalWindowRef> Refresh(WindowHandle handle)
    {
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

    /// <summary>Automation names each window reports, keyed by handle.</summary>
    public Dictionary<nint, List<string>> Texts { get; } = [];

    /// <summary>What was invoked, by automation id where there is one. Asserted <i>empty</i> more often than not.</summary>
    public List<string> Invocations { get; } = [];

    public List<(string Element, string Value)> ValueWrites { get; } = [];

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

    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    /// <summary>When set, a write is silently dropped — the lost-write case the read-back catches.</summary>
    public bool SilentlyDropValueWrites { get; set; }

    public OperationResult<string> GetValue(UiElementRef element) =>
        OperationResult.Ok(_values.TryGetValue(Describes(element), out string? value) ? value : string.Empty);

    public OperationResult<Unit> SetValue(UiElementRef element, string value)
    {
        ValueWrites.Add((Describes(element), value));
        if (!SilentlyDropValueWrites)
        {
            _values[Describes(element)] = value;
        }

        return OperationResult.Ok();
    }

    public OperationResult<IReadOnlyList<string>> ReadTextSnapshot(WindowHandle root, int maxItems) =>
        OperationResult.Ok<IReadOnlyList<string>>(
            Texts.TryGetValue(root.Value, out List<string>? texts) ? texts : []);

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

    public OperationResult<Unit> SendShortcut(WindowHandle verifiedTarget, KnownShortcut shortcut)
    {
        Sends.Add((verifiedTarget, shortcut));
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
