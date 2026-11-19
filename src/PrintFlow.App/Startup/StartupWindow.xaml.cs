using System.ComponentModel;
using System.Windows;
using PrintFlow.App.Resources;

namespace PrintFlow.App.Startup;

public partial class StartupWindow : Window
{
    private bool _dismissed;

    public StartupWindow()
    {
        InitializeComponent();
        ProductName.Text = Strings.App_Title;
        Status.Text = Strings.Startup_Loading;
        Title = $"{ProductName.Text} — {Status.Text}";
    }

    internal void Dismiss()
    {
        _dismissed = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        e.Cancel = !_dismissed;
    }
}
