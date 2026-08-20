using System.IO;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Reviews;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Persistence;

/// <summary>
/// The Trim step driven end to end by <see cref="SessionService"/> with the real deterministic
/// processor, a real workspace and a real database (Epic 11200 Part B §23–§25).
/// </summary>
/// <remarks>
/// The point of these, over the processor's own tests, is the orchestration: that a real
/// cropped file reaches <c>FileInspector</c>, gets hashed, becomes a
/// <c>Revision(OperationKind.Trim)</c> bound to the right upstream, and enters the same review
/// lifecycle every other reviewed step uses — and that a manual-crop outcome does none of
/// those things.
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class TrimStepTests
{
    // -----------------------------------------------------------------------------
    // §23: a real PREPARE_ASSET flow through Trim
    // -----------------------------------------------------------------------------

    [Fact]
    public async Task Trim_produces_a_real_cropped_file_hashed_into_a_Revision_awaiting_review()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId id = await StartAtTrimAsync(service, harness.WriteBorderedSourcePng());

        OperationResult<SessionView> started = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Trim), "tester", CancellationToken.None);
        started.IsSuccess.ShouldBeTrue(started.IsFailure ? started.Failure.ToString() : "");

        SessionStep trim = started.Value.Steps.Single(s => s.Step == StepKind.Trim);
        trim.State.ShouldBe(StepState.ReviewRequired);

        SessionAggregate aggregate = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        Revision trimmed = aggregate.Revisions.Single(r => r.Operation == OperationKind.Trim);
        trimmed.Id.ShouldBe(trim.CurrentRevisionId!.Value);

        // The file on disk is the cropped result, not a pass-through of the working copy.
        string absolute = harness.FileWorkspace.ResolveAbsolute(trimmed.File);
        File.Exists(absolute).ShouldBeTrue();
        trimmed.File.FileName.ShouldBe("trimmed.png");
        trimmed.File.Area.ShouldBe(WorkspaceArea.Working);
        trimmed.Facts.Format.ShouldBe(ImageFormat.Png);
        trimmed.Facts.PixelWidth.ShouldBe(5);
        trimmed.Facts.PixelHeight.ShouldBe(5);
        trimmed.Facts.HasAlpha.ShouldBe(true);

        // Hash-bound review, on the bytes actually written.
        trim.CurrentRevisionSha256!.Value.ShouldBe(trimmed.Facts.Sha256);

        // §23: the Revision hangs off the approved upstream result, not off the raw import.
        RevisionId upstream = aggregate.Revisions
            .Single(r => r.Operation == OperationKind.RemoveBackground).Id;
        trimmed.SourceRevisionId.ShouldBe(upstream);

        // The attempt records which algorithm ran, so a fake could never masquerade as this one.
        ProcessingAttempt attempt = aggregate.Attempts.Single(a => a.Step == StepKind.Trim);
        attempt.Status.ShouldBe(AttemptStatus.Succeeded);
        attempt.AdapterId.ShouldBe("internal-alpha-trim-v1");
        attempt.OutputRevisionId.ShouldBe(trimmed.Id);

        OperationResult<SessionView> approved = await service.ExecuteAsync(
            id, new WorkflowCommand.Approve(StepKind.Trim, trimmed.Facts.Sha256), "tester", CancellationToken.None);
        approved.IsSuccess.ShouldBeTrue(approved.IsFailure ? approved.Failure.ToString() : "");
        approved.Value.Steps.Single(s => s.Step == StepKind.Trim).State.ShouldBe(StepState.Approved);
    }

    /// <summary>Trim takes no automation lock: it drives no external application.</summary>
    /// <remarks>
    /// Worth asserting rather than assuming. Taking the lock for in-process pixel work would
    /// serialise every workstation's trims behind whichever session happened to be running one,
    /// for no reason at all (§17).
    /// </remarks>
    [Fact]
    public async Task Trim_never_acquires_the_external_automation_lock()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId id = await StartAtTrimAsync(service, harness.WriteBorderedSourcePng());

        OperationResult<SessionView> started = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Trim), "tester", CancellationToken.None);
        started.IsSuccess.ShouldBeTrue(started.IsFailure ? started.Failure.ToString() : "");

        AutomationLockState state = (await harness.Repository.GetAutomationLockAsync(CancellationToken.None)).Value;
        state.IsHeld.ShouldBeFalse();
    }

    /// <summary>An already-full canvas still produces a Revision and a review, never a silent skip.</summary>
    [Fact]
    public async Task Content_already_filling_the_canvas_still_produces_a_Trim_Revision_to_review()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        // The default synthetic source is opaque edge to edge.
        SessionId id = await StartAtTrimAsync(service, harness.WriteSourcePng());

        OperationResult<SessionView> started = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Trim), "tester", CancellationToken.None);
        started.IsSuccess.ShouldBeTrue(started.IsFailure ? started.Failure.ToString() : "");
        started.Value.Steps.Single(s => s.Step == StepKind.Trim).State.ShouldBe(StepState.ReviewRequired);

        SessionAggregate aggregate = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        Revision trimmed = aggregate.Revisions.Single(r => r.Operation == OperationKind.Trim);
        trimmed.Facts.PixelWidth.ShouldBe(6);
        trimmed.Facts.PixelHeight.ShouldBe(5);
    }

    // -----------------------------------------------------------------------------
    // §24: skip fall-through
    // -----------------------------------------------------------------------------

    /// <summary>
    /// With both Meitu steps skipped, Trim consumes the imported original.
    /// </summary>
    /// <remarks>
    /// No special case exists for this in <see cref="SessionService"/>, and that is the point:
    /// <c>WorkflowSnapshot.UpstreamRevisionOf</c> already answers "the newest approved Revision
    /// at or before this step", and a skipped step produces none. Asserting it here proves the
    /// deterministic trim inherited that answer rather than reaching for the current step's own
    /// input by hand.
    /// </remarks>
    [Fact]
    public async Task Skipped_upstream_steps_leave_Trim_working_from_the_imported_original()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        string source = harness.WriteBorderedSourcePng();

        SessionId id = (await service.ImportAsync(
            WorkflowType.PrepareAsset, source, "skipped", "tester", CancellationToken.None)).Value.Id;
        await Must(service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None));
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.Skip(StepKind.Enhancement, "already sharp"), "tester", CancellationToken.None));
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.Skip(StepKind.BackgroundRemoval, "already cut out"), "tester", CancellationToken.None));

        OperationResult<SessionView> started = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Trim), "tester", CancellationToken.None);
        started.IsSuccess.ShouldBeTrue(started.IsFailure ? started.Failure.ToString() : "");

        SessionAggregate aggregate = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        Revision imported = aggregate.Revisions.Single(r => r.Operation == OperationKind.Import);
        Revision trimmed = aggregate.Revisions.Single(r => r.Operation == OperationKind.Trim);

        trimmed.SourceRevisionId.ShouldBe(imported.Id);
        trimmed.Facts.PixelWidth.ShouldBe(5);
        trimmed.Facts.PixelHeight.ShouldBe(5);
    }

    // -----------------------------------------------------------------------------
    // §25: reject then retry
    // -----------------------------------------------------------------------------

    [Fact]
    public async Task Rejecting_a_trim_and_retrying_produces_a_second_independent_result()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId id = await StartAtTrimAsync(service, harness.WriteBorderedSourcePng());

        OperationResult<SessionView> startedA = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Trim), "tester", CancellationToken.None);
        startedA.IsSuccess.ShouldBeTrue(startedA.IsFailure ? startedA.Failure.ToString() : "");
        SessionStep stepA = startedA.Value.Steps.Single(s => s.Step == StepKind.Trim);
        RevisionId revisionIdA = stepA.CurrentRevisionId!.Value;

        OperationResult<SessionView> rejected = await service.ExecuteAsync(
            id,
            new WorkflowCommand.Reject(
                StepKind.Trim, stepA.CurrentRevisionSha256!.Value, RejectionReason.EdgeError, "clipped a shadow"),
            "tester", CancellationToken.None);
        rejected.IsSuccess.ShouldBeTrue(rejected.IsFailure ? rejected.Failure.ToString() : "");
        rejected.Value.Steps.Single(s => s.Step == StepKind.Trim).State.ShouldBe(StepState.RetryRequired);

        await Must(service.ExecuteAsync(id, new WorkflowCommand.Retry(StepKind.Trim), "tester", CancellationToken.None));

        OperationResult<SessionView> startedB = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Trim), "tester", CancellationToken.None);
        startedB.IsSuccess.ShouldBeTrue(startedB.IsFailure ? startedB.Failure.ToString() : "");
        RevisionId revisionIdB = startedB.Value.Steps.Single(s => s.Step == StepKind.Trim).CurrentRevisionId!.Value;

        revisionIdA.ShouldNotBe(revisionIdB);

        SessionAggregate aggregate = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        List<ProcessingAttempt> attempts = [.. aggregate.Attempts.Where(a => a.Step == StepKind.Trim)];
        attempts.Count.ShouldBe(2);
        attempts.Select(a => a.Id).Distinct().Count().ShouldBe(2);

        Revision revisionA = aggregate.Revisions.Single(r => r.Id == revisionIdA);
        Revision revisionB = aggregate.Revisions.Single(r => r.Id == revisionIdB);

        // The retry ran in its own Working\<attemptId>\ directory. The output of the first
        // attempt is never re-read or renamed into the second's place (§19).
        AttemptId attemptB = attempts.Single(a => a.OutputRevisionId == revisionIdB).Id;
        AttemptId attemptA = attempts.Single(a => a.OutputRevisionId == revisionIdA).Id;
        revisionB.File.RelativePath.ShouldContain(attemptB.Value.ToString("D"));
        revisionB.File.RelativePath.ShouldNotContain(attemptA.Value.ToString("D"));

        // Both files survive for comparison, and both review records are retained.
        File.Exists(harness.FileWorkspace.ResolveAbsolute(revisionA.File)).ShouldBeTrue();
        File.Exists(harness.FileWorkspace.ResolveAbsolute(revisionB.File)).ShouldBeTrue();
        aggregate.Reviews.ShouldContain(r =>
            r.Step == StepKind.Trim && r.SubjectId == revisionIdA.Value && !r.IsApproved);

        // A deterministic algorithm on unchanged input gives the same pixels; the two Revisions
        // are distinguished by identity and attempt, never by hoping the bytes differ.
        revisionB.Facts.Sha256.ShouldBe(revisionA.Facts.Sha256);
        revisionB.SourceRevisionId.ShouldBe(revisionA.SourceRevisionId);
    }

    // -----------------------------------------------------------------------------
    // §10: the manual-crop boundary, as the session sees it
    // -----------------------------------------------------------------------------

    /// <summary>
    /// A file with nothing to crop to fails the step honestly and fabricates nothing.
    /// </summary>
    /// <remarks>
    /// The assertions that matter are the negative ones. No <c>Revision</c> of any kind is
    /// written — not a Trim of the untrimmed working copy, and not a <c>ManualImport</c> of a
    /// file no human has touched yet — because either would record work that did not happen.
    /// </remarks>
    [Fact]
    public async Task A_source_with_no_alpha_content_fails_the_step_without_fabricating_a_Revision()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId id = await StartAtTrimAsync(service, harness.WriteFullyTransparentSourcePng());

        OperationResult<SessionView> started = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Trim), "tester", CancellationToken.None);

        started.IsFailure.ShouldBeTrue();
        started.Failure.Code.ShouldBe(FailureCode.ManualCropRequired);

        // Deterministic: pressing Retry cannot change the answer, and the failure says so.
        started.Failure.IsRetryable.ShouldBeFalse();

        SessionAggregate aggregate = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        aggregate.Revisions.ShouldNotContain(r => r.Operation == OperationKind.Trim);
        aggregate.Revisions.ShouldNotContain(r => r.Operation == OperationKind.ManualImport);

        // The attempt is retained as an audit record of the refusal, with its reason.
        ProcessingAttempt attempt = aggregate.Attempts.Single(a => a.Step == StepKind.Trim);
        attempt.Status.ShouldBe(AttemptStatus.Failed);
        attempt.OutputRevisionId.ShouldBeNull();
        attempt.Failure!.Code.ShouldBe(FailureCode.ManualCropRequired);

        aggregate.Steps.Single(s => s.Step == StepKind.Trim).State.ShouldBe(StepState.Failed);
        aggregate.Session.State.ShouldBe(SessionState.Active);
    }

    /// <summary>Refusing to trim never ends the session on the operator's behalf.</summary>
    /// <remarks>
    /// Part B implements the representation only. Handing the whole session off, or opening a
    /// manual-import flow, is a decision the operator makes on a surface that does not exist
    /// yet (Epic 11200 Part C), so the session stays exactly where it is.
    /// </remarks>
    [Fact]
    public async Task A_manual_crop_outcome_does_not_hand_the_session_off_by_itself()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId id = await StartAtTrimAsync(service, harness.WriteFullyTransparentSourcePng());
        await service.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.Trim), "tester", CancellationToken.None);

        SessionView reloaded = (await service.LoadAsync(id, CancellationToken.None)).Value;

        reloaded.State.ShouldBe(SessionState.Active);
        reloaded.Steps.Single(s => s.Step == StepKind.ApprovedPngExport).State.ShouldBe(StepState.Waiting);
    }

    // -----------------------------------------------------------------------------

    /// <summary>Imports <paramref name="source"/> and runs PREPARE_ASSET as far as Trim.</summary>
    private static async Task<SessionId> StartAtTrimAsync(ISessionService service, string source)
    {
        SessionId id = (await service.ImportAsync(
            WorkflowType.PrepareAsset, source, "trim-test", "tester", CancellationToken.None)).Value.Id;

        await Must(service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None));
        await RunAndApprove(service, id, StepKind.Enhancement);
        await RunAndApprove(service, id, StepKind.BackgroundRemoval);
        return id;
    }

    private static async Task RunAndApprove(ISessionService service, SessionId id, StepKind step)
    {
        OperationResult<SessionView> started =
            await service.ExecuteAsync(id, new WorkflowCommand.StartStep(step), "tester", CancellationToken.None);
        started.IsSuccess.ShouldBeTrue(started.IsFailure ? started.Failure.ToString() : "");

        Sha256 hash = started.Value.Steps.Single(s => s.Step == step).CurrentRevisionSha256!.Value;
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.Approve(step, hash), "tester", CancellationToken.None));
    }

    private static async Task Must(Task<OperationResult<SessionView>> resultTask)
    {
        OperationResult<SessionView> result = await resultTask;
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : "");
    }
}
