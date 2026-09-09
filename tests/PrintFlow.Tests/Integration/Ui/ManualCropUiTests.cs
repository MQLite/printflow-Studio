using System.IO;
using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Reviews;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Trimming;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

/// <summary>
/// The manual-crop surface as the operator drives it (Epic 11200 Part C2 §22, §25, §26, §31).
/// </summary>
/// <remarks>
/// The view model over the real service, real workspace, real database and real processors —
/// the same arrangement every other screen test uses, so "Submit invokes the service command"
/// is checked by the session really moving rather than by a spy recording a call.
/// <para>
/// Coordinates appear here only as arguments to <c>TrySetCropSelection</c>, never as synthesised
/// mouse events: the transform itself is <c>CropSurfaceLayoutTests</c>, and what is worth
/// asserting at this level is what the screen does with the answer (§31).
/// </para>
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class ManualCropUiTests
{
    /// <summary>The synthetic no-alpha source these tests crop, and the drag applied to it.</summary>
    /// <remarks>
    /// The image is 12×10 and the preview is not reduced at that size, so a surface of 12×10
    /// maps one drag pixel to one source pixel — which keeps the expected rectangle readable in
    /// the assertions instead of being the output of a second calculation.
    /// </remarks>
    private static CropSurfaceLayout Surface(SessionViewModel session) => new(
        SurfaceWidth: 12,
        SurfaceHeight: 10,
        PayloadPixelWidth: 12,
        PayloadPixelHeight: 10,
        SourcePixelWidth: 12,
        SourcePixelHeight: 10,
        session.IsFitToViewport,
        session.ZoomScale);

    // -----------------------------------------------------------------------------
    // §31: when the control appears
    // -----------------------------------------------------------------------------

    /// <summary>The crop control is absent during an ordinary review (§31).</summary>
    [Fact]
    public async Task The_crop_control_is_absent_during_a_normal_review()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = await AtTrimAsync(harness, transparentSource: false);

        await session.RunStepCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;

        session.IsReviewRequired.ShouldBeTrue();
        session.CanManualCrop.ShouldBeFalse();
        session.IsCropping.ShouldBeFalse();

        // Pressing it anyway does nothing: the command is the same guard the button reads.
        session.BeginManualCropCommand.Execute(null);
        session.IsCropping.ShouldBeFalse();
    }

    /// <summary>The crop control appears exactly for a ManualCropRequired outcome (§31).</summary>
    [Fact]
    public async Task The_crop_control_appears_only_for_a_manual_crop_outcome()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = await AtManualCropAsync(harness);

        session.IsManualCropRequired.ShouldBeTrue();
        session.CanManualCrop.ShouldBeTrue();

        // Not yet open, and nothing selected: eligibility is an offer, not a mode.
        session.IsCropping.ShouldBeFalse();
        session.CropPane.ShouldBeNull();

        session.BeginManualCropCommand.Execute(null);

        session.IsCropping.ShouldBeTrue();
        session.CropSelection.ShouldBeNull();
        session.CanApplyManualCrop.ShouldBeFalse();
        session.CropSelectionSummary.ShouldNotBeNullOrWhiteSpace();

        // The image to draw on is the file the crop will actually be applied to.
        session.CropPane.ShouldNotBeNull();
        session.CropPane!.HasImage.ShouldBeTrue();
        session.CropPane.SourcePixelWidth.ShouldBe(12);
        session.CropPane.SourcePixelHeight.ShouldBe(10);
    }

    // -----------------------------------------------------------------------------
    // §31: drawing
    // -----------------------------------------------------------------------------

    /// <summary>A drag becomes a rectangle in source-image pixels (§31).</summary>
    [Fact]
    public async Task Drawing_a_rectangle_produces_source_pixel_bounds()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = await InCropModeAsync(harness);

        session.TrySetCropSelection(Surface(session), 3, 2, 9, 7).ShouldBeTrue();

        session.CropSelection.ShouldBe(TrimBounds.FromEdges(3, 2, 9, 7));
        session.IsCropSelectionInvalid.ShouldBeFalse();
        session.CanApplyManualCrop.ShouldBeTrue();
    }

    /// <summary>Drawing again replaces the previous rectangle (§5).</summary>
    /// <remarks>
    /// The whole of the MVP's "the operator may redraw the rectangle": there are no handles and
    /// no edit mode, and a second drag simply is the new selection.
    /// </remarks>
    [Fact]
    public async Task Drawing_a_second_rectangle_replaces_the_first()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = await InCropModeAsync(harness);

        session.TrySetCropSelection(Surface(session), 3, 2, 9, 7).ShouldBeTrue();
        session.TrySetCropSelection(Surface(session), 1, 1, 5, 5).ShouldBeTrue();

        session.CropSelection.ShouldBe(TrimBounds.FromEdges(1, 1, 5, 5));
    }

    /// <summary>A fitted surface larger than the image scales the drag back down (§31).</summary>
    /// <remarks>
    /// The realistic case: a 12&#160;px image on a several-hundred-pixel pane. The rectangle
    /// recorded is in the artefact's pixels, not the pane's, which is what makes the crop the
    /// same whatever size the window happens to be.
    /// </remarks>
    [Fact]
    public async Task A_drag_on_a_fitted_surface_is_converted_to_source_pixels()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = await InCropModeAsync(harness);

        // 12×10 image fitted to a 240×200 pane: ×20 in both directions, no letterbox.
        CropSurfaceLayout fitted = Surface(session) with { SurfaceWidth = 240, SurfaceHeight = 200 };
        fitted.DisplayScale.ShouldBe(20.0, 1e-9);

        session.TrySetCropSelection(fitted, 60, 40, 180, 140).ShouldBeTrue();

        session.CropSelection.ShouldBe(TrimBounds.FromEdges(3, 2, 9, 7));
    }

    /// <summary>A drag while magnified is converted by the zoom in force (§31).</summary>
    [Fact]
    public async Task A_drag_while_magnified_is_converted_by_the_current_zoom()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = await InCropModeAsync(harness);

        session.ZoomInCommand.Execute(null);
        session.ZoomInCommand.Execute(null);
        session.ZoomInCommand.Execute(null);
        session.IsFitToViewport.ShouldBeFalse();
        session.ZoomScale.ShouldBe(1.25 * 1.25 * 1.25, 1e-9);

        // A surface exactly the magnified image's size, so the letterbox is zero and the only
        // thing under test is the zoom factor.
        double scale = session.ZoomScale;
        CropSurfaceLayout magnified = Surface(session) with
        {
            SurfaceWidth = 12 * scale,
            SurfaceHeight = 10 * scale,
        };

        session.TrySetCropSelection(magnified, 3 * scale, 2 * scale, 9 * scale, 7 * scale).ShouldBeTrue();

        session.CropSelection.ShouldBe(TrimBounds.FromEdges(3, 2, 9, 7));
    }

    /// <summary>An empty selection is refused and says so (§23, §31).</summary>
    [Fact]
    public async Task An_empty_crop_is_refused_and_cannot_be_applied()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = await InCropModeAsync(harness);

        // The ManualCropRequired failure line is still up from the automatic attempt; what must
        // not appear is a *new* failure caused by the refused drag.
        string? noticeBefore = session.Notice;
        noticeBefore.ShouldNotBeNullOrWhiteSpace();

        session.TrySetCropSelection(Surface(session), 5, 5, 5, 5).ShouldBeFalse();

        session.CropSelection.ShouldBeNull();
        session.IsCropSelectionInvalid.ShouldBeTrue();
        session.CanApplyManualCrop.ShouldBeFalse();
        session.ManualCropInvalidNotice.ShouldNotBeNullOrWhiteSpace();

        // Applying anyway does nothing but restate the refusal: no command, no state change.
        await session.ApplyManualCropCommand.ExecuteAsync(null);

        session.Notice.ShouldBe(noticeBefore);
        session.IsCropping.ShouldBeTrue();
        session.IsCropSelectionInvalid.ShouldBeTrue();
        await AssertNoManualImportAsync(harness);
    }

    /// <summary>A refusal is cleared by the next usable drag (§23).</summary>
    [Fact]
    public async Task A_usable_drag_clears_a_previous_refusal()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = await InCropModeAsync(harness);

        session.TrySetCropSelection(Surface(session), 5, 5, 5, 5).ShouldBeFalse();
        session.IsCropSelectionInvalid.ShouldBeTrue();

        session.TrySetCropSelection(Surface(session), 2, 2, 8, 8).ShouldBeTrue();
        session.IsCropSelectionInvalid.ShouldBeFalse();
    }

    /// <summary>Nothing is selectable before crop mode is entered.</summary>
    [Fact]
    public async Task A_drag_outside_crop_mode_selects_nothing()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = await AtManualCropAsync(harness);

        session.IsCropping.ShouldBeFalse();
        session.TrySetCropSelection(Surface(session), 1, 1, 8, 8).ShouldBeFalse();
        session.CropSelection.ShouldBeNull();
    }

    // -----------------------------------------------------------------------------
    // §22: Cancel
    // -----------------------------------------------------------------------------

    /// <summary>
    /// Cancelling after drawing leaves no file, no attempt, no Revision and no state change (§22).
    /// </summary>
    /// <remarks>
    /// Compared against the whole persisted session before and after, rather than against a
    /// list of things that might have changed: "nothing happened" is only worth asserting if
    /// the assertion could notice anything at all happening.
    /// </remarks>
    [Fact]
    public async Task Cancelling_a_crop_changes_nothing()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = await InCropModeAsync(harness);

        SessionId id = harness.Navigation.WorkflowSelectionFor!.Id;
        SessionAggregate before = (await harness.Inner.Repository.LoadAsync(id, CancellationToken.None)).Value!;

        session.TrySetCropSelection(Surface(session), 3, 2, 9, 7).ShouldBeTrue();
        session.CanApplyManualCrop.ShouldBeTrue();

        session.CancelManualCropCommand.Execute(null);

        // Back on the ManualCropRequired screen, with the offer still standing.
        session.IsCropping.ShouldBeFalse();
        session.CropSelection.ShouldBeNull();
        session.IsCropSelectionInvalid.ShouldBeFalse();
        session.CropPane.ShouldBeNull();
        session.IsManualCropRequired.ShouldBeTrue();
        session.CanManualCrop.ShouldBeTrue();
        session.Notice.ShouldNotBeNullOrWhiteSpace();

        SessionAggregate after = (await harness.Inner.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        after.Session.ShouldBe(before.Session);
        after.Steps.ShouldBe(before.Steps);
        after.Revisions.Count.ShouldBe(before.Revisions.Count);
        after.Attempts.Count.ShouldBe(before.Attempts.Count);
        after.Reviews.Count.ShouldBe(before.Reviews.Count);
        after.Revisions.ShouldNotContain(r => r.Operation == OperationKind.ManualImport);
    }

    // -----------------------------------------------------------------------------
    // §31: Submit, and the review that follows
    // -----------------------------------------------------------------------------

    /// <summary>
    /// Applying a crop runs the service command and opens a Before/After review (§18, §31).
    /// </summary>
    /// <remarks>
    /// Both halves of §31's last two bullets in one test, because they are one operator action:
    /// pressing Apply has to move the real session, and what the operator is looking at
    /// afterwards has to be the pair the lineage defines — the file they cropped, then the crop.
    /// </remarks>
    [Fact]
    public async Task Applying_a_crop_submits_the_command_and_opens_the_before_after_review()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = await InCropModeAsync(harness);

        session.TrySetCropSelection(Surface(session), 3, 2, 9, 7).ShouldBeTrue();
        await session.ApplyManualCropCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;

        session.Notice.ShouldBeNull();

        // Crop mode closed itself; the operator is reviewing, not still drawing.
        session.IsCropping.ShouldBeFalse();
        session.CropSelection.ShouldBeNull();
        session.CropPane.ShouldBeNull();
        session.IsReviewRequired.ShouldBeTrue();
        session.IsManualCropRequired.ShouldBeFalse();
        session.CanManualCrop.ShouldBeFalse();

        // Before is the file that was cropped; After is the crop (§18).
        session.PreviewPanes.Count.ShouldBe(2);
        session.PreviewPanes[0].Heading.ShouldBe(session.BeforeLabel);
        session.PreviewPanes[1].Heading.ShouldBe(session.AfterLabel);
        session.PreviewPanes.ShouldAllBe(pane => pane.HasImage);
        SyntheticImages.DecodeDimensions(session.PreviewPanes[0].Payload).ShouldBe((12, 10));
        SyntheticImages.DecodeDimensions(session.PreviewPanes[1].Payload).ShouldBe((6, 5));

        // And the persisted result is the ManualImport the operator drew.
        SessionId id = harness.Navigation.WorkflowSelectionFor!.Id;
        SessionAggregate aggregate = (await harness.Inner.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        Revision manual = aggregate.Revisions.Single(r => r.Operation == OperationKind.ManualImport);
        manual.Facts.PixelWidth.ShouldBe(6);
        manual.Facts.PixelHeight.ShouldBe(5);
        manual.SourceRevisionId.ShouldBe(aggregate.Revisions.Single(r => r.Operation == OperationKind.Import).Id);

        // §19: approval is the ordinary hash-bound one, from the screen's own buttons.
        session.CanApprove.ShouldBeTrue();
        await session.ApproveCommand.ExecuteAsync(null);

        session.Notice.ShouldBeNull();
        (await harness.Sessions.LoadAsync(id, CancellationToken.None)).Value
            .Steps.Single(s => s.Step == StepKind.Trim).State.ShouldBe(StepState.Approved);
    }

    /// <summary>
    /// Rejecting a crop from the screen reopens the manual path, not the automatic one (§21, §30).
    /// </summary>
    [Fact]
    public async Task Rejecting_a_crop_offers_another_crop_rather_than_an_automatic_retry()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = await InCropModeAsync(harness);

        session.TrySetCropSelection(Surface(session), 3, 2, 9, 7).ShouldBeTrue();
        await session.ApplyManualCropCommand.ExecuteAsync(null);
        session.CanReject.ShouldBeTrue();

        await session.RejectCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;

        session.Notice.ShouldBeNull();
        session.CanManualCrop.ShouldBeTrue();

        // A second crop, straight away, with no Retry and no automatic attempt in between.
        session.BeginManualCropCommand.Execute(null);
        session.TrySetCropSelection(Surface(session), 1, 1, 11, 9).ShouldBeTrue();
        await session.ApplyManualCropCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;

        session.Notice.ShouldBeNull();
        session.IsReviewRequired.ShouldBeTrue();

        SessionId id = harness.Navigation.WorkflowSelectionFor!.Id;
        SessionAggregate aggregate = (await harness.Inner.Repository.LoadAsync(id, CancellationToken.None)).Value!;

        // Both crops survive, and only one deterministic attempt was ever made.
        aggregate.Revisions.Count(r => r.Operation == OperationKind.ManualImport).ShouldBe(2);
        aggregate.Attempts.Count(a => a.AdapterId == "internal-alpha-trim-v1").ShouldBe(1);
        aggregate.Reviews.ShouldContain(r => r.Step == StepKind.Trim && !r.IsApproved);
    }

    // -----------------------------------------------------------------------------
    // §26: the preview stays non-authoritative
    // -----------------------------------------------------------------------------

    /// <summary>
    /// A cropped Revision that is previewed and then mutated still refuses approval (§26).
    /// </summary>
    /// <remarks>
    /// Part C1's rule, re-checked on the one artefact whose rectangle the operator chose
    /// themselves. Having drawn it and having seen it are still not the same as its bytes being
    /// the bytes on disk.
    /// </remarks>
    [Fact]
    public async Task A_previewed_manual_crop_whose_bytes_change_still_refuses_approval()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = await InCropModeAsync(harness);

        session.TrySetCropSelection(Surface(session), 3, 2, 9, 7).ShouldBeTrue();
        await session.ApplyManualCropCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;

        session.PreviewPanes.ShouldAllBe(pane => pane.HasImage);
        session.CanApprove.ShouldBeTrue();

        SessionId id = harness.Navigation.WorkflowSelectionFor!.Id;
        SessionAggregate aggregate = (await harness.Inner.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        Revision manual = aggregate.Revisions.Single(r => r.Operation == OperationKind.ManualImport);

        await File.WriteAllBytesAsync(
            harness.Inner.FileWorkspace.ResolveAbsolute(manual.File), SyntheticImages.Png(3, 2, alpha: true));

        await session.ApproveCommand.ExecuteAsync(null);

        session.Notice.ShouldNotBeNull();
        session.Notice.ShouldContain(nameof(FailureCode.RevisionIntegrityMismatch));

        (await harness.Sessions.LoadAsync(id, CancellationToken.None)).Value
            .Steps.Single(s => s.Step == StepKind.Trim).State.ShouldNotBe(StepState.Approved);
    }

    // -----------------------------------------------------------------------------
    // §25: stale-preview safety
    // -----------------------------------------------------------------------------

    /// <summary>
    /// A preview load for crop A that finishes after crop B has landed publishes nothing (§25).
    /// </summary>
    /// <remarks>
    /// The C1 generation token, exercised over the manual-crop path rather than duplicated for
    /// it. The risk is the same and the consequence is worse: a stale image beside a fresh hash
    /// would show the operator the rectangle they replaced while asking them to approve the one
    /// they drew.
    /// <para>
    /// The overlap is built around <c>Open</c> rather than around a command, and the reason is
    /// itself a safety property worth knowing: <see cref="SessionViewModel"/> serialises
    /// commands behind <c>IsBusy</c>, so a second command cannot begin while the first is still
    /// settling its previews. What genuinely can overlap is a load begun by opening a
    /// session — resuming one, in practice — which is exactly the case §25 describes.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_manual_crop_preview_overtaken_by_a_later_crop_publishes_nothing()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel driver = await InCropModeAsync(harness);
        SessionId id = harness.Navigation.WorkflowSelectionFor!.Id;

        // Crop A: a small rectangle, reviewed normally.
        driver.TrySetCropSelection(Surface(driver), 0, 0, 4, 4).ShouldBeTrue();
        await driver.ApplyManualCropCommand.ExecuteAsync(null);
        await driver.PreviewsLoaded;
        SyntheticImages.DecodeDimensions(driver.PreviewPanes[1].Payload).ShouldBe((4, 4));

        SessionView showingCropA = (await harness.Sessions.LoadAsync(id, CancellationToken.None)).Value;

        // A second screen over the same session — what resuming it does — with crop A's preview
        // load held open the moment it starts.
        GatedPreviewService gated = new(harness.Previews);
        SessionViewModel resumed = new(harness.Sessions, gated, harness.TiffReviews, new RecordingNavigation());

        gated.HoldNextCall();
        resumed.Open(showingCropA);
        Task overtaken = resumed.PreviewsLoaded;
        await gated.NextCallStarted;
        resumed.PreviewPanes.ShouldBeEmpty();

        // Meanwhile the session really moves on: crop A is rejected and a larger crop B is made.
        await driver.RejectCommand.ExecuteAsync(null);
        driver.CanManualCrop.ShouldBeTrue();
        driver.BeginManualCropCommand.Execute(null);
        driver.TrySetCropSelection(Surface(driver), 2, 1, 11, 9).ShouldBeTrue();
        await driver.ApplyManualCropCommand.ExecuteAsync(null);
        await driver.PreviewsLoaded;
        driver.Notice.ShouldBeNull();

        // The resumed screen is shown crop B, which starts and finishes its own load.
        SessionView showingCropB = (await harness.Sessions.LoadAsync(id, CancellationToken.None)).Value;
        resumed.Open(showingCropB);
        await resumed.PreviewsLoaded;

        resumed.PreviewPanes.Count.ShouldBe(2);
        SyntheticImages.DecodeDimensions(resumed.PreviewPanes[1].Payload).ShouldBe((9, 8));

        // Crop A's load finishes last, and publishes nothing.
        gated.Release();
        await overtaken;

        resumed.PreviewPanes.Count.ShouldBe(2);
        resumed.PreviewPanes[0].Heading.ShouldBe(resumed.BeforeLabel);
        resumed.PreviewPanes[1].Heading.ShouldBe(resumed.AfterLabel);
        SyntheticImages.DecodeDimensions(resumed.PreviewPanes[1].Payload).ShouldBe((9, 8));
    }

    /// <summary>
    /// The real preview seam with one call held open until a test releases it.
    /// </summary>
    /// <remarks>
    /// Delegates rather than stubs, so what eventually arrives is a real decoded image and the
    /// test is about <i>when</i> a load publishes rather than about what a double returns.
    /// </remarks>
    private sealed class GatedPreviewService : IArtefactPreviewService
    {
        private readonly IArtefactPreviewService _inner;

        /// <summary>The gate the held call waits on. Kept for <see cref="Release"/> to open.</summary>
        private TaskCompletionSource? _gate;

        /// <summary>The same gate, taken by the first call that arrives and then cleared.</summary>
        /// <remarks>
        /// Two fields for one gate, because "which call is held" and "who can release it" are
        /// different lifetimes: the arming is consumed so only one call is held, while the gate
        /// itself has to outlive that so a later <see cref="Release"/> can still open it.
        /// </remarks>
        private TaskCompletionSource? _armed;

        private TaskCompletionSource? _started;

        public GatedPreviewService(IArtefactPreviewService inner) => _inner = inner;

        /// <summary>Completes once the held call has actually begun waiting.</summary>
        public Task NextCallStarted => _started?.Task ?? Task.CompletedTask;

        /// <summary>Arms the gate: the next call blocks until <see cref="Release"/>.</summary>
        public void HoldNextCall()
        {
            _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _armed = _gate;
            _started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public void Release() => _gate?.TrySetResult();

        public async Task<OperationResult<ImagePreview>> GetPreviewAsync(
            SessionId sessionId, RevisionId revisionId, CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _armed, null) is { } armed)
            {
                _started?.TrySetResult();
                await armed.Task;
            }

            return await _inner.GetPreviewAsync(sessionId, revisionId, cancellationToken);
        }

        /// <summary>Ungated: this suite gates the crop surface's pane, not the Home list.</summary>
        public Task<OperationResult<ImagePreview>> GetRecentThumbnailAsync(
            SessionId sessionId, CancellationToken cancellationToken) =>
            _inner.GetRecentThumbnailAsync(sessionId, cancellationToken);
    }

    // -----------------------------------------------------------------------------

    /// <summary>Drives a PREPARE_ASSET session as far as Trim, skipping both Meitu steps.</summary>
    private static async Task<SessionViewModel> AtTrimAsync(
        HomeScreenHarness harness, bool transparentSource)
    {
        harness.FilePicker.Path = transparentSource
            ? harness.Inner.WriteOpaqueSourcePng("crop-ui-opaque.png")
            : harness.Inner.WriteBorderedSourcePng("crop-ui-bordered.png");

        await harness.Home.ChooseFileCommand.ExecuteAsync(null);

        SessionViewModel session = harness.Session(new RecordingNavigation());
        session.Open(harness.Navigation.WorkflowSelectionFor!);
        await session.ConfirmOriginalCommand.ExecuteAsync(null);
        await session.SkipCommand.ExecuteAsync(null);
        await session.SkipCommand.ExecuteAsync(null);

        session.Notice.ShouldBeNull();
        return session;
    }

    /// <summary>Runs the automatic trim on a no-alpha source, leaving the manual-crop offer up.</summary>
    private static async Task<SessionViewModel> AtManualCropAsync(HomeScreenHarness harness)
    {
        SessionViewModel session = await AtTrimAsync(harness, transparentSource: true);

        await session.RunStepCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;

        session.IsManualCropRequired.ShouldBeTrue();
        return session;
    }

    /// <summary>The same, with the crop surface open and ready for a drag.</summary>
    private static async Task<SessionViewModel> InCropModeAsync(HomeScreenHarness harness)
    {
        SessionViewModel session = await AtManualCropAsync(harness);
        session.BeginManualCropCommand.Execute(null);
        session.IsCropping.ShouldBeTrue();
        return session;
    }

    private static async Task AssertNoManualImportAsync(HomeScreenHarness harness)
    {
        SessionId id = harness.Navigation.WorkflowSelectionFor!.Id;
        SessionAggregate aggregate = (await harness.Inner.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        aggregate.Revisions.ShouldNotContain(r => r.Operation == OperationKind.ManualImport);
    }
}
