using System.IO;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Reviews;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Trimming;
using PrintFlow.Infrastructure.Adapters.Fake;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Persistence;

/// <summary>
/// Manual crop driven end to end by <see cref="SessionService"/>, with the real processor, a
/// real workspace and a real database (Epic 11200 Part C2 §13, §16, §28, §29, §30, §36).
/// </summary>
/// <remarks>
/// The claim these exist to check is not "the pixels are right" — <c>ManualCropProcessorTests</c>
/// covers that — but that an operator-drawn rectangle becomes an ordinary, auditable step
/// result: a fresh attempt, a real file, a <c>FileInspector</c> pass, a SHA-256, a
/// <c>ManualImport</c> Revision pointing at the file that was cropped, and the same hash-bound
/// review every other produced file goes through.
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class ManualCropStepTests
{
    /// <summary>The rectangle the "operator" draws in these tests, in source pixels.</summary>
    /// <remarks>
    /// Deliberately asymmetric and off-centre, so a crop that transposed its axes or measured
    /// from the wrong corner produces a differently shaped result rather than an accidentally
    /// correct square. The synthetic no-alpha source is 12×10.
    /// </remarks>
    private static readonly TrimBounds OperatorCrop = TrimBounds.FromEdges(3, 2, 9, 7);

    // -----------------------------------------------------------------------------
    // §28: the primary acceptance scenario
    // -----------------------------------------------------------------------------

    /// <summary>
    /// A no-alpha file goes Confirm → skip → skip → automatic Trim refuses → manual crop →
    /// review → approve → export → complete (§28).
    /// </summary>
    /// <remarks>
    /// The whole slice in one test, on the file that motivates it. Every step is the ordinary
    /// command path; nothing here reaches a processor, sets a state, or works around a guard.
    /// </remarks>
    [Fact]
    public async Task A_no_alpha_asset_completes_PREPARE_ASSET_through_a_manual_crop()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId id = await StartAtTrimAsync(service, harness.WriteOpaqueSourcePng());

        // Automatic trim is still tried first, and still refuses honestly.
        OperationResult<SessionView> automatic = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Trim), "tester", CancellationToken.None);

        automatic.IsFailure.ShouldBeTrue();
        automatic.Failure.Code.ShouldBe(FailureCode.ManualCropRequired);

        // The screen is now offered the manual crop, by the workflow layer rather than itself.
        SessionView blocked = (await service.LoadAsync(id, CancellationToken.None)).Value;
        blocked.CurrentStepFailure.ShouldBe(FailureCode.ManualCropRequired);
        blocked.CanManualCrop.ShouldBeTrue();
        blocked.State.ShouldBe(SessionState.Active);

        // Manual crop: an ordinary command, producing an ordinary reviewable result.
        OperationResult<SessionView> cropped = await service.ExecuteAsync(
            id, new WorkflowCommand.SubmitManualCrop(StepKind.Trim, OperatorCrop), "tester", CancellationToken.None);
        cropped.IsSuccess.ShouldBeTrue(cropped.IsFailure ? cropped.Failure.ToString() : "");

        SessionStep trim = cropped.Value.Steps.Single(s => s.Step == StepKind.Trim);
        trim.State.ShouldBe(StepState.ReviewRequired);

        SessionAggregate aggregate = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        Revision manual = aggregate.Revisions.Single(r => r.Operation == OperationKind.ManualImport);

        // §15: a real file that passed the ordinary inspect-and-hash pipeline.
        string absolute = harness.FileWorkspace.ResolveAbsolute(manual.File);
        File.Exists(absolute).ShouldBeTrue();
        manual.File.FileName.ShouldBe("manual-crop.png");
        manual.File.Area.ShouldBe(WorkspaceArea.Working);
        manual.Facts.Format.ShouldBe(ImageFormat.Png);
        manual.Facts.PixelWidth.ShouldBe(OperatorCrop.Width);
        manual.Facts.PixelHeight.ShouldBe(OperatorCrop.Height);
        manual.Facts.Sha256.ShouldBe(trim.CurrentRevisionSha256!.Value);

        // §16: the lineage is the Revision the operator actually cropped — here the import,
        // because both Meitu steps were skipped.
        Revision imported = aggregate.Revisions.Single(r => r.Operation == OperationKind.Import);
        manual.SourceRevisionId.ShouldBe(imported.Id);

        // §18: which is exactly what the Before/After pairing is resolved through.
        cropped.Value.HasBeforeAfterComparison.ShouldBeTrue();
        cropped.Value.UpstreamArtefact!.RevisionId.ShouldBe(imported.Id);
        cropped.Value.CurrentArtefact!.RevisionId.ShouldBe(manual.Id);

        // §17: reviewed normally, never auto-approved.
        cropped.Value.CanManualCrop.ShouldBeFalse();

        // §19: approval is hash-bound and ordinary.
        OperationResult<SessionView> approved = await service.ExecuteAsync(
            id, new WorkflowCommand.Approve(StepKind.Trim, manual.Facts.Sha256), "tester", CancellationToken.None);
        approved.IsSuccess.ShouldBeTrue(approved.IsFailure ? approved.Failure.ToString() : "");
        approved.Value.Steps.Single(s => s.Step == StepKind.Trim).State.ShouldBe(StepState.Approved);

        // And the workflow simply carries on from there.
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.ApprovedPngExport), "tester", CancellationToken.None));
        OperationResult<SessionView> completed = await service.ExecuteAsync(
            id, new WorkflowCommand.Complete(), "tester", CancellationToken.None);
        completed.IsSuccess.ShouldBeTrue(completed.IsFailure ? completed.Failure.ToString() : "");
        completed.Value.State.ShouldBe(SessionState.Completed);

        // The exported PNG is the cropped canvas, promoted unchanged.
        SessionAggregate finished = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        Revision exported = finished.Revisions.Single(r => r.Operation == OperationKind.PromoteApproved);
        exported.Facts.Sha256.ShouldBe(manual.Facts.Sha256);
        exported.Facts.PixelWidth.ShouldBe(OperatorCrop.Width);
        exported.Facts.PixelHeight.ShouldBe(OperatorCrop.Height);
    }

    /// <summary>Manual crop is not a handoff: the session stays Active throughout (§4).</summary>
    /// <remarks>
    /// Worth asserting outright rather than inferring from the scenario above. <c>HandOff</c>
    /// would have been the easy implementation — it already exists and already opens a working
    /// copy — and it would have ended automated progression for a session that has nothing wrong
    /// with it except that one rectangle had to be drawn by hand.
    /// </remarks>
    [Fact]
    public async Task A_manual_crop_never_hands_the_session_off()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId id = await AtManualCropAsync(service, harness);

        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.SubmitManualCrop(StepKind.Trim, OperatorCrop), "tester", CancellationToken.None));

        SessionView view = (await service.LoadAsync(id, CancellationToken.None)).Value;
        view.State.ShouldBe(SessionState.Active);
        view.CanContinueProcessing.ShouldBeTrue();

        SessionAggregate aggregate = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        (await harness.Repository.GetAutomationLockAsync(CancellationToken.None)).Value.IsHeld.ShouldBeFalse();
        aggregate.Session.State.ShouldBe(SessionState.Active);
    }

    // -----------------------------------------------------------------------------
    // §29: the audit trail
    // -----------------------------------------------------------------------------

    /// <summary>
    /// The failed deterministic attempt and the successful manual one both survive (§14, §29).
    /// </summary>
    /// <remarks>
    /// The refusal is part of the record: it is the reason a human cropped this file, and
    /// erasing it would leave a <c>ManualImport</c> Revision with no explanation. The two
    /// attempts are distinguished by their processor identity, so no later reader has to guess
    /// which code produced which result.
    /// </remarks>
    [Fact]
    public async Task Both_the_refused_automatic_attempt_and_the_manual_one_are_retained()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId id = await AtManualCropAsync(service, harness);

        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.SubmitManualCrop(StepKind.Trim, OperatorCrop), "tester", CancellationToken.None));

        SessionAggregate aggregate = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        List<ProcessingAttempt> attempts = [.. aggregate.Attempts.Where(a => a.Step == StepKind.Trim)];
        attempts.Count.ShouldBe(2);

        ProcessingAttempt automatic = attempts.Single(a => a.AdapterId == "internal-alpha-trim-v1");
        automatic.Status.ShouldBe(AttemptStatus.Failed);
        automatic.Failure!.Code.ShouldBe(FailureCode.ManualCropRequired);
        automatic.OutputRevisionId.ShouldBeNull();
        automatic.Operation.ShouldBe(OperationKind.Trim);

        ProcessingAttempt manual = attempts.Single(a => a.AdapterId == "internal-manual-crop-v1");
        manual.Status.ShouldBe(AttemptStatus.Succeeded);
        manual.Operation.ShouldBe(OperationKind.ManualImport);
        manual.OutputRevisionId.ShouldBe(
            aggregate.Revisions.Single(r => r.Operation == OperationKind.ManualImport).Id);

        // A fresh attempt, not a reuse of the one that failed (§14).
        manual.Id.ShouldNotBe(automatic.Id);
        manual.RetrySequence.ShouldBeGreaterThan(automatic.RetrySequence);
    }

    /// <summary>The imported original is never touched by a crop (§36).</summary>
    /// <remarks>
    /// The crop reads a per-attempt working copy, so this ought to hold by construction — which
    /// is exactly the kind of "ought to" worth pinning down. Both halves are checked: the bytes
    /// still hash to what the root Revision recorded, and the workspace's read-only marking is
    /// still on the file.
    /// </remarks>
    [Fact]
    public async Task The_imported_source_snapshot_is_unchanged_by_a_manual_crop()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId id = await AtManualCropAsync(service, harness);

        SessionAggregate before = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        Revision root = before.Revisions.Single(r => r.Operation == OperationKind.Import);
        string sourcePath = harness.FileWorkspace.ResolveAbsolute(root.File);
        byte[] originalBytes = await File.ReadAllBytesAsync(sourcePath);

        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.SubmitManualCrop(StepKind.Trim, OperatorCrop), "tester", CancellationToken.None));

        SessionAggregate after = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        Revision rootAfter = after.Revisions.Single(r => r.Operation == OperationKind.Import);

        rootAfter.File.ShouldBe(root.File);
        rootAfter.Facts.Sha256.ShouldBe(root.Facts.Sha256);
        rootAfter.IsValid.ShouldBeTrue();
        (await File.ReadAllBytesAsync(sourcePath)).ShouldBe(originalBytes);
        File.GetAttributes(sourcePath).HasFlag(FileAttributes.ReadOnly).ShouldBeTrue();

        // The cropped output lives in the attempt's own Working directory, not beside the source.
        Revision manual = after.Revisions.Single(r => r.Operation == OperationKind.ManualImport);
        manual.File.Area.ShouldBe(WorkspaceArea.Working);
        manual.File.RelativePath.ShouldNotBe(root.File.RelativePath);
    }

    // -----------------------------------------------------------------------------
    // §13: the guard
    // -----------------------------------------------------------------------------

    /// <summary>
    /// A crop against a Trim awaiting an ordinary review is refused (§13).
    /// </summary>
    /// <remarks>
    /// The step has a perfectly good automatic result on offer; there is nothing to crop by
    /// hand, and accepting the command would discard a result the operator has not judged yet.
    /// </remarks>
    [Fact]
    public async Task A_crop_against_a_successful_trim_awaiting_review_is_refused()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId id = await StartAtTrimAsync(service, harness.WriteBorderedSourcePng());
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Trim), "tester", CancellationToken.None));

        SessionView reviewing = (await service.LoadAsync(id, CancellationToken.None)).Value;
        reviewing.Steps.Single(s => s.Step == StepKind.Trim).State.ShouldBe(StepState.ReviewRequired);
        reviewing.CanManualCrop.ShouldBeFalse();

        OperationResult<SessionView> refused = await service.ExecuteAsync(
            id, new WorkflowCommand.SubmitManualCrop(StepKind.Trim, OperatorCrop), "tester", CancellationToken.None);

        refused.IsFailure.ShouldBeTrue();
        await AssertNoManualImportAsync(harness, id);
    }

    /// <summary>
    /// A crop against a step that failed for an unrelated reason is refused (§13).
    /// </summary>
    /// <remarks>
    /// The one case button visibility could never have covered, because the step state is the
    /// same one manual crop is offered in. Only the attempt's failure code separates them, which
    /// is why the rule lives where the attempts do.
    /// </remarks>
    [Fact]
    public async Task A_crop_against_an_unrelated_failure_is_refused()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId id = (await service.ImportAsync(
            WorkflowType.PrepareAsset, harness.WriteSourcePng("unrelated.png"), "unrelated", "tester",
            CancellationToken.None)).Value.Id;
        await Must(service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None));

        // Enhancement fails with an adapter error, which has nothing to do with cropping.
        harness.FakeMeitu.SetScenario(FakeAdapterScenario.FailWith(FailureCode.AdapterUnavailable));
        OperationResult<SessionView> failed = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None);
        failed.IsFailure.ShouldBeTrue();

        SessionView blocked = (await service.LoadAsync(id, CancellationToken.None)).Value;
        blocked.Steps.Single(s => s.Step == StepKind.Enhancement).State.ShouldBe(StepState.Failed);
        blocked.CurrentStepFailure.ShouldNotBe(FailureCode.ManualCropRequired);
        blocked.CanManualCrop.ShouldBeFalse();

        // Refused for the step it failed on, and refused for Trim, which is not even current.
        foreach (StepKind step in new[] { StepKind.Enhancement, StepKind.Trim })
        {
            OperationResult<SessionView> refused = await service.ExecuteAsync(
                id, new WorkflowCommand.SubmitManualCrop(step, OperatorCrop), "tester", CancellationToken.None);
            refused.IsFailure.ShouldBeTrue($"a crop of {step} must be refused here");
        }

        await AssertNoManualImportAsync(harness, id);
    }

    /// <summary>A crop against a completed session is refused (§13).</summary>
    [Fact]
    public async Task A_crop_against_a_completed_session_is_refused()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId id = await StartAtTrimAsync(service, harness.WriteBorderedSourcePng());
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Trim), "tester", CancellationToken.None));

        Sha256 trimmed = (await service.LoadAsync(id, CancellationToken.None))
            .Value.Steps.Single(s => s.Step == StepKind.Trim).CurrentRevisionSha256!.Value;
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.Approve(StepKind.Trim, trimmed), "tester", CancellationToken.None));
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.ApprovedPngExport), "tester", CancellationToken.None));
        await Must(service.ExecuteAsync(id, new WorkflowCommand.Complete(), "tester", CancellationToken.None));

        SessionView completed = (await service.LoadAsync(id, CancellationToken.None)).Value;
        completed.State.ShouldBe(SessionState.Completed);
        completed.CanManualCrop.ShouldBeFalse();

        OperationResult<SessionView> refused = await service.ExecuteAsync(
            id, new WorkflowCommand.SubmitManualCrop(StepKind.Trim, OperatorCrop), "tester", CancellationToken.None);

        refused.IsFailure.ShouldBeTrue();
        await AssertNoManualImportAsync(harness, id);
    }

    /// <summary>An empty rectangle is refused by the engine, before any file work (§23).</summary>
    [Fact]
    public async Task An_empty_rectangle_is_refused_by_the_workflow()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId id = await AtManualCropAsync(service, harness);

        OperationResult<SessionView> refused = await service.ExecuteAsync(
            id, new WorkflowCommand.SubmitManualCrop(StepKind.Trim, default), "tester", CancellationToken.None);

        refused.IsFailure.ShouldBeTrue();
        await AssertNoManualImportAsync(harness, id);

        // No attempt was even started for it.
        SessionAggregate aggregate = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        aggregate.Attempts.Count(a => a.Step == StepKind.Trim).ShouldBe(1);
    }

    /// <summary>
    /// A rectangle outside the source is refused, and the attempt records why (§23).
    /// </summary>
    /// <remarks>
    /// The engine cannot catch this one — a snapshot has no idea how large the image is — so it
    /// reaches the processor, which refuses it. The step goes back to Failed with the attempt
    /// retained, and the operator may simply draw again.
    /// </remarks>
    [Fact]
    public async Task A_rectangle_outside_the_source_fails_the_attempt_and_creates_no_Revision()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId id = await AtManualCropAsync(service, harness);

        OperationResult<SessionView> refused = await service.ExecuteAsync(
            id,
            new WorkflowCommand.SubmitManualCrop(StepKind.Trim, TrimBounds.FromEdges(50, 50, 90, 90)),
            "tester", CancellationToken.None);

        refused.IsFailure.ShouldBeTrue();
        refused.Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);
        await AssertNoManualImportAsync(harness, id);

        SessionAggregate aggregate = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        ProcessingAttempt failed = aggregate.Attempts
            .Single(a => a.Step == StepKind.Trim && a.AdapterId == "internal-manual-crop-v1");
        failed.Status.ShouldBe(AttemptStatus.Failed);
        failed.OutputRevisionId.ShouldBeNull();

        // Still eligible: the file has no alpha, so manual crop is still the way forward (§21).
        (await service.LoadAsync(id, CancellationToken.None)).Value.CanManualCrop.ShouldBeTrue();
    }

    // -----------------------------------------------------------------------------
    // §20, §21, §30: reject a crop, then crop again
    // -----------------------------------------------------------------------------

    /// <summary>
    /// Crop A → Reject → Crop B → Approve, with everything about A retained (§20, §21, §30).
    /// </summary>
    /// <remarks>
    /// The rejected crop is not a mistake to be tidied away: it is the record of a decision an
    /// operator made about a file, and the file it was made about has to still be there for that
    /// record to mean anything. The second crop is a separate attempt writing into its own
    /// directory, so it cannot overwrite the first even by accident.
    /// <para>
    /// The retry route is the point of §21. After rejecting a manual crop the operator is
    /// <b>not</b> sent back through the deterministic trim — it already said it cannot help with
    /// this file — so <c>CanManualCrop</c> stays true and the next crop needs no Retry first.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_rejected_manual_crop_is_retained_and_another_crop_may_follow()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId id = await AtManualCropAsync(service, harness);

        // Crop A.
        OperationResult<SessionView> cropA = await service.ExecuteAsync(
            id, new WorkflowCommand.SubmitManualCrop(StepKind.Trim, OperatorCrop), "tester", CancellationToken.None);
        cropA.IsSuccess.ShouldBeTrue(cropA.IsFailure ? cropA.Failure.ToString() : "");
        RevisionId revisionA = cropA.Value.Steps.Single(s => s.Step == StepKind.Trim).CurrentRevisionId!.Value;
        Sha256 hashA = cropA.Value.Steps.Single(s => s.Step == StepKind.Trim).CurrentRevisionSha256!.Value;

        // Rejected: the step reopens, and the corrective path stays manual (§21).
        OperationResult<SessionView> rejected = await service.ExecuteAsync(
            id,
            new WorkflowCommand.Reject(StepKind.Trim, hashA, RejectionReason.EdgeError, "cut too tight"),
            "tester", CancellationToken.None);
        rejected.IsSuccess.ShouldBeTrue(rejected.IsFailure ? rejected.Failure.ToString() : "");
        rejected.Value.Steps.Single(s => s.Step == StepKind.Trim).State.ShouldBe(StepState.RetryRequired);
        rejected.Value.CanManualCrop.ShouldBeTrue();

        // Crop B, with a different rectangle, and no Retry in between.
        TrimBounds second = TrimBounds.FromEdges(1, 1, 11, 9);
        OperationResult<SessionView> cropB = await service.ExecuteAsync(
            id, new WorkflowCommand.SubmitManualCrop(StepKind.Trim, second), "tester", CancellationToken.None);
        cropB.IsSuccess.ShouldBeTrue(cropB.IsFailure ? cropB.Failure.ToString() : "");

        SessionStep trim = cropB.Value.Steps.Single(s => s.Step == StepKind.Trim);
        trim.State.ShouldBe(StepState.ReviewRequired);
        RevisionId revisionB = trim.CurrentRevisionId!.Value;
        revisionB.ShouldNotBe(revisionA);

        SessionAggregate aggregate = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;

        // A is retained: the Revision, the rejection decision, and the file itself.
        Revision a = aggregate.Revisions.Single(r => r.Id == revisionA);
        Revision b = aggregate.Revisions.Single(r => r.Id == revisionB);
        a.Operation.ShouldBe(OperationKind.ManualImport);
        b.Operation.ShouldBe(OperationKind.ManualImport);
        File.Exists(harness.FileWorkspace.ResolveAbsolute(a.File)).ShouldBeTrue();
        File.Exists(harness.FileWorkspace.ResolveAbsolute(b.File)).ShouldBeTrue();
        aggregate.Reviews.ShouldContain(r =>
            r.Step == StepKind.Trim && r.SubjectId == revisionA.Value && !r.IsApproved);

        // The failed deterministic attempt is still there too, under all of it (§20).
        aggregate.Attempts.ShouldContain(x =>
            x.AdapterId == "internal-alpha-trim-v1" && x.Failure!.Code == FailureCode.ManualCropRequired);

        // Distinct attempts, each writing into its own directory: B cannot have overwritten A.
        List<ProcessingAttempt> manualAttempts =
            [.. aggregate.Attempts.Where(x => x.AdapterId == "internal-manual-crop-v1")];
        manualAttempts.Count.ShouldBe(2);
        AttemptId attemptA = manualAttempts.Single(x => x.OutputRevisionId == revisionA).Id;
        AttemptId attemptB = manualAttempts.Single(x => x.OutputRevisionId == revisionB).Id;
        b.File.RelativePath.ShouldContain(attemptB.Value.ToString("D"));
        b.File.RelativePath.ShouldNotContain(attemptA.Value.ToString("D"));

        // Different rectangles really did produce different files.
        b.Facts.Sha256.ShouldNotBe(a.Facts.Sha256);
        b.Facts.PixelWidth.ShouldBe(second.Width);
        b.Facts.PixelHeight.ShouldBe(second.Height);

        // Correct lineage on both: each hangs off the Revision that was cropped, not off A.
        RevisionId imported = aggregate.Revisions.Single(r => r.Operation == OperationKind.Import).Id;
        a.SourceRevisionId.ShouldBe(imported);
        b.SourceRevisionId.ShouldBe(imported);

        // The workflow continues from B alone.
        OperationResult<SessionView> approved = await service.ExecuteAsync(
            id, new WorkflowCommand.Approve(StepKind.Trim, b.Facts.Sha256), "tester", CancellationToken.None);
        approved.IsSuccess.ShouldBeTrue(approved.IsFailure ? approved.Failure.ToString() : "");
        approved.Value.Steps.Single(s => s.Step == StepKind.Trim).State.ShouldBe(StepState.Approved);

        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.ApprovedPngExport), "tester", CancellationToken.None));

        SessionAggregate exported = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        Revision promoted = exported.Revisions.Single(r => r.Operation == OperationKind.PromoteApproved);
        promoted.SourceRevisionId.ShouldBe(revisionB);
        promoted.Facts.Sha256.ShouldBe(b.Facts.Sha256);
    }

    /// <summary>
    /// Rejecting a <i>deterministic</i> trim does not open the manual-crop path (§21).
    /// </summary>
    /// <remarks>
    /// The other side of the rule above, and the one that keeps it narrow. A trim the operator
    /// disliked is an ordinary Retry case: the algorithm produced something, and running it
    /// again on a fresh copy is a sensible thing to offer. Only a file the algorithm said it
    /// could not handle earns the manual route.
    /// </remarks>
    [Fact]
    public async Task Rejecting_an_automatic_trim_leaves_the_ordinary_retry_path()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId id = await StartAtTrimAsync(service, harness.WriteBorderedSourcePng());
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Trim), "tester", CancellationToken.None));

        Sha256 hash = (await service.LoadAsync(id, CancellationToken.None))
            .Value.Steps.Single(s => s.Step == StepKind.Trim).CurrentRevisionSha256!.Value;

        OperationResult<SessionView> rejected = await service.ExecuteAsync(
            id,
            new WorkflowCommand.Reject(StepKind.Trim, hash, RejectionReason.EdgeError, "too tight"),
            "tester", CancellationToken.None);
        rejected.IsSuccess.ShouldBeTrue(rejected.IsFailure ? rejected.Failure.ToString() : "");

        rejected.Value.Steps.Single(s => s.Step == StepKind.Trim).State.ShouldBe(StepState.RetryRequired);
        rejected.Value.CanManualCrop.ShouldBeFalse();
        rejected.Value.AvailableCommands.ShouldContain(CommandKind.Retry);

        OperationResult<SessionView> refused = await service.ExecuteAsync(
            id, new WorkflowCommand.SubmitManualCrop(StepKind.Trim, OperatorCrop), "tester", CancellationToken.None);
        refused.IsFailure.ShouldBeTrue();
        await AssertNoManualImportAsync(harness, id);
    }

    // -----------------------------------------------------------------------------
    // §26: the hash is still the authority
    // -----------------------------------------------------------------------------

    /// <summary>
    /// A ManualImport whose bytes change after it was previewed still refuses approval (§26).
    /// </summary>
    /// <remarks>
    /// Manual crop introduces no exception to the integrity rule. The operator drew the
    /// rectangle, but what they approve is a file, and the SHA-256 recorded at validation is
    /// what that approval binds to.
    /// </remarks>
    [Fact]
    public async Task A_mutated_manual_crop_refuses_approval_with_RevisionIntegrityMismatch()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId id = await AtManualCropAsync(service, harness);

        OperationResult<SessionView> cropped = await service.ExecuteAsync(
            id, new WorkflowCommand.SubmitManualCrop(StepKind.Trim, OperatorCrop), "tester", CancellationToken.None);
        cropped.IsSuccess.ShouldBeTrue(cropped.IsFailure ? cropped.Failure.ToString() : "");

        SessionAggregate aggregate = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        Revision manual = aggregate.Revisions.Single(r => r.Operation == OperationKind.ManualImport);

        await File.WriteAllBytesAsync(
            harness.FileWorkspace.ResolveAbsolute(manual.File), SyntheticImages.Png(3, 2, alpha: true));

        OperationResult<SessionView> refused = await service.ExecuteAsync(
            id, new WorkflowCommand.Approve(StepKind.Trim, manual.Facts.Sha256), "tester", CancellationToken.None);

        refused.IsFailure.ShouldBeTrue();
        refused.Failure.Code.ShouldBe(FailureCode.RevisionIntegrityMismatch);

        SessionAggregate after = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        after.Steps.Single(s => s.Step == StepKind.Trim).State.ShouldNotBe(StepState.Approved);
        after.Revisions.Single(r => r.Id == manual.Id).IsValid.ShouldBeFalse();
    }

    // -----------------------------------------------------------------------------

    /// <summary>Imports <paramref name="source"/> and runs PREPARE_ASSET as far as Trim.</summary>
    /// <remarks>
    /// Both Meitu steps are skipped, which is the §28 scenario and also the honest one for a
    /// photograph that needs nothing but a crop. It means Trim's upstream is the imported
    /// original, so the lineage assertions have a single unambiguous answer.
    /// </remarks>
    private static async Task<SessionId> StartAtTrimAsync(ISessionService service, string source)
    {
        SessionId id = (await service.ImportAsync(
            WorkflowType.PrepareAsset, source, "manual-crop-test", "tester", CancellationToken.None)).Value.Id;

        await Must(service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None));
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.Skip(StepKind.Enhancement, "photo is already sharp"), "tester",
            CancellationToken.None));
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.Skip(StepKind.BackgroundRemoval, "background is wanted"), "tester",
            CancellationToken.None));

        return id;
    }

    /// <summary>Runs a no-alpha session up to the point where the automatic trim has refused.</summary>
    private static async Task<SessionId> AtManualCropAsync(
        ISessionService service, SessionServiceHarness harness)
    {
        SessionId id = await StartAtTrimAsync(service, harness.WriteOpaqueSourcePng());

        OperationResult<SessionView> automatic = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Trim), "tester", CancellationToken.None);
        automatic.IsFailure.ShouldBeTrue();
        automatic.Failure.Code.ShouldBe(FailureCode.ManualCropRequired);

        return id;
    }

    /// <summary>No crop happened: §15's "only after an actual cropped file exists".</summary>
    private static async Task AssertNoManualImportAsync(SessionServiceHarness harness, SessionId id)
    {
        SessionAggregate aggregate = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        aggregate.Revisions.ShouldNotContain(r => r.Operation == OperationKind.ManualImport);
    }

    private static async Task Must(Task<OperationResult<SessionView>> resultTask)
    {
        OperationResult<SessionView> result = await resultTask;
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : "");
    }
}
