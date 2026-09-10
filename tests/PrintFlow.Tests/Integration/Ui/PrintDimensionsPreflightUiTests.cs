using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using PrintFlow.App.Resources;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Trimming;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;
using static PrintFlow.Tests.Integration.Persistence.KeepOriginalExtentPersistenceTests;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class PrintDimensionsPreflightUiTests
{
    [Theory]
    [InlineData("en-US", "Effective source resolution", "No enlargement required", "Enlargement required")]
    [InlineData("zh-CN", "源图有效分辨率", "无需放大", "需要放大")]
    public async Task Rendered_drafts_update_through_UIA_without_recording_a_size(
        string culture, string effectiveLabel, string normalStatus, string enlargementStatus)
    {
        using HomeScreenHarness h = new();
        SessionView state = await AtDimensions(h);
        SessionAggregate before = await Load(h.Inner, state.Id);
        string px = culture == "zh-CN" ? "像素" : "px";
        string mm = culture == "zh-CN" ? "毫米" : "mm";
        WpfRendering.RenderExpectingNoBindingErrors(() => CreateView(h, state, culture),
            WpfRendering.ReviewViewport, tree =>
            {
                SessionViewModel model = (SessionViewModel)tree.Root.DataContext;
                model.ChooseCustomSizeCommand.Execute(null);
                model.SelectedTargetEdgeChoice = model.TargetEdgeChoices.Single(c => c.Edge == TargetEdge.Width);
                tree.Root.UpdateLayout();
                TextBox input = Control<TextBox>(tree.Root, "TargetMillimetres");
                SetValue(input, "25.4");
                PumpUntil(() => model.PreflightLoaded.IsCompleted);
                model.PreflightLoaded.GetAwaiter().GetResult();
                tree.Root.UpdateLayout();
                AssertRow(tree.Root, "SourcePixels", $"600 × 300 {px}");
                AssertRow(tree.Root, "PrintSize", $"25.4 × 12.7 {mm}");
                AssertRow(tree.Root, "OutputPixels", $"300 × 150 {px}");
                AssertRow(tree.Root, "EffectiveDpi", "600 × 600 PPI", effectiveLabel);
                AssertRow(tree.Root, "OutputDpi", "300 PPI");
                AssertRow(tree.Root, "EnlargementStatus", normalStatus);
                model.IsDraftPreflight.ShouldBeTrue();
                model.Preflight!.EnlargementOfferId.ShouldBeNull();

                SetValue(input, "101.6");
                PumpUntil(() => model.PreflightLoaded.IsCompleted);
                model.PreflightLoaded.GetAwaiter().GetResult();
                tree.Root.UpdateLayout();
                AssertRow(tree.Root, "EffectiveDpi", "150 × 150 PPI", effectiveLabel);
                AssertRow(tree.Root, "OutputPixels", $"1200 × 600 {px}");
                AssertRow(tree.Root, "EnlargementStatus", enlargementStatus);
                model.Preflight!.EnlargementAuthorised.ShouldBeFalse();
                model.CanAuthoriseEnlargement.ShouldBeFalse();

                SetValue(input, "");
                model.HasPrintDimensionsPreflight.ShouldBeFalse();
                model.PreflightRows.ShouldBeEmpty();
                return true;
            });
        SessionAggregate after = await Load(h.Inner, state.Id);
        after.Session.ShouldBe(before.Session);
        after.Steps.ShouldBe(before.Steps);
        after.Attempts.ShouldBe(before.Attempts);
        after.Revisions.ShouldBe(before.Revisions);
        after.Outputs.ShouldBe(before.Outputs);
        foreach (string size in new[] { "25.4", "101.6" })
        {
            Capture(h, state, culture, size == "25.4" ? "normal" : "enlargement", size);
        }
    }

    [Theory]
    [InlineData("automatic", "Artwork content", "5 × 5 px", "9 × 9 px")]
    [InlineData("manual", "Selected artwork", "6 × 5 px", "10 × 9 px")]
    [InlineData("keep", "Artwork extent", "Full original canvas", "12 × 10 px")]
    public async Task Persisted_geometry_is_rendered_from_the_exact_approved_artwork(
        string path, string label, string artwork, string canvas)
    {
        using HomeScreenHarness h = new();
        SessionId id = await AtTrim(h.Inner, h.Sessions, WorkflowType.PrepareCustomerDesign, opaque: path == "manual");
        SessionView state;
        if (path == "keep")
        {
            state = await Execute(h.Sessions, id, new WorkflowCommand.KeepOriginalExtent());
        }
        else
        {
            if (path == "automatic")
                await Execute(h.Sessions, id, new WorkflowCommand.SetTrimParameters(TrimMargin.Uniform(2)));
            state = await RunTrim(h.Sessions, id, manual: path == "manual");
            if (path == "manual")
                state = await Execute(h.Sessions, id, new WorkflowCommand.SubmitManualCrop(
                    StepKind.Trim, TrimBounds.FromEdges(3, 2, 9, 7), ManualCropMargin.Uniform(2)));
            state = await Execute(h.Sessions, id, new WorkflowCommand.Approve(StepKind.Trim, state.CurrentArtefact!.Sha256));
        }
        state = await Execute(h.Sessions, id, new WorkflowCommand.SetCustomTargetEdgeSize(TargetEdge.Width, 25.4m));
        // New service and view model: no editor or process-local geometry can supply these values.
        RestartedSession restarted = h.RestartSession(new RecordingNavigation());
        state = (await restarted.Sessions.LoadAsync(id, CancellationToken.None)).Value;
        WpfRendering.RenderExpectingNoBindingErrors(() => CreateView(h, state, "en-US"),
            WpfRendering.ReviewViewport, tree =>
            {
                AssertRow(tree.Root, "GraphicBounds", artwork, label);
                AssertRow(tree.Root, "FinalCanvas", canvas);
                AssertRow(tree.Root, "SourcePixels", canvas);
                return true;
            });
        Capture(h, state, "en-US", path, draftSize: null);
    }

    [Fact]
    public async Task Changing_maximum_bounds_reprojects_and_a_preset_uses_the_configured_authority()
    {
        using HomeScreenHarness h = new();
        SessionView state = await AtDimensions(h);
        SessionViewModel model = h.Session(new RecordingNavigation());
        model.Open(state);
        model.WidthMmText = "25.4";
        model.HeightMmText = "25.4";
        await model.PreflightLoaded;
        model.Preflight!.OutputPixelWidth.ShouldBe(300);
        model.Preflight.EffectiveSourcePpiX.ShouldBe(600);
        model.HeightMmText = "6.35";
        await model.PreflightLoaded;
        model.Preflight!.OutputPixelHeight.ShouldBe(75);
        model.Preflight.EffectiveSourcePpiY.ShouldBe(1200);
        model.ApplyPresetCommand.Execute(model.SizePresets.Single(p => p.Preset == SizePreset.A4));
        await model.PreflightLoaded;
        PrintDimensionsPreflight presetDraft = model.Preflight!;
        await model.SetMaximumBoundsCommand.ExecuteAsync(null);
        model.Preflight.ShouldBe(presetDraft);
        model.IsDraftPreflight.ShouldBeFalse();
    }

    [Fact]
    public async Task A_late_query_cannot_replace_a_newer_size_or_newly_opened_artwork()
    {
        using HomeScreenHarness h = new();
        SessionView state = await AtDimensions(h);
        GatedPreflightService service = new(h.Sessions);
        SessionViewModel model = new(service, h.Previews, h.TiffReviews, new RecordingNavigation());
        model.Open(state);
        model.ChooseCustomSizeCommand.Execute(null);
        model.SelectedTargetEdgeChoice = model.TargetEdgeChoices.Single(c => c.Edge == TargetEdge.Width);
        service.HoldNext();
        model.CustomMillimetresText = "25.4";
        Task oldSize = model.PreflightLoaded;
        await service.Started;
        model.CustomMillimetresText = "101.6";
        await model.PreflightLoaded;
        model.Preflight!.EffectiveSourcePpiX.ShouldBe(150);
        service.Release();
        await oldSize;
        model.Preflight!.EffectiveSourcePpiX.ShouldBe(150);

        service.HoldNext();
        model.CustomMillimetresText = "12.7";
        Task oldArtwork = model.PreflightLoaded;
        await service.Started;
        SessionView another = await AtDimensions(h);
        model.Open(another);
        model.Preflight.ShouldBeNull();
        service.Release();
        await oldArtwork;
        model.Preflight.ShouldBeNull();
        model.PreflightRows.ShouldBeEmpty();
    }

    [Fact]
    public async Task Keyboard_traverses_the_bound_custom_size_controls_and_panel_does_not_trap_focus()
    {
        using HomeScreenHarness h = new();
        SessionView state = await AtDimensions(h);
        WpfRendering.RenderExpectingNoBindingErrors(() => CreateView(h, state, "en-US"),
            WpfRendering.ReviewViewport, tree =>
            {
                SessionViewModel model = (SessionViewModel)tree.Root.DataContext;
                model.ChooseCustomSizeCommand.Execute(null);
                model.SelectedTargetEdgeChoice = model.TargetEdgeChoices.Single(c => c.Edge == TargetEdge.Width);
                model.CustomMillimetresText = "25.4";
                PumpUntil(() => model.PreflightLoaded.IsCompleted);
                Window window = new() { Title = "PrintFlow bounded preflight keyboard proof", Content = tree.Root, Width = 1000, Height = 700 };
                try
                {
                    window.Show();
                    tree.Root.UpdateLayout();
                    ComboBox edge = Control<ComboBox>(tree.Root, "TargetEdge");
                    TextBox millimetres = Control<TextBox>(tree.Root, "TargetMillimetres");
                    Button confirm = Control<Button>(tree.Root, "ConfirmSize");
                    edge.BringIntoView();
                    edge.Focus().ShouldBeTrue();
                    edge.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)).ShouldBeTrue();
                    millimetres.IsKeyboardFocused.ShouldBeTrue();
                    millimetres.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)).ShouldBeTrue();
                    confirm.IsKeyboardFocused.ShouldBeTrue();
                    confirm.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)).ShouldBeTrue();
                    confirm.IsKeyboardFocused.ShouldBeFalse();
                    return true;
                }
                finally
                {
                    window.Content = null;
                    window.Close();
                }
            });
    }

    private static SessionScreenView CreateView(HomeScreenHarness h, SessionView state, string culture)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        SessionViewModel model = h.Session(new RecordingNavigation());
        model.Open(state);
        return new SessionScreenView { DataContext = model };
    }

    private static void AssertRow(DependencyObject root, string key, string value, string? label = null)
    {
        TextBlock block = Control<TextBlock>(root, key);
        block.Text.ShouldBe(value);
        TextBlockAutomationPeer peer = new(block);
        peer.GetAutomationId().ShouldBe("Session.PrintDimensions." + key);
        peer.GetName().ShouldEndWith(": " + value);
        if (label is not null) peer.GetName().ShouldBe(label + ": " + value);
    }

    private static T Control<T>(DependencyObject root, string key) where T : DependencyObject =>
        SharedReviewSurfaceTests.Descendants<T>(root)
            .Single(e => AutomationProperties.GetAutomationId(e) == "Session.PrintDimensions." + key);

    private static void SetValue(TextBox box, string value) =>
        ((IValueProvider)new TextBoxAutomationPeer(box).GetPattern(PatternInterface.Value)!).SetValue(value);

    private static void PumpUntil(Func<bool> done)
    {
        Stopwatch elapsed = Stopwatch.StartNew();
        while (!done() && elapsed.Elapsed < TimeSpan.FromSeconds(10))
        {
            DispatcherFrame frame = new();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(1);
        }
        done().ShouldBeTrue("The bounded WPF query should complete.");
    }

    private static async Task<SessionView> AtDimensions(HomeScreenHarness h)
    {
        string file = h.Inner.Workspace.CreateSourceFile("preflight.png", SyntheticImages.Png(600, 300));
        SessionView imported = (await h.Sessions.ImportAsync(WorkflowType.GeneratePrintTiff, file, "preflight", "tester", CancellationToken.None)).Value;
        return await Execute(h.Sessions, imported.Id, new WorkflowCommand.ConfirmOriginal());
    }

    private static void Capture(HomeScreenHarness h, SessionView state, string culture, string name, string? draftSize)
    {
        string? directory = Environment.GetEnvironmentVariable("PRINTFLOW_PREFLIGHT_VISUAL_OUTPUT");
        if (string.IsNullOrWhiteSpace(directory)) return;
        WpfRendering.CapturePng(() =>
        {
            SessionScreenView view = CreateView(h, state, culture);
            SessionViewModel model = (SessionViewModel)view.DataContext;
            if (draftSize is not null)
            {
                model.ChooseCustomSizeCommand.Execute(null);
                model.SelectedTargetEdgeChoice = model.TargetEdgeChoices.Single(c => c.Edge == TargetEdge.Width);
                model.CustomMillimetresText = draftSize;
                PumpUntil(() => model.PreflightLoaded.IsCompleted);
            }
            return view;
        }, WpfRendering.ReviewViewport, Path.Combine(directory, $"{culture}-{name}.png"),
            tree =>
            {
                Control<Border>(tree.Root, "Preflight").BringIntoView();
                SharedReviewSurfaceTests.Settle(tree.Root);
            });
    }

    /// <summary>Delays one real query result, without replacing its calculation or database.</summary>
    private sealed class GatedPreflightService(ISessionService inner) : ISessionService
    {
        private TaskCompletionSource? _gate;
        private TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _hold;
        public Task Started => _started.Task;
        public void HoldNext()
        {
            _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _hold = true;
        }
        public void Release() => _gate!.SetResult();
        public async Task<OperationResult<PrintDimensionsPreflight>> PreviewPrintDimensionsAsync(
            SessionId id, WorkflowCommand command, CancellationToken cancellationToken)
        {
            TaskCompletionSource? gate = _hold ? _gate : null;
            _hold = false;
            var result = await inner.PreviewPrintDimensionsAsync(id, command, cancellationToken);
            if (gate is not null)
            {
                _started.SetResult();
                await gate.Task;
            }
            return result;
        }
        public event EventHandler<AutomationRuntimeView>? AutomationRuntimeChanged
        {
            add => inner.AutomationRuntimeChanged += value;
            remove => inner.AutomationRuntimeChanged -= value;
        }
        public AutomationRuntimeView GetAutomationRuntime(SessionId id) => inner.GetAutomationRuntime(id);
        public OperationResult<PrintFlow.Domain.Results.Unit> RequestStop(SessionId id, AutomationStopMode mode) => inner.RequestStop(id, mode);
        public Task<OperationResult<SessionView>> LoadAsync(SessionId id, CancellationToken token) => inner.LoadAsync(id, token);
        public Task<OperationResult<SessionView>> ImportAsync(WorkflowType type, string path, string? name, string? op, CancellationToken token) => inner.ImportAsync(type, path, name, op, token);
        public Task<OperationResult<SessionView>> ExecuteAsync(SessionId id, WorkflowCommand command, string? op, CancellationToken token) => inner.ExecuteAsync(id, command, op, token);
        public Task<OperationResult<SessionView>> AuthoriseCurrentEnlargementAsync(SessionId id, Guid offer, string? op, CancellationToken token) => inner.AuthoriseCurrentEnlargementAsync(id, offer, op, token);
        public Task<OperationResult<IReadOnlyList<RecoveryItem>>> ListRecoveryAsync(CancellationToken token) => inner.ListRecoveryAsync(token);
        public Task<OperationResult<SessionView>> ResolveRecoveryAsync(SessionId id, RecoveryAction action, string? path, string? op, CancellationToken token) => inner.ResolveRecoveryAsync(id, action, path, op, token);
        public Task<OperationResult<PrintFlow.Workflow.Services.ErrorDetailsView>> LoadErrorDetailsAsync(SessionId id, AttemptId attempt, CancellationToken token) => inner.LoadErrorDetailsAsync(id, attempt, token);
        public Task<OperationResult<SessionView>> ResolveErrorRecoveryAsync(SessionId id, AttemptId attempt, ErrorRecoveryAction action, string? op, CancellationToken token) => inner.ResolveErrorRecoveryAsync(id, attempt, action, op, token);
        public Task<OperationResult<IReadOnlyList<SessionListItem>>> ListRecentAsync(CancellationToken token) => inner.ListRecentAsync(token);
        public Task<OperationResult<PrintFlow.Domain.Results.Unit>> RemoveFromRecentAsync(SessionId id, CancellationToken token) => inner.RemoveFromRecentAsync(id, token);
    }
}
