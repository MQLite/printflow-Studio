using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Infrastructure.Adapters.Fake;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class GuidedSessionVisualEvidenceTests
{
    [Theory]
    [InlineData(1000, 700)]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    public async Task Chinese_guided_states_have_visible_actions_and_capture_real_Wpf(int width, int height)
    {
        using OperatorCultureScope culture = new("zh-CN");
        using HomeScreenHarness h = new();
        string source = h.Inner.WriteBorderedSourcePng("合成透明边缘—导航验收.png");
        string hash = Hash(source);
        Size viewport = new(width, height);
        SessionViewModel? asset = null;
        SessionViewModel? print = null;
        foreach (var route in Enum.GetValues<WorkflowType>())
        {
            var state = (await h.Sessions.ImportAsync(route, source, "合成图片—引导流程", "tester", default)).Value;
            var model = h.Session(new RecordingNavigation());
            model.Open(state); await model.PreviewsLoaded;
            await CaptureSnapshot(h, model, viewport, "source-" + route, hash);
            if (route == WorkflowType.PrepareAsset) asset = model;
            if (route == WorkflowType.GeneratePrintTiff) print = model;
        }
        var m = asset!;
        await m.ConfirmOriginalCommand.ExecuteAsync(null);
        m.SelectedReturnTarget = m.ReturnTargets.First(); m.BeginReturnCommand.Execute(null);
        await CaptureSnapshot(h, m, viewport, "return-confirmation", hash);
        m.CancelReturnCommand.Execute(null);
        h.Inner.FakeMeitu.SetScenario(FakeAdapterScenario.WaitForStopAt(ExternalOperationPhase.Busy));
        Task running = m.RunStepCommand.ExecuteAsync(null);
        await h.Inner.FakeMeitu.HangStarted;
        try
        {
            m.GuidedPage.ShouldBe(GuidedSessionPage.Processing);
            await CaptureSnapshot(h, m, viewport, "processing-stop-takeover", hash);
        }
        finally { m.StopAutomationCommand.Execute(null); await running; }
        await CaptureSnapshot(h, m, viewport, "failure-stopped", hash);
        var recovered = h.Session(new RecordingNavigation());
        recovered.Open((await h.Sessions.LoadAsync(m.Id, default)).Value);
        await recovered.PreviewsLoaded;
        int starts = h.Meitu.CallCount;
        await CaptureSnapshot(h, recovered, viewport, "recovered-stopped", hash);
        h.Meitu.CallCount.ShouldBe(starts);
        h.Inner.FakeMeitu.SetScenario(FakeAdapterScenario.Succeed);
        await m.RetryCommand.ExecuteAsync(null);
        await m.RunStepCommand.ExecuteAsync(null);
        m.GuidedPage.ShouldBe(GuidedSessionPage.Review);
        await CaptureSnapshot(h, m, viewport, "review-required", hash);
        m.ViewHistoryCommand.Execute(m.Steps.First()); await m.HistoryPreviewLoaded;
        await CaptureSnapshot(h, m, viewport, "history-readonly", hash);
        m.CloseHistoryCommand.Execute(null);
        await print!.ConfirmOriginalCommand.ExecuteAsync(null);
        print.NextStepText.ShouldContain("尺寸");
        print.NextStepText.ShouldNotContain("白墨");
        await CaptureSnapshot(h, print, viewport, "dimensions", hash);
        print.WidthMmText = "200"; print.HeightMmText = "150";
        await print.BackToHomeCommand.ExecuteAsync(null);
        print.IsConfirmingDraftLeave.ShouldBeTrue();
        await CaptureSnapshot(h, print, viewport, "draft-leave", hash);
        print.KeepDraftCommand.Execute(null);
        await print.SetMaximumBoundsCommand.ExecuteAsync(null);
        await CaptureSnapshot(h, print, viewport, "white-ink-explicit", hash);
        print.SelectedWhiteUnderbaseChoice = print.WhiteUnderbaseChoices[1];
        await print.SelectWhiteUnderbaseCommand.ExecuteAsync(null);
        await CaptureSnapshot(h, print, viewport, "preflight", hash);
        Hash(source).ShouldBe(hash);
        string? directory = Environment.GetEnvironmentVariable("PF_11169_CAPTURE_DIR");
        if (!string.IsNullOrWhiteSpace(directory)) File.Copy(source, Path.Combine(directory, "synthetic-source.png"), true);
    }

    private static async Task CaptureSnapshot(HomeScreenHarness h, SessionViewModel live, Size viewport, string state, string sourceHash)
    {
        // A WPF CollectionView belongs to its rendering STA. Use a freshly loaded view model
        // for each state; the driving model remains unbound and can continue on the test thread.
        var model = h.Session(new RecordingNavigation());
        model.Open((await h.Sessions.LoadAsync(live.Id, default)).Value);
        await model.PreviewsLoaded;
        if (live.IsConfirmingReturn)
        {
            model.SelectedReturnTarget = model.ReturnTargets.Single(t => t.Step == live.SelectedReturnTarget!.Step);
            model.BeginReturnCommand.Execute(null);
        }
        if (live.IsViewingHistory)
        {
            model.ViewHistoryCommand.Execute(model.Steps.First()); await model.HistoryPreviewLoaded;
        }
        if (live.IsConfirmingDraftLeave)
        {
            model.WidthMmText = live.WidthMmText; model.HeightMmText = live.HeightMmText;
            await model.BackToHomeCommand.ExecuteAsync(null);
        }
        Capture(model, viewport, state, sourceHash);
    }

    private static void Capture(SessionViewModel model, Size viewport, string state, string sourceHash)
    {
        Func<UserControl> create = () => new SessionScreenView { DataContext = model };
        var result = WpfRendering.RenderExpectingNoBindingErrors(create, viewport, tree =>
        {
            Settle(tree.Root);
            var actions = tree.OfType<Button>().Where(b => GuidedSessionProjectionTests.Shown(b) && b.ActualHeight > 0).ToArray();
            FrameworkElement panel = GuidedSessionShellTests.Find<FrameworkElement>(tree,
                model.IsConfirmingDraftLeave ? "Session.DraftLeave" : model.IsConfirmingReturn ? "Session.ReturnConfirmation" :
                model.IsViewingHistory ? "Session.HistoryWorkspace" : "Session.ActionBar");
            Inside(Bounds(panel, tree.Root), viewport, state);
            if (model.ShowsGuidedWhiteInk)
                Inside(Bounds(GuidedSessionShellTests.Find<ListBox>(tree, "Session.WhiteUnderbaseChoices"), tree.Root), viewport, "white-ink choices");
            if (model.CanStopAutomation)
            {
                foreach (string id in new[] { "Session.Stop", "Session.TakeOver" })
                {
                    var button = GuidedSessionShellTests.Find<Button>(tree, id);
                    button.IsEnabled.ShouldBeTrue(); GuidedSessionProjectionTests.Shown(button).ShouldBeTrue();
                    Inside(Bounds(button, tree.Root), viewport, id);
                }
            }
            var recommended = actions.Where(b => ReferenceEquals(b.Command, model.RecommendedCommand)).ToArray();
            if (!model.IsBusy && model.RecommendedCommand is not null)
            {
                recommended.ShouldNotBeEmpty(state + ": current recommendation has a visible control");
                foreach (var button in recommended) Inside(Bounds(button, tree.Root), viewport, state);
            }
            if (model.IsViewingHistory)
                GuidedSessionProjectionTests.Shown(GuidedSessionShellTests.Find<FrameworkElement>(tree, "Session.CurrentWorkspace")).ShouldBeFalse();
            if (model.IsConfirmingReturn || model.IsConfirmingDraftLeave)
                GuidedSessionShellTests.Find<FrameworkElement>(tree, "Session.ActionBar").IsEnabled.ShouldBeFalse();
            return new
            {
                MainSurface = Measure(Bounds(panel, tree.Root)),
                FocusName = (FocusManager.GetFocusedElement(FocusManager.GetFocusScope(tree.Root)) as FrameworkElement)?.Name,
                Actions = actions.Select(b => new { Id = AutomationProperties.GetAutomationId(b), b.IsEnabled,
                    Bounds = Measure(Bounds(b, tree.Root)), Recommended = ReferenceEquals(b.Command, model.RecommendedCommand) }).ToArray(),
                Scrolls = tree.OfType<ScrollViewer>().Where(s => GuidedSessionProjectionTests.Shown(s))
                    .Select(s => new { s.VerticalOffset, s.ScrollableHeight, s.ViewportHeight }).ToArray(),
                PreviewCount = tree.OfType<Image>().Count(i => i.Source is not null && GuidedSessionProjectionTests.Shown(i)),
            };
        });
        string? directory = Environment.GetEnvironmentVariable("PF_11169_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        string stem = $"{state}-{viewport.Width:0}x{viewport.Height:0}";
        string png = Path.Combine(directory, stem + ".png");
        WpfRendering.CapturePng(create, viewport, png, tree => Settle(tree.Root));
        File.WriteAllText(Path.Combine(directory, stem + ".json"), JsonSerializer.Serialize(new
        {
            State = state, Page = model.GuidedPage.ToString(), Language = "zh-CN", WidthDip = viewport.Width,
            HeightDip = viewport.Height, Dpi = 96, CaptureKind = "offscreen WPF; no Window.Show or desktop input",
            SourceSha256 = sourceHash, ScreenshotSha256 = Hash(png), Measurements = result.Facts,
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static Rect Bounds(FrameworkElement item, Visual root) => item.TransformToAncestor(root).TransformBounds(new Rect(item.RenderSize));
    private static object Measure(Rect r) => new { r.X, r.Y, r.Width, r.Height };
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
