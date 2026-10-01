using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using PrintFlow.App.ViewModels;

namespace PrintFlow.App.Views;

/// <summary>
/// Home. The code-behind exists only for drag-and-drop, which WPF exposes as events rather
/// than as bindable state, and for one layout proportion XAML cannot express.
/// </summary>
/// <remarks>
/// It reads the dropped paths and hands them straight to the view model's command; it makes no
/// decision about them. In particular the "exactly one file" rule is not applied here — the
/// view model applies it, so the rule is testable without a window (Part 3C2 §4, §15).
/// </remarks>
public partial class HomeView : UserControl
{
    /// <summary>
    /// The most of the screen the part above the lists may take before it scrolls on its own
    /// (SCRUM-11152).
    /// </summary>
    /// <remarks>
    /// With every disclosure open, the readiness summary, startup warnings, import box and a
    /// failure notice together outgrow a 1000×700 window, and the Recovery list below would be
    /// squeezed to nothing. Capping the upper part keeps both halves reachable by scrolling.
    /// Layout only: it decides nothing and binds nothing.
    /// </remarks>
    private const double UpperShare = 0.65;

    private HomeViewModel? _subscribedHome;
    private EventHandler? _observationChanged;
    private long _subscription;
    private bool _observing;
    private UIElement? _abandonOrigin;

    public HomeView()
    {
        InitializeComponent();
        Loaded += (_, _) => { _observing = true; Subscribe(); };
        Unloaded += (_, _) => { _observing = false; Unsubscribe(); };
        DataContextChanged += (_, _) => { Unsubscribe(); if (_observing) Subscribe(); };
    }

    private void Subscribe()
    {
        Unsubscribe();
        if (DataContext is not HomeViewModel home) return;
        _subscribedHome = home;
        long subscription = _subscription;
        _observationChanged = (_, _) =>
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_subscription == subscription && ReferenceEquals(_subscribedHome, home))
                    home.ReadReadiness();
            }));
        };
        home.ReadinessObservations.Changed += _observationChanged;
        home.PropertyChanged += OnHomeChanged;
        home.ReadReadiness();
    }

    /// <summary>
    /// Focus for the Abandon confirmation (SCRUM-11154 F-V4): it moves to "Keep the job" when the
    /// confirmation opens, so Enter or a held key can only keep the job, and returns to the button
    /// that opened it when the operator keeps the job. Focus only; it decides nothing.
    /// </summary>
    private void OnHomeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(HomeViewModel.IsConfirmingAbandon) || sender is not HomeViewModel home) return;
        if (home.IsConfirmingAbandon)
        {
            _abandonOrigin = Keyboard.FocusedElement as UIElement;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => AbandonKeepButton.Focus()));
            return;
        }
        UIElement? origin = _abandonOrigin;
        _abandonOrigin = null;
        if (origin is not null)
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { if (origin.IsVisible) origin.Focus(); }));
    }

    private void Unsubscribe()
    {
        ++_subscription;
        if (_subscribedHome is { } home && _observationChanged is { } handler)
            home.ReadinessObservations.Changed -= handler;
        if (_subscribedHome is { } subscribed) subscribed.PropertyChanged -= OnHomeChanged;
        _subscribedHome = null;
        _observationChanged = null;
    }

    private void OnLayoutSizeChanged(object sender, SizeChangedEventArgs e) =>
        Upper.MaxHeight = e.NewSize.Height * UpperShare;

    private static string[] DroppedPaths(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop)
            ? (string[])e.Data.GetData(DataFormats.FileDrop)!
            : [];

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;

        if (DataContext is not HomeViewModel home)
        {
            return;
        }

        // Every dropped path is passed on, including the ones a multi-file drop produced:
        // the refusal has to be able to say how many arrived.
        home.DropFilesCommand.Execute(DroppedPaths(e));
    }
}
