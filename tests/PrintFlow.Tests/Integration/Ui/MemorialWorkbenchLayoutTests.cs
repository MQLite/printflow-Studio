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

    [Fact]
    public async Task Saving_layout_completes_step_five_without_a_confirmation_or_preview()
    {
        using var fixture = new MemorialWorkflowAcceptanceTests.Fixture();
        await fixture.OpenReadyAsync();
        var vm = fixture.Vm;
        await vm.SwitchToDetailedCommand.ExecuteAsync(null);
        vm.DetailedStep = 5;
        await vm.PrepareMemorialLayoutCommand.ExecuteAsync(null);
        await vm.SaveMemorialLayoutCommand.ExecuteAsync(null);
        vm.Draft!.Layout.ShouldNotBeNull();
        vm.DetailedStep = 6;
        vm.DetailedSteps.Single(step => step.Number == 5).Status.ShouldBe(PrintFlow.App.Resources.Strings.AiDetail_Confirmed);

        await vm.SwitchToDetailedCommand.ExecuteAsync(null);
        vm.DetailedStep = 5;
        WpfRendering.RenderExpectingNoBindingErrors(() => Workbench(vm), WpfRendering.ReviewViewport, tree =>
        {
            var page = tree.OfType<AiGenerationEditorView>().Single();
            ((Expander)page.FindName("DraftTools")).IsExpanded = true;
            var review = tree.OfType<AiGenerationReviewView>().Single();
            ((Expander)review.FindName("ReviewOptions")).IsExpanded = true;
            ((Expander)review.FindName("LayoutOptions")).IsExpanded = true;
            tree.Root.UpdateLayout();
            tree.OfType<Button>().Any(button => GuidedSessionProjectionTests.Shown(button) &&
                ReferenceEquals(button.Command, vm.SaveMemorialLayoutCommand)).ShouldBeTrue();
            tree.OfType<Button>().Any(button => GuidedSessionProjectionTests.Shown(button) &&
                (System.Windows.Automation.AutomationProperties.GetAutomationId(button) == "Memorial.ConfirmLayout" ||
                 ReferenceEquals(button.Command, vm.RenderMemorialPreviewCommand))).ShouldBeFalse();
            return 0;
        });
    }

    [Fact]
    public async Task Final_step_shows_the_prompt_and_attachment_list_without_local_result_actions()
    {
        using var fixture = new MemorialWorkflowAcceptanceTests.Fixture();
        await fixture.OpenReadyAsync();
        var vm = fixture.Vm;
        await vm.SwitchToDetailedCommand.ExecuteAsync(null);
        vm.DetailedStep = 6;
        WpfRendering.RenderExpectingNoBindingErrors(() => Workbench(vm), WpfRendering.ReviewViewport, tree =>
        {
            tree.OfType<AiPromptEditor>().Single().Document.Parts.ShouldBe(vm.Document.Parts);
            tree.OfType<Expander>().Any(expander => System.Windows.Automation.AutomationProperties.GetAutomationId(expander) == "AiPrompt.ReferenceTools" &&
                GuidedSessionProjectionTests.Shown(expander)).ShouldBeTrue();
            tree.OfType<Button>().Any(button => ReferenceEquals(button.Command, vm.CopyCommand) &&
                GuidedSessionProjectionTests.Shown(button)).ShouldBeTrue();
            tree.OfType<Button>().Any(button => GuidedSessionProjectionTests.Shown(button) &&
                (ReferenceEquals(button.Command, vm.ComposeCommand) ||
                 ReferenceEquals(button.Command, vm.RenderMemorialMaterialCommand) ||
                 ReferenceEquals(button.Command, vm.RefreshMemorialResultsCommand))).ShouldBeFalse();
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
        vm.CanPreviewLayout.ShouldBeTrue();
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
