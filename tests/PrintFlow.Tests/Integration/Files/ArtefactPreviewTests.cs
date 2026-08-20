using System.IO;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Files;

/// <summary>
/// The read-only preview seam, over a real workspace, a real database and the real WIC
/// decoder (Epic 11200 Part C1 §22, §23, §24).
/// </summary>
/// <remarks>
/// Nothing here is doubled. The point of the slice is that an operator can see the file a
/// review is about, and a stubbed decoder would assert only that a stub returns what it was
/// told to. Every payload below is decoded again before it is believed.
/// <para>
/// The refusal cases matter more than the success ones. The seam names a session and a
/// Revision, so the question is never "can it be tricked into reading <c>C:\Windows</c>" — that
/// API does not exist — but "can it be pointed at a Revision that is not this operator's
/// work", which is what the middle two tests answer.
/// </para>
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class ArtefactPreviewTests
{
    // -----------------------------------------------------------------------------
    // §22: the seam
    // -----------------------------------------------------------------------------

    [Fact]
    public async Task A_managed_Revision_of_the_open_session_previews()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionView imported = (await service.ImportAsync(
            WorkflowType.PrepareAsset, harness.WriteBorderedSourcePng(), "preview", "tester",
            CancellationToken.None)).Value;

        RevisionId root = imported.CurrentArtefact!.RevisionId;

        OperationResult<ImagePreview> preview =
            await harness.Previews.GetPreviewAsync(imported.Id, root, CancellationToken.None);

        preview.IsSuccess.ShouldBeTrue(preview.IsFailure ? preview.Failure.ToString() : "");
        preview.Value.RevisionId.ShouldBe(root);
        preview.Value.SourcePixelWidth.ShouldBe(12);
        preview.Value.SourcePixelHeight.ShouldBe(10);
        preview.Value.HasTransparency.ShouldBeTrue();
        preview.Value.IsDownsampledForDisplay.ShouldBeFalse();

        // Believed only because something decoded it.
        SyntheticImages.DecodeDimensions(preview.Value.Payload).ShouldBe((12, 10));
    }

    /// <summary>
    /// Session A cannot preview session B's Revision (§22).
    /// </summary>
    /// <remarks>
    /// Both Revisions are perfectly valid and both files are present, so the only thing that can
    /// make this fail is the containment rule itself.
    /// </remarks>
    [Fact]
    public async Task A_Revision_belonging_to_another_session_is_refused()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionView a = (await service.ImportAsync(
            WorkflowType.PrepareAsset, harness.WriteSourcePng("a.png"), "a", "tester",
            CancellationToken.None)).Value;
        SessionView b = (await service.ImportAsync(
            WorkflowType.PrepareAsset, harness.WriteSourcePng("b.png"), "b", "tester",
            CancellationToken.None)).Value;

        RevisionId belongingToB = b.CurrentArtefact!.RevisionId;

        OperationResult<ImagePreview> refused =
            await harness.Previews.GetPreviewAsync(a.Id, belongingToB, CancellationToken.None);

        refused.IsFailure.ShouldBeTrue();
        refused.Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);

        // The control: the same Revision under its own session is fine, so the refusal above is
        // about ownership and not about the file being unreadable.
        (await harness.Previews.GetPreviewAsync(b.Id, belongingToB, CancellationToken.None))
            .IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_Revision_id_that_exists_nowhere_is_refused()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionView imported = (await service.ImportAsync(
            WorkflowType.PrepareAsset, harness.WriteSourcePng(), "unknown", "tester",
            CancellationToken.None)).Value;

        OperationResult<ImagePreview> refused = await harness.Previews.GetPreviewAsync(
            imported.Id, RevisionId.From(Guid.NewGuid()), CancellationToken.None);

        refused.IsFailure.ShouldBeTrue();
        refused.Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);
    }

    /// <summary>
    /// A known Revision whose file has gone fails the preview and moves nothing (§22).
    /// </summary>
    /// <remarks>
    /// This is the §21 promise in its sharpest form: the file the review is about is genuinely
    /// missing, which is about as bad as a preview request can go, and the session still holds
    /// exactly the state it held before — same session state, same step states, same artefact.
    /// A preview is not an attempt.
    /// </remarks>
    [Fact]
    public async Task A_missing_file_fails_the_preview_and_leaves_the_session_untouched()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId id = (await service.ImportAsync(
            WorkflowType.PrepareAsset, harness.WriteSourcePng(), "missing", "tester",
            CancellationToken.None)).Value.Id;

        await service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None);
        await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None);

        SessionView before = (await service.LoadAsync(id, CancellationToken.None)).Value;
        RevisionId enhanced = before.CurrentArtefact!.RevisionId;

        SessionAggregate aggregate = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        Revision revision = aggregate.Revisions.Single(r => r.Id == enhanced);
        File.Delete(harness.FileWorkspace.ResolveAbsolute(revision.File));

        OperationResult<ImagePreview> failed =
            await harness.Previews.GetPreviewAsync(id, enhanced, CancellationToken.None);

        failed.IsFailure.ShouldBeTrue();
        failed.Failure.Code.ShouldBe(FailureCode.OutputMissing);

        SessionView after = (await service.LoadAsync(id, CancellationToken.None)).Value;
        after.State.ShouldBe(before.State);
        after.CurrentStep!.Step.ShouldBe(before.CurrentStep!.Step);
        after.CurrentStep.State.ShouldBe(before.CurrentStep.State);
        after.CurrentArtefact!.RevisionId.ShouldBe(enhanced);
        after.Steps.Select(s => s.State).ShouldBe(before.Steps.Select(s => s.State));
    }

    /// <summary>
    /// Previewing writes nothing, anywhere (§4, §8).
    /// </summary>
    /// <remarks>
    /// Asserted over the whole workspace rather than over the one file, because the risks worth
    /// ruling out are the ones nobody would look for: a cache directory, a thumbnail beside the
    /// artefact, a flattened copy with the checkerboard baked in. Every file's bytes are
    /// compared, so a same-size rewrite in place would fail this too.
    /// </remarks>
    [Fact]
    public async Task Previewing_writes_nothing_to_the_workspace()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId id = (await service.ImportAsync(
            WorkflowType.PrepareAsset, harness.WriteBorderedSourcePng(), "no-writes", "tester",
            CancellationToken.None)).Value.Id;

        SessionView view = await DriveToResultOfAsync(service, id, StepKind.Trim);

        Dictionary<string, byte[]> before = SnapshotWorkspace(harness);

        for (int i = 0; i < 3; i++)
        {
            (await harness.Previews.GetPreviewAsync(
                id, view.CurrentArtefact!.RevisionId, CancellationToken.None)).IsSuccess.ShouldBeTrue();
            (await harness.Previews.GetPreviewAsync(
                id, view.UpstreamArtefact!.RevisionId, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        }

        Dictionary<string, byte[]> after = SnapshotWorkspace(harness);

        after.Keys.OrderBy(k => k, StringComparer.Ordinal)
            .ShouldBe(before.Keys.OrderBy(k => k, StringComparer.Ordinal));
        foreach ((string relative, byte[] bytes) in before)
        {
            after[relative].ShouldBe(bytes, $"'{relative}' changed while nothing but previews ran.");
        }
    }

    /// <summary>
    /// An image larger than the decoder's display bound is reduced, and says so (§6).
    /// </summary>
    /// <remarks>
    /// The reported <c>SourcePixel*</c> figures stay the artefact's own, which is what stops a
    /// reduced preview from quietly misrepresenting the file: the operator is told 2600 px wide
    /// and shown 2048, rather than told 2048.
    /// </remarks>
    [Fact]
    public async Task An_oversized_image_is_reduced_for_display_and_reports_that_it_was()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        // Deliberately a thin strip: 2600 px wide is past the bound, and 4 px tall keeps the
        // test cheap. The longest edge is what the rule is about.
        string oversized = harness.Workspace.CreateSourceFile(
            "oversized.png", SyntheticImages.PngWithAlpha(2600, 4, (_, _) => 255));

        SessionView imported = (await service.ImportAsync(
            WorkflowType.PrepareAsset, oversized, "oversized", "tester", CancellationToken.None)).Value;

        ImagePreview preview = (await harness.Previews.GetPreviewAsync(
            imported.Id, imported.CurrentArtefact!.RevisionId, CancellationToken.None)).Value;

        preview.SourcePixelWidth.ShouldBe(2600);
        preview.SourcePixelHeight.ShouldBe(4);
        preview.IsDownsampledForDisplay.ShouldBeTrue();
        preview.PixelWidth.ShouldBe(harness.PreviewDecoder.MaximumDisplayEdge);

        SyntheticImages.DecodeDimensions(preview.Payload).Width
            .ShouldBe(harness.PreviewDecoder.MaximumDisplayEdge);
    }

    /// <summary>An image within the bound is previewed at full resolution (§6).</summary>
    [Fact]
    public async Task An_image_within_the_display_bound_is_previewed_at_full_resolution()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionView imported = (await service.ImportAsync(
            WorkflowType.PrepareAsset, harness.WriteBorderedSourcePng(), "full-res", "tester",
            CancellationToken.None)).Value;

        ImagePreview preview = (await harness.Previews.GetPreviewAsync(
            imported.Id, imported.CurrentArtefact!.RevisionId, CancellationToken.None)).Value;

        preview.IsDownsampledForDisplay.ShouldBeFalse();
        (preview.PixelWidth, preview.PixelHeight)
            .ShouldBe((preview.SourcePixelWidth, preview.SourcePixelHeight));
    }

    // -----------------------------------------------------------------------------
    // §23: before/after pairing comes from the derivation edge
    // -----------------------------------------------------------------------------

    /// <summary>
    /// For each derived step, Before is the Revision the result was actually derived from (§23).
    /// </summary>
    /// <remarks>
    /// Asserted against <c>Revision.SourceRevisionId</c> read back out of the database, never
    /// against a file name: the two fake-adapter steps produce files with identical names and
    /// identical bytes, so a comparison built on names would pass while pairing the wrong pair.
    /// </remarks>
    [Theory]
    [InlineData(StepKind.Enhancement)]
    [InlineData(StepKind.BackgroundRemoval)]
    [InlineData(StepKind.Trim)]
    public async Task The_upstream_artefact_is_the_source_Revision_of_the_step_result(StepKind step)
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId id = (await service.ImportAsync(
            WorkflowType.PrepareAsset, harness.WriteBorderedSourcePng(), "pairing", "tester",
            CancellationToken.None)).Value.Id;

        SessionView view = await DriveToResultOfAsync(service, id, step);

        view.HasBeforeAfterComparison.ShouldBeTrue();

        SessionAggregate aggregate = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        Revision result = aggregate.Revisions.Single(r => r.Id == view.CurrentArtefact!.RevisionId);

        view.CurrentArtefact!.RevisionId.ShouldBe(result.Id);
        view.UpstreamArtefact!.RevisionId.ShouldBe(result.SourceRevisionId!.Value);

        // Both halves preview, which is what makes the pair a comparison rather than a claim.
        (await harness.Previews.GetPreviewAsync(id, view.UpstreamArtefact.RevisionId, CancellationToken.None))
            .IsSuccess.ShouldBeTrue();
        (await harness.Previews.GetPreviewAsync(id, view.CurrentArtefact.RevisionId, CancellationToken.None))
            .IsSuccess.ShouldBeTrue();
    }

    /// <summary>The imported original has no upstream, so it is one image and not a comparison.</summary>
    [Fact]
    public async Task The_imported_original_has_no_before()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionView imported = (await service.ImportAsync(
            WorkflowType.PrepareAsset, harness.WriteSourcePng(), "root", "tester",
            CancellationToken.None)).Value;

        imported.CurrentArtefact!.SourceRevisionId.ShouldBeNull();
        imported.UpstreamArtefact.ShouldBeNull();
        imported.HasBeforeAfterComparison.ShouldBeFalse();
    }

    // -----------------------------------------------------------------------------
    // §24: the Trim review read model, on a real synthetic transparent PNG
    // -----------------------------------------------------------------------------

    /// <summary>
    /// 12×10 in, 5×5 out, and both halves decode (§24).
    /// </summary>
    /// <remarks>
    /// The one test that ties the whole slice to the flow it exists for: a transparent image, a
    /// deterministic trim, and a review in which the operator can see the canvas actually got
    /// smaller. No screenshot is compared — the dimensions the two previews report, read back
    /// out of the payloads themselves, are the evidence.
    /// </remarks>
    [Fact]
    public async Task A_trimmed_result_previews_beside_its_uncropped_upstream()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId id = (await service.ImportAsync(
            WorkflowType.PrepareAsset, harness.WriteBorderedSourcePng(), "trim-review", "tester",
            CancellationToken.None)).Value.Id;

        SessionView view = await DriveToResultOfAsync(service, id, StepKind.Trim);
        view.HasBeforeAfterComparison.ShouldBeTrue();

        ImagePreview before = (await harness.Previews.GetPreviewAsync(
            id, view.UpstreamArtefact!.RevisionId, CancellationToken.None)).Value;
        ImagePreview after = (await harness.Previews.GetPreviewAsync(
            id, view.CurrentArtefact!.RevisionId, CancellationToken.None)).Value;

        (before.SourcePixelWidth, before.SourcePixelHeight).ShouldBe((12, 10));
        (after.SourcePixelWidth, after.SourcePixelHeight).ShouldBe((5, 5));

        SyntheticImages.DecodeDimensions(before.Payload).ShouldBe((12, 10));
        SyntheticImages.DecodeDimensions(after.Payload).ShouldBe((5, 5));

        // The checkerboard exists for exactly this: a cut-out whose alpha survived the crop.
        before.HasTransparency.ShouldBeTrue();
        after.HasTransparency.ShouldBeTrue();
    }

    /// <summary>
    /// A trim that needs a human produces no "after" to compare against (§17).
    /// </summary>
    /// <remarks>
    /// The absence is structural rather than a rule the screen follows: no Revision was created,
    /// so the artefact on screen is the step's <i>input</i>, and there is nothing for
    /// <c>HasBeforeAfterComparison</c> to be true about. The failure code survives the reload,
    /// which is what lets the notice appear again on Resume.
    /// </remarks>
    [Fact]
    public async Task A_manual_crop_outcome_offers_no_after_and_reports_its_code()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId id = (await service.ImportAsync(
            WorkflowType.PrepareAsset, harness.WriteFullyTransparentSourcePng(), "manual", "tester",
            CancellationToken.None)).Value.Id;

        await Must(service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None));
        await RunAndApproveAsync(service, id, StepKind.Enhancement);
        await RunAndApproveAsync(service, id, StepKind.BackgroundRemoval);

        OperationResult<SessionView> trimmed = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Trim), "tester", CancellationToken.None);
        trimmed.IsFailure.ShouldBeTrue();
        trimmed.Failure.Code.ShouldBe(FailureCode.ManualCropRequired);

        SessionView reloaded = (await service.LoadAsync(id, CancellationToken.None)).Value;

        reloaded.CurrentStepFailure.ShouldBe(FailureCode.ManualCropRequired);
        reloaded.HasBeforeAfterComparison.ShouldBeFalse();
        reloaded.UpstreamArtefact.ShouldBeNull();
        reloaded.CurrentArtefact!.IsCurrentStepResult.ShouldBeFalse();
    }

    // -----------------------------------------------------------------------------

    /// <summary>Every file under the workspace root, keyed by its relative path.</summary>
    private static Dictionary<string, byte[]> SnapshotWorkspace(SessionServiceHarness harness)
    {
        Dictionary<string, byte[]> files = new(StringComparer.OrdinalIgnoreCase);
        foreach (string absolute in Directory.EnumerateFiles(
            harness.Workspace.Root, "*", SearchOption.AllDirectories))
        {
            files[Path.GetRelativePath(harness.Workspace.Root, absolute)] = File.ReadAllBytes(absolute);
        }

        return files;
    }

    /// <summary>Runs PREPARE_ASSET until <paramref name="step"/> holds its own result.</summary>
    private static async Task<SessionView> DriveToResultOfAsync(
        ISessionService service, SessionId id, StepKind step)
    {
        await Must(service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None));

        foreach (StepKind earlier in new[] { StepKind.Enhancement, StepKind.BackgroundRemoval, StepKind.Trim })
        {
            if (earlier == step)
            {
                OperationResult<SessionView> ran = await service.ExecuteAsync(
                    id, new WorkflowCommand.StartStep(step), "tester", CancellationToken.None);
                ran.IsSuccess.ShouldBeTrue(ran.IsFailure ? ran.Failure.ToString() : "");
                return ran.Value;
            }

            await RunAndApproveAsync(service, id, earlier);
        }

        throw new ArgumentOutOfRangeException(nameof(step), step, "Not a PREPARE_ASSET pixel step.");
    }

    private static async Task RunAndApproveAsync(ISessionService service, SessionId id, StepKind step)
    {
        OperationResult<SessionView> started =
            await service.ExecuteAsync(id, new WorkflowCommand.StartStep(step), "tester", CancellationToken.None);
        started.IsSuccess.ShouldBeTrue(started.IsFailure ? started.Failure.ToString() : "");

        await Must(service.ExecuteAsync(
            id,
            new WorkflowCommand.Approve(step, started.Value.Steps.Single(s => s.Step == step).CurrentRevisionSha256!.Value),
            "tester",
            CancellationToken.None));
    }

    private static async Task Must(Task<OperationResult<SessionView>> resultTask)
    {
        OperationResult<SessionView> result = await resultTask;
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : "");
    }
}
