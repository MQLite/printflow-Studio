using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using PrintFlow.Domain.Results;

namespace PrintFlow.Infrastructure.Automation;

/// <summary>
/// The Windows implementation of <see cref="IExternalAppWindowLocator"/>.
/// </summary>
/// <remarks>
/// Two rules hold in every method. Ownership is decided by <c>GetWindowThreadProcessId</c> —
/// never by a window title, which any application can copy. And every Win32 or process fault is
/// converted to a structured failure here, so no <see cref="Win32Exception"/> text can reach the
/// operator's screen (Epic 11300 Part A §21).
/// </remarks>
public sealed class Win32ExternalAppWindowLocator : IExternalAppWindowLocator
{
    private const int MaxTextLength = 512;

    /// <inheritdoc />
    public OperationResult<IReadOnlyList<ExternalProcessRef>> FindProcessesByExecutable(
        string executableAbsolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableAbsolutePath);

        string processName;
        string expectedFullPath;
        try
        {
            processName = Path.GetFileNameWithoutExtension(executableAbsolutePath);
            expectedFullPath = Path.GetFullPath(executableAbsolutePath);
        }
        catch (ArgumentException ex)
        {
            return OperationResult.Fail<IReadOnlyList<ExternalProcessRef>>(
                FailureCode.MeituNotInstalled,
                $"'{executableAbsolutePath}' is not a usable executable path: {ex.Message}");
        }

        Process[] candidates;
        try
        {
            candidates = Process.GetProcessesByName(processName);
        }
        catch (InvalidOperationException ex)
        {
            return OperationResult.Fail<IReadOnlyList<ExternalProcessRef>>(
                FailureCode.MeituWindowNotFound, $"Process enumeration failed: {ex.Message}");
        }

        List<ExternalProcessRef> matches = [];
        foreach (Process candidate in candidates)
        {
            using (candidate)
            {
                // A process whose module path cannot be read is skipped rather than guessed at:
                // an unreadable identity is precisely the case that must not count as a match.
                string? modulePath = TryReadMainModulePath(candidate);
                if (modulePath is null ||
                    !string.Equals(
                        Path.GetFullPath(modulePath), expectedFullPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                DateTimeOffset started;
                try
                {
                    started = candidate.StartTime.ToUniversalTime();
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                {
                    continue;
                }

                matches.Add(new ExternalProcessRef(candidate.Id, modulePath, started));
            }
        }

        return OperationResult.Ok<IReadOnlyList<ExternalProcessRef>>(matches);
    }

    /// <inheritdoc />
    public OperationResult<ExternalProcessRef> Launch(string executableAbsolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableAbsolutePath);

        if (!File.Exists(executableAbsolutePath))
        {
            return OperationResult.Fail<ExternalProcessRef>(
                FailureCode.MeituNotInstalled,
                $"The accepted executable does not exist at '{executableAbsolutePath}'.");
        }

        ProcessStartInfo startInfo = new()
        {
            FileName = executableAbsolutePath,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executableAbsolutePath) ?? string.Empty,
        };

        try
        {
            using Process? started = Process.Start(startInfo);
            if (started is null)
            {
                return OperationResult.Fail<ExternalProcessRef>(
                    FailureCode.MeituLaunchFailed,
                    $"Starting '{executableAbsolutePath}' returned no process.");
            }

            string modulePath = TryReadMainModulePath(started) ?? executableAbsolutePath;
            return OperationResult.Ok(
                new ExternalProcessRef(started.Id, modulePath, started.StartTime.ToUniversalTime()));
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            return OperationResult.Fail<ExternalProcessRef>(
                FailureCode.MeituLaunchFailed,
                $"Starting '{executableAbsolutePath}' failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public bool IsAlive(ExternalProcessRef process)
    {
        ArgumentNullException.ThrowIfNull(process);

        try
        {
            using Process live = Process.GetProcessById(process.ProcessId);
            if (live.HasExited)
            {
                return false;
            }

            // Process ids are reused. Comparing the start time as well is what stops a recycled
            // id from being accepted as the instance PrintFlow verified earlier.
            return live.StartTime.ToUniversalTime() == process.StartedUtc;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public OperationResult<IReadOnlyList<ExternalWindowRef>> FindTopLevelWindows(ExternalProcessRef process)
    {
        ArgumentNullException.ThrowIfNull(process);

        OperationResult<List<ExternalWindowRef>> enumerated = EnumerateWindowsOf(process.ProcessId);
        if (enumerated.IsFailure)
        {
            return OperationResult.Fail<IReadOnlyList<ExternalWindowRef>>(enumerated.Failure);
        }

        List<ExternalWindowRef> visible =
        [
            .. enumerated.Value
                .Where(w => w.IsVisible && !w.Bounds.IsEmpty)
                .OrderByDescending(w => (long)w.Bounds.Width * w.Bounds.Height),
        ];

        return OperationResult.Ok<IReadOnlyList<ExternalWindowRef>>(visible);
    }

    /// <inheritdoc />
    public OperationResult<IReadOnlyList<ExternalWindowRef>> FindOwnedDialogs(
        ExternalProcessRef process, ExternalWindowRef mainWindow)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(mainWindow);

        OperationResult<List<ExternalWindowRef>> enumerated = EnumerateWindowsOf(process.ProcessId);
        if (enumerated.IsFailure)
        {
            return OperationResult.Fail<IReadOnlyList<ExternalWindowRef>>(enumerated.Failure);
        }

        List<ExternalWindowRef> dialogs =
        [
            .. enumerated.Value.Where(w =>
                w.IsVisible
                && w.Handle != mainWindow.Handle
                && NativeMethods.GetWindow(w.Handle.Value, NativeMethods.GW_OWNER) != 0),
        ];

        return OperationResult.Ok<IReadOnlyList<ExternalWindowRef>>(dialogs);
    }

    /// <inheritdoc />
    public OperationResult<ExternalWindowRef> Refresh(WindowHandle handle)
    {
        if (handle.IsNone || !NativeMethods.IsWindow(handle.Value))
        {
            return OperationResult.Fail<ExternalWindowRef>(
                FailureCode.MeituWindowNotFound, $"Window {handle} no longer exists.");
        }

        ExternalWindowRef? described = Describe(handle.Value);
        return described is null
            ? OperationResult.Fail<ExternalWindowRef>(
                FailureCode.MeituWindowNotFound, $"Window {handle} could not be read.")
            : OperationResult.Ok(described);
    }

    /// <inheritdoc />
    public OperationResult<Unit> Activate(ExternalWindowRef window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (!NativeMethods.IsWindow(window.Handle.Value))
        {
            return OperationResult.Fail<Unit>(
                FailureCode.MeituTargetLost, $"Window {window.Handle} no longer exists.");
        }

        if (NativeMethods.IsIconic(window.Handle.Value))
        {
            NativeMethods.ShowWindow(window.Handle.Value, NativeMethods.SW_RESTORE);
        }

        // The return value is deliberately ignored. Windows refuses foreground changes under
        // rules PrintFlow cannot influence, and a refusal is not itself an error — what matters
        // is the caller's subsequent ReadForeground check (§10). Treating a true here as proof
        // of focus is exactly the mistake this seam exists to prevent.
        NativeMethods.SetForegroundWindow(window.Handle.Value);
        return OperationResult.Ok();
    }

    /// <inheritdoc />
    public OperationResult<ForegroundIdentity> ReadForeground()
    {
        nint foreground = NativeMethods.GetForegroundWindow();
        if (foreground == 0)
        {
            return OperationResult.Ok(new ForegroundIdentity(WindowHandle.None, 0, "(none)"));
        }

        _ = NativeMethods.GetWindowThreadProcessId(foreground, out uint processId);

        string name;
        try
        {
            using Process owner = Process.GetProcessById((int)processId);
            name = owner.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            name = "(unknown)";
        }

        return OperationResult.Ok(new ForegroundIdentity(new WindowHandle(foreground), (int)processId, name));
    }

    private static OperationResult<List<ExternalWindowRef>> EnumerateWindowsOf(int processId)
    {
        List<ExternalWindowRef> found = [];

        bool enumerated = NativeMethods.EnumWindows(
            (handle, lParam) =>
            {
                _ = lParam;
                _ = NativeMethods.GetWindowThreadProcessId(handle, out uint owner);
                if (owner != (uint)processId)
                {
                    return true;
                }

                ExternalWindowRef? described = Describe(handle);
                if (described is not null)
                {
                    found.Add(described);
                }

                return true;
            },
            0);

        return enumerated || found.Count > 0
            ? OperationResult.Ok(found)
            : OperationResult.Fail<List<ExternalWindowRef>>(
                FailureCode.MeituWindowNotFound,
                $"Enumerating the top-level windows of process {processId} failed.");
    }

    private static ExternalWindowRef? Describe(nint handle)
    {
        _ = NativeMethods.GetWindowThreadProcessId(handle, out uint owner);
        if (owner == 0 || !NativeMethods.GetWindowRect(handle, out NativeMethods.RECT rect))
        {
            return null;
        }

        return new ExternalWindowRef(
            new WindowHandle(handle),
            (int)owner,
            ReadText(NativeMethods.GetWindowText, handle),
            ReadText(NativeMethods.GetClassName, handle),
            new WindowBounds(rect.Left, rect.Top, rect.Right, rect.Bottom),
            NativeMethods.IsWindowVisible(handle),
            NativeMethods.IsIconic(handle),
            NativeMethods.IsWindowEnabled(handle))
        {
            OwnerHandle = new WindowHandle(NativeMethods.GetWindow(handle, NativeMethods.GW_OWNER)),
        };
    }

    private static string ReadText(Func<nint, ushort[], int, int> read, nint handle)
    {
        ushort[] buffer = new ushort[MaxTextLength];
        int length = read(handle, buffer, buffer.Length);
        return length <= 0
            ? string.Empty
            : new string(MemoryMarshal.Cast<ushort, char>(buffer.AsSpan(0, Math.Min(length, buffer.Length))));
    }

    private static string? TryReadMainModulePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }
}
