using System.IO;
using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

/// <summary>
/// The review screen's own preview behaviour: what it shows, what it says when it cannot, and
/// what the three zoom buttons do (Epic 11200 Part C1 §7, §17, §19, §21, §26).
/// </summary>
/// <remarks>
/// Zoom is asserted as state and nothing else (§26). There is no pixel measurement anywhere
/// below: where the image lands is the view's business, and a test that pinned it would fail
/// the first time a margin changed while telling nobody anything about whether zooming works.
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class ImagePreviewControlTests
{
    // -----------------------------------------------------------------------------
    // §26: zoom state and its limits
    // -----------------------------------------------------------------------------

    [Fact]
    public void A_freshly_opened_session_starts_fitted_to_the_viewport()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = harness.Session(new RecordingNavigation());

        session.IsFitToViewport.ShouldBeTrue();
        session.ZoomScale.ShouldBe(1.0);
        session.ZoomLabel.ShouldBe(session.FitLabel);
    }

    [Fact]
    public void Zooming_in_leaves_fit_and_magnifies()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = harness.Session(new RecordingNavigation());

        session.ZoomInCommand.Execute(null);

        session.IsFitToViewport.ShouldBeFalse();
        session.ZoomScale.ShouldBe(1.25, 1e-9);
        session.ZoomLabel.ShouldBe("125%");
    }

    [Fact]
    public void Zooming_out_leaves_fit_and_reduces()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = harness.Session(new RecordingNavigation());

        session.ZoomOutCommand.Execute(null);

        session.IsFitToViewport.ShouldBeFalse();
        session.ZoomScale.ShouldBe(0.8, 1e-9);
    }

    /// <summary>The stated bounds hold however many times the button is pressed (§12).</summary>
    [Fact]
    public void Zoom_is_clamped_to_the_stated_bounds()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = harness.Session(new RecordingNavigation());

        for (int i = 0; i < 40; i++)
        {
            session.ZoomInCommand.Execute(null);
        }

        session.ZoomScale.ShouldBe(SessionViewModel.MaximumZoom, 1e-9);
        session.CanZoomIn.ShouldBeFalse();
        session.CanZoomOut.ShouldBeTrue();

        for (int i = 0; i < 80; i++)
        {
            session.ZoomOutCommand.Execute(null);
        }

        session.ZoomScale.ShouldBe(SessionViewModel.MinimumZoom, 1e-9);
        session.CanZoomOut.ShouldBeFalse();
        session.CanZoomIn.ShouldBeTrue();
    }

    [Fact]
    public void Reset_restores_the_opening_fit_state()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = harness.Session(new RecordingNavigation());

        session.ZoomInCommand.Execute(null);
        session.ZoomInCommand.Execute(null);
        session.IsFitToViewport.ShouldBeFalse();

        session.ResetZoomCommand.Execute(null);

        session.IsFitToViewport.ShouldBeTrue();
        session.ZoomScale.ShouldBe(1.0);
        session.ZoomLabel.ShouldBe(session.FitLabel);
    }

    /// <summary>Opening the next artefact starts fitted again, whatever the last one was left at (§15).</summary>
    [Fact]
    public async Task Showing_a_new_result_returns_to_fit()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = await OpenPrepareAssetAsync(harness, "zoom-reset.png");

        session.ZoomInCommand.Execute(null);
        session.IsFitToViewport.ShouldBeFalse();

        await session.RunStepCommand.ExecuteAsync(null);

        session.IsFitToViewport.ShouldBeTrue();
        session.ZoomScale.ShouldBe(1.0);
    }

    // -----------------------------------------------------------------------------
    // §7, §11: what the panes hold
    // -----------------------------------------------------------------------------

    /// <summary>The imported original is one pane, and the metadata beside it is untouched (§7).</summary>
    [Fact]
    public async Task The_imported_original_shows_one_preview_beside_its_metadata()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = await OpenPrepareAssetAsync(harness, "single-pane.png");

        session.HasPreview.ShouldBeTrue();
        session.PreviewPanes.Count.ShouldBe(1);
        session.PreviewPanes[0].Heading.ShouldBe(session.SinglePreviewLabel);
        session.PreviewPanes[0].HasImage.ShouldBeTrue();
        session.PreviewPanes[0].Payload.IsEmpty.ShouldBeFalse();

        // Everything Part 3C3A put on the screen is still on it (§7).
        session.ArtefactFileName.ShouldBe("single-pane.png");
        session.ArtefactPixels.ShouldNotBeNullOrWhiteSpace();
        session.ArtefactDpi.ShouldNotBeNullOrWhiteSpace();
        session.ArtefactHash.ShouldNotBeNullOrWhiteSpace();
        session.ArtefactRevision.ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>A derived result is Before then After, in that order (§11).</summary>
    [Fact]
    public async Task A_derived_result_shows_before_then_after()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = await OpenPrepareAssetAsync(harness, "before-after.png");

        await session.RunStepCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;

        session.PreviewPanes.Count.ShouldBe(2);
        session.PreviewPanes[0].Heading.ShouldBe(session.BeforeLabel);
        session.PreviewPanes[1].Heading.ShouldBe(session.AfterLabel);
        session.PreviewPanes.ShouldAllBe(pane => pane.HasImage);
    }

    /// <summary>
    /// A preview that will not decode says so and stops there (§21).
    /// </summary>
    /// <remarks>
    /// The file is deleted behind the screen's back, which is the harshest version of "the
    /// preview failed". What must survive is everything else: the metadata, the review controls,
    /// the session state, and the absence of any error notice — a picture that would not draw is
    /// not a processing failure and must never be reported as one.
    /// </remarks>
    [Fact]
    public async Task A_preview_that_cannot_be_produced_leaves_the_review_usable()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = await OpenPrepareAssetAsync(harness, "no-preview.png");

        await session.RunStepCommand.ExecuteAsync(null);
        session.IsReviewRequired.ShouldBeTrue();

        SessionId id = harness.Navigation.WorkflowSelectionFor!.Id;
        SessionAggregate aggregate = (await harness.Inner.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        Revision enhanced = aggregate.Revisions.Single(r => r.Operation == OperationKind.Enhance);
        File.Delete(harness.Inner.FileWorkspace.ResolveAbsolute(enhanced.File));

        // Re-opening the same session is what an operator returning to it does, and it is where
        // the preview is fetched again — this time from a file that is no longer there.
        session.Open((await harness.Sessions.LoadAsync(id, CancellationToken.None)).Value);
        await session.PreviewsLoaded;

        session.PreviewPanes.ShouldNotBeEmpty();
        session.PreviewPanes.ShouldContain(pane => pane.IsUnavailable);
        session.PreviewPanes.First(pane => pane.IsUnavailable).Unavailable.ShouldNotBeNullOrWhiteSpace();

        // Not a failure of anything else: no notice, the metadata still reads, and the review
        // decision is still on offer bound to the hash it was always bound to (§21).
        session.Notice.ShouldBeNull();
        session.ArtefactHash.ShouldNotBeNullOrWhiteSpace();
        session.IsReadOnly.ShouldBeFalse();
        session.IsReviewRequired.ShouldBeTrue();
        session.CanApprove.ShouldBeTrue();
    }

    // -----------------------------------------------------------------------------
    // §17: manual crop
    // -----------------------------------------------------------------------------

    /// <summary>
    /// A trim needing a human shows the notice, and no "after" is invented (§17).
    /// </summary>
    [Fact]
    public async Task A_manual_crop_outcome_shows_the_notice_and_no_after_pane()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = await OpenAtTrimAsync(harness, transparentSource: true);

        await session.RunStepCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;

        session.IsManualCropRequired.ShouldBeTrue();
        session.ManualCropNotice.ShouldBe(session.ManualCropNotice);

        // The failure line is still there as well; the notice adds to it, never replaces it.
        session.Notice.ShouldNotBeNullOrWhiteSpace();

        // One pane, and it is the upstream input rather than a fabricated result.
        session.PreviewPanes.Count.ShouldBe(1);
        session.PreviewPanes[0].Heading.ShouldBe(session.SinglePreviewLabel);
        session.ArtefactIsInput.ShouldBeTrue();
    }

    /// <summary>An ordinary trim shows both halves and no manual-crop notice.</summary>
    [Fact]
    public async Task A_deterministic_trim_shows_the_uncropped_upstream_beside_the_cropped_result()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = await OpenAtTrimAsync(harness, transparentSource: false);

        await session.RunStepCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;

        session.Notice.ShouldBeNull();
        session.IsManualCropRequired.ShouldBeFalse();
        session.IsReviewRequired.ShouldBeTrue();

        session.PreviewPanes.Count.ShouldBe(2);
        session.PreviewPanes[0].Heading.ShouldBe(session.BeforeLabel);
        session.PreviewPanes[1].Heading.ShouldBe(session.AfterLabel);
        session.PreviewPanes.ShouldAllBe(pane => pane.HasImage);

        // The visible fact of the slice: the canvas got smaller.
        SyntheticImages.DecodeDimensions(session.PreviewPanes[0].Payload).ShouldBe((12, 10));
        SyntheticImages.DecodeDimensions(session.PreviewPanes[1].Payload).ShouldBe((5, 5));
    }

    /// <summary>
    /// A preview load overtaken by a newer state publishes nothing.
    /// </summary>
    /// <remarks>
    /// Decoding a production-sized image takes long enough that a second command can land while
    /// the first load is still running. If the older load then appended its panes, the operator
    /// would be looking at the previous Revision's image beside the current Revision's hash —
    /// the one kind of staleness a review surface must never produce. The screen is driven here
    /// without awaiting the first load, which is what makes the overlap real rather than
    /// hypothetical.
    /// </remarks>
    [Fact]
    public async Task A_preview_load_overtaken_by_a_newer_state_publishes_nothing()
    {
        using HomeScreenHarness harness = new();
        harness.FilePicker.Path = harness.WriteSourceFile("overtaken.png");
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);

        // The first decode is held open, so the overlap is real rather than a matter of timing.
        GatedPreviewService gated = new(harness.Previews);
        SessionViewModel session = new(harness.Sessions, gated, harness.TiffReviews, new RecordingNavigation());

        session.Open(harness.Navigation.WorkflowSelectionFor!);
        Task overtaken = session.PreviewsLoaded;
        await gated.FirstCallStarted;

        // Two further states arrive while that first load is still stuck.
        await session.ConfirmOriginalCommand.ExecuteAsync(null);
        await session.RunStepCommand.ExecuteAsync(null);
        session.Notice.ShouldBeNull();
        session.PreviewPanes.Count.ShouldBe(2);

        gated.Release();
        await overtaken;

        // Exactly the panes the newest state calls for — no leftovers from the stale load.
        session.PreviewPanes.Count.ShouldBe(2);
        session.PreviewPanes[0].Heading.ShouldBe(session.BeforeLabel);
        session.PreviewPanes[1].Heading.ShouldBe(session.AfterLabel);
    }

    /// <summary>
    /// The real preview seam with its first call held open until a test says otherwise.
    /// </summary>
    /// <remarks>
    /// Delegates rather than stubs: the pane that eventually arrives is a real decoded image, so
    /// the test is about <i>when</i> a load publishes and not about what a double returns.
    /// </remarks>
    private sealed class GatedPreviewService : IArtefactPreviewService
    {
        private readonly IArtefactPreviewService _inner;
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public GatedPreviewService(IArtefactPreviewService inner) => _inner = inner;

        /// <summary>Completes once the held call has actually begun waiting.</summary>
        public Task FirstCallStarted => _started.Task;

        public void Release() => _gate.TrySetResult();

        public async Task<OperationResult<ImagePreview>> GetPreviewAsync(
            SessionId sessionId, RevisionId revisionId, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                _started.TrySetResult();
                await _gate.Task;
            }

            return await _inner.GetPreviewAsync(sessionId, revisionId, cancellationToken);
        }

        /// <summary>Ungated: this suite gates the review pane, not the Home list.</summary>
        public Task<OperationResult<ImagePreview>> GetRecentThumbnailAsync(
            SessionId sessionId, CancellationToken cancellationToken) =>
            _inner.GetRecentThumbnailAsync(sessionId, cancellationToken);
    }

    // -----------------------------------------------------------------------------
    // §18, §19: the preview changes nothing it must not
    // -----------------------------------------------------------------------------

    /// <summary>
    /// Having previewed a Revision does not make a mutated file approvable (§18).
    /// </summary>
    /// <remarks>
    /// The whole risk this test exists for: an operator looks at an image, the bytes change
    /// underneath, and the approval goes through on the strength of what was on screen. It does
    /// not. The preview is visual evidence; the SHA-256 is the authority, and the integrity
    /// re-check still refuses.
    /// </remarks>
    [Fact]
    public async Task A_previewed_Revision_whose_bytes_change_still_refuses_approval()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = await OpenPrepareAssetAsync(harness, "hash-bound.png");

        await session.RunStepCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;

        session.PreviewPanes.ShouldAllBe(pane => pane.HasImage);
        session.CanApprove.ShouldBeTrue();

        SessionId id = harness.Navigation.WorkflowSelectionFor!.Id;
        SessionAggregate aggregate = (await harness.Inner.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        Revision enhanced = aggregate.Revisions.Single(r => r.Operation == OperationKind.Enhance);

        await File.WriteAllBytesAsync(
            harness.Inner.FileWorkspace.ResolveAbsolute(enhanced.File),
            SyntheticImages.Png(3, 2, alpha: true));

        await session.ApproveCommand.ExecuteAsync(null);

        session.Notice.ShouldNotBeNull();
        session.Notice.ShouldContain(nameof(FailureCode.RevisionIntegrityMismatch));

        SessionView refused = (await harness.Sessions.LoadAsync(id, CancellationToken.None)).Value;
        refused.Steps.Single(step => step.Step == StepKind.Enhancement).State
            .ShouldNotBe(StepState.Approved);
    }

    /// <summary>
    /// Leaving for Home drops the images; resuming reloads them from what was persisted (§19).
    /// </summary>
    /// <remarks>
    /// The second screen is a different view model over the same service — which is exactly what
    /// navigation does — so it starts with nothing and has to fetch the preview again. A screen
    /// that had leaned on a cached bitmap would show one here without asking; this one asks.
    /// </remarks>
    [Fact]
    public async Task Going_home_releases_the_previews_and_resuming_reloads_them()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel first = await OpenPrepareAssetAsync(harness, "resume-preview.png");

        await first.RunStepCommand.ExecuteAsync(null);
        await first.PreviewsLoaded;
        first.PreviewPanes.Count.ShouldBe(2);

        await first.BackToHomeCommand.ExecuteAsync(null);

        first.PreviewPanes.ShouldBeEmpty();
        first.HasPreview.ShouldBeFalse();

        SessionId id = harness.Navigation.WorkflowSelectionFor!.Id;
        SessionView persisted = (await harness.Sessions.LoadAsync(id, CancellationToken.None)).Value;

        SessionViewModel resumed = harness.Session(new RecordingNavigation());
        resumed.PreviewPanes.ShouldBeEmpty();

        resumed.Open(persisted);
        await resumed.PreviewsLoaded;

        resumed.PreviewPanes.Count.ShouldBe(2);
        resumed.PreviewPanes.ShouldAllBe(pane => pane.HasImage);
        resumed.IsFitToViewport.ShouldBeTrue();
    }

    // -----------------------------------------------------------------------------

    /// <summary>Imports a PREPARE_ASSET session and confirms the original, ready to run a step.</summary>
    private static async Task<SessionViewModel> OpenPrepareAssetAsync(
        HomeScreenHarness harness, string fileName)
    {
        harness.FilePicker.Path = harness.WriteSourceFile(fileName);
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);

        SessionViewModel session = harness.Session(new RecordingNavigation());
        session.Open(harness.Navigation.WorkflowSelectionFor!);
        await session.PreviewsLoaded;
        await session.ConfirmOriginalCommand.ExecuteAsync(null);

        session.Notice.ShouldBeNull();
        return session;
    }

    /// <summary>Drives a PREPARE_ASSET session as far as Trim, ready for Run Step.</summary>
    private static async Task<SessionViewModel> OpenAtTrimAsync(
        HomeScreenHarness harness, bool transparentSource)
    {
        harness.FilePicker.Path = transparentSource
            ? harness.Inner.WriteFullyTransparentSourcePng("trim-ui-empty.png")
            : harness.Inner.WriteBorderedSourcePng("trim-ui-bordered.png");

        await harness.Home.ChooseFileCommand.ExecuteAsync(null);

        SessionViewModel session = harness.Session(new RecordingNavigation());
        session.Open(harness.Navigation.WorkflowSelectionFor!);
        await session.ConfirmOriginalCommand.ExecuteAsync(null);

        // Enhancement.
        await session.RunStepCommand.ExecuteAsync(null);
        await session.ApproveCommand.ExecuteAsync(null);

        // Background Removal, which since Epic 11300 Part C2B1 needs an explicit
        // reviewed-content authority before it will start (§7). C2B1 ships no control for it, so
        // the test issues the command through the service seam the operator UI will use in C2B2.
        await SessionServiceHarness.AuthoriseBackgroundRemovalAsync(
            harness.Sessions, harness.Navigation.WorkflowSelectionFor!.Id);
        await session.RunStepCommand.ExecuteAsync(null);
        await session.ApproveCommand.ExecuteAsync(null);

        session.Notice.ShouldBeNull();

        SessionView atTrim = (await harness.Sessions.LoadAsync(
            harness.Navigation.WorkflowSelectionFor!.Id, CancellationToken.None)).Value;
        atTrim.CurrentStep!.Step.ShouldBe(StepKind.Trim);

        return session;
    }
}
