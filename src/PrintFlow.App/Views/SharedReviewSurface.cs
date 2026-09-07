using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using PrintFlow.App.Resources;
using PrintFlow.App.ViewModels;

namespace PrintFlow.App.Views;

/// <summary>One display-only comparison surface for all general review workflows.</summary>
public sealed class SharedReviewSurface : UserControl
{
    private readonly DockPanel _root = new();
    private readonly WrapPanel _toolbar = new() { Margin = new Thickness(0, 0, 0, 6) };
    private readonly Grid _host = new();
    private readonly Slider _slider = new() { Minimum = 0, Maximum = 100, SmallChange = 1, LargeChange = 10, Width = 160 };
    private readonly List<(RadioButton Button, ReviewComparisonMode Mode)> _modes = [];
    private readonly List<(RadioButton Button, ReviewInspectionBackground Background)> _backgrounds = [];
    private readonly List<Image> _images = [];
    private readonly List<ScrollViewer> _scrolls = [];
    private readonly List<Grid> _canvases = [];
    private readonly Dictionary<ScrollViewer, Point> _pending = [];
    private readonly Brush _checkerboard;
    private SessionViewModel? _model;
    private Grid? _overlay;
    private Border? _divider;
    private bool _updating;

    public SharedReviewSurface()
    {
        Focusable = false;
        AutomationProperties.SetAutomationId(this, "Session.PreviewPanes");
        AutomationProperties.SetName(this, Strings.Session_PreviewHeading);
        DrawingGroup drawing = new();
        drawing.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
        drawing.Children.Add(new GeometryDrawing(Brushes.LightGray, null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
        drawing.Children.Add(new GeometryDrawing(Brushes.LightGray, null, new RectangleGeometry(new Rect(8, 8, 8, 8))));
        _checkerboard = new DrawingBrush(drawing) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 16, 16), Stretch = Stretch.None };
        _checkerboard.Freeze();
        AddMode(ReviewComparisonMode.SideBySide, "ReviewModeSideBySide", Strings.Session_ReviewModeSideBySide);
        AddMode(ReviewComparisonMode.Slider, "ReviewModeSlider", Strings.Session_ReviewModeSlider);
        AddBackground(ReviewInspectionBackground.Checkerboard, "ReviewBackgroundCheckerboard", Strings.Session_ReviewBackgroundCheckerboard);
        AddBackground(ReviewInspectionBackground.White, "ReviewBackgroundWhite", Strings.Session_ReviewBackgroundWhite);
        AddBackground(ReviewInspectionBackground.Black, "ReviewBackgroundBlack", Strings.Session_ReviewBackgroundBlack);
        AutomationProperties.SetAutomationId(_slider, "Session.ReviewComparisonSlider");
        AutomationProperties.SetName(_slider, Strings.Session_ReviewComparisonSlider);
        _slider.ValueChanged += (_, _) => { if (!_updating && _model is not null) _model.ReviewViewport.SliderPosition = _slider.Value; };
        _toolbar.Children.Add(_slider); Button actual = new() { Content = "100%", Padding = new Thickness(10, 4, 10, 4) };
        AutomationProperties.SetAutomationId(actual, "Session.ReviewActualSize");
        AutomationProperties.SetName(actual, "100%");
        actual.Click += (_, _) => { if (_model is not null) { _model.IsFitToViewport = false; _model.ZoomScale = 1; } };
        _toolbar.Children.Add(actual);
        DockPanel.SetDock(_toolbar, Dock.Top);
        _root.Children.Add(_toolbar);
        _root.Children.Add(_host);
        Content = _root;
        _host.SizeChanged += (_, _) => UpdateSizes();
        DataContextChanged += (_, _) => Attach(DataContext as SessionViewModel);
        Unloaded += (_, _) => Attach(null);
        Loaded += (_, _) => Attach(DataContext as SessionViewModel);
    }

    private RadioButton Choice(string id, string label, string group)
    {
        RadioButton button = new() { Content = label, GroupName = group, Margin = new Thickness(0, 2, 12, 2), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(button, "Session." + id);
        AutomationProperties.SetName(button, label);
        _toolbar.Children.Add(button);
        return button;
    }

    private void AddMode(ReviewComparisonMode mode, string id, string label)
    {
        RadioButton button = Choice(id, label, "Comparison" + GetHashCode());
        _modes.Add((button, mode));
        button.Checked += (_, _) => { if (!_updating && _model is not null) _model.ReviewViewport.Mode = mode; };
    }

    private void AddBackground(ReviewInspectionBackground background, string id, string label)
    {
        RadioButton button = Choice(id, label, "Background" + GetHashCode());
        _backgrounds.Add((button, background));
        button.Checked += (_, _) => { if (!_updating && _model is not null) _model.ReviewViewport.Background = background; };
    }

    private void Attach(SessionViewModel? model)
    {
        if (ReferenceEquals(model, _model)) return;
        if (_model is not null)
        {
            _model.PropertyChanged -= ModelChanged;
            _model.ReviewViewport.PropertyChanged -= ViewportChanged;
            _model.PreviewPanes.CollectionChanged -= PanesChanged;
        }
        _model = model;
        if (model is not null)
        {
            model.PropertyChanged += ModelChanged;
            model.ReviewViewport.PropertyChanged += ViewportChanged;
            model.PreviewPanes.CollectionChanged += PanesChanged;
        }
        ReloadImages();
    }

    private void PanesChanged(object? sender, NotifyCollectionChangedEventArgs e) => ReloadImages();
    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SessionViewModel.ZoomScale) or nameof(SessionViewModel.IsFitToViewport)) UpdateSizes();
    }
    private void ViewportChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReviewViewportState.Mode)) Rebuild();
        else if (e.PropertyName is nameof(ReviewViewportState.HorizontalPosition) or nameof(ReviewViewportState.VerticalPosition)) ApplyPositions();
        else UpdatePresentation();
    }

    private void ReloadImages()
    {
        _images.Clear();
        if (_model is not null)
        {
            PreviewPayloadConverter converter = new();
            foreach (ArtefactPreviewPane pane in _model.PreviewPanes)
            {
                Image image = new()
                {
                    Source = converter.Convert(pane.Payload, typeof(ImageSource), null, CultureInfo.CurrentCulture) as ImageSource,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Focusable = false
                };
                AutomationProperties.SetName(image, pane.Heading);
                _images.Add(image);
            }
        }
        Rebuild();
    }

    private bool IsSlider => _model?.ReviewViewport.Mode == ReviewComparisonMode.Slider && _images.Count == 2 && _model.PreviewPanes.All(p => p.HasImage);

    private void Rebuild()
    {
        foreach (Image image in _images)
            if (image.Parent is Panel parent) parent.Children.Remove(image);
        foreach (ScrollViewer scroll in _scrolls) scroll.ScrollChanged -= Scrolled;
        _scrolls.Clear();
        _pending.Clear();
        _canvases.Clear();
        _host.Children.Clear();
        _host.ColumnDefinitions.Clear();
        _overlay = null;
        _divider = null;
        if (_model is null) return;
        for (int i = 0; i < _images.Count; i++)
        {
            ArtefactPreviewPane pane = _model.PreviewPanes[i];
            Grid canvas = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            canvas.Children.Add(_images[i]);
            _canvases.Add(canvas);
            if (IsSlider && i == 1)
            {
                _overlay = canvas;
                Grid baseCanvas = (Grid)_scrolls[0].Content;
                baseCanvas.Children.Add(canvas);
                _divider = new Border { Width = 2, Background = Brushes.DodgerBlue, HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false };
                baseCanvas.Children.Add(_divider);
                continue;
            }
            _host.ColumnDefinitions.Add(new ColumnDefinition());
            DockPanel panel = new() { Margin = new Thickness(0, 0, 10, 0) };
            Grid.SetColumn(panel, i);
            StackPanel heading = new();
            heading.Children.Add(new TextBlock { Text = IsSlider ? _model.BeforeLabel + " / " + _model.AfterLabel : pane.Heading, FontWeight = FontWeights.SemiBold });
            heading.Children.Add(new TextBlock { Text = pane.FileName, TextTrimming = TextTrimming.CharacterEllipsis });
            heading.Children.Add(new TextBlock { Text = pane.Detail, Opacity = 0.6 });
            DockPanel.SetDock(heading, Dock.Top);
            panel.Children.Add(heading);
            ScrollViewer scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = canvas };
            AutomationProperties.SetAutomationId(scroll, IsSlider ? "Session.ReviewOverlay" : i == 0 ? "Session.ReviewBefore" : "Session.ReviewAfter");
            AutomationProperties.SetName(scroll, pane.Heading);
            scroll.ScrollChanged += Scrolled;
            _scrolls.Add(scroll);
            if (pane.HasImage) panel.Children.Add(scroll);
            else panel.Children.Add(new TextBlock { Text = pane.Unavailable, TextWrapping = TextWrapping.Wrap });
            _host.Children.Add(panel);
        }
        UpdatePresentation();
        UpdateSizes();
    }

    private void UpdatePresentation()
    {
        if (_model is null) return;
        _updating = true;
        foreach (var (button, mode) in _modes)
        {
            button.IsChecked = _model.ReviewViewport.Mode == mode;
            button.IsEnabled = mode == ReviewComparisonMode.SideBySide || (_images.Count == 2 && _model.PreviewPanes.All(p => p.HasImage));
        }
        foreach (var (button, background) in _backgrounds) button.IsChecked = _model.ReviewViewport.Background == background;
        _slider.Visibility = IsSlider ? Visibility.Visible : Visibility.Collapsed;
        _slider.Value = _model.ReviewViewport.SliderPosition;
        Brush brush = _model.ReviewViewport.Background switch { ReviewInspectionBackground.White => Brushes.White, ReviewInspectionBackground.Black => Brushes.Black, _ => TryFindResource("TransparencyCheckerboard") as Brush ?? _checkerboard };
        foreach (Grid canvas in _canvases) canvas.Background = brush;
        UpdateClip();
        _updating = false;
    }

    private void UpdateSizes()
    {
        if (_model is null || _images.Count == 0 || _host.ActualWidth <= 0 || _host.ActualHeight <= 0) return;
        double width = _model.PreviewPanes.Max(p => (double)Math.Max(1, p.PayloadPixelWidth));
        double height = _model.PreviewPanes.Max(p => (double)Math.Max(1, p.PayloadPixelHeight));
        double scale = _model.IsFitToViewport
            ? Math.Max(0.001, Math.Min(Math.Max(1, _host.ActualWidth / (IsSlider ? 1 : _images.Count) - 28) / width, Math.Max(1, _host.ActualHeight - 76) / height))
            : _model.ZoomScale;
        for (int i = 0; i < _images.Count; i++)
        {
            ArtefactPreviewPane pane = _model.PreviewPanes[i];
            _images[i].LayoutTransform = new ScaleTransform(_model.IsFitToViewport ? 1 : scale, _model.IsFitToViewport ? 1 : scale);
            _images[i].Width = Math.Max(1, pane.PayloadPixelWidth) * (_model.IsFitToViewport ? scale : 1);
            _images[i].Height = Math.Max(1, pane.PayloadPixelHeight) * (_model.IsFitToViewport ? scale : 1);
            _canvases[i].Width = (IsSlider ? width : Math.Max(1, pane.PayloadPixelWidth)) * scale;
            _canvases[i].Height = (IsSlider ? height : Math.Max(1, pane.PayloadPixelHeight)) * scale;
        }
        UpdateClip();
        ApplyPositions();
    }

    private void UpdateClip()
    {
        if (_overlay is not null && _model is not null)
        {
            _overlay.Clip = new RectangleGeometry(new Rect(0, 0, double.IsNaN(_overlay.Width) ? 0 : _overlay.Width * Math.Clamp(_model.ReviewViewport.SliderPosition, 0, 100) / 100, double.IsNaN(_overlay.Height) ? 0 : _overlay.Height));
            if (_divider is not null)
            {
                double fraction = Math.Clamp(_model.ReviewViewport.SliderPosition, 0, 100) / 100;
                _divider.Margin = new Thickness((double.IsNaN(_overlay.Width) ? 0 : _overlay.Width) * fraction, 0, 0, 0);
                _divider.Visibility = fraction is > 0 and < 1 ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    // ScrollChanged is queued by WPF layout: a synchronous boolean alone cannot suppress echoes.
    // Track the requested target too, and never interpret an extent/viewport change as a user pan.
    private void Scrolled(object sender, ScrollChangedEventArgs e)
    {
        if (_model is null || !ReferenceEquals(e.OriginalSource, sender)) return;
        ScrollViewer scroll = (ScrollViewer)sender;
        if (e.ExtentWidthChange != 0 || e.ExtentHeightChange != 0 || e.ViewportWidthChange != 0 || e.ViewportHeightChange != 0)
        {
            ApplyPositions();
            return;
        }
        if (_pending.Remove(scroll, out Point target) && Math.Abs(scroll.HorizontalOffset - target.X) < 0.01 && Math.Abs(scroll.VerticalOffset - target.Y) < 0.01) return;
        _updating = true;
        if (e.HorizontalChange != 0 && scroll.ScrollableWidth > 0) _model.ReviewViewport.HorizontalPosition = Math.Clamp(scroll.HorizontalOffset / scroll.ScrollableWidth, 0, 1);
        if (e.VerticalChange != 0 && scroll.ScrollableHeight > 0) _model.ReviewViewport.VerticalPosition = Math.Clamp(scroll.VerticalOffset / scroll.ScrollableHeight, 0, 1);
        _updating = false;
        ApplyPositions();
    }

    private void ApplyPositions()
    {
        if (_updating || _model is null) return;
        foreach (ScrollViewer scroll in _scrolls)
        {
            double x = Math.Clamp(_model.ReviewViewport.HorizontalPosition, 0, 1) * scroll.ScrollableWidth;
            double y = Math.Clamp(_model.ReviewViewport.VerticalPosition, 0, 1) * scroll.ScrollableHeight;
            if (Math.Abs(scroll.HorizontalOffset - x) < 0.01 && Math.Abs(scroll.VerticalOffset - y) < 0.01) continue;
            _pending[scroll] = new Point(x, y);
            scroll.ScrollToHorizontalOffset(x);
            scroll.ScrollToVerticalOffset(y);
        }
    }
}
