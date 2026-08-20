using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Trimming;

namespace PrintFlow.App.Views;

/// <summary>
/// The session screen. Every action is a bound command; the only code here is the crop
/// surface's pointer handling (Epic 11200 Part C2 §5).
/// </summary>
/// <remarks>
/// <b>Why this is code and not a binding.</b> A drag is three events and a transient rubber-band
/// rectangle, none of which is state the session has any business holding. What this file
/// deliberately does <i>not</i> do is decide anything: the display-to-source mapping is
/// <see cref="CropSurfaceLayout"/>, which is pure and tested on its own, and whether a
/// rectangle is usable is <see cref="SessionViewModel.TrySetCropSelection"/>. This file measures
/// the surface, reports two points, and draws an outline (§32).
/// <para>
/// It opens no file, names no path and reaches no service. The one thing a drag can ultimately
/// cause is a <c>SubmitManualCrop</c> command, and only after the operator presses Apply.
/// </para>
/// </remarks>
public partial class SessionScreenView : UserControl
{
    /// <summary>Where the current drag began, in crop-surface coordinates; null when not dragging.</summary>
    private Point? _dragOrigin;

    private SessionViewModel? _observed;

    public SessionScreenView()
    {
        InitializeComponent();

        CropOverlay.MouseLeftButtonDown += OnCropMouseDown;
        CropOverlay.MouseMove += OnCropMouseMove;
        CropOverlay.MouseLeftButtonUp += OnCropMouseUp;
        CropOverlay.LostMouseCapture += OnCropLostCapture;
        CropOverlay.SizeChanged += OnCropSurfaceResized;

        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;
    }

    /// <summary>The view model, when there is one of the expected type.</summary>
    private SessionViewModel? Model => DataContext as SessionViewModel;

    // -----------------------------------------------------------------------------
    // Drag: down, move, up
    // -----------------------------------------------------------------------------

    private void OnCropMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Model is not { IsCropping: true })
        {
            return;
        }

        _dragOrigin = e.GetPosition(CropOverlay);
        CropOverlay.CaptureMouse();
        e.Handled = true;
    }

    /// <summary>
    /// Draws the rubber band while the button is down.
    /// </summary>
    /// <remarks>
    /// Screen coordinates only, and not recorded anywhere: nothing is committed to the view
    /// model until the button comes up, so a drag the operator abandons by releasing outside
    /// leaves the previous selection alone rather than half-replacing it.
    /// </remarks>
    private void OnCropMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragOrigin is not { } origin || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        DrawOutline(origin, e.GetPosition(CropOverlay));
    }

    private void OnCropMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragOrigin is not { } origin || Model is not { } model)
        {
            return;
        }

        Point end = e.GetPosition(CropOverlay);
        _dragOrigin = null;
        CropOverlay.ReleaseMouseCapture();
        e.Handled = true;

        // The view model decides, through the pure geometry helper. A click that never moved,
        // or a rectangle entirely off the artwork, is refused there and the outline goes away
        // rather than being left over an area that was not accepted (§23).
        if (!model.TrySetCropSelection(CurrentLayout(model), origin.X, origin.Y, end.X, end.Y))
        {
            CropSelectionOutline.Visibility = Visibility.Collapsed;
            return;
        }

        RedrawSelection();
    }

    /// <summary>A drag interrupted by anything else — an alt-tab, a dialog — simply ends.</summary>
    private void OnCropLostCapture(object sender, MouseEventArgs e) => _dragOrigin = null;

    // -----------------------------------------------------------------------------
    // Keeping the outline over the same artwork
    // -----------------------------------------------------------------------------

    private void OnCropSurfaceResized(object sender, SizeChangedEventArgs e) => RedrawSelection();

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_observed is not null)
        {
            _observed.PropertyChanged -= OnModelPropertyChanged;
        }

        _observed = Model;

        if (_observed is not null)
        {
            _observed.PropertyChanged += OnModelPropertyChanged;
        }

        RedrawSelection();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_observed is not null)
        {
            _observed.PropertyChanged -= OnModelPropertyChanged;
            _observed = null;
        }
    }

    /// <summary>
    /// Re-projects the outline whenever what it is drawn over changes.
    /// </summary>
    /// <remarks>
    /// Zoom and fit change the mapping, and the selection itself changes when a new drag lands
    /// or the screen leaves crop mode. Because the selection is held in source pixels, every one
    /// of these is answered by projecting it again rather than by adjusting screen coordinates
    /// that would accumulate error (§7).
    /// </remarks>
    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is
            nameof(SessionViewModel.CropSelection) or
            nameof(SessionViewModel.IsCropping) or
            nameof(SessionViewModel.ZoomScale) or
            nameof(SessionViewModel.IsFitToViewport))
        {
            // Queued at Loaded priority so the surface has been re-measured for the new zoom
            // before the outline is placed on it; measuring first would project onto the
            // previous layout.
            Dispatcher.BeginInvoke(RedrawSelection, System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private void RedrawSelection()
    {
        if (Model is not { IsCropping: true, CropSelection: TrimBounds bounds } model ||
            !CurrentLayout(model).TryToSurfaceRect(bounds, out double x, out double y, out double w, out double h))
        {
            CropSelectionOutline.Visibility = Visibility.Collapsed;
            return;
        }

        Place(x, y, w, h);
    }

    private void DrawOutline(Point a, Point b) => Place(
        Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private void Place(double x, double y, double width, double height)
    {
        Canvas.SetLeft(CropSelectionOutline, x);
        Canvas.SetTop(CropSelectionOutline, y);
        CropSelectionOutline.Width = Math.Max(0, width);
        CropSelectionOutline.Height = Math.Max(0, height);
        CropSelectionOutline.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Reads the live geometry of the crop surface into a value the pure helper can work from.
    /// </summary>
    /// <remarks>
    /// The surface is the scrolled <i>content</i>, not the window onto it, which is why no
    /// scroll offset appears here — a point on this canvas is already a point in the content
    /// (see <see cref="CropSurfaceLayout"/>). A pane with no image yields zeroes, which the
    /// helper reports as unusable rather than dividing by.
    /// </remarks>
    private CropSurfaceLayout CurrentLayout(SessionViewModel model)
    {
        ArtefactPreviewPane? pane = model.CropPane;

        return new CropSurfaceLayout(
            CropOverlay.ActualWidth,
            CropOverlay.ActualHeight,
            pane?.PayloadPixelWidth ?? 0,
            pane?.PayloadPixelHeight ?? 0,
            pane?.SourcePixelWidth ?? 0,
            pane?.SourcePixelHeight ?? 0,
            model.IsFitToViewport,
            model.ZoomScale);
    }
}
