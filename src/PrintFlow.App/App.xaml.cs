using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using PrintFlow.App.Composition;
using PrintFlow.App.Navigation;
using PrintFlow.App.Resources;
using PrintFlow.App.Startup;
using PrintFlow.App.ViewModels;
using PrintFlow.Workflow.AiDesign;

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
    private readonly CancellationTokenSource _shutdown = new();

    protected override async void OnStartup(StartupEventArgs e)
    {
        StartupTiming.Log.Mark("Startup");
        base.OnStartup(e);

        // Closing the startup window must never terminate WPF during the handoff or refusal.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        StartupStatus status;
        try
        {
            var waiting = new StartupWindow();
            waiting.ContentRendered += (_, _) => StartupTiming.Log.Mark("StartupWindowRendered");
            waiting.Show();
            try
            {
                // SQLite, preset verification, and workspace recovery can run synchronously
                // inside their async methods. Keep that work off the WPF dispatcher.
                StartupResult result = await Task.Run(() => _startup.RunAsync(_shutdown.Token));
                status = result.Status;
                if (status.CanShowShell)
                {
                    _services = result.Services!;
                    var main = new MainWindow { DataContext = _services.GetRequiredService<ShellViewModel>() };
                    MainWindow = main;
                    await _services.GetRequiredService<INavigationService>().GoHomeAsync(_shutdown.Token);
                    StartupTiming.Log.Mark("HomeDataReady");

                    TaskCompletionSource handoff = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    void CompleteHandoff(object? sender, EventArgs args) => handoff.TrySetResult();
                    main.ContentRendered += CompleteHandoff;
                    main.Closed += CompleteHandoff;
                    try
                    {
                        main.Show();
                        await handoff.Task;
                    }
                    finally
                    {
                        main.ContentRendered -= CompleteHandoff;
                        main.Closed -= CompleteHandoff;
                    }

                    if (!main.IsVisible)
                    {
                        Shutdown();
                        return;
                    }
                    StartupTiming.Log.Mark("HomeReady");
                }
                else
                {
                    result.Dispose();
                }
            }
            finally
            {
                waiting.Dismiss();
            }
        }
        catch (Exception ex)
        {
            Refuse(ex.Message);
            return;
        }

        if (!status.IsPrimaryInstance && status.Failure is null)
        {
            // A second instance: it ran no recovery, opened no database, and owns nothing to
            // clean up. One sentence and out (Part 3C1 §5).
            MessageBox.Show(
                Strings.Startup_AlreadyRunning, Strings.App_Title,
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(ExitCodes.AlreadyRunning);
            return;
        }

        if (!status.CanShowShell)
        {
            Refuse(status.Failure?.ToString() ?? Strings.Startup_Failed);
            return;
        }

        ShutdownMode = ShutdownMode.OnLastWindowClose;
        var services = _services!;
        var navigation = services.GetRequiredService<INavigationService>();
        if (e.Args is ["--resume-work-result", var configuration])
        {
            var home = navigation.Current;
            var imported = await services.GetRequiredService<IAiDesignWorkResultImport>()
                .ResumeAsync(configuration, _shutdown.Token);
            if (_shutdown.IsCancellationRequested) return;
            if (imported.IsSuccess && ReferenceEquals(home, navigation.Current))
                navigation.GoToSession(imported.Value);
            else if (imported.IsFailure)
                MessageBox.Show(imported.Failure.TechnicalDetail, Strings.App_Title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            var receiver = services.GetRequiredService<IAiDesignWorkResultImport>();
            var shutdown = _shutdown.Token;
            _ = Task.Run(() => receiver.ResumePendingAsync(shutdown));
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _shutdown.Cancel();
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
