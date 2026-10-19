using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;

namespace PrintFlow.App.Views;

// Editing providers have the same presentation lifetime as action providers. Keep WPF's
// native patterns and read operations; only a mutation requires the captured UI lifetime.
internal sealed class SessionInputGuard
{
    private readonly FrameworkElement _owner;
    private readonly List<UIElement> _ancestors = [];
    private static readonly DependencyPropertyDescriptor VisibilityDescriptor =
        DependencyPropertyDescriptor.FromProperty(UIElement.VisibilityProperty, typeof(UIElement));
    private long _version;
    private bool _active = true;

    internal SessionInputGuard(FrameworkElement owner)
    {
        _owner = owner;
        owner.DataContextChanged += (_, _) => { _version++; DetachAncestors(); };
        owner.IsEnabledChanged += (_, _) => _version++;
        owner.Loaded += (_, _) => { _active = true; _version++; };
        owner.Unloaded += (_, _) =>
        {
            _active = false; _version++;
            DetachAncestors();
        };
    }

    private void Changed(object? sender, EventArgs args) => _version++;

    private void DetachAncestors()
    {
        foreach (var ancestor in _ancestors) VisibilityDescriptor.RemoveValueChanged(ancestor, Changed);
        _ancestors.Clear();
    }

    internal Func<bool> Capture()
    {
        // Ancestor visibility matters even on an off-screen tree where IsVisible is always false.
        for (DependencyObject? current = _owner; current is not null; current =
            current is Visual ? VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
            if (current is UIElement element && !_ancestors.Contains(element))
            {
                _ancestors.Add(element); VisibilityDescriptor.AddValueChanged(element, Changed);
            }
        var scope = SessionActionScope.State(_owner); var context = _owner.DataContext; long version = _version;
        return () => _active && version == _version && ReferenceEquals(context, _owner.DataContext) &&
            ReferenceEquals(scope, SessionActionScope.State(_owner)) && SessionActionScope.CanInteract(_owner);
    }

    // Native peers implement several interfaces on one object. Select by the requested
    // pattern, never by the first interface the object happens to implement.
    internal object? Wrap(PatternInterface requested, object? pattern) => requested switch
    {
        PatternInterface.Value when pattern is IValueProvider value => new CapturedValue(value, Capture()),
        PatternInterface.SelectionItem when pattern is ISelectionItemProvider selection => new Selection(selection, Capture()),
        PatternInterface.Toggle when pattern is IToggleProvider toggle => new CapturedToggle(toggle, Capture()),
        PatternInterface.RangeValue when pattern is IRangeValueProvider range => new Range(range, Capture()),
        PatternInterface.ExpandCollapse when pattern is IExpandCollapseProvider expand => new CapturedExpand(expand, Capture()),
        _ => pattern,
    };

    private sealed class CapturedValue(IValueProvider inner, Func<bool> current) : IValueProvider
    {
        public bool IsReadOnly => inner.IsReadOnly;
        public string Value => inner.Value;
        public void SetValue(string value) { if (current()) inner.SetValue(value); }
    }
    private sealed class Selection(ISelectionItemProvider inner, Func<bool> current) : ISelectionItemProvider
    {
        public bool IsSelected => inner.IsSelected;
        public IRawElementProviderSimple SelectionContainer => inner.SelectionContainer;
        public void AddToSelection() { if (current()) inner.AddToSelection(); }
        public void RemoveFromSelection() { if (current()) inner.RemoveFromSelection(); }
        public void Select() { if (current()) inner.Select(); }
    }
    private sealed class CapturedToggle(IToggleProvider inner, Func<bool> current) : IToggleProvider
    {
        public ToggleState ToggleState => inner.ToggleState;
        public void Toggle() { if (current()) inner.Toggle(); }
    }
    private sealed class Range(IRangeValueProvider inner, Func<bool> current) : IRangeValueProvider
    {
        public bool IsReadOnly => inner.IsReadOnly;
        public double Value => inner.Value;
        public double Minimum => inner.Minimum;
        public double Maximum => inner.Maximum;
        public double SmallChange => inner.SmallChange;
        public double LargeChange => inner.LargeChange;
        public void SetValue(double value) { if (current()) inner.SetValue(value); }
    }
    private sealed class CapturedExpand(IExpandCollapseProvider inner, Func<bool> current) : IExpandCollapseProvider
    {
        public ExpandCollapseState ExpandCollapseState => inner.ExpandCollapseState;
        public void Expand() { if (current()) inner.Expand(); }
        public void Collapse() { if (current()) inner.Collapse(); }
    }
}

public sealed class SessionTextBox : TextBox
{
    private readonly SessionInputGuard _input;
    public SessionTextBox() => _input = new(this);
    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);
    private sealed class Peer(SessionTextBox owner) : TextBoxAutomationPeer(owner)
    {
        public override object? GetPattern(PatternInterface pattern) => owner._input.Wrap(pattern, base.GetPattern(pattern));
    }
}

public sealed class SessionComboBox : ComboBox
{
    private readonly SessionInputGuard _input;
    public SessionComboBox() => _input = new(this);
    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);
    private sealed class Peer(SessionComboBox owner) : ComboBoxAutomationPeer(owner)
    {
        public override object? GetPattern(PatternInterface pattern) => owner._input.Wrap(pattern, base.GetPattern(pattern));
        protected override ItemAutomationPeer CreateItemAutomationPeer(object item) => new ItemPeer(item, this, owner._input);
    }
    private sealed class ItemPeer(object item, ComboBoxAutomationPeer parent, SessionInputGuard input) : ListBoxItemAutomationPeer(item, parent)
    {
        public override object? GetPattern(PatternInterface pattern) => input.Wrap(pattern, base.GetPattern(pattern));
    }
}

public sealed class SessionListBox : ListBox
{
    private readonly SessionInputGuard _input;
    public SessionListBox() => _input = new(this);
    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);
    private sealed class Peer(SessionListBox owner) : ListBoxAutomationPeer(owner)
    {
        protected override ItemAutomationPeer CreateItemAutomationPeer(object item) => new ItemPeer(item, this, owner._input);
    }
    private sealed class ItemPeer(object item, ListBoxAutomationPeer parent, SessionInputGuard input) : ListBoxItemAutomationPeer(item, parent)
    {
        public override object? GetPattern(PatternInterface pattern) => input.Wrap(pattern, base.GetPattern(pattern));
    }
}

public sealed class SessionRadioButton : RadioButton
{
    private readonly SessionInputGuard _input;
    public SessionRadioButton() => _input = new(this);
    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);
    private sealed class Peer(SessionRadioButton owner) : RadioButtonAutomationPeer(owner)
    {
        public override object? GetPattern(PatternInterface pattern) => owner._input.Wrap(pattern, base.GetPattern(pattern));
    }
}

public sealed class SessionExpander : Expander
{
    private readonly SessionInputGuard _input;
    public SessionExpander() => _input = new(this);
    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);
    private sealed class Peer(SessionExpander owner) : ExpanderAutomationPeer(owner)
    {
        public override object? GetPattern(PatternInterface pattern) => owner._input.Wrap(pattern, base.GetPattern(pattern));
    }
}
