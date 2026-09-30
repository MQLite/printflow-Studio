using PrintFlow.App.Navigation;
using PrintFlow.Workflow.Delivery;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.WorkstationEntry;

/// <summary>Only a future authorized bootstrap may supply native delegates. Smoke supplies none.</summary>
public sealed class ContainedNativePorts(OwnedPaths paths, IFilePicker? files = null,
    IDeliveryFolderPicker? folders = null, IDiagnosticPackageDestinationPicker? packages = null,
    IDeliveredFileShell? delivered = null, ICorrectionFolderShell? corrections = null)
    : IFilePicker, IDeliveryFolderPicker, IDiagnosticPackageDestinationPicker, IDeliveredFileShell, ICorrectionFolderShell
{
    public int NativeDispatches { get; private set; }
    public string? NextFixture { get; set; }
    public string? PickSingleFile(string dialogTitle, string filter) => PickSingleFile(dialogTitle, filter, null);
    public string? PickSingleFile(string dialogTitle, string filter, string? initialFolder)
    {
        if (initialFolder is not null) paths.Require(initialFolder, "workspace");
        string? selected;
        if (files is null) { selected = NextFixture; NextFixture = null; }
        else { NativeDispatches++; selected = files.PickSingleFile(dialogTitle, filter, initialFolder); }
        if (selected is null) return null;
        using NativePathLease held = paths.Read(selected, "fixtures");
        return selected;
    }
    public string? PickFolder(string dialogTitle, string? initialFolder)
    {
        if (initialFolder is not null) paths.Require(initialFolder, "delivery");
        if (folders is null) return null;
        NativeDispatches++;
        string? selected = folders.PickFolder(dialogTitle, initialFolder);
        return selected is null ? null : paths.Require(selected, "delivery");
    }
    public string? PickDestination(string dialogTitle, string filter, string suggestedFileName)
    {
        if (Path.GetFileName(suggestedFileName) != suggestedFileName) throw new IOException("Package suggestion is not a leaf.");
        if (packages is null) return null;
        NativeDispatches++;
        string? selected = packages.PickDestination(dialogTitle, filter, suggestedFileName);
        return selected is null ? null : paths.Require(selected, "export");
    }
    public ShellDispatchResult SelectInFolder(IDeliveredSelectionLease lease)
    {
        if (lease.IsDisposed) return new(false, "Disposed delivery lease.");
        try { paths.Require(lease.FinalPath, "delivery"); }
        catch (IOException ex) { return new(false, ex.Message); }
        if (delivered is null) return new(false, "Recorded only: native Explorer is disabled in PrepareAndSmoke.");
        NativeDispatches++;
        return delivered.SelectInFolder(lease);
    }
    public ShellDispatchResult Open(string absoluteFolder, string? preferredFileName, string? fallbackFileName)
    {
        try
        {
            paths.Require(absoluteFolder, "workspace");
            if (!absoluteFolder.Split(Path.DirectorySeparatorChar).Contains("Correction", StringComparer.OrdinalIgnoreCase))
                return new(false, "Not a correction folder.");
            foreach (string name in new[] { preferredFileName, fallbackFileName }.OfType<string>())
                if (Path.GetFileName(name) != name) return new(false, "Not a correction leaf.");
        }
        catch (IOException ex) { return new(false, ex.Message); }
        if (corrections is null) return new(false, "Recorded only: native Explorer is disabled in PrepareAndSmoke.");
        NativeDispatches++;
        return corrections.Open(absoluteFolder, preferredFileName, fallbackFileName);
    }
}
