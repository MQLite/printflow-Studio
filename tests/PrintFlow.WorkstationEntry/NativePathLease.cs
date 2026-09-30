using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PrintFlow.WorkstationEntry;

/// <summary>Run-owned NTFS identity protection. This is not an OS sandbox.</summary>
public sealed class NativePathLease : IDisposable
{
    private readonly List<SafeFileHandle> handles = [];
    public string Identity { get; private set; } = "";

    public static NativePathLease Ancestors(string path)
    {
        NativePathLease lease = new();
        try
        {
            List<string> components = [];
            for (string? current = Path.GetDirectoryName(path); current is not null; current = Path.GetDirectoryName(current)) components.Add(current);
            components.Reverse();
            foreach (string component in components)
            {
                if (!Directory.Exists(component))
                {
                    if (File.Exists(component)) throw new IOException("Ancestor is a file.");
                    continue;
                }
                lease.Open(component, directory: true);
            }
            if (Directory.Exists(path)) lease.Open(path, directory: true);
            return lease;
        }
        catch { lease.Dispose(); throw; }
    }

    public static NativePathLease ReadFile(string path)
    {
        NativePathLease lease = Ancestors(path);
        try { lease.Open(path, directory: false); return lease; }
        catch { lease.Dispose(); throw; }
    }

    public static NativePathLease MutableFile(string path)
    {
        NativePathLease lease = Ancestors(path);
        try { lease.Open(path, directory: false, writableSharing: true); return lease; }
        catch { lease.Dispose(); throw; }
    }

    public static NativePathLease RestrictDirectory(string path)
    {
        NativePathLease lease = Ancestors(path);
        try { lease.Open(path, directory: true, denyDirectoryWrites: true); return lease; }
        catch { lease.Dispose(); throw; }
    }

    // Observation only. Windows does not enforce rename exclusion for metadata-only opens.
    public static NativePathLease ObserveFile(string path)
    {
        NativePathLease lease = Ancestors(path);
        try { lease.Open(path, directory: false, writableSharing: true, metadataOnly: true); return lease; }
        catch { lease.Dispose(); throw; }
    }

    public static string IdentifyExclusiveFile(FileStream stream, string path)
    {
        if (!GetFileInformationByHandle(stream.SafeFileHandle, out FileInformation info) || (info.Attributes & 0x410) != 0 || info.Links != 1)
            throw new IOException("Exclusive ownership file is not ordinary single-link storage.");
        StringBuilder final = new(32768);
        uint count = GetFinalPathNameByHandleW(stream.SafeFileHandle, final, (uint)final.Capacity, 0);
        if (count == 0 || count >= final.Capacity || !string.Equals(final.ToString(), @"\\?\" + path, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Exclusive ownership file changed path/identity.");
        return $"{info.Volume:X8}:{info.IndexHigh:X8}{info.IndexLow:X8}:{info.CreationHigh:X8}{info.CreationLow:X8}";
    }

    private void Open(string path, bool directory, bool writableSharing = false, bool metadataOnly = false, bool denyDirectoryWrites = false)
    {
        // Data-access opens without delete sharing prevent replacement. Directory write
        // sharing permits owned child operations but DOES NOT prevent in-place reparse changes.
        // The prerequisite regression test records that unresolved containment limitation.
        SafeFileHandle handle = CreateFileW(path, directory ? 0x81u : metadataOnly ? 0x80u : 0x80000000u, denyDirectoryWrites ? 1u : directory || writableSharing ? 3u : 1u, 0, 3,
            0x00200000u | (directory ? 0x02000000u : 0), 0);
        if (handle.IsInvalid) { handle.Dispose(); throw new IOException("Cannot hold owned path identity.", new Win32Exception(Marshal.GetLastWin32Error())); }
        try
        {
            if (!GetFileInformationByHandle(handle, out FileInformation info)) throw new IOException("Cannot read NTFS identity.");
            if ((info.Attributes & 0x400) != 0 || ((info.Attributes & 0x10) != 0) != directory)
                throw new IOException("Reparse or unexpected filesystem object refused.");
            if (!directory && info.Links != 1) throw new IOException("Hard-linked files are refused.");
            StringBuilder final = new(32768);
            uint count = GetFinalPathNameByHandleW(handle, final, (uint)final.Capacity, 0);
            if (count == 0 || count >= final.Capacity || !string.Equals(final.ToString(), @"\\?\" + path, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Resolved path is an alias or changed identity.");
            if (directory)
            {
                if (!GetFileInformationByHandleEx(handle, 23, out uint flags, 4) || (flags & 1) != 0)
                    throw new IOException("Case-sensitive or unverified directory refused.");
            }
            Identity = $"{info.Volume:X8}:{info.IndexHigh:X8}{info.IndexLow:X8}:{info.CreationHigh:X8}{info.CreationLow:X8}";
            handles.Add(handle);
        }
        catch { handle.Dispose(); throw; }
    }

    public static void RequireLocalNtfs(string path)
    {
        string drive = path[..3];
        if (GetDriveTypeW(drive) != 3) throw new ArgumentException("Only fixed local NTFS is accepted for this run.");
        StringBuilder mapping = new(1024), volume = new(260), format = new(260);
        if (QueryDosDeviceW(path[..2], mapping, (uint)mapping.Capacity) == 0 || !mapping.ToString().StartsWith(@"\Device\HarddiskVolume", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Mapped and SUBST aliases are refused.");
        if (!GetVolumeInformationW(drive, volume, (uint)volume.Capacity, out _, out _, out _, format, (uint)format.Capacity) || format.ToString() != "NTFS")
            throw new ArgumentException("Volume must be verified local NTFS.");
    }

    public void Dispose() { for (int i = handles.Count - 1; i >= 0; i--) handles[i].Dispose(); handles.Clear(); }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh,
            Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, nint security, uint creation, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation info);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int kind, out uint value, uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder path, uint count, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern uint GetDriveTypeW(string root);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint QueryDosDeviceW(string device, StringBuilder target, uint count);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformationW(string root, StringBuilder volume, uint volumeSize,
        out uint serial, out uint maxComponent, out uint flags, StringBuilder format, uint formatSize);
}
