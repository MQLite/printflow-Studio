using System.Globalization;
using System.IO;
using System.Windows.Automation;
using System.Windows.Controls;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Infrastructure.Gate;
using PrintFlow.Infrastructure.Verification;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Integration.Diagnostics;

/// <summary>
/// The operator's Production Readiness screen (Epic 11500 Part C §3, §4, §5, §12).
/// </summary>
/// <remarks>
/// Most cases drive the screen against the <i>real</i> <see cref="VerifiedEnvironmentGate"/> over
/// <see cref="WorkstationVerificationFixture"/>'s synthetic workstation, so what the screen shows
/// is what the one authority actually concluded rather than what a stub was told to say. The
/// stub-backed cases exist only where the shape being asserted — a report with no advisory at
/// all — is one the accepted preset never produces.
/// <para>
/// Nothing here launches an application, reads the real workstation or touches the shipped
/// configuration.
/// </para>
/// </remarks>
/// <remarks>
/// In the SQLite collection although it opens no database, so that it never runs beside the
/// Settings tests: those switch the application's UI culture for real, and a screen resolving
/// its text in one culture while this class resolves the expected resource in another would be
/// a flake with no defect behind it (SCRUM-11119).
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class EnvironmentReadinessScreenTests
{
    // ---------------------------------------------------------------- §12 diagnostics

    /// <summary>A verified workstation reports Ready, with its advisories still visible (§5).</summary>
    [Fact]
    public async Task A_verified_workstation_reports_ready()
    {
        using WorkstationVerificationFixture fixture = new();
        EnvironmentReadinessViewModel screen = ScreenOver(fixture);

        await screen.OpenAsync(CancellationToken.None);

        screen.IsReady.ShouldBeTrue();
        screen.StatusText.ShouldBe(Resource("Environment_Verified"));
        screen.HasBlockingFailures.ShouldBeFalse();
        screen.HasNoBlockingFailures.ShouldBeTrue();
    }

    /// <summary>One blocking failure closes readiness and is the one named (§12).</summary>
    [Fact]
    public async Task One_blocking_failure_reports_not_ready()
    {
        using WorkstationVerificationFixture fixture = new();
        fixture.Facts.Display = fixture.Facts.Display with { ActiveDisplayCount = 2 };

        EnvironmentReadinessViewModel screen = ScreenOver(fixture);
        await screen.OpenAsync(CancellationToken.None);

        screen.IsReady.ShouldBeFalse();
        screen.StatusText.ShouldBe(Resource("Environment_NotVerified"));
        screen.BlockingFailures.ShouldHaveSingleItem()
            .SupportKey.ShouldBe(nameof(WorkstationVerificationCheck.DisplayConfiguration));
    }

    /// <summary>
    /// Several blocking failures are all listed, not only the first (§12).
    /// </summary>
    /// <remarks>
    /// The failure the workflow reports is deliberately one message — the first blocking failure
    /// in evaluation order (Part B §22). A screen that repeated that would send an operator to
    /// fix the display, restart, and discover the session was wrong too. This is the surface
    /// where the whole list belongs.
    /// </remarks>
    [Fact]
    public async Task Multiple_blocking_failures_are_all_visible()
    {
        using WorkstationVerificationFixture fixture = new();
        fixture.Facts.Display = fixture.Facts.Display with { ActiveDisplayCount = 2 };
        fixture.Facts.Culture = new UiCultureFacts("de-DE", WorkstationVerificationFixture.SystemUiCulture);
        fixture.Facts.Session = new InteractiveSessionFacts(false, 0, "Services", false, null);

        EnvironmentReadinessViewModel screen = ScreenOver(fixture);
        await screen.OpenAsync(CancellationToken.None);

        screen.IsReady.ShouldBeFalse();
        screen.BlockingFailures.Select(row => row.SupportKey).ShouldBe(
            [
                nameof(WorkstationVerificationCheck.InteractiveSession),
                nameof(WorkstationVerificationCheck.DisplayConfiguration),
                nameof(WorkstationVerificationCheck.UiCulture),
            ],
            ignoreOrder: true);
    }

    /// <summary>
    /// An advisory-only workstation is Ready <i>and</i> says an advisory is present (§5, §12).
    /// </summary>
    /// <remarks>
    /// The exact pairing §5 asks for, asserted as one fact because separating them would let a
    /// future change satisfy either half alone: an advisory that suppressed Ready, or a Ready
    /// that swallowed the advisory, would each pass one of two smaller tests.
    /// </remarks>
    [Fact]
    public async Task An_advisory_only_workstation_is_ready_with_the_advisory_shown()
    {
        using WorkstationVerificationFixture fixture = new();
        EnvironmentReadinessViewModel screen = ScreenOver(fixture);

        await screen.OpenAsync(CancellationToken.None);

        screen.IsReady.ShouldBeTrue();
        screen.HasAdvisories.ShouldBeTrue();
        screen.AdvisorySummary.ShouldBe(Resource("Environment_AdvisoriesPresent"));
        screen.Advisories.ShouldAllBe(row => !row.IsBlocking && !row.IsFailure && row.IsAdvisory);
        screen.BlockingFailures.ShouldBeEmpty("an advisory never closes production work.");
    }

    /// <summary>Blocking and advisory stay separately classified when both are present (§5, §12).</summary>
    [Fact]
    public async Task A_blocking_failure_and_an_advisory_stay_separately_classified()
    {
        using WorkstationVerificationFixture fixture = new();
        WorkstationVerificationFixture.Corrupt(fixture.PhotoshopPath);

        EnvironmentReadinessViewModel screen = ScreenOver(fixture);
        await screen.OpenAsync(CancellationToken.None);

        screen.IsReady.ShouldBeFalse();
        screen.HasBlockingFailures.ShouldBeTrue();
        screen.HasAdvisories.ShouldBeTrue();

        screen.BlockingFailures.ShouldAllBe(row => row.IsBlocking && row.IsFailure);
        screen.Advisories.ShouldAllBe(row => !row.IsBlocking);

        // The two lists never overlap: a row is one or the other, never counted as both.
        screen.BlockingFailures.Select(row => row.SupportKey)
            .Intersect(screen.Advisories.Select(row => row.SupportKey))
            .ShouldBeEmpty();
    }

    /// <summary>The preset identity and the observation time are both displayed (§3, §12).</summary>
    [Fact]
    public async Task The_preset_identity_and_observation_time_are_displayed()
    {
        using WorkstationVerificationFixture fixture = new();
        EnvironmentReadinessViewModel screen = ScreenOver(fixture);

        await screen.OpenAsync(CancellationToken.None);

        screen.PresetIdentity.ShouldContain(WorkstationVerificationFixture.PresetId);
        screen.PresetIdentity.ShouldContain(WorkstationVerificationFixture.PresetVersion);
        screen.ObservedAt.ShouldNotBeNullOrWhiteSpace();
        screen.HasReport.ShouldBeTrue();
    }

    /// <summary>
    /// A preset that did not verify states nothing, and the screen says so rather than blanking
    /// (§3).
    /// </summary>
    [Fact]
    public async Task An_unverified_preset_is_reported_as_stating_nothing()
    {
        using WorkstationVerificationFixture fixture = new();
        WorkstationVerificationFixture.Corrupt(fixture.ManifestPath);

        EnvironmentReadinessViewModel screen = ScreenOver(fixture);
        await screen.OpenAsync(CancellationToken.None);

        screen.IsReady.ShouldBeFalse();
        screen.PresetIdentity.ShouldBe(Resource("Environment_PresetUnavailable"));
    }

    /// <summary>
    /// Every row carries bounded detail, and no row carries a document, dump or inventory (§3).
    /// </summary>
    /// <remarks>
    /// The cap is the report's own (Part B §21), and this asserts the screen does not widen it:
    /// manifest JSON, an evidence chain and a machine inventory are all things a support screen
    /// is tempted to show and none of them are operator information.
    /// </remarks>
    [Fact]
    public async Task Every_row_carries_bounded_detail_and_no_document()
    {
        using WorkstationVerificationFixture fixture = new();
        WorkstationVerificationFixture.Corrupt(fixture.EvidencePaths[0]);

        EnvironmentReadinessViewModel screen = ScreenOver(fixture);
        await screen.OpenAsync(CancellationToken.None);

        screen.Checks.ShouldNotBeEmpty();
        foreach (EnvironmentCheckRow row in screen.Checks)
        {
            row.Detail.Length.ShouldBeLessThanOrEqualTo(200);
            row.Detail.ShouldNotContain("{");
            row.Detail.ShouldNotContain("sourceManifestIntegrity");
        }
    }

    /// <summary>
    /// A passing check shows its neutral subject and never its failure sentence (§3, §6).
    /// </summary>
    /// <remarks>
    /// The whole reason the screen has a second family of resource keys. The gate's
    /// <c>MessageKey</c> is written as the problem — "Display configuration does not match the
    /// verified workstation" — which beside the word "Passed" would say the opposite of what
    /// happened.
    /// </remarks>
    [Fact]
    public async Task A_passing_check_shows_its_subject_and_not_a_failure_sentence()
    {
        using WorkstationVerificationFixture fixture = new();
        EnvironmentReadinessViewModel screen = ScreenOver(fixture);
        await screen.OpenAsync(CancellationToken.None);

        EnvironmentCheckRow passed = screen.Checks.First(
            row => row.SupportKey == nameof(WorkstationVerificationCheck.DisplayConfiguration));

        passed.Status.ShouldBe(Resource("Environment_StatusPassed"));
        passed.HasExplanation.ShouldBeFalse();
        passed.Explanation.ShouldBeEmpty();
        passed.Name.ShouldNotBe("EnvironmentCheckName_DisplayConfiguration");
    }

    /// <summary>No row is ever shown as a raw enum name (§3).</summary>
    /// <remarks>
    /// The stable key is displayed beside the localised subject for support to quote, so the
    /// assertion is that the localised subject exists — not that the key is absent.
    /// </remarks>
    [Fact]
    public async Task No_check_reaches_the_operator_as_a_bare_resource_key()
    {
        using WorkstationVerificationFixture fixture = new();
        EnvironmentReadinessViewModel screen = ScreenOver(fixture);
        await screen.OpenAsync(CancellationToken.None);

        screen.Checks.Count.ShouldBe(Enum.GetValues<WorkstationVerificationCheck>().Length - 7,
            "the synthetic verifier exercises the passive phase only");
        foreach (EnvironmentCheckRow row in screen.Checks)
        {
            row.Name.ShouldNotStartWith("EnvironmentCheck", Case.Sensitive);
            row.Name.ShouldNotBe(row.SupportKey);
            row.Status.ShouldNotStartWith("Environment_", Case.Sensitive);
            row.Classification.ShouldNotStartWith("Environment_", Case.Sensitive);
        }
    }

    // ---------------------------------------------------------------- §12 localisation

    /// <summary>Every displayed string resolves in en-US (§3, §12).</summary>
    [Fact]
    public Task The_screen_resolves_its_wording_in_en_US() =>
        InCulture("en-US", async screen =>
        {
            screen.Heading.ShouldBe("Production readiness");
            screen.RefreshLabel.ShouldBe("Refresh status");
            screen.RunLiveChecksLabel.ShouldBe("Safe recovery and recheck");
            screen.RestartRequirement.ShouldContain("restart PrintFlow", Case.Insensitive);
            screen.Checks.ShouldAllBe(row => row.Name.Length > 0);
            await Task.CompletedTask;
        });

    /// <summary>
    /// Every displayed string resolves in zh-CN, from the built satellite (§3, §12).
    /// </summary>
    /// <remarks>
    /// A missing translation is silent — <c>Strings.Get</c> falls back to the key — so the
    /// assertion is that nothing on the screen is still a resource key. The committed resource
    /// files are checked separately as XML; this is the runtime half.
    /// </remarks>
    [Fact]
    public Task The_screen_resolves_its_wording_in_zh_CN() =>
        InCulture("zh-CN", async screen =>
        {
            screen.Heading.ShouldNotBe("Environment_Heading");
            screen.Heading.ShouldNotBe("Production readiness");
            screen.RefreshLabel.ShouldNotBe("Environment_Refresh");
            screen.RefreshLabel.ShouldBe("刷新状态");
            screen.RunLiveChecksLabel.ShouldBe("安全恢复并重新检查");
            screen.RestartRequirement.ShouldNotBe("Environment_RestartRequired");
            screen.RestartRequirement.ShouldContain("PrintFlow");
            screen.RefreshScope.ShouldNotBe("Environment_RefreshScope");

            screen.Checks.ShouldAllBe(
                row => !row.Name.StartsWith("EnvironmentCheckName_", StringComparison.Ordinal));
            screen.Advisories.ShouldAllBe(
                row => !row.Explanation.StartsWith("EnvironmentCheck_", StringComparison.Ordinal));

            await Task.CompletedTask;
        });

    // ---------------------------------------------------------------- §12 refresh

    /// <summary>
    /// A restored display shows as ready on the next refresh, in the same process (§4, §12).
    /// </summary>
    /// <remarks>
    /// The case the screen exists for. An operator who unplugs the second monitor must be able
    /// to press one button and see the answer change — a screen that cached its first reading
    /// would still be refusing, and would send them to restart PrintFlow for no reason.
    /// </remarks>
    [Fact]
    public async Task A_restored_display_becomes_ready_on_refresh_without_a_restart()
    {
        using WorkstationVerificationFixture fixture = new();
        DisplayFacts accepted = fixture.Facts.Display;
        fixture.Facts.Display = accepted with { ActiveDisplayCount = 2 };

        EnvironmentReadinessViewModel screen = ScreenOver(fixture);
        await screen.OpenAsync(CancellationToken.None);
        screen.IsReady.ShouldBeFalse();

        fixture.Facts.Display = accepted;
        await screen.RefreshCommand.ExecuteAsync(null);

        screen.IsReady.ShouldBeTrue("the same screen, the same process, the restored workstation.");
        screen.BlockingFailures.ShouldBeEmpty();
    }

    /// <summary>An unlocked workstation shows as ready on the next refresh (§4, §12).</summary>
    [Fact]
    public async Task A_restored_session_becomes_ready_on_refresh_without_a_restart()
    {
        using WorkstationVerificationFixture fixture = new();
        InteractiveSessionFacts accepted = fixture.Facts.Session;
        fixture.Facts.Session = accepted with { InputDesktopName = "Winlogon" };

        EnvironmentReadinessViewModel screen = ScreenOver(fixture);
        await screen.OpenAsync(CancellationToken.None);
        screen.IsReady.ShouldBeFalse();

        fixture.Facts.Session = accepted;
        await screen.RefreshCommand.ExecuteAsync(null);

        screen.IsReady.ShouldBeTrue();
    }

    /// <summary>
    /// Refreshing really re-observes the workstation rather than re-rendering a stored answer
    /// (§4).
    /// </summary>
    /// <remarks>
    /// Counted at the fact reader, because "the numbers on screen changed" would also be true of
    /// a screen that re-read a cached report. Three reads mean the machine was looked at three
    /// times.
    /// </remarks>
    [Fact]
    public async Task Every_refresh_re_observes_the_dynamic_facts()
    {
        using WorkstationVerificationFixture fixture = new();
        EnvironmentReadinessViewModel screen = ScreenOver(fixture);

        await screen.OpenAsync(CancellationToken.None);
        int afterFirst = fixture.Facts.DynamicReadCount;
        afterFirst.ShouldBeGreaterThan(0);

        await screen.RefreshCommand.ExecuteAsync(null);
        await screen.RefreshCommand.ExecuteAsync(null);

        fixture.Facts.DynamicReadCount.ShouldBe(afterFirst * 3);
    }

    /// <summary>
    /// A failure that has been fixed stops being listed, rather than accumulating (§4).
    /// </summary>
    [Fact]
    public async Task A_repaired_check_stops_being_listed()
    {
        using WorkstationVerificationFixture fixture = new();
        UiCultureFacts accepted = fixture.Facts.Culture;
        fixture.Facts.Culture = new UiCultureFacts("de-DE", WorkstationVerificationFixture.SystemUiCulture);

        EnvironmentReadinessViewModel screen = ScreenOver(fixture);
        await screen.OpenAsync(CancellationToken.None);
        screen.BlockingFailures.Count.ShouldBe(1);

        fixture.Facts.Culture = accepted;
        await screen.RefreshCommand.ExecuteAsync(null);

        screen.BlockingFailures.ShouldBeEmpty();
        screen.Checks.Count.ShouldBe(Enum.GetValues<WorkstationVerificationCheck>().Length - 7,
            "a refresh replaces the reading; it does not append to it.");
    }

    /// <summary>
    /// The observation time moves with the reading, and is never treated as permission (§4).
    /// </summary>
    [Fact]
    public async Task The_observation_time_follows_the_reading_and_authorises_nothing()
    {
        using WorkstationVerificationFixture fixture = new();
        EnvironmentReadinessViewModel screen = ScreenOver(fixture);

        await screen.OpenAsync(CancellationToken.None);
        string first = screen.ObservedAt;

        fixture.Clock.Advance(TimeSpan.FromHours(3));
        fixture.Facts.Display = fixture.Facts.Display with { ActiveDisplayCount = 2 };
        await screen.RefreshCommand.ExecuteAsync(null);

        screen.ObservedAt.ShouldNotBe(first);
        screen.IsReady.ShouldBeFalse(
            "a fresh timestamp is information about the reading, never a reason to be ready.");
    }

    // ---------------------------------------------------------------- §12 shape cases

    /// <summary>
    /// A report with no advisory says so, rather than leaving the line blank (§5).
    /// </summary>
    /// <remarks>
    /// Stub-backed: the accepted preset always emits both advisories, so this shape cannot be
    /// produced from the real verifier. Asserting it matters anyway — "Advisories: none" is what
    /// a future preset without advisories would display.
    /// </remarks>
    [Fact]
    public async Task A_report_with_no_advisory_says_none()
    {
        EnvironmentReadinessViewModel screen = new(
            new StubDiagnostics(new EnvironmentReadinessReport(
                true,
                "preset 1.0.0 (ABCDEF)",
                DateTimeOffset.UnixEpoch,
                [new EnvironmentCheckReport(
                    "OperatingSystem", EnvironmentCheckStatus.Passed, true,
                    "EnvironmentCheck_OperatingSystem", "matched")])),
            new RecordingNavigation());

        await screen.OpenAsync(CancellationToken.None);

        screen.IsReady.ShouldBeTrue();
        screen.HasAdvisories.ShouldBeFalse();
        screen.AdvisorySummary.ShouldBe(Resource("Environment_AdvisoriesNone"));
    }

    /// <summary>
    /// Readiness follows the report's own verdict and is never re-derived from the rows (§5).
    /// </summary>
    /// <remarks>
    /// Deliberately a contradictory report: <c>Verified</c> is true while an advisory is present.
    /// A screen that counted rows to decide readiness would report Not Ready here, and would
    /// then be a second copy of the verifier's policy living in the shell.
    /// </remarks>
    [Fact]
    public async Task Readiness_comes_from_the_report_and_is_not_re_derived_from_the_rows()
    {
        EnvironmentReadinessViewModel screen = new(
            new StubDiagnostics(new EnvironmentReadinessReport(
                true,
                "preset 1.0.0 (ABCDEF)",
                DateTimeOffset.UnixEpoch,
                [
                    new EnvironmentCheckReport(
                        "OperatingSystem", EnvironmentCheckStatus.Passed, true,
                        "EnvironmentCheck_OperatingSystem", "matched"),
                    new EnvironmentCheckReport(
                        "FilesystemReadOnlyPolicyAdvisory", EnvironmentCheckStatus.Advisory, false,
                        "EnvironmentCheck_FilesystemReadOnlyPolicyAdvisory", "16 of 27 are writable"),
                ])),
            new RecordingNavigation());

        await screen.OpenAsync(CancellationToken.None);

        screen.IsReady.ShouldBeTrue();
        screen.HasAdvisories.ShouldBeTrue();
        screen.HasBlockingFailures.ShouldBeFalse();
    }

    /// <summary>Leaving the screen goes Home and nothing else (§13).</summary>
    [Fact]
    public async Task The_only_way_off_the_screen_is_home()
    {
        using WorkstationVerificationFixture fixture = new();
        RecordingNavigation navigation = new();
        EnvironmentReadinessViewModel screen = new(
            new VerifiedEnvironmentGate(fixture.CreateVerifier()), navigation);

        await screen.OpenAsync(CancellationToken.None);
        await screen.BackToHomeCommand.ExecuteAsync(null);

        navigation.GoHomeCount.ShouldBe(1);
        navigation.SessionFor.ShouldBeNull();
        navigation.WorkflowSelectionFor.ShouldBeNull();
    }

    [Fact]
    public async Task The_explicit_live_command_replaces_the_passive_report_with_typed_live_detail()
    {
        EnvironmentReadinessReport passive = new(false, "preset", DateTimeOffset.UnixEpoch,
        [
            new EnvironmentCheckReport("OperatingSystem", EnvironmentCheckStatus.Passed, true,
                "EnvironmentCheck_OperatingSystem", "matched"),
        ]);
        EnvironmentReadinessReport live = new(false, "preset", DateTimeOffset.UnixEpoch.AddMinutes(1),
        [
            new EnvironmentCheckReport("PhotoshopColourSettings", EnvironmentCheckStatus.Failed, true,
                "EnvironmentCheck_PhotoshopColourSettings", "No setting was changed.",
                "RGB: Accepted", "RGB: Current", EnvironmentCheckPhase.LiveApplication),
            new EnvironmentCheckReport("PhotoshopTestImageRoundTrip", EnvironmentCheckStatus.Blocked, true,
                "EnvironmentCheck_PhotoshopTestImageRoundTrip", "Colour settings did not pass.",
                "Open and close exact probe", "(not run)", EnvironmentCheckPhase.LiveApplication),
        ]);
        StubDiagnostics diagnostics = new(passive, live);
        EnvironmentReadinessViewModel screen = new(diagnostics, new RecordingNavigation());

        await screen.OpenAsync(CancellationToken.None);
        diagnostics.LiveCalls.ShouldBe(0, "opening and refreshing the page must stay passive");

        await screen.RunLiveChecksCommand.ExecuteAsync(null);

        diagnostics.LiveCalls.ShouldBe(1);
        screen.AutomaticChecks.ShouldBeEmpty();
        screen.LiveApplicationChecks.Count.ShouldBe(2);
        EnvironmentCheckRow mismatch = screen.LiveApplicationChecks[0];
        mismatch.AutomationId.ShouldBe("Environment.Check.PhotoshopColourSettings");
        mismatch.Expected.ShouldBe("RGB: Accepted");
        mismatch.Current.ShouldBe("RGB: Current");
        screen.LiveApplicationChecks[1].IsBlocked.ShouldBeTrue();
        screen.LiveApplicationChecks[1].Status.ShouldBe(Resource("Environment_StatusBlocked"));
    }

    [Theory]
    [InlineData("en-US", "This check did not run because a prerequisite has not passed. Resolve the failed check first, then recheck.")]
    [InlineData("zh-CN", "前置检查未通过，本项未运行。请先处理未通过的检查项，再重新检查。")]
    public Task Live_checks_blocked_by_revalidation_do_not_claim_unobserved_application_failures(
        string culture, string notRunExplanation) =>
        InCulture(culture, async _ =>
        {
            EnvironmentCheckReport revalidation = new("ProductionRevalidation", EnvironmentCheckStatus.Failed,
                true, "EnvironmentCheck_ProductionRevalidation", "The candidate has no matching revalidation.");
            EnvironmentCheckReport[] blocked =
            [
                .. new[] { "ExternalApplicationAutomationLock", "PhotoshopSafeStartingState",
                    "PhotoshopColourSettings", "PhotoshopTestImageRoundTrip" }
                    .Select(key => new EnvironmentCheckReport(key, EnvironmentCheckStatus.Blocked, true,
                        "EnvironmentCheck_" + key, "ProductionRevalidation did not pass.",
                        Phase: EnvironmentCheckPhase.LiveApplication)),
            ];
            EnvironmentReadinessReport passive = new(false, "preset", DateTimeOffset.UnixEpoch,
                [revalidation, .. blocked]);
            EnvironmentCheckReport actualFailure = blocked[^1] with
            {
                Status = EnvironmentCheckStatus.Failed,
                Detail = "CloseGuard was not confirmed during the attempted probe.",
            };
            EnvironmentReadinessReport attempted = passive with { Checks = [actualFailure] };
            EnvironmentReadinessViewModel screen = new(new StubDiagnostics(passive, attempted),
                new RecordingNavigation());

            await screen.OpenAsync(CancellationToken.None);

            screen.IsReady.ShouldBeFalse();
            screen.AutomaticChecks.ShouldHaveSingleItem().Explanation
                .ShouldBe(Resource("EnvironmentCheck_ProductionRevalidation"));
            screen.BlockingFailures.Count.ShouldBe(5, "the real blocker and unexecuted required checks stay visible");
            screen.LiveApplicationChecks.ShouldAllBe(row => row.IsBlocked && !row.IsFailure);
            screen.LiveApplicationChecks.ShouldAllBe(row => row.Explanation == notRunExplanation);
            screen.LiveApplicationChecks.ShouldAllBe(row => row.Status == Resource("Environment_StatusBlocked"));

            await screen.RunLiveChecksCommand.ExecuteAsync(null);

            screen.IsReady.ShouldBeFalse();
            screen.LiveApplicationChecks.ShouldHaveSingleItem().Explanation
                .ShouldBe(Resource("EnvironmentCheck_PhotoshopTestImageRoundTrip"));
            screen.LiveApplicationChecks[0].IsFailure.ShouldBeTrue();
        });

    [Fact]
    public async Task An_already_absent_reconciliation_explains_history_without_making_the_screen_ready()
    {
        EnvironmentReadinessReport failed = new(false, "preset", DateTimeOffset.UnixEpoch,
        [
            new EnvironmentCheckReport("PhotoshopTestImageRoundTrip", EnvironmentCheckStatus.Failed, true,
                "EnvironmentCheck_PhotoshopTestImageRoundTrip", "The new test did not complete.",
                Phase: EnvironmentCheckPhase.LiveApplication),
        ])
        {
            Lifecycle = new ReadinessEvidenceLifecycle(null, null, null, false, false, DateTimeOffset.UnixEpoch, null)
            {
                Recovery = new ReadinessProbeRecovery(ReadinessProbeRecoveryStatus.AlreadyAbsent,
                    "ReadinessRecovery_AlreadyAbsentRecheck", "No close input was sent.", null),
            },
        };
        StubDiagnostics diagnostics = new(failed);
        EnvironmentReadinessViewModel screen = new(diagnostics, new RecordingNavigation());

        await screen.RunLiveChecksCommand.ExecuteAsync(null);

        screen.CheckActivityText.ShouldBe(Resource("ReadinessRecovery_AlreadyAbsentRecheck"));
        screen.IsReady.ShouldBeFalse("recovery alone is never a fresh passing check");
        screen.StatusText.ShouldBe(Resource("Environment_NotVerified"));
        screen.BlockingFailures.ShouldHaveSingleItem();
        await screen.RefreshCommand.ExecuteAsync(null);
        screen.IsReady.ShouldBeFalse();
        diagnostics.LiveCalls.ShouldBe(1, "passive refresh must not repeat recovery or the live probe");
    }

    [Fact]
    public async Task Cancelling_live_checks_waits_for_unwind_and_preserves_the_failed_report()
    {
        EnvironmentReadinessReport failed = new(false, "preset", DateTimeOffset.UnixEpoch,
        [
            new EnvironmentCheckReport("PhotoshopTestImageRoundTrip", EnvironmentCheckStatus.Failed, true,
                "EnvironmentCheck_PhotoshopTestImageRoundTrip", "CloseGuard was not confirmed.",
                Phase: EnvironmentCheckPhase.LiveApplication),
        ]);
        UnwindingDiagnostics diagnostics = new(failed);
        EnvironmentReadinessViewModel screen = new(diagnostics, new RecordingNavigation());
        await screen.OpenAsync(CancellationToken.None);

        Task run = screen.RunLiveChecksCommand.ExecuteAsync(null);
        try
        {
            screen.IsBusy.ShouldBeTrue();
            screen.RefreshCommand.CanExecute(null).ShouldBeFalse();
            screen.RunLiveChecksCommand.CanExecute(null).ShouldBeFalse();
            screen.BackToHomeCommand.CanExecute(null).ShouldBeFalse();

            screen.CancelLiveChecksCommand.Execute(null);
            diagnostics.Token.IsCancellationRequested.ShouldBeTrue();
            screen.CheckActivityText.ShouldBe(Resource("Environment_Cancelling"));
            screen.IsBusy.ShouldBeTrue("cancellation is not confirmation that desktop input has stopped");
            screen.CancelLiveChecksCommand.CanExecute(null).ShouldBeFalse();
            run.IsCompleted.ShouldBeFalse();
            await screen.RefreshCommand.ExecuteAsync(null);
            await screen.RunLiveChecksCommand.ExecuteAsync(null);
            diagnostics.LiveCalls.ShouldBe(1);
            diagnostics.ReadCalls.ShouldBe(1);
        }
        finally
        {
            diagnostics.Unwind.TrySetResult();
            await run;
        }

        screen.IsBusy.ShouldBeFalse();
        screen.IsReady.ShouldBeFalse();
        screen.BlockingFailures.ShouldHaveSingleItem().Detail.ShouldBe("CloseGuard was not confirmed.");
        screen.CheckActivityText.ShouldBe(Resource("Environment_Cancelled"));
        screen.RefreshCommand.CanExecute(null).ShouldBeTrue();
        screen.RunLiveChecksCommand.CanExecute(null).ShouldBeTrue();
        screen.BackToHomeCommand.CanExecute(null).ShouldBeTrue();
    }

    private sealed class UnwindingDiagnostics(EnvironmentReadinessReport report) : IEnvironmentDiagnostics
    {
        public TaskCompletionSource Unwind { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token { get; private set; }
        public int ReadCalls { get; private set; }
        public int LiveCalls { get; private set; }

        public EnvironmentReadinessReport Read()
        {
            ReadCalls++;
            return report;
        }

        public async Task<EnvironmentReadinessReport> RunLiveChecksAsync(CancellationToken cancellationToken)
        {
            LiveCalls++;
            Token = cancellationToken;
            await Unwind.Task;
            cancellationToken.ThrowIfCancellationRequested();
            return report;
        }
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("zh-CN")]
    public Task The_rendered_recovery_screen_keeps_support_details_collapsed(string culture) =>
        InCulture(culture, async _ =>
        {
            EnvironmentReadinessReport failed = new(false, "preset 1.0.0 (ABCDEF)", DateTimeOffset.UnixEpoch,
            [
                new EnvironmentCheckReport("PhotoshopTestImageRoundTrip", EnvironmentCheckStatus.Failed, true,
                    "EnvironmentCheck_PhotoshopTestImageRoundTrip", "CloseGuard: owned Save As surface not observed.",
                    Phase: EnvironmentCheckPhase.LiveApplication),
            ]);
            EnvironmentReadinessViewModel screen = new(new StubDiagnostics(failed), new RecordingNavigation());
            await screen.OpenAsync(CancellationToken.None);

            var rendered = WpfRendering.RenderExpectingNoBindingErrors(
                () => new EnvironmentReadinessView { DataContext = screen },
                WpfRendering.ReviewViewport,
                tree => new
                {
                    Expanders = tree.OfType<Expander>()
                        .Select(expander => (Id: AutomationProperties.GetAutomationId(expander), expander.IsExpanded))
                        .ToArray(),
                    RunLabel = tree.OfType<Button>()
                        .Single(button => AutomationProperties.GetAutomationId(button) == "Environment.RunLiveChecks")
                        .Content?.ToString(),
                });

            rendered.Facts.Expanders.ShouldContain(item =>
                item.Id == "Environment.Check.PhotoshopTestImageRoundTrip.Details" && !item.IsExpanded);
            rendered.Facts.Expanders.ShouldContain(item => item.Id == "Environment.AllChecks" && !item.IsExpanded);
            rendered.Facts.Expanders.ShouldContain(item => item.Id == "Environment.ReportDetails" && !item.IsExpanded);
            rendered.Facts.RunLabel.ShouldBe(screen.RunLiveChecksLabel);
            rendered.DesiredSize.Width.ShouldBeLessThanOrEqualTo(WpfRendering.ReviewViewport.Width);
            rendered.DesiredSize.Height.ShouldBeLessThanOrEqualTo(WpfRendering.ReviewViewport.Height);

            if (Environment.GetEnvironmentVariable("PF_ACCEPT_A2_UI_CAPTURE_ROOT") is { Length: > 0 } captureRoot)
            {
                WpfRendering.CapturePng(() => new EnvironmentReadinessView { DataContext = screen },
                    WpfRendering.ReviewViewport, Path.Combine(captureRoot, $"readiness-recovery-{culture}.png"));
            }
        });

    /// <summary>
    /// The committed operator wording for a key, resolved the way the shell resolves it.
    /// </summary>
    /// <remarks>
    /// Through <c>ResourceManager</c> rather than through <c>Strings</c>, which is internal to
    /// the shell: what a test may assert is what an operator would read, not which accessor
    /// produced it.
    /// </remarks>
    private static string Resource(string key) =>
        new System.Resources.ResourceManager(
                "PrintFlow.App.Resources.Strings", typeof(EnvironmentReadinessViewModel).Assembly)
            .GetString(key, CultureInfo.CurrentUICulture)
        ?? throw new InvalidOperationException($"No resource '{key}'.");

    private static EnvironmentReadinessViewModel ScreenOver(WorkstationVerificationFixture fixture) =>
        new(new VerifiedEnvironmentGate(fixture.CreateVerifier()), new RecordingNavigation());

    private static async Task InCulture(string name, Func<EnvironmentReadinessViewModel, Task> assert)
    {
        CultureInfo previousUi = CultureInfo.CurrentUICulture;
        CultureInfo previous = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo culture = CultureInfo.GetCultureInfo(name);
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.CurrentCulture = culture;

            using WorkstationVerificationFixture fixture = new();
            EnvironmentReadinessViewModel screen = ScreenOver(fixture);
            await screen.OpenAsync(CancellationToken.None);

            await assert(screen);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousUi;
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>A readiness seam that answers with one fixed report.</summary>
    private sealed class StubDiagnostics : IEnvironmentDiagnostics
    {
        private readonly EnvironmentReadinessReport _report;
        private readonly EnvironmentReadinessReport _liveReport;

        public StubDiagnostics(EnvironmentReadinessReport report, EnvironmentReadinessReport? liveReport = null)
        {
            _report = report;
            _liveReport = liveReport ?? report;
        }

        public int LiveCalls { get; private set; }

        public EnvironmentReadinessReport Read() => _report;

        public Task<EnvironmentReadinessReport> RunLiveChecksAsync(CancellationToken cancellationToken)
        {
            LiveCalls++;
            return Task.FromResult(_liveReport);
        }
    }
}
