using System.Globalization;
using System.IO;
using System.Text;

namespace PrintFlow.Infrastructure.Startup;

/// <summary>
/// The real <see cref="ISingleInstanceGuard"/>, backed by an exclusively opened lock file
/// (Epic 11100 Part 3C1 §2, §3).
/// </summary>
/// <remarks>
/// A file opened with <see cref="FileShare.None"/> is used in preference to the more obvious
/// named mutex for two reasons, both of which matter here:
/// <list type="bullet">
///   <item>a mutex is owned by a <i>thread</i> and is re-entrant for it, so it cannot answer
///         "is another instance running?" in a way that is provable without racing two real
///         operating-system processes — precisely the unreliable test Part 3C1 §10 rules out.
///         A file lock is owned by the process, so the invariant is testable directly;</item>
///   <item>a mutex in the <c>Global\</c> namespace needs the <c>SeCreateGlobalPrivilege</c>
///         right that a standard operator account does not hold, and one in <c>Local\</c> is
///         scoped to a single desktop session. A file lock is machine-wide either way.</item>
/// </list>
/// Staleness is not a concern: Windows closes the handle when the owning process dies, however
/// it dies, so a leftover lock file is never mistaken for a live claim. The bytes written into
/// it are for a human reading the directory and are never read back as evidence — "is this
/// claim still real?" is answered by the operating system holding the handle, and, for the
/// separate automation lock, by <c>IProcessLiveness</c>.
/// <para>
/// The path is deliberately independent of <c>appsettings.json</c>, because the guard is claimed
/// before configuration is read. It therefore covers every launch by the same operator on this
/// workstation, including one from a second Windows session. Two <i>different</i> Windows
/// accounts sharing one workspace are outside its reach and stay covered by the persisted
/// automation lock, which fails closed on an <c>Alive</c> or <c>Unknown</c> owner.
/// </para>
/// </remarks>
public sealed class SingleInstanceGuard : ISingleInstanceGuard
{
    private const string LockFileName = "printflow-studio.instance.lock";

    private readonly string _lockFilePath;
    private FileStream? _handle;

    /// <param name="lockFilePath">
    /// Overrides <see cref="DefaultLockFilePath"/>. Tests use it to keep each case isolated;
    /// the application never passes it.
    /// </param>
    public SingleInstanceGuard(string? lockFilePath = null)
    {
        _lockFilePath = string.IsNullOrWhiteSpace(lockFilePath) ? DefaultLockFilePath : lockFilePath;
    }

    /// <summary>
    /// <c>%LOCALAPPDATA%\PrintFlow Studio\printflow-studio.instance.lock</c> — application-owned,
    /// always writable by the operator, and never inside the workspace the guard protects.
    /// </summary>
    public static string DefaultLockFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PrintFlow Studio",
        LockFileName);

    /// <inheritdoc />
    public bool IsHeld => _handle is not null;

    /// <inheritdoc />
    public SingleInstanceOutcome TryAcquire()
    {
        if (_handle is not null)
        {
            return SingleInstanceOutcome.Acquired;
        }

        try
        {
            string? directory = Path.GetDirectoryName(_lockFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            FileStream handle = new(
                _lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

            Stamp(handle);
            _handle = handle;
            return SingleInstanceOutcome.Acquired;
        }
        catch (IOException)
        {
            // The only way an exclusive open fails with IOException on an existing, writable
            // path is that someone else holds it — and the only "someone else" that opens this
            // path is another PrintFlow instance.
            return SingleInstanceOutcome.AlreadyRunning;
        }
        catch (UnauthorizedAccessException)
        {
            return SingleInstanceOutcome.Unavailable;
        }
        catch (NotSupportedException)
        {
            return SingleInstanceOutcome.Unavailable;
        }
    }

    /// <summary>Releases the guard. Called only from ordinary application shutdown.</summary>
    public void Dispose()
    {
        FileStream? handle = _handle;
        _handle = null;
        handle?.Dispose();
    }

    /// <summary>
    /// Records who holds the guard, for an operator or engineer looking at the file. Best
    /// effort: failing to annotate a lock we already hold is not a reason to refuse startup.
    /// </summary>
    private static void Stamp(FileStream handle)
    {
        try
        {
            byte[] stamp = Encoding.UTF8.GetBytes(string.Create(
                CultureInfo.InvariantCulture,
                $"{Environment.MachineName} process {Environment.ProcessId} since {DateTimeOffset.UtcNow:O}"));

            handle.SetLength(0);
            handle.Write(stamp, 0, stamp.Length);
            handle.Flush();
        }
        catch (IOException)
        {
        }
    }
}
