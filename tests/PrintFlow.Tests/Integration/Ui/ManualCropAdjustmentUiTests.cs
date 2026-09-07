using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Shapes;
using System.Windows.Threading;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Trimming;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Services;
using Xunit.Abstractions;
using static PrintFlow.Tests.Integration.Persistence.KeepOriginalExtentPersistenceTests;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class ManualCropAdjustmentUiTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("en-US", "Tight", "Uniform margin", "Per-edge margin")]
    [InlineData("zh-CN", "紧贴选区", "统一边距", "分别设置边距")]
    public async Task Rendered_adjustments_validate_and_preview_the_exact_crop_without_persisting(
        string culture, string tight, string uniform, string edge)
    {
        using HomeScreenHarness h = new();
        SessionId id = await AtTrim(h.Inner, h.Sessions, WorkflowType.PrepareCustomerDesign, opaque: true);
        await Execute(h.Sessions, id, new WorkflowCommand.SetTrimParameters(TrimMargin.Uniform(5)));
        SessionView state = await RunTrim(h.Sessions, id, manual: true);
        SessionAggregate before = await Load(h.Inner, id);
        string[] files = Files(h);
        WpfRendering.Render(() =>
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            SessionViewModel model = h.Session(new RecordingNavigation()); model.Open(state);
            return new SessionScreenView { DataContext = model };
        }, new Size(1250, 900), tree =>
        {
            SessionViewModel model = (SessionViewModel)tree.Root.DataContext;
            PumpUntil(() => model.PreviewsLoaded.IsCompleted);
            Invoke(Control<Button>(tree.Root, "BeginManualCrop"));
            PumpUntil(() => model.IsCropping);
            Select(model, tree.Root);
            model.CropAppliedBounds.ShouldBe(TrimBounds.FromEdges(3, 2, 9, 7));
            foreach ((string suffix, string label) in new[] { ("Tight", tight), ("Uniform", uniform), ("PerEdge", edge) })
            {
                RadioButton button = Control<RadioButton>(tree.Root, "ManualCropMode" + suffix);
                RadioButtonAutomationPeer peer = new(button);
                peer.GetName().ShouldBe(label);
                peer.GetAutomationId().ShouldBe("Session.ManualCropMode" + suffix);
                button.IsEnabled.ShouldBeTrue(); button.Focusable.ShouldBeTrue();
                KeyboardNavigation.GetIsTabStop(button).ShouldBeTrue();
            }
            SelectMode(tree.Root, "Uniform");
            TextBox box = Control<TextBox>(tree.Root, "ManualCropUniformMargin");
            ((StackPanel)box.Parent).Visibility.ShouldBe(Visibility.Visible);
            new TextBoxAutomationPeer(box).GetName().ShouldBe(uniform);
            SetValue(box, "2");
            model.CropAppliedBounds.ShouldBe(TrimBounds.FromEdges(1, 0, 11, 9));
            AssertOutlines(tree.Root, model);
            foreach (string invalid in new[] { "-1", "", "oops", "1.5", "2147483648" })
            {
                SetValue(box, invalid);
                model.IsManualCropMarginInvalid.ShouldBeTrue();
                Control<Button>(tree.Root, "ApplyManualCrop").IsEnabled.ShouldBeFalse();
                model.CropAppliedBounds.ShouldBeNull();
                SharedReviewSurfaceTests.Settle((FrameworkElement)tree.Root);
                Control<Rectangle>(tree.Root, "ManualCropAppliedOutline").Visibility.ShouldBe(Visibility.Collapsed);
            }
            SetValue(box, "2147483647");
            model.CropAppliedBounds.ShouldBe(TrimBounds.Canvas(12, 10));
            model.CropSelection.ShouldBe(TrimBounds.FromEdges(3, 2, 9, 7));
            SelectMode(tree.Root, "PerEdge");
            SetValue(Control<TextBox>(tree.Root, "ManualCropMarginLeft"), "2");
            SetValue(Control<TextBox>(tree.Root, "ManualCropMarginTop"), "0");
            SetValue(Control<TextBox>(tree.Root, "ManualCropMarginRight"), "4");
            SetValue(Control<TextBox>(tree.Root, "ManualCropMarginBottom"), "1");
            model.CropAppliedBounds.ShouldBe(TrimBounds.FromEdges(1, 2, 12, 8));
            model.DraftManualCropGeometry!.Margin.ShouldBe(ManualCropMargin.PerEdge(0, 4, 1, 2));
            AssertOutlines(tree.Root, model);
            Control<TextBlock>(tree.Root, "ManualCropAppliedBounds").Text.ShouldContain("[1, 2 → 12, 8)");
            model.TrySetCropSelection(new CropSurfaceLayout(12, 10, 12, 10, 12, 10, true, 1), -1, 2, 9, 7).ShouldBeFalse();
            model.CanApplyManualCrop.ShouldBeFalse();
            Select(model, tree.Root);
            SelectMode(tree.Root, "Tight");
            model.CropAppliedBounds.ShouldBe(model.CropSelection);
            Control<Button>(tree.Root, "KeepOriginalExtent").Visibility.ShouldBe(Visibility.Visible);
            Invoke(Control<Button>(tree.Root, "CancelManualCrop"));
            PumpUntil(() => !model.IsCropping);
            model.ManualCropMode.ShouldBe(TrimMode.TightCrop);
            model.ManualCropUniformMargin.ShouldBe("0");
            model.CropAppliedBounds.ShouldBeNull();
            model.CanManualCrop.ShouldBeTrue();
            return true;
        }).BindingErrors.ShouldBeEmpty();
        SessionAggregate after = await Load(h.Inner, id);
        after.Session.ShouldBe(before.Session); after.Steps.ShouldBe(before.Steps);
        after.Attempts.ShouldBe(before.Attempts); after.Revisions.ShouldBe(before.Revisions); after.Reviews.ShouldBe(before.Reviews);
        Files(h).ShouldBe(files);
        after.Session.TrimMargin.ShouldBe(TrimMargin.Uniform(5));
    }

    [Theory]
    [InlineData("en-US", "Manual crop", "Selected bounds", "Applied crop bounds")]
    [InlineData("zh-CN", "手动裁切", "所选范围", "实际裁切范围")]
    public async Task Rendered_apply_uses_real_command_and_review_metadata_survives_restart(
        string culture, string heading, string selected, string applied)
    {
        using HomeScreenHarness h = new();
        SessionId id = await AtTrim(h.Inner, h.Sessions, WorkflowType.PrepareCustomerDesign, opaque: true);
        SessionView state = await RunTrim(h.Sessions, id, manual: true);
        WpfRendering.Render(() =>
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            SessionViewModel model = h.Session(new RecordingNavigation()); model.Open(state);
            return new SessionScreenView { DataContext = model };
        }, new Size(1250, 900), tree =>
        {
            SessionViewModel model = (SessionViewModel)tree.Root.DataContext;
            PumpUntil(() => model.PreviewsLoaded.IsCompleted);
            Invoke(Control<Button>(tree.Root, "BeginManualCrop")); PumpUntil(() => model.IsCropping);
            Select(model, tree.Root); SelectMode(tree.Root, "Uniform");
            SetValue(Control<TextBox>(tree.Root, "ManualCropUniformMargin"), "2");
            Invoke(Control<Button>(tree.Root, "ApplyManualCrop"));
            PumpUntil(() => !model.IsBusy && model.IsReviewRequired);
            model.HasManualCropGeometry.ShouldBeTrue(); model.HasTrimBounds.ShouldBeFalse();
            model.ManualCropHeading.ShouldBe(heading);
            model.ManualCropReviewSelected.ShouldStartWith(selected);
            model.ManualCropReviewApplied.ShouldStartWith(applied);
            model.ManualCropReviewApplied.ShouldContain("[1, 0 → 11, 9)");
            Control<StackPanel>(tree.Root, "ManualCropReviewMetadata").Visibility.ShouldBe(Visibility.Visible);
            model.PreviewPanes.Count.ShouldBe(2);
            return true;
        }).BindingErrors.ShouldBeEmpty();
        SessionAggregate after = await Load(h.Inner, id);
        Revision revision = after.Revisions.Single(r => r.Operation == OperationKind.ManualImport);
        SessionView resumed = (await h.Inner.CreateService().LoadAsync(id, CancellationToken.None)).Value;
        resumed.CurrentArtefact!.RevisionId.ShouldBe(revision.Id);
        resumed.CurrentArtefact.Sha256.ShouldBe(revision.Sha256);
        resumed.CurrentManualCropGeometry.ShouldBe(ManualCropGeometry.Create(TrimBounds.FromEdges(3, 2, 9, 7), ManualCropMargin.Uniform(2), 12, 10));
        resumed.CurrentStep!.State.ShouldBe(StepState.ReviewRequired);
    }

    [Fact]
    public Task Live_synthetic_WPF_UIA_uniform() => RunLive("uniform");

    [Fact]
    public Task Live_synthetic_WPF_UIA_per_edge() => RunLive("per-edge");

    [Fact]
    public Task Live_synthetic_WPF_UIA_cancel_keep() => RunLive("cancel-keep");

    private async Task RunLive(string mode)
    {
        if (Environment.GetEnvironmentVariable("PRINTFLOW_MANUAL_CROP_LIVE") != "1") return;
        using HomeScreenHarness h = new();
        SessionId id = await AtTrim(h.Inner, h.Sessions, WorkflowType.PrepareCustomerDesign, opaque: true);
        SessionView state = (await h.Sessions.LoadAsync(id, CancellationToken.None)).Value;
        SessionAggregate before = await Load(h.Inner, id);
        Revision upstream = before.Revisions.Single(r => r.Id == before.ToSnapshot().UpstreamRevisionOf(StepKind.Trim));
        byte[] sourceBytes = await File.ReadAllBytesAsync(h.Inner.FileWorkspace.ResolveAbsolute(upstream.File));
        var lockBefore = (await h.Inner.Repository.GetAutomationLockAsync(CancellationToken.None)).Value;
        SessionAggregate? offered = null;
        ManualCropGeometry? geometry = null;
        List<string> transcript = [$"Case {mode}; session {id}; source {upstream.Id}; SHA-256 {upstream.Sha256}."];
        WpfRendering.OnStaThread(() =>
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US"); CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            SessionViewModel model = h.Session(new RecordingNavigation()); model.Open(state);
            SessionScreenView view = new() { DataContext = model };
            Window window = new() { Title = "PrintFlow synthetic manual crop proof", Content = view, Width = 1250, Height = 900 };
            window.Show(); SharedReviewSurfaceTests.Settle(view);
            nint handle = new WindowInteropHelper(window).Handle;
            Dispatcher dispatcher = window.Dispatcher;
            Task drive = Task.Run(async () =>
            {
                AutomationElement root = AutomationElement.FromHandle(handle);
                AutomationElement Element(string name) => DesktopAutomation.Element(root, "Session." + name);
                void Mode(string name) => ((SelectionItemPattern)Element("ManualCropMode" + name).GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
                void Value(string name, string value) => ((ValuePattern)Element(name).GetCurrentPattern(ValuePattern.Pattern)).SetValue(value);
                DesktopAutomation.Invoke(Element("RunStep"));
                DesktopAutomation.Wait("ManualCropRequired", () => dispatcher.Invoke(() => !model.IsBusy && model.CanManualCrop));
                offered = await Load(h.Inner, id);
                string[] files = Files(h);
                DesktopAutomation.Invoke(Element("BeginManualCrop"));
                DesktopAutomation.Wait("manual editor", () => dispatcher.Invoke(() => model.IsCropping));
                dispatcher.Invoke(() => Select(model, view));
                Mode(mode == "uniform" ? "Uniform" : "PerEdge");
                if (mode == "uniform") Value("ManualCropUniformMargin", "2");
                else
                {
                    Value("ManualCropMarginLeft", "2"); Value("ManualCropMarginTop", "0");
                    Value("ManualCropMarginRight", "4"); Value("ManualCropMarginBottom", "1");
                }
                dispatcher.Invoke(() =>
                {
                    geometry = model.DraftManualCropGeometry; geometry.ShouldNotBeNull(); AssertOutlines(view, model);
                    // Traverse from the selected standard radio through editable values and actions.
                    RadioButton start = Control<RadioButton>(view, "ManualCropMode" + (mode == "uniform" ? "Uniform" : "PerEdge"));
                    start.Focus().ShouldBeTrue();
                    Button apply = Control<Button>(view, "ApplyManualCrop");
                    for (int i = 0; i < 50 && !apply.IsKeyboardFocused; i++)
                        ((UIElement)Keyboard.FocusedElement).MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                    apply.IsKeyboardFocused.ShouldBeTrue();
                    apply.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)).ShouldBeTrue();
                    Control<Button>(view, "CancelManualCrop").IsKeyboardFocused.ShouldBeTrue();
                });
                SessionAggregate edited = await Load(h.Inner, id);
                edited.Attempts.ShouldBe(offered.Attempts); edited.Revisions.ShouldBe(offered.Revisions); Files(h).ShouldBe(files);
                transcript.Add($"UIA mode/value controls; drag-coordinate seam against measured real WPF canvas. Selected {geometry!.SelectedBounds}; applied {geometry.AppliedBounds}; {geometry.Margin}. Both rendered outlines agree; keyboard traversal reaches Apply/Cancel; edits create no attempt or file.");
                if (mode == "cancel-keep")
                {
                    DesktopAutomation.Invoke(Element("CancelManualCrop"));
                    DesktopAutomation.Wait("editor cancelled", () => dispatcher.Invoke(() => !model.IsCropping));
                    SessionAggregate cancelled = await Load(h.Inner, id);
                    cancelled.Session.ShouldBe(offered.Session); cancelled.Steps.ShouldBe(offered.Steps);
                    cancelled.Attempts.ShouldBe(offered.Attempts); cancelled.Revisions.ShouldBe(offered.Revisions); Files(h).ShouldBe(files);
                    dispatcher.Invoke(() =>
                    {
                        Button keep = Control<Button>(view, "KeepOriginalExtent");
                        Control<Button>(view, "BeginManualCrop").Focus().ShouldBeTrue();
                        for (int i = 0; i < 100 && !keep.IsKeyboardFocused; i++)
                            ((UIElement)Keyboard.FocusedElement).MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                        keep.IsKeyboardFocused.ShouldBeTrue();
                    });
                    DesktopAutomation.Invoke(Element("KeepOriginalExtent"));
                    DesktopAutomation.Wait("extent retained", () => dispatcher.Invoke(() => !model.IsBusy && !model.CanKeepOriginalExtent));
                    transcript.Add("Cancel persisted nothing. Keyboard traversal reaches Keep original extent; UIA Invoke advances to Print Dimensions.");
                }
                else
                {
                    DesktopAutomation.Invoke(Element("ApplyManualCrop"));
                    DesktopAutomation.Wait("ReviewRequired", () => dispatcher.Invoke(() => !model.IsBusy && model.IsReviewRequired));
                    Element("ReviewBefore"); Element("ReviewAfter");
                    transcript.Add("UIA Apply -> shared Before/After ReviewRequired.");
                }
            });
            DispatcherFrame frame = new();
            _ = drive.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)), TaskScheduler.Default);
            try { Dispatcher.PushFrame(frame); drive.GetAwaiter().GetResult(); }
            finally { view.DataContext = null; window.Close(); }
        });
        SessionAggregate after = await Load(h.Inner, id);
        if (mode == "cancel-keep")
        {
            after.Steps.Single(s => s.Step == StepKind.Trim).State.ShouldBe(StepState.Skipped);
            after.Attempts.ShouldBe(offered!.Attempts); after.Revisions.ShouldBe(offered.Revisions);
            after.ToSnapshot().UpstreamRevisionOf(StepKind.PrintDimensions).ShouldBe(upstream.Id);
        }
        else
        {
            Revision crop = after.Revisions.Single(r => r.Operation == OperationKind.ManualImport);
            var attempt = after.Attempts.Single(a => a.ManualCropGeometry is not null);
            attempt.ManualCropGeometry.ShouldBe(geometry); attempt.TrimGeometry.ShouldBeNull();
            using (var connection = h.Inner.Database.OpenRaw())
            using (var query = connection.CreateCommand())
            {
                query.CommandText = """
                    SELECT ManualSelectedLeft, ManualSelectedTop, ManualSelectedRight, ManualSelectedBottom,
                           ManualAppliedLeft, ManualAppliedTop, ManualAppliedRight, ManualAppliedBottom,
                           ManualMarginTop, ManualMarginRight, ManualMarginBottom, ManualMarginLeft,
                           ManualMarginMode, OutputRevisionId
                    FROM ProcessingAttempt WHERE Id = $id
                    """;
                query.Parameters.AddWithValue("$id", attempt.Id.ToString());
                using var reader = query.ExecuteReader(); reader.Read().ShouldBeTrue();
                int[] expected = [3, 2, 9, 7, geometry!.AppliedBounds.Left, geometry.AppliedBounds.Top,
                    geometry.AppliedBounds.RightExclusive, geometry.AppliedBounds.BottomExclusive,
                    geometry.Margin.Top, geometry.Margin.Right, geometry.Margin.Bottom, geometry.Margin.Left];
                for (int i = 0; i < expected.Length; i++) reader.GetInt32(i).ShouldBe(expected[i]);
                reader.GetString(12).ShouldBe(mode == "uniform" ? "UNIFORM_MARGIN" : "EDGE_SPECIFIC_MARGIN");
                reader.GetString(13).ShouldBe(crop.Id.ToString());
                transcript.Add($"Raw SQLite SELECT: selected/applied L,T,R,B and margins T,R,B,L = {string.Join(",", expected)}; mode {reader.GetString(12)}; OutputRevisionId {reader.GetString(13)}.");
            }
            crop.Facts.PixelWidth.ShouldBe(geometry!.AppliedBounds.Width); crop.Facts.PixelHeight.ShouldBe(geometry.AppliedBounds.Height);
            byte[] bytes = await File.ReadAllBytesAsync(h.Inner.FileWorkspace.ResolveAbsolute(crop.File));
            SyntheticImages.DecodeDimensions(bytes).ShouldBe((geometry.AppliedBounds.Width, geometry.AppliedBounds.Height));
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant().ShouldBe(crop.Sha256.ToString().ToLowerInvariant());
            ISessionService restarted = h.Inner.CreateService();
            SessionView resumed = (await restarted.LoadAsync(id, CancellationToken.None)).Value;
            resumed.CurrentArtefact!.RevisionId.ShouldBe(crop.Id); resumed.CurrentArtefact.Sha256.ShouldBe(crop.Sha256);
            resumed.CurrentManualCropGeometry.ShouldBe(geometry); resumed.CurrentStep!.State.ShouldBe(StepState.ReviewRequired);
            WpfRendering.OnStaThread(() =>
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US"); CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
                SessionViewModel model = h.Session(new RecordingNavigation()); model.Open(resumed);
                SessionScreenView view = new() { DataContext = model };
                Window reopened = new() { Title = "PrintFlow reopened manual crop review", Content = view, Width = 1250, Height = 900 };
                reopened.Show();
                try
                {
                    PumpUntil(() => model.PreviewsLoaded.IsCompleted); SharedReviewSurfaceTests.Settle(view);
                    model.IsReviewRequired.ShouldBeTrue(); model.PreviewPanes.Count.ShouldBe(2);
                    model.HasManualCropGeometry.ShouldBeTrue();
                    Control<StackPanel>(view, "ManualCropReviewMetadata").IsVisible.ShouldBeTrue();
                    model.ManualCropReviewApplied.ShouldContain($"[{geometry.AppliedBounds.Left}, {geometry.AppliedBounds.Top} → {geometry.AppliedBounds.RightExclusive}, {geometry.AppliedBounds.BottomExclusive})");
                }
                finally { view.DataContext = null; reopened.Close(); }
            });
            transcript.Add("Closed editor window, rebuilt session service and reloaded through fresh SQLite connection; new real WPF window displays the same persisted manual crop review metadata and Before/After previews.");
            await Execute(restarted, id, new WorkflowCommand.Approve(StepKind.Trim, crop.Sha256));
            SessionAggregate approved = await Load(h.Inner, id);
            approved.ToSnapshot().UpstreamRevisionOf(StepKind.PrintDimensions).ShouldBe(crop.Id);
            transcript.Add($"Independent SQLite + raster readback: {geometry}; Revision {crop.Id}; SHA-256 {crop.Sha256}; {crop.Facts.PixelWidth}x{crop.Facts.PixelHeight}. Restart retains ReviewRequired and exact geometry/hash; approval makes this Revision downstream authority.");
        }
        (await File.ReadAllBytesAsync(h.Inner.FileWorkspace.ResolveAbsolute(upstream.File))).ShouldBe(sourceBytes);
        (await h.Inner.Repository.GetAutomationLockAsync(CancellationToken.None)).Value.ShouldBe(lockBefore);
        transcript.Add("Source bytes unchanged; automation lock unchanged/free.");
        string evidence = System.IO.Path.Combine("evidence", "manual-crop-live-" + mode + ".txt");
        Directory.CreateDirectory("evidence"); await File.WriteAllLinesAsync(evidence, transcript);
        foreach (string line in transcript) output.WriteLine(line);
    }

    private static string[] Files(HomeScreenHarness h) => Directory.GetFiles(h.Inner.Workspace.Root, "*", SearchOption.AllDirectories).Order().ToArray();
    private static T Control<T>(DependencyObject root, string id) where T : DependencyObject =>
        SharedReviewSurfaceTests.Descendants<T>(root).Single(e => AutomationProperties.GetAutomationId(e) == "Session." + id);
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();
    private static void SelectMode(DependencyObject view, string suffix) =>
        ((ISelectionItemProvider)new RadioButtonAutomationPeer(Control<RadioButton>(view, "ManualCropMode" + suffix)).GetPattern(PatternInterface.SelectionItem)!).Select();
    private static void SetValue(TextBox box, string value) => ((IValueProvider)new TextBoxAutomationPeer(box).GetPattern(PatternInterface.Value)!).SetValue(value);

    private static CropSurfaceLayout Layout(SessionViewModel model, DependencyObject view)
    {
        Canvas canvas = Control<Canvas>(view, "ManualCropSurface");
        var pane = model.CropPane!;
        return new CropSurfaceLayout(canvas.ActualWidth, canvas.ActualHeight, pane.PayloadPixelWidth, pane.PayloadPixelHeight,
            pane.SourcePixelWidth, pane.SourcePixelHeight, model.IsFitToViewport, model.ZoomScale);
    }

    private static void Select(SessionViewModel model, DependencyObject view)
    {
        SharedReviewSurfaceTests.Settle((FrameworkElement)view);
        CropSurfaceLayout layout = Layout(model, view);
        model.TrySetCropSelection(layout, layout.ToSurfaceX(3), layout.ToSurfaceY(2), layout.ToSurfaceX(9), layout.ToSurfaceY(7)).ShouldBeTrue();
        SharedReviewSurfaceTests.Settle((FrameworkElement)view);
    }

    private static void AssertOutlines(DependencyObject view, SessionViewModel model)
    {
        SharedReviewSurfaceTests.Settle((FrameworkElement)view);
        CropSurfaceLayout layout = Layout(model, view);
        foreach ((string suffix, TrimBounds bounds) in new[] { ("Selected", model.CropSelection!.Value), ("Applied", model.CropAppliedBounds!.Value) })
        {
            Rectangle outline = Control<Rectangle>(view, "ManualCrop" + suffix + "Outline");
            outline.Visibility.ShouldBe(Visibility.Visible);
            layout.TryToSurfaceRect(bounds, out double x, out double y, out double w, out double height).ShouldBeTrue();
            Canvas.GetLeft(outline).ShouldBe(x, 0.001); Canvas.GetTop(outline).ShouldBe(y, 0.001);
            outline.Width.ShouldBe(w, 0.001); outline.Height.ShouldBe(height, 0.001);
        }
    }

    private static void PumpUntil(Func<bool> predicate)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        while (!predicate() && DateTime.UtcNow < deadline)
        {
            DispatcherFrame frame = new();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
        predicate().ShouldBeTrue("The real command must finish within 20 seconds.");
    }
}
