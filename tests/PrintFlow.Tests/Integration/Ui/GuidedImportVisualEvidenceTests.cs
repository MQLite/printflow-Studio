using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Startup;
using PrintFlow.App.Views;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Infrastructure.Startup;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Tests.Integration.Persistence;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class GuidedImportVisualEvidenceTests
{
    [Theory]
    [InlineData(1000, 700)]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    public async Task Reviewed_resume_and_draft_decision_fit_supported_viewports(int width, int height)
    {
        using OperatorCultureScope culture = new("zh-CN"); using HomeScreenHarness h = new();
        string source = h.Inner.WriteBorderedSourcePng("继续处理—保留原件.png");
        var imported = (await h.Sessions.ImportAsync(WorkflowType.PrepareAsset, source, "继续处理的合成图案", "tester", default)).Value;
        await h.Home.RefreshCommand.ExecuteAsync(null);
        await h.Home.ResumeCommand.ExecuteAsync(h.Home.RecentSessions.Single());
        h.Navigation.SessionFor.ShouldBeNull();
        var route = h.WorkflowSelection(h.Navigation); route.Open(h.Navigation.WorkflowSelectionFor.ShouldNotBeNull());
        await route.PreviewLoaded;
        if (width == 1000)
            Capture(() => new WorkflowSelectionView { DataContext = route }, new Size(width, height), "review-resume-route", source,
                route.Workflows.Select(w => w.AutomationId).Append("WorkflowSelection.Back").ToArray());

        // A fresh model prevents sharing dispatcher-owned preview state between renders.
        var draft = h.WorkflowSelection(h.Navigation); draft.Open(imported); await draft.PreviewLoaded;
        draft.OutputName = "尚未保存的新输出名称"; await draft.BackToHomeCommand.ExecuteAsync(null);
        Capture(() => new WorkflowSelectionView { DataContext = draft }, new Size(width, height), "review-draft-leave", source,
            ["WorkflowSelection.KeepDraft", "WorkflowSelection.DiscardDraft"], tree =>
            {
                GuidedImportHomeTests.Find<TextBox>(tree, "WorkflowSelection.OutputName").IsEnabled.ShouldBeFalse();
                GuidedImportHomeTests.Find<Button>(tree, "WorkflowSelection.Back").IsEnabled.ShouldBeFalse();
                foreach (var choice in draft.Workflows) GuidedImportHomeTests.Find<Button>(tree, choice.AutomationId).IsEnabled.ShouldBeFalse();
            });
    }

    [Theory]
    [InlineData(1000, 700)]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    public async Task Chinese_home_routes_and_original_steps_fit_the_supported_viewports(int width, int height)
    {
        using OperatorCultureScope culture = new("zh-CN");
        using HomeScreenHarness h = new();
        h.StartupStatus.Publish(StartupStatus.Started(true, StartupRecoveryReport.Empty));
        string source = h.Inner.WriteBorderedSourcePng("合成图案—保留原件.png");
        var imported = (await h.Sessions.ImportAsync(WorkflowType.PrepareAsset, source, "待处理的合成图案", "tester", default)).Value;
        await h.Home.RefreshCommand.ExecuteAsync(null); await h.Home.ThumbnailsLoaded;
        Size size = new(width, height);
        Capture(() => new HomeView { DataContext = h.Home }, size, "home-continue", source,
            ["Home.ChooseFile", "Home.ResumeSession", "Home.ShowSettings"],
            tree => GuidedImportHomeTests.Find<Expander>(tree, "Home.HistoryDetails").IsExpanded.ShouldBeFalse());
        var route = h.WorkflowSelection(h.Navigation); route.Open(imported); await route.PreviewLoaded;
        Capture(() => new WorkflowSelectionView { DataContext = route }, size, "route-choice", source,
            route.Workflows.Select(w => w.AutomationId).Append("WorkflowSelection.Back").ToArray());
        foreach (var type in Enum.GetValues<WorkflowType>())
        {
            var state = (await h.Sessions.ImportAsync(type, source, "合成图案—" + type, "tester", default)).Value;
            var m = h.Session(h.Navigation); m.Open(state); await m.PreviewsLoaded;
            Capture(() => new SessionScreenView { DataContext = m }, size, "original-" + type, source,
                ["Session.ConfirmOriginal", "Session.ChangeRoute", "Session.BackToHome"], tree =>
                {
                    GuidedSessionProjectionTests.Shown(GuidedImportHomeTests.Find<ListBox>(tree, "Session.WhiteUnderbaseChoices")).ShouldBeFalse();
                    GuidedSessionProjectionTests.Shown(GuidedImportHomeTests.Find<ItemsControl>(tree, "Session.SizePresetList")).ShouldBeFalse();
                });
        }
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData("psd")]
    public async Task Chinese_preparation_and_source_review_retain_real_input_context(string format)
    {
        using OperatorCultureScope culture = new("zh-CN"); using SessionServiceHarness h = new();
        var service = format == "pdf" ? PdfPreparationWorkflowTests.Service(h, new WindowsPdfPreparationProcessor(h.FileWorkspace, h.FileInspector))
            : h.CreateServiceWithPhotoshop(new PsdPreparationWorkflowTests.PsdProcessor(h.FileWorkspace));
        string source = h.Workspace.CreateSourceFile("合成来源." + format,
            format == "pdf" ? PdfFixtures.Read("single") : PsdInputPreparationTests.RgbCompositePsd());
        var state = (await service.ImportAsync(WorkflowType.GeneratePrintTiff, source, "合成源图准备", "tester", default)).Value;
        var driver = new SessionViewModel(service, h.Previews, h.TiffReviews, hnav()); driver.Open(state);
        await Snapshot("pending", "Session.RunStep");
        await driver.RunStepCommand.ExecuteAsync(null);
        driver.GuidedPage.ShouldBe(GuidedSessionPage.Review);
        await Snapshot("review", "Session.Approve");
        async Task Snapshot(string suffix, string action)
        {
            var m = new SessionViewModel(service, h.Previews, h.TiffReviews, hnav());
            m.Open((await service.LoadAsync(state.Id, default)).Value); await m.PreviewsLoaded;
            Capture(() => new SessionScreenView { DataContext = m }, new Size(1000, 700), format + "-" + suffix, source, [action, "Session.BackToHome"]);
        }
        static RecordingNavigation hnav() => new();
    }

    [Fact]
    public async Task Chinese_import_progress_and_refusal_are_truthful_and_cancellable()
    {
        using OperatorCultureScope culture = new("zh-CN"); using HomeScreenHarness h = new();
        h.StartupStatus.Publish(StartupStatus.Started(true, StartupRecoveryReport.Empty));
        await h.Home.DropFilesCommand.ExecuteAsync(["one", "two"]);
        Capture(() => new HomeView { DataContext = h.Home }, new Size(1000, 700), "import-refused-multiple", null, ["Home.ChooseFile"]);
        var service = DispatchProxy.Create<ISessionService, GuidedSessionInputTests.DeferredService>();
        var proxy = (GuidedSessionInputTests.DeferredService)service; proxy.Inner = h.Sessions;
        var m = new HomeViewModel(service, h.Previews, h.Navigation, h.FilePicker, h.StartupStatus, h.Readiness);
        h.FilePicker.Path = h.WriteSourceFile("正在导入的合成图.png");
        var gate = proxy.HoldNext(nameof(ISessionService.ImportAsync));
        Task running = m.ChooseFileCommand.ExecuteAsync(null); await gate.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            Capture(() => new HomeView { DataContext = m }, new Size(1000, 700), "import-pending", h.FilePicker.Path, ["Home.CancelImport"], tree =>
            {
                GuidedImportHomeTests.Find<Button>(tree, "Home.ChooseFile").IsEnabled.ShouldBeFalse();
                GuidedImportHomeTests.Find<TextBlock>(tree, "Home.ImportProgress").Text.ShouldNotBeNullOrWhiteSpace();
            });
            m.CancelImportCommand.Execute(null);
        }
        finally { gate.Release.SetResult(); await running; }
        h.Navigation.WorkflowSelectionFor.ShouldBeNull();
    }

    private static void Capture(Func<UserControl> create, Size size, string state, string? source, string[] essential, Action<RenderedTree>? inspect = null)
    {
        var result = WpfRendering.RenderExpectingNoBindingErrors(create, size, tree =>
        {
            foreach (string id in essential)
            {
                var button = GuidedImportHomeTests.Find<Button>(tree, id);
                GuidedSessionProjectionTests.Shown(button).ShouldBeTrue(id); button.IsEnabled.ShouldBeTrue(id);
                var b = Bounds(button, tree.Root);
                b.Left.ShouldBeGreaterThanOrEqualTo(-0.5, id); b.Top.ShouldBeGreaterThanOrEqualTo(-0.5, id);
                b.Right.ShouldBeLessThanOrEqualTo(size.Width + 0.5, id); b.Bottom.ShouldBeLessThanOrEqualTo(size.Height + 0.5, id);
                foreach (var scroll in tree.OfType<ScrollViewer>().Where(s => s.IsAncestorOf(button)))
                {
                    var withinScroll = Bounds(button, scroll);
                    withinScroll.Top.ShouldBeGreaterThanOrEqualTo(-0.5, id);
                    withinScroll.Bottom.ShouldBeLessThanOrEqualTo(scroll.ViewportHeight + 0.5, id);
                }
            }
            inspect?.Invoke(tree);
            return new
            {
                Actions = tree.OfType<Button>().Where(b => GuidedSessionProjectionTests.Shown(b) && b.ActualHeight > 0 && tree.Root.IsAncestorOf(b))
                    .Select(b => new { Id = AutomationProperties.GetAutomationId(b), b.IsEnabled,
                        CanExecute = b.Command?.CanExecute(b.CommandParameter), Bounds = Measure(Bounds(b, tree.Root)) }).ToArray(),
                Scrolls = tree.OfType<ScrollViewer>().Where(s => GuidedSessionProjectionTests.Shown(s) && s.ActualHeight > 0 && tree.Root.IsAncestorOf(s))
                    .Select(s => new { s.VerticalOffset, s.ScrollableHeight, s.ViewportHeight }).ToArray(),
                Previews = tree.OfType<Image>().Count(i => i.Source is not null && GuidedSessionProjectionTests.Shown(i)),
            };
        });
        string? directory = Environment.GetEnvironmentVariable("PF_11170_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        string stem = state + $"-{size.Width:0}x{size.Height:0}"; string path = Path.Combine(directory, stem + ".png");
        WpfRendering.CapturePng(create, size, path);
        File.WriteAllText(Path.Combine(directory, stem + ".json"), JsonSerializer.Serialize(new
        {
            State = state, Language = "zh-CN", WidthDip = size.Width, HeightDip = size.Height, Dpi = 96,
            CaptureKind = "Synthetic offscreen WPF RenderTargetBitmap; no Window.Show or desktop input",
            SourceSha256 = source is null ? null : Hash(source), ScreenshotSha256 = Hash(path), EssentialActions = essential, Measurements = result.Facts,
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static Rect Bounds(FrameworkElement item, Visual root) => item.TransformToAncestor(root).TransformBounds(new Rect(item.RenderSize));
    private static object Measure(Rect r) => new { r.X, r.Y, r.Width, r.Height };
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
