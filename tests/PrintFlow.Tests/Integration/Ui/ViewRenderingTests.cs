using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using PrintFlow.App;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Trimming;
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

        session.CanSetMaximumBounds.ShouldBeTrue();
        session.CanSelectWhiteUnderbase.ShouldBeTrue();
        session.SizePresets.ShouldNotBeEmpty();

        // Typed input and a selection, so the preview and the confirmed lines have content too.
        session.WidthMmText = "200";
        session.HeightMmText = "150";
        session.SelectedWhiteUnderbaseChoice = session.WhiteUnderbaseChoices[0];
        session.PendingMaximumBounds.ShouldNotBeNullOrWhiteSpace();

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
        await session.SetMaximumBoundsCommand.ExecuteAsync(null);

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

        // Enhancement.
        await session.RunStepCommand.ExecuteAsync(null);
        await session.ApproveCommand.ExecuteAsync(null);

        // Background Removal, which since Epic 11300 Part C2B1 needs an explicit
        // reviewed-content authority before it will start (§7). C2B1 ships no control for it, so
        // the decision is issued through the service seam C2B2 will build the control on.
        await SessionServiceHarness.AuthoriseBackgroundRemovalAsync(
            harness.Sessions, harness.Navigation.WorkflowSelectionFor!.Id);
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
    // Epic 11200 Part C3 §25: the return selector and the trim margin controls
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// The return selector renders, with and without the confirmation showing (§25).
    /// </summary>
    /// <remarks>
    /// Twice, because the confirmation is a collapsed branch until it is opened: its warning
    /// text and its two buttons would otherwise never have a binding resolved. The
    /// <c>ComboBox</c>'s <c>DisplayMemberPath</c> is the one most likely to be written wrongly
    /// and the one that would be silent if it were.
    /// </remarks>
    [Fact]
    public async Task The_session_screen_renders_the_return_selector_with_no_binding_errors()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = await PrepareAssetAtTrimAsync(harness, transparentSource: false);

        session.CanReturnToStep.ShouldBeTrue();
        session.ReturnTargets.ShouldNotBeEmpty();
        RenderOnStaThread(() => new SessionScreenView { DataContext = session });

        session.SelectedReturnTarget = session.ReturnTargets[0];
        session.BeginReturnCommand.Execute(null);
        session.IsConfirmingReturn.ShouldBeTrue();
        RenderOnStaThread(() => new SessionScreenView { DataContext = session });
    }

    /// <summary>
    /// All three trim margin states render (§25).
    /// </summary>
    /// <remarks>
    /// Tight, uniform and edge-specific are three mutually exclusive branches of the same
    /// panel, so a single render would leave two of them — including the four-box grid, which
    /// is the fiddliest markup this slice adds — completely unexercised.
    /// </remarks>
    [Fact]
    public async Task The_session_screen_renders_every_trim_margin_state_with_no_binding_errors()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = await PrepareAssetAtTrimAsync(harness, transparentSource: false);

        session.CanSetTrimParameters.ShouldBeTrue();
        session.SelectedTrimMode.Mode.ShouldBe(TrimMode.TightCrop);
        RenderOnStaThread(() => new SessionScreenView { DataContext = session });

        session.SelectedTrimMode = session.TrimModes.Single(m => m.Mode == TrimMode.UniformMargin);
        session.UniformMarginText = "8";
        session.IsUniformMargin.ShouldBeTrue();
        RenderOnStaThread(() => new SessionScreenView { DataContext = session });

        session.SelectedTrimMode = session.TrimModes.Single(m => m.Mode == TrimMode.EdgeSpecificMargin);
        session.TopMarginText = "4";
        session.RightMarginText = "8";
        session.BottomMarginText = "4";
        session.LeftMarginText = "8";
        session.IsEdgeSpecificMargin.ShouldBeTrue();
        RenderOnStaThread(() => new SessionScreenView { DataContext = session });
    }

    /// <summary>
    /// The trim review renders with its parameter summary (§18, §25).
    /// </summary>
    /// <remarks>
    /// The one line this slice adds to the review panel, which is collapsed on every other
    /// render in this file because no other state has a deterministic trim on screen.
    /// </remarks>
    [Fact]
    public async Task The_session_screen_renders_the_trim_review_with_its_parameter_summary()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = await PrepareAssetAtTrimAsync(harness, transparentSource: false);

        session.SelectedTrimMode = session.TrimModes.Single(m => m.Mode == TrimMode.EdgeSpecificMargin);
        session.TopMarginText = "1";
        session.RightMarginText = "2";
        session.BottomMarginText = "1";
        session.LeftMarginText = "2";
        await session.ApplyTrimMarginCommand.ExecuteAsync(null);
        session.Notice.ShouldBeNull();

        await session.RunStepCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;

        session.IsReviewRequired.ShouldBeTrue();
        session.HasTrimParameters.ShouldBeTrue();
        session.TrimParametersSummary.ShouldNotBeNullOrWhiteSpace();

        RenderOnStaThread(() => new SessionScreenView { DataContext = session });
    }

    /// <summary>
    /// The same two panels with the Chinese resources loaded (§25).
    /// </summary>
    /// <remarks>
    /// What a build can honestly judge about zh-CN parity: every binding still resolves with the
    /// satellite loaded, the strings really are the translated ones rather than raw resource
    /// keys, and the screen still asks for no more room than the window it is given. Whether the
    /// Chinese wording <i>reads</i> well remains a human judgement, and nobody has made it
    /// (§26).
    /// </remarks>
    [Fact]
    public async Task The_return_and_trim_controls_fit_the_window_with_the_Chinese_resources()
    {
        CultureInfo previousUi = CultureInfo.CurrentUICulture;
        CultureInfo previous = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo chinese = CultureInfo.GetCultureInfo("zh-CN");
            CultureInfo.CurrentUICulture = chinese;
            CultureInfo.CurrentCulture = chinese;

            using HomeScreenHarness harness = new();
            SessionViewModel session = await PrepareAssetAtTrimAsync(harness, transparentSource: false);

            // The satellite really is what the screen is showing: a missing translation would
            // fall back to the resource key, which these would then equal.
            session.TrimHeading.ShouldNotBe("Session_TrimHeading");
            session.ReturnHeading.ShouldNotBe("Session_ReturnHeading");
            session.ReturnConfirmQuestion.ShouldNotBe("Session_ReturnConfirmQuestion");
            session.TrimModes.ShouldAllBe(mode => !mode.Label.StartsWith("Session_", StringComparison.Ordinal));

            session.SelectedTrimMode = session.TrimModes.Single(m => m.Mode == TrimMode.EdgeSpecificMargin);
            session.SelectedReturnTarget = session.ReturnTargets[0];
            session.BeginReturnCommand.Execute(null);

            RenderResult<int> rendered = WpfRendering.RenderExpectingNoBindingErrors(
                () => new SessionScreenView { DataContext = session }, WpfRendering.ReviewViewport, _ => 0);

            rendered.DesiredSize.Width.ShouldBeLessThanOrEqualTo(WpfRendering.ReviewViewport.Width);
            rendered.DesiredSize.Height.ShouldBeLessThanOrEqualTo(WpfRendering.ReviewViewport.Height);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousUi;
            CultureInfo.CurrentCulture = previous;
        }
    }

    // -------------------------------------------------------------------------------------
    // Background removal authority (Epic 11300 Part C2B2 §23)
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// All four Background Removal states render, at the representative window size (§23).
    /// </summary>
    /// <remarks>
    /// One test rather than four, because the four are one journey: undecided, authorised,
    /// ReviewRequired with the attempt's authority beside the result, and back to unauthorised
    /// after the reviewed content is replaced. Driving them in sequence is what proves the
    /// panels really do appear and disappear, which four independently constructed states would
    /// not.
    /// <para>
    /// One view model drives the session and a <i>separate</i> one is opened for each render.
    /// A rendered <c>ItemsControl</c> leaves a WPF <c>CollectionView</c> bound to the view
    /// model's collections on the STA thread that built it, and mutating those collections
    /// afterwards from the test thread throws — so the screen that was rendered is never driven
    /// again. Only a test has this problem: the application has one UI thread throughout.
    /// </para>
    /// <para>
    /// Each state is measured against <see cref="WpfRendering.ReviewViewport"/> — the size the
    /// operator screens are signed off against — so a panel that pushed the screen past the
    /// window would fail here rather than in front of an operator. There is no golden
    /// screenshot anywhere in this file (§23).
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_four_background_removal_states_render_and_fit_the_window()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel driver = await PrepareAssetAtBackgroundRemovalAsync(harness);
        SessionId id = harness.Navigation.WorkflowSelectionFor!.Id;

        // 1. Undecided: the authorisation panel is offered, with the confirmation open so its
        //    bindings are resolved too, and there is no run.
        driver.CanAuthoriseAutomaticSelection.ShouldBeTrue();
        driver.IsAutomaticSelectionPending.ShouldBeTrue();
        driver.CanRunBackgroundRemoval.ShouldBeFalse();
        await RenderStateAsync(harness, id, confirming: true);

        // 2. Authorised: the status line replaces the action, and the run becomes available.
        driver.BeginAutomaticSelectionCommand.Execute(null);
        await driver.ConfirmAutomaticSelectionCommand.ExecuteAsync(null);
        await driver.PreviewsLoaded;
        driver.IsAutomaticSelectionAuthorised.ShouldBeTrue();
        driver.CanRunBackgroundRemoval.ShouldBeTrue();
        await RenderStateAsync(harness, id, confirming: false);

        // 3. ReviewRequired: the cutout, its before/after pair, and the producing attempt's
        //    authority in the review panel.
        await driver.RunStepCommand.ExecuteAsync(null);
        await driver.PreviewsLoaded;
        driver.IsReviewRequired.ShouldBeTrue();
        driver.HasBackgroundRemovalAttemptAudit.ShouldBeTrue();
        driver.CanAuthoriseAutomaticSelection.ShouldBeFalse("the decision panel is gone during a review.");
        await RenderStateAsync(harness, id, confirming: false);

        // 4. Stale: the reviewed content is replaced, so the authority the session still holds
        //    is no longer usable and the screen is back where it started.
        await driver.RejectCommand.ExecuteAsync(null);
        driver.SelectedReturnTarget = driver.ReturnTargets.Single(t => t.Step == StepKind.Enhancement);
        driver.BeginReturnCommand.Execute(null);
        await driver.ConfirmReturnCommand.ExecuteAsync(null);
        await driver.RunStepCommand.ExecuteAsync(null);
        await driver.ApproveCommand.ExecuteAsync(null);
        await driver.PreviewsLoaded;

        driver.Notice.ShouldBeNull();
        driver.IsAutomaticSelectionAuthorised.ShouldBeFalse();
        driver.CanAuthoriseAutomaticSelection.ShouldBeTrue();
        driver.CanRunBackgroundRemoval.ShouldBeFalse();
        await RenderStateAsync(harness, id, confirming: false);
    }

    /// <summary>
    /// The Background Removal panels render with the Chinese resources loaded (§22, §23).
    /// </summary>
    /// <remarks>
    /// The same honest half a build can judge as the trim panels above: every binding still
    /// resolves with the satellite loaded, the strings really are translated rather than raw
    /// resource keys, and nothing outgrows the window. Whether the Chinese wording reads well
    /// is a human judgement (§24).
    /// </remarks>
    [Fact]
    public async Task The_background_removal_panels_fit_the_window_with_the_Chinese_resources()
    {
        CultureInfo previousUi = CultureInfo.CurrentUICulture;
        CultureInfo previous = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo chinese = CultureInfo.GetCultureInfo("zh-CN");
            CultureInfo.CurrentUICulture = chinese;
            CultureInfo.CurrentCulture = chinese;

            using HomeScreenHarness harness = new();
            SessionViewModel driver = await PrepareAssetAtBackgroundRemovalAsync(harness);
            SessionId id = harness.Navigation.WorkflowSelectionFor!.Id;

            // A missing translation would fall back to the resource key, which these would equal.
            driver.BackgroundRemovalHeading.ShouldNotBe("Session_BackgroundRemovalHeading");
            driver.BackgroundRemovalHint.ShouldNotBe("Session_BackgroundRemovalHint");
            driver.AuthoriseAutomaticSelectionLabel.ShouldNotBe("Session_BackgroundRemovalAuthorise");
            driver.AutomaticSelectionConfirmQuestion.ShouldNotBe("Session_BackgroundRemovalConfirmQuestion");
            driver.AutomaticSelectionNotAuthorisedNotice.ShouldNotBe("Session_BackgroundRemovalNotAuthorised");

            await RenderStateAsync(harness, id, confirming: true);

            driver.BeginAutomaticSelectionCommand.Execute(null);
            await driver.ConfirmAutomaticSelectionCommand.ExecuteAsync(null);
            await driver.PreviewsLoaded;
            driver.AutomaticSelectionAuthorisedNotice.ShouldNotStartWith("Session_");
            await RenderStateAsync(harness, id, confirming: false);

            await driver.RunStepCommand.ExecuteAsync(null);
            await driver.PreviewsLoaded;
            driver.BackgroundRemovalAttemptAudit.ShouldNotStartWith("Session_");
            await RenderStateAsync(harness, id, confirming: false);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousUi;
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>
    /// Opens a fresh screen on the session as it stands and renders it, asserting it asks for
    /// no more room than the window it is given.
    /// </summary>
    /// <remarks>
    /// Fresh every time, so the rendered screen is thrown away rather than driven on — see the
    /// journey test's remarks for why that matters.
    /// </remarks>
    private static async Task RenderStateAsync(HomeScreenHarness harness, SessionId id, bool confirming)
    {
        SessionViewModel screen = harness.Session(new RecordingNavigation());
        screen.Open((await harness.Sessions.LoadAsync(id, CancellationToken.None)).Value);
        await screen.PreviewsLoaded;

        if (confirming)
        {
            screen.BeginAutomaticSelectionCommand.Execute(null);
            screen.IsConfirmingAutomaticSelection.ShouldBeTrue();
        }

        RenderResult<int> rendered = WpfRendering.RenderExpectingNoBindingErrors(
            () => new SessionScreenView { DataContext = screen }, WpfRendering.ReviewViewport, _ => 0);

        rendered.DesiredSize.Width.ShouldBeLessThanOrEqualTo(WpfRendering.ReviewViewport.Width);
        rendered.DesiredSize.Height.ShouldBeLessThanOrEqualTo(WpfRendering.ReviewViewport.Height);
    }

    /// <summary>
    /// Imports, confirms, runs and approves Enhancement, and stops on Background Removal.
    /// </summary>
    private static async Task<SessionViewModel> PrepareAssetAtBackgroundRemovalAsync(HomeScreenHarness harness)
    {
        harness.FilePicker.Path = harness.Inner.WriteBorderedSourcePng("br-render.png");
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);

        SessionViewModel session = harness.Session(new RecordingNavigation());
        session.Open(harness.Navigation.WorkflowSelectionFor!);
        await session.ConfirmOriginalCommand.ExecuteAsync(null);
        await session.RunStepCommand.ExecuteAsync(null);
        await session.ApproveCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;

        session.Notice.ShouldBeNull();
        return session;
    }

    // -------------------------------------------------------------------------------------

    // -------------------------------------------------------------------------------------
    // Production readiness (Epic 11500 Part C §3, §5)
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// The readiness screen renders in both states, with no binding errors
    /// (Epic 11500 Part C §3).
    /// </summary>
    /// <remarks>
    /// Ready and not-ready are one test because they are one screen with different lists in it:
    /// the blocking list is empty in the first and populated in the second, and rendering only
    /// the empty one would never build the row template at all.
    /// <para>
    /// A separate screen is opened for each render. A rendered <c>ItemsControl</c> leaves a WPF
    /// <c>CollectionView</c> bound to the view model's collections on the STA thread that built
    /// it, so the one that was rendered is never refreshed again.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_readiness_screen_renders_in_both_states_with_no_binding_errors()
    {
        using WorkstationVerificationFixture verified = new();
        EnvironmentReadinessViewModel ready = ReadinessScreen(verified);
        await ready.OpenAsync(CancellationToken.None);
        ready.IsReady.ShouldBeTrue();
        ready.Advisories.ShouldNotBeEmpty();

        RenderOnStaThread(() => new EnvironmentReadinessView { DataContext = ready });

        using WorkstationVerificationFixture broken = new();
        WorkstationVerificationFixture.Corrupt(broken.PhotoshopPath);
        broken.Facts.Display = broken.Facts.Display with { ActiveDisplayCount = 2 };

        EnvironmentReadinessViewModel refused = ReadinessScreen(broken);
        await refused.OpenAsync(CancellationToken.None);
        refused.IsReady.ShouldBeFalse();
        refused.BlockingFailures.Count.ShouldBeGreaterThan(1);

        RenderOnStaThread(() => new EnvironmentReadinessView { DataContext = refused });
    }

    /// <summary>
    /// The readiness screen fits the operator's window with the Chinese resources
    /// (Epic 11500 Part C §3).
    /// </summary>
    /// <remarks>
    /// The screen carries the longest sentences in the shell — the restart requirement and what
    /// Refresh does — and they are the two an operator must actually read. Asserted at the size
    /// the operator screens are signed off against; whether the Chinese wording reads well
    /// remains a human judgement, and nobody has made it.
    /// </remarks>
    [Fact]
    public async Task The_readiness_screen_fits_the_window_with_the_Chinese_resources()
    {
        CultureInfo previousUi = CultureInfo.CurrentUICulture;
        CultureInfo previous = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo chinese = CultureInfo.GetCultureInfo("zh-CN");
            CultureInfo.CurrentUICulture = chinese;
            CultureInfo.CurrentCulture = chinese;

            using WorkstationVerificationFixture fixture = new();
            WorkstationVerificationFixture.Corrupt(fixture.PhotoshopPath);

            EnvironmentReadinessViewModel screen = ReadinessScreen(fixture);
            await screen.OpenAsync(CancellationToken.None);

            // The satellite really is what the screen is showing.
            screen.Heading.ShouldNotBe("Environment_Heading");
            screen.RestartRequirement.ShouldNotBe("Environment_RestartRequired");
            screen.Checks.ShouldAllBe(
                row => !row.Name.StartsWith("EnvironmentCheckName_", StringComparison.Ordinal));

            RenderResult<int> rendered = WpfRendering.RenderExpectingNoBindingErrors(
                () => new EnvironmentReadinessView { DataContext = screen },
                WpfRendering.ReviewViewport,
                _ => 0);

            rendered.DesiredSize.Width.ShouldBeLessThanOrEqualTo(WpfRendering.ReviewViewport.Width);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousUi;
            CultureInfo.CurrentCulture = previous;
        }
    }

    private static EnvironmentReadinessViewModel ReadinessScreen(WorkstationVerificationFixture fixture) =>
        new(new PrintFlow.Infrastructure.Gate.VerifiedEnvironmentGate(fixture.CreateVerifier()),
            new RecordingNavigation());

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
