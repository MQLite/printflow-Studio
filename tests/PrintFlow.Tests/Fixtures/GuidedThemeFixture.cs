using System.Windows;
using System.Windows.Controls;

namespace PrintFlow.Tests.Fixtures;

internal static class GuidedThemeFixture
{
    public static ResourceDictionary CreateResources() => new()
    {
        Source = new Uri("/PrintFlow.App;component/Themes/GuidedTheme.xaml", UriKind.Relative),
    };

    public static UserControl Host(FrameworkElement content)
    {
        UserControl host = new() { Content = content };
        host.Resources.MergedDictionaries.Add(CreateResources());
        host.SetResourceReference(Control.FontFamilyProperty, "PF.Font.Family");
        host.SetResourceReference(Control.FontSizeProperty, "PF.Font.Body");
        host.SetResourceReference(Control.ForegroundProperty, "PF.Brush.Ink");
        host.SetResourceReference(Control.BackgroundProperty, "PF.Brush.Canvas");
        return host;
    }
}
