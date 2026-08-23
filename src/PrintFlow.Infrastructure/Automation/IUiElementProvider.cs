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

    /// <summary>Invokes an element that has been located beneath a verified window.</summary>
    OperationResult<Unit> Invoke(UiElementRef element);

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
