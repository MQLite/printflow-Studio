using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using PrintFlow.App;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

/// <summary>
/// Renders each view against a real view model and fails on any WPF data-binding error
/// (Epic 11100 Part 3C2 §17, §18).
/// </summary>
/// <remarks>
/// A mistyped binding path is silent at compile time and silent at run time — WPF writes a
/// line to a trace source and shows an empty control. That is the one class of defect the view
/// model tests cannot see, and the one a manual smoke pass is most likely to miss on a screen
/// full of text, so it is asserted here instead: the views are measured and arranged for real,
/// with <see cref="PresentationTraceSources.DataBindingSource"/> escalated to error level.
/// <para>
/// Each case runs on its own STA thread because WPF elements require one. Nothing is shown:
/// layout alone is enough to resolve every template, item container and binding on the screen.
/// </para>
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class ViewRenderingTests
{
    [Fact]
    public async Task Home_renders_with_a_recent_session_and_no_binding_errors()
    {
        using HomeScreenHarness harness = new();
        harness.FilePicker.Path = harness.WriteSourceFile("rendered.png");
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);
        await harness.Home.RefreshCommand.ExecuteAsync(null);
        harness.Home.RecentSessions.ShouldNotBeEmpty();

        RenderOnStaThread(() => new HomeView { DataContext = harness.Home });
    }

    [Fact]
    public async Task Workflow_selection_renders_with_no_binding_errors()
    {
        using HomeScreenHarness harness = new();
        harness.FilePicker.Path = harness.WriteSourceFile("selection.png");
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);

        WorkflowSelectionViewModel selection = harness.WorkflowSelection(new RecordingNavigation());
        selection.Open(harness.Navigation.WorkflowSelectionFor!);

        RenderOnStaThread(() => new WorkflowSelectionView { DataContext = selection });
    }

    [Fact]
    public async Task The_session_screen_renders_with_no_binding_errors()
    {
        using HomeScreenHarness harness = new();
        harness.FilePicker.Path = harness.WriteSourceFile("session.png");
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);

        SessionViewModel session = harness.Session(new RecordingNavigation());
        SessionView opened = harness.Navigation.WorkflowSelectionFor!;
        session.Open(opened);
        session.Steps.Count.ShouldBe(opened.Steps.Count);
        opened.WorkflowType.ShouldBe(WorkflowType.PrepareAsset);

        RenderOnStaThread(() => new SessionScreenView { DataContext = session });
    }

    /// <summary>
    /// The review state renders too, with the artefact panel and the decision controls
    /// populated (Epic 11100 Part 3C3A §20).
    /// </summary>
    /// <remarks>
    /// The screen above shows a freshly imported session, where the review panel and most of
    /// the metadata grid are collapsed — so it would not have exercised their bindings at all.
    /// This one drives the session to <c>ReviewRequired</c> through the view model's own
    /// commands first, which is the state the operator spends the most time looking at.
    /// </remarks>
    [Fact]
    public async Task The_session_screen_renders_the_review_state_with_no_binding_errors()
    {
        using HomeScreenHarness harness = new();
        harness.FilePicker.Path = harness.WriteSourceFile("review.png");
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);

        SessionViewModel session = harness.Session(new RecordingNavigation());
        session.Open(harness.Navigation.WorkflowSelectionFor!);
        await session.ConfirmOriginalCommand.ExecuteAsync(null);
        await session.RunStepCommand.ExecuteAsync(null);

        session.Notice.ShouldBeNull();
        session.IsReviewRequired.ShouldBeTrue();
        session.HasArtefact.ShouldBeTrue();

        RenderOnStaThread(() => new SessionScreenView { DataContext = session });
    }

    /// <summary>
    /// The production panels render: the dimensions boxes with their preset buttons, and the
    /// W1 selector (Epic 11100 Part 3C3B §21).
    /// </summary>
    /// <remarks>
    /// Both are collapsed on every screen the tests above reach, so none of their bindings —
    /// including the preset buttons' <c>RelativeSource</c> command binding, which is the one
    /// most likely to be written wrongly — would have been resolved anywhere else.
    /// </remarks>
    [Fact]
    public async Task The_session_screen_renders_the_dimensions_and_w1_panels_with_no_binding_errors()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = await TiffSessionAtDimensionsAsync(harness, "dimensions-render.png");

        session.CanSetDimensions.ShouldBeTrue();
        session.CanSelectWhiteUnderbase.ShouldBeTrue();
        session.SizePresets.ShouldNotBeEmpty();

        // Typed input and a selection, so the preview and the confirmed lines have content too.
        session.WidthMmText = "200";
        session.HeightMmText = "150";
        session.SelectedWhiteUnderbaseChoice = session.WhiteUnderbaseChoices[0];
        session.PendingDimensions.ShouldNotBeNullOrWhiteSpace();

        RenderOnStaThread(() => new SessionScreenView { DataContext = session });
    }

    /// <summary>
    /// A completed production session renders, with its output list and the reopen action
    /// (Part 3C3B §21).
    /// </summary>
    [Fact]
    public async Task The_session_screen_renders_the_completed_state_and_output_list_with_no_binding_errors()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = await TiffSessionAtDimensionsAsync(harness, "outputs-render.png");

        session.WidthMmText = "200";
        session.HeightMmText = "150";
        await session.SetDimensionsCommand.ExecuteAsync(null);

        session.SelectedWhiteUnderbaseChoice = session.WhiteUnderbaseChoices[1];
        await session.SelectWhiteUnderbaseCommand.ExecuteAsync(null);

        await session.RunStepCommand.ExecuteAsync(null);
        await session.ApproveCommand.ExecuteAsync(null);
        await session.CompleteCommand.ExecuteAsync(null);

        session.Notice.ShouldBeNull();
        session.HasOutputs.ShouldBeTrue();
        session.CanAddAnotherSize.ShouldBeTrue();
        session.IsFakeTiffOutput.ShouldBeTrue();

        RenderOnStaThread(() => new SessionScreenView { DataContext = session });
    }

    // -------------------------------------------------------------------------------------
    // Epic 11200 Part C1 §25: the four preview states, rendered for real
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// The single-preview state: one pane over the checkerboard, and the zoom controls (§25.1).
    /// </summary>
    /// <remarks>
    /// The pane template, the payload converter, the checkerboard brush and the two zoom
    /// <c>RelativeSource</c> bindings — the transform's and the scrollbar trigger's — are all
    /// resolved for the first time here. Those <c>RelativeSource</c> paths are exactly the kind
    /// that compile fine and silently bind to nothing, which is what this whole file exists to
    /// catch.
    /// </remarks>
    [Fact]
    public async Task The_session_screen_renders_a_single_preview_with_no_binding_errors()
    {
        using HomeScreenHarness harness = new();
        harness.FilePicker.Path = harness.WriteSourceFile("preview-single.png");
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);

        SessionViewModel session = harness.Session(new RecordingNavigation());
        session.Open(harness.Navigation.WorkflowSelectionFor!);
        await session.PreviewsLoaded;

        session.PreviewPanes.Count.ShouldBe(1);
        session.PreviewPanes[0].HasImage.ShouldBeTrue();

        RenderOnStaThread(() => new SessionScreenView { DataContext = session });
    }

    /// <summary>The before/after state, at both the fitted and the magnified extreme (§25.2).</summary>
    /// <remarks>
    /// Rendered twice on purpose: fitted and zoomed take different branches of the pane
    /// template's two triggers — <c>Stretch</c> and the scrollbar visibilities — so rendering
    /// only one of them would leave half the template unexercised.
    /// </remarks>
    [Fact]
    public async Task The_session_screen_renders_the_before_after_comparison_with_no_binding_errors()
    {
        using HomeScreenHarness harness = new();
        harness.FilePicker.Path = harness.WriteSourceFile("preview-pair.png");
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);

        SessionViewModel session = harness.Session(new RecordingNavigation());
        session.Open(harness.Navigation.WorkflowSelectionFor!);
        await session.ConfirmOriginalCommand.ExecuteAsync(null);
        await session.RunStepCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;

        session.PreviewPanes.Count.ShouldBe(2);
        session.IsFitToViewport.ShouldBeTrue();
        RenderOnStaThread(() => new SessionScreenView { DataContext = session });

        session.ZoomInCommand.Execute(null);
        session.ZoomInCommand.Execute(null);
        session.IsFitToViewport.ShouldBeFalse();
        RenderOnStaThread(() => new SessionScreenView { DataContext = session });
    }

    /// <summary>
    /// The transparent-cut-out state: a real trimmed PNG over the checkerboard (§25.3).
    /// </summary>
    /// <remarks>
    /// The one render that shows what the slice is for — an uncropped 12×10 upstream beside the
    /// 5×5 result, both with live alpha. The checkerboard resource is a static brush and cannot
    /// produce a binding error of its own, so what this really proves is that the pair renders
    /// with genuinely transparent content in it.
    /// </remarks>
    [Fact]
    public async Task The_session_screen_renders_a_transparent_trim_result_with_no_binding_errors()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = await PrepareAssetAtTrimAsync(harness, transparentSource: false);

        await session.RunStepCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;

        session.Notice.ShouldBeNull();
        session.PreviewPanes.Count.ShouldBe(2);
        session.PreviewPanes.ShouldAllBe(pane => pane.HasImage);

        RenderOnStaThread(() => new SessionScreenView { DataContext = session });
    }

    /// <summary>The manual-crop state: the notice, and no fabricated "after" (§25.4).</summary>
    [Fact]
    public async Task The_session_screen_renders_the_manual_crop_state_with_no_binding_errors()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = await PrepareAssetAtTrimAsync(harness, transparentSource: true);

        await session.RunStepCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;

        session.IsManualCropRequired.ShouldBeTrue();
        session.PreviewPanes.Count.ShouldBe(1);

        RenderOnStaThread(() => new SessionScreenView { DataContext = session });
    }

    // -------------------------------------------------------------------------------------
    // Epic 11200 Part C2 §33: the crop surface, and the review it leads to
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// The crop surface renders, over a real image, fitted and magnified (§33).
    /// </summary>
    /// <remarks>
    /// Every binding this slice added is resolved for the first time here: the crop pane's own
    /// <c>DataContext</c> hop, the payload converter and checkerboard reused inside it, the two
    /// <c>RelativeSource</c> zoom bindings on a second image, the instructions, the selection
    /// summary and the Apply/Cancel buttons. Rendered twice for the same reason the review pair
    /// is — fitted and magnified take different branches of the template's triggers.
    /// <para>
    /// The overlay <c>Canvas</c> and its outline are named elements the code-behind wires up in
    /// the constructor, so a rendering pass is also the only automated check that those names
    /// still resolve.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_session_screen_renders_the_crop_surface_with_no_binding_errors()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = await OpaqueAssetAtTrimAsync(harness);

        await session.RunStepCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;

        session.CanManualCrop.ShouldBeTrue();
        session.BeginManualCropCommand.Execute(null);
        session.IsCropping.ShouldBeTrue();
        session.CropPane.ShouldNotBeNull();

        RenderOnStaThread(() => new SessionScreenView { DataContext = session });

        // With a selection drawn, so the summary line and the enabled Apply button render too.
        session.TrySetCropSelection(
            new CropSurfaceLayout(12, 10, 12, 10, 12, 10, session.IsFitToViewport, session.ZoomScale),
            2, 2, 10, 8).ShouldBeTrue();
        session.CanApplyManualCrop.ShouldBeTrue();
        RenderOnStaThread(() => new SessionScreenView { DataContext = session });

        session.ZoomInCommand.Execute(null);
        session.IsFitToViewport.ShouldBeFalse();
        RenderOnStaThread(() => new SessionScreenView { DataContext = session });
    }

    /// <summary>A refused selection renders its notice rather than a blank line (§23, §33).</summary>
    [Fact]
    public async Task The_session_screen_renders_a_refused_crop_selection_with_no_binding_errors()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = await OpaqueAssetAtTrimAsync(harness);

        await session.RunStepCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;
        session.BeginManualCropCommand.Execute(null);

        session.TrySetCropSelection(
            new CropSurfaceLayout(12, 10, 12, 10, 12, 10, session.IsFitToViewport, session.ZoomScale),
            5, 5, 5, 5).ShouldBeFalse();
        session.IsCropSelectionInvalid.ShouldBeTrue();

        RenderOnStaThread(() => new SessionScreenView { DataContext = session });
    }

    /// <summary>The Before/After review of a manual crop renders (§33).</summary>
    /// <remarks>
    /// The state the operator lands in after pressing Apply. It goes through the same review
    /// template as an automatic trim's result — which is the point of §18 — so what this really
    /// checks is that the crop surface has stood down and the ordinary panes are back.
    /// </remarks>
    [Fact]
    public async Task The_session_screen_renders_the_manual_crop_result_with_no_binding_errors()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = await OpaqueAssetAtTrimAsync(harness);

        await session.RunStepCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;
        session.BeginManualCropCommand.Execute(null);
        session.TrySetCropSelection(
            new CropSurfaceLayout(12, 10, 12, 10, 12, 10, session.IsFitToViewport, session.ZoomScale),
            3, 2, 9, 7).ShouldBeTrue();

        await session.ApplyManualCropCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;

        session.Notice.ShouldBeNull();
        session.IsCropping.ShouldBeFalse();
        session.IsReviewRequired.ShouldBeTrue();
        session.PreviewPanes.Count.ShouldBe(2);

        RenderOnStaThread(() => new SessionScreenView { DataContext = session });
    }

    /// <summary>A preview that could not be produced renders its notice, not a blank box (§21).</summary>
    [Fact]
    public async Task The_session_screen_renders_an_unavailable_preview_with_no_binding_errors()
    {
        using HomeScreenHarness harness = new();
        harness.FilePicker.Path = harness.WriteSourceFile("preview-gone.png");
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);

        SessionView opened = harness.Navigation.WorkflowSelectionFor!;

        // The imported source snapshot is marked read-only by the workspace, so removing it
        // takes the attribute off first. Only a test does this; nothing in the application has
        // a route to delete a Source file at all.
        string sourceCopy = harness.ResolveInWorkspace(
            (await harness.Inner.Repository.LoadAsync(opened.Id, CancellationToken.None))
                .Value!.Revisions[0].File.RelativePath);
        System.IO.File.SetAttributes(sourceCopy, System.IO.FileAttributes.Normal);
        System.IO.File.Delete(sourceCopy);

        SessionViewModel session = harness.Session(new RecordingNavigation());
        session.Open(opened);
        await session.PreviewsLoaded;

        session.PreviewPanes.Count.ShouldBe(1);
        session.PreviewPanes[0].IsUnavailable.ShouldBeTrue();

        RenderOnStaThread(() => new SessionScreenView { DataContext = session });
    }

    /// <summary>
    /// Imports a no-alpha PREPARE_ASSET session and drives it as far as Trim (Part C2 §33).
    /// </summary>
    /// <remarks>
    /// Both Meitu steps are skipped rather than run, so the file Trim receives is the operator's
    /// own opaque original. Running the fake enhancement first would leave Trim looking at
    /// whatever that adapter wrote, and whether <i>that</i> has an alpha channel is the fake's
    /// business — which would make the manual-crop state this helper exists to reach depend on a
    /// double's implementation detail.
    /// </remarks>
    private static async Task<SessionViewModel> OpaqueAssetAtTrimAsync(HomeScreenHarness harness)
    {
        harness.FilePicker.Path = harness.Inner.WriteOpaqueSourcePng("crop-render.png");
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);

        SessionViewModel session = harness.Session(new RecordingNavigation());
        session.Open(harness.Navigation.WorkflowSelectionFor!);
        await session.ConfirmOriginalCommand.ExecuteAsync(null);
        await session.SkipCommand.ExecuteAsync(null);
        await session.SkipCommand.ExecuteAsync(null);

        session.Notice.ShouldBeNull();
        return session;
    }

    /// <summary>Imports a PREPARE_ASSET session and drives it as far as Trim.</summary>
    private static async Task<SessionViewModel> PrepareAssetAtTrimAsync(
        HomeScreenHarness harness, bool transparentSource)
    {
        harness.FilePicker.Path = transparentSource
            ? harness.Inner.WriteFullyTransparentSourcePng("trim-render-empty.png")
            : harness.Inner.WriteBorderedSourcePng("trim-render-bordered.png");

        await harness.Home.ChooseFileCommand.ExecuteAsync(null);

        SessionViewModel session = harness.Session(new RecordingNavigation());
        session.Open(harness.Navigation.WorkflowSelectionFor!);
        await session.ConfirmOriginalCommand.ExecuteAsync(null);

        await session.RunStepCommand.ExecuteAsync(null);
        await session.ApproveCommand.ExecuteAsync(null);
        await session.RunStepCommand.ExecuteAsync(null);
        await session.ApproveCommand.ExecuteAsync(null);

        session.Notice.ShouldBeNull();
        return session;
    }

    /// <summary>Imports, chooses GENERATE_PRINT_TIFF and confirms, leaving PrintDimensions current.</summary>
    private static async Task<SessionViewModel> TiffSessionAtDimensionsAsync(
        HomeScreenHarness harness, string fileName)
    {
        harness.FilePicker.Path = harness.WriteSourceFile(fileName);
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);

        RecordingNavigation navigation = new();
        WorkflowSelectionViewModel selection = harness.WorkflowSelection(navigation);
        selection.Open(harness.Navigation.WorkflowSelectionFor!);
        await selection.SelectCommand.ExecuteAsync(
            selection.Workflows.Single(choice => choice.Type == WorkflowType.GeneratePrintTiff));

        SessionViewModel session = harness.Session(new RecordingNavigation());
        session.Open(navigation.SessionFor!);
        await session.ConfirmOriginalCommand.ExecuteAsync(null);
        session.Notice.ShouldBeNull();

        return session;
    }

    [Fact]
    public void The_shell_window_and_its_screen_templates_parse()
    {
        // Constructing the window runs the compiled XAML, which is where a bad DataTemplate
        // type reference or a missing view would surface.
        OnStaThread(() =>
        {
            MainWindow window = new();
            window.Resources.Count.ShouldBeGreaterThanOrEqualTo(3);
            window.Close();
        });
    }

    /// <summary>
    /// The negative control: proves the listener above would actually notice.
    /// </summary>
    /// <remarks>
    /// Without this, four passing "no binding errors" tests could equally mean "binding errors
    /// are never reported here" — an assertion that cannot fail is not an assertion.
    /// </remarks>
    [Fact]
    public void A_deliberately_wrong_binding_path_is_reported()
    {
        using HomeScreenHarness harness = new();

        IReadOnlyList<string> errors = Render(() => new UserControl
        {
            DataContext = harness.Session(new RecordingNavigation()),
            Content = new TextBlock().WithBinding(
                TextBlock.TextProperty, new System.Windows.Data.Binding("NoSuchProperty")),
        });

        errors.ShouldNotBeEmpty();
    }

    // -------------------------------------------------------------------------------------

    private static readonly Size Viewport = new(1200, 900);

    private static void RenderOnStaThread(Func<UserControl> create) =>
        WpfRendering.RenderExpectingNoBindingErrors(create, Viewport);

    private static IReadOnlyList<string> Render(Func<UserControl> create) =>
        WpfRendering.Render(create, Viewport);

    private static void OnStaThread(Action action) => WpfRendering.OnStaThread(action);
}

/// <summary>Small helper so a binding can be attached inline in a test expression.</summary>
internal static class BindingTestExtensions
{
    internal static T WithBinding<T>(this T element, DependencyProperty property, System.Windows.Data.BindingBase binding)
        where T : FrameworkElement
    {
        element.SetBinding(property, binding);
        return element;
    }
}
