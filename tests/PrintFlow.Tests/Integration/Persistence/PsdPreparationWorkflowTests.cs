using System.IO;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Tests.Integration.Ui;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Persistence;

[Collection(SqliteCollection.Name)]
public sealed class PsdPreparationWorkflowTests
{
    [Fact]
    public async Task Prepared_PSD_raster_and_inspection_survive_completion_retention()
    {
        using SessionServiceHarness h = new();
        var service = h.CreateServiceWithPhotoshop(new PsdProcessor(h.FileWorkspace));
        byte[] bytes = PsdInputPreparationTests.RgbCompositePsd();
        string source = h.Workspace.CreateSourceFile("retention.psd", bytes);
        var imported = await service.ImportAsync(WorkflowType.GeneratePrintTiff, source, "retention", "qa", default);
        var id = imported.Value.Id;
        var prepared = await RetentionCleanupTests.Command(service, id, new WorkflowCommand.StartStep(StepKind.OriginalConfirmation));
        await RetentionCleanupTests.Command(service, id, new WorkflowCommand.Approve(StepKind.OriginalConfirmation,
            prepared.CurrentStep!.CurrentRevisionSha256!.Value));
        SessionAggregate before = await RetentionCleanupTests.Load(h, id);
        service = h.CreateService();
        await RetentionCleanupTests.ProduceTiff(service, id, 120);
        var completed = await RetentionCleanupTests.Command(service, id, new WorkflowCommand.Complete());
        completed.CompletionCleanup!.IsComplete.ShouldBeTrue(completed.CompletionCleanup.Failure?.ToString());
        var after = await RetentionCleanupTests.AssertAuthority(h, id);
        after.Revisions.Single(r => r.Operation == OperationKind.PreparePsd).File.Area.ShouldBe(WorkspaceArea.Revisions);
        System.Text.Json.JsonSerializer.Serialize(after.Attempts.Single(a => a.Operation == OperationKind.PreparePsd))
            .ShouldBe(System.Text.Json.JsonSerializer.Serialize(before.Attempts.Single(a => a.Operation == OperationKind.PreparePsd)));
        File.ReadAllBytes(source).ShouldBe(bytes);
        await RetentionCleanupTests.Restart(h);
        await RetentionCleanupTests.AssertAuthority(h, id);
    }

    [Theory]
    [InlineData(AutomationStopMode.StopOperation)]
    [InlineData(AutomationStopMode.TakeOver)]
    public async Task Operator_stop_closes_psd_attempt_as_cancelled_and_releases_lock(AutomationStopMode mode)
    {
        using SessionServiceHarness h = new();
        PsdProcessor adapter = new(h.FileWorkspace);
        var service = h.CreateServiceWithPhotoshop(adapter);
        string source = h.Workspace.CreateSourceFile("synthetic.psd", PsdInputPreparationTests.RgbCompositePsd());
        var imported = await service.ImportAsync(WorkflowType.GeneratePrintTiff, source, null, "qa", CancellationToken.None);
        var id = imported.Value.Id;
        adapter.BeforeReturn = request =>
        {
            request.Stop.ReportPhase(ExternalOperationPhase.OperationRequested);
            service.RequestStop(id, mode).IsSuccess.ShouldBeTrue();
        };
        await service.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.OriginalConfirmation), "qa", CancellationToken.None);
        var state = (await h.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        state.Revisions.Count.ShouldBe(1);
        var attempt = state.Attempts.Single(a => a.Operation == OperationKind.PreparePsd);
        attempt.Status.ShouldBe(AttemptStatus.Cancelled);
        attempt.OutputRevisionId.ShouldBeNull();
        state.Session.State.ShouldBe(mode == AutomationStopMode.TakeOver ? SessionState.HandedOff : SessionState.Active);
        (await h.Repository.GetAutomationLockAsync(CancellationToken.None)).Value.IsHeld.ShouldBeFalse();
    }

    [Fact]
    public async Task Production_gate_refuses_before_psd_attempt_or_adapter_call()
    {
        using SessionServiceHarness h = new();
        PsdProcessor adapter = new(h.FileWorkspace) { Mode = AdapterExecutionMode.Production };
        var service = h.CreateServiceWithPhotoshop(adapter);
        string source = h.Workspace.CreateSourceFile("synthetic.psd", PsdInputPreparationTests.RgbCompositePsd());
        var imported = await service.ImportAsync(WorkflowType.GeneratePrintTiff, source, null, "qa", CancellationToken.None);
        var refused = await service.ExecuteAsync(imported.Value.Id, new WorkflowCommand.StartStep(StepKind.OriginalConfirmation), "qa", CancellationToken.None);
        refused.Failure.Code.ShouldBe(FailureCode.EnvironmentNotVerified);
        adapter.Calls.ShouldBe(0);
        (await h.Repository.LoadAsync(imported.Value.Id, CancellationToken.None)).Value!.Attempts.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(WorkflowType.GeneratePrintTiff)]
    [InlineData(WorkflowType.PrepareCustomerDesign)]
    [InlineData(WorkflowType.PrepareAsset)]
    public async Task Prepared_revision_is_reviewable_persisted_and_consumed_without_repreparing(WorkflowType workflow)
    {
        using SessionServiceHarness h = new();
        PsdProcessor adapter = new(h.FileWorkspace);
        var service = h.CreateServiceWithPhotoshop(adapter);
        byte[] bytes = PsdInputPreparationTests.RgbCompositePsd();
        string source = h.Workspace.CreateSourceFile("synthetic.psd", bytes);
        var imported = await service.ImportAsync(workflow, source, null, "qa", CancellationToken.None);
        imported.IsSuccess.ShouldBeTrue();
        var id = imported.Value.Id;
        var prepared = await service.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.OriginalConfirmation), "qa", CancellationToken.None);
        prepared.IsSuccess.ShouldBeTrue(prepared.IsFailure ? prepared.Failure.ToString() : "");
        prepared.Value.OriginalSourceFormat.ShouldBe(ImageFormat.Psd);
        prepared.Value.HasBeforeAfterComparison.ShouldBeFalse();
        var stored = (await h.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        var root = stored.Revisions.Single(r => r.IsRoot);
        var raster = stored.Revisions.Single(r => r.Operation == OperationKind.PreparePsd);
        root.Facts.Format.ShouldBe(ImageFormat.Psd);
        stored.Snapshot!.RootRevisionId.ShouldBe(root.Id);
        File.ReadAllBytes(h.FileWorkspace.ResolveAbsolute(root.File)).ShouldBe(bytes);
        File.ReadAllBytes(source).ShouldBe(bytes);
        raster.SourceRevisionId.ShouldBe(root.Id);
        raster.Id.ShouldNotBe(root.Id);
        raster.Facts.Format.ShouldBe(ImageFormat.Png);
        raster.Facts.PixelWidth.ShouldBe(4);
        raster.Facts.PixelHeight.ShouldBe(3);
        stored.ToSnapshot().CurrentStep!.State.ShouldBe(StepState.ReviewRequired);
        var attempt = stored.Attempts.Single(a => a.Operation == OperationKind.PreparePsd);
        attempt.OutputRevisionId.ShouldBe(raster.Id);
        attempt.PsdInspection.ShouldNotBeNull().HasRealMergedData.ShouldBeTrue();
        attempt.PsdInspection!.Channels.Length.ShouldBe(3);
        (await h.Previews.GetPreviewAsync(id, raster.Id, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await h.Previews.GetPreviewAsync(id, root.Id, CancellationToken.None)).IsFailure.ShouldBeTrue();

        // A fresh service with the same persisted database/workspace has no in-memory result.
        var restarted = h.CreateServiceWithPhotoshop(adapter);
        var resumed = await restarted.LoadAsync(id, CancellationToken.None);
        resumed.Value.CurrentArtefact!.RevisionId.ShouldBe(raster.Id);
        resumed.Value.CurrentArtefact.Sha256.ShouldBe(raster.Sha256);
        adapter.Calls.ShouldBe(1);
        (await restarted.ExecuteAsync(id, new WorkflowCommand.Approve(StepKind.OriginalConfirmation, raster.Sha256),
            "qa", CancellationToken.None)).IsSuccess.ShouldBeTrue();
        if (workflow != WorkflowType.GeneratePrintTiff)
        {
            (await restarted.ExecuteAsync(id, new WorkflowCommand.Skip(StepKind.Enhancement, "already finished"), "qa", CancellationToken.None)).IsSuccess.ShouldBeTrue();
            (await restarted.ExecuteAsync(id, new WorkflowCommand.Skip(StepKind.BackgroundRemoval, "keep background"), "qa", CancellationToken.None)).IsSuccess.ShouldBeTrue();
            var trimmed = await restarted.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.Trim), "qa", CancellationToken.None);
            trimmed.IsSuccess.ShouldBeTrue(trimmed.IsFailure ? trimmed.Failure.ToString() : "");
        }
        else
        {
            var sized = await restarted.ExecuteAsync(id,
                new WorkflowCommand.SetPrintDimensions(PrintDimensions.FromMillimetres(50, 50, SizePreset.Custom)), "qa", CancellationToken.None);
            sized.IsSuccess.ShouldBeTrue(sized.IsFailure ? sized.Failure.ToString() : "");
            (await h.Repository.LoadAsync(id, CancellationToken.None)).Value!.Session.PrintPreparationPlan!.SourceRevisionId.ShouldBe(raster.Id);
        }
        adapter.Calls.ShouldBe(1);
        (await h.Repository.GetAutomationLockAsync(CancellationToken.None)).Value.IsHeld.ShouldBeFalse();
    }

    [Theory]
    [InlineData(FailureCode.PhotoshopLaunchFailed)]
    [InlineData(FailureCode.PhotoshopUnknownState)]
    [InlineData(FailureCode.PhotoshopDocumentIdentityUnconfirmed)]
    [InlineData(FailureCode.PhotoshopOpenInputFailed)]
    [InlineData(FailureCode.PhotoshopTargetLost)]
    [InlineData(FailureCode.PsdCompositeMissing)]
    [InlineData(FailureCode.PsdUnsupported)]
    [InlineData(FailureCode.PsdPreparationFailed)]
    [InlineData(FailureCode.Cancelled)]
    public async Task Failed_preparation_preserves_source_and_retry_uses_a_new_attempt(FailureCode code)
    {
        using SessionServiceHarness h = new();
        PsdProcessor adapter = new(h.FileWorkspace) { Failure = code };
        var service = h.CreateServiceWithPhotoshop(adapter);
        byte[] bytes = PsdInputPreparationTests.RgbCompositePsd();
        string source = h.Workspace.CreateSourceFile("synthetic.psd", bytes);
        var imported = await service.ImportAsync(WorkflowType.GeneratePrintTiff, source, null, "qa", CancellationToken.None);
        var id = imported.Value.Id;
        (await service.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.OriginalConfirmation), "qa", CancellationToken.None)).Failure.Code.ShouldBe(code);
        var stored = (await h.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        stored.Revisions.Count.ShouldBe(1);
        stored.Attempts.Single(a => a.Operation == OperationKind.PreparePsd).Status.ShouldBe(AttemptStatus.Failed);
        stored.ToSnapshot().CurrentStep!.Step.ShouldBe(StepKind.OriginalConfirmation);
        (await h.Repository.GetAutomationLockAsync(CancellationToken.None)).Value.IsHeld.ShouldBeFalse();
        adapter.Failure = null;
        var retried = await service.ExecuteAsync(id, new WorkflowCommand.Retry(StepKind.OriginalConfirmation), "qa", CancellationToken.None);
        retried.IsSuccess.ShouldBeTrue(retried.IsFailure ? retried.Failure.ToString() : "");
        (await service.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.OriginalConfirmation), "qa", CancellationToken.None))
            .IsSuccess.ShouldBeTrue();
        stored = (await h.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        var attempts = stored.Attempts.Where(a => a.Operation == OperationKind.PreparePsd).OrderBy(a => a.RetrySequence).ToArray();
        attempts.Length.ShouldBe(2);
        attempts[0].Status.ShouldBe(AttemptStatus.Failed);
        attempts[0].OutputRevisionId.ShouldBeNull();
        attempts[1].Status.ShouldBe(AttemptStatus.Succeeded);
        attempts[1].RetrySequence.ShouldBe(1);
        attempts[1].RetryOfAttemptId.ShouldBe(attempts[0].Id);
        adapter.Paths.Distinct().Count().ShouldBe(2);
        File.ReadAllBytes(source).ShouldBe(bytes);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("malformed")]
    [InlineData("dimensions")]
    public async Task Invalid_adapter_output_never_creates_a_revision(string fault)
    {
        using SessionServiceHarness h = new();
        PsdProcessor adapter = new(h.FileWorkspace) { OutputFault = fault };
        var service = h.CreateServiceWithPhotoshop(adapter);
        string source = h.Workspace.CreateSourceFile("synthetic.psd", PsdInputPreparationTests.RgbCompositePsd());
        var imported = await service.ImportAsync(WorkflowType.GeneratePrintTiff, source, null, "qa", CancellationToken.None);
        (await service.ExecuteAsync(imported.Value.Id, new WorkflowCommand.StartStep(StepKind.OriginalConfirmation), "qa", CancellationToken.None)).IsFailure.ShouldBeTrue();
        var stored = (await h.Repository.LoadAsync(imported.Value.Id, CancellationToken.None)).Value!;
        stored.Revisions.Count.ShouldBe(1);
        stored.Attempts.Single(a => a.Operation == OperationKind.PreparePsd).PsdInspection.ShouldNotBeNull();
    }

    private sealed class PsdProcessor(IWorkspace workspace) : IPhotoshopOutputProcessor
    {
        public string AdapterId => "test-psd-boundary-v1";
        public AdapterExecutionMode Mode { get; init; } = AdapterExecutionMode.Fake;
        public Action<PsdPreparationRequest>? BeforeReturn { get; set; }
        public FailureCode? Failure { get; set; }
        public string? OutputFault { get; init; }
        public int Calls { get; private set; }
        public List<string> Paths { get; } = [];
        public Task<OperationResult<AdapterOutput>> GenerateAsync(PhotoshopRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OperationResult<AdapterOutput>> PreparePsdAsync(PsdPreparationRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            string path = workspace.ResolveAbsolute(request.ExpectedOutput); Paths.Add(path);
            BeforeReturn?.Invoke(request);
            if (request.Stop.RequestedMode is not null)
                return Task.FromResult(OperationResult.Fail<AdapterOutput>(FailureCode.Cancelled, "Operator stopped this attempt."));
            if (Failure is { } failure)
            {
                File.WriteAllText(path, "retained failed attempt output");
                return Task.FromResult(OperationResult.Fail<AdapterOutput>(failure, "Scripted boundary failure."));
            }
            if (OutputFault != "missing") File.WriteAllBytes(path, OutputFault == "malformed" ? [1, 2, 3] :
                SyntheticImages.Png(OutputFault == "dimensions" ? 5 : 4, 3));
            return Task.FromResult(OperationResult.Ok(new AdapterOutput(request.ExpectedOutput, TimeSpan.Zero, "test boundary")
            {
                PsdInspection = new PsdInspection(4, 3, "RGB", 8, true, true,
                    [new("Red", "COMPONENT"), new("Green", "COMPONENT"), new("Blue", "COMPONENT")], "test-double"),
            }));
        }
    }
}
