using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

/// <summary>
/// The production-TIFF review surface as a keyboard operator and a UI Automation driver meet it
/// (SCRUM-11104 §29–§32, §34, §48).
/// </summary>
/// <remarks>
/// Rendered, not read off the XAML. Every assertion here comes from an element a real WPF measure
/// and arrange pass produced, so a control present in the markup but never realised — or realised
/// without the identity it was given — fails here rather than on a live desktop.
/// <para>
/// Identity and wording are asserted separately, the convention <c>SessionAccessibilityTests</c>
/// established: an <c>AutomationId</c> is a contract with a driver and must not move when the
/// product is translated, while an accessible name is the operator's own localised sentence and
/// must never be a resource key or an enum.
/// </para>
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class TiffFinalReviewAccessibilityTests
{
    /// <summary>The mode controls a driver must be able to find without reading text (§29).</summary>
    private static readonly string[] ModeIds =
    [
        "Session.TiffReviewColour",
        "Session.TiffReviewWhiteInk",
        "Session.TiffReviewOverlay",
    ];

    // -----------------------------------------------------------------------------------
    // §29, §31 — stable identities, localised names
    // -----------------------------------------------------------------------------------

    [Fact]
    public async Task The_three_modes_render_with_stable_ids_and_no_binding_errors()
    {
        using HomeScreenHarness harness = TiffFinalReviewFixture.Harness(out _);
        SessionId id = (await TiffFinalReviewFixture.ReviewRequiredAsync(harness, "a11y-modes.png")).Id;

        RenderResult<string[]> rendered = await RenderAsync(harness, id, tree =>
            (string[])[.. tree.OfType<RadioButton>()
                .Where(IsOnScreen)
                .Select(AutomationProperties.GetAutomationId)
                .Where(value => value.StartsWith("Session.TiffReview", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)]);

        rendered.BindingErrors.ShouldBeEmpty();
        rendered.Facts.ShouldBe([.. ModeIds.Order(StringComparer.Ordinal)]);
    }

    /// <summary>
    /// The identity is stable across languages and the wording is not (§29, §31, §32).
    /// </summary>
    [Fact]
    public async Task The_mode_names_are_localised_while_their_identities_are_not()
    {
        using HomeScreenHarness harness = TiffFinalReviewFixture.Harness(out _);
        SessionId id = (await TiffFinalReviewFixture.ReviewRequiredAsync(harness, "a11y-locale.png")).Id;

        IReadOnlyList<Labelled> english = await InCultureAsync("en-US", harness, id);
        IReadOnlyList<Labelled> chinese = await InCultureAsync("zh-CN", harness, id);

        english.Select(m => m.Id).ShouldBe(chinese.Select(m => m.Id));
        english.Select(m => m.Name).ShouldBe(["Colour", "White ink", "Colour + white overlay"]);

        chinese.ShouldAllBe(m => m.Name.Length > 0);
        chinese.Select(m => m.Name).ShouldNotBe(english.Select(m => m.Name));
        chinese.ShouldNotContain(m => m.Name.StartsWith("Session_", StringComparison.Ordinal));
        chinese.ShouldNotContain(m => m.Name == m.Id);
    }

    /// <summary>
    /// The image announces what is being shown, not the class showing it (§31).
    /// </summary>
    [Theory]
    [InlineData(TiffReviewMode.Colour, "Colour preview")]
    [InlineData(TiffReviewMode.WhiteInk, "White ink preview")]
    [InlineData(TiffReviewMode.Overlay, "Colour and white ink overlay")]
    public async Task The_preview_announces_the_mode_it_is_showing(TiffReviewMode mode, string expected)
    {
        using HomeScreenHarness harness = TiffFinalReviewFixture.Harness(out _);
        SessionId id = (await TiffFinalReviewFixture.ReviewRequiredAsync(harness, $"a11y-name-{mode}.png")).Id;

        RenderResult<string> rendered = await RenderAsync(harness, id, tree =>
        {
            SessionViewModel model = (SessionViewModel)tree.Root.DataContext;
            model.TiffReviewMode = mode;
            tree.Root.UpdateLayout();

            ScrollViewer viewer = tree.OfType<ScrollViewer>()
                .Single(s => AutomationProperties.GetAutomationId(s) == "Session.TiffReviewImage");
            return UIElementAutomationPeer.CreatePeerForElement(viewer)?.GetName() ?? string.Empty;
        }, culture: "en-US");

        rendered.Facts.ShouldBe(expected);
        rendered.Facts.ShouldNotContain("TiffReviewSurface");
        rendered.Facts.ShouldNotContain("Image");
    }

    // -----------------------------------------------------------------------------------
    // §30 — a keyboard-only operator
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// Every mode, the metadata and both decisions are reachable by Tab, and nothing traps
    /// focus (§30).
    /// </summary>
    /// <remarks>
    /// A radio group is entered by Tab and walked with the arrow keys, so WPF gives only the
    /// checked member a tab stop. What matters for §30 is that the group is reachable at all and
    /// that no ancestor confines Tab inside it — an operator who tabs into the modes must be able
    /// to tab out to Approve.
    /// </remarks>
    [Fact]
    public async Task A_keyboard_operator_reaches_the_modes_the_path_and_both_decisions()
    {
        using HomeScreenHarness harness = TiffFinalReviewFixture.Harness(out _);
        SessionId id = (await TiffFinalReviewFixture.ReviewRequiredAsync(harness, "a11y-keyboard.png")).Id;

        RenderResult<Traversal> rendered = await RenderAsync(harness, id, tree =>
        {
            List<FrameworkElement> onScreen = [.. tree.OfType<FrameworkElement>().Where(IsOnScreen)];

            return new Traversal(
                [.. onScreen
                    .Where(element => element is Control { IsTabStop: true, Focusable: true, IsEnabled: true })
                    .Select(Describe)],
                [.. onScreen
                    .Where(element => element is RadioButton radio && radio.Focusable && radio.IsEnabled)
                    .Select(Describe)],
                [.. onScreen
                    .Where(element => Confines(KeyboardNavigation.GetTabNavigation(element))
                        || Confines(KeyboardNavigation.GetControlTabNavigation(element)))
                    .Select(Describe)]);
        });

        rendered.BindingErrors.ShouldBeEmpty();

        // All three modes are real, focusable, enabled controls.
        foreach (string modeId in ModeIds)
        {
            rendered.Facts.FocusableModes.ShouldContain(modeId);
        }

        // The group is entered by Tab, the path is selectable, and the decision is still there.
        rendered.Facts.Stops.ShouldContain(stop => ModeIds.Contains(stop));
        rendered.Facts.Stops.ShouldContain("Session.TiffOutputPath");
        rendered.Facts.Stops.ShouldContain("Session.TiffReviewImage");
        rendered.Facts.Stops.ShouldContain("Session.Approve");
        rendered.Facts.Stops.ShouldContain("Session.Reject");

        // And nothing new confines Tab (§30).
        rendered.Facts.Confined.ShouldBeEmpty();

        static bool Confines(KeyboardNavigationMode mode) =>
            mode is KeyboardNavigationMode.Cycle or KeyboardNavigationMode.Contained;
    }

    // -----------------------------------------------------------------------------------
    // §48 — driven through UI Automation, not through the view model
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// Selecting White ink through a UIA provider shows the white-ink payload and changes nothing
    /// the approval is bound to (§36, §48, §49).
    /// </summary>
    /// <remarks>
    /// The driver holds an <see cref="ISelectionItemProvider"/> obtained from a rendered radio
    /// button and never touches <see cref="SessionViewModel.TiffReviewMode"/> — so what is
    /// exercised is the path a real operator's click takes, all the way from the control to the
    /// image source on screen.
    /// </remarks>
    [Fact]
    public async Task Selecting_White_ink_through_UI_Automation_shows_the_white_ink_image()
    {
        using HomeScreenHarness harness = TiffFinalReviewFixture.Harness(out _);
        SessionId id = (await TiffFinalReviewFixture.ReviewRequiredAsync(harness, "a11y-uia.png")).Id;

        SessionViewModel screen = harness.Session(new RecordingNavigation());
        screen.Open((await harness.Sessions.LoadAsync(id, CancellationToken.None)).Value);
        await screen.PreviewsLoaded;
        screen.HasTiffReview.ShouldBeTrue();

        string outputId = screen.TiffReviewPrintOutputId!;
        string sha = screen.TiffReviewSha256!;

        Selection observed = default;

        WpfRendering.OnStaThread(() =>
        {
            SessionScreenView view = new() { DataContext = screen };
            view.Measure(WpfRendering.ReviewViewport);
            view.Arrange(new Rect(0, 0, WpfRendering.ReviewViewport.Width, WpfRendering.ReviewViewport.Height));
            view.UpdateLayout();

            RenderedTree tree = Flatten(view);
            Image image = tree.OfType<Image>()
                .Single(i => AutomationProperties.GetName(i).Length > 0
                    && i.Parent is Grid grid && grid.Parent is ScrollViewer scroll
                    && AutomationProperties.GetAutomationId(scroll) == "Session.TiffReviewImage");

            ImageSource? before = image.Source;
            before.ShouldNotBeNull();

            RadioButton whiteInk = tree.OfType<RadioButton>()
                .Single(r => AutomationProperties.GetAutomationId(r) == "Session.TiffReviewWhiteInk");
            AutomationPeer peer = UIElementAutomationPeer.CreatePeerForElement(whiteInk);

            ((ISelectionItemProvider)peer.GetPattern(PatternInterface.SelectionItem)!).Select();
            view.UpdateLayout();

            observed = new Selection(
                screen.TiffReviewMode,
                !ReferenceEquals(before, image.Source),
                AutomationProperties.GetName(image),
                image.Source is not null);

            view.DataContext = null;
            view.UpdateLayout();
        });

        observed.Mode.ShouldBe(TiffReviewMode.WhiteInk);
        observed.SourceChanged.ShouldBeTrue("the picture must actually change, not just the label");
        observed.Name.ShouldNotBeNullOrWhiteSpace();
        observed.HasSource.ShouldBeTrue();

        // A selection is a way of looking, and nothing more (§36, §49).
        screen.TiffReviewPrintOutputId.ShouldBe(outputId);
        screen.TiffReviewSha256.ShouldBe(sha);
        screen.CanApprove.ShouldBeTrue();

        SessionAggregate persisted = (await harness.Inner.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        persisted.Outputs.ShouldHaveSingleItem().ReviewState.ShouldBe(ReviewState.NotReviewed);
        persisted.Reviews.ShouldNotContain(r => r.Step == StepKind.PhotoshopOutput);
    }

    // -----------------------------------------------------------------------------------
    // §25, §29 — the metadata block is addressable and populated
    // -----------------------------------------------------------------------------------

    [Fact]
    public async Task The_metadata_rows_and_the_output_path_render_with_their_identities()
    {
        using HomeScreenHarness harness = TiffFinalReviewFixture.Harness(out _);
        SessionId id = (await TiffFinalReviewFixture.ReviewRequiredAsync(harness, "a11y-metadata.png")).Id;

        SessionAggregate persisted = (await harness.Inner.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        PrintOutput output = persisted.Outputs.ShouldHaveSingleItem();
        string expectedPath = harness.Inner.FileWorkspace.ResolveAbsolute(output.File);

        RenderResult<Rendered> rendered = await RenderAsync(harness, id, tree => new Rendered(
            [.. tree.OfType<TextBlock>()
                .Where(IsOnScreen)
                .Select(AutomationProperties.GetAutomationId)
                .Where(value => value.StartsWith("Session.TiffMetadata.", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)],
            tree.OfType<TextBox>()
                .Where(IsOnScreen)
                .SingleOrDefault(b => AutomationProperties.GetAutomationId(b) == "Session.TiffOutputPath")
                ?.Text ?? string.Empty,
            [.. tree.OfType<TextBlock>()
                .Where(IsOnScreen)
                .Where(block => AutomationProperties.GetAutomationId(block)
                    .StartsWith("Session.TiffMetadata.", StringComparison.Ordinal))
                .Select(block => block.Text)]));

        rendered.BindingErrors.ShouldBeEmpty();
        rendered.Facts.MetadataIds.ShouldBe(
        [
            "Session.TiffMetadata.Colour",
            "Session.TiffMetadata.EffectiveDpi",
            "Session.TiffMetadata.OutputFile",
            "Session.TiffMetadata.OutputPath",
            "Session.TiffMetadata.Physical",
            "Session.TiffMetadata.Pixels",
            "Session.TiffMetadata.Preset",
            "Session.TiffMetadata.Resolution",
            "Session.TiffMetadata.Sha256",
            "Session.TiffMetadata.WhiteInk",
        ]);

        rendered.Facts.Path.ShouldBe(expectedPath);
        rendered.Facts.Values.ShouldAllBe(value => !string.IsNullOrWhiteSpace(value));
    }

    // -----------------------------------------------------------------------------------
    // Rendering helpers
    // -----------------------------------------------------------------------------------

    private sealed record Labelled(string Id, string Name);

    private sealed record Traversal(
        IReadOnlyList<string> Stops,
        IReadOnlyList<string> FocusableModes,
        IReadOnlyList<string> Confined);

    private sealed record Rendered(
        IReadOnlyList<string> MetadataIds, string Path, IReadOnlyList<string> Values);

    private readonly record struct Selection(
        TiffReviewMode Mode, bool SourceChanged, string Name, bool HasSource);

    private static async Task<RenderResult<T>> RenderAsync<T>(
        HomeScreenHarness harness, SessionId id, Func<RenderedTree, T> inspect, string? culture = null)
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        try
        {
            if (culture is not null)
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            }

            SessionViewModel fresh = harness.Session(new RecordingNavigation());
            fresh.Open((await harness.Sessions.LoadAsync(id, CancellationToken.None)).Value);
            await fresh.PreviewsLoaded;
            fresh.HasTiffReview.ShouldBeTrue("the specialist surface must be up for this to mean anything");

            return WpfRendering.RenderExpectingNoBindingErrors(
                () => new SessionScreenView { DataContext = fresh }, WpfRendering.ReviewViewport, inspect);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    private static async Task<IReadOnlyList<Labelled>> InCultureAsync(
        string culture, HomeScreenHarness harness, SessionId id) =>
        (await RenderAsync(harness, id, tree =>
            (IReadOnlyList<Labelled>)[.. tree.OfType<RadioButton>()
                .Where(IsOnScreen)
                .Where(r => ModeIds.Contains(AutomationProperties.GetAutomationId(r)))
                .Select(r => new Labelled(
                    AutomationProperties.GetAutomationId(r),
                    UIElementAutomationPeer.CreatePeerForElement(r)?.GetName() ?? string.Empty))],
            culture)).Facts;

    private static string Describe(FrameworkElement element) =>
        AutomationProperties.GetAutomationId(element) is { Length: > 0 } id ? id : element.GetType().Name;

    /// <summary>Whether an element is one the operator can actually see and use.</summary>
    /// <remarks>
    /// <see cref="UIElement.IsVisible"/> cannot answer this for a tree that was measured and
    /// arranged but never attached to a window, which is exactly what these render. The declared
    /// visibility of the element and every ancestor is the same question without that dependency.
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

    private static RenderedTree Flatten(UserControl root)
    {
        List<DependencyObject> elements = [];
        Collect(root, elements);
        return new RenderedTree(root, elements);

        static void Collect(DependencyObject node, List<DependencyObject> into)
        {
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
                if (child is DependencyObject dependency && !into.Contains(dependency))
                {
                    Collect(dependency, into);
                }
            }
        }
    }
}
