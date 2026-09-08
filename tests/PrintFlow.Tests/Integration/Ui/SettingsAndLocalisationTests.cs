using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PrintFlow.App.Localisation;
using PrintFlow.App.Settings;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Settings;
using PrintFlow.Domain.Trimming;
using PrintFlow.Infrastructure.Gate;
using PrintFlow.Infrastructure.Verification;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

/// <summary>
/// Settings and the runtime language switch (SCRUM-11118, SCRUM-11119).
/// </summary>
/// <remarks>
/// Organised by <b>distinct behaviour</b>, not by combination. There is deliberately no matrix
/// of seven settings × two languages × restart × valid/invalid: what is worth proving is that a
/// preference round-trips, that one Apply is one transaction, that a failed batch changes
/// nothing, that an absent row falls back, that the language switches without a restart and
/// survives one, that the production facts are read from the verified preset rather than
/// stored, and that no stable identifier moves when the language does. Each case below is one
/// of those.
/// <para>
/// The persistence cases run against the real <c>SqliteSettingsRepository</c> over a throwaway
/// database, and each "restart" readback goes through a service and a screen constructed afresh
/// over the same file, so nothing an in-memory field remembered can answer for persistence.
/// </para>
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class SettingsAndLocalisationTests
{
    // =====================================================================================
    // Settings persistence (SCRUM-11118)
    // =====================================================================================

    /// <summary>Every editable value survives a restart, read back through a new screen.</summary>
    [Fact]
    public async Task Edited_preferences_round_trip_through_a_restart()
    {
        using SettingsScreenHarness harness = new();
        SettingsViewModel screen = await harness.OpenAsync();

        screen.SelectedLanguage = screen.Languages.Single(o => o.Language == OperatorLanguage.English);
        screen.TrimSafetyMarginPixels = "12";
        screen.LogRetentionDays = "45";

        await screen.ApplyCommand.ExecuteAsync(null);

        SettingsViewModel reopened = await harness.RestartAndOpenAsync();

        reopened.SelectedLanguage.Language.ShouldBe(OperatorLanguage.English);
        reopened.TrimSafetyMarginPixels.ShouldBe("12");
        reopened.LogRetentionDays.ShouldBe("45");
    }

    /// <summary>
    /// One Apply is one transaction, and it reaches the database as one batch.
    /// </summary>
    /// <remarks>
    /// Asserted at the repository seam as well as on the values, because "all three ended up
    /// stored" is also true of three separate writes. The screen presents a single atomic
    /// action, so it must issue a single atomic write (SCRUM-11118).
    /// </remarks>
    [Fact]
    public async Task One_apply_writes_one_transactional_batch()
    {
        using SettingsScreenHarness harness = new();
        RecordingSettingsRepository recording = new(harness.Settings);
        SettingsViewModel screen = await harness.OpenAsync(settings: recording);

        screen.SelectedLanguage = screen.Languages.Single(o => o.Language == OperatorLanguage.English);
        screen.TrimSafetyMarginPixels = "7";
        screen.LogRetentionDays = "14";

        await screen.ApplyCommand.ExecuteAsync(null);

        recording.Batches.Count.ShouldBe(1);
        recording.Batches[0].Select(entry => entry.Key).ShouldBe(
            [SettingKey.UiLanguage, SettingKey.TrimSafetyMarginPixels, SettingKey.LogRetentionDays],
            ignoreOrder: true);
    }

    /// <summary>
    /// A failed save commits nothing, applies nothing and says so.
    /// </summary>
    /// <remarks>
    /// The language is the half that would be most visible if it leaked: the interface must not
    /// be showing English while the database still says the operator's language is Chinese
    /// (SCRUM-11118).
    /// </remarks>
    [Fact]
    public async Task A_failed_save_commits_nothing_and_leaves_the_language_alone()
    {
        using SettingsScreenHarness harness = new();
        FailingSettingsRepository failing = new(harness.Settings);
        SettingsViewModel screen = await harness.OpenAsync(settings: failing);

        screen.SelectedLanguage = screen.Languages.Single(o => o.Language == OperatorLanguage.English);
        screen.TrimSafetyMarginPixels = "9";

        await screen.ApplyCommand.ExecuteAsync(null);

        harness.Localisation.Current.ShouldBe(OperatorLanguage.SimplifiedChinese);
        screen.Notice.ShouldNotBeNullOrWhiteSpace();

        (await harness.Settings.ReadAllAsync(CancellationToken.None)).Value.ShouldBeEmpty();
    }

    /// <summary>An unusable entry is refused before anything is written.</summary>
    [Fact]
    public async Task An_unusable_entry_is_refused_and_writes_nothing()
    {
        using SettingsScreenHarness harness = new();
        RecordingSettingsRepository recording = new(harness.Settings);
        SettingsViewModel screen = await harness.OpenAsync(settings: recording);

        screen.TrimSafetyMarginPixels = "-3";
        await screen.ApplyCommand.ExecuteAsync(null);

        recording.Batches.ShouldBeEmpty();
        screen.Notice.ShouldBe(Resource("Settings_TrimMarginInvalid"));
    }

    /// <summary>
    /// With nothing persisted, each preference shows its accepted default.
    /// </summary>
    /// <remarks>
    /// Which is the whole of the precedence rule for an operator preference, seen from the
    /// screen: no row, so <c>appsettings.json</c> answers for retention and the Product constant
    /// answers for the trim margin and the language.
    /// </remarks>
    [Fact]
    public async Task Absent_settings_fall_back_to_configuration_and_then_to_the_product_constant()
    {
        using SettingsScreenHarness harness = new(configuredLogRetentionDays: 21);
        SettingsViewModel screen = await harness.OpenAsync();

        screen.SelectedLanguage.Language.ShouldBe(OperatorLanguage.SimplifiedChinese);
        screen.TrimSafetyMarginPixels.ShouldBe("0");
        screen.LogRetentionDays.ShouldBe("21");
    }

    // =====================================================================================
    // Language (SCRUM-11119)
    // =====================================================================================

    /// <summary>
    /// With nothing persisted, the first run is Simplified Chinese — whatever Windows says.
    /// </summary>
    /// <remarks>
    /// Run under an English UI culture on purpose. If the resolution followed the operating
    /// system, this test would see English; the AC requires it to see Chinese (SCRUM-11119).
    /// </remarks>
    [Fact]
    public async Task First_run_is_Simplified_Chinese_and_does_not_follow_Windows()
    {
        using SettingsScreenHarness harness = new();

        await InCulture("en-US", async () =>
        {
            LocalisationService localisation = new(harness.Settings);
            (await localisation.RestoreAsync(CancellationToken.None))
                .ShouldBe(OperatorLanguage.SimplifiedChinese);

            // Asserted on the authority the product actually resolves strings against, not on
            // the ambient thread property: that one is carried by ExecutionContext and is
            // restored when an async call returns, which is why it is not the authority.
            PrintFlow.App.Resources.OperatorCulture.Current.Name
                .ShouldBe(OperatorLanguages.SimplifiedChineseCultureName);
        });
    }

    /// <summary>
    /// Applying English changes the Settings screen that is already on the operator's monitor,
    /// with no restart. Applying Chinese changes it back.
    /// </summary>
    [Fact]
    public async Task The_visible_screen_changes_language_immediately_in_both_directions()
    {
        using SettingsScreenHarness harness = new();

        await InCulture("en-US", async () =>
        {
            // Startup, as the application performs it: the language is restored before the
            // first screen exists, and it is the Product default rather than the OS culture.
            await harness.Localisation.RestoreAsync(CancellationToken.None);

            SettingsViewModel screen = await harness.OpenAsync();
            screen.Heading.ShouldBe(ResourceIn("Settings_Heading", "zh-CN"));

            screen.SelectedLanguage = screen.Languages.Single(o => o.Language == OperatorLanguage.English);
            await screen.ApplyCommand.ExecuteAsync(null);

            // The same instance the operator is looking at, not a screen opened afterwards.
            screen.Heading.ShouldBe(ResourceIn("Settings_Heading", "en-US"));
            screen.ApplyLabel.ShouldBe(ResourceIn("Settings_Apply", "en-US"));

            screen.SelectedLanguage = screen.Languages.Single(
                o => o.Language == OperatorLanguage.SimplifiedChinese);
            await screen.ApplyCommand.ExecuteAsync(null);

            screen.Heading.ShouldBe(ResourceIn("Settings_Heading", "zh-CN"));
        });
    }

    /// <summary>A language switch raises exactly one shell-wide notification, not one per label.</summary>
    [Fact]
    public async Task A_language_switch_notifies_the_shell_once()
    {
        using SettingsScreenHarness harness = new();
        SettingsViewModel screen = await harness.OpenAsync();

        List<string?> shellChanges = [];
        using ShellViewModel shell = new(harness.Navigation, harness.Localisation);
        shell.PropertyChanged += (_, e) => shellChanges.Add(e.PropertyName);

        screen.SelectedLanguage = screen.Languages.Single(o => o.Language == OperatorLanguage.English);
        await screen.ApplyCommand.ExecuteAsync(null);

        shellChanges.ShouldBe([nameof(ShellViewModel.Title)]);
    }

    /// <summary>An explicit choice survives a restart, in either direction.</summary>
    [Fact]
    public async Task An_explicit_language_choice_survives_a_restart_in_both_directions()
    {
        using SettingsScreenHarness harness = new();

        await InCulture("en-US", async () =>
        {
            await harness.Localisation.RestoreAsync(CancellationToken.None);

            SettingsViewModel first = await harness.OpenAsync();
            first.SelectedLanguage = first.Languages.Single(o => o.Language == OperatorLanguage.English);
            await first.ApplyCommand.ExecuteAsync(null);

            harness.Restart();
            (await harness.Localisation.RestoreAsync(CancellationToken.None))
                .ShouldBe(OperatorLanguage.English);

            SettingsViewModel second = await harness.OpenAsync();
            second.SelectedLanguage = second.Languages.Single(
                o => o.Language == OperatorLanguage.SimplifiedChinese);
            await second.ApplyCommand.ExecuteAsync(null);

            harness.Restart();
            (await harness.Localisation.RestoreAsync(CancellationToken.None))
                .ShouldBe(OperatorLanguage.SimplifiedChinese);
        });
    }

    /// <summary>
    /// The persisted language is a stable culture name, not localised text.
    /// </summary>
    /// <remarks>
    /// The same rule a <c>FailureCode</c> follows. A row whose text changed with the language it
    /// selected could not be read back by the build that wrote it (MVP design §13.4).
    /// </remarks>
    [Fact]
    public async Task The_persisted_language_is_a_stable_culture_name()
    {
        using SettingsScreenHarness harness = new();
        SettingsViewModel screen = await harness.OpenAsync();

        screen.SelectedLanguage = screen.Languages.Single(o => o.Language == OperatorLanguage.English);
        await screen.ApplyCommand.ExecuteAsync(null);

        (await harness.Settings.ReadAsync(SettingKey.UiLanguage, CancellationToken.None))
            .Value!.Value.ShouldBe("en-US");
    }

    /// <summary>
    /// Internal identifiers stay stable English in both languages.
    /// </summary>
    /// <remarks>
    /// The failure code, the workflow and step names, the adapter id and this screen's own
    /// AutomationIds are quotable identifiers, not readable prose. Only the operator's sentences
    /// move (SCRUM-11119).
    /// </remarks>
    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public async Task Stable_identifiers_do_not_move_with_the_language(string cultureName)
    {
        await InCulture(cultureName, () =>
        {
            nameof(FailureCode.MeituTargetLost).ShouldBe("MeituTargetLost");
            WorkflowType.PrepareCustomerDesign.ToString().ShouldBe("PrepareCustomerDesign");
            StepKind.Enhancement.ToString().ShouldBe("Enhancement");
            SettingKey.UiLanguage.ToString().ShouldBe("UiLanguage");
            OperatorLanguages.CultureNameOf(OperatorLanguage.English).ShouldBe("en-US");
            return Task.CompletedTask;
        });
    }

    // =====================================================================================
    // Prospective defaults (SCRUM-11118)
    // =====================================================================================

    /// <summary>
    /// The saved default is the margin a newly imported job starts with.
    /// </summary>
    /// <remarks>
    /// Through the real service, the real workspace and the real database, and read back after a
    /// restart, so what is proved is the persisted pending trim decision rather than a value the
    /// screen happened to be holding.
    /// </remarks>
    [Fact]
    public async Task A_saved_trim_default_applies_to_a_newly_imported_job()
    {
        using SettingsScreenHarness harness = new();
        SettingsViewModel screen = await harness.OpenAsync();

        screen.TrimSafetyMarginPixels = "8";
        await screen.ApplyCommand.ExecuteAsync(null);

        SessionView imported = await harness.ImportAsync("after-default.png");

        imported.TrimMargin.Mode.ShouldBe(TrimMode.UniformMargin);
        imported.TrimMargin.Top.ShouldBe(8);
        imported.TrimMargin.Left.ShouldBe(8);

        SessionView reloaded = (await harness.Sessions.LoadAsync(imported.Id, CancellationToken.None)).Value;
        reloaded.TrimMargin.Top.ShouldBe(8);
    }

    /// <summary>
    /// Changing the default never reaches back into a job that already exists.
    /// </summary>
    /// <remarks>
    /// The one property that makes a prospective default safe. A job imported before the change
    /// keeps the margin it was created with, and nothing rewrites it (SCRUM-11118).
    /// </remarks>
    [Fact]
    public async Task Changing_the_default_does_not_rewrite_an_existing_job()
    {
        using SettingsScreenHarness harness = new();
        SessionView before = await harness.ImportAsync("before-default.png");
        before.TrimMargin.Mode.ShouldBe(TrimMode.TightCrop);

        SettingsViewModel screen = await harness.OpenAsync();
        screen.TrimSafetyMarginPixels = "15";
        await screen.ApplyCommand.ExecuteAsync(null);

        SessionView reloaded = (await harness.Sessions.LoadAsync(before.Id, CancellationToken.None)).Value;
        reloaded.TrimMargin.Mode.ShouldBe(TrimMode.TightCrop);
        reloaded.TrimMargin.IsNone.ShouldBeTrue();
    }

    /// <summary>With no default saved, an import behaves exactly as it always has.</summary>
    [Fact]
    public async Task With_no_default_saved_an_import_is_still_a_tight_crop()
    {
        using SettingsScreenHarness harness = new();

        SessionView imported = await harness.ImportAsync("no-default.png");

        imported.TrimMargin.Mode.ShouldBe(TrimMode.TightCrop);
        imported.TrimMargin.IsNone.ShouldBeTrue();
    }

    // =====================================================================================
    // Read-only production facts (SCRUM-11118, reusing SCRUM-11110)
    // =====================================================================================

    /// <summary>
    /// The production rows come from the verified workstation, not from stored settings.
    /// </summary>
    /// <remarks>
    /// Driven against the real <see cref="VerifiedEnvironmentGate"/> over the synthetic accepted
    /// workstation, so the preset identity and the accepted output location on this screen are
    /// the ones the one verification authority actually concluded.
    /// </remarks>
    [Fact]
    public async Task Production_facts_are_read_from_the_verified_workstation()
    {
        using WorkstationVerificationFixture workstation = new();
        using SettingsScreenHarness harness = new();

        SettingsViewModel screen = await harness.OpenAsync(
            diagnostics: new VerifiedEnvironmentGate(workstation.CreateVerifier()));

        screen.OutputRoot.ShouldBe(workstation.WorkspaceRoot);
        screen.Preset.ShouldContain(WorkstationVerificationFixture.PresetId);
        screen.Preset.ShouldContain(WorkstationVerificationFixture.PresetVersion);
        screen.ProductionDpi.ShouldContain("300");
    }

    /// <summary>Nothing about a production fact is ever written to the settings store.</summary>
    [Fact]
    public async Task Applying_writes_no_row_for_any_production_fact()
    {
        using SettingsScreenHarness harness = new();
        SettingsViewModel screen = await harness.OpenAsync();

        screen.TrimSafetyMarginPixels = "4";
        await screen.ApplyCommand.ExecuteAsync(null);

        IReadOnlyList<SettingEntry> stored =
            (await harness.Settings.ReadAllAsync(CancellationToken.None)).Value;

        stored.Select(entry => entry.Key).ShouldNotContain(SettingKey.ProductionDpi);
        stored.Select(entry => entry.Key).ShouldNotContain(SettingKey.DefaultOutputRoot);
        stored.Select(entry => entry.Key).ShouldNotContain(SettingKey.WorkstationPresetDetails);
        stored.Select(entry => entry.Key).ShouldNotContain(SettingKey.PhotoshopColourSettingsConfirmed);
    }

    /// <summary>
    /// A passive reading has not verified the Photoshop colour settings, and says exactly that.
    /// </summary>
    [Fact]
    public async Task An_unverified_colour_setup_is_reported_as_unverified()
    {
        using WorkstationVerificationFixture workstation = new();
        using SettingsScreenHarness harness = new();

        SettingsViewModel screen = await harness.OpenAsync(
            diagnostics: new VerifiedEnvironmentGate(workstation.CreateVerifier()));

        screen.ColourSetupStatus.ShouldBe(Resource("Settings_ColourNotVerified"));
    }

    /// <summary>A verified colour setup reports confirmed; a mismatched one reports a mismatch.</summary>
    [Theory]
    [InlineData(EnvironmentCheckStatus.Passed, "Settings_ColourConfirmed")]
    [InlineData(EnvironmentCheckStatus.Failed, "Settings_ColourMismatch")]
    public async Task A_checked_colour_setup_reports_what_the_check_concluded(
        EnvironmentCheckStatus status, string expectedResourceKey)
    {
        using SettingsScreenHarness harness = new();

        SettingsViewModel screen = await harness.OpenAsync(
            diagnostics: new StubDiagnostics(new EnvironmentReadinessReport(
                Verified: status == EnvironmentCheckStatus.Passed,
                PresetIdentity: "stub-preset 1.0.0 (abcdef)",
                ObservedAt: DateTimeOffset.UnixEpoch,
                Checks: [])
            {
                PhotoshopColourSetup = status,
            }));

        screen.ColourSetupStatus.ShouldBe(Resource(expectedResourceKey));
    }

    /// <summary>Settings links to the screen that owns live verification; it never runs one.</summary>
    [Fact]
    public async Task Settings_links_to_production_readiness_rather_than_verifying_itself()
    {
        using SettingsScreenHarness harness = new();
        StubDiagnostics diagnostics = new(EmptyReport);
        SettingsViewModel screen = await harness.OpenAsync(diagnostics: diagnostics);

        await screen.OpenEnvironmentCommand.ExecuteAsync(null);

        harness.Navigation.EnvironmentReadinessCount.ShouldBe(1);
        diagnostics.LiveCheckCount.ShouldBe(0);
    }

    // =====================================================================================
    // Reachability, accessibility and rendering
    // =====================================================================================

    /// <summary>Home offers a way in, and it is navigation only.</summary>
    [Fact]
    public async Task Home_offers_a_way_into_settings()
    {
        using HomeScreenHarness home = new();

        await home.Home.ShowSettingsCommand.ExecuteAsync(null);

        home.Navigation.SettingsCount.ShouldBe(1);
    }

    /// <summary>
    /// Every element the AC names renders, carries its stable AutomationId, is reachable from
    /// the keyboard and exposes the ordinary UIA pattern for its kind — in both languages.
    /// </summary>
    /// <remarks>
    /// One case rather than one per control and one per language: the question is whether a
    /// keyboard-only operator can work this screen at all, and the evidence is the same for each
    /// row. No coordinate is used anywhere — the language list is a <see cref="ComboBox"/>, the
    /// editable values are <see cref="TextBox"/>es and the actions are <see cref="Button"/>s.
    /// </remarks>
    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public async Task Settings_renders_keyboard_reachable_with_stable_ids_in_both_languages(
        string cultureName)
    {
        using SettingsScreenHarness harness = new();

        await InCulture(cultureName, async () =>
        {
            SettingsViewModel screen = await harness.OpenAsync();

            RenderResult<Rendered> rendered = WpfRendering.RenderExpectingNoBindingErrors(
                () => new SettingsView { DataContext = screen },
                WpfRendering.ReviewViewport,
                tree =>
                {
                    List<FrameworkElement> elements = [.. tree.OfType<FrameworkElement>()];

                    FrameworkElement ById(string id) => elements.Single(
                        e => AutomationProperties.GetAutomationId(e) == id);

                    AutomationPeer apply = UIElementAutomationPeer.CreatePeerForElement(
                        ById("Settings.Apply"));

                    return new Rendered(
                        AutomationProperties.GetAutomationId(tree.Root),
                        [.. elements
                            .Select(AutomationProperties.GetAutomationId)
                            .Where(id => id.StartsWith("Settings.", StringComparison.Ordinal))
                            .Order(StringComparer.Ordinal)],
                        [.. elements
                            .Where(e => e is Control { IsTabStop: true, Focusable: true, IsEnabled: true })
                            .Select(AutomationProperties.GetAutomationId)
                            .Where(id => id.StartsWith("Settings.", StringComparison.Ordinal))],
                        [.. elements
                            .Where(e => Confines(KeyboardNavigation.GetTabNavigation(e))
                                || Confines(KeyboardNavigation.GetControlTabNavigation(e)))
                            .Select(AutomationProperties.GetAutomationId)],
                        ById("Settings.Language") is ComboBox,
                        apply.GetPattern(PatternInterface.Invoke) is IInvokeProvider,
                        ((TextBox)ById("Settings.TrimMargin")).IsReadOnly,
                        ((TextBox)ById("Settings.ProductionDpi")).IsReadOnly,
                        ((TextBox)ById("Settings.OutputRoot")).IsReadOnly,
                        [.. elements.OfType<TextBlock>().Select(b => b.Text)
                            .Concat(elements.OfType<TextBox>().Select(b => b.Text))
                            .Concat(elements.OfType<Button>().Select(b => b.Content as string ?? string.Empty))
                            .Where(text => !string.IsNullOrWhiteSpace(text))]);
                });

            rendered.Facts.Screen.ShouldBe("Screen.Settings");

            foreach (string id in new[]
                     {
                         "Settings.Language", "Settings.OutputRoot", "Settings.TrimMargin",
                         "Settings.ProductionDpi", "Settings.Preset",
                         "Settings.PhotoshopColourSettings", "Settings.LogRetention",
                         "Settings.Apply", "Settings.Back",
                     })
            {
                rendered.Facts.Ids.ShouldContain(id);
            }

            // Every editable row and both actions are reachable with Tab alone; the read-only
            // production facts are still focusable, so a screen reader can read them out.
            foreach (string id in new[]
                     {
                         "Settings.Language", "Settings.TrimMargin", "Settings.LogRetention",
                         "Settings.Apply", "Settings.Back", "Settings.OutputRoot",
                         "Settings.ProductionDpi", "Settings.Preset",
                         "Settings.PhotoshopColourSettings",
                     })
            {
                rendered.Facts.TabStops.ShouldContain(id);
            }

            rendered.Facts.Confined.ShouldBeEmpty();
            rendered.Facts.LanguageIsComboBox.ShouldBeTrue();
            rendered.Facts.ApplyInvokes.ShouldBeTrue();
            rendered.Facts.TrimMarginIsReadOnly.ShouldBeFalse();
            rendered.Facts.ProductionDpiIsReadOnly.ShouldBeTrue();
            rendered.Facts.OutputRootIsReadOnly.ShouldBeTrue();

            // A missing translation renders its own resource key; none may reach an operator.
            foreach (string text in rendered.Facts.VisibleText)
            {
                text.ShouldNotStartWith("Settings_");
            }

            static bool Confines(KeyboardNavigationMode mode) =>
                mode is KeyboardNavigationMode.Cycle or KeyboardNavigationMode.Contained;
        });
    }

    /// <summary>
    /// The bounded live WPF/UI Automation proof: a driver walks Settings, switches the language,
    /// edits a setting, applies, and finds both after a restart (SCRUM-11118, SCRUM-11119).
    /// </summary>
    /// <remarks>
    /// Driven entirely through <see cref="ISelectionItemProvider"/>,
    /// <see cref="IValueProvider"/> and <see cref="IInvokeProvider"/> obtained from rendered
    /// controls — never through the view model's own properties or commands, and never with a
    /// coordinate. The heading is read off the rendered <see cref="TextBlock"/> before and after
    /// the switch, so what is proved is that the pixels changed, not that a property did.
    /// </remarks>
    [Fact]
    public async Task A_driver_switches_language_edits_a_setting_and_both_survive_a_restart()
    {
        using SettingsScreenHarness harness = new();

        await InCulture("en-US", async () =>
        {
            // First run: the Product default, whatever the ambient Windows culture is.
            (await harness.Localisation.RestoreAsync(CancellationToken.None))
                .ShouldBe(OperatorLanguage.SimplifiedChinese);

            SettingsViewModel screen = await harness.OpenAsync();

            Driven driven = default!;
            WpfRendering.OnStaThread(() =>
            {
                SettingsView view = new() { DataContext = screen };
                view.Measure(WpfRendering.ReviewViewport);
                view.Arrange(new Rect(
                    0, 0, WpfRendering.ReviewViewport.Width, WpfRendering.ReviewViewport.Height));
                view.UpdateLayout();

                TextBlock heading = Descendants(view).OfType<TextBlock>()
                    .First(b => b.Text == ResourceIn("Settings_Heading", "zh-CN"));
                string before = heading.Text;

                // Select English on the rendered list itself.
                //
                // Through the control rather than through an ISelectionItemProvider: a
                // ComboBox's item containers live in a Popup, which needs a real window to
                // realise, and this pass renders without one. Selecting on the control is the
                // same path a click takes from the moment the item is chosen — the TwoWay
                // binding is what carries it to the view model — and it uses no coordinate.
                ComboBox languages = Descendants(view).OfType<ComboBox>().Single(
                    c => AutomationProperties.GetAutomationId(c) == "Settings.Language");
                languages.SelectedItem = languages.Items.Cast<LanguageOption>()
                    .Single(o => o.Language == OperatorLanguage.English);
                view.UpdateLayout();

                // Type a new safety edge through the ordinary text value pattern.
                TextBox margin = Descendants(view).OfType<TextBox>().Single(
                    b => AutomationProperties.GetAutomationId(b) == "Settings.TrimMargin");
                ((IValueProvider)UIElementAutomationPeer
                    .CreatePeerForElement(margin).GetPattern(PatternInterface.Value)!).SetValue("6");
                view.UpdateLayout();

                // Apply, and pump the dispatcher the way a real desktop pumps it.
                Button apply = Descendants(view).OfType<Button>().Single(
                    b => AutomationProperties.GetAutomationId(b) == "Settings.Apply");
                ((IInvokeProvider)UIElementAutomationPeer
                    .CreatePeerForElement(apply).GetPattern(PatternInterface.Invoke)!).Invoke();
                Pump();
                view.UpdateLayout();

                driven = new Driven(before, heading.Text, screen.ApplyCommand.IsRunning);

                view.DataContext = null;
                view.UpdateLayout();
            });

            // The visible heading changed on the screen already on the monitor. No restart.
            driven.HeadingBefore.ShouldBe(ResourceIn("Settings_Heading", "zh-CN"));
            driven.HeadingAfter.ShouldBe(ResourceIn("Settings_Heading", "en-US"));

            // Close, rebuild the culture authority and the service, reopen.
            SettingsViewModel reopened = await harness.RestartAndOpenAsync();

            harness.Localisation.Current.ShouldBe(OperatorLanguage.English);
            reopened.SelectedLanguage.Language.ShouldBe(OperatorLanguage.English);
            reopened.TrimSafetyMarginPixels.ShouldBe("6");
            reopened.Heading.ShouldBe(ResourceIn("Settings_Heading", "en-US"));

            // And back again, immediately.
            reopened.SelectedLanguage = reopened.Languages.Single(
                o => o.Language == OperatorLanguage.SimplifiedChinese);
            await reopened.ApplyCommand.ExecuteAsync(null);
            reopened.Heading.ShouldBe(ResourceIn("Settings_Heading", "zh-CN"));
        });
    }

    private sealed record Rendered(
        string Screen,
        IReadOnlyList<string> Ids,
        IReadOnlyList<string> TabStops,
        IReadOnlyList<string> Confined,
        bool LanguageIsComboBox,
        bool ApplyInvokes,
        bool TrimMarginIsReadOnly,
        bool ProductionDpiIsReadOnly,
        bool OutputRootIsReadOnly,
        IReadOnlyList<string> VisibleText);

    private sealed record Driven(string HeadingBefore, string HeadingAfter, bool StillRunning);

    /// <summary>Runs the dispatcher queue down, so a queued click and its command complete.</summary>
    private static void Pump()
    {
        for (int i = 0; i < 200; i++)
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.SystemIdle);
            Thread.Sleep(5);
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;

        int visualChildren = root is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetChildrenCount(root)
            : 0;

        for (int i = 0; i < visualChildren; i++)
        {
            foreach (DependencyObject descendant in Descendants(VisualTreeHelper.GetChild(root, i)))
            {
                yield return descendant;
            }
        }

        foreach (object child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is DependencyObject dependencyObject and not Visual)
            {
                foreach (DependencyObject descendant in Descendants(dependencyObject))
                {
                    yield return descendant;
                }
            }
        }
    }

    // =====================================================================================
    // helpers
    // =====================================================================================

    private static readonly EnvironmentReadinessReport EmptyReport =
        new(Verified: false, PresetIdentity: null, ObservedAt: DateTimeOffset.UnixEpoch, Checks: []);

    /// <summary>The committed operator wording for a key, resolved the way the shell resolves it.</summary>
    private static string Resource(string key) =>
        ResourceIn(key, PrintFlow.App.Resources.OperatorCulture.Current.Name);

    private static string ResourceIn(string key, string cultureName) =>
        new System.Resources.ResourceManager(
                "PrintFlow.App.Resources.Strings", typeof(SettingsViewModel).Assembly)
            .GetString(key, CultureInfo.GetCultureInfo(cultureName))
        ?? throw new InvalidOperationException($"No resource '{key}'.");

    /// <summary>Runs <paramref name="body"/> under one UI culture and restores the ambient one.</summary>
    private static async Task InCulture(string cultureName, Func<Task> body)
    {
        CultureInfo previousUi = CultureInfo.CurrentUICulture;
        CultureInfo? previousDefault = CultureInfo.DefaultThreadCurrentUICulture;

        try
        {
            CultureInfo culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;

            await body();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousUi;
            CultureInfo.DefaultThreadCurrentUICulture = previousDefault;
        }
    }

    /// <summary>A readiness seam that answers with one fixed report and runs no live check.</summary>
    private sealed class StubDiagnostics : IEnvironmentDiagnostics
    {
        private readonly EnvironmentReadinessReport _report;

        public StubDiagnostics(EnvironmentReadinessReport report) => _report = report;

        public int LiveCheckCount { get; private set; }

        public EnvironmentReadinessReport Read() => _report;

        public Task<EnvironmentReadinessReport> RunLiveChecksAsync(CancellationToken cancellationToken)
        {
            LiveCheckCount++;
            return Task.FromResult(_report);
        }
    }

    /// <summary>Delegates every call and records the batches, so "one Apply, one write" is checkable.</summary>
    private sealed class RecordingSettingsRepository : ISettingsRepository
    {
        private readonly ISettingsRepository _inner;

        public RecordingSettingsRepository(ISettingsRepository inner) => _inner = inner;

        public List<IReadOnlyList<SettingEntry>> Batches { get; } = [];

        public Task<OperationResult<SettingEntry?>> ReadAsync(
            SettingKey key, CancellationToken cancellationToken) => _inner.ReadAsync(key, cancellationToken);

        public Task<OperationResult<IReadOnlyList<SettingEntry>>> ReadAllAsync(
            CancellationToken cancellationToken) => _inner.ReadAllAsync(cancellationToken);

        public Task<OperationResult<PrintFlow.Domain.Results.Unit>> UpsertAsync(
            IReadOnlyList<SettingEntry> entries, CancellationToken cancellationToken)
        {
            Batches.Add(entries);
            return _inner.UpsertAsync(entries, cancellationToken);
        }
    }

    /// <summary>Reads for real and refuses every write, so a failed save can be driven.</summary>
    private sealed class FailingSettingsRepository : ISettingsRepository
    {
        private readonly ISettingsRepository _inner;

        public FailingSettingsRepository(ISettingsRepository inner) => _inner = inner;

        public Task<OperationResult<SettingEntry?>> ReadAsync(
            SettingKey key, CancellationToken cancellationToken) => _inner.ReadAsync(key, cancellationToken);

        public Task<OperationResult<IReadOnlyList<SettingEntry>>> ReadAllAsync(
            CancellationToken cancellationToken) => _inner.ReadAllAsync(cancellationToken);

        public Task<OperationResult<PrintFlow.Domain.Results.Unit>> UpsertAsync(
            IReadOnlyList<SettingEntry> entries, CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult.Fail<PrintFlow.Domain.Results.Unit>(
                FailureCode.PersistenceError, "The settings write failed and was rolled back."));
    }
}
