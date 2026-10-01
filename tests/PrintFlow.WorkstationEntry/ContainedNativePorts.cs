using PrintFlow.App.Navigation;
using PrintFlow.Workflow.Delivery;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.WorkstationEntry;

/// <summary>Only a future authorized bootstrap may supply native delegates. Smoke supplies none.</summary>
/// <remarks>
/// A native selection outside its owned role, or an unadmitted fixture, is refused as "no
/// selection": the product treats it like a cancelled picker and stays on its screen. Letting that
/// refusal escape a product command ended the visible host before its settled shutdown path.
/// Scripted picks and owned-tree integrity faults still throw.
/// </remarks>
public sealed class ContainedNativePorts(OwnedPaths paths, IFilePicker? files = null,
    IDeliveryFolderPicker? folders = null, IDiagnosticPackageDestinationPicker? packages = null,
    IDeliveredFileShell? delivered = null, ICorrectionFolderShell? corrections = null, Action<string>? refused = null)
    : IFilePicker, IDeliveryFolderPicker, IDiagnosticPackageDestinationPicker, IDeliveredFileShell, ICorrectionFolderShell
{
    public int NativeDispatches { get; private set; }
    public int PickerRefusals { get; private set; }
    public string? NextFixture { get; set; }
    public string? PickSingleFile(string dialogTitle, string filter) => PickSingleFile(dialogTitle, filter, null);
    public string? PickSingleFile(string dialogTitle, string filter, string? initialFolder)
    {
        if (initialFolder is not null) paths.Require(initialFolder, "workspace");
        if (files is null)
        {
            string? scripted = NextFixture; NextFixture = null;
            if (scripted is null) return null;
            using NativePathLease scriptedHeld = paths.Read(scripted, "fixtures");
            return scripted;
        }
        NativeDispatches++;
        string? selected = files.PickSingleFile(dialogTitle, filter, initialFolder);
        if (selected is null || Refused(selected, "fixtures")) return null;
        using NativePathLease held = paths.Read(selected, "fixtures");
        return selected;
    }
    public string? PickFolder(string dialogTitle, string? initialFolder)
    {
        if (initialFolder is not null) paths.Require(initialFolder, "delivery");
        if (folders is null) return null;
        NativeDispatches++;
        string? selected = folders.PickFolder(dialogTitle, initialFolder);
        return selected is null || Refused(selected, "delivery") ? null : paths.Require(selected, "delivery");
    }
    public string? PickDestination(string dialogTitle, string filter, string suggestedFileName)
    {
        if (Path.GetFileName(suggestedFileName) != suggestedFileName) throw new IOException("Package suggestion is not a leaf.");
        if (packages is null) return null;
        NativeDispatches++;
        string? selected = packages.PickDestination(dialogTitle, filter, suggestedFileName);
        return selected is null || Refused(selected, "export") ? null : paths.Require(selected, "export");
    }
    // Only an out-of-role or unadmitted selection becomes "no selection"; identity, alias and
    // directory-protection faults from the following Read/Require still refuse the run.
    private bool Refused(string selected, string role)
    {
        if (paths.SelectionRefusal(selected, role) is not { } reason) return false;
        PickerRefusals++;
        refused?.Invoke(RefusalNotice(role, reason));
        return true;
    }

    /// <summary>The operator's notice for a refused native selection: Chinese, with the technical reason last.</summary>
    public static string RefusalNotice(string role, string reason) =>
        "SYNTHETIC 测试入口：所选位置不是本次测试准备的" + (role == "fixtures" ? "测试文件" : "文件夹") + "，未被使用，也没有更改任何数据。\n" +
        "请改为选择已准备的测试位置：" + role switch
        {
            "fixtures" => @"fixtures\inputs 或 fixtures\returns 中的测试图片",
            "delivery" => "delivery 文件夹本身（不要新建文件夹）",
            _ => @"diagnostics\export 文件夹本身（不要新建文件夹）",
        } + "。\n\n技术原因：" + reason;
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
