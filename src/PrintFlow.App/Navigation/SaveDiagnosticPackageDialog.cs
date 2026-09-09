using System.Windows;
using Microsoft.Win32;

namespace PrintFlow.App.Navigation;

/// <summary>The owned Windows Save dialog used only after package contents were previewed.</summary>
public sealed class SaveDiagnosticPackageDialog : IDiagnosticPackageDestinationPicker
{
    public string? PickDestination(string dialogTitle, string filter, string suggestedFileName)
    {
        SaveFileDialog dialog = new()
        {
            Title = dialogTitle,
            Filter = filter,
            FileName = suggestedFileName,
            DefaultExt = ".zip",
            AddExtension = true,
            ValidateNames = true,
            // The package writer never overwrites. An occupied name receives a visible numeric
            // suffix and the screen reports the actual path, so no replacement prompt can imply
            // an overwrite that the writer will not perform.
            OverwritePrompt = false,
        };

        bool? accepted = Application.Current?.MainWindow is Window owner
            ? dialog.ShowDialog(owner)
            : dialog.ShowDialog();
        return accepted == true ? dialog.FileName : null;
    }
}
