using Microsoft.Win32;

namespace PrintFlow.App.Navigation;

/// <summary>The Windows common file dialog, restricted to one file.</summary>
/// <remarks>
/// <c>Multiselect</c> is left at its default of false rather than set explicitly nowhere: it is
/// set here on purpose, so the single-file rule is visible at the one place a second file could
/// otherwise enter the application.
/// </remarks>
public sealed class OpenFileDialogPicker : IFilePicker
{
    /// <inheritdoc />
    public string? PickSingleFile(string dialogTitle, string filter)
    {
        OpenFileDialog dialog = new()
        {
            Title = dialogTitle,
            Filter = filter,
            Multiselect = false,
            CheckFileExists = true,
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
