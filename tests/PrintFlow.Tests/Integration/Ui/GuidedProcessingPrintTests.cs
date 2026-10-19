using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class GuidedProcessingPrintTests
{
    [Theory]
    [InlineData(WorkflowType.GeneratePrintTiff, 0)]
    [InlineData(WorkflowType.GeneratePrintTiff, 1)]
    [InlineData(WorkflowType.GeneratePrintTiff, 2)]
    [InlineData(WorkflowType.PrepareCustomerDesign, 0)]
    [InlineData(WorkflowType.PrepareCustomerDesign, 1)]
    [InlineData(WorkflowType.PrepareCustomerDesign, 2)]
    public async Task Both_Tiff_routes_require_explicit_white_ink_and_show_current_preflight_summary(WorkflowType route, int branch)
    {
        using OperatorCultureScope culture = new("zh-CN");
        using HomeScreenHarness h = new();
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
        model.SelectedWhiteUnderbaseChoice.ShouldBeNull();
        model.CanRunStep.ShouldBeFalse();
        model.WhiteUnderbaseChoices.Count.ShouldBe(3);
        model.SelectedWhiteUnderbaseChoice = model.WhiteUnderbaseChoices[branch];
        await model.SelectWhiteUnderbaseCommand.ExecuteAsync(null);
        model.GuidedPage.ShouldBe(GuidedSessionPage.Preflight);
        var result = Summary(model);
        result.GetValueOrDefault("Session.PrintSummary.Requested").ShouldNotBeNullOrWhiteSpace();
        result.GetValueOrDefault("Session.PrintSummary.Actual")!.ShouldContain("毫米");
        result.GetValueOrDefault("Session.PrintSummary.Pixels")!.ShouldContain("像素");
        result.GetValueOrDefault("Session.PrintSummary.WhiteInk")!.ShouldContain(branch + " 像素");
        result.GetValueOrDefault("Session.PrintSummary.Contract")!.ShouldContain("300 PPI");
        (await h.Sessions.LoadAsync(model.Id, default)).Value.WhiteUnderbaseBranch.ShouldBe(model.WhiteUnderbaseChoices[branch].Branch);
    }

    [Fact]
    public async Task Readable_size_summary_preserves_raw_custom_input_and_does_not_authorise_enlargement()
    {
        using OperatorCultureScope culture = new("zh-CN");
        using HomeScreenHarness h = new();
        var model = await PrintSizeGuidanceSetup.AtDimensionsAsync(h, 40, 20);
        model.ChooseCustomSizeCommand.Execute(null);
        model.SelectedTargetEdgeChoice = model.TargetEdgeChoices.Single(c => c.Edge == TargetEdge.Width);
        model.CustomMillimetresText = "123.456789";
        await model.PreflightLoaded;
        model.Preflight!.RequiresEnlargement.ShouldBeTrue();
        model.Preflight.EnlargementAuthorised.ShouldBeFalse();
        var before = model.Preflight;
        var result = Summary(model);
        result.GetValueOrDefault("Session.PrintSummary.Requested")!.ShouldContain("123.46");
        result.GetValueOrDefault("Session.PrintSummary.Actual")!.ShouldNotMatch(@"\d+\.\d{3}");
        result.GetValueOrDefault("Session.PrintSummary.Risk").ShouldNotBeNullOrWhiteSpace();
        model.CustomMillimetresText.ShouldBe("123.456789");
        model.Preflight.ShouldBe(before);
        model.HasUsableEnlargementAuthority.ShouldBeFalse();
    }

    [Fact]
    public async Task Invalid_input_has_no_stale_print_summary_and_cannot_continue()
    {
        using HomeScreenHarness h = new();
        var model = await PrintSizeGuidanceSetup.AtDimensionsAsync(h);
        model.ChooseCustomSizeCommand.Execute(null);
        model.CustomMillimetresText = "invalid";
        await model.PreflightLoaded;
        model.HasCustomSizeInputHint.ShouldBeTrue();
        model.Preflight.ShouldBeNull();
        var enabled = WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = model }, new Size(1280, 720),
            tree => GuidedSessionShellTests.Find<Button>(tree, "Session.PrintDimensions.ConfirmSize").IsEnabled);
        enabled.Facts.ShouldBeFalse("an invalid size must not offer a continue action");
        Summary(model).ShouldBeEmpty();
    }

    [Fact]
    public async Task Asset_route_never_exposes_print_or_white_ink_summary()
    {
        using HomeScreenHarness h = new();
        var model = await GuidedSessionProjectionTests.Open(h, WorkflowType.PrepareAsset);
        Summary(model).ShouldBeEmpty();
    }

    internal static Dictionary<string, string> Summary(SessionViewModel model) =>
        WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = model }, new Size(1280, 720), tree =>
            tree.OfType<TextBlock>().Where(t => AutomationProperties.GetAutomationId(t).StartsWith("Session.PrintSummary.") && GuidedSessionProjectionTests.Shown(t))
                .ToDictionary(AutomationProperties.GetAutomationId, t => t.Text)).Facts;
}
