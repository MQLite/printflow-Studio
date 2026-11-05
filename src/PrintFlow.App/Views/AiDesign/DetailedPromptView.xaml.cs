using System.Windows;
using System.Windows.Controls;
using PrintFlow.App.Controls;
using PrintFlow.App.ViewModels.AiDesign;
namespace PrintFlow.App.Views.AiDesign;
public partial class DetailedPromptView : UserControl
{
    public DetailedPromptView() => InitializeComponent();
    private void OnImageReferenceInvoked(object sender, AiImageReferenceInvokedEventArgs e)
    {
        if (DataContext is AiPromptWorkbenchViewModel vm && vm.PickSlotImageCommand.CanExecute(e.SlotId)) vm.PickSlotImageCommand.Execute(e.SlotId);
    }

    private void OnEnlargeMemorialProof(object sender, RoutedEventArgs e)
    {
        if (DataContext is not AiPromptWorkbenchViewModel { IsMemorialResultVisible: true } vm) return;
        var proof = new MemorialProofView { DataContext = vm };
        var dialog = new Window
        {
            Title = AiMemorialLabels.ProofTitle, Content = proof,
            Width = Math.Min(1200, SystemParameters.WorkArea.Width), Height = Math.Min(820, SystemParameters.WorkArea.Height),
            Owner = Window.GetWindow(this), ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        try { dialog.ShowDialog(); }
        finally { proof.DataContext = null; dialog.Content = null; }
    }
}
