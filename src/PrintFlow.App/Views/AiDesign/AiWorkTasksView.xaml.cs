using System.Windows.Controls;
using PrintFlow.App.ViewModels.AiDesign;

namespace PrintFlow.App.Views.AiDesign;

public partial class AiWorkTasksView : UserControl
{
    public AiWorkTasksView()
    {
        InitializeComponent();
        Unloaded += (_, _) => (DataContext as AiWorkTasksViewModel)?.Deactivate();
    }
}
