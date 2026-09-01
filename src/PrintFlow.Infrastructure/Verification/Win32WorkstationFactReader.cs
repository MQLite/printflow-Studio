using System.Diagnostics;
using System.Globalization;
using PrintFlow.Infrastructure.Automation;

namespace PrintFlow.Infrastructure.Verification;

/// <summary>
/// Reads this workstation's operating-system, session, display and culture facts through
/// narrowly scoped OS calls (Epic 11500 Part A §18).
/// </summary>
/// <remarks>
/// <b>The whole environment-reading surface, and nothing more.</b> Each method answers one
/// closed question the accepted preset asks, using the specific API that answers it. There is
/// no method that takes a command, a script, a WMI query or a registry path from a caller, so
/// there is nothing here that a future caller could repurpose into a general machine-inspection
/// tool — and nothing above <c>PrintFlow.Infrastructure</c> can reach these calls at all.
/// <para>
/// Every method is total: an OS call that fails yields an "unreadable" fact rather than an
/// exception, because a verifier that throws cannot report why verification is closed.
/// </para>
/// </remarks>
internal sealed class Win32WorkstationFactReader : IWorkstationFactReader
{
    /// <inheritdoc />
    /// <remarks>
    /// <see cref="Environment.OSVersion"/> and <see cref="Environment.Is64BitOperatingSystem"/>
    /// carry version, build and architecture. The edition name is not among them, so it is read
    /// from the registry's product-name value — the single fixed value, named here and reachable
    /// through no caller-supplied path.
    /// </remarks>
    public OperatingSystemFacts ReadOperatingSystem()
    {
        Version version = Environment.OSVersion.Version;

        return new OperatingSystemFacts(
            Edition: ReadProductName() ?? "(unreadable)",
            Version: $"{version.Major}.{version.Minor}.{version.Build}",
            Build: version.Build.ToString(CultureInfo.InvariantCulture),
            Architecture: Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit");
    }

    /// <inheritdoc />
    public InteractiveSessionFacts ReadInteractiveSession()
    {
        uint sessionId;
        try
        {
            using Process current = Process.GetCurrentProcess();
            if (!NativeMethods.ProcessIdToSessionId((uint)current.Id, out sessionId))
            {
                sessionId = 0;
            }
        }
        catch (InvalidOperationException)
        {
            sessionId = 0;
        }

        return new InteractiveSessionFacts(
            UserInteractive: Environment.UserInteractive,
            SessionId: (int)sessionId,
            SessionName: Environment.GetEnvironmentVariable("SESSIONNAME"),
            IsRemoteSession: NativeMethods.GetSystemMetrics(NativeMethods.SM_REMOTESESSION) != 0,
            InputDesktopName: ReadInputDesktopName());
    }

    /// <inheritdoc />
    /// <remarks>
    /// Enumerates attached monitors and keeps the primary one's bounds and work area. No window
    /// is moved, positioned or measured, and no coordinate leaves this method: the numbers exist
    /// only to be compared against the signed baseline (§9).
    /// </remarks>
    public DisplayFacts ReadDisplay()
    {
        int count = 0;
        string? primaryDevice = null;
        DisplayRectangle? bounds = null;
        DisplayRectangle? workArea = null;

        bool OnMonitor(nint monitor, nint hdc, ref NativeMethods.RECT clip, nint data)
        {
            count++;

            NativeMethods.MONITORINFOEX info = default;
            info.Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFOEX>();
            if (!NativeMethods.GetMonitorInfo(monitor, ref info))
            {
                return true;
            }

            if ((info.Flags & NativeMethods.MONITORINFOF_PRIMARY) != 0)
            {
                primaryDevice = info.Device;
                bounds = Rectangle(info.Monitor);
                workArea = Rectangle(info.WorkArea);
            }

            return true;
        }

        NativeMethods.EnumDisplayMonitors(0, 0, OnMonitor, 0);

        uint dpi;
        try
        {
            dpi = NativeMethods.GetDpiForSystem();
        }
        catch (EntryPointNotFoundException)
        {
            dpi = 0;
        }

        return new DisplayFacts(count, primaryDevice, bounds, workArea, (int)dpi);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <see cref="NativeMethods.GetUserDefaultUILanguage"/>, not
    /// <see cref="CultureInfo.InstalledUICulture"/>. The two answer different questions and give
    /// different answers on the accepted workstation: the installed UI culture is the language
    /// Windows was set up in, while the user's UI language is what the signed-in operator sees —
    /// and therefore what Meitu and Photoshop present, which is what the accepted evidence was
    /// captured from (§10).
    /// </remarks>
    public UiCultureFacts ReadUiCulture() =>
        new(CultureNameOf(NativeMethods.GetUserDefaultUILanguage()),
            CultureNameOf(NativeMethods.GetSystemDefaultUILanguage()));

    private static string? CultureNameOf(ushort languageId)
    {
        if (languageId == 0)
        {
            return null;
        }

        try
        {
            string name = CultureInfo.GetCultureInfo(languageId).Name;
            return string.IsNullOrEmpty(name) ? null : name;
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Names the desktop currently receiving input, so a lock screen or elevation prompt is not
    /// mistaken for ordinary readiness (§8).
    /// </summary>
    private static string? ReadInputDesktopName()
    {
        nint desktop = NativeMethods.OpenInputDesktop(0, false, NativeMethods.DESKTOP_READOBJECTS);
        if (desktop == 0)
        {
            // The secure desktop refuses to be opened from an ordinary process. An unavailable
            // input desktop is itself the answer, and the verifier reads it as "not ready".
            return null;
        }

        try
        {
            ushort[] buffer = new ushort[128];
            return NativeMethods.GetUserObjectInformation(
                desktop, NativeMethods.UOI_NAME, buffer, (uint)(buffer.Length * sizeof(ushort)), out _)
                ? TrimToNull(buffer)
                : null;
        }
        finally
        {
            NativeMethods.CloseDesktop(desktop);
        }
    }

    private static string? TrimToNull(ushort[] buffer)
    {
        int length = Array.IndexOf(buffer, (ushort)0);
        if (length < 0)
        {
            length = buffer.Length;
        }

        string name = string.Concat(buffer.Take(length).Select(c => (char)c));
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    /// <summary>
    /// Reads the one fixed registry value that carries the Windows edition name.
    /// </summary>
    /// <remarks>
    /// The key and value are constants of this method. Nothing takes a registry path from a
    /// caller, and this reader is not exposed above Infrastructure, so no arbitrary registry
    /// surface reaches App or Workflow (§18, §23).
    /// </remarks>
    private static string? ReadProductName()
    {
        try
        {
            using Microsoft.Win32.RegistryKey? key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", writable: false);

            return key?.GetValue("ProductName") as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException
                                       or System.IO.IOException)
        {
            return null;
        }
    }

    private static DisplayRectangle Rectangle(NativeMethods.RECT rectangle) =>
        new(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);
}
