using System.IO;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Settings;
using PrintFlow.Domain.Trimming;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Delivery;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Persistence;

/// <summary>
/// Adjusting a trim review end to end through <see cref="SessionService"/> (SCRUM-11147): real
/// SQLite in a GUID-owned temp database, real workspace, the real internal trim and crop
/// processors on synthetic images, and a fake Meitu.
/// </summary>
/// <remarks>
/// The bordered source is 12×10 with alpha content in [3,2 → 8,7). A uniform 1-px margin makes
/// the automatic result R1 the rectangle [2,1 → 9,8) of it — so any adjustment wider than that
/// needs pixels R1 does not contain, which only a crop of the pre-trim source U can supply.
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class TrimAdjustmentStepTests
{
    private static readonly TrimBounds AutomaticApplied = TrimBounds.FromEdges(2, 1, 9, 8);
    private static readonly TrimBounds Wider = TrimBounds.FromEdges(1, 1, 10, 9);

    [Fact]
    public async Task Use_this_trim_crops_the_pre_trim_source_into_a_new_result_that_waits_for_its_own_review()
    {
        using SessionServiceHarness h = new();
        CountingMeituProcessor meitu = new(h.FakeMeitu);
        ISessionService service = h.CreateServiceWithMeitu(meitu);
        (SessionId id, SessionView review) = await AtTrimReviewAsync(h, service, enhance: true);
        SessionAggregate before = await LoadAsync(h, id);
        Revision u = before.Revisions.Single(r => r.Operation == OperationKind.Enhance);
        Revision r1 = before.Revisions.Single(r => r.Id == review.CurrentArtefact!.RevisionId);
        int meituCalls = meitu.CallCount;

        TrimAdjustmentView target = review.TrimAdjustment.ShouldNotBeNull();
        target.PreTrimRevisionId.ShouldBe(u.Id, "the approved enhancement result, never R1");
        target.CurrentBounds.ShouldBe(AutomaticApplied);
        target.AutomaticSuggestion.ShouldBe(AutomaticApplied);
        (target.SourcePixelWidth, target.SourcePixelHeight).ShouldBe((12, 10));

        SessionView adjusted = await MustAsync(service.ExecuteAsync(id, Adjust(target, Wider), "tester", CancellationToken.None));

        SessionAggregate after = await LoadAsync(h, id);
        SessionStep trim = adjusted.Steps.Single(s => s.Step == StepKind.Trim);
        trim.State.ShouldBe(StepState.ReviewRequired);
        Revision r2 = after.Revisions.Single(r => r.Id == trim.CurrentRevisionId);
        r2.Id.ShouldNotBe(r1.Id);
        r2.Operation.ShouldBe(OperationKind.ManualImport);
        r2.SourceRevisionId.ShouldBe(u.Id);
        (r2.Facts.PixelWidth, r2.Facts.PixelHeight).ShouldBe((Wider.Width, Wider.Height),
            "wider than R1: pixels only the pre-trim source still has");
        ProcessingAttempt crop = after.Attempts.Single(a => a.OutputRevisionId == r2.Id);
        crop.InputRevisionId.ShouldBe(u.Id);
        crop.ManualCropGeometry.ShouldBe(ManualCropGeometry.Create(Wider, ManualCropMargin.Tight, 12, 10));
        after.Attempts.Count.ShouldBe(before.Attempts.Count + 1, "exactly one new attempt");

        // R1 is history: valid, unreviewed, no longer offered. Nothing was reviewed, rejected or invalidated.
        after.Revisions.Single(r => r.Id == r1.Id).ShouldBe(r1);
        after.Reviews.Count.ShouldBe(before.Reviews.Count);
        after.Revisions.ShouldAllBe(r => r.IsValid);

        // Approved upstream work was reused, not repeated.
        meitu.CallCount.ShouldBe(meituCalls);
        after.Attempts.Where(a => a.Step != StepKind.Trim).ShouldBe(before.Attempts.Where(a => a.Step != StepKind.Trim));
        adjusted.Steps.Where(s => s.Step != StepKind.Trim).ShouldBe(review.Steps.Where(s => s.Step != StepKind.Trim));

        // The new result is reviewed like any other and can be adjusted again from the same source.
        adjusted.TrimAdjustment!.ResultRevisionId.ShouldBe(r2.Id);
        adjusted.TrimAdjustment.PreTrimRevisionId.ShouldBe(u.Id);
        adjusted.TrimAdjustment.CurrentBounds.ShouldBe(Wider);
        adjusted.TrimAdjustment.AutomaticSuggestion.ShouldBe(AutomaticApplied,
            "still the automatic attempt's stored rectangle, not recomputed and not margined twice");
        adjusted.AvailableCommands.ShouldContain(CommandKind.Approve);
        await MustAsync(service.ApproveExactReviewAsync(id, StepKind.Trim, r2.Id, r2.Sha256, "tester", CancellationToken.None));
    }

    [Fact]
    public async Task A_later_margin_setting_never_changes_the_stored_suggestion()
    {
        using SessionServiceHarness h = new();
        ISessionService service = h.CreateService();
        (SessionId id, SessionView review) = await AtTrimReviewAsync(h, service);

        // The workstation's default margin changes after the automatic trim ran.
        (await h.Settings.UpsertAsync([SettingEntry.Integer(SettingKey.TrimSafetyMarginPixels, 3)], CancellationToken.None))
            .IsSuccess.ShouldBeTrue();
        SessionView reloaded = (await service.LoadAsync(id, CancellationToken.None)).Value;
        SessionView second = await MustAsync(service.ExecuteAsync(
            id, Adjust(reloaded.TrimAdjustment!, Wider), "tester", CancellationToken.None));

        foreach (SessionView view in new[] { reloaded, second })
            view.TrimAdjustment!.AutomaticSuggestion.ShouldBe(AutomaticApplied,
                "the rectangle the automatic trim recorded: neither today's margin nor its own margin applied again");
        review.TrimAdjustment!.AutomaticSuggestion.ShouldBe(AutomaticApplied);
    }

    [Fact]
    public async Task An_earlier_delivered_copy_is_untouched_and_never_counts_as_saving_the_new_result()
    {
        using SessionServiceHarness h = new();
        ISessionService service = h.CreateService();
        string folder = FinalSaveFixtures.NewFolder("adjust-delivered");
        try
        {
            (SessionId id, SessionView first) = await AtTrimReviewAsync(h, service);
            FinalSaveCoordinator coordinator = new(service, FinalSaveFixtures.Delivery(h), "tester");
            FinalSaveResult saved = await coordinator.ConfirmAndSaveAsync(
                FinalSaveFixtures.Confirm(first, folder, "first.png"), null, CancellationToken.None);
            saved.Approval.ShouldBe(ApprovalOutcome.Succeeded);
            string delivered = Path.Combine(folder, "first.png");
            byte[] deliveredBytes = File.ReadAllBytes(delivered);
            ArtifactKey earlier = saved.Artifact!.Value;

            // Reopen Trim the lawful way, run it again, then adjust the new review.
            await MustAsync(service.ExecuteAsync(id, new WorkflowCommand.ReturnToStep(StepKind.Trim), "tester", CancellationToken.None));
            SessionView again = await MustAsync(service.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.Trim), "tester", CancellationToken.None));
            int reviewsBefore = (await LoadAsync(h, id)).Reviews.Count;
            SessionView adjusted = await MustAsync(service.ExecuteAsync(
                id, Adjust(again.TrimAdjustment!, Wider), "tester", CancellationToken.None));

            File.ReadAllBytes(delivered).ShouldBe(deliveredBytes, "a delivered copy is never overwritten or removed");
            Directory.GetFiles(folder).ShouldBe([delivered]);
            (await coordinator.Delivery.GetDeliveryStateAsync(id, earlier, CancellationToken.None)).Value
                .ShouldNotBeEmpty("the earlier delivery record is kept under its own identity");
            SessionAggregate after = await LoadAsync(h, id);
            after.Reviews.Count.ShouldBe(reviewsBefore, "the new result is not approved by anything that happened before");
            adjusted.Steps.Single(s => s.Step == StepKind.ApprovedPngExport).State.ShouldBe(StepState.Waiting);
            adjusted.CurrentArtefact!.RevisionId.ShouldNotBe(first.CurrentArtefact!.RevisionId);
        }
        finally { FinalSaveFixtures.Remove(folder); }
    }

    [Fact]
    public async Task A_full_canvas_automatic_result_is_restored_as_the_full_canvas()
    {
        using SessionServiceHarness h = new();
        ISessionService service = h.CreateService();
        string source = h.Workspace.CreateSourceFile("full.png", SyntheticImages.PngWithAlpha(8, 6, (_, _) => 255));
        (_, SessionView review) = await AtTrimReviewAsync(h, service, source: source, margin: TrimMargin.Tight);

        review.TrimAdjustment!.AutomaticSuggestion.ShouldBe(TrimBounds.Canvas(8, 6));
        review.TrimAdjustment.CurrentBounds.ShouldBe(TrimBounds.Canvas(8, 6));
    }

    [Fact]
    public async Task A_manual_crop_under_review_has_no_invented_suggestion_and_the_fallback_is_unchanged()
    {
        using SessionServiceHarness h = new();
        ISessionService service = h.CreateService();
        SessionId id = await StartAsync(service, h.WriteOpaqueSourcePng(), WorkflowType.PrepareAsset, enhance: false);
        (await service.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.Trim), "tester", CancellationToken.None))
            .Failure.Code.ShouldBe(FailureCode.ManualCropRequired);

        SessionView failed = (await service.LoadAsync(id, CancellationToken.None)).Value;
        failed.CanManualCrop.ShouldBeTrue();
        failed.TrimAdjustment.ShouldBeNull("adjustment is only offered from a review");

        TrimBounds drawn = TrimBounds.FromEdges(3, 2, 9, 7);
        SessionView manual = await MustAsync(service.ExecuteAsync(
            id, new WorkflowCommand.SubmitManualCrop(StepKind.Trim, drawn), "tester", CancellationToken.None));

        manual.TrimAdjustment.ShouldNotBeNull().CurrentBounds.ShouldBe(drawn);
        manual.TrimAdjustment.AutomaticSuggestion.ShouldBeNull();
        manual.CanManualCrop.ShouldBeFalse();
    }

    [Fact]
    public async Task Same_bounds_make_a_new_result_and_a_stale_revision_with_the_same_hash_is_refused_everywhere()
    {
        using SessionServiceHarness h = new();
        ISessionService service = h.CreateService();
        (SessionId id, SessionView review) = await AtTrimReviewAsync(h, service);

        SessionView second = await MustAsync(service.ExecuteAsync(
            id, Adjust(review.TrimAdjustment!, AutomaticApplied), "tester", CancellationToken.None));
        SessionView third = await MustAsync(service.ExecuteAsync(
            id, Adjust(second.TrimAdjustment!, AutomaticApplied), "tester", CancellationToken.None));

        ArtefactView r2 = second.CurrentArtefact!;
        ArtefactView r3 = third.CurrentArtefact!;
        r2.RevisionId.ShouldNotBe(review.CurrentArtefact!.RevisionId, "unchanged bounds still make a new result");
        r3.RevisionId.ShouldNotBe(r2.RevisionId);
        r3.Sha256.ShouldBe(r2.Sha256, "the same crop of the same source is byte-identical");

        // R2 is stale although its hash equals R3's: every exact entry refuses it.
        OperationResult<SessionView> staleAdjust = await service.ExecuteAsync(
            id, Adjust(second.TrimAdjustment!, Wider), "tester", CancellationToken.None);
        staleAdjust.IsFailure.ShouldBeTrue();
        OperationResult<SessionView> staleApprove = await service.ApproveExactReviewAsync(
            id, StepKind.Trim, r2.RevisionId, r2.Sha256, "tester", CancellationToken.None);
        staleApprove.IsFailure.ShouldBeTrue();
        FinalSaveResult staleConfirm = await new FinalSaveCoordinator(service, FinalSaveFixtures.Delivery(h), "tester")
            .ConfirmAndSaveAsync(FinalSaveFixtures.Confirm(second, FinalSaveFixtures.NewFolder("stale"), "stale.png"),
                null, CancellationToken.None);
        staleConfirm.Approval.ShouldNotBe(ApprovalOutcome.Succeeded);

        SessionAggregate after = await LoadAsync(h, id);
        after.Reviews.ShouldBeEmpty("nothing stale was approved");
        after.Steps.Single(s => s.Step == StepKind.Trim).CurrentRevisionId.ShouldBe(r3.RevisionId);
    }

    [Fact]
    public async Task Refused_adjustments_change_nothing()
    {
        using SessionServiceHarness h = new();
        ISessionService service = h.CreateService();
        (SessionId id, SessionView review) = await AtTrimReviewAsync(h, service);
        TrimAdjustmentView target = review.TrimAdjustment!;
        SessionAggregate before = await LoadAsync(h, id);

        WorkflowCommand[] refused =
        [
            Adjust(target, TrimBounds.FromEdges(0, 0, 13, 10)) ,
            Adjust(target, Wider) with { SourceRevision = target.ResultRevisionId, SourceHash = target.ResultSha256 },
            Adjust(target, Wider) with { ReviewedRevision = RevisionId.From(Guid.NewGuid()) },
            Adjust(target, Wider) with { SourceHash = Sha256.Parse(new string('F', 64)) },
        ];
        foreach (WorkflowCommand command in refused)
        {
            OperationResult<SessionView> result = await service.ExecuteAsync(id, command, "tester", CancellationToken.None);
            result.IsFailure.ShouldBeTrue(command.ToString());
            result.Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);
        }

        SessionAggregate after = await LoadAsync(h, id);
        after.Steps.ShouldBe(before.Steps);
        after.Attempts.Count.ShouldBe(before.Attempts.Count);
        after.Revisions.ShouldBe(before.Revisions);
    }

    [Fact]
    public async Task A_mutated_source_is_refused_before_any_commit_and_removes_the_offer()
    {
        using SessionServiceHarness h = new();
        ISessionService service = h.CreateService();
        // The enhanced result is U here: an imported original is write-protected by the workspace,
        // so the mutation is applied to a working-area source, exactly as the existing
        // background-removal integrity test does.
        (SessionId id, SessionView review) = await AtTrimReviewAsync(h, service, enhance: true);
        SessionAggregate before = await LoadAsync(h, id);
        Revision u = before.Revisions.Single(r => r.Id == review.TrimAdjustment!.PreTrimRevisionId);
        u.Operation.ShouldBe(OperationKind.Enhance);
        string path = h.FileWorkspace.ResolveAbsolute(u.File);
        byte[] bytes = File.ReadAllBytes(path);
        bytes[^1] ^= 0xFF;
        File.WriteAllBytes(path, bytes);

        OperationResult<SessionView> result = await service.ExecuteAsync(
            id, Adjust(review.TrimAdjustment!, Wider), "tester", CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.RevisionIntegrityMismatch);
        SessionAggregate after = await LoadAsync(h, id);
        after.Revisions.Single(r => r.Id == u.Id).InvalidationReason.ShouldBe(InvalidationReason.FileMutated);
        after.Steps.Single(s => s.Step == StepKind.Trim).CurrentRevisionId.ShouldBe(review.CurrentArtefact!.RevisionId);
        after.Attempts.Count.ShouldBe(before.Attempts.Count);
        (await service.LoadAsync(id, CancellationToken.None)).Value.TrimAdjustment
            .ShouldBeNull("an invalid source is never offered for adjustment");
    }

    [Fact]
    public async Task A_failed_crop_leaves_the_old_result_as_valid_unreviewed_history_and_the_existing_fallback()
    {
        using SessionServiceHarness h = new();
        ISessionService service = h.CreateService(manualCrop: new FailingCrop());
        (SessionId id, SessionView review) = await AtTrimReviewAsync(h, service);
        RevisionId r1 = review.CurrentArtefact!.RevisionId;

        OperationResult<SessionView> result = await service.ExecuteAsync(
            id, Adjust(review.TrimAdjustment!, Wider), "tester", CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        SessionAggregate after = await LoadAsync(h, id);
        SessionStep trim = after.Steps.Single(s => s.Step == StepKind.Trim);
        trim.State.ShouldBe(StepState.Failed);
        trim.CurrentRevisionId.ShouldBeNull();
        Revision old = after.Revisions.Single(r => r.Id == r1);
        old.IsValid.ShouldBeTrue("a failed replacement never marks the old result as replaced");
        old.ReviewState.ShouldBe(ReviewState.NotReviewed);
        after.Reviews.ShouldBeEmpty();

        SessionView reloaded = (await service.LoadAsync(id, CancellationToken.None)).Value;
        reloaded.CanManualCrop.ShouldBeTrue("the unchanged manual-crop fallback");
        reloaded.AvailableCommands.ShouldContain(CommandKind.Retry);
        reloaded.TrimAdjustment.ShouldBeNull();
    }

    [Fact]
    public async Task A_failed_opening_commit_leaves_the_review_exactly_as_it_was()
    {
        using SessionServiceHarness h = new();
        ISessionService service = h.CreateService();
        (SessionId id, SessionView review) = await AtTrimReviewAsync(h, service);
        SessionAggregate before = await LoadAsync(h, id);
        FaultingRepository faulting = new(h.Repository) { FailFromCommit = 1 };

        OperationResult<SessionView> result = await h.CreateService(repository: faulting).ExecuteAsync(
            id, Adjust(review.TrimAdjustment!, Wider), "tester", CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        SessionAggregate after = await LoadAsync(h, id);
        after.Steps.ShouldBe(before.Steps);
        after.Attempts.ShouldBe(before.Attempts);
        after.Revisions.ShouldBe(before.Revisions);
        (await service.LoadAsync(id, CancellationToken.None)).Value.TrimAdjustment.ShouldNotBeNull();
    }

    [Fact]
    public async Task An_uncertain_outcome_is_closed_by_existing_startup_recovery_without_touching_the_old_result()
    {
        using SessionServiceHarness h = new();
        ISessionService service = h.CreateService();
        (SessionId id, SessionView review) = await AtTrimReviewAsync(h, service);
        RevisionId r1 = review.CurrentArtefact!.RevisionId;
        FaultingRepository faulting = new(h.Repository) { FailFromCommit = 2 };

        OperationResult<SessionView> result = await h.CreateService(repository: faulting).ExecuteAsync(
            id, Adjust(review.TrimAdjustment!, Wider), "tester", CancellationToken.None);
        result.IsFailure.ShouldBeTrue();
        (await LoadAsync(h, id)).Steps.Single(s => s.Step == StepKind.Trim).State.ShouldBe(StepState.Processing);

        (await h.CreateRecoveryService(new FakeProcessLiveness(ProcessLiveness.Dead)).RecoverAsync(CancellationToken.None))
            .IsSuccess.ShouldBeTrue();

        SessionView recovered = (await service.LoadAsync(id, CancellationToken.None)).Value;
        recovered.Steps.Single(s => s.Step == StepKind.Trim).State.ShouldBe(StepState.Interrupted);
        recovered.AvailableCommands.ShouldContain(CommandKind.Retry);
        recovered.TrimAdjustment.ShouldBeNull();
        SessionAggregate after = await LoadAsync(h, id);
        after.Revisions.Single(r => r.Id == r1).IsValid.ShouldBeTrue();
        after.Reviews.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_customer_design_trim_is_adjusted_the_same_way()
    {
        using SessionServiceHarness h = new();
        ISessionService service = h.CreateService();
        (SessionId id, SessionView review) = await AtTrimReviewAsync(h, service, type: WorkflowType.PrepareCustomerDesign);

        SessionView adjusted = await MustAsync(service.ExecuteAsync(
            id, Adjust(review.TrimAdjustment!, Wider), "tester", CancellationToken.None));

        adjusted.CurrentStep!.Step.ShouldBe(StepKind.Trim);
        adjusted.CurrentStep.State.ShouldBe(StepState.ReviewRequired);
        adjusted.CurrentArtefact!.Facts.PixelWidth.ShouldBe(Wider.Width);
        adjusted.Steps.Single(s => s.Step == StepKind.PrintDimensions).State.ShouldBe(StepState.Waiting);
    }

    internal static WorkflowCommand.AdjustTrimFromReview Adjust(TrimAdjustmentView target, TrimBounds crop) =>
        new(target.ResultRevisionId, target.ResultSha256, target.PreTrimRevisionId, target.PreTrimSha256, crop);

    /// <summary>A trim under review, produced automatically with a 1-px margin unless told otherwise.</summary>
    internal static async Task<(SessionId Id, SessionView Review)> AtTrimReviewAsync(
        SessionServiceHarness h, ISessionService service, bool enhance = false, string? source = null,
        TrimMargin? margin = null, WorkflowType type = WorkflowType.PrepareAsset)
    {
        SessionId id = await StartAsync(service, source ?? h.WriteBorderedSourcePng(), type, enhance);
        await MustAsync(service.ExecuteAsync(id, new WorkflowCommand.SetTrimParameters(margin ?? TrimMargin.Uniform(1)),
            "tester", CancellationToken.None));
        SessionView review = await MustAsync(service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Trim), "tester", CancellationToken.None));
        review.CurrentStep!.Step.ShouldBe(StepKind.Trim);
        review.CurrentStep.State.ShouldBe(StepState.ReviewRequired);
        return (id, review);
    }

    private static async Task<SessionId> StartAsync(ISessionService service, string source, WorkflowType type, bool enhance)
    {
        SessionId id = (await service.ImportAsync(type, source, "adjust", "tester", CancellationToken.None)).Value.Id;
        await MustAsync(service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None));
        if (enhance)
        {
            SessionView enhanced = await MustAsync(service.ExecuteAsync(
                id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None));
            await MustAsync(service.ExecuteAsync(id,
                new WorkflowCommand.Approve(StepKind.Enhancement, enhanced.CurrentArtefact!.Sha256), "tester", CancellationToken.None));
        }
        else
        {
            await MustAsync(service.ExecuteAsync(id, new WorkflowCommand.Skip(StepKind.Enhancement), "tester", CancellationToken.None));
        }

        await MustAsync(service.ExecuteAsync(id, new WorkflowCommand.Skip(StepKind.BackgroundRemoval), "tester", CancellationToken.None));
        return id;
    }

    private static async Task<SessionAggregate> LoadAsync(SessionServiceHarness h, SessionId id) =>
        (await h.Repository.LoadAsync(id, CancellationToken.None)).Value!;

    internal static async Task<SessionView> MustAsync(Task<OperationResult<SessionView>> task)
    {
        OperationResult<SessionView> result = await task;
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : "");
        return result.Value;
    }

    private sealed class FailingCrop : IManualCropProcessor
    {
        public string ProcessorId => "failing-crop";

        public Task<OperationResult<ManualCropResult>> CropAsync(ManualCropRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult.Fail<ManualCropResult>(FailureCode.OutputUnreadable, "synthetic crop failure"));
    }
}
