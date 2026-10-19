using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;

namespace PrintFlow.App.Views;

/// <summary>A fresh gesture or acquired Invoke provider belongs to one shown session context.</summary>
public class SessionActionButton : Button
{
    protected sealed record InputStamp(object? Scope, long Version, ICommand? Command, object? Parameter);
    private long _inputVersion;
    private bool _active = true;
    private Key? _key;
    private InputStamp? _keyStamp;
    private InputStamp? _mouseStamp;

    public SessionActionButton()
    {
        Loaded += (_, _) => { _active = true; InvalidateInput(); };
        Unloaded += (_, _) => { _active = false; InvalidateInput(); };
    }

    protected InputStamp CaptureInput() => new(SessionActionScope.State(this), _inputVersion, Command, CommandParameter);
    protected bool IsCurrentInput(InputStamp? stamp) => stamp is not null && _active &&
        stamp.Version == _inputVersion && ReferenceEquals(stamp.Scope, SessionActionScope.State(this)) &&
        ReferenceEquals(stamp.Command, Command) && Equals(stamp.Parameter, CommandParameter) && SessionActionScope.CanInteract(this);
    protected void InvalidateInput()
    {
        _inputVersion++;
        _keyStamp = null; _mouseStamp = null;
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == VisibilityProperty || e.Property == IsEnabledProperty || e.Property == CommandProperty || e.Property == CommandParameterProperty)
            InvalidateInput();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space)) { base.OnKeyDown(e); return; }
        e.Handled = true;
        if (!e.IsRepeat && Keyboard.Modifiers == ModifierKeys.None && SessionActionScope.CanInteract(this))
        {
            _key = e.Key;
            _keyStamp = CaptureInput();
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space)) { base.OnKeyUp(e); return; }
        e.Handled = true;
        bool invoke = _key == e.Key && IsCurrentInput(_keyStamp);
        _key = null; _keyStamp = null;
        if (invoke) OnClick();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        _keyStamp = null;
        base.OnLostKeyboardFocus(e);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _mouseStamp = e.ClickCount == 1 && SessionActionScope.CanInteract(this) ? CaptureInput() : null;
        if (_mouseStamp is null) { e.Handled = true; return; }
        base.OnMouseLeftButtonDown(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        bool current = IsCurrentInput(_mouseStamp);
        _mouseStamp = null;
        if (!current)
        {
            e.Handled = true;
            if (IsMouseCaptured) ReleaseMouseCapture();
            return;
        }
        base.OnMouseLeftButtonUp(e);
    }

    protected override void OnClick()
    {
        if (_active && SessionActionScope.CanInteract(this)) base.OnClick();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new SessionActionPeer(this);

    private sealed class SessionActionPeer(SessionActionButton owner) : ButtonAutomationPeer(owner)
    {
        // Providers are per acquisition, not the cached peer itself. A retained provider cannot
        // start operating a later context, while a new accessibility request remains supported.
        public override object? GetPattern(PatternInterface patternInterface) => patternInterface == PatternInterface.Invoke
            ? new CapturedInvoke(owner, owner.CaptureInput()) : base.GetPattern(patternInterface);
    }

    private sealed class CapturedInvoke(SessionActionButton owner, InputStamp stamp) : IInvokeProvider
    {
        public void Invoke()
        {
            void Apply() { if (owner.IsCurrentInput(stamp)) owner.OnClick(); }
            // Match ButtonAutomationPeer's asynchronous dispatch, including the WPF
            // synchronization context used by asynchronous commands.
            owner.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(Apply));
        }
    }
}
