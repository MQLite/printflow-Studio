using System.Runtime.InteropServices;


namespace PrintFlow.Infrastructure.Automation;

/// <summary>
/// Every Win32 entry point the automation seam uses, in one file.
/// </summary>
/// <remarks>
/// Collected here rather than scattered across the implementations so that "which OS calls can
/// this application make?" is answerable by reading a single list. An architecture test asserts
/// that <c>DllImport</c>/<c>LibraryImport</c> appears nowhere outside
/// <c>PrintFlow.Infrastructure</c> (Epic 11300 Part A §26).
///
/// Note what is absent and must stay absent: <c>mouse_event</c>, <c>SetCursorPos</c>, and any
/// screen-coordinate click primitive. The only input primitive declared is <c>SendInput</c>,
/// and its single caller re-verifies the foreground window immediately beforehand
/// (Epic 11300 Part A §3).
/// </remarks>
internal static partial class NativeMethods
{
    internal const int SW_RESTORE = 9;

    internal const uint INPUT_KEYBOARD = 1;
    internal const uint KEYEVENTF_KEYUP = 0x0002;

    internal const int GWL_STYLE = -16;
    internal const long WS_VISIBLE = 0x10000000L;
    internal const long WS_DISABLED = 0x08000000L;

    internal const uint GW_OWNER = 4;

    // -- messages addressed to one identified child control -----------------------------
    //
    // These three are the whole of the control-actuation vocabulary. Each is addressed to a
    // specific window handle that has already been shown to belong to the verified process, so
    // — unlike SendInput — none of them can be delivered to whatever happens to hold focus.
    // Nothing here can express a coordinate (Epic 11400 Part A §10).

    internal const uint WM_SETTEXT = 0x000C;
    internal const uint WM_GETTEXT = 0x000D;
    internal const uint WM_GETTEXTLENGTH = 0x000E;
    internal const uint BM_CLICK = 0x00F5;

    internal const int PW_RENDERFULLCONTENT = 0x00000002;

    internal const uint SRCCOPY = 0x00CC0020;

    internal const int BI_RGB = 0;
    internal const uint DIB_RGB_COLORS = 0;

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KEYBDINPUT
    {
        internal ushort wVk;
        internal ushort wScan;
        internal uint dwFlags;
        internal uint time;
        internal nint dwExtraInfo;
    }

    /// <summary>
    /// A keyboard-only <c>INPUT</c>. The union's mouse and hardware arms are represented as
    /// padding rather than declared, so this type cannot express a mouse event at all.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct KEYBOARDINPUT
    {
        internal uint type;
        internal KEYBDINPUT ki;
        private readonly int _padding0;
        private readonly int _padding1;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BITMAPINFOHEADER
    {
        internal uint biSize;
        internal int biWidth;
        internal int biHeight;
        internal ushort biPlanes;
        internal ushort biBitCount;
        internal uint biCompression;
        internal uint biSizeImage;
        internal int biXPelsPerMeter;
        internal int biYPelsPerMeter;
        internal uint biClrUsed;
        internal uint biClrImportant;
    }

    internal delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    // -- window discovery ---------------------------------------------------------------

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [LibraryImport("user32.dll")]
    internal static partial uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindowVisible(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsIconic(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindowEnabled(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetWindowRect(nint hWnd, out RECT rect);

    [LibraryImport("user32.dll")]
    internal static partial nint GetWindow(nint hWnd, uint command);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static partial nint GetWindowLongPtr(nint hWnd, int index);

    // The wide entry points write UTF-16 code units into a caller-supplied buffer. Declared as
    // ushort[] rather than char[] because char is not blittable under runtime marshalling, and
    // the alternatives — disabling runtime marshalling for the whole assembly, or hand-written
    // unsafe pointers — are both larger changes than a cast at the one call site that reads
    // these (see Win32ExternalAppWindowLocator.ReadText).

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW")]
    internal static partial int GetWindowText(nint hWnd, [Out] ushort[] text, int count);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")]
    internal static partial int GetClassName(nint hWnd, [Out] ushort[] text, int count);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumChildWindows(nint parent, EnumWindowsProc callback, nint lParam);

    [LibraryImport("user32.dll")]
    internal static partial int GetDlgCtrlID(nint hWnd);

    // -- one identified control ----------------------------------------------------------
    //
    // Three overloads of the same entry point, one per message shape, so each call site is
    // statically prevented from passing the wrong lParam kind. There is deliberately no
    // general-purpose SendMessage(uint, nint, nint) here: a free-form message primitive would
    // let a caller send anything to anything, which is the opposite of what this seam is for.

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint SendSetText(nint hWnd, uint message, nint wParam, string text);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    internal static partial nint SendGetText(nint hWnd, uint message, nint wParam, [Out] ushort[] text);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    internal static partial nint SendControlMessage(nint hWnd, uint message, nint wParam, nint lParam);

    // -- foreground and activation ------------------------------------------------------

    [LibraryImport("user32.dll")]
    internal static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShowWindow(nint hWnd, int command);

    // -- input (keyboard only) ----------------------------------------------------------

    [LibraryImport("user32.dll")]
    internal static partial uint SendInput(uint count, [In] KEYBOARDINPUT[] inputs, int size);

    // -- evidence capture ---------------------------------------------------------------

    [LibraryImport("user32.dll")]
    internal static partial nint GetWindowDC(nint hWnd);

    [LibraryImport("user32.dll")]
    internal static partial int ReleaseDC(nint hWnd, nint hdc);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PrintWindow(nint hWnd, nint hdcBlt, int flags);

    [LibraryImport("gdi32.dll")]
    internal static partial nint CreateCompatibleDC(nint hdc);

    [LibraryImport("gdi32.dll")]
    internal static partial nint CreateCompatibleBitmap(nint hdc, int width, int height);

    [LibraryImport("gdi32.dll")]
    internal static partial nint SelectObject(nint hdc, nint handle);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteObject(nint handle);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteDC(nint hdc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool BitBlt(
        nint destination, int x, int y, int width, int height,
        nint source, int sourceX, int sourceY, uint rasterOperation);

    [LibraryImport("gdi32.dll")]
    internal static partial int GetDIBits(
        nint hdc, nint bitmap, uint startScan, uint scanLines,
        [Out] byte[] bits, ref BITMAPINFOHEADER info, uint usage);
}
