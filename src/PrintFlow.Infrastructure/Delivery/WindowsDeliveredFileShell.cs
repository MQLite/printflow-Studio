using System.Runtime.InteropServices;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Workflow.Delivery;

namespace PrintFlow.Infrastructure.Delivery;

/// <summary>Opens the parent folder with the exact leased file selected, without opening it.</summary>
public sealed class WindowsDeliveredFileShell : IDeliveredFileShell
{
    public ShellDispatchResult SelectInFolder(IDeliveredSelectionLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (lease.IsDisposed) return new(false, "The verified selection has already ended.");
        string path = lease.FinalPath;
        int parsed = NativeMethods.SHParseDisplayName(path, 0, out nint item, 0, out _);
        if (parsed != 0 || item == 0)
            return new(false, $"The shell could not parse the delivered path (0x{parsed:X8}).");
        try
        {
            // A single file item with no children selects that item in its parent folder.
            // Flags 0: no rename/edit mode, no desktop-folder special casing.
            int opened = NativeMethods.SHOpenFolderAndSelectItems(item, 0, null, 0);
            return opened == 0
                ? new(true)
                : new(false, $"The shell did not accept the folder request (0x{opened:X8}).");
        }
        finally { Marshal.FreeCoTaskMem(item); }
    }
}
