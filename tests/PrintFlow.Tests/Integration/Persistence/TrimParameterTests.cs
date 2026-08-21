using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Reviews;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Trimming;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Persistence;

/// <summary>
/// Operator-chosen trim margins, from the command to the pixels and back out as audit
/// (Epic 11200 Part C3 §13–§16, §20, §21).
/// </summary>
/// <remarks>
/// The pure margin arithmetic already has exhaustive coverage in <c>TrimBoundsTests</c> and
/// <c>TrimMarginTests</c>. What is untested until here is the wiring: that a number the
/// operator typed survives a command, a database round trip and an attempt, and comes out as
/// the size of a real file on disk. Every assertion below is on pixel dimensions the
/// <c>FileInspector</c> read back, never on what the request object said (§20).
/// <para>
/// The trim processor is the real one, never a double — a scripted trim would make every
/// expectation here self-fulfilling (Part B §23).
/// </para>
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class TrimParameterTests
{
    // -----------------------------------------------------------------------------
    // §20: the margin actually reaches the processor
    // -----------------------------------------------------------------------------
    //
    // The source is 12×10 with a 5×5 opaque block from (3,2) to (7,6) inclusive — so the alpha
    // bounds are [3,2 → 8,7), leaving 3 px of transparency to the left, 4 to the right, 2 above
    // and 3 below. The border is asymmetric on every edge on purpose: a per-edge margin applied
    // to the wrong side, or with two edges transposed, produces a different rectangle rather
    // than an accidentally correct one.

    /// <summary>Tight crops exactly to the alpha content, which is the default (§10, §20).</summary>
    /// <remarks>
    /// Runs without issuing <c>SetTrimParameters</c> at all, which is the point: a session that
    /// never touches the margin controls must behave exactly as it did before this slice.
    /// </remarks>
    [Fact]
    public async Task Tight_is_the_default_and_crops_to_the_alpha_bounds()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await StartAtTrimAsync(service, harness.WriteBorderedSourcePng());

        SessionView started = await RunTrimAsync(service, id);

        started.TrimMargin.ShouldBe(TrimMargin.Tight);
        (await TrimmedSizeAsync(harness, id)).ShouldBe((5, 5));
    }

    /// <summary>A uniform margin expands the crop equally on all four edges (§11, §20).</summary>
    [Fact]
    public async Task A_uniform_margin_expands_every_edge_by_the_same_amount()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await StartAtTrimAsync(service, harness.WriteBorderedSourcePng());

        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.SetTrimParameters(TrimMargin.Uniform(2)), "tester", CancellationToken.None));

        await RunTrimAsync(service, id);

        // 5×5 plus 2 px on each of the four edges, and every one of them fits: 9×9.
        (await TrimmedSizeAsync(harness, id)).ShouldBe((9, 9));
    }

    /// <summary>
    /// A per-edge margin expands each edge by its own amount (§12, §20).
    /// </summary>
    /// <remarks>
    /// The four numbers are all different and all fit inside the available border — 1 ≤ 2 above,
    /// 3 ≤ 4 right, 2 ≤ 3 below, 3 ≤ 3 left — so nothing here is clamped and the result is the
    /// arithmetic alone. Clamping gets its own test below.
    /// </remarks>
    [Fact]
    public async Task An_edge_specific_margin_expands_each_edge_by_its_own_amount()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await StartAtTrimAsync(service, harness.WriteBorderedSourcePng());

        await Must(service.ExecuteAsync(
            id,
            new WorkflowCommand.SetTrimParameters(TrimMargin.PerEdge(top: 1, right: 3, bottom: 2, left: 3)),
            "tester", CancellationToken.None));

        await RunTrimAsync(service, id);

        // Width  = 5 + left 3 + right 3 = 11; Height = 5 + top 1 + bottom 2 = 8.
        (await TrimmedSizeAsync(harness, id)).ShouldBe((11, 8));
    }

    /// <summary>
    /// An absurd margin clamps to the source canvas rather than escaping it (§20).
    /// </summary>
    /// <remarks>
    /// The property that makes the margin controls safe to hand to an operator: whatever number
    /// is typed, the crop rectangle stays inside the image, so no caller downstream has to
    /// re-check it. A 500 px margin on a 12×10 canvas is the whole canvas and not an error
    /// (Part B §8).
    /// </remarks>
    [Fact]
    public async Task A_margin_larger_than_the_canvas_clamps_to_the_source()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await StartAtTrimAsync(service, harness.WriteBorderedSourcePng());

        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.SetTrimParameters(TrimMargin.Uniform(500)), "tester", CancellationToken.None));

        await RunTrimAsync(service, id);

        (await TrimmedSizeAsync(harness, id)).ShouldBe((12, 10));
    }

    // -----------------------------------------------------------------------------
    // §16: a full-canvas result is still an ordinary trim
    // -----------------------------------------------------------------------------

    /// <summary>
    /// A margin that fills the canvas still produces a Revision awaiting review (§16).
    /// </summary>
    /// <remarks>
    /// The clamped case above, asked as a workflow question rather than a pixel one. Nothing
    /// auto-skips a trim whose output happens to equal its input: the operator asked for a trim,
    /// a trim ran, and what it produced is theirs to approve. Collapsing it into a skip would
    /// leave the session with no Revision recording what happened (Part B §3).
    /// </remarks>
    [Fact]
    public async Task A_margin_that_fills_the_canvas_still_produces_a_Revision_to_review()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await StartAtTrimAsync(service, harness.WriteBorderedSourcePng());

        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.SetTrimParameters(TrimMargin.Uniform(500)), "tester", CancellationToken.None));

        SessionView started = await RunTrimAsync(service, id);

        started.Steps.Single(s => s.Step == StepKind.Trim).State.ShouldBe(StepState.ReviewRequired);

        SessionAggregate aggregate = await LoadAsync(harness, id);
        Revision trimmed = aggregate.Revisions.Single(r => r.Operation == OperationKind.Trim);
        trimmed.Facts.PixelWidth.ShouldBe(12);
        trimmed.Facts.PixelHeight.ShouldBe(10);

        // And it is still attributable: the attempt says which margin produced the full canvas.
        aggregate.Attempts.Single(a => a.Step == StepKind.Trim)
            .TrimParameters.ShouldBe(TrimMargin.Uniform(500));
    }

    // -----------------------------------------------------------------------------
    // §21: parameter history across a reject-and-retry
    // -----------------------------------------------------------------------------

    /// <summary>
    /// Two attempts at different margins keep their own settings, both auditable (§15, §21).
    /// </summary>
    /// <remarks>
    /// The whole point of recording parameters on the attempt rather than only on the session.
    /// The session ends up holding 1/2/3/4 because that is what the next run would use — but
    /// attempt A must still say Uniform 4, because that is what produced the file the operator
    /// rejected. If the two records were one, the rejected result would retroactively claim to
    /// have been made with settings it never saw.
    /// </remarks>
    [Fact]
    public async Task A_retry_at_a_different_margin_leaves_the_first_attempts_parameters_intact()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await StartAtTrimAsync(service, harness.WriteBorderedSourcePng());

        // Attempt A: Uniform 4. On a 12×10 canvas with content at [3,2 → 8,7) that clamps to
        // the whole canvas, which is a perfectly ordinary result to reject.
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.SetTrimParameters(TrimMargin.Uniform(4)), "tester", CancellationToken.None));
        SessionView afterA = await RunTrimAsync(service, id);

        SessionStep trimA = afterA.Steps.Single(s => s.Step == StepKind.Trim);
        RevisionId revisionA = trimA.CurrentRevisionId!.Value;

        // The review surface says how this one was made, while it is being reviewed (§18).
        afterA.CurrentTrimParameters.ShouldBe(TrimMargin.Uniform(4));

        await Must(service.ExecuteAsync(
            id,
            new WorkflowCommand.Reject(
                StepKind.Trim, trimA.CurrentRevisionSha256!.Value, RejectionReason.EdgeError, "too much air"),
            "tester", CancellationToken.None));
        await Must(service.ExecuteAsync(id, new WorkflowCommand.Retry(StepKind.Trim), "tester", CancellationToken.None));

        // Attempt B: a different margin entirely, chosen after seeing A.
        TrimMargin edges = TrimMargin.PerEdge(top: 1, right: 2, bottom: 3, left: 4);
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.SetTrimParameters(edges), "tester", CancellationToken.None));
        SessionView afterB = await RunTrimAsync(service, id);

        SessionStep trimB = afterB.Steps.Single(s => s.Step == StepKind.Trim);
        RevisionId revisionB = trimB.CurrentRevisionId!.Value;
        revisionB.ShouldNotBe(revisionA);

        SessionAggregate aggregate = await LoadAsync(harness, id);
        List<ProcessingAttempt> trims = [.. aggregate.Attempts.Where(a => a.Step == StepKind.Trim)];
        trims.Count.ShouldBe(2);

        // §21: A records Uniform 4, B records 1/2/3/4, and both remain readable.
        ProcessingAttempt attemptA = trims.Single(a => a.OutputRevisionId == revisionA);
        ProcessingAttempt attemptB = trims.Single(a => a.OutputRevisionId == revisionB);
        attemptA.TrimParameters.ShouldBe(TrimMargin.Uniform(4));
        attemptB.TrimParameters.ShouldBe(edges);

        // Both Revisions and both files survive; nothing was overwritten in place.
        aggregate.Revisions.ShouldContain(r => r.Id == revisionA);
        aggregate.Revisions.ShouldContain(r => r.Id == revisionB);
        System.IO.File.Exists(harness.FileWorkspace.ResolveAbsolute(
            aggregate.Revisions.Single(r => r.Id == revisionA).File)).ShouldBeTrue();

        // §21: Revision B is the active result, and the review line describes B's settings.
        trimB.State.ShouldBe(StepState.ReviewRequired);
        afterB.CurrentTrimParameters.ShouldBe(edges);

        // The rejection is retained as history alongside its own parameters.
        aggregate.Reviews.ShouldContain(r =>
            r.Step == StepKind.Trim && r.SubjectId == revisionA.Value && !r.IsApproved);
    }

    // -----------------------------------------------------------------------------
    // §13: the decision is persisted, not held by a screen
    // -----------------------------------------------------------------------------

    /// <summary>
    /// A margin set now is still the margin after the application is restarted (§13).
    /// </summary>
    /// <remarks>
    /// A second <see cref="ISessionService"/> over the same database and workspace — what
    /// reopening the application does. If the decision lived only in a view model, the trim
    /// after a restart would silently run tight.
    /// </remarks>
    [Fact]
    public async Task The_chosen_margin_survives_a_restart_and_is_what_actually_runs()
    {
        using SessionServiceHarness harness = new();
        ISessionService first = harness.CreateService();
        SessionId id = await StartAtTrimAsync(first, harness.WriteBorderedSourcePng());

        await Must(first.ExecuteAsync(
            id, new WorkflowCommand.SetTrimParameters(TrimMargin.Uniform(2)), "tester", CancellationToken.None));

        ISessionService restarted = harness.CreateService();
        SessionView reloaded = (await restarted.LoadAsync(id, CancellationToken.None)).Value;
        reloaded.TrimMargin.ShouldBe(TrimMargin.Uniform(2));

        await RunTrimAsync(restarted, id);
        (await TrimmedSizeAsync(harness, id)).ShouldBe((9, 9));
    }

    /// <summary>
    /// A uniform zero is recorded as Uniform, not silently rewritten to Tight (§9).
    /// </summary>
    /// <remarks>
    /// The two produce an identical rectangle and mean different things to the person who chose
    /// them. Collapsing them would make the audit record answer a question the operator did not
    /// ask, and would make the review line and the mode selector disagree.
    /// </remarks>
    [Fact]
    public async Task A_uniform_zero_margin_keeps_its_mode()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await StartAtTrimAsync(service, harness.WriteBorderedSourcePng());

        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.SetTrimParameters(TrimMargin.Uniform(0)), "tester", CancellationToken.None));

        await RunTrimAsync(service, id);

        (await TrimmedSizeAsync(harness, id)).ShouldBe((5, 5));

        SessionAggregate aggregate = await LoadAsync(harness, id);
        TrimMargin recorded = aggregate.Attempts.Single(a => a.Step == StepKind.Trim).TrimParameters!.Value;
        recorded.Mode.ShouldBe(TrimMode.UniformMargin);
        recorded.IsNone.ShouldBeTrue();
    }

    // -----------------------------------------------------------------------------
    // §17: the manual-crop path carries no margin
    // -----------------------------------------------------------------------------

    /// <summary>
    /// A file the automatic trim refused is offered no margin controls (§17).
    /// </summary>
    /// <remarks>
    /// Adding margin to a crop that was never decided cannot help, so a control suggesting it
    /// might would be actively misleading. Both halves are asserted: the read model withdraws
    /// the offer, and the command is refused if something issues one anyway — button visibility
    /// is never the guard.
    /// </remarks>
    [Fact]
    public async Task A_manual_crop_outcome_withdraws_the_margin_controls_and_refuses_the_command()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await StartAtTrimAsync(service, harness.WriteOpaqueSourcePng());

        // The margin controls are offered while the trim is still about to run.
        SessionView beforeRun = (await service.LoadAsync(id, CancellationToken.None)).Value;
        beforeRun.CanSetTrimParameters.ShouldBeTrue();

        OperationResult<SessionView> refusedTrim = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Trim), "tester", CancellationToken.None);
        refusedTrim.Failure.Code.ShouldBe(FailureCode.ManualCropRequired);

        SessionView afterRefusal = (await service.LoadAsync(id, CancellationToken.None)).Value;
        afterRefusal.CanManualCrop.ShouldBeTrue();
        afterRefusal.CanSetTrimParameters.ShouldBeFalse();

        // The manual crop that follows records no trim parameters of its own.
        await Must(service.ExecuteAsync(
            id,
            new WorkflowCommand.SubmitManualCrop(StepKind.Trim, TrimBounds.FromEdges(3, 2, 9, 7)),
            "tester", CancellationToken.None));

        SessionAggregate aggregate = await LoadAsync(harness, id);
        aggregate.Attempts
            .Single(a => a.Operation == OperationKind.ManualImport)
            .TrimParameters.ShouldBeNull();

        // Nor does the review line claim any: a crop the operator drew has no automatic margin.
        (await service.LoadAsync(id, CancellationToken.None)).Value
            .HasTrimParameters.ShouldBeFalse();
    }

    /// <summary>The failed deterministic attempt still records what it was asked to do (§14).</summary>
    /// <remarks>
    /// A refusal is an attempt that happened, and "which margin was in force when the alpha scan
    /// gave up" is part of answering why. Recording it is free — the row is written before the
    /// scan runs — and its absence would leave a gap in the one history that explains a
    /// <c>ManualCropRequired</c>.
    /// </remarks>
    [Fact]
    public async Task A_refused_deterministic_attempt_still_records_its_margin()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await StartAtTrimAsync(service, harness.WriteOpaqueSourcePng());

        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.SetTrimParameters(TrimMargin.Uniform(3)), "tester", CancellationToken.None));

        await service.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.Trim), "tester", CancellationToken.None);

        SessionAggregate aggregate = await LoadAsync(harness, id);
        ProcessingAttempt refused = aggregate.Attempts.Single(a => a.Step == StepKind.Trim);
        refused.Status.ShouldBe(AttemptStatus.Failed);
        refused.Failure!.Code.ShouldBe(FailureCode.ManualCropRequired);
        refused.TrimParameters.ShouldBe(TrimMargin.Uniform(3));
    }

    // -----------------------------------------------------------------------------
    // §13: the command is a decision, and is refused outside its window
    // -----------------------------------------------------------------------------

    /// <summary>Setting a margin creates no attempt, no Revision and no file (§13).</summary>
    [Fact]
    public async Task Setting_a_margin_starts_nothing()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await StartAtTrimAsync(service, harness.WriteBorderedSourcePng());

        SessionAggregate before = await LoadAsync(harness, id);

        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.SetTrimParameters(TrimMargin.Uniform(6)), "tester", CancellationToken.None));

        SessionAggregate after = await LoadAsync(harness, id);
        after.Attempts.Count.ShouldBe(before.Attempts.Count);
        after.Revisions.Count.ShouldBe(before.Revisions.Count);
        after.Steps.ShouldBe(before.Steps);
        after.Session.TrimMargin.ShouldBe(TrimMargin.Uniform(6));
    }

    /// <summary>
    /// A margin cannot be recorded against a result already awaiting review (§13).
    /// </summary>
    /// <remarks>
    /// The window is "between attempts", and this is why it has to be. Accepting one here would
    /// leave the session claiming a margin the file on screen was not made with, while the
    /// attempt row said something else — precisely the drift the attempt-level record exists to
    /// prevent. Rejecting the result first reopens the window, which the retry test above uses.
    /// </remarks>
    [Fact]
    public async Task A_margin_cannot_be_changed_while_a_result_awaits_review()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await StartAtTrimAsync(service, harness.WriteBorderedSourcePng());

        SessionView reviewing = await RunTrimAsync(service, id);
        reviewing.Steps.Single(s => s.Step == StepKind.Trim).State.ShouldBe(StepState.ReviewRequired);
        reviewing.CanSetTrimParameters.ShouldBeFalse();

        OperationResult<SessionView> refused = await service.ExecuteAsync(
            id, new WorkflowCommand.SetTrimParameters(TrimMargin.Uniform(9)), "tester", CancellationToken.None);

        refused.IsFailure.ShouldBeTrue();
        refused.Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);
        (await LoadAsync(harness, id)).Session.TrimMargin.ShouldBe(TrimMargin.Tight);
    }

    /// <summary>A workflow with no Trim step has no trim margin to set (§13).</summary>
    [Fact]
    public async Task A_workflow_without_a_Trim_step_refuses_the_command()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId id = (await service.ImportAsync(
            WorkflowType.GeneratePrintTiff, harness.WriteSourcePng(), "no-trim", "tester",
            CancellationToken.None)).Value.Id;
        await Must(service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None));

        OperationResult<SessionView> refused = await service.ExecuteAsync(
            id, new WorkflowCommand.SetTrimParameters(TrimMargin.Uniform(2)), "tester", CancellationToken.None);

        refused.IsFailure.ShouldBeTrue();
        (await service.LoadAsync(id, CancellationToken.None)).Value.CanSetTrimParameters.ShouldBeFalse();
    }

    // -----------------------------------------------------------------------------

    /// <summary>The pixel size of the newest Trim Revision, read back off the inspected file.</summary>
    private static async Task<(int Width, int Height)> TrimmedSizeAsync(
        SessionServiceHarness harness, SessionId id)
    {
        SessionAggregate aggregate = await LoadAsync(harness, id);
        Revision trimmed = aggregate.Revisions
            .Where(r => r.Operation == OperationKind.Trim)
            .OrderByDescending(r => r.CreatedAtUtc)
            .First();

        System.IO.File.Exists(harness.FileWorkspace.ResolveAbsolute(trimmed.File)).ShouldBeTrue();
        return (trimmed.Facts.PixelWidth!.Value, trimmed.Facts.PixelHeight!.Value);
    }

    private static async Task<SessionAggregate> LoadAsync(SessionServiceHarness harness, SessionId id) =>
        (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;

    private static async Task<SessionView> RunTrimAsync(ISessionService service, SessionId id)
    {
        OperationResult<SessionView> started = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Trim), "tester", CancellationToken.None);
        started.IsSuccess.ShouldBeTrue(started.IsFailure ? started.Failure.ToString() : "");
        return started.Value;
    }

    /// <summary>Imports <paramref name="source"/> and runs PREPARE_ASSET as far as Trim.</summary>
    /// <remarks>
    /// Both Meitu steps are skipped, so Trim consumes the imported original and the expected
    /// rectangles below are arithmetic on a source this file describes rather than on whatever
    /// the fake adapters produced.
    /// </remarks>
    private static async Task<SessionId> StartAtTrimAsync(ISessionService service, string source)
    {
        SessionId id = (await service.ImportAsync(
            WorkflowType.PrepareAsset, source, "margin-test", "tester", CancellationToken.None)).Value.Id;

        await Must(service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None));
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.Skip(StepKind.Enhancement, "already sharp"), "tester", CancellationToken.None));
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.Skip(StepKind.BackgroundRemoval, "already cut out"), "tester", CancellationToken.None));

        return id;
    }

    private static async Task Must(Task<OperationResult<SessionView>> resultTask)
    {
        OperationResult<SessionView> result = await resultTask;
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : "");
    }
}
