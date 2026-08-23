using System.Windows.Automation;
using PrintFlow.Domain.Results;

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
            return OperationResult.Fail<IReadOnlyList<string>>(
                FailureCode.MeituTargetLost, $"Window {root} disappeared while being read: {ex.Message}");
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
}
