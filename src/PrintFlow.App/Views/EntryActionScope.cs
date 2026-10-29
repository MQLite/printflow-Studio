using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace PrintFlow.App.Views;

/// <summary>Reuses the session gesture boundary for Home and route selection.</summary>
internal sealed class EntryActionScope
{
    private readonly UserControl _view;
    private INotifyPropertyChanged? _model;
    private bool _active = true;

    internal EntryActionScope(UserControl view)
    {
        _view = view;
        SessionActionScope.SetActive(view, true);
        view.DataContextChanged += (_, _) => Observe();
        view.Loaded += (_, _) => { _active = true; Observe(); };
        view.Unloaded += (_, _) => { _active = false; Unsubscribe(); Invalidate(); };
        Observe();
    }

    private void Observe()
    {
        Unsubscribe();
        if (_active && _view.DataContext is INotifyPropertyChanged model)
        {
            _model = model;
            model.PropertyChanged += Changed;
        }
        Invalidate();
    }

    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "IsBusy" or "IsConfirmingAbandon" or "InteractionIdentity" or "CanStartImport")
            Invalidate();
    }

    private void Invalidate() => SessionActionScope.SetActive(_view, _active);
    private void Unsubscribe()
    {
        if (_model is not null) _model.PropertyChanged -= Changed;
        _model = null;
    }
}
