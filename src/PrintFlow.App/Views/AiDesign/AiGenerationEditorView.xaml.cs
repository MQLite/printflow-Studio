using System.Windows.Controls;
using PrintFlow.App.Controls;
using PrintFlow.App.ViewModels.AiDesign;

namespace PrintFlow.App.Views.AiDesign;

public partial class AiGenerationEditorView : UserControl
{
    public AiGenerationEditorView() => InitializeComponent();
    private void OnImageReferenceInvoked(object sender, AiImageReferenceInvokedEventArgs e)
    {
        if (DataContext is AiPromptWorkbenchViewModel vm && vm.PickSlotImageCommand.CanExecute(e.SlotId))
            vm.PickSlotImageCommand.Execute(e.SlotId);
    }
}
