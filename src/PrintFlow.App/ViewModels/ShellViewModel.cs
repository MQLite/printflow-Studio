using CommunityToolkit.Mvvm.ComponentModel;
using PrintFlow.App.Localisation;
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
/// <para>
/// The one thing it does listen for is a language change, because the window title is the only
/// operator-visible string that belongs to the shell rather than to a screen. It refreshes that
/// title and nothing else; every screen either refreshes itself or is constructed fresh by the
/// next navigation, already in the new language (SCRUM-11119).
/// </para>
/// </remarks>
public sealed class ShellViewModel : ObservableObject, IDisposable
{
    private readonly INavigationService _navigation;
    private readonly ILocalisationService _localisation;

    public ShellViewModel(INavigationService navigation, ILocalisationService localisation)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(localisation);

        _navigation = navigation;
        _localisation = localisation;
        _navigation.CurrentChanged += OnCurrentChanged;
        _localisation.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>The window title.</summary>
    public string Title => Strings.App_Title;

    /// <summary>The screen currently shown, resolved to a view by the shell's DataTemplates.</summary>
    public object? Current => _navigation.Current;

    public void Dispose()
    {
        _navigation.CurrentChanged -= OnCurrentChanged;
        _localisation.LanguageChanged -= OnLanguageChanged;
    }

    private void OnCurrentChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(Current));

    private void OnLanguageChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(Title));
}
