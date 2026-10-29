using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Adapters.Fake;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class GuidedProcessingVisualEvidenceTests
{
    [Theory]
    [InlineData(1000, 700)]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    public async Task Chinese_processing_review_crop_and_print_states_fit_the_current_workspace(int width, int height)
    {
        using OperatorCultureScope culture = new("zh-CN");
        Size viewport = new(width, height);
        using HomeScreenHarness h = new();
        var driver = await GuidedSessionProjectionTests.Open(h, WorkflowType.PrepareAsset);
        await driver.ConfirmOriginalCommand.ExecuteAsync(null);
        h.Inner.FakeMeitu.SetScenario(FakeAdapterScenario.WaitForStopAt(ExternalOperationPhase.Busy));
        Task run = driver.RunStepCommand.ExecuteAsync(null);
        await h.Inner.FakeMeitu.HangStarted;
        var running = await Snapshot(h, driver);
        var runningView = (await h.Sessions.LoadAsync(driver.Id, default)).Value;
        AutomationRuntimeView? stoppingRuntime = null;
        try
        {
            Capture(running, viewport, "processing", ["Session.Stop", "Session.TakeOver"]);
            if (width == 1000)
            {
                void OnStopping(object? _, AutomationRuntimeView runtime)
                {
                    if (runtime.State == AutomationRuntimeState.StopRequested) stoppingRuntime = runtime;
                }
                h.Sessions.AutomationRuntimeChanged += OnStopping;
                try { driver.StopAutomationCommand.Execute(null); }
                finally { h.Sessions.AutomationRuntimeChanged -= OnStopping; }
                stoppingRuntime.ShouldNotBeNull();
            }
        }
        finally { if (driver.CanStopAutomation) driver.StopAutomationCommand.Execute(null); await run; }
        if (width == 1000)
        {
            // The fake ends immediately after Stop; freeze the real observed runtime for a stable render.
            // Otherwise its concurrent Idle event can relabel the screenshot while WPF is drawing.
            var stoppingService = new ScriptedFailureSessionService(h.Sessions) { Runtime = stoppingRuntime };
            var stopping = new SessionViewModel(stoppingService, h.Previews, h.TiffReviews, new RecordingNavigation());
            stopping.Open(runningView);
            await stopping.PreviewsLoaded;
            stopping.IsStopping.ShouldBeTrue();
            Capture(stopping, viewport, "stopping", []);
            Capture(await Snapshot(h, driver), viewport, "stopped-failure", ["Session.Retry"]);
            await driver.RetryCommand.ExecuteAsync(null);
            h.Inner.FakeMeitu.SetScenario(FakeAdapterScenario.WaitForStopAt(ExternalOperationPhase.Busy));
            Task takeoverRun = driver.RunStepCommand.ExecuteAsync(null);
            await h.Inner.FakeMeitu.HangStarted;
            try
            {
                var takeover = await Snapshot(h, driver);
                takeover.BeginTakeOverCommand.Execute(null);
                Capture(takeover, viewport, "takeover-confirmation", ["Session.TakeOverConfirm", "Session.TakeOverCancel"]);
            }
            finally { driver.BeginTakeOverCommand.Execute(null); driver.ConfirmTakeOverCommand.Execute(null); await takeoverRun; }
            Capture(await Snapshot(h, driver), viewport, "handed-off", ["Session.ReenterAutomation"]);
        }

        foreach (string step in width == 1000 ? ReviewGuidanceSetup.Steps : new[] { "enhancement" })
        {
            using SessionServiceHarness reviewHarness = new();
            var review = (await ReviewGuidanceSetup.OpenAtAsync(reviewHarness, step)).Screen;
            Capture(review, viewport, "review-" + step, ["Session.Approve", "Session.Reject"]);
        }
        using (SessionServiceHarness cropHarness = new())
        {
            var crop = (await ReviewGuidanceSetup.OpenAtAsync(cropHarness, "trim")).Screen;
            crop.BeginTrimAdjustCommand.Execute(null);
            Capture(crop, viewport, "crop", ["Session.TrimAdjust.Cancel", "Session.TrimAdjust.Restore", "Session.TrimAdjust.Use"]);
            if (width == 1000)
            {
                crop.ToggleTrimCompareCommand.Execute(null);
                Capture(crop, viewport, "crop-comparison", ["Session.TrimAdjust.Cancel", "Session.TrimAdjust.CompareToggle", "Session.TrimAdjust.Use"]);
            }
        }

        var sizeDriver = await PrintSizeGuidanceSetup.AtDimensionsAsync(h);
        var dimensions = await Snapshot(h, sizeDriver);
        dimensions.ChooseCustomSizeCommand.Execute(null);
        dimensions.SelectedTargetEdgeChoice = dimensions.TargetEdgeChoices.Single(c => c.Edge == TargetEdge.Width);
        dimensions.CustomMillimetresText = "250";
        await dimensions.PreflightLoaded;
        Capture(dimensions, viewport, "dimensions", ["Session.PrintDimensions.ConfirmSize"]);
        sizeDriver.WidthMmText = "200"; sizeDriver.HeightMmText = "150";
        await sizeDriver.SetMaximumBoundsCommand.ExecuteAsync(null);
        Capture(await Snapshot(h, sizeDriver), viewport, "white-ink", []);
        sizeDriver.SelectedWhiteUnderbaseChoice = sizeDriver.WhiteUnderbaseChoices[1];
        await sizeDriver.SelectWhiteUnderbaseCommand.ExecuteAsync(null);
        Capture(await Snapshot(h, sizeDriver), viewport, "preflight", ["Session.RunStep"]);

        if (width == 1000)
        {
            var invalid = await PrintSizeGuidanceSetup.OpenStateAsync(h, "invalid");
            Capture(invalid, viewport, "invalid-size", []);
            var enlargement = await PrintSizeGuidanceSetup.OpenStateAsync(h, "enlargement");
            enlargement.SelectedWhiteUnderbaseChoice = enlargement.WhiteUnderbaseChoices[1];
            await enlargement.SelectWhiteUnderbaseCommand.ExecuteAsync(null);
            Capture(enlargement, viewport, "enlargement-blocked", []);
        }
    }

    private static async Task<SessionViewModel> Snapshot(HomeScreenHarness h, SessionViewModel live)
    {
        var model = h.Session(new RecordingNavigation());
        model.Open((await h.Sessions.LoadAsync(live.Id, default)).Value);
        await model.PreviewsLoaded;
        return model;
    }

    private static void Capture(SessionViewModel model, Size viewport, string state, string[] essential)
    {
        Func<UserControl> create = () => new SessionScreenView { DataContext = model };
        var result = WpfRendering.RenderExpectingNoBindingErrors(create, viewport, tree =>
        {
            try
            {
            Settle(tree.Root);
            tree = new RenderedTree(tree.Root, All(tree.Root).ToArray());
            return new
            {
                Actions = tree.OfType<Button>().Where(GuidedSessionProjectionTests.Shown)
                    .Select(b => new
                    {
                        Id = AutomationProperties.GetAutomationId(b), b.IsEnabled, Bounds = Bounds(b, tree.Root),
                        WithinScroll = tree.OfType<ScrollViewer>().Where(s => s.IsAncestorOf(b)).All(s =>
                        {
                            var inside = Bounds(b, s);
                            return inside.Top >= -0.5 && inside.Bottom <= s.ViewportHeight + 0.5;
                        }),
                    }).ToArray(),
                Images = tree.OfType<Image>().Where(i => i.Source is not null && GuidedSessionProjectionTests.Shown(i))
                    .Select(i => Bounds(i, tree.Root)).ToArray(),
                WhiteChoices = tree.OfType<ListBox>().Where(b => AutomationProperties.GetAutomationId(b) == "Session.WhiteUnderbaseChoices" && GuidedSessionProjectionTests.Shown(b))
                    .Select(b => Bounds(b, tree.Root)).ToArray(),
                Page = model.GuidedPage.ToString(),
                ProcessingState = tree.OfType<TextBlock>().FirstOrDefault(t => AutomationProperties.GetAutomationId(t) == "Session.Processing.State")?.Text,
                CustomInputs = tree.OfType<TextBox>().Where(t => AutomationProperties.GetAutomationId(t) == "Session.PrintDimensions.TargetMillimetres" && GuidedSessionProjectionTests.Shown(t))
                    .Select(t => new { Bounds = Bounds(t, tree.Root), InScroll = tree.OfType<ScrollViewer>().Where(s => s.IsAncestorOf(t)).All(s => Bounds(t, s).Bottom <= s.ViewportHeight + 0.5) }).ToArray(),
                // U06 puts these facts in a scrollable details column. Verify every full
                // sentence through that path, retaining both root and scroll clipping bounds.
                Summary = tree.OfType<TextBlock>().Where(t => AutomationProperties.GetAutomationId(t).StartsWith("Session.PrintSummary.") && GuidedSessionProjectionTests.Shown(t))
                    .Select(t =>
                    {
                        t.BringIntoView();
                        ProcessingWorkspaceRolloutTests.Settle(tree.Root);
                        return new
                        {
                            Id = AutomationProperties.GetAutomationId(t), t.Text, Bounds = Bounds(t, tree.Root),
                            WithinScroll = tree.OfType<ScrollViewer>().Where(s => s.IsAncestorOf(t)).All(s =>
                            {
                                Rect inside = Bounds(t, s);
                                return inside.Top >= -0.5 && inside.Bottom <= s.ViewportHeight + 0.5
                                    && inside.Left >= -0.5 && inside.Right <= s.ViewportWidth + 0.5;
                            }),
                        };
                    }).ToArray(),
            };
            }
            finally { tree.Root.DataContext = null; tree.Root.UpdateLayout(); }
        });
        WriteEvidence();
        foreach (string id in essential)
        {
            var action = result.Facts.Actions.Single(a => a.Id == id);
            action.IsEnabled.ShouldBeTrue(state + ": " + id);
            Inside(action.Bounds, viewport, state + ": " + id);
            action.WithinScroll.ShouldBeTrue(state + ": " + id + " must not be clipped by a scroll viewport");
        }
        foreach (var choice in result.Facts.WhiteChoices) Inside(choice, viewport, state + ": explicit white-ink choices");
        if (state == "stopping") result.Facts.ProcessingState.ShouldBe(PrintFlow.App.Resources.Strings.Resolve("Session_GuidedStopping"));
        foreach (var text in result.Facts.Summary)
        {
            Inside(text.Bounds, viewport, state + ": " + text.Id);
            text.WithinScroll.ShouldBeTrue(state + ": " + text.Id + " must be reachable without clipping");
        }
        if (state is "dimensions" or "white-ink" or "preflight")
            result.Facts.Images.Max(r => r.Height).ShouldBeGreaterThan(100, state + ": print preparation keeps a usable image preview");
        foreach (var input in result.Facts.CustomInputs)
        {
            Inside(input.Bounds, viewport, state + ": custom-size input");
            input.InScroll.ShouldBeTrue(state + ": current input must be above the fold");
        }
        void WriteEvidence()
        {
            string? directory = Environment.GetEnvironmentVariable("PF_11171_CAPTURE_DIR");
            if (string.IsNullOrWhiteSpace(directory)) return;
            string? onlyState = Environment.GetEnvironmentVariable("PF_11171_CAPTURE_STATE");
            if (!string.IsNullOrWhiteSpace(onlyState) && onlyState != state) return;
            Directory.CreateDirectory(directory);
            string stem = $"{state}-{viewport.Width:0}x{viewport.Height:0}";
            string path = Path.Combine(directory, stem + ".png");
            WpfRendering.CapturePng(create, viewport, path, tree => Settle(tree.Root));
            File.WriteAllText(Path.Combine(directory, stem + ".json"), JsonSerializer.Serialize(new
            {
                State = state, Language = "zh-CN", WidthDip = viewport.Width, HeightDip = viewport.Height, Dpi = 96,
                CaptureKind = "Synthetic offscreen WPF RenderTargetBitmap; no App/Window.Show, desktop input or real external application",
                SummaryGeometry = "Each summary measured after BringIntoView; screenshot uses the initial scroll position. Assertions may fail after capture; consult TRX.",
                ScreenshotSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), EssentialActions = essential,
                Measurements = result.Facts,
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private static Rect Bounds(FrameworkElement item, Visual root) => item.TransformToAncestor(root).TransformBounds(new Rect(item.RenderSize));
    private static IEnumerable<DependencyObject> All(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var item in All(VisualTreeHelper.GetChild(root, i))) yield return item;
    }
    private static void Inside(Rect r, Size viewport, string label)
    {
        r.Left.ShouldBeGreaterThanOrEqualTo(-0.5, label); r.Top.ShouldBeGreaterThanOrEqualTo(-0.5, label);
        r.Right.ShouldBeLessThanOrEqualTo(viewport.Width + 0.5, label); r.Bottom.ShouldBeLessThanOrEqualTo(viewport.Height + 0.5, label);
        r.Height.ShouldBeGreaterThan(0, label);
    }
    private static void Settle(UserControl root)
    {
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.SystemIdle);
        root.UpdateLayout();
    }
}
