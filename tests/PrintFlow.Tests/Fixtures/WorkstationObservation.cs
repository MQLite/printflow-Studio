using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PrintFlow.Tests.Fixtures;

/// <summary>
/// Bounded, read-only observations of the running workstation, for the Epic 11600 Part B soak
/// (§7, §8, §9).
/// </summary>
/// <remarks>
/// Trend observation, not workstation surveillance. Everything here reads counters the runtime
/// already exposes about three named processes and enumerates the window classes of one of them.
/// Nothing is persisted, nothing outside those processes is inspected, and no monitoring package
/// was added to obtain any of it (§21).
/// <para>
/// The accepted executable path is never written down in this repository — it lives in the signed
/// preset, and a fixture holding a second copy would be a second thing to drift. Where a path is
/// needed it is passed in, having been read from the verified baseline at run time.
/// </para>
/// </remarks>
internal static class WorkstationObservation
{
    /// <summary>The resource counters one process is currently reporting.</summary>
    /// <param name="Present">Whether a process of that name was found at all.</param>
    /// <param name="PrivateMemory">
    /// Private bytes. Reported alongside the working set rather than instead of it: a working set
    /// falls when Windows trims it, which looks like a recovery that did not happen, whereas
    /// private bytes do not move for that reason.
    /// </param>
    internal sealed record ProcessVitals(
        string Name,
        bool Present,
        int ProcessId,
        long WorkingSet,
        long PrivateMemory,
        int Handles,
        int Threads,
        string WindowTitle)
    {
        public static ProcessVitals Absent(string name) =>
            new(name, Present: false, 0, 0, 0, 0, 0, string.Empty);

        /// <summary>
        /// Reads the single process of <paramref name="processName"/>, or reports its absence.
        /// </summary>
        /// <remarks>
        /// More than one match is reported as the first one plus a count in the title, rather
        /// than averaged or summed: the soak's early-stop rules treat an ambiguous second
        /// instance as a condition to stop on, and a fixture that quietly merged two processes
        /// into one row would hide exactly that.
        /// </remarks>
        public static ProcessVitals Read(string processName)
        {
            Process[] found = Process.GetProcessesByName(processName);
            if (found.Length == 0)
            {
                return Absent(processName);
            }

            try
            {
                Process process = found[0];
                process.Refresh();

                string title = found.Length == 1
                    ? process.MainWindowTitle
                    : $"[{found.Length} processes] {process.MainWindowTitle}";

                return new ProcessVitals(
                    processName,
                    Present: true,
                    process.Id,
                    process.WorkingSet64,
                    process.PrivateMemorySize64,
                    process.HandleCount,
                    process.Threads.Count,
                    title);
            }
            catch (InvalidOperationException)
            {
                // The process exited between the enumeration and the read. An absent row is the
                // honest answer; a guessed one would be worse than a gap in the table.
                return Absent(processName);
            }
            finally
            {
                foreach (Process process in found)
                {
                    process.Dispose();
                }
            }
        }

        /// <summary>Reads this process — the PrintFlow graph the soak is driving.</summary>
        public static ProcessVitals ReadSelf()
        {
            using Process self = Process.GetCurrentProcess();
            self.Refresh();
            return new ProcessVitals(
                "PrintFlow",
                Present: true,
                self.Id,
                self.WorkingSet64,
                self.PrivateMemorySize64,
                self.HandleCount,
                self.Threads.Count,
                string.Empty);
        }

        public string Describe() => Present
            ? $"pid {ProcessId,-7} ws {Megabytes(WorkingSet),7} MB  priv {Megabytes(PrivateMemory),7} MB  " +
              $"handles {Handles,6}  threads {Threads,4}"
            : "(not running)";

        private static long Megabytes(long bytes) => bytes / (1024 * 1024);
    }

    /// <summary>
    /// A census of one process's child-window classes, used to look for a signal that tracks
    /// Photoshop's open-document count (Epic 11600 Part B §7, §9).
    /// </summary>
    /// <remarks>
    /// <b>Why this exists at all.</b> §7 asks for the number of open Photoshop documents "if
    /// determinable through an already accepted safe inspection seam", and there is no such seam:
    /// <c>IPhotoshopUiDriver</c> classifies a screen and probes one document's identity, and
    /// neither answers "how many are loaded". Photoshop CC 2019 draws its own document tabs, so a
    /// UI Automation walk finds no <c>TabItem</c> either.
    /// <para>
    /// So this does not claim to be a document count. It counts child windows by class, which is
    /// a read-only enumeration of one process's own windows, and lets the soak report which class
    /// counts — if any — move in step with the number of documents PrintFlow opened. A class that
    /// tracks is a measurement; if none tracks, the honest report is that the count was not
    /// measurable, and the accumulation claim rests on the window title and on each job's
    /// successful identity probe instead.
    /// </para>
    /// </remarks>
    internal sealed record WindowClassCensus(int TotalChildWindows, IReadOnlyDictionary<string, int> ByClass)
    {
        /// <summary>
        /// The classes worth printing. Photoshop has around a thousand child windows and almost
        /// all of them are palette furniture; naming the few document-shaped ones keeps the
        /// checkpoint table readable without deciding in advance which one is the answer.
        /// </summary>
        private static readonly string[] Reported =
            ["PSViewC", "OWL.Document", "OWL.TabGroup", "OWL.TabPane", "Photoshop_Document"];

        public static WindowClassCensus Empty { get; } =
            new(0, new Dictionary<string, int>(StringComparer.Ordinal));

        public static WindowClassCensus Read(string processName)
        {
            Process[] found = Process.GetProcessesByName(processName);
            try
            {
                if (found.Length == 0 || found[0].MainWindowHandle == IntPtr.Zero)
                {
                    return Empty;
                }

                Dictionary<string, int> byClass = new(StringComparer.Ordinal);
                int total = 0;

                bool Visit(IntPtr window, IntPtr _)
                {
                    total++;
                    StringBuilder name = new(256);
                    if (NativeMethods.GetClassName(window, name, name.Capacity) > 0)
                    {
                        string className = name.ToString();
                        byClass[className] = byClass.GetValueOrDefault(className) + 1;
                    }

                    return true;
                }

                NativeMethods.EnumChildWindows(found[0].MainWindowHandle, Visit, IntPtr.Zero);
                return new WindowClassCensus(total, byClass);
            }
            finally
            {
                foreach (Process process in found)
                {
                    process.Dispose();
                }
            }
        }

        public string Describe() => TotalChildWindows == 0
            ? "(no window)"
            : $"children {TotalChildWindows,5}  " +
              string.Join("  ", Reported.Select(c => $"{c} {ByClass.GetValueOrDefault(c),3}"));

        /// <summary>The counts this census reports, for comparing two checkpoints.</summary>
        public IReadOnlyList<(string Class, int Count)> Tracked() =>
            [.. Reported.Select(c => (c, ByClass.GetValueOrDefault(c)))];
    }

    /// <summary>
    /// Which Meitu versions are installed, which one the operator's own launcher resolves to, and
    /// which one is actually running (Epic 11600 Part B Phase 0).
    /// </summary>
    /// <param name="AcceptedExecutable">
    /// The accepted path, read from the verified preset baseline at run time and passed in.
    /// </param>
    /// <param name="InstalledVersionDirectories">
    /// Sibling directories of the accepted version that contain an executable of the same name —
    /// that is, the other installed versions of the same application.
    /// </param>
    /// <param name="LauncherConfiguredVersion">
    /// The version recorded in the launcher stub's own configuration file, which is what the
    /// Start&#160;Menu shortcut resolves through. Null when there is no such file.
    /// </param>
    /// <param name="RunningExecutables">The module path of every running process of that name.</param>
    internal sealed record MeituVersionTopology(
        string AcceptedExecutable,
        bool AcceptedExecutableExists,
        IReadOnlyList<string> InstalledVersionDirectories,
        string? LauncherConfiguredVersion,
        IReadOnlyList<string> RunningExecutables)
    {
        /// <summary>
        /// Reads the topology around <paramref name="acceptedExecutablePath"/>.
        /// </summary>
        /// <remarks>
        /// Everything is derived from the accepted path rather than from a constant: the version
        /// directory is its parent, the install root its grandparent, and the launcher stub the
        /// executable of the same name sitting in that root. That keeps this fixture true for
        /// whatever the signed preset names, and keeps the accepted path out of the repository.
        /// </remarks>
        public static MeituVersionTopology Read(string acceptedExecutablePath)
        {
            string executableName = Path.GetFileName(acceptedExecutablePath);
            string? versionDirectory = Path.GetDirectoryName(acceptedExecutablePath);
            string? installRoot = versionDirectory is null ? null : Path.GetDirectoryName(versionDirectory);

            List<string> installed = [];
            if (installRoot is not null && Directory.Exists(installRoot))
            {
                foreach (string candidate in Directory.EnumerateDirectories(installRoot))
                {
                    if (File.Exists(Path.Combine(candidate, executableName)))
                    {
                        installed.Add(Path.GetFileName(candidate));
                    }
                }
            }

            string? launcherVersion = installRoot is null
                ? null
                : ReadLauncherVersion(Path.Combine(installRoot, "Config.ini"));

            List<string> running = [];
            Process[] processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executableName));
            foreach (Process process in processes)
            {
                using (process)
                {
                    try
                    {
                        running.Add(process.MainModule?.FileName ?? "(module unreadable)");
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                    {
                        running.Add("(module unreadable)");
                    }
                }
            }

            return new MeituVersionTopology(
                acceptedExecutablePath,
                File.Exists(acceptedExecutablePath),
                [.. installed.OrderBy(v => v, StringComparer.Ordinal)],
                launcherVersion,
                [.. running.OrderBy(p => p, StringComparer.OrdinalIgnoreCase)]);
        }

        /// <summary>
        /// Whether every running instance is the accepted binary. False is an early-stop
        /// condition, not a warning (§5).
        /// </summary>
        public bool OnlyAcceptedIsRunning => RunningExecutables.All(path =>
            string.Equals(path, AcceptedExecutable, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// The part of the version picture an operation must not change: which versions are
        /// installed, and which one the operator's own launcher resolves to.
        /// </summary>
        /// <remarks>
        /// The running set is deliberately <b>not</b> in here. Launching the accepted binary is
        /// exactly what PrintFlow is supposed to do when nothing is running, so folding "running
        /// instances" into an it-must-not-change fingerprint made a cold start fail the very gate
        /// that exists to prove a cold start works — which is what the first Phase 0 run did.
        /// Whether the right binary is running is a separate question with a separate answer,
        /// <see cref="OnlyAcceptedIsRunning"/>, and it is asserted separately.
        /// </remarks>
        public string InstallationFingerprint =>
            $"installed=[{string.Join(",", InstalledVersionDirectories)}] " +
            $"launcher={LauncherConfiguredVersion ?? "-"}";

        /// <summary>The whole picture, for the log rather than for an assertion.</summary>
        public string Fingerprint =>
            $"{InstallationFingerprint} running=[{string.Join(",", RunningExecutables)}]";

        private static string? ReadLauncherVersion(string configPath)
        {
            if (!File.Exists(configPath))
            {
                return null;
            }

            foreach (string line in File.ReadLines(configPath))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("Version=", StringComparison.OrdinalIgnoreCase))
                {
                    return trimmed["Version=".Length..].Trim();
                }
            }

            return null;
        }
    }

    private static class NativeMethods
    {
        internal delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

        [DllImport("user32.dll")]
        internal static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr parameter);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetClassName(IntPtr window, StringBuilder name, int capacity);
    }
}
