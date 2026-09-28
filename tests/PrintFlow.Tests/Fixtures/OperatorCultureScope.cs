using System.Globalization;
using System.Reflection;
using PrintFlow.App.Resources;

namespace PrintFlow.Tests.Fixtures;

/// <summary>
/// Selects an operator language for one test and puts back exactly what it found: the raw
/// operator selection (normally none), the process-wide default UI culture and the calling
/// thread's UI culture.
/// </summary>
/// <remarks>
/// Restoring <see cref="OperatorCulture.Current"/> would be wrong: with nothing selected it is the
/// ambient fallback, and selecting it turns "follow the ambient culture" into a fixed language
/// that every later test in the collection inherits. <c>LocalisationService.Use</c> also sets
/// <see cref="CultureInfo.DefaultThreadCurrentUICulture"/>, which reaches every fresh render
/// thread.
/// </remarks>
internal sealed class OperatorCultureScope : IDisposable
{
    private static readonly FieldInfo SelectedField =
        typeof(OperatorCulture).GetField("_selected", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("OperatorCulture no longer keeps its selection in _selected.");

    private readonly CultureInfo? _selected = RawSelection;
    private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;
    private readonly CultureInfo? _defaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;

    /// <summary>Captures the current state without selecting a language.</summary>
    public OperatorCultureScope()
    {
    }

    public OperatorCultureScope(string language) => OperatorCulture.Select(CultureInfo.GetCultureInfo(language));

    /// <summary>The raw operator selection; null means strings follow the ambient UI culture.</summary>
    internal static CultureInfo? RawSelection => (CultureInfo?)SelectedField.GetValue(null);

    public void Dispose()
    {
        OperatorCulture.Select(_selected);
        CultureInfo.DefaultThreadCurrentUICulture = _defaultUiCulture;
        CultureInfo.CurrentUICulture = _uiCulture;
    }
}
