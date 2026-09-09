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
using PrintFlow.App.Resources;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

/// <summary>
/// Recent Processing as a keyboard operator and a UI Automation driver meet it, and the bounded
/// proof that the record-management action does what the AC says (SCRUM-11117; Jira 11602).
/// </summary>
/// <remarks>
/// Rendered, never read off the XAML: every assertion comes from an element a real WPF measure
/// and arrange pass produced, so a row template that never realises fails here rather than on a
/// live desktop. Identity and wording are asserted separately, the convention this suite's
/// neighbours established — an <c>AutomationId</c> is a contract with a driver and must not move
/// when the product is translated, while an accessible name is the operator's own localised text.
/// <para>
/// The last test is the proof the AC actually rests on: the operator's own path — find the row by
/// the name they gave the job, invoke the action through <see cref="IInvokeProvider"/>, no
/// coordinate anywhere — followed by an independent read of SQLite and the file system.
/// </para>
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class HomeRecentAccessibilityTests
{
    /// <summary>The row identities a driver finds Recent Processing by.</summary>
    private static readonly string[] RowIds =
    [
        "Home.RecentSession",
        "Home.RecentSessionThumbnail",
        "Home.ResumeSession",
        "Home.RemoveSessionRecord",
    ];

    // -----------------------------------------------------------------------------------
    // Identity and wording
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// A removable row renders every promised identity, with a real picture and no binding error.
    /// </summary>
    [Fact]
    public async Task A_removable_row_renders_its_thumbnail_and_every_stable_identity()
    {
        using HomeScreenHarness harness = new();
        HomeViewModel home = await RemovableListAsync(harness, "a11y-row.png");

        RenderResult<Rendered> rendered = WpfRendering.RenderExpectingNoBindingErrors(
            () => new HomeView { DataContext = home },
            WpfRendering.ReviewViewport,
            tree => new Rendered(
                [.. tree.OfType<FrameworkElement>()
                    .Where(IsOnScreen)
                    .Select(AutomationProperties.GetAutomationId)
                    .Where(id => id.StartsWith("Home.", StringComparison.Ordinal))
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)],
                tree.OfType<Image>().Count(image => image.Source is not null),
                AutomationProperties.GetAutomationId(tree.Root),
                [.. tree.OfType<FrameworkElement>()
                    .Where(element => AutomationProperties.GetAutomationId(element) == "Home.RecentSession")
                    .Select(AutomationProperties.GetName)]));

        foreach (string id in RowIds)
        {
            rendered.Facts.Ids.ShouldContain(id);
        }

        rendered.Facts.Ids.ShouldContain("Home.RecentSessionList");
        rendered.Facts.Screen.ShouldBe("Screen.Home");

        // The row announces the operator's own name for the work, which is how a driver finds it.
        rendered.Facts.RowNames.ShouldBe(["a11y-row"]);

        // And the picture really is on screen, not merely a bound but empty Image.
        rendered.Facts.ImagesWithSource.ShouldBe(1);
    }

    /// <summary>
    /// The identities are stable across languages and the wording is not.
    /// </summary>
    /// <remarks>
    /// The Remove action is the one that matters here: it is new wording, it is destructive-
    /// sounding, and an operator who reads it in the wrong language cannot tell a durable list
    /// change from a file deletion. Its label is checked as a real sentence in both languages,
    /// never as a resource key.
    /// </remarks>
    [Fact]
    public async Task The_row_actions_are_localised_while_their_identities_are_not()
    {
        // A harness each, because a rendered ItemsControl leaves a CollectionView bound to the
        // row collection on the render thread; refreshing the same list again from the test
        // thread would be a cross-thread collection change rather than anything about language.
        IReadOnlyList<Labelled> english = await InCultureAsync("a11y-en.png", "en-US");
        IReadOnlyList<Labelled> chinese = await InCultureAsync("a11y-zh.png", "zh-CN");

        english.Select(label => label.Id).ShouldBe(chinese.Select(label => label.Id));

        english.Single(label => label.Id == "Home.RemoveSessionRecord").Name.ShouldBe("Remove from list");
        chinese.Single(label => label.Id == "Home.RemoveSessionRecord").Name.ShouldBe("从列表移除");

        english.Select(label => label.Name).ShouldNotBe(chinese.Select(label => label.Name));
        chinese.ShouldAllBe(label => label.Name.Length > 0);
        chinese.ShouldNotContain(label => label.Name.StartsWith("Home_", StringComparison.Ordinal));
        chinese.ShouldNotContain(label => label.Name == label.Id);
    }

    /// <summary>
    /// The neutral no-picture state is a translated sentence in both languages.
    /// </summary>
    [Theory]
    [InlineData("en-US", "No preview")]
    [InlineData("zh-CN", "无预览")]
    public async Task The_missing_picture_state_is_translated(string culture, string expected)
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);

            using HomeScreenHarness harness = new();
            harness.FilePicker.Path = harness.Inner.Workspace.CreateSourceFile(
                $"unprepared-{culture}.psd", PsdInputPreparationTests.RgbCompositePsd());
            await harness.Home.ChooseFileCommand.ExecuteAsync(null);
            await harness.Home.RefreshCommand.ExecuteAsync(null);
            await harness.Home.ThumbnailsLoaded;

            harness.Home.RecentSessions.Single().HasThumbnail.ShouldBeFalse();

            RenderResult<string> rendered = WpfRendering.RenderExpectingNoBindingErrors(
                () => new HomeView { DataContext = harness.Home },
                WpfRendering.ReviewViewport,
                tree => tree.OfType<TextBlock>()
                    .Single(block =>
                        AutomationProperties.GetAutomationId(block) == "Home.RecentSessionNoThumbnail")
                    .Text);

            rendered.Facts.ShouldBe(expected);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    // -----------------------------------------------------------------------------------
    // Keyboard and patterns
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// A keyboard-only operator reaches both row actions, and nothing traps focus.
    /// </summary>
    /// <remarks>
    /// The thumbnail is deliberately absent from the expected stops. It is a picture, not a
    /// control: making it focusable would put a stop between the operator and every action on
    /// every row, for something there is nothing to do with.
    /// </remarks>
    [Fact]
    public async Task A_keyboard_operator_reaches_both_row_actions_without_being_trapped()
    {
        using HomeScreenHarness harness = new();
        HomeViewModel home = await RemovableListAsync(harness, "a11y-keyboard.png");

        RenderResult<Traversal> rendered = WpfRendering.RenderExpectingNoBindingErrors(
            () => new HomeView { DataContext = home },
            WpfRendering.ReviewViewport,
            tree =>
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

        rendered.Facts.Stops.ShouldContain("Home.ResumeSession");
        rendered.Facts.Stops.ShouldContain("Home.RemoveSessionRecord");
        rendered.Facts.Stops.ShouldNotContain("Home.RecentSessionThumbnail");
        rendered.Facts.Confined.ShouldBeEmpty();

        static bool Confines(KeyboardNavigationMode mode) =>
            mode is KeyboardNavigationMode.Cycle or KeyboardNavigationMode.Contained;
    }

    /// <summary>Both row actions expose the ordinary Invoke pattern a driver expects.</summary>
    [Fact]
    public async Task Both_row_actions_expose_the_invoke_pattern()
    {
        using HomeScreenHarness harness = new();
        HomeViewModel home = await RemovableListAsync(harness, "a11y-patterns.png");

        RenderResult<IReadOnlyList<bool>> rendered = WpfRendering.RenderExpectingNoBindingErrors(
            () => new HomeView { DataContext = home },
            WpfRendering.ReviewViewport,
            tree => (IReadOnlyList<bool>)
            [
                .. new[] { "Home.ResumeSession", "Home.RemoveSessionRecord" }.Select(id =>
                {
                    Button button = tree.OfType<Button>()
                        .Single(candidate => AutomationProperties.GetAutomationId(candidate) == id);
                    AutomationPeer peer = UIElementAutomationPeer.CreatePeerForElement(button);
                    return peer.GetPattern(PatternInterface.Invoke) is IInvokeProvider
                        && peer.GetAutomationControlType() == AutomationControlType.Button;
                }),
            ]);

        rendered.Facts.ShouldAllBe(supported => supported);
    }

    // -----------------------------------------------------------------------------------
    // The bounded proof
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// A driver finds the finished job by the operator's own name, invokes Remove from list, and
    /// only the record leaves — every file and every persisted row survives.
    /// </summary>
    /// <remarks>
    /// The scenario is the minimum that can distinguish right from wrong: a finished job carrying
    /// a real approved production TIFF, a second job still in progress that must be untouched, and
    /// an unresolved interruption that must remain recoverable. The row is located by its
    /// accessible name and the action by its <c>AutomationId</c> inside that row — no coordinate,
    /// no view-model property, no command called directly.
    /// <para>
    /// Everything after the invoke is read back independently of the screen: the repository, the
    /// recovery authority, and the bytes on disk.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_driver_removes_one_finished_record_and_nothing_else_changes()
    {
        using HomeScreenHarness harness = TiffFinalReviewFixture.Harness(out _);

        // One finished job with an approved production TIFF.
        TiffFinalReviewFixture.Review finished =
            await TiffFinalReviewFixture.ReviewRequiredAsync(harness, "driver-finished.png");
        await finished.Screen.ApproveCommand.ExecuteAsync(null);
        await finished.Screen.CompleteCommand.ExecuteAsync(null);
        finished.Screen.Notice.ShouldBeNull();

        // One job still being worked on.
        harness.FilePicker.Path = harness.WriteSourceFile("driver-in-progress.png");
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);
        SessionId inProgress = harness.Navigation.WorkflowSelectionFor!.Id;

        // One unresolved interruption, which must stay recoverable throughout.
        SessionId interrupted = await RecoverySurfaceTests.Seed(harness.Inner, "driver-interrupted");

        SessionAggregate before = await finished.ReloadAsync();
        Dictionary<string, byte[]> files = [];
        foreach (Revision revision in before.Revisions)
        {
            Capture(revision.File);
        }

        foreach (PrintOutput output in before.Outputs)
        {
            Capture(output.File);
        }

        files.Count.ShouldBeGreaterThan(1);
        before.Outputs.Single().ReviewState.ShouldBe(ReviewState.Approved);
        string customerSource = before.Snapshot!.OriginalSourcePath;
        byte[] customerBytes = System.IO.File.ReadAllBytes(customerSource);

        await harness.Home.RefreshCommand.ExecuteAsync(null);
        await harness.Home.ThumbnailsLoaded;

        RecentSessionRow target = harness.Home.RecentSessions.Single(row => row.Id == finished.Id);
        harness.Home.RecentSessions.Select(row => row.Id).ShouldContain(inProgress);
        harness.Home.RecentSessions.ShouldNotContain(row => row.Id == interrupted,
            "an unresolved interruption is a recovery entry, never a contradictory Recent card");

        IAsyncRelayCommand? inFlight = null;
        Invoked observed = default;

        WpfRendering.OnStaThread(() =>
        {
            HomeView view = new() { DataContext = harness.Home };
            view.Measure(WpfRendering.ReviewViewport);
            view.Arrange(new Rect(0, 0, WpfRendering.ReviewViewport.Width, WpfRendering.ReviewViewport.Height));
            view.UpdateLayout();

            // The row is found the way a driver finds it: by the name the operator gave the job.
            FrameworkElement row = Flatten(view).OfType<FrameworkElement>().Single(element =>
                AutomationProperties.GetAutomationId(element) == "Home.RecentSession" &&
                AutomationProperties.GetName(element) == target.DisplayName);

            // ...and the action inside that row, by its stable identity.
            Button remove = Descendants(row)
                .Single(button => AutomationProperties.GetAutomationId(button) == "Home.RemoveSessionRecord");

            AutomationPeer peer = UIElementAutomationPeer.CreatePeerForElement(remove);
            observed = new Invoked(peer.GetName(), remove.IsEnabled);
            ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)!).Invoke();

            inFlight = PumpUntilRemoveCompletes(harness.Home);

            view.DataContext = null;
            view.UpdateLayout();
        });

        if (inFlight?.ExecutionTask is { } running) await running;

        observed.Enabled.ShouldBeTrue();
        observed.Name.ShouldBe(Strings.Home_RemoveRecord);

        // Independently of the screen: the list, from the service.
        IReadOnlyList<SessionListItem> listed =
            (await harness.Sessions.ListRecentAsync(CancellationToken.None)).Value;
        listed.ShouldNotContain(item => item.Id == finished.Id);
        listed.Select(item => item.Id).ShouldContain(inProgress);

        // It survives a restart, over the same database.
        HomeViewModel restarted = harness.RestartHome(new RecordingNavigation());
        await restarted.RefreshCommand.ExecuteAsync(null);
        restarted.RecentSessions.ShouldNotContain(row => row.Id == finished.Id);
        restarted.RecentSessions.Select(row => row.Id).ShouldContain(inProgress);

        // Recovery authority is unchanged and still truthful.
        (await harness.Sessions.ListRecoveryAsync(CancellationToken.None))
            .Value.Select(entry => entry.Id).ShouldContain(interrupted);
        restarted.RecoverySessions.Select(entry => entry.Id).ShouldContain(interrupted);

        // Nothing was deleted: the record is intact, and so is every file it names.
        SessionAggregate after = await finished.ReloadAsync();
        after.Session.State.ShouldBe(SessionState.Completed);
        after.Revisions.ShouldBe(before.Revisions);
        after.Reviews.ShouldBe(before.Reviews);
        after.Outputs.ShouldBe(before.Outputs);
        after.Attempts.ShouldBe(before.Attempts);
        after.Snapshot.ShouldBe(before.Snapshot);

        foreach ((string path, byte[] bytes) in files)
        {
            System.IO.File.Exists(path).ShouldBeTrue($"{path} was removed with the record");
            System.IO.File.ReadAllBytes(path).ShouldBe(bytes, $"{path} was rewritten");
        }

        System.IO.File.ReadAllBytes(customerSource).ShouldBe(customerBytes);

        // And the unrelated in-progress job is still exactly what it was.
        (await harness.Inner.Repository.LoadAsync(inProgress, CancellationToken.None)).Value.ShouldNotBeNull();

        void Capture(Domain.Files.WorkspaceFileRef file)
        {
            string path = harness.Inner.FileWorkspace.ResolveAbsolute(file);
            if (System.IO.File.Exists(path)) files[path] = System.IO.File.ReadAllBytes(path);
        }
    }

    // -----------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------

    private sealed record Rendered(
        IReadOnlyList<string> Ids, int ImagesWithSource, string Screen, IReadOnlyList<string> RowNames);

    private sealed record Labelled(string Id, string Name);

    private sealed record Traversal(IReadOnlyList<string> Stops, IReadOnlyList<string> Confined);

    private readonly record struct Invoked(string Name, bool Enabled);

    /// <summary>One abandoned job — removable, with a picture — listed on a real Home.</summary>
    private static async Task<HomeViewModel> RemovableListAsync(HomeScreenHarness harness, string fileName)
    {
        harness.FilePicker.Path = harness.WriteSourceFile(fileName);
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);
        SessionId id = harness.Navigation.WorkflowSelectionFor!.Id;

        await harness.Sessions.ExecuteAsync(
            id, new WorkflowCommand.AbandonSession("test"), "tester", CancellationToken.None);

        await harness.Home.RefreshCommand.ExecuteAsync(null);
        await harness.Home.ThumbnailsLoaded;

        RecentSessionRow row = harness.Home.RecentSessions.Single();
        row.CanRemoveRecord.ShouldBeTrue();
        row.HasThumbnail.ShouldBeTrue();
        return harness.Home;
    }

    private static async Task<IReadOnlyList<Labelled>> InCultureAsync(string fileName, string culture)
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        using HomeScreenHarness harness = new();
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            HomeViewModel home = await RemovableListAsync(harness, fileName);

            return WpfRendering.RenderExpectingNoBindingErrors(
                () => new HomeView { DataContext = home },
                WpfRendering.ReviewViewport,
                tree => (IReadOnlyList<Labelled>)
                [
                    .. new[] { "Home.ResumeSession", "Home.RemoveSessionRecord" }.Select(id =>
                    {
                        Button button = tree.OfType<Button>()
                            .Single(candidate => AutomationProperties.GetAutomationId(candidate) == id);
                        return new Labelled(
                            id,
                            UIElementAutomationPeer.CreatePeerForElement(button)?.GetName() ?? string.Empty);
                    }),
                ]).Facts;
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    /// <summary>
    /// Runs the dispatcher until the command a UIA <c>Invoke</c> started has finished.
    /// </summary>
    /// <remarks>
    /// <see cref="ButtonAutomationPeer"/> raises the click through <c>Dispatcher.BeginInvoke</c>,
    /// so an unpumped thread would return before the button had been pressed at all. Pumping is
    /// what a real desktop does; doing it here is what keeps this proof on the operator's own path
    /// rather than on a direct call to the command. The wait is bounded so a defect stalls this
    /// test rather than the run.
    /// </remarks>
    private static IAsyncRelayCommand PumpUntilRemoveCompletes(HomeViewModel home)
    {
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        DispatcherFrame frame = new();

        DispatcherTimer deadline = new(DispatcherPriority.Send, dispatcher)
        {
            Interval = TimeSpan.FromSeconds(30),
        };
        deadline.Tick += (_, _) => frame.Continue = false;
        deadline.Start();

        dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (home.RemoveRecordCommand.ExecutionTask is not { } running)
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

        return home.RemoveRecordCommand;
    }

    private static string Describe(FrameworkElement element) =>
        AutomationProperties.GetAutomationId(element) is { Length: > 0 } id ? id : element.GetType().Name;

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

    private static IEnumerable<Button> Descendants(DependencyObject root)
    {
        List<DependencyObject> elements = [];
        Collect(root, elements);
        return elements.OfType<Button>();
    }

    private static RenderedTree Flatten(UserControl root)
    {
        List<DependencyObject> elements = [];
        Collect(root, elements);
        return new RenderedTree(root, elements);
    }

    private static void Collect(DependencyObject node, List<DependencyObject> into)
    {
        if (into.Contains(node))
        {
            return;
        }

        into.Add(node);

        int visualChildren = node is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetChildrenCount(node)
            : 0;

        for (int i = 0; i < visualChildren; i++)
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
