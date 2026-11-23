using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Trimming;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class GuidedSessionReviewFixTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Draft_modal_blocks_retained_and_fresh_crop_entry_without_losing_the_selection(bool discard)
    {
        using HomeScreenHarness h = new();
        var imported = (await h.Sessions.ImportAsync(WorkflowType.PrepareAsset,
            h.Inner.WriteOpaqueSourcePng(), "modal-crop", "tester", default)).Value;
        var m = h.Session(h.Navigation); m.Open(imported);
        await m.ConfirmOriginalCommand.ExecuteAsync(null);
        await m.SkipCommand.ExecuteAsync(null); await m.SkipCommand.ExecuteAsync(null);
        await m.RunStepCommand.ExecuteAsync(null); await m.PreviewsLoaded;
        m.BeginManualCropCommand.Execute(null);
        var layout = new CropSurfaceLayout(12, 10, 12, 10, 12, 10, m.IsFitToViewport, m.ZoomScale);
        m.TrySetCropSelection(layout, 3, 2, 9, 7).ShouldBeTrue();
        var selection = m.CropSelection;
        string persisted = JsonSerializer.Serialize((await h.Inner.Repository.LoadAsync(m.Id, default)).Value);
        WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = m }, new Size(1000, 700), tree =>
        {
            var button = GuidedSessionShellTests.Find<Button>(tree, "Session.BeginManualCrop");
            var retained = GuidedSessionInputTests.Provider(button);
            m.BackToHomeCommand.Execute(null);
            m.IsConfirmingDraftLeave.ShouldBeTrue();
            tree.Root.UpdateLayout();
            bool enabledDuringPrompt = button.IsEnabled;
            retained.Invoke();
            GuidedSessionInputTests.Provider(button).Invoke();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.SystemIdle);
            m.BeginManualCropCommand.Execute(null); // Command callers obey the same modal boundary.
            m.CancelManualCropCommand.Execute(null);
            m.TrySetCropSelection(layout, 1, 1, 10, 9).ShouldBeFalse();
            m.CropSelection.ShouldBe(selection);
            enabledDuringPrompt.ShouldBeFalse();
            if (discard) m.DiscardDraftAndLeaveCommand.Execute(null);
            else m.KeepDraftCommand.Execute(null);
            m.IsConfirmingDraftLeave.ShouldBeFalse();
            m.IsCropping.ShouldBe(!discard);
            m.CropSelection.ShouldBe(discard ? null : selection);
            return true;
        });
        JsonSerializer.Serialize((await h.Inner.Repository.LoadAsync(m.Id, default)).Value).ShouldBe(persisted);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Preflight_white_ink_changes_in_place_and_blocks_generation_until_saved(bool changeBranch, bool refuse)
    {
        using HomeScreenHarness h = new();
        var (m, fault) = await Open(h, WorkflowType.GeneratePrintTiff);
        await m.ConfirmOriginalCommand.ExecuteAsync(null);
        m.WidthMmText = "200"; m.HeightMmText = "150";
        await m.SetMaximumBoundsCommand.ExecuteAsync(null);
        m.SelectedWhiteUnderbaseChoice = m.WhiteUnderbaseChoices.Single(c => c.Branch == WhiteUnderbaseBranch.W1_1px);
        await m.SelectWhiteUnderbaseCommand.ExecuteAsync(null);
        m.GuidedPage.ShouldBe(GuidedSessionPage.Preflight);
        var preflight = m.Preflight;
        var current = m.WhiteUnderbaseChoices.Single(c => c.Branch == WhiteUnderbaseBranch.W1_1px);
        var requested = m.WhiteUnderbaseChoices.Single(c => c.Branch == WhiteUnderbaseBranch.W1_2px);
        int initialCalls = fault.ExecuteCalls;
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        fault.ExecuteGate = release.Task;
        fault.Refuse = refuse;
        string before = JsonSerializer.Serialize((await h.Inner.Repository.LoadAsync(m.Id, default)).Value);
        WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = m }, new Size(984, 661), tree =>
        {
            var choices = GuidedSessionShellTests.Find<ListBox>(tree, "Session.WhiteUnderbaseChoices");
            var run = GuidedSessionShellTests.Find<Button>(tree, "Session.RunStep");
            var summary = GuidedSessionShellTests.Find<TextBlock>(tree, "Session.PrintSummary.WhiteInk");
            choices.SelectedItem.ShouldBeSameAs(current);
            if (!changeBranch)
            {
                choices.SetCurrentValue(ListBox.SelectedItemProperty, current);
                fault.ExecuteCalls.ShouldBe(initialCalls);
                return true;
            }

            var previousContext = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                choices.SetCurrentValue(ListBox.SelectedItemProperty, requested);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.SystemIdle);
            m.GuidedPage.ShouldBe(GuidedSessionPage.Preflight);
            m.IsBusy.ShouldBeTrue();
            choices.IsEnabled.ShouldBeFalse();
            GuidedSessionProjectionTests.Shown(run).ShouldBeTrue();
            run.IsEnabled.ShouldBeFalse();
            summary.Text.ShouldContain(current.Label);

            var finished = m.ApplyGuidedWhiteInkCommand.ExecutionTask!.WaitAsync(TimeSpan.FromSeconds(10));
            DispatcherFrame frame = new();
            _ = finished.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
            release.SetResult();
            Dispatcher.PushFrame(frame);
            finished.GetAwaiter().GetResult();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.SystemIdle);
            m.GuidedPage.ShouldBe(GuidedSessionPage.Preflight);
            choices.SelectedItem.ShouldBeSameAs(requested);
            choices.IsEnabled.ShouldBeTrue();
            run.IsEnabled.ShouldBe(!refuse);
            summary.Text.ShouldContain(refuse ? current.Label : requested.Label);
            fault.ExecuteCalls.ShouldBe(initialCalls + 1);
            if (refuse)
            {
                m.RunStepCommand.ExecuteAsync(null).IsCompletedSuccessfully.ShouldBeTrue();
                fault.ExecuteCalls.ShouldBe(initialCalls + 1);
                GuidedSessionProjectionTests.Shown(GuidedSessionShellTests.Find<Button>(tree, "Session.WhiteUnderbase.Retry")).ShouldBeTrue();
                choices.SetCurrentValue(ListBox.SelectedItemProperty, current);
                run.IsEnabled.ShouldBeTrue();
                fault.ExecuteCalls.ShouldBe(initialCalls + 1);
            }
            return true;
        });

        if (refuse || !changeBranch)
            JsonSerializer.Serialize((await h.Inner.Repository.LoadAsync(m.Id, default)).Value).ShouldBe(before);
        else
        {
            m.Preflight.ShouldBe(preflight);
            m.Preflight.ShouldNotBeSameAs(preflight);
            (await h.Sessions.LoadAsync(m.Id, default)).Value.WhiteUnderbaseBranch.ShouldBe(requested.Branch);
            var resumed = h.Session(new RecordingNavigation());
            resumed.Open((await h.Sessions.LoadAsync(m.Id, default)).Value);
            await resumed.RunStepCommand.ExecuteAsync(null);
            resumed.GuidedPage.ShouldBe(GuidedSessionPage.Review);
            (await h.Inner.Repository.LoadAsync(m.Id, default)).Value!.Outputs.Single().Branch.ShouldBe(requested.Branch);
        }
    }

    [Fact]
    public async Task White_ink_arrow_navigation_keeps_focus_after_saving_and_retires_the_old_selection_provider()
    {
        using HomeScreenHarness h = new();
        var (model, fault) = await Open(h, WorkflowType.GeneratePrintTiff);
        await model.ConfirmOriginalCommand.ExecuteAsync(null);
        model.WidthMmText = "200";
        model.HeightMmText = "150";
        await model.SetMaximumBoundsCommand.ExecuteAsync(null);
        model.SelectedWhiteUnderbaseChoice = model.WhiteUnderbaseChoices[1];
        await model.SelectWhiteUnderbaseCommand.ExecuteAsync(null);
        int callsBeforeChange = fault.ExecuteCalls;

        WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = model }, new Size(984, 661), tree =>
        {
            Window window = new() { Content = tree.Root, Width = 984, Height = 661 };
            try
            {
                bool contentRendered = false;
                window.ContentRendered += (_, _) => contentRendered = true;
                window.Show();
                tree.Root.UpdateLayout();
                // Let Window finish its initial focus landing before simulating operator input.
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.SystemIdle);
                contentRendered.ShouldBeTrue();
                var choices = GuidedSessionShellTests.Find<ListBox>(tree, "Session.WhiteUnderbaseChoices");
                var starting = (ListBoxItem)choices.ItemContainerGenerator.ContainerFromIndex(1);
                starting.BringIntoView();
                starting.Focus().ShouldBeTrue();
                var peer = UIElementAutomationPeer.CreatePeerForElement(choices)!;
                var itemPeer = (AutomationPeer)peer.GetType().GetMethod("CreateItemAutomationPeer", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(peer, [model.WhiteUnderbaseChoices[1]])!;
                var retired = (ISelectionItemProvider)itemPeer.GetPattern(PatternInterface.SelectionItem)!;

                var previousContext = SynchronizationContext.Current;
                try
                {
                    SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                    var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(starting), Environment.TickCount, Key.Down)
                    {
                        RoutedEvent = Keyboard.PreviewKeyDownEvent,
                    };
                    starting.RaiseEvent(key);
                    key.RoutedEvent = Keyboard.KeyDownEvent;
                    starting.RaiseEvent(key);
                }
                finally
                {
                    SynchronizationContext.SetSynchronizationContext(previousContext);
                }
                var finished = model.ApplyGuidedWhiteInkCommand.ExecutionTask.ShouldNotBeNull().WaitAsync(TimeSpan.FromSeconds(10));
                DispatcherFrame frame = new();
                _ = finished.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
                Dispatcher.PushFrame(frame);
                finished.GetAwaiter().GetResult();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.SystemIdle);

                choices.SelectedIndex.ShouldBe(2);
                ((ListBoxItem)choices.ItemContainerGenerator.ContainerFromIndex(2)).IsKeyboardFocusWithin.ShouldBeTrue(
                    $"keyboard focus is on {(Keyboard.FocusedElement as FrameworkElement)?.Name}");
                retired.Select();
                fault.ExecuteCalls.ShouldBe(callsBeforeChange + 1);
                choices.SelectedIndex.ShouldBe(2);
                return true;
            }
            finally
            {
                window.Content = null;
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Applied_trim_becomes_the_navigation_baseline_without_erasing_later_edits(bool laterEdit)
    {
        using HomeScreenHarness h = new();
        var (m, _) = await Open(h, WorkflowType.PrepareAsset);
        await m.ConfirmOriginalCommand.ExecuteAsync(null);
        await m.SkipCommand.ExecuteAsync(null); await m.SkipCommand.ExecuteAsync(null);
        m.SelectedTrimMode = m.TrimModes.Single(c => c.Mode == TrimMode.UniformMargin);
        m.UniformMarginText = "2";
        await m.RunStepCommand.ExecuteAsync(null);
        string saved = JsonSerializer.Serialize((await h.Inner.Repository.LoadAsync(m.Id, default)).Value);
        if (laterEdit)
        {
            m.UniformMarginText = "3";
            m.Open((await h.Sessions.LoadAsync(m.Id, default)).Value);
            m.UniformMarginText.ShouldBe("3");
        }
        m.ViewHistoryCommand.Execute(m.Steps.First());
        m.IsConfirmingDraftLeave.ShouldBe(laterEdit);
        if (laterEdit) await m.DiscardDraftAndLeaveCommand.ExecuteAsync(null);
        m.IsViewingHistory.ShouldBeTrue();
        m.CloseHistoryCommand.Execute(null);
        m.SelectedTrimMode.Mode.ShouldBe(TrimMode.UniformMargin);
        m.UniformMarginText.ShouldBe("2");
        (await h.Sessions.LoadAsync(m.Id, default)).Value.TrimMargin.ShouldBe(TrimMargin.Uniform(2));
        JsonSerializer.Serialize((await h.Inner.Repository.LoadAsync(m.Id, default)).Value).ShouldBe(saved);
    }

    [Fact]
    public async Task Refused_trim_keeps_the_draft_and_does_not_move_the_confirmed_baseline()
    {
        using HomeScreenHarness h = new();
        var (m, fault) = await Open(h, WorkflowType.PrepareAsset);
        await m.ConfirmOriginalCommand.ExecuteAsync(null);
        await m.SkipCommand.ExecuteAsync(null); await m.SkipCommand.ExecuteAsync(null);
        string before = JsonSerializer.Serialize((await h.Inner.Repository.LoadAsync(m.Id, default)).Value);
        m.SelectedTrimMode = m.TrimModes.Single(c => c.Mode == TrimMode.UniformMargin);
        m.UniformMarginText = "2";
        fault.Refuse = true;
        await m.RunStepCommand.ExecuteAsync(null);
        m.UniformMarginText.ShouldBe("2");
        m.ViewHistoryCommand.Execute(m.Steps.First());
        m.IsConfirmingDraftLeave.ShouldBeTrue();
        await m.DiscardDraftAndLeaveCommand.ExecuteAsync(null);
        m.CloseHistoryCommand.Execute(null);
        m.SelectedTrimMode.Mode.ShouldBe(TrimMode.TightCrop);
        m.UniformMarginText.ShouldBe("0");
        JsonSerializer.Serialize((await h.Inner.Repository.LoadAsync(m.Id, default)).Value).ShouldBe(before);
    }

    private static async Task<(SessionViewModel Model, RefusingService Fault)> Open(HomeScreenHarness h, WorkflowType route)
    {
        var session = (await h.Sessions.ImportAsync(route, h.Inner.WriteBorderedSourcePng(), "review-fix", "tester", default)).Value;
        var service = DispatchProxy.Create<ISessionService, RefusingService>();
        var fault = (RefusingService)(object)service; fault.Inner = h.Sessions;
        var model = new SessionViewModel(service, h.Previews, h.TiffReviews, h.Navigation);
        model.Open(session); await model.PreviewsLoaded;
        return (model, fault);
    }

    public class RefusingService : DispatchProxy
    {
        public ISessionService Inner { get; set; } = null!;
        public bool Refuse { get; set; }
        public Task ExecuteGate { get; set; } = Task.CompletedTask;
        public int ExecuteCalls { get; private set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            method!.Name == nameof(ISessionService.ExecuteAsync) ? ExecuteAsync(method, args) : method.Invoke(Inner, args);

        private async Task<OperationResult<SessionView>> ExecuteAsync(MethodInfo method, object?[]? args)
        {
            ExecuteCalls++;
            await ExecuteGate.ConfigureAwait(false);
            return Refuse
                ? OperationResult<SessionView>.Failed(FailureCode.EnvironmentNotVerified, "Controlled review regression refusal")
                : await ((Task<OperationResult<SessionView>>)method.Invoke(Inner, args)!).ConfigureAwait(false);
        }
    }
}
