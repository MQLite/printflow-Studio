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

    /// <summary>A baseline with the shape of the real one, and none of its real values.</summary>
    internal static MeituBaseline Baseline() => new(
        ExecutablePath,
        ExecutableSha256,
        "9.9.9.9",
        "zh-CN",
        ["美图秀秀", "美图秀秀-图片编辑"],
        "美图秀秀",
        WelcomeMarkers);

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

/// <summary>Records every invoke and value write, and answers lookups from a script.</summary>
internal sealed class RecordingUiElementProvider : IUiElementProvider
{
    private readonly Dictionary<string, string> _findable = new(StringComparer.Ordinal);

    /// <summary>Automation names each window reports, keyed by handle.</summary>
    public Dictionary<nint, List<string>> Texts { get; } = [];

    public List<string> Invocations { get; } = [];

    public List<(string Element, string Value)> ValueWrites { get; } = [];

    /// <summary>Makes a query resolvable, so <see cref="Find"/> returns an element for it.</summary>
    public void MakeFindable(UiElementQuery query) => _findable[query.ToString()] = query.ToString();

    public void SetTexts(WindowHandle window, params string[] texts) =>
        Texts[window.Value] = [.. texts];

    public OperationResult<UiElementRef> Find(WindowHandle root, UiElementQuery query) =>
        _findable.ContainsKey(query.ToString())
            ? OperationResult.Ok(new UiElementRef(query.ToString(), query.ToString(), root))
            : OperationResult.Fail<UiElementRef>(
                FailureCode.MeituOpenInputFailed, $"Nothing matching {query} was scripted.");

    public OperationResult<Unit> Invoke(UiElementRef element)
    {
        Invocations.Add(element.Name);
        return OperationResult.Ok();
    }

    public OperationResult<Unit> SetValue(UiElementRef element, string value)
    {
        ValueWrites.Add((element.Name, value));
        return OperationResult.Ok();
    }

    public OperationResult<IReadOnlyList<string>> ReadTextSnapshot(WindowHandle root, int maxItems) =>
        OperationResult.Ok<IReadOnlyList<string>>(
            Texts.TryGetValue(root.Value, out List<string>? texts) ? texts : []);
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
