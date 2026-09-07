using System.Collections.Immutable;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Adapters.Photoshop;
using PrintFlow.Infrastructure.Automation;

namespace PrintFlow.Tests.Fixtures;

// The `Unit` result type, aliased inside the namespace: `PrintFlow.Tests.Unit` is a namespace
// in this assembly, and an enclosing namespace wins over a compilation-unit alias.
using Unit = PrintFlow.Domain.Results.Unit;

/// <summary>
/// Scriptable stand-ins for the OS seams the Photoshop foundation sits on
/// (Epic 11400 Part A §22).
/// </summary>
/// <remarks>
/// Only the operating system is faked. The classifier, the identity rule, the guarded driver and
/// the production adapter are all the real implementations, because every rule worth testing —
/// verify before input, wrong document refused, managed files only — lives in those and would be
/// proved by nothing if they were doubled.
///
/// The recorders matter as much as the scripts. Several tests assert that
/// <see cref="RecordingInputSink.Sends"/> or <see cref="FakeVerifiedControlSink.Presses"/> is
/// <i>empty</i>, which is the only way to state "no input was sent" as a checkable fact rather
/// than an intention.
///
/// The structural values below are the ones live discovery actually produced on the accepted
/// workstation — control ids 1148/1/2 for the Open dialog, 1001 twice for the identity surface,
/// the <c>OWL.*</c> marker classes — with a fake executable path and digest. Using the real
/// shapes matters: a test built on invented control ids would pass just as happily against an
/// adapter that had them wrong.
/// </remarks>
internal static class PhotoshopFakes
{
    internal const string ExecutablePath = @"C:\Fake\Adobe Photoshop CC 2019\Photoshop.exe";

    internal const string MainWindowClass = "Photoshop";

    internal const string NoDocumentTitle = "Adobe Photoshop CC 2019";

    internal const string StartScreenClass = "OWL.WelcomeScreenView";

    internal const string DocumentClass = "OWL.Document";

    internal static readonly ImmutableArray<string> EditorChromeClasses =
        ["OWL.MenuBar", "OWL.ApplicationBar", "OWL.Dock"];

    internal static readonly Sha256 ExecutableSha256 = Sha256.Parse(
        "2222222222222222222222222222222222222222222222222222222222222222");

    /// <summary>The managed Working directory the fake workspace resolves into.</summary>
    internal const string WorkingDirectory = @"C:\Fake\PrintFlowStudio\Sessions\S1\Working";

    internal const string ExpectedFileName = "PFTEST-A-0001_WORKING.png";

    internal static string ExpectedPath => System.IO.Path.Combine(WorkingDirectory, ExpectedFileName);

    internal static PhotoshopWindowStateSignature WindowStates() =>
        new(StartScreenClass, DocumentClass, EditorChromeClasses);

    internal static PhotoshopOpenDialogSignature OpenDialog() => new(
        WindowClassName: "#32770",
        Title: "打开",
        FileNameControlId: 1148,
        FileNameControlClass: "ComboBoxEx32",
        ConfirmControlId: 1,
        ConfirmControlClass: "Button",
        CancelControlId: 2,
        CancelControlClass: "Button");

    internal static PhotoshopDocumentIdentitySignature DocumentIdentity() => new(
        TitleSeparator: " @ ",
        DialogClassName: "#32770",
        DialogTitle: "另存为",
        FileNameControlId: 1001,
        FileNameControlClass: "Edit",
        AddressControlId: 1001,
        AddressControlClass: "ToolbarWindow32",
        AddressTextPrefix: "地址: ",
        CancelControlId: 2,
        CancelControlClass: "Button");

    internal static PhotoshopOwnedDocumentCleanupSignature OwnedDocumentCleanup() => new(
        SaveAsCopyMaySubstituteIdentityFileExtension: true,
        PromptWindowClassName: "PSDialogBox",
        PromptTitle: "Adobe Photoshop",
        Message: new PhotoshopDiscardPromptMessageSignature(
            ControlId: 203,
            ControlClass: "Static",
            TextPrefix: "要在关闭之前存储对 Adobe Photoshop 文档 “",
            TextSuffix: "”的更改吗？",
            TruncationMarker: "...",
            MinimumDocumentNamePrefixLength: 16),
        SaveControl: new PhotoshopDiscardPromptControlSignature(10, "Button", "是(&Y)"),
        DiscardControl: new PhotoshopDiscardPromptControlSignature(11, "Button", "否(&N)"),
        CancelControl: new PhotoshopDiscardPromptControlSignature(12, "Button", "取消"));

    internal static PhotoshopBaseline Baseline(
        PhotoshopWindowStateSignature? states = null,
        PhotoshopOpenDialogSignature? openDialog = null,
        PhotoshopDocumentIdentitySignature? identity = null,
        bool includeStates = true,
        bool includeOpenDialog = true,
        bool includeIdentity = true,
        bool includeCleanup = true) =>
        new(ExecutablePath,
            ExecutableSha256,
            AcceptedProductVersion: "20.0",
            AcceptedFileVersion: "20.0 (20200706.r.120 2020/07/06: 1208496)",
            UiLanguage: "Simplified Chinese",
            MainWindowClass,
            NoDocumentTitle,
            ExcludedInstallations: [@"C:\Fake\Adobe\Adobe Photoshop 2026\Photoshop.exe"],
            includeStates ? states ?? WindowStates() : null,
            includeOpenDialog ? openDialog ?? OpenDialog() : null,
            includeIdentity ? identity ?? DocumentIdentity() : null,
            W1Action: null,
            OwnedDocumentCleanup: includeCleanup ? OwnedDocumentCleanup() : null);

    internal static ExternalProcessRef Process(int id = 7777) =>
        new(id, ExecutablePath, new DateTimeOffset(2026, 8, 28, 9, 0, 0, TimeSpan.Zero));

    internal static ExternalWindowRef Window(
        nint handle = 0xA0BDC,
        int owningProcessId = 7777,
        string title = NoDocumentTitle,
        string className = MainWindowClass,
        bool enabled = true,
        bool visible = true) =>
        new(new WindowHandle(handle), owningProcessId, title, className,
            new WindowBounds(-8, -8, 1928, 1048), visible, IsMinimised: false, IsEnabled: enabled);

    internal static ExternalWindowRef Dialog(
        nint handle = 0xD1A10,
        int owningProcessId = 7777,
        string title = "打开",
        string className = "#32770") =>
        new(new WindowHandle(handle), owningProcessId, title, className,
            new WindowBounds(0, 0, 1433, 765), IsVisible: true, IsMinimised: false, IsEnabled: true);

    internal static PhotoshopTarget Target() => new(Process(), Window());

    /// <summary>The window title Photoshop shows for a loaded document of the given name.</summary>
    /// <remarks>
    /// The exact observed shape, including the Chinese layer and colour-mode suffix, because the
    /// title rule has to survive it: everything after the separator is noise the rule must
    /// ignore without ever being tempted to search it.
    /// </remarks>
    internal static string TitleFor(string fileName) => $"{fileName} @ 100% (图层 1, RGB/8)";
}

/// <summary>A baseline provider that returns whatever the test scripted.</summary>
internal sealed class StubPhotoshopBaselineProvider : IPhotoshopBaselineProvider
{
    private readonly OperationResult<PhotoshopBaseline> _result;

    internal StubPhotoshopBaselineProvider(PhotoshopBaseline baseline) =>
        _result = OperationResult.Ok(baseline);

    internal StubPhotoshopBaselineProvider(OperationFailure failure) =>
        _result = OperationResult.Fail<PhotoshopBaseline>(failure);

    public OperationResult<PhotoshopBaseline> GetVerifiedBaseline() => _result;
}

/// <summary>
/// A fake desktop of Win32 child controls, scriptable per host window.
/// </summary>
/// <remarks>
/// Models the two properties the real sink's safety rests on, because a fake that ignored them
/// could not fail the tests that matter: a control belongs to exactly one process, and a
/// control id is only an identity <i>together with</i> a class. The Save As surface here carries
/// two controls under id 1001 for that reason — a filename Edit and an address ToolbarWindow32 —
/// exactly as the real one does.
/// </remarks>
internal sealed class FakeVerifiedControlSink : IVerifiedControlSink
{
    private readonly Dictionary<nint, List<FakeControl>> _controls = [];
    private readonly Dictionary<nint, List<string>> _visibleClasses = [];

    /// <summary>Every value written, in order — asserted empty when nothing may be typed.</summary>
    public List<(int ControlId, string Value)> Writes { get; } = [];

    /// <summary>Every control pressed, in order.</summary>
    public List<(nint Host, int ControlId, string ClassName)> Presses { get; } = [];

    /// <summary>When set, a write is stored but this is what a read reports back instead.</summary>
    public string? ReadBackOverride { get; set; }

    /// <summary>Processes whose controls are refused, modelling ownership having changed.</summary>
    public HashSet<int> LostProcessIds { get; } = [];

    /// <summary>Controls whose invocation is rejected after they have been recognised.</summary>
    public HashSet<int> PressFailures { get; } = [];

    /// <summary>Runs after each press, so a test can model the surface it closes.</summary>
    public Action<nint, int>? OnPress { get; set; }

    internal sealed record FakeControl(int ControlId, string ClassName, string Text)
    {
        public string Text { get; set; } = Text;
    }

    public void SetVisibleClasses(WindowHandle host, params string[] classes) =>
        _visibleClasses[host.Value] = [.. classes];

    public void AddControl(WindowHandle host, int controlId, string className, string text = "")
    {
        if (!_controls.TryGetValue(host.Value, out List<FakeControl>? list))
        {
            list = [];
            _controls[host.Value] = list;
        }

        list.Add(new FakeControl(controlId, className, text));
    }

    public void ClearControls(WindowHandle host) => _controls.Remove(host.Value);

    public string TextOf(WindowHandle host, int controlId, string className) =>
        _controls.TryGetValue(host.Value, out List<FakeControl>? list)
            ? list.FirstOrDefault(c => c.ControlId == controlId &&
                                       string.Equals(c.ClassName, className, StringComparison.Ordinal))?.Text
              ?? string.Empty
            : string.Empty;

    public OperationResult<VerifiedControlRef> Locate(
        ExternalProcessRef owner, WindowHandle host, int controlId, string expectedClassName)
    {
        if (Lost(owner))
        {
            return OperationResult.Fail<VerifiedControlRef>(
                FailureCode.MeituTargetLost, "scripted ownership loss");
        }

        List<FakeControl> matches = _controls.TryGetValue(host.Value, out List<FakeControl>? list)
            ? [.. list.Where(c => c.ControlId == controlId &&
                                  string.Equals(c.ClassName, expectedClassName, StringComparison.Ordinal))]
            : [];

        return matches.Count == 1
            ? OperationResult.Ok(new VerifiedControlRef(
                new WindowHandle(host.Value + controlId), host, controlId, expectedClassName))
            : OperationResult.Fail<VerifiedControlRef>(
                FailureCode.MeituUnknownState,
                $"{matches.Count} controls with id {controlId} and class '{expectedClassName}'.");
    }

    public OperationResult<IReadOnlyList<VerifiedControlRef>> LocateByClass(
        ExternalProcessRef owner, WindowHandle host, string expectedClassName)
    {
        if (Lost(owner))
        {
            return OperationResult.Fail<IReadOnlyList<VerifiedControlRef>>(
                FailureCode.MeituTargetLost, "scripted ownership loss");
        }

        List<VerifiedControlRef> matches = _controls.TryGetValue(host.Value, out List<FakeControl>? list)
            ? [.. list
                .Where(c => string.Equals(c.ClassName, expectedClassName, StringComparison.Ordinal))
                .Select(c => new VerifiedControlRef(
                    new WindowHandle(host.Value + c.ControlId), host, c.ControlId, expectedClassName))]
            : [];

        return OperationResult.Ok<IReadOnlyList<VerifiedControlRef>>(matches);
    }

    public OperationResult<IReadOnlyList<string>> LocateVisibleClasses(
        ExternalProcessRef owner, WindowHandle host, int maxItems)
    {
        if (Lost(owner))
        {
            return OperationResult.Fail<IReadOnlyList<string>>(
                FailureCode.MeituTargetLost, "scripted ownership loss");
        }

        return OperationResult.Ok<IReadOnlyList<string>>(
            _visibleClasses.TryGetValue(host.Value, out List<string>? classes) ? classes : []);
    }

    public OperationResult<string> ReadText(ExternalProcessRef owner, VerifiedControlRef control)
    {
        if (Lost(owner))
        {
            return OperationResult.Fail<string>(FailureCode.MeituTargetLost, "scripted ownership loss");
        }

        if (ReadBackOverride is { } scripted)
        {
            return OperationResult.Ok(scripted);
        }

        return OperationResult.Ok(TextOf(control.Host, control.ControlId, control.ClassName));
    }

    public OperationResult<Unit> WriteText(
        ExternalProcessRef owner, VerifiedControlRef control, string value)
    {
        if (Lost(owner))
        {
            return OperationResult.Fail<Unit>(FailureCode.MeituTargetLost, "scripted ownership loss");
        }

        Writes.Add((control.ControlId, value));

        if (_controls.TryGetValue(control.Host.Value, out List<FakeControl>? list))
        {
            FakeControl? match = list.FirstOrDefault(c =>
                c.ControlId == control.ControlId &&
                string.Equals(c.ClassName, control.ClassName, StringComparison.Ordinal));
            if (match is not null)
            {
                match.Text = value;
            }
        }

        return OperationResult.Ok();
    }

    public OperationResult<Unit> Press(ExternalProcessRef owner, VerifiedControlRef control)
    {
        if (Lost(owner))
        {
            return OperationResult.Fail<Unit>(FailureCode.MeituTargetLost, "scripted ownership loss");
        }

        if (PressFailures.Contains(control.ControlId))
        {
            return OperationResult.Fail<Unit>(
                FailureCode.MeituUnknownState, "scripted control invocation failure");
        }

        Presses.Add((control.Host.Value, control.ControlId, control.ClassName));
        OnPress?.Invoke(control.Host.Value, control.ControlId);
        return OperationResult.Ok();
    }

    private bool Lost(ExternalProcessRef owner) => LostProcessIds.Contains(owner.ProcessId);
}

/// <summary>A workspace stub that resolves managed references into the fake Working directory.</summary>
/// <remarks>
/// Only <c>ResolveAbsolute</c> is implemented, and deliberately: the Photoshop foundation is
/// allowed to do exactly one thing with the workspace, and a stub that threw on everything else
/// is how that stays true as the adapter changes.
/// </remarks>
internal sealed class StubPhotoshopWorkspace(string? root = null) : PrintFlow.Workflow.Ports.IWorkspace
{
    private readonly string _root = root ?? PhotoshopFakes.WorkingDirectory;

    public string ResolveAbsolute(WorkspaceFileRef reference) =>
        System.IO.Path.Combine(_root, reference.FileName);

    public string ResolveAbsoluteDirectory(WorkspaceDirRef reference) =>
        throw new NotSupportedException("The Photoshop foundation resolves no directories.");

    public OperationResult<WorkspaceDirRef> CreateSession(
        PrintFlow.Domain.Ids.SessionId id, DateTimeOffset createdUtc) =>
        throw new NotSupportedException("The Photoshop foundation creates no sessions.");

    public Task<OperationResult<WorkspaceFileRef>> ImportSourceAsync(
        WorkspaceDirRef session, string sourceAbsolutePath, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The Photoshop foundation imports nothing.");

    public Task<OperationResult<WorkspaceFileRef>> CreateWorkingCopyAsync(
        WorkspaceDirRef session,
        PrintFlow.Domain.Ids.AttemptId attemptId,
        WorkspaceFileRef source,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("The Photoshop foundation creates no working copies.");

    public OperationResult<WorkspaceFileRef> ReserveOutput(
        WorkspaceDirRef session,
        WorkspaceArea area,
        string proposedFileName,
        PrintFlow.Domain.Outputs.NamingPatternSet patterns) =>
        throw new NotSupportedException("The Photoshop foundation reserves no outputs.");

    public Task<OperationResult<Unit>> WriteReservedAsync(
        WorkspaceFileRef reservedTarget, WorkspaceFileRef source, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The Photoshop foundation writes no outputs.");

    public Task<OperationResult<WorkspaceFileRef>> MoveToRejectedAsync(
        WorkspaceDirRef session, WorkspaceFileRef source, string fileName, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The Photoshop foundation rejects nothing.");

    public OperationResult<WorkingCleanupResult> CleanupWorking(WorkspaceDirRef session, WorkingCleanupPlan plan) =>
        throw new NotSupportedException("The Photoshop foundation cleans up nothing.");

    public OperationResult<Unit> VerifyRetentionFiles(WorkspaceDirRef session, IReadOnlyList<RetentionFile> files) =>
        throw new NotSupportedException();

    public Task<OperationResult<WorkspaceFileRef>> PromoteRevisionAsync(
        WorkspaceDirRef session, PrintFlow.Domain.Ids.RevisionId revisionId, RetentionFile source, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public OperationResult<IReadOnlyList<WorkingFileEntry>> ListWorkingFiles(WorkspaceDirRef session) =>
        throw new NotSupportedException("The Photoshop foundation lists nothing.");

    public OperationResult<Unit> QuarantineWorkingFile(WorkspaceFileRef file, string reason) =>
        throw new NotSupportedException("The Photoshop foundation quarantines nothing.");

    public OperationResult<Unit> Quarantine(string absolutePath, string reason) =>
        throw new NotSupportedException("The Photoshop foundation quarantines nothing.");
}
