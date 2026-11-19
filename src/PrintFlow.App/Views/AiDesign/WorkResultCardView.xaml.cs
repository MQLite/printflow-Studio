using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Data;

namespace PrintFlow.App.Views.AiDesign;

public partial class WorkResultCardView : UserControl
{
    public WorkResultCardView() => InitializeComponent();

    private void OnStageUpdated(object sender, DataTransferEventArgs e)
    {
        if (sender is TextBlock { IsVisible: true } stage &&
            UIElementAutomationPeer.CreatePeerForElement(stage) is { } peer)
            peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
