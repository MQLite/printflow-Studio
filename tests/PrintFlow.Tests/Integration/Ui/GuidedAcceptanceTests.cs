using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Delivery;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class GuidedAcceptanceTests
{
    // Catches loss of meaningful names or tab eligibility on the current guided action/input.
    // This observes WPF peers/properties, not OS Tab traversal or a screen reader.
    [Theory]
    [InlineData("source", "Session.ConfirmOriginal")]
    [InlineData("review", "Session.Approve")]
    [InlineData("dimensions", "Session.PrintDimensions.ConfirmSize")]
    public async Task Current_actions_and_inputs_expose_names_and_hidden_steps_stay_out_of_candidate_tab_set(string state, string action)
    {
        using OperatorCultureScope culture = new("zh-CN");
        using HomeScreenHarness h = new();
        var model = await GuidedSessionProjectionTests.Open(h,
            state == "dimensions" ? WorkflowType.GeneratePrintTiff : WorkflowType.PrepareAsset);
        if (state != "source") await model.ConfirmOriginalCommand.ExecuteAsync(null);
        if (state == "review") await model.RunStepCommand.ExecuteAsync(null);
        if (state == "dimensions")
        {
            model.ChooseCustomSizeCommand.Execute(null);
            model.SelectedTargetEdgeChoice = model.TargetEdgeChoices.Single(c => c.Edge == TargetEdge.Width);
            model.CustomMillimetresText = "150";
            await model.PreflightLoaded;
        }
        WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = model }, new Size(1000, 700), tree =>
        {
            try
            {
                Settle(tree.Root);
                var controls = GuidedCompletionSaveTests.Descendants(tree.Root).OfType<Control>().ToArray();
                var current = controls.Where(c => GuidedSessionProjectionTests.Shown(c) && c.IsEnabled && c.Focusable && KeyboardNavigation.GetIsTabStop(c)).ToArray();
                current.ShouldContain(c => AutomationProperties.GetAutomationId(c) == action);
                foreach (var control in current.Where(c => !string.IsNullOrEmpty(AutomationProperties.GetAutomationId(c))))
                {
                    string name = UIElementAutomationPeer.CreatePeerForElement(control)!.GetName();
                    name.ShouldNotBeNullOrWhiteSpace(AutomationProperties.GetAutomationId(control));
                    name.Contains("PrintFlow.", StringComparison.Ordinal).ShouldBeFalse("operator-facing names must not expose CLR types");
                }
                string hiddenAction = state == "source" ? "Session.Approve" : "Session.ConfirmOriginal";
                controls.ShouldContain(c => AutomationProperties.GetAutomationId(c) == hiddenAction);
                current.ShouldNotContain(c => AutomationProperties.GetAutomationId(c) == hiddenAction);
                foreach (var button in current.OfType<Button>()) button.IsDefault.ShouldBeFalse();
                return true;
            }
            finally { tree.Root.DataContext = null; tree.Root.UpdateLayout(); }
        });
    }

    // Catches long task names crowding the current image or fixed primary action.
    [Theory]
    [InlineData(1000, 700)]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    public async Task Long_Chinese_source_keeps_image_and_primary_action_visible(int width, int height)
    {
        using OperatorCultureScope culture = new("zh-CN");
        using HomeScreenHarness h = new();
        string name = "合成验收任务需要保留完整设计边缘与透明背景请逐步核对当前图片再继续处理";
        var view = (await h.Sessions.ImportAsync(WorkflowType.PrepareAsset, h.Inner.WriteBorderedSourcePng("合成中文原图.png"), name, "tester", default)).Value;
        var model = h.Session(new RecordingNavigation()); model.Open(view); await model.PreviewsLoaded;
        Capture(model, new(width, height), "long-chinese-source", ["Session.ConfirmOriginal"], true);
    }

    // Catches multi-size target rows and a long Chinese filename pushing save inputs/actions offscreen.
    [Theory]
    [InlineData(1000, 700)]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    public async Task Multiple_sizes_and_long_Chinese_filename_keep_current_save_target_reachable(int width, int height)
    {
        using OperatorCultureScope culture = new("zh-CN");
        using SessionServiceHarness h = new();
        var workflow = FinalSaveFixtures.TiffService(h);
        var first = await FinalSaveFixtures.TiffAtFinalReviewAsync(h, workflow);
        string folder = FinalSaveFixtures.NewFolder("e6-sizes");
        try
        {
            var delivery = FinalSaveFixtures.Delivery(h);
            var saved = await new FinalSaveCoordinator(workflow, delivery, "tester").ConfirmAndSaveAsync(
                FinalSaveFixtures.Confirm(first, folder, "first.tif"), null, default);
            saved.Delivery!.Code.ShouldBe(DeliveryCode.Delivered);
            (await workflow.ExecuteAsync(first.Id, new WorkflowCommand.Complete(), "tester", default)).IsSuccess.ShouldBeTrue();
            (await workflow.ExecuteAsync(first.Id, new WorkflowCommand.AddAnotherSize(), "tester", default)).IsSuccess.ShouldBeTrue();
            var second = await FinalSaveFixtures.NextTiffSizeAsync(workflow, first.Id, 250);
            var model = await GuidedCompletionSaveTests.Open(h, workflow, delivery, new GuidedCompletionSaveTests.Picker(folder), second);
            await model.ApproveCommand.ExecuteAsync(null); await model.FinalSaveFactsLoaded; await model.PreviewsLoaded;
            model.ChangeLocationCommand.Execute(null);
            model.SaveFileNameText = "合成中文第二尺寸待交付文件请核对当前审核与保存记录.tif";
            model.FinalSaveTargets.Count.ShouldBe(2);
            // Keep the command driver off the render STA: WPF collection views can retain
            // subscriptions after an offscreen tree is detached (as in the E5 fixture).
            var saveSnapshot = await GuidedCompletionSaveTests.Open(h, workflow, delivery, new GuidedCompletionSaveTests.Picker(folder),
                (await workflow.LoadAsync(model.Id, default)).Value);
            saveSnapshot.ChangeLocationCommand.Execute(null);
            saveSnapshot.SaveFileNameText = model.SaveFileNameText;
            Capture(saveSnapshot, new(width, height), "multisize-long-name", ["Session.FinalSave.Target", "Session.FinalSave.FileName", "Session.FinalSave.Folder", "Session.FinalSave.SaveApproved"], true);
            await model.SaveApprovedCommand.ExecuteAsync(null); await model.FinalSaveFactsLoaded;
            await model.CompleteCommand.ExecuteAsync(null); await model.FinalSaveFactsLoaded; await model.PreviewsLoaded;
            model.GuidedPage.ShouldBe(GuidedSessionPage.Completed);
            Capture(model, new(width, height), "multisize-completed", [], true);
        }
        finally { FinalSaveFixtures.Remove(folder); }
    }

    private static void Capture(SessionViewModel model, Size viewport, string state, string[] essential, bool requireImage)
    {
        Func<UserControl> create = () => new SessionScreenView { DataContext = model };
        var result = WpfRendering.RenderExpectingNoBindingErrors(create, viewport, tree =>
        {
            try
            {
                Settle(tree.Root);
                var all = GuidedCompletionSaveTests.Descendants(tree.Root).OfType<FrameworkElement>().ToArray();
                var facts = all.Where(e => GuidedSessionProjectionTests.Shown(e) && !string.IsNullOrEmpty(AutomationProperties.GetAutomationId(e)))
                    .Select(e => new { Id = AutomationProperties.GetAutomationId(e), e.IsEnabled, Bounds = Bounds(e, tree.Root),
                        WithinScroll = all.OfType<ScrollViewer>().Where(s => s.IsAncestorOf(e)).All(s => Bounds(e, s).Top >= -0.5 && Bounds(e, s).Bottom <= s.ViewportHeight + 0.5) }).ToArray();
                foreach (var id in essential)
                {
                    var item = facts.Single(f => f.Id == id);
                    item.IsEnabled.ShouldBeTrue(state + ": " + id);
                    Inside(item.Bounds, viewport); item.WithinScroll.ShouldBeTrue(state + ": " + id + " " +
                        string.Join("; ", all.OfType<ScrollViewer>().Where(s => s.IsAncestorOf(all.Single(e => AutomationProperties.GetAutomationId(e) == id)))
                            .Select(s => $"{s.Name}: viewport={s.ViewportHeight:0.##}, field={Bounds(all.Single(e => AutomationProperties.GetAutomationId(e) == id), s)}")));
                }
                var images = all.OfType<Image>().Where(i => i.Source is not null && GuidedSessionProjectionTests.Shown(i)).ToArray();
                if (requireImage)
                {
                    images.ShouldNotBeEmpty();
                    foreach (var image in images) Inside(Bounds(image, tree.Root), viewport);
                    images.Max(i => i.ActualHeight).ShouldBeGreaterThan(100);
                }
                Rect? historyAfterScroll = null;
                if (state.StartsWith("multisize", StringComparison.Ordinal))
                {
                    var list = all.OfType<ItemsControl>().Single(e => AutomationProperties.GetAutomationId(e) == "Session.OutputList");
                    list.Items.Count.ShouldBe(2, "both sizes retain their output history");
                    var names = GuidedCompletionSaveTests.Descendants(list).OfType<TextBlock>()
                        .Where(t => AutomationProperties.GetAutomationId(t) == "Session.OutputFileName").Select(t => t.Text).ToArray();
                    names.ShouldContain("final-tiff_200mm_CMYK_W.tif");
                    names.ShouldContain("final-tiff_250mm_CMYK_W.tif");
                    var scroll = all.OfType<ScrollViewer>().Single(s => s.IsAncestorOf(list));
                    scroll.ScrollToVerticalOffset(scroll.VerticalOffset + Bounds(list, scroll).Top);
                    Settle(tree.Root);
                    historyAfterScroll = Bounds(list, scroll);
                    historyAfterScroll.Value.Top.ShouldBeGreaterThanOrEqualTo(-0.5);
                    historyAfterScroll.Value.Bottom.ShouldBeLessThanOrEqualTo(scroll.ViewportHeight + 0.5);
                    foreach (var image in images) image.ActualHeight.ShouldBeGreaterThan(100, "scrolling output history cannot crowd the image");
                }
                return new { Elements = facts, Images = images.Select(i => Bounds(i, tree.Root)).ToArray(), HistoryAfterScroll = historyAfterScroll };
            }
            finally { tree.Root.DataContext = null; tree.Root.UpdateLayout(); }
        });
        string? directory = Environment.GetEnvironmentVariable("PF_11173_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        string stem = $"{state}-{viewport.Width:0}x{viewport.Height:0}";
        string path = Path.Combine(directory, stem + ".png");
        WpfRendering.CapturePng(create, viewport, path, t => Settle(t.Root));
        File.WriteAllText(Path.Combine(directory, stem + ".json"), JsonSerializer.Serialize(new {
            State = state, Language = "zh-CN", WidthDip = viewport.Width, HeightDip = viewport.Height, Dpi = 96,
            CaptureKind = "Synthetic offscreen WPF; no desktop, physical input or real DPI evidence",
            ScreenshotSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), Essential = essential, Measurements = result.Facts,
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static Rect Bounds(FrameworkElement e, Visual root) => e.TransformToAncestor(root).TransformBounds(new Rect(e.RenderSize));
    private static void Inside(Rect r, Size viewport)
    {
        r.Left.ShouldBeGreaterThanOrEqualTo(-0.5); r.Top.ShouldBeGreaterThanOrEqualTo(-0.5);
        r.Right.ShouldBeLessThanOrEqualTo(viewport.Width + 0.5); r.Bottom.ShouldBeLessThanOrEqualTo(viewport.Height + 0.5);
        r.Height.ShouldBeGreaterThan(0);
    }
    private static void Settle(UserControl root) { Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.SystemIdle); root.UpdateLayout(); }
}
