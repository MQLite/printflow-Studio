using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PrintFlow.App.Controls;

/// <summary>Arranges existing controls only. The supplied recommendation is compared, never invoked.</summary>
public sealed class GuidedActionPanel : Panel
{
    public static readonly DependencyProperty PrimaryCommandProperty = DependencyProperty.Register(
        nameof(PrimaryCommand), typeof(ICommand), typeof(GuidedActionPanel),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public ICommand? PrimaryCommand
    {
        get => (ICommand?)GetValue(PrimaryCommandProperty);
        set => SetValue(PrimaryCommandProperty, value);
    }

    private UIElement? Primary => PrimaryCommand is null ? null : InternalChildren.OfType<Button>()
        .FirstOrDefault(b => b.Visibility == Visibility.Visible && ReferenceEquals(b.Command, PrimaryCommand));

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsFinite(availableSize.Width) ? availableSize.Width : 1920;
        foreach (UIElement child in InternalChildren)
            child.Measure(new Size(width, double.PositiveInfinity));
        var primary = Primary;
        double secondaryWidth = Math.Max(1, width - (primary?.DesiredSize.Width ?? 0) - (primary is null ? 0 : 12));
        foreach (UIElement child in InternalChildren)
            if (!ReferenceEquals(child, primary)) child.Measure(new Size(secondaryWidth, double.PositiveInfinity));
        return new Size(width, Layout(width, false));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Layout(finalSize.Width, true);
        return finalSize;
    }

    private double Layout(double width, bool arrange)
    {
        var primary = Primary;
        double columnWidth = Math.Max(1, width - (primary?.DesiredSize.Width ?? 0) - (primary is null ? 0 : 12));
        double x = 0, y = 0, rowHeight = 0;
        foreach (UIElement child in InternalChildren)
        {
            if (ReferenceEquals(child, primary) || child.Visibility == Visibility.Collapsed) continue;
            double w = Math.Min(columnWidth, child.DesiredSize.Width), h = child.DesiredSize.Height;
            if (x > 0 && x + w > columnWidth)
            {
                x = 0; y += rowHeight + 6; rowHeight = 0;
            }
            if (arrange) child.Arrange(new Rect(x, y, w, h));
            x += w; rowHeight = Math.Max(rowHeight, h);
        }
        double height = Math.Max(y + rowHeight, primary?.DesiredSize.Height ?? 0);
        if (arrange && primary is not null)
            primary.Arrange(new Rect(Math.Max(0, width - primary.DesiredSize.Width),
                Math.Max(0, height - primary.DesiredSize.Height), primary.DesiredSize.Width, primary.DesiredSize.Height));
        return height;
    }
}
