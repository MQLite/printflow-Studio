using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class GuidedSessionShellTests
{
    [Fact]
    public async Task Choosing_custom_dimensions_exposes_and_recommends_the_fixed_submit_without_reopening_the_view()
    {
        using HomeScreenHarness harness = new();
        string source = harness.Inner.WriteBorderedSourcePng("guided-print.png");
        var imported = (await harness.Sessions.ImportAsync(WorkflowType.GeneratePrintTiff, source, "print", "tester", CancellationToken.None)).Value;
        var dimensions = (await harness.Sessions.ExecuteAsync(imported.Id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None)).Value;
        SessionViewModel model = harness.Session(new RecordingNavigation());
        model.Open(dimensions);
        await model.PreviewsLoaded;
        WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = model }, new Size(1000, 700), tree =>
        {
            Button confirm = Find<Button>(tree, "Session.PrintDimensions.ConfirmSize");
            confirm.Visibility.ShouldBe(Visibility.Collapsed);
            Button entry = Find<Button>(tree, "Session.PrintDimensions.CustomSize");
            Ancestors(entry).ShouldContain(Find<FrameworkElement>(tree, "Session.ActionBar"));
            model.ChooseCustomSizeCommand.Execute(null);
            tree.Root.UpdateLayout();
            confirm.Visibility.ShouldBe(Visibility.Visible);
            entry.Visibility.ShouldBe(Visibility.Collapsed);
            model.RecommendedCommand.ShouldBeSameAs(model.ConfirmCustomSizeCommand);
            Ancestors(confirm).ShouldContain(Find<FrameworkElement>(tree, "Session.ActionBar"));
            return true;
        });
    }

    // Break caught: the step list above the image consumes the review area and actions scroll away.
    [Theory]
    [InlineData(1000, 700)]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    public async Task Navigation_is_left_of_current_workspace_and_primary_actions_remain_fixed(int width, int height)
    {
        using OperatorCultureScope culture = new("zh-CN");
        using HomeScreenHarness harness = new();
        SessionViewModel model = await Open(harness);
        WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = model }, new Size(width, height), tree =>
        {
            FrameworkElement navigation = Find<FrameworkElement>(tree, "Session.StepList");
            FrameworkElement workspace = Find<FrameworkElement>(tree, "Session.CurrentWorkspace");
            FrameworkElement actions = Find<FrameworkElement>(tree, "Session.ActionBar");
            Button primary = Find<Button>(tree, "Session.ConfirmOriginal");
            Bounds(navigation, tree.Root).Right.ShouldBeLessThanOrEqualTo(Bounds(workspace, tree.Root).Left);
            Bounds(workspace, tree.Root).Bottom.ShouldBeLessThanOrEqualTo(Bounds(actions, tree.Root).Top);
            var bounds = Bounds(primary, tree.Root);
            bounds.Bottom.ShouldBeLessThanOrEqualTo(height);
            bounds.Right.ShouldBeLessThanOrEqualTo(width);
            bounds.Width.ShouldBeGreaterThan(0);
            Ancestors(primary).ShouldContain(actions);
            primary.Command.ShouldBeSameAs(model.ConfirmOriginalCommand);
            primary.IsDefault.ShouldBeFalse();
            return true;
        });
    }

    // Break caught: current form submit stays below a long form and is unreachable without scrolling.
    [Fact]
    public async Task Current_input_actions_are_in_the_fixed_bar()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel model = await Open(harness);
        await model.ConfirmOriginalCommand.ExecuteAsync(null);
        await model.SkipCommand.ExecuteAsync(null);
        await model.SkipCommand.ExecuteAsync(null);
        WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = model }, new Size(1000, 700), tree =>
        {
            FrameworkElement bar = Find<FrameworkElement>(tree, "Session.ActionBar");
            Button margin = tree.OfType<Button>().Single(b => ReferenceEquals(b.Command, model.ApplyTrimMarginCommand));
            Ancestors(margin).ShouldContain(bar);
            Button run = Find<Button>(tree, "Session.RunStep");
            Ancestors(run).ShouldContain(bar);
            return true;
        });
    }

    // Break caught: technical metadata is always exposed, crowding the current task; status loses neutral focus.
    [Fact]
    public async Task Technical_metadata_is_disclosed_without_hiding_failure_or_stop()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel model = await Open(harness);
        WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = model }, new Size(1000, 700), tree =>
        {
            Expander details = Find<Expander>(tree, "Session.TechnicalDetails");
            details.IsExpanded.ShouldBeFalse();
            FrameworkElement status = Find<FrameworkElement>(tree, "Session.OperatorStatus");
            status.Focusable.ShouldBeTrue();
            Ancestors(status).ShouldNotContain(details);
            Button stop = Find<Button>(tree, "Session.Stop");
            Ancestors(stop).ShouldNotContain(details);
            return true;
        });
    }

    internal static async Task<SessionViewModel> Open(HomeScreenHarness harness)
    {
        harness.FilePicker.Path = harness.Inner.WriteBorderedSourcePng("guided-synthetic.png");
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);
        SessionViewModel model = harness.Session(new RecordingNavigation());
        model.Open(harness.Navigation.WorkflowSelectionFor!);
        await model.PreviewsLoaded;
        return model;
    }

    internal static T Find<T>(RenderedTree tree, string id) where T : DependencyObject =>
        tree.OfType<T>().Single(x => AutomationProperties.GetAutomationId(x) == id);

    internal static Rect Bounds(FrameworkElement item, Visual root) =>
        item.TransformToAncestor(root).TransformBounds(new Rect(item.RenderSize));

    internal static IEnumerable<DependencyObject> Ancestors(DependencyObject item)
    {
        while ((item = VisualTreeHelper.GetParent(item)) is not null) yield return item;
    }
}
