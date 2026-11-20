using System.Globalization;
using System.Windows;
using PrintFlow.App.Resources;

namespace PrintFlow.App.Navigation;

public interface IOpenAiUploadPrompt
{
    bool Confirm(string operation);
}

public interface IAiDesignApiConsent
{
    bool Confirm();
    bool Confirm(bool withCloudReview) => Confirm();
}

internal sealed class SystemOpenAiUploadPrompt : IOpenAiUploadPrompt, IAiDesignApiConsent
{
    public bool Confirm(string operation) => Show(
        string.Format(CultureInfo.CurrentCulture, Strings.Resolve("ImageProcessing_OpenAiUploadQuestion"), operation));

    public bool Confirm() => Show(Strings.Resolve("AiPrompt_OpenAiConsent"));
    public bool Confirm(bool withCloudReview) =>
        withCloudReview ? Show(Strings.Resolve("AiPrompt_CloudConsent")) : Confirm();

    private static bool Show(string message)
    {
        string title = Strings.Resolve("ImageProcessing_OpenAiUploadTitle");
        return (Application.Current?.MainWindow is { } owner
            ? MessageBox.Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)
            : MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No))
            == MessageBoxResult.Yes;
    }
}
