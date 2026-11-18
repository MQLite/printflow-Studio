using System.Windows;
using System.Windows.Controls;
using PrintFlow.App.ViewModels;

namespace PrintFlow.App.Views;

public partial class ImageProcessingSettingsView : UserControl
{
    public ImageProcessingSettingsView() => InitializeComponent();

    private void SaveOpenAiCredential(object sender, RoutedEventArgs e)
    {
        string key = OpenAiKey.Password;
        OpenAiKey.Clear();
        if (DataContext is SettingsViewModel settings)
            settings.SaveOpenAiCredential(key);
    }

    private void ClearOpenAiKey(object sender, RoutedEventArgs e) => OpenAiKey.Clear();
}
