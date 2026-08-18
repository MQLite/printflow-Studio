using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using PrintFlow.App.Composition;
using PrintFlow.App.ViewModels;

namespace PrintFlow.App;

/// <summary>
/// Application entry point and owner of the service provider.
/// </summary>
/// <remarks>
/// Part 1 startup does exactly enough to prove the object graph composes and the shell
/// renders. Single-instance guarding, configuration, workspace-root resolution, database
/// migration and crash recovery are the later startup sequence (Epic 11100 plan §18).
/// </remarks>
public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _services = ServiceRegistration.BuildServiceProvider();

        MainWindow = new MainWindow
        {
            DataContext = _services.GetRequiredService<ShellViewModel>(),
        };
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        _services = null;
        base.OnExit(e);
    }
}
