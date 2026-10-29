using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using PrintFlow.App.Controls;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Tests.Integration.Persistence;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class GuidedSessionProjectionTests
{
    [Theory]
    [InlineData(WorkflowType.PrepareAsset, "Import,OriginalConfirmation,Enhancement,BackgroundRemoval,Trim,ApprovedPngExport")]
    [InlineData(WorkflowType.PrepareCustomerDesign, "Import,OriginalConfirmation,Enhancement,BackgroundRemoval,Trim,PrintDimensions,PhotoshopOutput")]
    [InlineData(WorkflowType.GeneratePrintTiff, "Import,OriginalConfirmation,PrintDimensions,PhotoshopOutput")]
    public async Task Routes_present_catalog_steps_and_hide_future_print_forms(WorkflowType route, string expected)
    {
        using HomeScreenHarness h = new();
        var model = await Open(h, route);
        string.Join(',', model.GuidedSteps.Select(s => s.Id)).ShouldBe(expected);
        model.GuidedSteps[0].State.ShouldBe(GuidedStepState.Completed);
        model.GuidedSteps[1].State.ShouldBe(GuidedStepState.Current);
        model.GuidedSteps.Skip(2).ShouldAllBe(s => s.State == GuidedStepState.Pending);
        WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = model }, new Size(1000, 700), tree =>
        {
            var white = GuidedSessionShellTests.Find<ListBox>(tree, "Session.WhiteUnderbaseChoices");
            Shown(white).ShouldBeFalse("white ink is a later current page, even when the service legally accepts it early");
            var dimensions = GuidedSessionShellTests.Find<ItemsControl>(tree, "Session.SizePresetList");
            Shown(dimensions).ShouldBeFalse();
            var steps = GuidedSessionShellTests.Find<ItemsControl>(tree, "Session.StepList");
            GuidedSessionShellTests.Ancestors(white).ShouldNotContain(steps);
            tree.OfType<Button>().Where(b => GuidedSessionShellTests.Ancestors(b).Contains(steps)).ShouldBeEmpty();
            return true;
        });
        Page(model).ShouldBe("Source");
    }

    [Fact]
    public async Task Optional_processing_still_requires_review_and_rejection_projects_attention()
    {
        using HomeScreenHarness h = new();
        var model = await Open(h, WorkflowType.PrepareAsset);
        await model.ConfirmOriginalCommand.ExecuteAsync(null);
        Page(model).ShouldBe("Ready");
        await model.RunStepCommand.ExecuteAsync(null);
        Page(model).ShouldBe("Review");
        model.CanSkip.ShouldBeFalse();
        await model.RejectCommand.ExecuteAsync(null);
        Page(model).ShouldBe("Failure");
        model.GuidedSteps.Single(s => s.Id == "Enhancement").State.ShouldBe(GuidedStepState.Attention);
        await model.RetryCommand.ExecuteAsync(null);
        await model.SkipCommand.ExecuteAsync(null);
        model.GuidedSteps.Single(s => s.Id == "Enhancement").State.ShouldBe(GuidedStepState.Skipped);
        await model.SkipCommand.ExecuteAsync(null);
        await model.RunStepCommand.ExecuteAsync(null);
        Page(model).ShouldBe("Review");
        await model.ApproveCommand.ExecuteAsync(null);
        await model.RunStepCommand.ExecuteAsync(null);
        Page(model).ShouldBe("Save");
        model.GuidedSteps.ShouldNotContain(s => s.Id == "PrintDimensions" || s.Id == "PhotoshopOutput");
        var persisted = (await h.Inner.Repository.LoadAsync(model.Id, default)).Value!;
        persisted.Steps.Single(s => s.Step == StepKind.Enhancement).State.ShouldBe(StepState.Skipped);
        persisted.Reviews.ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData(WorkflowType.GeneratePrintTiff, WhiteUnderbaseBranch.W1_0px)]
    [InlineData(WorkflowType.GeneratePrintTiff, WhiteUnderbaseBranch.W1_1px)]
    [InlineData(WorkflowType.GeneratePrintTiff, WhiteUnderbaseBranch.W1_2px)]
    [InlineData(WorkflowType.PrepareCustomerDesign, WhiteUnderbaseBranch.W1_1px)]
    public async Task Tiff_route_projects_dimensions_explicit_white_ink_preflight_review_and_another_size(WorkflowType route, WhiteUnderbaseBranch branch)
    {
        using HomeScreenHarness h = new();
        var model = await Open(h, route);
        await model.ConfirmOriginalCommand.ExecuteAsync(null);
        if (route == WorkflowType.PrepareCustomerDesign)
        {
            await model.SkipCommand.ExecuteAsync(null);
            await model.SkipCommand.ExecuteAsync(null);
            await model.RunStepCommand.ExecuteAsync(null);
            await model.ApproveCommand.ExecuteAsync(null);
        }
        Page(model).ShouldBe("Dimensions");
        model.SelectedWhiteUnderbaseChoice.ShouldBeNull();
        model.WidthMmText = "200";
        model.HeightMmText = "150";
        await model.SetMaximumBoundsCommand.ExecuteAsync(null);
        model.Notice.ShouldBeNull();
        Page(model).ShouldBe("WhiteInk");
        model.CanRunStep.ShouldBeFalse();
        model.SelectedWhiteUnderbaseChoice.ShouldBeNull();
        model.SelectedWhiteUnderbaseChoice = model.WhiteUnderbaseChoices.Single(c => c.Branch == branch);
        await model.SelectWhiteUnderbaseCommand.ExecuteAsync(null);
        Page(model).ShouldBe("Preflight");
        (await h.Sessions.LoadAsync(model.Id, default)).Value.WhiteUnderbaseBranch.ShouldBe(branch);
        await model.RunStepCommand.ExecuteAsync(null);
        Page(model).ShouldBe("Review");
        await model.ApproveCommand.ExecuteAsync(null);
        Page(model).ShouldBe("Save");
        await model.CompleteCommand.ExecuteAsync(null);
        Page(model).ShouldBe("Completed");
        await model.AddAnotherSizeCommand.ExecuteAsync(null);
        Page(model).ShouldBe("Dimensions");
        model.SelectedWhiteUnderbaseChoice.ShouldBeNull();
        (await h.Sessions.LoadAsync(model.Id, default)).Value.WhiteUnderbaseBranch.ShouldBeNull();
    }

    [Theory]
    [InlineData("pdf", "SourcePreparation", StepState.Waiting)]
    [InlineData("psd", "Failure", StepState.Failed)]
    public async Task Source_preparation_is_current_before_any_print_input_and_failure_does_not_advance(
        string extension, string expectedPage, StepState expectedState)
    {
        using HomeScreenHarness h = new();
        byte[] bytes = extension == "pdf" ? PdfFixtures.Read("single") : PsdInputPreparationTests.RgbCompositePsd();
        string path = h.Inner.Workspace.CreateSourceFile("guided." + extension, bytes);
        var state = (await h.Sessions.ImportAsync(WorkflowType.GeneratePrintTiff, path, "source", "tester", default)).Value;
        var model = h.Session(new RecordingNavigation());
        model.Open(state);
        await model.PreviewsLoaded;
        Page(model).ShouldBe("SourcePreparation");
        model.CanConfirmOriginal.ShouldBeFalse();
        await model.RunStepCommand.ExecuteAsync(null);
        // This harness has no production PDF/Photoshop preparation adapter. A truthful failure
        // stays on the source step; no raster, dimensions or automatic retry may be invented.
        Page(model).ShouldBe(expectedPage);
        model.Notice.ShouldNotBeNullOrWhiteSpace();
        var persisted = (await h.Sessions.LoadAsync(model.Id, default)).Value;
        persisted.CurrentStep!.Step.ShouldBe(StepKind.OriginalConfirmation);
        persisted.CurrentStep.State.ShouldBe(expectedState);
        persisted.WhiteUnderbaseBranch.ShouldBeNull();
    }

    [Fact]
    public async Task Prepared_single_page_pdf_projects_required_source_review_before_dimensions()
    {
        using SessionServiceHarness h = new();
        var service = PdfPreparationWorkflowTests.Service(h, new WindowsPdfPreparationProcessor(h.FileWorkspace, h.FileInspector));
        string path = h.Workspace.CreateSourceFile("guided-prepared.pdf", PdfFixtures.Read("single"));
        var state = (await service.ImportAsync(WorkflowType.GeneratePrintTiff, path, "pdf", "tester", default)).Value;
        SessionViewModel model = new(service, h.Previews, h.TiffReviews, new RecordingNavigation());
        model.Open(state);
        await model.RunStepCommand.ExecuteAsync(null);
        Page(model).ShouldBe("Review");
        model.CanSkip.ShouldBeFalse();
        var prepared = (await service.LoadAsync(model.Id, default)).Value;
        prepared.PdfInspection!.PreparedPageNumber.ShouldBe(1);
        prepared.CurrentStep!.State.ShouldBe(StepState.ReviewRequired);
        await model.ApproveCommand.ExecuteAsync(null);
        Page(model).ShouldBe("Dimensions");
    }

    [Fact]
    public async Task Current_white_ink_submission_is_recommended_and_reediting_does_not_expose_run()
    {
        using HomeScreenHarness h = new();
        var model = await Open(h, WorkflowType.GeneratePrintTiff);
        await model.ConfirmOriginalCommand.ExecuteAsync(null);
        model.WidthMmText = "200";
        model.HeightMmText = "150";
        await model.SetMaximumBoundsCommand.ExecuteAsync(null);
        model.RecommendedCommand.ShouldBeSameAs(model.SelectWhiteUnderbaseCommand);
        model.SelectedWhiteUnderbaseChoice = model.WhiteUnderbaseChoices[0];
        await model.SelectWhiteUnderbaseCommand.ExecuteAsync(null);
        // The new secondary page control must not itself change the persisted branch.
        WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = model }, new Size(1000, 700), tree =>
        {
            var change = GuidedSessionShellTests.Find<Button>(tree, "Session.WhiteUnderbase.Change");
            change.Command!.Execute(null);
            tree.Root.UpdateLayout();
            Page(model).ShouldBe("WhiteInk");
            Shown(GuidedSessionShellTests.Find<Button>(tree, "Session.RunStep")).ShouldBeFalse();
            model.SelectedWhiteUnderbaseChoice.ShouldBeNull();
            return true;
        });
        (await h.Sessions.LoadAsync(model.Id, default)).Value.WhiteUnderbaseBranch.ShouldBe(WhiteUnderbaseBranch.W1_0px);
    }

    [Fact]
    public async Task Current_background_authorisation_confirmation_is_in_the_fixed_bar()
    {
        using HomeScreenHarness h = new();
        var model = await Open(h, WorkflowType.PrepareAsset);
        await model.ConfirmOriginalCommand.ExecuteAsync(null);
        await model.SkipCommand.ExecuteAsync(null);
        model.BeginAutomaticSelectionCommand.Execute(null);
        model.IsConfirmingAutomaticSelection.ShouldBeTrue();
        WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = model }, new Size(1000, 700), tree =>
        {
            var confirm = tree.OfType<Button>().Single(b => ReferenceEquals(b.Command, model.ConfirmAutomaticSelectionCommand));
            GuidedSessionShellTests.Ancestors(confirm).ShouldContain(GuidedSessionShellTests.Find<FrameworkElement>(tree, "Session.ActionBar"));
            model.RecommendedCommand.ShouldBeSameAs(model.ConfirmAutomaticSelectionCommand);
            return true;
        });
    }

    internal static async Task<SessionViewModel> Open(HomeScreenHarness h, WorkflowType route)
    {
        string source = h.Inner.WriteBorderedSourcePng("guided-" + route + ".png");
        SessionView imported = (await h.Sessions.ImportAsync(route, source, "guided", "tester", default)).Value;
        SessionViewModel model = h.Session(new RecordingNavigation());
        model.Open(imported);
        await model.PreviewsLoaded;
        return model;
    }

    private static string Page(SessionViewModel model) => model.GuidedPage.ToString();
    internal static bool Shown(UIElement item) => item.Visibility == Visibility.Visible &&
        GuidedSessionShellTests.Ancestors(item).OfType<UIElement>().All(a => a.Visibility == Visibility.Visible);
}
