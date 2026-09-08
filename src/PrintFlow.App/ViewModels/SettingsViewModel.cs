using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrintFlow.App.Localisation;
using PrintFlow.App.Navigation;
using PrintFlow.App.Resources;
using PrintFlow.App.Settings;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Settings;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.App.ViewModels;

/// <summary>
/// One operator language, offered by its own name (SCRUM-11119).
/// </summary>
/// <remarks>
/// <see cref="DisplayName"/> is an endonym and is deliberately not a resource: a language list
/// that translated itself would show a Chinese-only operator the word "Chinese" in English, or
/// hide "English" behind an unfamiliar translation from someone looking for it. Every product
/// that offers a language choice writes each language in that language, and so does this one.
/// </remarks>
public sealed record LanguageOption(OperatorLanguage Language, string DisplayName)
{
    internal static LanguageOption For(OperatorLanguage language) => language switch
    {
        OperatorLanguage.SimplifiedChinese => new LanguageOption(language, "简体中文"),
        OperatorLanguage.English => new LanguageOption(language, "English"),
        _ => throw new ArgumentOutOfRangeException(
            nameof(language), language, "Unknown operator language."),
    };

    /// <summary>The stable English culture name, for an AutomationId and for support.</summary>
    public string CultureName => OperatorLanguages.CultureNameOf(Language);
}

/// <summary>
/// Settings: the few operator preferences this MVP has, and the production facts it is honest
/// about not letting anyone edit (SCRUM-11118, SCRUM-11119).
/// </summary>
/// <remarks>
/// <b>Two kinds of row, and the difference is the whole design.</b> Language, the default trim
/// safety margin and local log retention are <i>operator preferences</i>: they are persisted
/// through <see cref="ISettingsRepository"/>, the persisted row wins over
/// <c>appsettings.json</c> and over the Product constant, and the operator may change them.
/// Production resolution, the workstation preset, the accepted output location and the
/// Photoshop colour setup are <i>facts of the verified production preset</i>: they are read
/// from the environment verification authority at display time, they are shown read-only, and
/// no <c>Setting</c> row is ever written for them. A screen that let someone type a different
/// production DPI would be a screen that could make PrintFlow lie about what it printed.
/// <para>
/// <b>Save is one transaction.</b> The screen presents a single Apply, so it writes a single
/// <see cref="ISettingsRepository.UpsertAsync"/> batch: the three preferences commit together
/// or not at all. Nothing is applied to the running application until that batch has committed,
/// which is what keeps the visible language and the persisted language from ever disagreeing.
/// </para>
/// <para>
/// <b>Effects, per row.</b> Language is immediate and needs no restart. The trim default is
/// prospective — it is the margin a <i>newly imported</i> job starts with, and it never rewrites
/// a job already under way. Log retention is persisted configuration in this slice; the cleanup
/// that enforces it is SCRUM-11121 and is not implemented here, so nothing on this screen claims
/// that files are being deleted.
/// </para>
/// <para>
/// <b>It repairs nothing.</b> There is no button here that changes a Photoshop colour space,
/// rewrites the preset or touches the workstation. Where a live verification is what the
/// operator actually wants, this screen links to the Production Readiness screen that already
/// owns it rather than growing a second copy of it (Epic 11500 Part C §3).
/// </para>
/// </remarks>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsRepository _settings;
    private readonly ILocalisationService _localisation;
    private readonly IEnvironmentDiagnostics _diagnostics;
    private readonly INavigationService _navigation;
    private readonly SettingsDefaults _defaults;

    private EnvironmentReadinessReport? _report;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>The one bounded sentence this screen ever says about a save.</summary>
    [ObservableProperty]
    private string? _notice;

    [ObservableProperty]
    private LanguageOption _selectedLanguage = LanguageOption.For(OperatorLanguages.FirstRunDefault);

    [ObservableProperty]
    private string _trimSafetyMarginPixels =
        SettingsDefaults.FallbackTrimSafetyMarginPixels.ToString(CultureInfo.InvariantCulture);

    [ObservableProperty]
    private string _logRetentionDays =
        SettingsDefaults.FallbackLogRetentionDays.ToString(CultureInfo.InvariantCulture);

    public SettingsViewModel(
        ISettingsRepository settings,
        ILocalisationService localisation,
        IEnvironmentDiagnostics diagnostics,
        INavigationService navigation,
        SettingsDefaults defaults)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(localisation);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(defaults);

        _settings = settings;
        _localisation = localisation;
        _diagnostics = diagnostics;
        _navigation = navigation;
        _defaults = defaults;

        foreach (OperatorLanguage language in OperatorLanguages.All)
        {
            Languages.Add(LanguageOption.For(language));
        }

        SelectedLanguage = OptionFor(_localisation.Current);
        LogRetentionDays = ConfiguredRetentionDays().ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The supported languages, each written in its own language.</summary>
    public ObservableCollection<LanguageOption> Languages { get; } = [];

    // ----------------------------------------------------------------- labels

    public string Heading => Strings.Settings_Heading;

    public string GeneralHeading => Strings.Settings_GeneralHeading;

    public string ProductionHeading => Strings.Settings_ProductionHeading;

    public string ProductionHint => Strings.Settings_ProductionHint;

    public string DiagnosticsHeading => Strings.Settings_DiagnosticsHeading;

    public string LanguageLabel => Strings.Settings_Language;

    public string OutputRootLabel => Strings.Settings_OutputRoot;

    public string OutputRootHint => Strings.Settings_OutputRootHint;

    public string TrimMarginLabel => Strings.Settings_TrimMargin;

    public string TrimMarginHint => Strings.Settings_TrimMarginHint;

    public string ProductionDpiLabel => Strings.Settings_ProductionDpi;

    public string PresetLabel => Strings.Settings_Preset;

    public string ColourSetupLabel => Strings.Settings_ColourSetup;

    public string ColourSetupHint => Strings.Settings_ColourSetupHint;

    public string LogRetentionLabel => Strings.Settings_LogRetention;

    public string LogRetentionHint => Strings.Settings_LogRetentionHint;

    public string ApplyLabel => Strings.Settings_Apply;

    public string BackLabel => Strings.Nav_BackToHome;

    public string OpenEnvironmentLabel => Strings.Environment_Open;

    // ------------------------------------------------- read-only production facts

    /// <summary>
    /// The fixed production resolution, from the Product's own constant (MVP design §8.3).
    /// </summary>
    /// <remarks>
    /// Displayed, never stored. <see cref="PrintDimensions.ProductionDpi"/> is what every
    /// dimension calculation, every Photoshop preparation and every TIFF validation in this
    /// solution already uses; a second copy in a <c>Setting</c> row would be a second number
    /// able to disagree with the files PrintFlow actually produces.
    /// </remarks>
    public string ProductionDpi => string.Format(
        CultureInfo.CurrentCulture, Strings.Settings_ProductionDpiValue, PrintDimensions.ProductionDpi);

    /// <summary>The accepted preset's id, version and short digest, or a plain "not stated".</summary>
    public string Preset => _report?.PresetIdentity ?? Strings.Environment_PresetUnavailable;

    /// <summary>
    /// The output location the verified preset accepts.
    /// </summary>
    /// <remarks>
    /// Read from the readiness report's own named fact rather than from configuration or from a
    /// check this screen picked out by name, so it shows the location that was actually verified
    /// rather than the one this installation was merely pointed at — and so the shell names no
    /// part of the verification vocabulary (Epic 11500 Part B §11.10). When the preset states
    /// nothing, it says so instead of showing a path nobody has checked.
    /// </remarks>
    public string OutputRoot => string.IsNullOrWhiteSpace(_report?.AcceptedOutputRoot)
        ? Strings.Settings_OutputRootUnavailable
        : _report.AcceptedOutputRoot;

    /// <summary>
    /// Confirmed, a mismatch, or not checked — never a guess.
    /// </summary>
    /// <remarks>
    /// The colour setup is verified by the explicit live application phase, so a passive reading
    /// usually states nothing about it. That case is reported as "not checked yet", which is the
    /// truth, rather than as a confirmation this screen has no evidence for (SCRUM-11110).
    /// </remarks>
    public string ColourSetupStatus => _report?.PhotoshopColourSetup switch
    {
        null => Strings.Settings_ColourNotVerified,
        EnvironmentCheckStatus.Passed => Strings.Settings_ColourConfirmed,
        EnvironmentCheckStatus.Blocked => Strings.Settings_ColourNotVerified,
        _ => Strings.Settings_ColourMismatch,
    };

    /// <summary>True once a reading exists, so the screen shows nothing it has not read.</summary>
    public bool HasReport => _report is not null;

    // ----------------------------------------------------------------- behaviour

    /// <summary>Loads the persisted preferences and takes one passive production reading.</summary>
    public async Task OpenAsync(CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            SelectedLanguage = OptionFor(_localisation.Current);

            OperationResult<IReadOnlyList<SettingEntry>> persisted =
                await _settings.ReadAllAsync(cancellationToken).ConfigureAwait(true);

            if (persisted.IsSuccess)
            {
                ApplyPersisted(persisted.Value);
            }
            else
            {
                // The screen still opens: the production facts below are worth showing even
                // when the preferences above could not be read, and the operator is told which
                // half is missing rather than shown stale values as though they were current.
                Notice = Describe(Strings.Settings_LoadFailed, persisted.Failure);
            }

            // Off the UI thread for the same reason Production Readiness reads off it: the
            // first reading of a run hashes the accepted installations and the evidence chain.
            Apply(await Task.Run(_diagnostics.Read, cancellationToken).ConfigureAwait(true));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Commits every editable preference as one transaction, then applies what committed.
    /// </summary>
    /// <remarks>
    /// The order is the point. Both fields are validated before anything is written, so an
    /// unusable entry costs nothing; the batch is written as one
    /// <see cref="ISettingsRepository.UpsertAsync"/> call, so a failure leaves the database
    /// exactly as it was; and the language is switched only <i>after</i> the commit succeeded,
    /// so the interface can never be showing a language the database does not hold.
    /// <para>
    /// All three preferences are written every time rather than only the changed ones. One
    /// Apply means "these are my settings", and a diffing rule would make the answer to "what
    /// did Apply write" depend on state the operator cannot see.
    /// </para>
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync(CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        if (!TryReadPixels(TrimSafetyMarginPixels, out int marginPixels))
        {
            Notice = Strings.Settings_TrimMarginInvalid;
            return;
        }

        if (!TryReadRetentionDays(LogRetentionDays, out int retentionDays))
        {
            Notice = string.Format(
                CultureInfo.CurrentCulture,
                Strings.Settings_LogRetentionInvalid,
                SettingsDefaults.MaximumLogRetentionDays);
            return;
        }

        OperatorLanguage language = SelectedLanguage.Language;

        IsBusy = true;
        try
        {
            OperationResult<Unit> saved = await _settings.UpsertAsync(
                [
                    SettingEntry.Text(SettingKey.UiLanguage, OperatorLanguages.CultureNameOf(language)),
                    SettingEntry.Integer(SettingKey.TrimSafetyMarginPixels, marginPixels),
                    SettingEntry.Integer(SettingKey.LogRetentionDays, retentionDays),
                ],
                cancellationToken).ConfigureAwait(true);

            if (saved.IsFailure)
            {
                // Nothing committed, so nothing is applied: the operator stays here, in the
                // language the database still says is authoritative, with their entries intact.
                Notice = Describe(Strings.Settings_SaveFailed, saved.Failure);
                return;
            }

            _localisation.Use(language);

            // Re-displayed from the parsed values, so what the screen shows afterwards is what
            // was actually stored rather than what was typed.
            TrimSafetyMarginPixels = marginPixels.ToString(CultureInfo.InvariantCulture);
            LogRetentionDays = retentionDays.ToString(CultureInfo.InvariantCulture);

            RefreshLocalisedText();

            // After the refresh, so the confirmation is read in the language just chosen.
            Notice = Strings.Settings_Saved;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Opens the screen that owns live workstation verification (Epic 11500 Part C §3).</summary>
    [RelayCommand]
    private async Task OpenEnvironmentAsync(CancellationToken cancellationToken) =>
        await _navigation.GoToEnvironmentReadinessAsync(cancellationToken).ConfigureAwait(true);

    [RelayCommand]
    private async Task BackToHomeAsync(CancellationToken cancellationToken) =>
        await _navigation.GoHomeAsync(cancellationToken).ConfigureAwait(true);

    private bool CanApply() => !IsBusy;

    partial void OnIsBusyChanged(bool value) => ApplyCommand.NotifyCanExecuteChanged();

    private void ApplyPersisted(IReadOnlyList<SettingEntry> entries)
    {
        // Absent is not an error and never overwrites a default with a blank: each value keeps
        // the fallback it was constructed with — appsettings.json, then the Product constant.
        foreach (SettingEntry entry in entries)
        {
            switch (entry.Key)
            {
                case SettingKey.TrimSafetyMarginPixels when entry.AsInteger() is { } pixels && pixels >= 0:
                    TrimSafetyMarginPixels = pixels.ToString(CultureInfo.InvariantCulture);
                    break;

                case SettingKey.LogRetentionDays when entry.AsInteger() is { } days && days >= 1:
                    LogRetentionDays = days.ToString(CultureInfo.InvariantCulture);
                    break;

                default:
                    break;
            }
        }
    }

    private int ConfiguredRetentionDays() =>
        _defaults.LogRetentionDays is >= 1 and <= SettingsDefaults.MaximumLogRetentionDays
            ? _defaults.LogRetentionDays
            : SettingsDefaults.FallbackLogRetentionDays;

    private void Apply(EnvironmentReadinessReport report)
    {
        _report = report;

        OnPropertyChanged(nameof(HasReport));
        OnPropertyChanged(nameof(Preset));
        OnPropertyChanged(nameof(OutputRoot));
        OnPropertyChanged(nameof(ColourSetupStatus));
    }


    private LanguageOption OptionFor(OperatorLanguage language) =>
        Languages.First(option => option.Language == language);

    /// <summary>
    /// Re-reads every localised string on this screen at once.
    /// </summary>
    /// <remarks>
    /// One notification, not a list of labels: <c>Strings</c> resolves against the current UI
    /// culture on each read, so telling WPF that every binding on this view model is stale is
    /// the whole of a language switch on the visible screen. Every other screen is constructed
    /// fresh by navigation and is therefore already in the new language.
    /// </remarks>
    private void RefreshLocalisedText() => OnPropertyChanged(string.Empty);

    /// <summary>A whole, non-negative pixel count, in the operator's own number format.</summary>
    private static bool TryReadPixels(string? text, out int pixels) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out pixels) && pixels >= 0;

    /// <summary>A whole, bounded number of days. Zero would mean "delete immediately".</summary>
    private static bool TryReadRetentionDays(string? text, out int days) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out days)
            && days >= 1
            && days <= SettingsDefaults.MaximumLogRetentionDays;

    /// <summary>One localised sentence carrying the stable English failure code, as Home does.</summary>
    private static string Describe(string localisedSentence, OperationFailure failure) =>
        string.Format(CultureInfo.CurrentCulture, localisedSentence, failure.Code);
}
