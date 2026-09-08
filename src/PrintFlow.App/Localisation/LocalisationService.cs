using System.Globalization;
using PrintFlow.App.Resources;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Settings;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.App.Localisation;

/// <summary>
/// The application-wide <see cref="ILocalisationService"/> (SCRUM-11119).
/// </summary>
/// <remarks>
/// A singleton, because "which language is the interface in" is one fact per process, and the
/// culture it sets is process-wide state.
/// <para>
/// Two culture fields are written together and deliberately:
/// <see cref="CultureInfo.CurrentUICulture"/> so the thread that made the change sees it at
/// once, and <see cref="CultureInfo.DefaultThreadCurrentUICulture"/> so every thread started
/// afterwards — the background readings the readiness and Settings screens take, for one — sees
/// the same answer. <see cref="CultureInfo.CurrentCulture"/> is <b>not</b> touched: number,
/// date and path formatting are properties of the workstation's regional configuration, and
/// SCRUM-11119 asks for the interface language, not for a locale change.
/// </para>
/// </remarks>
public sealed class LocalisationService : ILocalisationService
{
    private readonly ISettingsRepository _settings;

    public LocalisationService(ISettingsRepository settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings;
        Current = OperatorLanguages.FirstRunDefault;
    }

    /// <inheritdoc />
    public OperatorLanguage Current { get; private set; }

    /// <inheritdoc />
    public event EventHandler? LanguageChanged;

    /// <inheritdoc />
    public async Task<OperatorLanguage> RestoreAsync(CancellationToken cancellationToken)
    {
        OperationResult<SettingEntry?> persisted =
            await _settings.ReadAsync(SettingKey.UiLanguage, cancellationToken).ConfigureAwait(false);

        // Absent, unreadable and unrecognised all get the same answer, because they are the same
        // situation from the operator's side: nobody has chosen, so the Product default stands.
        OperatorLanguage language = persisted.IsSuccess
            ? OperatorLanguages.FromCultureName(persisted.Value?.Value) ?? OperatorLanguages.FirstRunDefault
            : OperatorLanguages.FirstRunDefault;

        Apply(language);
        return language;
    }

    /// <inheritdoc />
    public void Use(OperatorLanguage language)
    {
        if (language == Current)
        {
            // Still re-applies the culture — a fresh process has Current at the default before
            // anything has been applied — but raises nothing, so a redundant Apply on the
            // Settings screen does not churn every binding in the shell.
            Apply(language);
            return;
        }

        Apply(language);
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Apply(OperatorLanguage language)
    {
        CultureInfo culture = OperatorLanguages.CultureOf(language);

        // The authoritative one: an explicit selection, held in one place, unaffected by which
        // thread or async continuation this ran on. Everything the operator reads resolves
        // through it.
        OperatorCulture.Select(culture);

        // And the ambient ones, kept in step so that anything outside PrintFlow's own resources
        // — a framework message, a WPF default — agrees with what the operator sees. They are
        // set as well as, never instead of, the selection above: CurrentUICulture is carried by
        // ExecutionContext and is restored to its previous value when an async continuation
        // unwinds, which is exactly the boundary a Settings Apply crosses.
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;

        Current = language;
    }
}
