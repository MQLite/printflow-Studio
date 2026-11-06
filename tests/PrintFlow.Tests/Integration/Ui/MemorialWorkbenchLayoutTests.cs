using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using PrintFlow.App.Controls;
using PrintFlow.App.ViewModels.AiDesign;
using PrintFlow.App.Views.AiDesign;
using PrintFlow.Domain.AiDesign.Memorial;
using PrintFlow.Domain.Results;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.AiDesign.Memorial;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class MemorialWorkbenchLayoutTests
{
    [Fact]
    public async Task Busy_render_can_be_cancelled_from_the_workbench()
    {
        var renderer = new PausedRenderer();
        using var fixture = new MemorialWorkflowAcceptanceTests.Fixture(renderer);
        await ConfirmLayoutAsync(fixture);
        var vm = fixture.Vm;
        Task rendering = vm.RenderMemorialPreviewCommand.ExecuteAsync(null);
        try
        {
            await renderer.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var facts = WpfRendering.RenderExpectingNoBindingErrors(() => Workbench(vm), new Size(800, 536), tree =>
            {
                Button stop = tree.OfType<Button>().Single(b => ReferenceEquals(b.Command, vm.CancelMemorialTaskCommand));
                Button save = tree.OfType<Button>().Single(b => ReferenceEquals(b.Command, vm.SaveDraftCommand));
                if (stop.IsEnabled)
                {
                    ((IInvokeProvider)new ButtonAutomationPeer(stop).GetPattern(PatternInterface.Invoke)!).Invoke();
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.SystemIdle);
                }
                return (StopEnabled: stop.IsEnabled, StopShown: GuidedSessionProjectionTests.Shown(stop),
                    StopKeyboardAccessible: stop.IsTabStop && stop.Focusable, SaveEnabled: save.IsEnabled);
            }).Facts;
            facts.StopEnabled.ShouldBeTrue();
            facts.StopShown.ShouldBeTrue();
            facts.StopKeyboardAccessible.ShouldBeTrue();
            facts.SaveEnabled.ShouldBeFalse();
            vm.RenderMemorialPreviewCommand.IsCancellationRequested.ShouldBeTrue();
        }
        finally
        {
            renderer.Finish.TrySetResult();
            await rendering;
        }
        vm.IsMemorialTaskRunning.ShouldBeFalse();
        vm.MemorialResult.ShouldBeNull();
    }

    [Theory]
    [InlineData(1000, 660)]
    [InlineData(800, 536)]
    public async Task Workbench_shows_both_proof_images_without_clipping(int width, int height)
    {
        using var fixture = new MemorialWorkflowAcceptanceTests.Fixture();
        await ConfirmLayoutAsync(fixture);
        var vm = fixture.Vm;
        await vm.RenderMemorialMaterialCommand.ExecuteAsync(null);
        var viewport = new Size(width, height);
        if (Environment.GetEnvironmentVariable("PRINTFLOW_MEMORIAL_WORKBENCH_EVIDENCE") is { Length: > 0 } evidence)
            WpfRendering.CapturePng(() => Workbench(vm), viewport, Path.Combine(evidence, $"workbench-{width}x{height}.png"));

        WpfRendering.RenderExpectingNoBindingErrors(() => Workbench(vm), viewport, tree =>
        {
            var images = tree.OfType<Image>().Where(image => image.Source is not null &&
                GuidedSessionProjectionTests.Shown(image)).ToArray();
            images.Length.ShouldBe(2);
            foreach (Image image in images)
            {
                image.ActualWidth.ShouldBeGreaterThan(70);
                image.ActualHeight.ShouldBeGreaterThan(70);
                ProofFractionVisible(image, tree.Root).ShouldBeGreaterThan(0.99);
            }
            return 0;
        });
    }

    [Fact]
    public async Task Proof_and_prompt_remain_reachable_when_switching_steps()
    {
        using var fixture = new MemorialWorkflowAcceptanceTests.Fixture();
        await ConfirmLayoutAsync(fixture);
        var vm = fixture.Vm;
        await vm.RenderMemorialMaterialCommand.ExecuteAsync(null);
        vm.DetailedStep = 4;
        WpfRendering.RenderExpectingNoBindingErrors(() => Workbench(vm), new Size(800, 536), tree =>
        {
            TabControl tabs = tree.OfType<TabControl>().Single();
            TabItem proofTab = tabs.Items.OfType<TabItem>().Single(item => Equals(item.Header, AiMemorialLabels.ProofTitle));
            TabItem promptTab = tabs.Items.OfType<TabItem>().Single(item => Equals(item.Header, AiPromptLabels.AiPrompt_FinalText));
            proofTab.Visibility.ShouldBe(Visibility.Collapsed);
            promptTab.IsSelected.ShouldBeTrue();

            vm.DetailedStep = 6;
            tree.Root.UpdateLayout();
            proofTab.IsSelected.ShouldBeTrue();
            promptTab.SetCurrentValue(TabItem.IsSelectedProperty, true);
            tree.Root.UpdateLayout();
            var editor = SharedReviewSurfaceTests.Descendants<AiPromptEditor>(tabs).Single();
            editor.IsEnabled.ShouldBeTrue();
            editor.Document.Parts.ShouldBe(vm.Document.Parts);

            vm.DetailedStep = 4;
            tree.Root.UpdateLayout();
            promptTab.IsSelected.ShouldBeTrue();
            vm.DetailedStep = 6;
            tree.Root.UpdateLayout();
            proofTab.IsSelected.ShouldBeTrue();
            return 0;
        });
    }

    [Fact]
    public async Task Final_step_does_not_offer_a_next_step()
    {
        using var fixture = new MemorialWorkflowAcceptanceTests.Fixture();
        await fixture.OpenReadyAsync();
        var vm = fixture.Vm;
        vm.DetailedStep = 6;
        WpfRendering.RenderExpectingNoBindingErrors(() => Workbench(vm), WpfRendering.ReviewViewport, tree =>
        {
            tree.OfType<Button>().Any(b => ReferenceEquals(b.Command, vm.NextDetailedStepCommand) &&
                GuidedSessionProjectionTests.Shown(b)).ShouldBeFalse();
            return 0;
        });
    }

    private static AiPromptWorkbenchView Workbench(AiPromptWorkbenchViewModel vm) => new() { DataContext = vm };

    private static async Task ConfirmLayoutAsync(MemorialWorkflowAcceptanceTests.Fixture fixture)
    {
        await fixture.OpenReadyAsync();
        var vm = fixture.Vm;
        await vm.PrepareMemorialLayoutCommand.ExecuteAsync(null);
        await vm.SaveMemorialLayoutCommand.ExecuteAsync(null);
        await vm.ConfirmMemorialLayoutCommand.ExecuteAsync(null);
        vm.CanPreviewLayout.ShouldBeTrue();
    }

    private static double ProofFractionVisible(Image image, FrameworkElement root)
    {
        Rect imageBounds = image.TransformToAncestor(root).TransformBounds(new Rect(image.RenderSize));
        Rect visible = Rect.Intersect(imageBounds, new Rect(root.RenderSize));
        foreach (FrameworkElement ancestor in GuidedSessionShellTests.Ancestors(image).OfType<FrameworkElement>())
        {
            Geometry? clip = LayoutInformation.GetLayoutClip(ancestor);
            if (clip is null || ancestor == root) continue;
            visible.Intersect(ancestor.TransformToAncestor(root).TransformBounds(clip.Bounds));
        }
        return visible.IsEmpty ? 0 : (visible.Width * visible.Height) / (imageBounds.Width * imageBounds.Height);
    }

    private sealed class PausedRenderer : IMemorialRenderer
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<OperationResult<MemorialRenderResult>> RenderAsync(MemorialRenderPlan plan, CancellationToken ct)
        {
            Started.TrySetResult();
            await Finish.Task;
            return OperationResult.Fail<MemorialRenderResult>(ct.IsCancellationRequested ? FailureCode.Cancelled : FailureCode.PreconditionNotMet, "Controlled render did not publish.");
        }
    }
}
