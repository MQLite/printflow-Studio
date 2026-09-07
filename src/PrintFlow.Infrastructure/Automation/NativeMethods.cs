using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;


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
    // Workspace retention reads link identity before clearing a redundant copy's readonly
    // attribute. This query cannot mutate files or interact with external applications.
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation information);

    [StructLayout(LayoutKind.Sequential)]
    internal struct FileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

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

    // -- Running Object Table attachment ------------------------------------------------
    //
    // These calls only retrieve an object that is already running. They do not activate a COM
    // LocalServer, which is essential on the accepted workstation: the Photoshop CC 2019
    // registration names an excluded stale executable, while the exact accepted process is
    // independently identified before this route is used.

    [DllImport("ole32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    internal static extern int CLSIDFromProgID(string progId, out Guid classId);

    [DllImport("oleaut32.dll", PreserveSig = true)]
    internal static extern int GetActiveObject(
        ref Guid classId,
        nint reserved,
        [MarshalAs(UnmanagedType.IUnknown)] out object? activeObject);

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

    // -- workstation verification (Epic 11500 Part A §18) --------------------------------
    //
    // Narrow, named readers for exactly the facts the accepted preset states: the user's UI
    // language, whether this is a local console session at an ordinary desktop, and the display
    // topology and system DPI. Each answers one closed question.
    //
    // Note what is absent and must stay absent from this group: no arbitrary registry read, no
    // command or script execution, and no process start. Verification inspects signed machine
    // facts; it never runs anything.

    internal const int SM_REMOTESESSION = 0x1000;

    internal const int UOI_NAME = 2;
    internal const uint DESKTOP_READOBJECTS = 0x0001;

    internal const uint MONITORINFOF_PRIMARY = 0x00000001;

    /// <summary>The signed-in user's UI language — what Meitu's and Photoshop's chrome follow.</summary>
    [LibraryImport("kernel32.dll")]
    internal static partial ushort GetUserDefaultUILanguage();

    /// <summary>The language Windows was installed in. Reported for context, never verified against.</summary>
    [LibraryImport("kernel32.dll")]
    internal static partial ushort GetSystemDefaultUILanguage();

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ProcessIdToSessionId(uint processId, out uint sessionId);

    [LibraryImport("user32.dll")]
    internal static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll")]
    internal static partial uint GetDpiForSystem();

    /// <summary>Opens the desktop currently receiving input, to learn its name and nothing else.</summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial nint OpenInputDesktop(
        uint flags, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint desiredAccess);

    [LibraryImport("user32.dll", EntryPoint = "GetUserObjectInformationW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetUserObjectInformation(
        nint handle, int index, [Out] ushort[] buffer, uint byteCount, out uint required);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseDesktop(nint desktop);

    /// <summary>
    /// One monitor's bounds, work area and device name.
    /// </summary>
    /// <remarks>
    /// Non-blittable because of the inline device-name buffer, so its two entry points use
    /// <c>DllImport</c> rather than the source-generated <c>LibraryImport</c>. The buffer is
    /// marshalled by the runtime; no pointer arithmetic is written here.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MONITORINFOEX
    {
        internal uint Size;
        internal RECT Monitor;
        internal RECT WorkArea;
        internal uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        internal string Device;
    }

    internal delegate bool MonitorEnumProc(nint monitor, nint hdc, ref RECT clip, nint data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplayMonitors(
        nint hdc, nint clip, MonitorEnumProc callback, nint data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(nint monitor, ref MONITORINFOEX info);
}
