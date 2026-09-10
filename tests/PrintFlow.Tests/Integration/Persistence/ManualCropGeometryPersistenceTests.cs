using Microsoft.Data.Sqlite;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Reviews;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Trimming;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Services;
using static PrintFlow.Tests.Integration.Persistence.KeepOriginalExtentPersistenceTests;

namespace PrintFlow.Tests.Integration.Persistence;

[Collection(SqliteCollection.Name)]
public sealed class ManualCropGeometryPersistenceTests
{
    [Fact]
    public async Task Legacy_manual_attempt_without_recorded_geometry_loads_as_null()
    {
        using SessionServiceHarness h = new();
        var service = h.CreateService();
        var id = await AtTrim(h, service, WorkflowType.PrepareAsset, opaque: true);
        var aggregate = await Load(h, id);
        var original = aggregate.Revisions.Single(r => r.IsRoot);
        var attemptId = AttemptId.From(Guid.CreateVersion7());
        // Insert the shape written by the historical schema: no manual geometry columns.
        using (var connection = h.Database.OpenRaw())
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO ProcessingAttempt (Id, SessionId, StepKind, Operation, AdapterId, StartedAtUtc,
                    EndedAtUtc, ResultStatus, OutputRevisionId, RetrySequence)
                VALUES ($id, $session, 'Trim', 'MANUAL_IMPORT', 'historical-manual-crop',
                    '2026-01-01T00:00:00.000Z', '2026-01-01T00:00:01.000Z', 'SUCCEEDED', $revision, 0)
                """;
            command.Parameters.AddWithValue("$id", attemptId.ToString());
            command.Parameters.AddWithValue("$session", id.ToString());
            command.Parameters.AddWithValue("$revision", original.Id.ToString());
            command.ExecuteNonQuery().ShouldBe(1);
        }
        (await Load(h, id)).Attempts.Single(a => a.Id == attemptId).ManualCropGeometry.ShouldBeNull();
        (await h.CreateService().LoadAsync(id, CancellationToken.None)).Value.ArtefactManualCropGeometry.ShouldBeNull();
    }

    [Theory]
    [InlineData(WorkflowType.PrepareAsset, 0)]
    [InlineData(WorkflowType.PrepareAsset, 1)]
    [InlineData(WorkflowType.PrepareAsset, 2)]
    [InlineData(WorkflowType.PrepareCustomerDesign, 0)]
    [InlineData(WorkflowType.PrepareCustomerDesign, 1)]
    [InlineData(WorkflowType.PrepareCustomerDesign, 2)]
    public async Task Real_crop_restart_review_and_downstream_keep_exact_geometry(WorkflowType workflow, int mode)
    {
        using SessionServiceHarness h = new();
        ISessionService service = h.CreateService();
        SessionId id = await AtTrim(h, service, workflow, opaque: true);
        await Execute(service, id, new WorkflowCommand.SetTrimParameters(TrimMargin.Uniform(5)));
        await RunTrim(service, id, manual: true);
        SessionAggregate before = await Load(h, id);
        var selection = TrimBounds.FromEdges(3, 2, 9, 7);
        var margin = mode switch { 0 => ManualCropMargin.Tight, 1 => ManualCropMargin.Uniform(5), _ => ManualCropMargin.PerEdge(0, 4, 1, 2) };
        var expected = ManualCropGeometry.Create(selection, margin, 12, 10);
        SessionView result = await Execute(service, id, new WorkflowCommand.SubmitManualCrop(StepKind.Trim, selection, margin));
        result.CurrentManualCropGeometry.ShouldBe(expected);
        result.CurrentTrimGeometry.ShouldBeNull();
        var restart = await h.CreateService().LoadAsync(id, CancellationToken.None);
        restart.Value.CurrentStep!.State.ShouldBe(StepState.ReviewRequired);
        restart.Value.CurrentArtefact!.ShouldBe(result.CurrentArtefact);
        restart.Value.CurrentManualCropGeometry.ShouldBe(expected);
        SessionAggregate saved = await Load(h, id);
        saved.Session.TrimMargin.ShouldBe(TrimMargin.Uniform(5));
        ProcessingAttempt attempt = saved.Attempts.Single(a => a.Operation == OperationKind.ManualImport);
        attempt.ManualCropGeometry.ShouldBe(expected);
        attempt.TrimGeometry.ShouldBeNull();
        attempt.TrimParameters.ShouldBeNull();
        Revision revision = saved.Revisions.Single(r => r.Id == attempt.OutputRevisionId);
        await AssertRaster(h, revision, expected);
        saved.Attempts.Where(a => a.Operation != OperationKind.ManualImport).ShouldAllBe(a => a.ManualCropGeometry == null);
        await Execute(service, id, new WorkflowCommand.Approve(StepKind.Trim, revision.Facts.Sha256));
        if (workflow == WorkflowType.PrepareAsset)
        {
            await Execute(service, id, new WorkflowCommand.StartStep(StepKind.ApprovedPngExport));
            var downstream = await Load(h, id);
            downstream.Attempts.Single(a => a.Operation == OperationKind.PromoteApproved).InputRevisionId.ShouldBe(revision.Id);
            downstream.Revisions.Single(r => r.Operation == OperationKind.PromoteApproved).Facts.ShouldBe(revision.Facts);
        }
        else
        {
            var dimensions = (await service.LoadAsync(id, CancellationToken.None)).Value;
            dimensions.CurrentStep!.Step.ShouldBe(StepKind.PrintDimensions);
            dimensions.CurrentArtefact!.RevisionId.ShouldBe(revision.Id);
            SessionView sized = await Execute(
                service, id, new WorkflowCommand.SetPrintDimensions(WorkflowScenario.CustomBox));
            PrintDimensionsPreflight preflight = sized.Preflight.ShouldNotBeNull();
            preflight.SourceRevisionId.ShouldBe(revision.Id);
            preflight.GraphicBoundsKind.ShouldBe(GraphicBoundsKind.ManualCrop);
            preflight.ArtworkBounds.ShouldBe(expected.SelectedBounds);
            preflight.FinalCanvasBounds.ShouldBe(expected.AppliedBounds);
            (await h.CreateService().LoadAsync(id, CancellationToken.None)).Value.Preflight.ShouldBe(preflight);
            (await Load(h, id)).Session.PrintPreparationPlan!.SourceRevisionId.ShouldBe(revision.Id);
            await Execute(service, id, new WorkflowCommand.SelectWhiteUnderbaseBranch(
                WhiteUnderbaseBranch.W1_1px, "synthetic retention test"));
            var tiff = await Execute(service, id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput));
            await Execute(service, id, new WorkflowCommand.Approve(StepKind.PhotoshopOutput,
                tiff.CurrentStep!.CurrentRevisionSha256!.Value));
        }
        var completed = await Execute(service, id, new WorkflowCommand.Complete());
        completed.CompletionCleanup!.IsComplete.ShouldBeTrue(completed.CompletionCleanup.Failure?.ToString());
        var retained = await RetentionCleanupTests.AssertAuthority(h, id);
        retained.Attempts.Single(a => a.Id == attempt.Id).ManualCropGeometry.ShouldBe(expected);
        var durableCrop = retained.Revisions.Single(r => r.Id == revision.Id);
        durableCrop.File.Area.ShouldBe(WorkspaceArea.Revisions);
        await AssertRaster(h, durableCrop, expected);
        await RetentionCleanupTests.Restart(h);
        await RetentionCleanupTests.AssertAuthority(h, id);
        var original = before.Revisions.Single(r => r.IsRoot);
        (await h.FileInspector.InspectAsync(h.FileWorkspace.ResolveAbsolute(original.File), CancellationToken.None)).Value.ShouldBe(original.Facts);
    }

    [Fact]
    public async Task Reject_retry_and_return_upstream_preserve_independent_immutable_history()
    {
        using SessionServiceHarness h = new();
        var service = h.CreateService();
        var id = await AtTrim(h, service, WorkflowType.PrepareAsset, opaque: true);
        await RunTrim(service, id, manual: true);
        var a = await Execute(service, id, new WorkflowCommand.SubmitManualCrop(StepKind.Trim,
            TrimBounds.FromEdges(3, 2, 9, 7), ManualCropMargin.Uniform(1)));
        var first = (await Load(h, id)).Attempts.Single(t => t.Operation == OperationKind.ManualImport);
        await Execute(service, id, new WorkflowCommand.Reject(StepKind.Trim, a.CurrentArtefact!.Sha256, RejectionReason.EdgeError, "adjust"));
        var b = await Execute(service, id, new WorkflowCommand.SubmitManualCrop(StepKind.Trim,
            TrimBounds.FromEdges(2, 1, 8, 6), ManualCropMargin.PerEdge(1, 3, 2, 0)));
        b.CurrentStep!.State.ShouldBe(StepState.ReviewRequired);
        var saved = await Load(h, id);
        saved.Attempts.Single(t => t.Id == first.Id).ShouldBe(first);
        var second = saved.Attempts.Single(t => t.Operation == OperationKind.ManualImport && t.Id != first.Id);
        second.ManualCropGeometry.ShouldBe(b.CurrentManualCropGeometry);
        second.ManualCropGeometry.ShouldNotBe(first.ManualCropGeometry);
        foreach (var attempt in new[] { first, second })
            await AssertRaster(h, saved.Revisions.Single(r => r.Id == attempt.OutputRevisionId), attempt.ManualCropGeometry!);
        using (var connection = h.Database.OpenRaw())
        {
            using var command = connection.CreateCommand();
            // Each replacement is coherent on its own; refusal must preserve recorded history.
            command.CommandText = "UPDATE ProcessingAttempt SET ManualSelectedRight = 8, ManualAppliedRight = 9 WHERE Id = $id";
            command.Parameters.AddWithValue("$id", first.Id.ToString());
            Should.Throw<SqliteException>(() => command.ExecuteNonQuery());
            command.CommandText = "UPDATE ProcessingAttempt SET ManualMarginMode = 'EDGE_SPECIFIC_MARGIN' WHERE Id = $id";
            Should.Throw<SqliteException>(() => command.ExecuteNonQuery());
            command.CommandText = """
                UPDATE ProcessingAttempt SET TrimMode = 'TIGHT_CROP', TrimMarginTop = 0,
                    TrimMarginRight = 0, TrimMarginBottom = 0, TrimMarginLeft = 0 WHERE Id = $id
                """;
            Should.Throw<SqliteException>(() => command.ExecuteNonQuery());
        }
        await Execute(service, id, new WorkflowCommand.Approve(StepKind.Trim, b.CurrentArtefact!.Sha256));
        await Execute(service, id, new WorkflowCommand.ReturnToStep(StepKind.Enhancement));
        var reset = await Load(h, id);
        reset.Revisions.Single(r => r.Id == second.OutputRevisionId).IsValid.ShouldBeFalse();
        reset.Attempts.Single(t => t.Id == first.Id).ShouldBe(first);
        reset.Attempts.Single(t => t.Id == second.Id).ShouldBe(second);
    }

    private static async Task AssertRaster(SessionServiceHarness h, Revision revision, ManualCropGeometry geometry)
    {
        revision.Facts.PixelWidth.ShouldBe(geometry.AppliedBounds.Width);
        revision.Facts.PixelHeight.ShouldBe(geometry.AppliedBounds.Height);
        var actual = (await h.FileInspector.InspectAsync(h.FileWorkspace.ResolveAbsolute(revision.File), CancellationToken.None)).Value;
        actual.PixelWidth.ShouldBe(geometry.AppliedBounds.Width);
        actual.PixelHeight.ShouldBe(geometry.AppliedBounds.Height);
        actual.Sha256.ShouldBe(revision.Facts.Sha256);
    }
}
