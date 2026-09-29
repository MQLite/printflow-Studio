using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PrintFlow.App.Navigation;
using PrintFlow.App.Resources;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.App.Localisation;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Trimming;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Tests.Integration.Persistence;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Delivery;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

/// <summary>
/// The trim adjustment as the Session screen drives it (SCRUM-11147): the view model over the
/// real service in a GUID-owned temp workspace and database, a real final-save coordinator over
/// temp folders, a stub picker and a recording shell. Views are rendered off-screen only; no
/// window is shown and no input device is touched.
/// </summary>
[Collection(SqliteCollection.Name)]
public sealed class TrimAdjustmentUiTests
{
    /// <summary>The bordered 12×10 source's automatic result with a 1-px margin.</summary>
    private static readonly TrimBounds Automatic = TrimBounds.FromEdges(2, 1, 9, 8);

    /// <summary>A 12×10 surface over the unreduced 12×10 preview: one surface unit per source pixel.</summary>
    private static readonly CropSurfaceLayout Surface = new(12, 10, 12, 10, 12, 10, IsFitToViewport: true, ZoomScale: 1);

    [Fact]
    public async Task The_editor_draws_on_the_pre_trim_source_and_nothing_it_does_before_Use_this_trim_is_persisted()
    {
        using SessionServiceHarness h = new();
        (ISessionService service, SessionViewModel screen, SessionView review) = await OpenAsync(h, enhance: true);
        Snapshot before = await Snapshot.TakeAsync(h, review.Id);
        TrimAdjustmentView target = review.TrimAdjustment!;

        screen.CanAdjustTrim.ShouldBeTrue();
        screen.BeginTrimAdjustCommand.Execute(null);

        screen.IsAdjustingTrim.ShouldBeTrue();
        screen.CropPane.ShouldNotBeNull();
        screen.CropPane!.RevisionId.ShouldBe(target.PreTrimRevisionId, "the pre-trim source, not the After pane");
        screen.CropPane.RevisionId.ShouldNotBe(review.CurrentArtefact!.RevisionId);
        (screen.CropPane.SourcePixelWidth, screen.CropPane.SourcePixelHeight).ShouldBe((12, 10));
        screen.CropSelection.ShouldBe(Automatic);
        screen.ShowsManualCropControls.ShouldBeFalse("the margin controls belong to the fallback");
        screen.IsTrimDraftUnchanged.ShouldBeTrue();

        // Drag, refused drag, nudge, border snap, zoom, compare, restore: all transient.
        screen.TryMoveTrimHandle(Surface, Automatic, CropHandle.Left, -1, 0).ShouldBeTrue();
        screen.CropSelection.ShouldBe(TrimBounds.FromEdges(1, 1, 9, 8));
        screen.TryMoveTrimHandle(Surface, screen.CropSelection!.Value, CropHandle.Left, -5, 0).ShouldBeFalse();
        screen.CropSelection.ShouldBe(TrimBounds.FromEdges(1, 1, 9, 8), "a refused position keeps the boundary it began with");
        screen.IsTrimAdjustInvalid.ShouldBeTrue();
        screen.TryNudgeTrimHandle(CropHandle.Bottom, 0, 1, bigStep: false, toBorder: false).ShouldBeTrue();
        screen.TryNudgeTrimHandle(CropHandle.Right, 1, 0, bigStep: false, toBorder: true).ShouldBeTrue();
        screen.CropSelection.ShouldBe(TrimBounds.FromEdges(1, 1, 12, 9));
        screen.IsTrimAdjustInvalid.ShouldBeFalse();
        screen.ZoomInCommand.Execute(null);
        screen.ShowTrimCompareViewCommand.Execute(null);
        screen.ShowsTrimCompare.ShouldBeTrue();
        screen.TrimProposedPayloadRect.ShouldBe(new PayloadRect(1, 1, 11, 8));
        screen.TrimAdjustCurrentPane!.RevisionId.ShouldBe(review.CurrentArtefact.RevisionId);
        screen.ShowTrimAdjustViewCommand.Execute(null);
        screen.RestoreAutomaticSuggestionCommand.Execute(null);
        screen.CropSelection.ShouldBe(Automatic, "exactly the stored automatic rectangle");

        (await Snapshot.TakeAsync(h, review.Id)).ShouldBe(before);

        // Cancel discards the draft and leaves the review exactly as it was.
        screen.CancelTrimAdjustCommand.Execute(null);
        screen.IsAdjustingTrim.ShouldBeFalse();
        screen.IsCropping.ShouldBeFalse();
        screen.CanApprove.ShouldBeTrue();
        (await Snapshot.TakeAsync(h, review.Id)).ShouldBe(before);
    }

    [Fact]
    public async Task While_adjusting_no_consequential_action_is_offered_or_accepted_and_the_review_identity_is_kept()
    {
        using SessionServiceHarness h = new();
        (_, SessionViewModel screen, SessionView review) = await OpenAsync(h);
        string? identity = screen.ReviewTargetIdentity;
        await SetFolderAsync(screen, FinalSaveFixtures.NewFolder("adjust-open"));
        screen.CanConfirmAndSave.ShouldBeTrue();

        screen.BeginTrimAdjustCommand.Execute(null);

        screen.CanApprove.ShouldBeFalse();
        screen.CanReject.ShouldBeFalse();
        screen.CanKeepOriginalExtent.ShouldBeFalse();
        screen.CanHandOff.ShouldBeFalse();
        screen.CanReturnToStep.ShouldBeFalse();
        screen.CanManualCrop.ShouldBeFalse();
        screen.CanApplyManualCrop.ShouldBeFalse("the old command is never sent from a review");
        screen.CanAdjustTrim.ShouldBeFalse();
        screen.CanConfirmAndSave.ShouldBeFalse();
        screen.IsSaveDraftEditable.ShouldBeFalse();
        screen.FinalSaveAlert.ShouldContain(Strings.FinalSave_TrimAdjustOpen);
        screen.ReviewTargetIdentity.ShouldBe(identity, "editing does not change what is under review");
        screen.NextStepText.ShouldBe(string.Format(CultureInfo.CurrentCulture, Strings.Session_NextTrimAdjust, screen.UseThisTrimLabel));
        screen.RecommendedCommand.ShouldBeSameAs(screen.UseThisTrimCommand);

        // Invoked anyway, nothing reaches the service.
        await screen.ApproveCommand.ExecuteAsync(null);
        await screen.RejectCommand.ExecuteAsync(null);
        await screen.KeepOriginalExtentCommand.ExecuteAsync(null);
        await screen.ConfirmAndSaveCommand.ExecuteAsync(null);
        await screen.ApplyManualCropCommand.ExecuteAsync(null);
        SessionAggregate after = (await h.Repository.LoadAsync(review.Id, CancellationToken.None)).Value!;
        after.Reviews.ShouldBeEmpty();
        after.Steps.Single(s => s.Step == StepKind.Trim).CurrentRevisionId.ShouldBe(review.CurrentArtefact!.RevisionId);
        screen.IsAdjustingTrim.ShouldBeTrue();
    }

    [Fact]
    public void Read_only_actions_on_a_delivered_copy_stay_while_everything_else_is_withheld()
    {
        using SessionServiceHarness h = new();
        SessionViewModel screen = new(h.CreateService(), h.Previews, h.TiffReviews, new RecordingNavigation());
        FinalSaveDisplay delivered = new()
        {
            Kind = FinalSaveKind.Saved, CanOpen = true, CanCheckSaved = true, CanCheckAgain = true, CanSaveAnotherCopy = true,
            CanSaveApproved = true, CanRetryRecorded = true, CanConfirmAndSave = true, IsEditable = true, Suggestion = "x.png",
        };
        screen.WhileAdjustingTrim(delivered).ShouldBe(delivered, "no change outside adjust mode");

        screen.IsAdjustingTrim = true;
        FinalSaveDisplay gated = screen.WhileAdjustingTrim(delivered);

        gated.CanOpen.ShouldBeTrue("opening an already delivered copy's folder is read-only");
        gated.CanCheckSaved.ShouldBeTrue("checking a delivered copy is read-only");
        (gated.CanConfirmAndSave, gated.CanSaveApproved, gated.CanRetryRecorded, gated.CanCheckAgain, gated.CanSaveAnotherCopy)
            .ShouldBe((false, false, false, false, false));
        gated.IsEditable.ShouldBeFalse();
        gated.Suggestion.ShouldBeNull();
        gated.Alert!.ShouldContain(Strings.FinalSave_TrimAdjustOpen);
    }

    [Fact]
    public async Task Use_this_trim_makes_a_new_result_that_waits_for_its_own_review_and_moves_every_exact_identity()
    {
        using SessionServiceHarness h = new();
        (_, SessionViewModel screen, SessionView review) = await OpenAsync(h);
        await SetFolderAsync(screen, FinalSaveFixtures.NewFolder("adjust-use"));
        screen.SaveFileNameText = "logo";
        string? reviewIdentity = screen.ReviewTargetIdentity;
        string? confirmIdentity = screen.ConfirmAndSaveIdentity;

        screen.BeginTrimAdjustCommand.Execute(null);
        string? useIdentity = screen.UseThisTrimIdentity;
        useIdentity.ShouldNotBeNull();
        screen.TryMoveTrimHandle(Surface, Automatic, CropHandle.TopLeft, -1, -1).ShouldBeTrue();
        screen.CanUseThisTrim.ShouldBeTrue();

        await screen.UseThisTrimCommand.ExecuteAsync(null);
        await screen.PreviewsLoaded;
        await screen.FinalSaveFactsLoaded;

        SessionAggregate after = (await h.Repository.LoadAsync(review.Id, CancellationToken.None)).Value!;
        Revision r2 = after.Revisions.Single(r => r.Id == after.Steps.Single(s => s.Step == StepKind.Trim).CurrentRevisionId);
        r2.Id.ShouldNotBe(review.CurrentArtefact!.RevisionId);
        after.Reviews.ShouldBeEmpty("Use this trim neither approves nor saves");

        screen.IsAdjustingTrim.ShouldBeFalse();
        screen.UseThisTrimIdentity.ShouldBeNull();
        screen.IsReviewRequired.ShouldBeTrue();
        screen.CanApprove.ShouldBeTrue();
        screen.Notice.ShouldBe(Strings.Session_TrimAdjustReady);
        screen.ReviewTargetIdentity.ShouldNotBe(reviewIdentity);
        screen.ReviewTargetIdentity!.ShouldContain(r2.Id.ToString());
        screen.ConfirmAndSaveIdentity.ShouldNotBe(confirmIdentity);
        screen.FinalSaveTargets.Single().Target.PendingReview!.RevisionId.ShouldBe(r2.Id);
        screen.SaveFileNameText.ShouldBe("logo", "only the operator's draft carries over");
        screen.PreviewPanes.Select(p => p.RevisionId!.Value).ShouldBe([review.TrimAdjustment!.PreTrimRevisionId, r2.Id]);

        // A fresh approval of the new result works through the exact entry.
        await screen.ApproveCommand.ExecuteAsync(null);
        (await h.Repository.LoadAsync(review.Id, CancellationToken.None)).Value!.Reviews
            .ShouldHaveSingleItem().SubjectId.ShouldBe(r2.Id.Value);
    }

    [Fact]
    public async Task A_screen_still_showing_a_replaced_result_cannot_approve_or_confirm_the_same_hash_successor()
    {
        using SessionServiceHarness h = new();
        (ISessionService service, SessionViewModel stale, SessionView review) = await OpenAsync(h);
        await SetFolderAsync(stale, FinalSaveFixtures.NewFolder("adjust-stale"));
        // Somewhere else, the same trim is adjusted twice to the same bounds: R3 has R2's bytes.
        SessionView second = await TrimAdjustmentStepTests.MustAsync(service.ExecuteAsync(review.Id,
            TrimAdjustmentStepTests.Adjust(review.TrimAdjustment!, Automatic), "tester", CancellationToken.None));
        stale.Open(second);
        await stale.PreviewsLoaded;
        await stale.FinalSaveFactsLoaded;
        SessionView third = await TrimAdjustmentStepTests.MustAsync(service.ExecuteAsync(review.Id,
            TrimAdjustmentStepTests.Adjust(second.TrimAdjustment!, Automatic), "tester", CancellationToken.None));
        third.CurrentArtefact!.Sha256.ShouldBe(second.CurrentArtefact!.Sha256);

        await stale.ApproveCommand.ExecuteAsync(null);
        stale.Notice.ShouldNotBeNullOrWhiteSpace();
        await stale.FinalSaveFactsLoaded;
        (await h.Repository.LoadAsync(review.Id, CancellationToken.None)).Value!.Reviews
            .ShouldBeEmpty("the plain Trim approval binds the displayed Revision, not only its hash");

        stale.Open(second);
        await stale.PreviewsLoaded;
        await stale.FinalSaveFactsLoaded;
        await stale.ConfirmAndSaveCommand.ExecuteAsync(null);
        (await h.Repository.LoadAsync(review.Id, CancellationToken.None)).Value!.Reviews.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_same_identity_refresh_keeps_the_draft_and_zoom_and_a_new_identity_discards_it()
    {
        using SessionServiceHarness h = new();
        (ISessionService service, SessionViewModel screen, SessionView review) = await OpenAsync(h);
        screen.BeginTrimAdjustCommand.Execute(null);
        screen.TryMoveTrimHandle(Surface, Automatic, CropHandle.Right, 2, 0).ShouldBeTrue();
        TrimBounds drawn = screen.CropSelection!.Value;
        screen.ZoomInCommand.Execute(null);
        double zoom = screen.ZoomScale;

        screen.Open((await service.LoadAsync(review.Id, CancellationToken.None)).Value);
        await screen.PreviewsLoaded;

        screen.IsAdjustingTrim.ShouldBeTrue();
        screen.CropSelection.ShouldBe(drawn);
        screen.ZoomScale.ShouldBe(zoom);
        screen.IsFitToViewport.ShouldBeFalse();
        screen.CropPane!.RevisionId.ShouldBe(review.TrimAdjustment!.PreTrimRevisionId, "redrawn on the newly published source");

        // The review moved on elsewhere: the draft belonged to a result no longer on offer.
        SessionView moved = await TrimAdjustmentStepTests.MustAsync(service.ExecuteAsync(review.Id,
            TrimAdjustmentStepTests.Adjust(review.TrimAdjustment!, Automatic), "tester", CancellationToken.None));
        screen.Open(moved);
        await screen.PreviewsLoaded;
        screen.IsAdjustingTrim.ShouldBeFalse();
        screen.CropSelection.ShouldBeNull();
    }

    [Theory]
    [InlineData("en", "zh-CN")]
    [InlineData("zh-CN", "en")]
    public async Task An_integrity_refusal_closes_the_editor_and_says_why(string language, string nextLanguage)
    {
        using OperatorCultureScope culture = new(language);
        using SessionServiceHarness h = new();
        LocalisationService localisation = new(h.Settings);
        localisation.Use(language == "en" ? OperatorLanguage.English : OperatorLanguage.SimplifiedChinese);
        (_, SessionViewModel screen, SessionView review) = await OpenAsync(h, enhance: true, localisation: localisation);
        SessionAggregate aggregate = (await h.Repository.LoadAsync(review.Id, CancellationToken.None)).Value!;
        Revision u = aggregate.Revisions.Single(r => r.Id == review.TrimAdjustment!.PreTrimRevisionId);
        screen.BeginTrimAdjustCommand.Execute(null);
        string path = h.FileWorkspace.ResolveAbsolute(u.File);
        byte[] bytes = File.ReadAllBytes(path);
        bytes[^1] ^= 0xFF;
        File.WriteAllBytes(path, bytes);

        await screen.UseThisTrimCommand.ExecuteAsync(null);

        screen.IsAdjustingTrim.ShouldBeFalse();
        screen.Notice!.ShouldContain(Strings.Session_TrimAdjustClosed);
        screen.Notice!.ShouldContain(Strings.Session_ActionFailedNext);
        screen.NoticeErrorCode.ShouldBe(nameof(FailureCode.RevisionIntegrityMismatch));
        screen.Notice!.ShouldNotContain(nameof(FailureCode.RevisionIntegrityMismatch));
        screen.CanAdjustTrim.ShouldBeFalse();
        screen.CanApprove.ShouldBeTrue("the result under review is unchanged and can still be reviewed");
        screen.CanRunStep.ShouldBeFalse();
        screen.CanRetry.ShouldBeFalse();
        screen.Notice!.ShouldNotContain(language == "en" ? "Run the step again" : "请重新执行该步骤");
        Snapshot beforeLanguage = await Snapshot.TakeAsync(h, review.Id);
        string oldAddition = Strings.Session_TrimAdjustClosed;
        var commands = (screen.CanAdjustTrim, screen.CanApprove, screen.CanRetry, screen.CanRunStep);

        localisation.Use(nextLanguage == "en" ? OperatorLanguage.English : OperatorLanguage.SimplifiedChinese);

        screen.Notice!.ShouldContain(Strings.Session_TrimAdjustClosed);
        screen.Notice!.ShouldNotContain(oldAddition);
        screen.Notice!.ShouldContain(Strings.Session_ActionFailedNext);
        screen.NoticeErrorCode.ShouldBe(nameof(FailureCode.RevisionIntegrityMismatch));
        (screen.CanAdjustTrim, screen.CanApprove, screen.CanRetry, screen.CanRunStep).ShouldBe(commands);
        (await Snapshot.TakeAsync(h, review.Id)).ShouldBe(beforeLanguage);
    }

    [Fact]
    public async Task Focus_goes_to_the_editor_heading_on_open_and_back_to_Adjust_or_the_status_panel_when_it_closes()
    {
        using SessionServiceHarness h = new();
        (_, SessionViewModel screen, SessionView review) = await OpenAsync(h);
        SessionView noLongerAdjustable = review with { TrimAdjustment = null };

        WpfRendering.OnStaThread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            SessionScreenView view = new() { DataContext = screen };
            Window window = new() { Content = view };
            try
            {
                // No Show, native window, activation or input: logical focus in the window scope,
                // placed by the view's own handlers exactly as the visible host would run them.
                FrameworkElement heading = (FrameworkElement)view.FindName("TrimAdjustHeadingText");
                Button begin = (Button)view.FindName("BeginTrimAdjustButton");
                Border status = (Border)view.FindName("OperatorStatusPanel");
                FocusManager.GetFocusScope(view).ShouldBeSameAs(window);

                screen.BeginTrimAdjustCommand.Execute(null);
                FocusManager.GetFocusedElement(window).ShouldBeSameAs(heading, "never Use this trim");

                screen.CancelTrimAdjustCommand.Execute(null);
                FocusManager.GetFocusedElement(window).ShouldBeSameAs(begin, "back to the control that opened the editor");

                screen.BeginTrimAdjustCommand.Execute(null);
                string? underReview = screen.ReviewTargetIdentity;
                screen.Open(noLongerAdjustable);
                Pump(screen.PreviewsLoaded);
                screen.IsAdjustingTrim.ShouldBeFalse();
                screen.ReviewTargetIdentity.ShouldBe(underReview, "the same result is still under review");
                FocusManager.GetFocusedElement(window).ShouldBeSameAs(status,
                    "the same review with nothing to adjust: the non-activating status panel");
            }
            finally
            {
                view.DataContext = null;
                window.Content = null;
                window.Close();
            }
        });
    }

    private static void Pump(Task task)
    {
        DispatcherFrame frame = new();
        task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
        Dispatcher.PushFrame(frame);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("zh-CN")]
    public async Task Asset_copy_never_mentions_print_size_and_customer_design_copy_says_it_comes_later(string language)
    {
        using OperatorCultureScope culture = new(language);
        using SessionServiceHarness h = new();
        (_, SessionViewModel asset, _) = await OpenAsync(h);
        (_, SessionViewModel design, _) = await OpenAsync(h, type: WorkflowType.PrepareCustomerDesign);
        asset.BeginTrimAdjustCommand.Execute(null);
        design.BeginTrimAdjustCommand.Execute(null);

        asset.TrimAdjustInstructions.ShouldBe(Strings.Session_TrimAdjustInstructions);
        design.TrimAdjustInstructions.ShouldBe($"{Strings.Session_TrimAdjustInstructions} {Strings.Session_TrimAdjustPrintSizeLater}");
        string[] assetCopy =
        [
            asset.TrimAdjustInstructions, asset.TrimAdjustHeading, asset.TrimAdjustKeyboardHint, asset.TrimAdjustKeptSummary,
            asset.TrimAdjustSuggestionSummary, asset.TrimAdjustInvalidNotice, asset.TrimAdjustUnchangedNotice,
            asset.UseThisTrimLabel, asset.RestoreSuggestionLabel, asset.CancelTrimAdjustLabel, asset.NextStepText,
        ];
        foreach (string text in assetCopy)
        {
            text.ShouldNotContain("print size", Case.Insensitive);
            text.ShouldNotContain("印刷尺寸");
            text.ShouldNotContain("打印尺寸");
            text.ShouldNotContain("TIFF");
        }
    }

    [Fact]
    public async Task The_unchanged_fallback_crop_never_shows_the_adjustment()
    {
        using SessionServiceHarness h = new();
        ISessionService service = h.CreateService();
        SessionId id = (await service.ImportAsync(WorkflowType.PrepareAsset, h.WriteOpaqueSourcePng(), "fallback", "tester",
            CancellationToken.None)).Value.Id;
        foreach (WorkflowCommand command in new WorkflowCommand[]
                 {
                     new WorkflowCommand.ConfirmOriginal(), new WorkflowCommand.Skip(StepKind.Enhancement),
                     new WorkflowCommand.Skip(StepKind.BackgroundRemoval),
                 })
            await TrimAdjustmentStepTests.MustAsync(service.ExecuteAsync(id, command, "tester", CancellationToken.None));
        await service.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.Trim), "tester", CancellationToken.None);
        SessionViewModel screen = new(service, h.Previews, h.TiffReviews, new RecordingNavigation());
        screen.Open((await service.LoadAsync(id, CancellationToken.None)).Value);
        await screen.PreviewsLoaded;

        screen.CanAdjustTrim.ShouldBeFalse();
        screen.BeginManualCropCommand.Execute(null);

        screen.IsCropping.ShouldBeTrue();
        screen.IsAdjustingTrim.ShouldBeFalse();
        screen.ShowsManualCropControls.ShouldBeTrue();
        screen.UseThisTrimIdentity.ShouldBeNull();
        screen.TryMoveTrimHandle(Surface, TrimBounds.FromEdges(1, 1, 5, 5), CropHandle.Left, 1, 0).ShouldBeFalse();
    }

    public static TheoryData<string, string, double, double> RenderCases => new()
    {
        { "adjust", "en", 1000, 700 }, { "adjust", "zh-CN", 1000, 700 },
        { "adjust", "en", 1920, 1040 }, { "adjust", "zh-CN", 1920, 1040 },
        { "border", "en", 1000, 700 }, { "border", "zh-CN", 1920, 1040 },
        { "small", "en", 1000, 700 }, { "small", "zh-CN", 1000, 700 },
        { "invalid", "zh-CN", 1000, 700 }, { "invalid", "en", 1920, 1040 },
        { "compare", "en", 1000, 700 }, { "compare", "zh-CN", 1920, 1040 },
        { "design", "en", 1000, 700 }, { "design", "zh-CN", 1000, 700 },
        { "tiny", "en", 1000, 700 }, { "tiny", "zh-CN", 1920, 1040 },
    };

    [Theory]
    [MemberData(nameof(RenderCases))]
    public async Task Rendered_adjustment_keeps_handles_whole_controls_reachable_and_text_wrapped(
        string state, string language, double width, double height)
    {
        using OperatorCultureScope culture = new(language);
        using SessionServiceHarness h = new();
        (_, SessionViewModel screen, _) = await OpenAsync(h,
            source: state is "border" or "tiny" ? h.Workspace.CreateSourceFile("full.png", SyntheticImages.PngWithAlpha(12, 10, (_, _) => 255)) : null,
            type: state == "design" ? WorkflowType.PrepareCustomerDesign : WorkflowType.PrepareAsset);
        screen.BeginTrimAdjustCommand.Execute(null);
        if (state == "small") for (int i = 0; i < 6; i++) screen.TryNudgeTrimHandle(CropHandle.Left, 1, 0, bigStep: false, toBorder: false);
        if (state == "small") screen.CropSelection.ShouldBe(TrimBounds.FromEdges(8, 1, 9, 8));
        if (state == "invalid") screen.TryNudgeTrimHandle(CropHandle.Left, 1, 0, bigStep: true, toBorder: false).ShouldBeFalse();
        if (state == "compare") screen.ShowTrimCompareViewCommand.Execute(null);
        // One source pixel wide, on the picture's right border: narrower than a handle.
        if (state == "tiny") screen.CropSelection = TrimBounds.FromEdges(11, 0, 12, 10);
        Size viewport = new(width, height);

        var facts = WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = screen }, viewport, tree =>
        {
            tree.Root.Dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
            tree.Root.UpdateLayout();
            Canvas overlay = tree.OfType<Canvas>().Single(c => AutomationProperties.GetAutomationId(c) == "Session.ManualCropSurface");
            List<Thumb> handles = tree.OfType<Thumb>().Where(t => AutomationProperties.GetAutomationId(t).StartsWith("Session.TrimAdjust.Handle.", StringComparison.Ordinal)).ToList();
            var shownHandles = handles.Where(t => t.Visibility == Visibility.Visible).ToList();
            var outsideOverlay = shownHandles.Where(t => Canvas.GetLeft(t) < -0.01 || Canvas.GetTop(t) < -0.01 ||
                Canvas.GetLeft(t) + t.Width > overlay.ActualWidth + 0.01 || Canvas.GetTop(t) + t.Height > overlay.ActualHeight + 0.01).ToList();
            FrameworkElement panel = tree.OfType<StackPanel>().Single(p => AutomationProperties.GetAutomationId(p) == "Session.TrimAdjust.Panel");
            var tooWide = tree.OfType<TextBlock>().Where(t => Shown(t) && IsInside(t, panel) && t.ActualWidth > panel.ActualWidth + 0.5).ToList();
            ReviewApprovalButton use = tree.OfType<ReviewApprovalButton>().Single(b => AutomationProperties.GetAutomationId(b) == "Session.TrimAdjust.Use");
            Image? proposed = tree.OfType<Image>().SingleOrDefault(i => AutomationProperties.GetAutomationId(i) == "Session.TrimAdjust.ProposedImage");

            // Clipping: every shown button of the left (preview/crop) column ends inside that column.
            Grid content = tree.OfType<Grid>().First(g => g.ColumnDefinitions.Count == 2 && g.ColumnDefinitions[1].Width.Value == 360);
            double columnRight = content.ColumnDefinitions[0].ActualWidth;
            FrameworkElement leftColumn = content.Children.OfType<FrameworkElement>().First(c => Grid.GetColumn(c) == 0);
            var clippedButtons = tree.OfType<ButtonBase>()
                .Where(b => Shown(b) && IsInside(b, leftColumn) &&
                            b.TransformToAncestor(content).Transform(new Point(b.ActualWidth, 0)).X > columnRight + 0.5)
                .Select(b => AutomationProperties.GetName(b) + "|" + b.Content).ToList();
            return (Handles: shownHandles.Count, Outside: outsideOverlay.Count,
                HandleNames: shownHandles.Select(t => AutomationProperties.GetName(t)).ToList(),
                HandlesFocusable: handles.All(t => t.Focusable && t.IsTabStop),
                TooWide: tooWide.Count, Clipped: clippedButtons, UseIdentity: use.TargetIdentity, UseDefault: use.IsDefault,
                AnyDefault: tree.OfType<Button>().Any(b => b.IsDefault),
                ApproveShown: tree.OfType<ReviewApprovalButton>().Any(b => AutomationProperties.GetAutomationId(b) == "Session.Approve" && Shown(b)),
                ConfirmShown: tree.OfType<ReviewApprovalButton>().Any(b => AutomationProperties.GetAutomationId(b) == "Session.FinalSave.ConfirmAndSave" && Shown(b)),
                OutlineShown: tree.OfType<System.Windows.Shapes.Rectangle>().Any(r => AutomationProperties.GetAutomationId(r) == "Session.TrimAdjust.Outline" && r.Visibility == Visibility.Visible),
                Proposed: proposed?.Source is BitmapSource bitmap ? (bitmap.PixelWidth, bitmap.PixelHeight) : (0, 0),
                Instructions: tree.OfType<TextBlock>().Single(t => AutomationProperties.GetAutomationId(t) == "Session.TrimAdjust.Instructions").Text);
        });

        facts.Facts.TooWide.ShouldBe(0, "adjust-panel text wraps inside its column");
        facts.Facts.Clipped.ShouldBeEmpty("no control of the image column is cut off at its right edge");
        facts.Facts.HandlesFocusable.ShouldBeTrue();
        facts.Facts.UseIdentity.ShouldBe(screen.UseThisTrimIdentity);
        facts.Facts.UseDefault.ShouldBeFalse();
        facts.Facts.AnyDefault.ShouldBeFalse("no implicit Enter submission, approval or save");
        facts.Facts.ApproveShown.ShouldBeFalse();
        facts.Facts.ConfirmShown.ShouldBeFalse();
        facts.Facts.Instructions.ShouldStartWith(screen.TrimAdjustInstructions);
        facts.Facts.Instructions.ShouldEndWith(screen.TrimAdjustKeyboardHint);
        if (state == "compare")
        {
            facts.Facts.Proposed.ShouldBe((Automatic.Width, Automatic.Height), "the proposed view is the draft, cut from the source preview");
        }
        else
        {
            facts.Facts.Handles.ShouldBe(8);
            facts.Facts.Outside.ShouldBe(0, "handles are anchored inside the boundary, even on the picture border");
            facts.Facts.HandleNames.ShouldAllBe(name => !string.IsNullOrWhiteSpace(name));
            facts.Facts.OutlineShown.ShouldBeTrue();
        }

        string? destination = Environment.GetEnvironmentVariable("PF_11147_CAPTURE_DIR");
        if (!string.IsNullOrWhiteSpace(destination))
            WpfRendering.CapturePng(() => new SessionScreenView { DataContext = screen }, viewport,
                Path.Combine(destination, $"trim-{state}-{language}-{width:0}x{height:0}.png"),
                tree => tree.Root.Dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { })));
    }

    // -----------------------------------------------------------------------------

    internal static async Task<(ISessionService Service, SessionViewModel Screen, SessionView Review)> OpenAsync(
        SessionServiceHarness h, bool enhance = false, string? source = null, WorkflowType type = WorkflowType.PrepareAsset,
        ILocalisationService? localisation = null)
    {
        ISessionService service = h.CreateService();
        (_, SessionView review) = await TrimAdjustmentStepTests.AtTrimReviewAsync(h, service, enhance, source,
            margin: source is null ? null : TrimMargin.Tight, type: type);
        review.TrimAdjustment.ShouldNotBeNull();
        FolderPicker picker = new();
        var screen = new SessionViewModel(service, h.Previews, h.TiffReviews, new RecordingNavigation(), null, localisation,
            new FinalSaveCoordinator(service, FinalSaveFixtures.Delivery(h), "tester"), picker, new RecordingDeliveredFileShell());
        Pickers.Add(screen, picker);
        screen.Open(review);
        await screen.PreviewsLoaded;
        await screen.FinalSaveFactsLoaded;
        return (service, screen, review);
    }

    /// <summary>Chooses a temp save folder the way an operator does: Change location, then the picker.</summary>
    private static Task SetFolderAsync(SessionViewModel screen, string folder)
    {
        Pickers.TryGetValue(screen, out FolderPicker? picker).ShouldBeTrue();
        picker!.Next = folder;
        screen.ChangeLocationCommand.Execute(null);
        return Task.CompletedTask;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SessionViewModel, FolderPicker> Pickers = new();

    private static bool Shown(DependencyObject element)
    {
        for (DependencyObject? node = element; node is not null; node = VisualTreeHelper.GetParent(node))
            if (node is UIElement { Visibility: not Visibility.Visible }) return false;
        return true;
    }

    private static bool IsInside(DependencyObject element, DependencyObject root)
    {
        for (DependencyObject? node = element; node is not null; node = VisualTreeHelper.GetParent(node))
            if (ReferenceEquals(node, root)) return true;
        return false;
    }

    /// <summary>Everything durable about a session that a transient editor action must not change.</summary>
    private sealed record Snapshot(string Steps, int Revisions, int Attempts, int Reviews, int Invalid)
    {
        public static async Task<Snapshot> TakeAsync(SessionServiceHarness h, SessionId id)
        {
            SessionAggregate a = (await h.Repository.LoadAsync(id, CancellationToken.None)).Value!;
            return new Snapshot(string.Join(";", a.Steps.Select(s => $"{s.Step}:{s.State}:{s.CurrentRevisionId}:{s.AttemptCount}")),
                a.Revisions.Count, a.Attempts.Count, a.Reviews.Count, a.Revisions.Count(r => !r.IsValid));
        }
    }

    /// <summary>Answers with a temp folder; never shows a dialog.</summary>
    private sealed class FolderPicker : IDeliveryFolderPicker
    {
        public string? Next { get; set; }
        public string? PickFolder(string title, string? initialFolder) => Next;
    }
}
