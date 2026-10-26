using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Threading;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class GuidedSessionEditorInputTests
{
    [Fact]
    public async Task Captured_disclosure_cannot_expand_the_next_presentation()
    {
        using HomeScreenHarness h = new();
        var first = await Dimensions(h); var second = await Dimensions(h);
        WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = first }, new Size(1000, 700), tree =>
        {
            var disclosure = GuidedSessionShellTests.Find<Expander>(tree, "Session.TechnicalDetails");
            var peer = UIElementAutomationPeer.CreatePeerForElement(disclosure)!;
            var old = (IExpandCollapseProvider)peer.GetPattern(PatternInterface.ExpandCollapse)!;
            tree.Root.DataContext = second; tree.Root.UpdateLayout();
            old.Expand(); disclosure.IsExpanded.ShouldBeFalse();
            ((IExpandCollapseProvider)peer.GetPattern(PatternInterface.ExpandCollapse)!).Expand();
            disclosure.IsExpanded.ShouldBeTrue();
            return true;
        });
    }

    [Fact]
    public async Task Automation_patterns_keep_the_requested_native_interface()
    {
        using HomeScreenHarness h = new();
        var m = await Dimensions(h);
        WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = m }, new Size(1000, 700), tree =>
        {
            var combo = GuidedSessionShellTests.Find<ComboBox>(tree, "Session.PrintDimensions.TargetEdge");
            var peer = UIElementAutomationPeer.CreatePeerForElement(combo)!;
            peer.GetPattern(PatternInterface.Selection).ShouldBeAssignableTo<ISelectionProvider>();
            peer.GetPattern(PatternInterface.ExpandCollapse).ShouldBeAssignableTo<IExpandCollapseProvider>();
            return true;
        });
    }

    [Theory]
    [InlineData("task")]
    [InlineData("queued-task")]
    [InlineData("step")]
    [InlineData("hidden")]
    [InlineData("unloaded")]
    public async Task Captured_text_input_cannot_edit_a_retired_or_hidden_presentation(string boundary)
    {
        using HomeScreenHarness h = new();
        var first = await Dimensions(h); var second = await Dimensions(h);
        WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = first }, new Size(1000, 700), tree =>
        {
            var input = GuidedSessionShellTests.Find<TextBox>(tree, "Session.PrintDimensions.TargetMillimetres");
            var old = Value(input);
            first.CustomMillimetresText = "111"; second.CustomMillimetresText = "222";
            if (boundary == "queued-task") Dispatcher.CurrentDispatcher.BeginInvoke(() => old.SetValue("999"), DispatcherPriority.Background);
            if (boundary.EndsWith("task")) tree.Root.DataContext = second;
            else if (boundary == "step") first.IsChoosingCustomSize = false;
            else if (boundary == "hidden") input.Visibility = Visibility.Collapsed;
            else tree.Root.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            tree.Root.UpdateLayout();
            string firstBefore = first.CustomMillimetresText;
            if (boundary != "queued-task") old.SetValue("999");
            Drain();
            first.CustomMillimetresText.ShouldBe(firstBefore); second.CustomMillimetresText.ShouldBe("222");
            if (boundary == "unloaded")
            {
                tree.Root.DataContext = second; tree.Root.UpdateLayout();
                Value(input).SetValue("998"); second.CustomMillimetresText.ShouldBe("222");
                tree.Root.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            }
            else if (boundary == "hidden") input.Visibility = Visibility.Visible;
            else if (boundary == "step") first.ChooseCustomSizeCommand.Execute(null);
            tree.Root.UpdateLayout();
            var restored = ((SessionViewModel)tree.Root.DataContext).CustomMillimetresText;
            old.SetValue("997"); Drain();
            ((SessionViewModel)tree.Root.DataContext).CustomMillimetresText.ShouldBe(restored, "a retired provider never becomes current after re-showing the editor");
            Value(input).SetValue("333"); Drain();
            ((SessionViewModel)tree.Root.DataContext).CustomMillimetresText.ShouldBe("333", "current visible text editing remains usable");
            return true;
        });
    }

    [Fact]
    public async Task Committing_dimensions_retires_the_same_models_text_provider()
    {
        using HomeScreenHarness h = new();
        var m = await Dimensions(h);
        var dimensions = (await h.Sessions.LoadAsync(m.Id, default)).Value;
        m.WidthMmText = "200"; m.HeightMmText = "150";
        await m.SetMaximumBoundsCommand.ExecuteAsync(null);
        var white = (await h.Sessions.LoadAsync(m.Id, default)).Value;
        // Reproduce a displayed old snapshot receiving the actual persisted next step.
        m.Open(dimensions); m.ChooseCustomSizeCommand.Execute(null);
        WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = m }, new Size(1000, 700), tree =>
        {
            var input = GuidedSessionShellTests.Find<TextBox>(tree, "Session.PrintDimensions.TargetMillimetres");
            var old = Value(input);
            m.Open(white); tree.Root.UpdateLayout();
            m.GuidedPage.ShouldBe(GuidedSessionPage.WhiteInk);
            var before = m.CustomMillimetresText;
            old.SetValue("999"); Value(input).SetValue("998"); Drain();
            m.CustomMillimetresText.ShouldBe(before);
            return true;
        });
    }

    [Theory]
    [InlineData("combo", false)]
    [InlineData("combo", true)]
    [InlineData("list", false)]
    [InlineData("radio", false)]
    public async Task Captured_selection_cannot_change_the_next_task_but_current_selection_works(string kind, bool queued)
    {
        using HomeScreenHarness h = new();
        var first = await Editor(h, kind); var second = await Editor(h, kind);
        WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = first }, new Size(1000, 700), tree =>
        {
            string id = kind == "combo" ? "Session.PrintDimensions.TargetEdge" : kind == "list" ? "Session.TrimModes" : "Session.ManualCropModeUniform";
            var input = GuidedSessionShellTests.Find<FrameworkElement>(tree, id);
            if (input is ComboBox combo)
            {
                // Materialize the native item containers without opening its Popup/HWND.
                // This artificial ItemsPresenter replaces the theme's popup hierarchy, so
                // provide alignment locally instead of retaining its ancestor bindings.
                // The guarded container/provider and every selection assertion stay real.
                combo.ItemContainerStyle = new Style(typeof(ComboBoxItem))
                {
                    Setters =
                    {
                        new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left),
                        new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center),
                    },
                };
                combo.Template = new ControlTemplate(typeof(ComboBox)) { VisualTree = new FrameworkElementFactory(typeof(ItemsPresenter)) };
                combo.ApplyTemplate(); combo.UpdateLayout();
            }
            var old = Selection(input);
            if (queued) Dispatcher.CurrentDispatcher.BeginInvoke(() => RetiredSelect(old), DispatcherPriority.Background);
            tree.Root.DataContext = second; tree.Root.UpdateLayout();
            var before = Selected(second, kind);
            if (!queued) RetiredSelect(old);
            Drain(); Selected(second, kind).ShouldBe(before);
            Selection(input).Select(); Drain();
            Selected(second, kind).ShouldNotBe(before, "a current selection provider remains usable");
            return true;
        });
    }

    private static IValueProvider Value(TextBox input) => (IValueProvider)UIElementAutomationPeer.CreatePeerForElement(input)!.GetPattern(PatternInterface.Value)!;
    private static void RetiredSelect(ISelectionItemProvider old)
    {
        // WPF may itself retire an item that no longer exists. Refusal is also safe.
        try { old.Select(); } catch (ElementNotAvailableException) { }
    }
    private static ISelectionItemProvider Selection(FrameworkElement input)
    {
        var peer = UIElementAutomationPeer.CreatePeerForElement(input)!;
        if (input is ItemsControl items)
            peer = (AutomationPeer)peer.GetType().GetMethod("CreateItemAutomationPeer", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(peer, [items.Items[1]])!;
        return (ISelectionItemProvider)peer.GetPattern(PatternInterface.SelectionItem)!;
    }
    private static string Selected(SessionViewModel m, string kind) => kind == "combo" ? m.SelectedTargetEdgeChoice?.Edge.ToString() ?? "none" :
        kind == "list" ? m.SelectedTrimMode.Mode.ToString() : m.IsManualCropUniform.ToString();
    private static void Drain() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.SystemIdle);
    private static async Task<SessionViewModel> Dimensions(HomeScreenHarness h)
    {
        var m = await GuidedSessionProjectionTests.Open(h, WorkflowType.GeneratePrintTiff);
        await m.ConfirmOriginalCommand.ExecuteAsync(null); m.ChooseCustomSizeCommand.Execute(null); return m;
    }
    private static async Task<SessionViewModel> Editor(HomeScreenHarness h, string kind)
    {
        if (kind == "combo") return await Dimensions(h);
        var imported = (await h.Sessions.ImportAsync(WorkflowType.PrepareAsset, h.Inner.WriteOpaqueSourcePng(), "selection", "tester", default)).Value;
        var m = h.Session(h.Navigation); m.Open(imported); await m.ConfirmOriginalCommand.ExecuteAsync(null);
        await m.SkipCommand.ExecuteAsync(null); await m.SkipCommand.ExecuteAsync(null);
        if (kind == "radio") { await m.RunStepCommand.ExecuteAsync(null); await m.PreviewsLoaded; m.BeginManualCropCommand.Execute(null); }
        return m;
    }
}
