using System.Windows;
using System.ComponentModel;
using PrintFlow.App.Navigation;
using PrintFlow.App.ViewModels;

namespace PrintFlow.App;

/// <summary>
/// The Part 1 shell window. Its <c>DataContext</c> is supplied by the composition root.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();
    private bool closeApproved, closePending;

    protected override async void OnClosing(CancelEventArgs e)
    {
        bool approved = closeApproved;
        closeApproved = false;
        base.OnClosing(e);
        if (e.Cancel || approved) return;
        if (DataContext is not ShellViewModel shell) return;
        object? source = shell.Current;
        Func<CancellationToken, Task<bool>>? requestClose = source switch
        {
            IAiAuthoringCloseGuard guard => guard.RequestCloseAsync,
            SettingsViewModel { IsEditingImageProcessing: true } settings => settings.RequestImageSettingsCloseAsync,
            _ => null,
        };
        if (requestClose is null) return;
        e.Cancel = true;
        if (closePending) return;
        closePending = true;
        try
        {
            if (await requestClose(CancellationToken.None) && ReferenceEquals(shell.Current, source))
            {
                // A clean draft can answer synchronously; unwind the first Closing event.
                await Dispatcher.InvokeAsync(() =>
                { if (ReferenceEquals(shell.Current, source)) { closeApproved = true; Close(); } });
            }
        }
        finally { closePending = false; }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is ShellViewModel { Current: IEntryScreenLifetime entry }) entry.Deactivate();
        base.OnClosed(e);
    }
}
