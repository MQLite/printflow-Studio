using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using PrintFlow.App.Composition;
using PrintFlow.App.Navigation;
using PrintFlow.App.Resources;
using PrintFlow.App.Startup;
using PrintFlow.App.ViewModels;

namespace PrintFlow.App;

/// <summary>
/// Application entry point, owner of the startup sequence and of the service provider.
/// </summary>
/// <remarks>
/// The sequence itself lives in <see cref="ApplicationStartup"/> so it is testable without WPF;
/// this class does only the two things that genuinely need the application object — showing a
/// window, and exiting (Epic 11100 Part 3C1 §4, §5).
/// <para>
/// <see cref="ApplicationStartup"/> holds the single-instance guard and is disposed in
/// <see cref="OnExit"/>, so the primary instance owns the guard for its entire lifetime and
/// releases it only on ordinary shutdown.
/// </para>
/// </remarks>
public partial class App : Application
{
    private readonly ApplicationStartup _startup = ApplicationStartup.ForInstalledLayout();
    private ServiceProvider? _services;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        StartupResult result;
        try
        {
            result = await _startup.RunAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Nothing has been shown yet, so an unexpected startup fault must surface here or
            // vanish. Refusing to start is the safe direction: the alternative is a shell whose
            // recovery pass never completed.
            Refuse(ex.Message);
            return;
        }

        StartupStatus status = result.Status;
        if (!status.IsPrimaryInstance && status.Failure is null)
        {
            // A second instance: it ran no recovery, opened no database, and owns nothing to
            // clean up. One sentence and out (Part 3C1 §5).
            MessageBox.Show(
                Strings.Startup_AlreadyRunning, Strings.App_Title,
                MessageBoxButton.OK, MessageBoxImage.Information);
            result.Dispose();
            Shutdown(ExitCodes.AlreadyRunning);
            return;
        }

        if (!status.CanShowShell)
        {
            result.Dispose();
            Refuse(status.Failure?.ToString() ?? Strings.Startup_Failed);
            return;
        }

        _services = result.Services;

        MainWindow = new MainWindow
        {
            DataContext = _services!.GetRequiredService<ShellViewModel>(),
        };
        MainWindow.Show();

        // Home is reachable only from here — after the guard was claimed, migrations applied and
        // recovery completed. There is no other caller that can put a screen on the window
        // (Part 3C2 §2).
        await _services!.GetRequiredService<INavigationService>().GoHomeAsync(CancellationToken.None);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        _services = null;

        // Releases the single-instance guard — ordinary shutdown is its whole lifetime.
        _startup.Dispose();

        base.OnExit(e);
    }

    private void Refuse(string technicalDetail)
    {
        MessageBox.Show(
            $"{Strings.Startup_Failed}\n\n{technicalDetail}", Strings.App_Title,
            MessageBoxButton.OK, MessageBoxImage.Error);
        Shutdown(ExitCodes.StartupRefused);
    }

    /// <summary>Process exit codes, so a launcher or a smoke test can tell the cases apart.</summary>
    private static class ExitCodes
    {
        internal const int AlreadyRunning = 2;
        internal const int StartupRefused = 3;
    }
}
