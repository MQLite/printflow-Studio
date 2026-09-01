using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

/// <summary>Real WPF layout/binding coverage for every flexible-size operator state.</summary>
[Collection(SqliteCollection.Name)]
public sealed class FlexibleSizeRenderingTests
{
    [Fact]
    public Task Every_flexible_size_state_renders_in_en_US() =>
        RenderMatrixAsync(CultureInfo.GetCultureInfo("en-US"));

    [Fact]
    public Task Every_flexible_size_state_renders_in_zh_CN() =>
        RenderMatrixAsync(CultureInfo.GetCultureInfo("zh-CN"));

    private static async Task RenderMatrixAsync(CultureInfo culture)
    {
        CultureInfo previousCulture = CultureInfo.CurrentCulture;
        CultureInfo previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;

            using HomeScreenHarness harness = new();

            // A. normal preset selection.
            SessionViewModel presetSelection = await AtDimensionsAsync(
                harness, Source(harness, $"a-{culture.Name}.png", 2000, 1000));
            SizePresetChoice a4Card = presetSelection.SizePresets
                .Single(choice => choice.Preset == SizePreset.A4);
            SizePresetChoice a5Card = presetSelection.SizePresets
                .Single(choice => choice.Preset == SizePreset.A5);
            a4Card.RecommendationLabel.ShouldBe(culture.Name == "zh-CN"
                ? "推荐长边：280 mm"
                : "Recommended long edge: 280 mm");
            a5Card.RecommendationLabel.ShouldBe(culture.Name == "zh-CN"
                ? "推荐短边：135 mm"
                : "Recommended short edge: 135 mm");
            a5Card.RecommendationLabel.ShouldNotContain(nameof(PresetRecommendationKind.MaximumShortEdge));
            CaptureAffectedState(presetSelection, culture, "preset-cards");
            Render(presetSelection);

            // B. selected A4 configured recommendation.
            SessionViewModel selectedA4 = await AtDimensionsAsync(
                harness, Source(harness, $"b-{culture.Name}.png", 2000, 1000));
            await UseA4Async(selectedA4);
            selectedA4.CurrentRecommendation.ShouldContain("280");
            Render(selectedA4);

            // C. pure Custom Size.
            SessionViewModel pureCustom = await AtDimensionsAsync(
                harness, Source(harness, $"c-{culture.Name}.png", 2000, 1000));
            pureCustom.ChooseCustomSizeCommand.Execute(null);
            Render(pureCustom);

            // D. Adjust selected preset.
            SessionViewModel adjustPreset = await AtDimensionsAsync(
                harness, Source(harness, $"d-{culture.Name}.png", 2000, 1000));
            await UseA4Async(adjustPreset);
            await adjustPreset.AdjustSizeCommand.ExecuteAsync(null);
            adjustPreset.HasCustomPresetContext.ShouldBeTrue();
            Render(adjustPreset);

            // D2. A5 adjustment retains the corrected short-edge context in the same locale.
            SessionViewModel adjustA5 = await AtDimensionsAsync(
                harness, Source(harness, $"d2-{culture.Name}.png", 2000, 1800));
            await UseA5Async(adjustA5);
            await adjustA5.AdjustSizeCommand.ExecuteAsync(null);
            adjustA5.HasCustomPresetContext.ShouldBeTrue();
            adjustA5.CustomPresetContext.ShouldContain("A5");
            adjustA5.CustomPresetRecommendation.ShouldBe(culture.Name == "zh-CN"
                ? "推荐短边：135 mm"
                : "Recommended short edge: 135 mm");
            CaptureAffectedState(adjustA5, culture, "adjust-a5");
            Render(adjustA5);

            // E. custom shrink.
            SessionViewModel shrink = await AtDimensionsAsync(
                harness, Source(harness, $"e-{culture.Name}.png", 2000, 1000));
            await CustomAsync(shrink, TargetEdge.LongEdge, "100");
            shrink.NeedsEnlargementAuthority.ShouldBeFalse();
            Render(shrink);

            // F. preset exceeded but no enlargement.
            SessionViewModel exceeded = await AtDimensionsAsync(
                harness, Source(harness, $"f-{culture.Name}.png", 4000, 2000));
            await UseA4Async(exceeded);
            await exceeded.AdjustSizeCommand.ExecuteAsync(null);
            await CompleteCustomFormAsync(exceeded, TargetEdge.LongEdge, "300");
            exceeded.HasPresetLimitNotice.ShouldBeTrue();
            exceeded.NeedsEnlargementAuthority.ShouldBeFalse();
            Render(exceeded);

            // G. enlargement warning, including both visible actions.
            SessionViewModel warning = await AtDimensionsAsync(
                harness, Source(harness, $"g-{culture.Name}.png", 40, 20));
            await CustomAsync(warning, TargetEdge.LongEdge, "100");
            warning.NeedsEnlargementAuthority.ShouldBeTrue();
            RenderWarning(warning);

            // H. enlargement authorised.
            SessionViewModel authorised = await AtDimensionsAsync(
                harness, Source(harness, $"h-{culture.Name}.png", 40, 20));
            await CustomAsync(authorised, TargetEdge.LongEdge, "100");
            await authorised.ContinueWithSizeCommand.ExecuteAsync(null);
            authorised.HasUsableEnlargementAuthority.ShouldBeTrue();
            Render(authorised);

            // I. Fake ReviewRequired — PresetFit.
            SessionViewModel presetReview = await AtDimensionsAsync(
                harness, Source(harness, $"i-{culture.Name}.png", 2000, 1000));
            await UseA4Async(presetReview);
            await ChooseBranchAsync(presetReview);
            await presetReview.RunStepCommand.ExecuteAsync(null);
            presetReview.HasPreparationAttemptAudit.ShouldBeTrue();
            Render(presetReview);

            // J. Fake ReviewRequired — TargetEdge + enlargement.
            SessionViewModel targetReview = await AtDimensionsAsync(
                harness, Source(harness, $"j-{culture.Name}.png", 40, 20));
            await CustomAsync(targetReview, TargetEdge.LongEdge, "100");
            await ChooseBranchAsync(targetReview);
            await targetReview.ContinueWithSizeCommand.ExecuteAsync(null);
            await targetReview.RunStepCommand.ExecuteAsync(null);
            targetReview.PreparationAttemptEnlargementConfirmation.ShouldNotBeNullOrWhiteSpace();
            Render(targetReview);

            // K. Add Another Size fresh state, with the earlier output still listed.
            SessionViewModel another = await AtDimensionsAsync(
                harness, Source(harness, $"k-{culture.Name}.png", 2000, 1000));
            await UseA4Async(another);
            await ChooseBranchAsync(another);
            await another.RunStepCommand.ExecuteAsync(null);
            await another.ApproveCommand.ExecuteAsync(null);
            await another.CompleteCommand.ExecuteAsync(null);
            await another.AddAnotherSizeCommand.ExecuteAsync(null);
            another.CanChooseFlexibleSize.ShouldBeTrue();
            another.Outputs.ShouldNotBeEmpty();
            Render(another);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    private static void Render(SessionViewModel screen)
    {
        RenderResult<int> rendered = WpfRendering.RenderExpectingNoBindingErrors(
            () => new SessionScreenView { DataContext = screen },
            WpfRendering.ReviewViewport,
            _ => 0);
        rendered.DesiredSize.Width.ShouldBeLessThanOrEqualTo(WpfRendering.ReviewViewport.Width);
    }

    private static void CaptureAffectedState(
        SessionViewModel screen, CultureInfo culture, string stateName)
    {
        string? outputDirectory = Environment.GetEnvironmentVariable("PRINTFLOW_A5_VISUAL_OUTPUT");
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            return;
        }

        int offset = stateName == "preset-cards" ? 120 : 240;
        WpfRendering.CapturePng(
            () => new SessionScreenView { DataContext = screen },
            WpfRendering.ReviewViewport,
            Path.Combine(outputDirectory, $"{culture.Name}-{stateName}.png"),
            tree => tree.OfType<ScrollViewer>()
                .Single(scroll => Grid.GetColumn(scroll) == 1 && scroll.Parent is Grid)
                .ScrollToVerticalOffset(offset));
    }

    private static void RenderWarning(SessionViewModel screen)
    {
        RenderResult<List<string>> rendered = WpfRendering.RenderExpectingNoBindingErrors(
            () => new SessionScreenView { DataContext = screen },
            WpfRendering.ReviewViewport,
            tree => tree.OfType<Button>()
                .Where(button => button.Visibility == Visibility.Visible)
                .Select(button => button.Content?.ToString() ?? string.Empty)
                .ToList());

        rendered.DesiredSize.Width.ShouldBeLessThanOrEqualTo(WpfRendering.ReviewViewport.Width);
        rendered.Facts.ShouldContain(screen.ChangeSizeLabel);
        rendered.Facts.ShouldContain(screen.ContinueWithSizeLabel);
    }

    private static async Task<SessionViewModel> AtDimensionsAsync(
        HomeScreenHarness harness, string source)
    {
        SessionView imported = (await harness.Sessions.ImportAsync(
            WorkflowType.GeneratePrintTiff,
            source,
            "render-flexible",
            "tester",
            CancellationToken.None)).Value;
        SessionView atDimensions = (await harness.Sessions.ExecuteAsync(
            imported.Id,
            new WorkflowCommand.ConfirmOriginal("finished design"),
            "tester",
            CancellationToken.None)).Value;
        SessionViewModel screen = harness.Session(new RecordingNavigation());
        screen.Open(atDimensions);
        return screen;
    }

    private static async Task UseA4Async(SessionViewModel screen) =>
        await screen.UsePresetCommand.ExecuteAsync(
            screen.SizePresets.Single(choice => choice.Preset == SizePreset.A4));

    private static async Task UseA5Async(SessionViewModel screen) =>
        await screen.UsePresetCommand.ExecuteAsync(
            screen.SizePresets.Single(choice => choice.Preset == SizePreset.A5));

    private static async Task CustomAsync(
        SessionViewModel screen, TargetEdge edge, string millimetres)
    {
        screen.ChooseCustomSizeCommand.Execute(null);
        await CompleteCustomFormAsync(screen, edge, millimetres);
    }

    private static async Task CompleteCustomFormAsync(
        SessionViewModel screen, TargetEdge edge, string millimetres)
    {
        screen.SelectedTargetEdgeChoice = screen.TargetEdgeChoices.Single(choice => choice.Edge == edge);
        screen.CustomMillimetresText = millimetres;
        await screen.ConfirmCustomSizeCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();
    }

    private static async Task ChooseBranchAsync(SessionViewModel screen)
    {
        screen.SelectedWhiteUnderbaseChoice = screen.WhiteUnderbaseChoices
            .Single(choice => choice.Branch == WhiteUnderbaseBranch.W1_1px);
        await screen.SelectWhiteUnderbaseCommand.ExecuteAsync(null);
    }

    private static string Source(
        HomeScreenHarness harness, string name, int width, int height) =>
        harness.Inner.Workspace.CreateSourceFile(
            name, SyntheticImages.Png(width, height, alpha: true));
}
