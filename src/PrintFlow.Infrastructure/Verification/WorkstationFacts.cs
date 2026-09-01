using PrintFlow.Domain.Files;

namespace PrintFlow.Infrastructure.Verification;

/// <summary>A rectangle as both the preset and Win32 state one.</summary>
public sealed record DisplayRectangle(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    public override string ToString() => $"{Width}x{Height} at ({Left},{Top})";
}

/// <summary>The operating-system facts the accepted baseline names, and only those (§7).</summary>
public sealed record OperatingSystemFacts(string Edition, string Version, string Build, string Architecture);

/// <summary>
/// Whether this process is running on a desktop a person is actually sitting at (§8).
/// </summary>
/// <param name="UserInteractive">False in a service or other non-interactive station.</param>
/// <param name="SessionId">0 is the isolated services session; a console user gets 1 or higher.</param>
/// <param name="SessionName">"Console" for the local console; an RDP session names itself.</param>
/// <param name="IsRemoteSession">True when the session is being served over a remote protocol.</param>
/// <param name="InputDesktopName">
/// The desktop currently receiving input, or <c>null</c> when it could not be opened. "Default"
/// is the ordinary user desktop; "Winlogon" is the secure desktop behind a lock screen or UAC
/// prompt, which is emphatically not ordinary readiness.
/// </param>
public sealed record InteractiveSessionFacts(
    bool UserInteractive,
    int SessionId,
    string? SessionName,
    bool IsRemoteSession,
    string? InputDesktopName);

/// <summary>The current display topology and scaling (§9).</summary>
/// <param name="ScalePercent">Derived from <paramref name="SystemDpi"/> against the 96-DPI base.</param>
public sealed record DisplayFacts(
    int ActiveDisplayCount,
    string? PrimaryDeviceName,
    DisplayRectangle? MonitorBounds,
    DisplayRectangle? WorkArea,
    int SystemDpi)
{
    public int ScalePercent => SystemDpi <= 0 ? 0 : (int)Math.Round(SystemDpi * 100.0 / 96.0);
}

/// <summary>
/// The two Windows UI languages, kept apart because they differ on this workstation (§10).
/// </summary>
/// <param name="UserUiCulture">
/// The signed-in user's UI language. This is what Meitu and Photoshop's own Windows chrome
/// follow, and therefore what the accepted external-app evidence was captured under.
/// </param>
/// <param name="SystemUiCulture">
/// The language Windows was installed in. Reported for context and never verified against:
/// the accepted baseline says nothing about it, and failing on it would be exactly the
/// unrelated-OS-property failure §7 forbids.
/// </param>
/// <remarks>
/// Neither is PrintFlow's own UI locale. PrintFlow renders in whichever of en-US or zh-CN the
/// operator selected, and that choice has no bearing on which localised surfaces the external
/// applications present (§10).
/// </remarks>
public sealed record UiCultureFacts(string? UserUiCulture, string? SystemUiCulture);

/// <summary>What is true of one file on disk, without opening the application that owns it.</summary>
/// <param name="Sha256">Null when the file is absent or could not be read.</param>
/// <param name="IsReadOnly">Reported for the §15 advisory; never a verification condition.</param>
public sealed record FileIdentityFacts(
    bool Exists,
    long Bytes,
    Sha256? Sha256,
    string? ProductVersion,
    string? FileVersion,
    bool IsReadOnly)
{
    /// <summary>The answer for a path that is not there at all.</summary>
    public static readonly FileIdentityFacts Absent = new(false, 0, null, null, null, false);
}

/// <summary>What is true of one directory (§11).</summary>
/// <param name="ExistsAsDirectory">True only for a directory; a file at the path is not one.</param>
/// <param name="ExistsAsFile">A file where a directory was required is its own distinct failure.</param>
/// <param name="IsReparsePoint">
/// A junction, symlink or <c>subst</c> target at the accepted root means the contract's path and
/// the bytes it reaches are no longer the same claim.
/// </param>
/// <param name="ResolvedFullPath">The canonical path, for comparison against the accepted root.</param>
/// <param name="FileSystem">"NTFS" on the accepted volume; null when it could not be read.</param>
public sealed record DirectoryFacts(
    bool ExistsAsDirectory,
    bool ExistsAsFile,
    bool IsReparsePoint,
    string? ResolvedFullPath,
    string? FileSystem)
{
    public static readonly DirectoryFacts Absent = new(false, false, false, null, null);
}

/// <summary>
/// The narrow window through which the verifier sees the machine (Part A §18).
/// </summary>
/// <remarks>
/// Four methods, each returning one closed record of exactly the facts the accepted preset
/// names. This is the whole environment-reading surface: there is no <c>RunPowerShell</c>,
/// no <c>RunCommand</c>, and no registry reader taking a caller-supplied path anywhere in the
/// solution, so no caller — in Infrastructure or above it — can ask the operating system a
/// question this contract does not already spell out.
/// <para>
/// An interface rather than static calls so the failing halves of §22's matrix can be tested
/// deterministically. A test can present a 125%-scaled dual-monitor remote session on any
/// machine; it cannot reconfigure the one the suite is running on.
/// </para>
/// </remarks>
public interface IWorkstationFactReader
{
    /// <summary>Reads the OS edition, version, build and architecture.</summary>
    OperatingSystemFacts ReadOperatingSystem();

    /// <summary>Reads the current session and input desktop. Dynamic: never cache the answer.</summary>
    InteractiveSessionFacts ReadInteractiveSession();

    /// <summary>Reads the current display topology and system DPI. Dynamic.</summary>
    DisplayFacts ReadDisplay();

    /// <summary>Reads the user and system UI languages. Dynamic.</summary>
    UiCultureFacts ReadUiCulture();
}

/// <summary>
/// The narrow window through which the verifier sees signed artefacts on disk (Part A §17).
/// </summary>
/// <remarks>
/// Reads bytes and version resources. It never starts a process: the accepted Meitu and
/// Photoshop identities are established from the binaries themselves, so static verification
/// launches neither application — nor Maintop, whose executable the accepted preset explicitly
/// records as uncaptured.
/// </remarks>
public interface IWorkstationArtifactReader
{
    /// <summary>Hashes a file in full and reads its version resource, without executing it.</summary>
    FileIdentityFacts ReadFile(string absolutePath);

    /// <summary>Describes a directory without enumerating or reading anything inside it.</summary>
    DirectoryFacts ReadDirectory(string absolutePath);
}
