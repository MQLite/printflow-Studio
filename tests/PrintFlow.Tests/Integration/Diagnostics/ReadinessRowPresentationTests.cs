using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using PrintFlow.App.Resources;
using PrintFlow.App.Startup;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Integration.Diagnostics;

/// <summary>
/// SCRUM-11154 F-V2, observed on the workstation in zh-CN: a readiness row titled with a raw
/// resource key, a headline saying the check "did not pass" beside a row saying it "did not run",
/// a row announced by its view-model type name and two unnamed lists taking Tab stops.
/// </summary>
/// <remarks>
/// The synthetic entry's report is the realistic input: one blocking check, not run, whose key has
/// no product name. Production check keys all have names (an architecture test enforces it), so
/// the fallback is presentation only. Nothing here changes Verified, blocking flags or order.
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class ReadinessRowPresentationTests
{
    private static readonly EnvironmentCheckReport NotRun = new("SyntheticEntry", EnvironmentCheckStatus.Blocked, true,
        "Environment_NotVerified", "SYNTHETIC: no workstation or external application was inspected.");

    [Fact]
    public async Task A_check_without_a_product_name_shows_a_neutral_subject_and_keeps_its_stable_key_in_details()
    {
        using OperatorCultureScope culture = new("zh-CN");
        EnvironmentReadinessViewModel screen = await ScreenAsync(NotRun);

        EnvironmentCheckRow row = screen.BlockingFailures.Single();
        row.Name.ShouldBe(Strings.Resolve("Environment_UnlistedCheck"));
        row.Name.ShouldNotContain("EnvironmentCheckName_");
        row.Name.ShouldNotContain("SyntheticEntry");
        row.SupportKey.ShouldBe("SyntheticEntry");
        row.AutomationId.ShouldBe("Environment.Check.SyntheticEntry");
        row.Status.ShouldBe("未运行");
    }

    [Fact]
    public async Task A_required_check_that_did_not_run_is_not_reported_as_a_failed_observation()
    {
        using OperatorCultureScope culture = new("zh-CN");
        EnvironmentReadinessViewModel screen = await ScreenAsync(NotRun);

        screen.IsReady.ShouldBeFalse();
        screen.StatusText.ShouldBe(Strings.Resolve("Environment_NotConfirmedNotRun"));
        screen.StatusText.ShouldNotContain("未通过");
        EnvironmentCheckRow row = screen.BlockingFailures.Single();
        row.IsBlocking.ShouldBeTrue();
        row.Explanation.ShouldBe(Strings.Resolve("Environment_CheckNotRun"));
        row.Explanation.ShouldNotContain("未通过");
    }

    [Fact]
    public async Task A_failed_required_check_keeps_the_failed_headline_and_its_own_sentence()
    {
        using OperatorCultureScope culture = new("zh-CN");
        EnvironmentReadinessViewModel screen = await ScreenAsync(NotRun,
            new("DisplayConfiguration", EnvironmentCheckStatus.Failed, true, "EnvironmentCheck_DisplayConfiguration", "observed"));

        screen.StatusText.ShouldBe(Strings.Environment_NotVerified);
        screen.BlockingFailures.Single(r => r.IsFailure).Explanation.ShouldBe(Strings.Resolve("EnvironmentCheck_DisplayConfiguration"));
    }

    [Fact]
    public async Task A_blocked_live_check_keeps_its_existing_prerequisite_sentence()
    {
        using OperatorCultureScope culture = new("zh-CN");
        EnvironmentReadinessViewModel screen = await ScreenAsync(NotRun with
        {
            CheckKey = "MeituLaunchability", Phase = EnvironmentCheckPhase.LiveApplication,
        });

        screen.BlockingFailures.Single().Explanation.ShouldBe(Strings.Resolve("Environment_LiveCheckNotRun"));
    }

    [Fact]
    public async Task A_verified_report_keeps_the_verified_headline()
    {
        using OperatorCultureScope culture = new("zh-CN");
        EnvironmentReadinessViewModel screen = await ScreenAsync(
            new EnvironmentReadinessReport(true, "preset", DateTimeOffset.UtcNow,
                [new("DisplayConfiguration", EnvironmentCheckStatus.Passed, true, "EnvironmentCheck_DisplayConfiguration", "ok")]));

        screen.StatusText.ShouldBe(Strings.Environment_Verified);
    }

    [Fact]
    public async Task Rows_are_announced_by_their_subject_and_the_lists_are_named_and_not_tab_stops()
    {
        using OperatorCultureScope culture = new("zh-CN");
        EnvironmentReadinessViewModel screen = await ScreenAsync(NotRun,
            new("FilesystemReadOnlyPolicyAdvisory", EnvironmentCheckStatus.Advisory, false, "EnvironmentCheck_FilesystemReadOnlyPolicyAdvisory", "note"));

        var facts = WpfRendering.RenderExpectingNoBindingErrors(() => new EnvironmentReadinessView { DataContext = screen },
            WpfRendering.ReviewViewport, tree =>
            {
                ItemsControl[] lists = [.. tree.OfType<ItemsControl>().Where(list => list is not HeaderedItemsControl && list.GetType() == typeof(ItemsControl))];
                return new
                {
                    Lists = lists.Select(list => (Name: AutomationProperties.GetName(list), list.IsTabStop, list.Focusable)).ToArray(),
                    ItemNames = lists.SelectMany(list => UIElementAutomationPeer.CreatePeerForElement(list).GetChildren() ?? [])
                        .Select(peer => peer.GetName()).ToArray(),
                };
            }).Facts;

        facts.Lists.Length.ShouldBeGreaterThanOrEqualTo(2);
        facts.Lists.ShouldAllBe(list => list.Name.Length > 0 && !list.IsTabStop && !list.Focusable);
        facts.Lists.Select(list => list.Name).ShouldContain(screen.BlockingHeading);
        facts.Lists.Select(list => list.Name).ShouldContain(screen.AdvisoriesHeading);
        facts.ItemNames.ShouldContain(Strings.Resolve("Environment_UnlistedCheck"));
        facts.ItemNames.ShouldAllBe(name => name.Length > 0 && !name.Contains(nameof(EnvironmentCheckRow)));
    }

    private static Task<EnvironmentReadinessViewModel> ScreenAsync(params EnvironmentCheckReport[] checks) =>
        ScreenAsync(new EnvironmentReadinessReport(false, null, DateTimeOffset.UtcNow, checks));

    private static async Task<EnvironmentReadinessViewModel> ScreenAsync(EnvironmentReadinessReport report)
    {
        EnvironmentReadinessViewModel screen = new(new FixedDiagnostics(report), new RecordingNavigation(), new ReadinessObservationAccessor());
        await screen.OpenAsync(CancellationToken.None);
        return screen;
    }

    private sealed class FixedDiagnostics(EnvironmentReadinessReport report) : IEnvironmentDiagnostics
    {
        public EnvironmentReadinessReport Read() => report;
        public Task<EnvironmentReadinessReport> RunLiveChecksAsync(CancellationToken cancellationToken) => Task.FromResult(report);
    }
}
