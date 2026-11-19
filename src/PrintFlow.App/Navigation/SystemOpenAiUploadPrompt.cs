using System.Globalization;
using System.Windows;
using PrintFlow.App.Resources;

namespace PrintFlow.App.Navigation;

public interface IOpenAiUploadPrompt
{
    bool Confirm(string operation);
}

internal sealed class SystemOpenAiUploadPrompt : IOpenAiUploadPrompt
{
    public bool Confirm(string operation)
    {
        string title = Strings.Resolve("ImageProcessing_OpenAiUploadTitle");
        string message = string.Format(CultureInfo.CurrentCulture, Strings.Resolve("ImageProcessing_OpenAiUploadQuestion"), operation);
        return (Application.Current?.MainWindow is { } owner
            ? MessageBox.Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)
            : MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No))
            == MessageBoxResult.Yes;
    }
}
