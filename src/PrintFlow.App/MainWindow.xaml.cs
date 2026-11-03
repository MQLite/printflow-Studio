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
        if (DataContext is not ShellViewModel shell || shell.Current is not IAiAuthoringCloseGuard guard) return;
        e.Cancel = true;
        if (closePending) return;
        closePending = true;
        try
        {
            if (await guard.RequestCloseAsync(CancellationToken.None) && ReferenceEquals(shell.Current, guard))
            {
                // A clean draft can answer synchronously; unwind the first Closing event.
                await Dispatcher.InvokeAsync(() =>
                { if (ReferenceEquals(shell.Current, guard)) { closeApproved = true; Close(); } });
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
