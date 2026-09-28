using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Workflow.Delivery;

namespace PrintFlow.Infrastructure.Delivery;

internal static class WindowsDeliveryNative
{
    internal const uint ReadAttributes = 0x80;
    internal const uint GenericRead = 0x80000000;
    internal const uint GenericWrite = 0x40000000;
    internal const uint DeleteAccess = 0x10000;
    internal const uint ShareRead = 1;
    internal const uint ShareWrite = 2;
    internal const uint ShareDelete = 4;
    internal const uint OpenExisting = 3;
    internal const uint CreateNew = 1;
    internal const uint Normal = 0x80;
    internal const uint BackupSemantics = 0x02000000;
    internal const uint OpenReparsePoint = 0x00200000;
    internal const uint ReparseAttribute = 0x400;
    private const int FileIdInfo = 18;
    private const int FileCaseSensitiveInfo = 23;
    private const int FileRenameInformation = 10;
    private const int FileDispositionInfo = 4;

    internal static SafeFileHandle OpenDirectory(string path)
    {
        SafeFileHandle handle = NativeMethods.CreateFileW(path, ReadAttributes, ShareRead, 0, OpenExisting,
            BackupSemantics | OpenReparsePoint, 0);
        if (handle.IsInvalid) throw Error("Open directory");
        try
        {
            if (!NativeMethods.GetFileInformationByHandle(handle, out NativeMethods.ByHandleFileInformation info)) throw Error("Identify directory");
            if ((info.Attributes & ReparseAttribute) != 0)
                throw new NotSupportedException("Destination or protected directory contains a reparse point.");
            if ((info.Attributes & 0x10) == 0) throw new NotSupportedException("Expected a directory handle.");
            if (!NativeMethods.GetFileCaseSensitiveInformation(handle, FileCaseSensitiveInfo,
                    out NativeMethods.FileCaseSensitiveInfoValue caseInfo, 4))
                throw new NotSupportedException("Directory case sensitivity cannot be verified.");
            if ((caseInfo.Flags & 1) != 0)
                throw new NotSupportedException("Case-sensitive directories are outside the supported destination envelope.");
            return handle;
        }
        catch { handle.Dispose(); throw; }
    }

    internal static SafeFileHandle OpenSource(string path)
    {
        SafeFileHandle handle = NativeMethods.CreateFileW(path, GenericRead, ShareRead, 0, OpenExisting,
            OpenReparsePoint, 0);
        if (handle.IsInvalid) throw Error("Open approved source");
        try
        {
            if (!NativeMethods.GetFileInformationByHandle(handle, out NativeMethods.ByHandleFileInformation info)) throw Error("Identify source");
            if ((info.Attributes & ReparseAttribute) != 0 || (info.Attributes & 0x10) != 0)
                throw new NotSupportedException("Approved source is not an ordinary file.");
            return handle;
        }
        catch { handle.Dispose(); throw; }
    }

    internal static SafeFileHandle OpenStage(string path, bool create)
    {
        SafeFileHandle handle = NativeMethods.CreateFileW(path, GenericRead | GenericWrite | DeleteAccess,
            ShareRead, 0, create ? CreateNew : OpenExisting, Normal | OpenReparsePoint, 0);
        if (handle.IsInvalid) throw Error(create ? "Create staging file" : "Open recorded staging file");
        try
        {
            if (!NativeMethods.GetFileInformationByHandle(handle, out NativeMethods.ByHandleFileInformation info)) throw Error("Identify staging file");
            if ((info.Attributes & ReparseAttribute) != 0 || (info.Attributes & 0x10) != 0)
                throw new NotSupportedException("Staging name does not resolve to an ordinary file.");
            return handle;
        }
        catch { handle.Dispose(); throw; }
    }

    internal static SafeFileHandle OpenFinal(string path, bool stagedHandleIsHeld)
    {
        SafeFileHandle handle = NativeMethods.CreateFileW(path, GenericRead,
            stagedHandleIsHeld ? ShareRead | ShareWrite | ShareDelete : ShareRead,
            0, OpenExisting, OpenReparsePoint, 0);
        if (handle.IsInvalid) throw Error("Open final destination");
        try
        {
            if (!NativeMethods.GetFileInformationByHandle(handle, out NativeMethods.ByHandleFileInformation info)) throw Error("Identify final destination");
            if ((info.Attributes & ReparseAttribute) != 0 || (info.Attributes & 0x10) != 0)
                throw new NotSupportedException("Final name does not resolve to an ordinary file.");
            return handle;
        }
        catch { handle.Dispose(); throw; }
    }

    internal static DeliveryFileIdentity Identity(SafeFileHandle handle)
    {
        if (!NativeMethods.GetFileInformationByHandleEx(handle, FileIdInfo, out NativeMethods.FileIdInfoValue id,
                (uint)Marshal.SizeOf<NativeMethods.FileIdInfoValue>())) throw Error("Read 128-bit file identity");
        if (!NativeMethods.GetFileInformationByHandle(handle, out NativeMethods.ByHandleFileInformation basic)) throw Error("Read file creation time");
        long fileTime = ((long)basic.CreationTime.High << 32) | basic.CreationTime.Low;
        return new DeliveryFileIdentity(id.VolumeSerialNumber.ToString("X16"),
            Convert.ToHexString(id.FileId), new DateTimeOffset(DateTime.FromFileTimeUtc(fileTime), TimeSpan.Zero));
    }

    internal static string ResolvedPath(SafeFileHandle handle)
    {
        StringBuilder buffer = new(32768);
        uint size = NativeMethods.GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
        if (size == 0 || size >= buffer.Capacity) throw Error("Resolve held directory path");
        string path = buffer.ToString();
        if (!path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Resolved path is not an ordinary local drive path.");
        return path[4..];
    }

    internal static void RequireLocalNtfs(string path)
    {
        if (path.Length < 3 || !char.IsLetter(path[0]) || path[1] != ':' || path[2] != '\\')
            throw new NotSupportedException("Only ordinary drive-letter folders are supported.");
        string root = path[..3];
        uint type = NativeMethods.GetDriveTypeW(root);
        if (type is not (2 or 3)) throw new NotSupportedException("Destination drive is not local fixed/removable storage.");
        StringBuilder dos = new(1024);
        if (NativeMethods.QueryDosDeviceW(path[..2], dos, (uint)dos.Capacity) == 0 ||
            !dos.ToString().StartsWith(@"\Device\", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Mapped, SUBST or unresolved drive-letter aliases are unsupported.");
        StringBuilder volume = new(260), format = new(260);
        if (!NativeMethods.GetVolumeInformationW(root, volume, (uint)volume.Capacity, out _, out _, out _,
                format, (uint)format.Capacity)) throw Error("Identify destination volume");
        if (!string.Equals(format.ToString(), "NTFS", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Only NTFS destinations are supported.");
    }

    internal static void RenameNoReplace(SafeFileHandle stage, SafeFileHandle directory, string leaf)
    {
        // NtSetInformationFile/FileRenameInformation explicitly supports RootDirectory +
        // a simple leaf. The Win32 wrapper returned ERROR_INVALID_PARAMETER for that form
        // on the verified host; never substitute an absolute/path-only rename.
        // https://learn.microsoft.com/windows-hardware/drivers/ddi/ntifs/ns-ntifs-_file_rename_information
        byte[] name = Encoding.Unicode.GetBytes(leaf);
        int nameOffset = Marshal.OffsetOf<NativeMethods.FileRenameInformationValue>(nameof(NativeMethods.FileRenameInformationValue.FileName)).ToInt32();
        int size = checked(Marshal.SizeOf<NativeMethods.FileRenameInformationValue>() + name.Length);
        nint buffer = Marshal.AllocHGlobal(size);
        bool directoryReferenced = false;
        try
        {
            directory.DangerousAddRef(ref directoryReferenced);
            Marshal.Copy(new byte[size], 0, buffer, size);
            Marshal.StructureToPtr(new NativeMethods.FileRenameInformationValue
            {
                Flags = 0, // ReplaceIfExists = FALSE; no POSIX or replacement flags.
                RootDirectory = directory.DangerousGetHandle(),
                FileNameLength = (uint)name.Length
            }, buffer, false);
            Marshal.Copy(name, 0, buffer + nameOffset, name.Length);
            // OpenStage uses a synchronous CreateFile handle (no FILE_FLAG_OVERLAPPED).
            // This call therefore completes before the rename buffer/IO_STATUS_BLOCK leave scope.
            int status = NativeMethods.NtSetInformationFile(stage, out _, buffer, (uint)size, FileRenameInformation);
            if (status != 0)
            {
                int code = unchecked((int)NativeMethods.RtlNtStatusToDosError(status));
                throw new Win32Exception(code,
                    $"No-replace publication: {new Win32Exception(code).Message} (ntstatus=0x{status:X8}, win32={code})");
            }
        }
        finally
        {
            if (directoryReferenced) directory.DangerousRelease();
            Marshal.FreeHGlobal(buffer);
        }
    }

    internal static void DeleteHeldFile(SafeFileHandle handle)
    {
        nint buffer = Marshal.AllocHGlobal(1);
        try
        {
            Marshal.WriteByte(buffer, 1);
            if (!NativeMethods.SetFileInformationByHandle(handle, FileDispositionInfo, buffer, 1))
                throw Error("Delete exact held staging file");
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    internal static Win32Exception Error(string operation)
    {
        int code = Marshal.GetLastWin32Error();
        return new Win32Exception(code, $"{operation}: {new Win32Exception(code).Message} (win32={code})");
    }
}
