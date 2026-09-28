using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace PrintFlow.App.Navigation;

/// <summary>The owned Windows folder dialog used only by the final-save draft.</summary>
public sealed class OpenFolderDialogPicker : IDeliveryFolderPicker
{
    public string? PickFolder(string dialogTitle, string? initialFolder)
    {
        OpenFolderDialog dialog = new()
        {
            Title = dialogTitle,
            Multiselect = false,
            ValidateNames = true,
        };
        // Start only from the folder the operator can already see; otherwise let Windows choose
        // without PrintFlow supplying a guessed customer or managed folder.
        if (!string.IsNullOrWhiteSpace(initialFolder) && Directory.Exists(initialFolder))
            dialog.InitialDirectory = initialFolder;

        bool? accepted = Application.Current?.MainWindow is Window owner
            ? dialog.ShowDialog(owner)
            : dialog.ShowDialog();
        return accepted == true ? dialog.FolderName : null;
    }
}
