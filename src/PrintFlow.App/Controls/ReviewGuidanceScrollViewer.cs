using System.Windows.Controls;
using System.Windows.Input;

namespace PrintFlow.App.Controls;

public sealed class ReviewGuidanceScrollViewer : ScrollViewer
{
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        bool canScroll = e.Delta > 0 ? VerticalOffset > 0 : VerticalOffset < ScrollableHeight;
        if (canScroll)
            base.OnMouseWheel(e);
    }
}
