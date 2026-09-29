using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PrintFlow.App.Resources;
using PrintFlow.App.Startup;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Gate;
using PrintFlow.Infrastructure.Verification;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

/// <summary>
/// Home's readiness summary (SCRUM-11152): what Production Readiness last observed in this run,
/// its time and first blocking reason, one way to the readiness screen, and the startup recovery
/// counts moved under details without losing the Recovery list or any warning.
/// </summary>
/// <remarks>
/// The report-to-Home cases drive the real Production Readiness view model over the real
/// <see cref="VerifiedEnvironmentGate"/> and the synthetic <see cref="WorkstationVerificationFixture"/>
/// workstation, so Home is asserted against what the one authority actually reported. Stubbed
/// seams appear only for shapes the fixture cannot produce: a live row that did not run, a read
/// that throws, a cancelled live check and a slow reading overtaken by a newer one.
/// <para>
/// Synthetic data, a test-owned database and off-screen rendering only: no window, UIA,
/// ApplicationStartup, production composition, live application or real workstation.
/// </para>
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class HomeReadinessSummaryTests
{
    // ---------------------------------------------------------------- AC2: not checked

    [Fact]
    public async Task A_new_run_is_not_checked_even_after_a_previous_run_passed_and_startup_verified_the_preset()
    {
        using WorkstationVerificationFixture fixture = new();
        ReadinessObservationAccessor previousRun = new();
        await ScreenOver(fixture, previousRun).OpenAsync(CancellationToken.None);
        previousRun.Current.Report!.Verified.ShouldBeTrue("the previous run really did pass");

        using HomeScreenHarness h = new();
        h.StartupStatus.Publish(StartupStatus.Started(presetVerified: true, StartupRecoveryReport.Empty));
        await h.Home.RefreshCommand.ExecuteAsync(null);

        HomeReadinessSummary summary = h.Home.Readiness;
        summary.State.ShouldBe(HomeReadinessState.NotChecked);
        summary.StatusText.ShouldBe(Strings.Resolve("Home_ReadinessNotChecked"));
        summary.HasCheckedAt.ShouldBeFalse("no observation happened in this run, so there is no time to show");
        summary.CheckedAtText.ShouldBeEmpty();
        summary.HasReason.ShouldBeFalse();
        summary.HintText.ShouldBe(Strings.Resolve("Home_ReadinessNotCheckedHint"));
        h.Home.ShowEnvironmentCommand.CanExecute(null).ShouldBeTrue();
        h.Home.PresetDetailText.ShouldBe(Strings.Resolve("Home_StartupPresetVerified"));
    }

    // ---------------------------------------------------------------- AC3: ready

    [Fact]
    public async Task A_passed_reading_in_this_run_shows_ready_with_that_readings_own_time()
    {
        using WorkstationVerificationFixture fixture = new();
        using HomeScreenHarness h = new();
        EnvironmentReadinessViewModel screen = ScreenOver(fixture, h.Readiness);
        await screen.OpenAsync(CancellationToken.None);
        screen.IsReady.ShouldBeTrue();
        screen.HasAdvisories.ShouldBeTrue("the fixture reports an advisory, which must not change the answer");

        await h.Home.RefreshCommand.ExecuteAsync(null);

        EnvironmentReadinessReport report = h.Readiness.Current.Report!;
        HomeReadinessSummary summary = h.Home.Readiness;
        summary.State.ShouldBe(HomeReadinessState.Ready);
        summary.StatusText.ShouldBe(Strings.Resolve("Home_ReadinessReady"));
        summary.CheckedAtText.ShouldBe(string.Format(CultureInfo.CurrentCulture,
            Strings.Resolve("Home_ReadinessCheckedAt"),
            report.ObservedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)));
        summary.CheckedAtText.ShouldContain(screen.ObservedAt, Case.Sensitive,
            "Home and Production Readiness show the same observation time");
        summary.HasReason.ShouldBeFalse();
        summary.HasTechnicalDetail.ShouldBeFalse();
    }

    [Fact]
    public void Advisories_never_change_the_answer_in_either_direction()
    {
        EnvironmentCheckReport advisory = new("FilesystemReadOnlyPolicyAdvisory", EnvironmentCheckStatus.Advisory,
            false, "EnvironmentCheck_FilesystemReadOnlyPolicyAdvisory", "advisory");
        EnvironmentCheckReport display = new("DisplayConfiguration", EnvironmentCheckStatus.Failed, true,
            "EnvironmentCheck_DisplayConfiguration", "two displays");

        Summary(new(true, "preset", At, [advisory])).State.ShouldBe(HomeReadinessState.Ready);

        HomeReadinessSummary blocked = Summary(new(false, "preset", At, [advisory, display]));
        blocked.State.ShouldBe(HomeReadinessState.Blocked);
        blocked.TechnicalDetailText.ShouldContain("DisplayConfiguration");
        blocked.TechnicalDetailText.ShouldNotContain("FilesystemReadOnlyPolicyAdvisory");
    }

    // ---------------------------------------------------------------- AC1: blocked

    [Fact]
    public async Task A_failed_reading_is_blocked_with_the_reports_first_blocker_and_never_ready()
    {
        using WorkstationVerificationFixture fixture = new();
        fixture.Facts.Display = fixture.Facts.Display with { ActiveDisplayCount = 2 };
        fixture.Facts.Culture = new UiCultureFacts("de-DE", WorkstationVerificationFixture.SystemUiCulture);
        fixture.Facts.Session = new InteractiveSessionFacts(false, 0, "Services", false, null);
        using HomeScreenHarness h = new();
        EnvironmentReadinessViewModel screen = ScreenOver(fixture, h.Readiness);
        await screen.OpenAsync(CancellationToken.None);
        screen.BlockingFailures.Count.ShouldBeGreaterThan(1);

        await h.Home.RefreshCommand.ExecuteAsync(null);

        EnvironmentCheckRow first = screen.BlockingFailures[0];
        first.SupportKey.ShouldBe(h.Readiness.Current.Report!.BlockingFailures.First().CheckKey);
        HomeReadinessSummary summary = h.Home.Readiness;
        summary.State.ShouldBe(HomeReadinessState.Blocked);
        summary.StatusText.ShouldBe(Strings.Resolve("Home_ReadinessBlocked"));
        summary.StatusText.ShouldNotBe(Strings.Resolve("Home_ReadinessReady"));
        summary.ReasonText.ShouldBe(string.Format(CultureInfo.CurrentCulture,
            Strings.Resolve("Home_ReadinessFirstReason"), first.Name, first.Explanation));
        summary.TechnicalDetailText.ShouldContain(first.SupportKey);
        summary.HasCheckedAt.ShouldBeTrue();
        summary.HintText.ShouldBe(Strings.Resolve("Home_ReadinessBlockedHint"));
        h.Home.ShowEnvironmentCommand.CanExecute(null).ShouldBeTrue();
    }

    /// <summary>
    /// The shape a passive reading has in a new run: automatic checks passed and every live row
    /// Blocked because no live evidence exists yet (SCRUM-11152 review F1).
    /// </summary>
    /// <remarks>
    /// Neither the live row's failure sentence nor the readiness screen's "a prerequisite has not
    /// passed" is true here: nothing failed. Home says the live check has no current result and
    /// names the readiness screen's own button for it.
    /// </remarks>
    [Fact]
    public void A_live_check_that_has_not_run_is_explained_without_claiming_any_failure()
    {
        EnvironmentCheckReport[] live =
        [
            .. new[]
            {
                "ExternalApplicationAutomationLock", "MeituLaunchability", "MeituSafeStartingState",
                "PhotoshopLaunchability", "PhotoshopSafeStartingState", "PhotoshopColourSettings",
                "PhotoshopTestImageRoundTrip",
            }.Select(key => new EnvironmentCheckReport(key, EnvironmentCheckStatus.Blocked, true,
                "EnvironmentCheck_" + key, "Live application verification has not been run in this PrintFlow process.",
                Phase: EnvironmentCheckPhase.LiveApplication)),
        ];
        HomeReadinessSummary summary = Summary(new(false, "preset", At,
        [
            new("OperatingSystem", EnvironmentCheckStatus.Passed, true, "EnvironmentCheck_OperatingSystem", "matched"),
            .. live,
        ]));

        summary.State.ShouldBe(HomeReadinessState.Blocked);
        summary.ReasonText.ShouldBe(string.Format(CultureInfo.CurrentCulture,
            Strings.Resolve("Home_ReadinessLiveCheckPending"), Strings.Environment_RunLiveChecks));
        summary.ReasonText.ShouldNotContain(Strings.Resolve("Environment_LiveCheckNotRun"));
        summary.ReasonText.ShouldNotContain(Strings.Resolve("EnvironmentCheck_ExternalApplicationAutomationLock"));
        summary.TechnicalDetailText.ShouldContain("ExternalApplicationAutomationLock");
    }

    [Fact]
    public void A_not_verified_report_that_names_no_blocker_is_not_confirmed_and_guesses_no_fault()
    {
        HomeReadinessSummary summary = Summary(new(false, null, At,
        [
            new("FilesystemReadOnlyPolicyAdvisory", EnvironmentCheckStatus.Advisory, false,
                "EnvironmentCheck_FilesystemReadOnlyPolicyAdvisory", "advisory"),
        ]));

        summary.State.ShouldBe(HomeReadinessState.NotConfirmed);
        summary.ReasonText.ShouldBe(Strings.Resolve("Home_ReadinessNoReason"));
        summary.HasTechnicalDetail.ShouldBeFalse();
        summary.HasCheckedAt.ShouldBeTrue("the report was observed, and its time is still true");
    }

    // ---------------------------------------------------------------- stale, failed, cancelled, late

    [Fact]
    public async Task A_later_reading_that_throws_withdraws_the_earlier_pass()
    {
        ScriptedDiagnostics diagnostics = new(Passed);
        using HomeScreenHarness h = new();
        EnvironmentReadinessViewModel screen = new(diagnostics, new RecordingNavigation(), h.Readiness);
        await screen.OpenAsync(CancellationToken.None);
        await h.Home.RefreshCommand.ExecuteAsync(null);
        h.Home.Readiness.State.ShouldBe(HomeReadinessState.Ready);

        diagnostics.NextReadThrows = true;
        await Should.ThrowAsync<InvalidOperationException>(() => screen.RefreshCommand.ExecuteAsync(null));
        await h.Home.RefreshCommand.ExecuteAsync(null);

        HomeReadinessSummary summary = h.Home.Readiness;
        summary.State.ShouldBe(HomeReadinessState.NotConfirmed);
        summary.ReasonText.ShouldBe(Strings.Resolve("Home_ReadinessUnfinishedReason"));
        summary.HasCheckedAt.ShouldBeFalse("the earlier time belongs to a result that is no longer shown");
    }

    [Fact]
    public async Task A_later_reading_that_fails_replaces_the_earlier_pass()
    {
        using WorkstationVerificationFixture fixture = new();
        using HomeScreenHarness h = new();
        EnvironmentReadinessViewModel screen = ScreenOver(fixture, h.Readiness);
        await screen.OpenAsync(CancellationToken.None);

        fixture.Facts.Display = fixture.Facts.Display with { ActiveDisplayCount = 2 };
        await screen.RefreshCommand.ExecuteAsync(null);
        await h.Home.RefreshCommand.ExecuteAsync(null);

        h.Home.Readiness.State.ShouldBe(HomeReadinessState.Blocked);
        h.Home.Readiness.TechnicalDetailText.ShouldContain(nameof(WorkstationVerificationCheck.DisplayConfiguration));
    }

    /// <remarks>
    /// Both shapes: the real gate returns a report whose running step is Failed with code
    /// Cancelled (review F3), and a seam may also throw. Neither is an observed fault.
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_running_then_cancelled_live_check_never_shows_the_earlier_pass_as_current(bool cancelReturnsReport)
    {
        ScriptedDiagnostics diagnostics = new(Passed) { HoldLiveCheck = true, CancelReturnsReport = cancelReturnsReport };
        using HomeScreenHarness h = new();
        EnvironmentReadinessViewModel screen = new(diagnostics, new RecordingNavigation(), h.Readiness);
        await screen.OpenAsync(CancellationToken.None);

        Task run = screen.RunLiveChecksCommand.ExecuteAsync(null);
        try
        {
            HomeViewModel during = Home(h);
            during.Readiness.State.ShouldBe(HomeReadinessState.Checking);
            during.Readiness.StatusText.ShouldBe(Strings.Resolve("Home_ReadinessChecking"));
            during.Readiness.HasCheckedAt.ShouldBeFalse();
            screen.CancelLiveChecksCommand.Execute(null);
        }
        finally
        {
            diagnostics.ReleaseLiveCheck.TrySetResult();
            await run;
        }

        await h.Home.RefreshCommand.ExecuteAsync(null);
        h.Home.Readiness.State.ShouldBe(HomeReadinessState.NotConfirmed);
        h.Home.Readiness.ReasonText.ShouldBe(Strings.Resolve("Home_ReadinessUnfinishedReason"));
        screen.IsReady.ShouldBe(!cancelReturnsReport, "the screen keeps or shows its own report; Home presents neither as current");
    }

    [Fact]
    public async Task A_slow_earlier_pass_completing_after_a_newer_failure_does_not_come_back()
    {
        using HomeScreenHarness h = new();
        HeldReadDiagnostics slow = new(Passed);
        EnvironmentReadinessViewModel first = new(slow, new RecordingNavigation(), h.Readiness);
        EnvironmentReadinessViewModel second = new(new ScriptedDiagnostics(Failed), new RecordingNavigation(), h.Readiness);

        Task earlier = first.OpenAsync(CancellationToken.None);
        slow.Entered.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue();
        await second.OpenAsync(CancellationToken.None);
        slow.Release.Set();
        await earlier;

        first.IsReady.ShouldBeTrue("the earlier reading really did pass");
        await h.Home.RefreshCommand.ExecuteAsync(null);
        h.Home.Readiness.State.ShouldBe(HomeReadinessState.Blocked);
    }

    [Fact]
    public void Only_the_latest_ticket_is_recorded_and_a_started_observation_hides_the_previous_report()
    {
        ReadinessObservationAccessor accessor = new();
        long passed = accessor.Begin();
        accessor.Complete(passed, Passed);
        accessor.Current.State.ShouldBe(ReadinessObservationState.Observed);

        long older = accessor.Begin();
        long newer = accessor.Begin();
        accessor.Current.Report.ShouldBeNull("a started observation hides the previous report");

        accessor.Complete(older, Passed);
        accessor.Current.State.ShouldBe(ReadinessObservationState.InProgress);
        accessor.Abandon(older);
        accessor.Current.State.ShouldBe(ReadinessObservationState.InProgress);

        accessor.Complete(newer, Failed);
        accessor.Current.Report.ShouldBeSameAs(Failed);
        accessor.Abandon(newer);
        accessor.Current.Report.ShouldBeSameAs(Failed, "an end already recorded is not overwritten");
    }

    [Fact]
    public async Task A_settings_reading_is_recorded_like_any_other_observation()
    {
        using SettingsScreenHarness settings = new();
        await settings.OpenAsync(diagnostics: new ScriptedDiagnostics(Failed));

        settings.Observations.Current.Report.ShouldBeSameAs(Failed);
    }

    // ---------------------------------------------------------------- same run, new run

    [Fact]
    public async Task Reopening_home_in_the_same_run_keeps_the_observation_and_a_new_run_starts_unchecked()
    {
        using WorkstationVerificationFixture fixture = new();
        using HomeScreenHarness h = new();
        await ScreenOver(fixture, h.Readiness).OpenAsync(CancellationToken.None);

        HomeViewModel reopened = Home(h);
        await reopened.RefreshCommand.ExecuteAsync(null);
        reopened.Readiness.State.ShouldBe(HomeReadinessState.Ready);

        HomeViewModel restarted = h.RestartHome(new RecordingNavigation());
        await restarted.RefreshCommand.ExecuteAsync(null);
        restarted.Readiness.State.ShouldBe(HomeReadinessState.NotChecked);
    }

    // ---------------------------------------------------------------- no hidden check

    [Fact]
    public async Task Home_rendering_refresh_language_details_and_navigation_observe_nothing_and_run_nothing()
    {
        typeof(HomeViewModel).GetConstructors().ShouldHaveSingleItem().GetParameters()
            .Select(p => p.ParameterType).ShouldNotContain(typeof(IEnvironmentDiagnostics));
        typeof(HomeViewModel).GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .Select(f => f.FieldType).ShouldNotContain(typeof(IEnvironmentDiagnostics));

        ScriptedDiagnostics diagnostics = new(Passed);
        using HomeScreenHarness h = new();
        await new EnvironmentReadinessViewModel(diagnostics, new RecordingNavigation(), h.Readiness)
            .OpenAsync(CancellationToken.None);
        ReadinessObservation observed = h.Readiness.Current;
        (diagnostics.ReadCalls, diagnostics.LiveCalls).ShouldBe((1, 0));

        using (new OperatorCultureScope("en"))
        {
            await h.Home.RefreshCommand.ExecuteAsync(null);
            await h.Home.RefreshCommand.ExecuteAsync(null);
            string english = h.Home.Readiness.StatusText + h.Home.Readiness.CheckedAtText + h.Home.RecoveryCountsText;
            OperatorCulture.Select(CultureInfo.GetCultureInfo("zh-CN"));
            string chinese = h.Home.Readiness.StatusText + h.Home.Readiness.CheckedAtText + h.Home.RecoveryCountsText;
            chinese.ShouldNotBe(english);
            RenderExpanded(h.Home, new Size(1000, 700));
            await h.Home.ShowEnvironmentCommand.ExecuteAsync(null);
        }

        h.Navigation.EnvironmentReadinessCount.ShouldBe(1);
        (diagnostics.ReadCalls, diagnostics.LiveCalls).ShouldBe((1, 0));
        h.Readiness.Current.ShouldBeSameAs(observed, "Home started no observation and recorded nothing");
        h.Meitu.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task The_readiness_button_opens_the_existing_screen_once_and_the_screen_runs_no_live_check()
    {
        ScriptedDiagnostics diagnostics = new(Passed);
        using HomeScreenHarness h = new();

        await h.Home.ShowEnvironmentCommand.ExecuteAsync(null);
        h.Navigation.EnvironmentReadinessCount.ShouldBe(1);

        // What the destination does when navigation opens it: its one existing passive reading.
        EnvironmentReadinessViewModel destination = new(diagnostics, new RecordingNavigation(), h.Readiness);
        await destination.OpenAsync(CancellationToken.None);
        (diagnostics.ReadCalls, diagnostics.LiveCalls).ShouldBe((1, 0));
    }

    // ---------------------------------------------------------------- AC4: recovery and notices

    [Fact]
    public async Task The_recovery_list_counts_warnings_and_a_failure_notice_coexist_with_the_summary()
    {
        using HomeScreenHarness h = new();
        SessionId interrupted = await RecoverySurfaceTests.Seed(h.Inner, "interrupted");
        h.StartupStatus.Publish(StartupStatus.Started(presetVerified: false, Recovered(), RetentionWarning()));
        ScriptedFailureSessionService service = new(h.Inner.CreateService());
        HomeViewModel home = new(service, h.Previews, h.Navigation, h.FilePicker, h.StartupStatus, h.Readiness);
        h.Readiness.Complete(h.Readiness.Begin(), Failed);
        await home.RefreshCommand.ExecuteAsync(null);

        home.RecoverySessions.ShouldHaveSingleItem().Id.ShouldBe(interrupted);
        home.HasRecoverySessions.ShouldBeTrue();
        home.RecoveryCountsText.ShouldBe(string.Format(CultureInfo.CurrentCulture,
            Strings.Resolve("Startup_RecoverySummary"), 1, 1, 1));
        home.RecoveryPendingText.ShouldBe(string.Format(CultureInfo.CurrentCulture,
            Strings.Resolve("Home_RecoveryPending"), 1));
        home.HasRetentionWarning.ShouldBeTrue();
        home.HasPresetWarning.ShouldBeTrue();
        home.HasRecoveryNotRunWarning.ShouldBeFalse();
        home.StartupSummary.ShouldContain(Strings.Resolve("Startup_DiagnosticRetentionWarning"));
        home.Readiness.State.ShouldBe(HomeReadinessState.Blocked);

        service.NextRecoveryFailure = OperationFailure.Create(FailureCode.PersistenceError, "Scripted commit failure.");
        await home.RestartRecoveryCommand.ExecuteAsync(home.RecoverySessions.Single());
        home.NoticeErrorCode.ShouldBe(nameof(FailureCode.PersistenceError));
        home.Readiness.State.ShouldBe(HomeReadinessState.Blocked);
        home.RecoverySessions.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Recovery_counts_read_zero_when_startup_recovered_nothing_and_warn_when_it_did_not_run()
    {
        using HomeScreenHarness clean = new();
        clean.StartupStatus.Publish(StartupStatus.Started(presetVerified: true, StartupRecoveryReport.Empty));
        await clean.Home.RefreshCommand.ExecuteAsync(null);
        clean.Home.RecoveryCountsText.ShouldBe(Strings.Resolve("Startup_RecoveryClean"));
        clean.Home.HasRecoveryNotRunWarning.ShouldBeFalse();
        clean.Home.HasPresetWarning.ShouldBeFalse();
        clean.Home.RecoveryPendingText.ShouldBeEmpty();

        using HomeScreenHarness notRun = new();
        await notRun.Home.RefreshCommand.ExecuteAsync(null);
        notRun.Home.HasRecoveryNotRunWarning.ShouldBeTrue();
        notRun.Home.RecoveryCountsText.ShouldBe(Strings.Resolve("Startup_RecoveryNotRun"));
    }

    [Fact]
    public async Task The_readiness_answer_changes_no_import_resume_or_recovery_eligibility()
    {
        using HomeScreenHarness h = new();
        await RecoverySurfaceTests.Seed(h.Inner, "recover");
        HomeViewModel notChecked = Home(h);
        await notChecked.RefreshCommand.ExecuteAsync(null);
        h.Readiness.Complete(h.Readiness.Begin(), Failed);
        HomeViewModel blocked = Home(h);
        await blocked.RefreshCommand.ExecuteAsync(null);
        blocked.Readiness.State.ShouldBe(HomeReadinessState.Blocked);

        RecoverySessionRow before = notChecked.RecoverySessions.Single();
        RecoverySessionRow after = blocked.RecoverySessions.Single();
        (after.CanRestart, after.CanImport, after.CanAbandon, after.ShowsPlainOpen, after.HasOpenCorrection)
            .ShouldBe((before.CanRestart, before.CanImport, before.CanAbandon, before.ShowsPlainOpen, before.HasOpenCorrection));
        foreach (HomeViewModel home in new[] { notChecked, blocked })
        {
            home.ChooseFileCommand.CanExecute(null).ShouldBeTrue();
            home.RestartRecoveryCommand.CanExecute(home.RecoverySessions.Single()).ShouldBeTrue();
            home.OpenRecoveryCommand.CanExecute(home.RecoverySessions.Single()).ShouldBeTrue();
        }

        h.FilePicker.Path = h.WriteSourceFile("while-blocked.png");
        await blocked.ChooseFileCommand.ExecuteAsync(null);
        h.Navigation.WorkflowSelectionFor.ShouldNotBeNull("importing is not gated on Home's summary");
    }

    // ---------------------------------------------------------------- bilingual wording

    [Theory]
    [InlineData("en")]
    [InlineData("zh-CN")]
    public void Every_state_is_worded_from_resources_in_both_languages(string language)
    {
        using OperatorCultureScope culture = new(language);
        foreach (HomeReadinessSummary summary in new[]
                 {
                     new HomeReadinessSummary(ReadinessObservation.None),
                     new HomeReadinessSummary(new(ReadinessObservationState.InProgress, null)),
                     new HomeReadinessSummary(new(ReadinessObservationState.Unfinished, null)),
                     Summary(Passed),
                     Summary(Failed),
                 })
        {
            foreach (string text in new[] { summary.Heading, summary.StatusText, summary.HintText, summary.DetailsLabel })
            {
                text.ShouldNotBeNullOrWhiteSpace();
                text.ShouldNotStartWith("Home_", Case.Sensitive, "a missing resource falls back to its key");
            }
        }

        foreach (string key in NewKeys)
        {
            // The exact satellite, without falling back to English, so a missing translation fails.
            new System.Resources.ResourceManager("PrintFlow.App.Resources.Strings", typeof(HomeViewModel).Assembly)
                .GetResourceSet(language == "en" ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(language), true, false)!
                .GetString(key).ShouldNotBeNullOrWhiteSpace(key);
        }
    }

    [Fact]
    public void A_language_change_rewords_the_same_observation_and_keeps_its_time()
    {
        using OperatorCultureScope culture = new("en");
        HomeReadinessSummary summary = Summary(Failed);
        string englishStatus = summary.StatusText;
        string englishReason = summary.ReasonText;
        string englishTime = summary.CheckedAtText;

        OperatorCulture.Select(CultureInfo.GetCultureInfo("zh-CN"));

        summary.State.ShouldBe(HomeReadinessState.Blocked);
        summary.StatusText.ShouldNotBe(englishStatus);
        summary.ReasonText.ShouldNotBe(englishReason);
        summary.ReasonText.ShouldContain(Strings.Resolve("EnvironmentCheckName_InteractiveSession"),
            Case.Sensitive, "the check name itself is reworded, not only the frame around it");
        summary.ReasonText.ShouldContain(Strings.Resolve("EnvironmentCheck_InteractiveSession"));
        englishReason.ShouldNotContain(Strings.Resolve("EnvironmentCheckName_InteractiveSession"));
        summary.CheckedAtText.ShouldContain(Failed.ObservedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
        englishTime.ShouldContain(Failed.ObservedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
    }

    // ---------------------------------------------------------------- AC5: layout and keyboard

    [Theory]
    [InlineData("en", 1000, 700)]
    [InlineData("zh-CN", 1000, 700)]
    [InlineData("en", 1920, 1040)]
    [InlineData("zh-CN", 1920, 1040)]
    public async Task The_summary_wraps_its_button_is_keyboard_reachable_and_recovery_stays_reachable(
        string language, double width, double height)
    {
        using OperatorCultureScope culture = new(language);
        using HomeScreenHarness h = new();
        await RecoverySurfaceTests.Seed(h.Inner, "first");
        await RecoverySurfaceTests.Seed(h.Inner, "second");
        h.StartupStatus.Publish(StartupStatus.Started(presetVerified: false, Recovered(), RetentionWarning()));
        h.Readiness.Complete(h.Readiness.Begin(), Failed);
        await h.Home.RefreshCommand.ExecuteAsync(null);
        h.Home.RecoverySessions.Count.ShouldBe(2);
        Size viewport = new(width, height);

        var rendered = WpfRendering.RenderExpectingNoBindingErrors(() => new HomeView { DataContext = h.Home }, viewport,
            tree =>
            {
                Settle(tree.Root);
                Button readiness = tree.OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "Home.ShowEnvironment");
                Expander startup = tree.OfType<Expander>().Single(e => AutomationProperties.GetAutomationId(e) == "Home.StartupDetails");
                Rect button = Bounds(readiness, tree.Root);
                TextBlock[] summaryText = tree.OfType<TextBlock>()
                    .Where(t => AutomationProperties.GetAutomationId(t).StartsWith("Home.Readiness.", StringComparison.Ordinal) && Shown(t))
                    .ToArray();
                ScrollViewer scroller = tree.OfType<ScrollViewer>().Single(s => AutomationProperties.GetAutomationId(s) == "Home.Lists");
                Button lastRecovery = tree.OfType<Button>()
                    .Where(b => AutomationProperties.GetAutomationId(b) == "Home.Recovery.Abandon" && Shown(b)).Last();
                scroller.ScrollToEnd();
                Settle(tree.Root);
                Rect recovery = Bounds(lastRecovery, scroller);
                ScrollViewer upper = tree.OfType<ScrollViewer>().Single(s => AutomationProperties.GetAutomationId(s) == "Home.Upper");
                Button choose = tree.OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "Home.ChooseFile");
                choose.BringIntoView();
                Settle(tree.Root);
                Rect chooseBounds = Bounds(choose, upper);
                return new
                {
                    ButtonInView = button.Top >= 0 && button.Bottom <= viewport.Height && button.Right <= viewport.Width,
                    readiness.Focusable,
                    readiness.IsTabStop,
                    readiness.IsEnabled,
                    readiness.IsDefault,
                    StartupCollapsed = !startup.IsExpanded,
                    Wrapped = summaryText.All(t => t.TextWrapping == TextWrapping.Wrap),
                    Clipped = summaryText.Where(t => t.DesiredSize.Width > t.ActualWidth + 0.5).Select(t => t.Text).ToArray(),
                    SummaryLines = summaryText.Length,
                    RecoveryReachable = recovery.Top >= 0 && recovery.Bottom <= scroller.ViewportHeight + 0.5,
                    ChooseFileReachable = chooseBounds.Top >= -0.5 && chooseBounds.Bottom <= upper.ViewportHeight + 0.5,
                    ScrollerHeight = scroller.ViewportHeight,
                };
            });

        rendered.Facts.ButtonInView.ShouldBeTrue();
        rendered.Facts.Focusable.ShouldBeTrue();
        rendered.Facts.IsTabStop.ShouldBeTrue();
        rendered.Facts.IsEnabled.ShouldBeTrue();
        rendered.Facts.IsDefault.ShouldBeFalse("Enter must not open anything implicitly");
        rendered.Facts.StartupCollapsed.ShouldBeTrue();
        rendered.Facts.Wrapped.ShouldBeTrue();
        rendered.Facts.Clipped.ShouldBeEmpty();
        rendered.Facts.SummaryLines.ShouldBeGreaterThanOrEqualTo(3, "status, reason and time are all shown for a blocked reading");
        rendered.Facts.RecoveryReachable.ShouldBeTrue("the last recovery action is reachable after scrolling");
        rendered.Facts.ChooseFileReachable.ShouldBeTrue("the import button is reachable after scrolling the upper part");
        rendered.Facts.ScrollerHeight.ShouldBeGreaterThan(120, "the lists keep a usable area below the summary");
        rendered.DesiredSize.Width.ShouldBeLessThanOrEqualTo(width);
    }

    /// <summary>
    /// The tallest the upper part of Home gets at the smallest supported viewport (review F4).
    /// </summary>
    /// <remarks>
    /// A blocked reading with its technical details open, the preset and retention warnings,
    /// Startup details open, and a failure notice with its Error details open, above two jobs in
    /// Recovery needed. Every expander is opened, then the recovery list is scrolled to its end:
    /// the last recovery action and the notice's code must both still be reachable.
    /// </remarks>
    [Theory]
    [InlineData("en")]
    [InlineData("zh-CN")]
    public async Task With_everything_expanded_the_notice_and_the_last_recovery_action_stay_reachable(string language)
    {
        using OperatorCultureScope culture = new(language);
        using HomeScreenHarness h = new();
        await RecoverySurfaceTests.Seed(h.Inner, "first");
        await RecoverySurfaceTests.Seed(h.Inner, "second");
        h.StartupStatus.Publish(StartupStatus.Started(presetVerified: false, Recovered(), RetentionWarning()));
        h.Readiness.Complete(h.Readiness.Begin(), Failed);
        ScriptedFailureSessionService service = new(h.Inner.CreateService());
        HomeViewModel home = new(service, h.Previews, h.Navigation, h.FilePicker, h.StartupStatus, h.Readiness);
        await home.RefreshCommand.ExecuteAsync(null);
        service.NextRecoveryFailure = OperationFailure.Create(FailureCode.PersistenceError, "Scripted commit failure.");
        await home.RestartRecoveryCommand.ExecuteAsync(home.RecoverySessions.First());
        home.HasNoticeErrorCode.ShouldBeTrue();
        home.RecoverySessions.Count.ShouldBe(2);
        Size viewport = new(1000, 700);

        var rendered = WpfRendering.RenderExpectingNoBindingErrors(() => new HomeView { DataContext = home }, viewport,
            tree =>
            {
                foreach (Expander e in tree.OfType<Expander>()) e.IsExpanded = true;
                Settle(tree.Root);
                ScrollViewer lists = tree.OfType<ScrollViewer>().Single(s => AutomationProperties.GetAutomationId(s) == "Home.Lists");
                lists.ScrollToEnd();
                Settle(tree.Root);
                Button lastRecovery = tree.OfType<Button>()
                    .Where(b => AutomationProperties.GetAutomationId(b) == "Home.Recovery.Abandon" && Shown(b)).Last();
                Rect recovery = Bounds(lastRecovery, lists);
                FrameworkElement code = tree.OfType<FrameworkElement>()
                    .Single(e => AutomationProperties.GetAutomationId(e) == "NoticeErrorDetails.Code");
                ScrollViewer upper = tree.OfType<ScrollViewer>().Single(s => AutomationProperties.GetAutomationId(s) == "Home.Upper");
                code.BringIntoView();
                Settle(tree.Root);
                Rect codeBounds = Bounds(code, upper);
                return new
                {
                    ListViewport = lists.ViewportHeight,
                    RecoveryReachable = recovery.Top >= -0.5 && recovery.Bottom <= lists.ViewportHeight + 0.5,
                    CodeVisible = codeBounds.Top >= -0.5 && codeBounds.Bottom <= upper.ViewportHeight + 0.5,
                };
            });

        rendered.Facts.RecoveryReachable.ShouldBeTrue($"list viewport {rendered.Facts.ListViewport:0}px");
        rendered.Facts.CodeVisible.ShouldBeTrue();
        rendered.DesiredSize.Width.ShouldBeLessThanOrEqualTo(viewport.Width);
    }

    /// <summary>
    /// Opt-in synthetic review images (PF_SCRUM11152_CAPTURE_DIR); not a golden-image assertion.
    /// </summary>
    [Theory]
    [InlineData("en")]
    [InlineData("zh-CN")]
    public async Task Captures_the_representative_states_when_asked(string language)
    {
        string? destination = Environment.GetEnvironmentVariable("PF_SCRUM11152_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(destination)) return;
        using OperatorCultureScope culture = new(language);

        foreach ((string state, Action<HomeScreenHarness> arrange, bool recovery) in new (string, Action<HomeScreenHarness>, bool)[]
                 {
                     ("not-checked", _ => { }, false),
                     ("ready", h => h.Readiness.Complete(h.Readiness.Begin(), Passed), false),
                     ("blocked", h => h.Readiness.Complete(h.Readiness.Begin(), Failed), false),
                     ("live-check-pending-warnings", h => h.Readiness.Complete(h.Readiness.Begin(), LiveCheckPending), true),
                     ("not-confirmed", h => { h.Readiness.Complete(h.Readiness.Begin(), Passed); h.Readiness.Abandon(h.Readiness.Begin()); }, false),
                     ("ready-recovery-warning", h => h.Readiness.Complete(h.Readiness.Begin(), Passed), true),
                 })
        {
            using HomeScreenHarness h = new();
            if (recovery)
            {
                await RecoverySurfaceTests.Seed(h.Inner, "interrupted-job");
                h.StartupStatus.Publish(StartupStatus.Started(presetVerified: !state.StartsWith("live-check-pending", StringComparison.Ordinal), Recovered(), RetentionWarning()));
            }
            else
            {
                h.StartupStatus.Publish(StartupStatus.Started(presetVerified: true, StartupRecoveryReport.Empty));
            }
            arrange(h);
            await h.Home.RefreshCommand.ExecuteAsync(null);
            foreach ((double w, double ht) in new[] { (1000d, 700d), (1920d, 1040d) })
            {
                foreach (bool expanded in recovery ? new[] { false, true } : new[] { false })
                {
                    string name = $"home-{state}{(expanded ? "-details" : string.Empty)}-{language}-{w:0}x{ht:0}.png";
                    WpfRendering.CapturePng(() => new HomeView { DataContext = h.Home }, new Size(w, ht),
                        Path.Combine(destination, name), tree =>
                        {
                            if (!expanded) return;
                            foreach (Expander e in tree.OfType<Expander>().Where(e => AutomationProperties.GetAutomationId(e) == "Home.StartupDetails"))
                                e.IsExpanded = true;
                        });
                }
            }
        }
    }

    // ---------------------------------------------------------------- helpers

    private static readonly string[] NewKeys =
    [
        "Home_ReadinessHeading", "Home_ReadinessNotChecked", "Home_ReadinessNotCheckedHint",
        "Home_ReadinessChecking", "Home_ReadinessCheckingHint", "Home_ReadinessReady", "Home_ReadinessReadyHint",
        "Home_ReadinessBlocked", "Home_ReadinessFirstReason", "Home_ReadinessLiveCheckPending", "Home_ReadinessBlockedHint",
        "Home_ReadinessNotConfirmed", "Home_ReadinessUnfinishedReason", "Home_ReadinessNoReason",
        "Home_ReadinessNotConfirmedHint", "Home_ReadinessCheckedAt", "Home_ReadinessTechnicalCheck",
        "Home_StartupDetails", "Home_StartupPresetVerified", "Home_StartupPresetNotVerified",
    ];

    private static readonly DateTimeOffset At = new(2026, 9, 29, 21, 12, 0, TimeSpan.Zero);

    private static readonly EnvironmentReadinessReport Passed = new(true, "preset 1.0.0 (ABCDEF)", At,
    [
        new("OperatingSystem", EnvironmentCheckStatus.Passed, true, "EnvironmentCheck_OperatingSystem", "matched"),
    ]);

    /// <summary>A new run's passive reading: automatic checks passed, live rows not run yet.</summary>
    private static readonly EnvironmentReadinessReport LiveCheckPending = new(false, "preset 1.0.0 (ABCDEF)", At,
    [
        new("OperatingSystem", EnvironmentCheckStatus.Passed, true, "EnvironmentCheck_OperatingSystem", "matched"),
        new("ExternalApplicationAutomationLock", EnvironmentCheckStatus.Blocked, true,
            "EnvironmentCheck_ExternalApplicationAutomationLock",
            "Live application verification has not been run in this PrintFlow process.",
            Phase: EnvironmentCheckPhase.LiveApplication),
    ]);

    private static readonly EnvironmentReadinessReport Failed = new(false, "preset 1.0.0 (ABCDEF)", At,
    [
        new("InteractiveSession", EnvironmentCheckStatus.Failed, true, "EnvironmentCheck_InteractiveSession", "locked"),
        new("DisplayConfiguration", EnvironmentCheckStatus.Failed, true, "EnvironmentCheck_DisplayConfiguration", "two displays"),
    ]);

    private static HomeReadinessSummary Summary(EnvironmentReadinessReport report) =>
        new(new ReadinessObservation(ReadinessObservationState.Observed, report));

    private static EnvironmentReadinessViewModel ScreenOver(
        WorkstationVerificationFixture fixture, ReadinessObservationAccessor observations) =>
        new(new VerifiedEnvironmentGate(fixture.CreateVerifier()), new RecordingNavigation(), observations);

    /// <summary>Another Home in the same run: same service, navigation, picker and observations.</summary>
    private static HomeViewModel Home(HomeScreenHarness h) =>
        new(h.Sessions, h.Previews, h.Navigation, h.FilePicker, h.StartupStatus, h.Readiness);

    private static StartupRecoveryReport Recovered() => new(
    [
        new(StartupRecoveryAction.AttemptInterrupted, At, null, null, null, "synthetic"),
        new(StartupRecoveryAction.AutomationLockReleased, At, null, null, null, "synthetic"),
        new(StartupRecoveryAction.WorkingFileQuarantined, At, null, null, null, "synthetic"),
    ]);

    private static DiagnosticRetentionReport RetentionWarning() =>
        new(0, 0, 0, 0, OperationFailure.Create(FailureCode.PersistenceError, "Synthetic retention warning."));

    private static void RenderExpanded(HomeViewModel home, Size viewport) =>
        WpfRendering.RenderExpectingNoBindingErrors(() => new HomeView { DataContext = home }, viewport, tree =>
        {
            foreach (Expander e in tree.OfType<Expander>()) e.IsExpanded = true;
            Settle(tree.Root);
            return 0;
        });

    private static void Settle(FrameworkElement root)
    {
        root.Dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
        root.UpdateLayout();
    }

    private static Rect Bounds(FrameworkElement element, Visual root) =>
        element.TransformToAncestor(root).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

    private static bool Shown(DependencyObject element)
    {
        for (DependencyObject? node = element; node is not null; node = VisualTreeHelper.GetParent(node))
            if (node is UIElement { Visibility: not Visibility.Visible }) return false;
        return true;
    }

    /// <summary>A diagnostics seam with scripted answers, counting every call.</summary>
    private sealed class ScriptedDiagnostics(EnvironmentReadinessReport report) : IEnvironmentDiagnostics
    {
        public int ReadCalls { get; private set; }
        public int LiveCalls { get; private set; }
        public bool NextReadThrows { get; set; }
        public bool HoldLiveCheck { get; init; }
        public bool CancelReturnsReport { get; init; }
        public TaskCompletionSource ReleaseLiveCheck { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public EnvironmentReadinessReport Read()
        {
            ReadCalls++;
            if (NextReadThrows) throw new InvalidOperationException("Scripted read failure.");
            return report;
        }

        public async Task<EnvironmentReadinessReport> RunLiveChecksAsync(CancellationToken cancellationToken)
        {
            LiveCalls++;
            if (HoldLiveCheck) await ReleaseLiveCheck.Task;
            if (cancellationToken.IsCancellationRequested && CancelReturnsReport)
            {
                // What the production live phase returns: the running step marked Failed/Cancelled.
                return new EnvironmentReadinessReport(false, report.PresetIdentity, At,
                [
                    .. report.Checks,
                    new("MeituLaunchability", EnvironmentCheckStatus.Failed, true, "EnvironmentCheck_MeituLaunchability",
                        "Live application verification was cancelled; no operator process was terminated.",
                        Phase: EnvironmentCheckPhase.LiveApplication),
                ]);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return report;
        }
    }

    /// <summary>A passive reading that waits until the test lets it finish.</summary>
    private sealed class HeldReadDiagnostics(EnvironmentReadinessReport report) : IEnvironmentDiagnostics
    {
        public ManualResetEventSlim Entered { get; } = new();
        public ManualResetEventSlim Release { get; } = new();

        public EnvironmentReadinessReport Read()
        {
            Entered.Set();
            Release.Wait(TimeSpan.FromSeconds(30));
            return report;
        }

        public Task<EnvironmentReadinessReport> RunLiveChecksAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
