using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using PrintFlow.App.Resources;
using PrintFlow.App.ViewModels;

namespace PrintFlow.App.Views;

/// <summary>One display-only comparison surface for all general review workflows.</summary>
public sealed class SharedReviewSurface : UserControl
{
    /// <summary>Save-page presentation only; never changes the review viewport or its approval.</summary>
    public static readonly DependencyProperty ResultOnlyProperty = DependencyProperty.Register(
        nameof(ResultOnly), typeof(bool), typeof(SharedReviewSurface),
        new PropertyMetadata(false, (d, _) => ((SharedReviewSurface)d).ReloadImages()));

    public bool ResultOnly
    {
        get => (bool)GetValue(ResultOnlyProperty);
        set => SetValue(ResultOnlyProperty, value);
    }

    /// <summary>Original inspection shows the actual current pane, retaining zoom and backgrounds.</summary>
    public static readonly DependencyProperty CurrentOnlyProperty = DependencyProperty.Register(
        nameof(CurrentOnly), typeof(bool), typeof(SharedReviewSurface),
        new PropertyMetadata(false, (d, _) => ((SharedReviewSurface)d).ReloadImages()));

    public bool CurrentOnly
    {
        get => (bool)GetValue(CurrentOnlyProperty);
        set => SetValue(CurrentOnlyProperty, value);
    }

    private IReadOnlyList<ArtefactPreviewPane> _panes = [];
    private readonly DockPanel _root = new();
    private readonly Button _reviewToggle = new SessionActionButton { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8) };
    private bool _reviewComparisonExpanded;
    private bool ShowsResultOnly => ResultOnly && !_reviewComparisonExpanded;
    private readonly StackPanel _toolbar = new() { Margin = new Thickness(0, 0, 0, 6) };
    private readonly ScrollViewer _toolbarScroll = new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Focusable = false,
        IsTabStop = false,
    };
    private readonly WrapPanel _zoomTools = new();
    private readonly WrapPanel _inspectionTools = new();
    private readonly Grid _host = new();
    private readonly Slider _slider = new() { Minimum = 0, Maximum = 100, SmallChange = 1, LargeChange = 10, Width = 120, MinHeight = 42 };
    private readonly Button _actualSize = new SessionActionButton { Content = "100%", Margin = new Thickness(0, 0, 6, 4) };
    private readonly Button _inspectionToggle = new SessionActionButton { Content = "⋯", MinWidth = 42, Margin = new Thickness(0, 0, 6, 4), Visibility = Visibility.Collapsed };
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
    private bool _compactToolsMode;
    private bool _inspectionExpanded;

    public SharedReviewSurface()
    {
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/PrintFlow.App;component/Themes/WpfUiTheme.xaml", UriKind.Relative)
        });
        FontSize = 16;
        Focusable = false;
        AutomationProperties.SetAutomationId(this, "Session.PreviewPanes");
        AutomationProperties.SetName(this, Strings.Session_PreviewHeading);
        _reviewToggle.SetResourceReference(StyleProperty, "PF.Button.Secondary");
        AutomationProperties.SetAutomationId(_reviewToggle, "Session.Save.ReviewResult");
        _reviewToggle.Click += (_, _) =>
        {
            _reviewComparisonExpanded = !_reviewComparisonExpanded;
            ReloadImages();
        };
        DockPanel.SetDock(_reviewToggle, Dock.Top);
        _root.Children.Add(_reviewToggle);
        DrawingGroup drawing = new();
        drawing.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
        drawing.Children.Add(new GeometryDrawing(Brushes.LightGray, null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
        drawing.Children.Add(new GeometryDrawing(Brushes.LightGray, null, new RectangleGeometry(new Rect(8, 8, 8, 8))));
        _checkerboard = new DrawingBrush(drawing) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 16, 16), Stretch = Stretch.None };
        _checkerboard.Freeze();
        AutomationProperties.SetAutomationId(_toolbar, "Session.ReviewTools");
        _toolbar.Children.Add(_zoomTools);
        _toolbar.Children.Add(_inspectionTools);
        TextBlock zoomStatus = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 4) };
        zoomStatus.SetBinding(TextBlock.TextProperty, new Binding(nameof(SessionViewModel.ZoomLabel)));
        AutomationProperties.SetAutomationId(zoomStatus, "Session.ReviewZoomStatus");
        _zoomTools.Children.Add(zoomStatus);
        AddZoom("ReviewZoomOut", "ZoomOutLabel", "ZoomOutCommand", "CanZoomOut");
        AddZoom("ReviewZoomIn", "ZoomInLabel", "ZoomInCommand", "CanZoomIn");
        AddZoom("ReviewResetZoom", "ResetZoomLabel", "ResetZoomCommand", null);
        AddMode(ReviewComparisonMode.SideBySide, "ReviewModeSideBySide", Strings.Session_ReviewModeSideBySide);
        AddMode(ReviewComparisonMode.Slider, "ReviewModeSlider", Strings.Session_ReviewModeSlider);
        AddBackground(ReviewInspectionBackground.Checkerboard, "ReviewBackgroundCheckerboard", Strings.Session_ReviewBackgroundCheckerboard);
        AddBackground(ReviewInspectionBackground.White, "ReviewBackgroundWhite", Strings.Session_ReviewBackgroundWhite);
        AddBackground(ReviewInspectionBackground.Black, "ReviewBackgroundBlack", Strings.Session_ReviewBackgroundBlack);
        AutomationProperties.SetAutomationId(_slider, "Session.ReviewComparisonSlider");
        AutomationProperties.SetName(_slider, Strings.Session_ReviewComparisonSlider);
        _slider.ValueChanged += (_, _) => { if (!_updating && _model is not null) _model.ReviewViewport.SliderPosition = _slider.Value; };
        _inspectionTools.Children.Add(_slider); Button actual = _actualSize;
        actual.SetResourceReference(StyleProperty, "PF.Button.Secondary");
        AutomationProperties.SetAutomationId(actual, "Session.ReviewActualSize");
        AutomationProperties.SetName(actual, "100%");
        actual.Click += (_, _) => { if (_model is not null) { _model.IsFitToViewport = false; _model.ZoomScale = 1; } };
        _zoomTools.Children.Add(actual);
        _inspectionToggle.SetResourceReference(StyleProperty, "PF.Button.Secondary");
        AutomationProperties.SetAutomationId(_inspectionToggle, "Session.ReviewToolsToggle");
        AutomationProperties.SetName(_inspectionToggle, Strings.Resolve("Session_ReviewToolsLabel"));
        _inspectionToggle.ToolTip = Strings.Resolve("Session_ReviewToolsLabel");
        _inspectionToggle.Click += (_, _) =>
        {
            _inspectionExpanded = !_inspectionExpanded;
            UpdatePresentation();
            UpdateSizes();
        };
        _zoomTools.Children.Add(_inspectionToggle);
        AutomationProperties.SetAutomationId(_toolbarScroll, "Session.ReviewToolsScroll");
        _toolbarScroll.Content = _toolbar;
        DockPanel.SetDock(_toolbarScroll, Dock.Top);
        _root.Children.Add(_toolbarScroll);
        _root.Children.Add(_host);
        Content = _root;
        _host.SizeChanged += (_, _) => UpdateSizes();
        SizeChanged += (_, _) => UpdateSizes();
        DataContextChanged += (_, _) => Attach(DataContext as SessionViewModel);
        Unloaded += (_, _) => Attach(null);
        Loaded += (_, _) => Attach(DataContext as SessionViewModel);
    }

    private void AddZoom(string id, string label, string command, string? enabled)
    {
        SessionActionButton button = new() { Margin = new Thickness(0, 0, 6, 4) };
        button.SetResourceReference(StyleProperty, "PF.Button.Secondary");
        button.SetBinding(ContentControl.ContentProperty, new Binding(label));
        button.SetBinding(AutomationProperties.NameProperty, new Binding(label));
        button.SetBinding(Button.CommandProperty, new Binding(command));
        if (enabled is not null) button.SetBinding(IsEnabledProperty, new Binding(enabled));
        AutomationProperties.SetAutomationId(button, "Session." + id);
        _zoomTools.Children.Add(button);
    }

    private RadioButton Choice(string id, string label, string group)
    {
        RadioButton button = new SessionRadioButton { Content = label, GroupName = group, Margin = new Thickness(0, 0, 8, 4), VerticalAlignment = VerticalAlignment.Center };
        button.SetResourceReference(StyleProperty, "PF.Input.RadioButton");
        AutomationProperties.SetAutomationId(button, "Session." + id);
        AutomationProperties.SetName(button, label);
        _inspectionTools.Children.Add(button);
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
        _reviewComparisonExpanded = false;
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
        if (e.PropertyName == nameof(SessionViewModel.SaveReviewPanes) && ResultOnly) ReloadImages();
        if (e.PropertyName == nameof(SessionViewModel.GuidedPage))
        {
            _reviewComparisonExpanded = false;
            ReloadImages();
        }
        if (e.PropertyName is nameof(SessionViewModel.ZoomScale) or nameof(SessionViewModel.IsFitToViewport)) UpdateSizes();
        if (e.PropertyName == nameof(SessionViewModel.ShowsGuidedPreviewTools)) { UpdatePresentation(); UpdateSizes(); }
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
        _panes = _model is null ? [] : ShowsResultOnly || CurrentOnly
            ? _model.PreviewPanes.TakeLast(1).ToArray()
            : ResultOnly ? _model.SaveReviewPanes : _model.PreviewPanes.ToArray();
        if (_model is not null)
        {
            PreviewPayloadConverter converter = new();
            foreach (ArtefactPreviewPane pane in _panes)
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

    private bool IsSlider => !ShowsResultOnly && _model?.ReviewViewport.Mode == ReviewComparisonMode.Slider && _images.Count == 2 && _panes.All(p => p.HasImage);

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
            ArtefactPreviewPane pane = _panes[i];
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
            if (CurrentOnly)
            {
                string detail = pane.FileName + " · " + pane.Detail;
                heading.Children.Add(new TextBlock { Text = detail, ToolTip = detail,
                    TextTrimming = TextTrimming.CharacterEllipsis });
            }
            else
            {
                heading.Children.Add(new TextBlock { Text = ShowsResultOnly ? Strings.Resolve("Session_SaveCurrentResult") : IsSlider ? _model.BeforeLabel + " / " + _model.AfterLabel : pane.Heading, FontWeight = FontWeights.SemiBold });
                string detail = pane.FileName + " · " + pane.Detail;
                heading.Children.Add(new TextBlock { Text = detail, ToolTip = detail, TextTrimming = TextTrimming.CharacterEllipsis });
            }
            DockPanel.SetDock(heading, Dock.Top);
            panel.Children.Add(heading);
            ScrollViewer scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = canvas };
            AutomationProperties.SetAutomationId(scroll, IsSlider ? "Session.ReviewOverlay" : ShowsResultOnly || CurrentOnly && _model.PreviewPanes.Count > 1 ? "Session.ReviewAfter" : i == 0 ? "Session.ReviewBefore" : "Session.ReviewAfter");
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
        _reviewToggle.Visibility = ResultOnly && _model.SaveReviewPanes.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        _reviewToggle.Content = Strings.Resolve(_reviewComparisonExpanded ? "Session_SaveHideReview" : "Session_SaveReviewResult");
        AutomationProperties.SetName(_reviewToggle, (string)_reviewToggle.Content);
        _toolbar.Visibility = !ShowsResultOnly && _model.ShowsGuidedPreviewTools ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (button, mode) in _modes)
        {
            button.IsChecked = _model.ReviewViewport.Mode == mode;
            button.IsEnabled = mode == ReviewComparisonMode.SideBySide || (_images.Count == 2 && _panes.All(p => p.HasImage));
            button.Visibility = _images.Count == 2 && _images.All(i => i.Source is not null) ? Visibility.Visible : Visibility.Collapsed;
        }
        bool hasImage = _images.Any(i => i.Source is not null);
        // Optional save comparison and completed inspection keep the image usable first.
        bool compact = ResultOnly || _model.GuidedPage == GuidedSessionPage.Completed;
        if (compact != _compactToolsMode)
        {
            _compactToolsMode = compact;
            _inspectionExpanded = false;
        }
        _inspectionTools.Visibility = compact && !_inspectionExpanded ? Visibility.Collapsed : Visibility.Visible;
        _inspectionToggle.Visibility = compact && hasImage ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (button, background) in _backgrounds)
        {
            button.IsChecked = _model.ReviewViewport.Background == background;
            button.Visibility = hasImage ? Visibility.Visible : Visibility.Collapsed;
        }
        _actualSize.Visibility = hasImage && (!compact || _inspectionExpanded) ? Visibility.Visible : Visibility.Collapsed;
        _slider.Visibility = IsSlider ? Visibility.Visible : Visibility.Collapsed;
        _slider.Value = _model.ReviewViewport.SliderPosition;
        Brush brush = _model.ReviewViewport.Background switch { ReviewInspectionBackground.White => Brushes.White, ReviewInspectionBackground.Black => Brushes.Black, _ => TryFindResource("TransparencyCheckerboard") as Brush ?? _checkerboard };
        foreach (Grid canvas in _canvases) canvas.Background = brush;
        UpdateClip();
        _updating = false;
    }

    private void UpdateSizes()
    {
        // A wrapped optional toolbar must leave room for the comparison it operates on.
        // The remaining tools stay reachable through their own scroll region at small sizes.
        _toolbarScroll.MaxHeight = _compactToolsMode && _root.ActualHeight > 0
            ? Math.Max(48, _root.ActualHeight - _reviewToggle.DesiredSize.Height - 196)
            : double.PositiveInfinity;
        if (_model is null || _images.Count == 0 || _host.ActualWidth <= 0 || _host.ActualHeight <= 0) return;
        double width = _panes.Max(p => (double)Math.Max(1, p.PayloadPixelWidth));
        double height = _panes.Max(p => (double)Math.Max(1, p.PayloadPixelHeight));
        bool fit = ShowsResultOnly || _model.IsFitToViewport || !_model.ShowsGuidedPreviewTools;
        double scale = fit
            ? Math.Max(0.001, Math.Min(Math.Max(1, _host.ActualWidth / (IsSlider ? 1 : _images.Count) - 28) / width, Math.Max(1, _host.ActualHeight - (CurrentOnly ? 28 : 76)) / height))
            : _model.ZoomScale;
        for (int i = 0; i < _images.Count; i++)
        {
            ArtefactPreviewPane pane = _panes[i];
            _images[i].LayoutTransform = new ScaleTransform(fit ? 1 : scale, fit ? 1 : scale);
            _images[i].Width = Math.Max(1, pane.PayloadPixelWidth) * (fit ? scale : 1);
            _images[i].Height = Math.Max(1, pane.PayloadPixelHeight) * (fit ? scale : 1);
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
            // Before (the base) shows left of the divider and After (the overlay) right of it, as
            // the "Before / After" heading and the side-by-side order read (SCRUM-11154 F-V3).
            double width = double.IsNaN(_overlay.Width) ? 0 : _overlay.Width;
            double fraction = Math.Clamp(_model.ReviewViewport.SliderPosition, 0, 100) / 100;
            _overlay.Clip = new RectangleGeometry(new Rect(width * fraction, 0, width * (1 - fraction), double.IsNaN(_overlay.Height) ? 0 : _overlay.Height));
            if (_divider is not null)
            {
                _divider.Margin = new Thickness(width * fraction, 0, 0, 0);
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
