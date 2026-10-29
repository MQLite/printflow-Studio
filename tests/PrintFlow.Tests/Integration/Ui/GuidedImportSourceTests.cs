using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Tests.Integration.Persistence;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class GuidedImportSourceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Route_return_is_available_only_before_the_first_derived_result(bool processed)
    {
        using HomeScreenHarness h = new();
        var imported = await GuidedImportRouteTests.Import(h, "return-route.png");
        var driver = h.Session(h.Navigation); driver.Open(imported);
        if (processed) { await driver.ConfirmOriginalCommand.ExecuteAsync(null); await driver.RunStepCommand.ExecuteAsync(null); }
        var shown = h.Session(h.Navigation); shown.Open((await h.Sessions.LoadAsync(imported.Id, default)).Value);
        await shown.PreviewsLoaded;
        WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = shown }, new Size(1000, 700), tree =>
        {
            var change = GuidedImportHomeTests.Find<Button>(tree, "Session.ChangeRoute");
            GuidedSessionProjectionTests.Shown(change).ShouldBe(!processed);
            change.Command!.Execute(null);
            if (processed) h.Navigation.WorkflowSelectionFor.ShouldBeNull();
            else h.Navigation.WorkflowSelectionFor!.Id.ShouldBe(imported.Id);
            return true;
        });
        (await h.Sessions.LoadAsync(imported.Id, default)).Value.WorkflowType.ShouldBe(WorkflowType.PrepareAsset);
    }

    [Theory]
    [InlineData("pdf", false)]
    [InlineData("psd", false)]
    [InlineData("pdf", true)]
    [InlineData("psd", true)]
    public async Task Source_information_is_visible_during_preparation_and_review_then_collapses(string format, bool asset)
    {
        using OperatorCultureScope culture = new("zh-CN");
        using SessionServiceHarness h = new();
        ISessionService service = format == "pdf"
            ? PdfPreparationWorkflowTests.Service(h, new WindowsPdfPreparationProcessor(h.FileWorkspace, h.FileInspector))
            : h.CreateServiceWithPhotoshop(new PsdPreparationWorkflowTests.PsdProcessor(h.FileWorkspace));
        byte[] bytes = format == "pdf" ? PdfFixtures.Read("single") : PsdInputPreparationTests.RgbCompositePsd();
        string fileName = "合成源文件." + format;
        string source = h.Workspace.CreateSourceFile(fileName, bytes);
        var imported = (await service.ImportAsync(asset ? WorkflowType.PrepareAsset : WorkflowType.GeneratePrintTiff, source, "different-output-name", "tester", default)).Value;
        var m = new SessionViewModel(service, h.Previews, h.TiffReviews, new RecordingNavigation()); m.Open(imported);
        await m.PreviewsLoaded;
        if (asset && format == "psd") m.PsdSourceNotice.ShouldNotContain("TIFF");
        await AssertSource(true);
        await m.RunStepCommand.ExecuteAsync(null); await m.PreviewsLoaded;
        m.GuidedPage.ShouldBe(GuidedSessionPage.Review); await AssertSource(true);
        if (asset && format == "pdf") m.PdfSourceNotice.ShouldNotContain("印刷尺寸");
        await m.ApproveCommand.ExecuteAsync(null);
        m.GuidedPage.ShouldBe(asset ? GuidedSessionPage.Ready : GuidedSessionPage.Dimensions); await AssertSource(false);
        File.ReadAllBytes(source).ShouldBe(bytes);
        var stored = (await h.Repository.LoadAsync(imported.Id, default)).Value!;
        stored.Snapshot!.OriginalFileName.ShouldBe(fileName);
        stored.Session.WhiteUnderbaseBranch.ShouldBeNull();

        async Task AssertSource(bool visible)
        {
            // Each STA render gets its own view model and CollectionViews; the workflow driver
            // remains unbound, exactly as the existing E2 evidence fixture does.
            var shown = new SessionViewModel(service, h.Previews, h.TiffReviews, new RecordingNavigation());
            shown.Open((await service.LoadAsync(imported.Id, default)).Value); await shown.PreviewsLoaded;
            WpfRendering.RenderExpectingNoBindingErrors(
            () => new SessionScreenView { DataContext = shown }, new Size(1000, 700), tree =>
            {
                string binding = format == "pdf" ? "PdfSourceNotice" : "PsdSourceNotice";
                var notice = tree.OfType<TextBlock>().Single(t => BindingOperations.GetBindingExpression(t, TextBlock.TextProperty)?.ParentBinding.Path.Path == binding);
                GuidedSessionProjectionTests.Shown(notice).ShouldBe(visible);
                if (visible)
                {
                    notice.Text.ShouldNotBeNullOrWhiteSpace();
                    tree.OfType<TextBlock>().ShouldContain(t => t.Text.Contains(fileName) && GuidedSessionProjectionTests.Shown(t));
                    GuidedSessionProjectionTests.Shown(GuidedSessionShellTests.Find<ListBox>(tree, "Session.WhiteUnderbaseChoices")).ShouldBeFalse();
                    GuidedSessionProjectionTests.Shown(GuidedSessionShellTests.Find<ItemsControl>(tree, "Session.SizePresetList")).ShouldBeFalse();
                }
                return true;
            });
        }
    }

    [Fact]
    public async Task Unavailable_ai_is_information_and_all_local_routes_still_reach_their_original_step()
    {
        using OperatorCultureScope culture = new("zh-CN");
        using HomeScreenHarness h = new();
        WpfRendering.RenderExpectingNoBindingErrors(() => new HomeView { DataContext = h.Home }, new Size(1000, 700), tree =>
        {
            var notice = GuidedImportHomeTests.Find<TextBlock>(tree, "Home.AiUnavailable");
            notice.Text.ShouldContain("暂不可用");
            tree.OfType<Button>().ShouldNotContain(b => AutomationProperties.GetAutomationId(b).StartsWith("AI.", StringComparison.Ordinal));
            GuidedImportHomeTests.Find<Button>(tree, "Home.ChooseFile").IsEnabled.ShouldBeTrue();
            return true;
        });
        foreach (var route in Enum.GetValues<WorkflowType>())
        {
            var state = await GuidedImportRouteTests.Import(h, "local-" + route + ".png");
            var selection = h.WorkflowSelection(h.Navigation); selection.Open(state);
            await selection.SelectCommand.ExecuteAsync(selection.Workflows.Single(w => w.Type == route));
            h.Navigation.SessionFor!.WorkflowType.ShouldBe(route);
            h.Navigation.SessionFor.CurrentStep!.Step.ShouldBe(StepKind.OriginalConfirmation);
            h.Navigation.SessionFor.WhiteUnderbaseBranch.ShouldBeNull();
        }
    }
}
