using System.Windows;
using System.Windows.Media;

namespace PrintFlow.App.Views;

/// <summary>Transient view identity. It never grants workflow or approval authority.</summary>
public static class SessionActionScope
{
    public static readonly DependencyProperty ContextProperty = DependencyProperty.RegisterAttached(
        "Context", typeof(string), typeof(SessionActionScope), new PropertyMetadata(null, ContextChanged));
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(ScopeState), typeof(SessionActionScope), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));

    public static string? GetContext(DependencyObject owner) => (string?)owner.GetValue(ContextProperty);
    public static void SetContext(DependencyObject owner, string? value) => owner.SetValue(ContextProperty, value);
    private static void ContextChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args) =>
        SetActive(owner, State(owner)?.Active ?? true);

    internal sealed class ScopeState(bool active) { public bool Active { get; } = active; }
    internal static ScopeState? State(DependencyObject owner) => (ScopeState?)owner.GetValue(StateProperty);
    internal static void SetActive(DependencyObject owner, bool active) => owner.SetValue(StateProperty, new ScopeState(active));

    internal static bool CanInteract(FrameworkElement control)
    {
        if (!control.IsEnabled || State(control) is { Active: false }) return false;
        // IsVisible is false on a deliberately off-screen WPF tree. Actual visibility and
        // enabled state through every ancestor express the same UI boundary without a HWND.
        for (DependencyObject? current = control; current is not null; current = Parent(current))
            if (current is UIElement element && (element.Visibility != Visibility.Visible || !element.IsEnabled)) return false;
        return true;
    }

    private static DependencyObject? Parent(DependencyObject owner) =>
        owner is Visual ? VisualTreeHelper.GetParent(owner) ?? LogicalTreeHelper.GetParent(owner)
            : LogicalTreeHelper.GetParent(owner);
}
