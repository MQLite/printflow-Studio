using CommunityToolkit.Mvvm.ComponentModel;
using PrintFlow.App.Navigation;
using PrintFlow.App.Resources;

namespace PrintFlow.App.ViewModels;

/// <summary>
/// The window's own view model: it holds whichever screen is current and nothing else
/// (Epic 11100 Part 3C2 §16).
/// </summary>
/// <remarks>
/// It owns no session, issues no command and reads no status. Keeping it empty is what stops
/// the shell from becoming the place where screens quietly start talking to each other —
/// everything they need arrives through <see cref="INavigationService"/>.
/// </remarks>
public sealed class ShellViewModel : ObservableObject, IDisposable
{
    private readonly INavigationService _navigation;

    public ShellViewModel(INavigationService navigation)
    {
        ArgumentNullException.ThrowIfNull(navigation);

        _navigation = navigation;
        _navigation.CurrentChanged += OnCurrentChanged;
    }

    /// <summary>The window title.</summary>
    public string Title => Strings.App_Title;

    /// <summary>The screen currently shown, resolved to a view by the shell's DataTemplates.</summary>
    public object? Current => _navigation.Current;

    public void Dispose() => _navigation.CurrentChanged -= OnCurrentChanged;

    private void OnCurrentChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(Current));
}
