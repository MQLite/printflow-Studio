using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using PrintFlow.App.Navigation;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Delivery;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class PlainChinesePrintCopyTests
{
    public static TheoryData<WorkflowType, int, int, int> WhiteInkCases()
    {
        TheoryData<WorkflowType, int, int, int> cases = new();
        foreach (var route in new[] { WorkflowType.GeneratePrintTiff, WorkflowType.PrepareCustomerDesign })
            for (int branch = 0; branch < 3; branch++)
                foreach (var (width, height) in new[] { (984, 661), (1280, 720), (1920, 1080) })
                    cases.Add(route, branch, width, height);
        return cases;
    }

    [Theory]
    [MemberData(nameof(WhiteInkCases))]
    public async Task White_ink_explains_inset_without_choosing_and_preserves_each_recorded_classification(
        WorkflowType route, int branch, int width, int height)
    {
        using OperatorCultureScope culture = new("zh-CN");
        using HomeScreenHarness h = new();
        SessionViewModel driver = await AtWhiteInk(h, route);
        SessionViewModel model = await Snapshot(h, driver);
        model.SelectedWhiteUnderbaseChoice.ShouldBeNull();
        model.CanConfirmWhiteUnderbase.ShouldBeFalse();
        model.WhiteUnderbaseHint.ShouldContain("白色墨层");
        model.WhiteUnderbaseHint.ShouldContain("像素");
        model.WhiteUnderbaseHint.ShouldContain("没有默认");
        model.GuidedWhiteInkReasonHint.ShouldContain("0 像素仍有白墨");
        model.GuidedWhiteInkSourceHint.ShouldContain("不会沿用");
        model.GuidedWhiteInkSourceHint.ShouldContain("PSD/PDF");
        model.GuidedWhiteInkSourceHint.ShouldContain("W1");
        model.WhiteUnderbaseChoices[0].Label.ShouldContain("不内缩");
        string[] classifications = ["精细细节", "普通图案", "实色／整块矩形图案"];
        for (int i = 0; i < 3; i++)
        {
            model.WhiteUnderbaseChoices[i].Label.ShouldContain(i + " 像素");
            model.WhiteUnderbaseChoices[i].Label.ShouldContain(classifications[i]);
        }
        var viewport = new Size(width, height);
        WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = model }, viewport, tree =>
        {
            ListBox choices = GuidedSessionShellTests.Find<ListBox>(tree, "Session.WhiteUnderbaseChoices");
            GuidedSessionProjectionTests.Shown(choices).ShouldBeTrue();
            choices.SelectedIndex.ShouldBe(-1);
            new ListBoxAutomationPeer(choices).GetName().ShouldBe(model.WhiteUnderbaseHeading);
            choices.Items.Count.ShouldBe(3);
            for (int i = 0; i < choices.Items.Count; i++)
            {
                choices.ScrollIntoView(choices.Items[i]);
                tree.Root.UpdateLayout();
                var item = (ListBoxItem)choices.ItemContainerGenerator.ContainerFromIndex(i);
                UIElementAutomationPeer.CreatePeerForElement(item)!.GetName().ShouldBe(model.WhiteUnderbaseChoices[i].Label);
                FitsText(item, model.WhiteUnderbaseChoices[i].Label);
                // Scrollable explanations may be long; every choice must remain reachable.
                item.BringIntoView();
                tree.Root.UpdateLayout();
                Inside(item, tree.Root, viewport);
            }
            Button confirm = tree.OfType<Button>().Single(b => ReferenceEquals(b.Command, model.SelectWhiteUnderbaseCommand));
            confirm.IsEnabled.ShouldBeFalse();
            FitsText(confirm, model.ConfirmWhiteUnderbaseLabel);
            Inside(confirm, tree.Root, viewport);
            return 0;
        });
        await Capture(model, viewport, $"white-{route}-{branch}");

        var selected = model.WhiteUnderbaseChoices[branch];
        driver.SelectedWhiteUnderbaseChoice = driver.WhiteUnderbaseChoices[branch];
        await driver.SelectWhiteUnderbaseCommand.ExecuteAsync(null);
        model = await Snapshot(h, driver);
        model.GuidedPage.ShouldBe(GuidedSessionPage.Preflight);
        model.ConfirmedWhiteUnderbase.ShouldContain(selected.Label);
        var session = (await h.Sessions.LoadAsync(model.Id, default)).Value;
        session.WhiteUnderbaseBranch.ShouldBe(selected.Branch);
        WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = model }, viewport, tree =>
        {
            TextBlock contract = GuidedSessionShellTests.Find<TextBlock>(tree, "Session.PrintSummary.Contract");
            contract.Text.ShouldContain("TIFF");
            contract.Text.ShouldContain("300 PPI");
            contract.Text.ShouldContain("每英寸像素数");
            contract.Text.ShouldContain("CMYK");
            contract.Text.ShouldContain("W1");
            contract.Text.ShouldContain("人工审核");
            contract.Text.ShouldContain("再保存");
            // The full production contract scrolls with the print form; the live physical
            // size and any unauthorised enlargement risk stay above the image.
            contract.BringIntoView();
            ProcessingWorkspaceRolloutTests.Settle(tree.Root);
            Inside(contract, tree.Root, viewport);
            return 0;
        });
        await Capture(model, viewport, $"preflight-{route}-{branch}");
    }

    [Theory]
    [InlineData(984, 661)]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    public async Task Size_help_separates_maximum_from_exact_size_and_keeps_technical_explanation_accessible(int width, int height)
    {
        using OperatorCultureScope culture = new("zh-CN");
        using HomeScreenHarness h = new();
        var model = await PrintSizeGuidanceSetup.OpenStateAsync(h, "draft");
        model.MaximumBoundsHint.ShouldContain("细小方格");
        model.PresetModeHelp.ShouldContain("上限");
        model.PresetModeHelp.ShouldContain("按原比例");
        model.PresetModeHelp.ShouldContain("须先确认");
        model.CustomModeHelp.ShouldContain("单独授权");
        var viewport = new Size(width, height);
        WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = model }, viewport, tree =>
        {
            var expander = tree.OfType<Expander>().Single(e => Equals(e.Header, model.SizeTechnicalDetailsLabel) && GuidedSessionProjectionTests.Shown(e));
            expander.IsExpanded.ShouldBeFalse();
            FitsText(expander, model.SizeTechnicalDetailsLabel);
            expander.IsExpanded = true;
            tree.Root.UpdateLayout();
            new RenderedTree(tree.Root, All(tree.Root).ToArray()).OfType<TextBlock>()
                .ShouldContain(t => AutomationProperties.GetAutomationId(t) == "Session.PrintDimensions.OutputDpi" && t.Text.Contains("300"));
            return 0;
        });
        model.Preflight!.EnlargementAuthorised.ShouldBeFalse();
        await Capture(model, viewport, "dimensions-draft");
    }

    [Theory]
    [InlineData(984, 661)]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    public async Task Visible_risk_names_explicit_authorisation_and_rendering_never_accepts_it(int width, int height)
    {
        using OperatorCultureScope culture = new("zh-CN");
        using HomeScreenHarness h = new();
        var model = await PrintSizeGuidanceSetup.OpenStateAsync(h, "enlargement");
        model.SelectedWhiteUnderbaseChoice = model.WhiteUnderbaseChoices[1];
        await model.SelectWhiteUnderbaseCommand.ExecuteAsync(null);
        model.HasUsableEnlargementAuthority.ShouldBeFalse();
        model.GuidedPrintRisk.ShouldContain("模糊");
        model.GuidedPrintRisk.ShouldContain("单独授权");
        var viewport = new Size(width, height);
        WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = model }, viewport, tree =>
        {
            var risk = GuidedSessionShellTests.Find<TextBlock>(tree, "Session.PrintSummary.Risk");
            GuidedSessionProjectionTests.Shown(risk).ShouldBeTrue();
            GuidedSessionShellTests.Ancestors(risk).OfType<Expander>().ShouldBeEmpty();
            Inside(risk, tree.Root, viewport);
            Button authorise = tree.OfType<Button>().Single(b => ReferenceEquals(b.Command, model.ContinueWithSizeCommand));
            new ButtonAutomationPeer(authorise).GetName().ShouldContain("授权放大");
            FitsText(authorise, model.ContinueWithSizeLabel);
            Inside(authorise, tree.Root, viewport);
            return 0;
        });
        model.HasUsableEnlargementAuthority.ShouldBeFalse();
        model.CanRunStep.ShouldBeFalse();
        await Capture(model, viewport, "enlargement-blocked");
    }

    [Theory]
    [InlineData(984, 661)]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    public async Task Tiff_validation_and_preview_never_claim_print_quality_or_saved_state(int width, int height)
    {
        using OperatorCultureScope culture = new("zh-CN");
        using SessionServiceHarness h = new();
        var model = (await ReviewGuidanceSetup.OpenAtAsync(h, "tiff")).Screen;
        string? identity = model.ReviewTargetIdentity;
        model.TiffReviewSummary.ShouldContain("文件结构");
        model.TiffReviewCaveat.ShouldContain("不代表");
        model.TiffReviewCaveat.ShouldContain("印刷质量");
        model.TiffReviewCaveat.ShouldContain("不会保存");
        var viewport = new Size(width, height);
        WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = model }, viewport, tree =>
        {
            tree.OfType<TextBlock>().Where(GuidedSessionProjectionTests.Shown)
                .ShouldContain(t => t.Text == model.TiffReviewCaveat);
            return 0;
        });
        foreach (var mode in Enum.GetValues<TiffReviewMode>())
        {
            model.TiffReviewMode = mode;
            model.TiffPreviewLegend.ShouldContain(mode switch
            {
                TiffReviewMode.Colour => "不能据此", TiffReviewMode.Overlay => "不是最终印刷效果", _ => "白墨",
            });
        }
        model.ReviewTargetIdentity.ShouldBe(identity);
        model.IsReviewRequired.ShouldBeTrue();
        model.CanApprove.ShouldBeTrue();
        await Capture(model, viewport, "tiff-review-overlay");
    }

    [Fact]
    public async Task Review_caveat_distinguishes_standalone_review_when_confirm_and_save_is_available()
    {
        using OperatorCultureScope culture = new("zh-CN");
        using SessionServiceHarness h = new();
        var service = FinalSaveFixtures.TiffService(h);
        var review = await FinalSaveFixtures.TiffAtFinalReviewAsync(h, service);
        string folder = FinalSaveFixtures.NewFolder("c03-combined-save");
        try
        {
            var model = new SessionViewModel(service, h.Previews, h.TiffReviews, new RecordingNavigation(), null, null,
                new FinalSaveCoordinator(service, FinalSaveFixtures.Delivery(h), "tester"),
                new FixedFolderPicker(folder), new RecordingDeliveredFileShell());
            model.Open(review);
            await model.PreviewsLoaded;
            await model.FinalSaveFactsLoaded;
            model.ChangeLocationCommand.Execute(null);
            model.CanConfirmAndSave.ShouldBeTrue();
            model.CanApprove.ShouldBeTrue();
            model.TiffReviewCaveat.ShouldContain("单独审核不会保存文件");
            string expectedFile = Path.Combine(folder, model.SaveFileNameText);
            File.Exists(expectedFile).ShouldBeFalse();
            await model.ConfirmAndSaveCommand.ExecuteAsync(null);
            await model.FinalSaveFactsLoaded;
            File.Exists(expectedFile).ShouldBeTrue("the existing combined action really does save");
            model.IsReviewRequired.ShouldBeFalse();
        }
        finally { FinalSaveFixtures.Remove(folder); }
    }

    [Fact]
    public async Task Asset_route_never_shows_print_contract_white_ink_or_enlargement_instructions()
    {
        using OperatorCultureScope culture = new("zh-CN");
        using HomeScreenHarness h = new();
        var model = await GuidedSessionProjectionTests.Open(h, WorkflowType.PrepareAsset);
        await model.ConfirmOriginalCommand.ExecuteAsync(null);
        WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = model }, new Size(984, 661), tree =>
        {
            GuidedSessionShellTests.Find<FrameworkElement>(tree, "Session.PrintSummary").Visibility.ShouldBe(Visibility.Collapsed);
            var text = tree.OfType<TextBlock>().Where(GuidedSessionProjectionTests.Shown).Select(t => t.Text).ToArray();
            text.ShouldNotContain(model.WhiteUnderbaseHint);
            text.ShouldNotContain(model.GuidedWhiteInkSourceHint);
            text.ShouldNotContain(model.GuidedPrintRisk);
            return 0;
        });
        model.ShowsGuidedPrintSummary.ShouldBeFalse();
    }

    private static async Task<SessionViewModel> AtWhiteInk(HomeScreenHarness h, WorkflowType route)
    {
        var model = await GuidedSessionProjectionTests.Open(h, route);
        await model.ConfirmOriginalCommand.ExecuteAsync(null);
        if (route == WorkflowType.PrepareCustomerDesign)
        {
            await model.SkipCommand.ExecuteAsync(null);
            await model.SkipCommand.ExecuteAsync(null);
            await model.RunStepCommand.ExecuteAsync(null);
            await model.ApproveCommand.ExecuteAsync(null);
        }
        model.WidthMmText = "200"; model.HeightMmText = "150";
        await model.SetMaximumBoundsCommand.ExecuteAsync(null);
        model.GuidedPage.ShouldBe(GuidedSessionPage.WhiteInk);
        return model;
    }

    // Keep live service writes off the STA-created collection views, as existing visual fixtures do.
    private static async Task<SessionViewModel> Snapshot(HomeScreenHarness h, SessionViewModel driver)
    {
        var model = h.Session(new RecordingNavigation());
        model.Open((await h.Sessions.LoadAsync(driver.Id, default)).Value);
        await model.PreviewsLoaded;
        return model;
    }

    private static void FitsText(Control control, string label)
    {
        var text = new FormattedText(label, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
            new Typeface(control.FontFamily, control.FontStyle, control.FontWeight, control.FontStretch), control.FontSize, Brushes.Black, 1);
        (text.WidthIncludingTrailingWhitespace + control.Padding.Left + control.Padding.Right)
            .ShouldBeLessThanOrEqualTo(control.ActualWidth + 1, label);
    }

    private static void Inside(FrameworkElement element, Visual root, Size viewport)
    {
        Rect bounds = element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
        bounds.Left.ShouldBeGreaterThanOrEqualTo(-0.5);
        bounds.Top.ShouldBeGreaterThanOrEqualTo(-0.5);
        bounds.Right.ShouldBeLessThanOrEqualTo(viewport.Width + 0.5);
        bounds.Bottom.ShouldBeLessThanOrEqualTo(viewport.Height + 0.5);
        bounds.Height.ShouldBeGreaterThan(0);
    }

    private static IEnumerable<DependencyObject> All(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in All(VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    private static Task Capture(SessionViewModel model, Size viewport, string state)
    {
        string? directory = Environment.GetEnvironmentVariable("PF_C03_CAPTURE_DIR");
        if (!string.IsNullOrWhiteSpace(directory))
            WpfRendering.CapturePng(() => new SessionScreenView { DataContext = model }, viewport,
                Path.Combine(directory, $"{state}-{viewport.Width}x{viewport.Height}.png"));
        return Task.CompletedTask;
    }

    private sealed class FixedFolderPicker(string folder) : IDeliveryFolderPicker
    {
        public string? PickFolder(string dialogTitle, string? initialFolder) => folder;
    }
}
