using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using PrintFlow.Domain.Results;

using System.ComponentModel;
using System.Diagnostics;

namespace PrintFlow.Infrastructure.Automation;

/// <summary>
/// The Windows UI Automation implementation of <see cref="IUiElementProvider"/>.
/// </summary>
/// <remarks>
/// Every entry point starts from <c>AutomationElement.FromHandle</c> on a window handle the
/// locator has already attributed to the expected process, so the search root is never the
/// desktop. <see cref="ElementNotAvailableException"/> — raised when the target window closes
/// mid-walk — is caught and converted to <see cref="FailureCode.MeituTargetLost"/> rather than
/// allowed to escape as an exception, because "the window went away" is a production event and
/// not a defect (Epic 11300 Part A §21).
/// </remarks>
public sealed class UiaElementProvider : IUiElementProvider
{
    private readonly int _maxSnapshotDepth;

    /// <param name="maxSnapshotDepth">
    /// How deep a text snapshot descends.
    /// </param>
    /// <remarks>
    /// Bounded rather than unlimited, because a runaway subtree walk looks exactly like a hang.
    /// The depth has to be generous, though: Meitu 7.8.7.5 is a Qt application whose start-page
    /// entries sit well below the top few levels, and a depth of 6 surfaced only 3 of the 10
    /// signed markers where 24 surfaces 9. The item limit passed to
    /// <see cref="ReadTextSnapshot"/> is the real cost bound; the depth exists to stop a
    /// pathological tree, not to trim a normal one.
    /// <para>
    /// A read that comes back too thin ends in <see cref="FailureCode.MeituUnknownState"/>,
    /// which stops — so a bound that is too tight makes PrintFlow refuse to work, never makes it
    /// act on a screen it has not recognised.
    /// </para>
    /// </remarks>
    public UiaElementProvider(int maxSnapshotDepth = 24)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSnapshotDepth);
        _maxSnapshotDepth = maxSnapshotDepth;
    }

    /// <inheritdoc />
    public OperationResult<UiElementRef> Find(WindowHandle root, UiElementQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        OperationResult<AutomationElement> rootElement = RootOf(root);
        if (rootElement.IsFailure)
        {
            return OperationResult.Fail<UiElementRef>(rootElement.Failure);
        }

        try
        {
            Condition condition = BuildCondition(query);
            AutomationElement? found = rootElement.Value.FindFirst(TreeScope.Descendants, condition);
            if (found is null)
            {
                return OperationResult.Fail<UiElementRef>(
                    FailureCode.MeituOpenInputFailed,
                    $"No element matching {query} exists beneath window {root}.");
            }

            return OperationResult.Ok(new UiElementRef(found, NameOf(found), root));
        }
        catch (ElementNotAvailableException ex)
        {
            return OperationResult.Fail<UiElementRef>(
                FailureCode.MeituTargetLost,
                $"Window {root} disappeared while searching for {query}: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            return OperationResult.Fail<UiElementRef>(
                FailureCode.MeituOpenInputFailed, $"Searching for {query} failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public OperationResult<IReadOnlyList<UiElementRef>> FindAll(WindowHandle root, UiElementQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        OperationResult<AutomationElement> rootElement = RootOf(root);
        if (rootElement.IsFailure)
        {
            return OperationResult.Fail<IReadOnlyList<UiElementRef>>(rootElement.Failure);
        }

        try
        {
            AutomationElementCollection found =
                rootElement.Value.FindAll(TreeScope.Descendants, BuildCondition(query));

            List<UiElementRef> matches = new(found.Count);
            foreach (AutomationElement element in found)
            {
                matches.Add(new UiElementRef(element, NameOf(element), root));
            }

            return OperationResult.Ok<IReadOnlyList<UiElementRef>>(matches);
        }
        catch (ElementNotAvailableException ex)
        {
            return OperationResult.Fail<IReadOnlyList<UiElementRef>>(
                FailureCode.MeituTargetLost,
                $"Window {root} disappeared while searching for {query}: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            return OperationResult.Fail<IReadOnlyList<UiElementRef>>(
                FailureCode.MeituOpenInputFailed, $"Searching for {query} failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public OperationResult<UiElementIdentity> DescribeWindow(WindowHandle root)
    {
        OperationResult<AutomationElement> element = RootOf(root);
        return element.IsFailure
            ? OperationResult.Fail<UiElementIdentity>(element.Failure)
            : Describe(new UiElementRef(element.Value, NameOf(element.Value), root));
    }

    /// <inheritdoc />
    public OperationResult<IReadOnlyList<string>> ReadMatchingTextSnapshot(
        WindowHandle root, IReadOnlyCollection<string> exactNames)
    {
        ArgumentNullException.ThrowIfNull(exactNames);
        string[] names = [.. exactNames.Where(name => !string.IsNullOrEmpty(name)).Distinct(StringComparer.Ordinal)];
        if (names.Length == 0)
        {
            return OperationResult.Ok<IReadOnlyList<string>>([]);
        }

        OperationResult<AutomationElement> rootElement = RootOf(root);
        if (rootElement.IsFailure)
        {
            return OperationResult.Fail<IReadOnlyList<string>>(rootElement.Failure);
        }

        try
        {
            Condition condition = names.Length == 1
                ? new PropertyCondition(AutomationElement.NameProperty, names[0])
                : new OrCondition(names
                    .Select(name => (Condition)new PropertyCondition(AutomationElement.NameProperty, name))
                    .ToArray());
            AutomationElementCollection found =
                rootElement.Value.FindAll(TreeScope.Descendants, condition);
            return OperationResult.Ok<IReadOnlyList<string>>(
                [.. found.Cast<AutomationElement>().Select(NameOf)]);
        }
        catch (ElementNotAvailableException ex)
        {
            return OperationResult.Fail<IReadOnlyList<string>>(UiReadInterruption.Create(
                root, "reading signed markers", ex, IsReadableRoot(root)));
        }
        catch (InvalidOperationException ex)
        {
            return OperationResult.Fail<IReadOnlyList<string>>(
                FailureCode.MeituOpenInputFailed,
                $"Reading signed markers beneath window {root} failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public OperationResult<UiElementIdentity> Describe(UiElementRef element)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (element.Native is not AutomationElement native)
        {
            return OperationResult.Fail<UiElementIdentity>(
                FailureCode.MeituOpenInputFailed,
                $"'{element.Name}' was not located by this provider and cannot be described.");
        }

        try
        {
            AutomationElement.AutomationElementInformation current = native.Current;
            System.Windows.Rect rect = current.BoundingRectangle;

            return OperationResult.Ok(new UiElementIdentity(
                ControlTypeName: ShortControlTypeName(current.ControlType),
                AutomationId: current.AutomationId ?? string.Empty,
                Name: current.Name ?? string.Empty,
                ClassName: current.ClassName ?? string.Empty,
                ProcessId: current.ProcessId,
                SupportedPatterns: PatternsOf(native),
                Bounds: rect.IsEmpty
                    ? default
                    : new UiBounds(rect.X, rect.Y, rect.Width, rect.Height),
                IsEnabled: current.IsEnabled,
                IsOffscreen: current.IsOffscreen));
        }
        catch (ElementNotAvailableException ex)
        {
            return OperationResult.Fail<UiElementIdentity>(
                FailureCode.MeituTargetLost, $"'{element.Name}' disappeared before it could be read: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            return OperationResult.Fail<UiElementIdentity>(
                FailureCode.MeituOpenInputFailed, $"Reading '{element.Name}' failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public OperationResult<UiElementRef> GetParent(UiElementRef element)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (element.Native is not AutomationElement native)
        {
            return OperationResult.Fail<UiElementRef>(
                FailureCode.MeituOpenInputFailed,
                $"'{element.Name}' was not located by this provider, so its parent cannot be walked to.");
        }

        try
        {
            // The control view rather than the raw view: the raw tree contains framework
            // scaffolding that no signed evidence describes, and a rule that walked through it
            // would be counting levels nobody has validated.
            AutomationElement? parent = TreeWalker.ControlViewWalker.GetParent(native);

            // Fully qualified: this file's own namespace is `…Infrastructure.Automation`, which
            // shadows the UI Automation `Automation` class the comparison needs.
            if (parent is null ||
                System.Windows.Automation.Automation.Compare(parent, AutomationElement.RootElement))
            {
                // The desktop is not an ancestor this slice may return: every element a rule may
                // consider has to live inside the window the locator already attributed to the
                // verified process.
                return OperationResult.Fail<UiElementRef>(
                    FailureCode.MeituOpenInputFailed,
                    $"'{element.Name}' has no parent inside window {element.RootWindow}.");
            }

            return OperationResult.Ok(new UiElementRef(parent, NameOf(parent), element.RootWindow));
        }
        catch (ElementNotAvailableException ex)
        {
            return OperationResult.Fail<UiElementRef>(
                FailureCode.MeituTargetLost,
                $"'{element.Name}' disappeared while its parent was being walked to: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            return OperationResult.Fail<UiElementRef>(
                FailureCode.MeituOpenInputFailed, $"Walking to the parent of '{element.Name}' failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public OperationResult<Unit> Invoke(UiElementRef element)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (element.Native is not AutomationElement native)
        {
            return OperationResult.Fail<Unit>(
                FailureCode.MeituOpenInputFailed,
                $"'{element.Name}' was not located by this provider and cannot be invoked.");
        }

        try
        {
            if (native.TryGetCurrentPattern(InvokePattern.Pattern, out object invoke))
            {
                ((InvokePattern)invoke).Invoke();
                return OperationResult.Ok();
            }

            if (native.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object select))
            {
                ((SelectionItemPattern)select).Select();
                return OperationResult.Ok();
            }

            // No supported pattern means no verifiable way to activate this control. The
            // alternative — synthesising a click at its bounding rectangle — is exactly the
            // coordinate-macro approach Part A §4 puts last, so it is not attempted here.
            return OperationResult.Fail<Unit>(
                FailureCode.MeituOpenInputFailed,
                $"'{element.Name}' exposes neither Invoke nor SelectionItem; no coordinate click is attempted.");
        }
        catch (ElementNotAvailableException ex)
        {
            return OperationResult.Fail<Unit>(
                FailureCode.MeituTargetLost, $"'{element.Name}' disappeared before it could be invoked: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            return OperationResult.Fail<Unit>(
                FailureCode.MeituOpenInputFailed, $"Invoking '{element.Name}' failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public OperationResult<Unit> ClickAtLiveClickablePoint(
        UiElementRef element,
        ExternalProcessRef acceptedProcess,
        WindowHandle expectedForegroundWindow)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(acceptedProcess);

        if (element.Native is not AutomationElement native || element.RootWindow.IsNone)
        {
            return OperationResult.Fail<Unit>(
                FailureCode.MeituOpenInputFailed,
                $"'{element.Name}' is not a live UI Automation element inside a verified window; " +
                "no pointer input was sent.");
        }

        try
        {
            AutomationElement.AutomationElementInformation current = native.Current;
            System.Windows.Rect bounds = current.BoundingRectangle;
            if (!current.IsEnabled || current.IsOffscreen || bounds.IsEmpty)
            {
                return OperationResult.Fail<Unit>(
                    FailureCode.MeituTargetLost,
                    $"'{element.Name}' is disabled, offscreen or has no live bounds; no pointer input was sent.");
            }

            if (expectedForegroundWindow.IsNone)
            {
                return OperationResult.Fail<Unit>(
                    FailureCode.MeituTargetLost,
                    $"'{element.Name}' has no expected foreground window; no pointer input was sent.");
            }

            OperationResult<AutomationElement> root = RootOf(element.RootWindow);
            if (root.IsFailure)
            {
                return OperationResult.Fail<Unit>(root.Failure);
            }

            OperationResult<AutomationElement> foregroundRoot = RootOf(expectedForegroundWindow);
            if (foregroundRoot.IsFailure)
            {
                return OperationResult.Fail<Unit>(foregroundRoot.Failure);
            }

            if (!IsSameProcessInstance(acceptedProcess) ||
                current.ProcessId != acceptedProcess.ProcessId ||
                root.Value.Current.ProcessId != acceptedProcess.ProcessId ||
                foregroundRoot.Value.Current.ProcessId != acceptedProcess.ProcessId ||
                NativeMethods.GetForegroundWindow() != expectedForegroundWindow.Value)
            {
                return OperationResult.Fail<Unit>(
                    FailureCode.MeituTargetLost,
                    $"'{element.Name}', its popup and the expected foreground window no longer belong " +
                    "to the accepted process instance, or the expected window lost the foreground; " +
                    "no pointer input was sent.");
            }

            if (!native.TryGetClickablePoint(out System.Windows.Point point) ||
                !bounds.Contains(point))
            {
                return OperationResult.Fail<Unit>(
                    FailureCode.MeituOpenInputFailed,
                    $"'{element.Name}' exposes no live clickable point inside its current bounds; " +
                    "no pointer input was sent.");
            }

            AutomationElement hit = AutomationElement.FromPoint(point);
            if (!System.Windows.Automation.Automation.Compare(hit, native))
            {
                return OperationResult.Fail<Unit>(
                    FailureCode.MeituTargetLost,
                    $"The live clickable point for '{element.Name}' now resolves to another element; " +
                    "no pointer input was sent.");
            }

            int left = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
            int top = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
            int width = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
            int height = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
            if (width <= 1 || height <= 1 ||
                point.X < left || point.X >= left + width ||
                point.Y < top || point.Y >= top + height)
            {
                return OperationResult.Fail<Unit>(
                    FailureCode.MeituOpenInputFailed,
                    $"The live clickable point for '{element.Name}' is outside the active virtual desktop; " +
                    "no pointer input was sent.");
            }

            int normalizedX = (int)Math.Round((point.X - left) * 65535d / (width - 1));
            int normalizedY = (int)Math.Round((point.Y - top) * 65535d / (height - 1));
            NativeMethods.POINTERINPUT[] sequence =
            [
                Pointer(normalizedX, normalizedY,
                    NativeMethods.MOUSEEVENTF_MOVE |
                    NativeMethods.MOUSEEVENTF_ABSOLUTE |
                    NativeMethods.MOUSEEVENTF_VIRTUALDESK),
                Pointer(normalizedX, normalizedY, NativeMethods.MOUSEEVENTF_LEFTDOWN),
                Pointer(normalizedX, normalizedY, NativeMethods.MOUSEEVENTF_LEFTUP),
            ];

            // The foreground check and hit test are deliberately repeated at the last possible
            // point. SendInput is not atomic with these reads, but no input is sent on an assumed
            // or cached target.
            if (!IsSameProcessInstance(acceptedProcess) ||
                NativeMethods.GetForegroundWindow() != expectedForegroundWindow.Value ||
                !System.Windows.Automation.Automation.Compare(AutomationElement.FromPoint(point), native))
            {
                return OperationResult.Fail<Unit>(
                    FailureCode.MeituTargetLost,
                    $"'{element.Name}' changed after validation; no pointer input was sent.");
            }

            uint sent = NativeMethods.SendPointerInput(
                (uint)sequence.Length, sequence, Marshal.SizeOf<NativeMethods.POINTERINPUT>());
            return sent == sequence.Length
                ? OperationResult.Ok()
                : OperationResult.Fail<Unit>(
                    FailureCode.MeituOpenInputFailed,
                    $"Windows accepted {sent} of {sequence.Length} pointer events for the freshly " +
                    $"validated '{element.Name}' item.");
        }
        catch (ElementNotAvailableException ex)
        {
            return OperationResult.Fail<Unit>(
                FailureCode.MeituTargetLost,
                $"'{element.Name}' disappeared before its live clickable point could be used: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            return OperationResult.Fail<Unit>(
                FailureCode.MeituOpenInputFailed,
                $"Reading the live clickable point for '{element.Name}' failed: {ex.Message}");
        }
    }

    private static bool IsSameProcessInstance(ExternalProcessRef acceptedProcess)
    {
        try
        {
            using Process live = Process.GetProcessById(acceptedProcess.ProcessId);
            return !live.HasExited && live.StartTime.ToUniversalTime() == acceptedProcess.StartedUtc;
        }
        catch (Exception ex) when (
            ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    private static NativeMethods.POINTERINPUT Pointer(int x, int y, uint flags) => new()
    {
        type = NativeMethods.INPUT_MOUSE,
        mi = new NativeMethods.POINTERINPUTDATA
        {
            dx = x,
            dy = y,
            dwFlags = flags,
        },
    };

    /// <inheritdoc />
    public OperationResult<string> GetValue(UiElementRef element)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (element.Native is not AutomationElement native)
        {
            return OperationResult.Fail<string>(
                FailureCode.MeituOpenInputFailed,
                $"'{element.Name}' was not located by this provider and cannot be read.");
        }

        try
        {
            if (!native.TryGetCurrentPattern(ValuePattern.Pattern, out object pattern))
            {
                return OperationResult.Fail<string>(
                    FailureCode.MeituOpenInputFailed, $"'{element.Name}' exposes no value pattern.");
            }

            return OperationResult.Ok(((ValuePattern)pattern).Current.Value ?? string.Empty);
        }
        catch (ElementNotAvailableException ex)
        {
            return OperationResult.Fail<string>(
                FailureCode.MeituTargetLost, $"'{element.Name}' disappeared before it could be read: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            return OperationResult.Fail<string>(
                FailureCode.MeituOpenInputFailed, $"Reading '{element.Name}' failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public OperationResult<Unit> SetValue(UiElementRef element, string value)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(value);

        if (element.Native is not AutomationElement native)
        {
            return OperationResult.Fail<Unit>(
                FailureCode.MeituOpenInputFailed,
                $"'{element.Name}' was not located by this provider and cannot be written to.");
        }

        try
        {
            if (!native.TryGetCurrentPattern(ValuePattern.Pattern, out object pattern))
            {
                return OperationResult.Fail<Unit>(
                    FailureCode.MeituOpenInputFailed,
                    $"'{element.Name}' exposes no value pattern; nothing was typed.");
            }

            ValuePattern valuePattern = (ValuePattern)pattern;
            if (valuePattern.Current.IsReadOnly)
            {
                return OperationResult.Fail<Unit>(
                    FailureCode.MeituOpenInputFailed, $"'{element.Name}' is read-only.");
            }

            valuePattern.SetValue(value);
            return OperationResult.Ok();
        }
        catch (ElementNotAvailableException ex)
        {
            return OperationResult.Fail<Unit>(
                FailureCode.MeituTargetLost, $"'{element.Name}' disappeared before it could be set: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            return OperationResult.Fail<Unit>(
                FailureCode.MeituOpenInputFailed, $"Setting '{element.Name}' failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public OperationResult<IReadOnlyList<string>> ReadTextSnapshot(WindowHandle root, int maxItems)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxItems);

        OperationResult<AutomationElement> rootElement = RootOf(root);
        if (rootElement.IsFailure)
        {
            return OperationResult.Fail<IReadOnlyList<string>>(rootElement.Failure);
        }

        List<string> names = [];
        try
        {
            Collect(rootElement.Value, names, maxItems, depth: 0);
        }
        catch (ElementNotAvailableException ex)
        {
            return OperationResult.Fail<IReadOnlyList<string>>(UiReadInterruption.Create(
                root, "being read", ex, IsReadableRoot(root)));
        }
        catch (InvalidOperationException ex)
        {
            return OperationResult.Fail<IReadOnlyList<string>>(
                FailureCode.MeituUnknownState, $"Window {root} could not be read: {ex.Message}");
        }

        return OperationResult.Ok<IReadOnlyList<string>>(names);
    }

    private void Collect(AutomationElement element, List<string> names, int maxItems, int depth)
    {
        if (depth > _maxSnapshotDepth || names.Count >= maxItems)
        {
            return;
        }

        AutomationElementCollection children = element.FindAll(TreeScope.Children, Condition.TrueCondition);
        foreach (AutomationElement child in children)
        {
            if (names.Count >= maxItems)
            {
                return;
            }

            string name = NameOf(child);
            if (name.Length > 0)
            {
                names.Add(name);
            }

            Collect(child, names, maxItems, depth + 1);
        }
    }

    /// <summary>
    /// Re-reads the root after a walk failed, so a removed descendant is not reported as a lost window.
    /// </summary>
    /// <remarks>
    /// A fresh root must resolve from the same handle and describe that same native window. This
    /// only labels the failure; it never turns the failed walk into an observation.
    /// </remarks>
    private static bool IsReadableRoot(WindowHandle root)
    {
        try
        {
            AutomationElement element = AutomationElement.FromHandle(root.Value);
            return new IntPtr(element.Current.NativeWindowHandle) == root.Value;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Any failure to re-read the root means it is not proven readable, which stops the caller.
            return false;
        }
    }

    private static OperationResult<AutomationElement> RootOf(WindowHandle root)
    {
        if (root.IsNone)
        {
            return OperationResult.Fail<AutomationElement>(
                FailureCode.MeituWindowNotFound, "No window handle was supplied.");
        }

        try
        {
            AutomationElement element = AutomationElement.FromHandle(root.Value);
            return OperationResult.Ok(element);
        }
        catch (ElementNotAvailableException ex)
        {
            return OperationResult.Fail<AutomationElement>(
                FailureCode.MeituTargetLost, $"Window {root} is no longer available: {ex.Message}");
        }
        catch (ArgumentException ex)
        {
            return OperationResult.Fail<AutomationElement>(
                FailureCode.MeituWindowNotFound, $"Window {root} is not a usable automation root: {ex.Message}");
        }
    }

    private static Condition BuildCondition(UiElementQuery query)
    {
        List<Condition> parts = [];

        ControlType? controlType = ControlTypeOf(query.Kind);
        if (controlType is not null)
        {
            parts.Add(new PropertyCondition(AutomationElement.ControlTypeProperty, controlType));
        }

        if (!string.IsNullOrEmpty(query.Name))
        {
            parts.Add(new PropertyCondition(AutomationElement.NameProperty, query.Name));
        }

        if (!string.IsNullOrEmpty(query.AutomationId))
        {
            parts.Add(new PropertyCondition(AutomationElement.AutomationIdProperty, query.AutomationId));
        }

        return parts.Count switch
        {
            0 => Condition.TrueCondition,
            1 => parts[0],
            _ => new AndCondition([.. parts]),
        };
    }

    private static ControlType? ControlTypeOf(UiControlKind kind) => kind switch
    {
        UiControlKind.Button => ControlType.Button,
        UiControlKind.Edit => ControlType.Edit,
        UiControlKind.ListItem => ControlType.ListItem,
        UiControlKind.Text => ControlType.Text,
        UiControlKind.ProgressBar => ControlType.ProgressBar,
        _ => null,
    };

    private static string NameOf(AutomationElement element)
    {
        try
        {
            return element.Current.Name ?? string.Empty;
        }
        catch (ElementNotAvailableException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Reduces <c>ControlType.CheckBox</c> to <c>CheckBox</c>.
    /// </summary>
    /// <remarks>
    /// Signed evidence records the short form because that is what a person reading a UI
    /// inspector sees. Normalising here, once, keeps the evidence files free of a .NET-specific
    /// prefix that would have to be repeated in every one of them.
    /// </remarks>
    private static string ShortControlTypeName(ControlType controlType)
    {
        string programmatic = controlType.ProgrammaticName ?? string.Empty;
        int separator = programmatic.LastIndexOf('.');
        return separator >= 0 && separator < programmatic.Length - 1
            ? programmatic[(separator + 1)..]
            : programmatic;
    }

    /// <summary>
    /// Maps the element's supported patterns onto the closed <see cref="UiPatternKind"/> set.
    /// </summary>
    /// <remarks>
    /// Patterns this slice has no vocabulary for are dropped rather than surfaced as strings.
    /// The consequence is deliberate: a rule can only ever require a pattern that
    /// <see cref="UiPatternKind"/> names, so signed evidence cannot demand something the
    /// adapter has no reviewed way to use.
    /// </remarks>
    private static ImmutableArray<UiPatternKind> PatternsOf(AutomationElement element)
    {
        ImmutableArray<UiPatternKind>.Builder patterns = ImmutableArray.CreateBuilder<UiPatternKind>();

        foreach (AutomationPattern supported in element.GetSupportedPatterns())
        {
            UiPatternKind? kind = KindOf(supported);
            if (kind is { } value && !patterns.Contains(value))
            {
                patterns.Add(value);
            }
        }

        return patterns.ToImmutable();
    }

    private static UiPatternKind? KindOf(AutomationPattern pattern)
    {
        if (pattern == InvokePattern.Pattern)
        {
            return UiPatternKind.Invoke;
        }

        if (pattern == ValuePattern.Pattern)
        {
            return UiPatternKind.Value;
        }

        if (pattern == TogglePattern.Pattern)
        {
            return UiPatternKind.Toggle;
        }

        if (pattern == SelectionItemPattern.Pattern)
        {
            return UiPatternKind.SelectionItem;
        }

        if (pattern == ExpandCollapsePattern.Pattern)
        {
            return UiPatternKind.ExpandCollapse;
        }

        return pattern == WindowPattern.Pattern ? UiPatternKind.Window : null;
    }
}
