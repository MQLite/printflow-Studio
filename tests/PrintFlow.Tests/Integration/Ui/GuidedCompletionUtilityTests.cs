using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using PrintFlow.App.Startup;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class GuidedCompletionUtilityTests
{
    [Theory]
    [InlineData("unchecked", "尚未检查")]
    [InlineData("blocked", "未运行")]
    [InlineData("stale", "已失效")]
    [InlineData("deferred", "未重新观察")]
    [InlineData("checked", "本次报告")]
    public async Task Readiness_unchecked_blocked_stale_and_checked_are_distinct(string state, string expected)
    {
        using OperatorCultureScope culture = new("zh-CN");
        var diagnostics = new FixedDiagnostics(Report(state));
        var model = new EnvironmentReadinessViewModel(diagnostics, new RecordingNavigation(), new ReadinessObservationAccessor());
        if (state != "unchecked") await model.OpenAsync(default);
        model.IsReady.ShouldBe(state == "checked");
        diagnostics.LiveCalls.ShouldBe(0);
        var shown = ReadReadiness(model);
        shown.GetValueOrDefault("Environment.Observation", string.Empty).ShouldContain(expected);
        if (state == "unchecked")
        {
            model.StatusText.ShouldContain("尚未检查");
            model.HasNoBlockingFailures.ShouldBeFalse("no reading cannot claim no blockers");
        }
        if (state is "stale" or "deferred")
        {
            shown.Values.ShouldContain(t => t.Contains("历史") && t.Contains(expected));
            model.Checks.Single().IsBlocked.ShouldBeTrue();
        }
        shown.Values.ShouldContain(t => t.Contains("重启 PrintFlow"));
    }

    [Fact]
    public async Task Opening_and_refreshing_utilities_never_run_live_checks()
    {
        using OperatorCultureScope culture = new("zh-CN");
        using SettingsScreenHarness h = new();
        var diagnostics = new FixedDiagnostics(Report("stale"));
        var settings = await h.OpenAsync(diagnostics: diagnostics);
        var environment = new EnvironmentReadinessViewModel(diagnostics, h.Navigation, h.Observations);
        await environment.OpenAsync(default);
        await environment.RefreshCommand.ExecuteAsync(null);
        settings.OpenEnvironmentCommand.Execute(null);
        diagnostics.LiveCalls.ShouldBe(0);
        (await h.Settings.ReadAllAsync(default)).Value.ShouldBeEmpty();
        await environment.RunLiveChecksCommand.ExecuteAsync(null);
        diagnostics.LiveCalls.ShouldBe(1, "only the explicit action reaches this seam");
    }

    [Fact]
    public async Task Settings_disclosure_preserves_validation_and_readonly_production()
    {
        using OperatorCultureScope culture = new("zh-CN");
        using SettingsScreenHarness h = new();
        var settings = await h.OpenAsync();
        settings.LogRetentionDays = "-1";
        await settings.ApplyCommand.ExecuteAsync(null);
        (await h.Settings.ReadAllAsync(default)).Value.ShouldBeEmpty();
        settings.Notice.ShouldNotBeNullOrWhiteSpace();
        var facts = WpfRendering.RenderExpectingNoBindingErrors(() => new SettingsView { DataContext = settings }, new Size(1000, 700), tree =>
        {
            var advanced = tree.OfType<Expander>().FirstOrDefault(e => AutomationProperties.GetAutomationId(e) == "Settings.Advanced");
            var common = new[] { "Settings.Language", "Settings.TrimMargin", "Settings.LogRetention" }.Select(id =>
            {
                var field = tree.OfType<FrameworkElement>().Single(e => AutomationProperties.GetAutomationId(e) == id);
                var scroll = GuidedSessionShellTests.Ancestors(field).OfType<ScrollViewer>().First();
                var p = field.TransformToAncestor(tree.Root).Transform(new Point());
                var bottom = scroll.TransformToAncestor(tree.Root).Transform(new Point(0, scroll.ActualHeight)).Y;
                return GuidedSessionProjectionTests.Shown(field) && p.Y + field.ActualHeight <= bottom + 1;
            }).ToArray();
            var production = new[] { "Settings.OutputRoot", "Settings.ProductionDpi", "Settings.Preset", "Settings.PhotoshopColourSettings" }
                .Select(id => tree.OfType<TextBox>().Single(e => AutomationProperties.GetAutomationId(e) == id))
                .All(t => t.IsReadOnly && GuidedSessionProjectionTests.Shown(t));
            var notice = tree.OfType<TextBlock>().Single(e => AutomationProperties.GetAutomationId(e) == "Settings.Notice");
            return new { AdvancedCollapsed = advanced is { IsExpanded: false }, Common = common, Production = production,
                Notice = GuidedSessionProjectionTests.Shown(notice) && !string.IsNullOrWhiteSpace(notice.Text) };
        }).Facts;
        facts.AdvancedCollapsed.ShouldBeTrue();
        facts.Common.ShouldAllBe(x => x);
        facts.Production.ShouldBeTrue(); facts.Notice.ShouldBeTrue();
    }

    internal static Dictionary<string, string> ReadReadiness(EnvironmentReadinessViewModel model) =>
        WpfRendering.RenderExpectingNoBindingErrors(() => new EnvironmentReadinessView { DataContext = model }, new Size(1000, 700), tree =>
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.SystemIdle);
            tree.Root.UpdateLayout();
            return GuidedCompletionSaveTests.Descendants(tree.Root).OfType<TextBlock>()
                .Where(t => GuidedSessionProjectionTests.Shown(t) && !string.IsNullOrEmpty(AutomationProperties.GetAutomationId(t)))
                .GroupBy(AutomationProperties.GetAutomationId).ToDictionary(g => g.Key, g => g.First().Text);
        }).Facts;

    internal static EnvironmentReadinessReport Report(string state)
    {
        var when = new DateTimeOffset(2026, 10, 19, 1, 0, 0, TimeSpan.Zero);
        var lifecycle = new ReadinessEvidenceLifecycle(state == "blocked" ? null : when.AddDays(-1), null, null,
            state is "deferred" or "checked", state is "blocked" or "deferred", when, null);
        var check = new EnvironmentCheckReport("PhotoshopColourSetup", state == "checked" ? EnvironmentCheckStatus.Passed : EnvironmentCheckStatus.Blocked,
            true, "EnvironmentCheck_PhotoshopColourSetup", "Synthetic readiness fixture; no application was inspected.",
            Phase: EnvironmentCheckPhase.LiveApplication) { Lifecycle = lifecycle };
        return new EnvironmentReadinessReport(state == "checked", "Synthetic preset", when, [check]) { Lifecycle = lifecycle };
    }

    internal sealed class FixedDiagnostics(EnvironmentReadinessReport report) : IEnvironmentDiagnostics
    {
        public int LiveCalls { get; private set; }
        public EnvironmentReadinessReport Read() => report;
        public Task<EnvironmentReadinessReport> RunLiveChecksAsync(CancellationToken cancellationToken)
        { LiveCalls++; return Task.FromResult(report); }
    }
}
