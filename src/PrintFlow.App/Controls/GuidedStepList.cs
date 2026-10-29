using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace PrintFlow.App.Controls;

/// <summary>Read-only route display. Visibility follows the current item; focus and authority never move.</summary>
public sealed class GuidedStepList : ItemsControl
{
    private bool _pending;

    public GuidedStepList() => Loaded += (_, _) => QueueCurrent();

    protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
    {
        base.OnItemsChanged(e);
        QueueCurrent();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        QueueCurrent();
    }

    private void QueueCurrent()
    {
        if (_pending || Dispatcher.HasShutdownStarted) return;
        _pending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _pending = false;
            // Read the latest collection after a clear/repopulate; never retain a stale item.
            var current = Items.OfType<GuidedStepItem>().FirstOrDefault(s => s.IsCurrent || s.State == GuidedStepState.Current);
            if (current is not null && ItemContainerGenerator.ContainerFromItem(current) is FrameworkElement container)
                container.BringIntoView();
        }));
    }
}
