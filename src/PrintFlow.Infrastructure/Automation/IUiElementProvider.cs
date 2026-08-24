using System.Collections.Immutable;
using PrintFlow.Domain.Results;

namespace PrintFlow.Infrastructure.Automation;

/// <summary>The control kinds this slice knows how to look for.</summary>
/// <remarks>
/// A closed set rather than a raw UI Automation control-type id: the adapter can only ask for
/// things the automation strategy has a documented reason to touch.
/// </remarks>
public enum UiControlKind
{
    Any,
    Button,
    Edit,
    ListItem,
    Text,
    ProgressBar,
}

/// <summary>
/// A search for one element inside one already-verified window.
/// </summary>
/// <param name="Kind">The control kind to match.</param>
/// <param name="Name">Exact automation name, when the workstation baseline supplies one.</param>
/// <param name="AutomationId">Exact automation id, when the control has a stable one.</param>
/// <remarks>
/// There is deliberately no free-form condition tree, no XPath and no "search the desktop"
/// root: a query is always evaluated beneath a specific window handle that
/// <see cref="IExternalAppWindowLocator"/> has already attributed to the expected process
/// (Epic 11300 Part A §8, §11).
/// </remarks>
public sealed record UiElementQuery(UiControlKind Kind, string? Name = null, string? AutomationId = null)
{
    public override string ToString() =>
        $"{Kind}(Name='{Name ?? "*"}', AutomationId='{AutomationId ?? "*"}')";
}

/// <summary>
/// The UI Automation patterns this slice can reason about, named rather than passed as raw
/// pattern ids.
/// </summary>
/// <remarks>
/// A closed set for the same reason <see cref="UiControlKind"/> is one. It exists so signed
/// evidence can state "the control PrintFlow may invoke supports Invoke" as a checkable fact
/// rather than something the adapter assumes.
/// </remarks>
public enum UiPatternKind
{
    Invoke,
    Value,
    Toggle,
    SelectionItem,
    ExpandCollapse,
    Window,
}

/// <summary>An element rectangle in screen coordinates, recorded as evidence.</summary>
/// <remarks>
/// Never a click target. Nothing in this solution synthesises a pointer event and no code path
/// turns these numbers back into a coordinate. They exist so a structural rule can assert that
/// a candidate owner actually <i>encloses</i> the marker it claims to own — one of the checks
/// that separates the real owning control from a same-shaped decoy elsewhere in the tree
/// (Epic 11300 Part B1 §4).
/// </remarks>
public readonly record struct UiBounds(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;

    public double Bottom => Top + Height;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>Whether this rectangle fully encloses <paramref name="inner"/>.</summary>
    public bool Contains(UiBounds inner) =>
        !IsEmpty && !inner.IsEmpty &&
        inner.Left >= Left && inner.Top >= Top &&
        inner.Right <= Right && inner.Bottom <= Bottom;

    public override string ToString() => $"{Left:0},{Top:0} {Width:0}x{Height:0}";
}

/// <summary>
/// The stable structural facts about one located element, read once and compared against signed
/// evidence.
/// </summary>
/// <param name="ControlTypeName">The control type, e.g. <c>Text</c>, <c>CheckBox</c>.</param>
/// <param name="AutomationId">The automation id, which in Meitu's Qt tree encodes the widget path.</param>
/// <param name="Name">The automation name.</param>
/// <param name="ClassName">The framework class name, e.g. <c>QLabel</c>, <c>CardButton</c>.</param>
/// <param name="ProcessId">The owning process, re-read from the element rather than assumed.</param>
/// <param name="SupportedPatterns">Which of the patterns this slice knows about the element exposes.</param>
/// <param name="Bounds">The element rectangle, for the enclosure check.</param>
/// <param name="IsEnabled">Whether the element is enabled.</param>
/// <param name="IsOffscreen">Whether the element is scrolled or clipped out of view.</param>
/// <remarks>
/// Separated from <see cref="UiElementRef"/> so structural rules are pure functions of records a
/// test can construct — the same split that makes <c>MeituStateClassifier</c> testable with no
/// desktop, no window and no screen.
/// </remarks>
public sealed record UiElementIdentity(
    string ControlTypeName,
    string AutomationId,
    string Name,
    string ClassName,
    int ProcessId,
    ImmutableArray<UiPatternKind> SupportedPatterns,
    UiBounds Bounds,
    bool IsEnabled,
    bool IsOffscreen)
{
    /// <summary>Whether the element exposes <paramref name="pattern"/>.</summary>
    public bool Supports(UiPatternKind pattern) =>
        !SupportedPatterns.IsDefaultOrEmpty && SupportedPatterns.Contains(pattern);

    public override string ToString() =>
        $"{ControlTypeName} id='{AutomationId}' name='{Name}' class='{ClassName}' pid={ProcessId}";
}

/// <summary>A located element, valid only for as long as its window lives.</summary>
/// <param name="Native">The underlying automation element, opaque outside this assembly.</param>
/// <param name="Name">The element's automation name at lookup time, recorded as evidence.</param>
/// <param name="RootWindow">The window the element was found beneath.</param>
public sealed record UiElementRef(object Native, string Name, WindowHandle RootWindow);

/// <summary>
/// Reads and invokes UI Automation elements beneath a verified window
/// (Epic 11300 Part A §4 priority 1).
/// </summary>
/// <remarks>
/// This is the preferred interaction route precisely because it needs no screen coordinate and
/// no keystroke: invoking a named element cannot land on a different application the way a
/// click at (940, 512) can.
///
/// <see cref="ReadTextSnapshot"/> is the state-detection input. It is a read: it invokes
/// nothing, expands nothing and focuses nothing, so calling it can never change what is on
/// screen.
/// </remarks>
public interface IUiElementProvider
{
    /// <summary>Finds one element beneath <paramref name="root"/>, or fails.</summary>
    OperationResult<UiElementRef> Find(WindowHandle root, UiElementQuery query);

    /// <summary>
    /// Finds <b>every</b> element beneath <paramref name="root"/> matching
    /// <paramref name="query"/>.
    /// </summary>
    /// <remarks>
    /// Exists so ambiguity can be detected rather than silently resolved. <see cref="Find"/>
    /// returns the first match, which is the right answer only when there is exactly one; a
    /// structural rule that must refuse two plausible candidates has to be able to see both
    /// (Epic 11300 Part B1 §5). Reading many matches invokes nothing and changes nothing.
    /// </remarks>
    OperationResult<IReadOnlyList<UiElementRef>> FindAll(WindowHandle root, UiElementQuery query);

    /// <summary>Reads the structural facts of a located element.</summary>
    /// <remarks>
    /// A read, and a fresh one: the identity returned describes the element now, not when it was
    /// located, which is what lets a rule re-check ownership at the point of decision.
    /// </remarks>
    OperationResult<UiElementIdentity> Describe(UiElementRef element);

    /// <summary>
    /// Returns the parent of a located element in the control view.
    /// </summary>
    /// <remarks>
    /// Walking upward is how the owning control of a signed text marker is found. The direction
    /// matters: descending from the window and picking something that looks clickable would let
    /// PrintFlow choose a control it has no evidence for, whereas ascending from an element that
    /// signed evidence names constrains every candidate to the marker's own ancestry (§3).
    /// A root element with no parent inside the window is a failure, not <c>null</c>.
    /// </remarks>
    OperationResult<UiElementRef> GetParent(UiElementRef element);

    /// <summary>Invokes an element that has been located beneath a verified window.</summary>
    OperationResult<Unit> Invoke(UiElementRef element);

    /// <summary>Reads an element's value through the value pattern.</summary>
    /// <remarks>
    /// Exists so a write can be checked before it is acted on. Setting a file-name field and
    /// then pressing Open without looking is a bet that the write landed; if it silently did
    /// not, Open acts on whatever the dialog already had selected — which is a file PrintFlow
    /// did not choose (Epic 11300 Part B1 §12).
    /// </remarks>
    OperationResult<string> GetValue(UiElementRef element);

    /// <summary>Sets an editable element's value through the value pattern, without typing.</summary>
    /// <remarks>
    /// Preferred over a keystroke sequence for exactly the reason the whole seam exists: the
    /// value is delivered to a named control that has already been shown to live inside the
    /// verified window, so a focus change part-way through cannot redirect it elsewhere.
    /// </remarks>
    OperationResult<Unit> SetValue(UiElementRef element, string value);

    /// <summary>
    /// Reads up to <paramref name="maxItems"/> visible automation names beneath
    /// <paramref name="root"/>, for state recognition and failure evidence.
    /// </summary>
    OperationResult<IReadOnlyList<string>> ReadTextSnapshot(WindowHandle root, int maxItems);
}
