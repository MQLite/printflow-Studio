using System.Windows;
using Microsoft.Win32;
using PrintFlow.App.Resources;

namespace PrintFlow.App.Navigation;

public sealed class SaveAiDesignPngDialog : IAiDesignPngSavePicker
{
    public string? PickDestination(string suggestedFileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = Strings.Resolve("AiMemorial_Export"), Filter = "PNG (*.png)|*.png",
            FileName = suggestedFileName, DefaultExt = ".png", AddExtension = true,
            ValidateNames = true, OverwritePrompt = false,
        };
        bool? accepted = Application.Current?.MainWindow is Window owner ? dialog.ShowDialog(owner) : dialog.ShowDialog();
        return accepted == true ? dialog.FileName : null;
    }
}
