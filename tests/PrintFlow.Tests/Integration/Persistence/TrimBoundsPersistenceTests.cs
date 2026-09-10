using Microsoft.Data.Sqlite;
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
/// The crop geometry one automatic trim established, from the processor to the database and back
/// out as operator-visible audit (SCRUM-11081).
/// </summary>
/// <remarks>
/// The trimming algorithm itself is not under test here — <c>AlphaBoundsTests</c>,
/// <c>TrimBoundsTests</c> and <c>DeterministicTrimTests</c> cover it exhaustively, and nothing in
/// this slice changed it. What is under test is the wiring that used to lose its answer: the two
/// rectangles the processor returned were dropped between <c>TrimResult</c> and the attempt's
/// closing transaction, so a Trim Revision could say how large it was but nothing could say which
/// part of the source it came from.
/// <para>
/// The trim processor is the real one throughout, never a double, for the reason
/// <c>TrimParameterTests</c> states: a scripted trim would make every expectation here
/// self-fulfilling.
/// </para>
/// <para>
/// <b>The fixture.</b> <c>WriteBorderedSourcePng</c> is 12×10 with a 5×5 opaque block from (3,2)
/// to (7,6) inclusive, so the alpha bounds are the half-open <c>[3,2 → 8,7)</c> — 3&#160;px of
/// transparency to the left, 4 to the right, 2 above and 3 below. Every border is a different
/// width on purpose: a rectangle applied to the wrong edge, or with two edges transposed, comes
/// out visibly wrong rather than accidentally right.
/// </para>
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class TrimBoundsPersistenceTests
{
    /// <summary>The detected content rectangle for the bordered fixture, before any margin.</summary>
    private static readonly TrimBounds Content = TrimBounds.FromEdges(3, 2, 8, 7);

    // -----------------------------------------------------------------------------
    // §4, §5, §6: the rectangles reach the producing attempt and survive SQLite
    // -----------------------------------------------------------------------------

    /// <summary>
    /// A successful automatic trim records what the alpha scan found and what was cropped (§4, §5).
    /// </summary>
    /// <remarks>
    /// The one assertion this whole slice exists for. Before it, the processor computed both
    /// rectangles, cropped to the second, and the orchestrator threw them away on the way to
    /// <c>InspectAsync</c>.
    /// </remarks>
    [Fact]
    public async Task A_successful_automatic_trim_persists_both_rectangles_on_the_producing_attempt()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await StartAtTrimAsync(service, harness.WriteBorderedSourcePng());

        await RunTrimAsync(service, id);

        ProcessingAttempt trim = await SucceededTrimAttemptAsync(harness, id);
        TrimGeometry geometry = trim.TrimGeometry.ShouldNotBeNull();

        geometry.ContentBounds.ShouldBe(Content);
        geometry.AppliedBounds.ShouldBe(Content, "a tight trim applies exactly what it detected");
    }

    /// <summary>
    /// The stored rectangles are the four edges themselves, not a size (§3, §7).
    /// </summary>
    /// <remarks>
    /// Read straight out of SQLite rather than through the mapper, because the mapper is one of
    /// the things being checked. Right and bottom are stored exclusive — <c>TrimBounds</c>'s own
    /// convention, unconverted — so 8 − 3 is the width with no correction, which is the property
    /// migration 0009 refuses to trade away for a storage-only inclusive spelling.
    /// </remarks>
    [Fact]
    public async Task The_stored_columns_hold_all_four_edges_in_the_half_open_convention()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await StartAtTrimAsync(service, harness.WriteBorderedSourcePng());

        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.SetTrimParameters(TrimMargin.Uniform(2)), "tester", CancellationToken.None));
        await RunTrimAsync(service, id);

        ProcessingAttempt trim = await SucceededTrimAttemptAsync(harness, id);

        using SqliteConnection connection = harness.Database.Factory.Open();
        using SqliteCommand read = connection.CreateCommand();
        read.CommandText =
            """
            SELECT TrimContentLeft, TrimContentTop, TrimContentRight, TrimContentBottom,
                   TrimAppliedLeft, TrimAppliedTop, TrimAppliedRight, TrimAppliedBottom
            FROM ProcessingAttempt WHERE Id = $id;
            """;
        read.Parameters.AddWithValue("$id", trim.Id.ToString());

        using SqliteDataReader row = read.ExecuteReader();
        row.Read().ShouldBeTrue();

        // Content [3,2 -> 8,7), applied [1,0 -> 10,9): 2 px on every edge, clamped nowhere
        // except the top, where only 2 px of border existed and exactly 2 were asked for.
        (row.GetInt32(0), row.GetInt32(1), row.GetInt32(2), row.GetInt32(3)).ShouldBe((3, 2, 8, 7));
        (row.GetInt32(4), row.GetInt32(5), row.GetInt32(6), row.GetInt32(7)).ShouldBe((1, 0, 10, 9));
    }

    /// <summary>
    /// Every value comes back off disk unchanged, through a service that never saw the run (§16).
    /// </summary>
    /// <remarks>
    /// A second <see cref="ISessionService"/> over the same database is what reopening the
    /// application does. Nothing is recomputed: the trim processor is not invoked at all on this
    /// path, so the rectangles the read model reports can only have come from persistence.
    /// </remarks>
    [Fact]
    public async Task The_rectangles_survive_a_restart_without_being_recomputed()
    {
        using SessionServiceHarness harness = new();
        ISessionService first = harness.CreateService();
        SessionId id = await StartAtTrimAsync(first, harness.WriteBorderedSourcePng());

        await Must(first.ExecuteAsync(
            id, new WorkflowCommand.SetTrimParameters(TrimMargin.PerEdge(top: 1, right: 3, bottom: 2, left: 3)),
            "tester", CancellationToken.None));
        SessionView produced = await RunTrimAsync(first, id);

        TrimGeometry before = produced.CurrentTrimGeometry.ShouldNotBeNull();
        Sha256 producedHash = produced.CurrentArtefact!.Sha256;

        ISessionService restarted = harness.CreateService();
        SessionView reopened = (await restarted.LoadAsync(id, CancellationToken.None)).Value;

        reopened.CurrentTrimGeometry.ShouldBe(before);
        reopened.CurrentArtefact!.Sha256.ShouldBe(producedHash, "the same Trim Revision, byte for byte");
        reopened.CurrentTrimParameters.ShouldBe(TrimMargin.PerEdge(top: 1, right: 3, bottom: 2, left: 3));
    }

    // -----------------------------------------------------------------------------
    // §5, §18: the applied rectangle is the size of the file that was produced
    // -----------------------------------------------------------------------------

    /// <summary>
    /// The output's pixel dimensions are the applied rectangle's, for every margin shape (§5, §18).
    /// </summary>
    /// <remarks>
    /// The invariant that ties the metadata to a real artefact. Both sides are measured rather
    /// than asserted from the request: the left side is what the closing transaction stored, and
    /// the right is what the file inspector read back off the PNG on disk.
    /// <para>
    /// The clamped case is the one worth having. A 500&#160;px margin on a 12×10 canvas is the
    /// whole canvas, so the applied rectangle is <c>[0,0 → 12,10)</c> and the output is 12×10 —
    /// a rectangle that is <i>not</i> content plus margin, and would not survive being
    /// reconstructed by arithmetic.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(0, 0, 0, 0, 0)]       // Tight
    [InlineData(2, 2, 2, 2, 2)]       // Uniform, nothing clamped
    [InlineData(1, 3, 2, 3, -1)]      // Per-edge, nothing clamped
    [InlineData(500, 500, 500, 500, 500)] // Clamped to the whole canvas on all four edges
    public async Task The_output_dimensions_equal_the_applied_rectangle(
        int top, int right, int bottom, int left, int uniform)
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await StartAtTrimAsync(service, harness.WriteBorderedSourcePng());

        TrimMargin margin = uniform switch
        {
            0 => TrimMargin.Tight,
            < 0 => TrimMargin.PerEdge(top, right, bottom, left),
            _ => TrimMargin.Uniform(uniform),
        };

        if (margin != TrimMargin.Tight)
        {
            await Must(service.ExecuteAsync(
                id, new WorkflowCommand.SetTrimParameters(margin), "tester", CancellationToken.None));
        }

        await RunTrimAsync(service, id);

        ProcessingAttempt trim = await SucceededTrimAttemptAsync(harness, id);
        TrimBounds applied = trim.TrimGeometry.ShouldNotBeNull().AppliedBounds;

        Revision output = (await LoadAsync(harness, id)).Revisions.Single(r => r.Id == trim.OutputRevisionId);
        System.IO.File.Exists(harness.FileWorkspace.ResolveAbsolute(output.File)).ShouldBeTrue();

        output.Facts.PixelWidth.ShouldBe(applied.Width);
        output.Facts.PixelHeight.ShouldBe(applied.Height);
    }

    /// <summary>
    /// A tight trim detects and applies the same rectangle; a margin makes them differ (§20).
    /// </summary>
    /// <remarks>
    /// The pair of readings that makes two stored rectangles worth having rather than one. Under
    /// a margin the content rectangle must be <i>unchanged</i> — the margin expands the crop, it
    /// does not move what the alpha scan found — which is the assertion that would fail if
    /// anything ever recomputed the content bounds from the trimmed file.
    /// </remarks>
    [Fact]
    public async Task Tight_detects_what_it_applies_and_a_margin_does_not_move_the_detected_content()
    {
        using SessionServiceHarness tightHarness = new();
        ISessionService tightService = tightHarness.CreateService();
        SessionId tightId = await StartAtTrimAsync(tightService, tightHarness.WriteBorderedSourcePng());
        await RunTrimAsync(tightService, tightId);

        TrimGeometry tight = (await SucceededTrimAttemptAsync(tightHarness, tightId)).TrimGeometry!;
        tight.ContentBounds.ShouldBe(tight.AppliedBounds);
        tight.IsTightToContent.ShouldBeTrue();

        using SessionServiceHarness marginHarness = new();
        ISessionService marginService = marginHarness.CreateService();
        SessionId marginId = await StartAtTrimAsync(marginService, marginHarness.WriteBorderedSourcePng());
        await Must(marginService.ExecuteAsync(
            marginId, new WorkflowCommand.SetTrimParameters(TrimMargin.Uniform(2)), "tester", CancellationToken.None));
        await RunTrimAsync(marginService, marginId);

        TrimGeometry margined = (await SucceededTrimAttemptAsync(marginHarness, marginId)).TrimGeometry!;
        margined.ContentBounds.ShouldBe(Content, "the margin expands the crop, it does not move the content");
        margined.AppliedBounds.ShouldNotBe(margined.ContentBounds);
        margined.AppliedBounds.ShouldBe(TrimBounds.FromEdges(1, 0, 10, 9));
    }

    /// <summary>
    /// A margin larger than the border records the clamped rectangle, not the arithmetic (§5).
    /// </summary>
    /// <remarks>
    /// 6&#160;px on every edge against borders of 2/4/3/3 clamps three of the four to the canvas.
    /// The applied rectangle is the whole 12×10, and recording <c>[-3,-4 → 14,13)</c> would
    /// describe a crop no decoder could take.
    /// </remarks>
    [Fact]
    public async Task A_clamped_margin_records_the_rectangle_that_was_actually_cropped()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await StartAtTrimAsync(service, harness.WriteBorderedSourcePng());

        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.SetTrimParameters(TrimMargin.Uniform(6)), "tester", CancellationToken.None));
        await RunTrimAsync(service, id);

        TrimGeometry geometry = (await SucceededTrimAttemptAsync(harness, id)).TrimGeometry!;
        geometry.ContentBounds.ShouldBe(Content);
        geometry.AppliedBounds.ShouldBe(TrimBounds.Canvas(12, 10));
        geometry.AppliedBounds.FitsWithin(12, 10).ShouldBeTrue();
    }

    // -----------------------------------------------------------------------------
    // §6, §17, §19: attempts keep their own geometry
    // -----------------------------------------------------------------------------

    /// <summary>
    /// Two attempts at different margins keep independent applied rectangles (§6, §17).
    /// </summary>
    /// <remarks>
    /// The reason the geometry is on the attempt and not on the session. The operator rejects a
    /// tight trim, widens the margin and re-runs; both attempts stay in the history, the first
    /// still describing the crop that produced <i>its</i> file, and the read model reports the
    /// second because that is the file now on screen.
    /// </remarks>
    [Fact]
    public async Task A_second_attempt_at_a_wider_margin_does_not_rewrite_the_first()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await StartAtTrimAsync(service, harness.WriteBorderedSourcePng());

        SessionView firstRun = await RunTrimAsync(service, id);
        ProcessingAttempt first = await SucceededTrimAttemptAsync(harness, id);
        AttemptId firstId = first.Id;
        RevisionId firstRevision = first.OutputRevisionId!.Value;

        await Must(service.ExecuteAsync(
            id,
            new WorkflowCommand.Reject(StepKind.Trim, firstRun.CurrentArtefact!.Sha256, RejectionReason.Other),
            "tester", CancellationToken.None));
        await Must(service.ExecuteAsync(id, new WorkflowCommand.Retry(StepKind.Trim), "tester", CancellationToken.None));
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.SetTrimParameters(TrimMargin.Uniform(2)), "tester", CancellationToken.None));

        SessionView secondRun = await RunTrimAsync(service, id);

        SessionAggregate aggregate = await LoadAsync(harness, id);
        ProcessingAttempt kept = aggregate.Attempts.Single(a => a.Id == firstId);
        ProcessingAttempt second = aggregate.Attempts.Single(
            a => a.Step == StepKind.Trim && a.Status == AttemptStatus.Succeeded && a.Id != firstId);

        // Attempt 1 keeps A / B1 / M1.
        kept.TrimParameters.ShouldBe(TrimMargin.Tight);
        kept.TrimGeometry!.ContentBounds.ShouldBe(Content);
        kept.TrimGeometry.AppliedBounds.ShouldBe(Content);
        kept.OutputRevisionId.ShouldBe(firstRevision);

        // Attempt 2 keeps A / B2 / M2 — the same detected content, a different crop.
        second.TrimParameters.ShouldBe(TrimMargin.Uniform(2));
        second.TrimGeometry!.ContentBounds.ShouldBe(Content);
        second.TrimGeometry.AppliedBounds.ShouldBe(TrimBounds.FromEdges(1, 0, 10, 9));

        // Each attempt's output agrees with its own applied rectangle.
        Revision firstOutput = aggregate.Revisions.Single(r => r.Id == firstRevision);
        Revision secondOutput = aggregate.Revisions.Single(r => r.Id == second.OutputRevisionId);
        (firstOutput.Facts.PixelWidth, firstOutput.Facts.PixelHeight).ShouldBe((5, 5));
        (secondOutput.Facts.PixelWidth, secondOutput.Facts.PixelHeight).ShouldBe((9, 9));

        // And the screen shows the current one.
        secondRun.CurrentTrimGeometry.ShouldBe(second.TrimGeometry);
    }

    /// <summary>
    /// Returning upstream moves the reported bounds forward and deletes no history (§19).
    /// </summary>
    /// <remarks>
    /// A superseded attempt is still an attempt that happened. Its rectangles describe a file
    /// that was really produced and really approved, and removing them because a later run
    /// replaced it would leave the audit history unable to say what the operator approved the
    /// first time.
    /// </remarks>
    [Fact]
    public async Task Returning_to_Trim_shows_the_new_bounds_and_keeps_the_superseded_attempt_intact()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await StartAtTrimAsync(service, harness.WriteBorderedSourcePng());

        SessionView firstRun = await RunTrimAsync(service, id);
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.Approve(StepKind.Trim, firstRun.CurrentArtefact!.Sha256),
            "tester", CancellationToken.None));

        ProcessingAttempt approved = await SucceededTrimAttemptAsync(harness, id);
        TrimGeometry approvedGeometry = approved.TrimGeometry!;

        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.ReturnToStep(StepKind.Trim), "tester", CancellationToken.None));
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.SetTrimParameters(TrimMargin.Uniform(1)), "tester", CancellationToken.None));

        SessionView secondRun = await RunTrimAsync(service, id);

        SessionAggregate aggregate = await LoadAsync(harness, id);
        aggregate.Attempts.Single(a => a.Id == approved.Id).TrimGeometry
            .ShouldBe(approvedGeometry, "a return upstream does not erase what an earlier attempt cropped");

        secondRun.CurrentTrimGeometry!.AppliedBounds.ShouldBe(TrimBounds.FromEdges(2, 1, 9, 8));
        secondRun.CurrentTrimGeometry.ContentBounds.ShouldBe(Content);
    }

    // -----------------------------------------------------------------------------
    // §8, §11: nothing is fabricated
    // -----------------------------------------------------------------------------

    /// <summary>
    /// A trim that returns ManualCropRequired records no geometry at all (§11).
    /// </summary>
    /// <remarks>
    /// No content was detected, so there is nothing to state and no crop was taken. A row
    /// claiming an applied rectangle here would say an automatic crop happened when the whole
    /// point of the outcome is that one could not.
    /// </remarks>
    [Fact]
    public async Task A_refused_automatic_trim_records_no_geometry()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await StartAtTrimAsync(service, harness.WriteOpaqueSourcePng());

        OperationResult<SessionView> refused = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Trim), "tester", CancellationToken.None);
        refused.Failure.Code.ShouldBe(FailureCode.ManualCropRequired);

        ProcessingAttempt attempt = (await LoadAsync(harness, id)).Attempts.Single(a => a.Step == StepKind.Trim);
        attempt.Status.ShouldBe(AttemptStatus.Failed);
        attempt.TrimParameters.ShouldBe(TrimMargin.Tight, "the margin in force is still worth recording");
        attempt.TrimGeometry.ShouldBeNull();

        (await service.LoadAsync(id, CancellationToken.None)).Value.HasTrimGeometry.ShouldBeFalse();
    }

    /// <summary>
    /// A manual crop records no automatic geometry either (§11).
    /// </summary>
    /// <remarks>
    /// The rectangle a human drew is a different fact with a different meaning, and persisting it
    /// in these columns is SCRUM-11082's gap rather than this slice's. What matters here is that
    /// the columns stay honestly empty rather than being filled with the operator's rectangle
    /// under a label that says "detected".
    /// </remarks>
    [Fact]
    public async Task A_manual_crop_records_no_automatic_geometry()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await StartAtTrimAsync(service, harness.WriteOpaqueSourcePng());

        await service.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.Trim), "tester", CancellationToken.None);
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.SubmitManualCrop(StepKind.Trim, TrimBounds.FromEdges(3, 2, 9, 7)),
            "tester", CancellationToken.None));

        (await LoadAsync(harness, id)).Attempts
            .Single(a => a.Operation == OperationKind.ManualImport)
            .TrimGeometry.ShouldBeNull();

        (await service.LoadAsync(id, CancellationToken.None)).Value.HasTrimGeometry.ShouldBeFalse();
    }

    /// <summary>
    /// Every non-trim attempt in a completed workflow carries null geometry (§8, §11).
    /// </summary>
    /// <remarks>
    /// The guard against the columns quietly becoming "whatever the last trim did". An import, a
    /// skip's absence of an attempt, a Meitu call, a Photoshop output and a promotion all end
    /// with these columns unset.
    /// </remarks>
    [Fact]
    public async Task Only_the_trim_attempt_carries_geometry()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await StartAtTrimAsync(service, harness.WriteBorderedSourcePng());

        SessionView run = await RunTrimAsync(service, id);
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.Approve(StepKind.Trim, run.CurrentArtefact!.Sha256),
            "tester", CancellationToken.None));

        SessionAggregate aggregate = await LoadAsync(harness, id);
        aggregate.Attempts
            .Where(a => a.Step != StepKind.Trim)
            .ShouldAllBe(a => a.TrimGeometry == null);
    }

    // -----------------------------------------------------------------------------
    // §12: the read model exposes the current rectangles
    // -----------------------------------------------------------------------------

    /// <summary>
    /// SessionView reports the bounds without the shell touching a database row (§12).
    /// </summary>
    /// <remarks>
    /// Scoped to the step's own result, exactly as the trim margin is: while the screen is
    /// showing the file a step is about to <i>consume</i>, that file's geometry would describe a
    /// crop made further upstream, which is not what a line beside a review is claiming.
    /// </remarks>
    [Fact]
    public async Task The_read_model_reports_the_bounds_only_for_the_step_s_own_result()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await StartAtTrimAsync(service, harness.WriteBorderedSourcePng());

        // Before the trim runs, the screen is showing the file Trim will consume.
        SessionView beforeRun = (await service.LoadAsync(id, CancellationToken.None)).Value;
        beforeRun.HasTrimGeometry.ShouldBeFalse();
        beforeRun.CurrentTrimGeometry.ShouldBeNull();

        SessionView afterRun = await RunTrimAsync(service, id);
        afterRun.HasTrimGeometry.ShouldBeTrue();
        afterRun.CurrentTrimGeometry!.ContentBounds.ShouldBe(Content);
        afterRun.CurrentTrimGeometry.AppliedBounds.ShouldBe(Content);
    }

    // -----------------------------------------------------------------------------
    // §18: the metadata, the artefact and downstream sizing agree
    // -----------------------------------------------------------------------------

    /// <summary>
    /// Print Dimensions works from the trimmed Revision, whose size is the applied rectangle (§18).
    /// </summary>
    /// <remarks>
    /// The chain this slice is here to make provable: bounds metadata → the real cropped file →
    /// the pixels downstream sizing plans against. Downstream is not made to depend on the
    /// coordinates — it still reads the Revision — but the two now demonstrably describe the same
    /// artefact.
    /// </remarks>
    [Fact]
    public async Task The_applied_rectangle_is_the_trimmed_Revision_that_print_dimensions_consume()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await StartAtTrimAsync(service, harness.WriteBorderedSourcePng());

        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.SetTrimParameters(TrimMargin.Uniform(2)), "tester", CancellationToken.None));
        SessionView run = await RunTrimAsync(service, id);
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.Approve(StepKind.Trim, run.CurrentArtefact!.Sha256),
            "tester", CancellationToken.None));

        ProcessingAttempt trim = await SucceededTrimAttemptAsync(harness, id);
        TrimBounds applied = trim.TrimGeometry!.AppliedBounds;

        SessionAggregate aggregate = await LoadAsync(harness, id);
        Revision trimmed = aggregate.Revisions.Single(r => r.Id == trim.OutputRevisionId);

        trimmed.Facts.PixelWidth.ShouldBe(applied.Width);
        trimmed.Facts.PixelHeight.ShouldBe(applied.Height);

        // The step that decides a print size is now looking at that same file.
        SessionView next = (await service.LoadAsync(id, CancellationToken.None)).Value;
        next.CurrentArtefact!.RevisionId.ShouldBe(trimmed.Id);
        next.CurrentArtefact.Facts.PixelWidth.ShouldBe(applied.Width);
        next.CurrentArtefact.Facts.PixelHeight.ShouldBe(applied.Height);
    }

    /// <summary>
    /// The detected extent is still readable one step downstream, without recomputation (§15).
    /// </summary>
    /// <remarks>
    /// What SCRUM-11094 needs: while a print size is being decided, "where did the artwork sit in
    /// the original" is a question about the file being sized, and the answer is already stored
    /// on the attempt that cropped it. The two readings are deliberately separate — the
    /// review-line reading goes false the moment Trim hands the file on, because a line beside a
    /// Print Dimensions review must not claim to describe how <i>that</i> step produced
    /// something.
    /// </remarks>
    [Fact]
    public async Task The_detected_bounds_stay_readable_at_the_print_dimensions_step()
    {
        // PREPARE_CUSTOMER_DESIGN, because it is the workflow whose Trim is followed by
        // Print Dimensions; PREPARE_ASSET goes straight to the PNG export.
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await StartAtTrimAsync(
            service, harness.WriteBorderedSourcePng(), WorkflowType.PrepareCustomerDesign);

        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.SetTrimParameters(TrimMargin.Uniform(2)), "tester", CancellationToken.None));
        SessionView run = await RunTrimAsync(service, id);

        run.HasTrimGeometry.ShouldBeTrue("the Trim review states how this file was cropped");
        run.HasDetectedGraphicBounds.ShouldBeTrue();

        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.Approve(StepKind.Trim, run.CurrentArtefact!.Sha256),
            "tester", CancellationToken.None));

        SessionView sizing = (await service.LoadAsync(id, CancellationToken.None)).Value;
        sizing.CurrentStep!.Step.ShouldBe(StepKind.PrintDimensions);
        sizing.CurrentArtefact!.IsCurrentStepResult.ShouldBeFalse("Print Dimensions consumes the trimmed file");

        // The review-line reading correctly withdraws...
        sizing.HasTrimGeometry.ShouldBeFalse();
        sizing.CurrentTrimGeometry.ShouldBeNull();

        // ...while the detected extent of the file being sized stays available, unchanged, and
        // sourced from persistence rather than from a second alpha scan.
        sizing.HasDetectedGraphicBounds.ShouldBeTrue();
        sizing.ArtefactTrimGeometry!.ContentBounds.ShouldBe(Content);
        sizing.ArtefactTrimGeometry.AppliedBounds.ShouldBe(TrimBounds.FromEdges(1, 0, 10, 9));

        SessionView sized = (await service.ExecuteAsync(
            id, new WorkflowCommand.SetPrintDimensions(WorkflowScenario.CustomBox),
            "tester", CancellationToken.None)).Value;
        PrintDimensionsPreflight preflight = sized.Preflight.ShouldNotBeNull();
        preflight.SourceRevisionId.ShouldBe(sizing.CurrentArtefact.RevisionId);
        preflight.GraphicBoundsKind.ShouldBe(GraphicBoundsKind.AutomaticTrim);
        preflight.ArtworkBounds.ShouldBe(Content);
        preflight.FinalCanvasBounds.ShouldBe(TrimBounds.FromEdges(1, 0, 10, 9));

        SessionView restarted = (await harness.CreateService().LoadAsync(id, CancellationToken.None)).Value;
        restarted.Preflight.ShouldBe(preflight,
            "the projection must reconstruct from SQLite attempt geometry and the stored plan");
    }

    // -----------------------------------------------------------------------------

    private static async Task<ProcessingAttempt> SucceededTrimAttemptAsync(
        SessionServiceHarness harness, SessionId id)
    {
        SessionAggregate aggregate = await LoadAsync(harness, id);
        return aggregate.Attempts
            .Where(a => a.Step == StepKind.Trim && a.Status == AttemptStatus.Succeeded)
            .OrderByDescending(a => a.StartedAtUtc)
            .First();
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

    private static async Task<SessionId> StartAtTrimAsync(
        ISessionService service, string source, WorkflowType workflow = WorkflowType.PrepareAsset)
    {
        SessionId id = (await service.ImportAsync(
            workflow, source, "bounds-test", "tester", CancellationToken.None)).Value.Id;

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
