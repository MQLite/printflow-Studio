using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PrintFlow.App.Views;

/// <summary>
/// Keeps a physical approval gesture attached to the result on which it began.
/// This is transient input handling; the bound command remains the approval authority.
/// </summary>
public sealed class ReviewApprovalButton : Button
{
    public static readonly DependencyProperty TargetIdentityProperty = DependencyProperty.Register(
        nameof(TargetIdentity), typeof(string), typeof(ReviewApprovalButton),
        new PropertyMetadata(null, OnTargetChanged));

    public string? TargetIdentity
    {
        get => (string?)GetValue(TargetIdentityProperty);
        set => SetValue(TargetIdentityProperty, value);
    }

    /// <summary>
    /// Whether the fresh-gesture guard applies (default true). When false the button behaves as a
    /// plain button: SCRUM-11148 guards background-removal Reject on the same Session.Reject
    /// button that other steps keep unguarded.
    /// </summary>
    public static readonly DependencyProperty IsGestureGuardedProperty = DependencyProperty.Register(
        nameof(IsGestureGuarded), typeof(bool), typeof(ReviewApprovalButton), new PropertyMetadata(true, OnTargetChanged));

    public bool IsGestureGuarded
    {
        get => (bool)GetValue(IsGestureGuardedProperty);
        set => SetValue(IsGestureGuardedProperty, value);
    }

    private Key? _key;
    private string? _keyTarget;
    private string? _mouseTarget;

    private static void OnTargetChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        ReviewApprovalButton button = (ReviewApprovalButton)sender;
        button._keyTarget = null;
        button._mouseTarget = null;
        if (button.IsMouseCaptured) button.ReleaseMouseCapture();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!IsGestureGuarded || e.Key is not (Key.Enter or Key.Space)) { base.OnKeyDown(e); return; }
        e.Handled = true;
        if (!e.IsRepeat && Keyboard.Modifiers == ModifierKeys.None)
        {
            _key = e.Key;
            _keyTarget = TargetIdentity;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (!IsGestureGuarded || e.Key is not (Key.Enter or Key.Space)) { base.OnKeyUp(e); return; }
        e.Handled = true;
        bool approve = _key == e.Key && _keyTarget is not null && _keyTarget == TargetIdentity;
        _key = null;
        _keyTarget = null;
        // Both keys activate on release. A down/repeat from the previous target is never
        // converted into a new approval, even if focus returned to this button meanwhile.
        if (approve) OnClick();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        _keyTarget = null;
        base.OnLostKeyboardFocus(e);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (!IsGestureGuarded) { base.OnMouseLeftButtonDown(e); return; }
        _mouseTarget = e.ClickCount == 1 ? TargetIdentity : null;
        if (_mouseTarget is null)
        {
            e.Handled = true;
            return;
        }
        base.OnMouseLeftButtonDown(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!IsGestureGuarded) { base.OnMouseLeftButtonUp(e); return; }
        bool current = _mouseTarget is not null && _mouseTarget == TargetIdentity;
        _mouseTarget = null;
        if (!current)
        {
            e.Handled = true;
            if (IsMouseCaptured) ReleaseMouseCapture();
            return;
        }
        base.OnMouseLeftButtonUp(e);
    }
}
