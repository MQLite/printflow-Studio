using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Persistence;

/// <summary>
/// The flexible-size decision end to end: resolved from the configured preset, calculated once,
/// bound to an exact artefact, persisted, snapshotted onto the attempt that used it, and refused
/// when stale or unauthorised (Epic 11400 Part B1A.2D §3, §6, §8, §9, §10, §13, §24, §30–§33).
/// </summary>
/// <remarks>
/// Against the real session service, workspace, SQLite database and Fake adapters, and every
/// assertion about an outcome reads what was actually <b>persisted</b> through the repository
/// rather than what a view model says.
/// <para>
/// The two failures these exist to catch are the ones the accepted contract turns on. The first is
/// a named preset resolving to its ISO paper size instead of the shop's configured limit — a
/// silent 17 mm error on every A4 job. The second is an enlargement running without the operator's
/// exact confirmation, which the software must never be able to grant itself.
/// </para>
/// <para>
/// Nothing here runs Photoshop. Fake mode is what makes the workflow observable end to end;
/// production resizing, CMYK and the TIFF save are B1A.3.
/// </para>
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class FlexibleSizeWorkflowTests
{
    // -------------------------------------------------------------------------------------
    // §3, §4: the configured preset is the named-preset authority
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// A4 records the configured 280 mm long edge, not the 210 × 297 mm ISO page (§3, §4).
    /// </summary>
    /// <remarks>
    /// The single most important assertion in this file. <c>PrintDimensions.NominalMillimetres</c>
    /// still answers "how big is a sheet of A4", and it is still the wrong answer to "how big does
    /// this shop print A4" — the two differ by 17 mm on the long edge, which no reviewer would spot
    /// on a screen and every customer would receive.
    /// </remarks>
    [Theory]
    [InlineData(SizePreset.A4, 280.0)]
    [InlineData(SizePreset.A5, 135.0)]
    public async Task A_named_preset_uses_the_configured_recommendation_and_not_the_nominal_page(
        SizePreset preset, double configuredLongEdgeMm)
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await AtDimensionsAsync(harness, service, WideSource(harness));

        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.SetPresetFitSize(preset), "tester", CancellationToken.None));

        SessionAggregate after = await LoadAsync(harness, id);

        // Ordinary preset use stays a maximum-bound decision under the accepted B1A.1 contract.
        after.Session.DimensionSemantics.ShouldBe(PrintDimensionSemantics.MaxBoundsV1);
        after.Session.TargetEdgePlan.ShouldBeNull();

        PrintPreparationPlan plan = after.Session.PrintPreparationPlan.ShouldNotBeNull();
        plan.LimitKind.ShouldBe(preset);
        plan.MaxWidthMm.ShouldBe(configuredLongEdgeMm);
        plan.MaxHeightMm.ShouldBe(configuredLongEdgeMm);

        (double WidthMm, double HeightMm) nominal =
            PrintDimensions.NominalMillimetres(preset).ShouldNotBeNull();
        plan.MaxHeightMm.ShouldNotBe(nominal.HeightMm);

        // And the decision records which configured recommendation it was made against, so it
        // stays readable as that decision after the preset moves on (§6, §19).
        FlexibleSizeSelection selection = after.Session.SizeSelection.ShouldNotBeNull();
        selection.Mode.ShouldBe(OperatorSizingMode.PresetFit);
        selection.PresetOverridden.ShouldBeFalse();
        selection.SelectedTargetEdge.ShouldBeNull();

        PresetPrintRecommendation recommendation = selection.Recommendation.ShouldNotBeNull();
        recommendation.Preset.ShouldBe(preset);
        recommendation.Kind.ShouldBe(PresetRecommendationKind.MaximumLongEdge);
        recommendation.MaxLongEdgeMm.ShouldBe((decimal)configuredLongEdgeMm);
    }

    /// <summary>
    /// A named preset cannot be recorded with millimetres supplied by the caller (§3, §19).
    /// </summary>
    /// <remarks>
    /// Refused rather than corrected. Silently substituting the configured recommendation would
    /// accept a command that said something else, and a caller that believed it had recorded
    /// 210 × 297 would be wrong without being told.
    /// </remarks>
    [Fact]
    public async Task A_named_preset_cannot_arrive_with_its_own_millimetres()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await AtDimensionsAsync(harness, service, WideSource(harness));

        OperationResult<SessionView> refused = await service.ExecuteAsync(
            id,
            new WorkflowCommand.SetPrintDimensions(
                PrintDimensions.FromMillimetres(210, 297, SizePreset.A4)),
            "tester",
            CancellationToken.None);

        refused.IsFailure.ShouldBeTrue();
        (await LoadAsync(harness, id)).Session.Dimensions.ShouldBeNull();
    }

    /// <summary>
    /// A preset the verified manifest does not configure is refused, never filled in (§4).
    /// </summary>
    /// <remarks>
    /// The fixture manifest configures no recommendation for a size it does not name, and the
    /// service does not reach for a paper standard to cover the gap. An installation that cannot
    /// state its own limit records no size at all.
    /// </remarks>
    [Fact]
    public async Task A_preset_the_manifest_does_not_configure_cannot_be_recorded()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService(new ConfiguredPresetProviderWithNoGeometry());
        SessionId id = await AtDimensionsAsync(harness, service, WideSource(harness));

        OperationResult<SessionView> refused = await service.ExecuteAsync(
            id, new WorkflowCommand.SetPresetFitSize(SizePreset.A4), "tester", CancellationToken.None);

        refused.IsFailure.ShouldBeTrue();
        refused.Failure.Code.ShouldBe(FailureCode.EnvironmentNotVerified);
        (await LoadAsync(harness, id)).Session.Dimensions.ShouldBeNull();
    }

    // -------------------------------------------------------------------------------------
    // §6, §8: one exact operator-chosen edge
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Each selectable edge becomes exactly one concrete Photoshop edge (§6, §8, §33.3–§33.6).
    /// </summary>
    /// <remarks>
    /// <c>LongEdge</c> is the case worth stating: it is not a third edge Photoshop understands, it
    /// is a request resolved against the source's own pixels into Width or Height. A landscape
    /// source resolves to Width and a portrait one to Height, from the same request.
    /// </remarks>
    [Theory]
    [InlineData(2000, 1000, TargetEdge.Width, LimitingEdge.Width)]
    [InlineData(2000, 1000, TargetEdge.Height, LimitingEdge.Height)]
    [InlineData(2000, 1000, TargetEdge.LongEdge, LimitingEdge.Width)]
    [InlineData(1000, 2000, TargetEdge.LongEdge, LimitingEdge.Height)]
    public async Task A_custom_edge_resolves_to_one_concrete_photoshop_edge(
        int sourceWidth, int sourceHeight, TargetEdge requested, LimitingEdge resolved)
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await AtDimensionsAsync(
            harness, service, Source(harness, sourceWidth, sourceHeight));

        await Must(service.ExecuteAsync(
            id,
            new WorkflowCommand.SetCustomTargetEdgeSize(requested, 60m),
            "tester",
            CancellationToken.None));

        SessionAggregate after = await LoadAsync(harness, id);
        after.Session.DimensionSemantics.ShouldBe(PrintDimensionSemantics.TargetEdgeV1);
        after.Session.PrintPreparationPlan.ShouldBeNull(
            "a target edge is not a fit box and never becomes one");

        TargetEdgePrintPreparationPlan plan = after.Session.TargetEdgePlan.ShouldNotBeNull();
        plan.Projection.SelectedTargetEdge.ShouldBe(requested);
        plan.Projection.PhotoshopTargetEdge.ShouldBe(resolved);
        plan.Projection.RequestedMillimetres.ShouldBe(60m);
        plan.SourcePixelWidth.ShouldBe(sourceWidth);
        plan.SourcePixelHeight.ShouldBe(sourceHeight);
        plan.ProductionDpi.ShouldBe(PrintDimensions.ProductionDpi);
    }

    /// <summary>
    /// The operator's exact decimal survives the round trip, midpoints included (§7, §34).
    /// </summary>
    /// <remarks>
    /// 84.709 mm is an exact midpoint of the accepted conversion: 84.709 × 1500 / 127 is 1000.5 px
    /// precisely, which rounds away from zero to 1001. A binary double would compute 1000.4999…
    /// and land on 1000 — one pixel, silently, on every job at that size — so the decimal is
    /// carried and stored as a decimal from the operator's keystroke to the database and back.
    /// </remarks>
    [Fact]
    public async Task An_exact_decimal_request_and_its_reduced_scale_survive_a_restart()
    {
        using SessionServiceHarness harness = new();
        SessionId id = await AtDimensionsAsync(
            harness, harness.CreateService(), Source(harness, 2000, 1000));

        await Must(harness.CreateService().ExecuteAsync(
            id,
            new WorkflowCommand.SetCustomTargetEdgeSize(TargetEdge.Width, 84.709m),
            "tester",
            CancellationToken.None));

        // A second service instance over the same database: nothing carries across but the rows.
        SessionAggregate reloaded = await LoadAsync(harness, id);
        TargetEdgePrintPreparationPlan plan = reloaded.Session.TargetEdgePlan.ShouldNotBeNull();

        plan.Projection.RequestedMillimetres.ShouldBe(84.709m);
        plan.Projection.ProjectedPixelWidth.ShouldBe(1001);
        plan.Projection.ProjectedPixelHeight.ShouldBe(501);

        // The reduced integer ratio, not a percentage that has already lost the answer.
        plan.Projection.ProjectedScale.Numerator.ShouldBe(1001);
        plan.Projection.ProjectedScale.Denominator.ShouldBe(2000);
        plan.Projection.Direction.ShouldBe(ResizeDirection.Shrink);
        plan.Projection.ResizePolicy.ShouldBe(PhotoshopResizeMode.BicubicSharper);
    }

    // -------------------------------------------------------------------------------------
    // §11: a preset override and an enlargement are separate decisions
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Going past the recommendation is not the same as running out of pixels (§11, §33.8).
    /// </summary>
    /// <remarks>
    /// A5 recommends a 135 mm long edge; 160 mm exceeds it. The 2000 px source holds 160 mm at
    /// 300 ppi comfortably, so the job shrinks — and needs no enlargement authority at all. A
    /// build that treated "past the preset" as "needs permission to enlarge" would ask the
    /// operator to authorise adding pixels to a job that removes them.
    /// </remarks>
    [Fact]
    public async Task A_preset_override_within_source_capacity_needs_no_enlargement_authority()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await AtDimensionsAsync(harness, service, Source(harness, 2000, 1000));

        await Must(service.ExecuteAsync(
            id,
            new WorkflowCommand.SetCustomTargetEdgeSize(TargetEdge.LongEdge, 160m, SizePreset.A5),
            "tester",
            CancellationToken.None));

        SessionAggregate after = await LoadAsync(harness, id);
        TargetEdgePrintPreparationPlan plan = after.Session.TargetEdgePlan.ShouldNotBeNull();

        plan.PresetLimitExceeded.ShouldBeTrue();
        plan.SourceCapacityExceeded.ShouldBeFalse();
        plan.RequiresEnlargementAuthority.ShouldBeFalse();
        plan.Projection.Direction.ShouldBe(ResizeDirection.Shrink);

        // The original recommendation is retained beside the override, never replaced by it (§6).
        FlexibleSizeSelection selection = after.Session.SizeSelection.ShouldNotBeNull();
        selection.PresetOverridden.ShouldBeTrue();
        selection.BasedOnPreset.ShouldBe(SizePreset.A5);
        selection.ConfiguredPresetLimitMm.ShouldBe(135m);
        selection.RequestedMillimetres.ShouldBe(160m);

        after.ToSnapshot().NeedsEnlargementAuthority.ShouldBeFalse();
        after.ToSnapshot().UsablePhotoshopPreparation.ShouldNotBeNull();
    }

    /// <summary>
    /// The same override past what the source holds is a separate, second problem (§11, §33.9).
    /// </summary>
    [Fact]
    public async Task A_preset_override_beyond_source_capacity_requires_enlargement_authority()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await AtDimensionsAsync(harness, service, Source(harness, 2000, 1000));

        await Must(service.ExecuteAsync(
            id,
            new WorkflowCommand.SetCustomTargetEdgeSize(TargetEdge.LongEdge, 200m, SizePreset.A5),
            "tester",
            CancellationToken.None));

        SessionAggregate after = await LoadAsync(harness, id);
        TargetEdgePrintPreparationPlan plan = after.Session.TargetEdgePlan.ShouldNotBeNull();

        plan.PresetLimitExceeded.ShouldBeTrue();
        plan.SourceCapacityExceeded.ShouldBeTrue();
        plan.RequiresEnlargementAuthority.ShouldBeTrue();
        plan.Projection.ResizePolicy.ShouldBe(PhotoshopResizeMode.PreserveDetails);

        // Recording the size grants nothing. The second confirmation is still owed (§10).
        after.Session.EnlargementAuthority.ShouldBeNull();

        WorkflowSnapshot snapshot = after.ToSnapshot();
        snapshot.NeedsEnlargementAuthority.ShouldBeTrue();
        snapshot.HasUsableEnlargementAuthority.ShouldBeFalse();
        snapshot.UsablePhotoshopPreparation.ShouldBeNull();

        // And it is emphatically not a size that needs choosing again: the operator meant it.
        snapshot.NeedsDimensionReview.ShouldBeFalse();
    }

    // -------------------------------------------------------------------------------------
    // §10, §14: the enlargement authority lifecycle
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// A shrink and a resolution-only run start with no enlargement authority (§10, §33.10–11).
    /// </summary>
    /// <remarks>
    /// 254 mm on a 3000 px source is exactly 3000 px, so nothing is resampled; 60 mm shrinks. In
    /// both cases the run is executable outright, because there is nothing to authorise and
    /// asking would be the software inventing a decision.
    /// </remarks>
    [Theory]
    [InlineData(254.0, ResizeDirection.ResolutionOnly, PhotoshopResizeMode.None)]
    [InlineData(60.0, ResizeDirection.Shrink, PhotoshopResizeMode.BicubicSharper)]
    public async Task A_run_that_adds_no_pixels_needs_no_authority(
        double millimetres, ResizeDirection direction, PhotoshopResizeMode policy)
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReadyForPhotoshopAsync(
            harness, service, TargetEdge.Width, (decimal)millimetres, Source(harness, 3000, 2000));

        WorkflowSnapshot snapshot = (await LoadAsync(harness, id)).ToSnapshot();
        snapshot.UsableTargetEdgePlan!.Projection.Direction.ShouldBe(direction);
        snapshot.UsableTargetEdgePlan!.Projection.ResizePolicy.ShouldBe(policy);
        snapshot.NeedsEnlargementAuthority.ShouldBeFalse();

        TargetEdgePreparation preparation = snapshot.UsablePhotoshopPreparation
            .ShouldBeOfType<TargetEdgePreparation>();
        preparation.Authority.ShouldBeNull();

        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", CancellationToken.None));

        (await LoadAsync(harness, id)).Attempts
            .ShouldContain(a => a.Step == StepKind.PhotoshopOutput);
    }

    /// <summary>
    /// An enlargement without its authority creates no attempt at all (§10, §14, §33.12).
    /// </summary>
    /// <remarks>
    /// The refusal has to come <b>before</b> the attempt row, the working copy, the automation
    /// lock and the adapter call. A missing product decision is not a failed Photoshop run, and
    /// writing one as such would put a fabricated external failure in the audit history — the
    /// history a shop reads when a customer asks what happened to their job.
    /// </remarks>
    [Fact]
    public async Task An_unauthorised_enlargement_is_refused_before_any_attempt_exists()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReadyForPhotoshopAsync(
            harness, service, TargetEdge.Width, 300m, Source(harness, 2000, 1000));

        OperationResult<SessionView> refused = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", CancellationToken.None);

        refused.IsFailure.ShouldBeTrue();
        refused.Failure.TechnicalDetail.ShouldContain("ENLARGEMENT NOT AUTHORISED");

        SessionAggregate after = await LoadAsync(harness, id);
        after.Attempts.ShouldNotContain(a => a.Step == StepKind.PhotoshopOutput);
        after.Steps.Single(s => s.Step == StepKind.PhotoshopOutput).State
            .ShouldBe(StepState.Waiting, "nothing ran, so nothing failed");
    }

    /// <summary>
    /// The exact confirmation makes the same enlargement executable (§9, §33.13).
    /// </summary>
    [Fact]
    public async Task An_exact_confirmation_makes_the_enlargement_executable_and_is_persisted()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReadyForPhotoshopAsync(
            harness, service, TargetEdge.Width, 300m, Source(harness, 2000, 1000));

        await AuthoriseEnlargementAsync(harness, service, id);

        SessionAggregate after = await LoadAsync(harness, id);
        EnlargementAuthority authority = after.Session.EnlargementAuthority.ShouldNotBeNull();
        TargetEdgePrintPreparationPlan plan = after.Session.TargetEdgePlan.ShouldNotBeNull();

        // Every fact the authority binds is the plan's own, so it can cover nothing else (§9).
        authority.SourceRevisionId.ShouldBe(plan.SourceRevisionId);
        authority.SourceSha256.ShouldBe(plan.SourceSha256);
        authority.SizingMode.ShouldBe(OperatorSizingMode.CustomTargetEdge);
        authority.SelectedTargetEdge.ShouldBe(TargetEdge.Width);
        authority.RequestedMillimetres.ShouldBe(300m);
        authority.ProjectedScale.ShouldBe(plan.Projection.ProjectedScale);
        authority.ProjectedTargetPixelWidth.ShouldBe(plan.Projection.ProjectedPixelWidth);
        authority.ProjectedTargetPixelHeight.ShouldBe(plan.Projection.ProjectedPixelHeight);

        WorkflowSnapshot snapshot = after.ToSnapshot();
        snapshot.HasUsableEnlargementAuthority.ShouldBeTrue();
        snapshot.NeedsEnlargementAuthority.ShouldBeFalse();
        snapshot.UsablePhotoshopPreparation.ShouldBeOfType<TargetEdgePreparation>()
            .Authority.ShouldBe(authority);
    }

    /// <summary>
    /// An authority covers one exact source and one exact target, and nothing else
    /// (§9, §10, §33.14–§33.17).
    /// </summary>
    /// <remarks>
    /// Each case moves exactly one of the facts the authority binds, and every one of them must
    /// stop it applying. The row is not deleted and nothing revokes it: it simply no longer
    /// describes what is on offer, which is the whole of the invalidation strategy (§30).
    /// <para>
    /// The mutations are written straight onto the session row, because the command path exists
    /// precisely to make these states unreachable — which is the property under test. Writing the
    /// row directly is the honest way to arrive at a state a tampered or migrated database could
    /// present.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("revision")]
    [InlineData("hash")]
    [InlineData("millimetres")]
    [InlineData("edge")]
    [InlineData("projection")]
    public async Task A_changed_source_or_target_stops_the_authority_applying(string changed)
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReadyForPhotoshopAsync(
            harness, service, TargetEdge.Width, 300m, Source(harness, 2000, 1000));
        await AuthoriseEnlargementAsync(harness, service, id);

        SessionAggregate granted = await LoadAsync(harness, id);
        EnlargementAuthority original = granted.Session.EnlargementAuthority!;

        EnlargementAuthority altered = changed switch
        {
            "revision" => EnlargementAuthority.Rehydrate(
                RevisionId.From(Guid.Parse("99999999-9999-9999-9999-999999999999")),
                original.SourceSha256, original.SizingMode, original.SelectedTargetEdge,
                original.RequestedMillimetres, original.ProjectedScale,
                original.ProjectedTargetPixelWidth, original.ProjectedTargetPixelHeight),

            "hash" => EnlargementAuthority.Rehydrate(
                original.SourceRevisionId, Sha256.Parse(new string('f', 64)), original.SizingMode,
                original.SelectedTargetEdge, original.RequestedMillimetres,
                original.ProjectedScale, original.ProjectedTargetPixelWidth,
                original.ProjectedTargetPixelHeight),

            "millimetres" => EnlargementAuthority.Rehydrate(
                original.SourceRevisionId, original.SourceSha256, original.SizingMode,
                original.SelectedTargetEdge, original.RequestedMillimetres + 1m,
                original.ProjectedScale, original.ProjectedTargetPixelWidth,
                original.ProjectedTargetPixelHeight),

            "edge" => EnlargementAuthority.Rehydrate(
                original.SourceRevisionId, original.SourceSha256, original.SizingMode,
                TargetEdge.Height, original.RequestedMillimetres, original.ProjectedScale,
                original.ProjectedTargetPixelWidth, original.ProjectedTargetPixelHeight),

            _ => EnlargementAuthority.Rehydrate(
                original.SourceRevisionId, original.SourceSha256, original.SizingMode,
                original.SelectedTargetEdge, original.RequestedMillimetres,
                original.ProjectedScale, original.ProjectedTargetPixelWidth + 1,
                original.ProjectedTargetPixelHeight),
        };

        altered.Authorises(granted.Session.TargetEdgePlan!).ShouldBeFalse();

        WorkflowSnapshot tampered =
            granted.ToSnapshot() with { EnlargementAuthority = altered };
        tampered.HasUsableEnlargementAuthority.ShouldBeFalse();
        tampered.NeedsEnlargementAuthority.ShouldBeTrue();
        tampered.UsablePhotoshopPreparation.ShouldBeNull();
    }

    /// <summary>
    /// Confirming an enlargement the session is not offering is refused (§9, §10).
    /// </summary>
    /// <remarks>
    /// The payload names what the operator was looking at, and it must still be what is on offer.
    /// Without this, "I agreed to enlarge to 300 mm" could be accepted against a session that now
    /// holds a different target — which is precisely how a per-image, per-size permission becomes
    /// a session setting.
    /// </remarks>
    [Fact]
    public async Task A_confirmation_naming_a_different_target_is_refused()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReadyForPhotoshopAsync(
            harness, service, TargetEdge.Width, 300m, Source(harness, 2000, 1000));

        TargetEdgePrintPreparationPlan plan = (await LoadAsync(harness, id)).Session.TargetEdgePlan!;

        OperationResult<SessionView> refused = await service.ExecuteAsync(
            id,
            new WorkflowCommand.AuthoriseEnlargement(
                plan.SourceRevisionId, plan.SourceSha256, TargetEdge.Width, 320m),
            "tester",
            CancellationToken.None);

        refused.IsFailure.ShouldBeTrue();
        (await LoadAsync(harness, id)).Session.EnlargementAuthority.ShouldBeNull();
    }

    /// <summary>
    /// A shrink offers nothing to confirm, and confirming it is refused (§10, §11).
    /// </summary>
    [Fact]
    public async Task An_enlargement_cannot_be_authorised_for_a_run_that_shrinks()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReadyForPhotoshopAsync(
            harness, service, TargetEdge.Width, 60m, Source(harness, 2000, 1000));

        TargetEdgePrintPreparationPlan plan = (await LoadAsync(harness, id)).Session.TargetEdgePlan!;

        OperationResult<SessionView> refused = await service.ExecuteAsync(
            id,
            new WorkflowCommand.AuthoriseEnlargement(
                plan.SourceRevisionId, plan.SourceSha256, TargetEdge.Width, 60m),
            "tester",
            CancellationToken.None);

        refused.IsFailure.ShouldBeTrue();
        (await LoadAsync(harness, id)).Session.EnlargementAuthority.ShouldBeNull();
    }

    // -------------------------------------------------------------------------------------
    // §24, §25: the immutable attempt snapshot and the request built from it
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// An authorised enlargement is snapshotted onto its attempt, authority included
    /// (§24, §33.21).
    /// </summary>
    /// <remarks>
    /// The attempt row is the answer to "what produced this file, and what permitted it". Reading
    /// the authority off the session instead would mean a later change of mind rewrote history:
    /// an authorised run would read as unauthorised as soon as the operator chose a different
    /// target.
    /// <para>
    /// The Fake adapter's own note is checked here too, because it is derived entirely from the
    /// request — so a note naming the direction, the resolved edge and the authority is evidence
    /// that a fully resolved operation reached the adapter and that the adapter decided none of it.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task An_authorised_enlargement_is_snapshotted_onto_the_attempt_that_ran_it()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReadyForPhotoshopAsync(
            harness, service, TargetEdge.Width, 300m, Source(harness, 2000, 1000), SizePreset.A5);
        await AuthoriseEnlargementAsync(harness, service, id);

        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", CancellationToken.None));

        SessionAggregate after = await LoadAsync(harness, id);
        ProcessingAttempt attempt = after.Attempts.Single(a => a.Step == StepKind.PhotoshopOutput);

        TargetEdgePreparation snapshot =
            attempt.Preparation.ShouldBeOfType<TargetEdgePreparation>();
        snapshot.Plan.ShouldBe(after.Session.TargetEdgePlan);
        snapshot.Authority.ShouldBe(after.Session.EnlargementAuthority);
        snapshot.IsAuthorisedEnlargement.ShouldBeTrue();
        snapshot.ResizePolicy.ShouldBe(PhotoshopResizeMode.PreserveDetails);

        // The override facts travel with it, so the audit row says what was asked for and what it
        // went past — self-contained, never joined back to a session that can still change (§24).
        snapshot.Plan.Selection.PresetOverridden.ShouldBeTrue();
        snapshot.Plan.Selection.BasedOnPreset.ShouldBe(SizePreset.A5);
        snapshot.Plan.Selection.RequestedMillimetres.ShouldBe(300m);

        string notes = attempt.AdapterNotes.ShouldNotBeNull();
        notes.ShouldStartWith("fake");
        notes.ShouldContain("TargetEdgeV1");
        notes.ShouldContain("Enlarge");
        notes.ShouldContain("PreserveDetails");
        notes.ShouldContain("enlargement authority required and present");

        // The Fake adapter never claims Preserve Details actually ran (§27).
        notes.ShouldContain("no Photoshop ran and nothing was resampled");
    }

    /// <summary>
    /// A later change of size does not relabel an earlier attempt (§24, §33.24).
    /// </summary>
    /// <remarks>
    /// The operator rejects the output, rewinds to the size step and records a different target.
    /// The closed attempt still says what it ran under, because the attempt upsert leaves those
    /// columns out of its <c>DO UPDATE</c> clause — a property of the SQL rather than a promise
    /// about the caller.
    /// </remarks>
    [Fact]
    public async Task A_later_target_does_not_rewrite_an_earlier_attempts_snapshot()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReadyForPhotoshopAsync(
            harness, service, TargetEdge.Width, 60m, Source(harness, 2000, 1000));

        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", CancellationToken.None));

        SessionAggregate first = await LoadAsync(harness, id);
        AttemptId attemptId = first.Attempts.Single(a => a.Step == StepKind.PhotoshopOutput).Id;
        TargetEdgePreparation ran =
            first.Attempts.Single(a => a.Id == attemptId).Preparation
                .ShouldBeOfType<TargetEdgePreparation>();
        ran.Plan.Projection.RequestedMillimetres.ShouldBe(60m);

        await Must(service.ExecuteAsync(
            id,
            new WorkflowCommand.Reject(
                StepKind.PhotoshopOutput,
                first.Steps.Single(s => s.Step == StepKind.PhotoshopOutput).CurrentRevisionSha256!.Value,
                Domain.Reviews.RejectionReason.Other),
            "tester",
            CancellationToken.None));
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.ReturnToStep(StepKind.PrintDimensions), "tester", CancellationToken.None));
        await Must(service.ExecuteAsync(
            id,
            new WorkflowCommand.SetCustomTargetEdgeSize(TargetEdge.Width, 40m),
            "tester",
            CancellationToken.None));

        SessionAggregate after = await LoadAsync(harness, id);
        after.Session.TargetEdgePlan!.Projection.RequestedMillimetres.ShouldBe(40m);

        TargetEdgePreparation unchanged =
            after.Attempts.Single(a => a.Id == attemptId).Preparation
                .ShouldBeOfType<TargetEdgePreparation>();
        unchanged.Plan.Projection.RequestedMillimetres.ShouldBe(60m);
        unchanged.ShouldBe(ran);
    }

    // -------------------------------------------------------------------------------------
    // §30, §31, §32: retry, return and the next sibling output
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// A retry over unchanged content keeps its enlargement authority (§30, §33.22).
    /// </summary>
    /// <remarks>
    /// Nothing re-grants it and nothing needs to. The authority names an exact source and an exact
    /// target, both of which a rejection leaves alone — so asking the operator to confirm the same
    /// enlargement again because the previous Photoshop result was rejected would be the software
    /// forgetting a decision it still holds.
    /// </remarks>
    [Fact]
    public async Task A_retry_over_unchanged_content_keeps_its_enlargement_authority()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReadyForPhotoshopAsync(
            harness, service, TargetEdge.Width, 300m, Source(harness, 2000, 1000));
        await AuthoriseEnlargementAsync(harness, service, id);

        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", CancellationToken.None));

        SessionAggregate produced = await LoadAsync(harness, id);
        await Must(service.ExecuteAsync(
            id,
            new WorkflowCommand.Reject(
                StepKind.PhotoshopOutput,
                produced.Steps.Single(s => s.Step == StepKind.PhotoshopOutput).CurrentRevisionSha256!.Value,
                Domain.Reviews.RejectionReason.Other),
            "tester",
            CancellationToken.None));
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.Retry(StepKind.PhotoshopOutput), "tester", CancellationToken.None));

        WorkflowSnapshot snapshot = (await LoadAsync(harness, id)).ToSnapshot();
        snapshot.HasUsableEnlargementAuthority.ShouldBeTrue();
        snapshot.NeedsEnlargementAuthority.ShouldBeFalse();

        // And the run really does start again without a second confirmation.
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", CancellationToken.None));
        (await LoadAsync(harness, id)).Attempts
            .Count(a => a.Step == StepKind.PhotoshopOutput).ShouldBe(2);
    }

    /// <summary>
    /// Returning to the size step clears the target and its authority (§31, §33.23).
    /// </summary>
    /// <remarks>
    /// The operator went back to choose a different size, so a confirmation of the old one has
    /// nothing left to apply to. Leaving it behind would mean the next target — whatever it turned
    /// out to be — arrived beside a permission nobody gave for it.
    /// </remarks>
    [Fact]
    public async Task Returning_to_the_size_step_clears_the_target_and_the_authority()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReadyForPhotoshopAsync(
            harness, service, TargetEdge.Width, 300m, Source(harness, 2000, 1000));
        await AuthoriseEnlargementAsync(harness, service, id);

        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.ReturnToStep(StepKind.PrintDimensions), "tester", CancellationToken.None));

        ProcessingSession rewound = (await LoadAsync(harness, id)).Session;
        rewound.Dimensions.ShouldBeNull();
        rewound.DimensionSemantics.ShouldBeNull();
        rewound.SizeSelection.ShouldBeNull();
        rewound.TargetEdgePlan.ShouldBeNull();
        rewound.EnlargementAuthority.ShouldBeNull();
    }

    /// <summary>
    /// A second output size starts with no target, no override and no authority (§32, §33.24).
    /// </summary>
    /// <remarks>
    /// Each output size is its own decision. Carrying an enlargement confirmation from the first
    /// sibling would authorise a second run nobody was asked about — and the completed sibling
    /// keeps its own immutable audit either way.
    /// </remarks>
    [Fact]
    public async Task Another_output_size_starts_without_a_target_or_an_authority()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReadyForPhotoshopAsync(
            harness, service, TargetEdge.Width, 300m, Source(harness, 2000, 1000));
        await AuthoriseEnlargementAsync(harness, service, id);

        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", CancellationToken.None));

        SessionAggregate produced = await LoadAsync(harness, id);
        AttemptId firstAttempt = produced.Attempts.Single(a => a.Step == StepKind.PhotoshopOutput).Id;

        await Must(service.ExecuteAsync(
            id,
            new WorkflowCommand.Approve(
                StepKind.PhotoshopOutput,
                produced.Steps.Single(s => s.Step == StepKind.PhotoshopOutput).CurrentRevisionSha256!.Value),
            "tester",
            CancellationToken.None));
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.Complete(), "tester", CancellationToken.None));
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.AddAnotherSize(), "tester", CancellationToken.None));

        SessionAggregate fresh = await LoadAsync(harness, id);
        fresh.Session.SizeSelection.ShouldBeNull();
        fresh.Session.TargetEdgePlan.ShouldBeNull();
        fresh.Session.EnlargementAuthority.ShouldBeNull();
        fresh.ToSnapshot().UsablePhotoshopPreparation.ShouldBeNull();

        // The completed sibling's audit is untouched.
        fresh.Attempts.Single(a => a.Id == firstAttempt).Preparation
            .ShouldBeOfType<TargetEdgePreparation>()
            .Authority.ShouldNotBeNull();
    }

    // -------------------------------------------------------------------------------------
    // §5, §18: the two contracts stay distinct
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// A maximum-bound session never becomes a target-edge one (§5, §18, §33.18).
    /// </summary>
    /// <remarks>
    /// v1.11.0 being configured does not reinterpret a decision made under the earlier contract.
    /// The session keeps its fit box, its MaxBoundsV1 reading, and no flexible-size selection at
    /// all — and it stays runnable on its own terms.
    /// </remarks>
    [Fact]
    public async Task A_maximum_bound_session_keeps_its_own_contract()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await AtDimensionsAsync(harness, service, Source(harness, 2000, 1000));

        await Must(service.ExecuteAsync(
            id,
            new WorkflowCommand.SetPrintDimensions(
                PrintDimensions.FromMillimetres(50, 50, SizePreset.Custom)),
            "tester",
            CancellationToken.None));

        SessionAggregate after = await LoadAsync(harness, id);
        after.Session.DimensionSemantics.ShouldBe(PrintDimensionSemantics.MaxBoundsV1);
        after.Session.PrintPreparationPlan.ShouldNotBeNull();
        after.Session.TargetEdgePlan.ShouldBeNull();
        after.Session.SizeSelection.ShouldBeNull(
            "a typed fit box predates the flexible-size vocabulary and is not retrofitted into it");
        after.Session.EnlargementAuthority.ShouldBeNull();

        after.ToSnapshot().UsablePhotoshopPreparation
            .ShouldBeOfType<FitWithinBoundsPreparation>();
    }

    /// <summary>
    /// A target-edge plan whose source has changed is refused, and needs the size choosing again
    /// (§13, §33.20).
    /// </summary>
    [Fact]
    public async Task A_target_edge_plan_bound_to_other_content_is_never_usable()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReadyForPhotoshopAsync(
            harness, service, TargetEdge.Width, 60m, Source(harness, 2000, 1000));

        SessionAggregate ready = await LoadAsync(harness, id);
        TargetEdgePrintPreparationPlan stale = TargetEdgePrintPreparationPlan.For(
            RevisionId.From(Guid.Parse("88888888-8888-8888-8888-888888888888")),
            Sha256.Parse(new string('c', 64)),
            2000,
            1000,
            ready.Session.SizeSelection!);

        WorkflowSnapshot tampered = ready.ToSnapshot() with { TargetEdgePlan = stale };
        tampered.UsableTargetEdgePlan.ShouldBeNull();
        tampered.UsablePhotoshopPreparation.ShouldBeNull();
        tampered.NeedsDimensionReview.ShouldBeTrue();
        tampered.NeedsEnlargementAuthority.ShouldBeFalse(
            "a stale plan is a size to choose again, not an enlargement to confirm");
    }

    // -------------------------------------------------------------------------------------
    // §28: the read-model seam the next UI slice binds to
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// The read model reports the decision, the projection and what may be decided next (§28).
    /// </summary>
    /// <remarks>
    /// Everything here is <i>reported</i>. A screen that recalculated any of it could offer a
    /// control the engine would refuse, which is the defect the seam exists to prevent — so the
    /// two readiness flags are checked against the engine's own answers rather than against a
    /// rule restated in the assertion.
    /// </remarks>
    [Fact]
    public async Task The_read_model_reports_the_decision_and_what_may_be_decided_next()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReadyForPhotoshopAsync(
            harness, service, TargetEdge.LongEdge, 200m, Source(harness, 2000, 1000), SizePreset.A5);

        SessionView view = (await service.LoadAsync(id, CancellationToken.None)).Value;
        FlexibleSizeView sizing = view.Sizing;

        sizing.SizingMode.ShouldBe(OperatorSizingMode.CustomTargetEdge);
        sizing.Preset.ShouldBe(SizePreset.A5);
        sizing.RecommendationKind.ShouldBe(PresetRecommendationKind.MaximumLongEdge);
        sizing.RecommendationMaxWidthMm.ShouldBe(135m);
        sizing.PresetOverride.ShouldBeTrue();
        sizing.RequestedTargetEdge.ShouldBe(TargetEdge.LongEdge);
        sizing.RequestedMillimetres.ShouldBe(200m);
        sizing.ResolvedLimitingEdge.ShouldBe(LimitingEdge.Width);
        sizing.ResizeDirection.ShouldBe(Domain.Outputs.ResizeDirection.Enlarge);
        sizing.ProjectedScalePercent.ShouldNotBeNull();
        sizing.PresetLimitExceeded.ShouldBeTrue();
        sizing.SourceCapacityExceeded.ShouldBeTrue();
        sizing.NeedsEnlargementAuthority.ShouldBeTrue();
        sizing.HasUsableEnlargementAuthority.ShouldBeFalse();
        sizing.CanAuthoriseEnlargement.ShouldBeTrue();
        view.CanRunPhotoshopOutput.ShouldBeFalse();

        // The configured recommendations the next screen may offer, and nothing else.
        sizing.PresetRecommendations.Select(r => r.Preset).ShouldBe(
            [SizePreset.A3Landscape, SizePreset.A3Portrait, SizePreset.A4, SizePreset.A5]);

        await AuthoriseEnlargementAsync(harness, service, id);

        SessionView confirmed = (await service.LoadAsync(id, CancellationToken.None)).Value;
        confirmed.Sizing.NeedsEnlargementAuthority.ShouldBeFalse();
        confirmed.Sizing.HasUsableEnlargementAuthority.ShouldBeTrue();
        confirmed.CanRunPhotoshopOutput.ShouldBeTrue();

        // CanAuthoriseEnlargement stays true, and that is the honest answer to the question it
        // asks: it reports whether the command would be accepted, and confirming the same exact
        // enlargement again is accepted and records the same authority. Whether a screen should
        // still be <i>showing</i> the control is NeedsEnlargementAuthority's answer, which is now
        // false — the two are deliberately separate, so a UI never has to infer legality from
        // whether a control looks useful (§28).
        confirmed.Sizing.CanAuthoriseEnlargement.ShouldBeTrue();
    }

    // -------------------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------------------

    /// <summary>A verified preset whose manifest states no geometry contract at all.</summary>
    private sealed class ConfiguredPresetProviderWithNoGeometry : Workflow.Ports.IWorkstationPresetProvider
    {
        public OperationResult<ProductionPresetRef> GetVerifiedPreset() =>
            OperationResult.Ok(new ProductionPresetRef(
                "no-geometry", "0.0.1", Sha256.Parse(new string('e', 64))));

        public OperationResult<NamingPatternSet> GetNamingPatterns() =>
            OperationResult.Ok(NamingPatternSet.DesignDefault);

        public OperationResult<PresetPrintRecommendationSet> GetPrintSizeRecommendations() =>
            OperationResult.Ok(new PresetPrintRecommendationSet([]));
    }

    private static string Source(SessionServiceHarness harness, int width, int height) =>
        harness.Workspace.CreateSourceFile(
            $"source-{width}x{height}.png", SyntheticImages.Png(width, height, alpha: true));

    /// <summary>A 2000×1000 px source: 169.3×84.7 mm at 300 ppi.</summary>
    private static string WideSource(SessionServiceHarness harness) => Source(harness, 2000, 1000);

    /// <summary>Imports a GENERATE_PRINT_TIFF session and confirms the original.</summary>
    private static async Task<SessionId> AtDimensionsAsync(
        SessionServiceHarness harness, ISessionService service, string source)
    {
        SessionId id = (await service.ImportAsync(
            WorkflowType.GeneratePrintTiff, source, "flexible", "tester", CancellationToken.None))
            .Value.Id;

        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.ConfirmOriginal("finished design"), "tester", CancellationToken.None));

        return id;
    }

    /// <summary>Records a custom target and the W1 branch, leaving PhotoshopOutput ready.</summary>
    private static async Task<SessionId> ReadyForPhotoshopAsync(
        SessionServiceHarness harness,
        ISessionService service,
        TargetEdge edge,
        decimal millimetres,
        string source,
        SizePreset? overridden = null)
    {
        SessionId id = await AtDimensionsAsync(harness, service, source);

        await Must(service.ExecuteAsync(
            id,
            new WorkflowCommand.SetCustomTargetEdgeSize(edge, millimetres, overridden),
            "tester",
            CancellationToken.None));
        await Must(service.ExecuteAsync(
            id,
            new WorkflowCommand.SelectWhiteUnderbaseBranch(WhiteUnderbaseBranch.W1_1px, "ordinary design"),
            "tester",
            CancellationToken.None));

        return id;
    }

    /// <summary>
    /// Confirms the enlargement the session is currently offering.
    /// </summary>
    /// <remarks>
    /// Reads the plan from the session rather than taking the values as arguments, so it always
    /// confirms the target actually on offer. A test proving a mismatched confirmation is refused
    /// issues the command itself with the values it means.
    /// </remarks>
    private static async Task AuthoriseEnlargementAsync(
        SessionServiceHarness harness, ISessionService service, SessionId id)
    {
        TargetEdgePrintPreparationPlan plan = (await LoadAsync(harness, id)).Session.TargetEdgePlan
            ?? throw new InvalidOperationException("The session holds no target-edge plan.");

        await Must(service.ExecuteAsync(
            id,
            new WorkflowCommand.AuthoriseEnlargement(
                plan.SourceRevisionId,
                plan.SourceSha256,
                plan.Projection.SelectedTargetEdge,
                plan.Projection.RequestedMillimetres),
            "tester",
            CancellationToken.None));
    }

    private static async Task<SessionAggregate> LoadAsync(SessionServiceHarness harness, SessionId id) =>
        (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;

    private static async Task Must(Task<OperationResult<SessionView>> resultTask)
    {
        OperationResult<SessionView> result = await resultTask;
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : "");
    }
}
