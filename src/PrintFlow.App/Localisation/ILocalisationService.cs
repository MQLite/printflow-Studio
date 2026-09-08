namespace PrintFlow.App.Localisation;

/// <summary>
/// The one authority for which language the operator interface is currently in
/// (SCRUM-11119).
/// </summary>
/// <remarks>
/// <b>One authority, not a per-screen habit.</b> Every operator-visible string already resolves
/// through <c>Strings</c>, which reads <see cref="System.Globalization.CultureInfo.CurrentUICulture"/>
/// at the moment it is asked. So a language change is one assignment plus one notification, and
/// no screen re-binds a label by hand: a view model refreshes all of its bindings at once when
/// <see cref="LanguageChanged"/> is raised, and every screen opened afterwards is constructed
/// in the new language because navigation resolves a fresh view model each visit.
/// <para>
/// <b>It applies; it does not commit.</b> <see cref="Use"/> changes what the operator sees and
/// nothing else. Persisting the choice belongs to the Settings screen's single transactional
/// save, so a failed write can never leave the visible language disagreeing with the language
/// the database says is authoritative (SCRUM-11118).
/// </para>
/// <para>
/// <b>The default is the Product's, not Windows'.</b> <see cref="RestoreAsync"/> answers an
/// absent, unreadable or unrecognised persisted value with
/// <see cref="OperatorLanguages.FirstRunDefault"/>. Nothing here reads the operating system's
/// UI culture.
/// </para>
/// </remarks>
public interface ILocalisationService
{
    /// <summary>The language the interface is in right now.</summary>
    OperatorLanguage Current { get; }

    /// <summary>Raised after <see cref="Current"/> changes, so open screens can refresh.</summary>
    event EventHandler? LanguageChanged;

    /// <summary>
    /// Applies the persisted choice, or the Product's first-run default when there is none.
    /// </summary>
    /// <remarks>
    /// Called once, during startup, before the shell is shown. A persistence failure is answered
    /// with the default rather than with a refusal: a language preference is not a reason to
    /// stop a production workstation from opening.
    /// </remarks>
    Task<OperatorLanguage> RestoreAsync(CancellationToken cancellationToken);

    /// <summary>Switches the interface to <paramref name="language"/> immediately.</summary>
    void Use(OperatorLanguage language);
}
