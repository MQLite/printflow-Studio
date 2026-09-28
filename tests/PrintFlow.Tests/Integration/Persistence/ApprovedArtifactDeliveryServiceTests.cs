using System.IO;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Automation;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Delivery;
using PrintFlow.Infrastructure.Sqlite;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Delivery;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Persistence;

[Collection(SqliteCollection.Name)]
public sealed class ApprovedArtifactDeliveryServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Interrupted_staging_checkpoint_retries_with_new_attempt_and_preserves_unowned_temp(
        bool committedStaging)
    {
        using SessionServiceHarness h = new();
        ArtifactKey key = await ReviewedPngAsync(h);
        var journal = new SqliteDeliveryRepository(h.Database.Factory);
        var interrupted = new LostAcknowledgementRepository(journal)
        {
            StagingFailure = committedStaging ? StagingFailureMode.AfterCommit : StagingFailureMode.BeforeCommit,
        };
        var service = new ApprovedArtifactDeliveryService(h.Repository, interrupted, h.FileWorkspace,
            new WindowsDeliveryFileSystem(), [h.Workspace.Root], h.Clock, h.TiffReviewDecoder);
        DeliveryOffer offer = (await service.GetOfferAsync(key, CancellationToken.None)).Offer!;
        byte[] approvedBytes = File.ReadAllBytes(h.FileWorkspace.ResolveAbsolute(offer.Artifact.File));
        string folder = Path.Combine(Path.GetTempPath(), "pf-delivery-interrupted-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var request = new DeliveryRequest(Guid.NewGuid(), key, offer.Artifact.ApprovedSha256,
                offer.Artifact.ReviewId, folder, "copy.png", offer.OfferVersion);
            if (committedStaging)
                (await service.DeliverAsync(request, null, CancellationToken.None)).Code
                    .ShouldBe(DeliveryCode.PersistenceFailed);
            else
                await Should.ThrowAsync<IOException>(() => service.DeliverAsync(request, null, CancellationToken.None));
            DeliveryJournalEntry first = (await journal.FindByRequestAsync(request.RequestId,
                CancellationToken.None)).Value!;
            first.Attempt!.State.ShouldBe(committedStaging
                ? DeliveryAttemptState.Staging : DeliveryAttemptState.Intent);
            string oldStage = Path.Combine(folder, first.Attempt.StagingLeaf);
            File.Exists(oldStage).ShouldBe(!committedStaging);
            byte[]? unownedBytes = committedStaging ? null : File.ReadAllBytes(oldStage);
            File.Exists(Path.Combine(folder, "copy.png")).ShouldBeFalse();
            ReadPreference(h).ShouldBeNull();

            var reopened = NewDelivery(h);
            DeliveryOffer freshOffer = (await reopened.GetOfferAsync(key, CancellationToken.None)).Offer!;
            DeliveryOutcome retry = await reopened.DeliverAsync(request with
            { SelectionVersion = freshOffer.OfferVersion }, null, CancellationToken.None);
            retry.Code.ShouldBe(DeliveryCode.Delivered, retry.Detail);
            retry.DeliveryId.ShouldBe(first.Delivery.DeliveryId);
            File.ReadAllBytes(Path.Combine(folder, "copy.png")).ShouldBe(approvedBytes);
            File.ReadAllBytes(h.FileWorkspace.ResolveAbsolute(offer.Artifact.File)).ShouldBe(approvedBytes);
            File.Exists(oldStage).ShouldBe(!committedStaging);
            if (unownedBytes is not null) File.ReadAllBytes(oldStage).ShouldBe(unownedBytes);
            var final = (await journal.FindByRequestAsync(request.RequestId, CancellationToken.None)).Value!;
            final.Attempt!.AttemptNumber.ShouldBe(2);
            final.Attempt.StagingLeaf.ShouldNotBe(first.Attempt.StagingLeaf);
            ReadPreference(h).ShouldNotBeNull();
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Theory]
    [InlineData(SyntheticSaveFault.UnwritableStage, DeliveryCode.DestinationNotWritable)]
    [InlineData(SyntheticSaveFault.DisconnectedStage, DeliveryCode.DestinationUnavailable)]
    [InlineData(SyntheticSaveFault.CopyIo, DeliveryCode.CopyFailed)]
    public async Task Synthetic_prepublication_fault_preserves_source_and_preference_then_retries(
        SyntheticSaveFault fault, DeliveryCode expected)
    {
        using SessionServiceHarness h = new();
        ArtifactKey key = await ReviewedPngAsync(h);
        var faultyFileSystem = new OneShotFaultFileSystem(fault);
        var service = new ApprovedArtifactDeliveryService(h.Repository,
            new SqliteDeliveryRepository(h.Database.Factory), h.FileWorkspace,
            faultyFileSystem, [h.Workspace.Root], h.Clock, h.TiffReviewDecoder);
        string root = Path.Combine(Path.GetTempPath(), "pf-delivery-fault-" + Guid.NewGuid().ToString("N"));
        string baselineFolder = Path.Combine(root, "baseline");
        string folder = Path.Combine(root, "fault");
        Directory.CreateDirectory(baselineFolder);
        Directory.CreateDirectory(folder);
        try
        {
            var baselineService = NewDelivery(h);
            DeliveryOffer baselineOffer = (await baselineService.GetOfferAsync(key, CancellationToken.None)).Offer!;
            DeliveryOutcome baseline = await baselineService.DeliverAsync(new DeliveryRequest(Guid.NewGuid(),
                key, baselineOffer.Artifact.ApprovedSha256, baselineOffer.Artifact.ReviewId,
                baselineFolder, "prior.png", baselineOffer.OfferVersion), null, CancellationToken.None);
            baseline.Code.ShouldBe(DeliveryCode.Delivered, baseline.Detail);
            string successfulPreference = ReadPreference(h)!;
            successfulPreference.ShouldNotBeNull();
            DeliveryOffer offer = (await service.GetOfferAsync(key, CancellationToken.None)).Offer!;
            string source = h.FileWorkspace.ResolveAbsolute(offer.Artifact.File);
            byte[] approvedBytes = File.ReadAllBytes(source);
            var request = new DeliveryRequest(Guid.NewGuid(), key, offer.Artifact.ApprovedSha256,
                offer.Artifact.ReviewId, folder, "copy.png", offer.OfferVersion);
            DeliveryOutcome failed = await service.DeliverAsync(request, null, CancellationToken.None);
            failed.Code.ShouldBe(expected, failed.Detail);
            File.ReadAllBytes(source).ShouldBe(approvedBytes);
            File.Exists(Path.Combine(folder, "copy.png")).ShouldBeFalse();
            Directory.EnumerateFiles(folder, "*.partial").ShouldBeEmpty();
            ReadPreference(h).ShouldBe(successfulPreference);
            DeliveryOutcome retry = await service.DeliverAsync(request, null, CancellationToken.None);
            retry.Code.ShouldBe(DeliveryCode.Delivered, retry.Detail);
            File.ReadAllBytes(Path.Combine(folder, "copy.png")).ShouldBe(approvedBytes);
            File.ReadAllBytes(source).ShouldBe(approvedBytes);
            ReadPreference(h).ShouldNotBe(successfulPreference);
            (await service.GetDeliveryStateAsync(key.SessionId, key, CancellationToken.None))
                .Value.Count.ShouldBe(2);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Unknown_publication_ack_reconciles_owned_final_before_any_recopy()
    {
        using SessionServiceHarness h = new();
        ArtifactKey key = await ReviewedPngAsync(h);
        var delivery = new ApprovedArtifactDeliveryService(h.Repository,
            new SqliteDeliveryRepository(h.Database.Factory), h.FileWorkspace,
            new UnknownPublicationAckFileSystem(), [h.Workspace.Root], h.Clock, h.TiffReviewDecoder);
        DeliveryOffer offer = (await delivery.GetOfferAsync(key, CancellationToken.None)).Offer!;
        string folder = Path.Combine(Path.GetTempPath(), "pf-delivery-uncertain-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var request = new DeliveryRequest(Guid.NewGuid(), key, offer.Artifact.ApprovedSha256,
                offer.Artifact.ReviewId, folder, "uncertain.png", offer.OfferVersion);
            DeliveryOutcome uncertain = await delivery.DeliverAsync(request, null, CancellationToken.None);
            uncertain.Code.ShouldBe(DeliveryCode.NeedsReconciliation);
            File.Exists(Path.Combine(folder, "uncertain.png")).ShouldBeTrue();
            (await new SqliteDeliveryRepository(h.Database.Factory).FindByIdAsync(
                uncertain.DeliveryId!.Value, CancellationToken.None)).Value!.Delivery.Status.ShouldBe("Pending");
            DeliveryOutcome recovered = await NewDelivery(h).ReconcileAsync(
                uncertain.DeliveryId.Value, null, CancellationToken.None);
            recovered.Code.ShouldBe(DeliveryCode.Delivered, recovered.Detail);
            recovered.DeliveryId.ShouldBe(uncertain.DeliveryId);
            Directory.EnumerateFiles(folder, "*.partial").ShouldBeEmpty();
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public async Task Lost_ready_and_delivered_acknowledgements_resolve_by_durable_readback()
    {
        using SessionServiceHarness h = new();
        ArtifactKey key = await ReviewedPngAsync(h);
        var repository = new LostAcknowledgementRepository(new SqliteDeliveryRepository(h.Database.Factory));
        var delivery = new ApprovedArtifactDeliveryService(h.Repository, repository, h.FileWorkspace,
            new WindowsDeliveryFileSystem(), [h.Workspace.Root], h.Clock, h.TiffReviewDecoder);
        DeliveryOffer offer = (await delivery.GetOfferAsync(key, CancellationToken.None)).Offer!;
        string folder = Path.Combine(Path.GetTempPath(), "pf-delivery-ack-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            DeliveryOutcome outcome = await delivery.DeliverAsync(new DeliveryRequest(Guid.NewGuid(), key,
                offer.Artifact.ApprovedSha256, offer.Artifact.ReviewId, folder, "ack.png",
                offer.OfferVersion), null, CancellationToken.None);
            outcome.Code.ShouldBe(DeliveryCode.Delivered, outcome.Detail);
            repository.ReadyAcknowledgementLost.ShouldBeTrue();
            repository.DeliveredAcknowledgementLost.ShouldBeTrue();
            (await new SqliteDeliveryRepository(h.Database.Factory).FindByIdAsync(outcome.DeliveryId!.Value,
                CancellationToken.None)).Value!.Delivery.Status.ShouldBe("Delivered");
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public async Task Changed_approved_source_refuses_before_final_or_preference()
    {
        using SessionServiceHarness h = new();
        ArtifactKey key = await ReviewedPngAsync(h);
        var delivery = NewDelivery(h);
        DeliveryOffer offer = (await delivery.GetOfferAsync(key, CancellationToken.None)).Offer!;
        string source = h.FileWorkspace.ResolveAbsolute(offer.Artifact.File);
        File.WriteAllBytes(source, [9, 8, 7]);
        string folder = Path.Combine(Path.GetTempPath(), "pf-delivery-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            DeliveryOutcome outcome = await delivery.DeliverAsync(new DeliveryRequest(Guid.NewGuid(), key,
                offer.Artifact.ApprovedSha256, offer.Artifact.ReviewId, folder, "changed.png",
                offer.OfferVersion), null, CancellationToken.None);
            outcome.Code.ShouldBe(DeliveryCode.SourceChanged);
            Directory.EnumerateFileSystemEntries(folder).ShouldBeEmpty();
            using var connection = h.Database.Factory.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM Setting WHERE Key='LAST_SUCCESSFUL_DELIVERY_DESTINATION';";
            Convert.ToInt32(command.ExecuteScalar()).ShouldBe(0);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, true)]
    public async Task Restart_reconciles_owned_ready_state_and_refuses_foreign_final(
        bool publishBeforeRestart, bool foreignFinal, bool recreateStage)
    {
        using SessionServiceHarness h = new();
        ArtifactKey key = await ReviewedPngAsync(h);
        ApprovedArtifact approved = (await NewDelivery(h).GetOfferAsync(key, CancellationToken.None)).Offer!.Artifact;
        var aggregate = (await h.Repository.LoadAsync(key.SessionId, CancellationToken.None)).Value!;
        var files = new WindowsDeliveryFileSystem();
        var repository = new SqliteDeliveryRepository(h.Database.Factory);
        string folder = Path.Combine(Path.GetTempPath(), "pf-delivery-reopen-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var opened = files.Open(h.FileWorkspace.ResolveAbsolute(approved.File), approved.ApprovedSha256,
                approved.ApprovedLength, folder, "ready.png", key.Kind, [h.Workspace.Root],
                aggregate.Snapshot?.OriginalSourcePath);
            opened.IsSuccess.ShouldBeTrue(opened.Detail);
            Guid deliveryId = Guid.NewGuid(); Guid attemptId = Guid.NewGuid();
            using (IDeliveryFileGuard guard = opened.Value!)
            {
                var record = new DeliveryRecord(deliveryId, Guid.NewGuid(), 0, key, approved.ReviewId,
                    approved.ApprovalSubjectKind.ToString(), approved.ApprovalSubjectId,
                    approved.SourceRevisionId, approved.ApprovedSha256, approved.ApprovedLength,
                    folder, "ready.png", guard.ResolvedFolder, guard.FinalPath,
                    guard.VolumeId, guard.DirectoryId, guard.DestinationKey, null, "Pending", h.Clock.GetUtcNow());
                var attempt = new DeliveryAttemptRecord(attemptId, deliveryId, 1, guard.DestinationKey,
                    DeliveryAttemptState.Intent, ".printflow-" + attemptId.ToString("N") + ".partial",
                    guard.DirectoryId, null, null, approved.ApprovedSha256, approved.ApprovedLength,
                    null, null, h.Clock.GetUtcNow(), null);
                (await repository.CreateOrCoalesceAsync(record, attempt, CancellationToken.None))
                    .IsSuccess.ShouldBeTrue();
                DeliveryFileIdentity stage = guard.CreateStaging(attempt.StagingLeaf).Value!;
                (await repository.MarkStagingAsync(attemptId, stage.FileId, stage.CreatedAtUtc,
                    CancellationToken.None)).IsSuccess.ShouldBeTrue();
                (await guard.CopyAndVerifyStageAsync(approved.ApprovedSha256, approved.ApprovedLength,
                    null, CancellationToken.None)).IsSuccess.ShouldBeTrue();
                (await repository.MarkReadyAsync(attemptId, h.Clock.GetUtcNow(), CancellationToken.None))
                    .IsSuccess.ShouldBeTrue();
                if (publishBeforeRestart)
                    guard.PublishNoReplace(attempt.StagingLeaf, stage).IsSuccess.ShouldBeTrue();
                if (foreignFinal)
                {
                    guard.DeleteHeldStaging(stage).IsSuccess.ShouldBeTrue();
                    File.Copy(h.FileWorkspace.ResolveAbsolute(approved.File), guard.FinalPath);
                }
            }
            if (recreateStage)
                File.Copy(Path.Combine(folder, "ready.png"),
                    Path.Combine(folder, ".printflow-" + attemptId.ToString("N") + ".partial"));
            DeliveryOutcome resumed = await NewDelivery(h).ReconcileAsync(deliveryId, null, CancellationToken.None);
            resumed.Code.ShouldBe(foreignFinal || recreateStage ? DeliveryCode.NeedsReconciliation : DeliveryCode.Delivered,
                resumed.Detail);
            resumed.DeliveryId.ShouldBe(deliveryId);
            File.Exists(Path.Combine(folder, "ready.png")).ShouldBeTrue();
            if (recreateStage)
                Directory.EnumerateFiles(folder, "*.partial").ShouldHaveSingleItem();
            else
                Directory.EnumerateFiles(folder, "*.partial").ShouldBeEmpty();
            if (foreignFinal || recreateStage)
                (await repository.FindByIdAsync(deliveryId, CancellationToken.None)).Value!
                    .Delivery.Status.ShouldBe("Pending");
            else
                (await NewDelivery(h).ReconcileAsync(deliveryId, null, CancellationToken.None))
                    .Code.ShouldBe(DeliveryCode.AlreadyDelivered);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public async Task Earlier_approved_tiff_remains_deliverable_while_new_size_is_pending()
    {
        using SessionServiceHarness h = new();
        var workflow = h.CreateService(photoshop: new SyntheticProductionTiffProcessor(h.FileWorkspace));
        SessionId id = (await workflow.ImportAsync(WorkflowType.GeneratePrintTiff, h.WriteSourcePng(),
            "older-size", "tester", CancellationToken.None)).Value.Id;
        (await workflow.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None))
            .IsSuccess.ShouldBeTrue();
        await SetTiffSizeAsync(workflow, id, 200);
        var first = await workflow.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput),
            "tester", CancellationToken.None);
        first.IsSuccess.ShouldBeTrue();
        var firstStep = first.Value.Steps.Single(s => s.Step == StepKind.PhotoshopOutput);
        (await workflow.ExecuteAsync(id, new WorkflowCommand.Approve(StepKind.PhotoshopOutput,
            firstStep.CurrentRevisionSha256!.Value), "tester", CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await workflow.ExecuteAsync(id, new WorkflowCommand.Complete(), "tester", CancellationToken.None))
            .IsSuccess.ShouldBeTrue();
        (await workflow.ExecuteAsync(id, new WorkflowCommand.AddAnotherSize(), "tester", CancellationToken.None))
            .IsSuccess.ShouldBeTrue();
        await SetTiffSizeAsync(workflow, id, 250);
        var second = await workflow.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput),
            "tester", CancellationToken.None);
        second.IsSuccess.ShouldBeTrue();
        var key = new ArtifactKey(id, ArtifactKind.ApprovedPrintTiff, firstStep.CurrentRevisionId!.Value.Value);
        var aggregate = (await h.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        ApprovedArtifactResolver.Resolve(aggregate, key).Refusal.ShouldBe(ArtifactRefusal.None);
        var secondStep = second.Value.Steps.Single(s => s.Step == StepKind.PhotoshopOutput);
        var pendingKey = new ArtifactKey(id, ArtifactKind.ApprovedPrintTiff,
            secondStep.CurrentRevisionId!.Value.Value);
        ApprovedArtifactResolver.Resolve(aggregate, pendingKey).Refusal.ShouldBe(ArtifactRefusal.PendingReview);
        var priorOutput = aggregate.Outputs.Single(o => o.Id.Value == key.ArtifactId);
        ApprovedArtifactResolver.Resolve(aggregate with { Outputs = aggregate.Outputs.Select(o =>
            o.Id == priorOutput.Id ? o with { RecycledAtUtc = h.Clock.GetUtcNow() } : o).ToArray() },
            key).Refusal.ShouldBe(ArtifactRefusal.Recycled);
        ApprovedArtifactResolver.Resolve(aggregate with { Outputs = aggregate.Outputs.Select(o =>
            o.Id == priorOutput.Id ? o with { IsValid = false } : o).ToArray() },
            key).Refusal.ShouldBe(ArtifactRefusal.Invalidated);
        ApprovedArtifactResolver.Resolve(aggregate with { Outputs = aggregate.Outputs.Select(o =>
            o.Id == priorOutput.Id ? o with { ReviewState = ReviewState.Rejected } : o).ToArray() },
            key).Refusal.ShouldBe(ArtifactRefusal.Rejected);
        var delivery = NewDelivery(h);
        var offer = await delivery.GetOfferAsync(key, CancellationToken.None);
        offer.Refusal.ShouldBe(ArtifactRefusal.None);
        offer.Offer!.Artifact.PhysicalWidthMm.ShouldNotBeNull();
        offer.Offer.Artifact.PhysicalWidthMm!.Value.ShouldBeLessThan(250);
        string folder = Path.Combine(Path.GetTempPath(), "pf-delivery-tiff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            DeliveryOutcome saved = await delivery.DeliverAsync(new DeliveryRequest(Guid.NewGuid(), key,
                offer.Offer.Artifact.ApprovedSha256, offer.Offer.Artifact.ReviewId, folder,
                "older.tiff", offer.Offer.OfferVersion), null, CancellationToken.None);
            saved.Code.ShouldBe(DeliveryCode.Delivered, saved.Detail);
            File.ReadAllBytes(saved.FinalPath!).LongLength.ShouldBe(offer.Offer.Artifact.ApprovedLength);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    private static async Task SetTiffSizeAsync(ISessionService workflow,
        SessionId id, int widthMm)
    {
        (await workflow.ExecuteAsync(id, new WorkflowCommand.SetPrintDimensions(
            PrintDimensions.FromMillimetres(widthMm, 150, SizePreset.Custom)),
            "tester", CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await workflow.ExecuteAsync(id, new WorkflowCommand.SelectWhiteUnderbaseBranch(
            WhiteUnderbaseBranch.W1_1px, "ordinary design"), "tester", CancellationToken.None))
            .IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Foreign_equal_bytes_are_a_collision_and_pre_cancelled_request_creates_no_final()
    {
        using SessionServiceHarness h = new();
        ArtifactKey artifact = await ReviewedPngAsync(h);
        var delivery = NewDelivery(h);
        DeliveryOffer offer = (await delivery.GetOfferAsync(artifact, CancellationToken.None)).Offer!;
        string folder = Path.Combine(Path.GetTempPath(), "pf-delivery-collision-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string source = h.FileWorkspace.ResolveAbsolute(offer.Artifact.File);
            byte[] bytes = File.ReadAllBytes(source);
            string final = Path.Combine(folder, "foreign.png");
            File.WriteAllBytes(final, bytes);
            var request = new DeliveryRequest(Guid.NewGuid(), artifact, offer.Artifact.ApprovedSha256,
                offer.Artifact.ReviewId, folder, "foreign.png", offer.OfferVersion);
            DeliveryOutcome collision = await delivery.DeliverAsync(request, null, CancellationToken.None);
            collision.Code.ShouldBe(DeliveryCode.Collision);
            collision.SuggestedFileName.ShouldNotBeNullOrWhiteSpace();
            File.ReadAllBytes(final).ShouldBe(bytes);
            using CancellationTokenSource cancelled = new();
            cancelled.Cancel();
            DeliveryOutcome stopped = await delivery.DeliverAsync(request with
            { RequestId = Guid.NewGuid(), FileName = "cancelled.png" }, null, cancelled.Token);
            stopped.Code.ShouldBe(DeliveryCode.Cancelled);
            File.Exists(Path.Combine(folder, "cancelled.png")).ShouldBeFalse();
            File.ReadAllBytes(source).ShouldBe(bytes);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    private static async Task<ArtifactKey> ReviewedPngAsync(SessionServiceHarness h)
    {
        var workflow = h.CreateService();
        SessionId id = await KeepOriginalExtentPersistenceTests.AtTrim(h, workflow, WorkflowType.PrepareAsset);
        var trim = await workflow.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.Trim),
            "tester", CancellationToken.None);
        trim.IsSuccess.ShouldBeTrue();
        (await workflow.ExecuteAsync(id, new WorkflowCommand.Approve(StepKind.Trim,
            trim.Value.CurrentArtefact!.Sha256), "tester", CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await workflow.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.ApprovedPngExport),
            "tester", CancellationToken.None)).IsSuccess.ShouldBeTrue();
        var aggregate = (await h.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        Revision promoted = aggregate.Revisions.Single(r => r.Operation == OperationKind.PromoteApproved);
        return new ArtifactKey(id, ArtifactKind.ApprovedAssetPng, promoted.Id.Value);
    }

    [Fact]
    public async Task Reviewed_png_delivers_exact_bytes_and_restart_rediscovers_without_copy()
    {
        using SessionServiceHarness h = new();
        var workflow = h.CreateService();
        SessionId id = await KeepOriginalExtentPersistenceTests.AtTrim(h, workflow, WorkflowType.PrepareAsset);
        var trim = await workflow.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.Trim),
            "tester", CancellationToken.None);
        trim.IsSuccess.ShouldBeTrue();
        (await workflow.ExecuteAsync(id, new WorkflowCommand.Approve(StepKind.Trim,
            trim.Value.CurrentArtefact!.Sha256), "tester", CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await workflow.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.ApprovedPngExport),
            "tester", CancellationToken.None)).IsSuccess.ShouldBeTrue();
        var aggregate = (await h.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        Revision promoted = aggregate.Revisions.Single(r => r.Operation == OperationKind.PromoteApproved);
        var artifact = new ArtifactKey(id, ArtifactKind.ApprovedAssetPng, promoted.Id.Value);
        string folder = Path.Combine(Path.GetTempPath(), "pf-delivery-service-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string evidence = Path.Combine(h.Workspace.Root, "Evidence");
            Directory.Exists(evidence).ShouldBeFalse();
            var delivery = NewDelivery(h);
            var offer = await delivery.GetOfferAsync(artifact, CancellationToken.None);
            offer.Refusal.ShouldBe(ArtifactRefusal.None);
            var request = new DeliveryRequest(Guid.NewGuid(), artifact, offer.Offer!.Artifact.ApprovedSha256,
                offer.Offer.Artifact.ReviewId, folder, "approved.png", offer.Offer.OfferVersion);
            DeliveryOutcome saved = await delivery.DeliverAsync(request, null, CancellationToken.None);
            saved.Code.ShouldBe(DeliveryCode.Delivered, saved.Detail);
            saved.FinalPath.ShouldBe(Path.Combine(folder, "approved.png"));
            Directory.Exists(evidence).ShouldBeFalse();
            File.ReadAllBytes(saved.FinalPath!).LongLength.ShouldBe(promoted.Facts.ByteLength);
            var state = await delivery.GetDeliveryStateAsync(id, artifact, CancellationToken.None);
            state.IsSuccess.ShouldBeTrue();
            state.Value.Single().Status.ShouldBe("Delivered");
            DeliveryFileObservation observed = await NewDelivery(h).CheckDeliveredFileAsync(
                saved.DeliveryId!.Value, CancellationToken.None);
            observed.Availability.ShouldBe(DeliveryAvailability.VerifiedNow);
            DeliveryOutcome duplicate = await delivery.DeliverAsync(request, null, CancellationToken.None);
            duplicate.Code.ShouldBe(DeliveryCode.AlreadyDelivered);
            var coalescedRequest = request with { RequestId = Guid.NewGuid(),
                Folder = folder.ToUpperInvariant(), FileName = "APPROVED.PNG" };
            DeliveryOutcome coalesced = await delivery.DeliverAsync(coalescedRequest, null, CancellationToken.None);
            coalesced.Code.ShouldBe(DeliveryCode.AlreadyDelivered);
            coalesced.DeliveryId.ShouldBe(saved.DeliveryId);
            (await delivery.DeliverAsync(coalescedRequest with { FileName = "other.png" },
                null, CancellationToken.None)).Code.ShouldBe(DeliveryCode.RequestConflict);
            (await delivery.DeliverAsync(coalescedRequest with { ExpectedSha256 =
                PrintFlow.Domain.Files.Sha256.Parse(new string('F', 64)) },
                null, CancellationToken.None)).Code.ShouldBe(DeliveryCode.RequestConflict);
            var selected = await NewDelivery(h).AcquireDeliveredSelectionAsync(
                saved.DeliveryId.Value, CancellationToken.None);
            selected.Availability.ShouldBe(DeliveryAvailability.VerifiedNow);
            var shellConsumer = new RecordingShellConsumer();
            shellConsumer.DispatchAndDispose(selected.Lease!);
            shellConsumer.SeenPath.ShouldBe(saved.FinalPath);
            selected.Lease!.IsDisposed.ShouldBeTrue();
            using (var exclusive = new FileStream(saved.FinalPath!, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                (await delivery.CheckDeliveredFileAsync(saved.DeliveryId.Value, CancellationToken.None))
                    .Availability.ShouldBe(DeliveryAvailability.Unavailable);
            File.WriteAllBytes(saved.FinalPath!, [1, 2, 3]);
            (await delivery.CheckDeliveredFileAsync(saved.DeliveryId.Value, CancellationToken.None))
                .Availability.ShouldBe(DeliveryAvailability.Changed);
            (await delivery.DeliverAsync(request, null, CancellationToken.None))
                .Code.ShouldBe(DeliveryCode.DeliveredFileChanged);
            File.Delete(saved.FinalPath!);
            DeliveryFileObservation missing = await delivery.CheckDeliveredFileAsync(
                saved.DeliveryId.Value, CancellationToken.None);
            missing.Availability.ShouldBe(DeliveryAvailability.Missing);
            var replacementRequest = new MissingDeliveryReplacementRequest(Guid.NewGuid(),
                saved.DeliveryId.Value, missing, offer.Offer.OfferVersion);
            var competingRequest = replacementRequest with { RequestId = Guid.NewGuid() };
            DeliveryOutcome[] replacements = await Task.WhenAll(
                delivery.ReplaceMissingAsync(replacementRequest, null, CancellationToken.None),
                delivery.ReplaceMissingAsync(competingRequest, null, CancellationToken.None));
            replacements.Count(x => x.Code == DeliveryCode.Delivered).ShouldBe(1);
            replacements.Count(x => x.Code == DeliveryCode.AlreadyDelivered).ShouldBe(1);
            replacements[0].DeliveryId.ShouldBe(replacements[1].DeliveryId);
            DeliveryOutcome replaced = replacements.Single(x => x.Code == DeliveryCode.Delivered);
            replaced.DeliveryId.ShouldNotBe(saved.DeliveryId);
            DeliveryOutcome repeated = await delivery.ReplaceMissingAsync(replacementRequest,
                null, CancellationToken.None);
            repeated.Code.ShouldBe(DeliveryCode.AlreadyDelivered);
            repeated.DeliveryId.ShouldBe(replaced.DeliveryId);
            var ordinaryAfterReplacement = request with { RequestId = Guid.NewGuid() };
            DeliveryOutcome ordinaryExisting = await delivery.DeliverAsync(ordinaryAfterReplacement,
                null, CancellationToken.None);
            ordinaryExisting.Code.ShouldBe(DeliveryCode.AlreadyDelivered);
            ordinaryExisting.DeliveryId.ShouldBe(replaced.DeliveryId);
            state = await delivery.GetDeliveryStateAsync(id, artifact, CancellationToken.None);
            state.Value.Count.ShouldBe(2);
            File.Delete(saved.FinalPath!);
            var ordinaryMissing = request with { RequestId = Guid.NewGuid() };
            DeliveryOutcome missingSuccessor = await delivery.DeliverAsync(ordinaryMissing,
                null, CancellationToken.None);
            missingSuccessor.Code.ShouldBe(DeliveryCode.DeliveredFileMissing);
            missingSuccessor.DeliveryId.ShouldBe(replaced.DeliveryId);
            File.Exists(saved.FinalPath!).ShouldBeFalse();
            (await delivery.GetDeliveryStateAsync(id, artifact, CancellationToken.None)).Value.Count.ShouldBe(2);
            (await delivery.DeliverAsync(ordinaryMissing with { FileName = "different.png" },
                null, CancellationToken.None)).Code.ShouldBe(DeliveryCode.RequestConflict);
            DeliveryFileObservation missingReplacement = await delivery.CheckDeliveredFileAsync(
                replaced.DeliveryId!.Value, CancellationToken.None);
            DeliveryOutcome third = await delivery.ReplaceMissingAsync(new MissingDeliveryReplacementRequest(
                Guid.NewGuid(), replaced.DeliveryId.Value, missingReplacement, offer.Offer.OfferVersion),
                null, CancellationToken.None);
            third.Code.ShouldBe(DeliveryCode.Delivered, third.Detail);
            third.DeliveryId.ShouldNotBe(replaced.DeliveryId);
            (await delivery.GetDeliveryStateAsync(id, artifact, CancellationToken.None)).Value.Count.ShouldBe(3);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    private static IReadOnlyList<string> ProductionLikeProtectedRoots(SessionServiceHarness h) =>
        [h.Workspace.Root, Path.GetDirectoryName(h.Database.Path)!,
            Path.Combine(h.Workspace.Root, "Evidence")];

    private static ApprovedArtifactDeliveryService NewDelivery(SessionServiceHarness h) => new(h.Repository,
        new SqliteDeliveryRepository(h.Database.Factory), h.FileWorkspace,
        new WindowsDeliveryFileSystem(), ProductionLikeProtectedRoots(h), h.Clock, h.TiffReviewDecoder);

    private static string? ReadPreference(SessionServiceHarness h)
    {
        using var connection = h.Database.Factory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM Setting WHERE Key='LAST_SUCCESSFUL_DELIVERY_DESTINATION';";
        return command.ExecuteScalar() as string;
    }

    [Fact]
    public async Task Unreviewed_keep_original_png_offer_refuses_without_adding_review()
    {
        using SessionServiceHarness h = new();
        var workflow = h.CreateService();
        var id = await KeepOriginalExtentPersistenceTests.AtTrim(h, workflow, WorkflowType.PrepareAsset);
        (await workflow.ExecuteAsync(id, new WorkflowCommand.KeepOriginalExtent(), "tester", CancellationToken.None))
            .IsSuccess.ShouldBeTrue();
        (await workflow.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.ApprovedPngExport),
            "tester", CancellationToken.None)).IsSuccess.ShouldBeTrue();
        var aggregate = (await h.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        Revision promoted = aggregate.Revisions.Single(r => r.Operation == OperationKind.PromoteApproved);
        var delivery = new ApprovedArtifactDeliveryService(h.Repository,
            new SqliteDeliveryRepository(h.Database.Factory), h.FileWorkspace,
            new WindowsDeliveryFileSystem(), [h.Workspace.Root], h.Clock, h.TiffReviewDecoder);

        var offer = await delivery.GetOfferAsync(
            new ArtifactKey(id, ArtifactKind.ApprovedAssetPng, promoted.Id.Value), CancellationToken.None);

        offer.Offer.ShouldBeNull();
        offer.Refusal.ShouldBe(ArtifactRefusal.ApprovalEvidenceMissing);
        (await h.Repository.LoadAsync(id, CancellationToken.None)).Value!.Reviews.ShouldBeEmpty();
    }

    private enum StagingFailureMode { None, BeforeCommit, AfterCommit }

    private sealed class LostAcknowledgementRepository(IDeliveryRepository inner) : IDeliveryRepository
    {
        public bool ReadyAcknowledgementLost { get; private set; }
        public bool DeliveredAcknowledgementLost { get; private set; }
        public StagingFailureMode StagingFailure { get; init; }

        public Task<OperationResult<IReadOnlyList<DeliveryJournalEntry>>> ListAsync(SessionId id,
            ArtifactKey? artifact, CancellationToken ct) => inner.ListAsync(id, artifact, ct);
        public Task<OperationResult<DeliveryJournalEntry?>> FindByIdAsync(Guid id, CancellationToken ct) =>
            inner.FindByIdAsync(id, ct);
        public Task<OperationResult<DeliveryJournalEntry?>> FindByRequestAsync(Guid id, CancellationToken ct) =>
            inner.FindByRequestAsync(id, ct);
        public Task<OperationResult<DeliveryJournalEntry?>> FindSuccessorAsync(Guid id, CancellationToken ct) =>
            inner.FindSuccessorAsync(id, ct);
        public Task<OperationResult<DeliveryJournalEntry?>> FindByDestinationAsync(string key, CancellationToken ct) =>
            inner.FindByDestinationAsync(key, ct);
        public Task<OperationResult<DeliveryJournalEntry>> CreateOrCoalesceAsync(DeliveryRecord d,
            DeliveryAttemptRecord a, CancellationToken ct) => inner.CreateOrCoalesceAsync(d, a, ct);
        public Task<OperationResult<DeliveryAttemptRecord>> BeginNextAttemptAsync(Guid id,
            string directoryId, string key, CancellationToken ct) =>
            inner.BeginNextAttemptAsync(id, directoryId, key, ct);
        public async Task<OperationResult<DeliveryAttemptRecord>> MarkStagingAsync(Guid id, string fileId,
            DateTimeOffset created, CancellationToken ct)
        {
            if (StagingFailure == StagingFailureMode.BeforeCommit)
                throw new IOException("Synthetic process interruption after native stage creation.");
            OperationResult<DeliveryAttemptRecord> result = await inner.MarkStagingAsync(id, fileId, created, ct);
            return StagingFailure == StagingFailureMode.AfterCommit && result.IsSuccess
                ? OperationResult.Fail<DeliveryAttemptRecord>(FailureCode.PersistenceError,
                    "Synthetic acknowledgement lost after durable Staging checkpoint.") : result;
        }
        public async Task<OperationResult<DeliveryAttemptRecord>> MarkReadyAsync(Guid id,
            DateTimeOffset verified, CancellationToken ct)
        {
            OperationResult<DeliveryAttemptRecord> result = await inner.MarkReadyAsync(id, verified, ct);
            if (result.IsSuccess && !ReadyAcknowledgementLost)
            {
                ReadyAcknowledgementLost = true;
                return OperationResult.Fail<DeliveryAttemptRecord>(FailureCode.PersistenceError,
                    "Synthetic acknowledgement lost after SQLite commit.");
            }
            return result;
        }
        public Task<OperationResult<DeliveryAttemptRecord>> EndAttemptAsync(Guid id,
            DeliveryAttemptState state, string? failure, CancellationToken ct) =>
            inner.EndAttemptAsync(id, state, failure, ct);
        public async Task<OperationResult<DeliveryJournalEntry>> MarkDeliveredAsync(Guid id, Guid attemptId,
            string finalFileId, DateTimeOffset finalCreation, DateTimeOffset verified, CancellationToken ct)
        {
            OperationResult<DeliveryJournalEntry> result = await inner.MarkDeliveredAsync(id, attemptId,
                finalFileId, finalCreation, verified, ct);
            if (result.IsSuccess && !DeliveredAcknowledgementLost)
            {
                DeliveredAcknowledgementLost = true;
                return OperationResult.Fail<DeliveryJournalEntry>(FailureCode.PersistenceError,
                    "Synthetic acknowledgement lost after SQLite commit.");
            }
            return result;
        }
    }

    private sealed class RecordingShellConsumer
    {
        public string? SeenPath { get; private set; }
        public void DispatchAndDispose(IDeliveredSelectionLease lease)
        {
            using (lease) SeenPath = lease.FinalPath;
        }
    }

    public enum SyntheticSaveFault { UnwritableStage, DisconnectedStage, CopyIo }

    private sealed class OneShotFaultFileSystem(SyntheticSaveFault fault) : IDeliveryFileSystem
    {
        private readonly WindowsDeliveryFileSystem _inner = new();
        private readonly SyntheticSaveFault _fault = fault;
        private bool _fired;

        public DeliveryFileResult<string> ValidateLeaf(string name, ArtifactKind kind) =>
            _inner.ValidateLeaf(name, kind);
        public string? SuggestAlternative(string leaf, Func<string, bool> exists) =>
            _inner.SuggestAlternative(leaf, exists);
        public DeliveryFileResult<IDeliveryFileGuard> Open(string sourcePath, Sha256 hash, long length,
            string folder, string leaf, ArtifactKind kind, IReadOnlyList<string> roots, string? original)
        {
            var opened = _inner.Open(sourcePath, hash, length, folder, leaf, kind, roots, original);
            return opened.IsSuccess
                ? DeliveryFileResult<IDeliveryFileGuard>.Ok(new OneShotFaultGuard(opened.Value!, this))
                : DeliveryFileResult<IDeliveryFileGuard>.Fail(opened.Code, opened.Detail!);
        }

        private sealed class OneShotFaultGuard(IDeliveryFileGuard inner, OneShotFaultFileSystem owner)
            : IDeliveryFileGuard
        {
            public string ResolvedFolder => inner.ResolvedFolder;
            public string FinalPath => inner.FinalPath;
            public string VolumeId => inner.VolumeId;
            public string DirectoryId => inner.DirectoryId;
            public string DestinationKey => inner.DestinationKey;
            public DeliveryFileIdentity SourceIdentity => inner.SourceIdentity;
            public DeliveryFileResult<bool> VerifySource() => inner.VerifySource();
            public DeliveryFinalCheck CheckFinal(DeliveryFileIdentity? expected = null) => inner.CheckFinal(expected);
            public DeliveryFileResult<DeliveryFileIdentity> CreateStaging(string leaf)
            {
                if (!owner._fired && owner._fault is SyntheticSaveFault.UnwritableStage or
                    SyntheticSaveFault.DisconnectedStage)
                {
                    owner._fired = true;
                    return DeliveryFileResult<DeliveryFileIdentity>.Fail(
                        owner._fault == SyntheticSaveFault.UnwritableStage
                            ? DeliveryCode.DestinationNotWritable : DeliveryCode.DestinationUnavailable,
                        "Synthetic prepublication stage creation fault.");
                }
                return inner.CreateStaging(leaf);
            }
            public DeliveryFileResult<DeliveryFileIdentity> OpenOwnedStaging(string leaf, DeliveryFileIdentity id) =>
                inner.OpenOwnedStaging(leaf, id);
            public Task<DeliveryFileResult<bool>> CopyAndVerifyStageAsync(Sha256 hash, long length,
                IProgress<long>? copied, CancellationToken ct)
            {
                if (!owner._fired && owner._fault == SyntheticSaveFault.CopyIo)
                {
                    owner._fired = true;
                    return inner.CopyAndVerifyStageAsync(hash, length, new ThrowingCopyProgress(), ct);
                }
                return inner.CopyAndVerifyStageAsync(hash, length, copied, ct);
            }
            public DeliveryFileResult<bool> VerifyStage(Sha256 hash, long length) => inner.VerifyStage(hash, length);
            public DeliveryFileResult<bool> PublishNoReplace(string leaf, DeliveryFileIdentity id) =>
                inner.PublishNoReplace(leaf, id);
            public DeliveryFileResult<bool> DeleteHeldStaging(DeliveryFileIdentity id) => inner.DeleteHeldStaging(id);
            public SelectionLeaseResult AcquireSelection(Guid id, DeliveryFileIdentity expected,
                Sha256 hash, long length) => inner.AcquireSelection(id, expected, hash, length);
            public void Dispose() => inner.Dispose();
        }

        private sealed class ThrowingCopyProgress : IProgress<long>
        {
            public void Report(long value) => throw new IOException(
                "Synthetic copy I/O interruption after staged bytes were written.");
        }
    }

    private sealed class UnknownPublicationAckFileSystem : IDeliveryFileSystem
    {
        private readonly WindowsDeliveryFileSystem _inner = new();
        public DeliveryFileResult<string> ValidateLeaf(string name, ArtifactKind kind) =>
            _inner.ValidateLeaf(name, kind);
        public string? SuggestAlternative(string leaf, Func<string, bool> exists) =>
            _inner.SuggestAlternative(leaf, exists);
        public DeliveryFileResult<IDeliveryFileGuard> Open(string sourcePath, Sha256 hash, long length,
            string folder, string leaf, ArtifactKind kind, IReadOnlyList<string> protectedRoots,
            string? original)
        {
            var opened = _inner.Open(sourcePath, hash, length, folder, leaf, kind, protectedRoots, original);
            return opened.IsSuccess
                ? DeliveryFileResult<IDeliveryFileGuard>.Ok(new UnknownPublicationAckGuard(opened.Value!))
                : DeliveryFileResult<IDeliveryFileGuard>.Fail(opened.Code, opened.Detail!);
        }
    }

    private sealed class UnknownPublicationAckGuard(IDeliveryFileGuard inner) : IDeliveryFileGuard
    {
        public string ResolvedFolder => inner.ResolvedFolder;
        public string FinalPath => inner.FinalPath;
        public string VolumeId => inner.VolumeId;
        public string DirectoryId => inner.DirectoryId;
        public string DestinationKey => inner.DestinationKey;
        public DeliveryFileIdentity SourceIdentity => inner.SourceIdentity;
        public DeliveryFileResult<bool> VerifySource() => inner.VerifySource();
        public DeliveryFinalCheck CheckFinal(DeliveryFileIdentity? expected = null) => inner.CheckFinal(expected);
        public DeliveryFileResult<DeliveryFileIdentity> CreateStaging(string leaf) => inner.CreateStaging(leaf);
        public DeliveryFileResult<DeliveryFileIdentity> OpenOwnedStaging(string leaf, DeliveryFileIdentity id) =>
            inner.OpenOwnedStaging(leaf, id);
        public Task<DeliveryFileResult<bool>> CopyAndVerifyStageAsync(Sha256 hash, long length,
            IProgress<long>? copied, CancellationToken ct) => inner.CopyAndVerifyStageAsync(hash, length, copied, ct);
        public DeliveryFileResult<bool> VerifyStage(Sha256 hash, long length) => inner.VerifyStage(hash, length);
        public DeliveryFileResult<bool> PublishNoReplace(string leaf, DeliveryFileIdentity id)
        {
            DeliveryFileResult<bool> actual = inner.PublishNoReplace(leaf, id);
            return actual.IsSuccess
                ? DeliveryFileResult<bool>.Fail(DeliveryCode.NeedsReconciliation,
                    "Synthetic native acknowledgement lost after real no-replace rename.") : actual;
        }
        public DeliveryFileResult<bool> DeleteHeldStaging(DeliveryFileIdentity id) => inner.DeleteHeldStaging(id);
        public SelectionLeaseResult AcquireSelection(Guid id, DeliveryFileIdentity expected,
            Sha256 hash, long length) => inner.AcquireSelection(id, expected, hash, length);
        public void Dispose() => inner.Dispose();
    }
}
