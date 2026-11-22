using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using PrintFlow.App.Resources;
using PrintFlow.App.ViewModels;

namespace PrintFlow.App.Views;

/// <summary>
/// Inspects a production TIFF as a composite, individual separations, a tinted overlay, or
/// two synchronized separations side by side.
/// </summary>
/// <remarks>
/// The screen's existing zoom and viewport position apply in every mode. The checkerboard
/// shows gaps in the selected ink coverage through the preview PNGs' display alpha.
/// </remarks>
public sealed class TiffReviewSurface : UserControl
{
    private static readonly Brush Checkerboard = CreateCheckerboard();
    private readonly DockPanel _root = new();
    private readonly StackPanel _toolbar = new() { Margin = new Thickness(0, 0, 0, 2) };
    private readonly WrapPanel _zoomTools = new();
    private readonly WrapPanel _modeTools = new();
    private readonly TextBlock _zoomStatus = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 2) };
    private readonly SessionComboBox _modeSelector = new()
    {
        Visibility = Visibility.Collapsed,
        MinHeight = 34,
        Padding = new Thickness(7, 3, 7, 3),
        Margin = new Thickness(0, 0, 6, 2),
        SelectedValuePath = nameof(FrameworkElement.Tag),
    };
    private readonly List<(RadioButton Button, TiffReviewMode Mode)> _modes = [];
    private readonly Grid _previews = new() { Visibility = Visibility.Collapsed };
    private readonly ColumnDefinition _compareGap = new() { Width = new GridLength(0) };
    private readonly ColumnDefinition _compareColumn = new() { Width = new GridLength(0) };
    private readonly PreviewPane _primary = new("Session.TiffReviewImage", "CMYK");
    private readonly PreviewPane _comparison = new("Session.TiffReviewCompareWhiteInk", $"W1 · {Strings.Session_TiffModeWhiteInk}");
    private readonly ScaleTransform _zoomTransform = new();

    private readonly TextBlock _legend = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 14 };
    private readonly TextBlock _detail = new()
    {
        Opacity = 0.7,
        FontSize = 14,
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 2, 0, 2),
    };
    private readonly TextBlock _unavailable = new() { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };

    private ImageSource? _composite;
    private ImageSource? _colour;
    private ImageSource? _whiteInk;
    private ImageSource? _overlay;
    private SessionViewModel? _model;
    private bool _updating;

    public TiffReviewSurface()
    {
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/PrintFlow.App;component/Themes/WpfUiTheme.xaml", UriKind.Relative)
        });
        FontSize = 16;
        Focusable = false;
        AutomationProperties.SetAutomationId(this, "Session.TiffReviewSurface");
        AutomationProperties.SetName(this, Strings.Session_TiffModeHeading);

        AutomationProperties.SetAutomationId(_toolbar, "Session.TiffReviewTools");
        _modeSelector.SetResourceReference(StyleProperty, "PF.Input.ComboBox");
        AutomationProperties.SetAutomationId(_modeSelector, "Session.TiffReviewModeSelector");
        AutomationProperties.SetName(_modeSelector, Strings.Session_TiffModeHeading);
        _zoomTools.Children.Add(_modeSelector);
        _zoomStatus.SetBinding(TextBlock.TextProperty, new Binding(nameof(SessionViewModel.ZoomLabel)));
        AutomationProperties.SetAutomationId(_zoomStatus, "Session.TiffReviewZoomStatus");
        _zoomTools.Children.Add(_zoomStatus);
        AddZoom("TiffReviewZoomOut", "−", nameof(SessionViewModel.ZoomOutLabel), nameof(SessionViewModel.ZoomOutCommand), nameof(SessionViewModel.CanZoomOut));
        AddZoom("TiffReviewZoomIn", "+", nameof(SessionViewModel.ZoomInLabel), nameof(SessionViewModel.ZoomInCommand), nameof(SessionViewModel.CanZoomIn));
        AddZoom("TiffReviewResetZoom", Strings.Session_ZoomFit, nameof(SessionViewModel.ResetZoomLabel), nameof(SessionViewModel.ResetZoomCommand), null);
        _toolbar.Children.Add(_zoomTools);
        _toolbar.Children.Add(_modeTools);
        AddMode(TiffReviewMode.Composite, "TiffReviewComposite", Strings.Session_TiffModeComposite, Strings.Session_TiffModeComposite);
        AddMode(TiffReviewMode.Compare, "TiffReviewCompare", Strings.Session_TiffModeCompare, Strings.Session_TiffModeCompareShort);
        AddMode(TiffReviewMode.Colour, "TiffReviewColour", Strings.Session_TiffModeColour, "CMYK");
        AddMode(TiffReviewMode.WhiteInk, "TiffReviewWhiteInk", Strings.Session_TiffModeWhiteInk, "W1");
        AddMode(TiffReviewMode.Overlay, "TiffReviewOverlay", Strings.Session_TiffModeOverlay, Strings.Session_TiffModeOverlayShort);
        _modeSelector.SetBinding(ComboBox.SelectedValueProperty, new Binding(nameof(SessionViewModel.TiffReviewMode)) { Mode = BindingMode.TwoWay });
        _modeSelector.SetBinding(IsEnabledProperty, new Binding(nameof(SessionViewModel.HasTiffReview)));
        _zoomTools.Children.Add(_detail);

        _previews.ColumnDefinitions.Add(new ColumnDefinition());
        _previews.ColumnDefinitions.Add(_compareGap);
        _previews.ColumnDefinitions.Add(_compareColumn);
        _previews.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _previews.RowDefinitions.Add(new RowDefinition());
        AddPreviewPane(_primary, 0);
        AddPreviewPane(_comparison, 2);

        AutomationProperties.SetAutomationId(_legend, "Session.TiffReviewLegend");
        AutomationProperties.SetAutomationId(_detail, "Session.TiffReviewDetail");
        AutomationProperties.SetAutomationId(_unavailable, "Session.TiffReviewUnavailable");

        DockPanel.SetDock(_toolbar, Dock.Top);
        DockPanel.SetDock(_legend, Dock.Bottom);
        DockPanel.SetDock(_unavailable, Dock.Top);
        _root.Children.Add(_toolbar);
        _root.Children.Add(_unavailable);
        _root.Children.Add(_legend);
        _root.Children.Add(_previews);
        Content = _root;

        DataContextChanged += (_, _) => Attach(DataContext as SessionViewModel);
        Unloaded += (_, _) => Attach(null);
        Loaded += (_, _) => Attach(DataContext as SessionViewModel);
    }

    private void AddPreviewPane(PreviewPane pane, int column)
    {
        pane.Image.LayoutTransform = _zoomTransform;
        Grid.SetColumn(pane.Heading, column);
        _previews.Children.Add(pane.Heading);
        Grid.SetRow(pane.Scroll, 1);
        Grid.SetColumn(pane.Scroll, column);
        _previews.Children.Add(pane.Scroll);
        pane.Scroll.SizeChanged += (_, _) => UpdateSize();
        pane.Scroll.ScrollChanged += (sender, e) => Scrolled(pane, sender, e);
    }

    private static Brush CreateCheckerboard()
    {
        SolidColorBrush light = new(Color.FromRgb(148, 148, 148));
        SolidColorBrush dark = new(Color.FromRgb(116, 116, 116));
        DrawingGroup drawing = new();
        drawing.Children.Add(new GeometryDrawing(light, null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
        drawing.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
        drawing.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(8, 8, 8, 8))));
        DrawingBrush brush = new(drawing)
        {
            TileMode = TileMode.Tile,
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, 16, 16),
            Stretch = Stretch.None,
        };
        brush.Freeze();
        return brush;
    }

    private void AddZoom(string id, string content, string label, string command, string? enabled)
    {
        Button button = new SessionActionButton
        {
            Content = content,
            Margin = new Thickness(0, 0, 4, 2),
            MinWidth = 30,
            MinHeight = 34,
            Padding = new Thickness(7, 3, 7, 3),
        };
        button.SetResourceReference(StyleProperty, "PF.Button.Secondary");
        button.SetBinding(ToolTipProperty, new Binding(label));
        button.SetBinding(AutomationProperties.NameProperty, new Binding(label));
        button.SetBinding(Button.CommandProperty, new Binding(command));
        if (enabled is not null) button.SetBinding(IsEnabledProperty, new Binding(enabled));
        AutomationProperties.SetAutomationId(button, "Session." + id);
        _zoomTools.Children.Add(button);
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
    private void AddMode(TiffReviewMode mode, string id, string label, string visibleLabel)
    {
        RadioButton button = new()
        {
            Content = visibleLabel,
            ToolTip = label,
            GroupName = "TiffReviewMode" + GetHashCode().ToString(CultureInfo.InvariantCulture),
            Margin = new Thickness(0, 0, 4, 0),
            Padding = new Thickness(2, 0, 2, 0),
            MinHeight = 42,
            VerticalAlignment = VerticalAlignment.Center,
        };
        button.SetResourceReference(StyleProperty, "PF.Input.RadioButton");

        AutomationProperties.SetAutomationId(button, "Session." + id);
        AutomationProperties.SetName(button, label);
        button.Checked += (_, _) =>
        {
            if (!_updating && _model is not null)
            {
                _model.TiffReviewMode = mode;
            }
        };

        _modeTools.Children.Add(button);
        _modes.Add((button, mode));
        _modeSelector.Items.Add(new ComboBoxItem { Content = label, Tag = mode });
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
            case nameof(SessionViewModel.HasTiffReview):
                ReloadImages();
                break;
            case nameof(SessionViewModel.TiffReviewMode):
                UpdatePresentation();
                UpdateSize();
                break;
            case nameof(SessionViewModel.IsTiffReviewUnavailable):
                UpdatePresentation();
                break;
            case nameof(SessionViewModel.GuidedPage):
                UpdateToolbar();
                break;
            case nameof(SessionViewModel.ZoomScale):
            case nameof(SessionViewModel.IsFitToViewport):
                UpdateToolbar();
                UpdateSize();
                break;
            default:
                break;
        }
    }

    private void ViewportChanged(object? sender, PropertyChangedEventArgs e)
    {
        // A pan updates both axes from one ScrollChanged event. Do not reapply the
        // still-old other axis while recording the first property notification.
        if (_updating) return;
        if (e.PropertyName is nameof(ReviewViewportState.HorizontalPosition)
            or nameof(ReviewViewportState.VerticalPosition))
        {
            ApplyPosition();
        }
    }

    /// <summary>Decodes the payloads when the model supplies a new review.</summary>
    private void ReloadImages()
    {
        PreviewPayloadConverter converter = new();
        _composite = Convert(converter, _model?.TiffCompositePayload ?? ReadOnlyMemory<byte>.Empty);
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
            _primary.Image.Source = null;
            _comparison.Image.Source = null;
            _previews.Visibility = Visibility.Collapsed;
            return;
        }

        _updating = true;
        UpdateToolbar();

        foreach ((RadioButton button, TiffReviewMode mode) in _modes)
        {
            button.IsChecked = _model.TiffReviewMode == mode;
            button.IsEnabled = _model.HasTiffReview;
        }

        bool compare = _model.TiffReviewMode == TiffReviewMode.Compare;
        _primary.Image.Source = _model.TiffReviewMode switch
        {
            TiffReviewMode.Colour or TiffReviewMode.Compare => _colour,
            TiffReviewMode.WhiteInk => _whiteInk,
            TiffReviewMode.Overlay => _overlay,
            _ => _composite,
        };
        _comparison.Image.Source = compare ? _whiteInk : null;
        Visibility compareVisibility = compare ? Visibility.Visible : Visibility.Collapsed;
        _primary.Heading.Visibility = compareVisibility;
        _comparison.Heading.Visibility = compareVisibility;
        _comparison.Scroll.Visibility = compareVisibility;
        _compareColumn.Width = compare ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        _compareGap.Width = new GridLength(compare ? 8 : 0);

        AutomationProperties.SetName(_primary.Scroll, _model.TiffPreviewAccessibleName);
        AutomationProperties.SetName(_primary.Image, compare ? Strings.Session_TiffColourPreviewName : _model.TiffPreviewAccessibleName);
        AutomationProperties.SetName(_comparison.Scroll, Strings.Session_TiffWhiteInkPreviewName);
        AutomationProperties.SetName(_comparison.Image, Strings.Session_TiffWhiteInkPreviewName);

        _legend.Text = _model.TiffPreviewLegend;
        _detail.Text = _model.TiffPreviewDetail;

        _unavailable.Text = _model.TiffReviewUnavailable ?? string.Empty;
        _unavailable.Visibility = _model.IsTiffReviewUnavailable ? Visibility.Visible : Visibility.Collapsed;
        _previews.Visibility = _model.HasTiffReview ? Visibility.Visible : Visibility.Collapsed;

        _updating = false;
    }

    private void UpdateToolbar()
    {
        bool compact = _model?.GuidedPage is GuidedSessionPage.Save or GuidedSessionPage.Completed;
        _modeTools.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        _modeSelector.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        _zoomStatus.Visibility = compact && _model?.IsFitToViewport == true ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateSize()
    {
        if (_model is null || !_model.HasTiffReview || _primary.Image.Source is null)
        {
            return;
        }

        double width = Math.Max(1, _model.TiffPreviewPixelWidth);
        double height = Math.Max(1, _model.TiffPreviewPixelHeight);
        // The actual viewport already excludes the wrapping tool rows and legends.
        // Reserving a fixed header height can crop the fitted image on narrow layouts.
        Size available = ViewportSize(_primary.Scroll);
        if (_model.TiffReviewMode == TiffReviewMode.Compare)
        {
            Size other = ViewportSize(_comparison.Scroll);
            available = new Size(Math.Min(available.Width, other.Width), Math.Min(available.Height, other.Height));
        }

        bool fit = _model.IsFitToViewport;
        double scale = fit
            ? Math.Min(available.Width / width, available.Height / height)
            : _model.ZoomScale;

        _zoomTransform.ScaleX = _zoomTransform.ScaleY = fit ? 1 : scale;
        _primary.Resize(width, height, scale, fit);
        _comparison.Resize(width, height, scale, fit);

        ApplyPosition();
    }

    private static Size ViewportSize(ScrollViewer scroll) => new(
        Math.Max(1, scroll.ViewportWidth > 0 ? scroll.ViewportWidth : scroll.ActualWidth),
        Math.Max(1, scroll.ViewportHeight > 0 ? scroll.ViewportHeight : scroll.ActualHeight));

    private void Scrolled(PreviewPane pane, object sender, ScrollChangedEventArgs e)
    {
        if (_updating || _model?.HasTiffReview != true || !ReferenceEquals(e.OriginalSource, sender) ||
            pane.Scroll.Visibility != Visibility.Visible)
        {
            return;
        }

        ScrollViewer scroll = pane.Scroll;
        if (e.ExtentWidthChange != 0 || e.ExtentHeightChange != 0 ||
            e.ViewportWidthChange != 0 || e.ViewportHeightChange != 0)
        {
            if (e.ViewportWidthChange != 0 || e.ViewportHeightChange != 0) UpdateSize();
            ApplyPosition();
            return;
        }

        // ScrollChanged is queued by layout. An echo of our synchronized scroll must not update
        // the shared position as if the operator had moved the other pane.
        Point? pending = pane.PendingOffset;
        pane.PendingOffset = null;
        if (pending is { } target && IsAtOffset(scroll, target)) return;

        _updating = true;
        if (e.HorizontalChange != 0 && scroll.ScrollableWidth > 0)
        {
            _model.ReviewViewport.HorizontalPosition =
                Math.Clamp(scroll.HorizontalOffset / scroll.ScrollableWidth, 0, 1);
        }
        if (e.VerticalChange != 0 && scroll.ScrollableHeight > 0)
        {
            _model.ReviewViewport.VerticalPosition =
                Math.Clamp(scroll.VerticalOffset / scroll.ScrollableHeight, 0, 1);
        }
        _updating = false;
        ApplyPosition();
    }

    private void ApplyPosition()
    {
        if (_model is null) return;

        Apply(_primary, _model.ReviewViewport);
        if (_comparison.Scroll.Visibility == Visibility.Visible)
        {
            Apply(_comparison, _model.ReviewViewport);
        }

        static void Apply(PreviewPane pane, ReviewViewportState viewport)
        {
            ScrollViewer scroll = pane.Scroll;
            Point target = new(
                Math.Clamp(viewport.HorizontalPosition, 0, 1) * scroll.ScrollableWidth,
                Math.Clamp(viewport.VerticalPosition, 0, 1) * scroll.ScrollableHeight);
            if (IsAtOffset(scroll, target)) return;

            pane.PendingOffset = target;
            scroll.ScrollToHorizontalOffset(target.X);
            scroll.ScrollToVerticalOffset(target.Y);
        }
    }

    private static bool IsAtOffset(ScrollViewer scroll, Point offset) =>
        Math.Abs(scroll.HorizontalOffset - offset.X) < 0.01 && Math.Abs(scroll.VerticalOffset - offset.Y) < 0.01;

    private sealed class PreviewPane
    {
        public PreviewPane(string automationId, string heading)
        {
            Heading.Text = heading;
            Canvas.Children.Add(Image);
            Scroll.Content = Canvas;
            AutomationProperties.SetAutomationId(Scroll, automationId);
        }

        public TextBlock Heading { get; } = new()
        {
            Margin = new Thickness(0, 0, 0, 4),
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontWeight = FontWeights.SemiBold,
            Visibility = Visibility.Collapsed,
        };

        public Image Image { get; } = new()
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Focusable = false,
        };

        public Grid Canvas { get; } = new()
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = Checkerboard,
        };

        // A tab stop lets keyboard operators pan with arrows and Page Up/Down.
        public ScrollViewer Scroll { get; } = new()
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Focusable = true,
            IsTabStop = true,
        };

        public Point? PendingOffset { get; set; }

        public void Resize(double width, double height, double scale, bool fit)
        {
            Image.Width = width * (fit ? scale : 1);
            Image.Height = height * (fit ? scale : 1);
            Canvas.Width = width * scale;
            Canvas.Height = height * scale;
        }
    }
}
