using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PrintFlow.App.ViewModels;

namespace PrintFlow.App.Views;

/// <summary>Bound commands own decisions; context changes focus a non-activating heading.</summary>
public partial class WorkflowSelectionView : UserControl
{
    private WorkflowSelectionViewModel? _observed;
    private bool _active = true;

    public WorkflowSelectionView()
    {
        InitializeComponent();
        _ = new EntryActionScope(this);
        DataContextChanged += (_, _) => Observe();
        Loaded += (_, _) => { _active = true; Observe(); };
        Unloaded += (_, _) => { _active = false; Unsubscribe(); };
    }

    private void Observe()
    {
        Unsubscribe();
        if (!_active || DataContext is not WorkflowSelectionViewModel model) return;
        _observed = model;
        model.PropertyChanged += OnModelChanged;
        FocusHeading();
    }

    private void Unsubscribe()
    {
        if (_observed is not null) _observed.PropertyChanged -= OnModelChanged;
        _observed = null;
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(WorkflowSelectionViewModel.InteractionIdentity)) FocusHeading();
    }

    private void FocusHeading()
    {
        if (!_active || _observed is null) return;
        FrameworkElement target = _observed.IsConfirmingDraftLeave ? DraftLeaveHeadingText : RouteHeading;
        FocusManager.SetFocusedElement(FocusManager.GetFocusScope(this), target);
        if (!IsLoaded) return;
        var context = SessionActionScope.State(this);
        Dispatcher.BeginInvoke(() =>
        {
            if (_active && ReferenceEquals(context, SessionActionScope.State(this)) && SessionActionScope.CanInteract(target)) target.Focus();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }
}
