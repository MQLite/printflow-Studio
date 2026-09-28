using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using PrintFlow.App.Localisation;
using PrintFlow.App.Resources;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Definitions;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class WorkflowPurposeCardTests
{
    [Theory]
    [InlineData("en", 1000, 700)]
    [InlineData("zh-CN", 1000, 700)]
    [InlineData("en", 1920, 1040)]
    [InlineData("zh-CN", 1920, 1040)]
    public async Task Every_card_explains_purpose_result_and_dimensions_without_clipping(
        string culture, int width, int height)
    {
        using HomeScreenHarness harness = new();
        harness.FilePicker.Path = harness.WriteSourceFile("purpose.png");
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);
        RecordingNavigation navigation = new();
        WorkflowSelectionViewModel model = harness.WorkflowSelection(navigation);
        model.Open(harness.Navigation.WorkflowSelectionFor!);
        await model.PreviewLoaded;
        Size viewport = new(width, height);
        OperatorCultureScope scope = new();
        try
        {
            OperatorCulture.Select(CultureInfo.GetCultureInfo(culture));
            // Construct under the chosen language, as ordinary navigation does.
            model = harness.WorkflowSelection(navigation);
            model.Open(harness.Navigation.WorkflowSelectionFor!);
            await model.PreviewLoaded;
            string[][] expected = Expected(culture);
            RenderResult<int> rendered = WpfRendering.RenderExpectingNoBindingErrors(
                () => new WorkflowSelectionView { DataContext = model }, viewport, tree =>
                {
                    Button[] choices = [.. tree.OfType<Button>()
                        .Where(b => b.CommandParameter is WorkflowChoice)];
                    choices.Length.ShouldBe(3);
                    choices.Select(b => ((WorkflowChoice)b.CommandParameter).Type).ShouldBe(
                        new[] { WorkflowType.PrepareAsset, WorkflowType.PrepareCustomerDesign, WorkflowType.GeneratePrintTiff });
                    for (int i = 0; i < choices.Length; i++)
                    {
                        Button choose = choices[i];
                        choose.IsEnabled.ShouldBeTrue();
                        choose.IsTabStop.ShouldBeTrue();
                        choose.Focusable.ShouldBeTrue();
                        choose.IsDefault.ShouldBeFalse();
                        choose.Command.ShouldBeSameAs(model.SelectCommand);
                        choose.Content.ShouldBe(model.SelectLabel);
                        Grid card = (Grid)choose.Parent;
                        TextBlock[] lines = [.. tree.OfType<TextBlock>()
                            .Where(t => ReferenceEquals(t.DataContext, choose.CommandParameter) && expected[i].Contains(t.Text))];
                        lines.Length.ShouldBe(3, $"all purpose/result/dimensions lines for card {i} in {culture}");
                        foreach (TextBlock line in lines)
                        {
                            line.DataContext.ShouldBeSameAs(choose.CommandParameter);
                            line.TextWrapping.ShouldBe(TextWrapping.Wrap);
                            line.TextTrimming.ShouldBe(TextTrimming.None);
                            // DesiredSize includes Margin; ActualHeight is the content box.
                            line.ActualHeight.ShouldBeGreaterThanOrEqualTo(
                                line.DesiredSize.Height - line.Margin.Top - line.Margin.Bottom - 0.5);
                            Rect bounds = line.TransformToAncestor(card).TransformBounds(new Rect(line.RenderSize));
                            bounds.Right.ShouldBeLessThanOrEqualTo(choose.TranslatePoint(new Point(), card).X + 0.5);
                            line.ActualWidth.ShouldBeGreaterThan(100);
                        }
                    }
                    tree.OfType<Selector>().ShouldBeEmpty();
                    tree.OfType<FrameworkElement>().ShouldNotContain(e =>
                        KeyboardNavigation.GetTabNavigation(e) == KeyboardNavigationMode.Cycle ||
                        KeyboardNavigation.GetTabNavigation(e) == KeyboardNavigationMode.Contained);
                    ScrollViewer scroll = tree.OfType<ScrollViewer>().Single(s => s.Content is StackPanel);
                    scroll.ScrollToBottom();
                    tree.Root.UpdateLayout();
                    Rect last = choices[^1].TransformToAncestor(scroll).TransformBounds(new Rect(choices[^1].RenderSize));
                    last.Top.ShouldBeGreaterThanOrEqualTo(0);
                    last.Bottom.ShouldBeLessThanOrEqualTo(scroll.ActualHeight + 0.5);
                    return choices.Length;
                });
            rendered.Facts.ShouldBe(3);
            navigation.SessionFor.ShouldBeNull();

            string? captures = Environment.GetEnvironmentVariable("PF_11146_CAPTURE_DIR");
            if (!string.IsNullOrWhiteSpace(captures))
            {
                WpfRendering.CapturePng(() => new WorkflowSelectionView { DataContext = model }, viewport,
                    Path.Combine(captures, $"cards-{culture}-{width}-top.png"));
                WpfRendering.CapturePng(() => new WorkflowSelectionView { DataContext = model }, viewport,
                    Path.Combine(captures, $"cards-{culture}-{width}-bottom.png"),
                    tree => tree.OfType<ScrollViewer>().Single(s => s.Content is StackPanel).ScrollToBottom());
            }
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public void Language_switch_refreshes_existing_cards_without_selecting_or_replacing_them()
    {
        using HomeScreenHarness harness = new();
        OperatorCultureScope scope = new();
        try
        {
            WpfRendering.RenderExpectingNoBindingErrors(() =>
            {
                LocalisationService localisation = new(harness.Inner.Settings);
                localisation.Use(OperatorLanguage.English);
                RecordingNavigation navigation = new();
                WorkflowSelectionViewModel model = new(harness.Sessions, harness.Previews, navigation, localisation);
                model.OutputName = "Keep my name";
                WorkflowSelectionView view = new() { DataContext = model, Tag = localisation };
                return view;
            }, WpfRendering.ReviewViewport, tree =>
            {
                WorkflowSelectionViewModel model = (WorkflowSelectionViewModel)tree.Root.DataContext;
                WorkflowChoice[] original = [.. model.Workflows];
                Button[] buttons = [.. tree.OfType<Button>().Where(b => b.CommandParameter is WorkflowChoice)];
                foreach (OperatorLanguage language in new[] { OperatorLanguage.SimplifiedChinese, OperatorLanguage.English })
                {
                    ((LocalisationService)tree.Root.Tag).Use(language);
                    tree.Root.UpdateLayout();
                    string culture = language == OperatorLanguage.English ? "en" : "zh-CN";
                    for (int i = 0; i < original.Length; i++)
                    {
                        model.Workflows[i].ShouldBeSameAs(original[i]);
                        buttons[i].CommandParameter.ShouldBeSameAs(original[i]);
                        AutomationProperties.GetName(buttons[i]).ShouldBe(original[i].Title);
                        buttons[i].Content.ShouldBe(model.SelectLabel);
                        string[] displayed = [.. tree.OfType<TextBlock>().Where(t => ReferenceEquals(t.DataContext, original[i])).Select(t => t.Text)];
                        foreach (string line in Expected(culture)[i]) displayed.ShouldContain(line);
                    }
                    model.OutputName.ShouldBe("Keep my name");
                }
                return 0;
            });
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public void Asset_route_has_no_print_size_or_TIFF_and_preparation_remains_optional()
    {
        WorkflowCatalog.PrepareAsset.Steps.ShouldNotContain(s =>
            s.Kind == StepKind.PrintDimensions || s.Kind == StepKind.PhotoshopOutput);
        foreach (WorkflowDefinition route in new[] { WorkflowCatalog.PrepareAsset, WorkflowCatalog.PrepareCustomerDesign })
        {
            route.Steps.Single(s => s.Kind == StepKind.Enhancement).IsSkippable.ShouldBeTrue();
            route.Steps.Single(s => s.Kind == StepKind.BackgroundRemoval).IsSkippable.ShouldBeTrue();
        }
    }

    private static string[][] Expected(string culture) => culture == "zh-CN"
        ? [
            ["适用于要放入设计中的图片或标志。可按需增强图片或移除背景。", "得到审核通过的透明 PNG。", "无需印刷尺寸，不生成 TIFF。"],
            ["适用于需要做打印前处理的客户成品设计。可按需增强图片或移除背景。", "得到审核通过的打印 TIFF（CMYK + 白墨）。", "需要印刷尺寸。"],
            ["适用于已完成、可直接制作打印文件的设计。", "得到审核通过的打印 TIFF（CMYK + 白墨）。", "需要印刷尺寸。"],
        ]
        : [
            ["Use this for a picture or logo you will use in a design. Enhance or remove the background if needed.", "You get an approved transparent PNG.", "No print size needed. No TIFF."],
            ["Use this for a customer's finished design that needs preparation for print. Enhance or remove the background if needed.", "You get an approved print TIFF (CMYK + white ink).", "Print size needed."],
            ["Use this for a finished design that is ready to make into a print file.", "You get an approved print TIFF (CMYK + white ink).", "Print size needed."],
        ];
}
