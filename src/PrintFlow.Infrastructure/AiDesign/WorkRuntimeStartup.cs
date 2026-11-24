using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using PrintFlow.Infrastructure.Workspace;

namespace PrintFlow.Infrastructure.AiDesign;

internal static class WorkRuntimeStartup
{
    public static async Task<string?> EnsureAsync(string config, string workspace, CancellationToken ct)
    {
        // Installations with an externally managed receiver keep their existing submission path.
        var manifest = Path.ChangeExtension(config, "startup.json");
        if (!File.Exists(manifest)) return null;
        ct.ThrowIfCancellationRequested();
        try
        {
            // Only the per-user Work settings can opt in to launching local executables.
            var settings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PrintFlowStudio", "Work");
            if (!Path.GetFullPath(manifest).StartsWith(settings + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return "WorkRuntimeStartupFailed";
            ReparsePointGuard.RefuseAncestry(manifest);
            var script = Path.Combine(AppContext.BaseDirectory, "WorkRuntime", "Ensure-PrintFlowWorkRuntime.ps1");
            ReparsePointGuard.RefuseAncestry(script);
            var shell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            var launch = new ProcessStartInfo(shell) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-File", script, "-StudioConfig", config, "-WorkspaceRoot", workspace })
                launch.ArgumentList.Add(arg);

            using var setup = Process.Start(launch);
            if (setup is null) return "WorkRuntimeStartupFailed";
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(85));
            try
            {
                await setup.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (!setup.HasExited) setup.Kill();
                ct.ThrowIfCancellationRequested();
                return "WorkRuntimeStartupFailed";
            }

            return setup.ExitCode switch
            {
                0 => null,
                10 => "WorkReceiverUnavailable",
                11 => "WorkTunnelUnavailable",
                _ => "WorkRuntimeStartupFailed"
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            return "WorkRuntimeStartupFailed";
        }
    }
}
