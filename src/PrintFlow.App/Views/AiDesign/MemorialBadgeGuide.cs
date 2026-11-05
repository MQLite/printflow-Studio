using System.Windows;
using System.Windows.Media;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.App.Views.AiDesign;

/// <summary>Screen-only overlay, fitted to the same rectangle as the displayed image.</summary>
public sealed class MemorialBadgeGuide : FrameworkElement
{
    public static readonly DependencyProperty PreviewProperty = DependencyProperty.Register(nameof(Preview),
        typeof(DecodedPreview), typeof(MemorialBadgeGuide), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SafeRatioProperty = DependencyProperty.Register(nameof(SafeRatio),
        typeof(double), typeof(MemorialBadgeGuide), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public DecodedPreview? Preview { get => (DecodedPreview?)GetValue(PreviewProperty); set => SetValue(PreviewProperty, value); }
    public double SafeRatio { get => (double)GetValue(SafeRatioProperty); set => SetValue(SafeRatioProperty, value); }

    protected override void OnRender(DrawingContext drawing)
    {
        if (Preview is not { PixelWidth: > 0, PixelHeight: > 0 } proof || SafeRatio is <= 0 or > 1 || !double.IsFinite(SafeRatio)) return;
        double scale = Math.Min(ActualWidth / proof.PixelWidth, ActualHeight / proof.PixelHeight);
        if (scale <= 0) return;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        double radiusX = proof.PixelWidth * scale * SafeRatio / 2, radiusY = proof.PixelHeight * scale * SafeRatio / 2;
        drawing.DrawEllipse(null, new Pen(Brushes.Black, 3), center, radiusX, radiusY);
        drawing.DrawEllipse(null, new Pen(Brushes.White, 1.5) { DashStyle = DashStyles.Dash }, center, radiusX, radiusY);
    }
}
