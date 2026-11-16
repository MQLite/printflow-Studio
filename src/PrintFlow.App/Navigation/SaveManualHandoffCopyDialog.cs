using System.IO;
using System.Windows;
using Microsoft.Win32;
using PrintFlow.App.Resources;
using PrintFlow.Domain.Files;

namespace PrintFlow.App.Navigation;

public interface IManualHandoffCopyPicker
{
    string? PickDestination(string suggestedFileName, ImageFormat format);
}

public sealed class SaveManualHandoffCopyDialog : IManualHandoffCopyPicker
{
    public string? PickDestination(string suggestedFileName, ImageFormat format)
    {
        (string extension, string filter) = format switch
        {
            ImageFormat.Jpeg => (".jpg", "JPEG (*.jpg;*.jpeg)|*.jpg;*.jpeg"),
            ImageFormat.Tiff => (".tif", "TIFF (*.tif;*.tiff)|*.tif;*.tiff"),
            _ => (".png", "PNG (*.png)|*.png"),
        };
        var dialog = new SaveFileDialog
        {
            Title = Strings.Resolve("Session_SaveManualHandoffCopy"),
            Filter = filter,
            FileName = Path.ChangeExtension(suggestedFileName, extension),
            DefaultExt = extension,
            AddExtension = true,
            ValidateNames = true,
            OverwritePrompt = false,
        };
        bool? accepted = Application.Current?.MainWindow is Window owner ? dialog.ShowDialog(owner) : dialog.ShowDialog();
        return accepted == true ? dialog.FileName : null;
    }
}
