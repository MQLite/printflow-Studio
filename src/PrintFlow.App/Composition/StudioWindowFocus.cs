using System.Windows;
using System.Windows.Interop;
using PrintFlow.Infrastructure.Automation;

namespace PrintFlow.App.Composition;

internal static class StudioWindowFocus
{
    internal static IDisposable? BeginHandoff(Func<bool> canReturnFocus, bool notifyOnFailure)
    {
        Application? app = Application.Current;
        if (app is null || !app.Dispatcher.CheckAccess() || app.MainWindow is not { IsActive: true } window)
        {
            return null;
        }

        bool MayNotify() => canReturnFocus() && window.IsVisible && !window.IsActive;
        return ExternalAppFocusHandoff.Begin(
            new WindowInteropHelper(window).Handle,
            () => !MayNotify() || (window.IsEnabled && window.WindowState != WindowState.Minimized && window.Activate()),
            () =>
            {
                if (notifyOnFailure && MayNotify())
                {
                    ExternalAppFocusHandoff.SignalTaskbar(new WindowInteropHelper(window).Handle);
                }
            });
    }
}
