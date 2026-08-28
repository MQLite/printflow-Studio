using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Persistence;

/// <summary>
/// The maximum-bound decision end to end: calculated once, bound to an exact artefact,
/// persisted, snapshotted onto the attempt that used it, and refused when stale
/// (Epic 11400 Part B1A.2A §5, §7, §11, §12, §15, §16, §22).
/// </summary>
/// <remarks>
/// Against the real session service, workspace, SQLite database and Fake adapters, and every
/// assertion about an outcome reads what was actually <b>persisted</b> through the repository
/// rather than what a view model says. A screen reporting a limiting edge the database does not
/// hold is exactly the defect these exist to catch.
/// <para>
/// Nothing here runs Photoshop. Fake mode is what makes the workflow observable end to end;
/// production resizing, CMYK and the TIFF save are B1A.3.
/// </para>
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class MaximumBoundsPlanTests
{
    // -------------------------------------------------------------------------------------
    // §5, §11: the plan is calculated, bound and persisted
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Recording bounds writes maximum-bound semantics and a plan bound to the exact upstream
    /// artefact (§4, §5, §7, §11).
    /// </summary>
    /// <remarks>
    /// The 12×10 px source is far inside 200×150 mm, so the accepted plan is resolution-only:
    /// nothing is resampled and no edge is written, which is the honest answer for a source that
    /// already fits (§22.4).
    /// </remarks>
    [Fact]
    public async Task Recording_bounds_persists_max_bounds_semantics_and_a_source_bound_plan()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await AtDimensionsAsync(harness, service);

        SessionAggregate before = await LoadAsync(harness, id);
        (RevisionId Id, Sha256 Sha256) upstream =
            before.ToSnapshot().UpstreamResultOf(StepKind.PhotoshopOutput)!.Value;

        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.SetPrintDimensions(Bounds(200, 150)), "tester", CancellationToken.None));

        SessionAggregate after = await LoadAsync(harness, id);
        after.Session.DimensionSemantics.ShouldBe(PrintDimensionSemantics.MaxBoundsV1);

        PrintPreparationPlan plan = after.Session.PrintPreparationPlan.ShouldNotBeNull();
        plan.SourceRevisionId.ShouldBe(upstream.Id);
        plan.SourceSha256.ShouldBe(upstream.Sha256);
        plan.MaxWidthMm.ShouldBe(200);
        plan.MaxHeightMm.ShouldBe(150);
        plan.LimitKind.ShouldBe(SizePreset.Custom);
        plan.ProductionDpi.ShouldBe(PrintDimensions.ProductionDpi);

        // The source's own pixels, read off its validated facts — never the millimetres'
        // independent conversion, which for 200×150 mm would be 2362×1772 (§8, §17).
        plan.SourcePixelWidth.ShouldBe(12);
        plan.SourcePixelHeight.ShouldBe(10);

        plan.Mode.ShouldBe(PrintPreparationMode.ResolutionOnly);
        plan.LimitingEdge.ShouldBe(LimitingEdge.None);
        plan.LimitingValueMm.ShouldBeNull();
        plan.ResizePolicy.ShouldBe(PhotoshopResizeMode.None);
        plan.ProjectedPixelWidth.ShouldBe(12);
        plan.ProjectedPixelHeight.ShouldBe(10);

        // The pre-existing operator-facing pair is untouched and still says what was typed.
        after.Session.Dimensions!.Value.WidthMm.ShouldBe(200);
        after.Session.Dimensions!.Value.HeightMm.ShouldBe(150);
    }

    /// <summary>
    /// A source larger than the box shrinks on its own limiting edge, at BicubicSharper
    /// (§22.1, §22.5).
    /// </summary>
    /// <remarks>
    /// 2000×1000 px is 169.3×84.7 mm at 300 ppi. Against a 50×50 mm box both edges overflow, and
    /// the wider one governs — so Photoshop would be given a width of 50 mm and derive the height
    /// itself. The projected pair is checked only to prove the derived edge lands inside the box;
    /// it is not what would be sent (§20).
    /// </remarks>
    [Fact]
    public async Task A_source_larger_than_the_box_shrinks_on_its_limiting_edge()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await AtDimensionsAsync(harness, service, WideSource(harness));

        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.SetPrintDimensions(Bounds(50, 50)), "tester", CancellationToken.None));

        PrintPreparationPlan plan =
            (await LoadAsync(harness, id)).Session.PrintPreparationPlan.ShouldNotBeNull();

        plan.Mode.ShouldBe(PrintPreparationMode.ProportionalShrink);
        plan.LimitingEdge.ShouldBe(LimitingEdge.Width);
        plan.LimitingValueMm.ShouldBe(50);
        plan.ResizePolicy.ShouldBe(PhotoshopResizeMode.BicubicSharper);

        plan.ProjectedPixelWidth.ShouldBe(PrintDimensions.PixelsFromMillimetres(50));
        plan.ProjectedPixelWidth.ShouldBeLessThan(2000);
        plan.ProjectedPixelHeight.ShouldBeLessThan(1000);
    }

    /// <summary>
    /// Every stored field survives a restart exactly (§11, §21).
    /// </summary>
    /// <remarks>
    /// A second service instance over the same database and workspace: nothing carries across but
    /// the rows on disk, so a plan that reads back equal proves the persistence contract rather
    /// than in-memory state.
    /// </remarks>
    [Fact]
    public async Task A_persisted_plan_round_trips_exactly_across_a_restart()
    {
        using SessionServiceHarness harness = new();
        SessionId id = await AtDimensionsAsync(harness, harness.CreateService(), WideSource(harness));

        ISessionService first = harness.CreateService();
        await Must(first.ExecuteAsync(
            id, new WorkflowCommand.SetPrintDimensions(Bounds(50, 50)), "tester", CancellationToken.None));
        await Must(first.ExecuteAsync(
            id,
            new WorkflowCommand.SelectWhiteUnderbaseBranch(WhiteUnderbaseBranch.W1_1px, "ordinary design"),
            "tester",
            CancellationToken.None));

        PrintPreparationPlan written =
            (await LoadAsync(harness, id)).Session.PrintPreparationPlan.ShouldNotBeNull();

        OperationResult<SessionView> reopened =
            await harness.CreateService().LoadAsync(id, CancellationToken.None);
        reopened.IsSuccess.ShouldBeTrue();

        PrintPreparationPlan restored =
            (await LoadAsync(harness, id)).Session.PrintPreparationPlan.ShouldNotBeNull();
        restored.ShouldBe(written);

        // And the read model reports it as currently usable rather than merely present.
        reopened.Value.DimensionSemantics.ShouldBe(PrintDimensionSemantics.MaxBoundsV1);
        reopened.Value.LimitingEdge.ShouldBe(LimitingEdge.Width);
        reopened.Value.NeedsDimensionReview.ShouldBeFalse();
        reopened.Value.CanRunPhotoshopOutput.ShouldBeTrue();
    }

    // -------------------------------------------------------------------------------------
    // §12: the immutable attempt snapshot
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// The attempt that produced the output records the plan it ran under (§12, §22.13).
    /// </summary>
    /// <remarks>
    /// The attempt copy is what makes "which fit box produced this TIFF?" answerable afterwards.
    /// It is written with the opening transaction, before the working copy and before the adapter
    /// call, so it describes what the attempt was asked to do rather than what it turned out to
    /// do — and it is the copy the request is built from.
    /// </remarks>
    [Fact]
    public async Task The_producing_attempt_snapshots_the_plan_it_ran_under()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReadyForPhotoshopAsync(harness, service);

        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", CancellationToken.None));

        SessionAggregate aggregate = await LoadAsync(harness, id);
        ProcessingAttempt attempt = aggregate.Attempts.Single(a => a.Step == StepKind.PhotoshopOutput);

        PrintPreparationPlan snapshot = attempt.PrintPreparationPlan.ShouldNotBeNull();
        snapshot.ShouldBe(aggregate.Session.PrintPreparationPlan);

        // Nothing else acquired one: a Meitu call, a trim and a promotion have no fit box, and
        // null there reads as "this attempt had no plan", never "it used the default".
        aggregate.Attempts
            .Where(a => a.Step != StepKind.PhotoshopOutput)
            .ShouldAllBe(a => a.PrintPreparationPlan == null);
    }

    /// <summary>
    /// A later change of limits does not relabel an earlier attempt (§12).
    /// </summary>
    /// <remarks>
    /// The whole reason the attempt keeps its own copy. The operator rejects an output, returns to
    /// PrintDimensions, records a different box and runs again: the session now says one thing and
    /// the first attempt must still say what it actually did. The attempt upsert leaves the plan
    /// columns out of its <c>DO UPDATE</c> clause, so this is a property of the SQL rather than a
    /// promise about the caller.
    /// </remarks>
    [Fact]
    public async Task A_later_change_of_limits_does_not_rewrite_an_earlier_attempt()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReadyForPhotoshopAsync(harness, service, Bounds(200, 150));

        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", CancellationToken.None));

        SessionAggregate first = await LoadAsync(harness, id);
        AttemptId firstAttemptId = first.Attempts.Single(a => a.Step == StepKind.PhotoshopOutput).Id;
        PrintPreparationPlan firstPlan = first.Attempts
            .Single(a => a.Id == firstAttemptId).PrintPreparationPlan.ShouldNotBeNull();
        firstPlan.MaxWidthMm.ShouldBe(200);

        // Reject, rewind to the size step, and record a different box.
        SessionStep produced = first.Steps.Single(s => s.Step == StepKind.PhotoshopOutput);
        await Must(service.ExecuteAsync(
            id,
            new WorkflowCommand.Reject(
                StepKind.PhotoshopOutput, produced.CurrentRevisionSha256!.Value,
                Domain.Reviews.RejectionReason.Other),
            "tester",
            CancellationToken.None));
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.ReturnToStep(StepKind.PrintDimensions), "tester", CancellationToken.None));
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.SetPrintDimensions(Bounds(100, 80)), "tester", CancellationToken.None));

        SessionAggregate after = await LoadAsync(harness, id);
        after.Session.PrintPreparationPlan!.MaxWidthMm.ShouldBe(100);

        // The closed attempt row is untouched: it still says what it ran under.
        after.Attempts.Single(a => a.Id == firstAttemptId)
            .PrintPreparationPlan.ShouldBe(firstPlan);
    }

    // -------------------------------------------------------------------------------------
    // §15, §16: what may and may not start Photoshop
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// A retry over unchanged upstream content keeps the same plan (§16, §22.10).
    /// </summary>
    /// <remarks>
    /// Nothing re-grants it and nothing needs to: the plan names an artefact, that artefact is
    /// still the one Photoshop will consume, and a rejected output does not change the design it
    /// was made from. The second attempt gets its own row carrying the same plan.
    /// </remarks>
    [Fact]
    public async Task A_retry_over_unchanged_upstream_content_keeps_the_plan()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReadyForPhotoshopAsync(harness, service);

        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", CancellationToken.None));

        SessionAggregate first = await LoadAsync(harness, id);
        PrintPreparationPlan original = first.Session.PrintPreparationPlan.ShouldNotBeNull();
        SessionStep produced = first.Steps.Single(s => s.Step == StepKind.PhotoshopOutput);

        await Must(service.ExecuteAsync(
            id,
            new WorkflowCommand.Reject(
                StepKind.PhotoshopOutput, produced.CurrentRevisionSha256!.Value,
                Domain.Reviews.RejectionReason.Other),
            "tester",
            CancellationToken.None));
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.Retry(StepKind.PhotoshopOutput), "tester", CancellationToken.None));

        // No new decision was recorded, and the run is still permitted.
        OperationResult<SessionView> view = await service.LoadAsync(id, CancellationToken.None);
        view.Value.CanRunPhotoshopOutput.ShouldBeTrue();
        view.Value.NeedsDimensionReview.ShouldBeFalse();

        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", CancellationToken.None));

        SessionAggregate second = await LoadAsync(harness, id);
        second.Attempts.Count(a => a.Step == StepKind.PhotoshopOutput).ShouldBe(2);
        second.Attempts
            .Where(a => a.Step == StepKind.PhotoshopOutput)
            .ShouldAllBe(a => a.PrintPreparationPlan == original);
    }

    /// <summary>
    /// A plan bound to different content cannot start Photoshop, and creates nothing
    /// (§15, §22.8, §22.12).
    /// </summary>
    /// <remarks>
    /// The refusal is structural and happens before anything is created: no attempt row, no
    /// working copy, no adapter call and no automation lock. A missing product decision is not a
    /// failed Photoshop run, and recording one as such would put a fabricated failure in the
    /// audit history.
    /// <para>
    /// The stale plan is written straight to the row rather than reached through the command
    /// path, because the command path is precisely what will not produce one — which is the point
    /// of the test.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_plan_bound_to_other_content_cannot_start_photoshop_and_creates_nothing()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReadyForPhotoshopAsync(harness, service);

        SessionAggregate ready = await LoadAsync(harness, id);
        PrintPreparationPlan usable = ready.Session.PrintPreparationPlan.ShouldNotBeNull();

        await RebindPlanAsync(harness, ready, usable with
        {
            SourceRevisionId = RevisionId.From(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc")),
        });

        int attemptsBefore = ready.Attempts.Count;

        OperationResult<SessionView> refused = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", CancellationToken.None);

        refused.IsFailure.ShouldBeTrue();
        refused.Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);

        SessionAggregate after = await LoadAsync(harness, id);
        after.Attempts.Count.ShouldBe(attemptsBefore, "a refused precondition creates no attempt");
        (await harness.Repository.GetAutomationLockAsync(CancellationToken.None))
            .Value.IsHeld.ShouldBeFalse("no automation lock is taken for a refused precondition");

        // The historical plan is retained rather than deleted: staleness is decided by matching,
        // so a plan nobody can use does not have to be hunted down (§16).
        after.Session.PrintPreparationPlan.ShouldNotBeNull();

        OperationResult<SessionView> view = await service.LoadAsync(id, CancellationToken.None);
        view.Value.NeedsDimensionReview.ShouldBeTrue();
        view.Value.CanRunPhotoshopOutput.ShouldBeFalse();
        view.Value.LimitingEdge.ShouldBeNull("a stale plan must not appear active");
        view.Value.MaxWidthMm.ShouldBeNull();
    }

    /// <summary>
    /// A plan whose bound bytes changed is refused for the integrity reason (§7, §22.9).
    /// </summary>
    /// <remarks>
    /// The case an id-only binding would let through. The Revision is the same one Photoshop will
    /// consume; only the recorded hash differs, which is what happens when a file is replaced in
    /// place under a Revision that keeps its identity.
    /// </remarks>
    [Fact]
    public async Task A_plan_whose_bound_hash_no_longer_matches_cannot_start_photoshop()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReadyForPhotoshopAsync(harness, service);

        SessionAggregate ready = await LoadAsync(harness, id);
        PrintPreparationPlan usable = ready.Session.PrintPreparationPlan.ShouldNotBeNull();

        await RebindPlanAsync(harness, ready, usable with
        {
            SourceSha256 = Sha256.Parse(new string('f', 64)),
        });

        OperationResult<SessionView> refused = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", CancellationToken.None);

        refused.IsFailure.ShouldBeTrue();
        refused.Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);
        (await LoadAsync(harness, id)).Attempts
            .ShouldAllBe(a => a.Step != StepKind.PhotoshopOutput);
    }

    /// <summary>
    /// A legacy exact pair is retained, reported as needing review, and never executable
    /// (§9, §10, §22.15).
    /// </summary>
    /// <remarks>
    /// The resumed pre-contract session. Its millimetres survive for audit and display, and the
    /// dimensions <i>would</i> fit the source's ratio — which is exactly the case §10 refuses to
    /// auto-convert, because "it happens to fit" is not the operator having decided which edge
    /// governs.
    /// <para>
    /// The row is written as migration 0005 leaves it: dimensions, LEGACY_EXACT_PAIR, and no
    /// plan. That is the state a real upgraded database is in.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_legacy_exact_pair_is_retained_needs_review_and_cannot_start_photoshop()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReadyForPhotoshopAsync(harness, service);

        SessionAggregate ready = await LoadAsync(harness, id);
        await CommitSessionAsync(harness, ready, ready.Session with
        {
            DimensionSemantics = PrintDimensionSemantics.LegacyExactPair,
            PrintPreparationPlan = null,
        });

        SessionAggregate legacy = await LoadAsync(harness, id);
        legacy.Session.Dimensions.ShouldNotBeNull("historical dimensions are retained for audit");
        legacy.Session.DimensionSemantics.ShouldBe(PrintDimensionSemantics.LegacyExactPair);
        legacy.Session.PrintPreparationPlan.ShouldBeNull();

        OperationResult<SessionView> view = await service.LoadAsync(id, CancellationToken.None);
        view.Value.Dimensions.ShouldNotBeNull();
        view.Value.DimensionSemantics.ShouldBe(PrintDimensionSemantics.LegacyExactPair);
        view.Value.NeedsDimensionReview.ShouldBeTrue();
        view.Value.CanRunPhotoshopOutput.ShouldBeFalse();
        view.Value.PreparationMode.ShouldBeNull("a legacy pair is not a preparation plan");
        view.Value.LimitingEdge.ShouldBeNull("nothing infers which old edge was intended");

        OperationResult<SessionView> refused = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", CancellationToken.None);

        refused.IsFailure.ShouldBeTrue();
        refused.Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);
        refused.Failure.TechnicalDetail.ShouldContain("DIMENSION REVIEW REQUIRED");

        SessionAggregate after = await LoadAsync(harness, id);
        after.Attempts.ShouldAllBe(a => a.Step != StepKind.PhotoshopOutput);
        after.Revisions.Count.ShouldBe(legacy.Revisions.Count, "no working copy and no Revision");
        (await harness.Repository.GetAutomationLockAsync(CancellationToken.None))
            .Value.IsHeld.ShouldBeFalse();
    }

    /// <summary>
    /// Reconfirming the limits under the current contract makes a legacy session runnable again
    /// (§10).
    /// </summary>
    /// <remarks>
    /// The way back, and it is the ordinary rewind rather than a special legacy repair path:
    /// <c>ReturnToStep</c> clears the pair with everything derived from it, and recording bounds
    /// again produces a plan calculated against the content Photoshop will actually consume.
    /// </remarks>
    [Fact]
    public async Task Reconfirming_the_limits_makes_a_legacy_session_runnable_again()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReadyForPhotoshopAsync(harness, service);

        SessionAggregate ready = await LoadAsync(harness, id);
        await CommitSessionAsync(harness, ready, ready.Session with
        {
            DimensionSemantics = PrintDimensionSemantics.LegacyExactPair,
            PrintPreparationPlan = null,
        });

        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.ReturnToStep(StepKind.PrintDimensions), "tester", CancellationToken.None));

        SessionAggregate rewound = await LoadAsync(harness, id);
        rewound.Session.Dimensions.ShouldBeNull();
        rewound.Session.DimensionSemantics.ShouldBeNull();

        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.SetPrintDimensions(Bounds(200, 150)), "tester", CancellationToken.None));
        await Must(service.ExecuteAsync(
            id,
            new WorkflowCommand.SelectWhiteUnderbaseBranch(WhiteUnderbaseBranch.W1_1px, "ordinary design"),
            "tester",
            CancellationToken.None));

        OperationResult<SessionView> view = await service.LoadAsync(id, CancellationToken.None);
        view.Value.DimensionSemantics.ShouldBe(PrintDimensionSemantics.MaxBoundsV1);
        view.Value.NeedsDimensionReview.ShouldBeFalse();
        view.Value.CanRunPhotoshopOutput.ShouldBeTrue();

        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", CancellationToken.None));
    }

    // -------------------------------------------------------------------------------------
    // §14, §18: the command path and the adapter it feeds
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Bounds recorded against bytes that changed underneath are refused, and nothing is written
    /// (§14, §22.9).
    /// </summary>
    /// <remarks>
    /// The plan's limiting edge is calculated from the source's pixels, so recording bounds is a
    /// decision about specific content and is integrity-checked exactly as an approval is. Without
    /// that check the mutation would only surface at <c>StartStep</c> — after a plan had been
    /// written for a file nobody fitted anything to.
    /// </remarks>
    [Fact]
    public async Task Bounds_recorded_over_mutated_source_bytes_are_refused()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await AtDimensionsAsync(harness, service);

        SessionAggregate ready = await LoadAsync(harness, id);
        RevisionId upstreamId = ready.ToSnapshot().UpstreamRevisionOf(StepKind.PhotoshopOutput)!.Value;
        Revision upstream = ready.Revisions.Single(r => r.Id == upstreamId);

        // The file is replaced in place: the Revision row still describes bytes that are gone.
        // The imported original is marked read-only by the workspace, so the attribute is cleared
        // first — this is simulating something outside PrintFlow having overwritten the file, not
        // testing that PrintFlow's own protection can be bypassed.
        string absolute = harness.FileWorkspace.ResolveAbsolute(upstream.File);
        System.IO.File.SetAttributes(absolute, System.IO.FileAttributes.Normal);
        System.IO.File.WriteAllBytes(absolute, SyntheticImages.Png(9, 9, alpha: true));

        OperationResult<SessionView> refused = await service.ExecuteAsync(
            id, new WorkflowCommand.SetPrintDimensions(Bounds(200, 150)), "tester", CancellationToken.None);

        refused.IsFailure.ShouldBeTrue();
        refused.Failure.Code.ShouldBe(FailureCode.RevisionIntegrityMismatch);

        SessionAggregate after = await LoadAsync(harness, id);
        after.Session.Dimensions.ShouldBeNull("no bounds are recorded against content that changed");
        after.Session.DimensionSemantics.ShouldBeNull();
        after.Session.PrintPreparationPlan.ShouldBeNull();
    }

    /// <summary>
    /// The Fake adapter receives the typed plan and reports it deterministically (§18, §22.14).
    /// </summary>
    /// <remarks>
    /// Fake mode may not fabricate a production claim, so what it records is the <i>projected</i>
    /// pair and the neutral policy — the plan it was handed — rather than a Photoshop read-back.
    /// The note is persisted on the attempt, so a run that never received a plan could not produce
    /// it.
    /// </remarks>
    [Fact]
    public async Task The_fake_adapter_consumes_the_typed_plan_and_records_it()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReadyForPhotoshopAsync(harness, service);

        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", CancellationToken.None));

        SessionAggregate aggregate = await LoadAsync(harness, id);
        ProcessingAttempt attempt = aggregate.Attempts.Single(a => a.Step == StepKind.PhotoshopOutput);
        PrintPreparationPlan plan = attempt.PrintPreparationPlan.ShouldNotBeNull();

        string notes = attempt.AdapterNotes.ShouldNotBeNull();
        notes.ShouldStartWith("fake");
        notes.ShouldContain(PrintDimensionSemantics.MaxBoundsV1.ToString());
        notes.ShouldContain($"{plan.ProjectedPixelWidth}x{plan.ProjectedPixelHeight}");
        notes.ShouldContain("300 ppi");
        notes.ShouldNotContain("Actual");
    }

    // -------------------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------------------

    private static PrintDimensions Bounds(double widthMm, double heightMm) =>
        PrintDimensions.FromMillimetres(widthMm, heightMm, SizePreset.Custom);

    /// <summary>A 2000×1000 px source: 169.3×84.7 mm at 300 ppi, so a small box shrinks it.</summary>
    private static string WideSource(SessionServiceHarness harness) =>
        harness.Workspace.CreateSourceFile("wide.png", SyntheticImages.Png(2000, 1000, alpha: true));

    /// <summary>Imports a GENERATE_PRINT_TIFF session and confirms the original.</summary>
    /// <remarks>
    /// GeneratePrintTiff rather than PrepareCustomerDesign because it is the shortest route to
    /// PrintDimensions: Import, confirm, size, output. The rules under test are about the size
    /// step and what consumes it, not about the steps in between.
    /// </remarks>
    private static async Task<SessionId> AtDimensionsAsync(
        SessionServiceHarness harness, ISessionService service, string? source = null)
    {
        SessionId id = (await service.ImportAsync(
            WorkflowType.GeneratePrintTiff,
            source ?? harness.WriteBorderedSourcePng(),
            "bounds",
            "tester",
            CancellationToken.None)).Value.Id;

        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.ConfirmOriginal("finished design"), "tester", CancellationToken.None));

        return id;
    }

    /// <summary>Records bounds and the W1 branch, leaving PhotoshopOutput ready to start.</summary>
    private static async Task<SessionId> ReadyForPhotoshopAsync(
        SessionServiceHarness harness, ISessionService service, PrintDimensions? bounds = null)
    {
        SessionId id = await AtDimensionsAsync(harness, service);

        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.SetPrintDimensions(bounds ?? Bounds(200, 150)), "tester", CancellationToken.None));
        await Must(service.ExecuteAsync(
            id,
            new WorkflowCommand.SelectWhiteUnderbaseBranch(WhiteUnderbaseBranch.W1_1px, "ordinary design"),
            "tester",
            CancellationToken.None));

        return id;
    }

    /// <summary>
    /// Writes a plan straight onto the session row, bypassing the command path.
    /// </summary>
    /// <remarks>
    /// Only for the staleness tests, and deliberately: the command path is what refuses to produce
    /// a plan bound to content the session is not about to consume, so reaching a stale row
    /// through it is impossible — which is the property under test. Writing the row directly is
    /// the honest way to arrive at the state a real session reaches when its upstream changes.
    /// </remarks>
    private static Task RebindPlanAsync(
        SessionServiceHarness harness, SessionAggregate aggregate, PrintPreparationPlan plan) =>
        CommitSessionAsync(harness, aggregate, aggregate.Session with { PrintPreparationPlan = plan });

    private static async Task CommitSessionAsync(
        SessionServiceHarness harness, SessionAggregate aggregate, ProcessingSession session)
    {
        SessionMutation mutation = new(session, aggregate.Steps, [], [], [], [], [], null, null);
        (await harness.Repository.CommitAsync(mutation, CancellationToken.None)).IsSuccess.ShouldBeTrue();
    }

    private static async Task<SessionAggregate> LoadAsync(SessionServiceHarness harness, SessionId id) =>
        (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;

    private static async Task Must(Task<OperationResult<SessionView>> resultTask)
    {
        OperationResult<SessionView> result = await resultTask;
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : "");
    }
}
