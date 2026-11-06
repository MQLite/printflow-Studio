using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PrintFlow.Infrastructure.Automation;

/// <summary>Remembers the external process instances used by one interactive Studio operation.</summary>
public static class ExternalAppFocusHandoff
{
    // The shell starts the scope on its dispatcher. The adapters can run on background threads.
    private static readonly AsyncLocal<Scope?> Current = new();

    public static IDisposable Begin(nint studioWindow, Func<bool> returnFocus, Action notify) =>
        Begin(returnFocus, notify, ReadForegroundProcessId, ReadProcessStart, studioWindow);

    internal static IDisposable Begin(
        Func<bool> returnFocus, Action notify, Func<int?> readForegroundProcessId,
        Func<int, DateTimeOffset?> readProcessStart, nint studioWindow = 0)
    {
        var scope = new Scope(Current.Value, returnFocus, notify, readForegroundProcessId, readProcessStart, studioWindow);
        Current.Value = scope;
        return scope;
    }

    internal static void TrackProcess(int processId) => Current.Value?.Track(processId);

    internal static void MarkEmptyEditor(ExternalWindowRef editor) => Current.Value?.MarkEmptyEditor(editor);

    public static void SignalTaskbar(nint window)
    {
        if (window == 0) return;
        var info = new NativeMethods.FlashWindowInfo
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.FlashWindowInfo>(), Window = window,
            Flags = NativeMethods.FlashTaskbar, Count = 3,
        };
        _ = NativeMethods.FlashWindowEx(ref info);
    }

    private static int? ReadForegroundProcessId()
    {
        nint window = NativeMethods.GetForegroundWindow();
        if (window == 0)
        {
            return null;
        }

        _ = NativeMethods.GetWindowThreadProcessId(window, out uint processId);
        return (int)processId;
    }

    private static uint? ReadLastInputTick()
    {
        var info = new NativeMethods.LastInputInfo { Size = (uint)Marshal.SizeOf<NativeMethods.LastInputInfo>() };
        return NativeMethods.GetLastInputInfo(ref info) ? info.Tick : null;
    }

    private static DateTimeOffset? ReadProcessStart(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return process.StartTime.ToUniversalTime();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return null;
        }
    }

    private sealed class Scope(
        Scope? previous, Func<bool> returnFocus, Action notify, Func<int?> readForegroundProcessId,
        Func<int, DateTimeOffset?> readProcessStart, nint studioWindow) : IDisposable
    {
        private readonly ConcurrentDictionary<int, DateTimeOffset> _targets = new();
        private (ExternalWindowRef Window, uint LastInput)? _emptyEditor;

        public void Track(int processId)
        {
            if (!_targets.ContainsKey(processId) && readProcessStart(processId) is { } start)
            {
                _targets.TryAdd(processId, start);
            }
        }

        public void MarkEmptyEditor(ExternalWindowRef editor)
        {
            if (ReadLastInputTick() is { } tick)
            {
                _emptyEditor = (editor, tick);
            }
        }

        public void Dispose()
        {
            if (!ReferenceEquals(Current.Value, this))
            {
                return;
            }

            Current.Value = previous;
            if (_targets.IsEmpty)
            {
                return;
            }

            // A missing or unidentified foreground window is not evidence of this handoff.
            if (readForegroundProcessId() is { } foreground &&
                _targets.TryGetValue(foreground, out DateTimeOffset start) &&
                readProcessStart(foreground) == start && readForegroundProcessId() == foreground)
            {
                if (returnFocus()) return;
                YieldFromEmptyEditor(foreground);
            }

            notify();
        }

        private void YieldFromEmptyEditor(int foregroundProcess)
        {
            if (studioWindow == 0 || _emptyEditor is not { } editor || editor.Window.OwningProcessId != foregroundProcess ||
                !NativeMethods.IsWindowVisible(studioWindow) || !NativeMethods.IsWindowEnabled(studioWindow) ||
                NativeMethods.IsIconic(studioWindow))
            {
                return;
            }

            nint editorWindow = editor.Window.Handle.Value;
            bool CanYield() => NativeMethods.GetForegroundWindow() == editorWindow &&
                NativeMethods.GetWindowThreadProcessId(editorWindow, out uint owner) != 0 && owner == (uint)foregroundProcess &&
                NativeMethods.IsWindowEnabled(editorWindow) && NativeMethods.GetWindow(editorWindow, NativeMethods.GW_OWNER) == 0 &&
                (NativeMethods.GetWindowLongPtr(editorWindow, NativeMethods.GWL_EXSTYLE).ToInt64() & NativeMethods.WS_EX_TOPMOST) == 0 &&
                ReadLastInputTick() == editor.LastInput;

            // Windows activates the next window in Z order when the foreground window is minimized.
            // Only yield from the editor just verified empty by this operation.
            const uint PlaceBehindEditor = NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE |
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER;
            if (CanYield() && NativeMethods.SetWindowPos(studioWindow, editorWindow, 0, 0, 0, 0, PlaceBehindEditor) && CanYield())
            {
                _ = NativeMethods.ShowWindowAsync(editorWindow, NativeMethods.SW_MINIMIZE);
            }
        }
    }
}
