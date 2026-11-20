using System.Windows;
using Microsoft.Win32;
using PrintFlow.App.Resources;

namespace PrintFlow.App.Navigation;

public interface ICloudDesignPdfSavePicker
{
    string? PickDestination();
}

public sealed class SaveCloudDesignPdfDialog : ICloudDesignPdfSavePicker
{
    public string? PickDestination()
    {
        var dialog = new SaveFileDialog
        {
            Title = Strings.Resolve("AiPrompt_SaveCloudPdf"),
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = "OpenAI-design.pdf",
            DefaultExt = ".pdf",
            AddExtension = true,
            ValidateNames = true,
            OverwritePrompt = false
        };
        bool? accepted = Application.Current?.MainWindow is Window owner ? dialog.ShowDialog(owner) : dialog.ShowDialog();
        return accepted == true ? dialog.FileName : null;
    }
}
