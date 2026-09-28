using System.IO;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Reviews;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Delivery;
using PrintFlow.Infrastructure.Sqlite;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Delivery;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Persistence;

/// <summary>
/// SCRUM-11145 final-save coordinator through the real session service, SQLite journal and
/// Windows NTFS delivery adapter on GUID-owned synthetic data. External processors are fakes.
/// </summary>
[Collection(SqliteCollection.Name)]
public sealed class FinalSaveCoordinatorTests
{
    [Fact]
    public async Task Tiff_final_review_approves_the_exact_revision_then_delivers_a_verified_copy()
    {
        using SessionServiceHarness h = new();
        ISessionService workflow = FinalSaveFixtures.TiffService(h);
        SessionView review = await FinalSaveFixtures.TiffAtFinalReviewAsync(h, workflow);
        var coordinator = new FinalSaveCoordinator(workflow, FinalSaveFixtures.Delivery(h), "tester");
        string folder = FinalSaveFixtures.NewFolder("tiff");
        try
        {
            List<FinalSaveStage> stages = [];
            FinalSaveRequest request = FinalSaveFixtures.Confirm(review, folder, "customer_A4.tif");
            int reviewsBefore = (await FinalSaveFixtures.LoadAsync(h, review.Id)).Reviews.Count;
            FinalSaveResult result = await coordinator.ConfirmAndSaveAsync(request,
                new SynchronousProgress<FinalSaveObservation>(o => { if (stages.Count == 0 || stages[^1] != o.Stage) stages.Add(o.Stage); }),
                CancellationToken.None);

            result.Approval.ShouldBe(ApprovalOutcome.Succeeded);
            result.Delivery!.Code.ShouldBe(DeliveryCode.Delivered, result.Delivery.Detail);
            result.Artifact.ShouldBe(new ArtifactKey(review.Id, ArtifactKind.ApprovedPrintTiff, review.CurrentArtefact!.RevisionId.Value));
            stages.ShouldBe([FinalSaveStage.CheckingDraft, FinalSaveStage.Approving, FinalSaveStage.Approved, FinalSaveStage.Saving]);
            SessionAggregate after = await FinalSaveFixtures.LoadAsync(h, review.Id);
            after.Reviews.Count.ShouldBe(reviewsBefore + 1);
            after.Reviews.Single(r => r.SubjectId == review.CurrentArtefact.RevisionId.Value).ReviewedSha256.ShouldBe(review.CurrentArtefact.Sha256);
            string approvedPath = h.FileWorkspace.ResolveAbsolute(after.Outputs.Single().File);
            File.ReadAllBytes(Path.Combine(folder, "customer_A4.tif")).ShouldBe(File.ReadAllBytes(approvedPath));
            (await coordinator.Delivery.GetLastSuccessfulDestinationAsync(CancellationToken.None)).Value!.Folder.ShouldBe(folder);
            after.Session.State.ShouldNotBe(SessionState.Completed, "saving never completes the session");
        }
        finally { FinalSaveFixtures.Remove(folder); }
    }

    [Fact]
    public async Task Png_final_trim_review_promotes_once_and_delivers_the_promoted_bytes()
    {
        using SessionServiceHarness h = new();
        ISessionService service = h.CreateService();
        var workflow = new ScriptedApprovalSessionService(service);
        SessionView review = await FinalSaveFixtures.PngAtFinalReviewAsync(h, service);
        var coordinator = new FinalSaveCoordinator(workflow, FinalSaveFixtures.Delivery(h), "tester");
        string folder = FinalSaveFixtures.NewFolder("png");
        try
        {
            FinalSaveResult result = await coordinator.ConfirmAndSaveAsync(
                FinalSaveFixtures.Confirm(review, folder, "logo.png"), null, CancellationToken.None);

            result.Approval.ShouldBe(ApprovalOutcome.Succeeded);
            result.PngPreparation.ShouldBe(PngPreparationOutcome.Prepared);
            result.Delivery!.Code.ShouldBe(DeliveryCode.Delivered, result.Delivery.Detail);
            SessionAggregate after = await FinalSaveFixtures.LoadAsync(h, review.Id);
            Revision promoted = after.Revisions.Single(r => r.Operation == OperationKind.PromoteApproved);
            promoted.SourceRevisionId.ShouldBe(review.CurrentArtefact!.RevisionId);
            result.Artifact!.Value.ArtifactId.ShouldBe(promoted.Id.Value);
            File.ReadAllBytes(Path.Combine(folder, "logo.png"))
                .ShouldBe(File.ReadAllBytes(h.FileWorkspace.ResolveAbsolute(promoted.File)));
            (workflow.ApproveCalls, workflow.PromoteCalls).ShouldBe((1, 1));
            var counts = await FinalSaveFixtures.WorkCountsAsync(h, review.Id);

            // A stale second confirmation of the same review is no longer a final review: nothing runs.
            FinalSaveResult stale = await coordinator.ConfirmAndSaveAsync(
                FinalSaveFixtures.Confirm(review, folder, "logo.png"), null, CancellationToken.None);
            stale.Approval.ShouldBe(ApprovalOutcome.NotAttempted);
            stale.Delivery.ShouldBeNull();

            // The existing promotion is returned, never repeated.
            (await service.PromoteReviewedPngAsync(review.Id, review.CurrentArtefact.RevisionId,
                review.CurrentArtefact.Sha256, "tester", CancellationToken.None)).IsSuccess.ShouldBeTrue();
            // An approved-only save of the same name and folder coalesces with no second copy.
            FinalSaveResult again = await coordinator.SaveApprovedAsync(
                FinalSaveFixtures.SaveApproved(result.Artifact.Value, folder, "logo.png"), null, CancellationToken.None);
            again.Delivery!.Code.ShouldBe(DeliveryCode.AlreadyDelivered);
            again.Delivery.DeliveryId.ShouldBe(result.Delivery.DeliveryId);
            (await FinalSaveFixtures.WorkCountsAsync(h, review.Id)).ShouldBe(counts);
            (workflow.ApproveCalls, workflow.PromoteCalls).ShouldBe((1, 1));
            Directory.GetFiles(folder).Length.ShouldBe(1);
        }
        finally { FinalSaveFixtures.Remove(folder); }
    }

    [Fact]
    public async Task A_newer_revision_with_the_same_hash_is_not_approved_through_a_stale_identity()
    {
        using SessionServiceHarness h = new();
        ISessionService workflow = h.CreateService();
        SessionView first = await FinalSaveFixtures.PngAtFinalReviewAsync(h, workflow);
        SessionId id = first.Id;
        (await workflow.ExecuteAsync(id, new WorkflowCommand.Reject(StepKind.Trim, first.CurrentArtefact!.Sha256,
            RejectionReason.Other), "tester", CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await workflow.ExecuteAsync(id, new WorkflowCommand.Retry(StepKind.Trim), "tester", CancellationToken.None))
            .IsSuccess.ShouldBeTrue();
        SessionView second = (await workflow.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.Trim),
            "tester", CancellationToken.None)).Value;
        // Deterministic trim: identical bytes, new result identity.
        second.CurrentArtefact!.Sha256.ShouldBe(first.CurrentArtefact.Sha256);
        second.CurrentArtefact.RevisionId.ShouldNotBe(first.CurrentArtefact.RevisionId);
        int reviewsBefore = (await FinalSaveFixtures.LoadAsync(h, id)).Reviews.Count;

        OperationResult<SessionView> direct = await workflow.ApproveExactReviewAsync(id, StepKind.Trim,
            first.CurrentArtefact.RevisionId, first.CurrentArtefact.Sha256, "tester", CancellationToken.None);
        direct.IsFailure.ShouldBeTrue();
        direct.Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);

        var delivery = new ObservedDeliveryService(FinalSaveFixtures.Delivery(h));
        var coordinator = new FinalSaveCoordinator(workflow, delivery, "tester");
        string folder = FinalSaveFixtures.NewFolder("stale");
        try
        {
            FinalSaveResult result = await coordinator.ConfirmAndSaveAsync(
                FinalSaveFixtures.Confirm(first, folder, "stale.png"), null, CancellationToken.None);
            result.Approval.ShouldBe(ApprovalOutcome.NotAttempted);
            delivery.DeliverCalls.ShouldBe(0);
            Directory.EnumerateFileSystemEntries(folder).ShouldBeEmpty();
            SessionAggregate after = await FinalSaveFixtures.LoadAsync(h, id);
            after.Reviews.Count.ShouldBe(reviewsBefore);
            after.Steps.Single(s => s.Step == StepKind.Trim).State.ShouldBe(StepState.ReviewRequired);
        }
        finally { FinalSaveFixtures.Remove(folder); }
    }

    [Theory]
    [InlineData(ScriptedApprovalSessionService.Mode.RefuseWithoutCommit, ApprovalOutcome.Refused)]
    [InlineData(ScriptedApprovalSessionService.Mode.RefuseAndFailReload, ApprovalOutcome.Unknown)]
    [InlineData(ScriptedApprovalSessionService.Mode.CommitThenReportFailure, ApprovalOutcome.Succeeded)]
    public async Task Approval_outcome_is_resolved_from_authority_and_only_success_exports(
        ScriptedApprovalSessionService.Mode mode, ApprovalOutcome expected)
    {
        using SessionServiceHarness h = new();
        ISessionService service = FinalSaveFixtures.TiffService(h);
        SessionView review = await FinalSaveFixtures.TiffAtFinalReviewAsync(h, service);
        var workflow = new ScriptedApprovalSessionService(service) { Approval = mode };
        var delivery = new ObservedDeliveryService(FinalSaveFixtures.Delivery(h));
        var coordinator = new FinalSaveCoordinator(workflow, delivery, "tester");
        string folder = FinalSaveFixtures.NewFolder("approval");
        try
        {
            FinalSaveResult result = await coordinator.ConfirmAndSaveAsync(
                FinalSaveFixtures.Confirm(review, folder, "approval.tif"), null, CancellationToken.None);
            result.Approval.ShouldBe(expected);
            workflow.ApproveCalls.ShouldBe(1, "an uncertain approval is read back, never re-approved");
            SessionAggregate after = await FinalSaveFixtures.LoadAsync(h, review.Id);
            if (expected == ApprovalOutcome.Succeeded)
            {
                result.Delivery!.Code.ShouldBe(DeliveryCode.Delivered);
                after.Reviews.Count(r => r.SubjectKind == ReviewSubjectKind.PrintOutput).ShouldBe(1);
                File.Exists(Path.Combine(folder, "approval.tif")).ShouldBeTrue();
            }
            else
            {
                result.Delivery.ShouldBeNull();
                delivery.DeliverCalls.ShouldBe(0);
                after.Reviews.ShouldNotContain(r => r.SubjectKind == ReviewSubjectKind.PrintOutput);
                Directory.EnumerateFileSystemEntries(folder).ShouldBeEmpty();
            }
        }
        finally { FinalSaveFixtures.Remove(folder); }
    }

    [Theory]
    [InlineData("", "draft.tif", DeliveryCode.UnsupportedDestination)]
    [InlineData("relative\\folder", "draft.tif", DeliveryCode.UnsupportedDestination)]
    [InlineData("\\\\server\\share\\jobs", "draft.tif", DeliveryCode.UnsupportedDestination)]
    [InlineData("MISSING", "draft.tif", DeliveryCode.DestinationUnavailable)]
    [InlineData("OK", "draft.png", DeliveryCode.InvalidName)]
    [InlineData("OK", "draft", DeliveryCode.InvalidName)]
    [InlineData("OK", "CON.tif", DeliveryCode.InvalidName)]
    [InlineData("WORKSPACE", "draft.tif", DeliveryCode.ProtectedDestination)]
    public async Task An_invalid_draft_is_refused_before_any_approval(string folderKind, string fileName, DeliveryCode expected)
    {
        using SessionServiceHarness h = new();
        ISessionService workflow = FinalSaveFixtures.TiffService(h);
        SessionView review = await FinalSaveFixtures.TiffAtFinalReviewAsync(h, workflow);
        var delivery = new ObservedDeliveryService(FinalSaveFixtures.Delivery(h));
        var coordinator = new FinalSaveCoordinator(workflow, delivery, "tester");
        string ok = FinalSaveFixtures.NewFolder("draft");
        try
        {
            string folder = folderKind switch
            {
                "OK" => ok,
                "MISSING" => Path.Combine(ok, "not-created"),
                "WORKSPACE" => Path.Combine(h.Workspace.Root),
                _ => folderKind,
            };
            FinalSaveResult result = await coordinator.ConfirmAndSaveAsync(
                FinalSaveFixtures.Confirm(review, folder, fileName), null, CancellationToken.None);
            result.Approval.ShouldBe(ApprovalOutcome.NotAttempted);
            result.DraftRefusal!.Refusal.ShouldBe(expected);
            delivery.DeliverCalls.ShouldBe(0);
            SessionAggregate after = await FinalSaveFixtures.LoadAsync(h, review.Id);
            after.Reviews.ShouldNotContain(r => r.SubjectKind == ReviewSubjectKind.PrintOutput);
            after.Steps.Single(s => s.Step == StepKind.PhotoshopOutput).State.ShouldBe(StepState.ReviewRequired);
            Directory.EnumerateFileSystemEntries(ok).ShouldBeEmpty();
        }
        finally { FinalSaveFixtures.Remove(ok); }
    }

    [Fact]
    public async Task Unreviewed_keep_original_png_stays_blocked_and_gains_no_approval()
    {
        using SessionServiceHarness h = new();
        ISessionService workflow = h.CreateService();
        SessionId id = await KeepOriginalExtentPersistenceTests.AtTrim(h, workflow, WorkflowType.PrepareAsset);
        (await workflow.ExecuteAsync(id, new WorkflowCommand.KeepOriginalExtent(), "tester", CancellationToken.None))
            .IsSuccess.ShouldBeTrue();
        (await workflow.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.ApprovedPngExport),
            "tester", CancellationToken.None)).IsSuccess.ShouldBeTrue();
        Revision promoted = (await FinalSaveFixtures.LoadAsync(h, id)).Revisions.Single(r => r.Operation == OperationKind.PromoteApproved);
        var delivery = new ObservedDeliveryService(FinalSaveFixtures.Delivery(h));
        var coordinator = new FinalSaveCoordinator(workflow, delivery, "tester");
        string folder = FinalSaveFixtures.NewFolder("unreviewed");
        try
        {
            FinalSaveResult result = await coordinator.SaveApprovedAsync(FinalSaveFixtures.SaveApproved(
                new ArtifactKey(id, ArtifactKind.ApprovedAssetPng, promoted.Id.Value), folder, "keep.png"), null, CancellationToken.None);
            result.OfferRefusal.ShouldBe(ArtifactRefusal.ApprovalEvidenceMissing);
            result.Delivery.ShouldBeNull();
            delivery.DeliverCalls.ShouldBe(0);
            (await FinalSaveFixtures.LoadAsync(h, id)).Reviews.ShouldBeEmpty();
            Directory.EnumerateFileSystemEntries(folder).ShouldBeEmpty();
        }
        finally { FinalSaveFixtures.Remove(folder); }
    }

    [Fact]
    public async Task Collision_and_cancellation_keep_approval_and_retries_never_reprocess_or_reapprove()
    {
        using SessionServiceHarness h = new();
        ISessionService service = FinalSaveFixtures.TiffService(h);
        SessionView review = await FinalSaveFixtures.TiffAtFinalReviewAsync(h, service);
        var workflow = new ScriptedApprovalSessionService(service);
        var coordinator = new FinalSaveCoordinator(workflow, FinalSaveFixtures.Delivery(h), "tester");
        string folder = FinalSaveFixtures.NewFolder("collision");
        try
        {
            byte[] foreign = [1, 2, 3, 4];
            File.WriteAllBytes(Path.Combine(folder, "taken.tif"), foreign);
            FinalSaveResult collided = await coordinator.ConfirmAndSaveAsync(
                FinalSaveFixtures.Confirm(review, folder, "taken.tif"), null, CancellationToken.None);
            collided.Approval.ShouldBe(ApprovalOutcome.Succeeded);
            collided.Delivery!.Code.ShouldBe(DeliveryCode.Collision);
            collided.Delivery.SuggestedFileName.ShouldBe("taken (2).tif");
            File.ReadAllBytes(Path.Combine(folder, "taken.tif")).ShouldBe(foreign);
            var afterApproval = await FinalSaveFixtures.WorkCountsAsync(h, review.Id);
            (await FinalSaveFixtures.LoadAsync(h, review.Id)).Reviews.Count(r => r.SubjectKind == ReviewSubjectKind.PrintOutput).ShouldBe(1);
            ArtifactKey key = collided.Artifact!.Value;

            using (CancellationTokenSource cancelled = new())
            {
                cancelled.Cancel();
                FinalSaveResult stopped = await coordinator.SaveApprovedAsync(
                    FinalSaveFixtures.SaveApproved(key, folder, "cancelled.tif"), null, cancelled.Token);
                stopped.Delivery!.Code.ShouldBe(DeliveryCode.Cancelled);
                File.Exists(Path.Combine(folder, "cancelled.tif")).ShouldBeFalse();
            }

            Guid retryIntent = Guid.NewGuid();
            FinalSaveResult saved = await coordinator.SaveApprovedAsync(
                FinalSaveFixtures.SaveApproved(key, folder, "taken (2).tif", retryIntent), null, CancellationToken.None);
            saved.Delivery!.Code.ShouldBe(DeliveryCode.Delivered, saved.Delivery.Detail);
            FinalSaveResult repeated = await coordinator.SaveApprovedAsync(
                FinalSaveFixtures.SaveApproved(key, folder, "taken (2).tif", retryIntent), null, CancellationToken.None);
            repeated.Delivery!.Code.ShouldBe(DeliveryCode.AlreadyDelivered);
            repeated.Delivery.DeliveryId.ShouldBe(saved.Delivery.DeliveryId);

            (await FinalSaveFixtures.WorkCountsAsync(h, review.Id)).ShouldBe(afterApproval);
            workflow.ApproveCalls.ShouldBe(1);
            workflow.PromoteCalls.ShouldBe(0);
        }
        finally { FinalSaveFixtures.Remove(folder); }
    }

    [Fact]
    public async Task Uncertain_publication_is_reconciled_at_its_recorded_destination_without_a_second_copy()
    {
        using SessionServiceHarness h = new();
        ISessionService workflow = FinalSaveFixtures.TiffService(h);
        SessionView review = await FinalSaveFixtures.TiffAtFinalReviewAsync(h, workflow);
        var faulty = new FinalSaveCoordinator(workflow,
            FinalSaveFixtures.Delivery(h, new LostPublicationAckFileSystem(new WindowsDeliveryFileSystem())), "tester");
        string folder = FinalSaveFixtures.NewFolder("uncertain");
        string other = FinalSaveFixtures.NewFolder("other");
        try
        {
            FinalSaveResult result = await faulty.ConfirmAndSaveAsync(
                FinalSaveFixtures.Confirm(review, folder, "maybe.tif"), null, CancellationToken.None);
            result.Approval.ShouldBe(ApprovalOutcome.Succeeded);
            result.Delivery!.Code.ShouldBe(DeliveryCode.NeedsReconciliation);
            Guid deliveryId = result.Delivery.DeliveryId!.Value;
            File.Exists(Path.Combine(folder, "maybe.tif")).ShouldBeTrue("a possibly published file is not 'nothing saved'");

            // A newer successful save elsewhere changes the remembered folder, not the recorded request.
            var restarted = new FinalSaveCoordinator(workflow, FinalSaveFixtures.Delivery(h), "tester");
            (await restarted.SaveApprovedAsync(FinalSaveFixtures.SaveApproved(result.Artifact!.Value, other, "other.tif"),
                null, CancellationToken.None)).Delivery!.Code.ShouldBe(DeliveryCode.Delivered);
            (await restarted.Delivery.GetLastSuccessfulDestinationAsync(CancellationToken.None)).Value!.Folder.ShouldBe(other);
            DeliveryState pending = (await restarted.Delivery.GetDeliveryStateAsync(review.Id, result.Artifact, CancellationToken.None))
                .Value.Single(s => s.DeliveryId == deliveryId);
            pending.Status.ShouldBe("Pending");
            pending.RequestedFolder.ShouldBe(folder);

            DeliveryOutcome reconciled = await restarted.ReconcileAsync(deliveryId, null, CancellationToken.None);
            reconciled.Code.ShouldBe(DeliveryCode.Delivered, reconciled.Detail);
            reconciled.DeliveryId.ShouldBe(deliveryId);
            reconciled.FinalPath.ShouldBe(Path.Combine(folder, "maybe.tif"));
            Directory.GetFiles(folder).ShouldBe([Path.Combine(folder, "maybe.tif")]);
            (await FinalSaveFixtures.LoadAsync(h, review.Id)).Reviews.Count(r => r.SubjectKind == ReviewSubjectKind.PrintOutput).ShouldBe(1);
        }
        finally { FinalSaveFixtures.Remove(folder, other); }
    }

    [Fact]
    public async Task Open_holds_the_verified_lease_through_dispatch_and_disposes_it_on_every_path()
    {
        using SessionServiceHarness h = new();
        ISessionService workflow = FinalSaveFixtures.TiffService(h);
        SessionView review = await FinalSaveFixtures.TiffAtFinalReviewAsync(h, workflow);
        var coordinator = new FinalSaveCoordinator(workflow, FinalSaveFixtures.Delivery(h), "tester");
        string folder = FinalSaveFixtures.NewFolder("open");
        try
        {
            FinalSaveResult saved = await coordinator.ConfirmAndSaveAsync(
                FinalSaveFixtures.Confirm(review, folder, "open.tif"), null, CancellationToken.None);
            Guid deliveryId = saved.Delivery!.DeliveryId!.Value;
            var counts = await FinalSaveFixtures.WorkCountsAsync(h, review.Id);
            var shell = new RecordingDeliveredFileShell();

            for (int i = 0; i < 3; i++)
            {
                OpenFolderOutcome opened = await coordinator.OpenContainingFolderAsync(deliveryId, shell.SelectInFolder, CancellationToken.None);
                opened.Code.ShouldBe(OpenFolderCode.Dispatched);
                shell.LastLease!.IsDisposed.ShouldBeTrue();
            }
            shell.Dispatches.ShouldAllBe(d => d.Path == Path.Combine(folder, "open.tif") && d.LeaseLive);

            shell.Throw = true;
            OpenFolderOutcome failed = await coordinator.OpenContainingFolderAsync(deliveryId, shell.SelectInFolder, CancellationToken.None);
            failed.Code.ShouldBe(OpenFolderCode.ShellFailed);
            shell.LastLease!.IsDisposed.ShouldBeTrue();
            shell.Throw = false;

            File.WriteAllBytes(Path.Combine(folder, "open.tif"), [9, 9, 9]);
            (await coordinator.OpenContainingFolderAsync(deliveryId, shell.SelectInFolder, CancellationToken.None))
                .Code.ShouldBe(OpenFolderCode.Changed);
            File.Delete(Path.Combine(folder, "open.tif"));
            int dispatched = shell.Dispatches.Count;
            (await coordinator.OpenContainingFolderAsync(deliveryId, shell.SelectInFolder, CancellationToken.None))
                .Code.ShouldBe(OpenFolderCode.Missing);
            shell.Dispatches.Count.ShouldBe(dispatched, "no shell request for a file that is not the delivered one");

            (await coordinator.Delivery.GetDeliveryStateAsync(review.Id, saved.Artifact, CancellationToken.None)).Value.Count.ShouldBe(1);
            (await FinalSaveFixtures.WorkCountsAsync(h, review.Id)).ShouldBe(counts);
        }
        finally { FinalSaveFixtures.Remove(folder); }
    }

    [Fact]
    public async Task Save_another_copy_needs_a_fresh_missing_observation_and_links_one_successor()
    {
        using SessionServiceHarness h = new();
        ISessionService workflow = FinalSaveFixtures.TiffService(h);
        SessionView review = await FinalSaveFixtures.TiffAtFinalReviewAsync(h, workflow);
        var delivery = new ObservedDeliveryService(FinalSaveFixtures.Delivery(h));
        var coordinator = new FinalSaveCoordinator(workflow, delivery, "tester");
        string folder = FinalSaveFixtures.NewFolder("another");
        try
        {
            FinalSaveResult saved = await coordinator.ConfirmAndSaveAsync(
                FinalSaveFixtures.Confirm(review, folder, "copy.tif"), null, CancellationToken.None);
            Guid prior = saved.Delivery!.DeliveryId!.Value;
            DeliveryOutcome present = await coordinator.SaveAnotherCopyAsync(prior, Guid.NewGuid(), null, CancellationToken.None);
            present.Code.ShouldBe(DeliveryCode.AlreadyDelivered);
            delivery.ReplaceCalls.ShouldBe(0);

            // Ordinary retry of a missing delivery creates nothing.
            File.Delete(Path.Combine(folder, "copy.tif"));
            (await coordinator.ReconcileAsync(prior, null, CancellationToken.None)).Code.ShouldBe(DeliveryCode.DeliveredFileMissing);
            File.Exists(Path.Combine(folder, "copy.tif")).ShouldBeFalse();

            DeliveryOutcome replaced = await coordinator.SaveAnotherCopyAsync(prior, Guid.NewGuid(), null, CancellationToken.None);
            replaced.Code.ShouldBe(DeliveryCode.Delivered, replaced.Detail);
            replaced.DeliveryId.ShouldNotBe(prior);
            IReadOnlyList<DeliveryState> states = (await coordinator.Delivery.GetDeliveryStateAsync(
                review.Id, saved.Artifact, CancellationToken.None)).Value;
            states.Count.ShouldBe(2);
            states.Single(s => s.DeliveryId == replaced.DeliveryId).ReplacementOfDeliveryId.ShouldBe(prior);
            (await FinalSaveFixtures.LoadAsync(h, review.Id)).Reviews.Count(r => r.SubjectKind == ReviewSubjectKind.PrintOutput).ShouldBe(1);
        }
        finally { FinalSaveFixtures.Remove(folder); }
    }

    private sealed class SynchronousProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
