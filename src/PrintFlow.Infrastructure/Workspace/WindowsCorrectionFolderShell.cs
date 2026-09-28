using System.IO;
using System.Runtime.InteropServices;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Workflow.Delivery;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Workspace;

/// <summary>
/// Opens a correction folder in Explorer with a file selected (SCRUM-11148, design §8.4).
/// </summary>
/// <remarks>
/// The same shell item selection the delivered-file shell uses, sharing only the native
/// declarations: no delivery lease, no delivery wording. The folder must exist and every name must
/// stay inside it; nothing is built from a command string, and nothing is opened, edited or
/// executed — only selected.
/// </remarks>
public sealed class WindowsCorrectionFolderShell : ICorrectionFolderShell
{
    public ShellDispatchResult Open(string absoluteFolder, string? preferredFileName, string? fallbackFileName)
    {
        if (string.IsNullOrWhiteSpace(absoluteFolder) || !Path.IsPathFullyQualified(absoluteFolder))
            return new(false, "The correction folder is not a full local path.");

        string folder;
        try
        {
            folder = Path.GetFullPath(absoluteFolder);
            if (!Directory.Exists(folder))
                return new(false, "The correction folder no longer exists.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new(false, $"The correction folder could not be checked: {ex.Message}");
        }

        string target = Within(folder, preferredFileName) ?? Within(folder, fallbackFileName) ?? folder;
        int parsed = NativeMethods.SHParseDisplayName(target, 0, out nint item, 0, out _);
        if (parsed != 0 || item == 0)
            return new(false, $"The shell could not parse the correction path (0x{parsed:X8}).");
        try
        {
            // A single item with no children selects that item in its parent folder.
            int opened = NativeMethods.SHOpenFolderAndSelectItems(item, 0, null, 0);
            return opened == 0
                ? new(true)
                : new(false, $"The shell did not accept the folder request (0x{opened:X8}).");
        }
        finally { Marshal.FreeCoTaskMem(item); }
    }

    /// <summary>The file inside <paramref name="folder"/>, when it is a plain name that exists.</summary>
    private static string? Within(string folder, string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return null;
        string path = Path.GetFullPath(Path.Combine(folder, fileName));
        return string.Equals(Path.GetDirectoryName(path), folder.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) &&
            File.Exists(path)
            ? path
            : null;
    }
}
