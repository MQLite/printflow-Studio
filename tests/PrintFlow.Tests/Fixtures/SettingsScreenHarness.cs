using System.Globalization;
using PrintFlow.App.Localisation;
using PrintFlow.App.Resources;
using PrintFlow.App.Settings;
using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Fixtures;

/// <summary>
/// A Settings screen wired to the real settings store, the real culture authority and the real
/// session service over a throwaway workspace and database (SCRUM-11118, SCRUM-11119).
/// </summary>
/// <remarks>
/// Nothing about PrintFlow's own behaviour is doubled by default: the screen writes through the
/// real <c>SqliteSettingsRepository</c>, the language it applies is applied by the real
/// <see cref="LocalisationService"/>, and the sessions a trim default reaches are created by the
/// real <see cref="SessionService"/>. A test that needs a failing write or a scripted workstation
/// reading passes one in at the call site, so the substitution is visible where it is made.
/// <para>
/// <b>It restores the process UI culture on disposal.</b> A language switch is genuinely global —
/// that is what makes the runtime switch work — so a test that performs one must not leave the
/// rest of the suite in the language it chose.
/// </para>
/// </remarks>
internal sealed class SettingsScreenHarness : IDisposable
{
    private readonly SessionServiceHarness _harness = new();
    private readonly CultureInfo _previousUiCulture = CultureInfo.CurrentUICulture;
    private readonly CultureInfo? _previousDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;

    /// <param name="configuredLogRetentionDays">
    /// What <c>appsettings.json</c> would have said, so the middle rung of the precedence is a
    /// value a test can recognise rather than the Product constant wearing its clothes.
    /// </param>
    public SettingsScreenHarness(
        int configuredLogRetentionDays = SettingsDefaults.FallbackLogRetentionDays)
    {
        Defaults = new SettingsDefaults(configuredLogRetentionDays);
        Localisation = new LocalisationService(Settings);
        Sessions = _harness.CreateService();
    }

    /// <summary>The real settings store, shared with the session service behind this harness.</summary>
    public ISettingsRepository Settings => _harness.Settings;

    /// <summary>The configured fallbacks the screen falls back to.</summary>
    public SettingsDefaults Defaults { get; }

    /// <summary>The real culture authority. Replaced wholesale by <see cref="Restart"/>.</summary>
    public LocalisationService Localisation { get; private set; }

    /// <summary>The session service the screen's prospective trim default reaches.</summary>
    public ISessionService Sessions { get; private set; }

    public RecordingNavigation Navigation { get; } = new();

    /// <summary>
    /// The readiness seam the screen reads its production facts from. Empty by default: a
    /// preference test should not need a workstation, and a screen with no reading must still
    /// open and say so.
    /// </summary>
    public IEnvironmentDiagnostics Diagnostics { get; } = new NoReadingDiagnostics();

    /// <summary>A Settings screen, optionally over a substituted store or readiness seam.</summary>
    public SettingsViewModel Create(
        ISettingsRepository? settings = null, IEnvironmentDiagnostics? diagnostics = null) =>
        new(settings ?? Settings, Localisation, diagnostics ?? Diagnostics, Navigation, Defaults,
            new LocalDiagnosticLocations(_harness.Database.Path,
                System.IO.Path.Combine(_harness.Workspace.Root, "Evidence")));

    /// <summary>A Settings screen that has already loaded, as navigation would have left it.</summary>
    public async Task<SettingsViewModel> OpenAsync(
        ISettingsRepository? settings = null, IEnvironmentDiagnostics? diagnostics = null)
    {
        SettingsViewModel screen = Create(settings, diagnostics);
        await screen.OpenAsync(CancellationToken.None);
        return screen;
    }

    /// <summary>
    /// What "close the application and open it again" looks like from a test.
    /// </summary>
    /// <remarks>
    /// The database and the workspace are the same; the culture authority and the session
    /// service are new, so anything either of them remembered in memory is gone and what the
    /// next screen shows can only have come from persistence.
    /// </remarks>
    public void Restart()
    {
        Localisation = new LocalisationService(Settings);
        Sessions = _harness.CreateService();
    }

    /// <summary>A restart, its language restored from persistence, and a freshly opened screen.</summary>
    public async Task<SettingsViewModel> RestartAndOpenAsync()
    {
        Restart();
        await Localisation.RestoreAsync(CancellationToken.None);
        return await OpenAsync();
    }

    /// <summary>Imports one synthetic source through the real service, as Home would.</summary>
    public async Task<SessionView> ImportAsync(string fileName)
    {
        string source = _harness.WriteSourcePng(fileName);
        return (await Sessions.ImportAsync(
            WorkflowType.PrepareCustomerDesign, source, null, "tester", CancellationToken.None)).Value;
    }

    public void Dispose()
    {
        // The selection is process-wide by design, so a test that made one puts the process
        // back as it found it: unselected, following the ambient culture.
        OperatorCulture.Select(null);
        CultureInfo.CurrentUICulture = _previousUiCulture;
        CultureInfo.DefaultThreadCurrentUICulture = _previousDefaultUiCulture;
        _harness.Dispose();
    }

    /// <summary>A readiness seam that has taken no reading, and never takes a live one.</summary>
    private sealed class NoReadingDiagnostics : IEnvironmentDiagnostics
    {
        public EnvironmentReadinessReport Read() =>
            new(Verified: false, PresetIdentity: null, ObservedAt: DateTimeOffset.UnixEpoch, Checks: []);

        public Task<EnvironmentReadinessReport> RunLiveChecksAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException("Settings never runs a live workstation check.");
    }
}
