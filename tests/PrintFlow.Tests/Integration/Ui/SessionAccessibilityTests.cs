using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Adapters.Fake;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

/// <summary>
/// The Session screen as an assistive technology or a UI Automation driver sees it
/// (SCRUM-11092-A §5, §6, §7, §9, §17).
/// </summary>
/// <remarks>
/// These are rendered tests, not source-text tests: every assertion reads an
/// <see cref="AutomationPeer"/> built from an element that a real WPF measure and arrange pass
/// produced, so a control that is present in the XAML but never realised — or realised without
/// the identity it was given — fails here rather than in a live desktop run.
/// <para>
/// The identity is asserted separately from the wording. An <c>AutomationId</c> is a contract
/// with a driver and must not move when the product is translated or reworded; an accessible
/// name is the operator's own localised sentence and must never be a resource key, an enum or a
/// view model class. Both halves are checked, in both languages.
/// </para>
/// <para>
/// Every render gets its own view model, loaded afresh from the session service, and the screen
/// a test drives is never the screen it renders: a rendered <c>ItemsControl</c> builds a
/// collection view owned by the render thread, and a later command would then rebuild that
/// collection from a thread that does not own it.
/// </para>
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class SessionAccessibilityTests
{
    /// <summary>The operator actions a driver must be able to find without reading text (§5).</summary>
    private static readonly string[] PrimaryActionIds =
    [
        "Session.RunStep",
        "Session.Approve",
        "Session.Reject",
        "Session.Retry",
        "Session.Skip",
        "Session.Stop",
        "Session.TakeOver",
        "Session.ReenterAutomation",
        "Session.SubmitManualResult",
        "Session.BackToHome",
    ];

    // -------------------------------------------------------------------------------------
    // §5: automation identity
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Every required identity exists in the XAML and reaches a rendered button.
    /// </summary>
    /// <remarks>
    /// Five states are rendered rather than one because the required actions are never all
    /// offered at once. Waiting offers Run Step and Skip; a computing session offers Stop and
    /// Take Over; a failed one offers Retry; a reviewed one offers Approve and Reject; and a
    /// handed-off one offers Submit Manual Result and Re-enter Automation. Back to Home is on
    /// all of them. Between them the five cover the list, and a missing identity names itself.
    /// </remarks>
    [Fact]
    public async Task Every_required_operator_action_renders_with_its_own_automation_id()
    {
        using HomeScreenHarness harness = new();

        HashSet<string> found =
        [
            .. await IdsAsync(harness, await ConfirmedAsync(harness, "identity-waiting.png")),
            .. await IdsWhileComputingAsync(harness, "identity-running.png"),
            .. await IdsAsync(harness, await FailedAsync(harness, "identity-failed.png")),
            .. await IdsAsync(harness, await InReviewAsync(harness, "identity-review.png")),
            .. await IdsAsync(harness, await HandedOffAsync(harness, "identity-handoff.png")),
        ];

        foreach (string id in PrimaryActionIds)
        {
            found.ShouldContain(id);
        }
    }

    /// <summary>
    /// No two buttons the operator can see at the same time answer to the same identity (§17).
    /// </summary>
    /// <remarks>
    /// The repeated actions this screen does have — one button per size preset — are inside an
    /// item template and deliberately carry no identity of their own, so an ambiguous match is
    /// not possible for a driver that searched the visible tree.
    /// </remarks>
    [Fact]
    public async Task Simultaneously_visible_actions_never_share_an_automation_id()
    {
        using HomeScreenHarness harness = new();

        foreach (SessionId id in new[]
        {
            await HandedOffAsync(harness, "unique-handoff.png"),
            await InReviewAsync(harness, "unique-review.png"),
            await ConfirmedAsync(harness, "unique-confirmed.png"),
        })
        {
            IReadOnlyList<string> ids = await IdsAsync(harness, id);
            ids.Distinct().Count().ShouldBe(ids.Count);
        }
    }

    /// <summary>
    /// The identities do not move when the operator's language does (§5).
    /// </summary>
    /// <remarks>
    /// The names must move — that is what a localised accessible name is for — so this asserts
    /// both halves at once: the same identity set, and different, non-empty wording under it.
    /// </remarks>
    [Fact]
    public async Task Automation_ids_are_stable_across_languages_while_names_are_translated()
    {
        using HomeScreenHarness harness = new();
        SessionId id = await HandedOffAsync(harness, "language.png");

        IReadOnlyList<Action> english = await InCultureAsync("en-US", harness, id);
        IReadOnlyList<Action> chinese = await InCultureAsync("zh-CN", harness, id);

        chinese.Select(c => c.Id).ShouldBe(english.Select(e => e.Id));
        chinese.ShouldAllBe(c => c.Name.Length > 0);

        english.Single(e => e.Id == "Session.SubmitManualResult").Name
            .ShouldNotBe(chinese.Single(c => c.Id == "Session.SubmitManualResult").Name);
    }

    // -------------------------------------------------------------------------------------
    // §6: accessible names
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Nothing on the screen announces an implementation detail (§6).
    /// </summary>
    /// <remarks>
    /// This is the assertion the first live attempt would have failed. The recovered session
    /// tree announced its step rows as <c>PrintFlow.App.ViewModels.SessionStepRow</c>, its
    /// preview panes as <c>ArtefactPreviewPane</c> and the window itself as
    /// <c>PrintFlow.App.ViewModels.SessionViewModel</c> — class names read aloud to an operator
    /// who cannot see the screen. Names come from item containers and explicit
    /// <c>AutomationProperties.Name</c> now, so the check covers every rendered peer rather than
    /// only the controls this slice touched.
    /// </remarks>
    [Fact]
    public async Task No_rendered_element_announces_a_type_or_resource_name()
    {
        using HomeScreenHarness harness = new();
        SessionId id = await HandedOffAsync(harness, "names.png");

        IReadOnlyList<string> leaked = (await RenderAsync(harness, id, tree => tree.Elements
            .OfType<UIElement>()
            .Select(NameOf)
            .Where(name => name.Contains("PrintFlow.App", StringComparison.Ordinal)
                || name.Contains("ViewModel", StringComparison.Ordinal)
                || name.Contains("Row", StringComparison.Ordinal)
                || name.StartsWith("Session_", StringComparison.Ordinal))
            .Distinct()
            .ToList())).Facts;

        leaked.ShouldBeEmpty();
    }

    /// <summary>The manual-result action reads as the operator's own wording, in both languages.</summary>
    [Fact]
    public async Task Submit_manual_result_announces_its_localised_operator_wording()
    {
        using HomeScreenHarness harness = new();
        SessionId id = await HandedOffAsync(harness, "submit-name.png");

        (await InCultureAsync("en-US", harness, id)).Single(a => a.Id == "Session.SubmitManualResult")
            .Name.ShouldBe("Submit Manual Result");
        (await InCultureAsync("zh-CN", harness, id)).Single(a => a.Id == "Session.SubmitManualResult")
            .Name.ShouldBe("提交手动处理结果");
    }

    // -------------------------------------------------------------------------------------
    // §7: keyboard navigation
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Tab reaches Submit Manual Result, and the display-only lists are not in the way (§7).
    /// </summary>
    /// <remarks>
    /// A measured and arranged tree that was never attached to a window has no focus scope, so
    /// WPF's own traversal — <c>PredictFocus</c>, <c>MoveFocus</c> — cannot be asked for the
    /// route here; it gives up after the first element. What can be asked, and is, is the two
    /// things the route is made of: which elements are tab stops, and in what order the tree
    /// holds them. That equals the route only while nothing on the screen reorders or confines
    /// traversal, so the absence of <c>TabIndex</c> and of any non-default
    /// <c>KeyboardNavigation</c> mode is asserted here as well rather than assumed.
    /// <para>
    /// The traversal itself is proven against the running application, by UI Automation, in the
    /// live desktop record for this slice. This test is what keeps the screen from drifting
    /// away from that proof between live runs.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_tab_stops_reach_submit_manual_result_and_skip_the_display_only_lists()
    {
        using HomeScreenHarness harness = new();
        SessionId id = await HandedOffAsync(harness, "tab-route.png");

        TabOrder order = (await RenderAsync(harness, id, TabStops)).Facts;

        order.Reordered.ShouldBeEmpty();
        order.Confined.ShouldBeEmpty();

        order.Stops.ShouldContain("Session.ReenterAutomation");
        order.Stops.ShouldContain("Session.SubmitManualResult");
        order.Stops.ShouldContain("Session.BackToHome");

        // Submit Manual Result is reachable before the operator has to leave the screen.
        List<string> stops = [.. order.Stops];
        stops.IndexOf("Session.SubmitManualResult").ShouldBeLessThan(stops.IndexOf("Session.BackToHome"));

        // A list of steps and a pair of preview images are there to be read, not operated.
        order.Stops.ShouldNotContain("Session.StepList");
        order.Stops.ShouldNotContain("Session.PreviewPanes");
        order.Stops.ShouldNotContain("Session.OutputList");
    }

    // -------------------------------------------------------------------------------------
    // §9: UI Automation patterns
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Submit Manual Result exists, is enabled and offers <c>InvokePattern</c> when eligible (§9).
    /// </summary>
    [Fact]
    public async Task Submit_manual_result_is_enabled_and_invokable_when_the_session_is_eligible()
    {
        using HomeScreenHarness harness = new();
        SessionId id = await HandedOffAsync(harness, "invokable.png");

        (bool present, bool enabled, bool invokable) = (await RenderAsync(harness, id, tree =>
        {
            Button? button = Find(tree, "Session.SubmitManualResult");
            if (button is null)
            {
                return (false, false, false);
            }

            AutomationPeer peer = UIElementAutomationPeer.CreatePeerForElement(button);
            return (true, peer.IsEnabled(), peer.GetPattern(PatternInterface.Invoke) is IInvokeProvider);
        })).Facts;

        present.ShouldBeTrue();
        enabled.ShouldBeTrue();
        invokable.ShouldBeTrue();
    }

    /// <summary>
    /// An ordinary active session does not offer it at all (§9, §16).
    /// </summary>
    /// <remarks>
    /// Absent rather than disabled is the existing contract — every action on this screen is
    /// shown only where <c>AvailableCommands</c> allows it — and accessibility work must not
    /// quietly widen eligibility, so the check is that the control is not rendered.
    /// </remarks>
    [Fact]
    public async Task Submit_manual_result_is_not_offered_while_the_session_is_still_automated()
    {
        using HomeScreenHarness harness = new();
        SessionId id = await ConfirmedAsync(harness, "ineligible.png");

        (await RenderAsync(harness, id, tree => Find(tree, "Session.SubmitManualResult") is not null))
            .Facts.ShouldBeFalse();
    }

    /// <summary>
    /// Invoking through UI Automation runs the real product command, picker and all (§9, §10).
    /// </summary>
    /// <remarks>
    /// The point of this test is that nothing in the manual-result path is reachable only from
    /// a view model method: the driver holds an <see cref="IInvokeProvider"/> obtained from a
    /// rendered button and never touches <c>SubmitManualResultCommand</c>, so what it exercises
    /// is the path the live desktop proof drives — with the Windows dialog replaced by the
    /// existing stub picker, which is the only part a headless test cannot show.
    /// </remarks>
    [Fact]
    public async Task Invoking_the_rendered_button_imports_the_chosen_file_and_asks_for_review()
    {
        using HomeScreenHarness harness = new();
        SessionId id = await HandedOffAsync(harness, "invoke-path.png");

        StubFilePicker picker = new(harness.Inner.WriteSourcePng("operator-finished-this.png"));
        SessionViewModel screen = new(harness.Sessions, harness.Previews, new RecordingNavigation(), picker);
        screen.Open((await harness.Sessions.LoadAsync(id, CancellationToken.None)).Value);
        await screen.PreviewsLoaded;
        screen.CanSubmitManualResult.ShouldBeTrue();

        WpfRendering.OnStaThread(() =>
        {
            SessionScreenView view = new() { DataContext = screen };
            view.Measure(WpfRendering.ReviewViewport);
            view.Arrange(new Rect(0, 0, WpfRendering.ReviewViewport.Width, WpfRendering.ReviewViewport.Height));
            view.UpdateLayout();

            Button button = Find(Flatten(view), "Session.SubmitManualResult")
                ?? throw new InvalidOperationException("Submit Manual Result was not rendered.");
            AutomationPeer peer = UIElementAutomationPeer.CreatePeerForElement(button);

            ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)!).Invoke();

            // ButtonAutomationPeer posts the click, and the command it raises is asynchronous,
            // so the dispatcher has to run for the invocation to reach the session service.
            Pump(() => picker.CallCount > 0 && !screen.IsBusy
                && screen.SubmitManualResultCommand.ExecutionTask is { IsCompleted: true });

            view.DataContext = null;
            view.UpdateLayout();
        });

        picker.CallCount.ShouldBe(1);
        screen.Notice.ShouldBeNull();

        SessionAggregate persisted = (await harness.Inner.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        Revision manual = persisted.Revisions.Single(r => r.Operation == OperationKind.ManualResultImport);
        persisted.Attempts.Single(a => a.OutputRevisionId == manual.Id)
            .Operation.ShouldBe(OperationKind.ManualResultImport);
        persisted.ToSnapshot().CurrentStep!.State.ShouldBe(StepState.ReviewRequired);
    }

    // -------------------------------------------------------------------------------------
    // Rendering helpers
    // -------------------------------------------------------------------------------------

    /// <summary>One automation identity and the wording read out under it.</summary>
    private sealed record Action(string Id, string Name);

    /// <summary>Renders a freshly loaded screen at the signed-off viewport, failing on bindings.</summary>
    private static async Task<RenderResult<T>> RenderAsync<T>(
        HomeScreenHarness harness, SessionId id, Func<RenderedTree, T> inspect)
    {
        SessionViewModel fresh = harness.Session(new RecordingNavigation());
        fresh.Open((await harness.Sessions.LoadAsync(id, CancellationToken.None)).Value);
        await fresh.PreviewsLoaded;

        return WpfRendering.RenderExpectingNoBindingErrors(
            () => new SessionScreenView { DataContext = fresh }, WpfRendering.ReviewViewport, inspect);
    }

    /// <summary>The identities of every action the operator can see.</summary>
    private static async Task<IReadOnlyList<string>> IdsAsync(HomeScreenHarness harness, SessionId id) =>
        (await RenderAsync(harness, id, tree => VisibleActions(tree).Select(a => a.Id).ToList())).Facts;

    /// <summary>The identity and wording of every visible action, under one operator language.</summary>
    private static async Task<IReadOnlyList<Action>> InCultureAsync(
        string culture, HomeScreenHarness harness, SessionId id)
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            return (await RenderAsync(harness, id, tree => VisibleActions(tree).ToList())).Facts;
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    private static IEnumerable<Action> VisibleActions(RenderedTree tree) =>
        tree.OfType<Button>()
            .Where(IsOnScreen)
            .Select(button => new Action(AutomationProperties.GetAutomationId(button), NameOf(button)))
            .Where(action => action.Id.Length > 0)
            .OrderBy(action => action.Id, StringComparer.Ordinal);

    /// <summary>What an assistive technology would read out for one element.</summary>
    private static string NameOf(UIElement element) =>
        UIElementAutomationPeer.CreatePeerForElement(element)?.GetName() ?? string.Empty;

    /// <summary>The button carrying one automation identity, if it was rendered and is on screen.</summary>
    private static Button? Find(RenderedTree tree, string automationId) =>
        tree.OfType<Button>().SingleOrDefault(
            button => IsOnScreen(button) && AutomationProperties.GetAutomationId(button) == automationId);

    /// <summary>
    /// Whether an element is one the operator can actually see and use.
    /// </summary>
    /// <remarks>
    /// <see cref="UIElement.IsVisible"/> cannot answer this: it is false for everything in a
    /// tree that was measured and arranged but never attached to a window, which is exactly
    /// what these tests render. The declared visibility of the element and of every ancestor is
    /// the same question without that dependency.
    /// </remarks>
    private static bool IsOnScreen(FrameworkElement element)
    {
        for (DependencyObject? node = element; node is not null; node = ParentOf(node))
        {
            if (node is UIElement { Visibility: not Visibility.Visible })
            {
                return false;
            }
        }

        return true;
    }

    private static DependencyObject? ParentOf(DependencyObject node) =>
        node is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node)
            : LogicalTreeHelper.GetParent(node);

    /// <summary>
    /// Where Tab stops on this screen, and anything that could make that order untrue.
    /// </summary>
    /// <param name="Stops">
    /// The tab stops in tree order, as automation identities. One without an identity is
    /// reported by type, so a stop that should not be there is legible in a failure message.
    /// </param>
    /// <param name="Reordered">Elements carrying an explicit <c>TabIndex</c>.</param>
    /// <param name="Confined">
    /// Elements whose <c>KeyboardNavigation</c> mode keeps Tab inside them: <c>Cycle</c> and
    /// <c>Contained</c> are the focus trap §7 forbids. <c>Once</c> is not — it is the ordinary
    /// treatment of a composite control, entered by Tab and walked with the arrow keys — so it
    /// is deliberately not reported here.
    /// </param>
    private sealed record TabOrder(
        IReadOnlyList<string> Stops, IReadOnlyList<string> Reordered, IReadOnlyList<string> Confined);

    private static TabOrder TabStops(RenderedTree tree)
    {
        List<FrameworkElement> onScreen = [.. tree.OfType<FrameworkElement>().Where(IsOnScreen)];

        return new TabOrder(
            [.. onScreen
                .Where(element => element is Control { IsTabStop: true, Focusable: true, IsEnabled: true })
                .Select(Describe)],
            [.. onScreen
                .Where(element => element is Control control && control.TabIndex != int.MaxValue)
                .Select(Describe)],
            [.. onScreen
                .Where(element => Confines(KeyboardNavigation.GetTabNavigation(element))
                    || Confines(KeyboardNavigation.GetControlTabNavigation(element)))
                .Select(Describe)]);

        static bool Confines(KeyboardNavigationMode mode) =>
            mode is KeyboardNavigationMode.Cycle or KeyboardNavigationMode.Contained;

        static string Describe(FrameworkElement element) =>
            AutomationProperties.GetAutomationId(element) is { Length: > 0 } id ? id : element.GetType().Name;
    }

    /// <summary>Everything in one arranged tree, visual and logical.</summary>
    private static RenderedTree Flatten(UserControl root)
    {
        List<DependencyObject> elements = [];
        Collect(root, elements);
        return new RenderedTree(root, elements);

        static void Collect(DependencyObject node, List<DependencyObject> into)
        {
            if (into.Contains(node))
            {
                return;
            }

            into.Add(node);

            int children = node is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetChildrenCount(node)
                : 0;

            for (int i = 0; i < children; i++)
            {
                Collect(VisualTreeHelper.GetChild(node, i), into);
            }

            foreach (object child in LogicalTreeHelper.GetChildren(node))
            {
                if (child is DependencyObject dependencyObject)
                {
                    Collect(dependencyObject, into);
                }
            }
        }
    }

    /// <summary>Runs the dispatcher until <paramref name="done"/> is true, or gives up.</summary>
    private static void Pump(Func<bool> done)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(30);
        while (!done() && DateTime.UtcNow < deadline)
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.SystemIdle);
            Thread.Sleep(5);
        }

        done().ShouldBeTrue("the invoked command did not complete");
    }

    // -------------------------------------------------------------------------------------
    // Session states
    // -------------------------------------------------------------------------------------

    /// <summary>An imported session whose original has been confirmed: Enhancement, Waiting.</summary>
    private static async Task<SessionId> ConfirmedAsync(HomeScreenHarness harness, string fileName)
    {
        harness.FilePicker.Path = harness.WriteSourceFile(fileName);
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);

        SessionView imported = harness.Navigation.WorkflowSelectionFor!;
        SessionViewModel screen = harness.Session(new RecordingNavigation());
        screen.Open(imported);
        await screen.ConfirmOriginalCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();

        return imported.Id;
    }

    /// <summary>...and Enhancement run to a result awaiting the operator's decision.</summary>
    private static async Task<SessionId> InReviewAsync(HomeScreenHarness harness, string fileName)
    {
        SessionId id = await ConfirmedAsync(harness, fileName);
        harness.Inner.FakeMeitu.SetScenario(FakeAdapterScenario.Succeed);

        SessionViewModel screen = await OpenAsync(harness, id);
        await screen.RunStepCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();
        screen.CanApprove.ShouldBeTrue();

        return id;
    }

    /// <summary>
    /// ...and Enhancement failed and handed to the operator: the state SCRUM-11092 is about.
    /// </summary>
    private static async Task<SessionId> HandedOffAsync(HomeScreenHarness harness, string fileName)
    {
        SessionId id = await ConfirmedAsync(harness, fileName);
        harness.Inner.FakeMeitu.SetScenario(FakeAdapterScenario.Timeout);

        SessionViewModel screen = await OpenAsync(harness, id);
        await screen.RunStepCommand.ExecuteAsync(null);
        await screen.HandOffCommand.ExecuteAsync(null);

        screen.IsHandedOff.ShouldBeTrue();
        screen.CanSubmitManualResult.ShouldBeTrue();

        return id;
    }

    /// <summary>...or Enhancement failed, which is the state that offers Retry.</summary>
    private static async Task<SessionId> FailedAsync(HomeScreenHarness harness, string fileName)
    {
        SessionId id = await ConfirmedAsync(harness, fileName);
        harness.Inner.FakeMeitu.SetScenario(FakeAdapterScenario.Timeout);

        SessionViewModel screen = await OpenAsync(harness, id);
        await screen.RunStepCommand.ExecuteAsync(null);
        screen.CanRetry.ShouldBeTrue();

        return id;
    }

    /// <summary>
    /// The identities offered while Enhancement is still computing — the only state with Stop
    /// and Take Over on screen.
    /// </summary>
    /// <remarks>
    /// The adapter is held at its busy phase only for as long as the render takes, and the run
    /// is then ended the way the existing stop tests end it, so nothing is abandoned mid-flight
    /// with the automation lock still held.
    /// </remarks>
    private static async Task<IReadOnlyList<string>> IdsWhileComputingAsync(
        HomeScreenHarness harness, string fileName)
    {
        SessionId id = await ConfirmedAsync(harness, fileName);
        harness.Inner.FakeMeitu.SetScenario(FakeAdapterScenario.WaitForStopAt(ExternalOperationPhase.Busy));

        SessionViewModel screen = await OpenAsync(harness, id);
        Task run = screen.RunStepCommand.ExecuteAsync(null);
        await harness.Inner.FakeMeitu.HangStarted;

        try
        {
            screen.CanStopAutomation.ShouldBeTrue();
            return await IdsAsync(harness, id);
        }
        finally
        {
            harness.Sessions.RequestStop(id, AutomationStopMode.TakeOver).IsSuccess.ShouldBeTrue();
            await run;
        }
    }

    /// <summary>A session screen over the current service, opened on a freshly loaded session.</summary>
    private static async Task<SessionViewModel> OpenAsync(HomeScreenHarness harness, SessionId id)
    {
        SessionViewModel screen = harness.Session(new RecordingNavigation());
        screen.Open((await harness.Sessions.LoadAsync(id, CancellationToken.None)).Value);
        await screen.PreviewsLoaded;
        return screen;
    }
}
