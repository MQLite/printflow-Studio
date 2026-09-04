using System.Globalization;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrintFlow.App.Composition;
using PrintFlow.App.Navigation;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Trimming;
using PrintFlow.Infrastructure.Startup;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

/// <summary>
/// The smoke passes for Part 3C3A (§21) and Part 3C3B (§22), driven through the <b>real
/// composed application graph</b>.
/// </summary>
/// <remarks>
/// These are the operator journeys the slices are signed off against, walked end to end from
/// Home through Workflow Selection to the session screen, on synthetic files only: Smoke A–D
/// are the PREPARE_ASSET paths — success, reject/retry, skip, hand-off — and the three
/// <c>Production_smoke</c> passes below are the TIFF paths added in Part 3C3B.
/// <para>
/// What makes them a smoke pass rather than another unit of the suite above is what is
/// <i>not</i> substituted: the whole graph comes from <see cref="ApplicationStartup"/> — the
/// same configuration load, directory creation, migration run, preset verification, crash
/// recovery and <c>ServiceRegistration</c> the shipped application performs — and navigation is
/// the real <see cref="NavigationService"/> resolving real screens from the container. Only the
/// two things a test cannot have are stood in for: the single-instance guard, and the modal
/// file dialog.
/// </para>
/// <para>
/// The remaining manual step is looking at the window, which
/// <c>ViewRenderingTests</c> covers by rendering the screens for real and failing on any
/// binding error.
/// </para>
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class SessionSmokeTests
{
    // -------------------------------------------------------------------------------------
    // Smoke A — success: Home -> PREPARE_ASSET -> Confirm -> Run -> Review -> Approve
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task Smoke_A_import_confirm_run_enhancement_and_approve()
    {
        using SmokeApplication app = await SmokeApplication.StartAsync();

        SessionViewModel session = await app.ImportAndChooseAsync("smoke-a.png", WorkflowType.PrepareAsset);
        SessionId id = app.OpenSessionId;

        session.CanConfirmOriginal.ShouldBeTrue();
        await session.ConfirmOriginalCommand.ExecuteAsync(null);

        session.CanRunStep.ShouldBeTrue();
        await session.RunStepCommand.ExecuteAsync(null);

        // The review state the operator would be looking at.
        session.IsReviewRequired.ShouldBeTrue();
        session.HasArtefact.ShouldBeTrue();
        session.ArtefactIsInput.ShouldBeFalse();
        session.IsFakeProcessing.ShouldBeTrue();

        await session.ApproveCommand.ExecuteAsync(null);
        session.Notice.ShouldBeNull();

        // Displayed and persisted agree.
        SessionAggregate persisted = await app.LoadAsync(id);
        persisted.Steps.Single(s => s.Step == StepKind.Enhancement).State.ShouldBe(StepState.Approved);
        persisted.Reviews.Single(r => r.Step == StepKind.Enhancement).IsApproved.ShouldBeTrue();
        persisted.ToSnapshot().CurrentStep!.Step.ShouldBe(StepKind.BackgroundRemoval);
    }

    // -------------------------------------------------------------------------------------
    // Smoke B — reject/retry: Run -> Reject -> Retry -> Run -> Approve, on a new attempt
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task Smoke_B_reject_retry_and_approve_background_removal_on_a_fresh_attempt()
    {
        using SmokeApplication app = await SmokeApplication.StartAsync();

        SessionViewModel session = await app.ImportAndChooseAsync("smoke-b.png", WorkflowType.PrepareAsset);
        SessionId id = app.OpenSessionId;

        await session.ConfirmOriginalCommand.ExecuteAsync(null);
        await session.SkipCommand.ExecuteAsync(null);        // past Enhancement, onto BackgroundRemoval

        (await app.LoadAsync(id)).ToSnapshot().CurrentStep!.Step.ShouldBe(StepKind.BackgroundRemoval);

        await app.AuthoriseBackgroundRemovalAsync();

        await session.RunStepCommand.ExecuteAsync(null);
        session.IsReviewRequired.ShouldBeTrue();

        await session.RejectCommand.ExecuteAsync(null);
        (await app.LoadAsync(id)).Steps.Single(s => s.Step == StepKind.BackgroundRemoval)
            .State.ShouldBe(StepState.RetryRequired);

        AttemptId firstAttempt = (await app.LoadAsync(id))
            .Attempts.Single(a => a.Step == StepKind.BackgroundRemoval).Id;

        await session.RetryCommand.ExecuteAsync(null);
        (await app.LoadAsync(id)).Steps.Single(s => s.Step == StepKind.BackgroundRemoval)
            .State.ShouldBe(StepState.Waiting);

        await session.RunStepCommand.ExecuteAsync(null);
        await session.ApproveCommand.ExecuteAsync(null);
        session.Notice.ShouldBeNull();

        SessionAggregate persisted = await app.LoadAsync(id);
        persisted.Steps.Single(s => s.Step == StepKind.BackgroundRemoval).State.ShouldBe(StepState.Approved);

        // A genuinely new attempt, and both decisions survive as history.
        List<ProcessingAttempt> attempts =
            [.. persisted.Attempts.Where(a => a.Step == StepKind.BackgroundRemoval)];
        attempts.Count.ShouldBe(2);
        attempts.ShouldContain(a => a.Id != firstAttempt);

        persisted.Reviews.Count(r => r.Step == StepKind.BackgroundRemoval).ShouldBe(2);
    }

    // -------------------------------------------------------------------------------------
    // Smoke C — skip: a fresh session, both skippable steps skipped, no Revisions created
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task Smoke_C_skipping_both_skippable_steps_creates_no_revisions()
    {
        using SmokeApplication app = await SmokeApplication.StartAsync();

        SessionViewModel session = await app.ImportAndChooseAsync("smoke-c.png", WorkflowType.PrepareAsset);
        SessionId id = app.OpenSessionId;

        await session.ConfirmOriginalCommand.ExecuteAsync(null);
        await session.SkipCommand.ExecuteAsync(null);        // Enhancement
        await session.SkipCommand.ExecuteAsync(null);        // BackgroundRemoval
        session.Notice.ShouldBeNull();

        SessionAggregate persisted = await app.LoadAsync(id);
        persisted.Steps.Single(s => s.Step == StepKind.Enhancement).State.ShouldBe(StepState.Skipped);
        persisted.Steps.Single(s => s.Step == StepKind.BackgroundRemoval).State.ShouldBe(StepState.Skipped);

        // No Revision for either skipped step: the only one is the imported original, and it
        // is what Trim will consume (MVP design §7.2).
        persisted.Revisions.Count.ShouldBe(1);
        persisted.Revisions.Single().IsRoot.ShouldBeTrue();
        persisted.Attempts.Count(a => a.Step is StepKind.Enhancement or StepKind.BackgroundRemoval).ShouldBe(0);

        persisted.ToSnapshot().CurrentStep!.Step.ShouldBe(StepKind.Trim);
    }

    // -------------------------------------------------------------------------------------
    // Smoke D — hand-off: automation ends and the run actions disappear
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task Smoke_D_handing_off_ends_automation_and_withdraws_the_run_actions()
    {
        using SmokeApplication app = await SmokeApplication.StartAsync();

        SessionViewModel session = await app.ImportAndChooseAsync("smoke-d.png", WorkflowType.PrepareAsset);
        SessionId id = app.OpenSessionId;

        await session.ConfirmOriginalCommand.ExecuteAsync(null);
        await session.RunStepCommand.ExecuteAsync(null);
        session.IsReviewRequired.ShouldBeTrue();

        session.CanHandOff.ShouldBeTrue();
        await session.HandOffCommand.ExecuteAsync(null);
        session.Notice.ShouldBeNull();

        session.IsHandedOff.ShouldBeTrue();
        session.CanRunStep.ShouldBeFalse();
        session.CanApprove.ShouldBeFalse();
        // SCRUM-11092 / SCRUM-11112: reject the retained offer before manual replacement.
        session.CanReject.ShouldBeTrue();
        session.CanSubmitManualResult.ShouldBeFalse();
        session.CanRetry.ShouldBeFalse();
        session.CanSkip.ShouldBeFalse();

        SessionAggregate persisted = await app.LoadAsync(id);
        persisted.Session.State.ShouldBe(SessionState.HandedOff);

        AutomationLockState automationLock =
            (await app.Repository.GetAutomationLockAsync(CancellationToken.None)).Value;
        automationLock.IsHeld.ShouldBeFalse();

        // Home still lists it, and still offers a way in — hand-off ended automation, not the
        // record (MVP design §6.5).
        HomeViewModel home = app.Services.GetRequiredService<HomeViewModel>();
        await home.RefreshCommand.ExecuteAsync(null);
        RecentSessionRow row = home.RecentSessions.Single(r => r.Id == id);
        row.CanContinueProcessing.ShouldBeFalse();
        row.CanAbandon.ShouldBeTrue();
    }

    // -------------------------------------------------------------------------------------
    // Part 3C3B Smoke A — Generate Print TIFF, end to end (§22)
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// GENERATE_PRINT_TIFF from import to Completed, on synthetic files only
    /// (Epic 11100 Part 3C3B §22, Smoke A).
    /// </summary>
    /// <remarks>
    /// The journey a production operator actually walks, through the real composed graph:
    /// confirm the original, enter a size, classify the design for W1, generate, review the
    /// TIFF, complete. Every step goes through the screen's own controls, so this is a smoke
    /// pass over the wiring rather than a second copy of the service tests.
    /// </remarks>
    [Fact]
    public async Task Production_smoke_A_generate_print_tiff_from_import_to_completed()
    {
        using SmokeApplication app = await SmokeApplication.StartAsync();

        SessionViewModel session =
            await app.ImportAndChooseAsync("smoke-tiff-a.png", WorkflowType.GeneratePrintTiff);
        SessionId id = app.OpenSessionId;

        await session.ConfirmOriginalCommand.ExecuteAsync(null);
        (await app.LoadAsync(id)).ToSnapshot().CurrentStep!.Step.ShouldBe(StepKind.PrintDimensions);

        session.CanSetMaximumBounds.ShouldBeTrue();
        session.SelectedWhiteUnderbaseChoice.ShouldBeNull();     // no default, ever

        await ConfirmSizeAndBranchAsync(session, widthMm: 200, WhiteUnderbaseBranch.W1_1px);

        session.CanRunStep.ShouldBeTrue();
        await session.RunStepCommand.ExecuteAsync(null);

        session.IsReviewRequired.ShouldBeTrue();
        session.IsFakeTiffOutput.ShouldBeTrue();                 // the synthetic-TIFF warning
        session.CanComplete.ShouldBeFalse();

        await session.ApproveCommand.ExecuteAsync(null);
        session.CanComplete.ShouldBeTrue();

        await session.CompleteCommand.ExecuteAsync(null);
        session.Notice.ShouldBeNull();

        SessionAggregate persisted = await app.LoadAsync(id);
        persisted.Session.State.ShouldBe(SessionState.Completed);

        PrintOutput output = persisted.Outputs.ShouldHaveSingleItem();
        output.ReviewState.ShouldBe(ReviewState.Approved);
        output.Dimensions.WidthMm.ShouldBe(200);
        output.Branch.ShouldBe(WhiteUnderbaseBranch.W1_1px);
        File.Exists(app.Workspace.ResolveAbsolute(output.File)).ShouldBeTrue();

        session.Outputs.ShouldHaveSingleItem();
    }

    // -------------------------------------------------------------------------------------
    // Part 3C3B Smoke B — another size from a completed session (§22)
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task Production_smoke_B_adding_another_size_leaves_the_first_output_in_place()
    {
        using SmokeApplication app = await SmokeApplication.StartAsync();

        SessionViewModel session =
            await app.ImportAndChooseAsync("smoke-tiff-b.png", WorkflowType.GeneratePrintTiff);
        SessionId id = app.OpenSessionId;

        await session.ConfirmOriginalCommand.ExecuteAsync(null);
        await ConfirmSizeAndBranchAsync(session, widthMm: 200, WhiteUnderbaseBranch.W1_1px);
        await session.RunStepCommand.ExecuteAsync(null);
        await session.ApproveCommand.ExecuteAsync(null);
        await session.CompleteCommand.ExecuteAsync(null);
        session.Notice.ShouldBeNull();

        session.CanAddAnotherSize.ShouldBeTrue();
        await session.AddAnotherSizeCommand.ExecuteAsync(null);

        // Reopened with both decisions cleared: the second output makes its own.
        session.CanSetMaximumBounds.ShouldBeTrue();
        session.SelectedWhiteUnderbaseChoice.ShouldBeNull();
        (await app.LoadAsync(id)).Session.WhiteUnderbaseBranch.ShouldBeNull();

        await ConfirmSizeAndBranchAsync(session, widthMm: 150, WhiteUnderbaseBranch.W1_2px);
        await session.RunStepCommand.ExecuteAsync(null);
        await session.ApproveCommand.ExecuteAsync(null);
        session.Notice.ShouldBeNull();

        SessionAggregate persisted = await app.LoadAsync(id);
        persisted.Outputs.Count.ShouldBe(2);
        persisted.Outputs.ShouldAllBe(o => o.IsValid && o.ReviewState == ReviewState.Approved);

        // Both files are still on disk, and both are on screen.
        foreach (PrintOutput output in persisted.Outputs)
        {
            File.Exists(app.Workspace.ResolveAbsolute(output.File)).ShouldBeTrue();
        }

        session.Outputs.Count.ShouldBe(2);
    }

    // -------------------------------------------------------------------------------------
    // Part 3C3B Smoke C — the customer-design production tail (§22)
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task Production_smoke_C_customer_design_reaches_a_completed_tiff_from_an_approved_trim()
    {
        using SmokeApplication app = await SmokeApplication.StartAsync();

        SessionViewModel session =
            await app.ImportAndChooseAsync("smoke-tiff-c.png", WorkflowType.PrepareCustomerDesign);
        SessionId id = app.OpenSessionId;

        await session.ConfirmOriginalCommand.ExecuteAsync(null);
        await session.RunStepCommand.ExecuteAsync(null);          // Enhancement
        await session.ApproveCommand.ExecuteAsync(null);
        await app.AuthoriseBackgroundRemovalAsync();
        await session.RunStepCommand.ExecuteAsync(null);          // BackgroundRemoval
        await session.ApproveCommand.ExecuteAsync(null);
        await session.RunStepCommand.ExecuteAsync(null);          // Trim
        await session.ApproveCommand.ExecuteAsync(null);
        session.Notice.ShouldBeNull();

        SessionAggregate atDimensions = await app.LoadAsync(id);
        atDimensions.ToSnapshot().CurrentStep!.Step.ShouldBe(StepKind.PrintDimensions);
        atDimensions.Steps.Single(s => s.Step == StepKind.Trim).State.ShouldBe(StepState.Approved);

        await ConfirmSizeAndBranchAsync(session, widthMm: 240, WhiteUnderbaseBranch.W1_0px);
        await session.RunStepCommand.ExecuteAsync(null);
        await session.ApproveCommand.ExecuteAsync(null);
        await session.CompleteCommand.ExecuteAsync(null);
        session.Notice.ShouldBeNull();

        SessionAggregate persisted = await app.LoadAsync(id);
        persisted.Session.State.ShouldBe(SessionState.Completed);

        PrintOutput output = persisted.Outputs.ShouldHaveSingleItem();
        output.ReviewState.ShouldBe(ReviewState.Approved);
        output.Branch.ShouldBe(WhiteUnderbaseBranch.W1_0px);
        output.Dimensions.WidthMm.ShouldBe(240);
    }

    /// <summary>Enters a size and classifies the design, through the screen's own controls.</summary>
    private static async Task ConfirmSizeAndBranchAsync(
        SessionViewModel session, double widthMm, WhiteUnderbaseBranch branch)
    {
        session.WidthMmText = widthMm.ToString(CultureInfo.CurrentCulture);
        session.HeightMmText = 150d.ToString(CultureInfo.CurrentCulture);
        await session.SetMaximumBoundsCommand.ExecuteAsync(null);
        session.Notice.ShouldBeNull();

        session.SelectedWhiteUnderbaseChoice =
            session.WhiteUnderbaseChoices.Single(choice => choice.Branch == branch);
        await session.SelectWhiteUnderbaseCommand.ExecuteAsync(null);
        session.Notice.ShouldBeNull();
    }

    // -------------------------------------------------------------------------------------

    /// <summary>
    /// A started application: the real startup sequence, the real container, the real
    /// navigation service, and a scripted file dialog.
    /// </summary>
    // -------------------------------------------------------------------------------------
    // Smoke E–G — image review (Epic 11200 Part C1 §27)
    // -------------------------------------------------------------------------------------
    //
    // §27 asks for an interactive pass over the three review screens. Interactive WPF is not
    // available here — there is no desktop to click on — so these do the honest alternative
    // §27 itself names: the real composed graph, driven through the real view models, rendered
    // for real at 1000×700, and then *inspected*. Each check below is one line of that manual
    // checklist turned into something a build can answer:
    //
    //   checkerboard visible      -> the pane's checkerboard brush is in the arranged tree
    //   Before/After labels       -> both headings are present, in that order
    //   trim shows a smaller canvas -> the two rendered bitmaps' pixel sizes are 12x10 and 5x5
    //   zoom works                -> the transform actually applied changes with the buttons
    //   no clipping at 1000x700   -> the screen's DesiredSize fits the viewport it was given
    //   zh-CN fits                -> the same, with the Chinese resources loaded
    //
    // What is genuinely not covered, and is stated rather than implied: nobody has looked at
    // the result. Colour, spacing and legibility remain a human judgement.

    [Fact]
    public async Task Smoke_E_enhancement_review_shows_before_and_after_over_a_checkerboard()
    {
        using SmokeApplication app = await SmokeApplication.StartAsync();

        SessionViewModel session = await app.ImportAndChooseAsync("smoke-e.png", WorkflowType.PrepareAsset);
        await session.ConfirmOriginalCommand.ExecuteAsync(null);
        await session.RunStepCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;

        session.IsReviewRequired.ShouldBeTrue();
        AssertComparisonRenders(session);
    }

    [Fact]
    public async Task Smoke_F_background_removal_review_shows_before_and_after()
    {
        using SmokeApplication app = await SmokeApplication.StartAsync();

        SessionViewModel session = await app.ImportAndChooseAsync("smoke-f.png", WorkflowType.PrepareAsset);
        await session.ConfirmOriginalCommand.ExecuteAsync(null);
        await session.RunStepCommand.ExecuteAsync(null);          // Enhancement
        await session.ApproveCommand.ExecuteAsync(null);
        await app.AuthoriseBackgroundRemovalAsync();
        await session.RunStepCommand.ExecuteAsync(null);          // BackgroundRemoval
        await session.PreviewsLoaded;

        session.Notice.ShouldBeNull();
        session.IsReviewRequired.ShouldBeTrue();
        AssertComparisonRenders(session);
    }

    /// <summary>
    /// The flow the slice exists for: a transparent image, a deterministic trim, and a review
    /// in which the cropped canvas is visibly smaller (§16, §27).
    /// </summary>
    [Fact]
    public async Task Smoke_G_trim_review_shows_the_cropped_canvas_beside_the_uncropped_one()
    {
        using SmokeApplication app = await SmokeApplication.StartAsync();

        // 12×10 with a 5×5 opaque block; everything else fully transparent.
        SessionViewModel session = await app.ImportAndChooseAsync(
            "smoke-g.png",
            WorkflowType.PrepareAsset,
            SyntheticImages.PngWithAlpha(12, 10, (x, y) => x is >= 3 and <= 7 && y is >= 2 and <= 6 ? (byte)255 : (byte)0));

        await session.ConfirmOriginalCommand.ExecuteAsync(null);
        await session.RunStepCommand.ExecuteAsync(null);          // Enhancement
        await session.ApproveCommand.ExecuteAsync(null);
        await app.AuthoriseBackgroundRemovalAsync();
        await session.RunStepCommand.ExecuteAsync(null);          // BackgroundRemoval
        await session.ApproveCommand.ExecuteAsync(null);
        await session.RunStepCommand.ExecuteAsync(null);          // Trim
        await session.PreviewsLoaded;

        session.Notice.ShouldBeNull();
        session.IsReviewRequired.ShouldBeTrue();

        ReviewScreenFacts rendered = AssertComparisonRenders(session);

        // The canvas really did get smaller, measured on the bitmaps that were drawn.
        rendered.BitmapSizes.ShouldBe([(12, 10), (5, 5)]);

        // And they really are transparent, which is what the checkerboard is behind.
        rendered.BitmapsCarryAlpha.ShouldAllBe(carries => carries);
    }

    /// <summary>
    /// The same review screen with the Chinese resources loaded (§20, §27).
    /// </summary>
    /// <remarks>
    /// "The zh-CN strings fit reasonably" is not something a build can judge, and this does not
    /// claim to. What it does claim is the part that would actually break a layout: with the
    /// longer Chinese sentences in place the screen still asks for no more room than the window
    /// it is given, and every binding still resolves.
    /// </remarks>
    [Fact]
    public async Task Smoke_H_the_review_screen_fits_the_window_with_the_Chinese_resources()
    {
        CultureInfo previousUi = CultureInfo.CurrentUICulture;
        CultureInfo previous = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo chinese = CultureInfo.GetCultureInfo("zh-CN");
            CultureInfo.CurrentUICulture = chinese;
            CultureInfo.CurrentCulture = chinese;

            using SmokeApplication app = await SmokeApplication.StartAsync();

            SessionViewModel session = await app.ImportAndChooseAsync("smoke-h.png", WorkflowType.PrepareAsset);
            await session.ConfirmOriginalCommand.ExecuteAsync(null);
            await session.RunStepCommand.ExecuteAsync(null);
            await session.PreviewsLoaded;

            // The Chinese satellite really is what the screen is showing.
            session.BeforeLabel.ShouldBe("处理前");
            session.AfterLabel.ShouldBe("处理后");
            session.ManualCropNotice.ShouldNotBe("Session_ManualCropRequiredNotice");

            AssertComparisonRenders(session);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousUi;
            CultureInfo.CurrentCulture = previous;
        }
    }

    // -------------------------------------------------------------------------------------
    // Smoke I — Epic 11200 Part C2 §35: the manual-crop journey, through the real graph
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// A no-alpha photo through the whole manual-crop journey: refuse, draw, apply, review,
    /// approve, export, complete (Part C2 §35).
    /// </summary>
    /// <remarks>
    /// <b>No human looked at this.</b> §35 asks for a visual pass on an interactive desktop, and
    /// this build has none, so what stands in for it is stated plainly rather than implied: the
    /// real composed graph from <see cref="ApplicationStartup"/> — including the real
    /// <c>WicManualCropProcessor</c> resolved from the container — walked from Home to
    /// completion, with each state measured and arranged for real and failing on any binding
    /// error. Nobody has confirmed by eye that the rectangle sits over the artwork.
    /// <para>
    /// What <i>is</i> checked automatically, and is the closest available answer to "does the
    /// selection align with the image", is the geometry: the crop surface is arranged at a real
    /// size and the drag is mapped through the same <see cref="CropSurfaceLayout"/> the view
    /// uses, at both fit and 200%, and must land on the same source rectangle both times.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Smoke_I_a_no_alpha_photo_is_cropped_by_hand_and_completes()
    {
        using SmokeApplication app = await SmokeApplication.StartAsync();

        // 12×10, opaque, no alpha channel at all — the case automatic trimming must refuse.
        SessionViewModel session = await app.ImportAndChooseAsync(
            "smoke-i.png",
            WorkflowType.PrepareAsset,
            SyntheticImages.OpaqueRgbPng(12, 10, (x, y) => ((byte)(x * 20), (byte)(y * 25), (byte)0x60)));

        await session.ConfirmOriginalCommand.ExecuteAsync(null);
        await session.SkipCommand.ExecuteAsync(null);
        await session.SkipCommand.ExecuteAsync(null);

        // The automatic trim refuses, and the screen offers the crop rather than a dead end.
        await session.RunStepCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;

        session.IsManualCropRequired.ShouldBeTrue();
        session.CanManualCrop.ShouldBeTrue();

        TrimBounds expected = TrimBounds.FromEdges(3, 2, 9, 7);

        // The crop surface is arranged for real on a second screen over the same session — the
        // one that is rendered is never driven afterwards, because a rendered ItemsControl binds
        // its collection to the render thread's dispatcher for good. Its measured geometry is
        // what the drag is mapped through: at fit and again at 200%, which must agree.
        CropSurfaceLayout fitted;
        using (SessionScreen probe = await app.OpenSecondScreenAsync())
        {
            probe.Model.BeginManualCropCommand.Execute(null);
            probe.Model.CropPane.ShouldNotBeNull();

            fitted = ArrangeCropSurface(probe.Model);
            fitted.IsUsable.ShouldBeTrue();
            DragOnto(probe.Model, fitted, expected).ShouldBe(expected);

            probe.Model.ZoomInCommand.Execute(null);
            probe.Model.ZoomInCommand.Execute(null);
            probe.Model.ZoomInCommand.Execute(null);
            probe.Model.IsFitToViewport.ShouldBeFalse();

            CropSurfaceLayout magnified = ArrangeCropSurface(probe.Model);
            magnified.DisplayScale.ShouldBe(probe.Model.ZoomScale, 1e-9);
            DragOnto(probe.Model, magnified, expected).ShouldBe(expected);
        }

        // The operator draws the same rectangle on the live screen and applies it.
        session.BeginManualCropCommand.Execute(null);
        DragOnto(session, fitted, expected).ShouldBe(expected);

        await session.ApplyManualCropCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;

        session.Notice.ShouldBeNull();
        session.IsCropping.ShouldBeFalse();
        session.IsReviewRequired.ShouldBeTrue();

        // And the review that follows renders as an ordinary Before/After pair.
        using (SessionScreen review = await app.OpenSecondScreenAsync())
        {
            ReviewScreenFacts rendered = AssertComparisonRenders(review.Model);
            rendered.BitmapSizes.ShouldBe([(12, 10), (6, 5)]);
        }

        // Approve, export, complete — nothing about the rest of the workflow is special.
        await session.ApproveCommand.ExecuteAsync(null);
        await session.RunStepCommand.ExecuteAsync(null);
        await session.CompleteCommand.ExecuteAsync(null);

        session.Notice.ShouldBeNull();
        session.IsReadOnly.ShouldBeTrue();

        SessionAggregate aggregate = await app.LoadAsync(app.OpenSessionId);
        aggregate.Session.State.ShouldBe(SessionState.Completed);

        Revision manual = aggregate.Revisions.Single(r => r.Operation == OperationKind.ManualImport);
        manual.Facts.PixelWidth.ShouldBe(expected.Width);
        manual.Facts.PixelHeight.ShouldBe(expected.Height);

        // The refusal and the crop are both in the history, produced by the two named processors.
        aggregate.Attempts.ShouldContain(a =>
            a.AdapterId == "internal-alpha-trim-v1" && a.Status == AttemptStatus.Failed);
        aggregate.Attempts.ShouldContain(a =>
            a.AdapterId == "internal-manual-crop-v1" && a.OutputRevisionId == manual.Id);

        // The approved PNG on disk is the cropped canvas.
        Revision exported = aggregate.Revisions.Single(r => r.Operation == OperationKind.PromoteApproved);
        File.Exists(app.Workspace.ResolveAbsolute(exported.File)).ShouldBeTrue();
        exported.Facts.Sha256.ShouldBe(manual.Facts.Sha256);
    }

    // -------------------------------------------------------------------------------------
    // Smoke J — Epic 11200 Part C3 §26: trim with a margin, review, return, re-trim
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// The C3 journey through the real graph: margin, review, return upstream, run again at a
    /// different margin (§26).
    /// </summary>
    /// <remarks>
    /// <b>No human looked at this.</b> §26 asks for a visual pass on an interactive desktop and
    /// this build has none, so what stands in for it is stated plainly rather than implied: the
    /// real composed graph from <see cref="ApplicationStartup"/> — real workspace, real
    /// database, real deterministic trim — driven from Home through to a second trim, with each
    /// review state measured and arranged for real and failing on any binding error. Nobody has
    /// confirmed by eye that the margin looks right around the artwork.
    /// <para>
    /// What <i>is</i> answered automatically is the part a person would be checking for: the two
    /// runs really produce different canvases, in the sizes the margins predict, measured on the
    /// bitmaps that were drawn — and both attempts remain attributable to their own settings
    /// after the return.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Smoke_J_a_trim_with_a_margin_is_reviewed_returned_to_and_run_again()
    {
        using SmokeApplication app = await SmokeApplication.StartAsync();

        // 12×10 with a 5×5 opaque block at [3,2 → 8,7); everything else fully transparent.
        SessionViewModel session = await app.ImportAndChooseAsync(
            "smoke-j.png",
            WorkflowType.PrepareAsset,
            SyntheticImages.PngWithAlpha(12, 10, (x, y) => x is >= 3 and <= 7 && y is >= 2 and <= 6 ? (byte)255 : (byte)0));

        await session.ConfirmOriginalCommand.ExecuteAsync(null);
        await session.SkipCommand.ExecuteAsync(null);
        await session.SkipCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;

        // Run A: a uniform 2 px margin, chosen on the screen and applied through the service.
        session.CanSetTrimParameters.ShouldBeTrue();
        session.SelectedTrimMode = session.TrimModes.Single(m => m.Mode == TrimMode.UniformMargin);
        session.UniformMarginText = "2";
        await session.ApplyTrimMarginCommand.ExecuteAsync(null);
        session.Notice.ShouldBeNull();

        await session.RunStepCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;

        session.IsReviewRequired.ShouldBeTrue();
        session.HasTrimParameters.ShouldBeTrue();

        using (SessionScreen review = await app.OpenSecondScreenAsync())
        {
            ReviewScreenFacts rendered = AssertComparisonRenders(review.Model);

            // 5×5 of content plus 2 px on every edge, all of which fit inside the 12×10 canvas.
            rendered.BitmapSizes.ShouldBe([(12, 10), (9, 9)]);
        }

        await session.ApproveCommand.ExecuteAsync(null);
        await session.RunStepCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;
        session.Notice.ShouldBeNull();

        // Return upstream to Trim, through the confirmation the operator would read.
        session.CanReturnToStep.ShouldBeTrue();
        session.SelectedReturnTarget = session.ReturnTargets.Single(t => t.Step == StepKind.Trim);
        session.BeginReturnCommand.Execute(null);
        session.IsConfirmingReturn.ShouldBeTrue();

        await session.ConfirmReturnCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;

        session.Notice.ShouldBeNull();
        session.CurrentStep.ShouldBe(session.Steps.Single(s => s.IsCurrent).Name);

        // Run B: a different margin entirely, on the reopened step.
        session.CanSetTrimParameters.ShouldBeTrue();
        session.SelectedTrimMode = session.TrimModes.Single(m => m.Mode == TrimMode.EdgeSpecificMargin);
        session.TopMarginText = "1";
        session.RightMarginText = "3";
        session.BottomMarginText = "2";
        session.LeftMarginText = "3";
        await session.ApplyTrimMarginCommand.ExecuteAsync(null);
        session.Notice.ShouldBeNull();

        await session.RunStepCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;

        session.IsReviewRequired.ShouldBeTrue();

        using (SessionScreen review = await app.OpenSecondScreenAsync())
        {
            ReviewScreenFacts rendered = AssertComparisonRenders(review.Model);

            // Width = 5 + 3 + 3 = 11; Height = 5 + 1 + 2 = 8. Visibly a different canvas from A.
            rendered.BitmapSizes.ShouldBe([(12, 10), (11, 8)]);
        }

        // Both runs survive, each attributable to its own settings (§15, §21).
        SessionAggregate aggregate = await app.LoadAsync(app.OpenSessionId);
        List<ProcessingAttempt> trims = [.. aggregate.Attempts.Where(a => a.Step == StepKind.Trim)];
        trims.Count.ShouldBe(2);
        trims.Select(a => a.TrimParameters).ShouldBe(
            [TrimMargin.Uniform(2), TrimMargin.PerEdge(top: 1, right: 3, bottom: 2, left: 3)],
            ignoreOrder: true);

        // Nothing was deleted by the return: both trimmed files are still on disk.
        foreach (Revision revision in aggregate.Revisions.Where(r => r.Operation == OperationKind.Trim))
        {
            File.Exists(app.Workspace.ResolveAbsolute(revision.File)).ShouldBeTrue();
        }
    }

    /// <summary>
    /// The other half of §26: a ManualCropRequired outcome offers no margin controls.
    /// </summary>
    /// <remarks>
    /// Through the real graph rather than the view-model suite, because the question is what an
    /// operator is <i>shown</i>: the crop tool appears and the margin panel is gone, so nothing
    /// on the screen suggests that adding pixels could rescue an image with no alpha to measure
    /// from. Rendered for real, so the two panels' visibility is exercised rather than asserted
    /// only as booleans.
    /// </remarks>
    [Fact]
    public async Task Smoke_K_a_manual_crop_outcome_offers_no_margin_controls()
    {
        using SmokeApplication app = await SmokeApplication.StartAsync();

        SessionViewModel session = await app.ImportAndChooseAsync(
            "smoke-k.png",
            WorkflowType.PrepareAsset,
            SyntheticImages.OpaqueRgbPng(12, 10, (x, y) => ((byte)(x * 20), (byte)(y * 25), (byte)0x60)));

        await session.ConfirmOriginalCommand.ExecuteAsync(null);
        await session.SkipCommand.ExecuteAsync(null);
        await session.SkipCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;

        // Before the run, the automatic trim is what is about to happen, so the margin controls
        // are the right thing to offer.
        session.CanSetTrimParameters.ShouldBeTrue();
        session.CanManualCrop.ShouldBeFalse();

        await session.RunStepCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;

        // Afterwards the two surfaces have swapped.
        session.IsManualCropRequired.ShouldBeTrue();
        session.CanManualCrop.ShouldBeTrue();
        session.CanSetTrimParameters.ShouldBeFalse();

        using SessionScreen screen = await app.OpenSecondScreenAsync();
        screen.Model.CanSetTrimParameters.ShouldBeFalse();
        screen.Model.CanManualCrop.ShouldBeTrue();

        WpfRendering.RenderExpectingNoBindingErrors(
            () => new SessionScreenView { DataContext = screen.Model }, WpfRendering.ReviewViewport);
    }

    /// <summary>
    /// A throwaway session screen, opened on the session's current state purely to be rendered.
    /// </summary>
    /// <remarks>
    /// <c>using</c>-scoped so the "render it, then stop using it" rule is visible in the test
    /// rather than remembered. It exists because rendering costs the view model something
    /// permanent: an <c>ItemsControl</c> bound to <c>PreviewPanes</c> creates a
    /// <c>CollectionView</c> owned by the render thread's dispatcher, and that thread is gone by
    /// the time the next command would touch the collection. Only tests hit this — the
    /// application renders on the thread it drives from.
    /// </remarks>
    private sealed class SessionScreen : IDisposable
    {
        public SessionScreen(SessionViewModel model) => Model = model;

        public SessionViewModel Model { get; }

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// Arranges the session screen and reads the crop surface's real measured geometry.
    /// </summary>
    /// <remarks>
    /// The numbers come from the arranged tree — the overlay canvas the view's own pointer
    /// handlers measure against — rather than from figures a test chose. That is what makes the
    /// mapping assertions above statements about the screen rather than about arithmetic already
    /// covered by <c>CropSurfaceLayoutTests</c>.
    /// </remarks>
    private static CropSurfaceLayout ArrangeCropSurface(SessionViewModel session)
    {
        ArtefactPreviewPane pane = session.CropPane!;

        RenderResult<(double Width, double Height)> rendered = WpfRendering.RenderExpectingNoBindingErrors(
            () => new SessionScreenView { DataContext = session },
            WpfRendering.ReviewViewport,
            tree =>
            {
                Canvas overlay = tree.OfType<Canvas>().Single(c => c.Name == "CropOverlay");
                return (overlay.ActualWidth, overlay.ActualHeight);
            });

        return new CropSurfaceLayout(
            rendered.Facts.Width, rendered.Facts.Height,
            pane.PayloadPixelWidth, pane.PayloadPixelHeight,
            pane.SourcePixelWidth, pane.SourcePixelHeight,
            session.IsFitToViewport, session.ZoomScale);
    }

    /// <summary>
    /// Drags over the artwork <paramref name="target"/> covers and returns what was selected.
    /// </summary>
    /// <remarks>
    /// The drag is expressed by projecting the intended source rectangle onto the measured
    /// surface, which is what makes the two zoom levels comparable: the same artwork is dragged
    /// over in both, and the answer must be the same rectangle even though the screen
    /// coordinates are not.
    /// </remarks>
    private static TrimBounds DragOnto(
        SessionViewModel session, CropSurfaceLayout layout, TrimBounds target)
    {
        layout.TryToSurfaceRect(target, out double x, out double y, out double width, out double height)
            .ShouldBeTrue();

        session.TrySetCropSelection(layout, x, y, x + width, y + height).ShouldBeTrue();
        return session.CropSelection!.Value;
    }

    /// <summary>
    /// What one rendered review screen turned out to contain (Part C1 §27).
    /// </summary>
    /// <remarks>
    /// Plain values only. They are pulled out on the render thread — every WPF element belongs
    /// to the STA thread that built it — and asserted here, where a failure message is readable.
    /// </remarks>
    private sealed record ReviewScreenFacts(
        IReadOnlyList<string> Texts,
        int CheckerboardPanels,
        IReadOnlyList<(int Width, int Height)> BitmapSizes,
        IReadOnlyList<bool> BitmapsCarryAlpha,
        IReadOnlyList<double> AppliedScales);

    /// <summary>
    /// Renders the review screen at 1000×700 and checks the §27 list.
    /// </summary>
    /// <remarks>
    /// The zoom check is deliberately made on the <see cref="ScaleTransform"/> that was really
    /// applied to a rendered <see cref="Image"/>, not on the view model's own number: the view
    /// model's number is already asserted in <c>ImagePreviewControlTests</c>, and the open
    /// question here is whether the two <c>RelativeSource</c> bindings that carry it to the
    /// image actually connect.
    /// </remarks>
    private static ReviewScreenFacts AssertComparisonRenders(SessionViewModel session)
    {
        session.PreviewPanes.Count.ShouldBe(2);
        session.PreviewPanes[0].Heading.ShouldBe(session.BeforeLabel);
        session.PreviewPanes[1].Heading.ShouldBe(session.AfterLabel);

        RenderResult<ReviewScreenFacts> fitted = RenderReview(session);

        // Both labels reached the screen, in the order they were built.
        List<string> texts = [.. fitted.Facts.Texts];
        texts.ShouldContain(session.BeforeLabel);
        texts.ShouldContain(session.AfterLabel);
        texts.IndexOf(session.BeforeLabel).ShouldBeLessThan(texts.IndexOf(session.AfterLabel));

        // The checkerboard is behind both images, and both images were drawn.
        fitted.Facts.CheckerboardPanels.ShouldBe(2);
        fitted.Facts.BitmapSizes.Count.ShouldBe(2);

        // Nothing asks for more room than the window gives it.
        fitted.DesiredSize.Width.ShouldBeLessThanOrEqualTo(WpfRendering.ReviewViewport.Width);
        fitted.DesiredSize.Height.ShouldBeLessThanOrEqualTo(WpfRendering.ReviewViewport.Height);

        // Zoom reaches the image through the bindings, not only the view model.
        fitted.Facts.AppliedScales.ShouldAllBe(scale => scale == 1.0);

        session.ZoomInCommand.Execute(null);
        session.ZoomInCommand.Execute(null);

        RenderReview(session).Facts.AppliedScales.ShouldAllBe(scale => scale > 1.0);

        session.ResetZoomCommand.Execute(null);
        return fitted.Facts;
    }

    private static RenderResult<ReviewScreenFacts> RenderReview(SessionViewModel session) =>
        WpfRendering.RenderExpectingNoBindingErrors(
            () => new SessionScreenView { DataContext = session },
            WpfRendering.ReviewViewport,
            Inspect);

    private static ReviewScreenFacts Inspect(RenderedTree tree)
    {
        Brush checkerboard = (Brush)tree.Root.FindResource("TransparencyCheckerboard");

        BitmapSource[] bitmaps = [.. tree.OfType<Image>()
            .Select(image => image.Source)
            .OfType<BitmapSource>()];

        return new ReviewScreenFacts(
            [.. tree.OfType<TextBlock>().Select(block => block.Text)],

            // On screen, not merely present in the tree. Since Epic 11200 Part C2 the session
            // screen also carries a crop surface over the same checkerboard; it is collapsed
            // during a review, so counting it would answer a different question from the one
            // §27 asks, which is how many checkerboards the operator is looking at.
            tree.OfType<Grid>().Count(grid => ReferenceEquals(grid.Background, checkerboard) && IsShown(grid)),
            [.. bitmaps.Select(bitmap => (bitmap.PixelWidth, bitmap.PixelHeight))],
            [.. bitmaps.Select(bitmap => bitmap.Format == PixelFormats.Bgra32)],
            [.. tree.OfType<Image>()
                .Where(image => image.Source is not null)
                .Select(image => image.LayoutTransform)
                .OfType<ScaleTransform>()
                .Select(transform => transform.ScaleX)]);
    }

    /// <summary>
    /// Whether an element and every ancestor above it is <see cref="Visibility.Visible"/>.
    /// </summary>
    /// <remarks>
    /// <c>UIElement.IsVisible</c> would be the obvious answer, but it is false for everything
    /// here: it also requires a presentation source, and this harness measures and arranges
    /// without ever showing a window. Walking the parents asks the part that is actually about
    /// the screen's content, and walks both trees for the same reason
    /// <c>WpfRendering.Collect</c> does — a templated element's parent may exist in only one.
    /// </remarks>
    private static bool IsShown(DependencyObject element)
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
        (node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : null)
        ?? LogicalTreeHelper.GetParent(node);

    private sealed class SmokeApplication : IDisposable
    {
        private readonly TempApplication _layout;
        private readonly FakeSingleInstanceGuard _guard;
        private readonly StartupResult _startup;
        private readonly StubFilePicker _picker;

        private SmokeApplication(
            TempApplication layout, FakeSingleInstanceGuard guard, StartupResult startup, StubFilePicker picker)
        {
            _layout = layout;
            _guard = guard;
            _startup = startup;
            _picker = picker;
        }

        public ServiceProvider Services => _startup.Services!;

        public ISessionRepository Repository => Services.GetRequiredService<ISessionRepository>();

        /// <summary>
        /// The application's own workspace, for the one thing a smoke pass must check on
        /// disk: that the file a produced output names is really there.
        /// </summary>
        public IWorkspace Workspace => Services.GetRequiredService<IWorkspace>();

        /// <summary>The session the navigation service currently has a screen open for.</summary>
        public SessionId OpenSessionId { get; private set; }

        /// <summary>
        /// Records the reviewed-content authority for the artefact Background Removal is about to
        /// consume, through the application's own session service (Epic 11300 Part C2B1 §5).
        /// </summary>
        /// <remarks>
        /// Issued against the service rather than a screen control, because C2B1 deliberately
        /// ships no operator UI for it -- the service/read-model seam is the whole of what this
        /// slice exposes, and C2B2 owns the control that will call it (§21). The screen is not
        /// refreshed afterwards on purpose: RunStep reads the current step from what the screen
        /// already holds and the service reloads the decision from the database, so this proves
        /// the persisted authority is what authorises the run.
        /// </remarks>
        public Task AuthoriseBackgroundRemovalAsync() =>
            SessionServiceHarness.AuthoriseBackgroundRemovalAsync(
                Services.GetRequiredService<ISessionService>(), OpenSessionId);

        public static async Task<SmokeApplication> StartAsync()
        {
            TempApplication layout = new();
            FakeSingleInstanceGuard guard = new(SingleInstanceOutcome.Acquired);
            StubFilePicker picker = new();

            StartupResult startup = await new ApplicationStartup(
                    guard,
                    layout.ConfigurationFilePath,
                    services => services.AddSingleton<IFilePicker>(picker))
                .RunAsync(CancellationToken.None);

            startup.Status.CanShowShell.ShouldBeTrue(
                startup.Status.Failure?.ToString() ?? "startup refused to show the shell");

            return new SmokeApplication(layout, guard, startup, picker);
        }

        /// <summary>
        /// Walks Home to Workflow Selection to the session screen, exactly as an operator
        /// would, and returns the live session view model the navigation service resolved.
        /// </summary>
        public async Task<SessionViewModel> ImportAndChooseAsync(
            string fileName, WorkflowType workflow, byte[]? content = null)
        {
            INavigationService navigation = Services.GetRequiredService<INavigationService>();
            await navigation.GoHomeAsync(CancellationToken.None);

            HomeViewModel home = (HomeViewModel)navigation.Current!;
            _picker.Path = WriteSyntheticFile(fileName, content);
            await home.ChooseFileCommand.ExecuteAsync(null);
            home.Notice.ShouldBeNull();

            WorkflowSelectionViewModel selection = navigation.Current.ShouldBeOfType<WorkflowSelectionViewModel>();
            selection.CanSelect.ShouldBeTrue();
            await selection.SelectCommand.ExecuteAsync(
                selection.Workflows.Single(choice => choice.Type == workflow));
            selection.Notice.ShouldBeNull();

            SessionViewModel session = navigation.Current.ShouldBeOfType<SessionViewModel>();

            await home.RefreshCommand.ExecuteAsync(null);
            OpenSessionId = home.RecentSessions[0].Id;

            return session;
        }

        public async Task<SessionAggregate> LoadAsync(SessionId id) =>
            (await Repository.LoadAsync(id, CancellationToken.None)).Value!;

        /// <summary>
        /// A second session screen over the same composed services, showing the open session's
        /// current persisted state.
        /// </summary>
        /// <remarks>
        /// Resolved from the container rather than constructed, so it is the same view model the
        /// navigation service would hand an operator resuming this session — including the same
        /// <c>IArtefactPreviewService</c> and the same real crop processor behind it.
        /// </remarks>
        public async Task<SessionScreen> OpenSecondScreenAsync()
        {
            ISessionService sessions = Services.GetRequiredService<ISessionService>();
            SessionView current = (await sessions.LoadAsync(OpenSessionId, CancellationToken.None)).Value;

            SessionViewModel model = Services.GetRequiredService<SessionViewModel>();
            model.Open(current);
            await model.PreviewsLoaded;

            return new SessionScreen(model);
        }

        public void Dispose()
        {
            _startup.Dispose();
            _guard.Dispose();
            _layout.Dispose();
        }

        /// <summary>
        /// A synthetic PNG outside the workspace, standing in for the operator's own file.
        /// </summary>
        /// <remarks>
        /// Written under the OS temp directory and never committed: no customer or production
        /// file is involved in any smoke pass (§21, task §50).
        /// </remarks>
        private static string WriteSyntheticFile(string fileName, byte[]? content = null)
        {
            string directory = Path.Combine(Path.GetTempPath(), "PrintFlowTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);

            string path = Path.Combine(directory, fileName);
            File.WriteAllBytes(path, content ?? SyntheticImages.Png(8, 6, alpha: true));
            return path;
        }
    }
}
