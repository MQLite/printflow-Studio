using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using PrintFlow.App.Startup;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Ids;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Delivery;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

/// <summary>
/// Opt-in Chinese off-screen captures of the surfaces SCRUM-11154's findings remediation changed
/// (F-V2, F-V4, F-V5, F-V8). Evidence generation for a human visual check, not a golden-image
/// assertion; without <c>PF_SCRUM11154_ZH_CAPTURE_DIR</c> it does nothing and proves nothing.
/// Only zh-CN is captured: the owner decided no English test round is arranged.
/// </summary>
[Collection(SqliteCollection.Name)]
public sealed class ZhFindingsCaptureTests
{
    private static readonly Size[] Sizes = [new(1000, 700), new(1920, 1040)];

    [Fact]
    public async Task Captures_changed_zh_CN_surfaces_when_requested()
    {
        string? destination = Environment.GetEnvironmentVariable("PF_SCRUM11154_ZH_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(destination)) return;
        using OperatorCultureScope culture = new("zh-CN");

        // F-V2: the synthetic entry's not-run readiness report.
        EnvironmentReadinessViewModel readiness = new(new FixedDiagnostics(new EnvironmentReadinessReport(false, null, DateTimeOffset.UtcNow,
            [new("SyntheticEntry", EnvironmentCheckStatus.Blocked, true, "Environment_NotVerified", "SYNTHETIC: no workstation or external application was inspected.")])),
            new RecordingNavigation(), new ReadinessObservationAccessor());
        await readiness.OpenAsync(CancellationToken.None);
        foreach (Size size in Sizes)
            WpfRendering.CapturePng(() => new EnvironmentReadinessView { DataContext = readiness }, size, Path.Combine(destination, $"FV2-readiness-not-run-zhCN-{size.Width:0}x{size.Height:0}.png"));

        // F-V8 and F-V4: a saved job awaiting Complete, then the open Abandon confirmation.
        using HomeScreenHarness harness = TiffFinalReviewFixture.Harness(out _);
        SessionView png = await FinalSaveFixtures.PngAtFinalReviewAsync(harness.Inner, harness.Sessions);
        SessionId tiffId = (await TiffFinalReviewFixture.ReviewRequiredAsync(harness, "long-operator-named-artwork-with-several-words-for-layout.png")).Id;
        string folder = FinalSaveFixtures.NewFolder("zh-capture");
        try
        {
            var coordinator = new FinalSaveCoordinator(harness.Sessions, FinalSaveFixtures.Delivery(harness.Inner), "tester");
            (await coordinator.ConfirmAndSaveAsync(FinalSaveFixtures.Confirm(png, folder, "pending-complete.png"), null, CancellationToken.None))
                .Delivery!.Code.ShouldBe(DeliveryCode.Delivered);
            await harness.Home.RefreshCommand.ExecuteAsync(null);
            foreach (Size size in Sizes)
                WpfRendering.CapturePng(() => new HomeView { DataContext = harness.Home }, size, Path.Combine(destination, $"FV8-recent-pending-complete-zhCN-{size.Width:0}x{size.Height:0}.png"), ScrollListsToTop);

            await harness.Home.AbandonCommand.ExecuteAsync(harness.Home.RecentSessions.First(r => r.CanAbandon));
            foreach (Size size in Sizes)
                WpfRendering.CapturePng(() => new HomeView { DataContext = harness.Home }, size, Path.Combine(destination, $"FV4-abandon-confirmation-zhCN-{size.Width:0}x{size.Height:0}.png"));
            harness.Home.KeepJobCommand.Execute(null);

            // F-V5: the TIFF review output row with a long name.
            foreach (Size size in Sizes)
            {
                SessionViewModel screen = harness.Session(new RecordingNavigation());
                screen.Open((await harness.Sessions.LoadAsync(tiffId, CancellationToken.None)).Value);
                await screen.PreviewsLoaded;
                WpfRendering.CapturePng(() => new SessionScreenView { DataContext = screen }, size, Path.Combine(destination, $"FV5-output-row-zhCN-{size.Width:0}x{size.Height:0}.png"));
            }
        }
        finally { FinalSaveFixtures.Remove(folder); }

        // F-V3: the slider at 25 % and 75 % over visibly asymmetric pictures (Before red|yellow, After blue).
        foreach (int position in new[] { 25, 75 })
        {
            SessionViewModel model = harness.Session(new RecordingNavigation());
            model.IsFitToViewport = true;
            SharedReviewSurfaceTests.UseAsymmetricPanes(model);
            model.ReviewViewport.Background = ReviewInspectionBackground.White;
            model.ReviewViewport.Mode = ReviewComparisonMode.Slider;
            model.ReviewViewport.SliderPosition = position;
            WpfRendering.CapturePng(() => new SharedReviewSurface { DataContext = model }, new Size(1000, 420),
                Path.Combine(destination, $"FV3-slider-{position}-before-left-after-right-zhCN-1000x420.png"));
        }
    }

    private static void ScrollListsToTop(RenderedTree tree)
    {
        tree.OfType<ScrollViewer>().Single(s => AutomationProperties.GetAutomationId(s) == "Home.Lists").ScrollToHome();
        tree.Root.UpdateLayout();
    }

    private sealed class FixedDiagnostics(EnvironmentReadinessReport report) : IEnvironmentDiagnostics
    {
        public EnvironmentReadinessReport Read() => report;
        public Task<EnvironmentReadinessReport> RunLiveChecksAsync(CancellationToken cancellationToken) => Task.FromResult(report);
    }
}
