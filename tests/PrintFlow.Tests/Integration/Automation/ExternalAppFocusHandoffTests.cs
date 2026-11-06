using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using PrintFlow.Infrastructure.Automation;

namespace PrintFlow.Tests.Integration.Automation;

public sealed class ExternalAppFocusHandoffTests
{
    // Opt-in because the test briefly takes foreground ownership on the interactive desktop.
    [Fact]
    public async Task Foreground_returns_when_direct_activation_is_denied()
    {
        if (Environment.GetEnvironmentVariable("PRINTFLOW_FOCUS_FALLBACK_SMOKE") != "1") return;

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ui = new Thread(() =>
        {
            var studio = new Window
            {
                Title = "PrintFlow focus smoke", Width = 480, Height = 240,
                Content = new TextBlock { Text = "PrintFlow focus smoke" },
            };
            studio.ContentRendered += async (_, _) =>
            {
                try
                {
                    studio.IsActive.ShouldBeTrue();
                    var locator = new Win32ExternalAppWindowLocator();
                    string script = "Add-Type -AssemblyName PresentationFramework; " +
                        "$w = [System.Windows.Window]::new(); $w.Title = 'PrintFlow test editor'; " +
                        "$w.Width = 480; $w.Height = 240; $null = $w.ShowDialog()";
                    string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
                    var info = new ProcessStartInfo("powershell.exe",
                        $"-NoProfile -Sta -WindowStyle Hidden -EncodedCommand {encoded}") { CreateNoWindow = true };
                    using var editor = Process.Start(info)!;
                    try
                    {
                        var studioHandle = new WindowHandle(new WindowInteropHelper(studio).Handle);
                        using (ExternalAppFocusHandoff.Begin(studioHandle.Value, () => false, () => { }))
                        {
                            for (int attempt = 0; attempt < 100 && editor.MainWindowHandle == 0; attempt++)
                            {
                                await Task.Delay(50);
                                editor.Refresh();
                            }

                            ExternalWindowRef target = locator.Refresh(new WindowHandle(editor.MainWindowHandle)).Value;
                            locator.Activate(target).IsSuccess.ShouldBeTrue();
                            for (int attempt = 0; attempt < 20 && locator.ReadForeground().Value.Handle != target.Handle; attempt++)
                            {
                                await Task.Delay(50);
                            }

                            locator.ReadForeground().Value.Handle.ShouldBe(target.Handle, "the test editor must start in front");
                            ExternalAppFocusHandoff.MarkEmptyEditor(target);
                        }

                        for (int attempt = 0; attempt < 20 && !studio.IsActive; attempt++)
                        {
                            await Task.Delay(50);
                        }

                        locator.ReadForeground().Value.Handle.ShouldBe(studioHandle, "Studio must regain the foreground");
                    }
                    finally
                    {
                        if (!editor.HasExited && (!editor.CloseMainWindow() || !editor.WaitForExit(3000)))
                        {
                            editor.Kill();
                        }
                    }

                    completion.SetResult();
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
                finally
                {
                    studio.Close();
                    Dispatcher.CurrentDispatcher.InvokeShutdown();
                }
            };
            studio.Show();
            Dispatcher.Run();
        }) { IsBackground = true };
        ui.SetApartmentState(ApartmentState.STA);
        ui.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }

    [Theory]
    [InlineData(42, true, true, true)]
    [InlineData(null, true, true, false)]
    [InlineData(99, true, true, false)]
    [InlineData(0, true, true, false)]
    [InlineData(42, true, false, false)]
    [InlineData(42, false, true, false)]
    public async Task Returns_focus_on_completion_only_when_the_operation_still_owns_it(
        int? foregroundProcess, bool usedExternalApp, bool sameInstance, bool expectedReturn)
    {
        DateTimeOffset start = DateTimeOffset.UnixEpoch;
        int? foreground = Environment.ProcessId;
        int returns = 0;

        using (ExternalAppFocusHandoff.Begin(() => { returns++; return true; }, () => { }, () => foreground, _ => start))
        {
            if (usedExternalApp)
            {
                // The Windows adapter runs on the workflow continuation, away from the UI thread.
                await Task.Run(() => ExternalAppFocusHandoff.TrackProcess(42));
            }

            foreground = foregroundProcess;
            if (!sameInstance)
            {
                start = start.AddSeconds(1);
            }

            returns.ShouldBe(0);
        }

        returns.ShouldBe(expectedReturn ? 1 : 0);
    }

    [Theory]
    [InlineData(42, false, 1, 1)]
    [InlineData(42, true, 1, 0)]
    [InlineData(99, false, 0, 1)]
    [InlineData(null, false, 0, 1)]
    public void Reports_completion_without_taking_focus_from_another_application(
        int? foreground, bool activationSucceeded, int expectedAttempts, int expectedNotifications)
    {
        int attempts = 0;
        int notifications = 0;
        using (ExternalAppFocusHandoff.Begin(() => { attempts++; return activationSucceeded; },
                   () => notifications++, () => foreground, _ => DateTimeOffset.UnixEpoch))
        {
            ExternalAppFocusHandoff.TrackProcess(42);
        }

        attempts.ShouldBe(expectedAttempts);
        notifications.ShouldBe(expectedNotifications);
    }
}
