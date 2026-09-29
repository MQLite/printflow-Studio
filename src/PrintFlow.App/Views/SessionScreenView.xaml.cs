using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Shapes;
using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Trimming;

namespace PrintFlow.App.Views;

/// <summary>
/// The session screen. Actions remain bound commands. This view handles crop pointer
/// geometry and non-activating focus when the exact review target changes.
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
    private string? _reviewTarget;
    private DependencyObject? _reviewFocusScope;

    public SessionScreenView()
    {
        InitializeComponent();

        CropOverlay.MouseLeftButtonDown += OnCropMouseDown;
        CropOverlay.MouseMove += OnCropMouseMove;
        CropOverlay.MouseLeftButtonUp += OnCropMouseUp;
        CropOverlay.LostMouseCapture += OnCropLostCapture;
        CropOverlay.SizeChanged += OnCropSurfaceResized;

        // Trim adjustment (SCRUM-11147): the press is taken in the tunnelling phase so a handle
        // is resolved by the same nearest-centre rule everywhere, never by whichever Thumb the
        // pointer happens to be over.
        CropOverlay.PreviewMouseLeftButtonDown += OnTrimPress;
        PreviewKeyDown += OnTrimEscape;
        foreach (Thumb handle in TrimHandles())
        {
            handle.KeyDown += OnTrimHandleKey;
        }

        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) =>
        {
            ObserveModel();
            FocusNewReview();
            if (_reviewTarget is not null &&
                ReferenceEquals(FocusManager.GetFocusedElement(FocusManager.GetFocusScope(this)), OperatorStatusPanel))
                OperatorStatusPanel.Focus();
        };
        Unloaded += OnUnloaded;
    }

    /// <summary>The view model, when there is one of the expected type.</summary>
    private SessionViewModel? Model => DataContext as SessionViewModel;

    // -----------------------------------------------------------------------------
    // Drag: down, move, up
    // -----------------------------------------------------------------------------

    private void OnCropMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Drawing a new rectangle is the unchanged fallback; a trim adjustment only moves handles.
        if (Model is not { IsCropping: true, IsAdjustingTrim: false })
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
        if (_trimGesture is not null)
        {
            MoveTrimHandle(e.GetPosition(CropOverlay));
            return;
        }

        if (_dragOrigin is not { } origin || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        DrawOutline(origin, e.GetPosition(CropOverlay));
    }

    private void OnCropMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_trimGesture is not null)
        {
            // The release position decides, exactly as the last move did; the gesture is cleared
            // before capture is released so the lost-capture handler does not undo it.
            MoveTrimHandle(e.GetPosition(CropOverlay));
            _trimGesture = null;
            CropOverlay.ReleaseMouseCapture();
            e.Handled = true;
            RedrawSelection();
            return;
        }

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

    /// <summary>
    /// A drag interrupted by anything else — an alt-tab, a dialog — simply ends. An interrupted
    /// handle drag also puts back the boundary it began with.
    /// </summary>
    private void OnCropLostCapture(object sender, MouseEventArgs e)
    {
        _dragOrigin = null;
        CancelTrimGesture();
    }

    // -----------------------------------------------------------------------------
    // Trim adjustment handles (SCRUM-11147)
    // -----------------------------------------------------------------------------

    /// <summary>The handle drag in progress, or null.</summary>
    private TrimHandleDrag? _trimGesture;

    /// <summary>The review target when the editor opened; decides where focus goes on close.</summary>
    private string? _reviewTargetAtTrimAdjust;

    private void OnTrimPress(object sender, MouseButtonEventArgs e)
    {
        if (Model is not { IsAdjustingTrim: true, IsComparingTrim: false, IsBusy: false, CropSelection: TrimBounds start } model ||
            e.ClickCount != 1 ||
            !CurrentLayout(model).TryToSurfaceRect(start, out double x, out double y, out double w, out double h))
        {
            return;
        }

        Point press = e.GetPosition(CropOverlay);
        if (CropHandleGesture.HitTest(x, y, w, h, press.X, press.Y, CropOverlay.ActualWidth, CropOverlay.ActualHeight) is not { } handle)
        {
            return;
        }

        _trimGesture = new TrimHandleDrag(handle, press.X, press.Y, start,
            SystemParameters.MinimumHorizontalDragDistance, SystemParameters.MinimumVerticalDragDistance);
        HandleFor(handle).Focus();
        CropOverlay.CaptureMouse();
        e.Handled = true;
    }

    /// <summary>
    /// Moves the dragged edges by the pointer's displacement since the press — never to the
    /// pointer itself, so grabbing a handle anywhere inside its square does not jump the edge.
    /// Below the system drag threshold nothing moves at all.
    /// </summary>
    private void MoveTrimHandle(Point pointer)
    {
        if (_trimGesture is not { } gesture || Model is not { } model ||
            gesture.Displacement(pointer.X, pointer.Y) is not { } delta)
        {
            return;
        }

        model.TryMoveTrimHandle(CurrentLayout(model), gesture.Start, gesture.Handle, delta.DeltaX, delta.DeltaY);
        RedrawSelection();
    }

    private void CancelTrimGesture()
    {
        if (_trimGesture is not { } gesture)
        {
            return;
        }

        _trimGesture = null;
        Model?.RestoreTrimDraft(gesture.Start);
        RedrawSelection();
    }

    private void OnTrimEscape(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || _trimGesture is null)
        {
            return;
        }

        CancelTrimGesture();
        CropOverlay.ReleaseMouseCapture();
        e.Handled = true;
    }

    /// <summary>
    /// Arrow keys move the focused handle's edges by one source pixel, Shift by ten, Ctrl to the
    /// picture border. Handled here so the surrounding scroll viewer does not scroll instead.
    /// </summary>
    private void OnTrimHandleKey(object sender, KeyEventArgs e)
    {
        if (sender is not Thumb { Tag: string tag } || !Enum.TryParse(tag, out CropHandle handle) || Model is not { } model ||
            !TrimHandleKeys.TryMap(e.Key, Keyboard.Modifiers, out int x, out int y, out bool bigStep, out bool toBorder))
        {
            return;
        }

        model.TryNudgeTrimHandle(handle, x, y, bigStep, toBorder);
        RedrawSelection();
        e.Handled = true;
    }

    private IEnumerable<Thumb> TrimHandles() =>
    [
        TrimHandleLeft, TrimHandleTop, TrimHandleRight, TrimHandleBottom,
        TrimHandleTopLeft, TrimHandleTopRight, TrimHandleBottomLeft, TrimHandleBottomRight,
    ];

    private Thumb HandleFor(CropHandle handle) => handle switch
    {
        CropHandle.Left => TrimHandleLeft,
        CropHandle.Top => TrimHandleTop,
        CropHandle.Right => TrimHandleRight,
        CropHandle.Bottom => TrimHandleBottom,
        CropHandle.TopLeft => TrimHandleTopLeft,
        CropHandle.TopRight => TrimHandleTopRight,
        CropHandle.BottomLeft => TrimHandleBottomLeft,
        _ => TrimHandleBottomRight,
    };

    /// <summary>
    /// Draws the adjustment: dimming outside the kept area within the picture, an inset
    /// two-tone outline, and the eight handles anchored inside the boundary.
    /// </summary>
    private void DrawTrimAdjustment(SessionViewModel model, TrimBounds bounds)
    {
        CropSurfaceLayout layout = CurrentLayout(model);
        if (!layout.TryToSurfaceRect(bounds, out double x, out double y, out double w, out double h))
        {
            HideTrimAdjustment();
            return;
        }

        double imageLeft = layout.LetterboxX, imageTop = layout.LetterboxY;
        double imageRight = imageLeft + layout.DisplayWidth, imageBottom = imageTop + layout.DisplayHeight;
        PlaceRect(TrimDimLeft, imageLeft, imageTop, x - imageLeft, imageBottom - imageTop);
        PlaceRect(TrimDimRight, x + w, imageTop, imageRight - (x + w), imageBottom - imageTop);
        PlaceRect(TrimDimTop, x, imageTop, w, y - imageTop);
        PlaceRect(TrimDimBottom, x, y + h, w, imageBottom - (y + h));
        PlaceRect(TrimOutlineLight, x, y, w, h);
        PlaceRect(TrimOutlineDark, x + 0.5, y + 0.5, w - 1, h - 1);

        foreach (CropHandle handle in Enum.GetValues<CropHandle>())
        {
            Thumb thumb = HandleFor(handle);
            // Kept whole inside the surface: on a boundary narrower than a handle the
            // inside-anchored centres cross, and a handle on the picture border must still show.
            // The press is hit-tested against these same centres.
            (double cx, double cy) = CropHandleGesture.Centre(x, y, w, h, handle, CropOverlay.ActualWidth, CropOverlay.ActualHeight);
            Canvas.SetLeft(thumb, cx - (CropHandleGesture.HandleSize / 2));
            Canvas.SetTop(thumb, cy - (CropHandleGesture.HandleSize / 2));
            AutomationProperties.SetName(thumb, SessionViewModel.TrimHandleName(handle));
            thumb.Visibility = Visibility.Visible;
        }
    }

    private void HideTrimAdjustment()
    {
        foreach (Rectangle part in new[] { TrimDimLeft, TrimDimTop, TrimDimRight, TrimDimBottom, TrimOutlineLight, TrimOutlineDark })
        {
            part.Visibility = Visibility.Collapsed;
        }

        foreach (Thumb thumb in TrimHandles())
        {
            thumb.Visibility = Visibility.Collapsed;
        }
    }

    private static void PlaceRect(FrameworkElement element, double x, double y, double width, double height)
    {
        if (width <= 0 || height <= 0)
        {
            element.Visibility = Visibility.Collapsed;
            return;
        }

        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        element.Width = width;
        element.Height = height;
        element.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Where focus goes when the trim editor opens or closes. Opening puts it on the editor's
    /// heading, never on Use this trim, so a key still held from the button that opened it cannot
    /// submit anything. Closing on the same review (Cancel, or an adjustment that can no longer
    /// be made) puts it back on Adjust trim edges, or on the non-activating status panel when that
    /// is no longer offered; a new review is placed by the existing new-review rule instead.
    /// </summary>
    private void PlaceTrimAdjustFocus(bool opened)
    {
        if (Model is not { } model)
        {
            return;
        }

        if (opened)
        {
            _reviewTargetAtTrimAdjust = model.ReviewTargetIdentity;
            PlaceFocus(TrimAdjustHeadingText);
            return;
        }

        if (model.ReviewTargetIdentity is { } target && target == _reviewTargetAtTrimAdjust)
        {
            PlaceFocus(model.CanAdjustTrim ? BeginTrimAdjustButton : OperatorStatusPanel);
        }

        _reviewTargetAtTrimAdjust = null;
    }

    /// <summary>Logical focus now, keyboard focus once the element has been laid out.</summary>
    private void PlaceFocus(FrameworkElement element)
    {
        FocusManager.SetFocusedElement(FocusManager.GetFocusScope(this), element);
        if (IsLoaded)
        {
            Dispatcher.BeginInvoke(() => element.Focus(), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    // -----------------------------------------------------------------------------
    // Keeping the outline over the same artwork
    // -----------------------------------------------------------------------------

    private void OnCropSurfaceResized(object sender, SizeChangedEventArgs e) => RedrawSelection();

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _reviewTarget = null;
        ObserveModel();
        FocusNewReview();
        RedrawSelection();
    }

    private void ObserveModel()
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

    }

    private void FocusNewReview()
    {
        string? target = Model?.ReviewTargetIdentity;
        DependencyObject scope = FocusManager.GetFocusScope(this);
        if (target == _reviewTarget && ReferenceEquals(scope, _reviewFocusScope)) return;
        _reviewTarget = target;
        _reviewFocusScope = scope;
        if (target is null) return;
        // Set logical focus immediately; apply keyboard focus when the view is loaded.
        // Attachment can move a pre-bound view into a Window's scope. Carry the initial
        // landing across that boundary, while preserving valid focus within the same scope.
        FocusManager.SetFocusedElement(scope, OperatorStatusPanel);
        if (IsLoaded) OperatorStatusPanel.Focus();
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
        if (e.PropertyName == nameof(SessionViewModel.NoticeErrorCode) && Model?.HasNoticeErrorCode == true)
        {
            // A failure from a lower control must still be visible. Only move this scrolling
            // column, after binding/layout; never move focus or invoke an operator command.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (Model?.HasNoticeErrorCode == true) SessionDetailsScroll.ScrollToTop();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        if (e.PropertyName is nameof(SessionViewModel.ReviewTargetIdentity) or "" or null)
            FocusNewReview();

        // Colleague correction (SCRUM-11148): Ask moves focus to the note, Cancel back to Ask, a
        // finished preparation to the non-activating handed-off heading, and a closed picker or a
        // refusal to Import. Never to Prepare or Import from a key still held elsewhere.
        if (e.PropertyName == nameof(SessionViewModel.CorrectionFocusToken) && Model is { } correcting)
        {
            FrameworkElement? target = correcting.CorrectionFocusTarget switch
            {
                CorrectionFocus.NoteBox => CorrectionNoteBox,
                CorrectionFocus.AskButton => AskColleagueButton,
                CorrectionFocus.HandedOffHeading => CorrectionHeadingText,
                CorrectionFocus.ImportButton => ImportCorrectedButton,
                _ => null,
            };
            if (target is not null) PlaceFocus(target);
        }

        if (e.PropertyName == nameof(SessionViewModel.IsAdjustingTrim))
        {
            bool opened = Model is { IsAdjustingTrim: true };
            if (!opened) CancelTrimGesture();
            PlaceTrimAdjustFocus(opened);
        }

        if (e.PropertyName is
            nameof(SessionViewModel.CropSelection) or
            nameof(SessionViewModel.CropAppliedBounds) or
            nameof(SessionViewModel.IsCropping) or
            nameof(SessionViewModel.IsAdjustingTrim) or
            nameof(SessionViewModel.IsComparingTrim) or
            nameof(SessionViewModel.CropPane) or
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
        CropAppliedOutline.Visibility = Visibility.Collapsed;
        if (Model is { IsAdjustingTrim: true, IsCropping: true, CropSelection: TrimBounds kept } adjusting)
        {
            CropSelectionOutline.Visibility = Visibility.Collapsed;
            DrawTrimAdjustment(adjusting, kept);
            return;
        }

        HideTrimAdjustment();
        if (Model is not { IsCropping: true, CropSelection: TrimBounds bounds } model ||
            !CurrentLayout(model).TryToSurfaceRect(bounds, out double x, out double y, out double w, out double h))
        {
            CropSelectionOutline.Visibility = Visibility.Collapsed;
            return;
        }

        Place(x, y, w, h);
        if (model.CropAppliedBounds is { } applied &&
            CurrentLayout(model).TryToSurfaceRect(applied, out x, out y, out w, out h))
        {
            Canvas.SetLeft(CropAppliedOutline, x);
            Canvas.SetTop(CropAppliedOutline, y);
            CropAppliedOutline.Width = w;
            CropAppliedOutline.Height = h;
            CropAppliedOutline.Visibility = Visibility.Visible;
        }
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
