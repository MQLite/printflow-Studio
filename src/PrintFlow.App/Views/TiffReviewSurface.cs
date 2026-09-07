using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using PrintFlow.App.Resources;
using PrintFlow.App.ViewModels;

namespace PrintFlow.App.Views;

/// <summary>
/// The specialist production-TIFF inspection surface: Colour, White ink, and Colour + white
/// overlay over one canvas (SCRUM-11104 §5, §14, §33, §34).
/// </summary>
/// <remarks>
/// A separate control from <see cref="SharedReviewSurface"/> and deliberately so. That one solves
/// general Before/After artefact comparison and knows nothing about ink; teaching it to represent
/// a spot channel would make every ordinary review carry machinery it never uses, and forcing a
/// spot channel through a comparison abstraction would mean representing white ink as something
/// it is not (§14).
/// <para>
/// <b>One image element, three sources.</b> All three payloads are decoded once when the model
/// hands them over and a mode switch only re-points <see cref="Image.Source"/>. Because the three
/// share a canvas and therefore a size, the <see cref="ScrollViewer"/>'s extent does not change
/// and its offsets survive the switch untouched — which is how §34's "inspect the same physical
/// region across modes" is held by construction rather than by restoring a remembered position.
/// </para>
/// <para>
/// Zoom is the screen's existing zoom, not a second one. The buttons in the header bind to the
/// same commands the general surface uses, and this control reads
/// <see cref="SessionViewModel.ZoomScale"/> and <see cref="SessionViewModel.IsFitToViewport"/>
/// exactly as that one does, so an operator who zoomed in before switching to White ink stays
/// zoomed in (§33).
/// </para>
/// <para>
/// There is no inspection-background switcher here. A production TIFF is opaque by contract — the
/// fifth sample is ink, not alpha — so a checkerboard would suggest a transparency the file
/// cannot express and would be the one place this surface actively misled (§13).
/// </para>
/// </remarks>
public sealed class TiffReviewSurface : UserControl
{
    private readonly DockPanel _root = new();
    private readonly WrapPanel _toolbar = new() { Margin = new Thickness(0, 0, 0, 6) };
    private readonly List<(RadioButton Button, TiffReviewMode Mode)> _modes = [];
    private readonly Image _image = new()
    {
        Stretch = Stretch.Uniform,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top,
        Focusable = false,
    };

    private readonly Grid _canvas = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Background = Brushes.White,
    };

    /// <summary>
    /// The viewport, and a tab stop of its own (§30, §34).
    /// </summary>
    /// <remarks>
    /// A <see cref="ScrollViewer"/> is not focusable by default, which would leave a keyboard-only
    /// operator able to change modes but not to move around the image inside them. Making it a
    /// tab stop is what turns the arrow keys and Page Up/Down into panning, so "inspect the same
    /// physical region across modes" is something an operator can actually do without a mouse.
    /// </remarks>
    private readonly ScrollViewer _scroll = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Focusable = true,
        IsTabStop = true,
    };

    private readonly TextBlock _legend = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.7 };
    private readonly TextBlock _detail = new() { Opacity = 0.6 };
    private readonly TextBlock _unavailable = new() { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };

    private ImageSource? _colour;
    private ImageSource? _whiteInk;
    private ImageSource? _overlay;
    private SessionViewModel? _model;
    private bool _updating;

    public TiffReviewSurface()
    {
        Focusable = false;
        AutomationProperties.SetAutomationId(this, "Session.TiffReviewSurface");
        AutomationProperties.SetName(this, Strings.Session_TiffModeHeading);

        AddMode(TiffReviewMode.Colour, "TiffReviewColour", Strings.Session_TiffModeColour);
        AddMode(TiffReviewMode.WhiteInk, "TiffReviewWhiteInk", Strings.Session_TiffModeWhiteInk);
        AddMode(TiffReviewMode.Overlay, "TiffReviewOverlay", Strings.Session_TiffModeOverlay);

        _canvas.Children.Add(_image);
        _scroll.Content = _canvas;
        AutomationProperties.SetAutomationId(_scroll, "Session.TiffReviewImage");

        AutomationProperties.SetAutomationId(_legend, "Session.TiffReviewLegend");
        AutomationProperties.SetAutomationId(_detail, "Session.TiffReviewDetail");
        AutomationProperties.SetAutomationId(_unavailable, "Session.TiffReviewUnavailable");

        DockPanel.SetDock(_toolbar, Dock.Top);
        DockPanel.SetDock(_legend, Dock.Bottom);
        DockPanel.SetDock(_detail, Dock.Bottom);
        DockPanel.SetDock(_unavailable, Dock.Top);
        _root.Children.Add(_toolbar);
        _root.Children.Add(_unavailable);
        _root.Children.Add(_legend);
        _root.Children.Add(_detail);
        _root.Children.Add(_scroll);
        Content = _root;

        _scroll.ScrollChanged += Scrolled;
        SizeChanged += (_, _) => UpdateSize();
        DataContextChanged += (_, _) => Attach(DataContext as SessionViewModel);
        Unloaded += (_, _) => Attach(null);
        Loaded += (_, _) => Attach(DataContext as SessionViewModel);
    }

    /// <summary>
    /// One mode control: a standard <see cref="RadioButton"/>, keyboard reachable and UIA
    /// selectable, with a stable id and a localised name (§29, §30, §31, §32).
    /// </summary>
    /// <remarks>
    /// The id is English and never localised; the accessible name is the operator's own wording
    /// in their own language. Those are two different jobs and a control that used one string for
    /// both would break a zh-CN test the moment the workstation language changed.
    /// </remarks>
    private void AddMode(TiffReviewMode mode, string id, string label)
    {
        RadioButton button = new()
        {
            Content = label,
            GroupName = "TiffReviewMode" + GetHashCode().ToString(CultureInfo.InvariantCulture),
            Margin = new Thickness(0, 2, 12, 2),
            VerticalAlignment = VerticalAlignment.Center,
        };

        AutomationProperties.SetAutomationId(button, "Session." + id);
        AutomationProperties.SetName(button, label);
        button.Checked += (_, _) =>
        {
            if (!_updating && _model is not null)
            {
                _model.TiffReviewMode = mode;
            }
        };

        _toolbar.Children.Add(button);
        _modes.Add((button, mode));
    }

    private void Attach(SessionViewModel? model)
    {
        if (ReferenceEquals(model, _model))
        {
            return;
        }

        if (_model is not null)
        {
            _model.PropertyChanged -= ModelChanged;
            _model.ReviewViewport.PropertyChanged -= ViewportChanged;
        }

        _model = model;

        if (model is not null)
        {
            model.PropertyChanged += ModelChanged;
            model.ReviewViewport.PropertyChanged += ViewportChanged;
        }

        ReloadImages();
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SessionViewModel.TiffColourPayload):
            case nameof(SessionViewModel.HasTiffReview):
                ReloadImages();
                break;
            case nameof(SessionViewModel.TiffReviewMode):
                UpdatePresentation();
                break;
            case nameof(SessionViewModel.ZoomScale):
            case nameof(SessionViewModel.IsFitToViewport):
                UpdateSize();
                break;
            default:
                break;
        }
    }

    private void ViewportChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ReviewViewportState.HorizontalPosition)
            or nameof(ReviewViewportState.VerticalPosition))
        {
            ApplyPosition();
        }
    }

    /// <summary>Decodes the three payloads once, when they change. Never on a mode switch (§15).</summary>
    private void ReloadImages()
    {
        PreviewPayloadConverter converter = new();
        _colour = Convert(converter, _model?.TiffColourPayload ?? ReadOnlyMemory<byte>.Empty);
        _whiteInk = Convert(converter, _model?.TiffWhiteInkPayload ?? ReadOnlyMemory<byte>.Empty);
        _overlay = Convert(converter, _model?.TiffOverlayPayload ?? ReadOnlyMemory<byte>.Empty);

        UpdatePresentation();
        UpdateSize();
    }

    private static ImageSource? Convert(PreviewPayloadConverter converter, ReadOnlyMemory<byte> payload) =>
        payload.IsEmpty
            ? null
            : converter.Convert(payload, typeof(ImageSource), null, CultureInfo.CurrentCulture) as ImageSource;

    private void UpdatePresentation()
    {
        if (_model is null)
        {
            return;
        }

        _updating = true;

        foreach ((RadioButton button, TiffReviewMode mode) in _modes)
        {
            button.IsChecked = _model.TiffReviewMode == mode;
            button.IsEnabled = _model.HasTiffReview;
        }

        _image.Source = _model.TiffReviewMode switch
        {
            TiffReviewMode.WhiteInk => _whiteInk,
            TiffReviewMode.Overlay => _overlay,
            _ => _colour,
        };

        // The name follows the mode, so a screen reader announces what is actually being shown
        // rather than the name of a control class (§31).
        AutomationProperties.SetName(_image, _model.TiffPreviewAccessibleName);
        AutomationProperties.SetName(_scroll, _model.TiffPreviewAccessibleName);

        _legend.Text = _model.TiffPreviewLegend;
        _detail.Text = _model.TiffPreviewDetail;

        _unavailable.Text = _model.TiffReviewUnavailable ?? string.Empty;
        _unavailable.Visibility = _model.IsTiffReviewUnavailable ? Visibility.Visible : Visibility.Collapsed;
        _scroll.Visibility = _model.HasTiffReview ? Visibility.Visible : Visibility.Collapsed;

        _updating = false;
    }

    private void UpdateSize()
    {
        if (_model is null || !_model.HasTiffReview || _image.Source is null)
        {
            return;
        }

        double width = Math.Max(1, _model.TiffPreviewPixelWidth);
        double height = Math.Max(1, _model.TiffPreviewPixelHeight);
        double available = Math.Max(1, ActualWidth - 28);
        double vertical = Math.Max(1, ActualHeight - 96);

        double scale = _model.IsFitToViewport
            ? Math.Max(0.001, Math.Min(available / width, vertical / height))
            : _model.ZoomScale;

        _image.LayoutTransform = new ScaleTransform(
            _model.IsFitToViewport ? 1 : scale, _model.IsFitToViewport ? 1 : scale);
        _image.Width = width * (_model.IsFitToViewport ? scale : 1);
        _image.Height = height * (_model.IsFitToViewport ? scale : 1);
        _canvas.Width = width * scale;
        _canvas.Height = height * scale;

        ApplyPosition();
    }

    // The same arrangement SharedReviewSurface uses, and for the same reason: ScrollChanged is
    // queued by layout, so an echo of a programmatic scroll must not be read back as a pan.
    private void Scrolled(object sender, ScrollChangedEventArgs e)
    {
        if (_updating || _model is null || !ReferenceEquals(e.OriginalSource, sender))
        {
            return;
        }

        if (e.ExtentWidthChange != 0 || e.ExtentHeightChange != 0 ||
            e.ViewportWidthChange != 0 || e.ViewportHeightChange != 0)
        {
            ApplyPosition();
            return;
        }

        _updating = true;
        if (e.HorizontalChange != 0 && _scroll.ScrollableWidth > 0)
        {
            _model.ReviewViewport.HorizontalPosition =
                Math.Clamp(_scroll.HorizontalOffset / _scroll.ScrollableWidth, 0, 1);
        }
        if (e.VerticalChange != 0 && _scroll.ScrollableHeight > 0)
        {
            _model.ReviewViewport.VerticalPosition =
                Math.Clamp(_scroll.VerticalOffset / _scroll.ScrollableHeight, 0, 1);
        }
        _updating = false;
    }

    private void ApplyPosition()
    {
        if (_model is null)
        {
            return;
        }

        double x = Math.Clamp(_model.ReviewViewport.HorizontalPosition, 0, 1) * _scroll.ScrollableWidth;
        double y = Math.Clamp(_model.ReviewViewport.VerticalPosition, 0, 1) * _scroll.ScrollableHeight;

        if (Math.Abs(_scroll.HorizontalOffset - x) >= 0.01)
        {
            _scroll.ScrollToHorizontalOffset(x);
        }

        if (Math.Abs(_scroll.VerticalOffset - y) >= 0.01)
        {
            _scroll.ScrollToVerticalOffset(y);
        }
    }
}
