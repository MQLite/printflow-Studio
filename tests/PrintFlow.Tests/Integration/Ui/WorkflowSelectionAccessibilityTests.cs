using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

/// <summary>
/// Workflow Selection as a keyboard operator and a UI Automation driver meet it
/// (SCRUM-11075, SCRUM-11078 §22–§25, §36–§38).
/// </summary>
/// <remarks>
/// Rendered, not read off the XAML: every assertion comes from an element a real WPF measure and
/// arrange pass produced, so a control present in the markup but never realised fails here rather
/// than on a live desktop.
/// <para>
/// Identity and wording are asserted separately, the convention <c>SessionAccessibilityTests</c>
/// established — an <c>AutomationId</c> is a contract with a driver and must not move when the
/// product is translated, while an accessible name is the operator's own localised sentence.
/// </para>
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class WorkflowSelectionAccessibilityTests
{
    /// <summary>The three fixed workflows, by the identity a driver finds them with (§17, §22).</summary>
    private static readonly string[] WorkflowIds =
    [
        "WorkflowSelection.PrepareDesignAsset",
        "WorkflowSelection.PrepareCustomerDesign",
        "WorkflowSelection.GeneratePrintTiff",
    ];

    // -----------------------------------------------------------------------------------
    // §21, §22 — the screen renders with the identities it promised
    // -----------------------------------------------------------------------------------

    [Fact]
    public async Task The_source_context_and_all_three_workflows_render_with_stable_ids()
    {
        using HomeScreenHarness harness = new();

        RenderResult<Rendered> rendered = await RenderAsync(harness, "a11y-ids.png", tree => new Rendered(
            [.. tree.OfType<FrameworkElement>()
                .Where(IsOnScreen)
                .Select(AutomationProperties.GetAutomationId)
                .Where(id => id.StartsWith("WorkflowSelection.", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)],
            tree.OfType<TextBox>()
                .Single(b => AutomationProperties.GetAutomationId(b) == "WorkflowSelection.OutputName")
                .Text,
            tree.OfType<TextBlock>()
                .Single(b => AutomationProperties.GetAutomationId(b) == "WorkflowSelection.SourceFile")
                .Text,
            tree.OfType<Image>().Count(i => i.Source is not null),
            AutomationProperties.GetAutomationId(tree.Root)));

        rendered.BindingErrors.ShouldBeEmpty();

        foreach (string id in WorkflowIds)
        {
            rendered.Facts.Ids.ShouldContain(id);
        }

        rendered.Facts.Ids.ShouldContain("WorkflowSelection.SourceFile");
        rendered.Facts.Ids.ShouldContain("WorkflowSelection.Preview");
        rendered.Facts.Ids.ShouldContain("WorkflowSelection.OutputName");
        rendered.Facts.Ids.ShouldContain("WorkflowSelection.Back");

        rendered.Facts.Screen.ShouldBe("Screen.WorkflowSelection");

        // The two are shown as separate facts about the same import, which is the point of §11.
        rendered.Facts.OutputName.ShouldBe("a11y-ids");
        rendered.Facts.SourceFile.ShouldBe("a11y-ids.png");

        // And the picture is really on screen, not merely a bound but empty Image (§12).
        rendered.Facts.ImagesWithSource.ShouldBe(1);
    }

    /// <summary>
    /// The identities are stable across languages and the wording is not (§25).
    /// </summary>
    [Fact]
    public async Task The_labels_are_localised_while_the_identities_are_not()
    {
        using HomeScreenHarness harness = new();

        IReadOnlyList<Labelled> english = await InCultureAsync("en-US", harness, "a11y-en.png");
        IReadOnlyList<Labelled> chinese = await InCultureAsync("zh-CN", harness, "a11y-zh.png");

        english.Select(m => m.Id).ShouldBe(chinese.Select(m => m.Id));
        english.Select(m => m.Name).ShouldBe(
            ["Prepare Design Asset", "Prepare Customer Design", "Generate Print TIFF"]);

        chinese.ShouldAllBe(m => m.Name.Length > 0);
        chinese.Select(m => m.Name).ShouldNotBe(english.Select(m => m.Name));
        chinese.ShouldNotContain(m => m.Name.StartsWith("Workflow_", StringComparison.Ordinal));
        chinese.ShouldNotContain(m => m.Name == m.Id);
    }

    /// <summary>The new labels are real sentences in both languages, not resource keys (§25).</summary>
    [Theory]
    [InlineData("en-US", "Source file", "Output name")]
    [InlineData("zh-CN", "源文件", "输出名称")]
    public async Task The_source_and_output_name_labels_are_translated(
        string culture, string sourceLabel, string outputLabel)
    {
        using HomeScreenHarness harness = new();

        RenderResult<Labels> rendered = await RenderAsync(harness, $"a11y-labels-{culture}.png", tree =>
        {
            WorkflowSelectionViewModel model = (WorkflowSelectionViewModel)tree.Root.DataContext;
            return new Labels(model.SourceFileLabel, model.OutputNameLabel, model.OutputNameHint);
        }, culture);

        rendered.BindingErrors.ShouldBeEmpty();
        rendered.Facts.Source.ShouldBe(sourceLabel);
        rendered.Facts.Output.ShouldBe(outputLabel);
        rendered.Facts.Hint.ShouldNotBeNullOrWhiteSpace();
        rendered.Facts.Hint.ShouldNotContain("WorkflowSelection_");
    }

    // -----------------------------------------------------------------------------------
    // §23 — a keyboard-only operator
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// The box, all three workflows and Back are reachable by Tab, and nothing traps focus (§23).
    /// </summary>
    [Fact]
    public async Task A_keyboard_operator_reaches_the_name_the_workflows_and_back()
    {
        using HomeScreenHarness harness = new();

        RenderResult<Traversal> rendered = await RenderAsync(harness, "a11y-keyboard.png", tree =>
        {
            List<FrameworkElement> onScreen = [.. tree.OfType<FrameworkElement>().Where(IsOnScreen)];

            return new Traversal(
                [.. onScreen
                    .Where(element => element is Control { IsTabStop: true, Focusable: true, IsEnabled: true })
                    .Select(Describe)],
                [.. onScreen
                    .Where(element => Confines(KeyboardNavigation.GetTabNavigation(element))
                        || Confines(KeyboardNavigation.GetControlTabNavigation(element)))
                    .Select(Describe)]);
        });

        rendered.BindingErrors.ShouldBeEmpty();

        rendered.Facts.Stops.ShouldContain("WorkflowSelection.OutputName");
        rendered.Facts.Stops.ShouldContain("WorkflowSelection.Back");
        foreach (string id in WorkflowIds)
        {
            rendered.Facts.Stops.ShouldContain(id);
        }

        rendered.Facts.Confined.ShouldBeEmpty();

        static bool Confines(KeyboardNavigationMode mode) =>
            mode is KeyboardNavigationMode.Cycle or KeyboardNavigationMode.Contained;
    }

    /// <summary>The name box exposes the ordinary text value pattern a driver expects (§24).</summary>
    [Fact]
    public async Task The_output_name_box_exposes_the_value_pattern()
    {
        using HomeScreenHarness harness = new();

        RenderResult<Patterns> rendered = await RenderAsync(harness, "a11y-patterns.png", tree =>
        {
            TextBox box = tree.OfType<TextBox>()
                .Single(b => AutomationProperties.GetAutomationId(b) == "WorkflowSelection.OutputName");
            AutomationPeer boxPeer = UIElementAutomationPeer.CreatePeerForElement(box);

            Button first = tree.OfType<Button>()
                .Single(b => AutomationProperties.GetAutomationId(b) == WorkflowIds[0]);
            AutomationPeer buttonPeer = UIElementAutomationPeer.CreatePeerForElement(first);

            return new Patterns(
                boxPeer.GetPattern(PatternInterface.Value) is IValueProvider { IsReadOnly: false },
                boxPeer.GetAutomationControlType(),
                buttonPeer.GetPattern(PatternInterface.Invoke) is IInvokeProvider,
                buttonPeer.GetAutomationControlType());
        });

        rendered.BindingErrors.ShouldBeEmpty();
        rendered.Facts.BoxIsWritableValue.ShouldBeTrue();
        rendered.Facts.BoxType.ShouldBe(AutomationControlType.Edit);
        rendered.Facts.ButtonInvokes.ShouldBeTrue();
        rendered.Facts.ButtonType.ShouldBe(AutomationControlType.Button);
    }

    // -----------------------------------------------------------------------------------
    // §36–§38 — the bounded live proof, driven entirely through UI Automation
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// Import, see the file and its picture, retype the Output Name and choose a workflow — all
    /// through real WPF and UI Automation, then check what was actually persisted (§37, §38).
    /// </summary>
    /// <remarks>
    /// The driver holds an <see cref="IValueProvider"/> and an <see cref="IInvokeProvider"/>
    /// obtained from rendered controls and never touches the view model's properties or commands,
    /// so what is exercised is the path a real operator's typing and click take. No coordinate is
    /// used anywhere. The session then runs one step against the deterministic fake adapter, which
    /// is what turns "the name was saved" into "the produced file is named from it".
    /// </remarks>
    [Fact]
    public async Task A_driver_retypes_the_name_chooses_a_workflow_and_the_output_is_named_from_it()
    {
        using HomeScreenHarness harness = new();
        harness.FilePicker.Path = harness.WriteSourceFile("live-proof.png");
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);

        RecordingNavigation navigation = new();
        WorkflowSelectionViewModel selection = harness.WorkflowSelection(navigation);
        selection.Open(harness.Navigation.WorkflowSelectionFor!);
        await selection.PreviewLoaded;

        Driven observed = default;
        IAsyncRelayCommand? inFlight = null;

        WpfRendering.OnStaThread(() =>
        {
            WorkflowSelectionView view = new() { DataContext = selection };
            view.Measure(WpfRendering.ReviewViewport);
            view.Arrange(new Rect(0, 0, WpfRendering.ReviewViewport.Width, WpfRendering.ReviewViewport.Height));
            view.UpdateLayout();

            RenderedTree tree = Flatten(view);

            string sourceOnScreen = tree.OfType<TextBlock>()
                .Single(b => AutomationProperties.GetAutomationId(b) == "WorkflowSelection.SourceFile")
                .Text;
            bool pictureOnScreen = tree.OfType<Image>().Any(i => i.Source is not null);

            TextBox box = tree.OfType<TextBox>()
                .Single(b => AutomationProperties.GetAutomationId(b) == "WorkflowSelection.OutputName");
            string before = box.Text;

            IValueProvider value = (IValueProvider)UIElementAutomationPeer
                .CreatePeerForElement(box).GetPattern(PatternInterface.Value)!;
            value.SetValue("Live Proof Name");
            view.UpdateLayout();

            Button chosen = tree.OfType<Button>()
                .Single(b => AutomationProperties.GetAutomationId(b) == "WorkflowSelection.PrepareDesignAsset");
            IInvokeProvider invoke = (IInvokeProvider)UIElementAutomationPeer
                .CreatePeerForElement(chosen).GetPattern(PatternInterface.Invoke)!;
            invoke.Invoke();

            // A UIA Invoke queues the click on the dispatcher rather than raising it inline, and
            // the command it starts is asynchronous — so the thread is pumped, exactly as a real
            // desktop pumps it, until the command has finished. Nothing is called on the view
            // model to make this happen.
            observed = new Driven(sourceOnScreen, pictureOnScreen, before, string.Empty);
            inFlight = PumpUntilSelectionCompletes(selection);
            observed = observed with { NameAfter = box.Text };

            // Detach before the STA thread ends, so the command completing later cannot raise
            // CanExecuteChanged at a Button whose thread is gone. The command itself is
            // unaffected: it was already started and is awaited below.
            view.DataContext = null;
            view.UpdateLayout();
        });

        if (inFlight?.ExecutionTask is { } running)
        {
            await running;
        }

        // What the operator saw before typing: the file, its picture, and the derived default.
        observed.SourceFileName.ShouldBe("live-proof.png");
        observed.HadPicture.ShouldBeTrue();
        observed.NameBefore.ShouldBe("live-proof");
        observed.NameAfter.ShouldBe("Live Proof Name");

        // The Session opened, on the workflow that was invoked, under the name that was typed.
        SessionView opened = navigation.SessionFor.ShouldNotBeNull();
        opened.WorkflowType.ShouldBe(WorkflowType.PrepareAsset);
        opened.OutputName.Value.ShouldBe("Live Proof Name");

        // Independently of the screen: what SQLite holds.
        SessionAggregate persisted =
            (await harness.Inner.Repository.LoadAsync(opened.Id, CancellationToken.None)).Value!;
        persisted.Session.OutputName.Value.ShouldBe("Live Proof Name");
        persisted.Snapshot!.OriginalFileName.ShouldBe("live-proof.png");

        // And one deterministic step further: the produced file is named from that base.
        (await harness.Sessions.ExecuteAsync(
            opened.Id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None))
            .IsSuccess.ShouldBeTrue();
        (await harness.Sessions.ExecuteAsync(
            opened.Id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None))
            .IsSuccess.ShouldBeTrue();

        SessionAggregate after =
            (await harness.Inner.Repository.LoadAsync(opened.Id, CancellationToken.None)).Value!;
        after.Revisions.Single(r => r.Operation == OperationKind.Enhance)
            .File.FileName.ShouldBe("Live Proof Name_HD.png");
    }

    // -----------------------------------------------------------------------------------
    // Rendering helpers
    // -----------------------------------------------------------------------------------

    private sealed record Rendered(
        IReadOnlyList<string> Ids, string OutputName, string SourceFile, int ImagesWithSource, string Screen);

    private sealed record Labelled(string Id, string Name);

    private sealed record Labels(string Source, string Output, string Hint);

    private sealed record Traversal(IReadOnlyList<string> Stops, IReadOnlyList<string> Confined);

    private readonly record struct Patterns(
        bool BoxIsWritableValue,
        AutomationControlType BoxType,
        bool ButtonInvokes,
        AutomationControlType ButtonType);

    private readonly record struct Driven(
        string SourceFileName, bool HadPicture, string NameBefore, string NameAfter);

    private static async Task<RenderResult<T>> RenderAsync<T>(
        HomeScreenHarness harness, string fileName, Func<RenderedTree, T> inspect, string? culture = null)
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        try
        {
            if (culture is not null)
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            }

            harness.FilePicker.Path = harness.WriteSourceFile(fileName);
            await harness.Home.ChooseFileCommand.ExecuteAsync(null);

            WorkflowSelectionViewModel selection = harness.WorkflowSelection(new RecordingNavigation());
            selection.Open(harness.Navigation.WorkflowSelectionFor!);
            await selection.PreviewLoaded;
            selection.HasPreview.ShouldBeTrue("the picture must be up for this to mean anything");

            return WpfRendering.RenderExpectingNoBindingErrors(
                () => new WorkflowSelectionView { DataContext = selection },
                WpfRendering.ReviewViewport,
                inspect);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    private static async Task<IReadOnlyList<Labelled>> InCultureAsync(
        string culture, HomeScreenHarness harness, string fileName) =>
        (await RenderAsync(harness, fileName, tree =>
            (IReadOnlyList<Labelled>)[.. WorkflowIds.Select(id =>
            {
                Button button = tree.OfType<Button>()
                    .Single(b => AutomationProperties.GetAutomationId(b) == id);
                return new Labelled(
                    id, UIElementAutomationPeer.CreatePeerForElement(button)?.GetName() ?? string.Empty);
            })],
            culture)).Facts;

    /// <summary>
    /// Runs the dispatcher until the command a UIA <c>Invoke</c> started has finished.
    /// </summary>
    /// <remarks>
    /// <see cref="ButtonAutomationPeer"/> raises the click through
    /// <c>Dispatcher.BeginInvoke</c>, so an unpumped thread would return before the button had
    /// been pressed at all. Pumping is what a real desktop does; doing it here is what keeps this
    /// proof on the operator's own path rather than on a direct call to the command.
    /// <para>
    /// The wait is bounded so a defect stalls this test rather than the run.
    /// </para>
    /// </remarks>
    private static IAsyncRelayCommand PumpUntilSelectionCompletes(WorkflowSelectionViewModel selection)
    {
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        DispatcherFrame frame = new();

        DispatcherTimer deadline = new(DispatcherPriority.Send, dispatcher)
        {
            Interval = TimeSpan.FromSeconds(30),
        };
        deadline.Tick += (_, _) => frame.Continue = false;
        deadline.Start();

        // Queued below the peer's own click, so the click has already run by the time this looks
        // for the task it started.
        dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (selection.SelectCommand.ExecutionTask is not { } running)
            {
                frame.Continue = false;
                return;
            }

            running.ContinueWith(
                _ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)),
                TaskScheduler.Default);
        }));

        Dispatcher.PushFrame(frame);
        deadline.Stop();

        return selection.SelectCommand;
    }

    private static string Describe(FrameworkElement element) =>
        AutomationProperties.GetAutomationId(element) is { Length: > 0 } id ? id : element.GetType().Name;

    /// <summary>Whether an element is one the operator can actually see and use.</summary>
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

            if (node is Visual or System.Windows.Media.Media3D.Visual3D)
            {
                int count = VisualTreeHelper.GetChildrenCount(node);
                for (int index = 0; index < count; index++)
                {
                    Collect(VisualTreeHelper.GetChild(node, index), into);
                }
            }

            foreach (object? child in LogicalTreeHelper.GetChildren(node))
            {
                if (child is DependencyObject dependency)
                {
                    Collect(dependency, into);
                }
            }
        }
    }
}
