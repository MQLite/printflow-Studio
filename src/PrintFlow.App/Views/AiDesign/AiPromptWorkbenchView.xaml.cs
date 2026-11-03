using System.Windows.Controls;
using PrintFlow.App.ViewModels.AiDesign;

namespace PrintFlow.App.Views.AiDesign;

public partial class AiPromptWorkbenchView : UserControl
{
    public AiPromptWorkbenchView()
    {
        InitializeComponent();
        Unloaded += (_, _) => (DataContext as AiPromptWorkbenchViewModel)?.Deactivate();
    }
}
