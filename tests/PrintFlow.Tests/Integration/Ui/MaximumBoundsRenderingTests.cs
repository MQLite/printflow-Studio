using System.Globalization;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

/// <summary>
/// Every maximum-bound state the session screen can be in is measured and arranged for real, in
/// both languages, and any WPF data-binding error fails the test
/// (Epic 11400 Part B1A.2B §27).
/// </summary>
/// <remarks>
/// A mistyped binding path is silent at compile time and silent at run time — WPF writes a line
/// to a trace source and shows an empty control. This slice adds several panels whose bindings no
/// other rendering test would resolve, because each of them is collapsed on every screen those
/// tests reach: the plan summary, the review warning, the historical millimetres and the
/// producing attempt's audit.
/// <para>
/// There is no golden-screenshot system and none is proposed. What a build can honestly judge is
/// that every binding resolves, that the satellite resources really are what a zh-CN workstation
/// would show, and that the screen asks for no more room than the window it is given. Whether the
/// wording <i>reads</i> well to a shop operator stays a human judgement and belongs to Final QA.
/// </para>
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class MaximumBoundsRenderingTests
{
    /// <summary>The maximum-box input panel, with limits typed but not confirmed (§27.1).</summary>
    [Fact]
    public async Task The_maximum_bounds_input_renders_with_no_binding_errors()
    {
        using HomeScreenHarness harness = new();
        Fixture open = await AtBoundsAsync(harness, "render-input.png");

        open.Screen.CanSetMaximumBounds.ShouldBeTrue();
        open.Screen.WidthMmText = "200";
        open.Screen.HeightMmText = "150";
        open.Screen.PendingMaximumBounds.ShouldNotBeNullOrWhiteSpace();

        Render(open.Screen);
    }

    /// <summary>
    /// The preset shortcuts, each carrying its own limits under its name (§27.2).
    /// </summary>
    /// <remarks>
    /// The preset buttons became two-line templates in this slice, so their content bindings —
    /// including the <c>RelativeSource</c> command binding that is the one most likely to be
    /// written wrongly — are resolved nowhere else.
    /// </remarks>
    [Fact]
    public async Task The_preset_shortcuts_render_with_their_bounds_and_no_binding_errors()
    {
        using HomeScreenHarness harness = new();
        Fixture open = await AtBoundsAsync(harness, "render-presets.png");

        open.Screen.SizePresets.ShouldNotBeEmpty();
        open.Screen.SizePresets.ShouldAllBe(preset => preset.BoundsLabel.Length > 0);

        // A long-edge box, so the single-limit wording is on screen as well as the box wording.
        open.Screen.ApplyPresetCommand.Execute(
            open.Screen.SizePresets.Single(preset => preset.Preset == SizePreset.A4));
        open.Screen.HeightMmText = open.Screen.WidthMmText;

        Render(open.Screen);
    }

    /// <summary>A confirmed plan that resamples nothing (§27.3).</summary>
    [Fact]
    public async Task The_resolution_only_plan_summary_renders_with_no_binding_errors()
    {
        using HomeScreenHarness harness = new();
        Fixture open = await AtBoundsAsync(harness, "render-resolution-only.png");

        await ConfirmBoundsAsync(open.Screen, 200, 150);

        open.Screen.HasPreparationPlan.ShouldBeTrue();
        open.Screen.HasPreparationLimitingEdge.ShouldBeFalse();

        Render(open.Screen);
    }

    /// <summary>A confirmed plan that shrinks proportionally, with its limiting edge (§27.4).</summary>
    [Fact]
    public async Task The_proportional_shrink_plan_summary_renders_with_no_binding_errors()
    {
        using HomeScreenHarness harness = new();
        Fixture open = await AtBoundsAsync(harness, "render-shrink.png", WideSource(harness));

        await ConfirmBoundsAsync(open.Screen, 50, 50);

        open.Screen.HasPreparationPlan.ShouldBeTrue();
        open.Screen.HasPreparationLimitingEdge.ShouldBeTrue();

        Render(open.Screen);
    }

    /// <summary>The review warning over a legacy pair, with its historical values (§27.5).</summary>
    [Fact]
    public async Task The_dimension_review_warning_renders_with_no_binding_errors()
    {
        using HomeScreenHarness harness = new();
        Fixture open = await LegacyAsync(harness, "render-legacy.png");

        open.Screen.NeedsDimensionReview.ShouldBeTrue();
        open.Screen.HasHistoricalBounds.ShouldBeTrue();
        open.Screen.CanReviewMaximumBounds.ShouldBeTrue();

        Render(open.Screen);
    }

    /// <summary>The return confirmation the review action opens (§27.6).</summary>
    [Fact]
    public async Task The_review_return_confirmation_renders_with_no_binding_errors()
    {
        using HomeScreenHarness harness = new();
        Fixture open = await LegacyAsync(harness, "render-return.png");

        open.Screen.ReviewMaximumBoundsCommand.Execute(null);
        open.Screen.IsConfirmingReturn.ShouldBeTrue();

        Render(open.Screen);
    }

    /// <summary>The Fake ReviewRequired state, with the producing attempt's audit (§27.7).</summary>
    [Fact]
    public async Task The_fake_review_required_attempt_audit_renders_with_no_binding_errors()
    {
        using HomeScreenHarness harness = new();
        Fixture open = await ReviewRequiredAsync(harness, "render-audit.png");

        open.Screen.IsReviewRequired.ShouldBeTrue();
        open.Screen.HasPreparationAttemptAudit.ShouldBeTrue();
        open.Screen.HasPreparationAttemptProjectionNotice.ShouldBeTrue();

        Render(open.Screen);
    }

    /// <summary>The fresh decision Add Another Size reopens the panel for (§27.8).</summary>
    [Fact]
    public async Task The_add_another_size_fresh_decision_state_renders_with_no_binding_errors()
    {
        using HomeScreenHarness harness = new();
        Fixture open = await ReviewRequiredAsync(harness, "render-another.png");

        await open.Screen.ApproveCommand.ExecuteAsync(null);
        await open.Screen.CompleteCommand.ExecuteAsync(null);
        await open.Screen.AddAnotherSizeCommand.ExecuteAsync(null);
        open.Screen.Notice.ShouldBeNull();

        open.Screen.CanSetMaximumBounds.ShouldBeTrue();
        open.Screen.HasPreparationPlan.ShouldBeFalse();
        open.Screen.Outputs.ShouldNotBeEmpty();

        Render(open.Screen);
    }

    /// <summary>
    /// The same states with the Chinese resources loaded (§26, §27).
    /// </summary>
    /// <remarks>
    /// Three states in one culture switch rather than one test each: the culture is ambient, so
    /// every extra test is another pair of assignments around a <c>finally</c> for no additional
    /// coverage. What is asserted is what a build can establish — that the satellite is really
    /// what is on screen, since a missing translation falls back to the raw resource key, and
    /// that the translated wording still fits the window.
    /// </remarks>
    [Fact]
    public async Task The_maximum_bound_states_render_with_the_Chinese_resources()
    {
        CultureInfo previousUi = CultureInfo.CurrentUICulture;
        CultureInfo previous = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo chinese = CultureInfo.GetCultureInfo("zh-CN");
            CultureInfo.CurrentUICulture = chinese;
            CultureInfo.CurrentCulture = chinese;

            using HomeScreenHarness harness = new();
            Fixture open = await AtBoundsAsync(harness, "render-zh.png", WideSource(harness));

            // The satellite really is what the screen is showing.
            open.Screen.MaximumBoundsHeading.ShouldNotBe("Session_MaxBoundsHeading");
            open.Screen.MaxWidthMmLabel.ShouldNotBe("Session_LabelMaxWidthMm");
            open.Screen.ConfirmMaximumBoundsLabel.ShouldNotBe("Session_MaxBoundsConfirm");
            open.Screen.SizePresets.ShouldAllBe(
                preset => !preset.BoundsLabel.StartsWith("Session_", StringComparison.Ordinal));

            // Each render gets its own screen, and the one being driven is never rendered: a
            // rendered ItemsControl builds a CollectionView owned by the render thread, and the
            // next command would then rebuild the steps collection from a thread that does not
            // own it. Driving one screen and rendering copies of it is what keeps the two apart.

            // 1. the input panel, with limits typed but not confirmed.
            await RenderFreshAsync(open, fresh =>
            {
                fresh.WidthMmText = "50";
                fresh.HeightMmText = "50";
                fresh.PendingMaximumBounds.ShouldNotBeNullOrWhiteSpace();
            });

            // 2. a proportional-shrink plan summary.
            await ConfirmBoundsAsync(open.Screen, 50, 50);
            open.Screen.PreparationModeText.ShouldNotBe("Session_PreparationProportionalShrink");
            open.Screen.PreparationLimitingEdge.ShouldNotBeNullOrWhiteSpace();
            await RenderFreshAsync(open);

            // 3. the Fake attempt audit under review.
            await ChooseBranchAsync(open.Screen);
            await open.Screen.RunStepCommand.ExecuteAsync(null);
            open.Screen.HasPreparationAttemptAudit.ShouldBeTrue();
            open.Screen.PreparationAttemptProjectionNotice
                .ShouldNotBe("Session_PreparationAttemptProjectionNotice");
            await RenderFreshAsync(open);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousUi;
            CultureInfo.CurrentCulture = previous;
        }
    }

    // -------------------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------------------

    private sealed record Fixture(HomeScreenHarness Harness, SessionViewModel Screen, SessionId Id)
    {
        public async Task<SessionAggregate> ReloadAsync() =>
            (await Harness.Inner.Repository.LoadAsync(Id, CancellationToken.None)).Value!;

        public async Task RefreshAsync() =>
            Screen.Open((await Harness.Sessions.LoadAsync(Id, CancellationToken.None)).Value);
    }

    /// <summary>
    /// Opens a fresh screen on the session as it stands, arranges any screen-only state, and
    /// renders it.
    /// </summary>
    /// <remarks>
    /// For a test that renders more than once. The rendered screen is thrown away rather than
    /// driven on: layout gives its <c>ItemsControl</c>s collection views owned by the render
    /// thread, and a later command rebuilding those collections from the test thread throws.
    /// </remarks>
    private static async Task RenderFreshAsync(Fixture open, Action<SessionViewModel>? arrange = null)
    {
        SessionViewModel fresh = open.Harness.Session(new RecordingNavigation());
        fresh.Open((await open.Harness.Sessions.LoadAsync(open.Id, CancellationToken.None)).Value);
        await fresh.PreviewsLoaded;

        arrange?.Invoke(fresh);
        Render(fresh);
    }

    /// <summary>Renders at the signed-off viewport and fails on any binding error.</summary>
    private static void Render(SessionViewModel screen)
    {
        RenderResult<int> rendered = WpfRendering.RenderExpectingNoBindingErrors(
            () => new SessionScreenView { DataContext = screen }, WpfRendering.ReviewViewport, _ => 0);

        rendered.DesiredSize.Width.ShouldBeLessThanOrEqualTo(WpfRendering.ReviewViewport.Width);
    }

    private static string WideSource(HomeScreenHarness harness) =>
        harness.Inner.Workspace.CreateSourceFile("wide-render.png", SyntheticImages.Png(2000, 1000, alpha: true));

    private static async Task<Fixture> AtBoundsAsync(
        HomeScreenHarness harness, string fileName, string? source = null)
    {
        harness.FilePicker.Path = source ?? harness.WriteSourceFile(fileName);
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);

        RecordingNavigation navigation = new();
        WorkflowSelectionViewModel selection = harness.WorkflowSelection(navigation);
        selection.Open(harness.Navigation.WorkflowSelectionFor!);
        await selection.SelectCommand.ExecuteAsync(
            selection.Workflows.Single(choice => choice.Type == WorkflowType.GeneratePrintTiff));

        SessionView chosen = navigation.SessionFor.ShouldNotBeNull();
        SessionViewModel screen = harness.Session(new RecordingNavigation());
        screen.Open(chosen);

        await screen.ConfirmOriginalCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();
        await screen.PreviewsLoaded;

        return new Fixture(harness, screen, chosen.Id);
    }

    private static async Task<Fixture> ReviewRequiredAsync(HomeScreenHarness harness, string fileName)
    {
        Fixture open = await AtBoundsAsync(harness, fileName);

        await ConfirmBoundsAsync(open.Screen, 200, 150);
        await ChooseBranchAsync(open.Screen);
        await open.Screen.RunStepCommand.ExecuteAsync(null);
        open.Screen.Notice.ShouldBeNull();
        await open.Screen.PreviewsLoaded;

        return open;
    }

    /// <summary>A session whose millimetres were written under the previous exact-size rule.</summary>
    private static async Task<Fixture> LegacyAsync(HomeScreenHarness harness, string fileName)
    {
        Fixture open = await AtBoundsAsync(harness, fileName);

        await ConfirmBoundsAsync(open.Screen, 200, 150);
        await ChooseBranchAsync(open.Screen);

        SessionAggregate ready = await open.ReloadAsync();
        SessionMutation mutation = new(
            ready.Session with
            {
                DimensionSemantics = PrintDimensionSemantics.LegacyExactPair,
                PrintPreparationPlan = null,
            },
            ready.Steps, [], [], [], [], [], null, null);

        (await harness.Inner.Repository.CommitAsync(mutation, CancellationToken.None))
            .IsSuccess.ShouldBeTrue();

        await open.RefreshAsync();
        await open.Screen.PreviewsLoaded;
        return open;
    }

    private static async Task ConfirmBoundsAsync(
        SessionViewModel screen, double maxWidthMm, double maxHeightMm)
    {
        screen.WidthMmText = maxWidthMm.ToString(CultureInfo.CurrentCulture);
        screen.HeightMmText = maxHeightMm.ToString(CultureInfo.CurrentCulture);
        await screen.SetMaximumBoundsCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();
    }

    private static async Task ChooseBranchAsync(SessionViewModel screen)
    {
        screen.SelectedWhiteUnderbaseChoice = screen.WhiteUnderbaseChoices
            .Single(choice => choice.Branch == WhiteUnderbaseBranch.W1_1px);
        await screen.SelectWhiteUnderbaseCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();
    }
}
