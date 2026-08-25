using System.IO;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Trimming;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Persistence;

/// <summary>
/// Returning upstream, driven end to end through <see cref="SessionService"/>
/// (Epic 11200 Part C3 §6, §7, §22, §23).
/// </summary>
/// <remarks>
/// The invalidation rules themselves are Part 3A's and are not re-implemented by this slice —
/// what is new is an operator surface that issues the command. These tests exist because that
/// surface makes the rules reachable by a person for the first time, so what they actually do
/// is worth pinning down at the level the operator experiences: which results stop being valid,
/// which survive, and what is still on disk afterwards.
/// <para>
/// The negative assertions carry most of the weight. Nothing is deleted, no review row is
/// removed, and a failed attempt stays failed — a "return" that tidied up after itself would
/// destroy exactly the history an audit trail is for (§6, §23).
/// </para>
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class ReturnToStepTests
{
    // -----------------------------------------------------------------------------
    // §22: PREPARE_ASSET — complete Trim, progress, return to Trim
    // -----------------------------------------------------------------------------

    /// <summary>
    /// Returning to Trim reopens it and invalidates what was derived from its result (§22).
    /// </summary>
    /// <remarks>
    /// The ApprovedPngExport downstream of Trim is a genuine descendant — it was promoted from
    /// the trimmed bytes — so it is the thing that must stop being valid. The upstream
    /// BackgroundRemoval result is not a descendant and must not be touched: an operator
    /// returning to redo a crop has said nothing about the cut-out that fed it.
    /// </remarks>
    [Fact]
    public async Task Returning_to_Trim_reopens_it_and_invalidates_only_what_came_after()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId id = await CompletePrepareAssetAsync(harness, service);

        SessionAggregate before = await LoadAsync(harness, id);
        RevisionId upstreamId = before.Revisions.Single(r => r.Operation == OperationKind.RemoveBackground).Id;
        RevisionId trimId = before.Revisions.Single(r => r.Operation == OperationKind.Trim).Id;
        RevisionId exportId = before.Revisions.Single(r => r.Operation == OperationKind.PromoteApproved).Id;

        SessionView returned = await ReturnToAsync(service, id, StepKind.Trim);

        // The step the operator asked for is open again, and so is everything after it.
        returned.Steps.Single(s => s.Step == StepKind.Trim).State.ShouldBe(StepState.Waiting);
        returned.Steps.Single(s => s.Step == StepKind.ApprovedPngExport).State.ShouldBe(StepState.Waiting);
        returned.Steps.Single(s => s.Step == StepKind.BackgroundRemoval).State.ShouldBe(StepState.Approved);
        returned.State.ShouldBe(SessionState.Active);

        SessionAggregate after = await LoadAsync(harness, id);

        // Descendants of the Trim result are invalid; the Trim result itself and everything
        // upstream of it are untouched (the walk invalidates what derives from a Revision,
        // never the Revision named).
        Valid(after, exportId).ShouldBeFalse();
        Valid(after, upstreamId).ShouldBeTrue();
        Valid(after, trimId).ShouldBeTrue();

        // §6: nothing is deleted. Every Revision row, every review decision and every file is
        // still there — invalidation is a label, not a removal.
        after.Revisions.Count.ShouldBe(before.Revisions.Count);
        after.Reviews.Count.ShouldBe(before.Reviews.Count);
        after.Attempts.Count.ShouldBe(before.Attempts.Count);
        foreach (Revision revision in after.Revisions)
        {
            File.Exists(harness.FileWorkspace.ResolveAbsolute(revision.File)).ShouldBeTrue(
                $"{revision.Operation} file must be retained after returning upstream.");
        }
    }

    /// <summary>
    /// Returning further upstream invalidates the trim as well (§22).
    /// </summary>
    /// <remarks>
    /// The same rule applied one step earlier, and the reason the previous test's boundary is
    /// meaningful rather than accidental: nothing about Trim is special-cased, and how much is
    /// invalidated follows from where the operator went back to.
    /// </remarks>
    [Fact]
    public async Task Returning_to_BackgroundRemoval_invalidates_the_trim_and_the_export()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId id = await CompletePrepareAssetAsync(harness, service);
        SessionAggregate before = await LoadAsync(harness, id);
        RevisionId enhancedId = before.Revisions.Single(r => r.Operation == OperationKind.Enhance).Id;
        RevisionId trimId = before.Revisions.Single(r => r.Operation == OperationKind.Trim).Id;
        RevisionId exportId = before.Revisions.Single(r => r.Operation == OperationKind.PromoteApproved).Id;

        await ReturnToAsync(service, id, StepKind.BackgroundRemoval);

        SessionAggregate after = await LoadAsync(harness, id);
        Valid(after, trimId).ShouldBeFalse();
        Valid(after, exportId).ShouldBeFalse();
        Valid(after, enhancedId).ShouldBeTrue();
    }

    /// <summary>
    /// A returned-to step can simply be run again, at a different margin (§13, §22).
    /// </summary>
    /// <remarks>
    /// Returning is only useful if what follows works, so this walks the operator's actual
    /// motive through to the end: go back, choose different settings, run, and get a different
    /// file — with the superseded one still on disk and still attributable to its own margin.
    /// </remarks>
    [Fact]
    public async Task After_returning_to_Trim_the_step_runs_again_with_new_parameters()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId id = await CompletePrepareAssetAsync(harness, service, harness.WriteBorderedSourcePng(), skipMeitu: true);
        RevisionId firstTrimId = (await LoadAsync(harness, id))
            .Revisions.Single(r => r.Operation == OperationKind.Trim).Id;

        SessionView returned = await ReturnToAsync(service, id, StepKind.Trim);

        // The margin controls are offered again, because Trim is once more about to run.
        returned.CanSetTrimParameters.ShouldBeTrue();
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.SetTrimParameters(TrimMargin.Uniform(2)), "tester", CancellationToken.None));

        OperationResult<SessionView> rerun = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Trim), "tester", CancellationToken.None);
        rerun.IsSuccess.ShouldBeTrue(rerun.IsFailure ? rerun.Failure.ToString() : "");

        SessionAggregate after = await LoadAsync(harness, id);
        List<Revision> trims = [.. after.Revisions.Where(r => r.Operation == OperationKind.Trim)];
        trims.Count.ShouldBe(2);

        Revision second = trims.Single(r => r.Id != firstTrimId);
        second.Facts.PixelWidth.ShouldBe(9);
        second.Facts.PixelHeight.ShouldBe(9);

        // Both attempts keep their own settings, across the return (§15).
        ProcessingAttempt firstAttempt = after.Attempts.Single(a => a.OutputRevisionId == firstTrimId);
        ProcessingAttempt secondAttempt = after.Attempts.Single(a => a.OutputRevisionId == second.Id);
        firstAttempt.TrimParameters.ShouldBe(TrimMargin.Tight);
        secondAttempt.TrimParameters.ShouldBe(TrimMargin.Uniform(2));

        // The superseded file survives for comparison.
        File.Exists(harness.FileWorkspace.ResolveAbsolute(
            after.Revisions.Single(r => r.Id == firstTrimId).File)).ShouldBeTrue();
    }

    // -----------------------------------------------------------------------------
    // §22: PREPARE_CUSTOMER_DESIGN — a produced TIFF, then a return upstream of it
    // -----------------------------------------------------------------------------

    /// <summary>
    /// Returning to Trim after producing a TIFF invalidates the dependent PrintOutput (§22).
    /// </summary>
    /// <remarks>
    /// The case that matters commercially: a file shaped and named like something that could be
    /// sent to the printer must stop claiming to reflect the design the moment the design is
    /// reopened. It stays listed and stays on disk — an operator needs to see that a size they
    /// produced is no longer current, which hiding it would prevent (Part 3C3B §15).
    /// </remarks>
    [Fact]
    public async Task Returning_upstream_of_a_produced_TIFF_invalidates_the_PrintOutput()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId id = await ProduceCustomerDesignTiffAsync(harness, service);

        SessionAggregate before = await LoadAsync(harness, id);
        PrintOutput outputBefore = before.Outputs.Single();
        outputBefore.IsValid.ShouldBeTrue();
        RevisionId trimId = before.Revisions.Single(r => r.Operation == OperationKind.Trim).Id;

        SessionView returned = await ReturnToAsync(service, id, StepKind.Trim);

        returned.Steps.Single(s => s.Step == StepKind.Trim).State.ShouldBe(StepState.Waiting);
        returned.Steps.Single(s => s.Step == StepKind.PhotoshopOutput).State.ShouldBe(StepState.Waiting);

        // The print size and the W1 branch are decisions attached to the run being rewound, so
        // they are cleared and must be made again explicitly rather than silently inherited.
        returned.Dimensions.ShouldBeNull();
        returned.WhiteUnderbaseBranch.ShouldBeNull();

        SessionAggregate after = await LoadAsync(harness, id);
        PrintOutput outputAfter = after.Outputs.Single();
        outputAfter.IsValid.ShouldBeFalse();

        // Still listed and still on disk: invalidated, not removed (§6).
        after.Outputs.Count.ShouldBe(before.Outputs.Count);
        File.Exists(harness.FileWorkspace.ResolveAbsolute(outputAfter.File)).ShouldBeTrue();

        // The Trim result the TIFF was built from is itself untouched — it is the ancestor the
        // walk starts from, not a descendant of it.
        Valid(after, trimId).ShouldBeTrue();
        after.Reviews.Count.ShouldBe(before.Reviews.Count);
    }

    // -----------------------------------------------------------------------------
    // §7, §22: AddAnotherSize — two sibling outputs
    // -----------------------------------------------------------------------------

    /// <summary>
    /// Returning upstream of the shared source invalidates both siblings (§7, §22).
    /// </summary>
    /// <remarks>
    /// A and B are siblings rather than a chain — both derive directly from the same approved
    /// design — so nothing about A's fate follows from B's. What makes them share one is that
    /// the destination is upstream of the Revision <i>both</i> descend from, and the walk
    /// reaches each of them independently.
    /// </remarks>
    [Fact]
    public async Task Returning_upstream_of_the_shared_design_invalidates_both_sibling_outputs()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        (SessionId id, PrintOutputId outputA, PrintOutputId outputB) =
            await ProduceTwoSizesAsync(harness, service);

        SessionAggregate before = await LoadAsync(harness, id);
        before.Outputs.Count.ShouldBe(2);
        before.Outputs.ShouldAllBe(o => o.IsValid);

        // Both sizes were built from the same approved design Revision.
        before.Outputs.Select(o => o.SourceRevisionId).Distinct().Count().ShouldBe(1);

        await ReturnToAsync(service, id, StepKind.OriginalConfirmation);

        SessionAggregate after = await LoadAsync(harness, id);
        after.Outputs.Single(o => o.Id == outputA).IsValid.ShouldBeFalse();
        after.Outputs.Single(o => o.Id == outputB).IsValid.ShouldBeFalse();

        // The imported original is the ancestor, not a descendant: it stays valid, and so does
        // every file (§6).
        after.Revisions.Single(r => r.IsRoot).IsValid.ShouldBeTrue();
        after.Outputs.Count.ShouldBe(2);
        foreach (PrintOutput output in after.Outputs)
        {
            File.Exists(harness.FileWorkspace.ResolveAbsolute(output.File)).ShouldBeTrue();
        }
    }

    /// <summary>
    /// A return that reaches neither sibling's ancestry leaves both valid (§7).
    /// </summary>
    /// <remarks>
    /// The other half of §7, and what makes the "both invalidate" case above meaningful rather
    /// than a sweep of everything recent: invalidation walks the derivation edges, so what a
    /// return costs depends on <i>lineage</i> and not on timing. Returning to PhotoshopOutput
    /// itself lands on the newest output's own Revision, from which nothing descends — so
    /// nothing is invalidated, including the older sibling produced long before it.
    /// <para>
    /// <b>A finding worth stating plainly.</b> Under the existing Part 3A rules there is no
    /// return that invalidates exactly one of two siblings, and this slice adds none. Siblings
    /// share a source by construction, so every destination at or above that source reaches
    /// both, and every destination below it reaches neither. The path that does retire one size
    /// while the other stands is rejection, not return, and is covered by
    /// <c>AddAnotherSizeTests.Rejecting_the_second_size_leaves_the_first_output_untouched</c>.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_return_that_reaches_neither_siblings_ancestry_leaves_both_valid()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        (SessionId id, PrintOutputId outputA, PrintOutputId outputB) =
            await ProduceTwoSizesAsync(harness, service);

        SessionAggregate before = await LoadAsync(harness, id);
        RevisionId sharedSource = before.Outputs.Select(o => o.SourceRevisionId).Distinct().Single();

        // PhotoshopOutput holds output B's own Revision, which is downstream of the shared
        // design rather than at or above it.
        before.Steps.Single(s => s.Step == StepKind.PhotoshopOutput)
            .CurrentRevisionId!.Value.ShouldNotBe(sharedSource);

        SessionView returned = await ReturnToAsync(service, id, StepKind.PhotoshopOutput);
        returned.Steps.Single(s => s.Step == StepKind.PhotoshopOutput).State.ShouldBe(StepState.Waiting);

        SessionAggregate after = await LoadAsync(harness, id);

        after.Outputs.Single(o => o.Id == outputA).IsValid.ShouldBeTrue();
        after.Outputs.Single(o => o.Id == outputB).IsValid.ShouldBeTrue();
        after.Revisions.ShouldAllBe(r => r.IsValid);
    }

    /// <summary>
    /// Every destination that reaches the shared design reaches both siblings (§7).
    /// </summary>
    /// <remarks>
    /// The general form of the two cases above, checked over every target the UI would actually
    /// offer rather than over two hand-picked ones. It states the invariant an operator relies
    /// on: whatever they pick from the selector, they never end up with one size silently
    /// invalidated and its twin still claiming to be current.
    /// </remarks>
    [Fact]
    public async Task No_offered_return_target_invalidates_exactly_one_of_two_siblings()
    {
        using SessionServiceHarness probeHarness = new();
        ISessionService probeService = probeHarness.CreateService();
        (SessionId probeId, _, _) = await ProduceTwoSizesAsync(probeHarness, probeService);

        IReadOnlyList<ReturnTargetView> targets =
            (await probeService.LoadAsync(probeId, CancellationToken.None)).Value.ReturnTargets;
        targets.ShouldNotBeEmpty();

        foreach (ReturnTargetView target in targets)
        {
            using SessionServiceHarness harness = new();
            ISessionService service = harness.CreateService();
            (SessionId id, PrintOutputId a, PrintOutputId b) = await ProduceTwoSizesAsync(harness, service);

            await ReturnToAsync(service, id, target.Step);

            SessionAggregate after = await LoadAsync(harness, id);
            bool validA = after.Outputs.Single(o => o.Id == a).IsValid;
            bool validB = after.Outputs.Single(o => o.Id == b).IsValid;

            validA.ShouldBe(validB,
                $"returning to {target.Step} left the two sibling outputs in different validity " +
                "states; siblings share a source, so a destination reaches both or neither.");
        }
    }

    // -----------------------------------------------------------------------------
    // §23: return after a manual import
    // -----------------------------------------------------------------------------

    /// <summary>
    /// Returning to Trim after a manual crop keeps every record, including the refusal (§23).
    /// </summary>
    /// <remarks>
    /// The hardest history to preserve, because the step's story has three parts: a
    /// deterministic attempt that failed, a manual crop that succeeded, and an approval. All
    /// three must survive the return — the failed attempt above all, since it is the only record
    /// of <i>why</i> a human had to draw a rectangle at all.
    /// <para>
    /// The ManualImport Revision itself follows the ordinary rule rather than a special one: it
    /// is Trim's own result, so returning to Trim leaves it valid and invalidates what came
    /// after it. Nothing about a human having made it changes where it sits in the lineage.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Returning_after_a_manual_crop_retains_the_crop_the_refusal_and_every_file()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId id = await ManualCropThroughToExportAsync(harness, service);

        SessionAggregate before = await LoadAsync(harness, id);
        Revision manual = before.Revisions.Single(r => r.Operation == OperationKind.ManualImport);
        RevisionId exportId = before.Revisions.Single(r => r.Operation == OperationKind.PromoteApproved).Id;

        ProcessingAttempt refusedBefore = before.Attempts.Single(a =>
            a.Step == StepKind.Trim && a.Status == AttemptStatus.Failed);
        refusedBefore.Failure!.Code.ShouldBe(FailureCode.ManualCropRequired);

        await ReturnToAsync(service, id, StepKind.Trim);

        SessionAggregate after = await LoadAsync(harness, id);

        // §23: the ManualImport Revision is retained in the audit trail, and stays valid —
        // it is the step's own result, and returning to a step does not invalidate it.
        Revision manualAfter = after.Revisions.Single(r => r.Operation == OperationKind.ManualImport);
        manualAfter.Id.ShouldBe(manual.Id);
        manualAfter.IsValid.ShouldBeTrue();

        // What was derived from it does become invalid, by the ordinary rule.
        Valid(after, exportId).ShouldBeFalse();

        // §23: the failed deterministic attempt remains, unchanged, with its reason.
        ProcessingAttempt refusedAfter = after.Attempts.Single(a =>
            a.Step == StepKind.Trim && a.Status == AttemptStatus.Failed);
        refusedAfter.ShouldBe(refusedBefore);

        // §23: no record deleted, no file removed.
        after.Attempts.Count.ShouldBe(before.Attempts.Count);
        after.Revisions.Count.ShouldBe(before.Revisions.Count);
        after.Reviews.Count.ShouldBe(before.Reviews.Count);
        foreach (Revision revision in after.Revisions)
        {
            File.Exists(harness.FileWorkspace.ResolveAbsolute(revision.File)).ShouldBeTrue();
        }
    }

    /// <summary>
    /// A reopened Trim starts from the automatic path again, not the manual one (§17, §23).
    /// </summary>
    /// <remarks>
    /// Worth stating because it is not obvious which way it should go. Returning rewinds the
    /// step to Waiting, and a Waiting Trim is one whose deterministic scan has not run in this
    /// pass — so the operator is offered the automatic trim and its margin controls, and reaches
    /// the manual surface again only if the scan refuses again. The earlier refusal stays in the
    /// history as a record, not as a standing verdict.
    /// </remarks>
    [Fact]
    public async Task A_reopened_Trim_offers_the_automatic_path_again()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId id = await ManualCropThroughToExportAsync(harness, service);
        SessionView returned = await ReturnToAsync(service, id, StepKind.Trim);

        returned.CanManualCrop.ShouldBeFalse();
        returned.CanSetTrimParameters.ShouldBeTrue();
        returned.CurrentStepFailure.ShouldBeNull();

        // And running it refuses again, for the same honest reason, because the file has not
        // changed — which is what puts the operator back on the manual path.
        OperationResult<SessionView> rerun = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Trim), "tester", CancellationToken.None);
        rerun.Failure.Code.ShouldBe(FailureCode.ManualCropRequired);

        (await service.LoadAsync(id, CancellationToken.None)).Value.CanManualCrop.ShouldBeTrue();
    }

    // -----------------------------------------------------------------------------
    // §8: the offered targets are the ones the service accepts
    // -----------------------------------------------------------------------------

    /// <summary>
    /// Every destination the read model offers is one <c>ExecuteAsync</c> accepts (§8).
    /// </summary>
    /// <remarks>
    /// The engine-level version of this is <c>ReturnTargetTests</c>, which is exhaustive over
    /// synthetic snapshots. This one is over a real session with real files and a real database,
    /// so it also covers the path the read model actually travels — targets that survive being
    /// computed, persisted, reloaded and handed to a screen.
    /// </remarks>
    [Fact]
    public async Task Every_offered_return_target_is_accepted_by_the_service()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId id = await CompletePrepareAssetAsync(harness, service);
        SessionView view = (await service.LoadAsync(id, CancellationToken.None)).Value;

        view.ReturnTargets.ShouldNotBeEmpty();
        view.CanReturnToStep.ShouldBeTrue();

        foreach (ReturnTargetView target in view.ReturnTargets)
        {
            // A fresh session per target: applying one changes which others remain legal, and
            // the question is whether each was legal when it was offered.
            using SessionServiceHarness attemptHarness = new();
            ISessionService attemptService = attemptHarness.CreateService();
            SessionId attemptId = await CompletePrepareAssetAsync(attemptHarness, attemptService);

            OperationResult<SessionView> result = await attemptService.ExecuteAsync(
                attemptId, new WorkflowCommand.ReturnToStep(target.Step), "tester", CancellationToken.None);

            result.IsSuccess.ShouldBeTrue(
                $"{target.Step} was offered as a return target but the service refused it: " +
                (result.IsFailure ? result.Failure.ToString() : ""));
        }
    }

    /// <summary>An abandoned session offers no destinations and refuses the command (§4).</summary>
    [Fact]
    public async Task A_session_that_cannot_progress_offers_no_targets_and_refuses_the_command()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        SessionId id = await CompletePrepareAssetAsync(harness, service);
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.AbandonSession("operator gave up"), "tester", CancellationToken.None));

        SessionView view = (await service.LoadAsync(id, CancellationToken.None)).Value;
        view.ReturnTargets.ShouldBeEmpty();
        view.CanReturnToStep.ShouldBeFalse();

        OperationResult<SessionView> refused = await service.ExecuteAsync(
            id, new WorkflowCommand.ReturnToStep(StepKind.Trim), "tester", CancellationToken.None);
        refused.IsFailure.ShouldBeTrue();
    }

    // -----------------------------------------------------------------------------
    // Shared arrangements
    // -----------------------------------------------------------------------------

    private static bool Valid(SessionAggregate aggregate, RevisionId id) =>
        aggregate.Revisions.Single(r => r.Id == id).IsValid;

    private static async Task<SessionAggregate> LoadAsync(SessionServiceHarness harness, SessionId id) =>
        (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;

    private static async Task<SessionView> ReturnToAsync(ISessionService service, SessionId id, StepKind target)
    {
        OperationResult<SessionView> returned = await service.ExecuteAsync(
            id, new WorkflowCommand.ReturnToStep(target), "tester", CancellationToken.None);
        returned.IsSuccess.ShouldBeTrue(returned.IsFailure ? returned.Failure.ToString() : "");
        return returned.Value;
    }

    /// <summary>Runs PREPARE_ASSET from import through to the promoted PNG.</summary>
    private static async Task<SessionId> CompletePrepareAssetAsync(
        SessionServiceHarness harness, ISessionService service, string? source = null, bool skipMeitu = false)
    {
        SessionId id = (await service.ImportAsync(
            WorkflowType.PrepareAsset, source ?? harness.WriteBorderedSourcePng(), "return-test", "tester",
            CancellationToken.None)).Value.Id;

        await Must(service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None));

        if (skipMeitu)
        {
            await Must(service.ExecuteAsync(
                id, new WorkflowCommand.Skip(StepKind.Enhancement, "already sharp"), "tester", CancellationToken.None));
            await Must(service.ExecuteAsync(
                id, new WorkflowCommand.Skip(StepKind.BackgroundRemoval, "already cut out"), "tester", CancellationToken.None));
        }
        else
        {
            await RunAndApproveAsync(service, id, StepKind.Enhancement);
            await RunAndApproveAsync(service, id, StepKind.BackgroundRemoval);
        }

        await RunAndApproveAsync(service, id, StepKind.Trim);
        await RunAndApproveAsync(service, id, StepKind.ApprovedPngExport);
        return id;
    }

    /// <summary>Runs PREPARE_CUSTOMER_DESIGN from import through to one approved TIFF.</summary>
    private static async Task<SessionId> ProduceCustomerDesignTiffAsync(
        SessionServiceHarness harness, ISessionService service)
    {
        SessionId id = (await service.ImportAsync(
            WorkflowType.PrepareCustomerDesign, harness.WriteBorderedSourcePng(), "design-test", "tester",
            CancellationToken.None)).Value.Id;

        await Must(service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None));
        await RunAndApproveAsync(service, id, StepKind.Enhancement);
        await RunAndApproveAsync(service, id, StepKind.BackgroundRemoval);
        await RunAndApproveAsync(service, id, StepKind.Trim);
        await ProduceTiffAsync(service, id, widthMm: 200);
        return id;
    }

    /// <summary>A completed GENERATE_PRINT_TIFF session holding two sibling outputs.</summary>
    private static async Task<(SessionId Id, PrintOutputId A, PrintOutputId B)> ProduceTwoSizesAsync(
        SessionServiceHarness harness, ISessionService service)
    {
        SessionId id = (await service.ImportAsync(
            WorkflowType.GeneratePrintTiff, harness.WriteSourcePng(), "sibling-return", "tester",
            CancellationToken.None)).Value.Id;

        await Must(service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None));

        RevisionId revisionA = await ProduceTiffAsync(service, id, widthMm: 200);
        await Must(service.ExecuteAsync(id, new WorkflowCommand.Complete(), "tester", CancellationToken.None));
        await Must(service.ExecuteAsync(id, new WorkflowCommand.AddAnotherSize(), "tester", CancellationToken.None));

        RevisionId revisionB = await ProduceTiffAsync(service, id, widthMm: 150);

        return (id, PrintOutputId.From(revisionA.Value), PrintOutputId.From(revisionB.Value));
    }

    /// <summary>Sets the size and branch, produces the TIFF and approves it.</summary>
    private static async Task<RevisionId> ProduceTiffAsync(ISessionService service, SessionId id, int widthMm)
    {
        await Must(service.ExecuteAsync(
            id,
            new WorkflowCommand.SetPrintDimensions(
                PrintDimensions.FromMillimetres(widthMm, 150, SizePreset.Custom)),
            "tester", CancellationToken.None));
        await Must(service.ExecuteAsync(
            id,
            new WorkflowCommand.SelectWhiteUnderbaseBranch(WhiteUnderbaseBranch.W1_1px, "ordinary design"),
            "tester", CancellationToken.None));

        OperationResult<SessionView> started = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", CancellationToken.None);
        started.IsSuccess.ShouldBeTrue(started.IsFailure ? started.Failure.ToString() : "");

        SessionStep step = started.Value.Steps.Single(s => s.Step == StepKind.PhotoshopOutput);
        await Must(service.ExecuteAsync(
            id,
            new WorkflowCommand.Approve(StepKind.PhotoshopOutput, step.CurrentRevisionSha256!.Value),
            "tester", CancellationToken.None));

        return step.CurrentRevisionId!.Value;
    }

    /// <summary>A no-alpha source cropped by hand, approved, and promoted to the export.</summary>
    private static async Task<SessionId> ManualCropThroughToExportAsync(
        SessionServiceHarness harness, ISessionService service)
    {
        SessionId id = (await service.ImportAsync(
            WorkflowType.PrepareAsset, harness.WriteOpaqueSourcePng(), "manual-return", "tester",
            CancellationToken.None)).Value.Id;

        await Must(service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None));
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.Skip(StepKind.Enhancement, "already sharp"), "tester", CancellationToken.None));
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.Skip(StepKind.BackgroundRemoval, "photo keeps its background"), "tester",
            CancellationToken.None));

        // The deterministic trim refuses: an opaque photo has no alpha to crop to.
        OperationResult<SessionView> refused = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Trim), "tester", CancellationToken.None);
        refused.Failure.Code.ShouldBe(FailureCode.ManualCropRequired);

        OperationResult<SessionView> cropped = await service.ExecuteAsync(
            id,
            new WorkflowCommand.SubmitManualCrop(StepKind.Trim, TrimBounds.FromEdges(3, 2, 9, 7)),
            "tester", CancellationToken.None);
        cropped.IsSuccess.ShouldBeTrue(cropped.IsFailure ? cropped.Failure.ToString() : "");

        Sha256 hash = cropped.Value.Steps.Single(s => s.Step == StepKind.Trim).CurrentRevisionSha256!.Value;
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.Approve(StepKind.Trim, hash), "tester", CancellationToken.None));

        await RunAndApproveAsync(service, id, StepKind.ApprovedPngExport);
        return id;
    }

    private static async Task RunAndApproveAsync(ISessionService service, SessionId id, StepKind step)
    {
        // Background Removal cannot start without an explicit reviewed-content authority since
        // Epic 11300 Part C2B1 (§7), so authorising is part of running it normally rather than
        // an extra this helper invented.
        if (step == StepKind.BackgroundRemoval)
        {
            await SessionServiceHarness.AuthoriseBackgroundRemovalAsync(service, id);
        }

        OperationResult<SessionView> started =
            await service.ExecuteAsync(id, new WorkflowCommand.StartStep(step), "tester", CancellationToken.None);
        started.IsSuccess.ShouldBeTrue(started.IsFailure ? started.Failure.ToString() : "");

        SessionStep produced = started.Value.Steps.Single(s => s.Step == step);
        if (produced.State != StepState.ReviewRequired)
        {
            return;
        }

        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.Approve(step, produced.CurrentRevisionSha256!.Value), "tester",
            CancellationToken.None));
    }

    private static async Task Must(Task<OperationResult<SessionView>> resultTask)
    {
        OperationResult<SessionView> result = await resultTask;
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : "");
    }
}
